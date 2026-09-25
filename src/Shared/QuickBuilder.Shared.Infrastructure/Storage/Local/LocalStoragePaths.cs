namespace QuickBuilder.Shared.Infrastructure.Storage.Local;

/// <summary>
/// Path handling shared by the local provider, its startup validation and its health check.
/// </summary>
internal static class LocalStoragePaths
{
    /// <summary>The directory, under a key's first segment, that holds writes in progress.</summary>
    internal const string IncomingDirectoryName = ".incoming";

    /// <summary>Prefix of the probe files written at the root by validation and health checks.</summary>
    internal const string ProbePrefix = ".probe-";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// The absolute path with every existing symbolic link along it resolved, so that a link
    /// cannot smuggle the storage directory into the application's own (003 research M-05).
    /// </summary>
    internal static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;

        foreach (var segment in full[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            var info = new DirectoryInfo(next);

            if (info.Exists && info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    next = Canonicalize(target.FullName);
                }
            }

            current = next;
        }

        return Path.TrimEndingDirectorySeparator(current.Length == 0 ? full : current);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or lies under it.</summary>
    internal static bool IsSameOrUnder(string path, string root)
    {
        var p = Path.TrimEndingDirectorySeparator(path);
        var r = Path.TrimEndingDirectorySeparator(root);

        return string.Equals(p, r, PathComparison)
            || p.StartsWith(r + Path.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>
    /// Writes, reads back and deletes a probe file at the root. Null on success; otherwise
    /// why it failed, for a startup message. Never throws.
    /// </summary>
    internal static async Task<string?> ProbeAsync(string root, CancellationToken cancellationToken)
    {
        var probe = Path.Combine(root, ProbePrefix + Guid.NewGuid().ToString("N"));
        byte[] expected = [0x51, 0x42, 0x2d, 0x6f, 0x6b];

        try
        {
            await File.WriteAllBytesAsync(probe, expected, cancellationToken).ConfigureAwait(false);
            var actual = await File.ReadAllBytesAsync(probe, cancellationToken).ConfigureAwait(false);

            return actual.AsSpan().SequenceEqual(expected)
                ? null
                : "a file written there did not read back unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return ex is OperationCanceledException ? "the check timed out." : ex.Message;
        }
        finally
        {
            TryDelete(probe);
        }
    }

    internal static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. Leftover probe and .part files are removed by
            // LocalIncomingCleanupService once they are an hour old.
        }
    }
}
