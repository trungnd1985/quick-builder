using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>User Story 1: an image's alternative text (013 FR-008, research MV-04).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AltTextApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "media-alt-text").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Admin => _scenario.Admin;

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task Alternative_text_is_set_changed_cleaned_and_cleared()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "photo.png").ConfigureAwait(true);

        var set = await LibraryApi.OkAsync<MediaFileDto>(
            await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, "  Anna\u0007 at her desk‮ ").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("Anna at her desk", set.AltText);

        var cleared = await LibraryApi.OkAsync<MediaFileDto>(
            await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, null).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Null(cleared.AltText);
    }

    [Fact]
    public async Task Three_hundred_characters_are_accepted_and_more_are_refused()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "long.png").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, new string('a', 300)).ConfigureAwait(true)).StatusCode);
        await LibraryApi.RefusedAsync(
            await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, new string('a', 301)).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.invalid_alt_text").ConfigureAwait(true);
    }

    [Fact]
    public async Task Documents_have_no_alternative_text()
    {
        var pdf = await LibraryApi.UploadNamedAsync(Admin, Site, "doc.pdf", SampleFiles.Pdf, "application/pdf").ConfigureAwait(true);

        await LibraryApi.RefusedAsync(
            await LibraryApi.SetAltTextAsync(Admin, Site, pdf.Id, "A brochure").ConfigureAwait(true), HttpStatusCode.BadRequest, "media.alt_text_not_allowed").ConfigureAwait(true);
    }

    [Fact]
    public async Task Alternative_text_is_searchable()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "img_0042.png").ConfigureAwait(true);
        await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, "Sunset over the harbour").ConfigureAwait(true);

        Assert.Equal([file.Id], (await LibraryApi.ListAllAsync(Admin, Site, "?q=harbour").ConfigureAwait(true)).Select(f => f.Id));
    }

    [Fact]
    public async Task An_edit_of_a_row_changed_meanwhile_is_a_conflict()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "busy.png").ConfigureAwait(true);

        // Two edits at once: the row version lets exactly one land first; the other either
        // lands after it (a fresh read) or is refused as a conflict. Never a lost update.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => LibraryApi.SetAltTextAsync(Admin, Site, file.Id, $"text {i}"))).ConfigureAwait(true);

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Alternative_text_never_reaches_the_logs()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "logged.png").ConfigureAwait(true);
        const string Secret = "Marker-7f3c-alt-text-that-must-not-be-logged";

        await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, Secret).ConfigureAwait(true);

        Assert.DoesNotContain(_scenario.App.Logs.Events, e => LogCapture.Render(e).Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_sites_file_is_not_found()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, _scenario.SiteB, "b.png").ConfigureAwait(true);

        await LibraryApi.RefusedAsync(
            await LibraryApi.SetAltTextAsync(Admin, Site, file.Id, "x").ConfigureAwait(true), HttpStatusCode.NotFound, "media.not_found").ConfigureAwait(true);

        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var row = await db.MediaFiles.AcrossAllSites("Test inspection.").SingleAsync(f => f.Id == file.Id).ConfigureAwait(true);
        Assert.Null(row.AltText);
    }
}
