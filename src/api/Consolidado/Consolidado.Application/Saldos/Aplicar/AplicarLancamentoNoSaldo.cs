using Consolidado.Application.Abstractions;
using Consolidado.Application.Telemetria;
using Consolidado.Domain.Saldos;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;

namespace Consolidado.Application.Saldos.Aplicar;

/// <summary>Disparado pelo evento LancamentoRegistrado (inclusive estornos, com o tipo inverso).</summary>
public sealed record AplicarLancamentoNoSaldoCommand(
    Guid EventId,
    TipoMovimento Tipo,
    decimal Valor,
    DateOnly DataCompetencia,
    DateTimeOffset OcorridoEm) : ICommand<Unit>;

/// <summary>
/// Consumidor idempotente (ADR-0005): verifica a inbox, aplica no saldo do dia e grava saldo +
/// inbox na mesma transação. Conflitos de concorrência sobem para o retry do consumidor, que
/// reprocessa em um novo escopo (a inbox torna a reaplicação segura). O cache do dia é
/// invalidado somente após o commit.
/// </summary>
public sealed class AplicarLancamentoNoSaldoHandler(
    ISaldoDiarioRepository saldos,
    IInbox inbox,
    IUnitOfWork unitOfWork,
    ICacheConsolidado cache,
    ITenantContext tenant,
    TimeProvider tempo,
    ConsolidadoMetricas metricas) : ICommandHandler<AplicarLancamentoNoSaldoCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AplicarLancamentoNoSaldoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await inbox.JaProcessadaAsync(command.EventId, cancellationToken))
        {
            metricas.EventoDuplicado();
            return Unit.Value;
        }

        var saldo = await saldos.ObterAsync(command.DataCompetencia, cancellationToken);
        if (saldo is null)
        {
            saldo = SaldoDiario.Novo(tenant.TenantId, command.DataCompetencia);
            saldos.Adicionar(saldo);
        }

        saldo.Aplicar(command.Tipo, command.Valor, command.OcorridoEm);
        var agora = tempo.GetUtcNow();
        inbox.Registrar(command.EventId, agora);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        metricas.EventoAplicado(agora - command.OcorridoEm);
        await cache.RemoverAsync(ChavesDeCache.Dia(tenant.TenantId, command.DataCompetencia), cancellationToken);

        return Unit.Value;
    }
}
