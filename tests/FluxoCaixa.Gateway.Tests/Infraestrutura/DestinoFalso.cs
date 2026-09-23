using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Gateway.Tests.Infraestrutura;

/// <summary>
/// Serviço de destino falso (Kestrel em porta real): ecoa o que recebeu, para verificar
/// roteamento, repasse do token e balanceamento entre réplicas.
/// </summary>
public sealed class DestinoFalso : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _requisicoes;

    private DestinoFalso(string nome, WebApplication app)
    {
        Nome = nome;
        _app = app;
    }

    public string Nome { get; }

    public string Endereco { get; private set; } = string.Empty;

    public int Requisicoes => Volatile.Read(ref _requisicoes);

    public static async Task<DestinoFalso> IniciarAsync(string nome)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var destino = new DestinoFalso(nome, app);

        app.MapGet("/health/ready", () => "Healthy");
        app.Map("/{**caminho}", (HttpContext http) =>
        {
            Interlocked.Increment(ref destino._requisicoes);
            return Results.Json(new
            {
                servico = nome,
                caminho = http.Request.Path.Value,
                metodo = http.Request.Method,
                autorizacao = !string.IsNullOrEmpty(http.Request.Headers.Authorization),
            });
        });

        await app.StartAsync();
        destino.Endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return destino;
    }

    public Task PararAsync() => _app.StopAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
