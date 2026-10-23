using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Forms.Domain;
using QuickBuilder.UnitTests.Pages;

namespace QuickBuilder.UnitTests.Forms;

/// <summary>
/// The server's check of submitted values (018 FR-007, FR-015, SC-003; research FS-08), from
/// the fixture the admin and the published form script run too, so all three agree.
/// </summary>
public sealed partial class FormValueRulesTests
{
    private static readonly string FixturePath =
        Path.Combine(ContractFiles.Root, "tests", "fixtures", "forms", "value-rules.json");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in Fixture())
        {
            data.Add(testCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Values_are_normalised_and_checked_as_the_shared_fixture_says(string name)
    {
        var testCase = Fixture().Single(c => c.Name == name);

        var result = FormValueRules.Check(testCase.Field, testCase.Values);

        Assert.Equal(testCase.Code, result.Code);
        Assert.Equal(testCase.Expected, result.Values);
    }

    [Fact]
    public void Every_code_of_the_contract_is_exercised()
    {
        var exercised = Fixture().Select(c => c.Code).OfType<string>().ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ValueCodes.All.Order(StringComparer.Ordinal), exercised.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<(string Name, FormField Field, string[] Values, string? Code, string[] Expected)> Fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expect = testCase.GetProperty("expect");
            yield return (
                testCase.GetProperty("name").GetString()!,
                FormField.Parse(testCase.GetProperty("field")),
                [.. testCase.GetProperty("values").EnumerateArray().Select(v => Expand(v.GetString()!))],
                expect.GetProperty("code").GetString(),
                [.. expect.GetProperty("values").EnumerateArray().Select(v => Expand(v.GetString()!))]);
        }
    }

    /// <summary>The fixture's <c>${Na}</c>: the letter a, N times.</summary>
    private static string Expand(string value) =>
        Repeat().Replace(value, m => new string('a', int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));

    [GeneratedRegex(@"\$\{(\d+)a\}")]
    private static partial Regex Repeat();
}
