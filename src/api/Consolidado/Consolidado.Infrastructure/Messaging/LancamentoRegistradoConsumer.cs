using Consolidado.Application.Saldos.Aplicar;
using Consolidado.Domain.Saldos;
using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel.Cqrs;
using MassTransit;

namespace Consolidado.Infrastructure.Messaging;

/// <summary>
/// Consome LancamentoRegistrado (fila <c>consolidado-lancamento-registrado</c> — ADR-0004).
/// O tenant já foi definido pelo TenantConsumeFilter a partir do evento. Falhas sobem para o
/// retry exponencial e, esgotadas as tentativas, a mensagem vai para a fila de erro (DLQ).
/// </summary>
public sealed class LancamentoRegistradoConsumer(IDispatcher dispatcher) : IConsumer<LancamentoRegistrado>
{
    public async Task Consume(ConsumeContext<LancamentoRegistrado> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var evento = context.Message;

        var tipo = evento.Tipo == TipoLancamento.Credito ? TipoMovimento.Credito : TipoMovimento.Debito;
        var resultado = await dispatcher.SendAsync(
            new AplicarLancamentoNoSaldoCommand(evento.EventId, tipo, evento.Valor, evento.DataCompetencia, evento.OcorridoEm),
            context.CancellationToken);

        if (resultado.IsFailure)
        {
            throw new InvalidOperationException($"Falha ao aplicar o evento {evento.EventId}: {resultado.Error.Code}");
        }
    }
}
