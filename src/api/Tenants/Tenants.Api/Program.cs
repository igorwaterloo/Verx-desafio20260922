using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.Infrastructure.Common.Telemetria;
using FluxoCaixa.Infrastructure.Common.Web;
using Tenants.Application;
using Tenants.Infrastructure;
using Tenants.Infrastructure.Persistence;

// Tenants.Api (contexto Plataforma — ADR-0017): onboarding, planos e usuários do tenant.
var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("tenants-api");

builder.Services.AddWebApiDefaults(builder.Configuration);
builder.Services.AddTenantsApplication();
builder.Services.AddTenantsInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:AplicarMigracoes"))
{
    await app.AplicarMigracoesAsync<TenantsDbContext>();
}

await app.SemearTenantsDeDemonstracaoAsync();

app.UseWebApiDefaults();

await app.RunAsync();

/// <summary>
/// Exposto para testes de integração (WebApplicationFactory).
/// </summary>
public partial class Program;
