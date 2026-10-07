using QuickBuilder.Cms.Domain;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// The rules of the collection schema beyond its JSON Schema, in the order contract §5 gives
/// (research CS-04).
/// </summary>
public sealed class CollectionSchemaRulesTests
{
    private static Field Text(string id, string key, bool required = false, bool multiline = false) =>
        new() { Id = id, Key = key, Name = key, Required = required, Type = FieldType.Text, Multiline = multiline, MaxLength = 256 };

    private static Field Flag(string id, string key) => new() { Id = id, Key = key, Name = key, Type = FieldType.Boolean };

    private static Field Choice(string id, string key, params FieldOption[] options) =>
        new() { Id = id, Key = key, Name = key, Type = FieldType.Option, Options = options };

    private static CollectionSchemaDocument Doc(string? title, params Field[] fields) => new() { TitleFieldId = title, Fields = fields };

    private static string? RuleOf(CollectionSchemaDocument document, SchemaDocumentKind kind = SchemaDocumentKind.Stored) =>
        CollectionSchemaRules.Check(document, kind)?.Rule;

    [Fact]
    public void A_valid_schema_breaks_no_rule()
    {
        Assert.Null(RuleOf(Doc("f1", Text("f1", "title", required: true), Flag("f2", "featured"))));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("slug")]
    [InlineData("status")]
    [InlineData("created-at")]
    [InlineData("updated-at")]
    [InlineData("published-at")]
    [InlineData("collection")]
    [InlineData("site")]
    [InlineData("schema-version")]
    public void Every_reserved_key_is_refused(string key)
    {
        var failure = CollectionSchemaRules.Check(Doc(null, Flag("f1", key)), SchemaDocumentKind.Stored);

        Assert.Equal(("reserved_key", "/fields/0/key"), (failure!.Rule, failure.Pointer));
    }

    [Fact]
    public void Title_is_not_reserved()
    {
        Assert.Null(RuleOf(Doc("f1", Text("f1", "title", required: true))));
    }

    [Fact]
    public void Rules_run_in_contract_order_each_over_every_field()
    {
        // A reserved key on the first field and a duplicate key later: the duplicate comes first.
        var failure = CollectionSchemaRules.Check(
            Doc(null, Flag("f1", "slug"), Flag("f2", "a"), Flag("f3", "a")), SchemaDocumentKind.Stored);

        Assert.Equal(("duplicate_key", "/fields/2/key"), (failure!.Rule, failure.Pointer));
    }

    [Fact]
    public void Option_labels_are_compared_ignoring_case()
    {
        var failure = CollectionSchemaRules.Check(
            Doc(null, Choice("f1", "kind", new FieldOption("o1", "Guides"), new FieldOption("o2", "GUIDES"))), SchemaDocumentKind.Stored);

        Assert.Equal(("duplicate_option_label", "/fields/0/options/1/label"), (failure!.Rule, failure.Pointer));
    }

    [Fact]
    public void Two_options_with_one_id_are_refused()
    {
        Assert.Equal("duplicate_option_id", RuleOf(Doc(null, Choice("f1", "kind", new FieldOption("o1", "A"), new FieldOption("o1", "B")))));
    }

    [Fact]
    public void A_minimum_above_the_maximum_is_refused()
    {
        var field = new Field { Id = "f1", Key = "age", Name = "Age", Type = FieldType.Number, Min = 10, Max = 5 };

        Assert.Equal("min_above_max", RuleOf(Doc(null, field)));
        Assert.Null(RuleOf(Doc(null, field with { Min = 5, Max = 5 })));
    }

    [Fact]
    public void The_title_field_must_be_a_required_single_line_text_field_that_exists()
    {
        Assert.Equal("invalid_title_field", RuleOf(Doc("f1", Flag("f1", "featured"))));
        Assert.Equal("invalid_title_field", RuleOf(Doc("f1", Text("f1", "body", required: true, multiline: true))));
        Assert.Equal("invalid_title_field", RuleOf(Doc("f1", Text("f1", "title"))));
        Assert.Equal("invalid_title_field", RuleOf(Doc("f9", Text("f1", "title", required: true))));
    }

    [Fact]
    public void A_stored_schema_with_a_single_line_text_field_must_name_its_title_field()
    {
        Assert.Equal("missing_title_field", RuleOf(Doc(null, Text("f1", "title", required: true))));
        Assert.Null(RuleOf(Doc(null, Text("f1", "body", multiline: true))));
    }

    [Fact]
    public void A_proposal_is_normalised_so_the_first_single_line_text_field_names_items_and_is_required()
    {
        var proposal = Doc(null, Flag("~1", "featured"), Text("~2", "headline"), Text("~3", "other")).Normalise();

        Assert.Equal("~2", proposal.TitleFieldId);
        Assert.True(proposal.Find("~2")!.Required);
        Assert.Null(RuleOf(proposal, SchemaDocumentKind.Proposal));
    }

    [Fact]
    public void Temporary_ids_are_refused_in_a_stored_schema_only()
    {
        var withTemporary = Doc(null, Flag("~1", "featured"), Choice("f2", "kind", new FieldOption("~1", "A")));

        Assert.Equal("temporary_id_in_stored", RuleOf(withTemporary));
        Assert.Null(RuleOf(withTemporary, SchemaDocumentKind.Proposal));
    }

    [Fact]
    public void A_temporary_option_id_has_its_own_pointer()
    {
        var failure = CollectionSchemaRules.Check(Doc(null, Choice("f1", "kind", new FieldOption("~1", "A"))), SchemaDocumentKind.Stored);

        Assert.Equal("/fields/0/options/0/id", failure!.Pointer);
    }
}
