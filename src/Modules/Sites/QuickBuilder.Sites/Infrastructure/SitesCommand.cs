using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Features.Transfer;
using QuickBuilder.Sites.Features.Transfer.Engine;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// <c>sites export | check | import</c>: scripted backups, restores and moves from the command
/// line (020 FR-032 … FR-034, contracts/operator-commands.md, research ST-18).
/// </summary>
/// <remarks>
/// Each run is an ordinary transfer job with <c>trigger = cli</c> and no member, checked and
/// confirmed by the same handlers as the admin, then run in this process under its own lease.
/// Archives are read from and written to the operator's files through <see cref="IOperatorFiles"/>.
/// Output names ids, counts and codes only; titles and content stay out of logs (Principle IX).
/// </remarks>
internal sealed class SitesCommand(
    IServiceScopeFactory scopes,
    IOperatorFiles files,
    TimeProvider clock,
    TransferWorker worker) : IHostCommand
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly IOperatorFiles _files = files;
    private readonly TimeProvider _clock = clock;
    private readonly TransferWorker _worker = worker;

    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int Refused = 2;
    internal const int MigrationsPending = 3;
    internal const int UnknownSite = 4;
    internal const int Busy = 5;
    internal const int ConfirmationRequired = 6;
    internal const int Usage = 64;

    internal static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(5);

    private const string UsageText =
        """
        Usage:
          sites export --site <id|host> --out <file> [--json]
          sites export --all --out <dir> [--json]
          sites check <file> [--into <id|host> | --restore <id|host>] [--json]
          sites import <file> --as-new --name <name> [--host-name <host>] [--start-fresh] [--accept-warnings] [--json]
          sites import <file> --into <id|host> --on-conflict replace|skip|rename [--accept-warnings] [--json]
          sites import <file> --restore <id|host> --confirm <site name> [--pre-restore-out <file>] [--accept-warnings] [--json]
        """;

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--all", "--as-new", "--start-fresh", "--accept-warnings", "--json" };

    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        "--site", "--out", "--into", "--restore", "--name", "--host-name", "--on-conflict", "--confirm", "--pre-restore-out",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "sites";

    public async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var output = Console.Out;

        if (args.Count == 0 || Parse(args.Skip(1).ToList()) is not { } options)
        {
            await output.WriteLineAsync(UsageText).ConfigureAwait(false);
            return Usage;
        }

        if (await MigrationsPendingAsync(cancellationToken).ConfigureAwait(false))
        {
            await output.WriteLineAsync("The database has pending migrations. Run `migrate` first.").ConfigureAwait(false);
            return MigrationsPending;
        }

        var run = new Run(this, options, output);
        return args[0] switch
        {
            "export" => await run.ExportAsync(cancellationToken).ConfigureAwait(false),
            "check" => await run.CheckOrImportAsync(import: false, cancellationToken).ConfigureAwait(false),
            "import" => await run.CheckOrImportAsync(import: true, cancellationToken).ConfigureAwait(false),
            _ => await run.UsageAsync().ConfigureAwait(false),
        };
    }

    /// <summary>Options and positional arguments, or null when an option is unknown or lacks its value.</summary>
    internal static Options? Parse(List<string> args)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var positional = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Flags.Contains(arg))
            {
                flags.Add(arg);
            }
            else if (Values.Contains(arg))
            {
                if (i + 1 >= args.Count || values.ContainsKey(arg))
                {
                    return null;
                }

                values[arg] = args[++i];
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new Options(flags, values, positional);
    }

    private async Task<bool> MigrationsPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        foreach (var provider in scope.ServiceProvider.GetServices<IModuleDatabaseProvider>())
        {
            await using var context = provider.CreateContext(scope.ServiceProvider);
            if ((await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
            {
                return true;
            }
        }

        return false;
    }

    internal sealed record Options(HashSet<string> Flags, Dictionary<string, string> Values, List<string> Positional)
    {
        public bool Has(string flag) => Flags.Contains(flag);

        public string? Value(string name) => Values.GetValueOrDefault(name);
    }

    /// <summary>One invocation: its options, its output and the jobs it runs.</summary>
    private sealed class Run(SitesCommand command, Options options, TextWriter output)
    {
        private readonly bool _json = options.Has("--json");

        public async Task<int> UsageAsync()
        {
            await output.WriteLineAsync(UsageText).ConfigureAwait(false);
            return Usage;
        }

        // --- export ---------------------------------------------------------------------

        public async Task<int> ExportAsync(CancellationToken cancellationToken)
        {
            var all = options.Has("--all");
            var site = options.Value("--site");
            var target = options.Value("--out");
            if (target is null || all == (site is not null) || options.Positional.Count > 0
                || options.Values.Keys.Any(k => k is not ("--site" or "--out")) || options.Flags.Any(f => f is not ("--all" or "--json")))
            {
                return await UsageAsync().ConfigureAwait(false);
            }

            if (!all)
            {
                if (await ResolveAsync(site!, cancellationToken).ConfigureAwait(false) is not { } one)
                {
                    return await UnknownSiteAsync(site!).ConfigureAwait(false);
                }

                if (command._files.FileExists(target))
                {
                    await output.WriteLineAsync($"{target} already exists; it is never overwritten.").ConfigureAwait(false);
                    return Usage;
                }

                var (code, job) = await ExportOneAsync(one.Id, target, cancellationToken).ConfigureAwait(false);
                await PrintAsync(job, cancellationToken).ConfigureAwait(false);
                return code;
            }

            if (!command._files.DirectoryExists(target))
            {
                await output.WriteLineAsync($"{target} is not a directory.").ConfigureAwait(false);
                return Usage;
            }

            // FR-033: every site, in site order; a failed one does not stop the others.
            var failed = 0;
            var jobs = new List<TransferJob?>();
            foreach (var each in await SitesAsync(cancellationToken).ConfigureAwait(false))
            {
                var stamp = command._clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                var name = string.IsNullOrEmpty(each.PrimaryHostName) ? each.Id.ToString("N") : each.PrimaryHostName;
                var file = Path.Combine(target, $"{name}-{stamp}.qbsite");
                var (code, job) = command._files.FileExists(file)
                    ? (Usage, null)
                    : await ExportOneAsync(each.Id, file, cancellationToken).ConfigureAwait(false);
                jobs.Add(job);
                if (!_json)
                {
                    await output.WriteLineAsync(code == Ok ? $"exported {each.Id:N} to {file}" : $"failed {each.Id:N} (exit {code})").ConfigureAwait(false);
                }

                if (code != Ok)
                {
                    failed++;
                }
            }

            if (_json)
            {
                var responses = new List<TransferResponse>();
                foreach (var job in jobs.OfType<TransferJob>())
                {
                    responses.Add(await ResponseAsync(job, cancellationToken).ConfigureAwait(false));
                }

                await output.WriteLineAsync(JsonSerializer.Serialize(new { transfers = responses, failed }, Json)).ConfigureAwait(false);
            }
            else
            {
                await output.WriteLineAsync($"{jobs.Count - failed} of {jobs.Count} site(s) exported.").ConfigureAwait(false);
            }

            return failed == 0 ? Ok : Failed;
        }

        private async Task<(int Code, TransferJob? Job)> ExportOneAsync(Guid siteId, string file, CancellationToken cancellationToken)
        {
            var job = TransferJob.StartExport(siteId, TransferTriggers.Cli, startedBy: null, command._clock.GetUtcNow());
            await using (var scope = command._scopes.CreateAsyncScope())
            {
                var saved = await scope.ServiceProvider.GetRequiredService<TransferJobStore>().SaveAsync(job, null, cancellationToken).ConfigureAwait(false);
                if (saved == SaveOutcome.SiteBusy)
                {
                    await output.WriteLineAsync($"Site {siteId:N} already has an export or import running.").ConfigureAwait(false);
                    return (Busy, null);
                }
            }

            var done = await RunToEndAsync(job.Id, cancellationToken).ConfigureAwait(false);
            if (done.State != TransferStates.Succeeded || done.ArchiveKey is null)
            {
                return (Failed, done);
            }

            return await CopyOutAsync(done.ArchiveKey, file, cancellationToken).ConfigureAwait(false) ? (Ok, done) : (Failed, done);
        }

        // --- check and import -------------------------------------------------------------

        public async Task<int> CheckOrImportAsync(bool import, CancellationToken cancellationToken)
        {
            if (options.Positional.Count != 1)
            {
                return await UsageAsync().ConfigureAwait(false);
            }

            var file = options.Positional[0];
            var into = options.Value("--into");
            var restore = options.Value("--restore");
            var asNew = options.Has("--as-new");
            var targets = (into is null ? 0 : 1) + (restore is null ? 0 : 1) + (asNew ? 1 : 0);
            string[] allowed = !import ? ["--into", "--restore"]
                : asNew ? ["--name", "--host-name"]
                : into is not null ? ["--into", "--on-conflict"]
                : ["--restore", "--confirm", "--pre-restore-out"];
            string[] allowedFlags = !import ? ["--json"] : asNew ? ["--as-new", "--start-fresh", "--accept-warnings", "--json"] : ["--accept-warnings", "--json"];
            if (targets > 1 || (import && targets == 0)
                || options.Values.Keys.Any(k => !allowed.Contains(k)) || options.Flags.Any(f => !allowedFlags.Contains(f))
                || (import && asNew && options.Value("--name") is not { Length: > 0 })
                || (import && into is not null && !TransferPolicies.All.Contains(options.Value("--on-conflict") ?? string.Empty)))
            {
                return await UsageAsync().ConfigureAwait(false);
            }

            if (!command._files.FileExists(file))
            {
                await output.WriteLineAsync($"{file} does not exist.").ConfigureAwait(false);
                return Usage;
            }

            SiteDto? site = null;
            if ((into ?? restore) is { } reference && (site = await ResolveAsync(reference, cancellationToken).ConfigureAwait(false)) is null)
            {
                return await UnknownSiteAsync(reference).ConfigureAwait(false);
            }

            var mode = restore is not null ? TransferModes.Restore : into is not null ? TransferModes.Merge : TransferModes.NewSite;
            var jobId = await UploadAsync(file, cancellationToken).ConfigureAwait(false);

            var requested = await DispatchAsync(new CheckArchiveCommand(jobId, mode, site?.Id), cancellationToken).ConfigureAwait(false);
            if (requested.IsFailure)
            {
                await output.WriteLineAsync($"The check could not start: {requested.Error!.Code}").ConfigureAwait(false);
                return requested.Error.Code == TransferErrors.Busy.Code ? Busy : Failed;
            }

            var checkedJob = await RunToEndAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (checkedJob.State == TransferStates.Refused)
            {
                await PrintAsync(checkedJob, cancellationToken).ConfigureAwait(false);
                return Refused;
            }

            if (checkedJob.State != TransferStates.AwaitingConfirmation)
            {
                await PrintAsync(checkedJob, cancellationToken).ConfigureAwait(false);
                return Failed;
            }

            if (!import)
            {
                await PrintAsync(checkedJob, cancellationToken).ConfigureAwait(false);
                return Ok;
            }

            var hasWarnings = HasWarnings(checkedJob.CheckResult);
            if (hasWarnings && !options.Has("--accept-warnings"))
            {
                await PrintAsync(checkedJob, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync("The check has warnings. Run again with --accept-warnings to import anyway.").ConfigureAwait(false);
                return ConfirmationRequired;
            }

            if (mode == TransferModes.Restore && !string.Equals(options.Value("--confirm")?.Trim(), site!.Name, StringComparison.Ordinal))
            {
                await output.WriteLineAsync("A restore needs --confirm with the site's current name, exactly.").ConfigureAwait(false);
                return ConfirmationRequired;
            }

            var confirmed = await DispatchAsync(
                new ConfirmImportCommand(
                    jobId,
                    asNew ? options.Value("--name") : null,
                    asNew ? options.Value("--host-name") : null,
                    asNew && options.Has("--start-fresh"),
                    into is not null ? options.Value("--on-conflict") : null,
                    hasWarnings,
                    mode == TransferModes.Restore ? options.Value("--confirm") : null,
                    checkedJob.CheckFingerprint),
                cancellationToken).ConfigureAwait(false);
            if (confirmed.IsFailure)
            {
                await output.WriteLineAsync($"The import could not start: {confirmed.Error!.Code}. {confirmed.Error.Message}").ConfigureAwait(false);
                return confirmed.Error.Code == TransferErrors.Busy.Code ? Busy
                    : confirmed.Error.Code == TransferErrors.ConfirmationMismatch.Code ? ConfirmationRequired
                    : confirmed.Error.Code is "transfer.invalid_options" or "sites.host_name_taken" ? Usage
                    : Failed;
            }

            var done = await RunToEndAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (done.State == TransferStates.Succeeded && options.Value("--pre-restore-out") is { } preRestoreOut && done.PreRestoreJobId is { } preRestore)
            {
                await using var scope = command._scopes.CreateAsyncScope();
                var export = await scope.ServiceProvider.GetRequiredService<TransferJobStore>().FindAsync(preRestore, cancellationToken).ConfigureAwait(false);
                if (export?.ArchiveKey is { } key && !await CopyOutAsync(key, preRestoreOut, cancellationToken).ConfigureAwait(false))
                {
                    await output.WriteLineAsync($"The site was restored, but its earlier state could not be written to {preRestoreOut}; it is kept in storage for 7 days.").ConfigureAwait(false);
                }
            }

            await PrintAsync(done, cancellationToken).ConfigureAwait(false);
            return done.State == TransferStates.Succeeded ? Ok : Failed;
        }

        /// <summary>Copies the operator's file into transfer storage as an upload (research ST-17; no size limit here).</summary>
        private async Task<Guid> UploadAsync(string file, CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var job = TransferJob.ReserveUpload(TransferTriggers.Cli, startedBy: null, command._clock.GetUtcNow());
            await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false);

            var key = TransferStorageKeys.ForInstallation(job.Id);
            long bytes;
            await using (var source = command._files.OpenRead(file))
            {
                var counting = new CountingReadStream(source);
                await storage.WriteAsync(key, counting, cancellationToken).ConfigureAwait(false);
                bytes = counting.Total;
            }

            job.Uploaded(key.Value, bytes, command._clock.GetUtcNow());
            await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false);
            return job.Id;
        }

        // --- running ------------------------------------------------------------------------

        /// <summary>
        /// Claims the job and runs it here, printing progress every 5 seconds, until it waits for
        /// confirmation or ends. When another instance's worker claims it first, follows that run.
        /// </summary>
        private async Task<TransferJob> RunToEndAsync(Guid jobId, CancellationToken cancellationToken)
        {
            var worker = command._worker;
            TransferJob? claimed = null;
            await using (var scope = command._scopes.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
                var job = await store.FindAsync(jobId, cancellationToken).ConfigureAwait(false);
                if (job is { IsClaimable: true })
                {
                    job.Claim(TransferWorker.InstanceId, command._clock.GetUtcNow());
                    if (await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false) == SaveOutcome.Saved)
                    {
                        claimed = job;
                    }
                }
            }

            var running = claimed is null ? Task.CompletedTask : worker.RunAsync(claimed, cancellationToken);
            while (true)
            {
                var finished = await Task.WhenAny(running, Task.Delay(ProgressEvery, command._clock, cancellationToken)).ConfigureAwait(false) == running;
                var current = await FindAsync(jobId, cancellationToken).ConfigureAwait(false);
                var settled = TransferStates.Final.Contains(current.State) || current.State == TransferStates.AwaitingConfirmation;
                if ((finished && claimed is not null) || settled)
                {
                    await running.ConfigureAwait(false);
                    return await FindAsync(jobId, cancellationToken).ConfigureAwait(false);
                }

                if (!_json)
                {
                    await output.WriteLineAsync($"[{current.Step ?? current.State}] {current.Percent}%").ConfigureAwait(false);
                }
            }
        }

        private async Task<bool> CopyOutAsync(string archiveKey, string file, CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await using var stored = await storage.OpenReadAsync(new StorageKey(archiveKey), cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return false;
            }

            try
            {
                await using var target = command._files.CreateNew(file);
                await stored.Content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (IOException)
            {
                // Never overwrite; a partial file of our own is removed.
                if (!command._files.FileExists(file))
                {
                    return false;
                }

                throw;
            }
            catch
            {
                command._files.Delete(file);
                throw;
            }
        }

        // --- output -------------------------------------------------------------------------

        private async Task PrintAsync(TransferJob? job, CancellationToken cancellationToken)
        {
            if (job is null)
            {
                return;
            }

            if (_json)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(await ResponseAsync(job, cancellationToken).ConfigureAwait(false), Json)).ConfigureAwait(false);
                return;
            }

            await output.WriteLineAsync($"transfer {job.Id:N}: {job.State}{(job.FailureCode is { } code ? $" ({code})" : string.Empty)}").ConfigureAwait(false);
            foreach (var text in new[] { job.Summary, job.CheckResult, job.Report }.OfType<string>())
            {
                await PrintCountsAsync(text).ConfigureAwait(false);
            }
        }

        /// <summary>Counts and problem codes only: labels are content and stay out of the output.</summary>
        private async Task PrintCountsAsync(string json)
        {
            using var document = JsonDocument.Parse(json);
            foreach (var section in new[] { "counts", "imported", "removed" })
            {
                if (document.RootElement.TryGetProperty(section, out var counts) && counts.ValueKind == JsonValueKind.Object)
                {
                    foreach (var count in counts.EnumerateObject())
                    {
                        await output.WriteLineAsync($"  {section} {count.Name}: {count.Value}").ConfigureAwait(false);
                    }
                }
            }

            if (document.RootElement.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
            {
                foreach (var problem in problems.EnumerateArray())
                {
                    var severity = problem.TryGetProperty("severity", out var s) ? s.GetString() : "problem";
                    await output.WriteLineAsync($"  {severity} {problem.GetProperty("code").GetString()}").ConfigureAwait(false);
                }
            }

            if (document.RootElement.TryGetProperty("conflicts", out var conflicts) && conflicts.ValueKind == JsonValueKind.Array)
            {
                await output.WriteLineAsync($"  conflicts: {conflicts.GetArrayLength()}").ConfigureAwait(false);
            }
        }

        private async Task<TransferResponse> ResponseAsync(TransferJob job, CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            return await TransferResponses.BuildAsync(job, scope.ServiceProvider.GetRequiredService<IUserDirectory>(), command._clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> UnknownSiteAsync(string reference)
        {
            await output.WriteLineAsync($"No site has the id or host name '{reference}'.").ConfigureAwait(false);
            return UnknownSite;
        }

        // --- lookups --------------------------------------------------------------------------

        private async Task<SiteDto?> ResolveAsync(string reference, CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            var directory = scope.ServiceProvider.GetRequiredService<ISiteDirectory>();
            if (Guid.TryParse(reference, out var id))
            {
                return await directory.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
            }

            // Disabled sites too: an operator backs up and restores those as well.
            if (!HostNames.TryNormalise(reference, out var host))
            {
                return null;
            }

            var owner = await scope.ServiceProvider.GetRequiredService<SitesDbContext>().HostNames
                .AcrossAllSites("The operator names a site by one of its host names (020 operator-commands.md).")
                .AsNoTracking()
                .Where(h => h.HostName == host)
                .Select(h => (Guid?)h.SiteId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return owner is { } siteId ? await directory.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false) : null;
        }

        private async Task<List<(Guid Id, string PrimaryHostName)>> SitesAsync(CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            var sites = await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Sites
                .AcrossAllSites("The operator exports every site (020 FR-033).")
                .AsNoTracking()
                .Include(s => s.HostNames)
                .OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. sites.Select(s => (s.Id, s.PrimaryHostName))];
        }

        private async Task<TransferJob> FindAsync(Guid jobId, CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TransferJobStore>().FindAsync(jobId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Transfer {jobId} disappeared.");
        }

        private async Task<QuickBuilder.Shared.Kernel.Results.Result<TransferResponse>> DispatchAsync(ICommand<QuickBuilder.Shared.Kernel.Results.Result<TransferResponse>> request, CancellationToken cancellationToken)
        {
            await using var scope = command._scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private static bool HasWarnings(string? checkResult)
        {
            if (checkResult is null)
            {
                return false;
            }

            using var document = JsonDocument.Parse(checkResult);
            return document.RootElement.TryGetProperty("problems", out var problems)
                && problems.EnumerateArray().Any(p => p.TryGetProperty("severity", out var s) && s.GetString() == "warning");
        }
    }

    /// <summary>Counts the bytes read, for the upload's recorded size.</summary>
    private sealed class CountingReadStream(Stream inner) : Stream
    {
        public long Total { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => Total; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Total += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Total += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
