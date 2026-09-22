namespace FluxoCaixa.SharedKernel;

/// <summary>
/// Datas de negócio no fuso do comerciante (America/Sao_Paulo). O Brasil não adota horário
/// de verão desde 2019; se o fuso não estiver disponível no sistema, usa UTC-3.
/// </summary>
public static class Calendario
{
    private static readonly TimeZoneInfo FusoSaoPaulo = ObterFuso();

    public static DateOnly DataEmSaoPaulo(DateTimeOffset instante) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, FusoSaoPaulo).DateTime);

    /// <summary>Primeiro instante do mês (em São Paulo) do <paramref name="instante"/>, em UTC.</summary>
    public static DateTimeOffset InicioDoMes(DateTimeOffset instante)
    {
        var local = TimeZoneInfo.ConvertTime(instante, FusoSaoPaulo);
        var inicioLocal = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(inicioLocal, FusoSaoPaulo.GetUtcOffset(inicioLocal)).ToUniversalTime();
    }

    private static TimeZoneInfo ObterFuso()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("America/Sao_Paulo", TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }
}
