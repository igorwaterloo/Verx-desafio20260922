using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.Infrastructure.Common.Web;
using Lancamentos.Application;
using Lancamentos.Infrastructure;
using Lancamentos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebApiDefaults(builder.Configuration);
builder.Services.AddLancamentosApplication();
builder.Services.AddLancamentosInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:AplicarMigracoes"))
{
    await app.AplicarMigracoesAsync<LancamentosDbContext>();
}

app.UseWebApiDefaults();

await app.RunAsync();

/// <summary>
/// Exposto para testes de integração (WebApplicationFactory).
/// </summary>
public partial class Program;
