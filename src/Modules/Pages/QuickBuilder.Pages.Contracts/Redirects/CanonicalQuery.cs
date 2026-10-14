using System.Text;

namespace QuickBuilder.Pages.Contracts.Redirects;

/// <summary>
/// One spelling for every equivalent query string (017 FR-017a, research SE-09).
/// </summary>
/// <remarks>
/// Pairs are decoded (<c>+</c> is a space, as browsers send forms), sorted by name and then by
/// value with ordinal comparison, so names keep their letter case, and re-encoded with every
/// character outside RFC 3986's unreserved set percent-encoded. A name without <c>=</c> has an
/// empty value. So <c>?b=2&amp;a=1</c> and <c>?a=1&amp;b=2</c> are one source, and a request
/// matches a redirect whatever order its parameters come in.
/// </remarks>
public static class CanonicalQuery
{
    public const int MaxLength = 512;

    /// <summary>The canonical form of a raw query, with or without its leading <c>?</c>; empty for none.</summary>
    public static string FromRaw(string? rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery))
        {
            return string.Empty;
        }

        var query = rawQuery[0] == '?' ? rawQuery[1..] : rawQuery;
        var pairs = new List<KeyValuePair<string, string?>>();
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var name = Decode(equals < 0 ? part : part[..equals]);
            var value = equals < 0 ? string.Empty : Decode(part[(equals + 1)..]);
            if (name.Length > 0)
            {
                pairs.Add(new(name, value));
            }
        }

        return FromPairs(pairs);
    }

    /// <summary>The canonical form of decoded pairs, as ASP.NET Core gives a request's query.</summary>
    public static string FromPairs(IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        var builder = new StringBuilder();
        foreach (var (name, value) in pairs
            .Where(p => !string.IsNullOrEmpty(p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value ?? string.Empty, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
        }

        return builder.ToString();
    }

    private static string Decode(string value)
    {
        var spaced = value.Replace('+', ' ');
        try
        {
            return Uri.UnescapeDataString(spaced);
        }
        catch (UriFormatException)
        {
            return spaced;
        }
    }
}
