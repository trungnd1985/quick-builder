using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// The write gate as EF Core runs it, on a context of its own: the synchronous saves the modules
/// do not use, a save that fails, and rows of no site (020 FR-028, research ST-07).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SiteWriteGateInterceptorTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_synchronous_save_is_gated_like_an_asynchronous_one()
    {
        var connectionString = await database.CreateDatabaseAsync("gate-sync").ConfigureAwait(true);
        var site = Guid.NewGuid();
        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            context.Notes.Add(new Note { SiteId = site, Text = "before" });
            Assert.Equal(1, context.SaveChanges());
            Assert.Null(context.Database.CurrentTransaction);
        }

        await using var import = new NpgsqlConnection(connectionString);
        await import.OpenAsync().ConfigureAwait(true);
        await using var transaction = await import.BeginTransactionAsync().ConfigureAwait(true);
        await SiteWriteLock.EnterExclusiveAsync(import, transaction, site, CancellationToken.None).ConfigureAwait(true);

        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            context.Notes.Add(new Note { SiteId = site, Text = "during" });
            var refused = Assert.Throws<SiteImportInProgressException>(() => context.SaveChanges());
            Assert.Equal(site, refused.SiteId);
            Assert.Null(context.Database.CurrentTransaction);

            // Another site's rows are not held up.
            context.ChangeTracker.Clear();
            context.Notes.Add(new Note { SiteId = Guid.NewGuid(), Text = "elsewhere" });
            Assert.Equal(1, context.SaveChanges());
        }

        await transaction.CommitAsync().ConfigureAwait(true);
        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            Assert.Equal(["before", "elsewhere"], await context.Notes.OrderBy(n => n.Text).Select(n => n.Text).ToListAsync().ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task A_failed_save_rolls_back_the_transaction_the_gate_opened()
    {
        var connectionString = await database.CreateDatabaseAsync("gate-failed").ConfigureAwait(true);
        var site = Guid.NewGuid();
        var id = Guid.NewGuid();
        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            context.Notes.Add(new Note { Id = id, SiteId = site, Text = "first" });
            await context.SaveChangesAsync().ConfigureAwait(true);
        }

        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            context.Notes.Add(new Note { Id = id, SiteId = site, Text = "same id" });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Null(context.Database.CurrentTransaction);
        }

        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            context.Notes.Add(new Note { Id = id, SiteId = site, Text = "same id again" });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync()).ConfigureAwait(true);
            Assert.Null(context.Database.CurrentTransaction);

            // The context stays usable: the gate left no transaction behind.
            context.ChangeTracker.Clear();
            context.Notes.Add(new Note { SiteId = site, Text = "after" });
            Assert.Equal(1, await context.SaveChangesAsync().ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task A_save_inside_the_caller_s_transaction_is_left_for_the_caller_to_commit()
    {
        var connectionString = await database.CreateDatabaseAsync("gate-caller").ConfigureAwait(true);
        var site = Guid.NewGuid();
        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(true);
            context.Notes.Add(new Note { SiteId = site, Text = "rolled back" });
            await context.SaveChangesAsync().ConfigureAwait(true);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            await transaction.RollbackAsync().ConfigureAwait(true);
        }

        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            Assert.Empty(await context.Notes.ToListAsync().ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task A_cancelled_save_leaves_no_transaction_and_writes_nothing()
    {
        var connectionString = await database.CreateDatabaseAsync("gate-cancel").ConfigureAwait(true);
        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(true);
            context.Notes.Add(new Note { SiteId = Guid.NewGuid(), Text = "cancelled" });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(cancelled.Token)).ConfigureAwait(true);
            Assert.Null(context.Database.CurrentTransaction);
        }

        await using (var context = await NotesContext.CreateAsync(connectionString).ConfigureAwait(true))
        {
            Assert.Empty(await context.Notes.ToListAsync().ConfigureAwait(true));
        }
    }

    [Fact]
    public void The_refusal_carries_its_code_and_messages()
    {
        var site = Guid.NewGuid();
        Assert.Equal("site.import_in_progress", SiteImportInProgressException.Code);
        Assert.Contains(site.ToString(), new SiteImportInProgressException(site).Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Empty, new SiteImportInProgressException().SiteId);
        Assert.Equal("paused", new SiteImportInProgressException("paused").Message);
        var inner = new InvalidOperationException();
        Assert.Same(inner, new SiteImportInProgressException("paused", inner).InnerException);
    }

    private sealed class Note : ISiteOwned
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SiteId { get; set; }

        public string Text { get; set; } = "";
    }

    private sealed class NotesContext(DbContextOptions<NotesContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        public static async Task<NotesContext> CreateAsync(string connectionString)
        {
            var options = new DbContextOptionsBuilder<NotesContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(new SiteWriteGateInterceptor())
                .Options;
            var context = new NotesContext(options);
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            return context;
        }
    }
}
