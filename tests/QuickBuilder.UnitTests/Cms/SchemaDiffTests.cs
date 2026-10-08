using QuickBuilder.Cms.Domain;
using static QuickBuilder.UnitTests.Cms.SchemaTestKit;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// A proposal compared with the stored schema by field id, and the ids it may use
/// (research CS-02, CS-06).
/// </summary>
public sealed class SchemaDiffTests
{
    private static ChangeKind[] Kinds(IEnumerable<FieldChange> changes) => [.. changes.Select(c => c.Kind)];

    [Fact]
    public void The_same_schema_and_details_differ_in_nothing()
    {
        var schema = Doc(Text("f1"), Of(FieldType.Boolean, "f2"));

        Assert.Empty(Diff(schema, schema));
    }

    [Fact]
    public void Changed_details_are_one_change()
    {
        var schema = Doc(Text("f1"));
        var changes = SchemaDiff.Compare(Details, schema, Details with { Name = "Articles", Slug = "articles" }, schema, new HashSet<string>());

        Assert.Equal([ChangeKind.DetailsChanged], Kinds(changes));
    }

    [Fact]
    public void An_added_field_a_removed_field_and_a_reorder_are_told_apart()
    {
        var before = Doc(Text("f1"), Of(FieldType.Boolean, "f2"), Of(FieldType.Number, "f3"));
        var after = Doc(Of(FieldType.Number, "f3"), Text("f1"), Of(FieldType.Date, "f4"));

        Assert.Equal(
            [ChangeKind.FieldAdded, ChangeKind.FieldRemoved, ChangeKind.FieldsReordered],
            Kinds(Diff(before, after, "f4")));
    }

    [Fact]
    public void A_rename_of_name_key_or_help_keeps_the_field()
    {
        var before = Doc(Text("f1", "summary"));
        var renamed = Doc(Text("f1", "excerpt") with { Name = "Excerpt", Help = "Shown in lists." });

        var change = Assert.Single(Diff(before, renamed));
        Assert.Equal((ChangeKind.FieldRenamed, "f1"), (change.Kind, change.FieldId));
    }

    [Fact]
    public void Required_and_type_changes_are_reported_and_settings_are_not_compared_across_types()
    {
        var before = Doc(Text("f1", maxLength: 10));
        var after = Doc(Of(FieldType.Number, "f1", "ktitle") with { Required = true, Name = "Count", Key = "count" });

        Assert.Equal([ChangeKind.FieldRenamed, ChangeKind.RequiredChanged, ChangeKind.TypeChanged], Kinds(Diff(before, after)));
    }

    [Theory]
    [InlineData(Settings.Multiline)]
    [InlineData(Settings.MaxLength)]
    [InlineData(Settings.Decimals)]
    [InlineData(Settings.Min)]
    [InlineData(Settings.Max)]
    [InlineData(Settings.WithTime)]
    [InlineData(Settings.Accept)]
    [InlineData(Settings.Target)]
    [InlineData(Settings.Formats)]
    public void Each_setting_is_its_own_change(string setting)
    {
        var (before, after) = setting switch
        {
            Settings.Multiline => (Text("f1"), Text("f1", multiline: true)),
            Settings.MaxLength => (Text("f1"), Text("f1", maxLength: 99)),
            Settings.Decimals => (Of(FieldType.Number, "f1"), Of(FieldType.Number, "f1") with { Decimals = 2 }),
            Settings.Min => (Of(FieldType.Number, "f1"), Of(FieldType.Number, "f1") with { Min = 1 }),
            Settings.Max => (Of(FieldType.Number, "f1"), Of(FieldType.Number, "f1") with { Max = 9 }),
            Settings.WithTime => (Of(FieldType.Date, "f1"), Of(FieldType.Date, "f1") with { WithTime = true }),
            Settings.Accept => (Of(FieldType.File, "f1"), Of(FieldType.File, "f1") with { Accept = FileAccept.Images }),
            Settings.Formats => (Of(FieldType.RichText, "f1"), Of(FieldType.RichText, "f1") with { Formats = new RichTextFormats(Tables: false) }),
            _ => (Of(FieldType.Reference, "f1"), Of(FieldType.Reference, "f1") with { Target = Guid.NewGuid() }),
        };

        var change = Assert.Single(Diff(Doc(before), Doc(after)));
        Assert.Equal((ChangeKind.SettingsChanged, setting), (change.Kind, change.Setting));
    }

