using Microsoft.Extensions.Logging;
using NetVips;

namespace QuickBuilder.Media.Imaging;

/// <summary>
/// Configures libvips once for untrusted input, and says what it can do (013 research MV-09).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Loaders</b>: every loader is blocked, then only JPEG, PNG, GIF, WebP and HEIF
/// (which reads AVIF) are allowed back. SVG, PDF, TIFF and the rest are compiled into the
/// shipped libvips (research MV-09 spike) but can never run. Untrusted operations are
/// blocked as well.</item>
/// <item><b>Memory</b>: the operation cache is off, so nothing decoded outlives its request.</item>
/// <item><b>CPU</b>: one worker thread per operation; parallelism comes from the variant
/// service's bounded pool (MV-12), so one request never takes every core.</item>
/// </list>
/// A singleton: constructing it applies the configuration, which is process-wide in libvips,
/// exactly once.
/// </remarks>
internal sealed class ImagingRuntime
{
    /// <summary>The loaders uploads may reach. Everything else stays blocked.</summary>
    internal static readonly string[] AllowedLoaders =
    [
        "VipsForeignLoadJpeg",
        "VipsForeignLoadPng",
        "VipsForeignLoadNsgif",
        "VipsForeignLoadWebp",
        "VipsForeignLoadHeif",
    ];

    private static readonly Lazy<bool> Configured = new(Configure, LazyThreadSafetyMode.ExecutionAndPublication);

    public ImagingRuntime(ILogger<ImagingRuntime> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        AvifVariants = Configured.Value;
        if (!AvifVariants)
        {
            logger.LogWarning(
                "The bundled libvips cannot read and write AVIF; AVIF files are served as uploaded, without resized copies.");
        }
    }

    /// <summary>Whether AVIF images can be read and written, so get variants (MV-09).</summary>
    public bool AvifVariants { get; }

    /// <summary>Applies the configuration. Returns whether AVIF is supported.</summary>
    private static bool Configure()
    {
        NetVips.NetVips.BlockUntrusted = true;

        Operation.Block("VipsForeignLoad", true);
        foreach (var loader in AllowedLoaders)
        {
            Operation.Block(loader, false);
        }

        Cache.Max = 0;
        Cache.MaxMem = 0;
        Cache.MaxFiles = 0;
        NetVips.NetVips.Concurrency = 1;

        return NetVips.NetVips.TypeFind("VipsOperation", "heifload_source") != IntPtr.Zero
            && NetVips.NetVips.TypeFind("VipsOperation", "heifsave_target") != IntPtr.Zero;
    }
}
