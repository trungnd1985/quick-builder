using System.Text.RegularExpressions;

namespace QuickBuilder.Shared.Kernel.Authorization;

/// <summary>
/// The roles the constitution names (Principle IX). Product-wide, not module-specific.
/// </summary>
/// <remarks>
/// <see cref="Administrator"/> is granted for the whole installation; <see cref="Designer"/>
/// and <see cref="Editor"/> are granted per site (spec FR-020, clarification Q3).
/// </remarks>
public enum SystemRole
{
    Administrator,
    Designer,
    Editor,
}

/// <summary>Whether a capability applies to the whole installation or to one site.</summary>
public enum CapabilityScope
{
    Installation,
    Site,
}

/// <summary>
/// A named permission to perform one kind of administrative action (spec FR-026).
/// </summary>
/// <remarks>
/// Registered by the module that owns the operation, from its <c>Add&lt;Module&gt;Module()</c>,
/// so a new module adds capabilities without editing Identity. <see cref="GrantedTo"/>
/// lists the <em>site</em> roles that hold it; Administrator holds every capability
/// everywhere and is never listed (contracts/authorization.md §2).
/// <para>
/// A name never changes once shipped: it is part of what a role means in stored data.
/// </para>
/// </remarks>
public sealed partial record CapabilityDefinition
{
    public CapabilityDefinition(string name, CapabilityScope scope, IReadOnlyList<SystemRole> grantedTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(grantedTo);

        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"Capability name '{name}' must be '<module>.<noun>.<verb>': lower-case, dot-separated.",
                nameof(name));
        }

        if (grantedTo.Contains(SystemRole.Administrator))
        {
            throw new ArgumentException(
                "Administrator holds every capability implicitly and must not be listed.",
                nameof(grantedTo));
        }

        Name = name;
        Scope = scope;
        GrantedTo = grantedTo;
    }

    public string Name { get; }

    public CapabilityScope Scope { get; }

    public IReadOnlyList<SystemRole> GrantedTo { get; }

    [GeneratedRegex("^[a-z]+(\\.[a-z_]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
