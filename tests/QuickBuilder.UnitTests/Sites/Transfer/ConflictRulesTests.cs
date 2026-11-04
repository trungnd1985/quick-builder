using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// What a merge does with a matched document, per kind and policy, and how renamed documents
/// are named (020 FR-022 … FR-026, research ST-10).
/// </summary>
public sealed class ConflictRulesTests
{
    public static TheoryData<string, ConflictPolicy, IdOutcome> Outcomes()
    {
        var data = new TheoryData<string, ConflictPolicy, IdOutcome>();
        foreach (var kind in new[] { ArchiveKinds.Page, ArchiveKinds.Component, ArchiveKinds.Collection, ArchiveKinds.Item, ArchiveKinds.Menu })
        {
            data.Add(kind, ConflictPolicy.Replace, IdOutcome.Replaced);
            data.Add(kind, ConflictPolicy.Skip, IdOutcome.Skipped);
            data.Add(kind, ConflictPolicy.Rename, IdOutcome.Renamed);
        }

        // Redirect sources are unique by rule (017); settings and tokens exist once per site.
        foreach (var kind in new[] { ArchiveKinds.Redirect, ArchiveKinds.Design, ArchiveKinds.Site })
        {
            data.Add(kind, ConflictPolicy.Replace, IdOutcome.Replaced);
            data.Add(kind, ConflictPolicy.Skip, IdOutcome.Skipped);
            data.Add(kind, ConflictPolicy.Rename, IdOutcome.Skipped);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void Each_kind_follows_its_policy(string kind, ConflictPolicy policy, IdOutcome outcome) =>
        Assert.Equal(outcome, ConflictRules.Outcome(kind, policy));

    [Fact]
    public void A_redirect_under_rename_is_skipped_and_points_to_the_target()
    {
        var map = new IdMap(ImportMode.Merge, ConflictPolicy.Rename);
        var archive = Guid.NewGuid();
        var target = Guid.NewGuid();
        map.Declare(ArchiveKinds.Redirect, archive, "/old");

        map.Match(ArchiveKinds.Redirect, archive, target, MatchedBy.Source);

        Assert.Equal((IdOutcome.Skipped, target), (map.Outcome(ArchiveKinds.Redirect, archive)!.Value, map.Resolve(ArchiveKinds.Redirect, archive)!.Value));
    }

    [Fact]
    public void Slugs_take_the_first_free_number_from_two()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "about", "about-2", "about-3" };

        Assert.Equal("about-4", ConflictRules.NextSlug("about", taken.Contains));
        Assert.Equal("news-2", ConflictRules.NextSlug("news", taken.Contains));
    }

    [Fact]
    public void Names_are_marked_imported_then_numbered()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "Footer", "Footer (imported)" };

        Assert.Equal("Header (imported)", ConflictRules.ImportedName("Header", taken.Contains));
        Assert.Equal("Footer (imported) 2", ConflictRules.ImportedName("Footer", taken.Contains));
    }

    [Fact]
    public void A_rename_that_never_finds_a_free_name_fails_rather_than_overwriting()
    {
        Assert.Throws<InvalidOperationException>(() => ConflictRules.NextSlug("a", _ => true));
        Assert.Throws<InvalidOperationException>(() => ConflictRules.ImportedName("a", _ => true));
    }

    [Fact]
    public void An_identity_match_that_also_clashes_by_address_is_one_conflict_against_the_identity_match()
    {
        // FR-022a: from the same site, identity wins; the address clash with another page is
        // reported beside it, not as a second match.
        var map = new IdMap(ImportMode.Merge, ConflictPolicy.Replace);
        var archive = Guid.NewGuid();
        map.Declare(ArchiveKinds.Page, archive, "About");

        map.Match(ArchiveKinds.Page, archive, archive, MatchedBy.Identity);

        Assert.Equal((IdOutcome.Replaced, archive), (map.Outcome(ArchiveKinds.Page, archive)!.Value, map.Resolve(ArchiveKinds.Page, archive)!.Value));
        Assert.Single(map.Matches());
    }
}
