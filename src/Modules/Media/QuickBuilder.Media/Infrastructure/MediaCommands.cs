using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// <c>media check-storage</c>: the consistency check from the command line, for operators
/// who have just copied media between providers (013 research MV-21,
/// contracts/storage-providers.md §6).
/// </summary>
/// <remarks>
/// Runs synchronously and prints counts and up to 100 <c>site/file</c> ids; file names never
/// appear. Exit codes: <c>0</c> nothing missing, <c>3</c> files missing, <c>1</c> the check
/// could not run or failed, <c>64</c> usage.
/// </remarks>
internal sealed class MediaCommands(StorageCheckRunner runner, IServiceScopeFactory scopes) : IHostCommand
{
    private const int Ok = 0;
    private const int Error = 1;
    private const int Missing = 3;
    private const int Usage = 64;
    private const int PrintedIds = 100;

    public string Name => "media";

    public async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var output = Console.Out;

        if (args.Count == 0 || args[0] != "check-storage")
        {
            await output.WriteLineAsync("Usage: media check-storage").ConfigureAwait(false);
            return Usage;
        }

        var check = await runner.TryClaimAsync(StorageCheckTrigger.Cli, startedBy: null, cancellationToken).ConfigureAwait(false);
        if (check is null)
        {
            await output.WriteLineAsync("A storage check is already running. Try again when it has finished.").ConfigureAwait(false);
            return Error;
        }

        await output.WriteLineAsync($"Checking {runner.Provider} storage…").ConfigureAwait(false);
        check = await runner.RunAsync(check.Id, cancellationToken).ConfigureAwait(false);

        if (check.Status != StorageCheckStatus.Completed)
        {
            await output.WriteLineAsync("The storage check failed. See the application log for the reason.").ConfigureAwait(false);
            return Error;
        }

        await output.WriteLineAsync($"{check.FilesChecked} file(s) checked, {check.MissingCount} missing.").ConfigureAwait(false);
        if (check.MissingCount == 0)
        {
            return Ok;
        }

        await using var scope = scopes.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<MediaDbContext>().StorageCheckMissing
            .AcrossAllSites("The operator's command lists missing files of every site (013 MV-21).")
            .AsNoTracking()
            .Where(m => m.CheckId == check.Id)
            .OrderBy(m => m.SiteId).ThenBy(m => m.FileId)
            .Take(PrintedIds)
            .Select(m => new { m.SiteId, m.FileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in ids)
        {
            await output.WriteLineAsync($"missing {id.SiteId:N}/{id.FileId:N}").ConfigureAwait(false);
        }

        if (check.MissingCount > ids.Count)
        {
            await output.WriteLineAsync($"…and {check.MissingCount - ids.Count} more. The admin screen lists up to {StorageCheck.MaxListedMissing}.").ConfigureAwait(false);
        }

        return Missing;
    }
}
