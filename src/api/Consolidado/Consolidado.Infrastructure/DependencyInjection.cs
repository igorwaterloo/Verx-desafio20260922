using Consolidado.Application.Abstractions;
using Consolidado.Infrastructure.Cache;
using Consolidado.Infrastructure.Messaging;
using Consolidado.Infrastructure.Persistence;
using FluxoCaixa.Infrastructure.Common.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Consolidado.Infrastructure;

/// <summary>
/// Composição da infraestrutura em blocos: a Api usa persistência (leitura) + cache; o Worker usa
/// persistência (escrita) + cache (invalidação) + mensageria (ADR-0010).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddConsolidadoPersistencia(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Consolidado")
            ?? throw new InvalidOperationException("ConnectionStrings:Consolidado não configurada.");

        services.AddDbContext<ConsolidadoDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));

        services.AddScoped<ISaldoDiarioRepository, SaldoDiarioRepository>();
        services.AddScoped<IInbox, Inbox>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISaldosLeitura, SaldosLeitura>();

        services.AddHealthChecks().AddDbContextCheck<ConsolidadoDbContext>("sqlserver", tags: ["ready"]);
        return services;
    }

    public static IServiceCollection AddConsolidadoCache(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var opcoes = configuration.GetSection("Redis");
        var timeoutMs = opcoes.GetValue("TimeoutMs", defaultValue: 50);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configuracao = ConfigurationOptions.Parse(opcoes["Endereco"] ?? "localhost:6379");
            configuracao.AbortOnConnectFail = false; // sobe mesmo com o Redis fora e reconecta em segundo plano
            configuracao.ConnectTimeout = 2_000;
            configuracao.SyncTimeout = timeoutMs;
            configuracao.AsyncTimeout = timeoutMs;
            return ConnectionMultiplexer.Connect(configuracao);
        });
        services.AddSingleton<DisjuntorDoCache>();
        services.AddSingleton<ICacheConsolidado, RedisCacheConsolidado>();

        // Sem a tag "ready": Redis fora degrada a latência, mas não tira a réplica do balanceamento.
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["cache"]);
        return services;
    }

    public static IServiceCollection AddConsolidadoMensageria(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("consolidado", includeNamespace: false));
            bus.AddConsumer<LancamentoRegistradoConsumer>();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.ConfigurarPadrao(context, configuration);
                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
