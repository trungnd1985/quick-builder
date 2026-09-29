using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.UnitTests.Pages.Publishing;

/// <summary>The publication aggregate (012 data-model §3.1, research PR-02, PR-05).</summary>
public sealed class PublicationTests
{
    private const string Document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";

    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private static Publication NewPage() => Publication.Start(Site, SubjectKinds.Page, Guid.NewGuid());

    private static PublishedVersion Publish(Publication publication, long draftVersion = 7, string title = "About") =>
        publication.Publish(Document, 1, title, draftVersion, VersionKinds.Publish, null, Actor, "/about", Now);

    [Fact]
    public void A_new_publication_is_not_live()
    {
        var publication = NewPage();

        Assert.False(publication.IsLive);
        Assert.Equal(1, publication.NextNumber);
        Assert.Equal(1, publication.Generation);
        Assert.False(publication.HasUnpublishedChanges(1, "About", null));
    }

    [Fact]
    public void Publishing_numbers_versions_one_after_another_and_makes_the_latest_live()
    {
        var publication = NewPage();

        var first = Publish(publication);
        var second = Publish(publication);

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal(second.Id, publication.LiveVersionId);
        Assert.Equal(2, publication.LiveNumber);
        Assert.Equal(3, publication.NextNumber);
        Assert.Equal(Actor, publication.LiveBy);
        Assert.Equal(Now, publication.LiveSinceUtc);
    }

    [Fact]
    public void Every_publish_moves_the_generation_on()
    {
        var publication = NewPage();

        Publish(publication);
        Publish(publication);

        Assert.Equal(3, publication.Generation);
    }

    [Fact]
    public void A_version_is_a_copy_of_what_was_published()
    {
        var version = Publish(NewPage(), title: "Pricing");

        Assert.Equal(Document, version.Document);
        Assert.Equal("Pricing", version.Title);
        Assert.Equal(VersionKinds.Publish, version.Kind);
        Assert.Equal(Actor, version.PublishedBy);
    }

    [Fact]
    public void The_draft_has_changes_when_its_version_or_the_title_moved_on()
    {
        var publication = NewPage();
        Publish(publication, draftVersion: 7, title: "About");

        Assert.False(publication.HasUnpublishedChanges(7, "About", "About"));
        Assert.True(publication.HasUnpublishedChanges(8, "About", "About"));
        Assert.True(publication.HasUnpublishedChanges(7, "About us", "About"));
    }

    [Fact]
    public void A_component_has_no_title_to_compare()
    {
        var publication = Publication.Start(Site, SubjectKinds.Component, Guid.NewGuid());
        publication.Publish(Document, 1, null, 3, VersionKinds.Publish, null, Actor, null, Now);

        Assert.False(publication.HasUnpublishedChanges(3, "Header", null));
    }

    [Fact]
    public void After_a_rollback_the_draft_reads_as_changed_until_the_next_publish()
    {
        var publication = NewPage();
        Publish(publication, draftVersion: 7);

        var rollback = publication.Publish(Document, 1, "About", null, VersionKinds.Rollback, 1, Actor, "/about", Now);

        Assert.Equal(2, rollback.Number);
        Assert.Equal(1, rollback.RestoredFromNumber);
        Assert.True(publication.HasUnpublishedChanges(7, "About", "About"));
    }

    [Fact]
    public void Unpublishing_clears_the_live_version_but_keeps_the_numbering()
    {
        var publication = NewPage();
        Publish(publication);
        var generation = publication.Generation;

        publication.Unpublish(scheduled: false, "/about", Now);

        Assert.False(publication.IsLive);
        Assert.Null(publication.LiveNumber);
        Assert.Equal(2, publication.NextNumber);
        Assert.Equal(generation + 1, publication.Generation);
        Assert.Throws<InvalidOperationException>(() => publication.Unpublish(scheduled: false, "/about", Now));
    }

    [Fact]
    public void Publishing_and_unpublishing_raise_their_events()
    {
        var publication = NewPage();

        Publish(publication);
        publication.Unpublish(scheduled: true, "/about", Now);

        Assert.Collection(
            publication.DomainEvents,
            e => Assert.Equal(1, Assert.IsType<SubjectPublishedEvent>(e).VersionNumber),
            e => Assert.True(Assert.IsType<SubjectUnpublishedEvent>(e).Scheduled));
    }
}
