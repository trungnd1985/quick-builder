using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Shared.Infrastructure.Schemas;

namespace QuickBuilder.UnitTests.Schemas;

/// <summary>
/// Old documents are brought to the current version in memory, step by step, and never
/// changed in place (019 contracts/schema-upgrades.md §2, FR-021 – FR-026).
/// </summary>
/// <remarks>
/// Every real contract is at version 1, so a test-only contract at version 3 proves the chain:
/// v1 had <c>title</c>, v2 renamed it to <c>heading</c>, v3 wrapped it in <c>content</c>.
/// </remarks>
public sealed class SchemaUpgradeChainTests
{
    private sealed class RenameTitle : ISchemaUpgradeStep
    {
        public int From => 1;

        public JsonObject Upgrade(JsonObject document)
        {
            document["heading"] = document["title"]?.DeepClone();
            document.Remove("title");
            document["schemaVersion"] = 2;
            return document;
        }
    }

    private sealed class WrapContent : ISchemaUpgradeStep
    {
        public int From => 2;

        public JsonObject Upgrade(JsonObject document)
        {
            var heading = document["heading"]?.DeepClone();
            document.Remove("heading");
            document["content"] = new JsonObject { ["heading"] = heading };
            document["schemaVersion"] = 3;
            return document;
        }
    }

    private sealed class Throws(int from) : ISchemaUpgradeStep
    {
        public int From => from;

        public JsonObject Upgrade(JsonObject document) => throw new InvalidOperationException("broken step");
    }

    private sealed class ForgetsTheVersion : ISchemaUpgradeStep
    {
        public int From => 1;

        public JsonObject Upgrade(JsonObject document) => document;
    }

    private static readonly SchemaUpgradeChain Chain = new("test-contract", 3, [new WrapContent(), new RenameTitle()]);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_current_document_comes_back_as_it_is()
    {
        var document = Parse("""{"schemaVersion":3,"content":{"heading":"Hi"}}""");

        var read = Assert.IsType<SchemaRead.Current>(Chain.Read(document, 3));

        Assert.True(JsonElement.DeepEquals(document, read.Document));
    }

    [Fact]
    public void An_older_document_is_upgraded_through_every_step_in_order()
    {
        var read = Assert.IsType<SchemaRead.Upgraded>(Chain.Read(Parse("""{"schemaVersion":1,"title":"Hi"}"""), 1));

        Assert.Equal(1, read.FromVersion);
        Assert.True(JsonElement.DeepEquals(Parse("""{"schemaVersion":3,"content":{"heading":"Hi"}}"""), read.Document));
    }

    [Fact]
    public void A_middle_version_runs_only_the_steps_after_it()
    {
        var read = Assert.IsType<SchemaRead.Upgraded>(Chain.Read(Parse("""{"schemaVersion":2,"heading":"Hi"}"""), 2));

        Assert.Equal(2, read.FromVersion);
        Assert.Equal("Hi", read.Document.GetProperty("content").GetProperty("heading").GetString());
    }

    [Fact]
    public void Reading_never_changes_the_stored_document()
    {
        var stored = Parse("""{"schemaVersion":1,"title":"Hi"}""");
        var before = stored.GetRawText();

        _ = Chain.Read(stored, 1);

        Assert.Equal(before, stored.GetRawText());
    }

    [Fact]
    public void A_newer_document_is_reported_and_left_alone()
    {
        var read = Assert.IsType<SchemaRead.TooNew>(Chain.Read(Parse("""{"schemaVersion":4}"""), 4));

        Assert.Equal(4, read.StoredVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_version_below_one_fails(int stored)
    {
        Assert.IsType<SchemaRead.Failed>(Chain.Read(Parse("""{"schemaVersion":0}"""), stored));
    }

    [Fact]
    public void A_step_that_throws_fails_that_document_only()
    {
        var chain = new SchemaUpgradeChain("test-contract", 2, [new Throws(1)]);

        var read = Assert.IsType<SchemaRead.Failed>(chain.Read(Parse("""{"schemaVersion":1}"""), 1));

        Assert.Equal(1, read.StoredVersion);
        Assert.Contains("1 to 2", read.Reason, StringComparison.Ordinal);
        Assert.IsType<SchemaRead.Current>(chain.Read(Parse("""{"schemaVersion":2}"""), 2));
    }

    [Fact]
    public void A_step_must_set_the_next_version()
    {
        var chain = new SchemaUpgradeChain("test-contract", 2, [new ForgetsTheVersion()]);

        Assert.IsType<SchemaRead.Failed>(chain.Read(Parse("""{"schemaVersion":1}"""), 1));
    }

    [Fact]
    public void A_document_that_is_not_an_object_fails()
    {
        Assert.IsType<SchemaRead.Failed>(Chain.Read(Parse("[1,2]"), 1));
    }

    [Fact]
    public void Missing_or_duplicate_steps_are_refused_when_the_chain_is_built()
    {
        Assert.Throws<ArgumentException>(() => new SchemaUpgradeChain("c", 3, [new RenameTitle()]));
        Assert.Throws<ArgumentException>(() => new SchemaUpgradeChain("c", 2, [new RenameTitle(), new ForgetsTheVersion()]));
        Assert.Throws<ArgumentException>(() => new SchemaUpgradeChain("c", 2, [new WrapContent()]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaUpgradeChain("c", 0, []));
    }

    [Fact]
    public void A_version_one_contract_needs_no_steps()
    {
        var chain = new SchemaUpgradeChain("page-schema", 1, []);

        Assert.Equal(1, chain.Current);
        Assert.Empty(chain.Steps);
        Assert.IsType<SchemaRead.Current>(chain.Read(Parse("""{"schemaVersion":1}"""), 1));
    }

    [Fact]
    public void A_contract_may_keep_its_version_under_another_name()
    {
        var chain = new SchemaUpgradeChain("binding-expression", 1, [], versionProperty: "bindingVersion");

        Assert.Equal("bindingVersion", chain.VersionProperty);
        Assert.IsType<SchemaRead.TooNew>(chain.Read(Parse("""{"bindingVersion":2}"""), 2));
    }
}
