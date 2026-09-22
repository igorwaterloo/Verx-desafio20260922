using System.Collections.Concurrent;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using Microsoft.Extensions.DependencyInjection;

namespace FluxoCaixa.Application.Common.Cqrs;

/// <summary>
/// Implementação própria do mediator (ADR-0013): resolve o handler pelo tipo concreto da requisição
/// e monta a cadeia de decorators. Os wrappers genéricos são cacheados por tipo, evitando
/// reflexão a cada chamada.
/// </summary>
internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result<TResponse>> SendAsync<TResponse>(
        ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (RequestWrapper<TResponse>)Wrappers.GetOrAdd(
            command.GetType(),
            static (tipo, resposta) => Activator.CreateInstance(
                typeof(CommandWrapper<,>).MakeGenericType(tipo, resposta))!,
            typeof(TResponse));

        return wrapper.HandleAsync(command, serviceProvider, cancellationToken);
    }

    public Task<Result<TResponse>> QueryAsync<TResponse>(
        IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (RequestWrapper<TResponse>)Wrappers.GetOrAdd(
            query.GetType(),
            static (tipo, resposta) => Activator.CreateInstance(
                typeof(QueryWrapper<,>).MakeGenericType(tipo, resposta))!,
            typeof(TResponse));

        return wrapper.HandleAsync(query, serviceProvider, cancellationToken);
    }
}

internal abstract class RequestWrapper<TResponse>
{
    public abstract Task<Result<TResponse>> HandleAsync(
        object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    protected static Task<Result<TResponse>> ExecutarPipeline<TRequest>(
        TRequest request,
        IServiceProvider serviceProvider,
        RequestHandlerDelegate<TResponse> handler,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // O primeiro decorator registrado fica mais externo na cadeia.
        var pipeline = serviceProvider
            .GetServices<IRequestDecorator<TRequest, TResponse>>()
            .Reverse()
            .Aggregate(handler, (next, decorator) => () => decorator.HandleAsync(request, next, cancellationToken));

        return pipeline();
    }

    protected static InvalidOperationException HandlerNaoRegistrado(Type tipoRequisicao) =>
        new($"Nenhum handler registrado para '{tipoRequisicao.Name}'. Verifique o registro em AddCqrs.");
}

internal sealed class CommandWrapper<TCommand, TResponse> : RequestWrapper<TResponse>
    where TCommand : ICommand<TResponse>
{
    public override Task<Result<TResponse>> HandleAsync(
        object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var command = (TCommand)request;
        var handler = serviceProvider.GetService<ICommandHandler<TCommand, TResponse>>()
            ?? throw HandlerNaoRegistrado(typeof(TCommand));

        return ExecutarPipeline(command, serviceProvider, () => handler.HandleAsync(command, cancellationToken), cancellationToken);
    }
}

internal sealed class QueryWrapper<TQuery, TResponse> : RequestWrapper<TResponse>
    where TQuery : IQuery<TResponse>
{
    public override Task<Result<TResponse>> HandleAsync(
        object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var query = (TQuery)request;
        var handler = serviceProvider.GetService<IQueryHandler<TQuery, TResponse>>()
            ?? throw HandlerNaoRegistrado(typeof(TQuery));

        return ExecutarPipeline(query, serviceProvider, () => handler.HandleAsync(query, cancellationToken), cancellationToken);
    }
}
