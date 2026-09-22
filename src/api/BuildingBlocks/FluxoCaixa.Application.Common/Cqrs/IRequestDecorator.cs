using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;

namespace FluxoCaixa.Application.Common.Cqrs;

public delegate Task<Result<TResponse>> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Decorator do pipeline CQRS: executa uma preocupação transversal (validação, log, métricas)
/// e decide se chama <c>next</c>. Os decorators executam na ordem em que foram registrados.
/// </summary>
public interface IRequestDecorator<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
