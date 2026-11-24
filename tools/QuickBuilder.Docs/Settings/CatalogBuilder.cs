using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Docs.Settings;

/// <summary>
/// Builds the setting catalog from annotated options classes (026 research OD-01, OD-02;
/// contracts/setting-attributes.md §3). Every broken rule becomes one line in
/// <see cref="Catalog.Problems"/>, in the form <c>&lt;where&gt;: &lt;problem&gt; — &lt;fix&gt;</c>.
/// </summary>
internal static partial class CatalogBuilder
{
    private const string Fix = "see docs/contributing.md#operator-docs";

    /// <summary>Builds the catalog of every <c>QuickBuilder.*</c> assembly the Host loads.</summary>
    public static Catalog BuildForProduct(string repositoryRoot) =>
        Build(ProductAssemblies.Load().SelectMany(LoadableTypes), DefaultsBinder.LoadShipped(repositoryRoot));

    /// <summary>
    /// Builds the catalog of every <see cref="SettingsSectionAttribute"/> class among
    /// <paramref name="types"/>, taking defaults from <paramref name="shipped"/>.
    /// </summary>
    public static Catalog Build(IEnumerable<Type> types, IConfiguration shipped)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(shipped);

        var problems = new List<string>();
        var exclusions = new List<Exclusion>();
        var sections = new List<SettingsSection>();

        foreach (var type in types.Where(t => t.GetCustomAttribute<SettingsSectionAttribute>() is not null))
        {
            var attribute = type.GetCustomAttribute<SettingsSectionAttribute>()!;
            if (!Areas.Exists(attribute.Area))
            {
                problems.Add($"{type.Name}: area '{attribute.Area}' is not one of the reference's areas — use one listed in tools/QuickBuilder.Docs/Settings/Areas.cs.");
                continue;
            }

            var constant = type.GetField("SectionName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (constant is not { IsLiteral: true } || constant.GetRawConstantValue() as string != attribute.Key)
            {
                problems.Add($"{type.Name}: [SettingsSection(\"{attribute.Key}\")] does not match its SectionName constant — make them equal.");
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(type, nonPublic: true)!;
                shipped.GetSection(attribute.Key).Bind(instance);
            }
            catch (Exception e) when (e is MissingMethodException or InvalidOperationException or TargetInvocationException)
            {
                problems.Add($"{type.Name}: cannot be created and bound ({e.GetBaseException().Message}) — give it a parameterless constructor.");
                continue;
            }

            var settings = new List<SettingDefinition>();
            Walk(type, instance, attribute.Key, attribute.Key, group: null, groupAppliesWhen: null, settings, exclusions, problems);
            sections.Add(new SettingsSection(attribute.Key, attribute.Area, attribute.Title, type, settings));
        }

        sections.Sort((a, b) =>
        {
            var byArea = Areas.Order(a.Area).CompareTo(Areas.Order(b.Area));
            return byArea != 0 ? byArea : string.CompareOrdinal(a.Key, b.Key);
        });

        var catalog = new Catalog(sections, [.. exclusions, .. FrameworkAllowList.Entries], problems);
        CheckAcrossSettings(catalog, problems);
        return catalog;
    }

    private static void Walk(
        Type type,
        object instance,
        string sectionKey,
        string prefix,
        string? group,
        string? groupAppliesWhen,
        List<SettingDefinition> settings,
        List<Exclusion> exclusions,
        List<string> problems)
    {
        // The configuration binder asks for every public property, settable or not; computed
        // and static ones can never be set from configuration, so they are not settings.
        foreach (var computed in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                     .Where(p => p.GetIndexParameters().Length == 0 && (p.GetMethod?.IsStatic == true || !IsBindable(p))))
        {
            exclusions.Add(new Exclusion($"{prefix}:{computed.Name}", "Computed by the product; configuration cannot set it.", "computed"));
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(IsBindable)
                     .OrderBy(p => p.MetadataToken))
        {
            var path = $"{prefix}:{property.Name}";
            var setting = property.GetCustomAttribute<SettingAttribute>();
            var nested = property.GetCustomAttribute<SettingsGroupAttribute>();
            var excluded = property.GetCustomAttribute<NotASettingAttribute>();
            var marks = (setting is null ? 0 : 1) + (nested is null ? 0 : 1) + (excluded is null ? 0 : 1);

            if (marks == 0)
            {
                problems.Add($"{path}: no [Setting], [SettingsGroup] or [NotASetting] — describe it for operators or say why it is not a setting ({Fix}).");
                continue;
            }

            if (marks > 1)
            {
                problems.Add($"{path}: more than one of [Setting], [SettingsGroup] and [NotASetting] — keep one.");
                continue;
            }

            if (excluded is not null)
            {
                exclusions.Add(new Exclusion(path, excluded.Reason, "not-a-setting"));
                continue;
            }

            var value = property.GetValue(instance);
            if (nested is not null)
            {
                var child = value ?? Activator.CreateInstance(property.PropertyType, nonPublic: true)!;
                Walk(property.PropertyType, child, sectionKey, path, nested.Title, nested.AppliesWhen ?? groupAppliesWhen, settings, exclusions, problems);
                continue;
            }

            if (Describe(property, value, path, sectionKey, group, groupAppliesWhen, setting!, problems) is { } definition)
            {
                settings.Add(definition);
            }
        }
    }

