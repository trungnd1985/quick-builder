using QuickBuilder.IntegrationTests.Isolation.Probes;

namespace QuickBuilder.IntegrationTests.Isolation;

/// <summary>
/// Every registered probe. A module that stores site-owned records adds its probe here
/// (docs/contributing.md, "Adding a module with site-owned data").
/// </summary>
public static class IsolationProbes
{
    public static IReadOnlyList<ISiteIsolationProbe> All { get; } =
    [
        new SitesIsolationProbe(),
        new MediaIsolationProbe(),
        new PagesIsolationProbe(),
        new EditorIsolationProbe(),
    ];

    public static TheoryData<string> Names
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var probe in All)
            {
                names.Add(probe.Module);
            }

            return names;
        }
    }

    public static ISiteIsolationProbe Named(string module) => All.Single(p => p.Module == module);
}
