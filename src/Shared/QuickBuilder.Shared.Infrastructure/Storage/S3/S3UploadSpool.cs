namespace QuickBuilder.Shared.Infrastructure.Storage.S3;

/// <summary>
/// Copies an upload to a temporary file before it is sent (013 contracts/storage-providers.md §3.3).
/// </summary>
/// <remarks>
/// S3 needs the length up front and a part that can be re-sent on retry; an upload stream
/// offers neither. The file is opened with <see cref="FileOptions.DeleteOnClose"/>, so it is
/// gone when the write ends however it ends, and holds at most the bytes the caller sent,
/// which the upload limit bounds. The only file-system use in the remote providers
/// (exempted by name in the module boundary tests).
/// </remarks>
internal static class S3UploadSpool
{
    private const int BufferSize = 81_920;

    /// <summary>The directory spooled uploads live in while they are sent.</summary>
    public static string Directory => Path.Combine(Path.GetTempPath(), "quickbuilder-s3");

    /// <summary>Copies <paramref name="content"/> to its end into a self-deleting file, positioned at 0.</summary>
    public static async Task<Stream> CreateAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        System.IO.Directory.CreateDirectory(Directory);
        var spool = new FileStream(
            Path.Combine(Directory, Guid.NewGuid().ToString("N") + ".spool"),
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
                BufferSize = BufferSize,
            });

        try
        {
            await content.CopyToAsync(spool, BufferSize, cancellationToken).ConfigureAwait(false);
            await spool.FlushAsync(cancellationToken).ConfigureAwait(false);
            spool.Position = 0;
            return spool;
        }
        catch
        {
            await spool.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
