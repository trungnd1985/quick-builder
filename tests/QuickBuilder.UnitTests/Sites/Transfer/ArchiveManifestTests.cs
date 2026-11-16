using System.IO.Compression;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Features.Transfer.Engine;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// The archive container: entry names, the manifest's entry list, and verification while
/// reading (020 contracts/site-archive.md §1–§2, research ST-02).
/// </summary>
public sealed class ArchiveManifestTests
{
    private static readonly Guid FileId = Guid.Parse("0b9e5a1c2d3e4f5061728394a5b6c7d8");

    private sealed record Row(Guid Id, string Title);

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("authors.jsonl")]
    [InlineData("parts/pages/page-drafts.jsonl")]
    [InlineData("media/0b9e5a1c2d3e4f5061728394a5b6c7d8")]
    public void The_four_entry_shapes_are_allowed(string name) =>
        Assert.True(ArchiveFormat.IsAllowedName(name));

    [Theory]
    [InlineData("../evil")]
    [InlineData("parts/../manifest.json")]
    [InlineData("/manifest.json")]
    [InlineData("parts\\pages\\pages.jsonl")]
    [InlineData("C:/x")]
    [InlineData("parts/Pages/pages.jsonl")]
    [InlineData("parts/pages/pages.json")]
    [InlineData("parts/pages/")]
    [InlineData("media/0B9E5A1C2D3E4F5061728394A5B6C7D8")]
    [InlineData("media/0b9e5a1c-2d3e-4f50-6172-8394a5b6c7d8")]
    [InlineData("parts/pages/deeper/pages.jsonl")]
    [InlineData("manifest.json\n")]
    [InlineData("readme.txt")]
    public void Every_other_name_is_refused(string name) =>
        Assert.False(ArchiveFormat.IsAllowedName(name));

    [Fact]
    public async Task An_archive_written_through_a_pipe_reads_back_with_every_entry_verified()
    {
        var bytes = await WriteArchiveAsync(rows: 1000);

        await using var reader = await OpenAsync(bytes);

        Assert.Equal(3, reader.Manifest.Entries.Count);
        Assert.True((await reader.VerifyAllAsync(null, default)).IsSuccess);

        var rows = await reader.ReadLinesAsync<Row>("pages", "pages", default).ToListAsync();
        Assert.Equal(1000, rows.Count);
        Assert.Equal("Page 999", rows[^1].Title);

        await using var media = await reader.OpenMediaAsync(FileId, default);
        using var copy = new MemoryStream();
        await media!.CopyToAsync(copy);
        Assert.Equal(MediaBytes, copy.ToArray());
        Assert.Empty(await reader.ReadAuthorsAsync(default).ToListAsync());
    }

    [Fact]
    public async Task Media_can_be_read_while_a_lines_entry_is_open()
    {
        await using var reader = await OpenAsync(await WriteArchiveAsync(rows: 5));

        await foreach (var _ in reader.ReadLinesAsync<Row>("pages", "pages", default))
        {
            await using var media = await reader.OpenMediaAsync(FileId, default);
            await media!.CopyToAsync(Stream.Null);
        }
    }

    [Fact]
    public async Task An_absent_entry_reads_as_empty_and_absent_media_as_null()
    {
        await using var reader = await OpenAsync(await WriteArchiveAsync(rows: 1));

        Assert.False(reader.HasLines("cms", "items"));
        Assert.Empty(await reader.ReadLinesAsync<Row>("cms", "items", default).ToListAsync());
        Assert.Null(await reader.OpenMediaAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_truncated_archive_is_unreadable()
    {
        var bytes = await WriteArchiveAsync(rows: 100);

        var opened = await TryOpenAsync(bytes[..(bytes.Length / 2)]);

        Assert.Equal("archive.unreadable", opened.Error!.Code);
    }

    [Fact]
    public async Task A_file_that_is_not_a_zip_is_unreadable()
    {
        var opened = await TryOpenAsync(Encoding.UTF8.GetBytes("not an archive"));

        Assert.Equal("archive.unreadable", opened.Error!.Code);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("parts/pages/")]
    [InlineData("readme.txt")]
    public async Task An_entry_outside_the_allowed_shapes_is_refused_before_anything_is_read(string name)
    {
        var bytes = await RewriteAsync(await WriteArchiveAsync(rows: 1), zip => zip.CreateEntry(name));

        Assert.Equal("archive.unsafe_path", (await TryOpenAsync(bytes)).Error!.Code);
    }

    [Fact]
    public async Task An_entry_missing_from_the_manifest_is_an_integrity_failure()
    {
        var bytes = await RewriteAsync(await WriteArchiveAsync(rows: 1), zip => zip.CreateEntry("parts/pages/extra.jsonl"));

        Assert.Equal("archive.integrity", (await TryOpenAsync(bytes)).Error!.Code);
    }

    [Fact]
    public async Task An_entry_the_manifest_lists_but_the_archive_lacks_is_an_integrity_failure()
    {
        var bytes = await RewriteAsync(await WriteArchiveAsync(rows: 1), zip => zip.GetEntry("parts/pages/pages.jsonl")!.Delete());

        Assert.Equal("archive.integrity", (await TryOpenAsync(bytes)).Error!.Code);
    }

    [Fact]
    public async Task A_changed_byte_of_the_same_length_is_found_while_verifying()
    {
        var bytes = await RewriteAsync(await WriteArchiveAsync(rows: 3), zip =>
        {
            var entry = zip.GetEntry("parts/pages/pages.jsonl")!;
            byte[] content;
            using (var read = entry.Open())
            using (var copy = new MemoryStream())
            {
                read.CopyTo(copy);
                content = copy.ToArray();
            }

            content[10] ^= 0x01;
            entry.Delete();
            using var write = zip.CreateEntry("parts/pages/pages.jsonl").Open();
            write.Write(content);
        });

        await using var reader = await OpenAsync(bytes);

        Assert.Equal("archive.integrity", (await reader.VerifyAllAsync(null, default)).Error!.Code);
        await Assert.ThrowsAsync<ArchiveIntegrityException>(
            async () => await reader.ReadLinesAsync<Row>("pages", "pages", default).ToListAsync());
    }

    [Fact]
    public async Task A_manifest_that_is_not_valid_json_is_unreadable()
    {
        var bytes = await RewriteAsync(await WriteArchiveAsync(rows: 1), zip =>
        {
            zip.GetEntry("manifest.json")!.Delete();
            using var write = zip.CreateEntry("manifest.json").Open();
            write.Write("{ not json"u8);
        });

        Assert.Equal("archive.unreadable", (await TryOpenAsync(bytes)).Error!.Code);
    }

    [Fact]
    public async Task A_line_that_does_not_fit_its_row_type_names_the_entry_and_line()
    {
        var bytes = await WriteArchiveAsync(rows: 2, extraLine: "{\"id\":\"not-a-guid\",\"title\":1}");
        await using var reader = await OpenAsync(bytes);

        var error = await Assert.ThrowsAsync<ArchiveFormatException>(
            async () => await reader.ReadLinesAsync<Row>("pages", "pages", default).ToListAsync());

        Assert.Contains("Line 3 of 'parts/pages/pages.jsonl'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_writer_refuses_names_outside_the_grammar_and_a_second_write_of_one_entry()
    {
        await using var output = new MemoryStream();
        await using var writer = await ZipArchiveWriter.CreateAsync(output, default);

        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteLinesAsync("Pages", "x", Rows(1), default));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteLinesAsync("pages", "../x", Rows(1), default));
        await writer.WriteLinesAsync("pages", "pages", Rows(1), default);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteLinesAsync("pages", "pages", Rows(1), default));
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static readonly byte[] MediaBytes = [.. Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7))];

    private static async IAsyncEnumerable<Row> Rows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new Row(Guid.CreateVersion7(), $"Page {i}");
        }

        await Task.CompletedTask;
    }

    /// <summary>Writes an archive through a pipe, as an export does, and returns its bytes.</summary>
    private static async Task<byte[]> WriteArchiveAsync(int rows, string? extraLine = null)
    {
        var pipe = new Pipe();
        var collected = new MemoryStream();
        var consumer = pipe.Reader.AsStream().CopyToAsync(collected);

        await using (var output = pipe.Writer.AsStream())
        {
            await using var writer = await ZipArchiveWriter.CreateAsync(output, default);
            await writer.WriteLinesAsync("pages", "pages", RowsWithExtra(rows, extraLine), default);
            await writer.WriteMediaAsync(FileId, new MemoryStream(MediaBytes), default);
            await writer.WriteAuthorsAsync(AsyncEnumerable.Empty<ArchiveAuthor>(), default);
            await writer.CompleteAsync(entries => new ArchiveManifest
            {
                Format = 1,
                Release = "1.0.0",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                Source = new ArchiveSource(Guid.CreateVersion7(), "Acme", Enabled: true),
                Parts = [new ArchivePartInfo("pages", 1, new Dictionary<string, int> { ["page-schema"] = 1 })],
                Counts = new Dictionary<string, long>(),
                Problems = [],
                Entries = entries,
            }, default);
        }

        await consumer;
        return collected.ToArray();
    }

    private static async IAsyncEnumerable<JsonElement> RowsWithExtra(int count, string? extraLine)
    {
        await foreach (var row in Rows(count))
        {
            yield return JsonSerializer.SerializeToElement(row, ArchiveJson.Options);
        }

        if (extraLine is not null)
        {
            yield return JsonElement.Parse(extraLine);
        }
    }

    private static async Task<byte[]> RewriteAsync(byte[] archive, Action<ZipArchive> change)
    {
        var copy = new MemoryStream();
        await copy.WriteAsync(archive);
        using (var zip = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
        {
            change(zip);
        }

        return copy.ToArray();
    }

    private static Task<QuickBuilder.Shared.Kernel.Results.Result<ZipArchiveReader>> TryOpenAsync(byte[] bytes) =>
        ZipArchiveReader.OpenAsync(_ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)), default);

    private static async Task<ZipArchiveReader> OpenAsync(byte[] bytes)
    {
        var opened = await TryOpenAsync(bytes);
        Assert.True(opened.IsSuccess, opened.Error?.ToString());
        return opened.Value;
    }
}
