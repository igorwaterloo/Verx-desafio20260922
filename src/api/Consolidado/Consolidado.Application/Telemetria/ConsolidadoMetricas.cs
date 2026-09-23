using System.Diagnostics.Metrics;

namespace Consolidado.Application.Telemetria;

/// <summary>
/// Métricas de negócio do Consolidado (ADR-0011). O <c>consolidado.atraso</c> mede o SLO-07 em
/// produção: tempo entre o lançamento (evento) e o saldo gravado. Sem tenant nos atributos (cardinalidade).
/// </summary>
public sealed class ConsolidadoMetricas
{
    public const string NomeDoMedidor = "FluxoCaixa.Consolidado";

    private readonly Histogram<double> _atraso;
    private readonly Counter<long> _eventos;
    private readonly Counter<long> _leiturasDoCache;

    public ConsolidadoMetricas(IMeterFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        var medidor = fabrica.Create(NomeDoMedidor);
        _atraso = medidor.CreateHistogram(
            "consolidado.atraso",
            unit: "s",
            description: "Tempo entre o lançamento e o saldo consolidado gravado (SLO-07 < 5 s).",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 60, 300] });
        _eventos = medidor.CreateCounter<long>(
            "consolidado.eventos", unit: "{evento}", description: "Eventos LancamentoRegistrado consumidos, por resultado.");
        _leiturasDoCache = medidor.CreateCounter<long>(
            "consolidado.cache.leituras", unit: "{leitura}", description: "Leituras do cache-aside, por resultado (ADR-0006).");
    }

    public void EventoAplicado(TimeSpan atraso)
    {
        _atraso.Record(Math.Max(0, atraso.TotalSeconds));
        _eventos.Add(1, new KeyValuePair<string, object?>("resultado", "aplicado"));
    }

    /// <summary>Reentrega já processada (inbox): descartada sem alterar o saldo.</summary>
    public void EventoDuplicado() => _eventos.Add(1, new KeyValuePair<string, object?>("resultado", "duplicado"));

    public void LeituraDoCache(ResultadoDoCache resultado) =>
        _leiturasDoCache.Add(1, new KeyValuePair<string, object?>("resultado", resultado switch
        {
            ResultadoDoCache.Acerto => "acerto",
            ResultadoDoCache.Falta => "falta",
            _ => "indisponivel",
        }));
}

public enum ResultadoDoCache
{
    Acerto,
    Falta,

    /// <summary>Redis fora ou disjuntor aberto: a consulta foi ao banco.</summary>
    Indisponivel,
}
