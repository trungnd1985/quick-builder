using Microsoft.EntityFrameworkCore;

namespace QuickBuilder.Shared.Infrastructure.Modules;

/// <summary>
/// Describes a module's database so the host can migrate it without knowing anything else
/// about it (FR-022, FR-024).
/// </summary>
/// <remarks>
/// Registered by each module's Add&lt;Module&gt;Module() entry point. The migration runner
/// iterates whatever is registered, which is how adding a module requires no edit to any
/// existing one (FR-004, SC-009).
/// </remarks>
public sealed record ModuleDatabase(string ModuleName, string Schema, Type DbContextType)
{
    /// <summary>
    /// Key for the PostgreSQL advisory lock guarding this module's migrations (D-04).
    /// </summary>
    /// <remarks>
    /// Derived from the module name so modules migrate independently rather than
    /// contending on one global lock. Advisory locks are held by the connection, so an
    /// instance that dies releases its lock automatically — no stuck-migration state to
    /// clean up.
    /// </remarks>
    public long AdvisoryLockKey => DeriveLockKey(ModuleName);

    internal static long DeriveLockKey(string moduleName)
    {
        // FNV-1a, 64-bit. Any stable hash works; this one is short, dependency-free and
        // deterministic across processes and runtimes, which a string's GetHashCode is
        // explicitly not.
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;
        foreach (var c in moduleName)
        {
            hash ^= c;
            hash *= prime;
        }

        return unchecked((long)hash);
    }
}

/// <summary>
/// Registers a module's database with the host. Implemented by module registration code.
/// </summary>
public interface IModuleDatabaseProvider
{
    ModuleDatabase Describe();

    /// <summary>
    /// Builds a context for migration work. Used by the migration runner, which lives in
    /// shared infrastructure and is allowed to know about EF Core.
    /// </summary>
    DbContext CreateContext(IServiceProvider services);

    /// <summary>
    /// Whether this module's database is reachable.
    /// </summary>
    /// <remarks>
    /// Exists so the readiness probe can answer without the host ever handling a
    /// DbContext. The host composes (FR-001); knowing what a DbContext is would be data
    /// access leaking into it, and an architecture test asserts it does not.
    /// </remarks>
    Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken);
}
