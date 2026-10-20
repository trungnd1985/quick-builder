using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Which integration events Pages' domain events become (012 research P-14; 024 data-model §6).
/// </summary>
/// <remarks>
/// Only the change tracker is inspected: nothing is saved, so no database is needed.
/// </remarks>
public sealed class PagesOutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_menu_publish_and_unpublish_announce_nothing()
    {
        using var database = NewContext();
        var publication = Publication.Start(Guid.NewGuid(), SubjectKinds.Menu, Guid.NewGuid());

        publication.Publish("""{"schemaVersion":1,"entries":[]}""", 1, "Main", 1, VersionKinds.Publish, null, Guid.NewGuid(), null, Now);
        publication.Unpublish(scheduled: false, path: null, Now);
        PagesOutbox.AddFrom(database, publication, Guid.NewGuid());

        Assert.Empty(database.ChangeTracker.Entries<OutboxMessage>());
        Assert.Empty(publication.DomainEvents);
    }

    [Fact]
    public void A_component_publish_still_announces_a_component_publish()
    {
        using var database = NewContext();
        var publication = Publication.Start(Guid.NewGuid(), SubjectKinds.Component, Guid.NewGuid());

        publication.Publish(Draft.EmptyDocument, 1, null, 1, VersionKinds.Publish, null, Guid.NewGuid(), null, Now);
        PagesOutbox.AddFrom(database, publication, Guid.NewGuid());

        var message = Assert.Single(database.ChangeTracker.Entries<OutboxMessage>()).Entity;
        Assert.Equal("Pages.ComponentPublished", message.EventType);
    }

    private static PagesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PagesDbContext>().UseNpgsql("Host=unused").Options, new NoSite());

    private sealed class NoSite : ISiteContext
    {
        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
