using FluxoCaixa.SharedKernel;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Lancamentos.Registrar;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;
using NSubstitute;
using Shouldly;
using Contrato = FluxoCaixa.Contracts;

namespace Lancamentos.Application.UnitTests;

public sealed class RegistrarLancamentoHandlerTests
{
    private readonly Cenario _cenario = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private RegistrarLancamentoHandler CriarHandler() => new(
        _cenario.Lancamentos, _cenario.Planos, _cenario.Idempotencia, _cenario.Publicador,
        _cenario.UnitOfWork, _cenario.Tenancy, _cenario.Tempo, _cenario.Metricas);

    private static RegistrarLancamentoCommand Comando(DateOnly? data = null, string? chave = null) =>
        new(TipoLancamento.Credito, 150.75m, data ?? Cenario.Hoje, "Venda no balcão", chave);

    [Fact]
    public async Task Handle_ComandoValido_PersistePublicaEventoERetornaOLancamento()
    {
        _cenario.Planos.ObterAsync(Arg.Any<CancellationToken>()).Returns(TenantPlano.Criar(Cenario.Tenant, "pro", 50_000, Cenario.Agora));

        var resultado = await CriarHandler().HandleAsync(Comando(), Ct);

        resultado.IsSuccess.ShouldBeTrue();
        var dto = resultado.Value;
        dto.Tipo.ShouldBe(TipoLancamento.Credito);
        dto.Valor.ShouldBe(150.75m);
        dto.CriadoPor.ShouldBe("usuario-1");

        _cenario.Lancamentos.Received(1).Adicionar(Arg.Is<Lancamento>(l => l.Id == dto.Id && l.TenantId == Cenario.Tenant));
        await _cenario.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var evento = _cenario.UltimoEventoPublicado().ShouldNotBeNull();
        evento.TenantId.ShouldBe(Cenario.Tenant);
        evento.LancamentoId.ShouldBe(dto.Id);
        evento.Tipo.ShouldBe(Contrato.TipoLancamento.Credito);
        evento.Valor.ShouldBe(150.75m);
        evento.DataCompetencia.ShouldBe(Cenario.Hoje);
        evento.LancamentoOriginalId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_QuotaDoPlanoAtingida_RetornaErroDeRegraSemPersistir()
    {
        _cenario.Planos.ObterAsync(Arg.Any<CancellationToken>()).Returns(TenantPlano.Criar(Cenario.Tenant, "pro", 3, Cenario.Agora));
        _cenario.Lancamentos.ContarCriadosDesdeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);

        var resultado = await CriarHandler().HandleAsync(Comando(), Ct);

        resultado.Error.Type.ShouldBe(ErrorType.BusinessRule);
        resultado.Error.Code.ShouldBe("lancamento.quota_excedida");
        _cenario.Lancamentos.DidNotReceiveWithAnyArgs().Adicionar(default!);
        await _cenario.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QuotaContaDesdeOInicioDoMesEmSaoPaulo()
    {
        await CriarHandler().HandleAsync(Comando(), Ct);

        // 01/09/2026 00:00 em São Paulo = 01/09/2026 03:00 UTC.
        await _cenario.Lancamentos.Received(1).ContarCriadosDesdeAsync(
            new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SemProjecaoDoPlano_AplicaOLimiteDoPlanoFree()
    {
        _cenario.Planos.ObterAsync(Arg.Any<CancellationToken>()).Returns((TenantPlano?)null);
        _cenario.Lancamentos.ContarCriadosDesdeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(TenantPlano.LimitePadrao);

        var resultado = await CriarHandler().HandleAsync(Comando(), Ct);

        resultado.Error.Code.ShouldBe("lancamento.quota_excedida");
        resultado.Error.Message.ShouldContain("free");
    }

    [Fact]
    public async Task Handle_RegraDeDominioViolada_RetornaOErroSemPersistir()
    {
        var resultado = await CriarHandler().HandleAsync(Comando(data: Cenario.Hoje.AddDays(1)), Ct);

        resultado.Error.ShouldBe(LancamentoErros.DataFutura);
        await _cenario.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cenario.Publicador.DidNotReceiveWithAnyArgs().PublishAsync<FluxoCaixa.Contracts.LancamentoRegistrado>(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChaveDeIdempotenciaJaUsada_RetornaOLancamentoOriginalSemCriarOutro()
    {
        var existente = Cenario.LancamentoExistente();
        _cenario.Idempotencia.ObterLancamentoIdAsync("chave-1", Arg.Any<CancellationToken>()).Returns(existente.Id);
        _cenario.Lancamentos.ObterPorIdAsync(existente.Id, Arg.Any<CancellationToken>()).Returns(existente);

        var resultado = await CriarHandler().HandleAsync(Comando(chave: "chave-1"), Ct);

        resultado.Value.Id.ShouldBe(existente.Id);
        _cenario.Lancamentos.DidNotReceiveWithAnyArgs().Adicionar(default!);
        await _cenario.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChaveNova_RegistraAChaveNaMesmaUnidadeDeTrabalho()
    {
        var resultado = await CriarHandler().HandleAsync(Comando(chave: "chave-2"), Ct);

        _cenario.Idempotencia.Received(1).Registrar("chave-2", resultado.Value.Id, Cenario.Agora);
    }

    [Fact]
    public async Task Handle_RequisicaoConcorrenteComMesmaChave_RetornaOLancamentoQueVenceu()
    {
        var vencedor = Cenario.LancamentoExistente();
        _cenario.Idempotencia.ObterLancamentoIdAsync("chave-3", Arg.Any<CancellationToken>()).Returns((Guid?)null, vencedor.Id);
        _cenario.Lancamentos.ObterPorIdAsync(vencedor.Id, Arg.Any<CancellationToken>()).Returns(vencedor);
        _cenario.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new ConflitoDePersistenciaException("chave duplicada")));

        var resultado = await CriarHandler().HandleAsync(Comando(chave: "chave-3"), Ct);

        resultado.Value.Id.ShouldBe(vencedor.Id);
    }
}
