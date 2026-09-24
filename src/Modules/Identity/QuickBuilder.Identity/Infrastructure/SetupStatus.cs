using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Contracts;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Whether the installation is claimed, answered cheaply after the first "yes"
/// (research R-12).
/// </summary>
/// <remarks>
/// Claiming only ever goes one way, so once this instance has seen the installation
/// claimed it never needs to ask the database again. That is safe on every instance: one
/// that has not yet seen the claim keeps asking until it does. The flag is per instance of
/// this singleton, not static, so each application host keeps its own.
/// </remarks>
internal sealed class SetupStatus(IServiceScopeFactory scopes) : IIdentitySetupStatus
{
    private volatile bool _claimed;

    public async Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default)
    {
        if (_claimed)
        {
            return true;
        }

        await using var scope = scopes.CreateAsyncScope();
        var installation = await scope.ServiceProvider
            .GetRequiredService<InstallationStore>()
            .EnsureAsync(cancellationToken)
            .ConfigureAwait(false);

        if (installation.IsClaimed)
        {
            _claimed = true;
        }

        return _claimed;
    }

    /// <summary>Called by the setup handler once its claim has committed.</summary>
    public void MarkClaimed() => _claimed = true;
}
