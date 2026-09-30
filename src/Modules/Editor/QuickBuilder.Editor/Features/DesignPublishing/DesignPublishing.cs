using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Features.GetDesignTokens;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.DesignPublishing;

// 012 FR-010, research PR-17: the design is published on its own, recorded, and can be rolled
// back (clarification Q1). contracts/publishing-api.yaml, "Design publishing".

/// <summary>What visitors get of the design: the live version, and whether the draft differs.</summary>
public sealed record DesignPublicationResponse(
    int LiveNumber,
    DateTimeOffset? LiveSinceUtc,
    UserReferenceResponse? LiveBy,
    bool HasUnpublishedChanges);

/// <summary>The token set version the caller saw; 0 while the site uses the defaults.</summary>
public sealed record PublishDesignRequest(uint Version);

/// <summary>The retained design version to publish again.</summary>
public sealed record RollbackDesignRequest(int VersionNumber);

/// <summary>One retained design version.</summary>
public sealed record DesignVersionSummaryResponse(
    int Number,
    string Kind,
    int? RestoredFromNumber,
    DateTimeOffset PublishedAtUtc,
    UserReferenceResponse PublishedBy);

/// <summary>The design's publication and its retained versions, newest first.</summary>
public sealed record DesignVersionsResponse(DesignPublicationResponse Publication, IReadOnlyList<DesignVersionSummaryResponse> Versions);

/// <summary>One retained design version's token document.</summary>
public sealed record DesignVersionResponse(int Number, int SchemaVersion, JsonElement Document);

internal sealed record PublishDesignCommand(Guid SiteId, uint Version) : ICommand<Result<DesignPublicationResponse>>;

internal sealed record RollbackDesignCommand(Guid SiteId, int VersionNumber) : ICommand<Result<DesignPublicationResponse>>;

internal sealed record ListDesignVersionsQuery(Guid SiteId) : IQuery<Result<DesignVersionsResponse>>;

internal sealed record GetDesignVersionQuery(Guid SiteId, int Number) : IQuery<Result<DesignVersionResponse>>;

/// <summary>The design's publication, as every design response shows it.</summary>
internal sealed class DesignPublicationReads(EditorDbContext database, IDesignTokenReader tokens, EditorAccess access)
{
    public async Task<DesignPublicationResponse> SummaryAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var publication = await database.ForSite<DesignPublication>(siteId)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        long draftVersion = (await tokens.ReadAsync(siteId, cancellationToken).ConfigureAwait(false)).Version;

        if (publication is null)
        {
            // Never published: visitors get the defaults, so a saved token set is a change.
            return new DesignPublicationResponse(0, null, null, HasUnpublishedChanges: draftVersion != 0);
        }

        var liveBy = new UserReferenceResponse(publication.LiveBy, await access.NameOfAsync(publication.LiveBy, cancellationToken).ConfigureAwait(false));
        return new DesignPublicationResponse(publication.LiveNumber, publication.LiveSinceUtc, liveBy, publication.HasUnpublishedChanges(draftVersion));
    }
}

internal sealed class PublishDesignHandler(Infrastructure.DesignPublishing publishing, DesignPublicationReads reads)
    : ICommandHandler<PublishDesignCommand, Result<DesignPublicationResponse>>
{
    public async Task<Result<DesignPublicationResponse>> HandleAsync(PublishDesignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await publishing.PublishAsync(command.SiteId, command.Version, cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? Result.Failure<DesignPublicationResponse>(result.Error)
            : Result.Success(await reads.SummaryAsync(command.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class RollbackDesignHandler(Infrastructure.DesignPublishing publishing, DesignPublicationReads reads)
    : ICommandHandler<RollbackDesignCommand, Result<DesignPublicationResponse>>
{
    public async Task<Result<DesignPublicationResponse>> HandleAsync(RollbackDesignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await publishing.RollbackAsync(command.SiteId, command.VersionNumber, cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? Result.Failure<DesignPublicationResponse>(result.Error)
            : Result.Success(await reads.SummaryAsync(command.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class ListDesignVersionsHandler(EditorDbContext database, EditorAccess access, DesignPublicationReads reads)
    : IQueryHandler<ListDesignVersionsQuery, Result<DesignVersionsResponse>>
{
    public async Task<Result<DesignVersionsResponse>> HandleAsync(ListDesignVersionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DesignVersionsResponse>(EditorErrors.SiteNotFound);
        }

        var versions = await database.ForSite<DesignVersion>(query.SiteId)
            .AsNoTracking()
            .OrderByDescending(v => v.Number)
            .Select(v => new { v.Number, v.Kind, v.RestoredFromNumber, v.PublishedAtUtc, v.PublishedBy })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = new Dictionary<Guid, string?>();
        foreach (var userId in versions.Select(v => v.PublishedBy).Distinct())
        {
            names[userId] = await access.NameOfAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(new DesignVersionsResponse(
            await reads.SummaryAsync(query.SiteId, cancellationToken).ConfigureAwait(false),
            [.. versions.Select(v => new DesignVersionSummaryResponse(
                v.Number, v.Kind, v.RestoredFromNumber, v.PublishedAtUtc, new UserReferenceResponse(v.PublishedBy, names[v.PublishedBy])))]));
    }
}

internal sealed class GetDesignVersionHandler(EditorDbContext database, EditorAccess access)
    : IQueryHandler<GetDesignVersionQuery, Result<DesignVersionResponse>>
{
    public async Task<Result<DesignVersionResponse>> HandleAsync(GetDesignVersionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DesignVersionResponse>(EditorErrors.SiteNotFound);
        }

        var version = await database.ForSite<DesignVersion>(query.SiteId)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Number == query.Number, cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            return Result.Failure<DesignVersionResponse>(EditorErrors.DesignVersionNotFound);
        }

        using var document = JsonDocument.Parse(version.Document);
        return Result.Success(new DesignVersionResponse(version.Number, version.SchemaVersion, document.RootElement.Clone()));
    }
}

/// <summary>Administrative surface: publish the site's design.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/design-tokens/publish")]
public sealed class PublishDesignController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishAsync(Guid siteId, PublishDesignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new PublishDesignCommand(siteId, request.Version), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: roll the site's design back to a retained version.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/design-tokens/rollback")]
public sealed class RollbackDesignController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RollbackAsync(Guid siteId, RollbackDesignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new RollbackDesignCommand(siteId, request.VersionNumber), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: the design's retained versions.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/design-tokens/versions")]
public sealed class ListDesignVersionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(EditorCapabilities.DesignTokensView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListDesignVersionsQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpGet("{number:int}")]
    [RequireCapability(EditorCapabilities.DesignTokensView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, int number, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetDesignVersionQuery(siteId, number), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
