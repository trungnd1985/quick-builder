using Microsoft.Extensions.DependencyInjection;

namespace QuickBuilder.Shared.Infrastructure.Commands;

/// <summary>
/// Chooses between "run a command and exit" and "start the web server"
/// (research R-16).
/// </summary>
public static class HostCommandRunner
{
    /// <summary>Exit code for an unrecognised command (sysexits EX_USAGE).</summary>
    public const int UnknownCommandExitCode = 64;

    /// <summary>
    /// Runs the command named by <paramref name="args"/>[0], or returns null when there is
    /// no command so the caller starts the web server.
    /// </summary>
    /// <remarks>
    /// An argument starting with <c>-</c> is a host option such as <c>--urls</c>, not a
    /// command, so the web server still starts with it.
    /// </remarks>
    public static async Task<int?> TryRunAsync(
        IReadOnlyList<string> args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        if (args.Count == 0 || args[0].StartsWith('-'))
        {
            return null;
        }

        var commands = services.GetServices<IHostCommand>().ToList();
        var command = commands.Find(c => string.Equals(c.Name, args[0], StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            await Console.Error.WriteLineAsync(
                $"Unknown command '{args[0]}'. Known commands: " +
                string.Join(", ", commands.Select(c => c.Name).Order(StringComparer.Ordinal)) + ".")
                .ConfigureAwait(false);
            return UnknownCommandExitCode;
        }

        return await command.RunAsync([.. args.Skip(1)], services, cancellationToken).ConfigureAwait(false);
    }
}
