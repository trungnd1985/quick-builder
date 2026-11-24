using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace QuickBuilder.IntegrationTests.Configuration;

/// <summary>
/// A configuration source that answers nothing and remembers every key asked of it (026 T041;
/// contracts/setting-attributes.md §4).
/// </summary>
/// <remarks>
/// Added last, its provider is asked first for every value: the configuration root walks
/// providers from the last added. Binding an options class asks for each property's full
/// path, and a direct read such as <c>GetConnectionString</c> asks for its key, so a boot
/// with this source attached shows every key the product reads.
/// </remarks>
internal sealed class RecordingConfigurationSource : IConfigurationSource
{
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every key read, and every section whose children were listed.</summary>
    public IReadOnlyCollection<string> Keys => [.. _keys.Keys];

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(_keys);

    private sealed class Provider(ConcurrentDictionary<string, byte> keys) : ConfigurationProvider
    {
        public override bool TryGet(string key, out string? value)
        {
            keys.TryAdd(key, 0);
            value = null;
            return false;
        }

        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            if (parentPath is { Length: > 0 })
            {
                keys.TryAdd(parentPath, 0);
            }

            return earlierKeys;
        }
    }
}
