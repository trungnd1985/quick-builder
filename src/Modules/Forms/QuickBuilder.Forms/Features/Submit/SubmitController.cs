using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Routing;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Forms.Features.Submit;

/// <summary>
/// Public surface: a visitor's form submission (018 FR-015; research FS-06, FS-09;
/// public-forms.md §1 to §3).
/// </summary>
/// <remarks>
/// <para>
/// Under <c>/_forms</c>, reserved on every site's host names, so site resolution has already run
/// and an unknown or disabled host never gets here (009). The body is urlencoded only, at most
/// 256 KB. The script asks for JSON; anything else gets the answers that work without scripts:
/// a <c>303</c> after success (post/redirect/get, so reloading sends nothing), and the page
/// rendered again with the visitor's values after a refusal, never cached.
/// </para>
/// </remarks>
[ApiController]
// Public surface: site visitors never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class SubmitController(IDispatcher dispatcher, ICurrentSite currentSite, IFormResponsePage responsePage, IPublicNotFoundPage notFound)
    : ControllerBase
{
    /// <summary>The largest body accepted (FR-015).</summary>
    public const int MaxBodyBytes = 262_144;

    private const string FieldPrefix = "f.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [HttpPost(PublicRoutes.Forms + "/{formId}")]
    [ReadsOwnForm]
    [RequestSizeLimit(MaxBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBodyBytes, ValueLengthLimit = MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status303SeeOther)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SubmitAsync(string formId, CancellationToken cancellationToken)
    {
        if (currentSite.Site is not { } site)
        {
            return NotFound();
        }

        // FR-015: refused before reading, whatever the server in front enforces.
        if (Request.ContentLength > MaxBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Urlencoded only: no files, no multipart parsing (FS-06).
        if (!string.Equals(Request.ContentType?.Split(';')[0].Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status400BadRequest);
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            // The form reader throws InvalidDataException when a value, a key or the count of them
            // passes its limits, which a body of unknown length can only do by being too large.
            return StatusCode(ex is InvalidDataException or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest);
        }

        var values = form.Keys
            .Where(k => k.StartsWith(FieldPrefix, StringComparison.Ordinal))
            .ToDictionary(k => k[FieldPrefix.Length..], k => (IReadOnlyList<string>)[.. form[k].OfType<string>()], StringComparer.Ordinal);
        var basePath = Request.PathBase.Value ?? string.Empty;

        var result = await dispatcher.SendAsync(
                new SubmitCommand(site, formId, form["_page"].FirstOrDefault(), form["_path"].FirstOrDefault(), values, form["_hp"].FirstOrDefault(), basePath, HttpContext.Connection.RemoteIpAddress),
                cancellationToken)
            .ConfigureAwait(false);

        Response.Headers.CacheControl = "no-store";
        return WantsJson()
            ? JsonAnswer(result)
            : await HtmlAnswerAsync(site, formId, basePath, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forms are only posted to: anything else is refused (public-forms.md §1).</summary>
    [HttpGet(PublicRoutes.Forms + "/{**rest}")]
    [HttpHead(PublicRoutes.Forms + "/{**rest}")]
    [HttpPut(PublicRoutes.Forms + "/{**rest}")]
    [HttpPatch(PublicRoutes.Forms + "/{**rest}")]
    [HttpDelete(PublicRoutes.Forms + "/{**rest}")]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    public IActionResult OtherMethod()
    {
        Response.Headers.Allow = "POST";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private bool WantsJson() =>
        Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private static int StatusOf(SubmitOutcome outcome) => outcome switch
    {
        SubmitOutcome.Invalid => StatusCodes.Status422UnprocessableEntity,
        SubmitOutcome.Limited => StatusCodes.Status429TooManyRequests,
        SubmitOutcome.Gone or SubmitOutcome.Error => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status200OK,
    };

    private static string StatusText(SubmitOutcome outcome) => outcome switch
    {
        SubmitOutcome.Invalid => "invalid",
        SubmitOutcome.Limited => "limited",
        SubmitOutcome.Gone => "gone",
        SubmitOutcome.Error => "error",
        _ => "ok",
    };

    /// <summary>public-forms.md §3: the silent outcomes answer exactly as a stored submission.</summary>
    private ContentResult JsonAnswer(SubmitResult result)
    {
        if (result.RetryAfterSeconds is { } retryAfter)
        {
            Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var looksAccepted = result.Outcome.LooksAccepted();
        var body = new
        {
            status = StatusText(looksAccepted ? SubmitOutcome.Accepted : result.Outcome),
            errors = result.Outcome == SubmitOutcome.Invalid ? result.Errors : null,
            redirect = looksAccepted ? result.Redirect : null,
            retryAfter = result.Outcome == SubmitOutcome.Limited ? result.RetryAfterSeconds : null,
        };

        return new ContentResult
        {
            StatusCode = looksAccepted ? StatusCodes.Status200OK : StatusOf(result.Outcome),
            ContentType = "application/json; charset=utf-8",
            Content = JsonSerializer.Serialize(body, Json),
        };
    }

    /// <summary>public-forms.md §2.4: a redirect after success, the page again after a refusal.</summary>
    private async Task<IActionResult> HtmlAnswerAsync(SiteDto site, string formId, string basePath, SubmitResult result, CancellationToken cancellationToken)
    {
        if (result.Outcome.LooksAccepted())
        {
            Response.Headers.Location = result.Redirect ?? basePath.TrimEnd('/') + (result.Path ?? "/") + "#" + "qb-sent-" + formId;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var state = new FormState(
            formId,
            result.Values,
            result.Errors,
            result.Outcome switch
            {
                SubmitOutcome.Invalid => FormBanner.None,
                SubmitOutcome.Limited => FormBanner.Limited,
                _ => FormBanner.Error,
            },
            result.RetryAfterSeconds);

        var page = await responsePage.RenderAsync(site, result.Path ?? "/", basePath, state, StatusOf(result.Outcome), cancellationToken).ConfigureAwait(false)
            ?? await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false);
        return new PublicAnswer(page);
    }

    /// <summary>
    /// Keeps model binding away from the body: the action reads the form itself, so its limits
    /// answer <c>413</c> and <c>400</c> as public-forms.md §1 says, not as a validation problem.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class ReadsOwnFormAttribute : Attribute, Microsoft.AspNetCore.Mvc.Filters.IResourceFilter
    {
        public void OnResourceExecuting(Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext context)
        {
            var factories = context.ValueProviderFactories;
            factories.RemoveType<FormValueProviderFactory>();
            factories.RemoveType<FormFileValueProviderFactory>();
            factories.RemoveType<JQueryFormValueProviderFactory>();
        }

        public void OnResourceExecuted(Microsoft.AspNetCore.Mvc.Filters.ResourceExecutedContext context)
        {
        }
    }

    /// <summary>Writes a <see cref="PublicResponse"/> exactly, as the page route does.</summary>
    private sealed class PublicAnswer(PublicResponse response) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            http.Response.StatusCode = response.StatusCode;
            http.Response.ContentType = response.ContentType;
            foreach (var (name, value) in response.Headers)
            {
                http.Response.Headers[name] = value;
            }

            http.Response.ContentLength = response.Body.Length;
            await http.Response.Body.WriteAsync(response.Body, http.RequestAborted).ConfigureAwait(false);
        }
    }
}
