using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// Whether email is on, and the test email (018 FR-037; forms-api.yaml): any signed-in user may
/// ask the first, only an Administrator may send the second, to their own address.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EmailEndpointsTests(DatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private static readonly Uri Status = new("/api/v1/forms/email-status", UriKind.Relative);
    private static readonly Uri Test = new("/api/v1/forms/email-test", UriKind.Relative);

    public Task InitializeAsync() => mailpit.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<FormsWorld> WorldAsync(string label, bool email = true) =>
        CreateAsync(database, label, settings: email
            ? mailpit.Settings().Where(s => s.Value is not null).ToDictionary(s => s.Key, s => s.Value!, StringComparer.Ordinal)
            : null);

    [Fact]
    public async Task Any_signed_in_user_learns_whether_email_is_on_and_nothing_more()
    {
        using var on = await WorldAsync("email-status-on");
        using var off = await WorldAsync("email-status-off", email: false);
        using var editor = await on.UserAsync((on.Site, SystemRole.Editor));

        var body = await editor.GetStringAsync(Status);

        Assert.Equal("""{"configured":true}""", body);
        Assert.Equal("""{"configured":false}""", await off.Admin.GetStringAsync(Status));
        Assert.Equal(HttpStatusCode.Unauthorized, (await on.Visitor().GetAsync(Status)).StatusCode);
    }

    [Fact]
    public async Task The_test_email_goes_to_the_administrators_own_address()
    {
        using var world = await WorldAsync("email-test-send");

        var response = await world.Admin.PostAsync(Test, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var message = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromSeconds(10)));
        Assert.Equal("QuickBuilder test email", message.Subject);
        Assert.EndsWith("@example.test", Assert.Single(message.To), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_email_the_test_is_refused_as_not_configured()
    {
        using var world = await WorldAsync("email-test-off", email: false);

        var response = await world.Admin.PostAsync(Test, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("forms.email_not_configured", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_returns_the_servers_reply()
    {
        using var world = await WorldAsync("email-test-refused");
        await mailpit.RefuseAsync(550);

        var response = await world.Admin.PostAsync(Test, null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("forms.email_failed", problem.RootElement.GetProperty("errorCode").GetString());
        Assert.StartsWith("550", problem.RootElement.GetProperty("serverReply").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_administrators_may_send_a_test_email()
    {
        using var world = await WorldAsync("email-test-designer");
        using var designer = await world.UserAsync((world.Site, SystemRole.Designer));

        var response = await designer.PostAsync(Test, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await mailpit.MessagesAsync());
    }
}
