using System.Globalization;

namespace Consolidado.Application.Saldos;

public sealed record SaldoDiarioDto(
    DateOnly Data,
    decimal TotalCreditos,
    decimal TotalDebitos,
    decimal Saldo,
    int QuantidadeLancamentos)
{
    /// <summary>Dia sem movimento: saldo zero, nunca 404 (RC-03).</summary>
    public static SaldoDiarioDto Vazio(DateOnly data) => new(data, 0m, 0m, 0m, 0);
}

public sealed record ConsolidadoPeriodoDto(
    DateOnly Inicio,
    DateOnly Fim,
    decimal TotalCreditos,
    decimal TotalDebitos,
    decimal Saldo,
    int QuantidadeLancamentos,
    IReadOnlyList<SaldoDiarioDto> Dias);

/// <summary>Chaves de cache sempre prefixadas pelo tenant (ADR-0006, ADR-0015).</summary>
public static class ChavesDeCache
{
    public static string Dia(Guid tenantId, DateOnly data) =>
        $"consolidado:{tenantId}:{Formatar(data)}";

    public static string Periodo(Guid tenantId, DateOnly inicio, DateOnly fim) =>
        $"consolidado:{tenantId}:{Formatar(inicio)}:{Formatar(fim)}";

    private static string Formatar(DateOnly data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// TTLs do cache (ADR-0006): o dia corrente muda a todo lançamento (TTL curto + invalidação pelo
/// Worker); dias passados só mudam por estorno. Períodos não são invalidados individualmente,
/// por isso usam TTL curto.
/// </summary>
public static class PoliticaDeCache
{
    public static readonly TimeSpan TtlDiaCorrente = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TtlDiaPassado = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TtlPeriodo = TimeSpan.FromSeconds(60);

    public static TimeSpan TtlDia(DateOnly data, DateOnly hoje) => data >= hoje ? TtlDiaCorrente : TtlDiaPassado;
}
