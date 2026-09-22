using Consolidado.Application;
using Consolidado.Infrastructure;
using FluxoCaixa.Infrastructure.Common.Web;

// Consolidado.Api (ADR-0010): somente leitura, com cache-aside no Redis. Executa com 2+ réplicas.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebApiDefaults(builder.Configuration);
builder.Services.AddConsolidadoApplication();
builder.Services.AddConsolidadoPersistencia(builder.Configuration);
builder.Services.AddConsolidadoCache(builder.Configuration);

var app = builder.Build();

app.UseWebApiDefaults();

await app.RunAsync();

/// <summary>
/// Exposto para testes de integração (WebApplicationFactory).
/// </summary>
public partial class Program;
