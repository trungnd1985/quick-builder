using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>
/// The migration runner's own bookkeeping: schema <c>platform</c> (019 research RL-07,
/// constitution 2.5.1 Principle I).
/// </summary>
/// <remarks>
/// Not a module. The runner migrates it first and reads it before any module migrates, so an
/// older release can be refused before it touches a newer database. Changes here are additive
/// only, forever: an older release must always be able to read the row a newer one wrote.
/// Only <c>QuickBuilder.Shared.Infrastructure.Migrations</c> may use it (an architecture test
/// enforces that).
/// </remarks>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    internal const string Schema = "platform";

    internal const string HistoryTable = "__EFMigrationsHistory";

    internal DbSet<DatabaseVersionRow> DatabaseVersion => Set<DatabaseVersionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DatabaseVersionRow>(entity =>
        {
            entity.ToTable("database_version", Schema, table =>
                table.HasCheckConstraint("ck_database_version_singleton", "id = 1"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.RecordedVersion).HasMaxLength(64);
            entity.Property(e => e.UpgradingTo).HasMaxLength(64);
        });

        modelBuilder.UseSnakeCaseColumns();
    }
}
