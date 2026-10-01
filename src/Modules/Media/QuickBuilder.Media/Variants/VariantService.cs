using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Media.Variants;

/// <summary>What to send for a variant request.</summary>
internal abstract record VariantResult
{
    private VariantResult()
    {
    }

    /// <summary>The variant, read from storage. The caller disposes it.</summary>
    public sealed record Stored(StoredFile File) : VariantResult;

    /// <summary>The variant, just produced by this request's wait.</summary>
    public sealed record Produced(byte[] Bytes) : VariantResult;

    /// <summary>No variant: send the original, uncached (013 MV-12).</summary>
    public sealed record Fallback : VariantResult;
}

/// <summary>
/// Produces each variant at most once, stores it, and serves it from storage afterwards
/// (013 FR-022, FR-029, research MV-12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hit path</b>: one storage read. That is all a request pays once a variant exists
/// (SC-004).
/// </para>
/// <para>
/// <b>Miss path</b>, in order:
/// <list type="number">
/// <item>In this process, requests for the same variant share one production (single-flight).</item>
/// <item>Across instances, a claim row decides who produces it (<see cref="VariantClaims"/>);
/// the others poll storage until it appears. Together: exactly once (SC-005).</item>
/// <item>Production waits for one of <c>Media:Variants:MaxConcurrent</c> slots and must finish
/// within <c>Media:Variants:Timeout</c>, waiting included (FR-029).</item>
/// </list>
/// </para>
/// <para>
/// <b>Failure</b>: when the image cannot be encoded, or encoding outlives the timeout, the
/// file's variants are withdrawn (<c>variants_state = unavailable</c>, with
/// <c>Media.FileChanged(variants)</c> so pages stop offering them) and the request gets the
/// original, uncached. Running out of time while <em>waiting</em> for a slot is load, not a
/// property of the image: that request gets the original and nothing is withdrawn.
/// </para>
/// </remarks>
internal sealed class VariantService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly ConcurrentDictionary<VariantKey, Lazy<Task<Outcome>>> _inflight = new();
    private readonly SemaphoreSlim _slots;
    private readonly IFileStorage _storage;
    private readonly VariantClaims _claims;
    private readonly IVariantEncoder _encoder;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<MediaOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<VariantService> _logger;

    public VariantService(
        IFileStorage storage,
        VariantClaims claims,
        IVariantEncoder encoder,
        IServiceScopeFactory scopes,
        IOptions<MediaOptions> options,
        TimeProvider clock,
        ILogger<VariantService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _storage = storage;
        _claims = claims;
        _encoder = encoder;
        _scopes = scopes;
        _options = options;
        _clock = clock;
        _logger = logger;

        var slots = options.Value.Variants.MaxConcurrent;
        _slots = new SemaphoreSlim(slots, slots);
    }

    private enum OutcomeKind
    {
        Produced,
        InStorage,
        Fallback,
    }

    private TimeSpan Timeout => _options.Value.Variants.Timeout;

    /// <summary>
    /// The variant of <paramref name="file"/>'s current version at <paramref name="width"/>.
    /// The caller has checked the width is one the file has (<see cref="VariantPlan"/>).
    /// </summary>
    public async Task<VariantResult> GetAsync(MediaFile file, int width, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        var key = new VariantKey(file.SiteId, file.Id, file.ContentVersion, width);
        var storageKey = MediaStorageKeys.Variant(key.SiteId, key.FileId, key.Version, key.Width);

        var stored = await _storage.OpenReadAsync(storageKey, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return new VariantResult.Stored(stored);
        }

        var outcome = await ProduceOnceAsync(file, key, storageKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        switch (outcome.Kind)
        {
            case OutcomeKind.Produced:
                return new VariantResult.Produced(outcome.Bytes!);
            case OutcomeKind.InStorage:
                var made = await _storage.OpenReadAsync(storageKey, cancellationToken).ConfigureAwait(false);
                return made is null ? new VariantResult.Fallback() : new VariantResult.Stored(made);
            default:
                return new VariantResult.Fallback();
        }
    }

    public void Dispose() => _slots.Dispose();

    /// <summary>Joins this process's production of <paramref name="key"/>, starting it if there is none.</summary>
    private Task<Outcome> ProduceOnceAsync(MediaFile file, VariantKey key, StorageKey storageKey)
    {
        var lazy = _inflight.GetOrAdd(key, k => new Lazy<Task<Outcome>>(() => ProduceAsync(file, k, storageKey)));
        var task = lazy.Value;

        // Forget it once done, whoever finishes first; later requests read storage instead.
        _ = task.ContinueWith(
            _ => _inflight.TryRemove(new KeyValuePair<VariantKey, Lazy<Task<Outcome>>>(key, lazy)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    /// <summary>
    /// Produces, stores and returns the variant, or waits for another instance to. Never uses a
    /// request's token: several requests may be waiting on this one production.
    /// </summary>
    private async Task<Outcome> ProduceAsync(MediaFile file, VariantKey key, StorageKey storageKey)
    {
        var lease = Timeout * 2;

        if (!await _claims.TryClaimAsync(key, lease, CancellationToken.None).ConfigureAwait(false))
        {
            if (await WaitForOtherInstanceAsync(storageKey, lease).ConfigureAwait(false))
            {
                return Outcome.InStorage;
            }

            // Its lease ran out without a variant: the other instance died. Try to take over.
            if (!await _claims.TryClaimAsync(key, lease, CancellationToken.None).ConfigureAwait(false))
            {
                return Outcome.Fallback;
            }
        }

        try
        {
            return await ProduceClaimedAsync(file, key, storageKey).ConfigureAwait(false);
        }
        finally
        {
            await _claims.ReleaseAsync(key).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForOtherInstanceAsync(StorageKey storageKey, TimeSpan lease)
    {
        var deadline = _clock.GetUtcNow() + lease;
        while (_clock.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, _clock).ConfigureAwait(false);
            if (await _storage.ExistsAsync(storageKey, CancellationToken.None).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Outcome> ProduceClaimedAsync(MediaFile file, VariantKey key, StorageKey storageKey)
    {
        using var timeout = new CancellationTokenSource(Timeout, _clock);

        try
        {
            await _slots.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Variant {Width} of media file {MediaFileId} on site {SiteId} waited too long for a production slot; the original was sent.",
                key.Width, key.FileId, key.SiteId);
            return Outcome.Fallback;
        }

        // The slot is held until the encoder has really stopped, even when this request stops
        // waiting for it at the timeout: that is what keeps CPU use bounded (FR-029).
        Task encoding = Task.CompletedTask;
        try
        {
            var original = await _storage.OpenReadAsync(new StorageKey(file.StorageKey), timeout.Token).ConfigureAwait(false);
            if (original is null)
            {
                // A missing original is not found anyway; nothing to withdraw.
                return Outcome.Fallback;
            }

            var encode = EncodeAndCloseAsync(original, file.Type, key.Width, timeout.Token);
            encoding = encode;

            // Stop waiting at the timeout, whether or not the encoder notices it.
            var bytes = await encode.WaitAsync(timeout.Token).ConfigureAwait(false);

            await StoreAsync(storageKey, bytes, key).ConfigureAwait(false);
            return Outcome.FromBytes(bytes);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Variant {Width} of media file {MediaFileId} on site {SiteId} took longer than {Timeout}; its variants are withdrawn.",
                key.Width, key.FileId, key.SiteId, Timeout);
            await WithdrawAsync(key).ConfigureAwait(false);
            return Outcome.Fallback;
        }
        catch (VariantEncodingException ex)
        {
            _logger.LogWarning(ex,
                "Variant {Width} of media file {MediaFileId} on site {SiteId} could not be produced; its variants are withdrawn.",
                key.Width, key.FileId, key.SiteId);
            await WithdrawAsync(key).ConfigureAwait(false);
            return Outcome.Fallback;
        }
        finally
        {
            if (encoding.IsCompleted)
            {
                _slots.Release();
            }
            else
            {
                _ = encoding.ContinueWith(
                    t => { _ = t.Exception; _slots.Release(); },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    /// <summary>Encodes, then closes the original, whenever the encoder finishes.</summary>
    private async Task<byte[]> EncodeAndCloseAsync(StoredFile original, MediaFileType type, int width, CancellationToken cancellationToken)
    {
        await using (original.ConfigureAwait(false))
        {
            return await _encoder.EncodeAsync(original.Content, type, width, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Stores the variant. Losing a race, or failing, still serves this request's bytes.</summary>
    private async Task StoreAsync(StorageKey storageKey, byte[] bytes, VariantKey key)
    {
        try
        {
            using var content = new MemoryStream(bytes, writable: false);
            await _storage.WriteAsync(storageKey, content, CancellationToken.None).ConfigureAwait(false);
        }
        catch (StorageKeyExistsException)
        {
            // Stored meanwhile (a stale claim taken over): the same bytes are there.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "Variant {Width} of media file {MediaFileId} on site {SiteId} could not be stored; it will be produced again.",
                key.Width, key.FileId, key.SiteId);
        }
    }

    /// <summary>Stops offering variants of this version, and tells pages (013 MV-12, MV-17).</summary>
    private async Task WithdrawAsync(VariantKey key)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var file = await database.MediaFiles
            .AcrossAllSites("Withdrawing the variants of a file named by its site and id (013 MV-12).")
            .FirstOrDefaultAsync(f => f.Id == key.FileId && f.SiteId == key.SiteId)
            .ConfigureAwait(false);

        if (file is null || !file.MarkVariantsUnavailable(key.Version))
        {
            return;
        }

        database.OutboxMessages.Add(OutboxMessage.From(
            new FileChanged(Guid.CreateVersion7(), _clock.GetUtcNow(), key.SiteId, key.FileId, FileChangeReasons.Variants)));

        try
        {
            await database.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Replaced or deleted meanwhile: the new version starts with variants available.
        }
    }

    private sealed record Outcome(OutcomeKind Kind, byte[]? Bytes)
    {
        public static readonly Outcome InStorage = new(OutcomeKind.InStorage, null);

        public static readonly Outcome Fallback = new(OutcomeKind.Fallback, null);

        public static Outcome FromBytes(byte[] bytes) => new(OutcomeKind.Produced, bytes);
    }
}
