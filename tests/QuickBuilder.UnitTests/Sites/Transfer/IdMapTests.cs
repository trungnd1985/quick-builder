using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// The id map: new ids for a new site, original ids for a restore, policy outcomes for a merge,
/// the fingerprint, and remapping inside documents (020 research ST-08, ST-09; data-model §6).
/// </summary>
public sealed partial class IdMapTests
{
    private static readonly Guid Page = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f");
    private static readonly Guid Item = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");

    [Fact]
    public void A_new_site_gives_every_declared_id_a_new_one_and_resolves_nothing_else()
    {
        var map = new IdMap(ImportMode.NewSite, policy: null);
        map.Declare(ArchiveKinds.Page, Page, "About");

        var target = map.Resolve(ArchiveKinds.Page, Page);

        Assert.NotNull(target);
        Assert.NotEqual(Page, target);
        Assert.Equal(IdOutcome.New, map.Outcome(ArchiveKinds.Page, Page));
        Assert.Null(map.Resolve(ArchiveKinds.Item, Page));
        Assert.Null(map.Resolve(ArchiveKinds.Page, Item));
    }

    [Fact]
    public void A_restore_keeps_every_original_id()
    {
        var map = new IdMap(ImportMode.Restore, policy: null);
        map.Declare(ArchiveKinds.Page, Page, "About");
        map.Match(ArchiveKinds.Page, Page, Guid.NewGuid(), MatchedBy.Identity);

        Assert.Equal(Page, map.Resolve(ArchiveKinds.Page, Page));
        Assert.Equal(IdOutcome.Original, map.Outcome(ArchiveKinds.Page, Page));
    }

    [Theory]
    [InlineData(ConflictPolicy.Replace, IdOutcome.Replaced, true)]
    [InlineData(ConflictPolicy.Skip, IdOutcome.Skipped, true)]
    [InlineData(ConflictPolicy.Rename, IdOutcome.Renamed, false)]
    public void A_merge_match_follows_the_policy(ConflictPolicy policy, IdOutcome outcome, bool resolvesToTarget)
    {
        var map = new IdMap(ImportMode.Merge, policy);
        var existing = Guid.NewGuid();
        map.Declare(ArchiveKinds.Page, Page, "About");

        map.Match(ArchiveKinds.Page, Page, existing, MatchedBy.Address);

        Assert.Equal(outcome, map.Outcome(ArchiveKinds.Page, Page));
        Assert.Equal(resolvesToTarget, map.Resolve(ArchiveKinds.Page, Page) == existing);
        Assert.NotEqual(Page, map.Resolve(ArchiveKinds.Page, Page));
    }

    [Fact]
    public void An_identical_media_file_is_reused_whatever_the_policy()
    {
        var map = new IdMap(ImportMode.Merge, ConflictPolicy.Rename);
        var existing = Guid.NewGuid();
        map.Declare(ArchiveKinds.MediaFile, Item, "logo.png");

        map.Reuse(ArchiveKinds.MediaFile, Item, existing);

        Assert.Equal((existing, IdOutcome.Reused), (map.Resolve(ArchiveKinds.MediaFile, Item)!.Value, map.Outcome(ArchiveKinds.MediaFile, Item)!.Value));
    }

    [Fact]
    public void Declaring_an_id_twice_or_matching_an_undeclared_one_fails()
    {
        var map = new IdMap(ImportMode.Merge, ConflictPolicy.Skip);
        map.Declare(ArchiveKinds.Page, Page, "About");

        Assert.Throws<ArchiveFormatException>(() => map.Declare(ArchiveKinds.Item, Page, "Same id, other kind"));
        Assert.Throws<InvalidOperationException>(() => map.Match(ArchiveKinds.Item, Item, Guid.NewGuid(), MatchedBy.Slug));
    }

    [Fact]
    public void An_unmapped_reference_stays_as_it_is_and_is_reported_broken()
    {
        var map = new IdMap(ImportMode.NewSite, policy: null);
        var broken = new List<(string, Guid)>();

        Assert.Equal(Item, map.Remap(ArchiveKinds.Item, Item, (k, id) => broken.Add((k, id))));
        Assert.Equal([(ArchiveKinds.Item, Item)], broken);
    }

    [Fact]
    public void The_fingerprint_is_stable_for_equal_matches_and_ignores_fresh_ids()
    {
        IdMap Build(Guid target)
        {
            var map = new IdMap(ImportMode.Merge, ConflictPolicy.Replace);
            map.Declare(ArchiveKinds.Page, Page, "About");
            map.Declare(ArchiveKinds.Item, Item, "hello");
            map.Match(ArchiveKinds.Page, Page, target, MatchedBy.Address);
            return map;
        }

        var target = Guid.NewGuid();
        Assert.Equal(Build(target).Fingerprint(), Build(target).Fingerprint());
        Assert.NotEqual(Build(target).Fingerprint(), Build(Guid.NewGuid()).Fingerprint());
    }

