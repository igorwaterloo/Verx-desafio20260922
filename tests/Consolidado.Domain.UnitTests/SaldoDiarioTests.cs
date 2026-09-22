using Consolidado.Domain.Saldos;
using Shouldly;

namespace Consolidado.Domain.UnitTests;

public sealed class SaldoDiarioTests
{
    private static readonly Guid Tenant = Guid.Parse("0192f79e-0002-7000-8000-000000000002");
    private static readonly DateOnly Dia = new(2026, 9, 22);
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Novo_IniciaZeradoParaOTenantEODia()
    {
        var saldo = SaldoDiario.Novo(Tenant, Dia);

        saldo.TenantId.ShouldBe(Tenant);
        saldo.Data.ShouldBe(Dia);
        saldo.TotalCreditos.ShouldBe(0m);
        saldo.TotalDebitos.ShouldBe(0m);
        saldo.Saldo.ShouldBe(0m);
        saldo.QuantidadeLancamentos.ShouldBe(0);
    }

    [Fact]
    public void Novo_TenantVazio_LancaExcecao()
    {
        Should.Throw<ArgumentException>(() => SaldoDiario.Novo(Guid.Empty, Dia));
    }

    [Fact]
    public void Aplicar_CreditosEDebitos_AcumulaTotaisESaldo()
    {
        var saldo = SaldoDiario.Novo(Tenant, Dia);

        saldo.Aplicar(TipoMovimento.Credito, 1500.50m, T0);
        saldo.Aplicar(TipoMovimento.Credito, 200m, T0.AddMinutes(1));
        saldo.Aplicar(TipoMovimento.Debito, 700.25m, T0.AddMinutes(2));

        saldo.TotalCreditos.ShouldBe(1700.50m);
        saldo.TotalDebitos.ShouldBe(700.25m);
        saldo.Saldo.ShouldBe(1000.25m);
        saldo.QuantidadeLancamentos.ShouldBe(3);
        saldo.AtualizadoEm.ShouldBe(T0.AddMinutes(2));
    }

    [Fact]
    public void Aplicar_DebitosMaioresQueCreditos_SaldoNegativo()
    {
        var saldo = SaldoDiario.Novo(Tenant, Dia);

        saldo.Aplicar(TipoMovimento.Debito, 50m, T0);

        saldo.Saldo.ShouldBe(-50m);
    }

    [Fact]
    public void Aplicar_OrdemDosEventosNaoAlteraOResultado()
    {
        // RC-02: a aplicação é comutativa — eventos fora de ordem produzem o mesmo saldo.
        var emOrdem = SaldoDiario.Novo(Tenant, Dia);
        emOrdem.Aplicar(TipoMovimento.Credito, 100m, T0);
        emOrdem.Aplicar(TipoMovimento.Debito, 30m, T0.AddMinutes(1));
        emOrdem.Aplicar(TipoMovimento.Credito, 10m, T0.AddMinutes(2));

        var foraDeOrdem = SaldoDiario.Novo(Tenant, Dia);
        foraDeOrdem.Aplicar(TipoMovimento.Credito, 10m, T0.AddMinutes(2));
        foraDeOrdem.Aplicar(TipoMovimento.Debito, 30m, T0.AddMinutes(1));
        foraDeOrdem.Aplicar(TipoMovimento.Credito, 100m, T0);

        foraDeOrdem.Saldo.ShouldBe(emOrdem.Saldo);
        foraDeOrdem.TotalCreditos.ShouldBe(emOrdem.TotalCreditos);
        foraDeOrdem.TotalDebitos.ShouldBe(emOrdem.TotalDebitos);
        foraDeOrdem.AtualizadoEm.ShouldBe(emOrdem.AtualizadoEm);
    }

    [Fact]
    public void Aplicar_EstornoDeUmCredito_ZeraOEfeitoNoSaldo()
    {
        var saldo = SaldoDiario.Novo(Tenant, Dia);

        saldo.Aplicar(TipoMovimento.Credito, 80m, T0);
        saldo.Aplicar(TipoMovimento.Debito, 80m, T0.AddMinutes(5));

        saldo.Saldo.ShouldBe(0m);
        saldo.QuantidadeLancamentos.ShouldBe(2);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Aplicar_ValorNaoPositivo_LancaExcecao(string valor)
    {
        var saldo = SaldoDiario.Novo(Tenant, Dia);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            saldo.Aplicar(TipoMovimento.Credito, decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture), T0));
    }

    [Fact]
    public void Aplicar_TipoInvalido_LancaExcecao()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            SaldoDiario.Novo(Tenant, Dia).Aplicar((TipoMovimento)0, 10m, T0));
    }

    [Fact]
    public void MensagemProcessada_RegistraOEventoDoTenant()
    {
        var eventId = Guid.NewGuid();

        var mensagem = MensagemProcessada.Registrar(eventId, Tenant, T0);

        mensagem.EventId.ShouldBe(eventId);
        mensagem.TenantId.ShouldBe(Tenant);
        mensagem.ProcessadaEm.ShouldBe(T0);
    }
}
