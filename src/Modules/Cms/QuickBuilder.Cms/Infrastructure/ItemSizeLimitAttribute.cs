using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Caps an item write's request body before it is read (015 research IT-18): 1 MiB, enough for
/// 100 fields of rich text at the 20,000-character limit, with room for markup.
/// </summary>
/// <remarks>
/// A declared <c>Content-Length</c> over the cap is refused here as <c>cms.item_too_large</c>,
/// before a byte is read; otherwise the server's per-request body limit is set, so it stops
/// receiving the moment the body passes it. The approach of Pages' <c>DraftSizeLimitAttribute</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
internal sealed class ItemSizeLimitAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (http.Request.ContentLength > CmsErrors.MaxItemRequestBytes)
        {
            context.Result = CmsErrorResults.ToActionResult(CmsErrors.ItemTooLarge());
            return;
        }

        var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = CmsErrors.MaxItemRequestBytes;
        }

        await next().ConfigureAwait(false);
    }
}
