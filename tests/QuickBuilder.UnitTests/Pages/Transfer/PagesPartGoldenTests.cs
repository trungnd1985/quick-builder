using QuickBuilder.Pages.Features.Transfer;
using QuickBuilder.UnitTests.Sites.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Pages.Transfer;

/// <summary>The Pages part's entries (020 contracts/archive-parts.md §3).</summary>
public sealed class PagesPartGoldenTests
{
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly DraftRow Draft = new(A, 1, ArchiveGolden.Json("""{"schemaVersion":1}"""), ArchiveGolden.At, A);

    [Fact]
    public void Tree_draft_component_and_menu_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new PageRow(A, null, 0, "About", "about", false, null, null, null, null, null, ArchiveGolden.At, A, ArchiveGolden.At, A),
            "id,parentId,position,title,slug,isHome,templateCollectionId,copiedFromId,copiedFromSavedAtUtc,copiedBy,copiedAtUtc,createdAtUtc,createdBy,updatedAtUtc,updatedBy"),
        () => ArchiveGolden.Pins(Draft, "subjectId,schemaVersion,document,updatedAtUtc,updatedBy"),
        () => ArchiveGolden.Pins(
            new ComponentRow(A, "Header", null, null, null, null, ArchiveGolden.At, A, ArchiveGolden.At, A, Draft),
            "id,name,copiedFromId,copiedFromSavedAtUtc,copiedBy,copiedAtUtc,createdAtUtc,createdBy,updatedAtUtc,updatedBy,draft"),
        () => ArchiveGolden.Pins(
            new MenuRow(A, "Main", ArchiveGolden.At, A, ArchiveGolden.At, A, Draft),
            "id,name,createdAtUtc,createdBy,updatedAtUtc,updatedBy,draft"));

    [Fact]
    public void Publication_version_schedule_and_redirect_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new PublicationRow("page", A, 2, 3, ArchiveGolden.At, A),
            "subjectKind,subjectId,liveNumber,nextNumber,liveSinceUtc,liveBy"),
        () => ArchiveGolden.Pins(
            new VersionRow(A, "page", A, 2, 1, ArchiveGolden.Json("""{"schemaVersion":1}"""), "About", "publish", null, A, ArchiveGolden.At),
            "id,subjectKind,subjectId,number,schemaVersion,document,title,kind,restoredFromNumber,publishedBy,publishedAtUtc"),
        () => ArchiveGolden.Pins(
            new ScheduleRow(A, "page", A, "publish", ArchiveGolden.At, 1, null, null, null, null, A, ArchiveGolden.At),
            "id,subjectKind,subjectId,action,dueAtUtc,revision,capturedDocument,capturedSchemaVersion,capturedTitle,redirectToPageId,createdBy,createdAtUtc"),
        () => ArchiveGolden.Pins(
            new RedirectRow(A, "exact", "/old", string.Empty, 301, "page", A, null, null, null, null, true, null, "manual", ArchiveGolden.At, A, ArchiveGolden.At, A),
            "id,matchKind,sourcePath,sourceQuery,statusCode,targetKind,targetPageId,targetCollectionId,targetItemId,targetPath,targetUrl,enabled,note,origin,createdAtUtc,createdBy,updatedAtUtc,updatedBy"));
}
