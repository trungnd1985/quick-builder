using QuickBuilder.SmokeTests.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// A release may change what visitors see only on pages a reviewed allow-list names
/// (019 FR-012a, contracts/snapshot.md §3, analysis U3).
/// </summary>
public sealed class VisibleChangeAllowListTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "qb-allow-" + Guid.NewGuid().ToString("N"));

    public VisibleChangeAllowListTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ExpectedPage Page(string path, string text, int status = 200, string host = "release-0-1-0.example.test") =>
        new(host, path, status, "sha256:" + text, text);

    private static Expectations Expect(params ExpectedPage[] pages) => new()
    {
        CreatedBy = "0.1.0",
        CreatedFrom = "baseline",
        Pages = pages,
        Records = new Dictionary<string, long>(),
        Members = [],
        Drafts = [],
    };

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(_directory, name), json);

    [Fact]
    public void A_listed_change_passes_and_is_still_shown()
    {
        Write("42-visible-changes.json", """
            { "issue": 42, "reason": "Buttons now read 'Send'.", "pages": [ { "host": "*", "path": "/contact" } ] }
            """);
        var allow = VisibleChangeAllowList.Load(_directory);

        var result = ExpectationComparer.Compare(Expect(Page("/contact", "Submit")), Expect(Page("/contact", "Send")), allow);

        Assert.True(result.Passed);
        var shown = Assert.Single(result.Allowed);
        Assert.Contains("#42", shown, StringComparison.Ordinal);
        Assert.Contains("+ Send", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_ending_in_a_star_matches_below_it_and_hosts_match_exactly()
    {
        Write("7-visible-changes.json", """
            { "issue": 7, "reason": "Dates moved.", "pages": [ { "host": "release-0-1-0.example.test", "path": "/news/*" } ] }
            """);
        var allow = VisibleChangeAllowList.Load(_directory);

        Assert.True(allow.Allows(Page("/news/one", "x")));
        Assert.True(allow.Allows(Page("/news/a/b", "x")));
        Assert.False(allow.Allows(Page("/newsletter", "x")));
        Assert.False(allow.Allows(Page("/news/one", "x", host: "other.example.test")));
    }

    [Fact]
    public void An_unlisted_change_still_fails()
    {
        Write("42-visible-changes.json", """{ "issue": 42, "reason": "r", "pages": [ { "host": "*", "path": "/contact" } ] }""");
        var allow = VisibleChangeAllowList.Load(_directory);

        var result = ExpectationComparer.Compare(
            Expect(Page("/contact", "Submit"), Page("/about", "Old")),
            Expect(Page("/contact", "Send"), Page("/about", "New")),
            allow);

        var failure = Assert.Single(result.Failures);
        Assert.Contains("/about", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_that_excuses_nothing_fails_so_stale_entries_cannot_hide_regressions()
    {
        Write("42-visible-changes.json", """{ "issue": 42, "reason": "r", "pages": [ { "host": "*", "path": "/" } ] }""");
        var allow = VisibleChangeAllowList.Load(_directory);

        var result = ExpectationComparer.Compare(Expect(Page("/", "Same")), Expect(Page("/", "Same")), allow);

        var failure = Assert.Single(result.Failures);
        Assert.Contains("unused allow-list entry", failure, StringComparison.Ordinal);
        Assert.Contains("#42", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_for_pages_this_snapshot_does_not_hold_does_not_apply()
    {
        Write("42-visible-changes.json", """{ "issue": 42, "reason": "r", "pages": [ { "host": "*", "path": "/added-later" } ] }""");
        var allow = VisibleChangeAllowList.Load(_directory);

        Assert.True(ExpectationComparer.Compare(Expect(Page("/", "Same")), Expect(Page("/", "Same")), allow).Passed);
    }

    [Fact]
    public void A_changed_status_is_never_excused()
    {
        Write("42-visible-changes.json", """{ "issue": 42, "reason": "r", "pages": [ { "host": "*", "path": "/contact" } ] }""");
        var allow = VisibleChangeAllowList.Load(_directory);

        var result = ExpectationComparer.Compare(Expect(Page("/contact", "Submit")), Expect(Page("/contact", "Submit", status: 404)), allow);

        Assert.Contains(result.Failures, f => f.Contains("status 200 became 404", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "issue": 42, "pages": [ { "host": "*", "path": "/x" } ] }""")]
    [InlineData("""{ "issue": 42, "reason": "r", "pages": [] }""")]
    [InlineData("""{ "issue": 42, "reason": "r", "pages": [ { "host": "*" } ] }""")]
    [InlineData("not json")]
    public void A_malformed_file_is_refused_by_name(string json)
    {
        Write("42-visible-changes.json", json);

        var error = Assert.Throws<InvalidDataException>(() => VisibleChangeAllowList.Load(_directory));
        Assert.Contains("42-visible-changes.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_folder_or_no_files_means_nothing_is_allowed()
    {
        Assert.Empty(VisibleChangeAllowList.Load(Path.Combine(_directory, "missing")).Entries);
        Assert.Empty(VisibleChangeAllowList.Load(_directory).Entries);
    }
}
