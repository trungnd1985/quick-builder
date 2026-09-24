using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// The Identity module's data context. Owns the <c>identity</c> schema and nothing else
/// (001 FR-022; data-model.md).
/// </summary>
/// <remarks>
/// No table here is site-owned, so there is no site query filter and no ambient site
/// context. <see cref="RoleGrant"/> refers to a site without being owned by one; see its
/// <see cref="InstallationScopedAttribute"/>.
/// </remarks>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    internal const string Schema = "identity";

    internal DbSet<Installation> Installation => Set<Installation>();

    internal DbSet<User> Users => Set<User>();

    internal DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();

    internal DbSet<Session> Sessions => Set<Session>();

    internal DbSet<SetPasswordLink> SetPasswordLinks => Set<SetPasswordLink>();

    internal DbSet<KnownDevice> KnownDevices => Set<KnownDevice>();

    internal DbSet<SignInThrottle> SignInThrottles => Set<SignInThrottle>();

    internal DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // Registered here rather than at the call sites that build options, so every way of
        // constructing this context — DI, migrations, health checks, tests — enforces it.
        optionsBuilder.AddInterceptors(SecurityEventAppendOnlyInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // citext: email addresses compare case-insensitively (FR-007), in the column rather
        // than in every query.
        modelBuilder.HasPostgresExtension("citext");

        var userStatus = new ValueConverter<UserStatus, string>(
            v => v == UserStatus.Active ? "active" : "deactivated",
            v => v == "active" ? UserStatus.Active : UserStatus.Deactivated);

        var role = new ValueConverter<SystemRole, string>(
            v => v.ToString().ToLowerInvariant(),
            v => Enum.Parse<SystemRole>(v, true));

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.ToTable("installation", Schema, table =>
            {
                table.HasCheckConstraint("ck_installation_singleton", "id = 1");
                table.HasCheckConstraint(
                    "ck_installation_claimed_xor_code",
                    "(claimed_at_utc IS NULL) = (setup_code IS NOT NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.SetupCode).HasMaxLength(12);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", Schema, table =>
            {
                table.HasCheckConstraint("ck_users_status", "status IN ('active', 'deactivated')");
                table.HasCheckConstraint(
                    "ck_users_deactivated_at",
                    "(status = 'deactivated') = (deactivated_at_utc IS NOT NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasColumnType("citext").HasMaxLength(User.MaxEmailLength).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(User.MaxDisplayNameLength).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion(userStatus).HasMaxLength(16).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("ux_users_email");
        });

        modelBuilder.Entity<RoleGrant>(entity =>
        {
            entity.ToTable("role_grants", Schema, table =>
            {
                table.HasCheckConstraint("ck_role_grants_role", "role IN ('administrator', 'designer', 'editor')");

                // FR-020: Administrator is installation-wide; Designer and Editor name a site.
                table.HasCheckConstraint("ck_role_grants_scope", "(role = 'administrator') = (site_id IS NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasConversion(role).HasMaxLength(16).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);

            entity
                .HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter("role = 'administrator'")
                .HasDatabaseName("ux_role_grants_one_administrator");

            entity
                .HasIndex(e => new { e.UserId, e.SiteId })
                .IsUnique()
                .HasFilter("site_id IS NOT NULL")
                .HasDatabaseName("ux_role_grants_one_role_per_site");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions", Schema);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired();
            entity.Property(e => e.RevocationReason).HasMaxLength(32);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("ux_sessions_token_hash");
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_sessions_user_id");
        });

        modelBuilder.Entity<SetPasswordLink>(entity =>
        {
            entity.ToTable("set_password_links", Schema);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired();
            entity.Property(e => e.InvalidationReason).HasMaxLength(32);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("ux_set_password_links_token_hash");

            // FR-011a: at most one live link per account.
            entity
                .HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter("used_at_utc IS NULL AND invalidated_at_utc IS NULL")
                .HasDatabaseName("ux_set_password_links_one_live");
        });

        modelBuilder.Entity<KnownDevice>(entity =>
        {
            entity.ToTable("known_devices", Schema);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("ux_known_devices_token_hash");
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_known_devices_user_id");
        });

        modelBuilder.Entity<SignInThrottle>(entity =>
        {
            entity.ToTable("sign_in_throttles", Schema, table =>
                table.HasCheckConstraint("ck_sign_in_throttles_kind", "kind IN ('client', 'account')"));
            entity.HasKey(e => new { e.Kind, e.Key });
            entity.Property(e => e.Kind).HasMaxLength(16);
            entity.Property(e => e.Key).HasMaxLength(64);
            entity.HasIndex(e => e.UpdatedAtUtc).HasDatabaseName("ix_sign_in_throttles_updated_at");
        });

        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.ToTable("security_events", Schema, table =>
                table.HasCheckConstraint("ck_security_events_outcome", "outcome IN ('succeeded', 'failed', 'refused')"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(48).IsRequired();
            entity.Property(e => e.Outcome).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Details).HasColumnType("jsonb").IsRequired();

            // No foreign keys to users: an event outlives nothing but its retention period,
            // and must never block anything else from changing.
            entity.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("ix_security_events_occurred_at");
        });

        // Every module carries the standard outbox tables: the shared dispatcher and
        // retention sweep visit each module's store (001 D-01). Identity publishes no
        // integration events yet.
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Refuses to save a modified or deleted <see cref="SecurityEvent"/> (research R-14).
/// </summary>
/// <remarks>
/// Security events are evidence. The retention sweep removes old ones with a set-based
/// delete that bypasses the change tracker, which is the single sanctioned exit; nothing
/// else may rewrite or remove one.
/// </remarks>
internal sealed class SecurityEventAppendOnlyInterceptor : SaveChangesInterceptor
{
    internal static readonly SecurityEventAppendOnlyInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContextEventData eventData)
    {
        var tampered = eventData.Context?.ChangeTracker
            .Entries<SecurityEvent>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted) == true;

        if (tampered)
        {
            throw new InvalidOperationException(
                "Security events are append-only: they may be added, never modified or deleted " +
                "(research R-14). Old events leave only through the retention sweep.");
        }
    }
}
