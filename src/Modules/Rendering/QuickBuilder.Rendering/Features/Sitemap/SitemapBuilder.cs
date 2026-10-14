using System.Globalization;
using System.Text;
using System.Xml;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Seo;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.Sitemap;

/// <summary>
/// Builds a site's sitemap files from published data only (017 FR-022 to FR-024, research
/// SE-14; public-seo.md §5).
/// </summary>
/// <remarks>
/// Every indexable address once, as its canonical address on the site's base URL: live pages
/// by path, then each listable template's published items by id, an order stable for one
/// generation. Up to 50,000 addresses make one <c>urlset</c>; more make a
/// <c>sitemapindex</c> of numbered files of at most 50,000. A site that discourages search
/// engines gets an empty <c>urlset</c> (FR-027).
/// </remarks>
internal sealed class SitemapBuilder(ISitemapReader pages, IPublishedItemReader items)
{
    /// <summary>The sitemap protocol's limit of addresses per file.</summary>
    public const int MaxPerFile = 50_000;

    /// <summary>How many items one read brings back.</summary>
    internal const int ItemBatch = 10_000;

    public const string IndexFile = "sitemap.xml";

    public async Task<SitemapSet> BuildAsync(SiteDto site, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var entries = new List<SitemapEntry>();
        if (!site.Search.Discouraged)
        {
            await CollectAsync(site, entries, cancellationToken).ConfigureAwait(false);
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (entries.Count <= MaxPerFile)
        {
            files[IndexFile] = UrlSet(entries);
            return new SitemapSet(files);
        }

        var chunks = entries.Chunk(MaxPerFile).ToList();
        var index = new List<(string Loc, DateTimeOffset LastModified)>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var name = $"sitemap-{i + 1}.xml";
            files[name] = UrlSet(chunks[i]);
            index.Add((SiteAddress.Absolute(site.BaseUrl, "/" + name), chunks[i].Max(e => e.LastModified)));
        }

        files[IndexFile] = Index(index);
        return new SitemapSet(files);
    }

    private async Task CollectAsync(SiteDto site, List<SitemapEntry> entries, CancellationToken cancellationToken)
    {
        var live = await pages.GetPagesAsync(site.Id, cancellationToken).ConfigureAwait(false);

        foreach (var page in live.Pages.Where(p => p.Indexable))
        {
            entries.Add(new SitemapEntry(SiteAddress.Absolute(site.BaseUrl, page.Path), page.LiveSinceUtc));
        }

        foreach (var template in live.Templates.Where(t => t.Indexable))
        {
            Guid? after = null;
            while (true)
            {
                var batch = await items.ListAddressesAsync(site.Id, template.CollectionId, template.HideFieldId, after, ItemBatch, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var item in batch.Where(i => !i.Hidden))
                {
                    var path = template.Pattern.Replace("{slug}", Uri.EscapeDataString(item.Slug), StringComparison.Ordinal);
                    var lastModified = item.LastPublishedAtUtc > template.LiveSinceUtc ? item.LastPublishedAtUtc : template.LiveSinceUtc;
                    entries.Add(new SitemapEntry(SiteAddress.Absolute(site.BaseUrl, path), lastModified));
                }

                if (batch.Count < ItemBatch)
                {
                    break;
                }

                after = batch[^1].ItemId;
            }
        }
    }

    private static byte[] UrlSet(IEnumerable<SitemapEntry> entries) => Write(writer =>
    {
        writer.WriteStartElement("urlset", Namespace);
        foreach (var entry in entries)
        {
            writer.WriteStartElement("url", Namespace);
            writer.WriteElementString("loc", Namespace, entry.Loc);
            writer.WriteElementString("lastmod", Namespace, W3C(entry.LastModified));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    private static byte[] Index(IEnumerable<(string Loc, DateTimeOffset LastModified)> files) => Write(writer =>
    {
        writer.WriteStartElement("sitemapindex", Namespace);
        foreach (var (loc, lastModified) in files)
        {
            writer.WriteStartElement("sitemap", Namespace);
            writer.WriteElementString("loc", Namespace, loc);
            writer.WriteElementString("lastmod", Namespace, W3C(lastModified));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    /// <summary>The sitemap protocol's namespace.</summary>
    public const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private static readonly XmlWriterSettings Settings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
    };

    private static byte[] Write(Action<XmlWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, Settings))
        {
            writer.WriteStartDocument();
            body(writer);
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>W3C date-time in UTC, to the second (sitemaps.org).</summary>
    private static string W3C(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private sealed record SitemapEntry(string Loc, DateTimeOffset LastModified);
}

/// <summary>A site's sitemap files by name: <c>sitemap.xml</c>, and <c>sitemap-n.xml</c> beyond 50,000 addresses.</summary>
internal sealed record SitemapSet(IReadOnlyDictionary<string, byte[]> Files)
{
    public long Size => Files.Values.Sum(f => (long)f.Length);
}
