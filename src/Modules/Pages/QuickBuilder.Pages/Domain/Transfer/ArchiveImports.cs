using QuickBuilder.Shared.Kernel.Results;

// 020 FR-021: the Pages rows of an imported site, as the archive holds them. Each factory takes
// rows the import's check has validated, under the ids the import's id map gives them, with
// documents already remapped into the target site (research ST-08, ST-09). Derived rows are not
// set here: the part rebuilds them from the documents (research ST-03).

namespace QuickBuilder.Pages.Domain
{
    /// <summary>The fields of an imported page.</summary>
    internal sealed record ImportedPage(
        Guid Id,
        Guid? ParentId,
        int Position,
        string Title,
        string Slug,
        bool IsHome,
        Guid? TemplateCollectionId,
        CopyOrigin? CopyOrigin,
        DateTimeOffset CreatedAtUtc,
        Guid CreatedBy,
        DateTimeOffset UpdatedAtUtc,
        Guid UpdatedBy);

    /// <summary>A site's page tree, imported whole.</summary>
    internal sealed partial class PageTree
    {
        /// <summary>
        /// The tree of a site that has none yet, holding <paramref name="pages"/>. Addresses and
        /// depths are computed by the same planning as any change, so a duplicate address, a
        /// reserved first segment or a tree that is too deep refuses the import (page-schema §5).
        /// </summary>
        public static Result<PageTree> Import(Guid siteId, IReadOnlyList<ImportedPage> pages, DateTimeOffset nowUtc)
        {
            ArgumentNullException.ThrowIfNull(pages);

            var tree = new PageTree(siteId, nowUtc);
            foreach (var row in pages)
            {
                tree._pages.Add(Page.FromArchive(siteId, row));
            }

            if (tree._pages.Count(p => p.IsHome) > 1)
            {
                return Result.Failure<PageTree>(PagesErrors.HomePageExists);
            }

            var ids = tree._pages.Select(p => p.Id).ToHashSet();
            if (tree._pages.Any(p => p.ParentId is { } parent && !ids.Contains(parent)))
            {
                return Result.Failure<PageTree>(PagesErrors.ParentNotFound);
            }

            var plan = tree.Plan(Shape.Of);
            if (plan.IsFailure)
            {
                return Result.Failure<PageTree>(plan.Error);
            }

            tree.Apply(plan.Value);

            // A page the plan cannot reach from the top hangs under a cycle.
            if (tree._pages.Any(p => p.Path.Length == 0))
            {
                return Result.Failure<PageTree>(PagesErrors.ParentNotFound);
            }

            tree.PageCount = tree._pages.Count;
            return Result.Success(tree);
        }

        /// <summary>
        /// Adds an archive's pages to this tree in a merge (020 FR-026, research ST-10). Parents
        /// come first; a parent is a page of this tree or one added before it. Each added page goes
        /// after its new siblings, in the archive's order. A slug its siblings already use takes
        /// <c>-2</c>, <c>-3</c>… (010 rules); a home page is added as one only where the tree has
        /// none. The whole tree is then planned as any change is, so a broken rule refuses it all.
        /// </summary>
        /// <returns>The pages whose slug changed, with the slug they took.</returns>
        public Result<IReadOnlyList<(Guid Id, string Slug)>> Merge(
            IReadOnlyList<ImportedPage> pages,
            Func<string, Func<string, bool>, string> nextSlug,
            DateTimeOffset nowUtc)
        {
            ArgumentNullException.ThrowIfNull(pages);
            ArgumentNullException.ThrowIfNull(nextSlug);

            if (_pages.Count + pages.Count > QuickBuilder.Pages.Contracts.PageAddresses.MaxPagesPerSite)
            {
                return Result.Failure<IReadOnlyList<(Guid, string)>>(PagesErrors.TooManyPages);
            }

            var renamed = new List<(Guid, string)>();
            foreach (var row in pages)
            {
                if (row.ParentId is { } parent && Find(parent) is null)
                {
                    return Result.Failure<IReadOnlyList<(Guid, string)>>(PagesErrors.ParentNotFound);
                }

                if (row.ParentId is { } templateParent && Find(templateParent)!.IsTemplate)
                {
                    return Result.Failure<IReadOnlyList<(Guid, string)>>(PagesErrors.TemplateHasChildren);
                }

                var isHome = row.IsHome && row.ParentId is null && Home is null;

                // A top-level page and a child of the home page share one address space.
                var space = row.ParentId is { } p && !Find(p)!.IsHome ? row.ParentId : null;
                var taken = _pages
                    .Where(x => !x.IsHome && !x.IsTemplate && (x.ParentId == space || (space is null && x.ParentId is { } xp && Find(xp)!.IsHome)))
                    .Select(x => x.Slug)
                    .ToHashSet(StringComparer.Ordinal);
                var slug = !isHome && row.TemplateCollectionId is null && taken.Contains(row.Slug) ? nextSlug(row.Slug, taken.Contains) : row.Slug;
                if (!string.Equals(slug, row.Slug, StringComparison.Ordinal))
                {
                    renamed.Add((row.Id, slug));
                }

                var position = Children(row.ParentId).Count;
                _pages.Add(Page.FromArchive(SiteId, row with { Slug = slug, IsHome = isHome, Position = position }));
            }

            var plan = Plan(Shape.Of);
            if (plan.IsFailure)
            {
                return Result.Failure<IReadOnlyList<(Guid, string)>>(plan.Error);
            }

            Apply(plan.Value);
            PageCount = _pages.Count;
            UpdatedAtUtc = nowUtc;
            return Result.Success<IReadOnlyList<(Guid, string)>>(renamed);
        }
    }

