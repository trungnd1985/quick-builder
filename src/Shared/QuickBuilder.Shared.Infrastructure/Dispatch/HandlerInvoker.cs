using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Shared.Infrastructure.Dispatch;

/// <summary>
/// Turns "call HandleAsync on this open-generic handler" into a cached compiled delegate.
/// </summary>
/// <remarks>
/// The dispatcher knows the request's runtime type but not its compile-time type, so some
/// late binding is unavoidable. Reflecting once per (handler type) pair and caching the
/// compiled delegate keeps the per-request cost to a dictionary lookup rather than a
/// reflective invoke.
/// </remarks>
internal static class HandlerInvoker
{
    private static readonly ConcurrentDictionary<Type, Delegate> HandleCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object, ValidationResult>> ValidateCache = new();

    internal static Task<TResult> InvokeAsync<TResult>(
        Type handlerType,
        object handler,
        object request,
        CancellationToken cancellationToken)
    {
        var invoke = (Func<object, object, CancellationToken, Task<TResult>>)HandleCache.GetOrAdd(
            handlerType,
            static type => BuildHandleDelegate<TResult>(type));

        return invoke(handler, request, cancellationToken);
    }

    internal static ValidationResult Validate(Type validatorType, object validator, object instance)
    {
        var invoke = ValidateCache.GetOrAdd(validatorType, static type => BuildValidateDelegate(type));
        return invoke(validator, instance);
    }

    private static Func<object, object, CancellationToken, Task<TResult>> BuildHandleDelegate<TResult>(Type handlerType)
    {
        var method = handlerType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{handlerType} has no HandleAsync method.");

        var requestType = method.GetParameters()[0].ParameterType;

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var tokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var call = Expression.Call(
            Expression.Convert(handlerParam, handlerType),
            method,
            Expression.Convert(requestParam, requestType),
            tokenParam);

        return Expression
            .Lambda<Func<object, object, CancellationToken, Task<TResult>>>(call, handlerParam, requestParam, tokenParam)
            .Compile();
    }

    private static Func<object, object, ValidationResult> BuildValidateDelegate(Type validatorType)
    {
        var method = validatorType.GetMethod("Validate", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{validatorType} has no Validate method.");

        var instanceType = method.GetParameters()[0].ParameterType;

        var validatorParam = Expression.Parameter(typeof(object), "validator");
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        var call = Expression.Call(
            Expression.Convert(validatorParam, validatorType),
            method,
            Expression.Convert(instanceParam, instanceType));

        return Expression
            .Lambda<Func<object, object, ValidationResult>>(call, validatorParam, instanceParam)
            .Compile();
    }
}
