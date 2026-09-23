using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure.Authorization;

/// <summary>
/// Every capability registered by every module (contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Built once from the <see cref="CapabilityDefinition"/>s modules register in DI. Two
/// registrations of one name fail at startup: a capability means one thing.
/// </remarks>
internal sealed class CapabilityCatalog
{
    private readonly Dictionary<string, CapabilityDefinition> _byName;

    public CapabilityCatalog(IEnumerable<CapabilityDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byName = new Dictionary<string, CapabilityDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!_byName.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException(
                    $"Capability '{definition.Name}' is registered twice ({_byName[definition.Name]} and {definition}). " +
                    "Each capability must be registered once, by the module that owns it.");
            }
        }
    }

    public IReadOnlyCollection<CapabilityDefinition> All => _byName.Values;

    public CapabilityDefinition? Find(string name) =>
        _byName.GetValueOrDefault(name);

    public CapabilityDefinition Get(string name) =>
        Find(name) ?? throw new InvalidOperationException(
            $"No capability named '{name}' is registered. Register it in the owning module's Add<Module>Module().");
}
