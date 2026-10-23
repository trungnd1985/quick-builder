using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms;

/// <summary>
/// Every expected failure the Forms module's admin operations report, by code (018
/// contracts/forms-api.yaml).
/// </summary>
/// <remarks>
/// As in <c>CmsErrors</c>, extra fields ride alongside the error instance and
/// <see cref="FormsErrorResults"/> copies them into the problem's extensions. Messages never
/// quote submitted values, names or addresses (Principle IX). The public submit endpoint does
/// not use these: it answers visitors as public-forms.md §2.4 and §3 describe.
/// </remarks>
internal static class FormsErrors
{
    private static readonly ConditionalWeakTable<Error, IReadOnlyDictionary<string, object?>> ExtensionsByError = [];

    internal static Error SiteNotFound { get; } = Error.NotFound("sites.not_found", "No such site.");

    internal static Error SubmissionNotFound { get; } =
        Error.NotFound("forms.submission_not_found", "No such submission on this site.");

    internal static Error InvalidFilter(string reason) =>
        Error.Validation("forms.invalid_filter", reason);

    internal static Error NotSpam { get; } =
        Error.Conflict("forms.not_spam", "Only a submission in Spam can be restored.");

    internal static Error EmailNotConfigured { get; } = Error.Conflict(
        "forms.email_not_configured", "Outgoing email is not configured on this installation.");

    /// <summary>The mail server refused or could not be reached; its reply, never credentials.</summary>
    internal static Error EmailFailed(string? serverReply) => With(
        Error.Conflict("forms.email_failed", "The mail server did not accept the message."),
        ("serverReply", serverReply));

    internal static IReadOnlyDictionary<string, object?>? ExtensionsOf(Error error) =>
        ExtensionsByError.TryGetValue(error, out var extensions) ? extensions : null;

    private static Error With(Error error, params (string Name, object? Value)[] extensions)
    {
        ExtensionsByError.AddOrUpdate(error, extensions.ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal));
        return error;
    }
}

/// <summary>Maps an expected failure to an HTTP response (001 FR-009; forms-api.yaml).</summary>
internal static class FormsErrorResults
{
    internal static IActionResult ToActionResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);

        var problem = new ProblemDetails
        {
            Type = $"https://quickbuilder.dev/errors/{error.Code.Replace('.', '/')}",
            Title = TitleFor(status),
            Status = status,
            Detail = error.Message,
        };
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString();

        if (FormsErrors.ExtensionsOf(error) is { } extensions)
        {
            foreach (var (name, value) in extensions)
            {
                problem.Extensions[name] = value;
            }
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    internal static int StatusFor(string code) => code switch
    {
        "forms.not_spam" or "forms.email_not_configured" => StatusCodes.Status409Conflict,
        "forms.email_failed" => StatusCodes.Status502BadGateway,
        _ when code.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status502BadGateway => "The mail server refused the message",
        _ => "The request could not be completed",
    };
}
