using FluxoCaixa.SharedKernel;
using Shouldly;
using Tenants.Domain.Tenants;

namespace Tenants.Domain.UnitTests;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    private static Tenant Novo(string plano = "free") =>
        Tenant.Criar("Loja Exemplo Ltda", "Loja Exemplo", "11.222.333/0001-81", plano, "admin@loja.dev", Agora).Value;

    [Fact]
    public void Criar_DadosValidos_IniciaPendenteSemOrganizacao()
    {
        var tenant = Novo("pro");

        tenant.Id.Version.ShouldBe(7);
        tenant.RazaoSocial.ShouldBe("Loja Exemplo Ltda");
        tenant.NomeFantasia.ShouldBe("Loja Exemplo");
        tenant.Cnpj.Numero.ShouldBe("11222333000181");
        tenant.PlanoCodigo.ShouldBe("pro");
        tenant.Status.ShouldBe(StatusTenant.Pendente);
        tenant.OrganizationId.ShouldBeNull();
        tenant.AdminEmail.ShouldBe("admin@loja.dev");
        tenant.CriadoEm.ShouldBe(Agora);
    }

    [Fact]
    public void Criar_CnpjInvalido_RetornaErro()
    {
        Tenant.Criar("Loja", null, "123", "free", "a@b.dev", Agora).Error.ShouldBe(TenantErros.CnpjInvalido);
    }

    [Fact]
    public void Criar_PlanoInexistente_RetornaErro()
    {
        Tenant.Criar("Loja Exemplo", null, "11222333000181", "enterprise", "a@b.dev", Agora).Error.ShouldBe(TenantErros.PlanoInexistente);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Criar_RazaoSocialInvalida_RetornaErro(string razao)
    {
        Tenant.Criar(razao, null, "11222333000181", "free", "a@b.dev", Agora).Error.ShouldBe(TenantErros.RazaoSocialInvalida);
    }

    [Fact]
    public void Ativar_Pendente_ViraAtivoComAOrganizacaoERegistraEvento()
    {
        var tenant = Novo();

        tenant.Ativar("org-123", Agora.AddSeconds(2));

        tenant.Status.ShouldBe(StatusTenant.Ativo);
        tenant.OrganizationId.ShouldBe("org-123");
        tenant.AtualizadoEm.ShouldBe(Agora.AddSeconds(2));
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TenantAtivado>().TenantId.ShouldBe(tenant.Id);
    }

    [Fact]
    public void Ativar_ForaDePendente_LancaExcecao()
    {
        var tenant = Novo();
        tenant.Ativar("org-123", Agora);

        Should.Throw<InvalidOperationException>(() => tenant.Ativar("org-456", Agora));
    }

    [Fact]
    public void MarcarFalha_Pendente_RegistraOMotivo()
    {
        var tenant = Novo();

        tenant.MarcarFalha("E-mail do administrador já cadastrado.", Agora);

        tenant.Status.ShouldBe(StatusTenant.Falhou);
        tenant.MotivoFalha.ShouldBe("E-mail do administrador já cadastrado.");
    }

    [Fact]
    public void RegistrarTentativa_ContaAsTentativasDeProvisionamento()
    {
        var tenant = Novo();

        tenant.RegistrarTentativa(Agora);
        tenant.RegistrarTentativa(Agora.AddMinutes(1));

        tenant.TentativasDeProvisionamento.ShouldBe(2);
    }

    [Fact]
    public void AlterarPlano_Ativo_AlteraOPlano()
    {
        var tenant = Novo("free");
        tenant.Ativar("org", Agora);

        var resultado = tenant.AlterarPlano("pro", usuariosAtuais: 2, Agora.AddDays(1));

        resultado.IsSuccess.ShouldBeTrue();
        tenant.PlanoCodigo.ShouldBe("pro");
        tenant.Plano.ShouldBe(CatalogoDePlanos.Pro);
    }

    [Fact]
    public void AlterarPlano_UsuariosAcimaDoLimiteDoNovoPlano_RetornaErro()
    {
        // RP-05: o downgrade não pode deixar o tenant com mais usuários que o plano permite.
        var tenant = Novo("pro");
        tenant.Ativar("org", Agora);

        var resultado = tenant.AlterarPlano("free", usuariosAtuais: 3, Agora);

        resultado.Error.ShouldBe(TenantErros.UsuariosAcimaDoLimite(CatalogoDePlanos.Free));
        resultado.Error.Type.ShouldBe(ErrorType.BusinessRule);
        tenant.PlanoCodigo.ShouldBe("pro");
    }

    [Fact]
    public void AlterarPlano_MesmoPlano_RetornaConflito()
    {
        var tenant = Novo("free");
        tenant.Ativar("org", Agora);

        tenant.AlterarPlano("free", 1, Agora).Error.ShouldBe(TenantErros.PlanoJaAtivo);
    }

    [Fact]
    public void AlterarPlano_TenantPendente_RetornaErro()
    {
        Novo().AlterarPlano("pro", 1, Agora).Error.ShouldBe(TenantErros.TenantNaoAtivo);
    }

    [Fact]
    public void AlterarPlano_PlanoInexistente_RetornaErro()
    {
        var tenant = Novo();
        tenant.Ativar("org", Agora);

        tenant.AlterarPlano("enterprise", 1, Agora).Error.ShouldBe(TenantErros.PlanoInexistente);
    }

    [Fact]
    public void Importar_CriaTenantJaAtivo()
    {
        var id = Guid.Parse("0192f79e-0002-7000-8000-000000000002");

        var tenant = Tenant.Importar(id, "Mercado Demo", "11222333000181", "pro", "org-demo", "admin@mercado.demo", Agora);

        tenant.Id.ShouldBe(id);
        tenant.Status.ShouldBe(StatusTenant.Ativo);
        tenant.OrganizationId.ShouldBe("org-demo");
    }
}
