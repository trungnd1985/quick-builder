using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// The <c>AddMediaReferences</c> migration's SQL backfill finds exactly what
/// <see cref="DependencyExtractor"/> finds, so upgraded usage matches what later saves record
/// (013 research MV-05, FR-013; analysis L1).
/// </summary>
/// <remarks>
/// A fresh database is migrated to the migration before <c>AddMediaReferences</c>, drafts are
/// written straight into it (bypassing validation, so edge cases can be stored), and then the
/// remaining migrations are applied, which runs the backfill.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MediaReferenceBackfillTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string BeforeMediaReferences = "20260929141546_AddPublishing";

    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "media-ref-backfill").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_migration_records_what_the_extractor_reads_for_every_document()
    {
        var documents = ValidExamples().Append((Name: "edge-cases", Document: EdgeCases)).ToList();
        var outline = string.Join('\n', documents.Select((_, i) => $"doc-{i}"));
        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, outline).ConfigureAwait(true);
        var pageIds = documents.Select((_, i) => pages[$"/doc-{i}"]).ToList();

        await MigrateAsync(BeforeMediaReferences).ConfigureAwait(true);

        await using (var scope = _scenario.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
            for (var i = 0; i < documents.Count; i++)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE pages.page_drafts SET document = {0}::jsonb WHERE page_id = {1}",
                    documents[i].Document,
                    pageIds[i]).ConfigureAwait(true);
            }
        }

        await MigrateAsync(target: null).ConfigureAwait(true);

        var recorded = await _scenario.ReadAsync(db => PagesScenario.Everything<DraftMediaReference>(db)
            .Where(r => r.SiteId == _scenario.SiteA)
            .ToListAsync()).ConfigureAwait(true);

        var sawAnImage = false;
        for (var i = 0; i < documents.Count; i++)
        {
            var expected = DependencyExtractor.Media(documents[i].Document).Order().ToList();
            var actual = recorded.Where(r => r.SubjectId == pageIds[i]).Select(r => r.MediaId).Order().ToList();

            Assert.True(expected.SequenceEqual(actual), $"{documents[i].Name}: extractor [{string.Join(", ", expected)}], migration [{string.Join(", ", actual)}]");
            sawAnImage |= expected.Count > 0;
        }

        Assert.True(sawAnImage, "At least one document must show a file, or this test proves nothing.");
        Assert.Equal(2, recorded.Count(r => r.SubjectId == pageIds[^1]));
    }

    private async Task MigrateAsync(string? target)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        await db.Database.GetService<IMigrator>().MigrateAsync(target).ConfigureAwait(false);
    }

    private static IEnumerable<(string Name, string Document)> ValidExamples()
    {
        var directory = Path.Combine(RepositoryRoot(), "docs", "contracts", "page-schema", "examples", "valid");
        return Directory.EnumerateFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(file => (Path.GetFileName(file), File.ReadAllText(file)));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    /// Deep nesting, an image with no media, a media id that is not a UUID, "media" on a node
    /// that is not an image, a number where the id should be, and one file shown twice.
    /// Exactly two distinct files are images' media.
    /// </summary>
    private const string EdgeCases =
        """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"s1","type":"section","props":{},"children":[
            {"id":"c1","type":"container","props":{},"children":[
              {"id":"c2","type":"container","props":{},"children":[
                {"id":"i1","type":"image","props":{"media":{"mediaId":"6f1c2c4e-0a5b-4f7e-9d3c-111111111111"}},"children":[]},
                {"id":"i2","type":"image","props":{"media":{"mediaId":"6F1C2C4E-0A5B-4F7E-9D3C-222222222222"}},"children":[]},
                {"id":"i3","type":"image","props":{"media":{"mediaId":"6f1c2c4e-0a5b-4f7e-9d3c-111111111111"}},"children":[]}
              ]}
            ]},
            {"id":"i4","type":"image","props":{"alt":"no media yet"},"children":[]},
            {"id":"i5","type":"image","props":{"media":{"mediaId":"not-a-uuid"}},"children":[]},
            {"id":"i6","type":"image","props":{"media":{"mediaId":42}},"children":[]},
            {"id":"t1","type":"text","props":{"text":"x","media":{"mediaId":"6f1c2c4e-0a5b-4f7e-9d3c-333333333333"}},"children":[]}
          ]}
        ]}}
        """;
}
