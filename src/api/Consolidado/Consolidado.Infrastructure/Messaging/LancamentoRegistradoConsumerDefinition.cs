using FluxoCaixa.Contracts;
using MassTransit;

namespace Consolidado.Infrastructure.Messaging;

/// <summary>
/// Particiona o consumo por <c>tenant + data de competência</c> (ADR-0010): eventos que atualizam a
/// mesma linha de <c>SaldoDiario</c> são aplicados em série, e linhas diferentes (outros tenants ou
/// dias) seguem em paralelo. Sem isso, consumidores concorrentes na mesma linha disputavam a
/// concorrência otimista e caíam no retry exponencial — atraso de segundos no pico e risco de DLQ.
/// </summary>
public sealed class LancamentoRegistradoConsumerDefinition : ConsumerDefinition<LancamentoRegistradoConsumer>
{
    /// <summary>Linhas de saldo processadas em paralelo por instância do worker.</summary>
    public const int Particoes = 16;

    public LancamentoRegistradoConsumerDefinition()
    {
        ConcurrentMessageLimit = Particoes;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<LancamentoRegistradoConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(endpointConfigurator);
        ArgumentNullException.ThrowIfNull(consumerConfigurator);

        endpointConfigurator.PrefetchCount = Particoes * 4;
        var particionador = endpointConfigurator.CreatePartitioner(Particoes);
        consumerConfigurator.Message<LancamentoRegistrado>(mensagem =>
            mensagem.UsePartitioner(particionador, consumo => ChaveDaLinha(consumo.Message)));
    }

    /// <summary>Chave da linha de saldo afetada pelo evento.</summary>
    public static string ChaveDaLinha(LancamentoRegistrado evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        return $"{evento.TenantId:N}:{evento.DataCompetencia.DayNumber}";
    }
}
