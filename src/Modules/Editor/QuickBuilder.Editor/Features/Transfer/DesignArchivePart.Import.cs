using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Editor.Features.Transfer;

/// <summary>Checking and applying the design part (020 FR-021, FR-021a, FR-027; research ST-10, ST-11, ST-21).</summary>
/// <remarks>
/// A site has one design, so a merge into a site that has one always meets it. Replace applies
/// the archive's draft and publishes its live design as the target's next version, so the
/// target's earlier versions stay in its history (FR-024). Skip and rename keep the target's
/// design and add only the tokens its draft lacks, unpublished (research ST-10).
/// </remarks>
internal sealed partial class DesignArchivePart
{
    private const string DesignLabel = "Design tokens";

    public async Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // FR-014: every token document against the token rules a save must pass.
        var validator = services.GetRequiredService<TokenSetValidator>();
        void Check(string label, JsonElement document)
        {
            var result = validator.Validate(JsonSerializer.SerializeToUtf8Bytes(document));
            if (result.IsFailure)
            {
                context.Report.Error("transfer.document_invalid", new TransferSubject(ArchiveKinds.Design, context.Manifest.Source.SiteId, label), result.Error!.Message);
            }
        }

        var hasDesign = false;
        await foreach (var design in context.Archive.ReadLinesAsync<DesignRow>(PartName, "design", cancellationToken).ConfigureAwait(false))
        {
            if (design.Draft is { } draft)
            {
                Check("Design draft", draft);
                hasDesign = true;
            }

            hasDesign |= design.LiveNumber is > 0;
        }

        var versions = 0;
        await foreach (var v in context.Archive.ReadLinesAsync<DesignVersionRow>(PartName, "design-versions", cancellationToken).ConfigureAwait(false))
        {
            Check($"Design version {v.Number}", v.Document);
            versions++;
        }

        context.Report.Count("design_versions", versions);

