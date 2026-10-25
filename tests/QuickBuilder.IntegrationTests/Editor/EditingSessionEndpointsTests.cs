using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Domain;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Opening, keeping alive, claiming, moving and releasing editing sessions, and listing
/// holders (023 US2; FR-009 to FR-016; data-model §1.1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EditingSessionEndpointsTests(DatabaseFixture database)
{
    [Fact]
    public async Task Opening_makes_a_viewer_and_each_viewer_sees_the_others_by_display_name_only()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-open");
        var (anna, annaId) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");

        var annas = await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page);
        var bens = await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page);

        Assert.Equal("viewer", EditingApi.Role(bens));
        Assert.Equal("edit", bens.GetProperty("access").GetString());
        Assert.Null(EditingApi.Lock(bens));
        var other = Assert.Single(bens.GetProperty("others").EnumerateArray());
        Assert.Equal(annaId, other.GetProperty("member").GetProperty("id").GetGuid());
        Assert.Equal("Anna Tran", other.GetProperty("member").GetProperty("displayName").GetString());
        Assert.False(other.GetProperty("holdsLock").GetBoolean());

        // FR-013: names only, never an email address.
        Assert.Equal(["displayName", "id"], other.GetProperty("member").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("@", bens.GetRawText(), StringComparison.Ordinal);

        var again = await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, EditingApi.SessionId(annas));
        Assert.Equal("Ben Hale", Assert.Single(again.GetProperty("others").EnumerateArray()).GetProperty("member").GetProperty("displayName").GetString());
        Assert.True(DateTimeOffset.UtcNow.AddMinutes(1) > again.GetProperty("serverNow").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_first_claim_takes_the_lock_and_others_are_refused_naming_the_holder()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-claim");
        var (anna, annaId) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page));
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page));

        var claimed = await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);
        Assert.Equal("holder", EditingApi.Role(claimed));
        Assert.True(EditingApi.Lock(claimed)!.Value.GetProperty("isYou").GetBoolean());

        // Idempotent for the holder.
        Assert.Equal("holder", EditingApi.Role(await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas)));

        var refused = await PagesApi.RefusedAsync(await EditingApi.ClaimAsync(ben, scenario.SiteA, bens), HttpStatusCode.Conflict, "editing.lock_held");
        Assert.Equal("Anna Tran", refused.Body.GetProperty("holder").GetProperty("displayName").GetString());
        Assert.Equal(annaId, refused.Body.GetProperty("holder").GetProperty("id").GetGuid());

        var seenByBen = await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens);
        var held = EditingApi.Lock(seenByBen)!.Value;
        Assert.Equal("Anna Tran", held.GetProperty("holder").GetProperty("displayName").GetString());
        Assert.False(held.GetProperty("isYou").GetBoolean());
        Assert.False(held.GetProperty("stale").GetBoolean());
        Assert.Equal(annas, held.GetProperty("holderSessionId").GetGuid());
    }

    [Fact]
    public async Task Activity_keeps_the_lock_fresh_only_for_the_holder_and_a_gone_holder_can_be_claimed_over()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-activity");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page));
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page));
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);

        // Both editors stay open, reading only, one minute at a time.
        async Task PassAsync(int minutes)
        {
            for (var i = 0; i < minutes; i++)
            {
                scenario.Clock.Advance(TimeSpan.FromMinutes(1));
                await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, annas, active: false);
                await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens, active: true);
            }
        }

        // A change at minute 20, then reading only: not stale until 30 minutes after it.
        // Ben reports activity too, but he holds no lock, so it is ignored.
        await PassAsync(20);
        await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, annas, active: true);
        await PassAsync(29);
        var notYet = EditingApi.Lock(await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens))!.Value;
        Assert.False(notYet.GetProperty("stale").GetBoolean());

        await PassAsync(1);
        var stale = EditingApi.Lock(await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens))!.Value;
        Assert.True(stale.GetProperty("stale").GetBoolean());
        Assert.Equal(30 * 60, stale.GetProperty("inactiveSeconds").GetInt32());

        // A stale holder still holds the lock: only a takeover moves it (FR-017).
        await PagesApi.RefusedAsync(await EditingApi.ClaimAsync(ben, scenario.SiteA, bens), HttpStatusCode.Conflict, "editing.lock_held");

        // Anna's browser stops: two minutes without a sign of life and the lock is gone (FR-016).
        scenario.Clock.Advance(TimeSpan.FromSeconds(119));
        await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens);
        Assert.NotNull(EditingApi.Lock(await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens)));
        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        var free = await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens);
        Assert.Null(EditingApi.Lock(free));
        Assert.Empty(free.GetProperty("others").EnumerateArray());

        Assert.Equal("holder", EditingApi.Role(await EditingApi.ClaimOkAsync(ben, scenario.SiteA, bens)));
        await PagesApi.RefusedAsync(await EditingApi.HeartbeatAsync(anna, scenario.SiteA, annas), HttpStatusCode.NotFound, "editing.session_gone");
    }

    [Fact]
    public async Task A_viewer_stops_being_shown_two_minutes_after_their_last_sign_of_life()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-viewer-expiry");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page);
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page));

        scenario.Clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Single((await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens)).GetProperty("others").EnumerateArray());
        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty((await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens)).GetProperty("others").EnumerateArray());
    }

    [Fact]
    public async Task Edit_here_instead_moves_the_members_own_lock_without_a_takeover_record()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-move");
        var (annaTab1, annaId) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var annaTab2 = await scenario.AnotherSessionAsync(annaId);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        var first = EditingApi.SessionId(await EditingApi.OpenOkAsync(annaTab1, scenario.SiteA, "page", page));
        await EditingApi.ClaimOkAsync(annaTab1, scenario.SiteA, first);

        var second = await EditingApi.OpenOkAsync(annaTab2, scenario.SiteA, "page", page);
        Assert.True(EditingApi.Lock(second)!.Value.GetProperty("isYourOtherSession").GetBoolean());

        var moved = await PagesApi.OkAsync(await EditingApi.MoveAsync(annaTab2, scenario.SiteA, EditingApi.SessionId(second), first));
        Assert.Equal("holder", EditingApi.Role(moved));

        var before = await EditingApi.HeartbeatOkAsync(annaTab1, scenario.SiteA, first);
        Assert.Equal("viewer", EditingApi.Role(before));
        var lost = before.GetProperty("lostLock");
        Assert.Equal("moved", lost.GetProperty("reason").GetString());
        Assert.Equal(annaId, lost.GetProperty("by").GetProperty("id").GetGuid());
        Assert.Equal(0, await scenario.ReadAsync(d => EditingScenario.Everything<EditingTakeover>(d).CountAsync()));

        // Someone else's session cannot be moved, active or not.
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page));
        await PagesApi.RefusedAsync(await EditingApi.MoveAsync(ben, scenario.SiteA, bens, EditingApi.SessionId(second)), HttpStatusCode.Conflict, "editing.lock_held");
    }

    [Fact]
    public async Task Releasing_frees_the_document_and_holders_are_listed_for_markers()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-release");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var pricing = await scenario.PageAsync("pricing");
        var about = await scenario.PageAsync("about");
        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", pricing));
        await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", about);
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);

        var holders = (await EditingApi.HoldersOkAsync(ben, scenario.SiteA, "page")).GetProperty("holders").EnumerateArray().ToList();
        var held = Assert.Single(holders);
        Assert.Equal(pricing, held.GetProperty("documentId").GetGuid());
        Assert.Equal("Anna Tran", held.GetProperty("holder").GetProperty("displayName").GetString());
        Assert.False(held.GetProperty("stale").GetBoolean());
        Assert.Empty((await EditingApi.HoldersOkAsync(ben, scenario.SiteA, "component")).GetProperty("holders").EnumerateArray());

        // Only one's own session can be released; anyone else's id is not found.
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", pricing));
        Assert.Equal(HttpStatusCode.NoContent, (await EditingApi.ReleaseAsync(ben, scenario.SiteA, annas)).StatusCode);
        Assert.NotNull(EditingApi.Lock(await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens)));
        await PagesApi.RefusedAsync(await EditingApi.HeartbeatAsync(ben, scenario.SiteA, annas), HttpStatusCode.NotFound, "editing.session_gone");

        Assert.Equal(HttpStatusCode.NoContent, (await EditingApi.ReleaseAsync(anna, scenario.SiteA, annas)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await EditingApi.ReleaseAsync(anna, scenario.SiteA, annas)).StatusCode);
        Assert.Null(EditingApi.Lock(await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens)));
        Assert.Empty((await EditingApi.HoldersOkAsync(ben, scenario.SiteA, "page")).GetProperty("holders").EnumerateArray());
    }

    [Fact]
    public async Task A_member_without_the_edit_capability_views_but_cannot_claim()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-view-only");
        var (edna, _) = await scenario.MemberAsync("Edna Ray", scenario.SiteA, SystemRole.Editor);
        var header = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header")).GetProperty("id").GetGuid();

        var opened = await EditingApi.OpenOkAsync(edna, scenario.SiteA, "component", header);
        Assert.Equal("view-only", opened.GetProperty("access").GetString());
        await PagesApi.RefusedAsync(await EditingApi.ClaimAsync(edna, scenario.SiteA, EditingApi.SessionId(opened)), HttpStatusCode.Forbidden, "editing.forbidden");

        // An Editor may edit page drafts.
        var page = await scenario.PageAsync("pricing");
        var pageSession = await EditingApi.OpenOkAsync(edna, scenario.SiteA, "page", page);
        Assert.Equal("edit", pageSession.GetProperty("access").GetString());
        Assert.Equal("holder", EditingApi.Role(await EditingApi.ClaimOkAsync(edna, scenario.SiteA, EditingApi.SessionId(pageSession))));
    }

    [Fact]
    public async Task Requests_are_checked_and_an_item_session_names_its_collection()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-checks");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (bea, _) = await scenario.MemberAsync("Bea Lund", scenario.SiteB, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");

        Assert.Equal(HttpStatusCode.BadRequest, (await EditingApi.OpenAsync(anna, scenario.SiteA, "menu", page)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await EditingApi.OpenAsync(anna, scenario.SiteA, "item", Guid.NewGuid())).StatusCode);
        await EditingApi.OpenOkAsync(anna, scenario.SiteA, "item", Guid.NewGuid(), Guid.NewGuid());

        // No role on site A: as though the site did not exist.
        Assert.Equal(HttpStatusCode.NotFound, (await EditingApi.OpenAsync(bea, scenario.SiteA, "page", page)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EditingApi.HoldersAsync(bea, scenario.SiteA, "page")).StatusCode);
    }
}
