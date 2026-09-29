using System.Security.Cryptography;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// The editor's block stylesheet, embedded at build time and served to visitors (012 research
/// PR-10, public-rendering.md §5).
/// </summary>
/// <remarks>
/// One file serves the canvas and every published page. Its address carries a hash of its
/// bytes, so it can be cached for a year: an upgrade that changes it changes the address.
/// </remarks>
internal sealed class BlockStylesheet
{
    private BlockStylesheet(byte[] content)
    {
        Content = content;
        Hash = Convert.ToBase64String(SHA256.HashData(content))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_')[..12];
    }

    /// <summary>The one instance, loaded once from the assembly.</summary>
    public static BlockStylesheet Instance { get; } = Load();

    public byte[] Content { get; }

    /// <summary>The first 12 base64url characters of the SHA-256 of <see cref="Content"/>.</summary>
    public string Hash { get; }

    /// <summary>The file name visitors fetch: <c>blocks.{hash}.css</c>.</summary>
    public string FileName => $"blocks.{Hash}.css";

    private static BlockStylesheet Load()
    {
        using var stream = typeof(BlockStylesheet).Assembly.GetManifestResourceStream("QuickBuilder.Rendering.blocks.css")
            ?? throw new InvalidOperationException(
                "The block stylesheet is not embedded. QuickBuilder.Rendering.csproj embeds src/admin/src/editor/blocks/blocks.css.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new BlockStylesheet(buffer.ToArray());
    }
}
