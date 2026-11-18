using QuickBuilder.Rendering.Contracts;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// The fixed answers a preview link gets when it cannot show its target (022 research PV-10,
/// spec FR-010, public-preview.md §3).
/// </summary>
/// <remarks>
/// Built once, from nothing the link points at: no site name, design, title or language, so
/// every failure, whatever its cause, is byte-identical (SC-005). English only for now (spec
/// assumption "Bar language and times").
/// </remarks>
internal static class NotAvailablePage
{
    /// <summary>Unknown, expired, revoked, invalidated or altered link, or a target that is gone: 404.</summary>
    public static PublicResponse Gone { get; } =
        PreviewResponse.Fixed(StatusCodes.Status404NotFound, PreviewTexts.GoneTitle, PreviewTexts.GoneMessage);

    /// <summary>A page draft that fails the page schema (PV-15): 503, try again in a minute.</summary>
    public static PublicResponse Later { get; } =
        PreviewResponse.Fixed(StatusCodes.Status503ServiceUnavailable, PreviewTexts.LaterTitle, PreviewTexts.LaterMessage, TimeSpan.FromSeconds(60));

    /// <summary>The "forms are not sent from previews" page (PV-12): 200.</summary>
    public static PublicResponse FormNotSent { get; } =
        PreviewResponse.Fixed(StatusCodes.Status200OK, PreviewTexts.FormTitle, PreviewTexts.FormMessage);

    /// <summary>Too many unknown links from this network (PV-18): 429 until the window ends.</summary>
    public static PublicResponse TooMany(TimeSpan retryAfter) =>
        PreviewResponse.Fixed(StatusCodes.Status429TooManyRequests, PreviewTexts.TooManyTitle, PreviewTexts.TooManyMessage, retryAfter);
}
