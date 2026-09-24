using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authorization;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.UnitTests.Identity;

/// <summary>
/// The fail-closed branches of authorization: wiring mistakes stop the app or refuse the
/// request, never grant it (002 contracts/authorization.md; constitution X critical paths).
/// </summary>
/// <remarks>
/// None of these branches is reached by a correctly wired installation, which is exactly
/// why they need their own tests: nothing else would notice if one started to allow.
/// </remarks>
public sealed class AuthorizationGuardTests
{
    private static readonly CapabilityDefinition SiteView = new("test.things.view", CapabilityScope.Site, [SystemRole.Editor]);

    [Fact]
    public void A_capability_registered_twice_stops_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CapabilityCatalog([SiteView, SiteView]));

        Assert.Contains("test.things.view", error.Message, StringComparison.Ordinal);
        Assert.Contains("registered twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unregistered_capability_is_an_error_not_a_refusal_or_a_grant()
    {
        var catalog = new CapabilityCatalog([SiteView]);

        Assert.Null(catalog.Find("test.things.delete"));
        var error = Assert.Throws<InvalidOperationException>(() => catalog.Get("test.things.delete"));
        Assert.Contains("test.things.delete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_site_scoped_capability_on_a_route_without_a_site_is_refused()
    {
        var handler = new CapabilityRequirementHandler(new SignedInEvaluator(), new CapabilityCatalog([SiteView]));
        var requirement = new CapabilityRequirement(SiteView.Name);
        var context = new AuthorizationHandlerContext([requirement], new System.Security.Claims.ClaimsPrincipal(), new DefaultHttpContext());

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
        Assert.Contains(context.FailureReasons, r => r.Message == CapabilityRequirementHandler.MissingCapability);
    }

    [Fact]
    public async Task An_anonymous_caller_holds_nothing()
    {
        await using var database = UnconnectedDatabase();
        var evaluator = new AccessEvaluator(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            database,
            new CapabilityCatalog([SiteView]));

        Assert.False(evaluator.IsSignedIn);
        Assert.False(await evaluator.IsAdministratorAsync());
        Assert.False(await evaluator.HasCapabilityAsync(SiteView.Name, Guid.NewGuid()));
        Assert.Equal(SiteVisibility.Nothing, await evaluator.GetVisibleSitesAsync());
    }

    [Fact]
    public async Task The_administrator_lock_refuses_to_run_outside_a_transaction()
    {
        await using var database = UnconnectedDatabase();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AdministratorGuard(database).LockAsync(CancellationToken.None));

        Assert.Contains("transaction", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A context that never connects: each branch under test returns before any query.</summary>
    private static IdentityDbContext UnconnectedDatabase() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql("Host=unused.invalid").Options);

    private sealed class SignedInEvaluator : IAccessEvaluator
    {
        public bool IsSignedIn => true;

        public Guid? UserId { get; } = Guid.NewGuid();

        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        // Would grant, so a refusal can only come from the missing site.
        public Task<bool> HasCapabilityAsync(string name, Guid? siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<SiteVisibility> GetVisibleSitesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SiteVisibility.Everything);
    }
}
