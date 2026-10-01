using Microsoft.Extensions.Options;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>Starts a host with storage settings and reports how start-up failed.</summary>
internal static class RemoteStartup
{
    /// <summary>The validation failure, and every log line the host wrote before stopping.</summary>
    public static (string Message, string Logs) Fail(string connectionString, IReadOnlyDictionary<string, string> settings)
    {
        using var app = new QuickBuilderApp(connectionString, settings: settings);

        var failure = Assert.ThrowsAny<Exception>(() => app.Services);
        var logs = string.Join('\n', app.Logs.Events.Select(e => e.RenderMessage() + " " + e.Exception));

        return (Unwrap(failure).Message, logs);
    }

    /// <summary>The validation failure, however the host wraps it.</summary>
    public static OptionsValidationException Unwrap(Exception failure)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException validation)
            {
                return validation;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.OfType<OptionsValidationException>().FirstOrDefault() is { } inner)
            {
                return inner;
            }
        }

        throw new Xunit.Sdk.XunitException($"Startup failed, but not with an options validation failure: {failure}");
    }
}
