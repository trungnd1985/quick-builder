using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// A file is listed and served only once its bytes are completely and durably stored;
/// a failed or interrupted upload leaves nothing (003 FR-011, US1 scenario 6, SC-006).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UploadConsistencyTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;
    private FaultyStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _scenario = await MediaScenario.CreateAsync(
            database,
            "upload-consistency",
            services => services.AddSingleton<IFileStorage>(sp =>
                new FaultyStorage(sp.GetRequiredService<LocalFileStorage>()))).ConfigureAwait(false);

        _storage = (FaultyStorage)_scenario.App.Services.GetRequiredService<IFileStorage>();
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Storage_failing_mid_write_refuses_the_upload_and_leaves_nothing()
    {
        _storage.FailWrite = (1_000, new IOException("Simulated storage failure."));

        var response = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.OfSize(SampleFiles.Png, 50_000), "x.png", "image/png").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("media.storage_unavailable", await ErrorCodeAsync(response).ConfigureAwait(true));
        await AssertNothingStoredAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task A_full_disk_is_reported_as_such_and_leaves_nothing()
    {
        _storage.FailWrite = (1_000, FaultyStorage.DiskFull());

        var response = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.OfSize(SampleFiles.Png, 50_000), "x.png", "image/png").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
        Assert.Equal("media.storage_full", await ErrorCodeAsync(response).ConfigureAwait(true));
        await AssertNothingStoredAsync().ConfigureAwait(true);
    }

    /// <summary>US1 scenario 6: the connection drops part-way through the body.</summary>
    [Fact]
    public async Task A_dropped_connection_leaves_nothing_listed_or_stored()
    {
        // The body sends 20 KB and then stalls; the client gives up, which is what a dropped
        // connection looks like to the server: the request is aborted mid-body.
        using var giveUp = new CancellationTokenSource();
        var body = new StreamContent(new StallingStream(SampleFiles.OfSize(SampleFiles.Png, 20_000), onStall: () => giveUp.CancelAfter(200)));
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(MediaApi.FilesPath(_scenario.SiteA), UriKind.Relative)) { Content = body };
        request.Headers.Add("X-QuickBuilder-File-Name", "x.png");
        request.Headers.TransferEncodingChunked = true;
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scenario.Admin.SendAsync(request, giveUp.Token)).ConfigureAwait(true);

        // The server notices the abort asynchronously; give it a moment to unwind.
        await WaitUntilAsync(() => StagedFiles().Length == 0).ConfigureAwait(true);
        await AssertNothingStoredAsync().ConfigureAwait(true);

        var page = await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        Assert.Empty(page.Items);
    }

    /// <summary>The bytes are written, then the insert fails: the handler removes the bytes.</summary>
    [Fact]
    public async Task A_failed_insert_after_the_write_removes_the_stored_bytes()
    {
        await ExecuteSqlAsync("ALTER TABLE media.media_files ADD CONSTRAINT test_reject_inserts CHECK (false) NOT VALID").ConfigureAwait(true);

        try
        {
            var response = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Png, "x.png", "image/png").ConfigureAwait(true);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await AssertNothingStoredAsync().ConfigureAwait(true);
        }
        finally
        {
            await ExecuteSqlAsync("ALTER TABLE media.media_files DROP CONSTRAINT test_reject_inserts").ConfigureAwait(true);
        }
    }

    private async Task AssertNothingStoredAsync()
    {
        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
        Assert.Empty(_scenario.StoredObjects());
    }

    private string[] StagedFiles() =>
        Directory.GetFiles(_scenario.App.StoragePath, "*.part", SearchOption.AllDirectories);

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
#pragma warning disable EF1002 // Test-only DDL with no user input.
        await context.Database.ExecuteSqlRawAsync(sql).ConfigureAwait(false);
#pragma warning restore EF1002
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return body.RootElement.GetProperty("errorCode").GetString();
    }

    /// <summary>Hands out its content, then never produces another byte until cancelled.</summary>
    private sealed class StallingStream(byte[] content, Action onStall) : MemoryStream(content)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                return read;
            }

            onStall();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override bool CanSeek => false;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
    }
}
