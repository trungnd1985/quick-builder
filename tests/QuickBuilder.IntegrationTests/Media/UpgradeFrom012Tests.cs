using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Upgrading from 012 keeps every file, address and page, and the background work after it
/// fills in what 013 adds (013 SC-012, FR-044, FR-045; constitution Principle III).
/// </summary>
/// <remarks>
/// The 012 installation is built by the application itself — files uploaded, pages drafted
/// and published with images — and then the Media and Pages schemas are migrated back to
/// their 012 migrations, which removes everything 013 recorded. Starting the current
/// application against that database is the upgrade.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom012Tests(DatabaseFixture database) : IAsyncLifetime
{
    private const string MediaAt012 = "20260925134808_InitialMedia";
    private const string PagesAt012 = "20260929141546_AddPublishing";
    private const string Host = "upgrade-012.test";

    private readonly string _storage = QuickBuilderApp.CreateTemporaryStorage();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        QuickBuilderApp.TryDeleteDirectory(_storage);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Files_addresses_and_pages_survive_and_the_backfills_complete_the_library()
    {
        var connection = await database.CreateDatabaseAsync("upgrade-012");
        var (siteId, photo, pdf, photoBytes, pdfBytes) = await Build012Async(connection);

        using var app = new QuickBuilderApp(connection, storagePath: _storage);
        _ = app.Services;
        var administrator = await IdentitySeed.SeedUserAsync(app.Services, administrator: true, displayName: "After upgrade");
        using var admin = await IdentitySeed.CreateSignedInClientAsync(app, administrator.Id);

        // Both migrations applied.
        Assert.Equal(1, await CountAsync(app, "SELECT count(*) FROM media.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddLibraryAndVariants'"));
        Assert.Equal(1, await CountAsync(app, "SELECT count(*) FROM pages.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddMediaReferences'"));

        // Every file is at the top level, without alternative text, at version 1, and served
        // byte for byte at its 003 address.
        var rows = await RowsAsync(app);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal((null, null, 1), (r.FolderId, r.AltText, r.ContentVersion)));
        Assert.Equal(MediaMetadataState.Pending, rows.Single(r => r.Id == photo).MetadataState);
        Assert.All(rows, r => Assert.Equal(SearchTextState.Provisional, r.SearchTextState));

        Assert.Equal(photoBytes, await BytesAsync(app, $"/_media/{photo:N}.jpg"));
        Assert.Equal(pdfBytes, await BytesAsync(app, $"/_media/{pdf:N}.pdf"));

        // Usage matches the documents: drafted on two pages, live on one.
        var usage = (await UsageScenario.UsageAsync(admin, siteId, photo)).GetProperty("summary");
        Assert.Equal((2, 2, 1), (usage.GetProperty("places").GetInt32(), usage.GetProperty("draftPlaces").GetInt32(), usage.GetProperty("livePlaces").GetInt32()));

        // Accent-folded search after the search-text backfill.
        Assert.Equal(2, await app.Services.GetRequiredService<SearchTextBackfillService>().RunOnceAsync(CancellationToken.None));
        var found = await LibraryApi.ListOkAsync(admin, siteId, "?q=ete");
        Assert.Equal([pdf], found.Items.Select(f => f.Id));

        // Dimensions, thumbnails and responsive markup after the metadata backfill.
        Assert.Equal(1, await app.Services.GetRequiredService<ImageMetadataBackfillService>().RunOnceAsync(CancellationToken.None));
        var queue = app.Services.GetRequiredService<ThumbnailQueue>();
        Assert.True(queue.Reader.TryRead(out var thumbnail));
        await app.Services.GetRequiredService<ThumbnailWorker>().ProduceAsync(thumbnail, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(_storage, MediaStorageKeys.Variant(siteId, photo, 1, 320).Value.Replace('/', Path.DirectorySeparatorChar))));

        await DispatchAsync(app);
        var page = await (await MediaApi.PublicAsync(app, Host, "/gallery")).Content.ReadAsStringAsync();
        Assert.Contains("width=\"800\"", page, StringComparison.Ordinal);
        Assert.Contains("height=\"1200\"", page, StringComparison.Ordinal);
        Assert.Contains("srcset=", page, StringComparison.Ordinal);

        // FR-044: what a future media manifest needs is present for every file.
        foreach (var file in (await LibraryApi.ListOkAsync(admin, siteId)).Items)
        {
            var json = JsonSerializer.SerializeToElement(file, MediaApi.Json);
            Assert.True(json.TryGetProperty("folderId", out _));
            Assert.True(json.TryGetProperty("altText", out _));
            Assert.Equal(1, json.GetProperty("contentVersion").GetInt32());
        }
    }

    /// <summary>
    /// A 012 installation: two files (a photo and <c>Été.pdf</c>), the photo drafted on two
    /// pages and published on one; then the 013 schema removed.
    /// </summary>
    private async Task<(Guid Site, Guid Photo, Guid Pdf, byte[] PhotoBytes, byte[] PdfBytes)> Build012Async(string connection)
    {
        Guid siteId;
        Guid photo;
        Guid pdf;
        using (var before = new QuickBuilderApp(connection, storagePath: _storage))
        {
            var admin = await before.CreateAdminClientAsync();
            siteId = await MediaApi.CreateSiteAsync(admin, "Upgraded", Host);
            photo = (await LibraryApi.UploadNamedAsync(admin, siteId, "photo.jpg", ImageFixtures.RotatedPhoto, "image/jpeg")).Id;
            pdf = (await LibraryApi.UploadNamedAsync(admin, siteId, "Été.pdf", SampleFiles.Pdf, "application/pdf")).Id;

            await UsageScenario.PageShowingAsync(admin, siteId, "gallery", photo, publish: true);
            await UsageScenario.PageShowingAsync(admin, siteId, "drafting", photo, publish: false);
        }

        await using (var media = new MediaDbContext(
            new DbContextOptionsBuilder<MediaDbContext>()
                .UseNpgsql(connection, n => n.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await media.GetService<IMigrator>().MigrateAsync(MediaAt012);
        }

        await using (var pages = new PagesDbContext(
            new DbContextOptionsBuilder<PagesDbContext>()
                .UseNpgsql(connection, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PagesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesAt012);
        }

        // 012 never produced variants: start from originals only.
        foreach (var variants in Directory.EnumerateDirectories(_storage, MediaStorageKeys.VariantSegment, SearchOption.AllDirectories).ToList())
        {
            Directory.Delete(variants, recursive: true);
        }

        return (siteId, photo, pdf, ImageFixtures.RotatedPhoto, SampleFiles.Pdf);
    }

    private static async Task<List<MediaFile>> RowsAsync(QuickBuilderApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaDbContext>().MediaFiles
            .AcrossAllSites("Test inspection of the upgraded library.")
            .AsNoTracking()
            .ToListAsync();
    }

    private static async Task<byte[]> BytesAsync(QuickBuilderApp app, string path)
    {
        var response = await MediaApi.PublicAsync(app, Host, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<long> CountAsync(QuickBuilderApp app, string sql)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<MediaDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task DispatchAsync(QuickBuilderApp app)
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(app.Services);
        var settings = app.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }
    }

    private sealed class NoSite : ISiteContext
    {
        public static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
