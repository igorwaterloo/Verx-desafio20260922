using MassTransit;
using Microsoft.Extensions.Configuration;

namespace FluxoCaixa.Infrastructure.Common.Messaging;

public static class RabbitMqConfiguracao
{
    /// <summary>
    /// Configuração comum dos barramentos (ADR-0004): host a partir da seção <c>RabbitMq</c>,
    /// tenant definido pelo evento em cada consumo e retry exponencial antes da fila de erro (DLQ).
    /// </summary>
    public static void ConfigurarPadrao(
        this IRabbitMqBusFactoryConfigurator rabbit,
        IBusRegistrationContext contexto,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(rabbit);
        ArgumentNullException.ThrowIfNull(configuration);

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

        rabbit.UseConsumeFilter(typeof(TenantConsumeFilter<>), contexto);
        rabbit.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}
