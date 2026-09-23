using System.Diagnostics.Metrics;
using Lancamentos.Domain.Lancamentos;

namespace Lancamentos.Application.Telemetria;

/// <summary>
/// Métricas de negócio do Lançamentos (ADR-0011), com a API de métricas do .NET: a Application não
/// depende do OpenTelemetry; o host exporta o medidor <see cref="NomeDoMedidor"/>. Os atributos não
/// incluem o tenant — num SaaS isso explodiria a cardinalidade; o tenant fica nos traces e nos logs.
/// </summary>
public sealed class LancamentosMetricas
{
    public const string NomeDoMedidor = "FluxoCaixa.Lancamentos";

    private readonly Counter<long> _registrados;
    private readonly Counter<long> _quotaExcedida;

    public LancamentosMetricas(IMeterFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        var medidor = fabrica.Create(NomeDoMedidor);
        _registrados = medidor.CreateCounter<long>(
            "lancamentos.registrados", unit: "{lancamento}", description: "Lançamentos gravados (registros e estornos).");
        _quotaExcedida = medidor.CreateCounter<long>(
            "lancamentos.quota_excedida", unit: "{requisicao}", description: "Registros recusados pela quota mensal do plano (RN-09).");
    }

    public void Registrado(Lancamento lancamento)
    {
        ArgumentNullException.ThrowIfNull(lancamento);
        _registrados.Add(
            1,
            new KeyValuePair<string, object?>("tipo", lancamento.Tipo == TipoLancamento.Credito ? "credito" : "debito"),
            new KeyValuePair<string, object?>("origem", lancamento.LancamentoOriginalId is null ? "registro" : "estorno"));
    }

    public void QuotaExcedida(string plano) =>
        _quotaExcedida.Add(1, new KeyValuePair<string, object?>("plano", plano));
}
