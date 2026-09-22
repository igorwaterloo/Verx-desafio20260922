using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel.Tenancy;
using MassTransit;

namespace FluxoCaixa.Infrastructure.Common.Messaging;

/// <summary>
/// Define o tenant da execução a partir do evento consumido (MT-02, ADR-0015), no mesmo escopo
/// de DI do consumidor. Evento sem tenant é rejeitado e, após as retentativas, vai para a fila de erro.
/// </summary>
public sealed class TenantConsumeFilter<TMensagem>(TenantContext tenantContext) : IFilter<ConsumeContext<TMensagem>>
    where TMensagem : class
{
    public Task Send(ConsumeContext<TMensagem> context, IPipe<ConsumeContext<TMensagem>> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Message is IIntegrationEvent evento)
        {
            if (evento.TenantId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Evento {typeof(TMensagem).Name} ({evento.EventId}) sem TenantId: mensagem rejeitada.");
            }

            tenantContext.Definir(evento.TenantId, usuarioId: null, roles: []);
        }

        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context?.CreateFilterScope("tenant");
}
