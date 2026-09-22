using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel.Tenancy;
using Shouldly;
using Tenants.Application.Tenants;
using Tenants.Domain.Tenants;
using Tenants.IntegrationTests.Infraestrutura;

namespace Tenants.IntegrationTests;

/// <summary>
/// Onboarding e gestão do tenant de ponta a ponta: Tenants.Api → Keycloak real (Organizations,
/// usuários, papéis) → TenantsDb/outbox → RabbitMQ.
/// </summary>
public sealed class TenantsTests(AmbienteDeTeste ambiente)
{
    private const string Rota = "/api/v1/tenants";
    private const string Senha = "Senha@123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Onboarding_CriaOrganizacaoEAdminQueFazLoginComAsClaimsDoTenant()
    {
        var email = EmailUnico();

        var tenant = await CadastrarAsync(Cnpj(), "pro", email);

        tenant.Status.ShouldBe(StatusTenant.Ativo);
        tenant.Plano.Codigo.ShouldBe("pro");

        var claims = await ambiente.LoginAsync(email, Senha);
        claims.GetProperty("tenant_id").GetString().ShouldBe(tenant.Id.ToString());
        claims.GetProperty("plano").GetString().ShouldBe("pro");
        claims.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain(TenantRoles.Admin);
        claims.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain(TenantRoles.Operador);

        var evento = await ambiente.AguardarEventoAsync<TenantProvisionado>(e => e.TenantId == tenant.Id);
        evento.ShouldNotBeNull().LimiteLancamentosMes.ShouldBe(50_000);
    }

