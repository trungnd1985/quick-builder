using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Features.ListLanguages;

/// <summary>The supported languages and the default (009 research S-09).</summary>
public sealed record ListLanguagesResponse(IReadOnlyList<string> Languages, string Default);

internal sealed record ListLanguagesQuery : IQuery<ListLanguagesResponse>;

internal sealed class ListLanguagesHandler : IQueryHandler<ListLanguagesQuery, ListLanguagesResponse>
{
    public Task<ListLanguagesResponse> HandleAsync(ListLanguagesQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new ListLanguagesResponse(SupportedLanguages.All, SupportedLanguages.Default));
}

/// <summary>
/// Administrative surface. Tags only: the admin shell renders their names in its own
/// language with <c>Intl.DisplayNames</c>.
/// </summary>
[ApiController]
[Route("api/v1/sites/languages")]
public sealed class ListLanguagesController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists the languages a site may choose as its default.</summary>
    [HttpGet]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await dispatcher.QueryAsync(new ListLanguagesQuery(), cancellationToken).ConfigureAwait(false));
}
