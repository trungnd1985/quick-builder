using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Identity.Features.ChangeOwnPassword;
using QuickBuilder.Identity.Features.CheckSetPasswordLink;
using QuickBuilder.Identity.Features.CompleteSetup;
using QuickBuilder.Identity.Features.CreateUser;
using QuickBuilder.Identity.Features.DeactivateUser;
using QuickBuilder.Identity.Features.GetMe;
using QuickBuilder.Identity.Features.GetStatus;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Features.IssueSetPasswordLink;
using QuickBuilder.Identity.Features.ListUsers;
using QuickBuilder.Identity.Features.ReactivateUser;
using QuickBuilder.Identity.Features.ReplaceGrants;
using QuickBuilder.Identity.Features.RevokeOwnOtherSessions;
using QuickBuilder.Identity.Features.RevokeUserSessions;
using QuickBuilder.Identity.Features.SetPassword;
using QuickBuilder.Identity.Features.SignIn;
using QuickBuilder.Identity.Features.SignOut;
using QuickBuilder.Identity.Features.UpdateUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Authorization;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Identity.Infrastructure.Setup;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity;

/// <summary>
/// The Identity module's registration entry points (001 FR-004).
/// </summary>
/// <remarks>
/// The host calls <see cref="AddIdentityModule"/> with the other modules, and
/// <see cref="UseIdentityModule"/> once in the pipeline between authentication and
/// authorization. Everything else in this assembly is internal apart from the controllers.
/// </remarks>
public static class IdentityModule
{
    /// <summary>The capability that guards every user-management operation.</summary>
    internal const string ManageUsers = "identity.users.manage";

    /// <summary>Registers the Identity module: data, options, services and controllers.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                // Own migrations history table inside the identity schema (001 FR-022).
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

        services.AddSingleton<IModuleDatabaseProvider, IdentityDatabaseProvider>();

        services
            .AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>();
        services
            .AddOptions<PublicUrlOptions>()
            .Bind(configuration.GetSection(PublicUrlOptions.SectionName));

        services.AddSingleton(new CapabilityDefinition(ManageUsers, CapabilityScope.Installation, []));

        // Security primitives (research R-03, R-04, R-07, R-15).
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Pbkdf2PasswordHasher>();
        services.AddSingleton<TransportSecurity>();
        services.AddScoped<SecurityEventRecorder>();
        services.AddScoped<SignInThrottleService>();
        services.AddScoped<KnownDeviceService>();
        services.AddScoped<SessionService>();
        services.AddScoped<SetPasswordLinkService>();
        services.AddScoped<AdministratorGuard>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddHostedService<IdentityStartupChecks>();

        // Setup (research R-09, R-12).
        services.AddScoped<InstallationStore>();
        services.AddSingleton<SetupStatus>();
        services.AddSingleton<IIdentitySetupStatus>(sp => sp.GetRequiredService<SetupStatus>());
        services.AddHostedService<InstallationBootstrapper>();

        AddAuthenticationAndAuthorization(services);
        AddFeatures(services);

