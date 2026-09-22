using FluxoCaixa.Infrastructure.Common.Messaging;
using Lancamentos.Application.Abstractions;
using Lancamentos.Infrastructure.Messaging;
using Lancamentos.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lancamentos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLancamentosInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Lancamentos")
            ?? throw new InvalidOperationException("ConnectionStrings:Lancamentos não configurada.");

        services.AddDbContext<LancamentosDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));

        services.AddScoped<ILancamentoRepository, LancamentoRepository>();
        services.AddScoped<ITenantPlanoRepository, TenantPlanoRepository>();
        services.AddScoped<IIdempotenciaRepository, IdempotenciaRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILancamentosLeitura, LancamentosLeitura>();
        services.AddScoped<IIntegrationEventPublisher, MassTransitEventPublisher>();

        services.AddHealthChecks().AddDbContextCheck<LancamentosDbContext>("sqlserver", tags: ["ready"]);

        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("lancamentos", includeNamespace: false));
            bus.AddConsumer<TenantProvisionadoConsumer>();
            bus.AddConsumer<PlanoDoTenantAlteradoConsumer>();

            // Transactional Outbox (ADR-0005): o evento é salvo na mesma transação do lançamento.
            bus.AddEntityFrameworkOutbox<LancamentosDbContext>(outbox =>
            {
                outbox.UseSqlServer();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });

            bus.UsingRabbitMq((context, rabbit) =>
            {
                var opcoes = configuration.GetSection("RabbitMq");
                rabbit.Host(
                    opcoes["Host"] ?? "localhost",
                    ushort.TryParse(opcoes["Porta"], out var porta) ? porta : (ushort)5672,
                    opcoes["VirtualHost"] ?? "/",
                    host =>
                    {
                        host.Username(opcoes["Usuario"] ?? "guest");
                        host.Password(opcoes["Senha"] ?? "guest");
                    });

                rabbit.UseConsumeFilter(typeof(TenantConsumeFilter<>), context);
                rabbit.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
