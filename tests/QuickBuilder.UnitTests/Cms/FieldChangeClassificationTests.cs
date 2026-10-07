using QuickBuilder.Cms.Domain;
using static QuickBuilder.UnitTests.Cms.SchemaTestKit;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// Every change gets the rule contract §7 and data-model §3.2 give it (research CS-07).
/// </summary>
public sealed class FieldChangeClassificationTests
{
    private static readonly (FieldType From, FieldType To, ChangeRule Rule)[] Allowed =
    [
        (FieldType.Text, FieldType.RichText, ChangeRule.Lossless),
        (FieldType.RichText, FieldType.Text, ChangeRule.Lossy),
        (FieldType.Number, FieldType.Text, ChangeRule.Lossless),
        (FieldType.Text, FieldType.Number, ChangeRule.Lossy),
        (FieldType.Date, FieldType.Text, ChangeRule.Lossless),
        (FieldType.Text, FieldType.Date, ChangeRule.Lossy),
        (FieldType.Option, FieldType.Text, ChangeRule.Lossless),
        (FieldType.Text, FieldType.Option, ChangeRule.Lossy),
        (FieldType.Boolean, FieldType.Text, ChangeRule.Lossless),
        (FieldType.Image, FieldType.File, ChangeRule.Lossless),
        (FieldType.File, FieldType.Image, ChangeRule.Lossy),
        (FieldType.Reference, FieldType.MultiReference, ChangeRule.Lossless),
        (FieldType.MultiReference, FieldType.Reference, ChangeRule.Lossy),
    ];

    public static TheoryData<string, string> EveryPair()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Enum.GetValues<FieldType>())
        {
            foreach (var to in Enum.GetValues<FieldType>().Where(t => t != from))
            {
                data.Add(from.ToName(), to.ToName());
            }
        }

        return data;
    }

    private static ChangeRule RuleOf(Field before, Field after, bool fillIn = false) =>
        FieldConversion.Classify(Assert.Single(Diff(Doc(before), Doc(after)), c => c.Kind != ChangeKind.FieldRenamed), fillIn);

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_type_change_has_the_contracts_rule(string fromName, string toName)
    {
        var (from, to) = (FieldTypes.Parse(fromName), FieldTypes.Parse(toName));
        var expected = Allowed.Where(a => a.From == from && a.To == to).Select(a => a.Rule).DefaultIfEmpty(ChangeRule.Forbidden).Single();

        Assert.Equal(expected, RuleOf(Of(from, "f1", "k"), Of(to, "f1", "k")));
    }

    [Fact]
    public void An_added_field_is_free_unless_it_comes_with_a_fill_in()
    {
        var added = Assert.Single(Diff(Doc(), Doc(Of(FieldType.Boolean, "f1")), "f1"));

        Assert.Equal(ChangeRule.Free, FieldConversion.Classify(added));
        Assert.Equal(ChangeRule.Reported, FieldConversion.Classify(added, hasFillIn: true));
    }

    [Fact]
    public void Removals_of_fields_and_options_are_lossy()
    {
        Assert.Equal(ChangeRule.Lossy, FieldConversion.Classify(Assert.Single(Diff(Doc(Text("f1")), Doc()))));
        Assert.Equal(
            ChangeRule.Lossy,
            RuleOf(Of(FieldType.Option, "f1") with { Options = [new("o1", "A"), new("o2", "B")] }, Of(FieldType.Option, "f1")));
    }

    [Fact]
    public void Turning_required_on_is_reported_and_off_is_free()
    {
        Assert.Equal(ChangeRule.Reported, RuleOf(Text("f1"), Text("f1", required: true)));
        Assert.Equal(ChangeRule.Free, RuleOf(Text("f1", required: true), Text("f1")));
    }

    [Theory]
    [InlineData(Settings.Multiline, false, true, nameof(ChangeRule.Free))]
    [InlineData(Settings.Multiline, true, false, nameof(ChangeRule.Lossless))]
    [InlineData(Settings.MaxLength, 256, 300, nameof(ChangeRule.Free))]
    [InlineData(Settings.MaxLength, 256, 100, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Decimals, 0, 2, nameof(ChangeRule.Free))]
    [InlineData(Settings.Decimals, 2, 1, nameof(ChangeRule.Lossy))]
    [InlineData(Settings.Min, null, 1, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Min, 5, 1, nameof(ChangeRule.Free))]
    [InlineData(Settings.Min, 1, 5, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Min, 1, null, nameof(ChangeRule.Free))]
    [InlineData(Settings.Max, null, 9, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Max, 9, 5, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Max, 5, 9, nameof(ChangeRule.Free))]
    [InlineData(Settings.Max, 5, null, nameof(ChangeRule.Free))]
    [InlineData(Settings.WithTime, false, true, nameof(ChangeRule.Lossless))]
    [InlineData(Settings.WithTime, true, false, nameof(ChangeRule.Lossy))]
    [InlineData(Settings.Accept, FileAccept.Any, FileAccept.Documents, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Accept, FileAccept.Documents, FileAccept.Images, nameof(ChangeRule.Reported))]
    [InlineData(Settings.Accept, FileAccept.Images, FileAccept.Any, nameof(ChangeRule.Free))]
    public void Each_setting_change_has_the_contracts_rule(string setting, object? from, object? to, string expectedRule)
    {
        var expected = Enum.Parse<ChangeRule>(expectedRule);
        var (before, after) = setting switch
        {
            Settings.Multiline => (Text("f1", multiline: (bool)from!), Text("f1", multiline: (bool)to!)),
            Settings.MaxLength => (Text("f1", maxLength: (int)from!), Text("f1", maxLength: (int)to!)),
            Settings.Decimals => (Of(FieldType.Number, "f1") with { Decimals = (int)from! }, Of(FieldType.Number, "f1") with { Decimals = (int)to! }),
            Settings.Min => (Of(FieldType.Number, "f1") with { Min = (int?)from }, Of(FieldType.Number, "f1") with { Min = (int?)to }),
            Settings.Max => (Of(FieldType.Number, "f1") with { Max = (int?)from }, Of(FieldType.Number, "f1") with { Max = (int?)to }),
            Settings.WithTime => (Of(FieldType.Date, "f1") with { WithTime = (bool)from! }, Of(FieldType.Date, "f1") with { WithTime = (bool)to! }),
            _ => (Of(FieldType.File, "f1") with { Accept = (string)from! }, Of(FieldType.File, "f1") with { Accept = (string)to! }),
        };

        Assert.Equal(expected, RuleOf(before, after));
    }

    [Fact]
    public void A_new_reference_target_is_forbidden()
    {
        Assert.Equal(ChangeRule.Forbidden, RuleOf(Of(FieldType.Reference, "f1"), Of(FieldType.Reference, "f1") with { Target = Guid.NewGuid() }));
    }

    [Fact]
    public void Renames_reorders_relabels_new_options_and_a_new_title_field_are_free()
    {
        var before = Doc(Of(FieldType.Option, "f1") with { Options = [new("o1", "A"), new("o2", "B")] }, Text("f2", required: true)) with { TitleFieldId = "f2" };
        var after = Doc(Text("f2", "headline", required: true), Of(FieldType.Option, "f1", "kind") with { Options = [new("o2", "B"), new("o1", "Z"), new("o3", "C")] });

        Assert.All(Diff(before, after with { TitleFieldId = "f2" }), c => Assert.Equal(ChangeRule.Free, FieldConversion.Classify(c)));
    }
}
