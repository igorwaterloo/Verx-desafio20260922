using System.Text.Json.Serialization;

namespace FluxoCaixa.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<TipoLancamento>))]
public enum TipoLancamento
{
    Credito = 1,
    Debito = 2,
}

/// <summary>
/// Um lançamento (inclusive estorno, com o tipo inverso) foi registrado.
/// Produtor: Lançamentos. Consumidor: Consolidado.
/// </summary>
public sealed record LancamentoRegistrado(
    Guid EventId,
    DateTimeOffset OcorridoEm,
    Guid TenantId,
    Guid LancamentoId,
    TipoLancamento Tipo,
    decimal Valor,
    DateOnly DataCompetencia,
    Guid? LancamentoOriginalId) : IIntegrationEvent
{
    public int Versao => 1;
}
