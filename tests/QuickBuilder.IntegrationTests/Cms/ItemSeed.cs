using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Inserts items as stored data, for tests (research CS-19). There is no API that creates
/// items until #15, so tests write through <see cref="CmsDbContext"/> directly.
/// </summary>
/// <remarks>
/// Values are well-formed for their field's type and valid for its settings, so every
/// difference a test sees afterwards is the schema change's doing. Values carry the canary
/// marker where they are text, for the log scans.
/// </remarks>
internal static class ItemSeed
{
    private static readonly Guid SomeMedia = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-0000000000aa");

    /// <summary>A well-formed, valid value of <paramref name="field"/> for item number <paramref name="n"/>.</summary>
    public static JsonNode ValueFor(Field field, int n) => field.Type switch
    {
        FieldType.Text => JsonValue.Create($"zz-canary-item {n}"),
        FieldType.RichText => FieldConversion.TextToRichText($"zz-canary-body {n}"),
        FieldType.Number => JsonValue.Create(Math.Max(field.Min ?? 0, n % 50)),
        FieldType.Boolean => JsonValue.Create(n % 2 == 0),
        FieldType.Date => JsonValue.Create(field.WithTime
            ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : new DateOnly(2026, 1, 1).AddDays(n % 300).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        FieldType.Image or FieldType.File => new JsonObject { ["mediaId"] = SomeMedia.ToString() },
        FieldType.Option => JsonValue.Create(field.Options[n % field.Options.Count].Id),
        FieldType.Reference => JsonValue.Create(Guid.CreateVersion7().ToString()),
        FieldType.MultiReference => new JsonArray(JsonValue.Create(Guid.CreateVersion7().ToString())),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    /// <summary>
    /// Inserts <paramref name="count"/> items into a collection of <paramref name="siteId"/>.
    /// Every field gets a value unless <paramref name="valuesFor"/> says otherwise; when
    /// <paramref name="published"/> is set, each item also gets a published copy of the same values.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> AddAsync(
        CmsScenario scenario,
        Guid siteId,
        Guid collectionId,
        int count,
        bool published = false,
        Func<Field, int, JsonNode?>? valuesFor = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        await using var scope = scenario.App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var schemaText = database.ForSite<Collection>(siteId).Where(c => c.Id == collectionId).Select(c => c.Schema).Single();
        var schema = CollectionSchemaDocument.Read(schemaText);
        var ids = new List<Guid>(count);

        for (var start = 0; start < count; start += 1000)
        {
            for (var n = start; n < Math.Min(count, start + 1000); n++)
            {
                var values = ItemValues.Empty();
                foreach (var field in schema.Fields)
                {
                    values.Set(field.Id, valuesFor is null ? ValueFor(field, n) : valuesFor(field, n));
                }

                var item = Item.Create(siteId, collectionId, values, published ? ItemValues.Read(JsonDocument.Parse(values.Write().ToJsonString())) : null, clock);
                database.Items.Add(item);
                ids.Add(item.Id);
            }

            await database.SaveChangesAsync().ConfigureAwait(false);
            database.ChangeTracker.Clear();
        }

        return ids;
    }
}
