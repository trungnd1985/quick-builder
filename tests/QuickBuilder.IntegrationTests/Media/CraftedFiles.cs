using System.Text;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>One upload attempt and the outcome the spec requires for it.</summary>
/// <param name="Name">What the case is, for the test report.</param>
/// <param name="Bytes">The body.</param>
/// <param name="FileName">The file-name header, or null to omit it.</param>
/// <param name="ContentType">The declared type, or null to omit it.</param>
/// <param name="Status">The expected status.</param>
/// <param name="ErrorCode">The expected errorCode, or null when the upload succeeds.</param>
/// <param name="Chunked">Send without a Content-Length.</param>
public sealed record CraftedFile(
    string Name,
    byte[] Bytes,
    string? FileName,
    string? ContentType,
    int Status,
    string? ErrorCode,
    bool Chunked = false)
{
    public bool Accepted => Status == 201;

    public override string ToString() => Name;
}

/// <summary>
/// The SC-002 test set: at least 50 crafted uploads, generated in code so each is readable
/// in review (003 research M-20).
/// </summary>
/// <remarks>
/// Polyglots come in two kinds (M-07): those that lead with a non-allowed format are
/// refused; those that lead with an allowed signature are accepted — and are the core
/// cases for SC-004, where serving must make them inert.
/// </remarks>
internal static class CraftedFiles
{
    /// <summary>The size limit the validation tests run with, small enough to exercise quickly.</summary>
    public const int Limit = 64 * 1024;

    private const int Unsupported = 415;
    private const int BadRequest = 400;
    private const int TooLarge = 413;
    private const int Created = 201;

    public static IReadOnlyList<CraftedFile> All { get; } = Build();

    public static IEnumerable<CraftedFile> Accepted => All.Where(c => c.Accepted);

