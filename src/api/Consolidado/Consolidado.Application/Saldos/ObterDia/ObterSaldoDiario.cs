using Consolidado.Application.Abstractions;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;

namespace Consolidado.Application.Saldos.ObterDia;

public sealed record ObterSaldoDiarioQuery(DateOnly Data) : IQuery<SaldoDiarioDto>;

/// <summary>Cache-aside (ADR-0006): cache → banco → cache. Dia sem movimento retorna zeros (RC-03).</summary>
public sealed class ObterSaldoDiarioHandler(
    ISaldosLeitura leitura,
    ICacheConsolidado cache,
    ITenantContext tenant,
    TimeProvider tempo) : IQueryHandler<ObterSaldoDiarioQuery, SaldoDiarioDto>
{
    public async Task<Result<SaldoDiarioDto>> HandleAsync(ObterSaldoDiarioQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var chave = ChavesDeCache.Dia(tenant.TenantId, query.Data);
        if (await cache.ObterAsync<SaldoDiarioDto>(chave, cancellationToken) is { } emCache)
        {
            return emCache;
        }

        var saldo = await leitura.ObterAsync(query.Data, cancellationToken) ?? SaldoDiarioDto.Vazio(query.Data);

        var hoje = Calendario.DataEmSaoPaulo(tempo.GetUtcNow());
        await cache.DefinirAsync(chave, saldo, PoliticaDeCache.TtlDia(query.Data, hoje), cancellationToken);

        return saldo;
    }
}
