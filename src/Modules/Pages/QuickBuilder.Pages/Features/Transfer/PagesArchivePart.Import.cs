using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Pages.Features.Transfer;

/// <summary>
/// Checking and applying the Pages part (020 FR-019, FR-021, FR-021a, FR-029; research ST-03,
/// ST-11; 018 FR-040, 024 FR-036).
/// </summary>
/// <remarks>
/// Rows are inserted as the archive holds them, then the rows the module derives from documents
/// are rebuilt with the same extractors a save and a publish use: component, media and menu
/// usage, collection uses, live dependencies, menu targets and the sitemap generation. Publish
/// events go to the outbox in the import's transaction, so the form registry and the render
/// cache follow after the commit, as after any publish.
/// </remarks>
internal sealed partial class PagesArchivePart
{
    private const int Batch = 500;

    public async Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Session is { } session)
        {
            database.JoinTransfer(session.Connection, session.Transaction);
        }

        long pages = 0, components = 0, menus = 0, redirects = 0;
        await foreach (var p in context.Archive.ReadLinesAsync<PageRow>(PartName, "tree", cancellationToken).ConfigureAwait(false))
        {
            context.Ids.Declare(ArchiveKinds.Page, p.Id, p.Title);
            pages++;
        }

        await foreach (var c in context.Archive.ReadLinesAsync<ComponentRow>(PartName, "components", cancellationToken).ConfigureAwait(false))
        {
            context.Ids.Declare(ArchiveKinds.Component, c.Id, c.Name);
            components++;
        }

        await foreach (var m in context.Archive.ReadLinesAsync<MenuRow>(PartName, "menus", cancellationToken).ConfigureAwait(false))
        {
            context.Ids.Declare(ArchiveKinds.Menu, m.Id, m.Name);
            menus++;
        }

        await foreach (var r in context.Archive.ReadLinesAsync<RedirectRow>(PartName, "redirects", cancellationToken).ConfigureAwait(false))
        {
            context.Ids.Declare(ArchiveKinds.Redirect, r.Id, r.SourcePath);
            redirects++;
        }

        // FR-029: a schedule whose time has passed is dropped, and said so before confirming.
        await foreach (var s in context.Archive.ReadLinesAsync<ScheduleRow>(PartName, "schedules", cancellationToken).ConfigureAwait(false))
        {
            if (s.DueAtUtc <= context.NowUtc)
            {
                context.Report.PastSchedule(new TransferSubject(s.SubjectKind, s.SubjectId, s.CapturedTitle ?? s.SubjectId.ToString()), s.DueAtUtc, s.Action);
            }
        }

        context.Report.Count(ArchiveKinds.Page, pages);
        context.Report.Count(ArchiveKinds.Component, components);
        context.Report.Count(ArchiveKinds.Menu, menus);
        context.Report.Count(ArchiveKinds.Redirect, redirects);

        if (context.Mode == ImportMode.Merge)
        {
            await MatchAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Mode == ImportMode.Restore)
        {
            // FR-027b: what a restore removes.
            void Removed(string kind, Guid id, string label)
            {
                if (!context.Ids.IsDeclared(kind, id))
                {
                    context.Report.Removal(new TransferSubject(kind, id, label));
                    context.Ids.Note($"remove:{kind}:{id:N}");
                }
            }

            foreach (var p in await database.Pages.AsNoTracking().Select(p => new { p.Id, p.Path }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                Removed(ArchiveKinds.Page, p.Id, p.Path);
            }

            foreach (var c in await database.Components.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                Removed(ArchiveKinds.Component, c.Id, c.Name);
            }

            foreach (var m in await database.Menus.AsNoTracking().Select(m => new { m.Id, m.Name }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                Removed(ArchiveKinds.Menu, m.Id, m.Name);
            }

            foreach (var r in await database.Redirects.AsNoTracking().Select(r => new { r.Id, r.SourcePath }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                Removed(ArchiveKinds.Redirect, r.Id, r.SourcePath);
            }
        }
    }

    public async Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        if (context.Mode == ImportMode.Merge)
        {
            await MergeAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var site = context.TargetSiteId;
        if (context.Mode == ImportMode.Restore)
        {
            await DeleteSiteRowsAsync(context, cancellationToken).ConfigureAwait(false);
        }
        var ids = context.Ids;
        var authors = context.Authors;
        var fresh = context.StartFresh;
        var clock = services.GetRequiredService<TimeProvider>();

        // Versions first, in memory: start fresh takes a live version as the draft where the
        // archive has no newer draft, and the live state points at them.
        var versions = await Lines<VersionRow>(context.Archive, "versions", cancellationToken).ConfigureAwait(false);
        var publications = (await Lines<PublicationRow>(context.Archive, "publications", cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => (p.SubjectKind, p.SubjectId));
        VersionRow? Live(string kind, Guid id) =>
            publications.TryGetValue((kind, id), out var p) && p.LiveNumber is { } n
                ? versions.FirstOrDefault(v => v.SubjectKind == kind && v.SubjectId == id && v.Number == n)
                : null;

        // The page tree, through the tree's own address rules.
        var pageRows = await Lines<PageRow>(context.Archive, "tree", cancellationToken).ConfigureAwait(false);
        var tree = PageTree.Import(site, [.. pageRows.Select(p => new ImportedPage(
            ids.Remap(ArchiveKinds.Page, p.Id),
            p.ParentId is { } parent ? ids.Remap(ArchiveKinds.Page, parent) : null,
            p.Position,
            p.Title,
            p.Slug,
            p.IsHome,
            p.TemplateCollectionId is { } collection ? ids.Remap(ArchiveKinds.Collection, collection) : null,
            fresh || p.CopiedFromId is not { } source ? null : new CopyOrigin(ids.Remap(ArchiveKinds.Page, source), p.CopiedFromSavedAtUtc!.Value, authors.Map(p.CopiedBy!.Value), p.CopiedAtUtc!.Value),
            p.CreatedAtUtc,
            authors.Map(p.CreatedBy),
            p.UpdatedAtUtc,
            authors.Map(p.UpdatedBy)))], context.NowUtc);
        if (tree.IsFailure)
        {
            throw new ArchiveFormatException($"The page tree cannot be imported: {tree.Error!.Message}");
        }

        database.PageTrees.Add(tree.Value);

        var drafts = new List<(string Kind, Guid Id, string Text, Guid? Template)>();
        var pageDrafts = (await Lines<DraftRow>(context.Archive, "page-drafts", cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.SubjectId);
        foreach (var p in pageRows)
        {
            var id = ids.Remap(ArchiveKinds.Page, p.Id);
            var draft = PageDraft.CreateEmpty(site, id, authors.Map(p.CreatedBy), clock);
            if (Source(fresh, pageDrafts.GetValueOrDefault(p.Id), Live(SubjectKinds.Page, p.Id)) is { } source)
            {
                draft.ImportDocument(ids.RemapDocument(source.Document), source.SchemaVersion, authors.Map(source.By), source.At);
            }

            database.PageDrafts.Add(draft);
            drafts.Add((SubjectKinds.Page, id, draft.Document, tree.Value.Pages.Single(x => x.Id == id).TemplateCollectionId));
        }

        var componentRows = await Lines<ComponentRow>(context.Archive, "components", cancellationToken).ConfigureAwait(false);
        foreach (var c in componentRows)
        {
            var id = ids.Remap(ArchiveKinds.Component, c.Id);
            database.Components.Add(Component.FromArchive(
                id, site, c.Name,
                fresh || c.CopiedFromId is not { } source ? null : new CopyOrigin(ids.Remap(ArchiveKinds.Component, source), c.CopiedFromSavedAtUtc!.Value, authors.Map(c.CopiedBy!.Value), c.CopiedAtUtc!.Value),
                c.CreatedAtUtc, authors.Map(c.CreatedBy), c.UpdatedAtUtc, authors.Map(c.UpdatedBy)));
            var draft = ComponentDraft.CreateEmpty(site, id, authors.Map(c.CreatedBy), clock);
            if (Source(fresh, c.Draft, Live(SubjectKinds.Component, c.Id)) is { } content)
            {
                draft.ImportDocument(ids.RemapDocument(content.Document), content.SchemaVersion, authors.Map(content.By), content.At);
            }

            database.ComponentDrafts.Add(draft);
            drafts.Add((SubjectKinds.Component, id, draft.Document, null));
        }

        var menuRows = await Lines<MenuRow>(context.Archive, "menus", cancellationToken).ConfigureAwait(false);
        var menuDrafts = new List<(Guid Id, string Text)>();
        foreach (var m in menuRows)
        {
            var id = ids.Remap(ArchiveKinds.Menu, m.Id);
            database.Menus.Add(Menu.FromArchive(id, site, m.Name, m.CreatedAtUtc, authors.Map(m.CreatedBy), m.UpdatedAtUtc, authors.Map(m.UpdatedBy)));
            var draft = Source(fresh, m.Draft, Live(SubjectKinds.Menu, m.Id)) is { } content
                ? MenuDraft.FromArchive(id, site, content.SchemaVersion, ids.RemapDocument(content.Document), content.At, authors.Map(content.By))
                : MenuDraft.CreateEmpty(site, id, authors.Map(m.CreatedBy), clock);
            database.MenuDrafts.Add(draft);
            menuDrafts.Add((id, draft.Document));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported(ArchiveKinds.Page, pageRows.Count);
        context.Report.Imported(ArchiveKinds.Component, componentRows.Count);
        context.Report.Imported(ArchiveKinds.Menu, menuRows.Count);
        context.Progress.Report(0.3);

        var live = new List<(string Kind, Guid Id, string Document, int Number)>();
        if (!fresh)
        {
            live = await ApplyPublishingAsync(context, versions, publications, (_, _) => true, cancellationToken).ConfigureAwait(false);
        }

        context.Progress.Report(0.6);
        await ApplyRedirectsAsync(context, cancellationToken).ConfigureAwait(false);

        // Derived rows, as a save and a publish make them (research ST-03).
        var uses = services.GetRequiredService<CollectionUseRows>();
        var menus = menuRows.Select(m => ids.Remap(ArchiveKinds.Menu, m.Id)).ToHashSet();
        foreach (var (kind, id, text, template) in drafts)
        {
            if (kind == SubjectKinds.Page)
            {
                database.ComponentUsages.AddRange(DependencyExtractor.Components(text).Distinct().Select(c => new ComponentUsage(site, id, c)));
            }

            database.DraftMediaReferences.AddRange(DependencyExtractor.Media(text).Distinct().Select(m => new DraftMediaReference(site, kind, id, m)));
            database.MenuUsages.AddRange(DependencyExtractor.Menus(text).Distinct().Where(menus.Contains).Select(m => new MenuUsage(site, kind, id, m)));
            await uses.StageAsync(site, kind, id, CollectionUsageStates.Draft, text, template, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (id, text) in menuDrafts)
        {
            database.MenuTargets.AddRange(MenuTargets.Distinct(MenuTargets.Extract(text)).Select(t => new MenuTarget(site, id, t.Kind, t.Id)));
        }

        var publishing = services.GetRequiredService<PublishingWrites>();
        foreach (var (kind, id, document, number) in live)
        {
            var template = kind == SubjectKinds.Page ? tree.Value.Pages.Single(p => p.Id == id).TemplateCollectionId : null;
            var path = kind == SubjectKinds.Page ? tree.Value.Pages.Single(p => p.Id == id).Path : null;
            await publishing.ReplaceDependenciesAsync(site, new PublishingWrites.Subject(kind, id, site, string.Empty, path, template), document, cancellationToken).ConfigureAwait(false);
            if (kind != SubjectKinds.Menu)
            {
                await uses.StageAsync(site, kind, id, CollectionUsageStates.Live, document, template, cancellationToken).ConfigureAwait(false);
            }

            // The form registry and the render cache follow a publish (018 FR-040; 012 PR-05).
            if (kind == SubjectKinds.Page)
            {
                PagesOutbox.Add(database, new PagePublished(Guid.CreateVersion7(), context.NowUtc, site, id, number, "import", path ?? string.Empty, context.ActorUserId));
            }
            else if (kind == SubjectKinds.Component)
            {
                PagesOutbox.Add(database, new ComponentPublished(Guid.CreateVersion7(), context.NowUtc, site, id, number, "import", context.ActorUserId));
            }
        }

        if (live.Any(l => l.Kind == SubjectKinds.Page))
        {
            await services.GetRequiredService<SitemapGeneration>().BumpAsync(site, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
    }

    /// <summary>
    /// Every document against the rules a member's save must pass, and the tree against the
    /// tree's address rules (FR-014, research ST-20). An invalid entry refuses the archive.
    /// </summary>
    private async Task ValidateAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        var pages = services.GetRequiredService<QuickBuilder.Pages.Contracts.IPageDocumentValidator>();
        var menus = services.GetRequiredService<QuickBuilder.Pages.Infrastructure.Schema.MenuDocumentValidator>();
        var titles = new Dictionary<Guid, string>();

        void Check(string kind, Guid id, string label, JsonElement document, bool menu, bool component)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
            var result = menu
                ? menus.Validate(bytes)
                : pages.Validate(bytes, component ? QuickBuilder.Pages.Contracts.DocumentKind.Component : QuickBuilder.Pages.Contracts.DocumentKind.Page);
            if (!result.IsValid)
            {
                context.Report.Error(
                    result.Code == "pages.unsupported_schema_version" ? "transfer.document_too_new" : "transfer.document_invalid",
                    new TransferSubject(kind, id, label),
                    $"{result.Message} ({result.Rule} at {result.Location})");
            }
        }

        var rows = await Lines<PageRow>(context.Archive, "tree", cancellationToken).ConfigureAwait(false);
        foreach (var p in rows)
        {
            titles[p.Id] = p.Title;
        }

        var tree = PageTree.Import(Guid.Empty, [.. rows.Select(p => new ImportedPage(
            p.Id, p.ParentId, p.Position, p.Title, p.Slug, p.IsHome, p.TemplateCollectionId, null, p.CreatedAtUtc, p.CreatedBy, p.UpdatedAtUtc, p.UpdatedBy))], context.NowUtc);
        if (tree.IsFailure)
        {
            context.Report.Error("transfer.document_invalid", null, $"The page tree cannot be imported: {tree.Error!.Message}");
        }

        await foreach (var d in context.Archive.ReadLinesAsync<DraftRow>(PartName, "page-drafts", cancellationToken).ConfigureAwait(false))
        {
            Check(ArchiveKinds.Page, d.SubjectId, titles.GetValueOrDefault(d.SubjectId, d.SubjectId.ToString()), d.Document, menu: false, component: false);
        }

        await foreach (var c in context.Archive.ReadLinesAsync<ComponentRow>(PartName, "components", cancellationToken).ConfigureAwait(false))
        {
            if (c.Draft is { } draft)
            {
                Check(ArchiveKinds.Component, c.Id, c.Name, draft.Document, menu: false, component: true);
            }
        }

        await foreach (var m in context.Archive.ReadLinesAsync<MenuRow>(PartName, "menus", cancellationToken).ConfigureAwait(false))
        {
            if (m.Draft is { } draft)
            {
                Check(ArchiveKinds.Menu, m.Id, m.Name, draft.Document, menu: true, component: false);
            }
        }

        await foreach (var v in context.Archive.ReadLinesAsync<VersionRow>(PartName, "versions", cancellationToken).ConfigureAwait(false))
        {
            Check(v.SubjectKind, v.SubjectId, v.Title ?? titles.GetValueOrDefault(v.SubjectId, v.SubjectId.ToString()), v.Document,
                menu: v.SubjectKind == SubjectKinds.Menu, component: v.SubjectKind == SubjectKinds.Component);
        }
    }

    /// <summary>
    /// Research ST-12: a restore removes the site's Pages rows, derived ones included, before the
    /// archive's come back. The publish, redirect and menu change logs are kept: they record what
    /// happened on this installation.
    /// </summary>
    private async Task DeleteSiteRowsAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        var removed = await database.Pages.Select(p => new { p.Id, p.Path }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var components = await database.Components.Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Removed(ArchiveKinds.Page, removed.Count);

        await database.VersionMediaReferences.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.DraftMediaReferences.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.LiveDependencies.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.ComponentUsages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.CollectionUsages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.MenuTargets.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.MenuUsages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.PublishSchedules.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.PublishedVersions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.Publications.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.PageDrafts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.ComponentDrafts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.Components.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.MenuDrafts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.Menus.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.RedirectUsages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.Redirects.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.Pages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.PageTrees.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        // Who holds forms, editing sessions and caches of removed documents learns as after a delete.
        foreach (var page in removed.Where(p => context.Ids.Outcome(ArchiveKinds.Page, p.Id) is null))
        {
            PagesOutbox.Add(database, new PageDeleted(Guid.CreateVersion7(), context.NowUtc, context.TargetSiteId, page.Id, page.Path, context.ActorUserId));
        }

        foreach (var component in components.Where(c => context.Ids.Outcome(ArchiveKinds.Component, c) is null))
        {
            PagesOutbox.Add(database, new ComponentDeleted(Guid.CreateVersion7(), context.NowUtc, context.TargetSiteId, component, context.ActorUserId));
        }
    }

    /// <summary>
    /// Retained versions, publications and future schedules of a full import, of the subjects
    /// <paramref name="includes"/> takes by subject kind and archive id (a merge: the added ones).
    /// </summary>
    private async Task<List<(string Kind, Guid Id, string Document, int Number)>> ApplyPublishingAsync(
        PartApplyContext context,
        List<VersionRow> versions,
        Dictionary<(string, Guid), PublicationRow> publications,
        Func<string, Guid, bool> includes,
        CancellationToken cancellationToken)
    {
        var site = context.TargetSiteId;
        var ids = context.Ids;
        var authors = context.Authors;
        var versionIds = new Dictionary<(string, Guid, int), (Guid Id, string Document)>();

        foreach (var v in versions.Where(v => includes(v.SubjectKind, v.SubjectId)))
        {
            var subject = Subject(ids, v.SubjectKind, v.SubjectId);
            var document = ids.RemapDocument(v.Document);
            var version = PublishedVersion.FromArchive(
                Guid.CreateVersion7(), site, v.SubjectKind, subject, v.Number, v.SchemaVersion, document, v.Title, v.Kind, v.RestoredFromNumber,
                authors.Map(v.PublishedBy), v.PublishedAtUtc);
            database.PublishedVersions.Add(version);
            database.VersionMediaReferences.AddRange(DependencyExtractor.Media(document).Distinct().Select(m => new VersionMediaReference(site, version.Id, m)));
            versionIds[(v.SubjectKind, v.SubjectId, v.Number)] = (version.Id, document);
        }

        var live = new List<(string Kind, Guid Id, string Document, int Number)>();
        foreach (var p in publications.Values.Where(p => includes(p.SubjectKind, p.SubjectId)))
        {
            var subject = Subject(ids, p.SubjectKind, p.SubjectId);
            (Guid Id, string Document)? current = p.LiveNumber is { } n && versionIds.TryGetValue((p.SubjectKind, p.SubjectId, n), out var found) ? found : null;
            database.Publications.Add(Publication.FromArchive(
                site, p.SubjectKind, subject, current?.Id, p.LiveNumber, p.NextNumber, p.LiveSinceUtc, authors.Map(p.LiveBy),
                current is { } c && p.SubjectKind == SubjectKinds.Page ? DependencyExtractor.PageParamNames(c.Document) : []));
            if (current is { } liveVersion)
            {
                live.Add((p.SubjectKind, subject, liveVersion.Document, p.LiveNumber!.Value));
            }
        }

        foreach (var s in await Lines<ScheduleRow>(context.Archive, "schedules", cancellationToken).ConfigureAwait(false))
        {
            var label = new TransferSubject(s.SubjectKind, s.SubjectId, s.CapturedTitle ?? s.SubjectId.ToString());
            if (s.DueAtUtc <= context.NowUtc)
            {
                context.Report.DroppedSchedule(label, s.DueAtUtc);
                continue;
            }

            if (!includes(s.SubjectKind, s.SubjectId))
            {
                continue;
            }

            database.PublishSchedules.Add(PublishSchedule.FromArchive(
                Guid.CreateVersion7(), site, s.SubjectKind, Subject(ids, s.SubjectKind, s.SubjectId), s.Action, s.DueAtUtc, s.Revision,
                s.CapturedDocument is { } captured ? ids.RemapDocument(captured) : null, s.CapturedSchemaVersion, s.CapturedTitle,
                s.RedirectToPageId is { } page ? ids.Remap(ArchiveKinds.Page, page) : null, authors.Map(s.CreatedBy), s.CreatedAtUtc));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported("versions", versions.Count);
        return live;
    }

    private async Task ApplyRedirectsAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        var ids = context.Ids;
        long count = 0;
        await foreach (var r in context.Archive.ReadLinesAsync<RedirectRow>(PartName, "redirects", cancellationToken).ConfigureAwait(false))
        {
            database.Redirects.Add(Redirect.FromArchive(
                ids.Remap(ArchiveKinds.Redirect, r.Id), context.TargetSiteId, r.MatchKind, r.SourcePath, r.SourceQuery, r.StatusCode, r.TargetKind,
                r.TargetPageId is { } page ? ids.Remap(ArchiveKinds.Page, page) : null,
                r.TargetCollectionId is { } collection ? ids.Remap(ArchiveKinds.Collection, collection) : null,
                r.TargetItemId is { } item ? ids.Remap(ArchiveKinds.Item, item) : null,
                r.TargetPath, r.TargetUrl, r.Enabled, r.Note, r.Origin, r.CreatedAtUtc,
                context.Authors.Map(r.CreatedBy), r.UpdatedAtUtc, context.Authors.Map(r.UpdatedBy)));
            if (++count % Batch == 0)
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported(ArchiveKinds.Redirect, count);
    }

    /// <summary>
    /// What a draft is made of: the archive's draft, or, starting fresh, its live version where
    /// the archive's draft is not newer (FR-021a, research ST-11).
    /// </summary>
    private static DraftSource? Source(bool fresh, DraftRow? draft, VersionRow? live)
    {
        if (fresh && live is not null && (draft is null || draft.UpdatedAtUtc <= live.PublishedAtUtc))
        {
            return new DraftSource(live.Document, live.SchemaVersion, live.PublishedBy, live.PublishedAtUtc);
        }

        return draft is null ? null : new DraftSource(draft.Document, draft.SchemaVersion, draft.UpdatedBy, draft.UpdatedAtUtc);
    }

    private static Guid Subject(IIdMap ids, string kind, Guid id) => ids.Remap(kind switch
    {
        SubjectKinds.Page => ArchiveKinds.Page,
        SubjectKinds.Component => ArchiveKinds.Component,
        _ => ArchiveKinds.Menu,
    }, id);

    private static async Task<List<T>> Lines<T>(IArchiveReader archive, string entry, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await foreach (var row in archive.ReadLinesAsync<T>(PartName, entry, cancellationToken).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows;
    }

    private sealed record DraftSource(JsonElement Document, int SchemaVersion, Guid By, DateTimeOffset At);
}