    private static List<CraftedFile> Build()
    {
        var cases = new List<CraftedFile>();

        void Refuse(string name, byte[] bytes, string? file, string? type, int status, string code, bool chunked = false) =>
            cases.Add(new CraftedFile(name, bytes, file, type, status, code, chunked));

        void Accept(string name, byte[] bytes, string file, string? type) =>
            cases.Add(new CraftedFile(name, bytes, file, type, Created, null));

        const string script = "<script>alert(document.cookie)</script>";
        const string html = "<!DOCTYPE html><html><body><script>fetch('/api/v1/sites')</script></body></html>";
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"1\"/></svg>";
        const string svgScript = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><script>alert(2)</script></svg>";

        // --- Not an allowed type, whatever the name or declared type says --------------------
        Refuse("script named .jpg", Utf8(script), "x.jpg", "image/jpeg", Unsupported, "media.unsupported_type");
        Refuse("script named .png", Utf8(script), "x.png", "image/png", Unsupported, "media.unsupported_type");
        Refuse("script named .pdf", Utf8(script), "x.pdf", "application/pdf", Unsupported, "media.unsupported_type");
        Refuse("script named .gif, undeclared", Utf8(script), "x.gif", null, Unsupported, "media.unsupported_type");
        Refuse("html named .jpg", Utf8(html), "page.jpg", "image/jpeg", Unsupported, "media.unsupported_type");
        Refuse("html named .webp", Utf8(html), "page.webp", "image/webp", Unsupported, "media.unsupported_type");
        Refuse("html as html", Utf8(html), "page.html", "text/html", Unsupported, "media.unsupported_type");
        Refuse("svg", Utf8(svg), "logo.svg", "image/svg+xml", Unsupported, "media.unsupported_type");
        Refuse("svg with script", Utf8(svgScript), "logo.svg", "image/svg+xml", Unsupported, "media.unsupported_type");
        Refuse("svg named .png", Utf8(svgScript), "logo.png", "image/png", Unsupported, "media.unsupported_type");
        Refuse("windows executable", [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0], "setup.exe", "application/x-msdownload", Unsupported, "media.unsupported_type");
        Refuse("executable named .jpg", [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0], "photo.jpg", "image/jpeg", Unsupported, "media.unsupported_type");
        Refuse("elf named .png", [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0], "x.png", "image/png", Unsupported, "media.unsupported_type");
        Refuse("mach-o named .gif", [0xCF, 0xFA, 0xED, 0xFE, 7, 0, 0, 1], "x.gif", "image/gif", Unsupported, "media.unsupported_type");
        Refuse("zip", [0x50, 0x4B, 0x03, 0x04, 20, 0, 0, 0], "archive.zip", "application/zip", Unsupported, "media.unsupported_type");
        Refuse("docx", [0x50, 0x4B, 0x03, 0x04, 20, 0, 6, 0], "report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Unsupported, "media.unsupported_type");
        Refuse("zip named .pdf", [0x50, 0x4B, 0x03, 0x04, 20, 0, 0, 0], "report.pdf", "application/pdf", Unsupported, "media.unsupported_type");
        Refuse("html leading a gif", Utf8("<html>").Concat(SampleFiles.Gif).ToArray(), "x.gif", "image/gif", Unsupported, "media.unsupported_type");
        Refuse("php leading a png", Utf8("<?php system($_GET['c']); ?>").Concat(SampleFiles.Png).ToArray(), "x.png", "image/png", Unsupported, "media.unsupported_type");
        Refuse("json", Utf8("{\"a\":1}"), "data.json", "application/json", Unsupported, "media.unsupported_type");
        Refuse("plain text", Utf8("hello"), "notes.txt", "text/plain", Unsupported, "media.unsupported_type");
        Refuse("javascript", Utf8("alert(1)"), "app.js", "application/javascript", Unsupported, "media.unsupported_type");
        Refuse("gif88a near miss", Utf8("GIF88a\u0001\u0000"), "x.gif", "image/gif", Unsupported, "media.unsupported_type");
        Refuse("riff wave as webp", Utf8("RIFF\u001a\u0000\u0000\u0000WAVEfmt "), "x.webp", "image/webp", Unsupported, "media.unsupported_type");
        Refuse("heic as avif", SampleFiles.Ftyp("heic", "mif1", "heic"), "x.avif", "image/avif", Unsupported, "media.unsupported_type");
        Refuse("pdf after a space", Utf8(" %PDF-1.4"), "x.pdf", "application/pdf", Unsupported, "media.unsupported_type");

        // --- Allowed content, contradicted by its name or declared type ----------------------
        foreach (var (bytes, extension, contentType) in SampleFiles.All)
        {
            var wrongExtension = extension == "png" ? "gif" : "png";
            var wrongType = contentType == "image/png" ? "image/gif" : "image/png";

            Refuse($"{extension} with a .{wrongExtension} name", bytes, $"file.{wrongExtension}", contentType, BadRequest, "media.type_mismatch");
            Refuse($"{extension} declared as {wrongType}", bytes, $"file.{extension}", wrongType, BadRequest, "media.type_mismatch");
            Refuse($"{extension} with no extension", bytes, "file", contentType, BadRequest, "media.type_mismatch");
        }

        Refuse("png declared as text/html", SampleFiles.Png, "x.png", "text/html", BadRequest, "media.type_mismatch");

        // --- Empty, unnamed, oversized ------------------------------------------------------
        Refuse("empty", [], "empty.png", "image/png", BadRequest, "media.empty");
        Refuse("no file name header", SampleFiles.Png, null, "image/png", BadRequest, "media.file_name_required");
        Refuse("a name of only dots", SampleFiles.Png, "...", "image/png", BadRequest, "media.file_name_required");
        Refuse("one byte over the limit", SampleFiles.OfSize(SampleFiles.Png, Limit + 1), "big.png", "image/png", TooLarge, "media.too_large");
        Refuse("five times the limit", SampleFiles.OfSize(SampleFiles.Png, Limit * 5), "big.png", "image/png", TooLarge, "media.too_large");
        Refuse("one byte over, chunked", SampleFiles.OfSize(SampleFiles.Png, Limit + 1), "big.png", "image/png", TooLarge, "media.too_large", chunked: true);
        Refuse("five times the limit, chunked", SampleFiles.OfSize(SampleFiles.Png, Limit * 5), "big.png", "image/png", TooLarge, "media.too_large", chunked: true);

        // --- Accepted --------------------------------------------------------------------------
        foreach (var (bytes, extension, contentType) in SampleFiles.All)
        {
            Accept($"{extension}, declared", bytes, $"ok.{extension}", contentType);
            Accept($"{extension}, octet-stream", bytes, $"ok.{extension}", "application/octet-stream");
            Accept($"{extension}, undeclared", bytes, $"ok.{extension}", null);
        }

        Accept("jpeg with .jpeg and upper case", SampleFiles.Jpeg, "OK.JPEG", "image/jpeg");
        Accept("exactly at the limit", SampleFiles.OfSize(SampleFiles.Png, Limit), "limit.png", "image/png");
        Accept("gif/javascript polyglot", SampleFiles.Gif.Then("/*<script>alert(1)</script>*/=1;alert(document.cookie)"), "poly.gif", "image/gif");
        Accept("jpeg/html polyglot", SampleFiles.Jpeg.Then("<html><script>alert(document.cookie)</script></html>"), "poly.jpg", "image/jpeg");
        Accept("pdf with javascript", Utf8("%PDF-1.4\n1 0 obj<</Type/Catalog/OpenAction<</S/JavaScript/JS(app.alert(document.cookie))>>>>endobj\n%%EOF\n"), "script.pdf", "application/pdf");

        return cases;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
