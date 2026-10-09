using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Cms.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Cms.Items;

/// <summary>
/// The server's report for every golden item-values example equals <c>expected.json</c>
/// (015 FR-012, research IT-05). The admin runs the same files in <c>itemValues.spec.ts</c>.
/// </summary>
/// <remarks>
/// The examples need no database: the lookups here know no media file and no item, so a media
/// or reference value is always reported missing, as <c>expected.json</c> says.
/// </remarks>
public sealed class ItemValueGoldenTests
{
    private static readonly string Directory = Path.Combine(CmsContractFiles.ExamplesDirectory, "items");

    private static readonly CollectionSchemaDocument Schema =
        CollectionSchemaDocument.Read(File.ReadAllText(Path.Combine(Directory, "schema.json")));

    private static readonly ItemValidation Validation = new(
        new ItemValueValidator(new CollectionSchemaValidator()),
        new NoMedia(),
        new NoItems());

    public static TheoryData<string> Examples() => [.. ExampleNames()];

    private static List<string> ExampleNames()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, "expected.json")));
        return expected.RootElement.EnumerateObject().Select(e => e.Name).ToList();
    }

    [Fact]
    public void Every_example_file_has_an_expected_report()
    {
        var files = System.IO.Directory.GetFiles(Directory, "*.json")
            .Select(Path.GetFileName)
            .Where(f => f is not ("schema.json" or "expected.json"))
            .Order(StringComparer.Ordinal);

        Assert.Equal(files, ExampleNames().Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task The_report_is_the_expected_one(string file)
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, "expected.json")));
        var want = expected.RootElement.GetProperty(file).GetProperty("problems").EnumerateArray()
            .Select(p => new ItemProblem(
                p.GetProperty("fieldId").GetString()!,
                p.GetProperty("problem").GetString()!,
                p.TryGetProperty("limit", out var limit) ? limit.GetDecimal() : null,
                p.TryGetProperty("format", out var format) ? format.GetString() : null,
                p.TryGetProperty("path", out var path) ? path.GetString() : null))
            .ToList();

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, file)));
        var values = ItemInput.ReadValues(document.RootElement);
        Assert.NotNull(values);

        var report = await Validation.ValidateAsync(Guid.NewGuid(), Guid.NewGuid(), Schema, "example", values, CancellationToken.None);

        Assert.Equal(want, report.Problems);
    }

    private sealed class NoMedia : IItemMediaLookup
    {
        public Task<Func<Guid, MediaKind>> LookupAsync(Guid siteId, IEnumerable<JsonNode?> values, CancellationToken cancellationToken) =>
            Task.FromResult<Func<Guid, MediaKind>>(_ => MediaKind.Missing);
    }

    private sealed class NoItems : IItemReferenceLookup
    {
        public Task<IReadOnlyDictionary<Guid, Guid>> CollectionsOfAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());
    }
}
