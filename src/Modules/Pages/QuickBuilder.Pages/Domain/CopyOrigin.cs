namespace QuickBuilder.Pages.Domain;

/// <summary>
/// Where a copy came from (023 FR-021, research CE-11): the document and the version the member
/// had loaded, identified by when it was saved, and who made the copy, when.
/// </summary>
internal sealed record CopyOrigin(Guid SourceId, DateTimeOffset SourceSavedAtUtc, Guid CopiedBy, DateTimeOffset CopiedAtUtc);
