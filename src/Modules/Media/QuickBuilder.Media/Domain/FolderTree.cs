using System.Globalization;
using System.Text;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// One row per site whose row version serialises changes to that site's folder structure
/// (013 data-model.md §1.3, research MV-02).
/// </summary>
/// <remarks>
/// Two members moving folders at once could each pass the "no cycle, at most 8 levels" check
/// against the tree they loaded, and together break it. Every structural change bumps
/// <see cref="Revision"/> in the same transaction, so the second save fails its concurrency
/// check instead of committing a cycle. This is the same pattern as 010's page tree.
/// </remarks>
internal sealed class FolderTree : ISiteOwned
{
    private FolderTree()
    {
    }

    public FolderTree(Guid siteId) => SiteId = siteId;

    public Guid SiteId { get; private set; }

    public long Revision { get; private set; }

    /// <summary>PostgreSQL's <c>xmin</c>, mapped as the concurrency token.</summary>
    public uint Version { get; private set; }

    internal void Touch() => Revision++;
}

/// <summary>
/// The rules of a site's folder structure, checked against the whole tree (013 FR-001,
/// FR-002, FR-004, data-model.md §3.2).
/// </summary>
/// <remarks>
/// A site has at most <see cref="MaxFolders"/> folders, so the handlers load them all and
/// this checks depth, cycles and sibling names in memory. The database still has the last
/// word: the sibling-name index, the <c>RESTRICT</c> foreign keys and the tree's row version.
/// </remarks>
internal sealed class FolderStructure
{
    /// <summary>How deep folders may nest; a top-level folder is at depth 1 (FR-001).</summary>
    public const int MaxDepth = 8;

    /// <summary>How many folders one site may have (research MV-02).</summary>
    public const int MaxFolders = 1_000;

    private readonly Dictionary<Guid, MediaFolder> _byId;

    public FolderStructure(IEnumerable<MediaFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _byId = folders.ToDictionary(f => f.Id);
    }

    public int Count => _byId.Count;

    public MediaFolder? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>Creates a folder under <paramref name="parentId"/>, or at the top level.</summary>
    public Result<MediaFolder> Create(Guid id, Guid siteId, Guid? parentId, string? rawName, DateTimeOffset now, Guid createdBy)
    {
        if (CleanName(rawName) is not { } name)
        {
            return Result.Failure<MediaFolder>(MediaErrors.InvalidFolderName);
        }

        if (parentId is { } p && !_byId.ContainsKey(p))
        {
            return Result.Failure<MediaFolder>(MediaErrors.FolderNotFound);
        }

        if (_byId.Count >= MaxFolders)
        {
            return Result.Failure<MediaFolder>(MediaErrors.FolderLimit);
        }

        if (DepthOf(parentId) + 1 > MaxDepth)
        {
            return Result.Failure<MediaFolder>(MediaErrors.FolderTooDeep);
        }

        if (NameTaken(parentId, name, except: null))
        {
            return Result.Failure<MediaFolder>(MediaErrors.FolderNameTaken);
        }

        var folder = new MediaFolder(id, siteId, parentId, name, now, createdBy);
        _byId.Add(folder.Id, folder);
        return Result.Success(folder);
    }

    /// <summary>Renames <paramref name="folder"/>; its siblings' names must stay distinct.</summary>
    public Result Rename(MediaFolder folder, string? rawName)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (CleanName(rawName) is not { } name)
        {
            return Result.Failure(MediaErrors.InvalidFolderName);
        }

        if (NameTaken(folder.ParentId, name, except: folder.Id))
        {
            return Result.Failure(MediaErrors.FolderNameTaken);
        }

        folder.Rename(name);
        return Result.Success();
    }

    /// <summary>
    /// Moves <paramref name="folder"/> and everything in it under <paramref name="parentId"/>,
    /// or to the top level. No file's address changes (FR-003).
    /// </summary>
    public Result Move(MediaFolder folder, Guid? parentId)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (parentId is { } p)
        {
            if (!_byId.ContainsKey(p))
            {
                return Result.Failure(MediaErrors.FolderNotFound);
            }

            if (Descendants(folder.Id).Contains(p))
            {
                return Result.Failure(MediaErrors.FolderCycle);
            }
        }

        if (DepthOf(parentId) + HeightOf(folder.Id) > MaxDepth)
        {
            return Result.Failure(MediaErrors.FolderTooDeep);
        }

        if (NameTaken(parentId, folder.Name, except: folder.Id))
        {
            return Result.Failure(MediaErrors.FolderNameTaken);
        }

        folder.MoveTo(parentId);
        return Result.Success();
    }

    /// <summary><paramref name="id"/> and every folder inside it, at any depth.</summary>
    public IReadOnlySet<Guid> Descendants(Guid id)
    {
        var children = _byId.Values
            .Where(f => f.ParentId is not null)
            .ToLookup(f => f.ParentId!.Value, f => f.Id);

        var found = new HashSet<Guid> { id };
        var pending = new Stack<Guid>();
        pending.Push(id);

        while (pending.Count > 0)
        {
            foreach (var child in children[pending.Pop()])
            {
                if (found.Add(child))
                {
                    pending.Push(child);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// A folder name cleaned for storage, or null when nothing usable is left or it is too
    /// long (FR-004): path segments dropped (only the last is kept, as for file names),
    /// control and format characters removed, and whitespace trimmed.
    /// </summary>
    public static string? CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var lastSeparator = raw.LastIndexOfAny(['/', '\\']);
        var name = lastSeparator >= 0 ? raw[(lastSeparator + 1)..] : raw;

        var builder = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                builder.Append(rune.ToString());
            }
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length is 0 or > MediaFolder.MaxNameLength ? null : cleaned;
    }

    /// <summary>The depth of a folder: 1 at the top level; 0 for "no folder".</summary>
    private int DepthOf(Guid? id)
    {
        var depth = 0;
        var seen = new HashSet<Guid>();

        for (var current = id; current is { } c && _byId.TryGetValue(c, out var folder); current = folder.ParentId)
        {
            if (!seen.Add(c))
            {
                throw new InvalidOperationException("The stored folder tree contains a cycle; the row version should have prevented it.");
            }

            depth++;
        }

        return depth;
    }

    /// <summary>How many levels the subtree rooted at <paramref name="id"/> spans, itself included.</summary>
    private int HeightOf(Guid id)
    {
        var inside = Descendants(id);
        return inside.Max(d => DepthOf(d)) - DepthOf(id) + 1;
    }

    private bool NameTaken(Guid? parentId, string name, Guid? except)
    {
        var key = MediaFolder.KeyFor(name);
        return _byId.Values.Any(f => f.ParentId == parentId && f.Id != except && f.NameKey == key);
    }
}
