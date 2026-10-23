using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Text;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Forms.Features.Submit;

/// <summary>A visitor's submission of one form (018 FR-015; public-forms.md §1).</summary>
/// <param name="Site">The site the request was addressed to, already resolved.</param>
/// <param name="FormId">The form, from the route.</param>
/// <param name="PageId">The posted <c>_page</c>.</param>
/// <param name="Path">The posted <c>_path</c>.</param>
/// <param name="Values">The posted <c>f.&lt;id&gt;</c> values, by field id; other keys are not here.</param>
/// <param name="Trap">The posted <c>_hp</c>.</param>
/// <param name="BasePath">The installation's base path, for a redirect to a page.</param>
/// <param name="Client">The visitor's address, for the rate limit only; never stored as it is.</param>
internal sealed record SubmitCommand(
    SiteDto Site,
    string FormId,
    string? PageId,
    string? Path,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    string? Trap,
    string BasePath,
    IPAddress? Client = null) : ICommand<SubmitResult>;

/// <summary>
/// What the endpoint answers with: the outcome, the failing fields' codes, the values to show
/// again, where a success goes, and the address to show a refusal on.
/// </summary>
internal sealed record SubmitResult(
    SubmitOutcome Outcome,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    string? Redirect,
    string? Path,
    int? RetryAfterSeconds = null);

