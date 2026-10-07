namespace QuickBuilder.Cms.Contracts;

/// <summary>
/// The limits of collection schema version 1 (docs/contracts/collection-schema.md §8,
/// research CS-18).
/// </summary>
/// <remarks>
/// Raising one is additive and needs no new contract version. Those the schema file also
/// states are checked against it by <c>CollectionSchemaContractTests</c>.
/// </remarks>
public static class CollectionSchemaLimits
{
    /// <summary>Collections per site.</summary>
    public const int MaxCollectionsPerSite = 100;

    /// <summary>Fields per collection.</summary>
    public const int MaxFields = 100;

    /// <summary>Options per option field.</summary>
    public const int MaxOptions = 200;

    /// <summary>Items one multi-reference value may list.</summary>
    public const int MaxReferences = 100;

    /// <summary>The most bytes of UTF-8 JSON one proposal may take.</summary>
    public const int MaxProposalBytes = 65_536;

    /// <summary>Display names of collections and fields, and option labels, in characters.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Field keys and collection address names, in characters.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>A field's help text, in characters.</summary>
    public const int MaxHelpLength = 300;

    /// <summary>A collection's description, in characters.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>The longest a text field's <c>maxLength</c> may be.</summary>
    public const int MaxTextLength = 10_000;

    /// <summary>The most decimal places a number field may keep.</summary>
    public const int MaxDecimals = 6;

    /// <summary>The largest whole number a number value may hold, so JavaScript reads it exactly.</summary>
    public const long MaxSafeInteger = 9_007_199_254_740_991;

    /// <summary>How many example items a preview names per change.</summary>
    public const int MaxPreviewExamples = 10;

    /// <summary>The form of field keys and collection address names.</summary>
    public const string KeyPattern = "^[a-z][a-z0-9-]{0,63}$";
}
