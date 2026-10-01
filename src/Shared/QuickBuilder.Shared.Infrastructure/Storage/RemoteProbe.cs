using System.Security.Cryptography;

namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>Why a remote store failed its probe. Never carries the service's own message (013 FR-033).</summary>
internal enum ProbeFailure
{
    None,
    Credentials,
    ContainerMissing,
    AccessDenied,
    Unreachable,
    NoConditionalWrites,
    Mismatch,
}

/// <summary>What a remote provider exposes to <see cref="RemoteProbe"/>: raw object names, below its key prefix.</summary>
internal interface IRemoteProbeTarget
{
    /// <summary>Creates the object only if absent; throws <see cref="StorageKeyExistsException"/> otherwise.</summary>
    Task CreateOnlyAsync(string name, byte[] content, CancellationToken cancellationToken);

    /// <summary>The object's bytes, or null when it does not exist.</summary>
    Task<byte[]?> ReadAsync(string name, CancellationToken cancellationToken);

    Task DeleteAsync(string name, CancellationToken cancellationToken);

    /// <summary>Maps a provider exception to the setting at fault.</summary>
    ProbeFailure Classify(Exception exception);
}

/// <summary>
/// One round trip against a remote store: create, re-create (which must be refused), read,
/// compare, delete (013 contracts/storage-providers.md §5).
/// </summary>
/// <remarks>
/// Shared by start-up validation (30 s, with the conditional-write check) and readiness
/// (2 s, without). Probe objects live under <see cref="Folder"/>, a name outside the key
/// grammar, so listings never return them and moving between providers leaves them out.
/// </remarks>
internal static class RemoteProbe
{
    public const string Folder = ".quickbuilder-probe/";

    public static readonly TimeSpan ValidationBudget = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan HealthBudget = TimeSpan.FromSeconds(2);

    public static async Task<ProbeFailure> RunAsync(
        IRemoteProbeTarget target,
        TimeSpan budget,
        bool checkConditionalWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        var token = timeout.Token;

        var name = Folder + Guid.NewGuid().ToString("N");
        var content = RandomNumberGenerator.GetBytes(64);
        var created = false;

        try
        {
            await target.CreateOnlyAsync(name, content, token).ConfigureAwait(false);
            created = true;

            if (checkConditionalWrite)
            {
                try
                {
                    await target.CreateOnlyAsync(name, [0], token).ConfigureAwait(false);
                    return ProbeFailure.NoConditionalWrites;
                }
                catch (StorageKeyExistsException)
                {
                    // Refused, as it must be.
                }
            }

            var read = await target.ReadAsync(name, token).ConfigureAwait(false);
            return read is not null && read.AsSpan().SequenceEqual(content) ? ProbeFailure.None : ProbeFailure.Mismatch;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeFailure.Unreachable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = target.Classify(ex);
            return failure == ProbeFailure.None ? ProbeFailure.Unreachable : failure;
        }
        finally
        {
            if (created)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(budget);
                    await target.DeleteAsync(name, cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception) // Best effort: a leftover probe object is harmless and outside the key grammar.
                {
                }
            }
        }
    }

    /// <summary>Whether a key prefix setting is empty or whole key segments ending in '/'.</summary>
    public static bool IsValidKeyPrefix(string prefix) =>
        prefix.Length == 0 || (prefix.EndsWith('/') && StorageKey.IsValidPrefix(prefix));

    /// <summary>Whether a URL setting is an absolute http or https URL.</summary>
    public static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
