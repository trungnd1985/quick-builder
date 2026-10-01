using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>Pages that use media files, and the usage endpoint, for 013 US5's tests.</summary>
internal static class UsageScenario
{
    public static string UsagePath(Guid siteId, Guid fileId) => $"{LibraryApi.FilePath(siteId, fileId)}/usage";

    public static async Task<JsonElement> UsageAsync(HttpClient client, Guid siteId, Guid fileId)
    {
        var response = await client.GetAsync(new Uri(UsagePath(siteId, fileId), UriKind.Relative)).ConfigureAwait(false);
        return await LibraryApi.OkAsync<JsonElement>(response).ConfigureAwait(false);
    }

    /// <summary>Creates a page whose draft shows <paramref name="file"/>, optionally publishing it.</summary>
    public static async Task<Guid> PageShowingAsync(HttpClient admin, Guid siteId, string slug, Guid file, bool publish)
    {
        var page = (await PagesApi.SeedTreeAsync(admin, siteId, slug).ConfigureAwait(false))[$"/{slug}"];
        var path = PagesApi.PagePath(siteId, page);
        await PublishingApi.SaveAsync(admin, path, MediaUsageReaderTests.Image(file)).ConfigureAwait(false);
        if (publish)
        {
            await PublishingApi.PublishCurrentAsync(admin, path).ConfigureAwait(false);
        }

        return page;
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid fileId, bool confirmInUse) =>
        client.DeleteAsync(new Uri(LibraryApi.FilePath(siteId, fileId) + (confirmInUse ? "?confirmInUse=true" : string.Empty), UriKind.Relative));

    public static async Task<MediaFileDto> FileAsync(HttpClient client, Guid siteId, Guid fileId) =>
        (await (await LibraryApi.GetFileAsync(client, siteId, fileId).ConfigureAwait(false))
            .Content.ReadFromJsonAsync<MediaFileDto>(MediaApi.Json).ConfigureAwait(false))!;

    /// <summary>
    /// Replaces the access evaluator with one that never grants <paramref name="withheld"/>: a
    /// member who may see the library but not page content, which no built-in role is.
    /// </summary>
    public static void Withhold(IServiceCollection services, string withheld)
    {
        var existing = services.Last(d => d.ServiceType == typeof(IAccessEvaluator));
        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(
            typeof(IAccessEvaluator),
            sp => new Withholding(Create(sp, existing), withheld),
            existing.Lifetime));
    }

    private static IAccessEvaluator Create(IServiceProvider services, ServiceDescriptor descriptor) =>
        (IAccessEvaluator)(descriptor.ImplementationFactory?.Invoke(services)
            ?? descriptor.ImplementationInstance
            ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!));

    private sealed class Withholding(IAccessEvaluator inner, string withheld) : IAccessEvaluator
    {
        public bool IsSignedIn => inner.IsSignedIn;

        public Guid? UserId => inner.UserId;

        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => inner.IsAdministratorAsync(cancellationToken);

        public Task<bool> HasCapabilityAsync(string name, Guid? siteId, CancellationToken cancellationToken = default) =>
            name == withheld ? Task.FromResult(false) : inner.HasCapabilityAsync(name, siteId, cancellationToken);

        public Task<SiteVisibility> GetVisibleSitesAsync(CancellationToken cancellationToken = default) => inner.GetVisibleSitesAsync(cancellationToken);
    }
}
