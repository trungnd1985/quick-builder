namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// Checks a content document against the page schema contract (docs/contracts/page-schema.md).
/// </summary>
/// <remarks>
/// The one validator both the Pages module and, later, the renderer (#12) use, so both
/// sides accept exactly the same documents (constitution Principle VI). Checks run in the
/// order §3 gives, and stop at the first failure: size, well-formed JSON without duplicate
/// properties, a supported <c>schemaVersion</c>, a structural walk (depth, node count,
/// unique ids), the schema, then the component rules that need no database.
/// <para>
/// Whether a referenced component exists is not decided here: that needs the caller's
/// site, so the result lists the referenced ids and the caller checks them.
/// </para>
/// </remarks>
public interface IPageDocumentValidator
{
    DocumentValidationResult Validate(ReadOnlySpan<byte> utf8Json, DocumentKind kind);
}

/// <summary>Which kind of document is being checked; the rules differ (§3 rule 7).</summary>
public enum DocumentKind
{
    /// <summary>A page's content. May reference shared components.</summary>
    Page,

    /// <summary>A shared component's content. May not reference components.</summary>
    Component,
}

/// <summary>The outcome of <see cref="IPageDocumentValidator.Validate"/>.</summary>
public sealed record DocumentValidationResult
{
    private DocumentValidationResult()
    {
    }

    public bool IsValid => Rule is null;

    /// <summary>The document's <c>schemaVersion</c>, when it was read.</summary>
    public int SchemaVersion { get; private init; }

    /// <summary>Every distinct <c>componentId</c> the document references. Empty on failure.</summary>
    public IReadOnlySet<Guid> ReferencedComponentIds { get; private init; } = new HashSet<Guid>();

    /// <summary>
    /// Every distinct collection a Collection list block names as its source, with the pointer
    /// of the first <c>source</c> that names it (016, contract §3 rule 14). Whether each is a
    /// collection of the caller's site is the caller's to check, as for components. Empty on
    /// failure.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> ReferencedCollections { get; private init; } = new Dictionary<Guid, string>();

    /// <summary>
    /// The error code: <c>pages.invalid_document</c>, <c>pages.unsupported_schema_version</c>
    /// or <c>pages.document_too_large</c>. Null when valid.
    /// </summary>
    public string? Code { get; private init; }

    /// <summary>
    /// Which rule failed, e.g. <c>duplicate_node_id</c> (contracts/pages-api.yaml,
    /// <c>DocumentProblem</c>). Null when valid.
    /// </summary>
    public string? Rule { get; private init; }

    /// <summary>
    /// A JSON Pointer to the first offending location; empty for the whole document. Sent
    /// to clients as the problem's <c>pointer</c>.
    /// </summary>
    public string? Location { get; private init; }

    /// <summary>A human-readable explanation. Never includes document content.</summary>
    public string? Message { get; private init; }

    public static DocumentValidationResult Valid(int schemaVersion, IReadOnlySet<Guid> referencedComponentIds) =>
        new() { SchemaVersion = schemaVersion, ReferencedComponentIds = referencedComponentIds };

    public static DocumentValidationResult Valid(
        int schemaVersion, IReadOnlySet<Guid> referencedComponentIds, IReadOnlyDictionary<Guid, string> referencedCollections) =>
        new() { SchemaVersion = schemaVersion, ReferencedComponentIds = referencedComponentIds, ReferencedCollections = referencedCollections };

    public static DocumentValidationResult Invalid(string code, string rule, string location, string message) =>
        new() { Code = code, Rule = rule, Location = location, Message = message };
}