    /// <summary>An imported page.</summary>
    internal sealed partial class Page
    {
        public static Page FromArchive(Guid siteId, ImportedPage row)
        {
            ArgumentNullException.ThrowIfNull(row);
            var page = new Page
            {
                Id = row.Id,
                SiteId = siteId,
                ParentId = row.ParentId,
                Position = row.Position,
                Title = row.Title,
                Slug = row.Slug,
                IsHome = row.IsHome,
                TemplateCollectionId = row.TemplateCollectionId,
                Version = 1,
                CreatedAtUtc = row.CreatedAtUtc,
                CreatedBy = row.CreatedBy,
                UpdatedAtUtc = row.UpdatedAtUtc,
                UpdatedBy = row.UpdatedBy,
            };

            if (row.CopyOrigin is { } origin)
            {
                page.MarkCopyOf(origin);
            }

            return page;
        }
    }

    /// <summary>An imported draft of a page or component.</summary>
    internal abstract partial class Draft
    {
        /// <summary>Takes an imported document as it was last saved, by whom and when.</summary>
        internal void ImportDocument(string document, int schemaVersion, Guid updatedBy, DateTimeOffset updatedAtUtc)
        {
            ArgumentException.ThrowIfNullOrEmpty(document);
            Document = document;
            SchemaVersion = schemaVersion;
            UpdatedBy = updatedBy;
            UpdatedAtUtc = updatedAtUtc;
        }
    }

    /// <summary>An imported shared component.</summary>
    internal sealed partial class Component
    {
        public static Component FromArchive(
            Guid id,
            Guid siteId,
            string name,
            CopyOrigin? copyOrigin,
            DateTimeOffset createdAtUtc,
            Guid createdBy,
            DateTimeOffset updatedAtUtc,
            Guid updatedBy) => new()
            {
                Id = id,
                SiteId = siteId,
                Name = name,
                CopiedFromId = copyOrigin?.SourceId,
                CopiedFromSavedAtUtc = copyOrigin?.SourceSavedAtUtc,
                CopiedBy = copyOrigin?.CopiedBy,
                CopiedAtUtc = copyOrigin?.CopiedAtUtc,
                CreatedAtUtc = createdAtUtc,
                CreatedBy = createdBy,
                UpdatedAtUtc = updatedAtUtc,
                UpdatedBy = updatedBy,
            };
    }

    /// <summary>An imported publication: which retained version of a page, component or menu is live.</summary>
    internal sealed partial class Publication
    {
        public static Publication FromArchive(
            Guid siteId,
            string subjectKind,
            Guid subjectId,
            Guid? liveVersionId,
            int? liveNumber,
            int nextNumber,
            DateTimeOffset? liveSinceUtc,
            Guid? liveBy,
            IReadOnlyCollection<string> pageParamNames)
        {
            var publication = new Publication(siteId, subjectKind, subjectId)
            {
                LiveVersionId = liveVersionId,
                LiveNumber = liveVersionId is null ? null : liveNumber,
                NextNumber = Math.Max(nextNumber, (liveNumber ?? 0) + 1),
                LiveSinceUtc = liveVersionId is null ? null : liveSinceUtc,
                LiveBy = liveVersionId is null ? null : liveBy,
            };
            publication.SetPageParamNames(pageParamNames);
            return publication;
        }
    }

