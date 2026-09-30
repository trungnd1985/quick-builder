using QuickBuilder.Media.Features.ListFiles;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Cursors round-trip for each sort and refuse reuse with another sort or query (013 MV-03).</summary>
public sealed class ListingCursorTests
{
    private static readonly uint Query = ListingCursor.Fingerprint("folder-1", "folder", "photo", "image");

    [Theory]
    [InlineData((int)ListingSort.Uploaded, true, 638_000_000_000_000_000L, null)]
    [InlineData((int)ListingSort.Size, false, 20_971_520L, null)]
    [InlineData((int)ListingSort.Name, false, 0L, "nguyen photo.jpg")]
    [InlineData((int)ListingSort.Name, true, 0L, "")]
    public void Cursors_round_trip(int sortValue, bool descending, long number, string? text)
    {
        var sort = (ListingSort)sortValue;
        var cursor = new ListingCursor(sort, descending, Query, Guid.NewGuid(), number, text);

        Assert.Equal(cursor, ListingCursor.TryDecode(cursor.Encode(), sort, descending, Query));
    }

    [Fact]
    public void A_cursor_from_another_sort_direction_or_query_is_refused()
    {
        var encoded = new ListingCursor(ListingSort.Uploaded, true, Query, Guid.NewGuid(), 1, null).Encode();

        Assert.Null(ListingCursor.TryDecode(encoded, ListingSort.Size, true, Query));
        Assert.Null(ListingCursor.TryDecode(encoded, ListingSort.Uploaded, false, Query));
        Assert.Null(ListingCursor.TryDecode(encoded, ListingSort.Uploaded, true, ListingCursor.Fingerprint("folder-2", "folder", "photo", "image")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !")]
    [InlineData("AAAA")]
    [InlineData("AQEBAAAAAA")]
    public void Malformed_input_gives_null(string value) =>
        Assert.Null(ListingCursor.TryDecode(value, ListingSort.Uploaded, true, Query));

    [Fact]
    public void Fingerprints_tell_empty_and_missing_filters_apart() =>
        Assert.NotEqual(ListingCursor.Fingerprint(null, "x"), ListingCursor.Fingerprint("", "x"));
}
