using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.Host.Configuration;

/// <summary>
/// Bridges the host's bound options to the shared outbox runtime.
/// </summary>
/// <remarks>
/// Shared infrastructure must not depend on the host (FR-003), and an architecture test
/// asserts it does not — so the outbox reads a small interface the host implements rather
/// than reaching for <c>IOptions&lt;OutboxOptions&gt;</c> itself.
/// </remarks>
internal sealed class OutboxSettingsProvider(IOptions<OutboxOptions> options) : IOutboxSettingsProvider
{
    public OutboxSettings Get()
    {
        var value = options.Value;

        return new OutboxSettings(
            value.Enabled,
            value.PollInterval,
            value.BatchSize,
            value.HandlerTimeout,
            value.MaxAttempts,
            value.RetentionPeriod);
    }
}
