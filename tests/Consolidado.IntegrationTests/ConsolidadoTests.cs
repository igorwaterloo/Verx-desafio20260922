using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Consolidado.Application.Saldos;
using Consolidado.IntegrationTests.Infraestrutura;
using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel;
using Shouldly;

namespace Consolidado.IntegrationTests;

/// <summary>
/// Evento LancamentoRegistrado → RabbitMQ → Consolidado.Worker (inbox + saldo) → Consolidado.Api
/// (cache-aside no Redis), com containers reais.
/// </summary>
public sealed class ConsolidadoTests(AmbienteDeTeste ambiente)
{
    private const string Rota = "/api/v1/consolidado";
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);

    private static DateOnly Hoje => Calendario.DataEmSaoPaulo(DateTimeOffset.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Eventos_AtualizamOSaldoDoDia()
    {
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);

        await PublicarAsync(tenant, TipoLancamento.Credito, 1500.50m);
        await PublicarAsync(tenant, TipoLancamento.Credito, 200m);
        await PublicarAsync(tenant, TipoLancamento.Debito, 700.25m);

        var saldo = await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 3);
        saldo.TotalCreditos.ShouldBe(1700.50m);
        saldo.TotalDebitos.ShouldBe(700.25m);
        saldo.Saldo.ShouldBe(1000.25m);
    }

    [Fact]
    public async Task EventoDuplicado_EhAplicadoUmaUnicaVez()
    {
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);
        var evento = Evento(tenant, TipoLancamento.Credito, 100m);

        await ambiente.Publicador.Publish(evento, Ct);
        await ambiente.Publicador.Publish(evento, Ct); // reentrega (at-least-once)
        await PublicarAsync(tenant, TipoLancamento.Credito, 1m);

        await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 2);
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);

        var saldo = await ObterDiaAsync(cliente, Hoje);
        saldo.QuantidadeLancamentos.ShouldBe(2);
        saldo.TotalCreditos.ShouldBe(101m);
    }

    [Fact]
    public async Task RajadaNoMesmoDia_EhAplicadaInteiraEmPoucosSegundos()
    {
        // Caso real de pico: todos os lançamentos do comerciante caem na mesma linha (tenant + dia).
        // Consumidores concorrentes na mesma linha geravam conflitos de concorrência otimista, que iam
        // para o retry exponencial (atraso de segundos e risco de DLQ). O particionamento por
        // tenant + data aplica essas mensagens em série, sem conflitos.
        const int Quantidade = 300;
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);

        var cronometro = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, Quantidade)
            .Select(_ => PublicarAsync(tenant, TipoLancamento.Credito, 1m)));

        var saldo = await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == Quantidade);
        cronometro.Stop();

        saldo.TotalCreditos.ShouldBe(Quantidade);
        cronometro.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8), "a rajada deve convergir sem esperar retentativas");
    }

    [Fact]
    public async Task EstornoDeCredito_AnulaOEfeitoNoSaldo()
    {
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);
        var original = Evento(tenant, TipoLancamento.Credito, 80m);

        await ambiente.Publicador.Publish(original, Ct);
        await ambiente.Publicador.Publish(
            Evento(tenant, TipoLancamento.Debito, 80m) with { LancamentoOriginalId = original.LancamentoId }, Ct);

        var saldo = await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 2);
        saldo.Saldo.ShouldBe(0m);
    }

    [Fact]
    public async Task DiaSemMovimento_RetornaSaldoZerado()
    {
        using var cliente = ambiente.Api.ClienteDo(Guid.NewGuid());

        var saldo = await ObterDiaAsync(cliente, Hoje.AddDays(-10));

        saldo.ShouldBe(SaldoDiarioDto.Vazio(Hoje.AddDays(-10)));
    }

    [Fact]
    public async Task Periodo_PreencheDiasSemMovimentoESomaOsTotais()
    {
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);
        var inicio = Hoje.AddDays(-3);

        await PublicarAsync(tenant, TipoLancamento.Credito, 300m, Hoje.AddDays(-2));
        await PublicarAsync(tenant, TipoLancamento.Debito, 50m, Hoje);
        await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 1);
        await AguardarSaldoAsync(cliente, Hoje.AddDays(-2), s => s.QuantidadeLancamentos == 1);

        using var resposta = await cliente.GetAsync($"{Rota}?inicio={Data(inicio)}&fim={Data(Hoje)}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var periodo = (await resposta.Content.ReadFromJsonAsync<ConsolidadoPeriodoDto>(Ct))!;
        periodo.Dias.Count.ShouldBe(4);
        periodo.Dias[0].ShouldBe(SaldoDiarioDto.Vazio(inicio));
        periodo.TotalCreditos.ShouldBe(300m);
        periodo.TotalDebitos.ShouldBe(50m);
        periodo.Saldo.ShouldBe(250m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(93)]
    public async Task Periodo_Invalido_Retorna400(int diasAtePassado)
    {
        using var cliente = ambiente.Api.ClienteDo(Guid.NewGuid());
        var inicio = diasAtePassado < 0 ? Hoje.AddDays(1) : Hoje.AddDays(-diasAtePassado);

        using var resposta = await cliente.GetAsync($"{Rota}?inicio={Data(inicio)}&fim={Data(Hoje)}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Isolamento_TenantNaoEnxergaOSaldoDeOutroTenant()
    {
        var tenantA = Guid.NewGuid();
        using var clienteA = ambiente.Api.ClienteDo(tenantA);
        using var clienteB = ambiente.Api.ClienteDo(Guid.NewGuid());

        await PublicarAsync(tenantA, TipoLancamento.Credito, 999m);
        await AguardarSaldoAsync(clienteA, Hoje, s => s.QuantidadeLancamentos == 1);

        (await ObterDiaAsync(clienteB, Hoje)).ShouldBe(SaldoDiarioDto.Vazio(Hoje));
    }

    [Fact]
    public async Task Cache_ConsultaArmazenaNoRedisENovoEventoInvalida()
    {
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);
        var chave = ChavesDeCache.Dia(tenant, Hoje);

        await PublicarAsync(tenant, TipoLancamento.Credito, 10m);
        await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 1);

        // O disjuntor pode estar aberto por um teste anterior (Redis pausado): a consulta volta a
        // popular o cache assim que ele fecha — sem intervenção manual.
        await AguardarAsync(async () =>
        {
            await ObterDiaAsync(cliente, Hoje);
            return await ambiente.Redis.KeyExistsAsync(chave);
        }, "A consulta deveria ter populado o cache.");

        await PublicarAsync(tenant, TipoLancamento.Credito, 5m);

        var saldo = await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 2);
        saldo.TotalCreditos.ShouldBe(15m);
    }

    [Fact]
    public async Task RedisIndisponivel_ConsultaContinuaRespondendoPeloBanco()
    {
        var tenant = Guid.NewGuid();
        using var cliente = ambiente.Api.ClienteDo(tenant);
        await PublicarAsync(tenant, TipoLancamento.Credito, 42m);
        await AguardarSaldoAsync(cliente, Hoje, s => s.QuantidadeLancamentos == 1);

        await ambiente.PausarRedisAsync();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var cronometro = Stopwatch.StartNew();
                using var resposta = await cliente.GetAsync($"{Rota}/{Data(Hoje)}", Ct);
                cronometro.Stop();

                resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await resposta.Content.ReadFromJsonAsync<SaldoDiarioDto>(Ct))!.TotalCreditos.ShouldBe(42m);
                cronometro.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
            }
        }
        finally
        {
            await ambiente.RetomarRedisAsync();
        }
    }

    [Fact]
    public async Task SemAutenticacao_Retorna401ESemTenant_Retorna403()
    {
        using var anonimo = ambiente.Api.CreateClient();
        using var semTenant = ambiente.Api.ClienteSemTenant();

        (await anonimo.GetAsync($"{Rota}/{Data(Hoje)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await semTenant.GetAsync($"{Rota}/{Data(Hoje)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static LancamentoRegistrado Evento(Guid tenant, TipoLancamento tipo, decimal valor, DateOnly? data = null) =>
        new(Guid.CreateVersion7(), DateTimeOffset.UtcNow, tenant, Guid.CreateVersion7(), tipo, valor, data ?? Hoje, null);

    private Task PublicarAsync(Guid tenant, TipoLancamento tipo, decimal valor, DateOnly? data = null) =>
        ambiente.Publicador.Publish(Evento(tenant, tipo, valor, data), Ct);

    private static async Task AguardarAsync(Func<Task<bool>> condicao, string mensagem)
    {
        var fim = DateTime.UtcNow + Espera;
        while (DateTime.UtcNow < fim)
        {
            if (await condicao())
            {
                return;
            }

            await Task.Delay(500, Ct);
        }

        throw new TimeoutException(mensagem);
    }

    private static string Data(DateOnly data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<SaldoDiarioDto> ObterDiaAsync(HttpClient cliente, DateOnly data)
    {
        using var resposta = await cliente.GetAsync($"{Rota}/{Data(data)}", Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<SaldoDiarioDto>(Ct))!;
    }

    private static async Task<SaldoDiarioDto> AguardarSaldoAsync(HttpClient cliente, DateOnly data, Func<SaldoDiarioDto, bool> condicao)
    {
        var fim = DateTime.UtcNow + Espera;
        SaldoDiarioDto ultimo;
        do
        {
            ultimo = await ObterDiaAsync(cliente, data);
            if (condicao(ultimo))
            {
                return ultimo;
            }

            await Task.Delay(300, Ct);
        }
        while (DateTime.UtcNow < fim);

        throw new TimeoutException($"Saldo de {data} não atingiu a condição. Último: {ultimo}");
    }
}
