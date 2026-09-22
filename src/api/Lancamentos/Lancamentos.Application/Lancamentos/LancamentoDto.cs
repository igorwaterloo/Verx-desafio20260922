using Lancamentos.Domain.Lancamentos;
using Contrato = FluxoCaixa.Contracts;

namespace Lancamentos.Application.Lancamentos;

public sealed record LancamentoDto(
    Guid Id,
    TipoLancamento Tipo,
    decimal Valor,
    DateOnly DataCompetencia,
    string Descricao,
    Guid? LancamentoOriginalId,
    bool Estornado,
    string CriadoPor,
    DateTimeOffset CriadoEm)
{
    public static LancamentoDto De(Lancamento lancamento)
    {
        ArgumentNullException.ThrowIfNull(lancamento);

        return new LancamentoDto(
            lancamento.Id,
            lancamento.Tipo,
            lancamento.Valor.Valor,
            lancamento.DataCompetencia,
            lancamento.Descricao,
            lancamento.LancamentoOriginalId,
            lancamento.Estornado,
            lancamento.CriadoPor,
            lancamento.CriadoEm);
    }
}

public sealed record Pagina<T>(IReadOnlyList<T> Itens, int NumeroPagina, int TamanhoPagina, int Total);

internal static class EventosDeIntegracao
{
    /// <summary>Evento público <c>LancamentoRegistrado</c> v1 (também usado para estornos, com o tipo inverso).</summary>
    public static Contrato.LancamentoRegistrado LancamentoRegistrado(Lancamento lancamento, DateTimeOffset ocorridoEm) =>
        new(
            EventId: Guid.CreateVersion7(ocorridoEm),
            OcorridoEm: ocorridoEm,
            TenantId: lancamento.TenantId,
            LancamentoId: lancamento.Id,
            Tipo: lancamento.Tipo == TipoLancamento.Credito ? Contrato.TipoLancamento.Credito : Contrato.TipoLancamento.Debito,
            Valor: lancamento.Valor.Valor,
            DataCompetencia: lancamento.DataCompetencia,
            LancamentoOriginalId: lancamento.LancamentoOriginalId);
}
