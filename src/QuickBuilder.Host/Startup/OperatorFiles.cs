using QuickBuilder.Shared.Infrastructure.Commands;

namespace QuickBuilder.Host;

/// <summary>
/// The local file system, for files an operator names on a command line, such as a site archive
/// for <c>sites export</c> and <c>sites import</c> (020 contracts/operator-commands.md).
/// </summary>
internal sealed class OperatorFiles : IOperatorFiles
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public Stream CreateNew(string path) =>
        new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81_920, FileOptions.Asynchronous);

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
