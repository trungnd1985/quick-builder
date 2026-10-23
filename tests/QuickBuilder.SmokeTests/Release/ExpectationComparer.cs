namespace QuickBuilder.SmokeTests.Release;

/// <summary>The outcome of comparing a snapshot's expectations with the upgraded database.</summary>
/// <param name="Failures">One line per broken promise, naming the page or table.</param>
/// <param name="Allowed">Differences accepted by the visible-change allow-list, still shown.</param>
internal sealed record ComparisonResult(IReadOnlyList<string> Failures, IReadOnlyList<string> Allowed)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// Compares and merges expectations (019 contracts/snapshot.md §3–§4, FR-012, FR-014).
/// </summary>
/// <remarks>Pure and Docker-free; the unit tests compile this file in.</remarks>
internal static class ExpectationComparer
{
    /// <summary>
    /// Every page before must exist after with the same status and visible form; every counted
    /// table must keep at least as many rows (a migration may derive rows, never lose any). A
    /// page whose visible form changed passes only when <paramref name="allowList"/> names it
    /// (FR-012a), and every allow-list entry must excuse at least one page.
    /// </summary>
    public static ComparisonResult Compare(Expectations before, Expectations after, VisibleChangeAllowList? allowList = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        allowList ??= VisibleChangeAllowList.Empty;

        var failures = new List<string>();
        var allowed = new List<string>();
        var used = new HashSet<VisibleChangeEntry>();
        var pagesAfter = after.Pages.ToDictionary(p => p.Address, StringComparer.Ordinal);

        foreach (var page in before.Pages)
        {
            if (!pagesAfter.TryGetValue(page.Address, out var now))
            {
                failures.Add($"{page.Address}: missing after the upgrade.");
                continue;
            }

            if (now.Status != page.Status)
            {
                failures.Add($"{page.Address}: status {page.Status} became {now.Status}.");
                continue;
            }

            if (now.Fingerprint != page.Fingerprint)
            {
                if (allowList.Excusing(page) is { } entry)
                {
                    used.Add(entry);
                    allowed.Add($"{page.Address}: changed as #{entry.Issue} says ({entry.Reason}).\n{Diff(page.Text, now.Text)}");
                }
                else
                {
                    failures.Add($"{page.Address}: what visitors see changed.\n{Diff(page.Text, now.Text)}");
                }
            }
        }

        // Stale: the snapshot has pages the entry names, and none of them changed. An entry whose
        // pages this snapshot does not hold (an older upgrade source) simply does not apply.
        failures.AddRange(allowList.Entries
            .Where(e => !used.Contains(e) && before.Pages.Any(p => allowList.Excusing(p) == e))
            .Select(e => $"{e.File}: unused allow-list entry #{e.Issue}: no page it names changed. Remove it."));

        foreach (var (table, count) in before.Records.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (!after.Records.TryGetValue(table, out var nowCount))
            {
                failures.Add($"{table}: missing after the upgrade.");
            }
            else if (nowCount < count)
            {
                failures.Add($"{table}: {count} rows became {nowCount}.");
            }
        }

        return new ComparisonResult(failures, allowed);
    }

    /// <summary>
    /// The expectations of snapshot(N): everything snapshot(N-1) promised plus N's own pages and
    /// members. Records are N's, which already count the whole database (FR-014).
    /// </summary>
    public static Expectations Merge(Expectations older, Expectations newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        return newer with
        {
            Pages = Union(older.Pages, newer.Pages, p => p.Address),
            Members = Union(older.Members, newer.Members, m => m.Email),
            Drafts = Union(older.Drafts, newer.Drafts, d => d.Endpoint),
        };
    }

    /// <summary>A two-line diff of the visible text, trimmed to <see cref="ExpectedPage.TextLength"/>.</summary>
    public static string Diff(string before, string after) =>
        $"  - {Trim(before)}\n  + {Trim(after)}";

    private static string Trim(string text) =>
        text.Length <= ExpectedPage.TextLength ? text : text[..ExpectedPage.TextLength] + "…";

    private static List<T> Union<T>(IEnumerable<T> older, IEnumerable<T> newer, Func<T, string> key)
    {
        var merged = older.ToDictionary(key, StringComparer.Ordinal);
        foreach (var item in newer)
        {
            merged[key(item)] = item;
        }

        return [.. merged.Values];
    }
}
