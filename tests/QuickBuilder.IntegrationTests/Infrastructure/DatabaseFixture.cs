using Testcontainers.PostgreSql;
using Xunit;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// A real PostgreSQL instance, shared by every test in the collection.
/// </summary>
/// <remarks>
/// Constitution Principle X forbids in-memory database providers, and this feature is a
/// good illustration of why: the behaviours most worth testing here — <c>FOR UPDATE SKIP
/// LOCKED</c> claiming, advisory-lock migration serialisation, <c>citext</c> uniqueness,
/// partial indexes — either do not exist or behave differently outside real PostgreSQL. An
/// in-memory provider would report green while proving nothing.
/// <para>
/// One container per collection, one database per test class. Starting a container per
/// class would make the suite unbearably slow; sharing a single database across classes
/// would make tests order-dependent. A database each is the cheap middle.
/// </para>
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("quickbuilder_tests")
        .WithUsername("quickbuilder")
        .WithPassword("test-only-password")
        // Every test database gets its own connection pool, and several tests run a burst of
        // parallel requests. The default of 100 is exhausted by idle pooled connections
        // across a full run; QuickBuilderApp also clears its pools when disposed.
        .WithCommand("-c", "max_connections=300")
        // Reuse keeps repeat local runs fast; CI starts clean each time regardless.
        .WithCleanUp(true)
        .Build();

    /// <summary>Connection string for the container's default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Creates a fresh database and returns its connection string.
    /// </summary>
    /// <remarks>
    /// Each test class gets its own so that migrations, seeded sites and outbox rows from
    /// one class cannot influence another. The name is unique per call rather than derived
    /// from the class name, so a class may ask for several.
    /// </remarks>
    public async Task<string> CreateDatabaseAsync(string label)
    {
        var full = $"qb_{label.ToLowerInvariant()}_{Guid.NewGuid():N}";
        var name = full[..Math.Min(full.Length, 40)];

        var result = await _container
            .ExecScriptAsync($"CREATE DATABASE \"{name}\";")
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not create test database '{name}': {result.Stderr}");
        }

        // The container's connection string points at its default database; swap in ours.
        return _container.GetConnectionString().Replace(
            "Database=quickbuilder_tests",
            $"Database={name}",
            StringComparison.Ordinal);
    }
}

/// <summary>
/// Binds every integration test to the one container.
/// </summary>
/// <remarks>
/// xUnit runs collections in parallel but never two classes within one collection, so this
/// also serialises tests that would otherwise contend on the same database server. That is
/// a deliberate trade: a slower suite that is deterministic beats a fast one that is
/// flaky.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "xUnit collection fixtures are named <Thing>Collection by convention.")]
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
