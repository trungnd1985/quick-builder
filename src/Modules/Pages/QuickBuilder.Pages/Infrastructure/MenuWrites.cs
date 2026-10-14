using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure.Schema;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Creating, duplicating, renaming, deleting menus and saving their drafts (024 FR-001 to
/// FR-011, research NV-02 to NV-06, NV-18).
/// </summary>
/// <remarks>
/// Every write records a <see cref="MenuChange"/> in its own transaction (FR-005). A draft save
/// validates the document before loading anything, then checks only the targets the previous
/// draft did not hold (rule 8, NV-04), then rewrites the <c>menu_targets</c> index with the
/// draft. Publishing is <see cref="PublishingWrites"/>' job, as for components.
/// </remarks>
internal sealed class MenuWrites(
    PagesDbContext database,
    MenuDocumentValidator validator,
    PagesAccess access,
    ICollectionSchemaReader collections,
    PublishingWrites publishing,
    TimeProvider clock,
    ILogger<MenuWrites> logger)
{
    /// <summary>Creates a menu with an empty draft, or a copy of another menu's draft.</summary>
    /// <remarks>
    /// The per-site limit is counted before the insert, so two creates at once at 49 may both
    /// pass. The limit keeps lists short, not data safe (NV-20), so that race is accepted rather
    /// than serialised.
    /// </remarks>
    public async Task<Result<Guid>> CreateAsync(Guid siteId, string name, Guid? duplicateOf, CancellationToken cancellationToken)
    {
        MenuDraft? source = null;
        if (duplicateOf is { } sourceId)
        {
            source = await database.ForSite<MenuDraft>(siteId)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.MenuId == sourceId, cancellationToken)
                .ConfigureAwait(false);
            if (source is null)
            {
                return Result.Failure<Guid>(PagesErrors.MenuNotFound);
            }
        }

        var existing = await database.ForSite<Menu>(siteId).CountAsync(cancellationToken).ConfigureAwait(false);
        var actor = access.Actor;
        var created = Menu.Create(siteId, name, existing, actor, clock);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        var menu = created.Value;
        database.Menus.Add(menu);
        if (source is null)
        {
            database.MenuDrafts.Add(MenuDraft.CreateEmpty(siteId, menu.Id, actor, clock));
        }
        else
        {
            database.MenuDrafts.Add(MenuDraft.CopyOf(source, menu.Id, actor, clock));
            database.MenuTargets.AddRange(MenuTargets.Distinct(MenuTargets.Extract(source.Document))
                .Select(t => new MenuTarget(siteId, menu.Id, t.Kind, t.Id)));
        }

        database.MenuChanges.Add(new MenuChange(
            siteId, menu.Id, source is null ? MenuChangeActions.Created : MenuChangeActions.Duplicated, actor, clock.GetUtcNow()));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            return Result.Failure<Guid>(PagesErrors.DuplicateMenuName);
        }

        logger.LogInformation("Menu {MenuId} created on site {SiteId}.", menu.Id, siteId);
        return Result.Success(menu.Id);
    }

    public async Task<Result> RenameAsync(Guid siteId, Guid menuId, string name, uint version, CancellationToken cancellationToken)
    {
        var menu = await database.ForSite<Menu>(siteId).FirstOrDefaultAsync(m => m.Id == menuId, cancellationToken).ConfigureAwait(false);
        if (menu is null)
        {
            return Result.Failure(PagesErrors.MenuNotFound);
        }

        if (menu.Version != version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        var renamed = menu.Rename(name, access.Actor, clock);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        database.MenuChanges.Add(new MenuChange(siteId, menuId, MenuChangeActions.Renamed, access.Actor, clock.GetUtcNow()));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            return Result.Failure(PagesErrors.DuplicateMenuName);
        }

        return Result.Success();
    }

    /// <summary>Replaces the draft with <paramref name="document"/>, when the caller saw <paramref name="version"/>.</summary>
    public Task<Result> SaveDraftAsync(Guid siteId, Guid menuId, JsonElement document, uint version, CancellationToken cancellationToken) =>
        ReplaceDraftAsync(siteId, menuId, document.GetRawText(), version, cancellationToken);

    /// <summary>
    /// The one way a menu draft changes: a save, or a published version copied into the draft
    /// (012 copy-to-draft), so rule 8 and the target index hold for both. Saves; the caller
    /// commits any surrounding transaction.
    /// </summary>
    /// <param name="version">The draft version the caller saw, or null to replace whatever is there.</param>
    internal async Task<Result> ReplaceDraftAsync(Guid siteId, Guid menuId, string text, uint? version, CancellationToken cancellationToken)
    {
        var checkedDocument = validator.Validate(Encoding.UTF8.GetBytes(text));
        if (!checkedDocument.IsValid)
        {
            return Result.Failure(PagesErrors.InvalidDocument(checkedDocument));
        }

        var draft = await database.ForSite<MenuDraft>(siteId).FirstOrDefaultAsync(d => d.MenuId == menuId, cancellationToken).ConfigureAwait(false);
        if (draft is null)
        {
            return Result.Failure(PagesErrors.MenuNotFound);
        }

        var targets = MenuTargets.Extract(text);
        var known = await database.ForSite<MenuTarget>(siteId)
            .Where(t => t.MenuId == menuId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (await FirstUnknownTargetAsync(siteId, targets, known, cancellationToken).ConfigureAwait(false) is { } unknown)
        {
            return Result.Failure(PagesErrors.InvalidDocument(DocumentValidationResult.Invalid(
                "pages.invalid_menu_document", "unknown_target", unknown.Pointer, "The entry points at a page or item this site does not have.")));
        }

        if (version is { } seen && draft.Version != seen)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        var actor = access.Actor;
        draft.Replace(text, checkedDocument.SchemaVersion, actor, clock);

        var wanted = MenuTargets.Distinct(targets);
        database.MenuTargets.RemoveRange(known.Where(k => !wanted.Contains((k.TargetKind, k.TargetId))));
        database.MenuTargets.AddRange(wanted
            .Where(w => !known.Exists(k => k.TargetKind == w.Kind && k.TargetId == w.Id))
            .Select(w => new MenuTarget(siteId, menuId, w.Kind, w.Id)));

        database.MenuChanges.Add(new MenuChange(siteId, menuId, MenuChangeActions.DraftSaved, actor, clock.GetUtcNow()));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved between our read and our write.
            return Result.Failure(PagesErrors.OutOfDate);
        }

        return Result.Success();
    }

    /// <summary>
    /// Deletes a menu (FR-003): refused while a live page or component shows it; otherwise its
    /// draft, indexes and publishing records go with it, and drafts that used it keep a dangling
    /// <c>menuId</c>.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid siteId, Guid menuId, uint version, CancellationToken cancellationToken)
    {
        var menu = await database.ForSite<Menu>(siteId).FirstOrDefaultAsync(m => m.Id == menuId, cancellationToken).ConfigureAwait(false);
        if (menu is null)
        {
            return Result.Failure(PagesErrors.MenuNotFound);
        }

        if (menu.Version != version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        if (await InUseErrorAsync(siteId, menuId, cancellationToken).ConfigureAwait(false) is { } inUse)
        {
            return Result.Failure(inUse);
        }

        // Its publishing records and the menu go together, or not at all.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var actor = access.Actor;
        await publishing.ForgetAsync(new PublishingWrites.Subject(SubjectKinds.Menu, menuId, siteId, menu.Name, Path: null), actor, cancellationToken)
            .ConfigureAwait(false);

        // The draft, targets and usages go by cascade.
        database.Menus.Remove(menu);
        database.MenuChanges.Add(new MenuChange(siteId, menuId, MenuChangeActions.Deleted, actor, clock.GetUtcNow()));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        logger.LogInformation("Menu {MenuId} deleted from site {SiteId}.", menuId, siteId);
        return Result.Success();
    }

    /// <summary>
    /// FR-003: the refusal when live pages or components show the menu, or null when none do.
    /// Also used before a menu is unpublished.
    /// </summary>
    internal async Task<Error?> InUseErrorAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var users = await database.ForSite<LiveDependency>(siteId)
            .Where(d => d.TargetKind == DependencyKinds.Menu && d.TargetId == menuId)
            .Select(d => new { d.SubjectKind, d.SubjectId })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pages = users.Count(u => u.SubjectKind == SubjectKinds.Page);
        var components = users.Count(u => u.SubjectKind == SubjectKinds.Component);
        return pages + components > 0 ? PagesErrors.MenuInUseByPublishedContent(pages, components) : null;
    }

    /// <summary>
    /// Rule 8 (NV-04): the first target, in document order, that the previous draft did not hold
    /// and that is not a page, or an item of the named collection, of this site.
    /// </summary>
    private async Task<MenuTargetRef?> FirstUnknownTargetAsync(
        Guid siteId, IReadOnlyList<MenuTargetRef> targets, List<MenuTarget> known, CancellationToken cancellationToken)
    {
        var fresh = targets.Where(t => !known.Exists(k => k.TargetKind == t.Kind && k.TargetId == t.Id)).ToList();
        if (fresh.Count == 0)
        {
            return null;
        }

        var pageIds = fresh.Where(t => t.Kind == MenuTargetKinds.Page).Select(t => t.Id).Distinct().ToList();
        var pages = pageIds.Count == 0
            ? []
            : (await database.ForSite<Page>(siteId).Where(p => pageIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();

        var itemIds = fresh.Where(t => t.Kind == MenuTargetKinds.Item).Select(t => t.Id).Distinct().ToList();
        var items = itemIds.Count == 0
            ? new Dictionary<Guid, ItemAddressParts>()
            : await collections.GetItemAddressPartsAsync(siteId, itemIds, cancellationToken).ConfigureAwait(false);

        return fresh.FirstOrDefault(t => t.Kind == MenuTargetKinds.Page
            ? !pages.Contains(t.Id)
            : !items.TryGetValue(t.Id, out var item) || item.CollectionId != t.CollectionId);
    }

    private static bool IsDuplicateName(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_menus_site_name",
        };
}
