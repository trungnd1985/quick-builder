using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>One routable endpoint, with a concrete path to call it on.</summary>
public sealed record CallableEndpoint(string Method, string Path, string Pattern)
{
    public override string ToString() => $"{Method} {Pattern}";
}

/// <summary>
/// Enumerates the application's real endpoints (002 research R-06).
/// </summary>
/// <remarks>
/// Tests that make a claim about "every endpoint" read the list from the running
/// application rather than from a hand-written list, so a newly added endpoint is covered
/// without anyone remembering to add it.
/// </remarks>
internal static class EndpointCatalog
{
    /// <summary>Every endpoint whose path starts with <paramref name="prefix"/>.</summary>
    public static IReadOnlyList<CallableEndpoint> Under(WebApplicationFactory<Program> app, string prefix)
    {
        ArgumentNullException.ThrowIfNull(app);

        var sources = app.Services.GetServices<EndpointDataSource>();

        return [.. sources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new CallableEndpoint(method, ConcretePath(e), e.RoutePattern.RawText ?? string.Empty)))
            .Distinct()
            .OrderBy(e => e.Pattern, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal)];
    }

    /// <summary>Fills each route parameter with a plausible value.</summary>
    private static string ConcretePath(RouteEndpoint endpoint)
    {
        var segments = endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter =>
                parameter.ParameterPolicies.Any(p => p.Content == "guid") || parameter.Name.EndsWith("Id", StringComparison.Ordinal)
                    ? Guid.NewGuid().ToString()
                    : "value",
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternSeparatorPart separator => separator.Content,
            _ => string.Empty,
        })));

        return "/" + string.Join('/', segments);
    }

    /// <summary>Sends a request to <paramref name="endpoint"/> with an empty JSON body when it takes one.</summary>
    public static Task<HttpResponseMessage> CallAsync(HttpClient client, CallableEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);

        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), new Uri(endpoint.Path, UriKind.Relative));
        if (endpoint.Method is not ("GET" or "HEAD" or "DELETE"))
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }
}
