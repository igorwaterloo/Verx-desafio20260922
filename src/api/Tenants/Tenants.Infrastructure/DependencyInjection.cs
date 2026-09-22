using FluxoCaixa.Infrastructure.Common.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Tenants.Application.Abstractions;
using Tenants.Infrastructure.Identidade;
using Tenants.Infrastructure.Persistence;

namespace Tenants.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTenantsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Tenants")
            ?? throw new InvalidOperationException("ConnectionStrings:Tenants não configurada.");

        services.AddDbContext<TenantsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IIntegrationEventPublisher, MassTransitEventPublisher>();
        services.AddHealthChecks().AddDbContextCheck<TenantsDbContext>("sqlserver", tags: ["ready"]);

        AddKeycloak(services, configuration);

        services.AddMassTransit(bus =>
        {
            bus.AddEntityFrameworkOutbox<TenantsDbContext>(outbox =>
            {
                outbox.UseSqlServer();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });
            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.ConfigurarPadrao(context, configuration);
                rabbit.ConfigureEndpoints(context);
            });
        });

        services.AddHostedService<ExpiracaoDePendentesService>();
        return services;
    }

    private static void AddKeycloak(IServiceCollection services, IConfiguration configuration)
    {
        var opcoes = configuration.GetSection("Keycloak").Get<KeycloakOpcoes>() ?? new KeycloakOpcoes();
        services.AddSingleton(opcoes);
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(KeycloakTokenHandler.ClienteDeToken, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddTransient<KeycloakTokenHandler>();

        services
            .AddHttpClient<IProvedorDeIdentidade, KeycloakProvedorDeIdentidade>(c =>
                c.BaseAddress = new Uri($"{opcoes.Url.TrimEnd('/')}/admin/realms/{opcoes.Realm}/"))
            .AddHttpMessageHandler<KeycloakTokenHandler>()
            // Resiliência: timeouts curtos e retry com backoff; esgotados, o onboarding responde 503 e é retomável.
            .AddStandardResilienceHandler(resiliencia =>
            {
                resiliencia.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                resiliencia.Retry.MaxRetryAttempts = 2;
                resiliencia.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
                resiliencia.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });
    }
}
