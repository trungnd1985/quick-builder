namespace QuickBuilder.Shared.Kernel.Dispatch;

/// <summary>Marker for a write operation. Handled by exactly one handler.</summary>
public interface ICommand<TResult>;

/// <summary>Marker for a read operation. Handled by exactly one handler.</summary>
public interface IQuery<TResult>;

/// <summary>
/// Handles one command (FR-006). Registered in DI and resolved by the dispatcher.
/// </summary>
/// <remarks>
/// Constitution Principle I forbids MediatR and similar libraries, so this interface and
/// the dispatcher that resolves it are the whole mechanism. A handler owns its business
/// rules and uses its module's DbContext directly — no repository wrapper.
/// </remarks>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles one query (FR-006).</summary>
/// <remarks>
/// Read paths use AsNoTracking() and project to DTOs rather than returning entities —
/// see the Technology Constraints section of the constitution.
/// </remarks>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
