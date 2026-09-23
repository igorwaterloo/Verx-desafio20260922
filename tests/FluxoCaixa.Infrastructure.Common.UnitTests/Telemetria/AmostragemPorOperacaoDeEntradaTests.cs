using System.Diagnostics;
using FluxoCaixa.Infrastructure.Common.Telemetria;
using OpenTelemetry.Trace;
using Shouldly;

namespace FluxoCaixa.Infrastructure.Common.UnitTests.Telemetria;

/// <summary>
/// Traces só começam em operações de entrada (requisição, consumo, publicação). Atividade de fundo
/// sem pai — varredura do outbox/inbox no SQL, sondas de health check — não vira trace.
/// </summary>
public sealed class AmostragemPorOperacaoDeEntradaTests
{
    private readonly Sampler _amostragem = AmostragemPorOperacaoDeEntrada.Criar();

    [Theory]
    [InlineData(ActivityKind.Server)]
    [InlineData(ActivityKind.Consumer)]
    [InlineData(ActivityKind.Producer)]
    public void RaizDeEntrada_EhGravada(ActivityKind tipo) =>
        Decidir(default, tipo).ShouldBe(SamplingDecision.RecordAndSample);

    [Theory]
    [InlineData(ActivityKind.Client)]
    [InlineData(ActivityKind.Internal)]
    public void RaizDeFundo_EhDescartada(ActivityKind tipo) =>
        Decidir(default, tipo).ShouldBe(SamplingDecision.Drop);

    [Fact]
    public void FilhoDeTraceGravado_EhGravadoMesmoSendoConsultaSql()
    {
        var pai = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: false);

        Decidir(pai, ActivityKind.Client).ShouldBe(SamplingDecision.RecordAndSample);
    }

    private SamplingDecision Decidir(ActivityContext pai, ActivityKind tipo) =>
        _amostragem.ShouldSample(new SamplingParameters(pai, ActivityTraceId.CreateRandom(), "operacao", tipo)).Decision;
}
