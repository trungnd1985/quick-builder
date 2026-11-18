using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Features.ServePublicFile;
using QuickBuilder.Shared.Infrastructure.Dispatch;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// <see cref="IMediaContent"/> through the very handler <c>/_media</c> uses (022 research PV-11),
/// so a preview serves exactly what the public site would, and nothing it would not.
/// </summary>
internal sealed class MediaContentReader(IDispatcher dispatcher) : IMediaContent
{
    public async Task<MediaFileContent?> OpenAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var result = await dispatcher.QueryAsync(new ServePublicFileQuery(address), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return null;
        }

        var content = result.Value;
        return new MediaFileContent(
            content.File.Id,
            content.Stored.Content,
            content.File.ContentType,
            content.File.FileName,
            $"\"{content.Address.ETagValue}\"",
            content.File.ReplacedAtUtc ?? content.File.UploadedAtUtc,
            content.File.Type.IsImage);
    }
}
