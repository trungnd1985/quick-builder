using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// A real SMTP server for the email tests (018 research FS-22; constitution Principle X): Mailpit
/// in a container, accepting any sign-in without encryption, with its "chaos" switch on so a test
/// can make it refuse with a chosen SMTP reply.
/// </summary>
/// <remarks>
/// The container is not restarted to simulate an outage: a restart maps a new host port. A
/// refusal is made with chaos instead, and an unreachable server with a port nobody listens on.
/// </remarks>
public class MailpitFixture : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.30.7";

    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer _container;
    private HttpClient? _api;

    public MailpitFixture()
        : this(["--smtp-auth-accept-any", "--smtp-auth-allow-insecure", "--enable-chaos"])
    {
    }

    protected MailpitFixture(IReadOnlyList<string> arguments)
    {
        _container = new ContainerBuilder(Image)
            .WithPortBinding(SmtpPort, assignRandomHostPort: true)
            .WithPortBinding(ApiPort, assignRandomHostPort: true)
            .WithCommand([.. arguments])
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(ApiPort).ForPath("/livez")))
            .Build();
    }

    /// <summary>Always a loopback address, so signing in without encryption passes the startup check.</summary>
    public string Host { get; } = "127.0.0.1";

    public int Port => _container.GetMappedPublicPort(SmtpPort);

    private HttpClient Api => _api ?? throw new InvalidOperationException("Mailpit has not started.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _api = new HttpClient { BaseAddress = new Uri($"http://{Host}:{_container.GetMappedPublicPort(ApiPort)}") };
    }

    public async Task DisposeAsync()
    {
        _api?.Dispose();
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The settings that point the application at this server.</summary>
    public Dictionary<string, string?> Settings(string from = "site@example.com") => new(StringComparer.Ordinal)
    {
        ["Email:Smtp:Host"] = Host,
        ["Email:Smtp:Port"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email:Smtp:Security"] = "None",
        ["Email:Smtp:From"] = from,
        ["Email:Smtp:Timeout"] = "00:00:10",
    };

    /// <summary>Removes every message and turns chaos off, so a test starts from an empty mailbox.</summary>
    public async Task ResetAsync()
    {
        using var delete = await Api.DeleteAsync(new Uri("/api/v1/messages", UriKind.Relative)).ConfigureAwait(false);
        delete.EnsureSuccessStatusCode();
        await RefuseAsync(null).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the server answer every <c>MAIL FROM</c> (or, with <paramref name="atSignIn"/>, every
    /// sign-in) with <paramref name="code"/>, or accept again when it is null.
    /// </summary>
    public async Task RefuseAsync(int? code, bool atSignIn = false)
    {
        var refuse = new { ErrorCode = code ?? 451, Probability = code is null ? 0 : 100 };
        var accept = new { ErrorCode = 451, Probability = 0 };
        var triggers = new { Sender = atSignIn ? accept : refuse, Authentication = atSignIn ? refuse : accept };
        using var response = await Api.PutAsJsonAsync(new Uri("/api/v1/chaos", UriKind.Relative), triggers).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Every message received, newest first, with its full text and headers.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesAsync()
    {
        using var list = JsonDocument.Parse(await Api.GetStringAsync(new Uri("/api/v1/messages", UriKind.Relative)).ConfigureAwait(false));
        var messages = new List<MailpitMessage>();
        foreach (var summary in list.RootElement.GetProperty("messages").EnumerateArray())
        {
            var id = summary.GetProperty("ID").GetString()!;
            using var message = JsonDocument.Parse(await Api.GetStringAsync(new Uri($"/api/v1/message/{id}", UriKind.Relative)).ConfigureAwait(false));
            using var headers = JsonDocument.Parse(await Api.GetStringAsync(new Uri($"/api/v1/message/{id}/headers", UriKind.Relative)).ConfigureAwait(false));
            var root = message.RootElement;
            messages.Add(new MailpitMessage(
                root.GetProperty("Subject").GetString()!,
                [.. root.GetProperty("To").EnumerateArray().Select(a => a.GetProperty("Address").GetString()!)],
                [.. root.GetProperty("ReplyTo").EnumerateArray().Select(a => a.GetProperty("Address").GetString()!)],
                root.GetProperty("Text").GetString()!,
                headers.RootElement.EnumerateObject().Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }

        return messages;
    }

    /// <summary>Waits until at least <paramref name="count"/> messages arrived, for at most <paramref name="within"/>.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> WaitForAsync(int count, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            var messages = await MessagesAsync().ConfigureAwait(false);
            if (messages.Count >= count || DateTime.UtcNow > deadline)
            {
                return messages;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }
}

/// <summary>Mailpit requiring STARTTLS, with a self-signed certificate nobody trusts.</summary>
public sealed class StartTlsMailpitFixture()
    : MailpitFixture(["--smtp-auth-accept-any", "--enable-chaos", "--smtp-tls-cert", "sans:localhost", "--smtp-tls-key", "sans:localhost", "--smtp-require-starttls"]);

/// <summary>One message as Mailpit received it.</summary>
public sealed record MailpitMessage(string Subject, IReadOnlyList<string> To, IReadOnlyList<string> ReplyTo, string Text, IReadOnlySet<string> Headers);
