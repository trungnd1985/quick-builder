using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Email;

namespace QuickBuilder.IntegrationTests.Email;

/// <summary>
/// The SMTP provider on its own, against a real server (018 research FS-18, FS-22; constitution
/// Principle X): security modes, signing in, the timeout, which refusals may be retried, and a
/// reply that never carries the password.
/// </summary>
public sealed class SmtpEmailSenderTests(MailpitFixture mailpit, StartTlsMailpitFixture startTls)
    : IClassFixture<MailpitFixture>, IClassFixture<StartTlsMailpitFixture>, IAsyncLifetime
{
    private const string Password = "s3cret-Pa55word";

    private static readonly EmailMessage Message = new(["editor@example.com", "owner@example.com"], "New submission: Contact", "Hello\nthere\n", "anna@example.com");

    public Task InitializeAsync() => mailpit.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static SmtpEmailSender Sender(string host, int port, SmtpSecurity security = SmtpSecurity.None, TimeSpan? timeout = null) =>
        new(Options.Create(new SmtpOptions
        {
            Host = host,
            Port = port,
            Security = security,
            UserName = "quickbuilder",
            Password = Password,
            From = "site@example.com",
            FromName = "Acme site",
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        }));

    [Fact]
    public async Task Signing_in_without_encryption_on_this_machine_sends_the_message()
    {
        var result = await Sender(mailpit.Host, mailpit.Port).SendAsync(Message, CancellationToken.None);

        Assert.True(result.Accepted, result.ServerReply);
        var received = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal("New submission: Contact", received.Subject);
        Assert.Equal(["editor@example.com", "owner@example.com"], received.To.Order(StringComparer.Ordinal));
        Assert.Equal(["anna@example.com"], received.ReplyTo);
        Assert.Equal("Hello\r\nthere\r\n", received.Text);
    }

    [Fact]
    public async Task A_subject_cannot_add_a_header()
    {
        var result = await Sender(mailpit.Host, mailpit.Port).SendAsync(Message with { Subject = "Hi\r\nBcc: spy@example.com" }, CancellationToken.None);

        Assert.True(result.Accepted, result.ServerReply);
        var received = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal("Hi Bcc: spy@example.com", received.Subject);
        Assert.DoesNotContain("Bcc", received.Headers);
        Assert.Equal(2, received.To.Count);
    }

    [Theory]
    [InlineData(451, true)]
    [InlineData(452, true)]
    [InlineData(550, false)]
    [InlineData(554, false)]
    public async Task A_temporary_refusal_may_be_retried_and_a_permanent_one_may_not(int code, bool retryable)
    {
        await mailpit.RefuseAsync(code);

        var result = await Sender(mailpit.Host, mailpit.Port).SendAsync(Message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(retryable, result.Retryable);
        Assert.StartsWith($"{code} ", result.ServerReply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_sign_in_is_permanent_and_its_reply_never_holds_the_password()
    {
        await mailpit.RefuseAsync(535, atSignIn: true);

        var result = await Sender(mailpit.Host, mailpit.Port).SendAsync(Message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.False(result.Retryable);
        Assert.DoesNotContain(Password, result.ServerReply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_may_be_retried()
    {
        var result = await Sender("127.0.0.1", ClosedPort()).SendAsync(Message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.True(result.Retryable);
        Assert.False(string.IsNullOrEmpty(result.ServerReply));
    }

    [Fact]
    public async Task A_server_that_never_answers_times_out_and_may_be_retried()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var accepting = silent.AcceptTcpClientAsync();

        var started = DateTime.UtcNow;
        var result = await Sender("127.0.0.1", ((IPEndPoint)silent.LocalEndpoint).Port, timeout: TimeSpan.FromSeconds(1)).SendAsync(Message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.True(result.Retryable);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        (await accepting).Dispose();
    }

    [Fact]
    public async Task StartTls_refuses_a_server_whose_certificate_is_not_trusted()
    {
        var result = await Sender(startTls.Host, startTls.Port, SmtpSecurity.StartTls).SendAsync(Message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("certificate", result.ServerReply, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await startTls.MessagesAsync());
    }

    [Fact]
    public async Task No_encryption_is_refused_by_a_server_that_requires_it()
    {
        var result = await Sender(startTls.Host, startTls.Port).SendAsync(Message, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.False(result.Retryable);
        Assert.Empty(await startTls.MessagesAsync());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("127.0.0.1", true)]
    public void The_host_setting_chooses_the_sender(string host, bool configured)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = host,
            ["Email:Smtp:From"] = "site@example.com",
        }).Build();
        using var services = new ServiceCollection()
            .AddEmail()
            .Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName))
            .BuildServiceProvider();

        var sender = services.GetRequiredService<IEmailSender>();

        Assert.Equal(configured, sender.IsConfigured);
        Assert.IsType(configured ? typeof(SmtpEmailSender) : typeof(DisabledEmailSender), sender);
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
