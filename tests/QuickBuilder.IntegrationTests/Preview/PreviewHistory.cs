using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>The Editor outbox, for preview link tests: the outbox does not run in tests.</summary>
internal static class PreviewHistory
{
    /// <summary>The payloads of the Editor outbox's events of <paramref name="type"/>, oldest first.</summary>
    public static async Task<List<string>> EditorOutboxAsync(QuickBuilderApp app, string type)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EditorDbContext>();
        return await database.OutboxMessages
            .Where(m => m.EventType == type)
            .OrderBy(m => m.OccurredAtUtc)
            .Select(m => m.Payload)
            .ToListAsync()
            .ConfigureAwait(false);
    }
}