    [Fact]
    public async Task Onboarding_CnpjJaCadastrado_Retorna409()
    {
        var cnpj = Cnpj();
        await CadastrarAsync(cnpj, "free", EmailUnico());

        using var resposta = await PostCadastroAsync(cnpj, "free", EmailUnico());

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).ShouldBe("tenant.cnpj_ja_cadastrado");
    }

    [Fact]
    public async Task Onboarding_EmailDeOutroTenant_CompensaEMarcaFalha()
    {
        var email = EmailUnico();
        await CadastrarAsync(Cnpj(), "free", email);
        var cnpj = Cnpj();

        using var resposta = await PostCadastroAsync(cnpj, "free", email);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).ShouldBe("tenant.email_ja_cadastrado");

        using var novaTentativa = await PostCadastroAsync(cnpj, "free", EmailUnico());
        (await CodigoAsync(novaTentativa)).ShouldBe("tenant.provisionamento_falhou");
    }

    [Fact]
    public async Task SemeaduraDeDemonstracao_RegistraOsTenantsDoRealmLocal()
    {
        var mercado = Guid.Parse("0192f79e-0002-7000-8000-000000000002");
        using var cliente = ambiente.Api.ClienteDo(mercado, TenantRoles.Operador);

        var tenant = (await cliente.GetFromJsonAsync<TenantDto>($"{Rota}/atual", Json, Ct))!;

        tenant.Status.ShouldBe(StatusTenant.Ativo);
        tenant.Plano.Codigo.ShouldBe("pro");
        (await ambiente.AguardarEventoAsync<TenantProvisionado>(e => e.TenantId == mercado)).ShouldNotBeNull();
    }

    [Fact]
    public async Task AlterarPlano_Admin_AtualizaTokenPublicaEventoEOperadorNaoPode()
    {
        var email = EmailUnico();
        var tenant = await CadastrarAsync(Cnpj(), "free", email);

        using var operador = ambiente.Api.ClienteDo(tenant.Id, TenantRoles.Operador);
        (await operador.PutAsJsonAsync($"{Rota}/atual/plano", new { plano = "pro" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var admin = ambiente.Api.ClienteDo(tenant.Id, TenantRoles.Admin, TenantRoles.Operador);
        using var resposta = await admin.PutAsJsonAsync($"{Rota}/atual/plano", new { plano = "pro" }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ambiente.AguardarEventoAsync<PlanoDoTenantAlterado>(e => e.TenantId == tenant.Id && e.PlanoCodigo == "pro")).ShouldNotBeNull();
        (await ambiente.LoginAsync(email, Senha)).GetProperty("plano").GetString().ShouldBe("pro");
    }

    [Fact]
    public async Task Usuarios_AdminCriaOperadorAteOLimiteDoPlano()
    {
        var tenant = await CadastrarAsync(Cnpj(), "free", EmailUnico()); // Free: 2 usuários (admin + 1)
        using var admin = ambiente.Api.ClienteDo(tenant.Id, TenantRoles.Admin, TenantRoles.Operador);
        var emailOperador = EmailUnico();

        using var criado = await admin.PostAsJsonAsync($"{Rota}/atual/usuarios",
            new { nome = "Operador Teste", email = emailOperador, senha = Senha, papel = TenantRoles.Operador }, Ct);
        criado.StatusCode.ShouldBe(HttpStatusCode.Created);

        var claims = await ambiente.LoginAsync(emailOperador, Senha);
        claims.GetProperty("tenant_id").GetString().ShouldBe(tenant.Id.ToString());
        claims.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldNotContain(TenantRoles.Admin);

        using var acimaDoLimite = await admin.PostAsJsonAsync($"{Rota}/atual/usuarios",
            new { nome = "Mais Um", email = EmailUnico(), senha = Senha, papel = TenantRoles.Operador }, Ct);
        acimaDoLimite.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(acimaDoLimite)).ShouldBe("tenant.limite_usuarios");

        (await admin.GetFromJsonAsync<JsonArray>($"{Rota}/atual/usuarios", Ct))!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Onboarding_ComKeycloakIndisponivel_Retorna503EConcluiAoReenviar()
    {
        var cnpj = Cnpj();
        var email = EmailUnico();

        await ambiente.PausarKeycloakAsync();
        try
        {
            using var indisponivel = await PostCadastroAsync(cnpj, "free", email);
            indisponivel.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await CodigoAsync(indisponivel)).ShouldBe("tenant.identidade_indisponivel");
        }
        finally
        {
            await ambiente.RetomarKeycloakAsync();
        }

        // Reenviar a mesma requisição retoma o onboarding pendente (o circuito pode levar alguns segundos para fechar).
        HttpStatusCode status = default;
        for (var tentativa = 0; tentativa < 15 && status != HttpStatusCode.Created; tentativa++)
        {
            using var reenvio = await PostCadastroAsync(cnpj, "free", email);
            status = reenvio.StatusCode;
            if (status != HttpStatusCode.Created)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), Ct);
            }
        }

        status.ShouldBe(HttpStatusCode.Created);
        (await ambiente.LoginAsync(email, Senha)).GetProperty("plano").GetString().ShouldBe("free");
    }

    [Fact]
    public async Task Planos_CatalogoEhPublico()
    {
        using var anonimo = ambiente.Api.CreateClient();

        var planos = (await anonimo.GetFromJsonAsync<JsonArray>("/api/v1/planos", Ct))!;

        planos.Select(p => p!["codigo"]!.GetValue<string>()).ShouldBe(["free", "pro"]);
    }

    [Fact]
    public async Task Onboarding_DadosInvalidos_Retorna400()
    {
        using var resposta = await ambiente.Api.CreateClient().PostAsJsonAsync(Rota,
            new { razaoSocial = "", cnpj = "123", plano = "enterprise", administrador = new { nome = "A", email = "x", senha = "1" } }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<TenantDto> CadastrarAsync(string cnpj, string plano, string email)
    {
        using var resposta = await PostCadastroAsync(cnpj, plano, email);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync(Ct));
        return (await resposta.Content.ReadFromJsonAsync<TenantDto>(Json, Ct))!;
    }

    private Task<HttpResponseMessage> PostCadastroAsync(string cnpj, string plano, string email) =>
        ambiente.Api.CreateClient().PostAsJsonAsync(Rota, new
        {
            razaoSocial = $"Empresa {cnpj} Ltda",
            nomeFantasia = $"Empresa {cnpj}",
            cnpj,
            plano,
            administrador = new { nome = "Admin Teste", email, senha = Senha },
        }, Ct);

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        JsonNode.Parse(await resposta.Content.ReadAsStringAsync(Ct))?["codigo"]?.GetValue<string>();

    private static string EmailUnico() => $"admin-{Guid.NewGuid():N}@teste.dev";

    /// <summary>CNPJ aleatório com dígitos verificadores válidos.</summary>
    private static string Cnpj()
    {
        var baseDoCnpj = string.Concat(Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(10))) + "0001";
        static int Digito(string d, int[] pesos)
        {
            var resto = pesos.Select((p, i) => (d[i] - '0') * p).Sum() % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        var primeiro = Digito(baseDoCnpj, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var segundo = Digito(baseDoCnpj + primeiro, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{baseDoCnpj}{primeiro}{segundo}";
    }
}
