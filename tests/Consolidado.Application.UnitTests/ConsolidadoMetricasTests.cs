using Consolidado.Application.Abstractions;
using Consolidado.Application.Saldos.Aplicar;
using Consolidado.Application.Telemetria;
using Consolidado.Domain.Saldos;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace Consolidado.Application.UnitTests;

/// <summary>Métricas do Consolidado: atraso da consolidação (SLO-07) e eventos por resultado.</summary>
public sealed class ConsolidadoMetricasTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    private readonly MedidoresDeTeste _medidores = new();
    private readonly ConsolidadoMetricas _metricas;
    private readonly MetricCollector<double> _atraso;
    private readonly MetricCollector<long> _eventos;
    private readonly IInbox _inbox = Substitute.For<IInbox>();
    private readonly TenantContext _tenant = new();

    public ConsolidadoMetricasTests()
    {
        _metricas = new ConsolidadoMetricas(_medidores);
        _atraso = new MetricCollector<double>(_medidores, ConsolidadoMetricas.NomeDoMedidor, "consolidado.atraso");
        _eventos = new MetricCollector<long>(_medidores, ConsolidadoMetricas.NomeDoMedidor, "consolidado.eventos");
        _tenant.Definir(Guid.NewGuid(), null, []);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EventoAplicado_RegistraOAtrasoDesdeOLancamento()
    {
        await Handler().HandleAsync(Evento(ocorridoEm: Agora.AddSeconds(-1.5)), Ct);

        _atraso.GetMeasurementSnapshot().ShouldHaveSingleItem().Value.ShouldBe(1.5, tolerance: 0.001);
        _eventos.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["resultado"].ShouldBe("aplicado");
    }

    [Fact]
    public async Task EventoDuplicado_ContaComoDuplicadoSemAtraso()
    {
        var evento = Evento(ocorridoEm: Agora.AddSeconds(-1));
        _inbox.JaProcessadaAsync(evento.EventId, Arg.Any<CancellationToken>()).Returns(true);

        await Handler().HandleAsync(evento, Ct);

        _atraso.GetMeasurementSnapshot().ShouldBeEmpty();
        _eventos.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["resultado"].ShouldBe("duplicado");
    }

    [Theory]
    [InlineData(ResultadoDoCache.Acerto, "acerto")]
    [InlineData(ResultadoDoCache.Falta, "falta")]
    [InlineData(ResultadoDoCache.Indisponivel, "indisponivel")]
    public void LeituraDoCache_ContaPorResultado(ResultadoDoCache resultado, string esperado)
    {
        using var leituras = new MetricCollector<long>(_medidores, ConsolidadoMetricas.NomeDoMedidor, "consolidado.cache.leituras");

        _metricas.LeituraDoCache(resultado);

        leituras.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["resultado"].ShouldBe(esperado);
    }

    [Fact]
    public void Retentativa_ContaOReprocessamento()
    {
        using var retentativas = new MetricCollector<long>(_medidores, ConsolidadoMetricas.NomeDoMedidor, "consolidado.retentativas");

        _metricas.Retentativa();

        retentativas.GetMeasurementSnapshot().ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    public void Dispose()
    {
        _atraso.Dispose();
        _eventos.Dispose();
        _medidores.Dispose();
    }

    private AplicarLancamentoNoSaldoHandler Handler() => new(
        Substitute.For<ISaldoDiarioRepository>(), _inbox, Substitute.For<IUnitOfWork>(), Substitute.For<ICacheConsolidado>(),
        _tenant, new FakeTimeProvider(Agora), _metricas);

    private static AplicarLancamentoNoSaldoCommand Evento(DateTimeOffset ocorridoEm) =>
        new(Guid.NewGuid(), TipoMovimento.Credito, 10m, new DateOnly(2026, 9, 22), ocorridoEm);
}
