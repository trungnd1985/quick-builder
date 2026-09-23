namespace QuickBuilder.Shared.Infrastructure.Commands;

/// <summary>
/// A command-line operation run instead of the web server, e.g.
/// <c>dotnet QuickBuilder.Host.dll migrate</c> (contracts/operator-commands.md).
/// </summary>
/// <remarks>
/// Modules register implementations from their <c>Add&lt;Module&gt;Module()</c>; the host
/// only matches the first argument to a <see cref="Name"/> and runs it. None of these is
/// reachable over the network, which is what makes them suitable for operator recovery
/// (spec FR-028).
/// </remarks>
public interface IHostCommand
{
    /// <summary>The first command-line argument that selects this command.</summary>
    string Name { get; }

    /// <summary>Runs the command and returns the process exit code.</summary>
    /// <param name="args">The arguments after the command name.</param>
    Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, CancellationToken cancellationToken);
}
