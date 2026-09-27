using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Caps a draft save's request body before it is read (research P-16).
/// </summary>
/// <remarks>
/// The body is the document plus a small envelope (<c>version</c>), so the cap is the
/// document limit with room to spare; the document's own 1 MiB is checked exactly by the
/// validator. A declared <c>Content-Length</c> over the cap is refused here, before a byte is
/// read; otherwise the server's per-request body limit is set, so it stops receiving the
/// moment the body passes it. The same approach as Media's upload limit.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
internal sealed class DraftSizeLimitAttribute : Attribute, IAsyncResourceFilter
{
    /// <summary>1.1 MiB: the document limit plus room for the envelope.</summary>
    internal const long MaxBodyBytes = PageSchema.MaxDocumentBytes + (PageSchema.MaxDocumentBytes / 10);

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            context.Result = PagesErrorResults.ToActionResult(PagesErrors.DocumentTooLarge);
            return;
        }

        var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        await next().ConfigureAwait(false);
    }
}