        return services;
    }

    /// <summary>Feature slices. Each is registered here and nowhere else.</summary>
    private static void AddFeatures(IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetStatusQuery, StatusResponse>, GetStatusHandler>();

        services.AddScoped<ICommandHandler<CompleteSetupCommand, Result<MeResponse>>, CompleteSetupHandler>();
        services.AddScoped<IValidator<CompleteSetupCommand>, CompleteSetupValidator>();

        services.AddScoped<IQueryHandler<GetMeQuery, Result<MeResponse>>, GetMeHandler>();

        services.AddScoped<ICommandHandler<SignInCommand, Result<MeResponse>>, SignInHandler>();
        services.AddScoped<IValidator<SignInCommand>, SignInValidator>();

        services.AddScoped<ICommandHandler<SignOutCommand, Result>, SignOutHandler>();

        // User management (US3).
        services.AddScoped<IQueryHandler<ListUsersQuery, IReadOnlyList<UserResponse>>, ListUsersHandler>();
        services.AddScoped<IQueryHandler<GetUserQuery, Result<UserResponse>>, GetUserHandler>();
        services.AddScoped<ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>, CreateUserHandler>();
        services.AddScoped<IValidator<CreateUserCommand>, CreateUserValidator>();
        services.AddScoped<ICommandHandler<UpdateUserCommand, Result<UserResponse>>, UpdateUserHandler>();
        services.AddScoped<IValidator<UpdateUserCommand>, UpdateUserValidator>();
        services.AddScoped<ICommandHandler<ReplaceGrantsCommand, Result<UserResponse>>, ReplaceGrantsHandler>();
        services.AddScoped<IValidator<ReplaceGrantsCommand>, ReplaceGrantsValidator>();
        services.AddScoped<ICommandHandler<DeactivateUserCommand, Result<UserResponse>>, DeactivateUserHandler>();
        services.AddScoped<ICommandHandler<ReactivateUserCommand, Result<UserResponse>>, ReactivateUserHandler>();
        services.AddScoped<ICommandHandler<IssueSetPasswordLinkCommand, Result<IssuedLinkResponse>>, IssueSetPasswordLinkHandler>();
        services.AddScoped<ICommandHandler<RevokeUserSessionsCommand, Result>, RevokeUserSessionsHandler>();

        // Links and self-service (US3).
        services.AddScoped<ICommandHandler<CheckSetPasswordLinkCommand, Result<CheckSetPasswordLinkResponse>>, CheckSetPasswordLinkHandler>();
        services.AddScoped<ICommandHandler<SetPasswordCommand, Result>, SetPasswordHandler>();
        services.AddScoped<ICommandHandler<ChangeOwnPasswordCommand, Result>, ChangeOwnPasswordHandler>();
        services.AddScoped<IValidator<ChangeOwnPasswordCommand>, ChangeOwnPasswordValidator>();
        services.AddScoped<ICommandHandler<RevokeOwnOtherSessionsCommand, Result>, RevokeOwnOtherSessionsHandler>();
    }

    /// <summary>
    /// Session authentication, and policy-based authorization from the kernel's declarations
    /// (research R-04, R-05). The fallback policy requires a signed-in user, so an endpoint
    /// that declares nothing fails closed.
    /// </summary>
    private static void AddAuthenticationAndAuthorization(IServiceCollection services)
    {
        services
            .AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
                SessionAuthenticationHandler.SchemeName,
                configureOptions: null);

        services.AddAuthorization(options =>
        {
            var signedIn = new AuthorizationPolicyBuilder(SessionAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build();

            options.DefaultPolicy = signedIn;
            options.FallbackPolicy = signedIn;
            options.AddPolicy(IdentityPolicies.SignedIn, signedIn);
        });

        services.AddSingleton<CapabilityCatalog>();
        services.AddScoped<IAccessEvaluator, AccessEvaluator>();
        services.AddSingleton<IAuthorizationPolicyProvider, CapabilityPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, CapabilityRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();
        services.Configure<MvcOptions>(options => options.Conventions.Add(new CapabilityConvention()));
    }

    /// <summary>
    /// Adds the Identity module's request pipeline pieces. Called by the host after
    /// <c>UseAuthentication()</c> and before <c>UseAuthorization()</c>.
    /// </summary>
    /// <remarks>
    /// The setup gate comes first: during setup mode an admin request gets "setup required"
    /// rather than "sign in", because there is nobody to sign in as yet. The request guard
    /// follows, so a forged state change is refused before authorization runs.
    /// </remarks>
    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<SetupGateMiddleware>();
        app.UseMiddleware<RequestGuardMiddleware>();

        return app;
    }
}

/// <summary>Describes the module's database to the host (001 FR-024, D-04).</summary>
internal sealed class IdentityDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Identity", IdentityDbContext.Schema, typeof(IdentityDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<DbContextOptions<IdentityDbContext>>();
        return new IdentityDbContext(options);
    }

    public async Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = CreateContext(services);
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A probe answers rather than throws; the component that hit the failure logs it.
            return false;
        }
    }
}
