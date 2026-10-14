using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Redirects;

/// <summary>What an import added and refused (seo-api.yaml).</summary>
public sealed record ImportRedirectsResponse(int Added, int Shortened, IReadOnlyList<ImportRefusalResponse> Refused);

/// <summary>A refused line: its number and why, never its content (Principle IX).</summary>
public sealed record ImportRefusalResponse(int Line, string ErrorCode, string? Detail);

internal sealed record ImportRedirectsCommand(Guid SiteId, byte[] File) : ICommand<Result<ImportRedirectsResponse>>;

internal sealed record ExportRedirectsQuery(Guid SiteId) : IQuery<Result<string>>;

/// <summary>
/// Import and export of a site's redirects as CSV (017 FR-019, research SE-13).
/// </summary>
/// <remarks>
/// Every row is checked as a created redirect is, against the site and the rows before it;
/// every valid row is added in one transaction, and every refused one reported by line. A file
/// that is not UTF-8, too large, or would take the site past its limit is refused whole.
/// </remarks>
internal sealed class RedirectFileHandlers(PagesDbContext database, RedirectWrites writes, PagesAccess access)
    : ICommandHandler<ImportRedirectsCommand, Result<ImportRedirectsResponse>>,
      IQueryHandler<ExportRedirectsQuery, Result<string>>
{
    public const int MaxRows = 5_000;

    public const int MaxBytes = 1024 * 1024;

    public async Task<Result<ImportRedirectsResponse>> HandleAsync(ImportRedirectsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ImportRedirectsResponse>(PagesErrors.SiteNotFound);
        }

        if (command.File.Length > MaxBytes)
        {
            return Result.Failure<ImportRedirectsResponse>(PagesErrors.RedirectImportTooLarge(MaxRows, MaxBytes));
        }

        var read = RedirectCsv.Read(command.File);
        if (read.IsFailure)
        {
            return Result.Failure<ImportRedirectsResponse>(read.Error);
        }

        var lines = read.Value;
        if (lines.Count > MaxRows)
        {
            return Result.Failure<ImportRedirectsResponse>(PagesErrors.RedirectImportTooLarge(MaxRows, MaxBytes));
        }

        var state = await writes.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (!RedirectWrites.HasRoomFor(state, lines.Count))
        {
            return Result.Failure<ImportRedirectsResponse>(PagesErrors.RedirectLimitReached(RedirectWrites.MaxPerSite));
        }

        var actor = access.Actor;
        var refused = new List<ImportRefusalResponse>();
        var added = 0;
        var shortened = 0;
        foreach (var line in lines)
        {
            var cells = line.Cells;
            if (cells.Length < 3 || RedirectCsv.StatusCodeOf(cells[2]) is not { } status || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1]))
            {
                refused.Add(new ImportRefusalResponse(line.Line, "redirects.import_row_malformed", null));
                continue;
            }

            var match = cells.Length > 3 && !string.IsNullOrWhiteSpace(cells[3]) ? cells[3].Trim().ToLowerInvariant() : RedirectMatchKinds.Exact;
            var note = cells.Length > 4 ? cells[4] : null;
            var targetText = cells[1].Trim();
            var input = targetText.StartsWith('/')
                ? new RedirectTargetInput(RedirectTargetKinds.Path, Path: targetText)
                : new RedirectTargetInput(RedirectTargetKinds.Url, Url: targetText);

            var target = await writes.ResolveTargetAsync(state, input, cancellationToken).ConfigureAwait(false);
            if (target.IsFailure)
            {
                refused.Add(Refusal(line.Line, target.Error));
                continue;
            }

            var check = RedirectWrites.Check(state, null, match, cells[0], target.Value, status, note);
            if (check.IsFailure)
            {
                refused.Add(Refusal(line.Line, check.Error));
                continue;
            }

            writes.Add(state, check.Value, status, enabled: true, note, RedirectOrigins.Import, actor);
            added++;
            shortened += check.Value.Shortened ? 1 : 0;
        }

        if (added > 0)
        {
            var saved = await writes.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return Result.Failure<ImportRedirectsResponse>(saved.Error!);
            }
        }

        return Result.Success(new ImportRedirectsResponse(added, shortened, refused));
    }

    public async Task<Result<string>> HandleAsync(ExportRedirectsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<string>(PagesErrors.SiteNotFound);
        }

        var state = await writes.LoadAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        var redirects = await database.ForSite<Redirect>(query.SiteId)
            .AsNoTracking()
            .OrderBy(r => r.SourcePath)
            .ThenBy(r => r.SourceQuery)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Pages and items are written as their current address; a broken one as its last
        // known form is not available, so it is written as the page's address, if any.
        var rows = redirects.Select(r => new RedirectCsvRow(
            RedirectViews.Source(r),
            RedirectViews.TargetAddress(state, r.Target) ?? state.AddressOf(r.Target) ?? string.Empty,
            r.StatusCode,
            r.MatchKind,
            r.Note));

        return Result.Success(RedirectCsv.Write(rows));
    }

    /// <summary>The refusal of a line: its code, and the redirect it collides with when there is one.</summary>
    private static ImportRefusalResponse Refusal(int line, Error error)
    {
        var detail = PagesErrors.ExtensionsOf(error)?.TryGetValue("redirectId", out var id) == true && id is Guid guid && guid != Guid.Empty
            ? guid.ToString()
            : null;
        return new ImportRefusalResponse(line, error.Code, detail);
    }
}

/// <summary>Administrative surface: import and export a site's redirects.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/redirects")]
public sealed class RedirectFilesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("import")]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [Consumes("text/csv", "text/plain", "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> ImportAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var limit = RedirectFileHandlers.MaxBytes;
        if (Request.ContentLength > limit)
        {
            return PagesErrorResults.ToActionResult(PagesErrors.RedirectImportTooLarge(RedirectFileHandlers.MaxRows, limit));
        }

        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = limit + 1;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                return PagesErrorResults.ToActionResult(PagesErrors.RedirectImportTooLarge(RedirectFileHandlers.MaxRows, limit));
            }
        }

        var result = await dispatcher.SendAsync(new ImportRedirectsCommand(siteId, buffer.ToArray()), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpGet("export")]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ExportRedirectsQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? PagesErrorResults.ToActionResult(result.Error)
            : File(Encoding.UTF8.GetBytes(result.Value), "text/csv; charset=utf-8", "redirects.csv");
    }
}
