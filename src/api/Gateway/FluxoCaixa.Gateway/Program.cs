using FluxoCaixa.Gateway;
using FluxoCaixa.Infrastructure.Common.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Health;

// API Gateway (ADR-0009): entrada única da SPA. Roteia por prefixo, valida o JWT (defesa em
// profundidade — os serviços validam de novo), aplica rate limiting por tenant/plano, CORS e headers
// de segurança, e balanceia as réplicas do Consolidado com health checks ativo e passivo.
var builder = WebApplication.CreateBuilder(args);

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

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Health check passivo: uma falha de transporte (conexão recusada, timeout) tira a réplica do
// balanceamento imediatamente; ela volta após o período de reativação ou pelo health check ativo.
builder.Services.Configure<TransportFailureRateHealthPolicyOptions>(opcoes =>
{
    opcoes.DetectionWindowSize = TimeSpan.FromSeconds(30);
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
app.MapReverseProxy();

await app.RunAsync();

/// <summary>
/// Exposto para testes de integração (WebApplicationFactory).
/// </summary>
public partial class Program;
