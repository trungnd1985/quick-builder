using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Every archive an earlier release wrote still imports and renders (020 SC-008, FR-038;
/// Principle III). Each release adds the archive of the seeded site to
/// <c>tests/fixtures/archives/</c> (see its README).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ArchiveFromPreviousReleaseTests(DatabaseFixture database)
{
    /// <summary>Set to <c>1</c> to write this release's archive into the fixtures folder (T068).</summary>
    private const string WriteVariable = "QB_WRITE_ARCHIVE_FIXTURE";

    public static TheoryData<string> Archives()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(FixturesDirectory(), "*.qbsite").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public async Task An_archive_of_an_earlier_release_imports_and_its_pages_render(string file)
    {
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync("archive-release").ConfigureAwait(true));
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
        var archive = await File.ReadAllBytesAsync(Path.Combine(FixturesDirectory(), file)).ConfigureAwait(true);

        var done = await TransferApi.ImportAsNewSiteAsync(app, admin, archive, "From " + file).ConfigureAwait(true);

        Assert.Equal(0, done.GetProperty("report").GetProperty("broken").GetArrayLength());
        var visitor = app.CreateClientForHost("cms-a.test");
        var about = await visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, about.StatusCode);
        var html = await about.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Contains("About Acme", html, StringComparison.Ordinal);

        // 066 FR-022: an archive from before corner radii imports without any, and its pages get
        // the default radii, as every set saved before them does.
        var tokens = await QuickBuilder.IntegrationTests.Editor.EditorApi.GetOkAsync(admin, done.GetProperty("siteId").GetGuid()).ConfigureAwait(true);
        Assert.False(tokens.GetProperty("document").TryGetProperty("radii", out _) && !tokens.GetProperty("isDefault").GetBoolean());
        Assert.Contains("--qb-radius-radius-md:", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task This_release_writes_its_archive_fixture_when_asked()
    {
        if (Environment.GetEnvironmentVariable(WriteVariable) != "1")
        {
            return;
        }

        using var site = await FullSite.CreateAsync(database, "archive-fixture").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        var version = site.App.Services.GetRequiredService<ApplicationVersion>().Version;
        await File.WriteAllBytesAsync(Path.Combine(FixturesDirectory(), $"{version.Major}.{version.Minor}.qbsite"), archive).ConfigureAwait(true);
    }

    private static string FixturesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "archives");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/fixtures/archives was not found above the test output.");
    }
}
