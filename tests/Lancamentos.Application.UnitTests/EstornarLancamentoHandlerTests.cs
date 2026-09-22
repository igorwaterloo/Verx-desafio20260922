using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Lancamentos.Estornar;
using Lancamentos.Domain.Lancamentos;
using NSubstitute;
using Shouldly;
using Contrato = FluxoCaixa.Contracts;

namespace Lancamentos.Application.UnitTests;

public sealed class EstornarLancamentoHandlerTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EstornarLancamentoHandler CriarHandler(Cenario c) => new(
        c.Lancamentos, c.Publicador, c.UnitOfWork, c.Tenancy, c.Tempo);

    [Fact]
    public async Task Handle_UsuarioSemPapelAdmin_RetornaProibido()
    {
        var cenario = new Cenario(TenantRoles.Operador);

        var resultado = await CriarHandler(cenario).HandleAsync(new EstornarLancamentoCommand(Guid.NewGuid()), Ct);

        resultado.Error.ShouldBe(LancamentoErros.EstornoRestritoAoAdmin);
        resultado.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_LancamentoInexistenteOuDeOutroTenant_RetornaNaoEncontrado()
    {
        var cenario = new Cenario(TenantRoles.Admin);
        cenario.Lancamentos.ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Lancamento?)null);

        var resultado = await CriarHandler(cenario).HandleAsync(new EstornarLancamentoCommand(Guid.NewGuid()), Ct);

        resultado.Error.ShouldBe(LancamentoErros.NaoEncontrado);
    }

    [Fact]
    public async Task Handle_Admin_CriaEstornoPublicaEventoESalva()
    {
        var cenario = new Cenario(TenantRoles.Admin);
        var original = Cenario.LancamentoExistente(TipoLancamento.Credito);
        cenario.Lancamentos.ObterPorIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);

        var resultado = await CriarHandler(cenario).HandleAsync(new EstornarLancamentoCommand(original.Id), Ct);

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.LancamentoOriginalId.ShouldBe(original.Id);
        resultado.Value.Tipo.ShouldBe(TipoLancamento.Debito);
        original.Estornado.ShouldBeTrue();
        cenario.Lancamentos.Received(1).Adicionar(Arg.Is<Lancamento>(l => l.LancamentoOriginalId == original.Id));
        await cenario.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var evento = cenario.UltimoEventoPublicado().ShouldNotBeNull();
        evento.LancamentoOriginalId.ShouldBe(original.Id);
        evento.Tipo.ShouldBe(Contrato.TipoLancamento.Debito);
        evento.DataCompetencia.ShouldBe(original.DataCompetencia);
    }

    [Fact]
    public async Task Handle_LancamentoJaEstornado_RetornaConflito()
    {
        var cenario = new Cenario(TenantRoles.Admin);
        var original = Cenario.LancamentoExistente();
        original.Estornar("outro-admin", Cenario.Agora);
        cenario.Lancamentos.ObterPorIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);

        var resultado = await CriarHandler(cenario).HandleAsync(new EstornarLancamentoCommand(original.Id), Ct);

        resultado.Error.ShouldBe(LancamentoErros.JaEstornado);
    }

    [Fact]
    public async Task Handle_EstornoConcorrenteDetectadoAoSalvar_RetornaConflito()
    {
        var cenario = new Cenario(TenantRoles.Admin);
        var original = Cenario.LancamentoExistente();
        cenario.Lancamentos.ObterPorIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        cenario.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ConflitoDePersistenciaException("estorno duplicado")));

        var resultado = await CriarHandler(cenario).HandleAsync(new EstornarLancamentoCommand(original.Id), Ct);

        resultado.Error.ShouldBe(LancamentoErros.JaEstornado);
    }
}
