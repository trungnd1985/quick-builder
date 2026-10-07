using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Media.Contracts;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Tells images from documents for the conversions that need it (research CS-12): file to
/// image, and narrowing what a file field accepts.
/// </summary>
/// <remarks>
/// Asks Media once per preview or save, through its read-only contract, for every media id
/// the affected values hold. An id Media does not return no longer exists, and its value is
/// kept as missing. Reading outside the Cms transaction is safe: a replace never changes a
/// file's kind (013 FR-017), and a file deleted in between only becomes missing.
/// </remarks>
internal sealed class MediaKinds(IPublicMediaReader media)
{
    public async Task<Func<Guid, MediaKind>> LookupAsync(Guid siteId, IEnumerable<JsonNode?> values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        var ids = values
            .Where(v => v is JsonObject o && o.ContainsKey("mediaId"))
            .Select(v => FieldConversion.MediaIdOf(v!))
            .ToHashSet();

        if (ids.Count == 0)
        {
            return static _ => MediaKind.Missing;
        }

        var files = await media.GetAsync(siteId, ids, cancellationToken).ConfigureAwait(false);
        return id => files.TryGetValue(id, out var file)
            ? file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? MediaKind.Image : MediaKind.Document
            : MediaKind.Missing;
    }
}
