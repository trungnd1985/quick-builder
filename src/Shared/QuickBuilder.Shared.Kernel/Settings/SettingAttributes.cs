namespace QuickBuilder.Shared.Kernel.Settings;

/// <summary>
/// Marks an options class as one configuration section an operator can set (026 contracts/
/// setting-attributes.md). Read only by the documentation tool; the product never reads it.
/// </summary>
/// <param name="key">The section key; must equal the class's <c>SectionName</c> constant.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SettingsSectionAttribute(string key) : Attribute
{
    /// <summary>The section key, e.g. <c>Database</c> or <c>Email:Smtp</c>.</summary>
    public string Key { get; } = key;

    /// <summary>The reference area the section is listed under (026 data-model §2).</summary>
    public required string Area { get; init; }

    /// <summary>The heading shown above the section's settings.</summary>
    public required string Title { get; init; }
}

/// <summary>
/// Describes one setting to operators. The name, type, default, required flag and range are
/// derived from the property and its validation attributes, never repeated here.
/// </summary>
/// <param name="description">Plain-language description for operators.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SettingAttribute(string description) : Attribute
{
    /// <summary>Plain-language description for operators.</summary>
    public string Description { get; } = description;

    /// <summary>True for passwords, keys and connection strings.</summary>
    public bool Secret { get; init; }

    /// <summary>The documented default, only when the product computes it at run time.</summary>
    public string? DefaultText { get; init; }

    /// <summary>A human description of the accepted format, required with a regular expression.</summary>
    public string? Format { get; init; }

    /// <summary>
    /// The accepted values when a validator, not an attribute, enforces them, e.g.
    /// <c>5 minutes – 24 hours</c>.
    /// </summary>
    public string? Allowed { get; init; }

    /// <summary>
    /// For a list or map: the placeholder after the key, e.g. <c>n</c> gives
    /// <c>Hosting:KnownProxies:&lt;n&gt;</c>. Empty documents the bare key. Defaults to
    /// <c>n</c> for lists and <c>name</c> for maps.
    /// </summary>
    public string? EntryName { get; init; }

    /// <summary>When the setting applies, e.g. <c>Storage:Provider = S3</c>.</summary>
    public string? AppliesWhen { get; init; }

    /// <summary>Why starting the application does not read this setting, when it does not.</summary>
    public string? ObservedLazily { get; init; }
}

/// <summary>Marks a property holding a nested group of settings.</summary>
/// <param name="title">The heading shown above the group's settings.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SettingsGroupAttribute(string title) : Attribute
{
    /// <summary>The heading shown above the group's settings.</summary>
    public string Title { get; } = title;

    /// <summary>When every setting in the group applies, e.g. <c>Storage:Provider = S3</c>.</summary>
    public string? AppliesWhen { get; init; }
}

/// <summary>Marks a bound property that is not an operator setting.</summary>
/// <param name="reason">Why it is not documented.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NotASettingAttribute(string reason) : Attribute
{
    /// <summary>Why it is not documented.</summary>
    public string Reason { get; } = reason;
}

/// <summary>Marks a setting kept for compatibility that will be removed.</summary>
/// <param name="removeIn">The release that removes it, e.g. <c>2.0.0</c>.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DeprecatedSettingAttribute(string removeIn) : Attribute
{
    /// <summary>The release that removes it.</summary>
    public string RemoveIn { get; } = removeIn;

    /// <summary>The setting to use instead, when there is one.</summary>
    public string? Replacement { get; init; }
}
