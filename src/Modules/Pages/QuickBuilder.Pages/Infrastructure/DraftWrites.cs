using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Saving a draft, for pages and components alike (FR-018, FR-021, research P-08, P-11).
/// </summary>
/// <remarks>
/// The document is validated before anything is loaded, so a refused document never takes
/// a row lock and never changes what is stored. A page draft's component references are
/// checked against this site's components, and its usage rows are rewritten in the same
/// <c>SaveChanges</c> as the draft; the usage foreign keys then make a reference to another
/// site's component, or to one deleted a moment ago, impossible to commit.
/// </remarks>
internal sealed partial class DraftWrites(
    PagesDbContext database,
    IPageDocumentValidator validator,
    PagesAccess access,
    PageReads reads,
    QuickBuilder.Cms.Contracts.ICollectionSchemaReader collections,
    CollectionUseRows uses,
    PageParamRules pageParams,
    ILogger<DraftWrites> logger,
    TimeProvider clock)
{
    /// <summary>
    /// Contract §3 rule 14 (016): every Collection list names a collection of this site. Like
    /// rule 6 for components it needs the site, so it is checked here, after every other rule;
    /// an unknown id and another site's id fail alike.
    /// </summary>
    private async Task<Error?> CheckCollectionsAsync(Guid siteId, DocumentValidationResult checkedDocument, CancellationToken cancellationToken)
    {
        if (checkedDocument.ReferencedCollections.Count == 0)
        {
            return null;
        }

        var known = await collections.ExistingAsync(siteId, [.. checkedDocument.ReferencedCollections.Keys], cancellationToken).ConfigureAwait(false);
        // The validator collected them in document order, so the first unknown is the first in the document.
        var unknown = checkedDocument.ReferencedCollections
            .Where(c => !known.Contains(c.Key))
            .Select(c => c.Value)
            .FirstOrDefault();

        return unknown is null
            ? null
            : PagesErrors.InvalidDocument(DocumentValidationResult.Invalid(
                "pages.invalid_document", "unknown_collection", unknown, "The list names a collection this site does not have."));
    }

    /// <summary>
    /// Whether a stored draft uses a rich text format an admin from before 035 would drop
    /// (035 FR-020, research RT-13). Such a client gets <c>pages.admin_outdated</c> instead.
    /// </summary>
    private static bool UsesExtendedFormats(string storedDocument)
    {
        using var stored = JsonDocument.Parse(storedDocument);
        return RichTextRules.DocumentUsesExtendedFormats(stored.RootElement);
    }

    /// <summary>The request-level rule shared by both draft saves: a document must be sent.</summary>
    internal static ValidationResult CheckRequest(JsonElement document) =>
        document.ValueKind == JsonValueKind.Undefined
            ? ValidationResult.Invalid(new ValidationFailure("document", "A document is required."))
            : ValidationResult.Valid();

    public async Task<Result<DraftResponse>> SavePageDraftAsync(
        Guid siteId,
        Guid pageId,
        JsonElement document,
        uint version,
        bool clientKeepsEveryFormat,
        CancellationToken cancellationToken)
    {
        var text = document.GetRawText();
        var checkedDocument = validator.Validate(Encoding.UTF8.GetBytes(text), DocumentKind.Page);
        if (!checkedDocument.IsValid)
        {
            return Result.Failure<DraftResponse>(PagesErrors.InvalidDocument(checkedDocument));
        }

        var draft = await database.ForSite<PageDraft>(siteId)
            .FirstOrDefaultAsync(d => d.PageId == pageId, cancellationToken)
            .ConfigureAwait(false);

        if (draft is null)
        {
            return Result.Failure<DraftResponse>(PagesErrors.NotFound);
        }

        if (!clientKeepsEveryFormat && UsesExtendedFormats(draft.Document))
        {
            return Result.Failure<DraftResponse>(PagesErrors.AdminOutdated);
        }

        // 017 FR-002, research SE-03: the canonical address and hiding are structure decisions.
        // Compared with the draft, so an unchanged value never stops an Editor's save.
        if (SeoRestrictedFields.FirstChanged(draft.Document, text) is { } restricted
            && !await access.MayChangeStructureAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DraftResponse>(PagesErrors.SeoRestricted(restricted));
        }

        var referenced = checkedDocument.ReferencedComponentIds;
        if (referenced.Count > 0)
        {
            var known = await database.ForSite<Component>(siteId)
                .CountAsync(c => referenced.Contains(c.Id), cancellationToken)
                .ConfigureAwait(false);

            if (known != referenced.Count)
            {
                return Result.Failure<DraftResponse>(PagesErrors.UnknownComponent);
            }
        }

        if (await CheckCollectionsAsync(siteId, checkedDocument, cancellationToken).ConfigureAwait(false) is { } unknownCollection)
        {
            return Result.Failure<DraftResponse>(unknownCollection);
        }

        // Rule 21 (016 FR-027): no page parameter shared with a component this page shows.
        if (await pageParams.CheckPageAsync(siteId, text, live: false, cancellationToken).ConfigureAwait(false) is { } pageParamTaken)
        {
            return Result.Failure<DraftResponse>(pageParamTaken);
        }

        if (draft.Version != version)
        {
            return await ConflictAsync(draft, cancellationToken).ConfigureAwait(false);
        }

        draft.Replace(text, checkedDocument.SchemaVersion, access.Actor, clock);

        var existing = await database.ForSite<ComponentUsage>(siteId)
            .Where(u => u.PageId == pageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        database.ComponentUsages.RemoveRange(existing.Where(u => !referenced.Contains(u.ComponentId)));
        database.ComponentUsages.AddRange(referenced
            .Where(id => !existing.Exists(u => u.ComponentId == id))
            .Select(id => new ComponentUsage(siteId, pageId, id)));

        await ReplaceMediaReferencesAsync(siteId, SubjectKinds.Page, pageId, text, cancellationToken).ConfigureAwait(false);
        await ReplaceMenuUsagesAsync(siteId, SubjectKinds.Page, pageId, text, cancellationToken).ConfigureAwait(false);

        // 016 TB-13: the collections the draft uses, and the page's template, saved with it.
        var template = await database.ForSite<Page>(siteId)
            .Where(p => p.Id == pageId)
            .Select(p => p.TemplateCollectionId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        await uses.StageAsync(siteId, SubjectKinds.Page, pageId, CollectionUsageStates.Draft, text, template, cancellationToken).ConfigureAwait(false);

        return await SaveAsync(draft, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DraftResponse>> SaveComponentDraftAsync(
        Guid siteId,
        Guid componentId,
        JsonElement document,
        uint version,
        bool clientKeepsEveryFormat,
        CancellationToken cancellationToken)
    {
        var text = document.GetRawText();
        var checkedDocument = validator.Validate(Encoding.UTF8.GetBytes(text), DocumentKind.Component);
        if (!checkedDocument.IsValid)
        {
            return Result.Failure<DraftResponse>(PagesErrors.InvalidDocument(checkedDocument));
        }

        var draft = await database.ForSite<ComponentDraft>(siteId)
            .FirstOrDefaultAsync(d => d.ComponentId == componentId, cancellationToken)
            .ConfigureAwait(false);

        if (draft is null)
        {
            return Result.Failure<DraftResponse>(PagesErrors.ComponentNotFound);
        }

        if (await CheckCollectionsAsync(siteId, checkedDocument, cancellationToken).ConfigureAwait(false) is { } unknownCollection)
        {
            return Result.Failure<DraftResponse>(unknownCollection);
        }

        // Rule 21 (016 FR-027): no page parameter shared with a page that shows this component.
        if (await pageParams.CheckComponentAsync(siteId, componentId, text, live: false, cancellationToken).ConfigureAwait(false) is { } pageParamTaken)
        {
            return Result.Failure<DraftResponse>(pageParamTaken);
        }

        if (!clientKeepsEveryFormat && UsesExtendedFormats(draft.Document))
        {
            return Result.Failure<DraftResponse>(PagesErrors.AdminOutdated);
        }

        if (draft.Version != version)
        {
            return await ConflictAsync(draft, cancellationToken).ConfigureAwait(false);
        }

        draft.Replace(text, checkedDocument.SchemaVersion, access.Actor, clock);
        await ReplaceMediaReferencesAsync(siteId, SubjectKinds.Component, componentId, text, cancellationToken).ConfigureAwait(false);
        await ReplaceMenuUsagesAsync(siteId, SubjectKinds.Component, componentId, text, cancellationToken).ConfigureAwait(false);
        await uses.StageAsync(siteId, SubjectKinds.Component, componentId, CollectionUsageStates.Draft, text, null, cancellationToken).ConfigureAwait(false);
        return await SaveAsync(draft, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the draft's media references exactly the files <paramref name="document"/> shows
    /// (013 research MV-05). Tracked changes, saved with the draft, so usage and draft commit
    /// together or not at all.
    /// </summary>
    private async Task ReplaceMediaReferencesAsync(
        Guid siteId, string subjectKind, Guid subjectId, string document, CancellationToken cancellationToken)
    {
        var wanted = DependencyExtractor.Media(document);

        var existing = await database.ForSite<DraftMediaReference>(siteId)
            .Where(r => r.SubjectKind == subjectKind && r.SubjectId == subjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        database.DraftMediaReferences.RemoveRange(existing.Where(r => !wanted.Contains(r.MediaId)));
        database.DraftMediaReferences.AddRange(wanted
            .Where(id => !existing.Exists(r => r.MediaId == id))
            .Select(id => new DraftMediaReference(siteId, subjectKind, subjectId, id)));
    }

    /// <summary>
    /// Makes the draft's menu usage exactly the menus <paramref name="document"/> shows (024
    /// research NV-06), saved with the draft. A menu id that names no menu of the site — one
    /// deleted, or never there — is a dangling reference the draft may keep, so it gets no row:
    /// rows exist only to answer "which drafts show this menu?".
    /// </summary>
    internal async Task ReplaceMenuUsagesAsync(
        Guid siteId, string subjectKind, Guid subjectId, string document, CancellationToken cancellationToken)
    {
        var shown = DependencyExtractor.Menus(document);
        var wanted = shown.Count == 0
            ? []
            : (await database.ForSite<Domain.Menus.Menu>(siteId).Where(m => shown.Contains(m.Id)).Select(m => m.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();

        var existing = await database.ForSite<Domain.Menus.MenuUsage>(siteId)
            .Where(u => u.SubjectKind == subjectKind && u.SubjectId == subjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        database.MenuUsages.RemoveRange(existing.Where(u => !wanted.Contains(u.MenuId)));
        database.MenuUsages.AddRange(wanted
            .Where(id => !existing.Exists(u => u.MenuId == id))
            .Select(id => new Domain.Menus.MenuUsage(siteId, subjectKind, subjectId, id)));
    }

    /// <summary>
    /// 023 research CE-09, FR-003, FR-024: the refusal of an out-of-date draft save, naming who
    /// saved the stored version, when, and its version, read again from the database (the
    /// tracked draft may be the one that lost); and its log entry, with ids only.
    /// </summary>
    private async Task<Result<DraftResponse>> ConflictAsync(Draft draft, CancellationToken cancellationToken)
    {
        var (kind, id) = draft switch
        {
            PageDraft page => (SubjectKinds.Page, page.PageId),
            ComponentDraft component => (SubjectKinds.Component, component.ComponentId),
            _ => throw new InvalidOperationException($"Unknown draft type {draft.GetType().Name}."),
        };

        var stored = kind == SubjectKinds.Page
            ? await database.ForSite<PageDraft>(draft.SiteId).AsNoTracking()
                .Where(d => d.PageId == id)
                .Select(d => new { d.UpdatedBy, d.UpdatedAtUtc, d.Version })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : await database.ForSite<ComponentDraft>(draft.SiteId).AsNoTracking()
                .Where(d => d.ComponentId == id)
                .Select(d => new { d.UpdatedBy, d.UpdatedAtUtc, d.Version })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            // Deleted while we were saving.
            return Result.Failure<DraftResponse>(kind == SubjectKinds.Page ? PagesErrors.NotFound : PagesErrors.ComponentNotFound);
        }

        var names = await access.NamesAsync([stored.UpdatedBy], cancellationToken).ConfigureAwait(false);
        LogDraftConflict(logger, draft.SiteId, kind, id, access.Actor, stored.UpdatedBy);
        return Result.Failure<DraftResponse>(PagesErrors.DraftOutOfDate(
            stored.UpdatedBy, names.GetValueOrDefault(stored.UpdatedBy) ?? string.Empty, stored.UpdatedAtUtc, stored.Version));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Draft save refused as out of date: site {SiteId}, {Kind} {DocumentId}, by {ActorId}; the stored draft was saved by {ChangedById}")]
    private static partial void LogDraftConflict(ILogger logger, Guid siteId, string kind, Guid documentId, Guid actorId, Guid changedById);

    private async Task<Result<DraftResponse>> SaveAsync(Draft draft, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved between our read and our write.
            return await ConflictAsync(draft, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: "fk_component_usages_components_site_id_component_id",
        })
        {
            // A referenced component was deleted after we checked it (research P-11).
            return Result.Failure<DraftResponse>(PagesErrors.UnknownComponent);
        }

        return Result.Success(await reads.DraftAsync(draft, cancellationToken).ConfigureAwait(false));
    }
}
