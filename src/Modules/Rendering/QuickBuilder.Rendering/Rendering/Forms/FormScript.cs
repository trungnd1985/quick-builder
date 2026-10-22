using System.Security.Cryptography;
using System.Text;

namespace QuickBuilder.Rendering.Rendering.Forms;

/// <summary>
/// The form script: Form blocks' enhancements (018 FR-010; research FS-10; public-forms.md §2.2).
/// </summary>
/// <remarks>
/// <para>
/// Forms work without it: they post, and a refusal comes back as a page (FS-09). With it, a form
/// checks every field with the same rules as the server before sending, shows the same messages
/// and summary in place, sends with <c>fetch</c> asking for JSON, disables its button while
/// sending, and shows the success panel or follows the redirect.
/// </para>
/// <para>
/// Its text is <c>Resources/form-script.js</c>, embedded, so no escaping stands between the file
/// and the bytes the page carries. It is written once per page, after the first form, and the
/// page's content security policy admits exactly these bytes by their hash. The admin's
/// <c>formScript.spec.ts</c> runs the same file against the shared value-rule fixture.
/// </para>
/// </remarks>
internal static class FormScript
{
    /// <summary>The script's text, exactly as written between the tags.</summary>
    public static string Content { get; } = Load();

    /// <summary>The script element as the page carries it.</summary>
    public static string Element { get; } = "<script>" + Content + "</script>";

    /// <summary>Base64 SHA-256 of <see cref="Content"/>, for <c>script-src 'sha256-…'</c>.</summary>
    public static string Hash { get; } = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Content)));

    private static string Load()
    {
        using var stream = typeof(FormScript).Assembly.GetManifestResourceStream("QuickBuilder.Rendering.form-script.js")
            ?? throw new InvalidOperationException("The form script is not embedded. QuickBuilder.Rendering.csproj embeds Resources/form-script.js.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Line endings are fixed to LF so the hash is the same however the file was checked out.
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }
}
