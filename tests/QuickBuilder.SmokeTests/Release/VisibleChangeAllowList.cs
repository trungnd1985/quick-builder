using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>A page an allow-list entry names: a host name or <c>*</c>, and a path that may end in <c>/*</c>.</summary>
internal sealed record VisibleChangePage(string Host, string Path);

/// <summary>One reviewed, deliberate change to what visitors see, and the pages it touches.</summary>
internal sealed record VisibleChangeEntry(int Issue, string Reason, IReadOnlyList<VisibleChangePage> Pages, string File);

/// <summary>
/// The pages a release may change for visitors on purpose (019 FR-012a, contracts/snapshot.md §3).
/// </summary>
/// <remarks>
/// Read from <c>docs/releases/unreleased/*-visible-changes.json</c>, reviewed in the pull request
/// that makes the change. Status codes, records and sign-in are never excused, and an entry that
/// excuses nothing fails the gate, so a stale entry cannot hide a later regression.
/// <para>Pure and Docker-free; the unit tests compile this file in.</para>
/// </remarks>
internal sealed class VisibleChangeAllowList(IReadOnlyList<VisibleChangeEntry> entries)
{
    public const string FilePattern = "*-visible-changes.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static VisibleChangeAllowList Empty { get; } = new([]);

    public IReadOnlyList<VisibleChangeEntry> Entries { get; } = entries;

    /// <summary>Every allow-list file in <paramref name="directory"/>; none when it does not exist.</summary>
    /// <exception cref="InvalidDataException">A file is malformed; the message names it.</exception>
    public static VisibleChangeAllowList Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Empty;
        }

        var entries = new List<VisibleChangeEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, FilePattern).Order(StringComparer.Ordinal))
        {
            var name = System.IO.Path.GetFileName(path);
            FileShape? shape;
            try
            {
                shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), Json);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{name} is not valid JSON: {ex.Message}", ex);
            }

            if (shape is null
                || string.IsNullOrWhiteSpace(shape.Reason)
                || shape.Pages is not { Count: > 0 }
                || shape.Pages.Any(p => string.IsNullOrWhiteSpace(p.Host) || string.IsNullOrWhiteSpace(p.Path)))
            {
                throw new InvalidDataException($"{name} needs an issue, a reason and at least one page with a host and a path (contracts/snapshot.md §3).");
            }

            entries.Add(new VisibleChangeEntry(shape.Issue, shape.Reason, [.. shape.Pages.Select(p => new VisibleChangePage(p.Host!, p.Path!))], name));
        }

        return new VisibleChangeAllowList(entries);
    }

    public bool Allows(ExpectedPage page) => Excusing(page) is not null;

    /// <summary>The entry that excuses a change to <paramref name="page"/>, if any.</summary>
    public VisibleChangeEntry? Excusing(ExpectedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Entries.FirstOrDefault(e => e.Pages.Any(p => Matches(p, page)));
    }

    private static bool Matches(VisibleChangePage rule, ExpectedPage page) =>
        (rule.Host == "*" || string.Equals(rule.Host, page.Host, StringComparison.OrdinalIgnoreCase))
        && (rule.Path.EndsWith("/*", StringComparison.Ordinal)
            ? page.Path.StartsWith(rule.Path[..^1], StringComparison.Ordinal)
            : string.Equals(rule.Path, page.Path, StringComparison.Ordinal));

    private sealed class FileShape
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
