using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Pages.Features.Transfer;

/// <summary>
/// Merging the Pages part into an existing site (020 FR-022 … FR-027, research ST-08, ST-10).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Pages match by address, components and menus by name, redirects by source; from the same
/// site, by identity first (FR-022a).</item>
/// <item>Replace keeps the target's id, place and children. Its draft takes the archive's, and its
/// live state the archive's, published as its next version, so its history stays (FR-024).</item>
/// <item>Skip keeps the target, and references point to it (FR-025).</item>
/// <item>Rename adds the archive's document beside the target's under the owning feature's naming
/// rules (FR-026). Redirects cannot be renamed and are skipped.</item>
/// </list>
/// Nothing of the target without a counterpart in the archive changes (FR-027).
/// </remarks>
internal sealed partial class PagesArchivePart
{
    /// <summary>FR-022, FR-022a: matches the archive's documents to the target's.</summary>
    private async Task MatchAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        var same = context.SameSiteAsTarget;
        var rows = await Lines<PageRow>(context.Archive, "tree", cancellationToken).ConfigureAwait(false);
        var archived = PageTree.Import(Guid.Empty, [.. rows.Select(p => new ImportedPage(
            p.Id, p.ParentId, p.Position, p.Title, p.Slug, p.IsHome, p.TemplateCollectionId, null, p.CreatedAtUtc, p.CreatedBy, p.UpdatedAtUtc, p.UpdatedBy))], context.NowUtc);
        if (archived.IsFailure)
        {
            // Refused by the validation already.
            return;
        }

