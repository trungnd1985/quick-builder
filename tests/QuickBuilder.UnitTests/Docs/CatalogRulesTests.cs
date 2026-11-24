using static QuickBuilder.UnitTests.Docs.CatalogFixtures;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// The catalog's rules, each failure one line naming the setting and the fix (026 T006;
/// contracts/setting-attributes.md §3 rules 1–7).
/// </summary>
public sealed class CatalogRulesTests
{
    [Fact]
    public void A_well_annotated_section_breaks_no_rule()
    {
        Assert.Empty(Build(typeof(Good)).Problems);
    }

    [Fact]
    public void Rule_1_the_section_key_must_equal_SectionName()
    {
        var problem = Assert.Single(Build(typeof(Mismatch)).Problems);
        Assert.Contains("does not match its SectionName constant", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_area_is_refused()
    {
        var problem = Assert.Single(Build(typeof(UnknownArea)).Problems);
        Assert.Contains("area 'nowhere'", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Broken:Unmarked: no [Setting], [SettingsGroup] or [NotASetting]")]
    [InlineData("Broken:Empty: the description must be at least one sentence")]
    [InlineData("Broken:NoStop: the description must be at least one sentence")]
    [InlineData("Broken:Cites: the description cites a requirement or research id")]
    [InlineData("Broken:Pattern: has [RegularExpression] but no Format")]
    [InlineData("Broken:Odd: AppliesWhen 'nonsense' is not '<key> = <value>'")]
    [InlineData("Broken:Missing: AppliesWhen names 'Broken:Absent', which is not a declared setting")]
    [InlineData("Broken:SecretAccessKey: the name suggests a credential but Secret is not set")]
    [InlineData("Broken:Twice: more than one of [Setting], [SettingsGroup] and [NotASetting]")]
    public void Each_broken_rule_is_reported_with_its_setting(string expected)
    {
        var problems = Build(typeof(Broken)).Problems;

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
        Assert.All(problems, p => Assert.Contains(" — ", p, StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_prefix_is_not_mistaken_for_a_credential()
    {
        Assert.DoesNotContain(Build(typeof(Good)).Problems, p => p.Contains("KeyPrefix", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule_7_a_key_declared_twice_is_refused()
    {
        var problems = Build(typeof(DuplicateOne), typeof(DuplicateTwo)).Problems;

        Assert.Contains(problems, p => p.StartsWith("Same:Value: declared 2 times", StringComparison.Ordinal));
    }
}
