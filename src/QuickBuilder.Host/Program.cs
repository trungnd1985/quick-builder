using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using QuickBuilder.Cms;
using QuickBuilder.Editor;
using QuickBuilder.Forms;
using QuickBuilder.Host;
using QuickBuilder.Host.Configuration;
using QuickBuilder.Host.Middleware;
using QuickBuilder.Host.Observability;
using QuickBuilder.Host.Routing;
using QuickBuilder.Identity;
using QuickBuilder.Media;
using QuickBuilder.Pages;
using QuickBuilder.Rendering;
using QuickBuilder.Shared.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Shared.Infrastructure.Migrations;
using QuickBuilder.Shared.Infrastructure.Migrations.Platform;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Startup;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Versioning;
using QuickBuilder.Sites;
using Serilog;

// Composition only (FR-001). No business rule and no data access appears in this project;
// an architecture test asserts it.

var builder = WebApplication.CreateBuilder(args);

// FR-031: structured logging from the first line, so startup failures are structured too.
builder.Host.UseSerilog((context, services, configuration) =>
    LoggingSetup.Configure(configuration, context.Configuration, services));

// --- Configuration (FR-028, FR-029) ------------------------------------------------
// ValidateOnStart means an invalid value stops the process before it listens, naming the
// offending key, rather than surfacing later as a confusing runtime error.
builder.Services
    .AddOptions<ConnectionOptions>()
    .Bind(builder.Configuration.GetSection(ConnectionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<HostingOptions>()
    .Bind(builder.Configuration.GetSection(HostingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<HostingOptions>, HostingOptionsValidator>();

builder.Services
    .AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<AdminOptions>()
    .Bind(builder.Configuration.GetSection(AdminOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// 012 research PR-07: the in-memory cache's size bound.
builder.Services
    .AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<TelemetryOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// 018 research FS-18: outgoing email. An empty Email:Smtp:Host turns it off; anything else
// must name a sender and never sign in unencrypted to another machine.
builder.Services
    .AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddEmail();

// --- Shared services ---------------------------------------------------------------
// 019 RL-01: what this build is. Read from the Host assembly, not the entry assembly: under
// tests the entry assembly is the test runner.
var applicationVersion = ApplicationVersion.FromAssembly(typeof(Program).Assembly);
builder.Services.AddSingleton(applicationVersion);
builder.Services.AddSingleton<IOutboxSettingsProvider, OutboxSettingsProvider>();
builder.Services.AddSharedInfrastructure();

// 019 RL-07: the migration runner's own record of which release migrated the database.
builder.Services.AddPlatformDatabase(builder.Configuration);

// 003: file storage. The host names its own directories so the local provider can refuse
// to live inside any of them (research M-05), without Shared.Infrastructure depending on
// ASP.NET Core to find them.
builder.Services.AddFileStorage(
    builder.Configuration,
    [AppContext.BaseDirectory, builder.Environment.ContentRootPath, builder.Environment.WebRootPath]);

builder.Services.AddSingleton<IHostCommand, MigrateCommand>();
builder.Services.AddTelemetry(builder.Configuration);

// --- Modules -----------------------------------------------------------------------
// One line per module. Adding another requires no edit to any existing one (SC-009).
builder.Services.AddSitesModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddMediaModule(builder.Configuration);
builder.Services.AddPagesModule(builder.Configuration);
builder.Services.AddEditorModule(builder.Configuration);
builder.Services.AddCmsModule(builder.Configuration);
builder.Services.AddRenderingModule(builder.Configuration);
builder.Services.AddFormsModule(builder.Configuration);

// Controllers live in their modules and are discovered through application parts
// (constitution Principle I). Minimal APIs are not used anywhere.
builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(SitesModule).Assembly)
    .AddApplicationPart(typeof(IdentityModule).Assembly)
    .AddApplicationPart(typeof(MediaModule).Assembly)
    .AddApplicationPart(typeof(PagesModule).Assembly)
    .AddApplicationPart(typeof(EditorModule).Assembly)
    .AddApplicationPart(typeof(CmsModule).Assembly)
    .AddApplicationPart(typeof(RenderingModule).Assembly)
    .AddApplicationPart(typeof(FormsModule).Assembly);

// 019 RL-08: the startup check, before the host exists. A refused database starts nothing of
// QuickBuilder's own: its hosted services are removed here, and the pipeline below is the
// refusal's alone. The migration runner checks again after the host is built.
var refusal = await InstallationPreflight.CheckAsync(builder.Configuration, applicationVersion).ConfigureAwait(false);
if (refusal is not null)
{
    RefusalHost.RemoveApplicationHostedServices(builder.Services);
}

var app = builder.Build();

// 019 FR-006: every start, command or server, says which release it is before anything else.
app.Logger.LogInformation("QuickBuilder {Version}", applicationVersion.Describe());

// --- Commands ----------------------------------------------------------------------
// `dotnet QuickBuilder.Host.dll <command>` runs a registered IHostCommand and exits
// instead of serving HTTP: `migrate` (FR-023) and the modules' operator commands
// (contracts/operator-commands.md). The host only matches the name.
var commandExitCode = await HostCommandRunner.TryRunAsync(args, app.Services).ConfigureAwait(false);
if (commandExitCode is { } exitCode)
{
    return exitCode;
}

if (refusal is { } refused)
{
    // Logged once, with both versions and what to do; the probe gets the anonymous detail.
    app.Logger.LogCritical("{Refusal}", refused.LogMessage);
    app.Services.GetRequiredService<StartupState>().Advance(StartupPhase.Incompatible, refused.Detail);
    RefusalHost.Configure(app, app.Services.GetRequiredService<IOptions<HostingOptions>>().Value.BasePath, refused.Detail);
    await app.RunAsync().ConfigureAwait(false);
    return 0;
}

await StartupTasks.RunMigrationsAsync(app).ConfigureAwait(false);
await StartupTasks.CheckAdminHostNamesAsync(app).ConfigureAwait(false);

// --- Pipeline ----------------------------------------------------------------------
// Order matters and is asserted by tests rather than left to reading:
//   forwarded headers -> base path -> exceptions -> admin host confinement -> security
//   headers -> static files
//   -> routing -> site resolution -> authentication -> Identity module (setup gate,
//   request guard) -> authorization -> endpoints
//
// Static files come before authentication: the admin shell's assets must load for an
// anonymous browser, or it could never show the sign-in screen.
var hosting = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value;

app.UseForwardedHeaders(ProxySetup.BuildOptions(hosting));

if (!string.IsNullOrEmpty(hosting.BasePath))
{
    app.UsePathBase(hosting.BasePath);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// 009 FR-018a: before the security headers and static files, either of which would give
// the administrative surface away on a client domain (research S-13).
app.UseMiddleware<AdminHostConfinementMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Serves the built admin assets from wwwroot/admin.
app.UseStaticFiles();

app.UseRouting();

// FR-011a: runs after routing so reserved prefixes are known, and before endpoints so a
// handler never sees an unresolved public request.
app.UseMiddleware<SiteResolutionMiddleware>();

// 002: every admin endpoint is authenticated and authorized on the server. Endpoints that
// declare nothing fall back to "signed-in user required", so a forgotten declaration fails
// closed (research R-05).
app.UseAuthentication();
app.UseIdentityModule();
app.UseAuthorization();

app.MapControllers();

// FR-043: any /admin/* deep link returns the SPA shell so client-side routing can take
// over. Without this, refreshing on a nested admin route 404s. Anonymous: the shell has to
// load before anyone can sign in.
app.MapFallbackToFile($"{RoutePrefixes.Admin}/{{*path:nonfile}}", "admin/index.html")
    .AllowAnonymous();

await app.RunAsync().ConfigureAwait(false);
return 0;

/// <summary>
/// Exposed so integration tests can drive the real pipeline through
/// WebApplicationFactory. Principle X forbids testing a different pipeline than the one
/// that ships.
/// </summary>
public partial class Program;
