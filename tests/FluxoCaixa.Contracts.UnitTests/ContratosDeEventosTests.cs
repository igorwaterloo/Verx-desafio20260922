using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace FluxoCaixa.Contracts.UnitTests;

/// <summary>
/// Testes de contrato: o JSON dos eventos deve corresponder ao formato publicado em docs/dominio.md.
/// Uma falha aqui indica mudança incompatível com os consumidores.
/// </summary>
public sealed class ContratosDeEventosTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Guid TenantId = Guid.Parse("0192f79e-1111-7aaa-8bbb-0123456789ab");

    [Fact]
    public void LancamentoRegistrado_SerializaNoFormatoDoContratoV1()
    {
        var evento = new LancamentoRegistrado(
            EventId: Guid.Parse("0192f7a0-8c1e-7c3a-9b6e-2f1d8e4a5b6c"),
            OcorridoEm: new DateTimeOffset(2026, 9, 22, 14, 30, 0, TimeSpan.Zero),
            TenantId: TenantId,
            LancamentoId: Guid.Parse("0192f7a0-8c1d-7a11-8f00-1a2b3c4d5e6f"),
            Tipo: TipoLancamento.Credito,
            Valor: 150.75m,
            DataCompetencia: new DateOnly(2026, 9, 22),
            LancamentoOriginalId: null);

        var json = JsonNode.Parse(JsonSerializer.Serialize(evento, Web))!.AsObject();

        json.Select(p => p.Key).ShouldBe(
            ["eventId", "ocorridoEm", "tenantId", "lancamentoId", "tipo", "valor", "dataCompetencia", "lancamentoOriginalId", "versao"],
            ignoreOrder: true);
        json["tipo"]!.GetValue<string>().ShouldBe("Credito");
        json["valor"]!.GetValue<decimal>().ShouldBe(150.75m);
        json["dataCompetencia"]!.GetValue<string>().ShouldBe("2026-09-22");
        json["versao"]!.GetValue<int>().ShouldBe(1);
        json["tenantId"]!.GetValue<Guid>().ShouldBe(TenantId);
    }

    [Fact]
    public void LancamentoRegistrado_DesserializaOJsonDocumentado()
    {
        const string json = """
            {
              "eventId": "0192f7a0-8c1e-7c3a-9b6e-2f1d8e4a5b6c",
              "ocorridoEm": "2026-09-22T14:30:00Z",
              "versao": 1,
              "tenantId": "0192f79e-1111-7aaa-8bbb-0123456789ab",
              "lancamentoId": "0192f7a0-8c1d-7a11-8f00-1a2b3c4d5e6f",
              "tipo": "Debito",
              "valor": 99.90,
              "dataCompetencia": "2026-09-22",
              "lancamentoOriginalId": null
            }
            """;

        var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(json, Web)!;

        evento.Tipo.ShouldBe(TipoLancamento.Debito);
        evento.Valor.ShouldBe(99.90m);
        evento.DataCompetencia.ShouldBe(new DateOnly(2026, 9, 22));
        evento.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public void EventosDePlano_SerializamNoFormatoDoContratoV1()
    {
        IIntegrationEvent[] eventos =
        [
            new TenantProvisionado(Guid.NewGuid(), DateTimeOffset.UtcNow, TenantId, "pro", 50_000, 20),
            new PlanoDoTenantAlterado(Guid.NewGuid(), DateTimeOffset.UtcNow, TenantId, "free", 1_000, 2),
        ];

        foreach (var evento in eventos)
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(evento, evento.GetType(), Web))!.AsObject();

            json.Select(p => p.Key).ShouldBe(
                ["eventId", "ocorridoEm", "tenantId", "planoCodigo", "limiteLancamentosMes", "limiteUsuarios", "versao"],
                ignoreOrder: true);
        }
    }

    [Fact]
    public void TodosOsEventos_SaoTiposSelados()
    {
        var tiposDeEvento = typeof(IIntegrationEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .ToList();

        tiposDeEvento.ShouldNotBeEmpty();
        tiposDeEvento.ShouldAllBe(t => t.IsSealed);
    }
}
