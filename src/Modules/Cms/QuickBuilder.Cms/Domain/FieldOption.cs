namespace QuickBuilder.Cms.Domain;

/// <summary>One choice of an option field: a permanent id and a label (contract §2).</summary>
/// <remarks>Items store the id, so relabelling an option changes no item (FR-025).</remarks>
internal sealed record FieldOption(string Id, string Label);
