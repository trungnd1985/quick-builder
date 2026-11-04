using QuickBuilder.Editor.Features.Transfer;
using QuickBuilder.UnitTests.Sites.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Editor.Transfer;

/// <summary>The design part's entries (020 contracts/archive-parts.md §3).</summary>
public sealed class DesignPartGoldenTests
{
    [Fact]
    public void Design_and_version_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new DesignRow(1, ArchiveGolden.Json("""{"schemaVersion":1}"""), ArchiveGolden.At, Guid.CreateVersion7(), 2, 3, ArchiveGolden.At, Guid.CreateVersion7()),
            "draftSchemaVersion,draft,draftUpdatedAtUtc,draftUpdatedBy,liveNumber,nextNumber,liveSinceUtc,liveBy"),
        () => ArchiveGolden.Pins(
            new DesignVersionRow(1, 1, ArchiveGolden.Json("""{"schemaVersion":1}"""), "publish", null, Guid.CreateVersion7(), ArchiveGolden.At),
            "number,schemaVersion,document,kind,restoredFromNumber,publishedBy,publishedAtUtc"));
}
