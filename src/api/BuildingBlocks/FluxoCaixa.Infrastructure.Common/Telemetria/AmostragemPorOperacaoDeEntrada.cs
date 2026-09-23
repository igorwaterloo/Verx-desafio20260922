using System.Diagnostics;
using OpenTelemetry.Trace;

namespace FluxoCaixa.Infrastructure.Common.Telemetria;

/// <summary>
/// Um trace só começa numa operação de entrada: requisição recebida (<c>Server</c>), mensagem consumida
/// (<c>Consumer</c>) ou publicada (<c>Producer</c>). Atividade de fundo sem pai — varredura do outbox e do
/// inbox do MassTransit no SQL a cada segundo, sondas de health check do gateway — é descartada; dentro
/// de um trace existente, tudo é gravado (<see cref="ParentBasedSampler"/>).
/// </summary>
public sealed class AmostragemPorOperacaoDeEntrada : Sampler
{
    private static readonly SamplingResult Gravar = new(SamplingDecision.RecordAndSample);
    private static readonly SamplingResult Descartar = new(SamplingDecision.Drop);

    private AmostragemPorOperacaoDeEntrada()
    {
        Description = nameof(AmostragemPorOperacaoDeEntrada);
    }

    /// <summary>Amostragem para spans raiz por tipo de operação; filhos seguem a decisão do pai.</summary>
    public static Sampler Criar() => new ParentBasedSampler(new AmostragemPorOperacaoDeEntrada());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.Kind is ActivityKind.Server or ActivityKind.Consumer or ActivityKind.Producer
            ? Gravar
            : Descartar;
}
