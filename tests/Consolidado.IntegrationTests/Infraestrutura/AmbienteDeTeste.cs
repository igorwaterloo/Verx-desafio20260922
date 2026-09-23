using Consolidado.Application;
using Consolidado.Infrastructure;
using Consolidado.Infrastructure.Persistence;
using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.Infrastructure.Common.Web;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Consolidado.IntegrationTests.Infraestrutura.AmbienteDeTeste))]

namespace Consolidado.IntegrationTests.Infraestrutura;

/// <summary>
/// Ambiente real (ADR-0012): SQL Server, RabbitMQ e Redis em containers; o Consolidado.Worker como
/// host com a mesma composição de produção; a Consolidado.Api em memória; e um barramento que
/// publica LancamentoRegistrado como o serviço de Lançamentos faria.
/// </summary>
public sealed class AmbienteDeTeste : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.3-alpine").WithUsername("teste").WithPassword("teste").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.8-alpine").Build();

    private IHost? _worker;
    private IBusControl? _publicador;
    private ConnectionMultiplexer? _redisDoTeste;

    public ConsolidadoApiFactory Api { get; private set; } = null!;

    /// <summary>Serviços do worker (ex.: IMeterFactory para verificar métricas).</summary>
    public IServiceProvider ServicosDoWorker => _worker?.Services ?? throw new InvalidOperationException("Ambiente não iniciado.");

    public IPublishEndpoint Publicador => _publicador ?? throw new InvalidOperationException("Ambiente não iniciado.");

    public IDatabase Redis => _redisDoTeste?.GetDatabase() ?? throw new InvalidOperationException("Ambiente não iniciado.");

    public IReadOnlyDictionary<string, string?> Configuracao { get; private set; } = new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _rabbitMq.StartAsync(), _redis.StartAsync());

        Configuracao = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Consolidado"] = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
            {
                InitialCatalog = "ConsolidadoDb",
            }.ConnectionString,
            ["RabbitMq:Host"] = _rabbitMq.Hostname,
            ["RabbitMq:Porta"] = _rabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Usuario"] = "teste",
            ["RabbitMq:Senha"] = "teste",
            ["Redis:Endereco"] = _redis.GetConnectionString(),
            ["Autenticacao:Authority"] = "https://keycloak.teste.invalido/realms/fluxo-caixa",
        };

        // Worker: mesma composição do Consolidado.Worker/Program.cs.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Configuracao);
        builder.Services.AddTenancy();
        builder.Services.AddConsolidadoApplication();
        builder.Services.AddConsolidadoPersistencia(builder.Configuration);
        builder.Services.AddConsolidadoCache(builder.Configuration);
        builder.Services.AddConsolidadoMensageria(builder.Configuration);
        _worker = builder.Build();
        await _worker.AplicarMigracoesAsync<ConsolidadoDbContext>();
        await _worker.StartAsync();

        Api = new ConsolidadoApiFactory(this);
        _ = Api.Server;

        _publicador = Bus.Factory.CreateUsingRabbitMq(rabbit =>
            rabbit.Host(_rabbitMq.Hostname, _rabbitMq.GetMappedPublicPort(5672), "/", host =>
            {
                host.Username("teste");
                host.Password("teste");
            }));
        await _publicador.StartAsync();

        _redisDoTeste = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        if (_publicador is not null)
        {
            await _publicador.StopAsync();
        }

        if (_worker is not null)
        {
            await _worker.StopAsync();
            _worker.Dispose();
        }

        if (_redisDoTeste is not null)
        {
            await _redisDoTeste.DisposeAsync();
        }

        await Api.DisposeAsync();
        await Task.WhenAll(_redis.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask(), _sqlServer.DisposeAsync().AsTask());
    }

    public Task PausarRedisAsync() => _redis.PauseAsync();

    public Task RetomarRedisAsync() => _redis.UnpauseAsync();
}
