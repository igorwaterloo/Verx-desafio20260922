using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Tenancy;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Tenants.Application.Abstractions;
using Tenants.Application.Tenants.Provisionar;
using Tenants.Domain.Tenants;

namespace Tenants.Application.UnitTests;

public sealed class ProvisionarTenantHandlerTests
{
    private readonly Cenario _c = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ProvisionarTenantHandler Handler() => new(_c.Tenants, _c.Identidade, _c.Publicador, _c.UnitOfWork, _c.Tempo);

    private static ProvisionarTenantCommand Comando(string plano = "pro") =>
        new("Loja Exemplo Ltda", "Loja Exemplo", "11.222.333/0001-81", plano, "Maria Silva", "Admin@Loja.dev", "Senha@123");

    [Fact]
    public async Task Handle_TenantNovo_GravaPendenteProvisionaIdentidadeAtivaEPublicaEvento()
    {
        _c.Identidade.GarantirOrganizacaoAsync(Arg.Any<Guid>(), "Loja Exemplo", "pro", Arg.Any<CancellationToken>()).Returns("org-9");
        _c.IdentidadeCriaUsuarios();
        Tenant? gravado = null;
        _c.Tenants.Adicionar(Arg.Do<Tenant>(t => gravado = t));

        var resultado = await Handler().HandleAsync(Comando(), Ct);

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.Status.ShouldBe(StatusTenant.Ativo);
        gravado.ShouldNotBeNull().OrganizationId.ShouldBe("org-9");

        // Saga (RP-02): primeiro persiste Pendente; só depois de a identidade existir, ativa.
        await _c.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _c.Identidade.Received(1).CriarUsuarioAsync(
            Arg.Is<NovoUsuarioDoTenant>(u => u.OrganizationId == "org-9" && u.TenantId == gravado.Id && u.Plano == "pro"
                && u.Email == "admin@loja.dev" && u.Senha == "Senha@123" && u.Papel == TenantRoles.Admin),
            Arg.Any<CancellationToken>());

        var evento = _c.UltimoEvento<TenantProvisionado>().ShouldNotBeNull();
        evento.TenantId.ShouldBe(gravado.Id);
        evento.PlanoCodigo.ShouldBe("pro");
        evento.LimiteLancamentosMes.ShouldBe(50_000);
        evento.LimiteUsuarios.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_CnpjDeTenantAtivo_RetornaConflitoSemProvisionar()
    {
        _c.Tenants.ObterPorCnpjAsync("11222333000181", Arg.Any<CancellationToken>()).Returns(Cenario.TenantAtivo());

        var resultado = await Handler().HandleAsync(Comando(), Ct);

        resultado.Error.ShouldBe(TenantErros.CnpjJaCadastrado);
        await _c.Identidade.DidNotReceiveWithAnyArgs().GarantirOrganizacaoAsync(default, default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantPendenteDoMesmoCnpj_RetomaOProvisionamento()
    {
        var pendente = Tenant.Criar("Loja Exemplo Ltda", null, "11222333000181", "pro", "admin@loja.dev", Cenario.Agora.AddMinutes(-5)).Value;
        _c.Tenants.ObterPorCnpjAsync("11222333000181", Arg.Any<CancellationToken>()).Returns(pendente);
        _c.Identidade.GarantirOrganizacaoAsync(pendente.Id, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("org-1");
        _c.IdentidadeCriaUsuarios();

        var resultado = await Handler().HandleAsync(Comando(), Ct);

        resultado.Value.Id.ShouldBe(pendente.Id);
        pendente.Status.ShouldBe(StatusTenant.Ativo);
        _c.Tenants.DidNotReceiveWithAnyArgs().Adicionar(default!);
    }

    [Fact]
    public async Task Handle_EmailJaUsadoPorOutroTenant_CompensaEMarcaFalha()
    {
        _c.Identidade.GarantirOrganizacaoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("org-1");
        _c.Identidade.CriarUsuarioAsync(Arg.Any<NovoUsuarioDoTenant>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(TenantErros.EmailJaCadastrado));
        Tenant? gravado = null;
        _c.Tenants.Adicionar(Arg.Do<Tenant>(t => gravado = t));

        var resultado = await Handler().HandleAsync(Comando(), Ct);

        resultado.Error.ShouldBe(TenantErros.EmailJaCadastrado);
        await _c.Identidade.Received(1).RemoverOrganizacaoAsync(gravado!.Id, Arg.Any<CancellationToken>());
        gravado.Status.ShouldBe(StatusTenant.Falhou);
        _c.UltimoEvento<TenantProvisionado>().ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ProvedorDeIdentidadeIndisponivel_MantemPendenteERetorna503()
    {
        _c.Identidade.GarantirOrganizacaoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProvedorDeIdentidadeIndisponivelException("timeout"));
        Tenant? gravado = null;
        _c.Tenants.Adicionar(Arg.Do<Tenant>(t => gravado = t));

        var resultado = await Handler().HandleAsync(Comando(), Ct);

        resultado.Error.ShouldBe(TenantErros.ProvedorDeIdentidadeIndisponivel);
        resultado.Error.Type.ShouldBe(ErrorType.Unavailable);
        gravado!.Status.ShouldBe(StatusTenant.Pendente);
        gravado.TentativasDeProvisionamento.ShouldBe(1);
        _c.UltimoEvento<TenantProvisionado>().ShouldBeNull();
    }

    [Fact]
    public async Task Handle_TenantQueFalhouAnteriormente_RetornaConflito()
    {
        var falhou = Tenant.Criar("Loja Exemplo Ltda", null, "11222333000181", "pro", "admin@loja.dev", Cenario.Agora).Value;
        falhou.MarcarFalha("motivo", Cenario.Agora);
        _c.Tenants.ObterPorCnpjAsync("11222333000181", Arg.Any<CancellationToken>()).Returns(falhou);

        (await Handler().HandleAsync(Comando(), Ct)).Error.ShouldBe(TenantErros.ProvisionamentoFalhou);
    }

    [Fact]
    public void Validator_SenhaFracaEDadosInvalidos_TemErros()
    {
        var resultado = new ProvisionarTenantValidator().Validate(
            new ProvisionarTenantCommand("", null, "", "", "A", "sem-arroba", "123"));

        resultado.Errors.Select(e => e.PropertyName).Distinct().ShouldBe(
            ["RazaoSocial", "Cnpj", "PlanoCodigo", "AdminNome", "AdminEmail", "AdminSenha"], ignoreOrder: true);
    }
}
