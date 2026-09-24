using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Features.CreateUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.IssueSetPasswordLink;

internal sealed record IssueSetPasswordLinkCommand(Guid UserId) : ICommand<Result<IssuedLinkResponse>>;

/// <summary>
/// A new set-password link, for onboarding or a forgotten password (spec FR-011, FR-011a).
/// Any earlier live link stops working. The account's current password keeps working until
/// the new link is used.
/// </summary>
internal sealed class IssueSetPasswordLinkHandler(
    IdentityDbContext database,
    IAccessEvaluator access,
    SetPasswordLinkService links)
    : ICommandHandler<IssueSetPasswordLinkCommand, Result<IssuedLinkResponse>>
{
    public async Task<Result<IssuedLinkResponse>> HandleAsync(IssueSetPasswordLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await database.Users.AsNoTracking()
            .Where(u => u.Id == command.UserId)
            .Select(u => new { u.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure<IssuedLinkResponse>(IdentityErrors.UserNotFound);
        }

        if (user.Status != Domain.UserStatus.Active)
        {
            return Result.Failure<IssuedLinkResponse>(IdentityErrors.UserDeactivated);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var link = await links.IssueAsync(command.UserId, access.UserId, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new IssuedLinkResponse(link.Token, link.ExpiresAtUtc));
    }
}

[ApiController]
[Route("api/v1/identity/users/{userId:guid}/set-password-links")]
public sealed class IssueSetPasswordLinkController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new IssueSetPasswordLinkCommand(userId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? IdentityProblems.ToActionResult(result.Error)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }
}
