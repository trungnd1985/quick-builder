using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Cms.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Cms.Items;

/// <summary>
/// One report per item copy, one problem per field (015 research IT-05). Malformed values are
/// never stored; the rest are stored in a draft and refused at publish (clarification Q2).
/// </summary>
public sealed class ItemValidationTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Posts = Guid.NewGuid();
    private static readonly Guid People = SchemaTestKit.People;
    private static readonly Guid Image = Guid.NewGuid();
    private static readonly Guid Pdf = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid OtherPost = Guid.NewGuid();
    private static readonly Guid Self = Guid.NewGuid();

    private static readonly CollectionSchemaDocument Schema = SchemaTestKit.Doc(
        SchemaTestKit.Text("f1", "title", required: true, maxLength: 10),
        new Field { Id = "f2", Key = "price", Name = "n", Type = FieldType.Number, Decimals = 1, Min = 0, Max = 100 },
        new Field { Id = "f3", Key = "category", Name = "n", Type = FieldType.Option, Options = [new FieldOption("o1", "A")] },
        new Field { Id = "f4", Key = "cover", Name = "n", Type = FieldType.Image },
        new Field { Id = "f5", Key = "brochure", Name = "n", Type = FieldType.File, Accept = FileAccept.Documents },
        new Field { Id = "f6", Key = "author", Name = "n", Type = FieldType.Reference, Target = People },
        new Field { Id = "f7", Key = "related", Name = "n", Type = FieldType.MultiReference, Target = Posts },
        new Field { Id = "f8", Key = "featured", Name = "n", Type = FieldType.Boolean, Required = true });

    private static readonly ItemValidation Validation = new(
        new ItemValueValidator(new CollectionSchemaValidator()),
        new FakeMedia(new Dictionary<Guid, MediaKind> { [Image] = MediaKind.Image, [Pdf] = MediaKind.Document }),
        new FakeItems(new Dictionary<Guid, Guid> { [Ann] = People, [OtherPost] = Posts, [Self] = Posts }));

    private static ItemValues Values(JsonObject values) =>
        ItemValues.Read(JsonDocument.Parse(new JsonObject { ["schemaVersion"] = 1, ["values"] = values }.ToJsonString()));

    private static JsonObject Valid() => new()
    {
        ["f1"] = "Hello",
        ["f2"] = 9.5m,
        ["f3"] = "o1",
        ["f4"] = new JsonObject { ["mediaId"] = Image.ToString() },
        ["f5"] = new JsonObject { ["mediaId"] = Pdf.ToString() },
        ["f6"] = Ann.ToString(),
        ["f7"] = new JsonArray(OtherPost.ToString()),
    };

    private static async Task<ItemReport> ReportAsync(JsonObject values, string slug = "hello") =>
        await Validation.ValidateAsync(Site, Self, Schema, slug, Values(values), CancellationToken.None);

    [Fact]
    public async Task A_valid_item_has_an_empty_report()
    {
        var report = await ReportAsync(Valid());

        Assert.True(report.IsEmpty);
        Assert.False(report.HasMalformed);
    }

    [Theory]
    [InlineData("f2", "\"seven\"", ItemProblems.Malformed)]
    [InlineData("f3", "\"o99\"", ItemProblems.UnknownOption)]
    [InlineData("f9", "\"x\"", ItemProblems.UnknownField)]
    public async Task Malformed_values_are_reported_as_malformed(string fieldId, string json, string problem)
    {
        var values = Valid();
        values[fieldId] = JsonNode.Parse(json);

        var report = await ReportAsync(values);

        Assert.True(report.HasMalformed);
        Assert.Equal(new ItemProblem(fieldId, problem), Assert.Single(report.Problems));
    }

    [Fact]
    public async Task References_must_exist_be_in_the_target_collection_and_not_be_the_item()
    {
        var missing = Valid();
        missing["f6"] = Guid.NewGuid().ToString();
        Assert.Equal(ItemProblems.ReferenceMissing, Assert.Single((await ReportAsync(missing)).Problems).Problem);

        var wrong = Valid();
        wrong["f6"] = OtherPost.ToString();
        Assert.Equal(ItemProblems.ReferenceWrongCollection, Assert.Single((await ReportAsync(wrong)).Problems).Problem);

        var self = Valid();
        self["f7"] = new JsonArray(OtherPost.ToString(), Self.ToString());
        Assert.Equal(ItemProblems.ReferenceSelf, Assert.Single((await ReportAsync(self)).Problems).Problem);
    }

    [Fact]
    public async Task A_multi_reference_holds_distinct_items_up_to_the_limit()
    {
        var duplicate = Valid();
        duplicate["f7"] = new JsonArray(OtherPost.ToString(), OtherPost.ToString());
        Assert.Equal(ItemProblems.DuplicateReference, Assert.Single((await ReportAsync(duplicate)).Problems).Problem);

        var tooMany = Valid();
        tooMany["f7"] = new JsonArray(Enumerable.Range(0, 101).Select(_ => (JsonNode?)JsonValue.Create(Guid.NewGuid().ToString())).ToArray());
        var problem = Assert.Single((await ReportAsync(tooMany)).Problems);
        Assert.Equal(new ItemProblem("f7", ItemProblems.TooManyReferences, 100), problem);
    }

    [Fact]
    public async Task A_bad_slug_is_malformed() =>
        Assert.Equal(ItemProblems.InvalidSlug, Assert.Single((await ReportAsync(Valid(), "Not A Slug")).Problems).Problem);

    [Fact]
    public async Task Missing_and_out_of_settings_values_are_reported_not_malformed()
    {
        var values = Valid();
        values.Remove("f1");
        values["f2"] = 101m;
        values["f4"] = new JsonObject { ["mediaId"] = Pdf.ToString() };
        values["f5"] = new JsonObject { ["mediaId"] = Guid.NewGuid().ToString() };

        var report = await ReportAsync(values);

        Assert.False(report.HasMalformed);
        Assert.Equal(
            [
                new ItemProblem("f1", ItemProblems.MissingRequired),
                new ItemProblem("f2", ItemProblems.OutOfRange, 100),
                new ItemProblem("f4", ItemProblems.FileKind),
                new ItemProblem("f5", ItemProblems.MediaMissing),
            ],
            report.Problems);
    }

    [Fact]
    public async Task A_required_rich_text_with_no_text_and_no_images_is_missing()
    {
        var schema = SchemaTestKit.Doc(new Field { Id = "f1", Key = "body", Name = "n", Type = FieldType.RichText, Required = true });
        var empty = new JsonObject { ["f1"] = JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph"},{"type":"paragraph"}]}""") };
        var filled = new JsonObject { ["f1"] = JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}""") };

        var emptyReport = await Validation.ValidateAsync(Site, Self, schema, "hello", Values(empty), CancellationToken.None);
        var filledReport = await Validation.ValidateAsync(Site, Self, schema, "hello", Values(filled), CancellationToken.None);

        Assert.Equal([new ItemProblem("f1", ItemProblems.MissingRequired)], emptyReport.Problems);
        Assert.Empty(filledReport.Problems);
    }

    [Theory]
    [InlineData("f1", "\"Far too long a title\"", ItemProblems.TooLong, 10)]
    [InlineData("f1", "\"two\\nlines\"", ItemProblems.NotSingleLine, null)]
    [InlineData("f2", "9.55", ItemProblems.TooManyDecimals, 1)]
    [InlineData("f2", "-1", ItemProblems.OutOfRange, 0)]
    public async Task Each_broken_setting_has_its_code_and_limit(string fieldId, string json, string problem, int? limit)
    {
        var values = Valid();
        values[fieldId] = JsonNode.Parse(json);

        var report = await ReportAsync(values);

        Assert.Equal(new ItemProblem(fieldId, problem, limit), Assert.Single(report.Problems));
        Assert.False(report.HasMalformed);
    }

    [Fact]
    public async Task A_required_boolean_is_never_missing()
    {
        var report = await ReportAsync(Valid());

        Assert.DoesNotContain(report.Problems, p => p.FieldId == "f8");
    }

    [Fact]
    public void The_report_round_trips_through_the_attention_column()
    {
        var report = new ItemReport([new ItemProblem("f1", ItemProblems.TooLong, 10), new ItemProblem("f2", ItemProblems.MissingRequired)]);

        using var attention = report.ToAttention();

        Assert.Equal(report.Problems, ItemReport.Read(attention).Problems);
        Assert.Null(ItemReport.Clean.ToAttention());
    }

    private sealed class FakeMedia(Dictionary<Guid, MediaKind> files) : IItemMediaLookup
    {
        public Task<Func<Guid, MediaKind>> LookupAsync(Guid siteId, IEnumerable<JsonNode?> values, CancellationToken cancellationToken) =>
            Task.FromResult<Func<Guid, MediaKind>>(id => files.GetValueOrDefault(id, MediaKind.Missing));
    }

    private sealed class FakeItems(Dictionary<Guid, Guid> items) : IItemReferenceLookup
    {
        public Task<IReadOnlyDictionary<Guid, Guid>> CollectionsOfAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(items.Where(i => itemIds.Contains(i.Key)).ToDictionary());
    }
}
