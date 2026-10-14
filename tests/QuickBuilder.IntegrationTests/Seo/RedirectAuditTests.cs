using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain.Redirects;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// Every action on a redirect is recorded with who and when, ids and codes only, and the
/// record outlives the redirect (017 FR-021, research SE-13a).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectAuditTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "seo-redirect-audit").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Changes_turning_on_and_off_and_deleting_are_recorded_and_kept()
    {
        var created = await RedirectsApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "/audited", RedirectsApi.ToPath("/a"), note: "secret note").ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();

        var changed = await PagesApi.OkAsync(await RedirectsApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, id, created.GetProperty("version").GetUInt32(), "/audited", RedirectsApi.ToPath("/b")).ConfigureAwait(true)).ConfigureAwait(true);
        var off = await PagesApi.OkAsync(await RedirectsApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, id, changed.GetProperty("version").GetUInt32(), "/audited", RedirectsApi.ToPath("/b"), enabled: false).ConfigureAwait(true)).ConfigureAwait(true);
        var on = await PagesApi.OkAsync(await RedirectsApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, id, off.GetProperty("version").GetUInt32(), "/audited", RedirectsApi.ToPath("/b")).ConfigureAwait(true)).ConfigureAwait(true);
        await RedirectsApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, id, on.GetProperty("version").GetUInt32()).ConfigureAwait(true);

        var rows = await _scenario.ReadAsync(db => PagesScenario.Everything<RedirectChange>(db)
            .AsNoTracking()
            .Where(c => c.RedirectId == id)
            .OrderBy(c => c.AtUtc)
            .ThenBy(c => c.Id)
            .ToListAsync()).ConfigureAwait(true);

        Assert.Equal(["created", "changed", "turned_off", "turned_on", "deleted"], rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.NotNull(r.ActorUserId));
        Assert.All(rows, r => Assert.Equal(_scenario.SiteA, r.SiteId));
    }

    [Fact]
    public async Task The_record_holds_no_source_target_or_note()
    {
        await RedirectsApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "/very-private-source", RedirectsApi.ToPath("/very-private-target"), note: "very private note").ConfigureAwait(true);

        var columns = await _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'pages' AND table_name = 'redirect_changes'")
            .ToListAsync()).ConfigureAwait(true);

        Assert.Equal(["action", "actor_user_id", "at_utc", "id", "origin", "redirect_id", "site_id"], columns.Order(StringComparer.Ordinal));
    }
}
