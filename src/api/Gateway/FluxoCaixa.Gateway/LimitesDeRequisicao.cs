using System.Globalization;
using System.Threading.RateLimiting;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FluxoCaixa.Gateway;

/// <summary>Nomes das políticas de rate limiting usadas nas rotas do YARP (appsettings: RateLimiterPolicy).</summary>
public static class PoliticasDeLimite
{
    /// <summary>Rotas autenticadas: token bucket por tenant, com a vazão do plano (claim <c>plano</c>).</summary>
    public const string PorTenant = "por-tenant";

    /// <summary>Cadastro público de empresas: janela fixa por IP, restritiva (anti-abuso).</summary>
    public const string Onboarding = "onboarding";

    /// <summary>Leituras públicas (catálogo de planos): janela fixa por IP.</summary>
    public const string Publico = "publico";
}

public sealed class OpcoesDeLimite
{
    /// <summary>Requisições por segundo por tenant, por plano (ADR-0017). Plano desconhecido usa o "free".</summary>
    public Dictionary<string, int> RequisicoesPorSegundo { get; set; } = new(StringComparer.Ordinal)
    {
        ["free"] = 20,
        ["pro"] = 100,
    };

    public int OnboardingPorMinutoPorIp { get; set; } = 10;

    public int PublicoPorMinutoPorIp { get; set; } = 120;

    public int VazaoDoPlano(string? plano) =>
        plano is not null && RequisicoesPorSegundo.TryGetValue(plano, out var rps)
            ? rps
            : RequisicoesPorSegundo.GetValueOrDefault("free", 20);
}

/// <summary>
/// Rate limiting do Gateway (ADR-0009/0015): cada tenant tem o próprio bucket, dimensionado pelo
/// plano — um tenant que excede o limite recebe 429 sem afetar os demais (RNF-04, SLO-10).
/// </summary>
public static class LimitesDeRequisicao
{
    public static IServiceCollection AddLimitesDeRequisicao(this IServiceCollection services, IConfiguration configuration)
    {
        var opcoes = configuration.GetSection("LimitesDeRequisicao").Get<OpcoesDeLimite>() ?? new OpcoesDeLimite();
        services.AddSingleton(opcoes);
        services.AddSingleton<GatewayMetricas>();

        services.AddRateLimiter(limites =>
        {
            limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limites.OnRejected = ResponderLimiteExcedidoAsync;

            limites.AddPolicy(PoliticasDeLimite.PorTenant, http =>
            {
                var tenant = http.User.FindFirst(TenantClaims.TenantId)?.Value;
                if (tenant is null)
                {
                    // Sem tenant (anônimo ou token inválido): limita por IP; a autorização responderá 401/403.
                    return JanelaPorIp(http, "sem-tenant", opcoes.PublicoPorMinutoPorIp);
                }

                var plano = http.User.FindFirst(TenantClaims.Plano)?.Value;
                var rps = opcoes.VazaoDoPlano(plano);
                return RateLimitPartition.GetTokenBucketLimiter($"tenant:{tenant}:{plano}", _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = rps,
                    TokensPerPeriod = rps,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            limites.AddPolicy(PoliticasDeLimite.Onboarding, http => JanelaPorIp(http, "onboarding", opcoes.OnboardingPorMinutoPorIp));
            limites.AddPolicy(PoliticasDeLimite.Publico, http => JanelaPorIp(http, "publico", opcoes.PublicoPorMinutoPorIp));
        });

        return services;
    }

    private static RateLimitPartition<string> JanelaPorIp(HttpContext http, string prefixo, int permitidasPorMinuto) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{prefixo}:{http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitidasPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });

    private static async ValueTask ResponderLimiteExcedidoAsync(OnRejectedContext contexto, CancellationToken cancellationToken)
    {
        var http = contexto.HttpContext;
        var plano = http.User.FindFirst(TenantClaims.Plano)?.Value;
        var comTenant = http.User.FindFirst(TenantClaims.TenantId) is not null;
        http.RequestServices.GetRequiredService<GatewayMetricas>()
            .LimiteExcedido(comTenant ? "tenant" : "ip", comTenant ? plano ?? "desconhecido" : "anonimo");

        var resposta = http.Response;
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var aguardar))
        {
            resposta.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(aguardar.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Limite de requisições excedido.",
            Detail = "O limite de requisições do plano foi atingido. Aguarde e tente novamente.",
            Type = "https://httpstatuses.io/429",
            Instance = contexto.HttpContext.Request.Path,
        };
        problema.Extensions["codigo"] = "gateway.limite_de_requisicoes";

        await resposta.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
