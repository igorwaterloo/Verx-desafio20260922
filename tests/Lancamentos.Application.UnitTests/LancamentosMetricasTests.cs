using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Lancamentos.Estornar;
using Lancamentos.Application.Lancamentos.Registrar;
using Lancamentos.Application.Telemetria;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NSubstitute;
using Shouldly;

namespace Lancamentos.Application.UnitTests;

/// <summary>Métricas de negócio do Lançamentos (ADR-0011): sem tenant nos atributos (cardinalidade).</summary>
public sealed class LancamentosMetricasTests : IDisposable
{
    private readonly MedidoresDeTeste _medidores = new();
    private readonly LancamentosMetricas _metricas;
    private readonly MetricCollector<long> _registrados;
    private readonly MetricCollector<long> _quotaExcedida;

    public LancamentosMetricasTests()
    {
        _metricas = new LancamentosMetricas(_medidores);
        _registrados = new MetricCollector<long>(_medidores, LancamentosMetricas.NomeDoMedidor, "lancamentos.registrados");
        _quotaExcedida = new MetricCollector<long>(_medidores, LancamentosMetricas.NomeDoMedidor, "lancamentos.quota_excedida");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registro_ContaOLancamentoPorTipoEOrigem()
    {
        var cenario = new Cenario();

        await Handler(cenario).HandleAsync(Comando(), Ct);

        var medicao = _registrados.GetMeasurementSnapshot().ShouldHaveSingleItem();
        medicao.Value.ShouldBe(1);
        medicao.Tags["tipo"].ShouldBe("credito");
        medicao.Tags["origem"].ShouldBe("registro");
        medicao.Tags.ShouldNotContainKey("tenant.id");
    }

    [Fact]
    public async Task ReenvioIdempotente_NaoContaDeNovo()
    {
        var cenario = new Cenario();
        var existente = Lancamento.Criar(Cenario.Tenant, TipoLancamento.Credito, 10m, Cenario.Hoje, "Venda", "usuario-1", Cenario.Agora).Value;
        cenario.Idempotencia.ObterLancamentoIdAsync("chave", Arg.Any<CancellationToken>()).Returns(existente.Id);
        cenario.Lancamentos.ObterPorIdAsync(existente.Id, Arg.Any<CancellationToken>()).Returns(existente);

        await Handler(cenario).HandleAsync(Comando("chave"), Ct);

        _registrados.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task QuotaExcedida_ContaPorPlanoENaoRegistra()
    {
        var cenario = new Cenario();
        cenario.Planos.ObterAsync(Arg.Any<CancellationToken>()).Returns(TenantPlano.Criar(Cenario.Tenant, "free", 1, Cenario.Agora));
        cenario.Lancamentos.ContarCriadosDesdeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(1);

        await Handler(cenario).HandleAsync(Comando(), Ct);

        _quotaExcedida.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["plano"].ShouldBe("free");
        _registrados.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Estorno_ContaComOTipoInversoEOrigemEstorno()
    {
        var cenario = new Cenario(TenantRoles.Admin);
        var original = Lancamento.Criar(Cenario.Tenant, TipoLancamento.Credito, 10m, Cenario.Hoje, "Venda", "usuario-1", Cenario.Agora).Value;
        cenario.Lancamentos.ObterPorIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);

        await new EstornarLancamentoHandler(cenario.Lancamentos, cenario.Publicador, cenario.UnitOfWork, cenario.Tenancy, cenario.Tempo, _metricas)
            .HandleAsync(new EstornarLancamentoCommand(original.Id), Ct);

        var medicao = _registrados.GetMeasurementSnapshot().ShouldHaveSingleItem();
        medicao.Tags["tipo"].ShouldBe("debito");
        medicao.Tags["origem"].ShouldBe("estorno");
    }

    public void Dispose()
    {
        _registrados.Dispose();
        _quotaExcedida.Dispose();
        _medidores.Dispose();
    }

    private RegistrarLancamentoHandler Handler(Cenario c) => new(
        c.Lancamentos, c.Planos, c.Idempotencia, c.Publicador, c.UnitOfWork, c.Tenancy, c.Tempo, _metricas);

    private static RegistrarLancamentoCommand Comando(string? chave = null) =>
        new(TipoLancamento.Credito, 100m, Cenario.Hoje, "Venda no balcão", chave);
}
