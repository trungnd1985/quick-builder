using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;

namespace QuickBuilder.UnitTests.Storage;

/// <summary>Storage configuration fails startup with a message naming the key (003 FR-022, FR-023, SC-010).</summary>
public sealed class StorageOptionsValidationTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "quickbuilder-unit", Guid.NewGuid().ToString("N"));
    private readonly LocalStorageOptionsValidator _validator = new();

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Fact]
    public void A_writable_absolute_path_outside_the_application_is_valid_and_is_created()
    {
        var path = Path.Combine(_temp, "media");

        var result = _validator.Validate(null, Options(path));

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFiles(path));
    }

    [Theory]
    [InlineData("S3")]
    [InlineData("azure")]
    [InlineData("")]
    public void An_unknown_provider_fails_listing_the_supported_ones(string provider)
    {
        var result = _validator.Validate(null, Options(_temp, provider));

        Assert.True(result.Failed);
        Assert.Contains("Storage:Provider", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Local", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_provider_name_is_case_insensitive() =>
        Assert.True(_validator.Validate(null, Options(_temp, "local")).Succeeded);

    [Theory]
    [InlineData("relative/path")]
    [InlineData("media")]
    public void A_relative_path_fails_naming_the_key(string path)
    {
        var result = _validator.Validate(null, Options(path));

        Assert.True(result.Failed);
        Assert.Contains("Storage:Local:Path", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("absolute", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_inside_a_forbidden_root_fails()
    {
        var appRoot = Path.Combine(_temp, "app");
        Directory.CreateDirectory(appRoot);

        var options = Options(Path.Combine(appRoot, "wwwroot", "media"));
        options.ForbiddenRoots.Add(appRoot);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Storage:Local:Path", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("inside the application", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_forbidden_root_itself_fails()
    {
        var options = Options(_temp);
        options.ForbiddenRoots.Add(_temp);

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void A_sibling_that_merely_shares_a_name_prefix_is_allowed()
    {
        var options = Options(Path.Combine(_temp, "app-media"));
        options.ForbiddenRoots.Add(Path.Combine(_temp, "app"));

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void A_path_that_cannot_be_written_fails_naming_the_key()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permission bits only.
        }

        if (Environment.UserName == "root")
        {
            return; // Root ignores permission bits.
        }

        var readOnly = Path.Combine(_temp, "read-only");
        Directory.CreateDirectory(readOnly);
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var result = _validator.Validate(null, Options(readOnly));

            Assert.True(result.Failed);
            Assert.Contains("Storage:Local:Path", result.FailureMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void The_per_user_default_is_absolute() =>
        Assert.True(Path.IsPathFullyQualified(LocalStorageSettings.DefaultPath));

    private static StorageOptions Options(string path, string provider = "Local") =>
        new() { Provider = provider, Local = { Path = path } };
}
