using Amazon.Runtime;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;
using QuickBuilder.Shared.Infrastructure.Storage.S3;

namespace QuickBuilder.UnitTests.Storage;

/// <summary>
/// Which credentials each remote provider uses (013 FR-032, tasks T075). No cloud: the
/// factories that build the clients are inspected directly.
/// </summary>
public sealed class CredentialSelectionTests
{
    [Fact]
    public void S3_without_keys_uses_the_ambient_chain()
    {
        var settings = new S3StorageSettings { Bucket = "b", Region = "eu-west-1" };

        Assert.Null(S3ClientFactory.Credentials(settings));
    }

    [Fact]
    public void S3_with_both_keys_uses_them()
    {
        var settings = new S3StorageSettings { Bucket = "b", Region = "eu-west-1", AccessKeyId = "AKIDEXAMPLE", SecretAccessKey = "secret-value" };

        var credentials = Assert.IsType<BasicAWSCredentials>(S3ClientFactory.Credentials(settings));
        Assert.Equal("AKIDEXAMPLE", credentials.GetCredentials().AccessKey);
    }

    [Fact]
    public void S3_with_a_service_url_signs_for_the_configured_region_with_compatible_checksums()
    {
        var config = S3ClientFactory.Config(new S3StorageSettings
        {
            Bucket = "b",
            Region = "auto",
            ServiceUrl = "https://example-account.r2.cloudflarestorage.com",
            ForcePathStyle = true,
        });

        Assert.Equal("auto", config.AuthenticationRegion);
        Assert.True(config.ForcePathStyle);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, config.ResponseChecksumValidation);
        Assert.Equal(RequestRetryMode.Standard, config.RetryMode);
        Assert.Equal(2, config.MaxErrorRetry);
    }

    [Fact]
    public void Azure_with_an_account_url_uses_the_environments_identity_without_interactive_sources()
    {
        var options = AzureBlobClientFactory.CredentialOptions(new AzureBlobStorageSettings
        {
            Container = "media",
            AccountUrl = "https://account.blob.core.windows.net",
        });

        Assert.NotNull(options);
        Assert.True(options.ExcludeInteractiveBrowserCredential);
        Assert.True(options.ExcludeVisualStudioCredential);
        Assert.True(options.ExcludeVisualStudioCodeCredential);
        Assert.True(options.ExcludeAzureDeveloperCliCredential);
        Assert.False(options.ExcludeManagedIdentityCredential);
        Assert.False(options.ExcludeWorkloadIdentityCredential);
        Assert.False(options.ExcludeEnvironmentCredential);
    }

    [Fact]
    public void Azure_with_a_connection_string_uses_no_token_credential() =>
        Assert.Null(AzureBlobClientFactory.CredentialOptions(new AzureBlobStorageSettings
        {
            Container = "media",
            ConnectionString = "UseDevelopmentStorage=true",
        }));
}
