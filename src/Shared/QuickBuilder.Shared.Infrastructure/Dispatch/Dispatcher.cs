using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Shared.Infrastructure.Dispatch;

/// <summary>
/// Resolves and runs the handler for a command or query (FR-006).
/// </summary>
/// <remarks>
/// Controllers depend on this and nothing else, which is what keeps them free of business
/// rules and data access (FR-007).
/// </remarks>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeAsync<TResult>(command, typeof(ICommandHandler<,>), cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeAsync<TResult>(query, typeof(IQueryHandler<,>), cancellationToken);
    }

    private Task<TResult> InvokeAsync<TResult>(
        object request,
        Type openHandlerType,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();

        // FR-008: validation runs first and short-circuits, so the handler is never even
        // constructed for an invalid request.
        //
        // Why an exception rather than a failure Result: TResult is arbitrary, so the
        // dispatcher cannot generically manufacture "a failed TResult". A dedicated
        // exception type keeps validation distinguishable from an unexpected failure
        // (FR-008) because the host maps it to 400 ValidationProblem while everything
        // else becomes a 500 revealing nothing (FR-034). FR-009's "map results without
        // inspecting exceptions" governs *handler* outcomes — expected business failures
        // such as a taken host name — and those still travel as Result/Error.
        var validation = ValidateRequest(requestType, request);
        if (!validation.IsValid)
        {
            throw new RequestValidationException(validation);
        }

        var handlerType = openHandlerType.MakeGenericType(requestType, typeof(TResult));
        var handler = services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for {requestType.Name}. Register it in the owning " +
                "module's Add<Module>Module() entry point.");

        return HandlerInvoker.InvokeAsync<TResult>(handlerType, handler, request, cancellationToken);
    }

    private ValidationResult ValidateRequest(Type requestType, object request)
    {
        // A request with no registered validator is valid by default — validators are
        // opt-in per feature slice.
        var validatorType = typeof(IValidator<>).MakeGenericType(requestType);
        var validator = services.GetService(validatorType);

        return validator is null
            ? ValidationResult.Valid()
            : HandlerInvoker.Validate(validatorType, validator, request);
    }
}

/// <summary>
/// Thrown by the dispatcher when a request fails validation, before any handler runs.
/// </summary>
/// <remarks>
/// The host translates this into a 400 with per-field messages. It is deliberately a
/// distinct type so that it can never be confused with an unexpected failure.
/// </remarks>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(ValidationResult result)
        : base("The request failed validation.")
        => Result = result;

    public RequestValidationException() : base("The request failed validation.")
        => Result = ValidationResult.Valid();

    public RequestValidationException(string message) : base(message)
        => Result = ValidationResult.Valid();

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
        => Result = ValidationResult.Valid();

    public ValidationResult Result { get; }
}
