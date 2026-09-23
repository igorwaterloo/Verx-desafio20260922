using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluxoCaixa.Gateway.Tests.Infraestrutura;
using Shouldly;

namespace FluxoCaixa.Gateway.Tests;

/// <summary>Rate limiting por tenant/plano (noisy neighbor — SLO-10) e por IP; balanceamento e failover.</summary>
public sealed class LimitesEBalanceamentoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RateLimit_TenantFreeExcedeOProprioLimiteSemAfetarUmTenantPro()
    {
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos, new Dictionary<string, string>
        {
            ["LimitesDeRequisicao:RequisicoesPorSegundo:free"] = "5",
            ["LimitesDeRequisicao:RequisicoesPorSegundo:pro"] = "100",
        });
        using var free = gateway.ClienteDo(Guid.NewGuid(), "free", "operador");
        using var pro = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");

        var respostasFree = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => free.GetAsync("/api/v1/consolidado/2026-09-22", Ct)));
        var respostasPro = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => pro.GetAsync("/api/v1/consolidado/2026-09-22", Ct)));

        respostasFree.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeLessThanOrEqualTo(5 + 1);
        var rejeitada = respostasFree.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        rejeitada.Headers.RetryAfter.ShouldNotBeNull();
        rejeitada.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await rejeitada.Content.ReadFromJsonAsync<JsonObject>(Ct))!["codigo"]!.GetValue<string>().ShouldBe("gateway.limite_de_requisicoes");

        respostasPro.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK, "O tenant Pro não pode ser afetado pelo excesso do tenant Free.");

        foreach (var resposta in respostasFree.Concat(respostasPro))
        {
            resposta.Dispose();
        }
    }

    [Fact]
    public async Task RateLimit_OnboardingPublicoLimitadoPorIp()
    {
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos, new Dictionary<string, string>
        {
            ["LimitesDeRequisicao:OnboardingPorMinutoPorIp"] = "3",
        });
        using var anonimo = gateway.ClienteAnonimo();

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var resposta = await anonimo.PostAsJsonAsync("/api/v1/tenants", new { razaoSocial = $"Empresa {i}" }, Ct);
            status.Add(resposta.StatusCode);
        }

        status.Take(3).ShouldAllBe(s => s == HttpStatusCode.OK);
        status.Skip(3).ShouldAllBe(s => s == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Balanceamento_DistribuiAsConsultasEntreAsReplicasDoConsolidado()
    {
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");

        for (var i = 0; i < 10; i++)
        {
            using var resposta = await cliente.GetAsync("/api/v1/consolidado/2026-09-22", Ct);
            resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        destinos.Consolidado1.Requisicoes.ShouldBeGreaterThan(0);
        destinos.Consolidado2.Requisicoes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Failover_LeiturasSaoReenviadasAOutraReplicaSemFalhaParaOCliente()
    {
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");

        await destinos.Consolidado2.PararAsync();

        var status = new List<HttpStatusCode>();
        var servidas = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            using var resposta = await cliente.GetAsync("/api/v1/consolidado/2026-09-22", Ct);
            status.Add(resposta.StatusCode);
            if (resposta.IsSuccessStatusCode)
            {
                servidas.Add((await resposta.Content.ReadFromJsonAsync<JsonObject>(Ct))!["servico"]!.GetValue<string>());
            }
        }

        // A leitura que cai na réplica fora do ar é reenviada à outra: o cliente não vê nenhuma falha.
        status.ShouldAllBe(s => s == HttpStatusCode.OK);
        servidas.ShouldAllBe(s => s == "consolidado-1");
    }

    [Fact]
    public async Task Failover_ReplicaQueAtendeuMuitoTrafegoECai_LeiturasContinuamSemFalha()
    {
        // Cenário encontrado na verificação ponta a ponta: com muitos sucessos recentes, a taxa de falha
        // da réplica demora a cruzar o limite do health check passivo; a retentativa cobre essa janela.
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");
        for (var i = 0; i < 40; i++)
        {
            using var aquecimento = await cliente.GetAsync("/api/v1/consolidado/2026-09-22", Ct);
        }

        await destinos.Consolidado1.PararAsync();

        for (var i = 0; i < 20; i++)
        {
            using var resposta = await cliente.GetAsync("/api/v1/consolidado/2026-09-22", Ct);
            resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Failover_ReplicaQueSumiuDaRede_LeiturasNaoFicamPenduradas()
    {
        // Cenário do teste de caos (Fase 8): um container parado some da rede e a conexão ao IP dele não é
        // recusada — fica pendurada. Sem timeout de conexão, cada leitura esperava o ActivityTimeout (10 s)
        // antes da retentativa. Com o timeout de conexão curto, a leitura vai para a outra réplica em ~1 s.
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos, new Dictionary<string, string>
        {
            // Endereço não roteável: os pacotes são descartados, como os de um container que saiu da rede.
            ["ReverseProxy:Clusters:consolidado:Destinations:consolidado-api-2:Address"] = "http://10.255.255.1:8080",
        });
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");

        for (var i = 0; i < 4; i++)
        {
            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            using var resposta = await cliente.GetAsync("/api/v1/consolidado/2026-09-22", Ct);
            cronometro.Stop();

            resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
            cronometro.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task Failover_TodasAsReplicasForaDoAr_Retorna502()
    {
        await using var destinos = await IniciarDestinosAsync();
        await using var gateway = new GatewayFactory(destinos);
        using var cliente = gateway.ClienteDo(Guid.NewGuid(), "pro", "operador");
        await destinos.Consolidado1.PararAsync();
        await destinos.Consolidado2.PararAsync();

        using var resposta = await cliente.GetAsync("/api/v1/consolidado/2026-09-22", Ct);

        resposta.StatusCode.ShouldBeOneOf(HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<Destinos> IniciarDestinosAsync()
    {
        var destinos = new Destinos();
        await destinos.InitializeAsync();
        return destinos;
    }
}
