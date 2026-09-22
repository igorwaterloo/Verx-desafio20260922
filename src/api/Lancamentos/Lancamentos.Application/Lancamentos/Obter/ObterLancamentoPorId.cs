using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using Lancamentos.Application.Abstractions;
using Lancamentos.Domain.Lancamentos;

namespace Lancamentos.Application.Lancamentos.Obter;

public sealed record ObterLancamentoPorIdQuery(Guid Id) : IQuery<LancamentoDto>;

public sealed class ObterLancamentoPorIdHandler(ILancamentosLeitura leitura)
    : IQueryHandler<ObterLancamentoPorIdQuery, LancamentoDto>
{
    public async Task<Result<LancamentoDto>> HandleAsync(ObterLancamentoPorIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await leitura.ObterPorIdAsync(query.Id, cancellationToken) is { } dto
            ? dto
            : LancamentoErros.NaoEncontrado;
    }
}
