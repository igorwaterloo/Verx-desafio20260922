using FluxoCaixa.Contracts;
using FluxoCaixa.Infrastructure.Common.Telemetria;
using FluxoCaixa.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Infrastructure.Common.Messaging;

/// <summary>
/// Define o tenant da execução a partir do evento consumido (MT-02, ADR-0015), no mesmo escopo
/// de DI do consumidor. Evento sem tenant é rejeitado e, após as retentativas, vai para a fila de erro.
/// O tenant também marca o span do consumo e os logs do processamento (ADR-0011).
/// </summary>
public sealed class TenantConsumeFilter<TMensagem>(TenantContext tenantContext, ILogger<TenantConsumeFilter<TMensagem>> logger)
    : IFilter<ConsumeContext<TMensagem>>
    where TMensagem : class
{
    public async Task Send(ConsumeContext<TMensagem> context, IPipe<ConsumeContext<TMensagem>> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Message is not IIntegrationEvent evento)
        {
            await next.Send(context);
            return;
        }

        if (evento.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Evento {typeof(TMensagem).Name} ({evento.EventId}) sem TenantId: mensagem rejeitada.");
        }

        tenantContext.Definir(evento.TenantId, usuarioId: null, roles: []);
        using var escopo = Observabilidade.MarcarTenant(logger, evento.TenantId);
        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context?.CreateFilterScope("tenant");
}
