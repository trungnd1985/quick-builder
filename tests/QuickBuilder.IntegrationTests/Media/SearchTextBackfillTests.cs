using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Files from before 013 get accent-folded search text after start-up, one site at a time
/// (013 research MV-26, analysis H1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SearchTextBackfillTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "search-text-backfill").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private SearchTextBackfillService Backfill => _scenario.App.Services.GetRequiredService<SearchTextBackfillService>();

    [Fact]
    public async Task Provisional_rows_on_every_site_are_normalised_and_a_second_pass_changes_nothing()
    {
        var a = await InsertProvisionalAsync(_scenario.SiteA, "Été brochure.pdf").ConfigureAwait(true);
        var b = await InsertProvisionalAsync(_scenario.SiteB, "Nguyễn.jpg").ConfigureAwait(true);

        // Two passes at once, as two instances would run them.
        var counts = await Task.WhenAll(
            Backfill.RunOnceAsync(CancellationToken.None),
            Backfill.RunOnceAsync(CancellationToken.None)).ConfigureAwait(true);

        var rows = (await _scenario.RowsAsync().ConfigureAwait(true)).ToDictionary(r => r.Id);
        Assert.Equal("ete brochure.pdf", rows[a].SearchText);
        Assert.Equal("nguyen.jpg", rows[b].SearchText);
        Assert.All(rows.Values, r => Assert.Equal(SearchTextState.Normalised, r.SearchTextState));
        Assert.InRange(counts.Sum(), 2, 4);

        Assert.Equal(0, await Backfill.RunOnceAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>A row as the 013 migration leaves a pre-013 file: lower-cased, accents intact.</summary>
    private async Task<Guid> InsertProvisionalAsync(Guid siteId, string name)
    {
        var id = Guid.NewGuid();

        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        db.MediaFiles.Add(MediaFile.Create(
            id, siteId, name, MediaFileType.Pdf, "pdf", 10, DateTimeOffset.UtcNow, Guid.NewGuid(), $"{siteId:N}/media/{id:N}"));
        await db.SaveChangesAsync().ConfigureAwait(false);

        await db.MediaFiles
            .AcrossAllSites("Test sets up a row as the migration leaves it.")
            .Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.SearchText, name.ToLowerInvariant())
                .SetProperty(f => f.SearchTextState, SearchTextState.Provisional))
            .ConfigureAwait(false);

        return id;
    }
}