    [Fact]
    public void Option_additions_removals_relabels_and_reorders_are_told_apart()
    {
        var before = Doc(Of(FieldType.Option, "f1") with { Options = [new("o1", "News"), new("o2", "Guides"), new("o3", "Releases")] });
        var after = Doc(Of(FieldType.Option, "f1") with { Options = [new("o2", "Guides"), new("o1", "Company news"), new("o4", "Events")] });

        Assert.Equal(
            [ChangeKind.OptionAdded, ChangeKind.OptionRemoved, ChangeKind.OptionRelabelled, ChangeKind.OptionsReordered],
            Kinds(Diff(before, after)).Order());
    }

    [Fact]
    public void A_new_title_field_is_a_change()
    {
        var before = Doc(Text("f1", required: true), Text("f2", "headline", required: true)) with { TitleFieldId = "f1" };
        var after = before with { TitleFieldId = "f2" };

        var change = Assert.Single(Diff(before, after));
        Assert.Equal((ChangeKind.TitleFieldChanged, "f2"), (change.Kind, change.After!.Id));
    }

    [Fact]
    public void Temporary_ids_become_the_next_permanent_ids_in_document_order()
    {
        var proposal = Doc(Text("~2"), Of(FieldType.Option, "~1") with { Options = [new("~3", "A"), new("~4", "B")] }) with { TitleFieldId = "~2" };

        var assigned = IdAssignment.Assign(proposal, nextFieldNumber: 7, nextOptionNumber: 3);

        Assert.Equal(["f7", "f8"], assigned.Document.Fields.Select(f => f.Id));
        Assert.Equal(["o3", "o4"], assigned.Document.Fields[1].Options.Select(o => o.Id));
        Assert.Equal("f7", assigned.Document.TitleFieldId);
        Assert.Equal((9, 5), (assigned.NextFieldNumber, assigned.NextOptionNumber));
        Assert.Equal("f8", assigned.Resolve("~1"));
        Assert.Equal("f1", assigned.Resolve("f1"));
    }

    [Fact]
    public void A_permanent_id_the_collection_never_issued_or_has_removed_is_unknown()
    {
        var stored = Doc(Text("f1"));

        Assert.Equal(("cms.unknown_field_id", "/fields/1/id"), Pick(IdAssignment.FindUnknownOrDuplicate(stored, Doc(Text("f1"), Of(FieldType.Boolean, "f2", "x")))));
        Assert.Null(IdAssignment.FindUnknownOrDuplicate(stored, Doc(Text("f1"), Of(FieldType.Boolean, "~1", "x"))));
    }

    [Fact]
    public void An_option_id_the_field_does_not_have_is_unknown()
    {
        var stored = Doc(Of(FieldType.Option, "f1"));
        var proposal = Doc(Of(FieldType.Option, "f1") with { Options = [new("o1", "A"), new("o9", "B")] });

        Assert.Equal(("cms.unknown_option_id", "/fields/0/options/1/id"), Pick(IdAssignment.FindUnknownOrDuplicate(stored, proposal)));
    }

    [Fact]
    public void A_temporary_id_used_twice_across_fields_and_options_is_refused()
    {
        var proposal = Doc(Of(FieldType.Option, "~1") with { Options = [new("~1", "A")] });

        var problem = IdAssignment.FindUnknownOrDuplicate(CollectionSchemaDocument.Empty, proposal);
        Assert.Equal(("duplicate_temporary_id", "/fields/0/options/0/id"), (problem!.Value.Rule, problem.Value.Pointer));
    }

    private static (string Code, string Pointer) Pick((string Code, string Rule, string Pointer)? problem) =>
        (problem!.Value.Code, problem.Value.Pointer);
}
