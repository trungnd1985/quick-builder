using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// The server's validator gives exactly the answer the contract's golden files state
/// (docs/contracts/collection-schema.md §5a, SC-006). The admin runs the same files in
/// <c>collectionSchema.spec.ts</c>.
/// </summary>
public sealed class CollectionSchemaGoldenTests
{
    private static readonly CollectionSchemaValidator Validator = new();

    public static TheoryData<string> Valid() => [.. CmsContractFiles.ValidExamples().Select(file => Path.GetFileName(file))];

    public static TheoryData<string, string, string, string> Invalid()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var (file, code, rule, pointer) in CmsContractFiles.InvalidExamples())
        {
            data.Add(Path.GetFileName(file), code, rule, pointer);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void Every_valid_example_is_accepted_as_a_stored_schema(string file)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(CmsContractFiles.ExamplesDirectory, "valid", file)), SchemaDocumentKind.Stored);

        Assert.True(result.IsValid, $"{file}: {result.Rule} at {result.Pointer}: {result.Message}");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Every_invalid_example_fails_with_its_code_rule_and_pointer(string file, string code, string rule, string location)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(CmsContractFiles.ExamplesDirectory, "invalid", file)), SchemaDocumentKind.Stored);

        Assert.False(result.IsValid, $"{file} was accepted.");
        Assert.Equal((code, rule, location), (result.Code, result.Rule, result.Pointer));
        Assert.StartsWith(rule, file, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_over_64_kib_is_too_large_before_it_is_parsed()
    {
        var bytes = new byte[QuickBuilder.Cms.Contracts.CollectionSchemaLimits.MaxProposalBytes + 1];
        Array.Fill(bytes, (byte)' ');

        Assert.Equal("cms.proposal_too_large", Validator.Validate(bytes, SchemaDocumentKind.Proposal).Code);
    }
}
