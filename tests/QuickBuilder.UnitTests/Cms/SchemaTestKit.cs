using QuickBuilder.Cms.Domain;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>Small builders for fields and schemas in the Cms unit tests.</summary>
internal static class SchemaTestKit
{
    public static readonly Guid People = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f");

    public static Field Text(string id, string key = "title", bool required = false, bool multiline = false, int maxLength = 256) =>
        new() { Id = id, Key = key, Name = key, Required = required, Type = FieldType.Text, Multiline = multiline, MaxLength = maxLength };

    public static Field Of(FieldType type, string id, string? key = null) => type switch
    {
        FieldType.Text => Text(id, key ?? $"k{id}"),
        FieldType.Number => new Field { Id = id, Key = key ?? $"k{id}", Name = "n", Type = type, Decimals = 0 },
        FieldType.Date => new Field { Id = id, Key = key ?? $"k{id}", Name = "n", Type = type },
        FieldType.File => new Field { Id = id, Key = key ?? $"k{id}", Name = "n", Type = type, Accept = FileAccept.Any },
        FieldType.Option => new Field { Id = id, Key = key ?? $"k{id}", Name = "n", Type = type, Options = [new FieldOption("o1", "A")] },
        FieldType.Reference or FieldType.MultiReference => new Field { Id = id, Key = key ?? $"k{id}", Name = "n", Type = type, Target = People },
        _ => new Field { Id = id, Key = key ?? $"k{id}", Name = "n", Type = type },
    };

    public static CollectionSchemaDocument Doc(params Field[] fields) => new() { Fields = fields };

    public static CollectionDetails Details { get; } = new("Blog posts", "blog-posts", null);

    public static IReadOnlyList<FieldChange> Diff(CollectionSchemaDocument before, CollectionSchemaDocument after, params string[] added) =>
        SchemaDiff.Compare(Details, before, Details, after, added.ToHashSet(StringComparer.Ordinal));
}
