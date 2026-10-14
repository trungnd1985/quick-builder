using NetArchTest.Rules;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Counting a redirect's use costs a visitor nothing (017 research SE-12): the counter the public
/// path calls holds its tallies in memory, and only the background flusher writes them.
/// </summary>
public sealed class RedirectHitCounterTests
{
    private static readonly System.Reflection.Assembly Pages = typeof(Pages.PagesModule).Assembly;

    [Fact]
    public void The_hit_counter_touches_no_database_or_file()
    {
        var offenders = Types.InAssembly(Pages)
            .That().HaveName("RedirectHitCounter")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "System.IO.File",
                "System.IO.FileStream",
                "System.Net.Http",
                "QuickBuilder.Pages.Infrastructure.PagesDbContext",
                "QuickBuilder.Pages.Infrastructure.PublishedReadDbContext")
            .GetResult()
            .FailingTypes?
            .Select(t => t.FullName!)
            .ToList() ?? [];

        Assert.True(offenders.Count == 0, "The redirect hit counter must stay in memory:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_counter_exists_under_its_name()
    {
        Assert.Contains(Pages.GetTypes(), t => t.Name == "RedirectHitCounter");
    }
}
