using System.Collections.Concurrent;
using FluxoCaixa.Contracts;
using MassTransit;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(Lancamentos.IntegrationTests.Infraestrutura.AmbienteDeTeste))]

// Paralelismo desativado em xunit.runner.json: os testes compartilham containers e o cenário
// de resiliência pausa o RabbitMQ.

namespace Lancamentos.IntegrationTests.Infraestrutura;

/// <summary>
/// Ambiente real de integração (ADR-0012): SQL Server e RabbitMQ em containers efêmeros, a API em
/// memória (WebApplicationFactory) e um barramento "observador" que recebe os eventos publicados
/// e publica eventos da Plataforma.
/// </summary>
public sealed class AmbienteDeTeste : IAsyncLifetime
{
    private const string UsuarioRabbit = "teste";
    private const string SenhaRabbit = "teste";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.3-alpine")
        .WithUsername(UsuarioRabbit)
        .WithPassword(SenhaRabbit)
        .Build();

    private readonly ConcurrentQueue<LancamentoRegistrado> _eventosRecebidos = new();
    private IBusControl? _observador;

    public LancamentosApiFactory Api { get; private set; } = null!;

    public IPublishEndpoint Publicador => _observador ?? throw new InvalidOperationException("Ambiente não iniciado.");

    public string ConnectionString => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
    {
        InitialCatalog = "LancamentosDb",
    }.ConnectionString;

    public string RabbitHost => _rabbitMq.Hostname;

    public ushort RabbitPorta => _rabbitMq.GetMappedPublicPort(5672);

    public string RabbitUsuario => UsuarioRabbit;

    public string RabbitSenha => SenhaRabbit;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _rabbitMq.StartAsync());

        Api = new LancamentosApiFactory(this);
        _ = Api.Server; // inicia a API (aplica migrations e sobe o MassTransit)

        _observador = Bus.Factory.CreateUsingRabbitMq(rabbit =>
        {
            rabbit.Host(RabbitHost, RabbitPorta, "/", host =>
            {
                host.Username(RabbitUsuario);
                host.Password(RabbitSenha);
            });
            rabbit.ReceiveEndpoint("teste-observador-lancamento-registrado", endpoint =>
                endpoint.Handler<LancamentoRegistrado>(contexto =>
                {
                    _eventosRecebidos.Enqueue(contexto.Message);
                    return Task.CompletedTask;
                }));
        });
        await _observador.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_observador is not null)
        {
            await _observador.StopAsync();
        }

        await Api.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }

    /// <summary>Aguarda um <see cref="LancamentoRegistrado"/> que satisfaça o filtro.</summary>
    public async Task<LancamentoRegistrado?> AguardarEventoAsync(Func<LancamentoRegistrado, bool> filtro, TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            if (_eventosRecebidos.FirstOrDefault(filtro) is { } evento)
            {
                return evento;
            }

            await Task.Delay(200);
        }

        return null;
    }

    public Task PausarRabbitMqAsync() => _rabbitMq.PauseAsync();

    public Task RetomarRabbitMqAsync() => _rabbitMq.UnpauseAsync();
}
