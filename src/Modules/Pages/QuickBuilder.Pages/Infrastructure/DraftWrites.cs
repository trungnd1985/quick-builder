using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
internal sealed class DraftWrites(
    PagesDbContext database,
    IPageDocumentValidator validator,
    PagesAccess access,
    PageReads reads,
    QuickBuilder.Cms.Contracts.ICollectionSchemaReader collections,
    CollectionUseRows uses,
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

        if (draft.Version != version)
        {
            return Result.Failure<DraftResponse>(PagesErrors.OutOfDate);
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

        if (!clientKeepsEveryFormat && UsesExtendedFormats(draft.Document))
        {
            return Result.Failure<DraftResponse>(PagesErrors.AdminOutdated);
        }

        if (draft.Version != version)
        {
            return Result.Failure<DraftResponse>(PagesErrors.OutOfDate);
        }

        draft.Replace(text, checkedDocument.SchemaVersion, access.Actor, clock);
        await ReplaceMediaReferencesAsync(siteId, SubjectKinds.Component, componentId, text, cancellationToken).ConfigureAwait(false);
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

    private async Task<Result<DraftResponse>> SaveAsync(Draft draft, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved between our read and our write.
            return Result.Failure<DraftResponse>(PagesErrors.OutOfDate);
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
