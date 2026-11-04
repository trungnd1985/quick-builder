namespace QuickBuilder.Shared.Infrastructure.Commands;

/// <summary>
/// Files an operator names on a command line, on the machine the command runs on (020
/// contracts/operator-commands.md).
/// </summary>
/// <remarks>
/// Modules never touch the file system: stored bytes go through <c>IFileStorage</c> (003
/// FR-019). An operator's backup file is not stored content, so the host, which may use the
/// file system, implements this for the commands that read or write such files.
/// </remarks>
public interface IOperatorFiles
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Opens an existing file for reading. Throws <see cref="FileNotFoundException"/> when there is none.</summary>
    Stream OpenRead(string path);

    /// <summary>Creates a file that must not exist yet. Throws <see cref="IOException"/> when it does.</summary>
    Stream CreateNew(string path);

    /// <summary>Removes a file the command wrote but could not finish. Nothing happens when there is none.</summary>
    void Delete(string path);
}
