using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using Xunit;

namespace QuickBuilder.UnitTests.Dispatch;

/// <summary>
/// The command/query dispatcher and its validation decorator (FR-006, FR-008).
/// </summary>
/// <remarks>
/// The constitution forbids a mediator library, so this dispatcher is the whole
/// mechanism — every feature in every module goes through it. Its behaviour is worth
/// pinning precisely rather than inferring from endpoint tests.
/// <para>
/// The assertion that matters most is that an invalid request never reaches its handler.
/// A validator that runs *after* the handler is worse than none: it reports a problem
/// that has already been committed.
/// </para>
/// </remarks>
public sealed class DispatcherTests
{
    private sealed record Ping(string Message) : ICommand<Result<string>>;

    private sealed record Ask(string Question) : IQuery<Result<string>>;

    private sealed class PingHandler : ICommandHandler<Ping, Result<string>>
    {
        internal bool WasCalled { get; private set; }

        public Task<Result<string>> HandleAsync(Ping command, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(Result.Success(command.Message.ToUpperInvariant()));
        }
    }

    private sealed class AskHandler : IQueryHandler<Ask, Result<string>>
    {
        public Task<Result<string>> HandleAsync(Ask query, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success($"answer:{query.Question}"));
    }

    private sealed class RejectEverything : IValidator<Ping>
    {
        public ValidationResult Validate(Ping instance) =>
            ValidationResult.Invalid(new ValidationFailure(nameof(Ping.Message), "Not allowed."));
    }

    private static Dispatcher BuildDispatcher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);

        var provider = services.BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<Dispatcher>(provider);
    }

    [Fact]
    public async Task Command_reaches_its_registered_handler()
    {
        var handler = new PingHandler();
        var dispatcher = BuildDispatcher(s =>
            s.AddSingleton<ICommandHandler<Ping, Result<string>>>(handler));

        var result = await dispatcher.SendAsync(new Ping("hello"));

        Assert.True(handler.WasCalled);
        Assert.True(result.IsSuccess);
        Assert.Equal("HELLO", result.Value);
    }

    [Fact]
    public async Task Query_reaches_its_registered_handler()
    {
        var dispatcher = BuildDispatcher(s =>
            s.AddSingleton<IQueryHandler<Ask, Result<string>>, AskHandler>());

        var result = await dispatcher.QueryAsync(new Ask("why"));

        Assert.Equal("answer:why", result.Value);
    }

    /// <summary>
    /// FR-008: a validation failure short-circuits before the handler runs.
    /// </summary>
    /// <remarks>
    /// Asserting on <c>WasCalled</c> rather than only on the thrown exception. An
    /// implementation that validated and then ran the handler anyway would still throw,
    /// and would still pass a test that only checked for the exception.
    /// </remarks>
    [Fact]
    public async Task Invalid_request_never_reaches_the_handler()
    {
        var handler = new PingHandler();
        var dispatcher = BuildDispatcher(s =>
        {
            s.AddSingleton<ICommandHandler<Ping, Result<string>>>(handler);
            s.AddSingleton<IValidator<Ping>, RejectEverything>();
        });

        var thrown = await Assert.ThrowsAsync<RequestValidationException>(
            () => dispatcher.SendAsync(new Ping("hello")));

        Assert.False(handler.WasCalled);
        Assert.Contains(
            thrown.Result.Failures,
            f => f.Field == nameof(Ping.Message) && f.Message == "Not allowed.");
    }

    /// <summary>A request with no registered validator is valid by default.</summary>
    [Fact]
    public async Task Request_without_a_validator_is_dispatched()
    {
        var handler = new PingHandler();
        var dispatcher = BuildDispatcher(s =>
            s.AddSingleton<ICommandHandler<Ping, Result<string>>>(handler));

        var result = await dispatcher.SendAsync(new Ping("no validator"));

        Assert.True(handler.WasCalled);
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A missing handler fails with a message that says how to fix it.
    /// </summary>
    /// <remarks>
    /// Forgetting to register a handler is the most common mistake when adding a feature
    /// slice, so the message names the entry point where the registration belongs rather
    /// than reporting a bare resolution failure.
    /// </remarks>
    [Fact]
    public async Task Missing_handler_reports_where_to_register_it()
    {
        var dispatcher = BuildDispatcher(_ => { });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new Ping("orphan")));

        Assert.Contains(nameof(Ping), thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Add<Module>Module()", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var dispatcher = BuildDispatcher(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => dispatcher.SendAsync<Result<string>>(null!));
    }
}
