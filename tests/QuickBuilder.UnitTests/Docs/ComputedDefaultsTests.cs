using QuickBuilder.Docs.Settings;
using QuickBuilder.Media;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// Defaults the product computes at run time are documented as text; each text must match
/// what the product does when the setting is absent (026 T044; SC-003).
/// </summary>
public sealed class ComputedDefaultsTests
{
    private static readonly Catalog Catalog = CatalogBuilder.BuildForProduct(RepositoryRoot());

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static string Documented(string key) => Catalog.Settings.Single(s => s.Key == key).Default;

    [Fact]
    public void Every_computed_default_has_a_test_here()
    {
        Assert.Equal(
            ["Media:AllowedTypes", "Media:Variants:MaxConcurrent", "Media:Variants:Widths", "Storage:Local:Path"],
            Catalog.Settings.Where(s => s.DefaultIsComputed).Select(s => s.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_media_path_is_the_images_and_the_per_user_folder_elsewhere()
    {
        var documented = Documented("Storage:Local:Path");
        var dockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile"));

        Assert.Contains("ENV Storage__Local__Path=/var/lib/quickbuilder/media", dockerfile, StringComparison.Ordinal);
        Assert.Contains("`/var/lib/quickbuilder/media`", documented, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("QuickBuilder", "media"), LocalStorageSettings.DefaultPath, StringComparison.Ordinal);
        Assert.Contains("`{LocalApplicationData}/QuickBuilder/media`", documented, StringComparison.Ordinal);
    }

    [Fact]
    public void Allowed_types_default_to_every_supported_type()
    {
        var options = new MediaOptions();
        new MediaOptionsDefaults().PostConfigure(null, options);

        Assert.Equal(
            MediaFileType.All.Select(t => t.Mime),
            options.AllowedTypes);
        Assert.Equal(
            $"all six: {string.Join(", ", MediaFileType.All.Select(t => $"`{t.Mime}`"))}",
            Documented("Media:AllowedTypes"));
    }

    [Fact]
    public void Widths_default_to_the_built_in_list()
    {
        var options = new MediaOptions();
        new MediaOptionsDefaults().PostConfigure(null, options);

        Assert.Equal(string.Join(", ", options.Variants.Widths.Select(w => $"`{w}`")), Documented("Media:Variants:Widths"));
    }

    [Fact]
    public void Concurrency_defaults_to_half_the_processors_at_least_one()
    {
        Assert.Equal(Math.Max(1, Environment.ProcessorCount / 2), new VariantOptions().MaxConcurrent);
        Assert.Equal("half the processor count, at least 1", Documented("Media:Variants:MaxConcurrent"));
    }
}
