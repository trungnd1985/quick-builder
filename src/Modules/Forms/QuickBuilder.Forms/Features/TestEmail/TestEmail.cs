using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.TestEmail;

internal sealed record TestEmailCommand : ICommand<Result<TestEmailResponse>>;

/// <summary>What the mail server said when it accepted the test message.</summary>
public sealed record TestEmailResponse(string? ServerReply);

/// <summary>
/// Sends a test email to the signed-in Administrator's own address (018 FR-037), so the operator
/// can check the SMTP settings without waiting for a visitor. The server's reply, never the
/// credentials, is returned either way.
/// </summary>
internal sealed class TestEmailHandler(IEmailSender email, IUserDirectory users, IAccessEvaluator access, ILogger<TestEmailHandler> logger)
    : ICommandHandler<TestEmailCommand, Result<TestEmailResponse>>
{
    public async Task<Result<TestEmailResponse>> HandleAsync(TestEmailCommand command, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured)
        {
            return Result.Failure<TestEmailResponse>(FormsErrors.EmailNotConfigured);
        }

        var userId = access.UserId ?? throw new InvalidOperationException("A test email was requested without a signed-in user.");
        var address = await users.OwnEmailAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The signed-in user has no account.");

        var result = await email.SendAsync(
            new EmailMessage(
                [address],
                "QuickBuilder test email",
                "This is a test email from QuickBuilder.\n\nOutgoing email works: form notifications will be sent through this mail server.\n",
                ReplyTo: null),
            cancellationToken).ConfigureAwait(false);

        if (!result.Accepted)
        {
            logger.LogWarning("A test email was refused by the mail server (retryable: {Retryable}).", result.Retryable);
            return Result.Failure<TestEmailResponse>(FormsErrors.EmailFailed(result.ServerReply));
        }

        return Result.Success(new TestEmailResponse(result.ServerReply));
    }
}

/// <summary>Administrative surface: the test email, for Administrators.</summary>
[ApiController]
[Route("api/v1/forms/email-test")]
public sealed class TestEmailController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(FormsCapabilities.EmailTest)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new TestEmailCommand(), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
