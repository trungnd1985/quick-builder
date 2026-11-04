using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Sites.Features.Transfer.Engine;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>Requests of 020 contracts/transfer-api.yaml, and the worker, for tests.</summary>
internal static class TransferApi
{
    public static string ExportsPath(Guid siteId) => $"/api/v1/sites/{siteId}/exports";

    public static string SiteTransfersPath(Guid siteId) => $"/api/v1/sites/{siteId}/transfers";

    public static string TransferPath(Guid transferId) => $"/api/v1/transfers/{transferId}";

    public static Task<HttpResponseMessage> StartExportAsync(HttpClient client, Guid siteId) =>
        client.PostAsync(new Uri(ExportsPath(siteId), UriKind.Relative), content: null);

    public static async Task<Guid> StartExportOkAsync(HttpClient client, Guid siteId) =>
        Id(await PagesApi.OkAsync(await StartExportAsync(client, siteId).ConfigureAwait(false), HttpStatusCode.Accepted).ConfigureAwait(false));

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid transferId) =>
        client.GetAsync(new Uri(TransferPath(transferId), UriKind.Relative));

    public static async Task<JsonElement> GetOkAsync(HttpClient client, Guid transferId) =>
        await PagesApi.OkAsync(await GetAsync(client, transferId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid transferId) =>
        client.PostAsync(new Uri($"{TransferPath(transferId)}/cancel", UriKind.Relative), content: null);

    public static Task<HttpResponseMessage> DeleteArchiveAsync(HttpClient client, Guid transferId) =>
        client.DeleteAsync(new Uri(TransferPath(transferId), UriKind.Relative));

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(SiteTransfersPath(siteId), UriKind.Relative));

    public static Task<HttpResponseMessage> DownloadAsync(HttpClient client, Guid transferId, string? range = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{TransferPath(transferId)}/archive", UriKind.Relative));
        if (range is not null)
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        return client.SendAsync(request);
    }

    /// <summary>Delivers every pending integration event, as the outbox's timer would.</summary>
    public static async Task DispatchOutboxAsync(QuickBuilderApp app)
    {
        var dispatcher = ActivatorUtilities.CreateInstance<QuickBuilder.Shared.Infrastructure.Outbox.OutboxDispatcher>(app.Services);
        var settings = app.Services.GetRequiredService<QuickBuilder.Shared.Infrastructure.Outbox.IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    /// <summary>Runs the transfer worker until nothing is claimable, as its timer would.</summary>
    public static Task<int> RunWorkerAsync(QuickBuilderApp app) =>
        app.Services.GetRequiredService<TransferWorker>().RunOnceAsync(CancellationToken.None);

    /// <summary>Exports the site through the API and the worker, and returns the archive's bytes.</summary>
    public static async Task<(Guid TransferId, byte[] Archive)> ExportAsync(QuickBuilderApp app, HttpClient admin, Guid siteId)
    {
        var id = await StartExportOkAsync(admin, siteId).ConfigureAwait(false);
        await RunWorkerAsync(app).ConfigureAwait(false);

        var transfer = await GetOkAsync(admin, id).ConfigureAwait(false);
        if (transfer.GetProperty("state").GetString() != "succeeded")
        {
            throw new InvalidOperationException($"The export ended '{transfer.GetProperty("state").GetString()}': {transfer.GetProperty("failureCode")}.");
        }

        var download = await DownloadAsync(admin, id).ConfigureAwait(false);
        download.EnsureSuccessStatusCode();
        return (id, await download.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
    }

    /// <summary>Runs an export of the site through the API and the worker, without downloading it; returns the transfer id.</summary>
    public static async Task<Guid> RunExportAsync(QuickBuilderApp app, HttpClient admin, Guid siteId)
    {
        var id = await StartExportOkAsync(admin, siteId).ConfigureAwait(false);
        await RunWorkerAsync(app).ConfigureAwait(false);

        var transfer = await GetOkAsync(admin, id).ConfigureAwait(false);
        if (transfer.GetProperty("state").GetString() != "succeeded")
        {
            throw new InvalidOperationException($"The export ended '{transfer.GetProperty("state").GetString()}': {transfer.GetProperty("failureCode")}.");
        }

        return id;
    }

    /// <summary>Exports the site and streams the archive to <paramref name="path"/>, for archives too big for one array; returns its size.</summary>
    public static async Task<long> ExportToFileAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string path)
    {
        var id = await RunExportAsync(app, admin, siteId).ConfigureAwait(false);
        return await DownloadToFileAsync(admin, id, path).ConfigureAwait(false);
    }

    /// <summary>Streams a finished export's archive to <paramref name="path"/>; returns its size.</summary>
    public static async Task<long> DownloadToFileAsync(HttpClient admin, Guid transferId, string path)
    {
        var id = transferId;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{TransferPath(id)}/archive", UriKind.Relative));
        using var download = await admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        download.EnsureSuccessStatusCode();
        var file = File.Create(path);
        await using (file.ConfigureAwait(false))
        {
            await download.Content.CopyToAsync(file).ConfigureAwait(false);
            return file.Length;
        }
    }

    /// <summary>Opens archive bytes with the import's reader, failing the test when they are refused.</summary>
    public static async Task<ZipArchiveReader> OpenAsync(byte[] archive)
    {
        var opened = await ZipArchiveReader.OpenAsync(_ => Task.FromResult<Stream>(new MemoryStream(archive, writable: false)), CancellationToken.None)
            .ConfigureAwait(false);
        if (opened.IsFailure)
        {
            throw new InvalidOperationException($"The archive was refused: {opened.Error}");
        }

        return opened.Value;
    }

    /// <summary>Every line of one part entry, as JSON.</summary>
    public static async Task<List<JsonElement>> LinesAsync(ZipArchiveReader archive, string part, string entry)
    {
        var lines = new List<JsonElement>();
        await foreach (var line in archive.ReadLinesAsync<JsonElement>(part, entry, CancellationToken.None).ConfigureAwait(false))
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Every line of authors.jsonl, as JSON.</summary>
    public static async Task<List<JsonElement>> LinesAsync(ZipArchiveReader archive, string authors)
    {
        ArgumentNullException.ThrowIfNull(archive);
        _ = authors;
        var lines = new List<JsonElement>();
        await foreach (var author in archive.ReadAuthorsAsync(CancellationToken.None).ConfigureAwait(false))
        {
            lines.Add(JsonSerializer.SerializeToElement(author, QuickBuilder.Sites.Contracts.Transfer.ArchiveJson.Options));
        }

        return lines;
    }

    /// <summary>Uploads archive bytes through the admin API; returns the transfer id.</summary>
    public static async Task<Guid> UploadAsync(HttpClient client, byte[] archive)
    {
        using var body = new ByteArrayContent(archive);
        return await UploadAsync(client, body).ConfigureAwait(false);
    }

    /// <summary>Uploads an archive body, such as a file stream, through the admin API; returns the transfer id.</summary>
    public static async Task<Guid> UploadAsync(HttpClient client, HttpContent body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var reserved = await PagesApi.OkAsync(await client.PostAsync(new Uri("/api/v1/transfers/uploads", UriKind.Relative), content: null).ConfigureAwait(false), HttpStatusCode.Created).ConfigureAwait(false);
        var id = Id(reserved);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        var put = await client.PutAsync(new Uri($"/api/v1/transfers/uploads/{id}", UriKind.Relative), body).ConfigureAwait(false);
        Assert.True(put.StatusCode == HttpStatusCode.NoContent, $"Upload answered {(int)put.StatusCode}: {await put.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        return id;
    }

    /// <summary>Checks an uploaded archive and runs the worker; returns the transfer.</summary>
    public static async Task<JsonElement> CheckAsync(QuickBuilderApp app, HttpClient client, Guid transferId, string mode, Guid? targetSiteId = null)
    {
        await PagesApi.OkAsync(await PostJsonAsync(client, $"{TransferPath(transferId)}/check", new { mode, targetSiteId }).ConfigureAwait(false), HttpStatusCode.Accepted).ConfigureAwait(false);
        await RunWorkerAsync(app).ConfigureAwait(false);
        return await GetOkAsync(client, transferId).ConfigureAwait(false);
    }

    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid transferId, object options) =>
        PostJsonAsync(client, $"{TransferPath(transferId)}/confirm", options);

    /// <summary>Uploads, checks, confirms and runs an import as a new site; returns the finished transfer.</summary>
    public static async Task<JsonElement> ImportAsNewSiteAsync(
        QuickBuilderApp app, HttpClient client, byte[] archive, string name, string? hostName = null, bool startFresh = false)
    {
        var id = await UploadAsync(client, archive).ConfigureAwait(false);
        return await ImportUploadedAsNewSiteAsync(app, client, id, name, hostName, startFresh).ConfigureAwait(false);
    }

    /// <summary>Streams an archive file up, for archives too big for one array; returns the transfer id.</summary>
    public static async Task<Guid> UploadFileAsync(HttpClient client, string path)
    {
        var file = File.OpenRead(path);
        await using (file.ConfigureAwait(false))
        {
            using var body = new StreamContent(file);
            return await UploadAsync(client, body).ConfigureAwait(false);
        }
    }

    /// <summary>Checks, confirms and runs an import of an uploaded archive as a new site; returns the finished transfer.</summary>
    public static async Task<JsonElement> ImportUploadedAsNewSiteAsync(
        QuickBuilderApp app, HttpClient client, Guid id, string name, string? hostName = null, bool startFresh = false)
    {
        var checkedTransfer = await CheckAsync(app, client, id, "new_site").ConfigureAwait(false);
        Assert.True(checkedTransfer.GetProperty("state").GetString() == "awaiting_confirmation", $"The check ended: {checkedTransfer.GetProperty("checkResult")}");

        await PagesApi.OkAsync(await ConfirmAsync(client, id, new
        {
            newSiteName = name,
            newSiteHostName = hostName,
            startFresh,
            acceptWarnings = true,
            checkFingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString(),
        }).ConfigureAwait(false), HttpStatusCode.Accepted).ConfigureAwait(false);
        await RunWorkerAsync(app).ConfigureAwait(false);

        var done = await GetOkAsync(client, id).ConfigureAwait(false);
        Assert.True(done.GetProperty("state").GetString() == "succeeded", $"The import ended '{done.GetProperty("state").GetString()}': {done.GetProperty("failureCode")}");
        return done;
    }

    public static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    public static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);
}
