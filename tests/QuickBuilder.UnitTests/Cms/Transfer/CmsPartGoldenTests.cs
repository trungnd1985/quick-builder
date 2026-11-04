using QuickBuilder.Cms.Features.Transfer;
using QuickBuilder.UnitTests.Sites.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Cms.Transfer;

/// <summary>The Cms part's entries (020 contracts/archive-parts.md §3).</summary>
public sealed class CmsPartGoldenTests
{
    private static readonly Guid A = Guid.CreateVersion7();

    [Fact]
    public void Collection_and_schema_change_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new CollectionRow(A, "Posts", "posts", null, 1, ArchiveGolden.Json("""{"schemaVersion":1,"fields":[]}"""), 3, 2, 10, 20, ArchiveGolden.At, A, ArchiveGolden.At, A),
            "id,name,slug,description,schemaVersion,schema,revision,schemaRevision,nextFieldNumber,nextOptionNumber,createdAtUtc,createdBy,updatedAtUtc,updatedBy"),
        () => ArchiveGolden.Pins(
            new SchemaChangeRow(A, 2, ArchiveGolden.At, A, ArchiveGolden.Json("[]")),
            "collectionId,revision,changedAtUtc,changedBy,summary"));

    [Fact]
    public void Item_version_schedule_and_history_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new ItemRow(A, A, "hello", "Hello", "changed", ArchiveGolden.Json("""{"schemaVersion":1,"values":{}}"""), 4, 2, A, ArchiveGolden.At, "member",
                ArchiveGolden.Json("""{"schemaVersion":1,"values":{}}"""), "hello", ArchiveGolden.At, A, ArchiveGolden.At, 1, 2, null, null, null, null,
                ArchiveGolden.At, A, ArchiveGolden.At, A),
            "id,collectionId,slug,name,status,draft,revision,draftRevision,draftChangedBy,draftChangedAtUtc,draftChangeKind,published,publishedSlug,publishedAtUtc,publishedBy,firstPublishedAtUtc,liveVersion,nextVersionNumber,copiedFromId,copiedFromSavedAtUtc,copiedBy,copiedAtUtc,createdAtUtc,createdBy,updatedAtUtc,updatedBy"),
        () => ArchiveGolden.Pins(
            new ItemVersionRow(A, 1, ArchiveGolden.Json("""{"schemaVersion":1,"values":{}}"""), "hello", "publish", null, A, ArchiveGolden.At),
            "itemId,number,values,slug,kind,restoredFrom,publishedBy,publishedAtUtc"),
        () => ArchiveGolden.Pins(
            new ItemScheduleRow(A, A, "publish", ArchiveGolden.At, 3, null, null, false, null, A, ArchiveGolden.At),
            "id,itemId,action,dueAtUtc,revision,capturedValues,capturedSlug,redirectOldAddress,redirectToPageId,createdBy,createdAtUtc"),
        () => ArchiveGolden.Pins(
            new ItemHistoryRow(A, A, "published", 1, null, null, null, ArchiveGolden.At, A),
            "id,itemId,kind,versionNumber,scheduleAction,dueAtUtc,failureCode,atUtc,by"));
}
