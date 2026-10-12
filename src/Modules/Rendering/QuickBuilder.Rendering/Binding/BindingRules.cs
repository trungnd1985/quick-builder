namespace QuickBuilder.Rendering.Binding;

/// <summary>What kind of value a bindable property takes (binding-expression.md §4).</summary>
internal enum PropKind
{
    Text,
    RichText,
    Image,
    Link,
}

/// <summary>
/// Which value may fill which block property: the compatibility table of
/// binding-expression.md §4 (016 research TB-05).
/// </summary>
/// <remarks>
/// The admin's <c>bindingRules.ts</c> holds the same table, and both run the contract's
/// <c>resolve/</c> golden cases, so the editor marks exactly the bindings the site treats as
/// broken. A broken binding is never refused at save: schemas change in another module
/// afterwards, so it is shown as empty instead.
/// </remarks>
internal static class BindingRules
{
    public const string FieldMissing = "field_missing";
    public const string TypeMismatch = "type_mismatch";
    public const string ScopeMissing = "scope_missing";
    public const string ViaNotReference = "via_not_reference";

    /// <summary>The bindable properties of each block type, and the kind of value each takes.</summary>
    public static PropKind? KindOf(string blockType, string prop) => (blockType, prop) switch
    {
        ("heading", "text") or ("text", "text") or ("button", "label") or ("image", "alt") => PropKind.Text,
        ("rich-text", "content") => PropKind.RichText,
        ("image", "media") => PropKind.Image,
        ("button", "link") or ("image", "link") or ("heading", "link") => PropKind.Link,
        _ => null,
    };

    /// <summary>Whether a field of <paramref name="field"/>'s type can fill a property of <paramref name="kind"/>.</summary>
    public static bool Fits(PropKind kind, FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return kind switch
        {
            PropKind.Text => field.Type is FieldInfo.Text or FieldInfo.Number or FieldInfo.Date or FieldInfo.Option
                or FieldInfo.Boolean or FieldInfo.Reference,
            PropKind.RichText => field.Type == FieldInfo.RichText || (field.Type == FieldInfo.Text && field.Multiline),
            PropKind.Image => field.Type == FieldInfo.Image,
            PropKind.Link => field.Type == FieldInfo.File || (field.Type == FieldInfo.Text && !field.Multiline),
            _ => false,
        };
    }

    /// <summary>Whether a built-in item value can fill a property of <paramref name="kind"/>.</summary>
    public static bool Fits(PropKind kind, string builtIn) => kind switch
    {
        PropKind.Text => builtIn is BuiltIns.Slug or BuiltIns.FirstPublishedAt or BuiltIns.LastPublishedAt,
        PropKind.Link => builtIn == BuiltIns.Url,
        _ => false,
    };
}

/// <summary>The built-in item values (binding-expression.md §2).</summary>
internal static class BuiltIns
{
    public const string Url = "url";
    public const string Slug = "slug";
    public const string FirstPublishedAt = "firstPublishedAt";
    public const string LastPublishedAt = "lastPublishedAt";
}
