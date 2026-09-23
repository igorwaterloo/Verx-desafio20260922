using FluxoCaixa.Gateway;
using FluxoCaixa.Infrastructure.Common.Telemetria;
using FluxoCaixa.Infrastructure.Common.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Health;

// API Gateway (ADR-0009): entrada única da SPA. Roteia por prefixo, valida o JWT (defesa em
// profundidade — os serviços validam de novo), aplica rate limiting por tenant/plano, CORS e headers
// de segurança, e balanceia as réplicas do Consolidado com health checks ativo e passivo.
var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("gateway", GatewayMetricas.NomeDoMedidor);

var tamanhoMaximoDoCorpo = builder.Configuration.GetValue("Gateway:TamanhoMaximoDoCorpoBytes", 1_048_576L);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = tamanhoMaximoDoCorpo;
});

builder.Services.AddProblemDetails();
builder.Services.AddAutenticacaoKeycloak(builder.Configuration);
builder.Services.AddLimitesDeRequisicao(builder.Configuration);
builder.Services.AddCorsDaSpa(builder.Configuration);
builder.Services.AddHealthChecks();

// Timeout de conexão curto: uma réplica que saiu da rede (container parado, nó perdido) não recusa a
// conexão — ela fica pendurada até o ActivityTimeout. Falhando em ~1 s, a leitura é reenviada à outra
// réplica e o health check passivo retira a réplica do balanceamento (encontrado no teste de caos, Fase 8).
var tempoLimiteDeConexao = builder.Configuration.GetValue("Gateway:TempoLimiteDeConexao", TimeSpan.FromSeconds(1));

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .ConfigureHttpClient((_, handler) => handler.ConnectTimeout = tempoLimiteDeConexao);

// Health check passivo: a taxa de falhas de transporte (conexão recusada, timeout) numa janela curta
// retira a réplica do balanceamento; ela volta após o período de reativação ou pelo health check ativo.
// Como uma réplica que acabou de atender muito tráfego demora a cruzar a taxa, as leituras também são
// reenviadas a outra réplica (RetentativaEmOutraReplica) — o cliente não vê a falha.
builder.Services.Configure<TransportFailureRateHealthPolicyOptions>(opcoes =>
{
    opcoes.DetectionWindowSize = TimeSpan.FromSeconds(10);
    opcoes.MinimalTotalCountThreshold = 1;
    opcoes.DefaultFailureRateLimit = 0.3;
});

var app = builder.Build();

app.UseCabecalhosDeSeguranca(tamanhoMaximoDoCorpo);
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRouting();
app.UseCors(Seguranca.PoliticaCors);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapReverseProxy(proxy =>
{
    // Retentativa de leituras em outra réplica antes do balanceamento; em seguida, o pipeline padrão.
    proxy.Use(RetentativaEmOutraReplica.ExecutarAsync);
    proxy.UseSessionAffinity();
    proxy.UseLoadBalancing();
    proxy.UsePassiveHealthChecks();
});

await app.RunAsync();

/// <summary>
/// Exposto para testes de integração (WebApplicationFactory).
/// </summary>
public partial class Program;
