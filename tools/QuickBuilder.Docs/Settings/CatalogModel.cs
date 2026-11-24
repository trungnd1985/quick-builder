using System.Reflection;

namespace QuickBuilder.Docs.Settings;

/// <summary>One configuration section an operator can set (026 data-model §1).</summary>
internal sealed record SettingsSection(
    string Key,
    string Area,
    string Title,
    Type Type,
    IReadOnlyList<SettingDefinition> Settings);

/// <summary>One documented setting, derived from an annotated property (026 data-model §1).</summary>
internal sealed record SettingDefinition(
    string Key,
    string EnvironmentName,
    string SectionKey,
    string? Group,
    string? GroupAppliesWhen,
    string Type,
    string Default,
    bool DefaultIsComputed,
    bool Required,
    string Allowed,
    string Description,
    bool Secret,
    string? AppliesWhen,
    string? DeprecatedReplacement,
    string? DeprecatedRemoveIn,
    string? ObservedLazily,
    bool IsCollection,
    PropertyInfo Property)
{
    /// <summary>
    /// The configuration path an observer sees for this setting: the key without the
    /// <c>:&lt;n&gt;</c> or <c>:&lt;name&gt;</c> placeholder of a list or map.
    /// </summary>
    public string Path { get; init; } = Key;

    /// <summary>The HTML anchor of the setting's row, e.g. <c>database-automigrate</c>.</summary>
    public string Anchor => AnchorFor(Key);

    /// <summary>The anchor for a key: lower case, <c>:</c> to <c>-</c>, placeholders dropped.</summary>
    public static string AnchorFor(string key) =>
        string.Join('-', key.Split(':').Where(s => !s.StartsWith('<')).Select(s => s.ToLowerInvariant()));
}

/// <summary>A configuration key that is read but deliberately not documented (FR-016).</summary>
internal sealed record Exclusion(string Pattern, string Reason, string Kind)
{
    /// <summary>True when <paramref name="key"/> matches this exclusion, ignoring case as configuration does.</summary>
    public bool Matches(string key) =>
        Pattern.EndsWith('*')
            ? key.StartsWith(Pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(key, Pattern, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The whole catalog, and every rule it breaks.</summary>
internal sealed record Catalog(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<Exclusion> Exclusions,
    IReadOnlyList<string> Problems)
{
    /// <summary>Every setting, in reference order.</summary>
    public IEnumerable<SettingDefinition> Settings => Sections.SelectMany(s => s.Settings);

    /// <summary>
    /// True when <paramref name="key"/> (as an observer sees it) is a declared setting, an entry
    /// of a declared list or map, or a prefix of one.
    /// </summary>
    public bool Declares(string key) =>
        Settings.Any(s =>
            string.Equals(s.Path, key, StringComparison.OrdinalIgnoreCase)
            || (s.IsCollection && key.StartsWith(s.Path + ":", StringComparison.OrdinalIgnoreCase)));

    /// <summary>True when <paramref name="key"/> is a section or group path above declared settings.</summary>
    public bool IsParentPath(string key) =>
        Settings.Any(s => s.Path.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
}
