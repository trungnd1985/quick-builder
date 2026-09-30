using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Features.GetDesignTokens;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Editor.Features.SaveDesignTokens;

/// <summary>The whole new token set, and the <see cref="Version"/> the caller last saw (0 before the first save).</summary>
public sealed record SaveDesignTokensRequest(JsonElement Document, uint Version);

internal sealed record SaveDesignTokensCommand(Guid SiteId, JsonElement Document, uint Version)
    : ICommand<Result<DesignTokensResponse>>;

internal sealed class SaveDesignTokensValidator : IValidator<SaveDesignTokensCommand>
{
    public ValidationResult Validate(SaveDesignTokensCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return instance.Document.ValueKind == JsonValueKind.Undefined
            ? ValidationResult.Invalid(new ValidationFailure("document", "A document is required."))
            : ValidationResult.Valid();
    }
}

/// <summary>
/// Replaces a site's design tokens (011 FR-031, US6). Changes nothing visitors see
/// (FR-034). A token left out is deleted; blocks that use it fall back to their property
/// default (clarification Q4), so nothing else changes here.
/// </summary>
internal sealed partial class SaveDesignTokensHandler(
    EditorDbContext database,
    TokenSetValidator validator,
    IDesignTokenReader reader,
    EditorAccess access,
    DesignPublishing.DesignPublicationReads publication,
    TimeProvider clock,
    ILogger<SaveDesignTokensHandler> logger)
    : ICommandHandler<SaveDesignTokensCommand, Result<DesignTokensResponse>>
{
    public async Task<Result<DesignTokensResponse>> HandleAsync(SaveDesignTokensCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DesignTokensResponse>(EditorErrors.SiteNotFound);
        }

        var text = command.Document.GetRawText();
        var checkedSet = validator.Validate(Encoding.UTF8.GetBytes(text));
        if (checkedSet.IsFailure)
        {
            return Result.Failure<DesignTokensResponse>(checkedSet.Error!);
        }

        var existing = await database.ForSite<DesignTokenSet>(command.SiteId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // The first save: only a caller who saw the defaults (version 0) may make it.
            if (command.Version != 0)
            {
                return Result.Failure<DesignTokensResponse>(EditorErrors.OutOfDate);
            }

            database.DesignTokenSets.Add(DesignTokenSet.Create(command.SiteId, text, checkedSet.Value, access.Actor, clock));
        }
        else
        {
            if (existing.Version != command.Version)
            {
                return Result.Failure<DesignTokensResponse>(EditorErrors.OutOfDate);
            }

            existing.Replace(text, checkedSet.Value, access.Actor, clock);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved between our read and our write.
            return Result.Failure<DesignTokensResponse>(EditorErrors.OutOfDate);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two first saves raced; the other one won.
            return Result.Failure<DesignTokensResponse>(EditorErrors.OutOfDate);
        }

        var saved = await reader.ReadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        LogSaved(logger, command.SiteId, saved.Version);
        return Result.Success(await DesignTokensResponses.FromAsync(saved, access, publication, cancellationToken).ConfigureAwait(false));
    }

    // Ids and versions only: never token names or values (011 FR-045).
    [LoggerMessage(Level = LogLevel.Information, Message = "Design tokens of site {SiteId} saved at version {Version}")]
    private static partial void LogSaved(ILogger logger, Guid siteId, uint version);
}

/// <summary>
/// Caps a token save's request body before it is read, as <c>DraftSizeLimitAttribute</c> does
/// for drafts: the token set limit plus room for the envelope. The set's own 64 KiB is checked
/// exactly by the validator.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
internal sealed class TokenSetSizeLimitAttribute : Attribute, IAsyncResourceFilter
{
    internal const long MaxBodyBytes = DesignTokens.MaxDocumentBytes + 4096;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            context.Result = EditorErrorResults.ToActionResult(EditorErrors.TooLarge);
            return;
        }

        var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        await next().ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: replace a site's design tokens.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/design-tokens")]
public sealed class SaveDesignTokensController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [TokenSetSizeLimit]
    [RequireCapability(EditorCapabilities.DesignTokensManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, SaveDesignTokensRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new SaveDesignTokensCommand(siteId, request.Document, request.Version),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error!) : Ok(result.Value);
    }
}
