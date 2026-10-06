using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// An encoder that counts productions and can be slowed or made to fail, around the real one
/// (013 T049, T050, T051).
/// </summary>
internal sealed class ControllableEncoder(IVariantEncoder inner) : IVariantEncoder
{
    private int _productions;

    /// <summary>How many times a variant was encoded, per width.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<int, int> ByWidth { get; } = new();

    public int Productions => _productions;

    /// <summary>A delay before each encoding, honouring cancellation.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>When set, every encoding fails as an undecodable image would.</summary>
    public bool Fail { get; set; }

    /// <summary>When set, the delay ignores cancellation, as a stuck native call would.</summary>
    public bool IgnoreCancellation { get; set; }

    public async Task<byte[]> EncodeAsync(Stream original, MediaFileType type, int width, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _productions);
        ByWidth.AddOrUpdate(width, 1, (_, n) => n + 1);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, IgnoreCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
        }

        if (Fail)
        {
            throw new VariantEncodingException("Simulated encoding failure.");
        }

        return await inner.EncodeAsync(original, type, width, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the app's encoder with a controllable one, and returns a way to reach it.</summary>
    public static void Install(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IVariantEncoder>(sp =>
            new ControllableEncoder(new VariantEncoder(sp.GetRequiredService<ImagingRuntime>()))));

    public static ControllableEncoder Of(IServiceProvider services) =>
        (ControllableEncoder)services.GetRequiredService<IVariantEncoder>();
}
