using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>One published page as visitors saw it when the snapshot was taken.</summary>
internal sealed record ExpectedPage(string Host, string Path, int Status, string Fingerprint, string Text)
{
    /// <summary>The first 400 characters of the visible text, for diffs.</summary>
    public const int TextLength = 400;

    [JsonIgnore]
    public string Address => $"{Host}{Path}";
}

/// <summary>A member the content journey created, with the password it chose.</summary>
internal sealed record ExpectedMember(string Email, string Password, string Role);

/// <summary>A draft that must still open through the admin API after an upgrade.</summary>
internal sealed record ExpectedDraft(string Kind, string Endpoint);

/// <summary>
/// What must hold after any later release upgrades a snapshot (019 contracts/snapshot.md §2).
/// </summary>
/// <remarks>Pure and Docker-free; the unit tests compile this file in.</remarks>
internal sealed record Expectations
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Tables whose row counts move for reasons unrelated to content, with why. An entry is
    /// <c>schema.table</c>, or <c>*.table</c> for a table every module has (snapshot.md §3).
    /// </summary>
    public static IReadOnlyDictionary<string, string> VolatileTables { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["*.__EFMigrationsHistory"] = "grows with every migration by design",
        ["*.outbox_messages"] = "delivered events are pruned by retention",
        ["*.processed_events"] = "pruned with the outbox",
        ["identity.sessions"] = "sign-ins come and go; the gate signs in itself",
        ["identity.sign_in_throttles"] = "expire",
        ["identity.known_devices"] = "the gate signs in from new devices",
        ["identity.set_password_links"] = "expire and are consumed",
        ["forms.rate_windows"] = "expire within the hour",
        ["forms.spam_counters"] = "counters, not content",
        ["media.storage_checks"] = "the storage health check records its probes, at every start",
        ["pages.redirect_usage"] = "hit counters, written as visitors (and the gate) follow redirects",
        ["platform.database_version"] = "changes on every upgrade by design",
    };

    public int Format { get; init; } = 1;

    public required string CreatedBy { get; init; }

    public required string CreatedFrom { get; init; }

    public required IReadOnlyList<ExpectedPage> Pages { get; init; }

    public required IReadOnlyDictionary<string, long> Records { get; init; }

    public required IReadOnlyList<ExpectedMember> Members { get; init; }

    public required IReadOnlyList<ExpectedDraft> Drafts { get; init; }

    public static bool IsVolatile(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var name = table[(table.IndexOf('.', StringComparison.Ordinal) + 1)..];
        return VolatileTables.ContainsKey(table) || VolatileTables.ContainsKey($"*.{name}");
    }

    /// <summary>Volatile entries that match none of <paramref name="tables"/>: the list has gone stale.</summary>
    public static IReadOnlyList<string> StaleVolatileEntries(IEnumerable<string> tables)
    {
        var all = tables.ToList();
        return [.. VolatileTables.Keys
            .Where(entry => entry.StartsWith("*.", StringComparison.Ordinal)
                ? !all.Any(t => t.EndsWith(entry[1..], StringComparison.Ordinal))
                : !all.Contains(entry))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Whether text shows <paramref name="today"/>: such a page would differ when the snapshot is
    /// restored on another day, so capture refuses it (snapshot.md §3, "time-dependent page").
    /// </summary>
    public static bool ShowsDate(string text, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(text);
        var culture = CultureInfo.InvariantCulture;
        string[] forms =
        [
            today.ToString("yyyy-MM-dd", culture),
            today.ToString("d MMMM yyyy", culture),
            today.ToString("MMMM d, yyyy", culture),
            today.ToString("dd/MM/yyyy", culture),
            today.ToString("MM/dd/yyyy", culture),
        ];
        return forms.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Expectations FromJson(string json) =>
        JsonSerializer.Deserialize<Expectations>(json, Json)
        ?? throw new InvalidOperationException("The expectations file is empty.");
}