/// <summary>
/// Checks a submission against the form as it is live and stores it when it passes (018 FR-015,
/// FR-020; research FS-07, FS-08; data-model §4).
/// </summary>
/// <remarks>
/// Values are checked field by field with <see cref="FormValueRules"/>; fields the form does not
/// define were never read. Nothing is stored unless every field passes, and the submission is one
/// row, so a failure stores nothing. Logs name forms and pages by id, never a value (FR-022).
/// </remarks>
internal sealed class SubmitHandler(
    FormsDbContext database,
    LiveFormLocator locator,
    IPublishedContentReader pages,
    IEmailSender email,
    RateWindows rateWindows,
    TimeProvider clock,
    ILogger<SubmitHandler> logger) : ICommandHandler<SubmitCommand, SubmitResult>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SubmitResult> HandleAsync(SubmitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var siteId = command.Site.Id;
        var posted = command.Path is { Length: > 0 } given && given[0] == '/' ? given : "/";

        // Row 1: every request counts, before anything is looked up, so a flood costs one upsert.
        var rate = await rateWindows.CountAsync(siteId, command.Client, cancellationToken).ConfigureAwait(false);
        if (rate.Limited)
        {
            await CountSpamAsync(siteId, command.FormId, SpamCounterKinds.RateLimited, cancellationToken).ConfigureAwait(false);
            return new SubmitResult(
                SubmitOutcome.Limited, new Dictionary<string, string>(), command.Values, Redirect: null, posted, (int)Math.Ceiling(rate.RetryAfter.TotalSeconds));
        }

        var live = await locator.FindAsync(siteId, command.FormId, command.PageId, command.Path, cancellationToken).ConfigureAwait(false);
        var path = live?.Path ?? posted;

        if (live is null)
        {
            return Refused(SubmitOutcome.Gone, command.Values, path);
        }

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var checkedValues = new List<(FormField Field, IReadOnlyList<string> Values)>();
        foreach (var field in live.Form.Fields)
        {
            var result = FormValueRules.Check(field, command.Values.GetValueOrDefault(field.Id) ?? []);
            if (result.Code is { } code)
            {
                errors[field.Id] = code;
            }

            checkedValues.Add((field, result.Values));
        }

        var shown = checkedValues.ToDictionary(c => c.Field.Id, c => c.Values, StringComparer.Ordinal);
        var trapped = !string.IsNullOrEmpty(command.Trap);

        // Rows 4 and 5: invalid values are refused, or silently dropped when the trap was filled.
        if (errors.Count > 0)
        {
            var refused = SubmitPolicy.Decide(new SubmitFacts(RateLimited: false, FormLive: true, TrapFilled: trapped, ValuesValid: false, Duplicate: false));
            if (refused == SubmitOutcome.Dropped)
            {
                await CountSpamAsync(siteId, live.Form.Id, SpamCounterKinds.TrappedDropped, cancellationToken).ConfigureAwait(false);
                return await LooksAcceptedAsync(refused, siteId, live, shown, path, command.BasePath, cancellationToken).ConfigureAwait(false);
            }

            return new SubmitResult(refused, errors, shown, Redirect: null, path);
        }

        var now = clock.GetUtcNow();
        var fingerprint = Fingerprint.Of(live.Form.Id, checkedValues.Select(c => (c.Field.Id, c.Values)));

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Row 6 (FS-14): the lock makes two instances agree on which of two equal requests came first.
        await database.LockFingerprintAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        var since = now - DuplicateWindow;
        var duplicate = await database.Submissions
            .AnyAsync(s => s.FormId == live.Form.Id && s.Fingerprint == fingerprint && s.ReceivedAtUtc >= since, cancellationToken)
            .ConfigureAwait(false);

        var outcome = SubmitPolicy.Decide(new SubmitFacts(RateLimited: false, FormLive: true, TrapFilled: trapped, ValuesValid: true, Duplicate: duplicate));
        if (outcome == SubmitOutcome.Duplicate)
        {
            return await LooksAcceptedAsync(outcome, siteId, live, shown, path, command.BasePath, cancellationToken).ConfigureAwait(false);
        }

        // Row 7: a filled trap with valid values is kept in Spam and never notified (FR-016a).
        var spam = outcome == SubmitOutcome.Spam;
        var submission = Submission.Receive(siteId, new SubmissionReceipt(
            live.Form.Id,
            live.Form.Name,
            live.PageId,
            path,
            now,
            ValuesJson(checkedValues),
            SearchText.Normalise(string.Join(' ', checkedValues.SelectMany(c => c.Values))),
            fingerprint,
            IsSpam: spam,
            spam ? NotificationStates.None : NotificationStates.Initial(hasRecipients: live.Form.Notify.Recipients.Count > 0, emailConfigured: email.IsConfigured)));

        database.Submissions.Add(submission);

        // FS-17: the notification waits in the same transaction as the submission, so a stored
        // submission always has its delivery and a refused one never does.
        if (submission.NotificationState == NotificationStates.Pending)
        {
            database.NotificationDeliveries.Add(NotificationDelivery.For(submission, now));
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "A submission of form {FormId} on site {SiteId} could not be stored.", live.Form.Id, siteId);
            return new SubmitResult(SubmitOutcome.Error, errors, shown, Redirect: null, path);
        }

        if (spam)
        {
            await CountSpamAsync(siteId, live.Form.Id, SpamCounterKinds.TrappedKept, cancellationToken).ConfigureAwait(false);
        }

        return await LooksAcceptedAsync(outcome, siteId, live, shown, path, command.BasePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How long an identical submission of the same form is taken as a repeat (FS-14).</summary>
    internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(10);

    // Silent outcomes answer exactly as a success, redirect included, so a bot learns nothing (FR-016).
    private async Task<SubmitResult> LooksAcceptedAsync(
        SubmitOutcome outcome,
        Guid siteId,
        LiveForm live,
        IReadOnlyDictionary<string, IReadOnlyList<string>> shown,
        string path,
        string basePath,
        CancellationToken cancellationToken) =>
        new(outcome, new Dictionary<string, string>(), shown, await RedirectAsync(siteId, live.Form.Redirect, basePath, cancellationToken).ConfigureAwait(false), path);

    private Task<int> CountSpamAsync(Guid siteId, string formId, string kind, CancellationToken cancellationToken) =>
        database.CountSpamAsync(siteId, formId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), kind, cancellationToken);

    private static SubmitResult Refused(SubmitOutcome outcome, IReadOnlyDictionary<string, IReadOnlyList<string>> values, string path) =>
        new(outcome, new Dictionary<string, string>(), values, Redirect: null, path);

    /// <summary>
    /// Where a success goes: the redirect page's current address while it is live, or the
    /// external address; null for the success message, also when the page is no longer live
    /// (analysis M3).
    /// </summary>
    private async Task<string?> RedirectAsync(Guid siteId, FormRedirect? redirect, string basePath, CancellationToken cancellationToken)
    {
        if (redirect?.Href is { } href)
        {
            return href;
        }

        if (redirect?.PageId is not { } pageId)
        {
            return null;
        }

        var targets = await pages.GetLinkTargetsAsync(siteId, [pageId], cancellationToken).ConfigureAwait(false);
        return targets.TryGetValue(pageId, out var target) && target.IsLive && !target.Path.Contains('{', StringComparison.Ordinal)
            ? basePath.TrimEnd('/') + target.Path
            : null;
    }

    /// <summary>The stored values (data-model §1): each field with its label and type as they are now.</summary>
    private static string ValuesJson(IEnumerable<(FormField Field, IReadOnlyList<string> Values)> values) =>
        JsonSerializer.Serialize(
            values.Select(v => new
            {
                fieldId = v.Field.Id,
                label = v.Field.Label,
                type = v.Field.Type,
                value = v.Field.Type == FieldTypes.Checkboxes ? (object)v.Values : v.Values.Count > 0 ? v.Values[0] : string.Empty,
            }),
            Json);
}
