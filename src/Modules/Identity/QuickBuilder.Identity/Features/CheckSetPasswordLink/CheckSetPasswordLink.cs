using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.CheckSetPasswordLink;

/// <summary>The token travels in the body, never in the URL, so it never reaches access logs (R-10).</summary>
public sealed record CheckSetPasswordLinkRequest(string? Token);

public sealed record CheckSetPasswordLinkResponse(DateTimeOffset ExpiresAtUtc);

internal sealed record CheckSetPasswordLinkCommand(string? Token) : ICommand<Result<CheckSetPasswordLinkResponse>>;

/// <summary>
/// Whether a link can be used, so the set-password screen can say so before the user types
/// a password. Every unusable link gets the same answer (FR-011a); guessing is throttled
/// per client like guessing passwords.
/// </summary>
internal sealed class CheckSetPasswordLinkHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    SignInThrottleService throttle,
    SetPasswordLinkService links)
    : ICommandHandler<CheckSetPasswordLinkCommand, Result<CheckSetPasswordLinkResponse>>
{
    public async Task<Result<CheckSetPasswordLinkResponse>> HandleAsync(CheckSetPasswordLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Checking a link runs only within an HTTP request.");
        var clientKey = TransportSecurity.ClientKey(context.Connection.RemoteIpAddress);

        var clientCheck = await throttle.CheckClientAsync(clientKey, cancellationToken).ConfigureAwait(false);
        if (!clientCheck.Allowed)
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(clientCheck.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Result.Failure<CheckSetPasswordLinkResponse>(IdentityErrors.Throttled);
        }

        var expiresAt = await links.CheckAsync(command.Token, cancellationToken).ConfigureAwait(false);
        if (expiresAt is null)
        {
            await throttle.RecordFailureAsync(clientKey, accountKey: null, cancellationToken).ConfigureAwait(false);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure<CheckSetPasswordLinkResponse>(IdentityErrors.LinkInvalid);
        }

        return Result.Success(new CheckSetPasswordLinkResponse(expiresAt.Value));
    }
}

[ApiController]
[Route("api/v1/identity/set-password-links/check")]
public sealed class CheckSetPasswordLinkController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CheckAsync(CheckSetPasswordLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new CheckSetPasswordLinkCommand(request.Token), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
