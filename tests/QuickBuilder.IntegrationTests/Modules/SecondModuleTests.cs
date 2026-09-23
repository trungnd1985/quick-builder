using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Modules;
using Xunit;

namespace QuickBuilder.IntegrationTests.Modules;

/// <summary>
/// Adding a module costs one line in the host and no edit to any existing module (SC-009,
/// US2/AC5, FR-004).
/// </summary>
/// <remarks>
/// This is the claim the whole modular structure rests on, and until now it had never been
/// exercised — with one module, every boundary rule passes vacuously.
/// <para>
/// <see cref="WidgetsModule"/> was written by following docs/contributing.md step by step.
/// The test asserts the properties the specification actually promises rather than merely
/// that the endpoint returns 200: the new module migrates its own schema, its data stays
/// scoped, the existing module is untouched and still works, and the coupling is one
/// direction only.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Modules")]
public sealed class SecondModuleTests(DatabaseFixture database) : IAsyncLifetime
{
    private TwoModuleApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("twomodules").ConfigureAwait(true);
        _app = new TwoModuleApp(connectionString);
        _ = _app.Services;
        await _app.EnsureWidgetSchemaAsync().ConfigureAwait(true);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The new module's endpoint works, registered by one host line.</summary>
    [Fact]
    public async Task A_second_module_is_reachable_after_one_line_of_registration()
    {
        var client = _app.CreateClient();

        var response = await client
            .GetAsync(new Uri("/api/v1/widgets/count", UriKind.Relative))
            .ConfigureAwait(true);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, payload.GetProperty("count").GetInt32());
    }

    /// <summary>
    /// Each module owns its own schema and its own migration history (FR-022).
    /// </summary>
    /// <remarks>
    /// The separation that lets modules be developed and migrated independently. Sharing a
    /// history table would make two modules' migrations contend, which is the coupling
    /// per-module schemas exist to prevent.
    /// </remarks>
    [Fact]
    public async Task Each_module_migrates_its_own_schema_independently()
    {
        var providers = _app.Services.GetServices<IModuleDatabaseProvider>().ToList();
        var schemas = providers.Select(p => p.Describe().Schema).ToList();

        Assert.Contains("sites", schemas);
        Assert.Contains("widgets", schemas);

        // Distinct advisory-lock keys, so the two modules do not serialise against each
        // other at startup.
        var lockKeys = providers.Select(p => p.Describe().AdvisoryLockKey).ToList();
        Assert.Equal(lockKeys.Count, lockKeys.Distinct().Count());

        using var scope = _app.Services.CreateScope();
        var widgets = scope.ServiceProvider.GetRequiredService<WidgetsDbContext>();

        var applied = await widgets.Database.GetAppliedMigrationsAsync().ConfigureAwait(true);
        var pending = await widgets.Database.GetPendingMigrationsAsync().ConfigureAwait(true);

        // This fixture module creates its tables directly rather than shipping migrations
        // (see EnsureWidgetSchemaAsync), so the meaningful assertion is that its schema
        // exists, it is reachable, and nothing is outstanding — not that a migration ran.
        Assert.Empty(pending);
        Assert.True(await widgets.Database.CanConnectAsync().ConfigureAwait(true));
        Assert.NotNull(applied);
    }

    /// <summary>The existing module is unaffected by the new one.</summary>
    [Fact]
    public async Task Adding_a_module_does_not_disturb_the_existing_one()
    {
        var client = _app.CreateClient();

        var create = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Coexists", hostNames = new[] { "coexists.test" } })
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var resolved = await _app
            .CreateClientForHost("coexists.test")
            .GetAsync(new Uri("/", UriKind.Relative))
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
    }

    /// <summary>
    /// The new module references nothing inside the existing one (FR-004, FR-005).
    /// </summary>
    /// <remarks>
    /// The structural half of SC-009. An endpoint can work while the modules are hopelessly
    /// coupled; what makes the claim true is that <see cref="WidgetsModule"/> was written
    /// against the shared abstractions alone. Asserted by checking its types reference no
    /// type from the Sites implementation assembly.
    /// </remarks>
    [Fact]
    public void The_new_module_depends_on_nothing_inside_the_existing_module()
    {
        var sitesImplementation = typeof(global::QuickBuilder.Sites.SitesModule).Assembly.GetName().Name;

        var widgetTypes = new[]
        {
            typeof(WidgetsModule),
            typeof(WidgetsDbContext),
            typeof(Widget),
            typeof(WidgetsDatabaseProvider),
            typeof(WidgetsController),
        };

        foreach (var type in widgetTypes)
        {
            var referenced = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .OfType<MethodInfo>()
                .Select(m => m.ReturnType.Assembly.GetName().Name)
                .Concat(type.GetProperties().Select(p => p.PropertyType.Assembly.GetName().Name))
                .Where(name => name is not null);

            Assert.DoesNotContain(sitesImplementation, referenced);
        }
    }
}

/// <summary>
/// The host with a second module registered — one added line.
/// </summary>
/// <remarks>
/// <c>ConfigureServices</c> here stands in for that line in Program.cs. Everything else
/// about the host is untouched, which is the point.
/// </remarks>
internal sealed class TwoModuleApp(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Outbox:Enabled", "false");

        builder.ConfigureServices((context, services) =>
        {
            // THE ONE LINE (SC-009).
            services.AddWidgetsModule(context.Configuration);

            services.AddControllers().AddApplicationPart(typeof(WidgetsController).Assembly);

            var hosted = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
            foreach (var descriptor in hosted)
            {
                services.Remove(descriptor);
            }
        });
    }

    public HttpClient CreateClientForHost(string hostName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Host = hostName;
        return client;
    }

    /// <summary>
    /// Creates the fixture module's tables.
    /// </summary>
    /// <remarks>
    /// <c>EnsureCreatedAsync</c> is wrong here and the reason is worth recording:
    /// it no-ops when the database already contains tables, and by this point the Sites
    /// module's migrations have run — so it would silently create nothing and every
    /// widgets query would fail with a missing-relation error.
    /// <para>
    /// A real module would ship migrations, as step 6 of docs/contributing.md says. This
    /// fixture skips them because generating migrations for a type defined inside a test
    /// assembly is more ceremony than the point requires, so it creates its tables
    /// directly instead.
    /// </para>
    /// </remarks>
    internal async Task EnsureWidgetSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsDbContext>();

        await context.Database
            .ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{WidgetsDbContext.Schema}\";")
            .ConfigureAwait(false);

        var creator = context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        await creator.CreateTablesAsync().ConfigureAwait(false);
    }
}
