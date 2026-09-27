using QuickBuilder.Pages.Domain;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// The tree's invariants hold after any sequence of operations, accepted or refused
/// (010 SC-002, data-model.md "Invariants of PageTree").
/// </summary>
/// <remarks>
/// Random sequences from fixed seeds, so a failure reproduces. Arguments are drawn from a
/// small pool of slugs so collisions, reserved segments and deep moves happen often, and
/// versions are sometimes stale on purpose.
/// </remarks>
public sealed class PageTreeInvariantTests
{
    private static readonly string[] Slugs = ["a", "b", "c", "faq", "news", "api", "team", "x-y"];

    public static TheoryData<int> Seeds()
    {
        var seeds = new TheoryData<int>();
        for (var seed = 1; seed <= 200; seed++)
        {
            seeds.Add(seed);
        }

        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_invariant_holds_after_every_step(int seed)
    {
        var random = new Random(seed);
        var kit = new PageTreeTestKit();
        var tree = kit.Tree;

        for (var step = 0; step < 50; step++)
        {
            var pages = tree.Pages.ToList();
            var any = pages.Count > 0 ? pages[random.Next(pages.Count)] : null;
            var other = pages.Count > 0 ? pages[random.Next(pages.Count)] : null;
            var slug = Slugs[random.Next(Slugs.Length)];
            var version = any is null ? 1 : (random.Next(10) == 0 ? any.Version - 1 : any.Version);

            switch (random.Next(5))
            {
                case 0 or 1:
                    tree.CreatePage(
                        "t", slug, random.Next(3) == 0 ? null : other?.Id, Placement.Last,
                        isHome: random.Next(8) == 0, PageTreeTestKit.Actor, kit.Clock);
                    break;
                case 2 when any is not null:
                    tree.Rename(any.Id, "t", slug, version, PageTreeTestKit.Actor, kit.Clock);
                    break;
                case 3 when any is not null:
                    var parent = random.Next(3) == 0 ? null : other?.Id;
                    var siblings = pages.Where(p => p.ParentId == parent).ToList();
                    Guid? anchor = siblings.Count > 0 && random.Next(2) == 0 ? siblings[random.Next(siblings.Count)].Id : null;
                    tree.Move(any.Id, parent, anchor, version, PageTreeTestKit.Actor, kit.Clock);
                    break;
                case 4 when any is not null:
                    if (random.Next(2) == 0)
                    {
                        tree.MakeHome(any.Id, version, PageTreeTestKit.Actor, kit.Clock);
                    }
                    else
                    {
                        tree.Delete(any.Id, version, kit.Clock);
                    }

                    break;
            }

            kit.AssertInvariants();
        }
    }
}