    [Fact]
    public void Remapping_a_document_replaces_declared_ids_in_either_spelling_and_nothing_else()
    {
        var map = new IdMap(ImportMode.NewSite, policy: null);
        map.Declare(ArchiveKinds.Page, Page, "About");
        map.Declare(ArchiveKinds.Item, Item, "hello");
        var other = Guid.NewGuid();
        var json = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"OTHER","type":"button","props":{"link":{"pageId":"PAGE"},"item":"ITEM","note":"PAGE in text"},"children":[]}]}}"""
            .Replace("OTHER", other.ToString(), StringComparison.Ordinal)
            .Replace("PAGE", Page.ToString(), StringComparison.Ordinal)
            .Replace("ITEM", Item.ToString("N"), StringComparison.Ordinal);
        var document = JsonDocument.Parse(json).RootElement;

        var remapped = JsonDocument.Parse(map.RemapDocument(document)).RootElement.GetRawText();

        Assert.Contains(map.Resolve(ArchiveKinds.Page, Page)!.Value.ToString(), remapped, StringComparison.Ordinal);
        Assert.Contains(map.Resolve(ArchiveKinds.Item, Item)!.Value.ToString("N"), remapped, StringComparison.Ordinal);
        Assert.Contains(other.ToString(), remapped, StringComparison.Ordinal);
        Assert.Contains($"{Page} in text", remapped, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every golden document of every content contract (page schema, menu, collection item
    /// values, design tokens) remaps losslessly: declared ids, wherever they appear, go to their
    /// targets, and every other value, node and field ids included, is unchanged (T038).
    /// </summary>
    [Theory]
    [MemberData(nameof(ContractExamples))]
    public void Every_contract_example_remaps_only_its_declared_ids(string file)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), file)));
        var text = source.RootElement.GetRawText();
        var ids = Uuid().Matches(text).Select(m => Guid.Parse(m.Value)).Distinct().ToList();
        var declared = ids.Where((_, i) => i % 2 == 0).ToHashSet();
        var map = new IdMap(ImportMode.NewSite, policy: null);
        foreach (var id in declared)
        {
            map.Declare(ArchiveKinds.Page, id, "declared");
        }

        var remapped = JsonDocument.Parse(map.RemapDocument(source.RootElement)).RootElement.GetRawText();

        foreach (var id in ids)
        {
            if (declared.Contains(id))
            {
                Assert.DoesNotContain(id.ToString(), remapped, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(map.Resolve(ArchiveKinds.Page, id)!.Value.ToString(), remapped, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Contains(id.ToString(), remapped, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Structure and every value that is not an id are kept exactly.
        Assert.Equal(StripUuids(text), StripUuids(remapped));
    }

    public static TheoryData<string> ContractExamples()
    {
        var data = new TheoryData<string>();
        var root = RepositoryRoot();
        foreach (var contract in new[] { "page-schema", "menu", "design-tokens" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "docs", "contracts", contract, "examples", "valid"), "*.json").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetRelativePath(root, file));
            }
        }

        foreach (var file in Directory.GetFiles(Path.Combine(root, "docs", "contracts", "collection-schema", "examples", "items"), "valid-*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(root, file));
        }

        return data;
    }

    private static string StripUuids(string json) =>
        Uuid().Replace(System.Text.Json.Nodes.JsonNode.Parse(json)!.ToJsonString(), "<id>");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "contracts")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("docs/contracts was not found above the test output.");
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Uuid();
}

/// <summary>Matching and the fingerprint across modes and policies (T069).</summary>
public sealed partial class IdMapTests
{
    [Fact]
    public void The_fingerprint_does_not_depend_on_the_policy_chosen_after_the_check()
    {
        var target = Guid.NewGuid();
        string Of(ConflictPolicy? policy)
        {
            var map = new IdMap(ImportMode.Merge, policy);
            map.Declare(ArchiveKinds.Page, Page, "About");
            map.Match(ArchiveKinds.Page, Page, target, MatchedBy.Address);
            return map.Fingerprint();
        }

        Assert.Equal(Of(null), Of(ConflictPolicy.Replace));
        Assert.Equal(Of(null), Of(ConflictPolicy.Skip));
        Assert.Equal(Of(null), Of(ConflictPolicy.Rename));
    }

    [Fact]
    public void The_fingerprint_covers_noted_facts_and_how_each_id_matched()
    {
        var target = Guid.NewGuid();
        string Of(MatchedBy by, params string[] notes)
        {
            var map = new IdMap(ImportMode.Restore, policy: null);
            map.Declare(ArchiveKinds.Page, Page, "About");
            map.Match(ArchiveKinds.Page, Page, target, by);
            foreach (var note in notes)
            {
                map.Note(note);
            }

            return map.Fingerprint();
        }

        Assert.Equal(Of(MatchedBy.Identity, "remove:page:a", "remove:page:b"), Of(MatchedBy.Identity, "remove:page:b", "remove:page:a"));
        Assert.NotEqual(Of(MatchedBy.Identity, "remove:page:a"), Of(MatchedBy.Identity));
        Assert.NotEqual(Of(MatchedBy.Identity), Of(MatchedBy.Address));
    }

    [Fact]
    public void A_merge_without_a_match_gives_a_new_id_and_a_check_without_a_policy_records_the_match_only()
    {
        var map = new IdMap(ImportMode.Merge, policy: null);
        var target = Guid.NewGuid();
        map.Declare(ArchiveKinds.Item, Item, "hello");
        map.Declare(ArchiveKinds.Page, Page, "About");

        map.Match(ArchiveKinds.Page, Page, target, MatchedBy.Address);

        Assert.Equal(IdOutcome.New, map.Outcome(ArchiveKinds.Item, Item));
        Assert.NotEqual(Item, map.Resolve(ArchiveKinds.Item, Item));
        Assert.Equal(IdOutcome.New, map.Outcome(ArchiveKinds.Page, Page));
        Assert.Equal([(Page, ArchiveKinds.Page, "About", target, MatchedBy.Address)], map.Matches().ToList());
    }
}
