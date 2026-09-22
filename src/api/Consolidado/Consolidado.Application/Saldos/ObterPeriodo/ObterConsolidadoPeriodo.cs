using Consolidado.Application.Abstractions;
using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;

namespace Consolidado.Application.Saldos.ObterPeriodo;

public sealed record ObterConsolidadoPeriodoQuery(DateOnly Inicio, DateOnly Fim) : IQuery<ConsolidadoPeriodoDto>;

public sealed class ObterConsolidadoPeriodoValidator : AbstractValidator<ObterConsolidadoPeriodoQuery>
{
    public const int DiasMaximo = 93;

    public ObterConsolidadoPeriodoValidator()
    {
        RuleFor(q => q.Inicio).NotEmpty().WithMessage("A data inicial é obrigatória.");
        RuleFor(q => q.Fim)
            .NotEmpty().WithMessage("A data final é obrigatória.")
            .GreaterThanOrEqualTo(q => q.Inicio).WithMessage("A data final deve ser igual ou posterior à inicial.")
            .Must((q, fim) => fim.DayNumber - q.Inicio.DayNumber < DiasMaximo)
            .WithMessage($"O período deve ter no máximo {DiasMaximo} dias.");
    }
}

/// <summary>Consolidado do período com todos os dias (sem movimento = zeros) e os totais.</summary>
public sealed class ObterConsolidadoPeriodoHandler(
    ISaldosLeitura leitura,
    ICacheConsolidado cache,
    ITenantContext tenant) : IQueryHandler<ObterConsolidadoPeriodoQuery, ConsolidadoPeriodoDto>
{
    public async Task<Result<ConsolidadoPeriodoDto>> HandleAsync(ObterConsolidadoPeriodoQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var chave = ChavesDeCache.Periodo(tenant.TenantId, query.Inicio, query.Fim);
        if (await cache.ObterAsync<ConsolidadoPeriodoDto>(chave, cancellationToken) is { } emCache)
        {
            return emCache;
        }

        var comMovimento = (await leitura.ListarPeriodoAsync(query.Inicio, query.Fim, cancellationToken))
            .ToDictionary(d => d.Data);

        var dias = Enumerable.Range(0, query.Fim.DayNumber - query.Inicio.DayNumber + 1)
            .Select(query.Inicio.AddDays)
            .Select(data => comMovimento.GetValueOrDefault(data) ?? SaldoDiarioDto.Vazio(data))
            .ToList();

        var creditos = dias.Sum(d => d.TotalCreditos);
        var debitos = dias.Sum(d => d.TotalDebitos);
        var periodo = new ConsolidadoPeriodoDto(
            query.Inicio, query.Fim, creditos, debitos, creditos - debitos, dias.Sum(d => d.QuantidadeLancamentos), dias);

        await cache.DefinirAsync(chave, periodo, PoliticaDeCache.TtlPeriodo, cancellationToken);
        return periodo;
    }
}
