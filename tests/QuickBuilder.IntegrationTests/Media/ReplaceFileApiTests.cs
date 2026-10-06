using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 6: replacing a file's contents keeps its identity and everything that points to
/// it, moves its address, and never serves old bytes at the new one (013 FR-017 – FR-020,
/// SC-009, SC-015, research MV-07).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReplaceFileApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly ManualClock _clock = new();
    private MediaScenario _scenario = null!;
    private FaultyStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _scenario = await MediaScenario.CreateAsync(
            database,
            "replace-file",
            services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.AddSingleton<IFileStorage>(sp => new FaultyStorage(sp.GetRequiredService<LocalFileStorage>()));
            }).ConfigureAwait(false);
        _storage = (FaultyStorage)_scenario.App.Services.GetRequiredService<IFileStorage>();
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task A_replace_keeps_the_identity_and_records_the_new_version()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(_scenario.Admin, Site, "Logos");
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");
        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.MoveAsync(_scenario.Admin, Site, [original.Id], folder.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LibraryApi.SetAltTextAsync(_scenario.Admin, Site, original.Id, "Company logo")).StatusCode);
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "about", original.Id, publish: false);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var replaced = await ReplaceOkAsync(_scenario.Admin, original, "new-logo.png", ImageFixtures.LogoV2);

        Assert.Equal(original.Id, replaced.Id);
        Assert.Equal("logo.png", replaced.FileName);
        Assert.Equal(folder.Id, replaced.FolderId);
        Assert.Equal("Company logo", replaced.AltText);
        Assert.Equal(original.UploadedBy.Id, replaced.UploadedBy.Id);
        Assert.Equal(original.UploadedAtUtc, replaced.UploadedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, replaced.Usage.DraftPlaces);

        Assert.Equal(2, replaced.ContentVersion);
        Assert.NotNull(replaced.ReplacedBy);
        // The database keeps microseconds.
        Assert.Equal(_clock.GetUtcNow(), replaced.ReplacedAtUtc!.Value, TimeSpan.FromMilliseconds(1));
        Assert.EndsWith($"/_media/{original.Id:N}-v2.png", replaced.PublicPath, StringComparison.Ordinal);
        Assert.Equal((900, 300), (replaced.Width, replaced.Height));
        Assert.Equal("recorded", replaced.MetadataState);
    }

    [Fact]
    public async Task Old_addresses_are_gone_at_once_and_the_new_one_never_serves_old_bytes()
    {
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");
        var oldVariant = $"/_media/{original.Id:N}-v1-w320.png";
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, oldVariant)).StatusCode);

        var replaced = await ReplaceOkAsync(_scenario.Admin, original, "v2.png", ImageFixtures.LogoV2);

        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, original.PublicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, oldVariant)).StatusCode);

        for (var i = 0; i < 5; i++)
        {
            var current = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, replaced.PublicPath);
            Assert.Equal(ImageFixtures.LogoV2, await current.Content.ReadAsByteArrayAsync());
        }

        Assert.False(File.Exists(_scenario.ObjectPath(Site, original.Id)), "The previous version's bytes are removed after the commit.");
    }

    [Fact]
    public async Task Old_bytes_that_could_not_be_removed_are_swept_later_and_never_served()
    {
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");

        _storage.FailDelete = new IOException("Simulated storage failure.");
        await ReplaceOkAsync(_scenario.Admin, original, "v2.png", ImageFixtures.LogoV2);
        _storage.FailDelete = null;

        var leftover = _scenario.ObjectPath(Site, original.Id);
        Assert.True(File.Exists(leftover));
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, original.PublicPath)).StatusCode);

        _clock.Advance(TimeSpan.FromDays(2));
        File.SetLastWriteTimeUtc(leftover, (_clock.GetUtcNow() - TimeSpan.FromDays(2)).UtcDateTime);
        await _scenario.App.Services.GetRequiredService<MediaOrphanSweepService>().SweepAsync(CancellationToken.None);

        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public async Task A_live_page_shows_the_new_contents_at_its_next_request_and_the_thumbnail_is_queued()
    {
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "brand", original.Id, publish: true);
        Assert.Contains($"{original.Id:N}.png", await PageAsync("/brand"), StringComparison.Ordinal);
        Drain();

        await ReplaceOkAsync(_scenario.Admin, original, "v2.png", ImageFixtures.LogoV2);
        await DispatchAsync();

        var html = await PageAsync("/brand");
        Assert.Contains($"{original.Id:N}-v2", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"{original.Id:N}.png", html, StringComparison.Ordinal);

        Assert.True(_scenario.App.Services.GetRequiredService<ThumbnailQueue>().Reader.TryRead(out var request));
        Assert.Equal((original.Id, 2), (request.FileId, request.ContentVersion));
    }

    public static TheoryData<string, string, string, HttpStatusCode> Refusals => new()
    {
        { "pdf", "doc.pdf", "media.kind_mismatch", HttpStatusCode.BadRequest },
        { "huge", "huge.png", "media.dimensions_too_large", HttpStatusCode.BadRequest },
        { "empty", "empty.png", "media.empty", HttpStatusCode.BadRequest },
        { "text", "notes.txt", "media.unsupported_type", HttpStatusCode.UnsupportedMediaType },
        { "mismatch", "logo.jpg", "media.type_mismatch", HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refused_replacement_changes_nothing(string body, string name, string code, HttpStatusCode status)
    {
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");
        var bytes = body switch
        {
            "pdf" => SampleFiles.Pdf,
            "huge" => ImageFixtures.HugePixels,
            "empty" => [],
            "text" => "just some text"u8.ToArray(),
            _ => ImageFixtures.LogoV2,
        };

        var response = await ReplaceAsync(_scenario.Admin, Site, original.Id, original.ContentVersion, name, bytes);

        Assert.Equal(status, response.StatusCode);
        await LibraryApi.RefusedAsync(response, status, code);
        await AssertUnchangedAsync(original);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict()
    {
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");
        await ReplaceOkAsync(_scenario.Admin, original, "v2.png", ImageFixtures.LogoV2);

        var stale = await ReplaceAsync(_scenario.Admin, Site, original.Id, 1, "v3.png", ImageFixtures.Logo);

        await LibraryApi.RefusedAsync(stale, HttpStatusCode.Conflict, "media.conflict");
        Assert.Equal(2, (await UsageScenario.FileAsync(_scenario.Admin, Site, original.Id)).ContentVersion);
    }

    [Fact]
    public async Task A_replacement_that_fails_while_storing_leaves_the_file_and_its_address_unchanged()
    {
        var original = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", ImageFixtures.Logo, "image/png");

        _storage.FailWrite = (1_000, new IOException("Simulated failure part-way through."));
        var failed = await ReplaceAsync(_scenario.Admin, Site, original.Id, 1, "v2.png", ImageFixtures.LogoV2);
        _storage.FailWrite = null;

        await LibraryApi.RefusedAsync(failed, HttpStatusCode.ServiceUnavailable, "media.storage_unavailable");
        await AssertUnchangedAsync(original);
        Assert.False(File.Exists(Path.Combine(_scenario.App.StoragePath, MediaStorageKeys.For(Site, original.Id, 2).Value.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task An_editor_may_replace_their_own_file_unless_a_live_page_shows_it()
    {
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor));
        var unused = await LibraryApi.UploadNamedAsync(editor, Site, "mine.png", ImageFixtures.Logo, "image/png");
        var live = await LibraryApi.UploadNamedAsync(editor, Site, "shown.png", ImageFixtures.Logo, "image/png");
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "shown", live.Id, publish: true);

        Assert.Equal(2, (await ReplaceOkAsync(editor, unused, "v2.png", ImageFixtures.LogoV2)).ContentVersion);

        Assert.False((await UsageScenario.FileAsync(editor, Site, live.Id)).CanReplace);
        await LibraryApi.RefusedAsync(
            await ReplaceAsync(editor, Site, live.Id, 1, "v2.png", ImageFixtures.LogoV2),
            HttpStatusCode.Forbidden,
            "media.in_live_use");
        await AssertUnchangedAsync(live);

        // A Designer may.
        var (designer, _) = await _scenario.UserAsync((Site, SystemRole.Designer));
        Assert.Equal(2, (await ReplaceOkAsync(designer, live, "v2.png", ImageFixtures.LogoV2)).ContentVersion);
    }

    [Fact]
    public async Task Another_persons_file_needs_the_delete_any_capability()
    {
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor));
        var theirs = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "admins.png", ImageFixtures.Logo, "image/png");

        await LibraryApi.RefusedAsync(
            await ReplaceAsync(editor, Site, theirs.Id, 1, "v2.png", ImageFixtures.LogoV2),
            HttpStatusCode.Forbidden,
            "identity.forbidden");
        await AssertUnchangedAsync(theirs);
    }

    private async Task AssertUnchangedAsync(MediaFileDto original)
    {
        var now = await UsageScenario.FileAsync(_scenario.Admin, Site, original.Id);
        Assert.Equal(1, now.ContentVersion);
        Assert.Null(now.ReplacedAtUtc);
        Assert.Equal(original.PublicPath, now.PublicPath);

        var served = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, original.PublicPath);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    private static async Task<MediaFileDto> ReplaceOkAsync(HttpClient client, MediaFileDto file, string name, byte[] bytes)
    {
        var response = await ReplaceAsync(client, file.SiteId, file.Id, file.ContentVersion, name, bytes);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MediaFileDto>(MediaApi.Json))!;
    }

    internal static Task<HttpResponseMessage> ReplaceAsync(HttpClient client, Guid siteId, Guid fileId, int expectedVersion, string name, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"{LibraryApi.FilePath(siteId, fileId)}/content?expectedContentVersion={expectedVersion}", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(name.EndsWith(".pdf", StringComparison.Ordinal) ? "application/pdf" : "application/octet-stream");
        request.Headers.Add("X-QuickBuilder-File-Name", name);
        return client.SendAsync(request);
    }

    private async Task<string> PageAsync(string path) =>
        await (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, path)).Content.ReadAsStringAsync();

    private void Drain()
    {
        var queue = _scenario.App.Services.GetRequiredService<ThumbnailQueue>();
        while (queue.Reader.TryRead(out _))
        {
        }
    }

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }
    }
}
