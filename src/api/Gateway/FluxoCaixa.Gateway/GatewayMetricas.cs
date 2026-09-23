using System.Diagnostics.Metrics;

namespace FluxoCaixa.Gateway;

/// <summary>
/// Métricas do gateway (ADR-0011): requisições barradas pelo rate limiting, por tipo de limite e
/// plano — evidência do noisy neighbor (SLO-10) e sinal para upgrade de plano. Sem o tenant (cardinalidade).
/// </summary>
public sealed class GatewayMetricas
{
    public const string NomeDoMedidor = "FluxoCaixa.Gateway";

    private readonly Counter<long> _limiteExcedido;

    public GatewayMetricas(IMeterFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _limiteExcedido = fabrica.Create(NomeDoMedidor).CreateCounter<long>(
            "gateway.limite_excedido", unit: "{requisicao}", description: "Requisições recusadas com 429 pelo rate limiting.");
    }

    /// <param name="limite"><c>tenant</c> (vazão do plano) ou <c>ip</c> (rotas públicas e requisições sem tenant).</param>
    public void LimiteExcedido(string limite, string plano) =>
        _limiteExcedido.Add(
            1,
            new KeyValuePair<string, object?>("limite", limite),
            new KeyValuePair<string, object?>("plano", plano));
}
