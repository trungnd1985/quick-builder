using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// <c>identity setup-code</c> and <c>identity recovery-link --email …</c>: the operator's way
/// back in when nobody can sign in (spec FR-028, FR-004; contracts/operator-commands.md).
/// </summary>
/// <remarks>
/// Run with command-line access to the installation, e.g.
/// <c>docker compose exec app dotnet QuickBuilder.Host.dll identity setup-code</c>. Nothing
/// here is reachable over the network. Output goes to standard output directly, never
/// through the logger: a recovery token must not end up in collected logs.
/// </remarks>
internal sealed class IdentityCommands(
    IServiceScopeFactory scopes,
    IOptions<PublicUrlOptions> publicUrl,
    IConfiguration configuration) : IHostCommand
{
    private const int Ok = 0;
    private const int WrongSetupState = 2;
    private const int MigrationsPending = 3;
    private const int NoSuchAdministrator = 4;
    private const int Usage = 64;

    public string Name => "identity";

    public async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        var output = Console.Out;
        var subcommand = args.Count > 0 ? args[0] : string.Empty;

        if (subcommand is not ("setup-code" or "recovery-link"))
        {
            await PrintUsageAsync(output).ConfigureAwait(false);
            return Usage;
        }

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if ((await database.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
        {
            await output.WriteLineAsync("The database has pending migrations. Run the migrate command first.").ConfigureAwait(false);
            return MigrationsPending;
        }

        return subcommand == "setup-code"
            ? await PrintSetupCodeAsync(scope.ServiceProvider, output, cancellationToken).ConfigureAwait(false)
            : await IssueRecoveryLinkAsync(scope.ServiceProvider, EmailArgument(args), output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> PrintSetupCodeAsync(IServiceProvider services, TextWriter output, CancellationToken cancellationToken)
    {
        var database = services.GetRequiredService<IdentityDbContext>();
        var events = services.GetRequiredService<SecurityEventRecorder>();
        var installation = await services.GetRequiredService<InstallationStore>().EnsureAsync(cancellationToken).ConfigureAwait(false);

        if (installation.IsClaimed || installation.SetupCode is null)
        {
            events.Record(SecurityEventTypes.OperatorRecoveryUsed, SecurityEventOutcomes.Refused, null, null, new OperatorCommandDetails("setup-code"));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync("Setup is complete; there is no setup code.").ConfigureAwait(false);
            return WrongSetupState;
        }

        events.Record(SecurityEventTypes.OperatorRecoveryUsed, SecurityEventOutcomes.Succeeded, null, null, new OperatorCommandDetails("setup-code"));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Setup code: {Tokens.FormatSetupCode(installation.SetupCode)}").ConfigureAwait(false);
        return Ok;
    }

    private async Task<int> IssueRecoveryLinkAsync(IServiceProvider services, string? email, TextWriter output, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            await PrintUsageAsync(output).ConfigureAwait(false);
            return Usage;
        }

        var database = services.GetRequiredService<IdentityDbContext>();
        var events = services.GetRequiredService<SecurityEventRecorder>();

        if (!(await services.GetRequiredService<InstallationStore>().EnsureAsync(cancellationToken).ConfigureAwait(false)).IsClaimed)
        {
            await output.WriteLineAsync("Setup is not complete. Use 'identity setup-code' instead.").ConfigureAwait(false);
            return WrongSetupState;
        }

        var normalised = User.NormaliseEmail(email);
        var administrator = await database.Users
            .Where(u => u.Email == normalised && u.Status == UserStatus.Active)
            .Join(database.RoleGrants.Where(g => g.Role == SystemRole.Administrator), u => u.Id, g => g.UserId, (u, g) => u)
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (administrator is null)
        {
            events.Record(SecurityEventTypes.OperatorRecoveryUsed, SecurityEventOutcomes.Refused, null, null, new OperatorCommandDetails("recovery-link"));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync("No active administrator has that email address.").ConfigureAwait(false);
            return NoSuchAdministrator;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var link = await services.GetRequiredService<SetPasswordLinkService>()
            .IssueAsync(administrator.Id, issuedByUserId: null, cancellationToken)
            .ConfigureAwait(false);
        events.Record(SecurityEventTypes.OperatorRecoveryUsed, SecurityEventOutcomes.Succeeded, null, administrator.Id, new OperatorCommandDetails("recovery-link"));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        await PrintLinkAsync(output, link).ConfigureAwait(false);
        return Ok;
    }

    /// <summary>
    /// The full link when the public address is configured; otherwise the path, for the
    /// operator to put after the address they use (research R-10: the server never guesses
    /// its own address).
    /// </summary>
    private async Task PrintLinkAsync(TextWriter output, IssuedLink link)
    {
        var basePath = (configuration["Hosting:BasePath"] ?? string.Empty).TrimEnd('/');
        var adminPrefix = (configuration["Admin:PathPrefix"] ?? "/admin").TrimEnd('/');
        var path = $"{basePath}{adminPrefix}/set-password#token={Uri.EscapeDataString(link.Token)}";
        var expires = link.ExpiresAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await output.WriteLineAsync($"Set-password link (expires {expires}):").ConfigureAwait(false);

        var publicAddress = publicUrl.Value.PublicUrl.TrimEnd('/');
        if (publicAddress.Length > 0)
        {
            // Hosting:PublicUrl is the address operators reach the installation at, base
            // path included, so only the admin part is appended.
            await output.WriteLineAsync($"{publicAddress}{adminPrefix}/set-password#token={Uri.EscapeDataString(link.Token)}").ConfigureAwait(false);
            return;
        }

        await output.WriteLineAsync(path).ConfigureAwait(false);
        await output.WriteLineAsync("Prepend the address you use to reach the admin, e.g. http://127.0.0.1:8080").ConfigureAwait(false);
    }

    private static string? EmailArgument(IReadOnlyList<string> args)
    {
        for (var i = 1; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--email", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static Task PrintUsageAsync(TextWriter output) =>
        output.WriteLineAsync(
            "Usage:\n" +
            "  identity setup-code                       Print the setup code (before setup).\n" +
            "  identity recovery-link --email <address>  Issue a set-password link for an active administrator.");
}
