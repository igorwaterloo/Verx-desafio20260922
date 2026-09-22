using FluxoCaixa.SharedKernel;
using Lancamentos.Domain.Lancamentos;
using Shouldly;

namespace Lancamentos.Domain.UnitTests;

public sealed class LancamentoTests
{
    private static readonly Guid Tenant = Guid.Parse("0192f79e-0002-7000-8000-000000000002");

    // 22/09/2026 15:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private static Lancamento CriarValido(TipoLancamento tipo = TipoLancamento.Credito, decimal valor = 150.75m) =>
        Lancamento.Criar(Tenant, tipo, valor, Hoje, "Venda no balcão", "usuario-1", Agora).Value;

    [Fact]
    public void Criar_DadosValidos_PreencheOsCamposERegistraEvento()
    {
        var resultado = Lancamento.Criar(Tenant, TipoLancamento.Debito, 99.90m, Hoje, "  Pagamento fornecedor  ", "usuario-1", Agora);

        resultado.IsSuccess.ShouldBeTrue();
        var lancamento = resultado.Value;
        lancamento.Id.ShouldNotBe(Guid.Empty);
        lancamento.TenantId.ShouldBe(Tenant);
        lancamento.Tipo.ShouldBe(TipoLancamento.Debito);
        lancamento.Valor.Valor.ShouldBe(99.90m);
        lancamento.DataCompetencia.ShouldBe(Hoje);
        lancamento.Descricao.ShouldBe("Pagamento fornecedor");
        lancamento.CriadoPor.ShouldBe("usuario-1");
        lancamento.CriadoEm.ShouldBe(Agora);
        lancamento.Estornado.ShouldBeFalse();
        lancamento.EhEstorno.ShouldBeFalse();
        lancamento.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LancamentoCriado>().LancamentoId.ShouldBe(lancamento.Id);
    }

    [Fact]
    public void Criar_IdsSaoOrdenaveisNoTempo()
    {
        var primeiro = CriarValido();
        var segundo = CriarValido();

        primeiro.Id.Version.ShouldBe(7);
        segundo.Id.ShouldNotBe(primeiro.Id);
    }

    [Fact]
    public void Criar_TenantVazio_LancaExcecao()
    {
        Should.Throw<ArgumentException>(() =>
            Lancamento.Criar(Guid.Empty, TipoLancamento.Credito, 10m, Hoje, "Venda", "u", Agora));
    }

    [Fact]
    public void Criar_TipoInvalido_RetornaErro()
    {
        Lancamento.Criar(Tenant, (TipoLancamento)0, 10m, Hoje, "Venda", "u", Agora)
            .Error.ShouldBe(LancamentoErros.TipoInvalido);
    }

    [Fact]
    public void Criar_ValorInvalido_RetornaErroDoDinheiro()
    {
        Lancamento.Criar(Tenant, TipoLancamento.Credito, -1m, Hoje, "Venda", "u", Agora)
            .Error.ShouldBe(LancamentoErros.ValorDeveSerPositivo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public void Criar_DescricaoInvalida_RetornaErro(string descricao)
    {
        Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje, descricao, "u", Agora)
            .Error.ShouldBe(LancamentoErros.DescricaoInvalida);
    }

    [Fact]
    public void Criar_DescricaoAcimaDe200Caracteres_RetornaErro()
    {
        Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje, new string('x', 201), "u", Agora)
            .Error.ShouldBe(LancamentoErros.DescricaoInvalida);
    }

    [Fact]
    public void Criar_DataFutura_RetornaErro()
    {
        Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje.AddDays(1), "Venda", "u", Agora)
            .Error.ShouldBe(LancamentoErros.DataFutura);
    }

    [Fact]
    public void Criar_DataDeHojeEmSaoPaulo_QuandoEmUtcJaEAmanha_EhAceita()
    {
        // 22/09 23:30 em São Paulo = 23/09 02:30 UTC: "hoje" é 22/09 (RN-02 usa o fuso America/Sao_Paulo).
        var agoraNoFimDoDia = new DateTimeOffset(2026, 9, 23, 2, 30, 0, TimeSpan.Zero);

        Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje, "Venda", "u", agoraNoFimDoDia).IsSuccess.ShouldBeTrue();
        Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje.AddDays(1), "Venda", "u", agoraNoFimDoDia)
            .Error.ShouldBe(LancamentoErros.DataFutura);
    }

    [Fact]
    public void Criar_DataPassada_EhAceita()
    {
        Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje.AddDays(-30), "Venda", "u", Agora).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Estornar_Credito_GeraDebitoComMesmoValorEDataEMarcaOOriginal()
    {
        var original = CriarValido(TipoLancamento.Credito, 150.75m);
        original.ClearDomainEvents();
        var momentoDoEstorno = Agora.AddDays(3);

        var resultado = original.Estornar("admin-1", momentoDoEstorno);

        resultado.IsSuccess.ShouldBeTrue();
        var estorno = resultado.Value;
        estorno.Tipo.ShouldBe(TipoLancamento.Debito);
        estorno.Valor.ShouldBe(original.Valor);
        estorno.DataCompetencia.ShouldBe(original.DataCompetencia);
        estorno.TenantId.ShouldBe(original.TenantId);
        estorno.LancamentoOriginalId.ShouldBe(original.Id);
        estorno.EhEstorno.ShouldBeTrue();
        estorno.CriadoPor.ShouldBe("admin-1");
        estorno.CriadoEm.ShouldBe(momentoDoEstorno);
        estorno.Descricao.ShouldStartWith("Estorno: ");
        original.Estornado.ShouldBeTrue();
        original.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LancamentoEstornado>().EstornoId.ShouldBe(estorno.Id);
    }

    [Fact]
    public void Estornar_Debito_GeraCredito()
    {
        CriarValido(TipoLancamento.Debito).Estornar("admin-1", Agora).Value.Tipo.ShouldBe(TipoLancamento.Credito);
    }

    [Fact]
    public void Estornar_LancamentoJaEstornado_RetornaConflito()
    {
        var original = CriarValido();
        original.Estornar("admin-1", Agora);

        var resultado = original.Estornar("admin-1", Agora);

        resultado.Error.ShouldBe(LancamentoErros.JaEstornado);
        resultado.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Estornar_UmEstorno_RetornaConflito()
    {
        var estorno = CriarValido().Estornar("admin-1", Agora).Value;

        estorno.Estornar("admin-1", Agora).Error.ShouldBe(LancamentoErros.EstornoNaoPodeSerEstornado);
    }

    [Fact]
    public void Estornar_DescricaoLonga_MantemOLimiteDe200Caracteres()
    {
        var original = Lancamento.Criar(Tenant, TipoLancamento.Credito, 10m, Hoje, new string('x', 200), "u", Agora).Value;

        original.Estornar("admin-1", Agora).Value.Descricao.Length.ShouldBe(200);
    }
}
