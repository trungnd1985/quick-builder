using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Forms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// Forms in a preview send nothing (022 FR-026, research PV-12): they post to the preview, carry
/// no form script, and no submission, rate window or notification follows.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FormSubmitIsolationTests(DatabaseFixture database) : IAsyncLifetime
{
    private FormsWorld _world = null!;
    private Guid _page;

    public async Task InitializeAsync()
    {
        _world = await FormsWorld.CreateAsync(database, "preview-forms").ConfigureAwait(false);
        _page = await _world.PageAsync("contact").ConfigureAwait(false);
        await _world.PublishPageAsync(_page, FormsWorld.Document(FormsWorld.Form("contact", "Contact"))).ConfigureAwait(false);
        await _world.DeliverEventsAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_previewed_form_posts_to_the_preview_without_a_script_and_its_policy_allows_that()
    {
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _page, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(true);

        var (status, html, response) = await PreviewLinksApi.OpenAsync(_world.App, link.Path, CmsScenario.HostA).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("action=\"/_preview/form\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/_forms/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("form-action 'self'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posting_a_previewed_form_stores_counts_and_sends_nothing()
    {
        var before = await CountsAsync().ConfigureAwait(true);

        using var visitor = _world.Visitor();
        var response = await visitor.PostAsync(
                new Uri("/_preview/form", UriKind.Relative),
                new FormUrlEncodedContent([new("e00000000001", "reviewer@example.com"), new("_form", "contact")]))
            .ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Forms are not sent from previews.", body, StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync().ConfigureAwait(true));
    }

    private Task<(int Submissions, int Windows, int Deliveries, int Outbox)> CountsAsync() =>
        _world.WithFormsAsync(async db => (
            await db.ForSite<QuickBuilder.Forms.Domain.Submission>(_world.Site).CountAsync().ConfigureAwait(false),
            await db.ForSite<QuickBuilder.Forms.Domain.RateWindow>(_world.Site).CountAsync().ConfigureAwait(false),
            await db.ForSite<QuickBuilder.Forms.Domain.NotificationDelivery>(_world.Site).CountAsync().ConfigureAwait(false),
            await db.Set<QuickBuilder.Shared.Infrastructure.Outbox.OutboxMessage>().CountAsync().ConfigureAwait(false)));
}
