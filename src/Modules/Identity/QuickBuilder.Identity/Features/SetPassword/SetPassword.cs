using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.SetPassword;

public sealed record SetPasswordRequest(string? Token, string? NewPassword);

internal sealed record SetPasswordCommand(string? Token, string? NewPassword) : ICommand<Result>;

/// <summary>
/// Uses a set-password link (spec FR-011, FR-011a). The caller is not signed in by this:
/// they sign in afterwards with the new password, which proves they know it.
/// </summary>
internal sealed class SetPasswordHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    TransportSecurity transport,
    SignInThrottleService throttle,
    SetPasswordLinkService links)
    : ICommandHandler<SetPasswordCommand, Result>
{
    public async Task<Result> HandleAsync(SetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Setting a password runs only within an HTTP request.");

        if (transport.RequireSecureTransport(context) is { } insecure)
        {
            return Result.Failure(insecure);
        }

        var clientKey = TransportSecurity.ClientKey(context.Connection.RemoteIpAddress);
        var clientCheck = await throttle.CheckClientAsync(clientKey, cancellationToken).ConfigureAwait(false);
        if (!clientCheck.Allowed)
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(clientCheck.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Result.Failure(IdentityErrors.Throttled);
        }

        var result = await links.ConsumeAsync(command.Token, command.NewPassword, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await throttle.RecordFailureAsync(clientKey, accountKey: null, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}

[ApiController]
[Route("api/v1/identity/set-password")]
public sealed class SetPasswordController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetAsync(SetPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new SetPasswordCommand(request.Token, request.NewPassword), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error!) : NoContent();
    }
}
