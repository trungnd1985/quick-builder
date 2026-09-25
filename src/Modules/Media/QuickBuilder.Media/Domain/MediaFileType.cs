namespace QuickBuilder.Media.Domain;

/// <summary>
/// One of the six file types uploads may have (003 FR-007, research M-07, M-08).
/// </summary>
/// <remarks>
/// A closed set: operators may narrow it through <c>Media:AllowedTypes</c> but never widen
/// it. SVG, HTML, scripts and executables are absent on purpose — SVG can carry script, and
/// supporting it safely needs its own specification.
/// </remarks>
internal sealed class MediaFileType
{
    public static readonly MediaFileType Jpeg = new("image/jpeg", ["jpg", "jpeg"], isImage: true);
    public static readonly MediaFileType Png = new("image/png", ["png"], isImage: true);
    public static readonly MediaFileType Gif = new("image/gif", ["gif"], isImage: true);
    public static readonly MediaFileType WebP = new("image/webp", ["webp"], isImage: true);
    public static readonly MediaFileType Avif = new("image/avif", ["avif"], isImage: true);
    public static readonly MediaFileType Pdf = new("application/pdf", ["pdf"], isImage: false);

    /// <summary>Every built-in type, in a stable order.</summary>
    public static IReadOnlyList<MediaFileType> All { get; } = [Jpeg, Png, Gif, WebP, Avif, Pdf];

    /// <summary>Declared types that mean "the client did not say" (M-08).</summary>
    private static readonly string[] Undeclared = ["application/octet-stream"];

    private MediaFileType(string mime, IReadOnlyList<string> extensions, bool isImage)
    {
        Mime = mime;
        Extensions = extensions;
        IsImage = isImage;
    }

    /// <summary>The canonical media type, exactly as served.</summary>
    public string Mime { get; }

    /// <summary>Accepted file-name extensions, lower-case, without the dot.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Images get a sandboxing CSP when served; PDFs cannot (research M-11).</summary>
    public bool IsImage { get; }

    /// <summary>The built-in type with this canonical media type, or null.</summary>
    public static MediaFileType? FromMime(string? mime) =>
        All.FirstOrDefault(t => string.Equals(t.Mime, mime?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a file detected as this type may carry <paramref name="extension"/> and
    /// <paramref name="declaredType"/> (FR-006, M-08).
    /// </summary>
    /// <remarks>
    /// The extension must be one of this type's, compared case-insensitively; a name without
    /// one is a mismatch. The declared type must equal the canonical one, unless it is absent
    /// or <c>application/octet-stream</c> — browsers leave <c>File.type</c> empty for
    /// extensions the OS does not know (AVIF on older systems), and refusing those users for
    /// their OS's ignorance helps nobody. A declared <em>contradiction</em> is still refused.
    /// Parameters after a <c>;</c> (such as <c>charset</c>) are ignored.
    /// </remarks>
    public bool Accepts(string? extension, string? declaredType)
    {
        if (string.IsNullOrEmpty(extension)
            || !Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var declared = declaredType?.Split(';', 2)[0].Trim();
        return string.IsNullOrEmpty(declared)
            || Undeclared.Contains(declared, StringComparer.OrdinalIgnoreCase)
            || string.Equals(declared, Mime, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Mime;
}
