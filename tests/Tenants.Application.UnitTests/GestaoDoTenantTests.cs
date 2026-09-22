using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Tenancy;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Tenants.Application.Abstractions;
using Tenants.Application.Tenants.AlterarPlano;
using Tenants.Application.Tenants.Consultas;
using Tenants.Application.Tenants.Expirar;
using Tenants.Application.Tenants.Usuarios;
using Tenants.Domain.Tenants;

namespace Tenants.Application.UnitTests;

public sealed class GestaoDoTenantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AlterarPlano_Operador_RetornaProibido()
    {
        var c = new Cenario().ComoUsuarioDo(Cenario.TenantAtivo(), TenantRoles.Operador);

        var resultado = await new AlterarPlanoHandler(c.Tenants, c.Identidade, c.Publicador, c.UnitOfWork, c.Tenancy, c.Tempo)
            .HandleAsync(new AlterarPlanoCommand("pro"), Ct);

        resultado.Error.ShouldBe(TenantErros.SomenteAdmin);
    }

    [Fact]
    public async Task AlterarPlano_Admin_AtualizaIdentidadePublicaEventoESalva()
    {
        var tenant = Cenario.TenantAtivo("free");
        var c = new Cenario().ComoUsuarioDo(tenant, TenantRoles.Admin).ComUsuariosNoTenant(2);

        var resultado = await new AlterarPlanoHandler(c.Tenants, c.Identidade, c.Publicador, c.UnitOfWork, c.Tenancy, c.Tempo)
            .HandleAsync(new AlterarPlanoCommand("pro"), Ct);

        resultado.Value.Plano.Codigo.ShouldBe("pro");
        await c.Identidade.Received(1).AtualizarPlanoAsync("org-1", tenant.Id, "pro", Arg.Any<CancellationToken>());
        var evento = c.UltimoEvento<PlanoDoTenantAlterado>().ShouldNotBeNull();
        evento.TenantId.ShouldBe(tenant.Id);
        evento.LimiteLancamentosMes.ShouldBe(50_000);
        await c.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlterarPlano_DowngradeComUsuariosAcimaDoLimite_RetornaErroSemAlterarAIdentidade()
    {
        var tenant = Cenario.TenantAtivo("pro");
        var c = new Cenario().ComoUsuarioDo(tenant, TenantRoles.Admin).ComUsuariosNoTenant(3);

        var resultado = await new AlterarPlanoHandler(c.Tenants, c.Identidade, c.Publicador, c.UnitOfWork, c.Tenancy, c.Tempo)
            .HandleAsync(new AlterarPlanoCommand("free"), Ct);

        resultado.Error.Code.ShouldBe("tenant.limite_usuarios");
        await c.Identidade.DidNotReceiveWithAnyArgs().AtualizarPlanoAsync(default!, default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlterarPlano_IdentidadeIndisponivel_Retorna503SemSalvar()
    {
        var c = new Cenario().ComoUsuarioDo(Cenario.TenantAtivo("free"), TenantRoles.Admin).ComUsuariosNoTenant(1);
        c.Identidade.AtualizarPlanoAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProvedorDeIdentidadeIndisponivelException("fora"));

        var resultado = await new AlterarPlanoHandler(c.Tenants, c.Identidade, c.Publicador, c.UnitOfWork, c.Tenancy, c.Tempo)
            .HandleAsync(new AlterarPlanoCommand("pro"), Ct);

        resultado.Error.Type.ShouldBe(ErrorType.Unavailable);
        await c.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdicionarUsuario_NoLimiteDoPlano_RetornaErroDeRegra()
    {
        // RP-04: Free permite 2 usuários.
        var c = new Cenario().ComoUsuarioDo(Cenario.TenantAtivo("free"), TenantRoles.Admin).ComUsuariosNoTenant(2);

        var resultado = await new AdicionarUsuarioHandler(c.Tenants, c.Identidade, c.Tenancy)
            .HandleAsync(new AdicionarUsuarioCommand("João", "joao@loja.dev", "Senha@123", TenantRoles.Operador), Ct);

        resultado.Error.ShouldBe(TenantErros.UsuariosAcimaDoLimite(CatalogoDePlanos.Free));
    }

    [Fact]
    public async Task AdicionarUsuario_DentroDoLimite_CriaNaOrganizacaoComOPapel()
    {
        var tenant = Cenario.TenantAtivo("free");
        var c = new Cenario().ComoUsuarioDo(tenant, TenantRoles.Admin).ComUsuariosNoTenant(1);
        c.IdentidadeCriaUsuarios();

        var resultado = await new AdicionarUsuarioHandler(c.Tenants, c.Identidade, c.Tenancy)
            .HandleAsync(new AdicionarUsuarioCommand("João", "Joao@Loja.dev", "Senha@123", TenantRoles.Operador), Ct);

        resultado.Value.Email.ShouldBe("joao@loja.dev");
        await c.Identidade.Received(1).CriarUsuarioAsync(
            Arg.Is<NovoUsuarioDoTenant>(u => u.OrganizationId == "org-1" && u.TenantId == tenant.Id && u.Plano == "free" && u.Papel == TenantRoles.Operador),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdicionarUsuarioValidator_PapelInvalido_TemErro()
    {
        new AdicionarUsuarioValidator().Validate(new AdicionarUsuarioCommand("João", "joao@loja.dev", "Senha@123", "superusuario"))
            .Errors.ShouldContain(e => e.PropertyName == "Papel");
    }

    [Fact]
    public async Task ObterTenantAtual_RetornaOsDadosEOPlano()
    {
        var tenant = Cenario.TenantAtivo("pro");
        var c = new Cenario().ComoUsuarioDo(tenant, TenantRoles.Operador);

        var resultado = await new ObterTenantAtualHandler(c.Tenants, c.Tenancy).HandleAsync(new ObterTenantAtualQuery(), Ct);

        resultado.Value.Id.ShouldBe(tenant.Id);
        resultado.Value.Cnpj.ShouldBe("11.222.333/0001-81");
        resultado.Value.Plano.ShouldBe(CatalogoDePlanos.Pro);
    }

    [Fact]
    public async Task ListarPlanos_RetornaOCatalogo()
    {
        var resultado = await new ListarPlanosHandler().HandleAsync(new ListarPlanosQuery(), Ct);

        resultado.Value.ShouldBe(CatalogoDePlanos.Todos);
    }

    [Fact]
    public async Task ExpirarPendentes_CompensaEMarcaFalhaNosPendentesAntigos()
    {
        var c = new Cenario();
        var antigo = Tenant.Criar("Loja Antiga", null, "11222333000181", "free", "a@b.dev", Cenario.Agora.AddDays(-2)).Value;
        c.Tenants.ListarPendentesCriadosAntesDeAsync(Cenario.Agora.AddHours(-24), Arg.Any<CancellationToken>()).Returns([antigo]);

        var resultado = await new ExpirarProvisionamentosPendentesHandler(c.Tenants, c.Identidade, c.UnitOfWork, c.Tempo)
            .HandleAsync(new ExpirarProvisionamentosPendentesCommand(TimeSpan.FromHours(24)), Ct);

        resultado.Value.ShouldBe(1);
        antigo.Status.ShouldBe(StatusTenant.Falhou);
        await c.Identidade.Received(1).RemoverOrganizacaoAsync(antigo.Id, Arg.Any<CancellationToken>());
        await c.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
