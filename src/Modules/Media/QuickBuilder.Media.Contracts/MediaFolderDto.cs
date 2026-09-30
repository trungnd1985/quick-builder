namespace QuickBuilder.Media.Contracts;

/// <summary>One folder of a site's library (013 contracts/media-api.yaml <c>Folder</c>).</summary>
/// <param name="Id">The folder's id.</param>
/// <param name="Name">Its name. Render as text, never as HTML.</param>
/// <param name="ParentId">The containing folder; null at the top level.</param>
/// <param name="FileCount">Files directly in the folder.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
public sealed record MediaFolderDto(Guid Id, string Name, Guid? ParentId, int FileCount, DateTimeOffset CreatedAtUtc);

/// <summary>A site's whole folder tree (013 contracts/media-api.yaml <c>FolderTree</c>).</summary>
/// <param name="Folders">Every folder, at most 1,000.</param>
/// <param name="TopLevelFileCount">Files not in any folder.</param>
/// <param name="Revision">Changes on every structural change.</param>
public sealed record MediaFolderTreeDto(IReadOnlyList<MediaFolderDto> Folders, int TopLevelFileCount, long Revision);
