using QuickBuilder.Docs.Settings;
using static QuickBuilder.UnitTests.Docs.CatalogFixtures;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// What the catalog derives instead of anyone typing it twice: keys, environment names, types,
/// defaults, allowed values and conditions (026 T007; data-model §1; research OD-02).
/// </summary>
public sealed class CatalogDerivationTests
{
    private static SettingDefinition Setting(Catalog catalog, string key) =>
        catalog.Settings.Single(s => s.Key == key);

    [Fact]
    public void Scalars_take_their_type_and_initialiser_default()
    {
        var catalog = Build(typeof(Good));

        Assert.Equal("true/false", Setting(catalog, "Good:Flag").Type);
        Assert.Equal("`true`", Setting(catalog, "Good:Flag").Default);
        Assert.Equal("whole number", Setting(catalog, "Good:Count").Type);
        Assert.Equal("duration (`[d.]hh:mm:ss`)", Setting(catalog, "Good:Wait").Type);
        Assert.Equal("`00:00:30`", Setting(catalog, "Good:Wait").Default);
        Assert.Equal("`Safe`", Setting(catalog, "Good:Choice").Default);
    }

    [Fact]
    public void The_shipped_appsettings_wins_over_the_initialiser()
    {
        var catalog = Build("""{ "Good": { "Count": 7, "Wait": "00:01:00", "Names": ["a", "b"] } }""", typeof(Good));

        Assert.Equal("`7`", Setting(catalog, "Good:Count").Default);
        Assert.Equal("`00:01:00`", Setting(catalog, "Good:Wait").Default);
        Assert.Equal("`a`, `b`", Setting(catalog, "Good:Names:<n>").Default);
    }

    [Fact]
    public void Lists_and_maps_get_placeholders_in_both_forms()
    {
        var catalog = Build(typeof(Good));

        var names = Setting(catalog, "Good:Names:<n>");
        Assert.Equal("Good__Names__0", names.EnvironmentName);
        Assert.Equal("list of text", names.Type);
        Assert.Equal("Good:Names", names.Path);

        var levels = Setting(catalog, "Good:Levels:<name>");
        Assert.Equal("Good__Levels__<name>", levels.EnvironmentName);
        Assert.Equal("map of text", levels.Type);
    }

    [Fact]
    public void Allowed_values_come_from_ranges_and_enums()
    {
        var catalog = Build(typeof(Good));

        Assert.Equal("`1` – `10`", Setting(catalog, "Good:Count").Allowed);
        Assert.Equal("`Fast`, `Safe`", Setting(catalog, "Good:Choice").Allowed);
        Assert.Equal("—", Setting(catalog, "Good:Flag").Allowed);
    }

    [Fact]
    public void Required_means_the_operator_must_set_it()
    {
        var connection = Setting(Build(typeof(Good)), "Good:ConnectionString");

        Assert.True(connection.Required);
        Assert.Equal("*(none)*", connection.Default);
        Assert.True(connection.Secret);
    }

    [Fact]
    public void A_stated_default_text_replaces_the_bound_value()
    {
        var path = Setting(Build(typeof(Good)), "Good:Path");

        Assert.Equal("next to the app", path.Default);
        Assert.True(path.DefaultIsComputed);
    }

    [Fact]
    public void Groups_pass_their_condition_to_their_settings()
    {
        var bucket = Setting(Build(typeof(Good)), "Good:Remote:Bucket");

        Assert.Equal("Remote", bucket.Group);
        Assert.Equal("Good:Choice = Fast", bucket.GroupAppliesWhen);
    }

    [Fact]
    public void Excluded_and_derived_members_are_not_settings()
    {
        var catalog = Build(typeof(Good));

        Assert.DoesNotContain(catalog.Settings, s => s.Key.Contains("Internal", StringComparison.Ordinal) || s.Key.Contains("Derived", StringComparison.Ordinal));
        Assert.Contains(catalog.Exclusions, e => e.Pattern == "Good:Internal" && e.Kind == "not-a-setting");
    }

    [Fact]
    public void Observed_keys_match_lists_and_parents_case_insensitively()
    {
        var catalog = Build(typeof(Good));

        Assert.True(catalog.Declares("good:flag"));
        Assert.True(catalog.Declares("Good:Names:0"));
        Assert.True(catalog.IsParentPath("Good:Remote"));
        Assert.False(catalog.Declares("Good:Nope"));
    }
}
