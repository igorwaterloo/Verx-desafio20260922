using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consolidado.IntegrationTests.Infraestrutura;

/// <summary>
/// Consolidado.Api real contra os containers; a autenticação JWT é substituída por um esquema de
/// teste com as mesmas claims do Keycloak (sub, tenant_id, roles).
/// </summary>
public sealed class ConsolidadoApiFactory(AmbienteDeTeste ambiente) : WebApplicationFactory<Program>
{
    public HttpClient ClienteDo(Guid tenantId) => Cliente(tenantId.ToString());

    public HttpClient ClienteSemTenant() => Cliente(null);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        foreach (var (chave, valor) in ambiente.Configuracao)
        {
            builder.UseSetting(chave, valor);
        }

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

    private HttpClient Cliente(string? tenantId)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderUsuario, "usuario-teste");
        if (tenantId is not null)
        {
            cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderTenant, tenantId);
        }

        cliente.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return cliente;
    }
}

public sealed class TesteAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Esquema = "Teste";
    public const string HeaderUsuario = "X-Teste-Usuario";
    public const string HeaderTenant = "X-Teste-Tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderUsuario, out var usuario))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(TenantClaims.Usuario, usuario.ToString()),
            new(TenantClaims.Roles, TenantRoles.Operador),
        };
        if (Request.Headers.TryGetValue(HeaderTenant, out var tenant))
        {
            claims.Add(new Claim(TenantClaims.TenantId, tenant.ToString()));
        }

        var identidade = new ClaimsIdentity(claims, Esquema, TenantClaims.Usuario, TenantClaims.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema)));
    }
}
