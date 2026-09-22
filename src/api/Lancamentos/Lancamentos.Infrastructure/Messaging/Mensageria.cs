using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel.Cqrs;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Planos;
using MassTransit;

namespace Lancamentos.Infrastructure.Messaging;

/// <summary>
/// Publica via <see cref="IPublishEndpoint"/> do escopo: com o Bus Outbox do MassTransit, a mensagem é
/// gravada na tabela de outbox e só sai para o RabbitMQ após o commit (ADR-0005).
/// </summary>
internal sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvento>(TEvento evento, CancellationToken cancellationToken)
        where TEvento : class, IIntegrationEvent =>
        publishEndpoint.Publish(evento, cancellationToken);
}

/// <summary>Mantém a projeção local do plano (ADR-0017). O tenant já foi definido pelo TenantConsumeFilter.</summary>
public sealed class TenantProvisionadoConsumer(IDispatcher dispatcher) : IConsumer<TenantProvisionado>
{
    public Task Consume(ConsumeContext<TenantProvisionado> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;
        return PlanoConsumers.AtualizarAsync(dispatcher, m.PlanoCodigo, m.LimiteLancamentosMes, m.OcorridoEm, context.CancellationToken);
    }
}

public sealed class PlanoDoTenantAlteradoConsumer(IDispatcher dispatcher) : IConsumer<PlanoDoTenantAlterado>
{
    public Task Consume(ConsumeContext<PlanoDoTenantAlterado> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;
        return PlanoConsumers.AtualizarAsync(dispatcher, m.PlanoCodigo, m.LimiteLancamentosMes, m.OcorridoEm, context.CancellationToken);
    }
}

internal static class PlanoConsumers
{
    public static async Task AtualizarAsync(
        IDispatcher dispatcher, string planoCodigo, int limite, DateTimeOffset ocorridoEm, CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.SendAsync(new AtualizarPlanoDoTenantCommand(planoCodigo, limite, ocorridoEm), cancellationToken);
        if (resultado.IsFailure)
        {
            // Falha -> retentativas e, esgotadas, fila de erro (DLQ).
            throw new InvalidOperationException($"Falha ao atualizar o plano do tenant: {resultado.Error.Code}");
        }
    }
}
