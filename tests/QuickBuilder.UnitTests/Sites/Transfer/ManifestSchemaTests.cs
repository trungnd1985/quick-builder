using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using QuickBuilder.Pages.Contracts.Schema;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Features.Transfer.Engine;
using QuickBuilder.UnitTests.Pages;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// The manifest an export writes matches the published schema, and the published example's
/// manifest describes its files (docs/contracts/site-archive.md, 020 T087).
/// </summary>
public sealed class ManifestSchemaTests
{
    private static string Directory => Path.Combine(ContractFiles.Root, "docs", "contracts", "site-archive");

    private static string Example => Path.Combine(Directory, "examples", "minimal");

    [Fact]
    public async Task The_manifest_the_writer_writes_matches_the_schema()
    {
        var bytes = await WriteAsync().ConfigureAwait(true);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        await using var manifest = await zip.GetEntry("manifest.json")!.OpenAsync().ConfigureAwait(true);
        using var document = await JsonDocument.ParseAsync(manifest).ConfigureAwait(true);

        Assert.Null(Schema().Validate(document.RootElement));
    }

    [Fact]
    public void The_golden_archive_s_manifest_matches_the_schema()
    {
        var golden = Path.Combine(ContractFiles.Root, "tests", "fixtures", "archives", "0.2.qbsite");
        using var zip = ZipFile.OpenRead(golden);
        using var manifest = zip.GetEntry("manifest.json")!.Open();
        using var document = JsonDocument.Parse(manifest);

        Assert.Null(Schema().Validate(document.RootElement));
    }

    [Fact]
    public void The_example_manifest_matches_the_schema_and_lists_every_file_with_its_hash()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Example, "manifest.json")));
        Assert.Null(Schema().Validate(document.RootElement));

        var listed = document.RootElement.GetProperty("entries").EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => (e.GetProperty("length").GetInt64(), e.GetProperty("sha256").GetString()!));
        var files = System.IO.Directory.GetFiles(Example, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Example, f).Replace('\\', '/'))
            .Where(f => f != "manifest.json")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(files, listed.Keys.Order(StringComparer.Ordinal));
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Example, file));
            Assert.Equal(listed[file], (bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.True(ArchiveFormat.IsAllowedName(file), file);
        }
    }

    [Theory]
    [InlineData("""{"format":2}""", "/format")]
    [InlineData("""{"release":"one"}""", "/release")]
    [InlineData("""{"entries":[{"name":"../evil","length":1,"sha256":"00"}]}""", "/entries/0/name")]
    [InlineData("""{"extra":true}""", "/extra")]
    public void A_manifest_that_breaks_the_format_fails_where_it_breaks(string change, string location)
    {
        using var example = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Example, "manifest.json")));
        var merged = System.Text.Json.Nodes.JsonNode.Parse(example.RootElement.GetRawText())!.AsObject();
        foreach (var (key, value) in System.Text.Json.Nodes.JsonNode.Parse(change)!.AsObject().ToList())
        {
            merged[key] = value?.DeepClone();
        }

        using var broken = JsonDocument.Parse(merged.ToJsonString());
        Assert.Equal(location, Schema().Validate(broken.RootElement)?.Location);
    }

    private static SchemaSubsetValidator Schema()
    {
        using var schema = File.OpenRead(Path.Combine(Directory, "manifest.v1.schema.json"));
        return SchemaSubsetValidator.Load(schema);
    }

    private static async Task<byte[]> WriteAsync()
    {
        using var output = new MemoryStream();
        await using (var writer = await ZipArchiveWriter.CreateAsync(output, default).ConfigureAwait(false))
        {
            await writer.WriteLinesAsync("pages", "tree", Rows(), default).ConfigureAwait(false);
            await writer.WriteMediaAsync(Guid.CreateVersion7(), new MemoryStream([1, 2, 3]), default).ConfigureAwait(false);
            await writer.WriteAuthorsAsync(Authors(), default).ConfigureAwait(false);
            await writer.CompleteAsync(entries => new ArchiveManifest
            {
                Format = 1,
                Release = "0.2.0-dev",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Source = new ArchiveSource(Guid.CreateVersion7(), "Acme", Enabled: true),
                Parts = [new ArchivePartInfo("pages", 1, new Dictionary<string, int> { ["page-schema"] = 1 })],
                Counts = new Dictionary<string, long> { ["page"] = 1, ["media_bytes"] = 3 },
                Problems = [new ArchiveProblem("transfer.media_missing", "media_file", Guid.CreateVersion7())],
                Entries = entries,
            }, default).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static async IAsyncEnumerable<object> Rows()
    {
        yield return new { id = Guid.CreateVersion7(), title = "Home" };
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<ArchiveAuthor> Authors()
    {
        yield return new ArchiveAuthor(Guid.CreateVersion7(), "Linh Pham");
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
