using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Forms.Features.ExportSubmissions;

/// <summary>
/// The filtered inbox as CSV (018 FR-029, FR-030; research FS-16): one row per submission,
/// newest first; time, form and page; then one column per field label found in the exported
/// submissions, in order of first appearance. Spam is never exported (FR-016a).
/// </summary>
/// <remarks>
/// Everything is read in one read-only, repeatable-read transaction, so the label pass and the
/// rows see the same snapshot: a submission arriving during the export is not in the file. Rows
/// are streamed; the export is recorded with its filters, never the search words.
/// </remarks>
internal sealed class SubmissionExport(FormsDbContext database, SubmissionReads reads, SubmissionLog log, FormsAccess access, TimeProvider clock)
{
    private const int Batch = 500;

    public async Task WriteAsync(SiteDto site, SubmissionFilter filter, Stream body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(filter);

        int count;
        await using (var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false))
        {
            await database.MakeTransactionReadOnlyAsync(cancellationToken).ConfigureAwait(false);

            var query = reads.Matching(site.Id, filter).AsNoTracking();
            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var values in SubmissionReads.Page(query, after: null).Select(s => s.Values).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                foreach (var label in Labels(values))
                {
                    if (seen.Add(label))
                    {
                        labels.Add(label);
                    }
                }
            }

            var forms = await reads.FormsAsync(site.Id, cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(body, CsvWriter.Encoding, bufferSize: 64 * 1024, leaveOpen: true);
            var csv = new CsvWriter(writer);
            await csv.WriteRowAsync(["Received (UTC)", "Form", "Page", .. labels], cancellationToken).ConfigureAwait(false);

            count = 0;
            (DateTimeOffset, Guid)? after = null;
            while (true)
            {
                var page = await SubmissionReads.Page(query, after).Take(Batch).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var submission in page)
                {
                    var byLabel = SubmissionReads.Values(submission)
                        .GroupBy(v => v.Label, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => string.Join("; ", g.SelectMany(v => v.Values)), StringComparer.Ordinal);
                    await csv.WriteRowAsync(
                        [
                            submission.ReceivedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            forms.TryGetValue(submission.FormId, out var form) ? form.Form.Name : submission.FormName,
                            submission.PagePath,
                            .. labels.Select(l => byLabel.GetValueOrDefault(l, string.Empty)),
                        ],
                        cancellationToken).ConfigureAwait(false);
                }

                count += page.Count;
                if (page.Count < Batch)
                {
                    break;
                }

                after = (page[^1].ReceivedAtUtc, page[^1].Id);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        log.Add(site.Id, access.Actor, SubmissionLogActions.Export, filter.FormId, count, filter.ToLogJson());
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file name: the site's host and the time, so exports sort and never collide.</summary>
    public string FileName(SiteDto site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var host = new string([.. site.PrimaryHostName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        return $"submissions-{host}-{clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";
    }

    private static IEnumerable<string> Labels(string valuesJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(valuesJson);
        return [.. document.RootElement.EnumerateArray().Select(v => v.GetProperty("label").GetString()!)];
    }
}

internal sealed record ExportSubmissionsCommand(Guid SiteId, SubmissionQueryRequest Parameters, Func<string, Stream> OpenBody) : ICommand<Result>;

/// <summary>Checks the request, then streams the file into the body the controller opens for it.</summary>
internal sealed class ExportSubmissionsHandler(FormsAccess access, SubmissionExport export) : ICommandHandler<ExportSubmissionsCommand, Result>
{
    public async Task<Result> HandleAsync(ExportSubmissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await access.SiteAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return Result.Failure(FormsErrors.SiteNotFound);
        }

        var filter = command.Parameters.ToFilter();
        if (filter.IsFailure)
        {
            return Result.Failure(filter.Error);
        }

        // FR-029: the export covers the inbox only, whatever box was asked for.
        await export.WriteAsync(site, filter.Value with { Spam = false }, command.OpenBody(export.FileName(site)), cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Administrative surface: export submissions.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions/export")]
public sealed class ExportSubmissionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportAsync(Guid siteId, [FromQuery] SubmissionQueryRequest parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await dispatcher.SendAsync(
                new ExportSubmissionsCommand(siteId, parameters, fileName =>
                {
                    Response.ContentType = "text/csv; charset=utf-8";
                    Response.Headers.CacheControl = "no-store";
                    Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
                    return Response.Body;
                }),
                cancellationToken)
            .ConfigureAwait(false);

        // On success the file has been written to the body already.
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : new EmptyResult();
    }
}
