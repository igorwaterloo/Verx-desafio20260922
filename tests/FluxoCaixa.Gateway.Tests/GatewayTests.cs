using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluxoCaixa.Gateway.Tests.Infraestrutura;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace FluxoCaixa.Gateway.Tests;

/// <summary>Roteamento, autenticação, rotas públicas, headers de segurança, CORS e limites de corpo.</summary>
public sealed class GatewayTests(Destinos destinos) : IClassFixture<Destinos>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/lancamentos?data=2026-09-22", "lancamentos")]
    [InlineData("/api/v1/tenants/atual", "tenants")]
    [InlineData("/api/v1/consolidado/2026-09-22", "consolidado")]
    public async Task Roteamento_EncaminhaPorPrefixoRepassandoOToken(string caminho, string servicoEsperado)
    {
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");

        using var resposta = await cliente.GetAsync(caminho, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eco = (await resposta.Content.ReadFromJsonAsync<JsonObject>(Ct))!;
        eco["servico"]!.GetValue<string>().ShouldStartWith(servicoEsperado);
        eco["caminho"]!.GetValue<string>().ShouldBe(caminho.Split('?')[0]);
        eco["autorizacao"]!.GetValue<bool>().ShouldBeTrue("O token deve chegar ao serviço, que valida de novo (defesa em profundidade).");
    }

    [Fact]
    public async Task Autenticacao_SemTokenComAssinaturaInvalidaOuExpirado_Retorna401()
    {
        await using var gateway = new GatewayFactory(destinos);
        using var anonimo = gateway.ClienteAnonimo();
        using var assinaturaInvalida = gateway.ClienteAnonimo();
        assinaturaInvalida.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            GatewayFactory.Token(Guid.NewGuid(), "pro", ["operador"], new SymmetricSecurityKey(Encoding.UTF8.GetBytes("outra-chave-qualquer-com-tamanho-suficiente!!"))));
        using var expirado = gateway.ClienteAnonimo();
        expirado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            GatewayFactory.Token(Guid.NewGuid(), "pro", ["operador"], expiraEm: DateTime.UtcNow.AddMinutes(-1)));

        var antes = destinos.Lancamentos.Requisicoes;

        foreach (var cliente in new[] { anonimo, assinaturaInvalida, expirado })
        {
            (await cliente.GetAsync("/api/v1/lancamentos?data=2026-09-22", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        destinos.Lancamentos.Requisicoes.ShouldBe(antes, "Requisições não autenticadas não chegam ao serviço.");
    }

    [Fact]
    public async Task RotasPublicas_OnboardingECatalogoLiberadosMasOTenantAtualNao()
    {
        await using var gateway = new GatewayFactory(destinos);
        using var anonimo = gateway.ClienteAnonimo();

        (await anonimo.PostAsJsonAsync("/api/v1/tenants", new { razaoSocial = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonimo.GetAsync("/api/v1/planos", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonimo.GetAsync("/api/v1/tenants/atual", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.PutAsJsonAsync("/api/v1/tenants/atual/plano", new { plano = "pro" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CabecalhosDeSeguranca_EstaoPresentesEOServidorNaoSeIdentifica()
    {
        await using var gateway = new GatewayFactory(destinos);

        using var resposta = await gateway.ClienteAnonimo().GetAsync("/health/live", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        resposta.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        resposta.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        resposta.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        resposta.Headers.Contains("Server").ShouldBeFalse();
        resposta.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
    }

    [Fact]
    public async Task Cors_PermiteSomenteAOrigemDaSpa()
    {
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteAnonimo();

        using var permitida = await cliente.SendAsync(Preflight("http://localhost:4200"), Ct);
        using var negada = await cliente.SendAsync(Preflight("https://site-malicioso.dev"), Ct);

        permitida.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["http://localhost:4200"]);
        permitida.Headers.GetValues("Access-Control-Allow-Headers").Single().ShouldContain("Idempotency-Key", Case.Insensitive);
        negada.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();

        static HttpRequestMessage Preflight(string origem)
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Options, "/api/v1/lancamentos");
            requisicao.Headers.Add("Origin", origem);
            requisicao.Headers.Add("Access-Control-Request-Method", "POST");
            requisicao.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,idempotency-key");
            return requisicao;
        }
    }

    [Fact]
    public async Task CorpoAcimaDoLimite_Retorna413SemEncaminhar()
    {
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");
        var antes = destinos.Lancamentos.Requisicoes;

        using var resposta = await cliente.PostAsync("/api/v1/lancamentos",
            new StringContent(new string('x', 2048), Encoding.UTF8, "application/json"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        destinos.Lancamentos.Requisicoes.ShouldBe(antes);
    }
}