    /// <summary>An imported retained version.</summary>
    internal sealed partial class PublishedVersion
    {
        public static PublishedVersion FromArchive(
            Guid id,
            Guid siteId,
            string subjectKind,
            Guid subjectId,
            int number,
            int schemaVersion,
            string document,
            string? title,
            string kind,
            int? restoredFromNumber,
            Guid publishedBy,
            DateTimeOffset publishedAtUtc) =>
            new(siteId, subjectKind, subjectId, number, schemaVersion, document, title, kind, restoredFromNumber, publishedBy, publishedAtUtc) { Id = id };
    }

    /// <summary>An imported pending schedule.</summary>
    internal sealed partial class PublishSchedule
    {
        public static PublishSchedule FromArchive(
            Guid id,
            Guid siteId,
            string subjectKind,
            Guid subjectId,
            string action,
            DateTimeOffset dueAtUtc,
            int revision,
            string? capturedDocument,
            int? capturedSchemaVersion,
            string? capturedTitle,
            Guid? redirectToPageId,
            Guid createdBy,
            DateTimeOffset createdAtUtc) => new()
            {
                Id = id,
                SiteId = siteId,
                SubjectKind = subjectKind,
                SubjectId = subjectId,
                Action = action,
                DueAtUtc = dueAtUtc,
                Revision = revision,
                State = ScheduleStates.Pending,
                CapturedDocument = capturedDocument,
                CapturedSchemaVersion = capturedSchemaVersion,
                CapturedTitle = capturedTitle,
                RedirectToPageId = redirectToPageId,
                CreatedBy = createdBy,
                CreatedAtUtc = createdAtUtc,
            };
    }
}

namespace QuickBuilder.Pages.Domain.Menus
{
    /// <summary>An imported menu.</summary>
    internal sealed partial class Menu
    {
        public static Menu FromArchive(Guid id, Guid siteId, string name, DateTimeOffset createdAtUtc, Guid createdBy, DateTimeOffset updatedAtUtc, Guid updatedBy) => new()
        {
            Id = id,
            SiteId = siteId,
            Name = name,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = updatedAtUtc,
            UpdatedBy = updatedBy,
        };
    }

    /// <summary>An imported menu draft.</summary>
    internal sealed partial class MenuDraft
    {
        public static MenuDraft FromArchive(Guid menuId, Guid siteId, int schemaVersion, string document, DateTimeOffset updatedAtUtc, Guid updatedBy) => new()
        {
            MenuId = menuId,
            SiteId = siteId,
            SchemaVersion = schemaVersion,
            Document = document,
            UpdatedAtUtc = updatedAtUtc,
            UpdatedBy = updatedBy,
        };
    }
}

namespace QuickBuilder.Pages.Domain.Redirects
{
    /// <summary>An imported redirect.</summary>
    internal sealed partial class Redirect
    {
        public static Redirect FromArchive(
            Guid id,
            Guid siteId,
            string matchKind,
            string sourcePath,
            string sourceQuery,
            int statusCode,
            string targetKind,
            Guid? targetPageId,
            Guid? targetCollectionId,
            Guid? targetItemId,
            string? targetPath,
            string? targetUrl,
            bool enabled,
            string? note,
            string origin,
            DateTimeOffset createdAtUtc,
            Guid? createdBy,
            DateTimeOffset updatedAtUtc,
            Guid? updatedBy) => new()
            {
                Id = id,
                SiteId = siteId,
                MatchKind = matchKind,
                SourcePath = sourcePath,
                SourceQuery = sourceQuery,
                StatusCode = statusCode,
                TargetKind = targetKind,
                TargetPageId = targetPageId,
                TargetCollectionId = targetCollectionId,
                TargetItemId = targetItemId,
                TargetPath = targetPath,
                TargetUrl = targetUrl,
                Enabled = enabled,
                Note = note,
                Origin = origin,
                CreatedAtUtc = createdAtUtc,
                CreatedBy = createdBy,
                UpdatedAtUtc = updatedAtUtc,
                UpdatedBy = updatedBy,
            };
    }
}
