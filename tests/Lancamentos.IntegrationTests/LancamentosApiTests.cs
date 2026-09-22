using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Lancamentos;
using Lancamentos.IntegrationTests.Infraestrutura;
using Shouldly;
using Dominio = Lancamentos.Domain.Lancamentos;

namespace Lancamentos.IntegrationTests;

/// <summary>
/// Lancamentos.Api de ponta a ponta com SQL Server e RabbitMQ reais: HTTP → pipeline (auth, tenant,
/// políticas) → CQRS → EF Core → outbox → broker.
/// </summary>
public sealed class LancamentosApiTests(AmbienteDeTeste ambiente)
{
    private const string Rota = "/api/v1/lancamentos";
    private static readonly TimeSpan EsperaPorEvento = TimeSpan.FromSeconds(30);

    private static DateOnly Hoje => Calendario.DataEmSaoPaulo(DateTimeOffset.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LancamentosApiFactory Api => ambiente.Api;

    [Fact]
    public async Task Registrar_DadosValidos_Retorna201ComLocationEOLancamento()
    {
        var tenant = Guid.NewGuid();
        using var cliente = Api.ClienteDo(tenant, TenantRoles.Operador);

        using var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(150.75m), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var lancamento = await LerAsync<LancamentoDto>(resposta);
        lancamento.Valor.ShouldBe(150.75m);
        lancamento.Tipo.ShouldBe(Dominio.TipoLancamento.Credito);
        resposta.Headers.Location!.ToString().ShouldEndWith($"{Rota}/{lancamento.Id}");

        using var consulta = await cliente.GetAsync(resposta.Headers.Location, Ct);
        consulta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registrar_DadosValidos_PublicaLancamentoRegistradoPeloOutbox()
    {
        var tenant = Guid.NewGuid();
        using var cliente = Api.ClienteDo(tenant, TenantRoles.Operador);

        var lancamento = await RegistrarAsync(cliente, 99.90m, Dominio.TipoLancamento.Debito);

        var evento = await ambiente.AguardarEventoAsync(e => e.LancamentoId == lancamento.Id, EsperaPorEvento);
        evento.ShouldNotBeNull("O evento LancamentoRegistrado não chegou ao broker.");
        evento.TenantId.ShouldBe(tenant);
        evento.Tipo.ShouldBe(TipoLancamento.Debito);
        evento.Valor.ShouldBe(99.90m);
        evento.DataCompetencia.ShouldBe(Hoje);
    }

    [Fact]
    public async Task Registrar_DadosInvalidos_Retorna400ComErrosPorCampo()
    {
        using var cliente = Api.ClienteDo(Guid.NewGuid(), TenantRoles.Operador);

        using var resposta = await cliente.PostAsJsonAsync(Rota, new { tipo = "Credito", valor = 0, dataCompetencia = Hoje, descricao = "" }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        resposta.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var erros = (await LerJsonAsync(resposta))["errors"]!.AsObject();
        erros.ContainsKey("Valor").ShouldBeTrue();
        erros.ContainsKey("Descricao").ShouldBeTrue();
    }

    [Fact]
    public async Task Registrar_DataFutura_Retorna400ComCodigoDoErro()
    {
        using var cliente = Api.ClienteDo(Guid.NewGuid(), TenantRoles.Operador);

        using var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(10m, data: Hoje.AddDays(1)), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LerJsonAsync(resposta))["codigo"]!.GetValue<string>().ShouldBe("lancamento.data_futura");
    }

    [Fact]
    public async Task Registrar_SemAutenticacao_Retorna401()
    {
        using var cliente = Api.CreateClient();

        using var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(10m), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registrar_TokenSemTenant_Retorna403()
    {
        using var cliente = Api.ClienteSemTenant();

        using var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(10m), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Registrar_MesmaIdempotencyKey_RetornaOMesmoLancamentoSemDuplicar()
    {
        var tenant = Guid.NewGuid();
        using var cliente = Api.ClienteDo(tenant, TenantRoles.Operador);
        var chave = Guid.NewGuid().ToString();

        var primeiro = await RegistrarAsync(cliente, 50m, chave: chave);
        var segundo = await RegistrarAsync(cliente, 50m, chave: chave);

        segundo.Id.ShouldBe(primeiro.Id);
        (await ListarAsync(cliente)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Isolamento_TenantNaoEnxergaNemAlteraDadosDeOutroTenant()
    {
        using var clienteA = Api.ClienteDo(Guid.NewGuid(), TenantRoles.Admin);
        using var clienteB = Api.ClienteDo(Guid.NewGuid(), TenantRoles.Admin);
        var lancamentoDeA = await RegistrarAsync(clienteA, 300m);

        using var consulta = await clienteB.GetAsync($"{Rota}/{lancamentoDeA.Id}", Ct);
        consulta.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ListarAsync(clienteB)).Itens.ShouldBeEmpty();
        (await ListarAsync(clienteA)).Itens.ShouldContain(l => l.Id == lancamentoDeA.Id);

        using var estorno = await clienteB.PostAsync($"{Rota}/{lancamentoDeA.Id}/estorno", null, Ct);
        estorno.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Estornar_ComPapelOperador_Retorna403()
    {
        var tenant = Guid.NewGuid();
        using var operador = Api.ClienteDo(tenant, TenantRoles.Operador);
        var lancamento = await RegistrarAsync(operador, 10m);

        using var resposta = await operador.PostAsync($"{Rota}/{lancamento.Id}/estorno", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Estornar_ComAdmin_CriaOEstornoEBloqueiaUmSegundo()
    {
        var tenant = Guid.NewGuid();
        using var admin = Api.ClienteDo(tenant, TenantRoles.Admin, TenantRoles.Operador);
        var original = await RegistrarAsync(admin, 80m, Dominio.TipoLancamento.Credito);

        using var primeiro = await admin.PostAsync($"{Rota}/{original.Id}/estorno", null, Ct);
        primeiro.StatusCode.ShouldBe(HttpStatusCode.Created);
        var estorno = await LerAsync<LancamentoDto>(primeiro);
        estorno.Tipo.ShouldBe(Dominio.TipoLancamento.Debito);
        estorno.LancamentoOriginalId.ShouldBe(original.Id);

        using var segundo = await admin.PostAsync($"{Rota}/{original.Id}/estorno", null, Ct);
        segundo.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerJsonAsync(segundo))["codigo"]!.GetValue<string>().ShouldBe("lancamento.ja_estornado");

        var evento = await ambiente.AguardarEventoAsync(e => e.LancamentoId == estorno.Id, EsperaPorEvento);
        evento.ShouldNotBeNull();
        evento.LancamentoOriginalId.ShouldBe(original.Id);
    }

    [Fact]
    public async Task Registrar_QuotaDoPlanoAtingida_Retorna422()
    {
        var tenant = Guid.NewGuid();
        await ambiente.Publicador.Publish(
            new PlanoDoTenantAlterado(Guid.NewGuid(), DateTimeOffset.UtcNow, tenant, "teste", LimiteLancamentosMes: 2, LimiteUsuarios: 2),
            Ct);
        await AguardarAsync(() => Api.PlanoProjetadoAsync(tenant, limite: 2), "A projeção do plano não foi atualizada pelo consumidor.");
        using var cliente = Api.ClienteDo(tenant, TenantRoles.Operador);

        await RegistrarAsync(cliente, 1m);
        await RegistrarAsync(cliente, 2m);
        using var terceiro = await cliente.PostAsJsonAsync(Rota, Corpo(3m), Ct);

        terceiro.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LerJsonAsync(terceiro))["codigo"]!.GetValue<string>().ShouldBe("lancamento.quota_excedida");
    }

    [Fact]
    public async Task Registrar_ComRabbitMqIndisponivel_Retorna201EPublicaQuandoOBrokerVolta()
    {
        var tenant = Guid.NewGuid();
        using var cliente = Api.ClienteDo(tenant, TenantRoles.Operador);

        await ambiente.PausarRabbitMqAsync();
        LancamentoDto lancamento;
        try
        {
            // RNF-01: o registro depende apenas do banco; o evento fica no outbox.
            lancamento = await RegistrarAsync(cliente, 42m);
        }
        finally
        {
            await ambiente.RetomarRabbitMqAsync();
        }

        var evento = await ambiente.AguardarEventoAsync(e => e.LancamentoId == lancamento.Id, TimeSpan.FromSeconds(90));
        evento.ShouldNotBeNull("O outbox não entregou o evento após o broker voltar.");
    }

    private static object Corpo(decimal valor, Dominio.TipoLancamento tipo = Dominio.TipoLancamento.Credito, DateOnly? data = null) =>
        new { tipo = tipo.ToString(), valor, dataCompetencia = data ?? Hoje, descricao = "Movimento de teste" };

    private static async Task<LancamentoDto> RegistrarAsync(
        HttpClient cliente, decimal valor, Dominio.TipoLancamento tipo = Dominio.TipoLancamento.Credito, string? chave = null)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, Rota) { Content = JsonContent.Create(Corpo(valor, tipo)) };
        if (chave is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chave);
        }

        using var resposta = await cliente.SendAsync(requisicao, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync(Ct));
        return await LerAsync<LancamentoDto>(resposta);
    }

    private static async Task<Pagina<LancamentoDto>> ListarAsync(HttpClient cliente)
    {
        using var resposta = await cliente.GetAsync($"{Rota}?data={Hoje:yyyy-MM-dd}", Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await LerAsync<Pagina<LancamentoDto>>(resposta);
    }

    private static async Task<T> LerAsync<T>(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<T>(LancamentosApiFactory.Json, Ct))!;

    private static async Task<JsonObject> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonNode.Parse(await resposta.Content.ReadAsStringAsync(Ct))!.AsObject();

    private static async Task AguardarAsync(Func<Task<bool>> condicao, string mensagem)
    {
        var fim = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < fim)
        {
            if (await condicao())
            {
                return;
            }

            await Task.Delay(250, Ct);
        }

        throw new TimeoutException(mensagem);
    }
}
