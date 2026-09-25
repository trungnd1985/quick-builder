using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// The Media module's expected failures, and how each maps to HTTP (003 contracts/media-api.yaml).
/// </summary>
internal static class MediaErrors
{
    public static readonly Error NotFound = Error.NotFound("media.not_found", "No such file.");

    /// <summary>Byte-identical to Sites' and Identity's "no such site" (002 authorization.md §3).</summary>
    public static readonly Error SiteNotFound = Error.NotFound("sites.not_found", "No such site.");

    /// <summary>Byte-identical to Identity's forbidden response (002 authorization.md §3).</summary>
    public static readonly Error Forbidden = new("identity.forbidden", "You do not have permission to do this.");

    public static readonly Error Empty = Error.Validation("media.empty", "The file is empty.");

    public static readonly Error FileNameRequired = Error.Validation(
        "media.file_name_required",
        "Send the file's name in the X-QuickBuilder-File-Name header.");

    public static readonly Error TypeMismatch = Error.Validation(
        "media.type_mismatch",
        "The file's name or declared type does not match its contents.");

    public static readonly Error UnsupportedType = Error.Validation(
        "media.unsupported_type",
        "The file's contents are not an allowed type.");

    public static readonly Error StorageUnavailable = new(
        "media.storage_unavailable",
        "The file could not be stored. Nothing was added; try again.");

    public static readonly Error StorageFull = new(
        "media.storage_full",
        "The storage location is out of space. Nothing was added.");

    public static readonly Error InvalidCursor = Error.Validation(
        "media.invalid_cursor",
        "The cursor is not one this server issued.");

    public static Error InvalidLimit(int maximum) => Error.Validation(
        "media.invalid_limit",
        $"The page size must be between 1 and {maximum}.");

    public static Error TooLarge(long limit) => Error.Validation(
        "media.too_large",
        $"The file is larger than the limit of {limit} bytes.");
}

/// <summary>
/// Maps a Media failure to a problem response in 001's shape (FR-009).
/// </summary>
internal static class MediaErrorResults
{
    public static int StatusFor(string errorCode) => errorCode switch
    {
        "media.not_found" or "sites.not_found" => StatusCodes.Status404NotFound,
        "identity.forbidden" => StatusCodes.Status403Forbidden,
        "media.too_large" => StatusCodes.Status413PayloadTooLarge,
        "media.unsupported_type" => StatusCodes.Status415UnsupportedMediaType,
        "media.storage_unavailable" => StatusCodes.Status503ServiceUnavailable,
        "media.storage_full" => StatusCodes.Status507InsufficientStorage,
        _ => StatusCodes.Status400BadRequest,
    };

    public static IActionResult ToActionResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);
        return new ObjectResult(Create(status, error))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>For filters, which answer before model binding or the action run.</summary>
    public static Task WriteAsync(HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            Create(status, error), options: null, contentType: "application/problem+json", context.RequestAborted);
    }

    private static ProblemDetails Create(int status, Error error)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://quickbuilder.dev/errors/{error.Code.Replace('.', '/')}",
            Title = TitleFor(status),
            Status = status,
            Detail = error.Message,
        };
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();
        return problem;
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status413PayloadTooLarge => "Too large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported type",
        StatusCodes.Status503ServiceUnavailable => "Storage unavailable",
        StatusCodes.Status507InsufficientStorage => "Out of space",
        _ => "The request could not be completed",
    };
}
