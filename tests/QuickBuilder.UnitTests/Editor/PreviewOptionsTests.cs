using Microsoft.Extensions.Configuration;
using QuickBuilder.Editor.Infrastructure;

namespace QuickBuilder.UnitTests.Editor;

/// <summary>The <c>Preview</c> settings: the emergency switch of 022 research PV-07.</summary>
public sealed class PreviewOptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static PreviewOptionsValidator Validator() => new(new FixedClock(Now));

    private static PreviewOptions Bind(string? value)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null)
        {
            settings["Preview:LinksValidFrom"] = value;
        }

        var options = new PreviewOptions();
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection(PreviewOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void The_switch_may_be_absent()
    {
        var options = Bind(null);

        Assert.Null(options.LinksValidFrom);
        Assert.True(Validator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void An_iso_8601_utc_time_is_read()
    {
        var options = Bind("2026-11-19T11:30:00Z");

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 11, 30, 0, TimeSpan.Zero), options.LinksValidFrom);
        Assert.True(Validator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Up_to_one_day_ahead_is_allowed_and_later_stops_startup()
    {
        Assert.True(Validator().Validate(null, new PreviewOptions { LinksValidFrom = Now.AddDays(1) }).Succeeded);

        var result = Validator().Validate(null, new PreviewOptions { LinksValidFrom = Now.AddDays(1).AddMinutes(1) });

        Assert.True(result.Failed);
        Assert.Contains("Preview:LinksValidFrom", result.FailureMessage, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
