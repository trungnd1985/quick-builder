using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// The filters every list of submissions takes (018 FR-026, FR-027; forms-api.yaml): the box,
/// the form, a date range, the read state and search words.
/// </summary>
internal sealed record SubmissionFilter(
    bool Spam,
    string? FormId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool? Read,
    string? Search)
{
    /// <summary>The longest search accepted (forms-api.yaml).</summary>
    public const int MaxSearchLength = 200;

    /// <summary>The filters as recorded for an export, with only whether a search was used (FR-030).</summary>
    public string ToLogJson() => JsonSerializer.Serialize(new
    {
        formId = FormId,
        from = From,
        to = To,
        read = Read,
        searchUsed = !string.IsNullOrWhiteSpace(Search),
    });
}

/// <summary>Reading submissions for the admin (018 research FS-15).</summary>
internal sealed class SubmissionReads(FormsDbContext database)
{
    public const int PageSize = 50;

    /// <summary>The site's submissions matching <paramref name="filter"/>, not ordered.</summary>
    public IQueryable<Submission> Matching(Guid siteId, SubmissionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = database.ForSite<Submission>(siteId).Where(s => s.IsSpam == filter.Spam);

        if (filter.FormId is { Length: > 0 } formId)
        {
            query = query.Where(s => s.FormId == formId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(s => s.ReceivedAtUtc >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(s => s.ReceivedAtUtc <= to);
        }

        if (filter.Read is { } read)
        {
            query = query.Where(s => s.IsRead == read);
        }

        // FR-027: every word, anywhere in any value, ignoring case and accents, literally.
        foreach (var word in Words(filter.Search))
        {
            var pattern = SearchText.LikePattern(word);
            query = query.Where(s => EF.Functions.Like(s.SearchText, pattern, "\\"));
        }

        return query;
    }

    /// <summary>Newest first, after the keyset cursor when there is one.</summary>
    public static IQueryable<Submission> Page(IQueryable<Submission> query, (DateTimeOffset ReceivedAt, Guid Id)? after)
    {
        if (after is { } cursor)
        {
            query = query.Where(s => EF.Functions.LessThan(ValueTuple.Create(s.ReceivedAtUtc, s.Id), ValueTuple.Create(cursor.ReceivedAt, cursor.Id)));
        }

        return query.OrderByDescending(s => s.ReceivedAtUtc).ThenByDescending(s => s.Id);
    }

    /// <summary>The site's form registry rows by form id, with whether each is published now.</summary>
    public async Task<Dictionary<string, (FormRecord Form, bool Published)>> FormsAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var forms = await database.ForSite<FormRecord>(siteId).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var published = await database.ForSite<FormPlacement>(siteId)
            .AsNoTracking()
            .Select(p => p.FormId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return forms.ToDictionary(f => f.FormId, f => (f, published.Contains(f.FormId)), StringComparer.Ordinal);
    }

    public static IEnumerable<string> Words(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : SearchText.Normalise(search).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>An opaque keyset cursor: the last row's time and id.</summary>
    public static string Cursor(Submission last)
    {
        ArgumentNullException.ThrowIfNull(last);
        var text = $"{last.ReceivedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{last.Id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>A cursor back to its time and id; null when it is not one this module made.</summary>
    public static (DateTimeOffset ReceivedAt, Guid Id)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 100)
        {
            return null;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split(':');
            return parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && Guid.TryParseExact(parts[1], "N", out var id)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The stored values of a submission (data-model §1).</summary>
    public static IReadOnlyList<StoredValue> Values(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        using var document = JsonDocument.Parse(submission.Values);
        return [.. document.RootElement.EnumerateArray().Select(v => new StoredValue(
            v.GetProperty("fieldId").GetString()!,
            v.GetProperty("label").GetString()!,
            v.GetProperty("type").GetString()!,
            v.GetProperty("value").ValueKind == JsonValueKind.Array
                ? [.. v.GetProperty("value").EnumerateArray().Select(e => e.GetString()!)]
                : v.GetProperty("value").GetString() is { Length: > 0 } one ? [one] : []))];
    }

    /// <summary>The first values, joined, for a list row (forms-api.yaml, <c>preview</c>).</summary>
    public static string Preview(IReadOnlyList<StoredValue> values)
    {
        var text = string.Join(" · ", values.Where(v => v.Values.Count > 0).Select(v => string.Join(", ", v.Values).Replace('\n', ' ')));
        return text.Length <= 160 ? text : string.Concat(text.AsSpan(0, 159), "…");
    }
}

/// <summary>One stored value: the field, as it was when received, and what was entered.</summary>
internal sealed record StoredValue(string FieldId, string Label, string Type, IReadOnlyList<string> Values);
