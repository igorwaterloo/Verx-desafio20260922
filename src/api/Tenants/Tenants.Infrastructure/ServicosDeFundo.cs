using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel.Cqrs;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tenants.Application.Abstractions;
using Tenants.Application.Tenants.Expirar;
using Tenants.Domain.Tenants;

namespace Tenants.Infrastructure;

/// <summary>Publica via Bus Outbox do MassTransit: a mensagem sai só após o commit (ADR-0005).</summary>
internal sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvento>(TEvento evento, CancellationToken cancellationToken)
        where TEvento : class, IIntegrationEvent =>
        publishEndpoint.Publish(evento, cancellationToken);
}

/// <summary>Compensa periodicamente os onboardings abandonados (Pendente há mais de 24 h).</summary>
internal sealed partial class ExpiracaoDePendentesService(IServiceScopeFactory scopes, ILogger<ExpiracaoDePendentesService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IdadeMaxima = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var resultado = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                    .SendAsync(new ExpirarProvisionamentosPendentesCommand(IdadeMaxima), stoppingToken);
                if (resultado is { IsSuccess: true, Value: > 0 })
                {
                    LogExpirados(logger, resultado.Value);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFalha(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Quantidade} onboarding(s) pendente(s) expirado(s) e compensado(s).")]
    private static partial void LogExpirados(ILogger logger, int quantidade);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao expirar onboardings pendentes.")]
    private static partial void LogFalha(ILogger logger, Exception ex);
}

/// <summary>
/// Registra no TenantsDb os tenants de demonstração que já existem no realm local (Organizations
/// importadas) e publica TenantProvisionado, para que o Lançamentos aplique a quota do plano correto.
/// Idempotente: só atua sobre tenants ainda não cadastrados. Somente para ambiente local.
/// </summary>
public static partial class SemeaduraDeDemonstracao
{
    private static readonly (Guid Id, string Nome, string Cnpj, string Plano, string Email)[] TenantsDemo =
    [
        (Guid.Parse("0192f79e-0001-7000-8000-000000000001"), "Padaria Demo", "11444777000161", "free", "admin@padaria.demo"),
        (Guid.Parse("0192f79e-0002-7000-8000-000000000002"), "Mercado Demo", "45208395000150", "pro", "admin@mercado.demo"),
    ];

    public static async Task SemearTenantsDeDemonstracaoAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("Demonstracao:SemearTenants"))
        {
            return;
        }

        await using var scope = host.Services.CreateAsyncScope();
        var servicos = scope.ServiceProvider;
        var logger = servicos.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SemeaduraDeDemonstracao));
        var tenants = servicos.GetRequiredService<ITenantRepository>();
        var identidade = servicos.GetRequiredService<IProvedorDeIdentidade>();
        var publicador = servicos.GetRequiredService<IIntegrationEventPublisher>();
        var agora = servicos.GetRequiredService<TimeProvider>().GetUtcNow();
        var semeados = 0;

        try
        {
            foreach (var demo in TenantsDemo)
            {
                if (await tenants.ObterPorIdAsync(demo.Id, cancellationToken) is not null)
                {
                    continue;
                }

                var organizationId = await identidade.GarantirOrganizacaoAsync(demo.Id, demo.Nome, demo.Plano, cancellationToken);
                var tenant = Tenant.Importar(demo.Id, demo.Nome, demo.Cnpj, demo.Plano, organizationId, demo.Email, agora);
                tenants.Adicionar(tenant);
                await publicador.PublishAsync(
                    new TenantProvisionado(Guid.CreateVersion7(agora), agora, tenant.Id, tenant.Plano.Codigo, tenant.Plano.LimiteLancamentosMes, tenant.Plano.LimiteUsuarios),
                    cancellationToken);
                semeados++;
            }

            if (semeados > 0)
            {
                await servicos.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
                LogSemeados(logger, semeados);
            }
        }
        catch (ProvedorDeIdentidadeIndisponivelException ex)
        {
            LogSemearAdiado(logger, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Quantidade} tenant(s) de demonstração semeado(s).")]
    private static partial void LogSemeados(ILogger logger, int quantidade);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Semeadura de demonstração adiada: {Motivo}")]
    private static partial void LogSemearAdiado(ILogger logger, string motivo);
}
