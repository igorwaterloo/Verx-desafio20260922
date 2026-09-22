using Consolidado.Application;
using Consolidado.Infrastructure;
using Consolidado.Infrastructure.Persistence;
using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.Infrastructure.Common.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// Consolidado.Worker (ADR-0010): consome LancamentoRegistrado e mantém a projeção de saldos.
// Processo separado da Api para que a drenagem de backlog não dispute recursos com as consultas.
// Expõe apenas health checks HTTP (liveness/readiness) para o orquestrador.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenancy();
builder.Services.AddConsolidadoApplication();
builder.Services.AddConsolidadoPersistencia(builder.Configuration);
builder.Services.AddConsolidadoCache(builder.Configuration);
builder.Services.AddConsolidadoMensageria(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// O Worker é o escritor da projeção: é ele quem aplica as migrations do ConsolidadoDb.
if (app.Configuration.GetValue<bool>("Database:AplicarMigracoes"))
{
    await app.AplicarMigracoesAsync<ConsolidadoDbContext>();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

await app.RunAsync();
