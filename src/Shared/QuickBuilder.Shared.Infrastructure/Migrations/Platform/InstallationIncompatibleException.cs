namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>
/// The startup check refused the database (019 data-model §2): a newer release migrated it, or
/// it is too old to upgrade directly. The full reason was logged at Critical; the message is
/// the anonymous detail for the readiness probe.
/// </summary>
public sealed class InstallationIncompatibleException : Exception
{
    public InstallationIncompatibleException()
    {
    }

    public InstallationIncompatibleException(string detail)
        : base(detail)
    {
    }

    public InstallationIncompatibleException(string detail, Exception innerException)
        : base(detail, innerException)
    {
    }
}
