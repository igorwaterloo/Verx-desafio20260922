using FluxoCaixa.Infrastructure.Common.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebApiDefaults();

var app = builder.Build();

app.UseWebApiDefaults();

app.Run();

/// <summary>
/// Exposto para testes de integração (WebApplicationFactory).
/// </summary>
public partial class Program;
