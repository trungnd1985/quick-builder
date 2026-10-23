using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// An upgrade killed halfway resumes cleanly (019 FR-018, research RL-20).
/// </summary>
/// <remarks>
/// EF Core runs each migration in its own transaction, so a kill leaves either the whole
/// migration or none of it. A step that opts out of the transaction (<c>CREATE INDEX
/// CONCURRENTLY</c> needs to) can be left half-done, and is run again on the next start; it
/// must therefore be written to be re-run.
/// </remarks>
public sealed partial class MigrationTransactionTests
{
    private const string Provider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static IEnumerable<Type> Migrations() =>
        ModuleBoundaryTests.ModuleImplementations
            .Append(typeof(Shared.Infrastructure.Telemetry).Assembly)
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void Every_module_and_the_platform_schema_has_migrations()
    {
        var assemblies = Migrations().Select(t => t.Assembly.GetName().Name).Distinct().ToList();

        Assert.Contains("QuickBuilder.Shared.Infrastructure", assemblies);
        Assert.True(assemblies.Count >= 8, "Every module with data has migrations; fewer found means the scan is broken.");
    }

    [Fact]
    public void Statements_outside_a_transaction_can_be_run_again()
    {
        var offenders = new List<string>();

        foreach (var type in Migrations())
        {
            var migration = (Migration)Activator.CreateInstance(type)!;
            migration.ActiveProvider = Provider;

            offenders.AddRange(migration.UpOperations
                .OfType<SqlOperation>()
                .Where(o => o.SuppressTransaction && !Guarded().IsMatch(o.Sql))
                .Select(o => $"{type.Name}: {o.Sql.Trim()[..Math.Min(80, o.Sql.Trim().Length)]}"));
        }

        Assert.True(
            offenders.Count == 0,
            "These statements run outside a transaction but are not written to be re-run " +
            "(use IF NOT EXISTS / IF EXISTS):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_rule_sees_an_unguarded_statement()
    {
        Assert.DoesNotMatch(Guarded(), "CREATE INDEX CONCURRENTLY ix_a ON a (b)");
        Assert.Matches(Guarded(), "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_a ON a (b)");
        Assert.Matches(Guarded(), "drop index concurrently if exists ix_a");
    }

    /// <summary>
    /// Migrations only move forward (019 FR-015): nothing in the product migrates to a named
    /// target, which is how a database is taken back down.
    /// </summary>
    /// <remarks>
    /// The <c>Down</c> methods EF generates stay, because the upgrade tests use them to build an
    /// older database. This checks the source of the product, not the tests.
    /// </remarks>
    [Fact]
    public void The_product_never_migrates_to_a_target()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            if (MigratorService().IsMatch(text) || TargetedMigrate().IsMatch(text))
            {
                offenders.Add(Path.GetRelativePath(src, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These files migrate to a target or use IMigrator; the product migrates forward only:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_target_rule_sees_a_targeted_call()
    {
        Assert.Matches(TargetedMigrate(), "await db.Database.MigrateAsync(\"20260101_Initial\", token);");
        Assert.Matches(TargetedMigrate(), "context.Database.Migrate(target);");
        Assert.DoesNotMatch(TargetedMigrate(), "await context.Database.MigrateAsync(timeout.Token).ConfigureAwait(false);");
        Assert.DoesNotMatch(TargetedMigrate(), "await context.Database.MigrateAsync(token)");
        Assert.DoesNotMatch(TargetedMigrate(), "var values = migrator.Migrate(item, schema);");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [GeneratedRegex(@"\bIF\s+(NOT\s+)?EXISTS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Guarded();

    // Database.Migrate( or Database.MigrateAsync( with two arguments, or one that is not a
    // cancellation token. Only the EF database facade: Cms has its own item-value migrator.
    [GeneratedRegex(@"\bDatabase\s*\.\s*Migrate(?:Async)?\(\s*(?:""[^""]*""|[A-Za-z_][\w.]*\s*,|(?![\w.]*(?:[Tt]oken|cancellationToken)\s*\))[A-Za-z_][\w.]*\s*\))", RegexOptions.CultureInvariant)]
    private static partial Regex TargetedMigrate();

    // EF Core's migrator service, which can migrate to any target, up or down.
    [GeneratedRegex(@"\bIMigrator\b", RegexOptions.CultureInvariant)]
    private static partial Regex MigratorService();
}
