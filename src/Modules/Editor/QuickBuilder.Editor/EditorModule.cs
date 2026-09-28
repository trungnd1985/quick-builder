using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuickBuilder.Editor;

/// <summary>
/// The Editor module's single registration entry point (specs/011-editor-core).
/// </summary>
public static class EditorModule
{
    /// <summary>Registers the Editor module: data, capabilities and slices.</summary>
    public static IServiceCollection AddEditorModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services;
    }
}