    private static bool IsBindable(PropertyInfo property) =>
        property.GetIndexParameters().Length == 0
        && property.GetMethod is { IsPublic: true }
        && (property.SetMethod is { IsPublic: true } || IsCollection(property.PropertyType));

    private static SettingDefinition? Describe(
        PropertyInfo property,
        object? value,
        string path,
        string sectionKey,
        string? group,
        string? groupAppliesWhen,
        SettingAttribute setting,
        List<string> problems)
    {
        var shape = Shape(property.PropertyType);
        var element = shape.Element;
        var elementName = TypeName(element);
        if (elementName is null)
        {
            problems.Add($"{path}: type {property.PropertyType.Name} has no operator description — use text, a number, true/false, a duration, an enum, or a list or map of them.");
            return null;
        }

        var type = shape.Kind switch
        {
            Kind.List => $"list of {elementName}",
            Kind.Map => $"map of {elementName}",
            _ => elementName,
        };

        var entry = setting.EntryName ?? (shape.Kind == Kind.Map ? "name" : "n");
        var key = shape.Kind == Kind.Scalar || entry.Length == 0 ? path : $"{path}:<{entry}>";
        var environment = path.Replace(":", "__", StringComparison.Ordinal) + shape.Kind switch
        {
            Kind.List => "__0",
            Kind.Map => $"__<{(entry.Length == 0 ? "name" : entry)}>",
            _ => string.Empty,
        };

        // Required for an operator means "you must set it": [Required] with nothing shipped.
        var required = property.GetCustomAttribute<RequiredAttribute>() is not null
                       && setting.DefaultText is null
                       && value is null or "";
        var allowed = Allowed(property, element, setting, path, problems);

        var description = setting.Description.Trim();
        if (description.Length == 0 || !description.EndsWith('.'))
        {
            problems.Add($"{path}: the description must be at least one sentence ending with '.' — write it for operators.");
        }

        if (RequirementId().IsMatch(description))
        {
            problems.Add($"{path}: the description cites a requirement or research id — operators cannot follow it; describe the effect instead.");
        }

        var secret = setting.Secret;
        var secretStated = CustomAttributeData.GetCustomAttributes(property)
            .Where(a => a.AttributeType == typeof(SettingAttribute))
            .SelectMany(a => a.NamedArguments)
            .Any(a => a.MemberName == nameof(SettingAttribute.Secret));
        if (!secretStated && LooksSecret(property.Name))
        {
            problems.Add($"{path}: the name suggests a credential but Secret is not set — add Secret = true, or Secret = false if it is not one.");
        }

        var deprecated = property.GetCustomAttribute<DeprecatedSettingAttribute>();

        return new SettingDefinition(
            Key: key,
            EnvironmentName: environment,
            SectionKey: sectionKey,
            Group: group,
            GroupAppliesWhen: groupAppliesWhen,
            Type: type,
            Default: setting.DefaultText ?? RenderDefault(value, required),
            DefaultIsComputed: setting.DefaultText is not null,
            Required: required,
            Allowed: allowed,
            Description: description,
            Secret: secret,
            AppliesWhen: setting.AppliesWhen,
            DeprecatedReplacement: deprecated?.Replacement,
            DeprecatedRemoveIn: deprecated?.RemoveIn,
            ObservedLazily: setting.ObservedLazily,
            IsCollection: shape.Kind != Kind.Scalar,
            Property: property)
        {
            Path = path,
        };
    }

    private static string Allowed(PropertyInfo property, Type element, SettingAttribute setting, string path, List<string> problems)
    {
        var parts = new List<string>();

        if (property.GetCustomAttribute<RangeAttribute>() is { } range)
        {
            parts.Add($"`{Invariant(range.Minimum)}` – `{Invariant(range.Maximum)}`");
        }

        if (property.GetCustomAttribute<AllowedValuesAttribute>() is { } values)
        {
            parts.Add(string.Join(", ", values.Values.Select(v => $"`{Invariant(v)}`")));
        }
        else if (element.IsEnum)
        {
            parts.Add(string.Join(", ", Enum.GetNames(element).Select(n => $"`{n}`")));
        }

        if (property.GetCustomAttribute<LengthAttribute>() is { } length)
        {
            parts.Add($"{length.MinimumLength} – {length.MaximumLength} characters");
        }

        if (property.GetCustomAttribute<MaxLengthAttribute>() is { } max)
        {
            parts.Add($"at most {max.Length} characters");
        }

        if (property.GetCustomAttribute<MinLengthAttribute>() is { } min)
        {
            parts.Add($"at least {min.Length} characters");
        }

        if (setting.Allowed is { Length: > 0 } stated)
        {
            parts.Add(stated);
        }

        if (setting.Format is { Length: > 0 } format)
        {
            parts.Add(format);
        }
        else if (property.GetCustomAttribute<RegularExpressionAttribute>() is not null)
        {
            problems.Add($"{path}: has [RegularExpression] but no Format — describe the accepted format in words.");
        }

        return parts.Count == 0 ? "—" : string.Join("; ", parts);
    }

