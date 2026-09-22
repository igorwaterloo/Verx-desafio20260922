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

namespace Tenants.IntegrationTests.Infraestrutura;

/// <summary>
/// Tenants.Api real contra os containers. As chamadas autenticadas usam um esquema de teste com as
/// mesmas claims do Keycloak; o Keycloak real é usado pela Api como provedor de identidade.
/// </summary>
public sealed class TenantsApiFactory(AmbienteDeTeste ambiente) : WebApplicationFactory<Program>
{
    public HttpClient ClienteDo(Guid tenantId, params string[] roles)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderUsuario, "usuario-teste");
        cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderTenant, tenantId.ToString());
        cliente.DefaultRequestHeaders.Add(TesteAuthHandler.HeaderRoles, string.Join(',', roles));
        cliente.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return cliente;
    }

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
}

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
