using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;

namespace FluxoCaixa.Application.Common.Cqrs;

/// <summary>
/// Executa os validadores FluentValidation da requisição. Com falhas, retorna
/// <see cref="ValidationError"/> sem chamar o handler.
/// </summary>
public sealed class ValidationDecorator<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IRequestDecorator<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var contexto = new ValidationContext<TRequest>(request);
        var falhas = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in validators)
        {
            var resultado = await validator.ValidateAsync(contexto, cancellationToken);
            falhas.AddRange(resultado.Errors);
        }

        if (falhas.Count == 0)
        {
            return await next();
        }

        var erros = falhas
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        return Result.Failure<TResponse>(new ValidationError(erros));
    }
}
