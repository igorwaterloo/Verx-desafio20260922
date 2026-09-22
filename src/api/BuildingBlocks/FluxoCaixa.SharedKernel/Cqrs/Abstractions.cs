namespace FluxoCaixa.SharedKernel.Cqrs;

/// <summary>
/// Requisição da camada de aplicação que produz <typeparamref name="TResponse"/>.
/// Base comum de commands e queries (ADR-0003).
/// </summary>
#pragma warning disable CA1040 // Interfaces marcadoras são intencionais: tipam o pipeline CQRS.
public interface IRequest<TResponse>;

/// <summary>Intenção de alterar o estado do sistema.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>Leitura sem efeitos colaterais.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;
#pragma warning restore CA1040

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Ponto de entrada da camada de aplicação: encaminha a requisição ao handler, passando pelos decorators.
/// </summary>
public interface IDispatcher
{
    Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}

/// <summary>Resposta vazia para commands que não retornam valor.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
