using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Sends the notification of each submission, in the background (018 FR-031, FR-034; research
/// FS-17): every 5 seconds it fails expired claims, then claims due deliveries one at a time,
/// sends each and records the outcome, mirrored onto the submission.
/// </summary>
/// <remarks>
/// <para>
/// A claim commits <c>sending</c> before the message leaves, so a delivery is sent at most once:
/// an instance that dies mid-send leaves a claim whose lease runs out, and that delivery fails
/// as interrupted instead of going out twice. A refusal that may pass waits 1, 5, 15, 30 and
/// 60 minutes; a permanent one, or the sixth, fails for good.
/// </para>
/// <para>
/// The message is built at each attempt from the submission and its form's last published
/// settings. Logs name the submission, form and site, never a recipient, value or the server's
/// reply, which may quote an address; the reply is kept on the delivery for the admin.
/// </para>
/// </remarks>
internal sealed class NotificationWorker(
    IServiceScopeFactory scopes,
    IEmailSender email,
    AdminLinks links,
    TimeProvider clock,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!email.IsConfigured)
        {
            return;
        }

        if (!links.CanLink)
        {
            logger.LogWarning(
                "Email is configured but Hosting:PublicUrl is empty, so form notifications cannot link to the submission. Set Hosting:PublicUrl to the address the admin is reached at.");
        }

        using var timer = new PeriodicTimer(PollInterval, clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending form notifications failed; retrying next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Fails expired claims, then sends every due delivery. Returns how many it attempted. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var interrupted = await database.FailExpiredLeasesAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            if (interrupted > 0)
            {
                logger.LogWarning("{Count} form notification(s) were interrupted mid-send and will not be retried.", interrupted);
            }
        }

        var attempted = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            if (await database.ClaimNextDeliveryAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false) is not { } claim)
            {
                return attempted;
            }

            attempted++;
            await SendAsync(scope.ServiceProvider, database, claim.SubmissionId, claim.SiteId, cancellationToken).ConfigureAwait(false);
        }

        return attempted;
    }

    private async Task SendAsync(IServiceProvider services, FormsDbContext database, Guid submissionId, Guid siteId, CancellationToken cancellationToken)
    {
        var delivery = await database.ForSite<NotificationDelivery>(siteId).SingleOrDefaultAsync(d => d.SubmissionId == submissionId, cancellationToken).ConfigureAwait(false);
        var submission = await database.ForSite<Submission>(siteId).SingleOrDefaultAsync(s => s.Id == submissionId, cancellationToken).ConfigureAwait(false);
        if (delivery is null || submission is null)
        {
            // Deleted since the claim; the delivery went with it.
            return;
        }

        var form = await database.ForSite<FormRecord>(siteId).AsNoTracking().SingleOrDefaultAsync(f => f.FormId == submission.FormId, cancellationToken).ConfigureAwait(false);
        var site = await services.GetRequiredService<ISiteDirectory>().FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false);

        EmailResult result;
        if (form is null || site is null || form.Recipients.Length == 0)
        {
            result = new EmailResult(Accepted: false, Retryable: false, ServerReply: "The form no longer has anyone to notify.");
        }
        else
        {
            var message = services.GetRequiredService<NotificationComposer>().Compose(submission, form, site.Name, site.DefaultLanguage);
            result = await email.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.GetUtcNow();
        if (result.Accepted)
        {
            delivery.MarkSent(now);
        }
        else
        {
            delivery.MarkRefused(result.ServerReply ?? "The mail server refused the message.", result.Retryable, now);
        }

        submission.SetNotificationState(delivery.SubmissionState);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (result.Accepted)
        {
            logger.LogInformation("Sent the notification of submission {SubmissionId} (form {FormId}, site {SiteId}).", submissionId, submission.FormId, siteId);
        }
        else
        {
            logger.LogWarning(
                "The notification of submission {SubmissionId} (form {FormId}, site {SiteId}) was refused on attempt {Attempt}; {Outcome}.",
                submissionId,
                submission.FormId,
                siteId,
                delivery.Attempts,
                delivery.State == DeliveryStates.Pending ? "it will be retried" : "it will not be retried");
        }
    }
}
