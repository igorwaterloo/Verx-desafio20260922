using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FluxoCaixa.Infrastructure.Common.Telemetria;

/// <summary>
/// OpenTelemetry padrão dos serviços (ADR-0011): traces, métricas e logs com o mesmo recurso
/// (<c>service.name</c>, instância, ambiente), exportados por OTLP quando
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> está configurado (Aspire Dashboard localmente; coletor em produção).
/// </summary>
public static class Observabilidade
{
    /// <summary>Atributo com o tenant nos spans e nos logs. Não entra em métricas (cardinalidade).</summary>
    public const string AtributoTenant = "tenant.id";

    /// <summary>
    /// Fontes de trace nativas (sem pacote de instrumentação). A fonte do YARP não é assinada: ela só
    /// acrescenta o span das sondas de health check (raiz a cada 2 s); o encaminhamento já aparece nos
    /// spans do ASP.NET Core e do HttpClient do gateway.
    /// </summary>
    public static readonly string[] FontesNativas = ["MassTransit"];

    public static TBuilder AddObservabilidade<TBuilder>(this TBuilder builder, string nomeDoServico, params string[] medidores)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(nomeDoServico);

        var versao = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso
                .AddService(nomeDoServico, serviceVersion: versao, serviceInstanceId: Environment.MachineName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .SetSampler(AmostragemPorOperacaoDeEntrada.Criar())
                .AddAspNetCoreInstrumentation(opcoes => opcoes.Filter = contexto => !EhHealthCheck(contexto.Request.Path))
                .AddHttpClientInstrumentation(opcoes =>
                    opcoes.FilterHttpRequestMessage = requisicao => !EhHealthCheck(requisicao.RequestUri?.AbsolutePath))
                .AddSqlClientInstrumentation()
                .AddSource(FontesNativas))
            .WithMetrics(metricas => metricas
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("MassTransit")
                .AddMeter(medidores));

        builder.Logging.AddOpenTelemetry(logs =>
        {
            logs.IncludeFormattedMessage = true;
            logs.IncludeScopes = true;
        });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>Marca o span atual e devolve um escopo de log com o tenant.</summary>
    public static IDisposable? MarcarTenant(ILogger logger, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Activity.Current?.SetTag(AtributoTenant, tenantId.ToString());
        return logger.BeginScope(new Dictionary<string, object> { [AtributoTenant] = tenantId.ToString() });
    }

    private static bool EhHealthCheck(PathString caminho) => caminho.StartsWithSegments("/health");

    private static bool EhHealthCheck(string? caminho) =>
        caminho is not null && caminho.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
}
