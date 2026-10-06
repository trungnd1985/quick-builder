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

    // --- 013 (data-model.md §5) ------------------------------------------------------

    public static readonly Error DimensionsTooLarge = Error.Validation(
        "media.dimensions_too_large",
        "The image has more pixels than this installation accepts.");

    public static readonly Error KindMismatch = Error.Validation(
        "media.kind_mismatch",
        "A replacement must be the same kind of file: an image for an image, a document for a document.");

    public static readonly Error InUse = Error.Conflict(
        "media.in_use",
        "The file is used by pages or components. Confirm to delete it anyway.");

    public static readonly Error InLiveUse = new(
        "media.in_live_use",
        "Published pages show this file. Only a Designer or Administrator can change or delete it.");

    public static readonly Error Conflict = Error.Conflict(
        "media.conflict",
        "The file changed since you loaded it. Reload and try again.");

    public static readonly Error AltTextNotAllowed = Error.Validation(
        "media.alt_text_not_allowed",
        "Only images have alternative text.");

    public static readonly Error InvalidAltText = Error.Validation(
        "media.invalid_alt_text",
        "Alternative text may be at most 300 characters.");

    public static readonly Error FolderNotFound = Error.NotFound("media.folder_not_found", "No such folder.");

    public static readonly Error FolderNameTaken = Error.Conflict(
        "media.folder_name_taken",
        "A folder with this name already exists here.");

    public static readonly Error InvalidFolderName = Error.Validation(
        "media.invalid_folder_name",
        "A folder name must be 1 to 100 characters.");

    public static readonly Error FolderTooDeep = Error.Validation(
        "media.folder_too_deep",
        "Folders can be nested at most 8 levels deep.");

    public static readonly Error FolderCycle = Error.Validation(
        "media.folder_cycle",
        "A folder cannot be moved into itself or one of its own folders.");

    public static readonly Error FolderLimit = Error.Validation(
        "media.folder_limit",
        "A site can have at most 1,000 folders.");

    public static readonly Error FolderNotEmpty = Error.Conflict(
        "media.folder_not_empty",
        "The folder still contains files or folders.");

    public static readonly Error FolderConflict = Error.Conflict(
        "media.folder_conflict",
        "The folders changed while you were working. Reload and try again.");

    public static readonly Error TooManyFiles = Error.Validation(
        "media.too_many_files",
        "At most 100 files can be moved at once.");

    public static Error InvalidQuery(string detail) => Error.Validation("media.invalid_query", detail);

    public static readonly Error StorageCheckRunning = Error.Conflict(
        "media.storage_check_running",
        "A storage check is already running.");
}

/// <summary>
/// Maps a Media failure to a problem response in 001's shape (FR-009).
/// </summary>
internal static class MediaErrorResults
{
    public static int StatusFor(string errorCode) => errorCode switch
    {
        "media.not_found" or "sites.not_found" or "media.folder_not_found" => StatusCodes.Status404NotFound,
        "identity.forbidden" or "media.in_live_use" => StatusCodes.Status403Forbidden,
        "media.in_use" or "media.conflict" or "media.folder_name_taken" or "media.folder_not_empty"
            or "media.folder_conflict" or "media.storage_check_running" => StatusCodes.Status409Conflict,
        "media.too_large" => StatusCodes.Status413PayloadTooLarge,
        "media.unsupported_type" => StatusCodes.Status415UnsupportedMediaType,
        "media.storage_unavailable" => StatusCodes.Status503ServiceUnavailable,
        "media.storage_full" => StatusCodes.Status507InsufficientStorage,
        _ => StatusCodes.Status400BadRequest,
    };

    public static IActionResult ToActionResult(Error error) => ToActionResult(error, extensions: null);

    /// <summary>
    /// As <see cref="ToActionResult(Error)"/>, with extra problem members, such as the usage
    /// summary a <c>media.in_use</c> refusal carries (013 contracts/media-api.yaml).
    /// </summary>
    public static IActionResult ToActionResult(Error error, IReadOnlyDictionary<string, object?>? extensions)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);
        var problem = Create(status, error);
        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        return new ObjectResult(problem)
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
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status413PayloadTooLarge => "Too large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported type",
        StatusCodes.Status503ServiceUnavailable => "Storage unavailable",
        StatusCodes.Status507InsufficientStorage => "Out of space",
        _ => "The request could not be completed",
    };
}
