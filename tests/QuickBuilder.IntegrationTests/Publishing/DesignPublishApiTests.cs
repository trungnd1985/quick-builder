using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 2: the site's design is published on its own, recorded, and can be rolled back
/// (012 FR-010, clarification Q1, research PR-17).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DesignPublishApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    private string Tokens => $"/api/v1/editor/sites/{_scenario.SiteA}/design-tokens";

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "design-publish").ConfigureAwait(false);
        var about = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(false))["/about"];
        var path = PagesApi.PagePath(_scenario.SiteA, about);
        await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.HeadingDocument("About")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Before_any_publish_pages_use_the_default_design()
    {
        var tokens = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri(Tokens, UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(0, tokens.GetProperty("publication").GetProperty("liveNumber").GetInt32());
        Assert.False(tokens.GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean());
        Assert.Contains("--qb-color-color-brand:#2563eb", await PageAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_tokens_changes_nothing_public_until_the_design_is_published()
    {
        var saved = await SaveBrandAsync("#16a34a").ConfigureAwait(true);
        Assert.True(saved.GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean());
        Assert.Contains("--qb-color-color-brand:#2563eb", await PageAsync().ConfigureAwait(true), StringComparison.Ordinal);

        var published = await PagesApi.OkAsync(await PublishAsync(saved.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(1, published.GetProperty("liveNumber").GetInt32());
        Assert.False(published.GetProperty("hasUnpublishedChanges").GetBoolean());
        Assert.Contains("--qb-color-color-brand:#16a34a", await PageAsync().ConfigureAwait(true), StringComparison.Ordinal);

        var events = await EditorOutboxAsync().ConfigureAwait(true);
        var design = Assert.Single(events, e => e.Type == DesignPublished.EventType);
        Assert.DoesNotContain("16a34a", design.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_token_version_is_refused()
    {
        var saved = await SaveBrandAsync("#16a34a").ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PublishAsync(saved.GetProperty("version").GetUInt32() + 1).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "editor.out_of_date").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_rollback_publishes_an_old_version_again_and_leaves_the_draft_alone()
    {
        var green = await SaveBrandAsync("#16a34a").ConfigureAwait(true);
        await PublishAsync(green.GetProperty("version").GetUInt32()).ConfigureAwait(true);
        var red = await SaveBrandAsync("#dc2626").ConfigureAwait(true);
        await PublishAsync(red.GetProperty("version").GetUInt32()).ConfigureAwait(true);

        var rolledBack = await PagesApi.OkAsync(await _scenario.Admin.PostAsJsonAsync(
            new Uri($"{Tokens}/rollback", UriKind.Relative), new { versionNumber = 1 }).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(3, rolledBack.GetProperty("liveNumber").GetInt32());
        Assert.True(rolledBack.GetProperty("hasUnpublishedChanges").GetBoolean());
        Assert.Contains("--qb-color-color-brand:#16a34a", await PageAsync().ConfigureAwait(true), StringComparison.Ordinal);

        var draft = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri(Tokens, UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("#dc2626", draft.GetProperty("document").GetRawText(), StringComparison.Ordinal);

        var versions = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri($"{Tokens}/versions", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal([3, 2, 1], versions.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
        Assert.Equal(1, versions.GetProperty("versions")[0].GetProperty("restoredFromNumber").GetInt32());

        var one = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri($"{Tokens}/versions/1", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("#16a34a", one.GetProperty("document").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_that_is_not_kept_is_not_found()
    {
        await PagesApi.RefusedAsync(
            await _scenario.Admin.PostAsJsonAsync(new Uri($"{Tokens}/rollback", UriKind.Relative), new { versionNumber = 9 }).ConfigureAwait(true),
            HttpStatusCode.NotFound,
            "editor.design_version_not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await _scenario.Admin.GetAsync(new Uri($"{Tokens}/versions/9", UriKind.Relative)).ConfigureAwait(true),
            HttpStatusCode.NotFound,
            "editor.design_version_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Only_the_fifty_newest_versions_are_kept()
    {
        for (var i = 0; i < 55; i++)
        {
            var tokens = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri(Tokens, UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
            await PagesApi.OkAsync(await PublishAsync(tokens.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        }

        var versions = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri($"{Tokens}/versions", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        var numbers = versions.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()).ToList();

        Assert.Equal(50, numbers.Count);
        Assert.Equal(55, numbers[0]);
        Assert.Equal(6, numbers[^1]);
    }

    [Fact]
    public async Task Designers_publish_the_design_and_editors_may_only_look()
    {
        var designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);
        var editor = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);

        var tokens = await PagesApi.OkAsync(await editor.GetAsync(new Uri(Tokens, UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(tokens.GetProperty("permissions").GetProperty("canPublish").GetBoolean());

        await PagesApi.RefusedAsync(
            await editor.PostAsJsonAsync(new Uri($"{Tokens}/publish", UriKind.Relative), new { version = 0 }).ConfigureAwait(true),
            HttpStatusCode.Forbidden,
            "identity.forbidden").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await editor.PostAsJsonAsync(new Uri($"{Tokens}/rollback", UriKind.Relative), new { versionNumber = 1 }).ConfigureAwait(true),
            HttpStatusCode.Forbidden,
            "identity.forbidden").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync(new Uri($"{Tokens}/versions", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await designer.PostAsJsonAsync(new Uri($"{Tokens}/publish", UriKind.Relative), new { version = 0 }).ConfigureAwait(true)).StatusCode);
    }

    private Task<HttpResponseMessage> PublishAsync(uint version) =>
        _scenario.Admin.PostAsJsonAsync(new Uri($"{Tokens}/publish", UriKind.Relative), new { version });

    private async Task<JsonElement> SaveBrandAsync(string colour)
    {
        var current = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(new Uri(Tokens, UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);
        var document = current.GetProperty("document").GetRawText().Replace(
            current.GetProperty("document").GetProperty("colors")[0].GetProperty("value").GetString()!, colour, StringComparison.Ordinal);

        var body = $$"""{"version":{{current.GetProperty("version").GetUInt32()}},"document":{{document}}}""";
        var response = await _scenario.Admin.PutAsync(new Uri(Tokens, UriKind.Relative), new StringContent(body, System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
        return await PagesApi.OkAsync(response).ConfigureAwait(false);
    }

    private Task<string> PageAsync() =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri("/about", UriKind.Relative));

    private async Task<List<(string Type, string Payload)>> EditorOutboxAsync()
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EditorDbContext>();
        return (await db.Set<OutboxMessage>().AsNoTracking().Select(m => new { m.EventType, m.Payload }).ToListAsync().ConfigureAwait(false))
            .Select(m => (m.EventType, m.Payload)).ToList();
    }
}
