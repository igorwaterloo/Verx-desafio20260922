using Consolidado.Application.Abstractions;
using Consolidado.Application.Saldos;
using Consolidado.Application.Saldos.ObterDia;
using Consolidado.Application.Saldos.ObterPeriodo;
using FluentValidation.TestHelper;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace Consolidado.Application.UnitTests;

public sealed class ConsultasDeSaldoTests
{
    private static readonly Guid Tenant = Guid.Parse("0192f79e-0002-7000-8000-000000000002");

    // 22/09/2026 15:00 em São Paulo.
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private readonly ISaldosLeitura _leitura = Substitute.For<ISaldosLeitura>();
    private readonly ICacheConsolidado _cache = Substitute.For<ICacheConsolidado>();
    private readonly TenantContext _tenant = new();
    private readonly FakeTimeProvider _tempo = new(Agora);

    public ConsultasDeSaldoTests() => _tenant.Definir(Tenant, "usuario-1", [TenantRoles.Operador]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ObterSaldoDiarioHandler HandlerDia() => new(_leitura, _cache, _tenant, _tempo);

    private ObterConsolidadoPeriodoHandler HandlerPeriodo() => new(_leitura, _cache, _tenant);

    [Fact]
    public async Task Dia_CacheHit_RetornaSemConsultarOBanco()
    {
        var emCache = new SaldoDiarioDto(Hoje, 10m, 2m, 8m, 3);
        _cache.ObterAsync<SaldoDiarioDto>(ChavesDeCache.Dia(Tenant, Hoje), Arg.Any<CancellationToken>()).Returns(emCache);

        var resultado = await HandlerDia().HandleAsync(new ObterSaldoDiarioQuery(Hoje), Ct);

        resultado.Value.ShouldBe(emCache);
        await _leitura.DidNotReceiveWithAnyArgs().ObterAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dia_CacheMissNoDiaCorrente_ConsultaOBancoEArmazenaComTtlCurto()
    {
        var doBanco = new SaldoDiarioDto(Hoje, 100m, 40m, 60m, 5);
        _leitura.ObterAsync(Hoje, Arg.Any<CancellationToken>()).Returns(doBanco);

        var resultado = await HandlerDia().HandleAsync(new ObterSaldoDiarioQuery(Hoje), Ct);

        resultado.Value.ShouldBe(doBanco);
        await _cache.Received(1).DefinirAsync(ChavesDeCache.Dia(Tenant, Hoje), doBanco, PoliticaDeCache.TtlDiaCorrente, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dia_CacheMissEmDiaPassado_ArmazenaComTtlLongo()
    {
        var ontem = Hoje.AddDays(-1);
        _leitura.ObterAsync(ontem, Arg.Any<CancellationToken>()).Returns(new SaldoDiarioDto(ontem, 1m, 0m, 1m, 1));

        await HandlerDia().HandleAsync(new ObterSaldoDiarioQuery(ontem), Ct);

        await _cache.Received(1).DefinirAsync(ChavesDeCache.Dia(Tenant, ontem), Arg.Any<SaldoDiarioDto>(), PoliticaDeCache.TtlDiaPassado, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dia_SemMovimento_RetornaSaldoZerado()
    {
        _leitura.ObterAsync(Hoje, Arg.Any<CancellationToken>()).Returns((SaldoDiarioDto?)null);

        var resultado = await HandlerDia().HandleAsync(new ObterSaldoDiarioQuery(Hoje), Ct);

        resultado.Value.ShouldBe(SaldoDiarioDto.Vazio(Hoje));
    }

    [Fact]
    public async Task Periodo_PreencheDiasSemMovimentoESomaOsTotais()
    {
        var inicio = Hoje.AddDays(-3);
        _leitura.ListarPeriodoAsync(inicio, Hoje, Arg.Any<CancellationToken>()).Returns(
        [
            new SaldoDiarioDto(inicio, 100m, 0m, 100m, 1),
            new SaldoDiarioDto(Hoje, 50m, 80m, -30m, 2),
        ]);

        var resultado = await HandlerPeriodo().HandleAsync(new ObterConsolidadoPeriodoQuery(inicio, Hoje), Ct);

        var periodo = resultado.Value;
        periodo.Dias.Select(d => d.Data).ShouldBe([inicio, inicio.AddDays(1), inicio.AddDays(2), Hoje]);
        periodo.Dias[1].ShouldBe(SaldoDiarioDto.Vazio(inicio.AddDays(1)));
        periodo.TotalCreditos.ShouldBe(150m);
        periodo.TotalDebitos.ShouldBe(80m);
        periodo.Saldo.ShouldBe(70m);
        periodo.QuantidadeLancamentos.ShouldBe(3);
        await _cache.Received(1).DefinirAsync(ChavesDeCache.Periodo(Tenant, inicio, Hoje), periodo, PoliticaDeCache.TtlPeriodo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Periodo_CacheHit_RetornaSemConsultarOBanco()
    {
        var emCache = new ConsolidadoPeriodoDto(Hoje, Hoje, 0m, 0m, 0m, 0, []);
        _cache.ObterAsync<ConsolidadoPeriodoDto>(ChavesDeCache.Periodo(Tenant, Hoje, Hoje), Arg.Any<CancellationToken>()).Returns(emCache);

        var resultado = await HandlerPeriodo().HandleAsync(new ObterConsolidadoPeriodoQuery(Hoje, Hoje), Ct);

        resultado.Value.ShouldBe(emCache);
        await _leitura.DidNotReceiveWithAnyArgs().ListarPeriodoAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PeriodoValidator_FimAntesDoInicio_TemErro()
    {
        new ObterConsolidadoPeriodoValidator().TestValidate(new ObterConsolidadoPeriodoQuery(Hoje, Hoje.AddDays(-1)))
            .ShouldHaveValidationErrorFor(q => q.Fim);
    }

    [Theory]
    [InlineData(93, true)]
    [InlineData(94, false)]
    public void PeriodoValidator_LimitaA93Dias(int dias, bool valido)
    {
        var inicio = Hoje.AddDays(-(dias - 1));

        new ObterConsolidadoPeriodoValidator().TestValidate(new ObterConsolidadoPeriodoQuery(inicio, Hoje)).IsValid.ShouldBe(valido);
    }

    [Fact]
    public void ChavesDeCache_SaoPrefixadasPeloTenant()
    {
        ChavesDeCache.Dia(Tenant, Hoje).ShouldBe($"consolidado:{Tenant}:2026-09-22");
        ChavesDeCache.Periodo(Tenant, Hoje.AddDays(-1), Hoje).ShouldBe($"consolidado:{Tenant}:2026-09-21:2026-09-22");
    }
}
