using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// SC-002: every crafted file gets exactly the outcome the spec requires, and a refusal
/// leaves nothing behind (003 FR-006 to FR-010).
/// </summary>
/// <remarks>
/// One test over the whole set rather than a theory: each theory case would get its own
/// database and application, turning a few seconds of uploads into minutes of setup. Every
/// failing case is still reported by name.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UploadValidationTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(
            database,
            "upload-validation",
            services => services.Configure<MediaOptions>(o => o.MaxFileSizeBytes = CraftedFiles.Limit)).ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Every_crafted_file_gets_its_required_outcome_and_refusals_leave_nothing()
    {
        Assert.True(CraftedFiles.All.Count >= 50, $"SC-002 asks for at least 50 crafted files; there are {CraftedFiles.All.Count}.");
        Assert.Contains(CraftedFiles.All, c => c.Accepted);
        Assert.Contains(CraftedFiles.All, c => !c.Accepted);

        var failures = new List<string>();
        foreach (var crafted in CraftedFiles.All)
        {
            var problem = await CheckAsync(crafted).ConfigureAwait(true);
            if (problem is not null)
            {
                failures.Add($"{crafted}: {problem}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {CraftedFiles.All.Count} crafted files got the wrong outcome:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>Null when the outcome is right; otherwise what went wrong.</summary>
    private async Task<string?> CheckAsync(CraftedFile crafted)
    {
        var rowsBefore = (await _scenario.RowsAsync().ConfigureAwait(false)).Count;
        var objectsBefore = _scenario.StoredObjects().Count;

        var response = await MediaApi.UploadAsync(
            _scenario.Admin, _scenario.SiteA, crafted.Bytes, crafted.FileName, crafted.ContentType, crafted.Chunked).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if ((int)response.StatusCode != crafted.Status)
        {
            return $"expected {crafted.Status}, got {(int)response.StatusCode}: {body}";
        }

        var rowsAfter = (await _scenario.RowsAsync().ConfigureAwait(false)).Count;
        var objectsAfter = _scenario.StoredObjects().Count;

        if (crafted.Accepted)
        {
            return rowsAfter == rowsBefore + 1 && objectsAfter == objectsBefore + 1
                ? null
                : $"accepted, but rows went {rowsBefore}→{rowsAfter} and objects {objectsBefore}→{objectsAfter}";
        }

        using var problem = JsonDocument.Parse(body);
        var code = problem.RootElement.GetProperty("errorCode").GetString();
        if (code != crafted.ErrorCode)
        {
            return $"expected errorCode {crafted.ErrorCode}, got {code}";
        }

        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return "the refusal is not application/problem+json";
        }

        // A refusal leaves no row and no stored object (FR-011).
        return rowsAfter == rowsBefore && objectsAfter == objectsBefore
            ? null
            : $"refused, but rows went {rowsBefore}→{rowsAfter} and objects {objectsBefore}→{objectsAfter}";
    }
}
