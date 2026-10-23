using System.Text.Json;

namespace QuickBuilder.Release;

/// <summary>
/// The release's deliberate changes to what visitors see, from
/// <c>docs/releases/unreleased/*-visible-changes.json</c> (019 FR-012a, contracts/snapshot.md §3).
/// The upgrade gate reads the same files; this side only lists them in the notes.
/// </summary>
internal static class VisibleChanges
{
    public const string Pattern = "*-visible-changes.json";

    internal sealed record Entry(int Issue, string Reason, IReadOnlyList<string> Pages);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <exception cref="ReleaseRuleException">A file is malformed; the message names it.</exception>
    public static IReadOnlyList<Entry> Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<Entry>();
        foreach (var path in Directory.EnumerateFiles(directory, Pattern).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            Shape? shape;
            try
            {
                shape = JsonSerializer.Deserialize<Shape>(File.ReadAllText(path), Json);
            }
            catch (JsonException ex)
            {
                throw new ReleaseRuleException($"{name} is not valid JSON: {ex.Message}");
            }

            if (shape is null || string.IsNullOrWhiteSpace(shape.Reason) || shape.Pages is not { Count: > 0 }
                || shape.Pages.Any(p => string.IsNullOrWhiteSpace(p.Host) || string.IsNullOrWhiteSpace(p.Path)))
            {
                throw new ReleaseRuleException($"{name} needs an issue, a reason and at least one page with a host and a path (contracts/snapshot.md §3).");
            }

            entries.Add(new Entry(shape.Issue, shape.Reason, [.. shape.Pages.Select(p => $"{p.Host}{p.Path}")]));
        }

        return entries;
    }

    private sealed class Shape
    {
        public int Issue { get; set; }

        public string? Reason { get; set; }

        public List<PageShape>? Pages { get; set; }
    }

    private sealed class PageShape
    {
        public string? Host { get; set; }

        public string? Path { get; set; }
    }
}
