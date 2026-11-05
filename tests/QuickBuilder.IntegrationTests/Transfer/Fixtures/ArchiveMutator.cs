using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.IntegrationTests.Transfer.Fixtures;

/// <summary>
/// Makes damaged, newer, unsafe and invalid archives from a valid one (020 T057, SC-003).
/// </summary>
/// <remarks>
/// <see cref="Rewrite"/> changes entries and then writes a manifest whose entry list, lengths and
/// hashes match again, so the archive passes the integrity check and reaches the check it is
/// meant to fail. The raw mutations (truncate, flip a byte) leave the manifest as it was.
/// </remarks>
internal static class ArchiveMutator
{
    public static byte[] Truncate(byte[] archive) => archive[..(archive.Length / 2)];

    /// <summary>Changes one byte of one entry's content without updating the manifest.</summary>
    public static byte[] FlipByteIn(byte[] archive, string entry) => Raw(archive, zip =>
    {
        var content = Read(zip, entry);
        content[content.Length / 2] ^= 0x20;
        Replace(zip, entry, content);
    });

    /// <summary>Adds an entry without listing it in the manifest.</summary>
    public static byte[] AddUnlisted(byte[] archive, string entry) => Raw(archive, zip => Write(zip, entry, "{}"u8.ToArray()));

    public static byte[] Remove(byte[] archive, string entry) => Raw(archive, zip => zip.GetEntry(entry)!.Delete());

    /// <summary>Applies <paramref name="change"/> to the entries, then rewrites the manifest to match them.</summary>
    public static byte[] Rewrite(byte[] archive, Action<ZipArchive, JsonObject> change) => Raw(archive, zip =>
    {
        var manifest = JsonNode.Parse(Read(zip, "manifest.json"))!.AsObject();
        change(zip, manifest);

        var entries = new JsonArray();
        foreach (var entry in zip.Entries.Where(e => e.FullName != "manifest.json").ToList())
        {
            var bytes = Read(zip, entry.FullName);
            entries.Add(new JsonObject
            {
                ["name"] = entry.FullName,
                ["length"] = bytes.LongLength,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            });
        }

        manifest["entries"] = entries;
        Replace(zip, "manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    });

    /// <summary>Changes every line of a <c>.jsonl</c> entry, keeping the manifest consistent.</summary>
    public static byte[] EditLines(byte[] archive, string entry, Func<JsonObject, JsonObject> change) => Rewrite(archive, (zip, _) =>
    {
        var lines = Encoding.UTF8.GetString(Read(zip, entry)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => change(JsonNode.Parse(l)!.AsObject()).ToJsonString());
        Replace(zip, entry, Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
    });

    /// <summary>Replaces one entry's content, keeping the manifest consistent.</summary>
    public static byte[] ReplaceEntry(byte[] archive, string entry, byte[] content) => Rewrite(archive, (zip, _) => Replace(zip, entry, content));

    public static byte[] SetManifest(byte[] archive, Action<JsonObject> change) => Rewrite(archive, (_, manifest) => change(manifest));

    /// <summary>The name of the first media entry.</summary>
    public static string FirstMedia(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        return zip.Entries.First(e => e.FullName.StartsWith("media/", StringComparison.Ordinal)).FullName;
    }

    private static byte[] Raw(byte[] archive, Action<ZipArchive> change)
    {
        var copy = new MemoryStream();
        copy.Write(archive);
        using (var zip = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
        {
            change(zip);
        }

        return copy.ToArray();
    }

    private static byte[] Read(ZipArchive zip, string entry)
    {
        using var stream = zip.GetEntry(entry)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Replace(ZipArchive zip, string entry, byte[] content)
    {
        zip.GetEntry(entry)?.Delete();
        Write(zip, entry, content);
    }

    private static void Write(ZipArchive zip, string entry, byte[] content)
    {
        using var stream = zip.CreateEntry(entry).Open();
        stream.Write(content);
    }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
}
