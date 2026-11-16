using System.Text.Json;
using QuickBuilder.Sites.Contracts.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// Pins a part's entry rows to the archive contract (020 contracts/archive-parts.md §3, T024):
/// the field names and their order as written, and a lossless read back. Renaming or dropping a
/// field changes archive format 1 and must fail here first.
/// </summary>
internal static class ArchiveGolden
{
    public static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public static void Pins<T>(T row, string fields)
    {
        var line = JsonSerializer.Serialize(row, ArchiveJson.Options);

        Assert.DoesNotContain('\n', line);
        using var document = JsonDocument.Parse(line);
        Assert.Equal(fields, string.Join(",", document.RootElement.EnumerateObject().Select(p => p.Name)));

        var back = JsonSerializer.Deserialize<T>(line, ArchiveJson.Options);
        Assert.Equal(line, JsonSerializer.Serialize(back, ArchiveJson.Options));
    }

    public static JsonElement Json(string json) => JsonElement.Parse(json);
}
