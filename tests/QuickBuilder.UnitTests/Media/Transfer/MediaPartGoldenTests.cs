using QuickBuilder.Media.Features.Transfer;
using QuickBuilder.UnitTests.Sites.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Media.Transfer;

/// <summary>The Media part's entries (020 contracts/archive-parts.md §3).</summary>
public sealed class MediaPartGoldenTests
{
    [Fact]
    public void Folder_and_file_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new MediaFolderRow(Guid.CreateVersion7(), null, "Photos", ArchiveGolden.At, Guid.CreateVersion7()),
            "id,parentId,name,createdAtUtc,createdBy"),
        () => ArchiveGolden.Pins(
            new MediaFileRow(Guid.CreateVersion7(), null, "logo.png", "image/png", "png", 1234, new string('a', 64), "Logo", 64, 32, false, 2,
                ArchiveGolden.At, Guid.CreateVersion7(), ArchiveGolden.At, Guid.CreateVersion7(), MediaFileRow.Included),
            "id,folderId,fileName,contentType,extension,sizeBytes,sha256,altText,width,height,isAnimated,contentVersion,uploadedAtUtc,uploadedBy,replacedAtUtc,replacedBy,bytes"));
}
