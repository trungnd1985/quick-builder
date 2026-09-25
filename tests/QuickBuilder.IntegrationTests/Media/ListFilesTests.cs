using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Browsing a site's library: newest first, a page at a time, each file exactly once
/// (003 FR-003, US3 scenario 1, research M-16).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ListFilesTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "list").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Files_are_listed_newest_first_with_a_total()
    {
        var uploaded = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            uploaded.Add((await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true)).Id);
        }

        var page = await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        Assert.Equal(uploaded.AsEnumerable().Reverse(), page.Items.Select(f => f.Id));
        Assert.Equal(3, page.TotalCount);
        Assert.Null(page.NextCursor);
    }

    /// <summary>US3 scenario 1: paging visits every file exactly once, even while others are uploaded.</summary>
    [Fact]
    public async Task Paging_visits_every_file_exactly_once_while_uploads_continue()
    {
        var expected = new HashSet<Guid>();
        for (var i = 0; i < 12; i++)
        {
            expected.Add((await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true)).Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteA, cursor, limit: 5).ConfigureAwait(true);
            seen.AddRange(page.Items.Select(f => f.Id));
            cursor = page.NextCursor;
            pages++;

            // Newer uploads land before the cursor, so they never shift the pages still to come.
            await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(expected.Order(), seen.Order());
    }

    [Fact]
    public async Task The_page_size_defaults_to_fifty_and_is_bounded()
    {
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.ListAsync(_scenario.Admin, _scenario.SiteA, limit: 100).ConfigureAwait(true)).StatusCode);

        foreach (var limit in new[] { 0, 101, -1 })
        {
            var response = await MediaApi.ListAsync(_scenario.Admin, _scenario.SiteA, limit: limit).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("media.invalid_limit", await ErrorCodeAsync(response).ConfigureAwait(true));
        }
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("AAAA")]
    [InlineData("%%%")]
    public async Task A_cursor_the_server_did_not_issue_is_refused(string cursor)
    {
        var response = await MediaApi.ListAsync(_scenario.Admin, _scenario.SiteA, cursor).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("media.invalid_cursor", await ErrorCodeAsync(response).ConfigureAwait(true));
    }

    [Fact]
    public async Task One_sites_listing_never_includes_another_sites_files()
    {
        var a = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        var b = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true);

        Assert.Equal([a.Id], (await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true)).Items.Select(f => f.Id));
        Assert.Equal([b.Id], (await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true)).Items.Select(f => f.Id));
    }

    [Fact]
    public async Task A_site_you_cannot_see_and_a_site_that_does_not_exist_answer_identically()
    {
        var (editorOfB, _) = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Editor)).ConfigureAwait(true);

        var hidden = await MediaApi.ListAsync(editorOfB, _scenario.SiteA).ConfigureAwait(true);
        var missingForAdmin = await MediaApi.ListAsync(_scenario.Admin, Guid.NewGuid()).ConfigureAwait(true);
        var siteNotFound = await _scenario.Admin.GetAsync(new Uri($"/api/v1/sites/{Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingForAdmin.StatusCode);

        // Byte-identical to 002's "no such site", apart from traceId (analysis I1).
        var reference = await SignInHelpers.BodyWithoutTraceIdAsync(siteNotFound).ConfigureAwait(true);
        Assert.Equal(reference, await SignInHelpers.BodyWithoutTraceIdAsync(hidden).ConfigureAwait(true));
        Assert.Equal(reference, await SignInHelpers.BodyWithoutTraceIdAsync(missingForAdmin).ConfigureAwait(true));
    }

    /// <summary>US3 scenario 5 / edge case: a file whose bytes vanished is listed as unavailable.</summary>
    [Fact]
    public async Task A_file_whose_bytes_are_missing_is_listed_as_unavailable()
    {
        var kept = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        var lost = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        File.Delete(_scenario.ObjectPath(_scenario.SiteA, lost.Id));

        var page = await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        Assert.True(page.Items.Single(f => f.Id == kept.Id).Available);
        Assert.False(page.Items.Single(f => f.Id == lost.Id).Available);
    }

    /// <summary>Clarification Q4 as the UI sees it: canDelete mirrors the server's rule.</summary>
    [Fact]
    public async Task Can_delete_reflects_the_callers_role_and_authorship()
    {
        var (editor, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var (designer, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);

        var editorsOwn = await MediaApi.UploadOkAsync(editor, _scenario.SiteA).ConfigureAwait(true);
        var someoneElses = await MediaApi.UploadOkAsync(designer, _scenario.SiteA).ConfigureAwait(true);

        var asEditor = (await MediaApi.ListOkAsync(editor, _scenario.SiteA).ConfigureAwait(true)).Items.ToDictionary(f => f.Id);
        var asDesigner = (await MediaApi.ListOkAsync(designer, _scenario.SiteA).ConfigureAwait(true)).Items.ToDictionary(f => f.Id);

        Assert.True(asEditor[editorsOwn.Id].CanDelete);
        Assert.False(asEditor[someoneElses.Id].CanDelete);
        Assert.True(asDesigner[editorsOwn.Id].CanDelete);
        Assert.True(asDesigner[someoneElses.Id].CanDelete);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return body.RootElement.GetProperty("errorCode").GetString();
    }
}
