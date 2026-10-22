using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>Registers outgoing email (018 research FS-18).</summary>
public static class EmailRegistration
{
    /// <summary>
    /// Registers <see cref="IEmailSender"/>: the SMTP sender when <c>Email:Smtp:Host</c> is set,
    /// otherwise one that never sends. The host binds and validates <see cref="SmtpOptions"/>.
    /// </summary>
    /// <remarks>
    /// The choice is made from the bound options when the sender is first needed, not from
    /// configuration read here, so a host that adds configuration late (as tests do) still gets
    /// the sender its settings call for.
    /// </remarks>
    public static IServiceCollection AddEmail(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
        services.AddSingleton<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SmtpOptions>>();
            return options.Value.IsConfigured ? new SmtpEmailSender(options) : new DisabledEmailSender();
        });
        return services;
    }
}
