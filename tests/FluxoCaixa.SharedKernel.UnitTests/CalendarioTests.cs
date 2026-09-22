using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class CalendarioTests
{
    [Fact]
    public void DataEmSaoPaulo_ConverteParaOFusoDeBrasilia()
    {
        // 23/09 02:30 UTC ainda é 22/09 23:30 em São Paulo.
        Calendario.DataEmSaoPaulo(new DateTimeOffset(2026, 9, 23, 2, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 9, 22));
        Calendario.DataEmSaoPaulo(new DateTimeOffset(2026, 9, 23, 3, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 9, 23));
    }

    [Fact]
    public void InicioDoMes_RetornaAMeiaNoiteDoDia1EmSaoPauloEmUtc()
    {
        Calendario.InicioDoMes(new DateTimeOffset(2026, 9, 22, 18, 0, 0, TimeSpan.Zero))
            .ShouldBe(new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void InicioDoMes_NaViradaDoMesEmUtc_UsaOMesDeSaoPaulo()
    {
        // 01/10 01:00 UTC ainda é 30/09 em São Paulo: o mês corrente é setembro.
        Calendario.InicioDoMes(new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero))
            .ShouldBe(new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero));
    }
}
