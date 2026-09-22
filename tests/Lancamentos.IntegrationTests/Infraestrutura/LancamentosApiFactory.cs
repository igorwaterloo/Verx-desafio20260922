using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lancamentos.IntegrationTests.Infraestrutura;

/// <summary>
/// Sobe a Lancamentos.Api real contra os containers. A autenticação JWT é substituída por um
/// esquema de teste que lê as claims de headers — o resto do pipeline (tenant, políticas) é o real.
/// </summary>
public sealed class LancamentosApiFactory(AmbienteDeTeste ambiente) : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Cliente autenticado como um usuário do tenant, com os papéis informados.</summary>
    public HttpClient ClienteDo(Guid tenantId, params string[] roles) =>
        Cliente(tenantId.ToString(), roles);

    /// <summary>Cliente autenticado, mas sem a claim tenant_id.</summary>
    public HttpClient ClienteSemTenant() => Cliente(null, TenantRoles.Operador);

    public async Task<bool> PlanoProjetadoAsync(Guid tenantId, int limite)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Definir(tenantId, "teste", []);
        var contexto = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        return await contexto.TenantsPlanos.AnyAsync(p => p.LimiteLancamentosMes == limite);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:Lancamentos", ambiente.ConnectionString);
        builder.UseSetting("RabbitMq:Host", ambiente.RabbitHost);
        builder.UseSetting("RabbitMq:Porta", ambiente.RabbitPorta.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RabbitMq:Usuario", ambiente.RabbitUsuario);
        builder.UseSetting("RabbitMq:Senha", ambiente.RabbitSenha);
        builder.UseSetting("Database:AplicarMigracoes", "true");
        builder.UseSetting("Autenticacao:Authority", "https://keycloak.teste.invalido/realms/fluxo-caixa");

        builder.ConfigureTestServices(services =>
            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = TesteAuthHandler.Esquema;
                    options.DefaultAuthenticateScheme = TesteAuthHandler.Esquema;
                    options.DefaultChallengeScheme = TesteAuthHandler.Esquema;
                })
                .AddScheme<AuthenticationSchemeOptions, TesteAuthHandler>(TesteAuthHandler.Esquema, _ => { }));
    }

    private HttpClient Cliente(string? tenantId, params string[] roles)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderUsuario, $"usuario-{Guid.NewGuid():N}");
        if (tenantId is not null)
        {
            cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderTenant, tenantId);
        }

        cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderRoles, string.Join(',', roles));
        cliente.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return cliente;
    }
}

/// <summary>Autenticação de teste: monta as mesmas claims que o Keycloak emite (sub, tenant_id, roles).</summary>
public sealed class TesteAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Esquema = "Teste";
    public const string HeaderUsuario = "X-Teste-Usuario";
    public const string HeaderTenant = "X-Teste-Tenant";
    public const string HeaderRoles = "X-Teste-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderUsuario, out var usuario))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(TenantClaims.Usuario, usuario.ToString()) };
        if (Request.Headers.TryGetValue(HeaderTenant, out var tenant))
        {
            claims.Add(new Claim(TenantClaims.TenantId, tenant.ToString()));
        }

        claims.AddRange(Request.Headers[HeaderRoles].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(role => new Claim(TenantClaims.Roles, role)));

        var identidade = new ClaimsIdentity(claims, Esquema, TenantClaims.Usuario, TenantClaims.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema)));
    }
}