        if (context.Mode == ImportMode.Merge && hasDesign && context.Session is { } session && context.TargetSiteId is { } target)
        {
            database.JoinTransfer(session.Connection, session.Transaction);
            var draft = await database.DesignTokenSets.AsNoTracking().Select(s => new { s.Version }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var live = await database.DesignPublications.AsNoTracking().Select(p => new { p.LiveNumber }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (draft is not null || live?.LiveNumber is > 0)
            {
                context.Report.Conflict(new TransferSubject(ArchiveKinds.Design, context.Manifest.Source.SiteId, DesignLabel), target, MatchedBy.Identity);

                // The apply refuses when the target's design changed since this check (ST-06).
                context.Ids.Note($"design:{draft?.Version}:{live?.LiveNumber}");
            }
        }
    }

    public async Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        var site = context.TargetSiteId;
        if (context.Mode == ImportMode.Restore)
        {
            await database.DesignVersions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.DesignPublications.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.DesignTokenSets.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        DesignRow? design = null;
        await foreach (var row in context.Archive.ReadLinesAsync<DesignRow>(PartName, "design", cancellationToken).ConfigureAwait(false))
        {
            design = row;
        }

        var versions = new List<DesignVersionRow>();
        await foreach (var row in context.Archive.ReadLinesAsync<DesignVersionRow>(PartName, "design-versions", cancellationToken).ConfigureAwait(false))
        {
            versions.Add(row);
        }

        if (context.Mode == ImportMode.Merge)
        {
            await MergeAsync(context, design, versions, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (context.StartFresh)
        {
            // FR-021a: one draft, nothing published. The draft is the archive's, or its live
            // version where the design was published but never edited after.
            var live = versions.FirstOrDefault(v => v.Number == design?.LiveNumber);
            if (design?.Draft is { } draftDocument)
            {
                database.DesignTokenSets.Add(DesignTokenSet.Import(site, context.Ids.RemapDocument(draftDocument), design.DraftSchemaVersion!.Value, ActorOrAuthor(context, design.DraftUpdatedBy), context.NowUtc));
            }
            else if (live is not null)
            {
                database.DesignTokenSets.Add(DesignTokenSet.Import(site, context.Ids.RemapDocument(live.Document), live.SchemaVersion, ActorOrAuthor(context, live.PublishedBy), context.NowUtc));
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (design?.Draft is { } draft)
        {
            database.DesignTokenSets.Add(DesignTokenSet.Import(
                site, context.Ids.RemapDocument(draft), design.DraftSchemaVersion!.Value, context.Authors.Map(design.DraftUpdatedBy ?? Guid.Empty), design.DraftUpdatedAtUtc ?? context.NowUtc));
        }

        foreach (var v in versions)
        {
            database.DesignVersions.Add(new DesignVersion(
                site, v.Number, v.SchemaVersion, context.Ids.RemapDocument(v.Document), v.Kind, v.RestoredFromNumber, context.Authors.Map(v.PublishedBy), v.PublishedAtUtc));
        }

        if (design?.LiveNumber is { } liveNumber && liveNumber > 0)
        {
            database.DesignPublications.Add(DesignPublication.Import(
                site, liveNumber, design.NextNumber ?? liveNumber + 1, design.LiveSinceUtc ?? context.NowUtc, context.Authors.Map(design.LiveBy ?? Guid.Empty)));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported("design_versions", versions.Count);
    }

    /// <summary>Research ST-10, row "design tokens".</summary>
    private async Task MergeAsync(PartApplyContext context, DesignRow? design, List<DesignVersionRow> versions, CancellationToken cancellationToken)
    {
        var site = context.TargetSiteId;
        var clock = services.GetRequiredService<TimeProvider>();
        var actor = ActorOrAuthor(context, design?.DraftUpdatedBy);
        var live = versions.FirstOrDefault(v => v.Number == design?.LiveNumber);
        (string Document, int SchemaVersion)? incoming = design?.Draft is { } d
            ? (context.Ids.RemapDocument(d), design.DraftSchemaVersion!.Value)
            : live is not null ? (context.Ids.RemapDocument(live.Document), live.SchemaVersion) : null;
        if (incoming is not { } archive)
        {
            return;
        }

        var tokens = await database.DesignTokenSets.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var subject = new TransferSubject(ArchiveKinds.Design, context.Manifest.Source.SiteId, DesignLabel);
        if (tokens is null)
        {
            // Nothing to meet: the archive's draft is the target's first.
            database.DesignTokenSets.Add(DesignTokenSet.Import(site, archive.Document, archive.SchemaVersion, actor, context.NowUtc));
        }
        else if (context.Policy == ConflictPolicy.Replace)
        {
            tokens.Replace(archive.Document, archive.SchemaVersion, actor, clock);
            context.Report.Resolved(subject, site, IdOutcome.Replaced);
        }
        else if (tokens.SchemaVersion != archive.SchemaVersion)
        {
            // Tokens of two schema versions are not combined; the target's design is kept.
            context.Report.Resolved(subject, site, IdOutcome.Skipped);
        }
        else
        {
            var merged = AddMissing(JsonNode.Parse(tokens.Document), JsonNode.Parse(archive.Document));
            var validator = services.GetRequiredService<TokenSetValidator>();
            if (merged.Changed && validator.Validate(System.Text.Encoding.UTF8.GetBytes(merged.Node!.ToJsonString())).IsSuccess)
            {
                tokens.Replace(merged.Node!.ToJsonString(), tokens.SchemaVersion, actor, clock);
            }

            context.Report.Resolved(subject, site, IdOutcome.Skipped);
        }

        // Replace takes the archive's live state too, as the target's next version (FR-024).
        if (live is not null && (tokens is null || context.Policy == ConflictPolicy.Replace))
        {
            var publication = await database.DesignPublications.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (publication is null)
            {
                publication = DesignPublication.Start(site);
                database.DesignPublications.Add(publication);
            }

            var version = publication.Publish(context.Ids.RemapDocument(live.Document), live.SchemaVersion, tokenSetVersion: -1, actor, context.NowUtc);
            database.DesignVersions.Add(version);
            database.OutboxMessages.Add(OutboxMessage.From(new DesignPublished(
                Guid.CreateVersion7(), context.NowUtc, site, version.Number, version.Kind, version.RestoredFromNumber, actor)));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The target's tokens with every token of the archive's that the target lacks added; a
    /// token both hold keeps the target's value.
    /// </summary>
    internal static (JsonNode? Node, bool Changed) AddMissing(JsonNode? target, JsonNode? archive)
    {
        if (target is not JsonObject into || archive is not JsonObject from)
        {
            return (target, false);
        }

        var changed = false;
        foreach (var (key, value) in from.ToList())
        {
            if (!into.ContainsKey(key))
            {
                into[key] = value?.DeepClone();
                changed = true;
            }
            else
            {
                changed |= AddMissing(into[key], value).Changed;
            }
        }

        return (into, changed);
    }

    /// <summary>Start fresh names the importing Administrator; the command line keeps the archive's author.</summary>
    private static Guid ActorOrAuthor(PartApplyContext context, Guid? archiveAuthor) =>
        context.ActorUserId ?? context.Authors.Map(archiveAuthor ?? Guid.Empty);
}
