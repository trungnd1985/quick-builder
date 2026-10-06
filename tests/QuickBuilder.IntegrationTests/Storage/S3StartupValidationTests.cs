using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// Each S3 misconfiguration stops the host before it listens, naming the key, and neither the
/// message nor any log line carries the secret key (013 contracts/storage-providers.md §5;
/// FR-033, SC-011).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class S3StartupValidationTests(DatabaseFixture database, S3Fixture s3) : IAsyncLifetime, IClassFixture<S3Fixture>
{
    private const string WrongSecret = "this-is-not-the-secret-key";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("s3-startup").ConfigureAwait(false);

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> Misconfigurations => new()
    {
        { "no-bucket", "Storage:S3:Bucket" },
        { "no-region", "Storage:S3:Region" },
        { "key-without-secret", "Storage:S3:SecretAccessKey" },
        { "secret-without-key", "Storage:S3:AccessKeyId" },
        { "missing-bucket", "Storage:S3:Bucket" },
        { "unreachable", "Storage:S3:ServiceUrl" },
        { "relative-url", "Storage:S3:ServiceUrl" },
    };

    [Theory]
    [MemberData(nameof(Misconfigurations))]
    public void A_misconfiguration_stops_startup_naming_the_key_without_secrets(string problem, string key)
    {
        var settings = s3.Configuration();
        var secrets = new List<string> { S3Fixture.SecretKey };

        switch (problem)
        {
            case "no-bucket":
                settings.Remove("Storage:S3:Bucket");
                break;
            case "no-region":
                settings.Remove("Storage:S3:Region");
                break;
            case "key-without-secret":
                settings.Remove("Storage:S3:SecretAccessKey");
                break;
            case "secret-without-key":
                settings.Remove("Storage:S3:AccessKeyId");
                break;
            case "missing-bucket":
                settings["Storage:S3:Bucket"] = $"missing-{Guid.NewGuid():N}";
                break;
            case "unreachable":
                settings["Storage:S3:ServiceUrl"] = "http://127.0.0.1:1";
                break;
            case "relative-url":
                settings["Storage:S3:ServiceUrl"] = "localstack:4566";
                break;
        }

        var (message, logs) = RemoteStartup.Fail(_connectionString, settings);

        Assert.Contains(key, message, StringComparison.Ordinal);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The service rejects the secret. LocalStack accepts any credentials, so this answers as
    /// AWS does for a wrong secret key: 403 with <c>SignatureDoesNotMatch</c>.
    /// </summary>
    [Fact]
    public async Task A_rejected_secret_stops_startup_naming_the_key_without_secrets()
    {
        await using var service = await RejectsSignaturesService.StartAsync();
        var settings = s3.Configuration();
        settings["Storage:S3:ServiceUrl"] = service.Url;
        settings["Storage:S3:SecretAccessKey"] = WrongSecret;

        var (message, logs) = RemoteStartup.Fail(_connectionString, settings);

        Assert.Contains("Storage:S3:AccessKeyId", message, StringComparison.Ordinal);
        Assert.Contains("rejected the credentials", message, StringComparison.Ordinal);
        foreach (var secret in new[] { WrongSecret, S3Fixture.SecretKey })
        {
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }

        Assert.True(service.Requests > 0, "The provider never asked the service.");
    }

    [Fact]
    public async Task A_service_that_ignores_conditional_writes_is_refused()
    {
        await using var proxy = await IgnoresConditionsProxy.StartAsync(s3.ServiceUrl);
        var settings = s3.Configuration();
        settings["Storage:S3:ServiceUrl"] = proxy.Url;

        var (message, logs) = RemoteStartup.Fail(_connectionString, settings);

        Assert.Contains("Storage:S3:ServiceUrl", message, StringComparison.Ordinal);
        Assert.Contains("conditional writes", message, StringComparison.Ordinal);
        Assert.DoesNotContain(S3Fixture.SecretKey, logs, StringComparison.Ordinal);
    }

    [Fact]
    public void A_working_configuration_starts()
    {
        using var app = new QuickBuilderApp(_connectionString, settings: s3.Configuration());

        Assert.NotNull(app.Services);
    }

    /// <summary>
    /// Stands in front of the S3 service and answers a refused create-only write as if it had
    /// succeeded: what a service that ignores <c>If-None-Match</c> looks like to the client.
    /// Requests pass through unchanged, Host header included, so signatures still verify.
    /// </summary>
    private sealed class IgnoresConditionsProxy : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _upstream = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        private IgnoresConditionsProxy(WebApplication app) => _app = app;

        public string Url { get; private set; } = string.Empty;

        public static async Task<IgnoresConditionsProxy> StartAsync(string target)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var proxy = new IgnoresConditionsProxy(app);
            app.Run(context => proxy.ForwardAsync(context, new Uri(target)));

            await app.StartAsync();
            proxy.Url = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return proxy;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _upstream.Dispose();
        }

        private async Task ForwardAsync(HttpContext context, Uri target)
        {
            var uri = new Uri(target, context.Request.Path + context.Request.QueryString);
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), uri);

            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            if (body.Length > 0 || context.Request.ContentLength is not null)
            {
                request.Content = new ByteArrayContent(body.ToArray());
            }

            foreach (var (name, values) in context.Request.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(name, [.. values]))
                {
                    request.Content?.Headers.TryAddWithoutValidation(name, [.. values]);
                }
            }

            using var response = await _upstream.SendAsync(request);
            var conditional = context.Request.Headers.IfNoneMatch.Count > 0;
            if (conditional && response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ETag = "\"overwritten\"";
                return;
            }

            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
            {
                if (!string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers[name] = values.ToArray();
                }
            }

            await response.Content.CopyToAsync(context.Response.Body);
        }
    }

    /// <summary>An S3 endpoint that rejects every request's signature, as AWS does for a wrong secret key.</summary>
    private sealed class RejectsSignaturesService : IAsyncDisposable
    {
        private const string Error =
            """<?xml version="1.0" encoding="UTF-8"?><Error><Code>SignatureDoesNotMatch</Code><Message>The request signature we calculated does not match the signature you provided. Check your key and signing method.</Message><RequestId>0</RequestId></Error>""";

        private readonly WebApplication _app;
        private int _requests;

        private RejectsSignaturesService(WebApplication app) => _app = app;

        public string Url { get; private set; } = string.Empty;

        public int Requests => _requests;

        public static async Task<RejectsSignaturesService> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var service = new RejectsSignaturesService(app);
            app.Run(async context =>
            {
                Interlocked.Increment(ref service._requests);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/xml";
                await context.Response.WriteAsync(Error);
            });

            await app.StartAsync();
            service.Url = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return service;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
