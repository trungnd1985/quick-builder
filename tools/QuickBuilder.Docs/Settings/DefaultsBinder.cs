using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace QuickBuilder.Docs.Settings;

/// <summary>
/// The configuration an operator gets when they set nothing: the shipped
/// <c>appsettings.json</c> only, never <c>appsettings.Development.json</c> (026 research OD-02).
/// </summary>
internal static class DefaultsBinder
{
    public const string ShippedPath = "src/QuickBuilder.Host/appsettings.json";

    public static IConfiguration LoadShipped(string repositoryRoot) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repositoryRoot, ShippedPath), optional: false, reloadOnChange: false)
            .Build();
}

/// <summary>Loads the Host and every <c>QuickBuilder.*</c> assembly it references.</summary>
internal static class ProductAssemblies
{
    public static IReadOnlyList<Assembly> Load()
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Stack<AssemblyName>([new AssemblyName("QuickBuilder.Host")]);
        while (pending.TryPop(out var name))
        {
            if (loaded.ContainsKey(name.Name!))
            {
                continue;
            }

            var assembly = Assembly.Load(name);
            loaded[name.Name!] = assembly;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(r => r.Name!.StartsWith("QuickBuilder.", StringComparison.Ordinal)))
            {
                pending.Push(reference);
            }
        }

        return [.. loaded.Values.OrderBy(a => a.GetName().Name, StringComparer.Ordinal)];
    }
}
