using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Host.Configuration;

/// <summary>
/// Settings that third-party code reads for itself (Serilog), described for the configuration
/// reference (026 data-model §1, ExternalSetting).
/// </summary>
/// <remarks>
/// The product never binds this class: Serilog reads the <c>Serilog</c> section directly. It
/// exists so the reference is generated from code like every other section, with defaults
/// taken from the shipped <c>appsettings.json</c>, and so the configuration-read check knows
/// these keys are documented.
/// </remarks>
[SettingsSection(SectionName, Area = "logging", Title = "Logging")]
internal sealed class LoggingSettingsDescription
{
    public const string SectionName = "Serilog";

    [SettingsGroup("Log levels")]
    public LevelSettings MinimumLevel { get; set; } = new();

    /// <summary>The <c>Serilog:MinimumLevel</c> keys.</summary>
    internal sealed class LevelSettings
    {
        [Setting("The lowest level written to the log for every component without an override.",
            Allowed = "`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`")]
        public string Default { get; set; } = string.Empty;

        [Setting("The lowest level for one component and everything under it, by namespace, e.g. " +
                 "`Serilog__MinimumLevel__Override__QuickBuilder=Debug`.",
            EntryName = "Namespace",
            Allowed = "`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`")]
        public Dictionary<string, string> Override { get; set; } = [];
    }
}
