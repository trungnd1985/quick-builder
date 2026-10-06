using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;
using QuickBuilder.Shared.Infrastructure.Storage.S3;

namespace QuickBuilder.UnitTests.Storage;

/// <summary>
/// The remote providers' settings rules, which need no network (013 contracts/storage-providers.md §5.1).
/// Each failure names its key and never contains a secret value (FR-033).
/// </summary>
public sealed class RemoteStorageSettingsTests
{
    private const string Secret = "do-not-print-this-secret";

    public static TheoryData<string, string> S3Misconfigurations => new()
    {
        { "bucket", "Storage:S3:Bucket" },
        { "region", "Storage:S3:Region" },
        { "access-key-only", "Storage:S3:AccessKeyId" },
        { "secret-only", "Storage:S3:SecretAccessKey" },
        { "relative-url", "Storage:S3:ServiceUrl" },
        { "ftp-url", "Storage:S3:ServiceUrl" },
        { "prefix-no-slash", "Storage:S3:KeyPrefix" },
        { "prefix-upper", "Storage:S3:KeyPrefix" },
    };

    [Theory]
    [MemberData(nameof(S3Misconfigurations))]
    public void An_s3_setting_breaking_a_rule_is_named(string problem, string key)
    {
        var settings = new S3StorageSettings { Bucket = "media", Region = "eu-west-1", AccessKeyId = "AKID", SecretAccessKey = Secret };
        switch (problem)
        {
            case "bucket": settings.Bucket = string.Empty; break;
            case "region": settings.Region = " "; break;
            case "access-key-only": settings.SecretAccessKey = string.Empty; break;
            case "secret-only": settings.AccessKeyId = string.Empty; break;
            case "relative-url": settings.ServiceUrl = "minio:9000"; break;
            case "ftp-url": settings.ServiceUrl = "ftp://minio:9000"; break;
            case "prefix-no-slash": settings.KeyPrefix = "quickbuilder"; break;
            case "prefix-upper": settings.KeyPrefix = "QuickBuilder/"; break;
        }

        var failures = S3StorageOptionsValidator.Rules(settings).ToList();

        var failure = Assert.Single(failures);
        Assert.Contains(key, failure, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("quickbuilder/")]
    [InlineData("sites/production/")]
    public void Valid_s3_settings_pass_the_rules(string prefix) =>
        Assert.Empty(S3StorageOptionsValidator.Rules(new S3StorageSettings
        {
            Bucket = "media",
            Region = "auto",
            ServiceUrl = "https://example.r2.cloudflarestorage.com",
            KeyPrefix = prefix,
        }));

    public static TheoryData<string, string> AzureMisconfigurations => new()
    {
        { "container", "Storage:AzureBlob:Container" },
        { "neither", "Storage:AzureBlob:ConnectionString" },
        { "both", "Storage:AzureBlob:AccountUrl" },
        { "relative-url", "Storage:AzureBlob:AccountUrl" },
        { "http-url", "Storage:AzureBlob:AccountUrl" },
        { "prefix", "Storage:AzureBlob:KeyPrefix" },
    };

    [Theory]
    [MemberData(nameof(AzureMisconfigurations))]
    public void An_azure_setting_breaking_a_rule_is_named(string problem, string key)
    {
        var settings = new AzureBlobStorageSettings { Container = "media", ConnectionString = $"AccountName=a;AccountKey={Secret}" };
        switch (problem)
        {
            case "container": settings.Container = string.Empty; break;
            case "neither": settings.ConnectionString = string.Empty; break;
            case "both": settings.AccountUrl = "https://a.blob.core.windows.net"; break;
            case "relative-url": settings.ConnectionString = string.Empty; settings.AccountUrl = $"a.blob.core.windows.net?sig={Secret}"; break;
            case "http-url": settings.ConnectionString = string.Empty; settings.AccountUrl = $"http://a.blob.core.windows.net?sig={Secret}"; break;
            case "prefix": settings.KeyPrefix = "../"; break;
        }

        var failure = Assert.Single(AzureBlobStorageOptionsValidator.Rules(settings));
        Assert.Contains(key, failure, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_connection_string_is_refused_without_quoting_it()
    {
        var result = new AzureBlobStorageOptionsValidator().Validate(null, new StorageOptions
        {
            Provider = "azureblob",
            AzureBlob = { Container = "media", ConnectionString = $"not a connection string {Secret}" },
        });

        Assert.True(result.Failed);
        Assert.Contains("Storage:AzureBlob:ConnectionString", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_validator_ignores_the_other_providers()
    {
        var local = new StorageOptions { Provider = "Local" };

        Assert.True(new S3StorageOptionsValidator().Validate(null, local).Succeeded);
        Assert.True(new AzureBlobStorageOptionsValidator().Validate(null, local).Succeeded);
    }
}
