using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Trash;
using Xunit;

namespace QuickBuilder.UnitTests.Trash;

/// <summary>The purge date is computed when read, from the current setting (021 TR-15, FR-025).</summary>
public sealed class TrashRetentionTests
{
    private static readonly DateTimeOffset TrashedAt = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Purge_date_is_the_trash_time_plus_the_retention_days()
    {
        var retention = new TrashRetention(new Monitor(new TrashOptions { RetentionDays = 30 }));

        Assert.Equal(TrashedAt.AddDays(30), retention.PurgeOn(TrashedAt));
    }

    [Fact]
    public void Entries_trashed_at_or_before_now_minus_retention_are_due()
    {
        var retention = new TrashRetention(new Monitor(new TrashOptions { RetentionDays = 7 }));
        var now = TrashedAt.AddDays(10);

        Assert.Equal(now.AddDays(-7), retention.DueBefore(now));
    }

    [Fact]
    public void A_changed_setting_applies_to_every_entry_at_once()
    {
        var monitor = new Monitor(new TrashOptions { RetentionDays = 30 });
        var retention = new TrashRetention(monitor);
        Assert.Equal(TrashedAt.AddDays(30), retention.PurgeOn(TrashedAt));

        monitor.CurrentValue = new TrashOptions { RetentionDays = 60 };

        Assert.Equal(TrashedAt.AddDays(60), retention.PurgeOn(TrashedAt));
        Assert.Equal(60, retention.Days);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    public void The_allowed_range_is_1_to_3650_days(int days, bool valid)
    {
        var options = new TrashOptions { RetentionDays = days };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options,
            new System.ComponentModel.DataAnnotations.ValidationContext(options),
            results,
            validateAllProperties: true);

        Assert.Equal(valid, ok);
    }

    private sealed class Monitor(TrashOptions value) : IOptionsMonitor<TrashOptions>
    {
        public TrashOptions CurrentValue { get; set; } = value;

        public TrashOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TrashOptions, string?> listener) => null;
    }
}
