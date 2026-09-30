using System.Threading.Channels;
using QuickBuilder.Media.Domain;

namespace QuickBuilder.Media.Variants;

/// <summary>One image whose thumbnail should be produced soon.</summary>
internal readonly record struct ThumbnailRequest(Guid SiteId, Guid FileId, int ContentVersion);

/// <summary>
/// Images waiting for their thumbnail, produced right after upload or replacement
/// (013 clarification, research MV-13).
/// </summary>
/// <remarks>
/// In process and bounded: when it is full, or the instance stops, requests are simply lost,
/// and the first request for the thumbnail produces it instead. That fallback is why the
/// queue does not need to survive restarts, and why a full queue never slows an upload.
/// </remarks>
internal sealed class ThumbnailQueue
{
    internal const int Capacity = 1_000;

    private readonly Channel<ThumbnailRequest> _channel = Channel.CreateBounded<ThumbnailRequest>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false,
        });

    internal ChannelReader<ThumbnailRequest> Reader => _channel.Reader;

    /// <summary>Asks for <paramref name="file"/>'s thumbnail. Never blocks; images only.</summary>
    public void Enqueue(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Type.IsImage)
        {
            _channel.Writer.TryWrite(new ThumbnailRequest(file.SiteId, file.Id, file.ContentVersion));
        }
    }
}