    private static void CheckAcrossSettings(Catalog catalog, List<string> problems)
    {
        foreach (var duplicate in catalog.Settings.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"{duplicate.Key}: declared {duplicate.Count()} times — declare it once and mark the other properties [NotASetting].");
        }

        foreach (var setting in catalog.Settings)
        {
            foreach (var condition in new[] { setting.AppliesWhen, setting.GroupAppliesWhen }.OfType<string>().Distinct())
            {
                var match = Condition().Match(condition);
                if (!match.Success)
                {
                    problems.Add($"{setting.Key}: AppliesWhen '{condition}' is not '<key> = <value>' — write it as e.g. 'Storage:Provider = S3'.");
                }
                else if (!catalog.Settings.Any(s => string.Equals(s.Key, match.Groups["key"].Value, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{setting.Key}: AppliesWhen names '{match.Groups["key"].Value}', which is not a declared setting — name a declared key.");
                }
            }

            if (setting.DeprecatedReplacement is { Length: > 0 } replacement
                && !catalog.Settings.Any(s => string.Equals(s.Key, replacement, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{setting.Key}: deprecated in favour of '{replacement}', which is not a declared setting — name a declared key.");
            }
        }
    }

    internal static string RenderDefault(object? value, bool required)
    {
        var none = required ? "*(none)*" : "*(empty)*";
        return value switch
        {
            null => none,
            string { Length: 0 } => none,
            string text => $"`{text}`",
            bool flag => flag ? "`true`" : "`false`",
            TimeSpan span => $"`{span.ToString("c", CultureInfo.InvariantCulture)}`",
            DateTimeOffset moment => $"`{moment.ToString("O", CultureInfo.InvariantCulture)}`",
            Enum named => $"`{named}`",
            IDictionary map => map.Count == 0
                ? none
                : string.Join(", ", map.Keys.Cast<object>().OrderBy(k => k.ToString(), StringComparer.Ordinal).Select(k => $"`{k}`: `{Invariant(map[k])}`")),
            IEnumerable items when Enumerable.Any(items.Cast<object>()) => string.Join(", ", items.Cast<object>().Select(i => $"`{Invariant(i)}`")),
            IEnumerable => none,
            _ => $"`{Invariant(value)}`",
        };
    }

    private static string Invariant(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string? TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum)
        {
            return "one of the listed values";
        }

        return underlying switch
        {
            _ when underlying == typeof(string) => "text",
            _ when underlying == typeof(bool) => "true/false",
            _ when underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) => "whole number",
            _ when underlying == typeof(double) || underlying == typeof(decimal) || underlying == typeof(float) => "number",
            _ when underlying == typeof(TimeSpan) => "duration (`[d.]hh:mm:ss`)",
            _ when underlying == typeof(DateTimeOffset) || underlying == typeof(DateTime) => "date and time (ISO 8601, UTC)",
            _ when underlying == typeof(Uri) => "URL",
            _ => null,
        };
    }

    private enum Kind
    {
        Scalar,
        List,
        Map,
    }

    private static (Kind Kind, Type Element) Shape(Type type)
    {
        if (type == typeof(string))
        {
            return (Kind.Scalar, type);
        }

        var dictionary = Interfaces(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionary is not null)
        {
            return (Kind.Map, dictionary.GetGenericArguments()[1]);
        }

        if (type.IsArray)
        {
            return (Kind.List, type.GetElementType()!);
        }

        var enumerable = Interfaces(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable is not null ? (Kind.List, enumerable.GetGenericArguments()[0]) : (Kind.Scalar, type);
    }

    private static bool IsCollection(Type type) => Shape(type).Kind != Kind.Scalar;

    private static Type[] Interfaces(Type type) =>
        type.IsInterface ? [type, .. type.GetInterfaces()] : type.GetInterfaces();

    private static bool LooksSecret(string name) =>
        (name.EndsWith("Password", StringComparison.Ordinal)
         || name.EndsWith("Secret", StringComparison.Ordinal)
         || name.EndsWith("ConnectionString", StringComparison.Ordinal)
         || name.EndsWith("Token", StringComparison.Ordinal)
         || name.EndsWith("Key", StringComparison.Ordinal))
        && !name.EndsWith("KeyPrefix", StringComparison.Ordinal);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }

    [GeneratedRegex(@"\b(FR|SC|OD|RL|PV|PR|ST|TR|MV|ED|D|M|S)-\d", RegexOptions.CultureInvariant)]
    private static partial Regex RequirementId();

    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9:]*) = (?<value>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Condition();
}