        var pages = await database.Pages.AsNoTracking().Select(p => new { p.Id, p.Path, p.Title, p.Version }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var pagesById = pages.ToDictionary(p => p.Id);
        var pagesByPath = pages.ToDictionary(p => p.Path, StringComparer.Ordinal);
        foreach (var page in archived.Value.Pages)
        {
            var subject = new TransferSubject(ArchiveKinds.Page, page.Id, page.Title);
            var target = same && pagesById.TryGetValue(page.Id, out var byId) ? byId : null;
            var by = target is null ? MatchedBy.Address : MatchedBy.Identity;
            target ??= pagesByPath.GetValueOrDefault(page.Path);
            if (target is null)
            {
                continue;
            }

            context.Ids.Match(ArchiveKinds.Page, page.Id, target.Id, by);
            var clash = by == MatchedBy.Identity && pagesByPath.TryGetValue(page.Path, out var other) && other.Id != target.Id
                ? new TransferSubject(ArchiveKinds.Page, other.Id, other.Title)
                : null;
            context.Report.Conflict(subject, target.Id, by, clash);
            context.Ids.Note($"page:{target.Id:N}:{target.Version}:{target.Path}");
        }

        var components = await database.Components.AsNoTracking().Select(c => new { c.Id, c.Name, c.UpdatedAtUtc }).ToListAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var c in context.Archive.ReadLinesAsync<ComponentRow>(PartName, "components", cancellationToken).ConfigureAwait(false))
        {
            var byId = same ? components.FirstOrDefault(t => t.Id == c.Id) : null;
            var byName = components.FirstOrDefault(t => string.Equals(t.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            if ((byId ?? byName) is not { } target)
            {
                continue;
            }

            var by = byId is null ? MatchedBy.Name : MatchedBy.Identity;
            context.Ids.Match(ArchiveKinds.Component, c.Id, target.Id, by);
            context.Report.Conflict(
                new TransferSubject(ArchiveKinds.Component, c.Id, c.Name), target.Id, by,
                byId is not null && byName is not null && byName.Id != byId.Id ? new TransferSubject(ArchiveKinds.Component, byName.Id, byName.Name) : null);
            context.Ids.Note($"component:{target.Id:N}:{target.UpdatedAtUtc:O}");
        }

        var menus = await database.Menus.AsNoTracking().Select(m => new { m.Id, m.Name, m.UpdatedAtUtc }).ToListAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var m in context.Archive.ReadLinesAsync<MenuRow>(PartName, "menus", cancellationToken).ConfigureAwait(false))
        {
            var byId = same ? menus.FirstOrDefault(t => t.Id == m.Id) : null;
            var byName = menus.FirstOrDefault(t => string.Equals(t.Name, m.Name, StringComparison.OrdinalIgnoreCase));
            if ((byId ?? byName) is not { } target)
            {
                continue;
            }

            var by = byId is null ? MatchedBy.Name : MatchedBy.Identity;
            context.Ids.Match(ArchiveKinds.Menu, m.Id, target.Id, by);
            context.Report.Conflict(
                new TransferSubject(ArchiveKinds.Menu, m.Id, m.Name), target.Id, by,
                byId is not null && byName is not null && byName.Id != byId.Id ? new TransferSubject(ArchiveKinds.Menu, byName.Id, byName.Name) : null);
            context.Ids.Note($"menu:{target.Id:N}:{target.UpdatedAtUtc:O}");
        }

        var redirects = await database.Redirects.AsNoTracking()
            .Select(r => new { r.Id, r.MatchKind, r.SourcePath, r.SourceQuery, r.UpdatedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        await foreach (var r in context.Archive.ReadLinesAsync<RedirectRow>(PartName, "redirects", cancellationToken).ConfigureAwait(false))
        {
            var bySource = redirects.FirstOrDefault(t => t.MatchKind == r.MatchKind
                && string.Equals(t.SourcePath, r.SourcePath, StringComparison.Ordinal)
                && string.Equals(t.SourceQuery, r.SourceQuery, StringComparison.Ordinal));
            var byId = same ? redirects.FirstOrDefault(t => t.Id == r.Id) : null;

            // A source is unique by rule (017): a redirect whose source another target redirect
            // holds can only meet that one.
            var target = bySource ?? byId;
            if (target is null)
            {
                continue;
            }

            var by = bySource is null ? MatchedBy.Identity : MatchedBy.Source;
            context.Ids.Match(ArchiveKinds.Redirect, r.Id, target.Id, by);
            context.Report.Conflict(new TransferSubject(ArchiveKinds.Redirect, r.Id, r.SourcePath), target.Id, by);
            context.Ids.Note($"redirect:{target.Id:N}:{target.UpdatedAtUtc:O}");
        }
    }

    /// <summary>Applies a merge per research ST-10.</summary>
    private async Task MergeAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        var site = context.TargetSiteId;
        var ids = context.Ids;
        var authors = context.Authors;
        var clock = services.GetRequiredService<TimeProvider>();
        var actor = context.ActorUserId ?? Guid.Empty;

        bool Adds(string kind, Guid archiveId) => ids.Outcome(kind, archiveId) is IdOutcome.New or IdOutcome.Renamed;
        bool Replaces(string kind, Guid archiveId) => ids.Outcome(kind, archiveId) == IdOutcome.Replaced;
        void Kept(string kind, Guid archiveId, string label)
        {
            if (ids.Outcome(kind, archiveId) == IdOutcome.Skipped)
            {
                context.Report.Resolved(new TransferSubject(kind, archiveId, label), ids.Resolve(kind, archiveId)!.Value, IdOutcome.Skipped);
            }
        }

        var versions = await Lines<VersionRow>(context.Archive, "versions", cancellationToken).ConfigureAwait(false);
        var publications = (await Lines<PublicationRow>(context.Archive, "publications", cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => (p.SubjectKind, p.SubjectId));
        VersionRow? Live(string kind, Guid id) =>
            publications.TryGetValue((kind, id), out var p) && p.LiveNumber is { } n
                ? versions.FirstOrDefault(v => v.SubjectKind == kind && v.SubjectId == id && v.Number == n)
                : null;

        // The tree: added pages beside the target's, through the tree's address rules.
        var pageRows = await Lines<PageRow>(context.Archive, "tree", cancellationToken).ConfigureAwait(false);
        var tree = await database.PageTrees.Include(t => t.Pages).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (tree is null)
        {
            tree = PageTree.Start(site, clock);
            database.PageTrees.Add(tree);
        }

        var addedPages = pageRows.Where(p => Adds(ArchiveKinds.Page, p.Id)).ToList();
        var merged = tree.Merge([.. addedPages.Select(p => new ImportedPage(
            ids.Remap(ArchiveKinds.Page, p.Id),
            p.ParentId is { } parent ? ids.Remap(ArchiveKinds.Page, parent) : null,
            p.Position,
            p.Title,
            p.Slug,
            p.IsHome,
            p.TemplateCollectionId is { } collection ? ids.Remap(ArchiveKinds.Collection, collection) : null,
            p.CopiedFromId is not { } source ? null : new CopyOrigin(ids.Remap(ArchiveKinds.Page, source), p.CopiedFromSavedAtUtc!.Value, authors.Map(p.CopiedBy!.Value), p.CopiedAtUtc!.Value),
            p.CreatedAtUtc,
            authors.Map(p.CreatedBy),
            p.UpdatedAtUtc,
            authors.Map(p.UpdatedBy)))], ConflictRules.NextSlug, context.NowUtc);
        if (merged.IsFailure)
        {
            throw new ArchiveFormatException($"The archive's pages cannot be added to this site: {merged.Error!.Message}");
        }

        var newSlugs = merged.Value.ToDictionary(r => r.Id, r => r.Slug);
        foreach (var p in pageRows)
        {
            var id = ids.Remap(ArchiveKinds.Page, p.Id);
            if (ids.Outcome(ArchiveKinds.Page, p.Id) == IdOutcome.Renamed || newSlugs.ContainsKey(id))
            {
                context.Report.Resolved(new TransferSubject(ArchiveKinds.Page, p.Id, p.Title), id, IdOutcome.Renamed, tree.Pages.Single(x => x.Id == id).Path);
            }

            Kept(ArchiveKinds.Page, p.Id, p.Title);
        }

        // Drafts: new ones for added documents, the archive's content for replaced ones.
        var drafts = new List<(string Kind, Guid Id, string Text, Guid? Template)>();
        var pageDrafts = (await Lines<DraftRow>(context.Archive, "page-drafts", cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.SubjectId);
        var replacedPages = pageRows.Where(p => Replaces(ArchiveKinds.Page, p.Id)).Select(p => ids.Resolve(ArchiveKinds.Page, p.Id)!.Value).ToList();
        var replacedPageDrafts = await database.PageDrafts
            .Where(d => replacedPages.Contains(d.PageId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var p in pageRows)
        {
            var id = ids.Remap(ArchiveKinds.Page, p.Id);
            var template = tree.Pages.Single(x => x.Id == id).TemplateCollectionId;
            var archiveDraft = pageDrafts.GetValueOrDefault(p.Id);
            if (Adds(ArchiveKinds.Page, p.Id))
            {
                var draft = PageDraft.CreateEmpty(site, id, authors.Map(p.CreatedBy), clock);
                if (archiveDraft is not null)
                {
                    draft.ImportDocument(ids.RemapDocument(archiveDraft.Document), archiveDraft.SchemaVersion, authors.Map(archiveDraft.UpdatedBy), archiveDraft.UpdatedAtUtc);
                }

                database.PageDrafts.Add(draft);
                drafts.Add((SubjectKinds.Page, id, draft.Document, template));
            }
            else if (Replaces(ArchiveKinds.Page, p.Id) && archiveDraft is not null)
            {
                var draft = replacedPageDrafts.Single(d => d.PageId == id);
                draft.Replace(ids.RemapDocument(archiveDraft.Document), archiveDraft.SchemaVersion, actor, clock);
                drafts.Add((SubjectKinds.Page, id, draft.Document, template));
                context.Report.Resolved(new TransferSubject(ArchiveKinds.Page, p.Id, p.Title), id, IdOutcome.Replaced);
            }
        }

        var componentRows = await Lines<ComponentRow>(context.Archive, "components", cancellationToken).ConfigureAwait(false);
        var componentNames = (await database.Components.Select(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in componentRows)
        {
            var id = ids.Remap(ArchiveKinds.Component, c.Id);
            if (Adds(ArchiveKinds.Component, c.Id))
            {
                var name = componentNames.Contains(c.Name) ? ConflictRules.ImportedName(c.Name, componentNames.Contains) : c.Name;
                componentNames.Add(name);
                if (ids.Outcome(ArchiveKinds.Component, c.Id) == IdOutcome.Renamed)
                {
                    context.Report.Resolved(new TransferSubject(ArchiveKinds.Component, c.Id, c.Name), id, IdOutcome.Renamed, name);
                }

                database.Components.Add(Component.FromArchive(
                    id, site, name,
                    c.CopiedFromId is not { } source ? null : new CopyOrigin(ids.Remap(ArchiveKinds.Component, source), c.CopiedFromSavedAtUtc!.Value, authors.Map(c.CopiedBy!.Value), c.CopiedAtUtc!.Value),
                    c.CreatedAtUtc, authors.Map(c.CreatedBy), c.UpdatedAtUtc, authors.Map(c.UpdatedBy)));
                var draft = ComponentDraft.CreateEmpty(site, id, authors.Map(c.CreatedBy), clock);
                if (c.Draft is { } content)
                {
                    draft.ImportDocument(ids.RemapDocument(content.Document), content.SchemaVersion, authors.Map(content.UpdatedBy), content.UpdatedAtUtc);
                }

                database.ComponentDrafts.Add(draft);
                drafts.Add((SubjectKinds.Component, id, draft.Document, null));
            }
            else if (Replaces(ArchiveKinds.Component, c.Id))
            {
                if (c.Draft is { } content)
                {
                    var draft = await database.ComponentDrafts.SingleAsync(d => d.ComponentId == id, cancellationToken).ConfigureAwait(false);
                    draft.Replace(ids.RemapDocument(content.Document), content.SchemaVersion, actor, clock);
                    drafts.Add((SubjectKinds.Component, id, draft.Document, null));
                }

                context.Report.Resolved(new TransferSubject(ArchiveKinds.Component, c.Id, c.Name), id, IdOutcome.Replaced);
            }
            else
            {
                Kept(ArchiveKinds.Component, c.Id, c.Name);
            }
        }

        var menuRows = await Lines<MenuRow>(context.Archive, "menus", cancellationToken).ConfigureAwait(false);
        var menuNames = (await database.Menus.Select(m => m.Name).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menuDrafts = new List<(Guid Id, string Text)>();
        foreach (var m in menuRows)
        {
            var id = ids.Remap(ArchiveKinds.Menu, m.Id);
            if (Adds(ArchiveKinds.Menu, m.Id))
            {
                var name = menuNames.Contains(m.Name) ? ConflictRules.ImportedName(m.Name, menuNames.Contains) : m.Name;
                menuNames.Add(name);
                if (ids.Outcome(ArchiveKinds.Menu, m.Id) == IdOutcome.Renamed)
                {
                    context.Report.Resolved(new TransferSubject(ArchiveKinds.Menu, m.Id, m.Name), id, IdOutcome.Renamed, name);
                }

                database.Menus.Add(Menu.FromArchive(id, site, name, m.CreatedAtUtc, authors.Map(m.CreatedBy), m.UpdatedAtUtc, authors.Map(m.UpdatedBy)));
                var draft = m.Draft is { } content
                    ? MenuDraft.FromArchive(id, site, content.SchemaVersion, ids.RemapDocument(content.Document), content.UpdatedAtUtc, authors.Map(content.UpdatedBy))
                    : MenuDraft.CreateEmpty(site, id, authors.Map(m.CreatedBy), clock);
                database.MenuDrafts.Add(draft);
                menuDrafts.Add((id, draft.Document));
            }
            else if (Replaces(ArchiveKinds.Menu, m.Id))
            {
                if (m.Draft is { } content)
                {
                    var draft = await database.MenuDrafts.SingleAsync(d => d.MenuId == id, cancellationToken).ConfigureAwait(false);
                    draft.Replace(ids.RemapDocument(content.Document), content.SchemaVersion, actor, clock);
                    menuDrafts.Add((id, draft.Document));
                }

                context.Report.Resolved(new TransferSubject(ArchiveKinds.Menu, m.Id, m.Name), id, IdOutcome.Replaced);
            }
            else
            {
                Kept(ArchiveKinds.Menu, m.Id, m.Name);
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported(ArchiveKinds.Page, addedPages.Count);
        context.Report.Imported(ArchiveKinds.Component, componentRows.Count(c => Adds(ArchiveKinds.Component, c.Id)));
        context.Report.Imported(ArchiveKinds.Menu, menuRows.Count(m => Adds(ArchiveKinds.Menu, m.Id)));
        context.Progress.Report(0.3);

        // Publishing: added documents bring their versions and schedules; replaced ones take the
        // archive's live state as their next version.
        var live = await ApplyPublishingAsync(context, versions, publications, (kind, id) => Adds(KindOf(kind), id), cancellationToken).ConfigureAwait(false);
        var replacedLive = await PublishReplacedAsync(context, tree, Live, cancellationToken).ConfigureAwait(false);
        context.Progress.Report(0.6);

        await MergeRedirectsAsync(context, cancellationToken).ConfigureAwait(false);

        // Derived rows, as a save and a publish make them (research ST-03).
        await ReplaceDraftRowsAsync(drafts, menuDrafts, cancellationToken).ConfigureAwait(false);
        var uses = services.GetRequiredService<CollectionUseRows>();
        var allMenus = (await database.Menus.Select(m => m.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        foreach (var (kind, id, text, template) in drafts)
        {
            if (kind == SubjectKinds.Page)
            {
                database.ComponentUsages.AddRange(DependencyExtractor.Components(text).Distinct().Select(c => new ComponentUsage(site, id, c)));
            }

            database.DraftMediaReferences.AddRange(DependencyExtractor.Media(text).Distinct().Select(m => new DraftMediaReference(site, kind, id, m)));
            database.MenuUsages.AddRange(DependencyExtractor.Menus(text).Distinct().Where(allMenus.Contains).Select(m => new MenuUsage(site, kind, id, m)));
            await uses.StageAsync(site, kind, id, CollectionUsageStates.Draft, text, template, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (id, text) in menuDrafts)
        {
            database.MenuTargets.AddRange(MenuTargets.Distinct(MenuTargets.Extract(text)).Select(t => new MenuTarget(site, id, t.Kind, t.Id)));
        }

        var publishing = services.GetRequiredService<PublishingWrites>();
        foreach (var (kind, id, document, number, viaEvent) in live.Select(l => (l.Kind, l.Id, l.Document, l.Number, false)).Concat(replacedLive.Select(l => (l.Kind, l.Id, l.Document, l.Number, true))))
        {
            var page = kind == SubjectKinds.Page ? tree.Pages.Single(p => p.Id == id) : null;
            await publishing.ReplaceDependenciesAsync(site, new PublishingWrites.Subject(kind, id, site, string.Empty, page?.Path, page?.TemplateCollectionId), document, cancellationToken).ConfigureAwait(false);
            if (kind != SubjectKinds.Menu)
            {
                await uses.StageAsync(site, kind, id, CollectionUsageStates.Live, document, page?.TemplateCollectionId, cancellationToken).ConfigureAwait(false);
            }

            // Replaced documents announce their publish through the publication's own event.
            if (viaEvent)
            {
                continue;
            }

            if (kind == SubjectKinds.Page)
            {
                PagesOutbox.Add(database, new PagePublished(Guid.CreateVersion7(), context.NowUtc, site, id, number, "import", page!.Path, context.ActorUserId));
            }
            else if (kind == SubjectKinds.Component)
            {
                PagesOutbox.Add(database, new ComponentPublished(Guid.CreateVersion7(), context.NowUtc, site, id, number, "import", context.ActorUserId));
            }
        }

        if (live.Concat(replacedLive).Any(l => l.Kind == SubjectKinds.Page))
        {
            await services.GetRequiredService<SitemapGeneration>().BumpAsync(site, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
    }

    /// <summary>
    /// FR-024: a replaced page, component or menu takes the archive's live state. Live in the
    /// archive: published as the target's next version, so its earlier versions stay. Not live
    /// in the archive but live here: taken off the public site.
    /// </summary>
    private async Task<List<(string Kind, Guid Id, string Document, int Number)>> PublishReplacedAsync(
        PartApplyContext context,
        PageTree tree,
        Func<string, Guid, VersionRow?> live,
        CancellationToken cancellationToken)
    {
        var site = context.TargetSiteId;
        var actor = context.ActorUserId ?? Guid.Empty;
        var published = new List<(string, Guid, string, int)>();
        var subjects = new List<(string SubjectKind, Guid ArchiveId)>();
        foreach (var p in await Lines<PageRow>(context.Archive, "tree", cancellationToken).ConfigureAwait(false))
        {
            subjects.Add((SubjectKinds.Page, p.Id));
        }

        foreach (var c in await Lines<ComponentRow>(context.Archive, "components", cancellationToken).ConfigureAwait(false))
        {
            subjects.Add((SubjectKinds.Component, c.Id));
        }

        foreach (var m in await Lines<MenuRow>(context.Archive, "menus", cancellationToken).ConfigureAwait(false))
        {
            subjects.Add((SubjectKinds.Menu, m.Id));
        }

        foreach (var (subjectKind, archiveId) in subjects)
        {
            if (context.Ids.Outcome(KindOf(subjectKind), archiveId) != IdOutcome.Replaced)
            {
                continue;
            }

            var id = context.Ids.Resolve(KindOf(subjectKind), archiveId)!.Value;
            var path = subjectKind == SubjectKinds.Page ? tree.Pages.Single(p => p.Id == id).Path : null;
            var publication = await database.Publications
                .FirstOrDefaultAsync(p => p.SubjectKind == subjectKind && p.SubjectId == id, cancellationToken)
                .ConfigureAwait(false);
            if (live(subjectKind, archiveId) is { } version)
            {
                if (publication is null)
                {
                    publication = Publication.Start(site, subjectKind, id);
                    database.Publications.Add(publication);
                }

                var document = context.Ids.RemapDocument(version.Document);
                var created = publication.Publish(document, version.SchemaVersion, version.Title, null, VersionKinds.Publish, null, actor, path, context.NowUtc);
                database.PublishedVersions.Add(created);
                database.VersionMediaReferences.AddRange(DependencyExtractor.Media(document).Distinct().Select(m => new VersionMediaReference(site, created.Id, m)));
                PagesOutbox.AddFrom(database, publication, context.ActorUserId);
                published.Add((subjectKind, id, document, created.Number));
            }
            else if (publication is { IsLive: true })
            {
                publication.Unpublish(scheduled: false, path, context.NowUtc);
                await database.LiveDependencies.Where(d => d.SubjectKind == subjectKind && d.SubjectId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                PagesOutbox.AddFrom(database, publication, context.ActorUserId);
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return published;
    }

    /// <summary>Redirects: added, or the target's taking the archive's target and kind (research ST-10).</summary>
    private async Task MergeRedirectsAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        var ids = context.Ids;
        long count = 0;
        await foreach (var r in context.Archive.ReadLinesAsync<RedirectRow>(PartName, "redirects", cancellationToken).ConfigureAwait(false))
        {
            var subject = new TransferSubject(ArchiveKinds.Redirect, r.Id, r.SourcePath);
            var target = new RedirectTarget(
                r.TargetKind,
                r.TargetPageId is { } page ? ids.Remap(ArchiveKinds.Page, page) : null,
                r.TargetCollectionId is { } collection ? ids.Remap(ArchiveKinds.Collection, collection) : null,
                r.TargetItemId is { } item ? ids.Remap(ArchiveKinds.Item, item) : null,
                r.TargetPath,
                r.TargetUrl);
            switch (ids.Outcome(ArchiveKinds.Redirect, r.Id))
            {
                case IdOutcome.Replaced:
                    var existing = await database.Redirects.SingleAsync(x => x.Id == ids.Resolve(ArchiveKinds.Redirect, r.Id)!.Value, cancellationToken).ConfigureAwait(false);
                    existing.Change(existing.MatchKind, existing.SourcePath, existing.SourceQuery, target, r.StatusCode, r.Enabled, r.Note, context.ActorUserId, context.NowUtc);
                    context.Report.Resolved(subject, existing.Id, IdOutcome.Replaced);
                    break;

                case IdOutcome.Skipped:
                    // Rename cannot apply to a redirect (research ST-10): it is skipped, and said so.
                    context.Report.Resolved(subject, ids.Resolve(ArchiveKinds.Redirect, r.Id)!.Value, IdOutcome.Skipped);
                    break;

                default:
                    database.Redirects.Add(Redirect.FromArchive(
                        ids.Remap(ArchiveKinds.Redirect, r.Id), context.TargetSiteId, r.MatchKind, r.SourcePath, r.SourceQuery, r.StatusCode, target.Kind,
                        target.PageId, target.CollectionId, target.ItemId, target.Path, target.Url, r.Enabled, r.Note, r.Origin, r.CreatedAtUtc,
                        context.Authors.Map(r.CreatedBy), r.UpdatedAtUtc, context.Authors.Map(r.UpdatedBy)));
                    count++;
                    break;
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported(ArchiveKinds.Redirect, count);
    }

    /// <summary>A replaced draft's derived rows are rebuilt: the old ones go first.</summary>
    private async Task ReplaceDraftRowsAsync(
        List<(string Kind, Guid Id, string Text, Guid? Template)> drafts,
        List<(Guid Id, string Text)> menuDrafts,
        CancellationToken cancellationToken)
    {
        var pages = drafts.Where(d => d.Kind == SubjectKinds.Page).Select(d => d.Id).ToList();
        var subjects = drafts.Select(d => d.Id).ToList();
        var menus = menuDrafts.Select(m => m.Id).ToList();
        await database.ComponentUsages.Where(u => pages.Contains(u.PageId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.DraftMediaReferences.Where(r => subjects.Contains(r.SubjectId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.MenuUsages.Where(u => subjects.Contains(u.SubjectId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.MenuTargets.Where(t => menus.Contains(t.MenuId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string KindOf(string subjectKind) => subjectKind switch
    {
        SubjectKinds.Page => ArchiveKinds.Page,
        SubjectKinds.Component => ArchiveKinds.Component,
        _ => ArchiveKinds.Menu,
    };
}
