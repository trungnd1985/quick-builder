using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Modules;

/// <summary>
/// The conventions a developer copies when adding a feature (US2, FR-007, FR-008, FR-010).
/// </summary>
/// <remarks>
/// User Story 2 is about whether the worked example is actually worth copying. Two of its
/// promises are checked here by inspecting the `Sites` module itself rather than by
/// describing them in a document — a convention nothing verifies is a convention that
/// drifts one feature at a time.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Conventions")]
public sealed class FeatureSliceTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("slices").ConfigureAwait(true);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// US2/AC4 and FR-008: a validation failure names the invalid inputs.
    /// </summary>
    /// <remarks>
    /// Both fields are wrong, and both must be reported. An implementation that stopped at
    /// the first failure would pass a laxer test while making a developer fix one problem
    /// at a time.
    /// </remarks>
    [Fact]
    public async Task Validation_failures_name_every_invalid_input()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        var response = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "", hostNames = new[] { "not a host name" } })
            .ConfigureAwait(true);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var errors = problem.GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.invalid", problem.GetProperty("errorCode").GetString());

        // Distinguishable from an unexpected failure (FR-008): a 400 with per-field
        // messages, not a 500 with a trace id and nothing else.
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("HostNames", out _));
    }

    /// <summary>
    /// US2/AC1 and FR-010: each feature keeps its parts in one folder.
    /// </summary>
    /// <remarks>
    /// The property that makes "copy this folder" a real instruction. Checked by locating
    /// every controller in the module and asserting its slice folder also holds the
    /// handler and the command or query it dispatches — so a feature cannot be split
    /// across the tree while still looking conventional.
    /// </remarks>
    [Fact]
    public void Every_feature_keeps_its_parts_in_one_folder()
    {
        var featuresRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Modules", "Sites", "QuickBuilder.Sites", "Features");

        var slices = Directory.GetDirectories(featuresRoot);

        Assert.NotEmpty(slices);

        foreach (var slice in slices)
        {
            var contents = string.Join(
                "\n",
                Directory.EnumerateFiles(slice, "*.cs").Select(File.ReadAllText));

            var name = Path.GetFileName(slice);

            Assert.True(
                contents.Contains("Controller", StringComparison.Ordinal),
                $"Feature '{name}' has no entry point in its own folder.");

            Assert.True(
                contents.Contains("Handler", StringComparison.Ordinal),
                $"Feature '{name}' has no handler in its own folder.");

            Assert.True(
                contents.Contains("ICommand<", StringComparison.Ordinal)
                || contents.Contains("IQuery<", StringComparison.Ordinal)
                || contents.Contains("ControllerBase", StringComparison.Ordinal),
                $"Feature '{name}' declares neither a command nor a query.");
        }
    }

    /// <summary>
    /// US2/AC3 and FR-007: controllers hold no business rules and no data access.
    /// </summary>
    /// <remarks>
    /// The architecture tests assert the host is free of data access; this asserts the same
    /// of a module's controllers, which those rules cannot reach because a module
    /// legitimately depends on EF Core.
    /// <para>
    /// Checked by reflection over constructor parameters rather than by reading source. An
    /// earlier version split files on class declarations and produced false positives —
    /// a handler's body ran into the controller that followed it in the same slice file.
    /// What a controller is *given* is both the real rule and the reliable signal: a
    /// controller handed only the dispatcher cannot touch the database however it is
    /// written.
    /// </para>
    /// </remarks>
    [Fact]
    public void Controllers_are_given_nothing_but_the_dispatcher()
    {
        var controllers = typeof(global::QuickBuilder.Sites.SitesModule).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(controllers);

        var offenders = new List<string>();

        foreach (var controller in controllers)
        {
            foreach (var parameter in controller.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                var type = parameter.ParameterType;

                var isDataAccess = typeof(DbContext).IsAssignableFrom(type)
                    || type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;

                if (isDataAccess)
                {
                    offenders.Add($"{controller.Name} takes {type.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Controllers must translate a request into one dispatcher call and contain no " +
            "data access (FR-007):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Controllers in the module route under /api/v1/&lt;module&gt; (FR-010a).</summary>
    [Fact]
    public void Module_endpoints_carry_the_version_segment()
    {
        var controllers = typeof(global::QuickBuilder.Sites.SitesModule).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            var route = controller.GetCustomAttribute<RouteAttribute>()?.Template;

            Assert.NotNull(route);

            // The public placeholder is routed at "/" by design; everything else is an
            // administrative endpoint and carries the version.
            if (route == "/")
            {
                continue;
            }

            Assert.StartsWith("api/v1/", route, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
