using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// Titles, names, slugs and content never reach the logs from editing sessions, takeovers,
/// fencing refusals, conflicts or copies, and every takeover and refusal leaves exactly one log
/// entry with the ids that explain it (023 FR-024; constitution Principle IX).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EditingContentNotLoggedTests(DatabaseFixture database)
{
    [Fact]
    public async Task No_title_name_slug_or_content_appears_and_each_refusal_is_logged_once()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "editing-not-logged");
        var marker = PagesApi.CanaryMarker;
        var (anna, annaId) = await scenario.MemberAsync($"{marker}Anna", scenario.SiteA, SystemRole.Designer);
        var (ben, benId) = await scenario.MemberAsync($"{marker}Ben", scenario.SiteA, SystemRole.Designer);
        var page = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "pricing", title: $"{marker}Pricing")).GetProperty("id").GetGuid();
        var path = PagesApi.PagePath(scenario.SiteA, page);

        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page));
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page));
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);
        await EditingApi.ClaimAsync(ben, scenario.SiteA, bens);

        // A fencing refusal, and a conflict.
        await PagesApi.SaveDraftWithHeaderAsync(ben, path, PagesApi.DocumentWithText($"{marker}ben"), "X-QB-Editing-Session", bens.ToString());
        var loaded = (await PagesApi.DraftOkAsync(anna, path)).GetProperty("version").GetUInt32();
        await PagesApi.SaveDraftAsync(anna, path, PagesApi.DocumentWithText($"{marker}one"), loaded);
        await PagesApi.SaveDraftAsync(anna, path, PagesApi.DocumentWithText($"{marker}two"), loaded);

        // A takeover after thirty idle minutes.
        for (var i = 0; i < 31; i++)
        {
            scenario.Clock.Advance(TimeSpan.FromMinutes(1));
            await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, annas);
            await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens);
        }

        await PagesApi.OkAsync(await EditingApi.TakeOverAsync(ben, scenario.SiteA, bens, annas));

        // A copy.
        await ben.PostAsJsonAsync(new Uri($"{path}/copies", UriKind.Relative), new
        {
            document = JsonElement.Parse(PagesApi.DocumentWithText($"{marker}copy")),
            baseSavedAtUtc = DateTimeOffset.UtcNow,
            title = $"{marker}Pricing (copy)",
        });

        var events = scenario.App.Logs.Events.Select(LogCapture.Render).ToList();
        var leaked = events.FirstOrDefault(e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        Assert.True(leaked is null, $"A title, name or content reached the logs: {leaked}");

        Assert.Single(events, e => e.Contains("another session holds the lock", StringComparison.Ordinal) && e.Contains(benId.ToString(), StringComparison.Ordinal));
        Assert.Single(events, e => e.Contains("refused as out of date", StringComparison.Ordinal) && e.Contains(page.ToString(), StringComparison.Ordinal));
        Assert.Single(events, e => e.Contains("taken over", StringComparison.Ordinal) && e.Contains(annaId.ToString(), StringComparison.Ordinal) && e.Contains(benId.ToString(), StringComparison.Ordinal));

        // The takeover record holds ids, a kind and a duration: its one text column is the kind.
        var textColumns = await scenario.ReadAsync(d => d.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns " +
                "WHERE table_schema = 'editor' AND table_name = 'editing_takeovers' AND data_type IN ('text', 'character varying', 'citext', 'jsonb', 'USER-DEFINED')")
            .ToListAsync());
        Assert.Equal(["document_kind"], textColumns);
    }
}
