using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 1: a site member uploads files to the site's library (003 FR-001, FR-002, FR-011).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UploadFileTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "upload").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    public static TheoryData<int> AllowedTypes => new() { 0, 1, 2, 3, 4, 5 };

    /// <summary>US1 scenario 1: each allowed type is stored, recorded and given a public address.</summary>
    [Theory]
    [MemberData(nameof(AllowedTypes))]
    public async Task Each_allowed_type_is_stored_with_its_details_and_a_public_address(int index)
    {
        var (bytes, extension, contentType) = SampleFiles.All[index];
        var (editor, editorId) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var name = MediaApi.CanaryName(extension);

        var response = await MediaApi.UploadAsync(editor, _scenario.SiteA, bytes, name, contentType).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = JsonSerializer.Deserialize<MediaFileDto>(await response.Content.ReadAsStringAsync().ConfigureAwait(true), MediaApi.Json)!;

        Assert.Equal(_scenario.SiteA, file.SiteId);
        Assert.Equal(name, file.FileName);
        Assert.Equal(contentType, file.ContentType);
        Assert.Equal(bytes.Length, file.SizeBytes);
        Assert.Equal(editorId, file.UploadedBy.Id);
        Assert.Equal("Media Tester", file.UploadedBy.DisplayName);
        Assert.Equal(MediaApi.PublicPath(file.Id, extension), file.PublicPath);
        Assert.True(file.Available);
        Assert.True(file.CanDelete);
        Assert.InRange(file.UploadedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal($"/api/v1/media/sites/{_scenario.SiteA}/files/{file.Id}/content", response.Headers.Location?.OriginalString);

        // The row and the stored bytes, exactly as uploaded (FR-002, FR-005).
        var row = Assert.Single(await _scenario.RowsAsync().ConfigureAwait(true), r => r.Id == file.Id);
        Assert.Equal($"{_scenario.SiteA:N}/media/{file.Id:N}", row.StorageKey);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(_scenario.ObjectPath(_scenario.SiteA, file.Id)).ConfigureAwait(true));
    }

    /// <summary>Every site role can upload (FR-025): Editor, Designer and Administrator.</summary>
    [Fact]
    public async Task Every_site_role_can_upload()
    {
        var (editor, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var (designer, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);

        foreach (var client in new[] { editor, designer, _scenario.Admin })
        {
            await MediaApi.UploadOkAsync(client, _scenario.SiteA).ConfigureAwait(true);
        }

        Assert.Equal(3, (await _scenario.RowsAsync().ConfigureAwait(true)).Count);
    }

    /// <summary>US1 scenario 7: a site you hold no role on behaves as if it does not exist.</summary>
    [Fact]
    public async Task Uploading_to_a_site_you_cannot_see_is_indistinguishable_from_a_missing_site()
    {
        var (editorOfB, _) = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Editor)).ConfigureAwait(true);

        var hidden = await MediaApi.UploadAsync(editorOfB, _scenario.SiteA, SampleFiles.Png, MediaApi.CanaryName("png"), "image/png").ConfigureAwait(true);
        var missing = await MediaApi.UploadAsync(_scenario.Admin, Guid.NewGuid(), SampleFiles.Png, MediaApi.CanaryName("png"), "image/png").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            await SignInHelpers.BodyWithoutTraceIdAsync(missing).ConfigureAwait(true),
            await SignInHelpers.BodyWithoutTraceIdAsync(hidden).ConfigureAwait(true));

        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
        Assert.Empty(_scenario.StoredObjects());
    }

    [Fact]
    public async Task Uploading_without_signing_in_is_refused()
    {
        var anonymous = _scenario.App.CreateAnonymousClient();

        var response = await MediaApi.UploadAsync(anonymous, _scenario.SiteA, SampleFiles.Png, MediaApi.CanaryName("png"), "image/png").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
    }

    /// <summary>The extension is stored as given (lower-cased) and appears in the address.</summary>
    [Fact]
    public async Task A_jpeg_named_with_upper_case_jpeg_keeps_that_extension()
    {
        var response = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Jpeg, "Holiday.JPEG", "image/jpeg").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = JsonSerializer.Deserialize<MediaFileDto>(await response.Content.ReadAsStringAsync().ConfigureAwait(true), MediaApi.Json)!;
        Assert.Equal("Holiday.JPEG", file.FileName);
        Assert.EndsWith(".jpeg", file.PublicPath, StringComparison.Ordinal);
    }

    /// <summary>Two files with one name are two files (spec edge case).</summary>
    [Fact]
    public async Task Two_uploads_with_the_same_name_are_kept_separately()
    {
        await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Png, "same.png", "image/png").ConfigureAwait(true);
        await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Png, "same.png", "image/png").ConfigureAwait(true);

        var rows = await _scenario.RowsAsync().ConfigureAwait(true);
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].Id, rows[1].Id);
        Assert.Equal(2, _scenario.StoredObjects().Count);
    }
}
