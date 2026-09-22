using FluentValidation.TestHelper;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Lancamentos;
using Lancamentos.Application.Lancamentos.Listar;
using Lancamentos.Application.Lancamentos.Obter;
using Lancamentos.Application.Lancamentos.Registrar;
using Lancamentos.Application.Planos;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;
using NSubstitute;
using Shouldly;

namespace Lancamentos.Application.UnitTests;

public sealed class ConsultasEPlanoTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ObterPorId_Inexistente_RetornaNaoEncontrado()
    {
        var leitura = Substitute.For<ILancamentosLeitura>();
        leitura.ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((LancamentoDto?)null);

        var resultado = await new ObterLancamentoPorIdHandler(leitura).HandleAsync(new ObterLancamentoPorIdQuery(Guid.NewGuid()), Ct);

        resultado.Error.ShouldBe(LancamentoErros.NaoEncontrado);
    }

    [Fact]
    public async Task Listar_RetornaAPaginaDaLeitura()
    {
        var leitura = Substitute.For<ILancamentosLeitura>();
        var pagina = new Pagina<LancamentoDto>([], 2, 10, 0);
        leitura.ListarPorDataAsync(Cenario.Hoje, 2, 10, Arg.Any<CancellationToken>()).Returns(pagina);

        var resultado = await new ListarLancamentosHandler(leitura).HandleAsync(new ListarLancamentosQuery(Cenario.Hoje, 2, 10), Ct);

        resultado.Value.ShouldBeSameAs(pagina);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void ListarValidator_PaginacaoInvalida_TemErro(int pagina, int tamanho)
    {
        new ListarLancamentosValidator().TestValidate(new ListarLancamentosQuery(Cenario.Hoje, pagina, tamanho)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void RegistrarValidator_CamposInvalidos_TemErrosPorCampo()
    {
        var resultado = new RegistrarLancamentoValidator().TestValidate(
            new RegistrarLancamentoCommand((TipoLancamento)9, 0m, default, "", new string('k', 101)));

        resultado.ShouldHaveValidationErrorFor(c => c.Tipo);
        resultado.ShouldHaveValidationErrorFor(c => c.Valor);
        resultado.ShouldHaveValidationErrorFor(c => c.DataCompetencia);
        resultado.ShouldHaveValidationErrorFor(c => c.Descricao);
        resultado.ShouldHaveValidationErrorFor(c => c.ChaveIdempotencia);
    }

    [Fact]
    public async Task AtualizarPlano_SemProjecao_CriaAProjecaoDoTenant()
    {
        var cenario = new Cenario();
        cenario.Planos.ObterAsync(Arg.Any<CancellationToken>()).Returns((TenantPlano?)null);
        var handler = new AtualizarPlanoDoTenantHandler(cenario.Planos, cenario.UnitOfWork, cenario.Tenancy);

        var resultado = await handler.HandleAsync(new AtualizarPlanoDoTenantCommand("pro", 50_000, Cenario.Agora), Ct);

        resultado.IsSuccess.ShouldBeTrue();
        cenario.Planos.Received(1).Adicionar(Arg.Is<TenantPlano>(p => p.TenantId == Cenario.Tenant && p.LimiteLancamentosMes == 50_000));
        await cenario.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtualizarPlano_EventoMaisAntigo_NaoAlteraNemSalva()
    {
        var cenario = new Cenario();
        var atual = TenantPlano.Criar(Cenario.Tenant, "pro", 50_000, Cenario.Agora);
        cenario.Planos.ObterAsync(Arg.Any<CancellationToken>()).Returns(atual);
        var handler = new AtualizarPlanoDoTenantHandler(cenario.Planos, cenario.UnitOfWork, cenario.Tenancy);

        await handler.HandleAsync(new AtualizarPlanoDoTenantCommand("free", 1_000, Cenario.Agora.AddMinutes(-5)), Ct);

        atual.PlanoCodigo.ShouldBe("pro");
        await cenario.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
