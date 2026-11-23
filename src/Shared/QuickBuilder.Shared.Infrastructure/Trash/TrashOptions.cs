using System.ComponentModel.DataAnnotations;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Shared.Infrastructure.Trash;

/// <summary>
/// How long deleted content stays in a site's trash (021 FR-025, research TR-15).
/// </summary>
/// <remarks>
/// One installation-wide value set by the operator (clarification Q3), bound to the
/// <c>Trash</c> section and validated at startup: an invalid value stops the process and
/// names <c>Trash:RetentionDays</c> and its range. Days rather than a TimeSpan, because
/// operators think in days and a TimeSpan accepts meaningless values such as 90 minutes.
/// </remarks>
[SettingsSection(SectionName, Area = "trash", Title = "Trash")]
public sealed class TrashOptions
{
    public const string SectionName = "Trash";

    /// <summary>Days an entry stays in the trash before it is purged. 1 to 3650, default 30.</summary>
    [Range(1, 3650, ErrorMessage = "Trash:RetentionDays must be between 1 and 3650 days.")]
    [Setting("Days deleted pages, shared components, collection items and media files stay in their site's trash before an hourly job deletes them permanently. A change applies at once to everything already in the trash.")]
    public int RetentionDays { get; set; } = 30;
}
