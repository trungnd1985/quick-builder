using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Applies <c>Media:MaxFileSizeBytes</c> to one endpoint before its body is read (003 FR-008, research M-09).
/// </summary>
/// <remarks>
/// Two layers, because a chunked upload declares no length:
/// <list type="bullet">
/// <item>A declared <c>Content-Length</c> over the limit is refused here, before a byte of
/// the body is read.</item>
/// <item>Otherwise the server's own per-request body limit is set to the operator's limit
/// — the framework's supported way to change it for one endpoint — so the server stops
/// receiving the moment the body passes it. The upload stream counts bytes as well, for
/// servers that expose no such limit.</item>
/// </list>
/// A resource filter, so it runs before model binding could touch the body.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
internal sealed class UploadSizeLimitAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var limit = http.RequestServices.GetRequiredService<IOptions<MediaOptions>>().Value.MaxFileSizeBytes;

        if (http.Request.ContentLength > limit)
        {
            context.Result = MediaErrorResults.ToActionResult(MediaErrors.TooLarge(limit));
            return;
        }

        var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = limit;
        }

        await next().ConfigureAwait(false);
    }
}
