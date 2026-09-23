using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FluxoCaixa.Gateway.Tests.Infraestrutura;

/// <summary>
/// Gateway real (rotas, autenticação, rate limiting, CORS, headers) apontando para destinos falsos.
/// O JWT é validado de verdade; só a chave de assinatura é de teste (em vez do JWKS do Keycloak).
/// </summary>
public sealed class GatewayFactory(Destinos destinos, IReadOnlyDictionary<string, string>? configuracaoExtra = null)
    : WebApplicationFactory<Program>
{
    public const string Emissor = "https://keycloak.teste/realms/fluxo-caixa";
    private static readonly SymmetricSecurityKey Chave = new(Encoding.UTF8.GetBytes("chave-de-teste-do-gateway-com-256-bits-ok!!"));

    public HttpClient ClienteAnonimo() => CreateClient();

    public HttpClient ClienteDo(Guid tenantId, string plano, params string[] roles)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, plano, roles));
        return cliente;
    }

    public static string Token(Guid tenantId, string plano, string[] roles, SecurityKey? chave = null, DateTime? expiraEm = null)
    {
        var agora = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emissor,
            Audience = "fluxo-caixa-api",
            NotBefore = (expiraEm ?? agora.AddMinutes(5)).AddMinutes(-10),
            IssuedAt = (expiraEm ?? agora.AddMinutes(5)).AddMinutes(-10),
            Expires = expiraEm ?? agora.AddMinutes(5),
            Subject = new ClaimsIdentity([new Claim("sub", "usuario-teste")]),
            Claims = new Dictionary<string, object>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["plano"] = plano,
                ["roles"] = roles,
            },
            SigningCredentials = new SigningCredentials(chave ?? Chave, SecurityAlgorithms.HmacSha256),
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ReverseProxy:Clusters:tenants:Destinations:tenants-api:Address", destinos.Tenants.Endereco);
        builder.UseSetting("ReverseProxy:Clusters:lancamentos:Destinations:lancamentos-api:Address", destinos.Lancamentos.Endereco);
        builder.UseSetting("ReverseProxy:Clusters:consolidado:Destinations:consolidado-api-1:Address", destinos.Consolidado1.Endereco);
        builder.UseSetting("ReverseProxy:Clusters:consolidado:Destinations:consolidado-api-2:Address", destinos.Consolidado2.Endereco);
        builder.UseSetting("Gateway:TamanhoMaximoDoCorpoBytes", "1024");
        foreach (var (chave, valor) in configuracaoExtra ?? new Dictionary<string, string>())
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters.IssuerSigningKey = Chave;
                options.TokenValidationParameters.ValidIssuer = Emissor;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            }));
    }
}

/// <summary>Os quatro destinos do gateway (Tenants, Lançamentos e duas réplicas do Consolidado).</summary>
public sealed class Destinos : IAsyncLifetime
{
    public DestinoFalso Tenants { get; private set; } = null!;

    public DestinoFalso Lancamentos { get; private set; } = null!;

    public DestinoFalso Consolidado1 { get; private set; } = null!;

    public DestinoFalso Consolidado2 { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Tenants = await DestinoFalso.IniciarAsync("tenants");
        Lancamentos = await DestinoFalso.IniciarAsync("lancamentos");
        Consolidado1 = await DestinoFalso.IniciarAsync("consolidado-1");
        Consolidado2 = await DestinoFalso.IniciarAsync("consolidado-2");
    }

    public async ValueTask DisposeAsync()
    {
        await Tenants.DisposeAsync();
        await Lancamentos.DisposeAsync();
        await Consolidado1.DisposeAsync();
        await Consolidado2.DisposeAsync();
    }
}
