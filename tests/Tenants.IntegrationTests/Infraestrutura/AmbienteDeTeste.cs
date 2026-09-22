using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using FluxoCaixa.Contracts;
using MassTransit;
using Testcontainers.Keycloak;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(Tenants.IntegrationTests.Infraestrutura.AmbienteDeTeste))]

namespace Tenants.IntegrationTests.Infraestrutura;

/// <summary>
/// SQL Server, RabbitMQ e um Keycloak real com o realm versionado do projeto (deploy/keycloak); a
/// Tenants.Api em memória; e um barramento observador dos eventos de tenant/plano.
/// </summary>
public sealed class AmbienteDeTeste : IAsyncLifetime
{
    public const string SegredoDaContaDeServico = "segredo-de-teste";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.3-alpine").WithUsername("teste").WithPassword("teste").Build();
    private readonly KeycloakContainer _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.7.4")
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "realm-fluxo-caixa.json")), "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .WithEnvironment("TENANTS_CLIENT_SECRET", SegredoDaContaDeServico)
        .Build();

    private readonly ConcurrentQueue<IIntegrationEvent> _eventos = new();
    private IBusControl? _observador;

    public TenantsApiFactory Api { get; private set; } = null!;

    public string KeycloakUrl => _keycloak.GetBaseAddress().TrimEnd('/');

    public IReadOnlyDictionary<string, string?> Configuracao { get; private set; } = new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _rabbitMq.StartAsync(), _keycloak.StartAsync());

        // O observador sobe antes da Api para receber também os eventos da semeadura de demonstração.
        _observador = Bus.Factory.CreateUsingRabbitMq(rabbit =>
        {
            rabbit.Host(_rabbitMq.Hostname, _rabbitMq.GetMappedPublicPort(5672), "/", host =>
            {
                host.Username("teste");
                host.Password("teste");
            });
            rabbit.ReceiveEndpoint("teste-observador-tenants", endpoint =>
            {
                endpoint.Handler<TenantProvisionado>(c => Registrar(c.Message));
                endpoint.Handler<PlanoDoTenantAlterado>(c => Registrar(c.Message));
            });
        });
        await _observador.StartAsync();

        Configuracao = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Tenants"] = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
            {
                InitialCatalog = "TenantsDb",
            }.ConnectionString,
            ["RabbitMq:Host"] = _rabbitMq.Hostname,
            ["RabbitMq:Porta"] = _rabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Usuario"] = "teste",
            ["RabbitMq:Senha"] = "teste",
            ["Keycloak:Url"] = KeycloakUrl,
            ["Keycloak:ClientSecret"] = SegredoDaContaDeServico,
            ["Database:AplicarMigracoes"] = "true",
            ["Demonstracao:SemearTenants"] = "true",
            ["Autenticacao:Authority"] = "https://keycloak.teste.invalido/realms/fluxo-caixa",
        };

        Api = new TenantsApiFactory(this);
        _ = Api.Server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_observador is not null)
        {
            await _observador.StopAsync();
        }

        await Api.DisposeAsync();
        await Task.WhenAll(_keycloak.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask(), _sqlServer.DisposeAsync().AsTask());
    }

    public Task PausarKeycloakAsync() => _keycloak.PauseAsync();

    public Task RetomarKeycloakAsync() => _keycloak.UnpauseAsync();

    public async Task<T?> AguardarEventoAsync<T>(Func<T, bool> filtro, TimeSpan? limite = null)
        where T : class, IIntegrationEvent
    {
        var fim = DateTime.UtcNow + (limite ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < fim)
        {
            if (_eventos.OfType<T>().FirstOrDefault(filtro) is { } evento)
            {
                return evento;
            }

            await Task.Delay(200);
        }

        return null;
    }

    /// <summary>Faz login real no Keycloak (client de testes) e devolve as claims do access token.</summary>
    public async Task<JsonElement> LoginAsync(string usuario, string senha)
    {
        using var http = new HttpClient();
        using var resposta = await http.PostAsync(
            new Uri($"{KeycloakUrl}/realms/fluxo-caixa/protocol/openid-connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "fluxo-caixa-testes",
                ["username"] = usuario,
                ["password"] = senha,
            }));
        resposta.EnsureSuccessStatusCode();

        var token = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        var carga = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        carga = carga.PadRight(carga.Length + ((4 - (carga.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(carga)).RootElement.Clone();
    }

    private Task Registrar(IIntegrationEvent evento)
    {
        _eventos.Enqueue(evento);
        return Task.CompletedTask;
    }
}
