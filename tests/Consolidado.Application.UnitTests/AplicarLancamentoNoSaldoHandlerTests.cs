using Consolidado.Application.Abstractions;
using Consolidado.Application.Saldos;
using Consolidado.Application.Saldos.Aplicar;
using Consolidado.Application.Telemetria;
using Consolidado.Domain.Saldos;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Consolidado.Application.UnitTests;

public sealed class AplicarLancamentoNoSaldoHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("0192f79e-0002-7000-8000-000000000002");
    private static readonly DateOnly Dia = new(2026, 9, 22);
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    private readonly ISaldoDiarioRepository _saldos = Substitute.For<ISaldoDiarioRepository>();
    private readonly IInbox _inbox = Substitute.For<IInbox>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICacheConsolidado _cache = Substitute.For<ICacheConsolidado>();
    private readonly TenantContext _tenant = new();

    public AplicarLancamentoNoSaldoHandlerTests() => _tenant.Definir(Tenant, null, []);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AplicarLancamentoNoSaldoHandler CriarHandler() =>
        new(_saldos, _inbox, _unitOfWork, _cache, _tenant, new FakeTimeProvider(Agora), new ConsolidadoMetricas(new MedidoresDeTeste()));

    private static AplicarLancamentoNoSaldoCommand Evento(TipoMovimento tipo = TipoMovimento.Credito, decimal valor = 100m) =>
        new(Guid.NewGuid(), tipo, valor, Dia, Agora.AddSeconds(-2));

    [Fact]
    public async Task Handle_EventoNovoSemSaldoDoDia_CriaAplicaRegistraNaInboxSalvaEInvalidaOCache()
    {
        _saldos.ObterAsync(Dia, Arg.Any<CancellationToken>()).Returns((SaldoDiario?)null);
        var evento = Evento(valor: 150.75m);

        var resultado = await CriarHandler().HandleAsync(evento, Ct);

        resultado.IsSuccess.ShouldBeTrue();
        _saldos.Received(1).Adicionar(Arg.Is<SaldoDiario>(s =>
            s.TenantId == Tenant && s.Data == Dia && s.TotalCreditos == 150.75m && s.QuantidadeLancamentos == 1));
        _inbox.Received(1).Registrar(evento.EventId, Agora);
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _cache.RemoverAsync(ChavesDeCache.Dia(Tenant, Dia), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_SaldoDoDiaExistente_AcumulaNoMesmoRegistro()
    {
        var existente = SaldoDiario.Novo(Tenant, Dia);
        existente.Aplicar(TipoMovimento.Credito, 500m, Agora.AddHours(-1));
        _saldos.ObterAsync(Dia, Arg.Any<CancellationToken>()).Returns(existente);

        await CriarHandler().HandleAsync(Evento(TipoMovimento.Debito, 120m), Ct);

        existente.Saldo.ShouldBe(380m);
        existente.QuantidadeLancamentos.ShouldBe(2);
        _saldos.DidNotReceiveWithAnyArgs().Adicionar(default!);
    }

    [Fact]
    public async Task Handle_EventoJaProcessado_NaoAlteraOSaldoNemSalva()
    {
        var evento = Evento();
        _inbox.JaProcessadaAsync(evento.EventId, Arg.Any<CancellationToken>()).Returns(true);

        var resultado = await CriarHandler().HandleAsync(evento, Ct);

        resultado.IsSuccess.ShouldBeTrue();
        await _saldos.DidNotReceiveWithAnyArgs().ObterAsync(default, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConflitoAoSalvar_PropagaParaARetentativaDoConsumidor()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConflitoDePersistenciaException("rowversion"));

        await Should.ThrowAsync<ConflitoDePersistenciaException>(() => CriarHandler().HandleAsync(Evento(), Ct));
        await _cache.DidNotReceiveWithAnyArgs().RemoverAsync(default!, Arg.Any<CancellationToken>());
    }
}
