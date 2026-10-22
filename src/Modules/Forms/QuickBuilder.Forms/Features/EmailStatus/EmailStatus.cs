using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Forms.Features.EmailStatus;

/// <summary>Whether outgoing email is configured: yes or no, never the server's details (018 FR-037).</summary>
public sealed record EmailStatusResponse(bool Configured);

/// <summary>
/// Administrative surface: lets the editor warn that notifications will not be sent while email
/// is off. Any signed-in user may ask; the answer reveals nothing about the mail server.
/// </summary>
[ApiController]
[Route("api/v1/forms/email-status")]
public sealed class EmailStatusController(IEmailSender email) : ControllerBase
{
    [HttpGet]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new EmailStatusResponse(email.IsConfigured));
}
