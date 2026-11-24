namespace QuickBuilder.Docs.Settings;

/// <summary>
/// The reference's areas in page order (026 data-model §2). Adding one is a deliberate,
/// reviewed change: it moves headings operators link to.
/// </summary>
internal static class Areas
{
    public static IReadOnlyList<(string Id, string Title)> All { get; } =
    [
        ("required", "Required"),
        ("database", "Database"),
        ("hosting", "Hosting and reverse proxies"),
        ("admin", "Admin"),
        ("identity", "Identity"),
        ("storage", "Storage"),
        ("media", "Media"),
        ("transfer", "Site export and import"),
        ("outbox", "Outbox"),
        ("trash", "Trash"),
        ("preview", "Preview links"),
        ("menus", "Menus"),
        ("editing", "Editing sessions"),
        ("email", "Email"),
        ("forms", "Forms"),
        ("cache", "Cache"),
        ("telemetry", "Telemetry"),
        ("logging", "Logging"),
    ];

    public static bool Exists(string id) => All.Any(a => a.Id == id);

    public static int Order(string id) => All.Select((a, i) => (a.Id, i)).First(p => p.Id == id).i;
}
