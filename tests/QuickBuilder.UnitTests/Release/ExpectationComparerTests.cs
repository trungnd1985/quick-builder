using QuickBuilder.SmokeTests.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// What must hold after a later release upgrades a snapshot: the same pages, no lost rows
/// (019 contracts/snapshot.md §2–§3, FR-012, FR-014).
/// </summary>
public sealed class ExpectationComparerTests
{
    private static ExpectedPage Page(string path, int status = 200, string text = "Hello world", string host = "release-0-1-0.example.test") =>
        new(host, path, status, "sha256:" + text.GetHashCode(StringComparison.Ordinal).ToString("x8", System.Globalization.CultureInfo.InvariantCulture), text);

    private static Expectations Expect(IEnumerable<ExpectedPage> pages, Dictionary<string, long>? records = null) =>
        new()
        {
            CreatedBy = "0.1.0",
            CreatedFrom = "baseline",
            Pages = [.. pages],
            Records = records ?? new Dictionary<string, long> { ["pages.pages"] = 4, ["cms.items"] = 3 },
            Members = [new ExpectedMember("admin@release-0-1-0.example.test", "pw", "Administrator")],
            Drafts = [],
        };

    [Fact]
    public void The_same_pages_and_records_pass()
    {
        var before = Expect([Page("/"), Page("/about")]);

        var result = ExpectationComparer.Compare(before, before);

        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void A_changed_page_fails_with_its_address_and_a_text_diff()
    {
        var before = Expect([Page("/"), Page("/about", text: "We build websites.")]);
        var after = Expect([Page("/"), Page("/about", text: "We build web sites.")]);

        var failure = Assert.Single(ExpectationComparer.Compare(before, after).Failures);

        Assert.Contains("release-0-1-0.example.test/about", failure, StringComparison.Ordinal);
        Assert.Contains("- We build websites.", failure, StringComparison.Ordinal);
        Assert.Contains("+ We build web sites.", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_status_fails()
    {
        var failure = Assert.Single(ExpectationComparer.Compare(
            Expect([Page("/gone", status: 404)]),
            Expect([Page("/gone", status: 200)])).Failures);

        Assert.Contains("status 404 became 200", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_that_disappeared_fails()
    {
        var failure = Assert.Single(ExpectationComparer.Compare(Expect([Page("/"), Page("/about")]), Expect([Page("/")])).Failures);

        Assert.Contains("/about", failure, StringComparison.Ordinal);
        Assert.Contains("missing", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Fewer_rows_or_a_missing_table_fails_and_names_it()
    {
        var before = Expect([Page("/")], new Dictionary<string, long> { ["pages.pages"] = 4, ["cms.items"] = 3, ["forms.submissions"] = 2 });
        var after = Expect([Page("/")], new Dictionary<string, long> { ["pages.pages"] = 3, ["cms.items"] = 3 });

        var failures = ExpectationComparer.Compare(before, after).Failures;

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, f => f.Contains("pages.pages", StringComparison.Ordinal) && f.Contains("4 rows became 3", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("forms.submissions", StringComparison.Ordinal) && f.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void More_rows_pass_because_migrations_may_derive_rows()
    {
        var before = Expect([Page("/")], new Dictionary<string, long> { ["cms.items"] = 3 });
        var after = Expect([Page("/")], new Dictionary<string, long> { ["cms.items"] = 3, ["cms.item_index"] = 10 });

        Assert.True(ExpectationComparer.Compare(before, after).Passed);
    }

    [Fact]
    public void Merging_keeps_every_page_and_member_of_the_older_snapshot()
    {
        var older = Expect([Page("/", host: "release-0-1-0.example.test")]);
        var newer = Expect([Page("/", host: "release-0-2-0.example.test")]) with
        {
            CreatedBy = "0.2.0",
            CreatedFrom = "0.1.0",
            Members = [new ExpectedMember("admin@release-0-2-0.example.test", "pw2", "Administrator")],
        };

        var merged = ExpectationComparer.Merge(older, newer);

        Assert.Equal("0.2.0", merged.CreatedBy);
        Assert.Equal(2, merged.Pages.Count);
        Assert.Equal(2, merged.Members.Count);
        Assert.Equal(newer.Records, merged.Records);
    }

    [Fact]
    public void Merging_prefers_the_newer_capture_of_the_same_page()
    {
        var older = Expect([Page("/", text: "old")]);
        var newer = Expect([Page("/", text: "new")]);

        Assert.Equal("new", Assert.Single(ExpectationComparer.Merge(older, newer).Pages).Text);
    }

    [Fact]
    public void A_volatile_entry_that_matches_no_table_is_reported_as_stale()
    {
        string[] tables = ["identity.sessions", "pages.pages", "pages.outbox_messages"];

        var stale = Expectations.StaleVolatileEntries(tables);

        Assert.Contains("forms.rate_windows", stale);
        Assert.DoesNotContain("identity.sessions", stale);
        Assert.DoesNotContain("*.outbox_messages", stale);
    }

    [Fact]
    public void Volatile_tables_are_not_counted_and_others_are()
    {
        Assert.True(Expectations.IsVolatile("pages.outbox_messages"));
        Assert.True(Expectations.IsVolatile("cms.__EFMigrationsHistory"));
        Assert.True(Expectations.IsVolatile("platform.database_version"));
        Assert.False(Expectations.IsVolatile("pages.pages"));
        Assert.False(Expectations.IsVolatile("identity.users"));
    }

    [Theory]
    [InlineData("Published on 2026-10-23.", true)]
    [InlineData("Published on 23 October 2026.", true)]
    [InlineData("Published on October 23, 2026.", true)]
    [InlineData("Published on 2026-10-22.", false)]
    [InlineData("Copyright 2026", false)]
    public void A_page_showing_today_is_time_dependent(string text, bool expected)
    {
        Assert.Equal(expected, Expectations.ShowsDate(text, new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void Expectations_round_trip_through_json()
    {
        var original = Expect([Page("/")]);

        var copy = Expectations.FromJson(original.ToJson());

        Assert.Equal(original.ToJson(), copy.ToJson());
        Assert.Equal(1, copy.Format);
    }
}
