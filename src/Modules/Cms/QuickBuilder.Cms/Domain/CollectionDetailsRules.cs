using System.Text.RegularExpressions;
using QuickBuilder.Cms.Contracts;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// The rules for a collection's own details: display name, address name and description
/// (FR-002, FR-007).
/// </summary>
internal static partial class CollectionDetailsRules
{
    /// <summary>Trims the name and description; null when they cannot be made valid.</summary>
    public static CollectionDetails Normalise(string? name, string? slug, string? description) =>
        new(name?.Trim() ?? string.Empty, slug?.Trim() ?? string.Empty, string.IsNullOrWhiteSpace(description) ? null : description.Trim());

    public static RuleFailure? Check(CollectionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (details.Name.Length is 0 or > CollectionSchemaLimits.MaxNameLength || details.Name.Any(char.IsControl))
        {
            return new RuleFailure("invalid_name", "/name", $"A name is 1 to {CollectionSchemaLimits.MaxNameLength} characters, without control characters.");
        }

        if (!KeyPattern().IsMatch(details.Slug))
        {
            return new RuleFailure("invalid_slug", "/slug", $"An address name is 1 to {CollectionSchemaLimits.MaxKeyLength} lower-case letters, digits and hyphens, starting with a letter.");
        }

        if (details.Description is { } text
            && (text.Length > CollectionSchemaLimits.MaxDescriptionLength || text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')))
        {
            return new RuleFailure("invalid_description", "/description", $"A description is at most {CollectionSchemaLimits.MaxDescriptionLength} characters.");
        }

        return null;
    }

    [GeneratedRegex(CollectionSchemaLimits.KeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
