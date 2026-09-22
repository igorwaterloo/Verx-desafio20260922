using System.Text.Json;
using Consolidado.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Consolidado.Infrastructure.Cache;

/// <summary>
/// Cache-aside no Redis (ADR-0006). Nunca propaga falhas: com o Redis lento ou fora, a leitura
/// vira "miss" e a consulta segue para o banco (fluxo 8). Um disjuntor evita pagar o timeout a
/// cada requisição enquanto o Redis estiver indisponível.
/// </summary>
internal sealed partial class RedisCacheConsolidado(
    IConnectionMultiplexer redis,
    DisjuntorDoCache disjuntor,
    ILogger<RedisCacheConsolidado> logger) : ICacheConsolidado
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> ObterAsync<T>(string chave, CancellationToken cancellationToken)
        where T : class
    {
        if (!disjuntor.PermiteChamada)
        {
            return null;
        }

        try
        {
            var valor = await redis.GetDatabase().StringGetAsync(chave);
            disjuntor.RegistrarSucesso();
            return valor.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(valor.ToString(), Json);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            RegistrarFalha("GET", ex);
            return null;
        }
    }

    public async Task DefinirAsync<T>(string chave, T valor, TimeSpan ttl, CancellationToken cancellationToken)
        where T : class
    {
        if (!disjuntor.PermiteChamada)
        {
            return;
        }

        try
        {
            await redis.GetDatabase().StringSetAsync(chave, JsonSerializer.Serialize(valor, Json), ttl);
            disjuntor.RegistrarSucesso();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            RegistrarFalha("SET", ex);
        }
    }

    public async Task RemoverAsync(string chave, CancellationToken cancellationToken)
    {
        // Invalidação é tentada mesmo com o disjuntor aberto: se falhar, o TTL limita o dado desatualizado.
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(chave);
            disjuntor.RegistrarSucesso();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            RegistrarFalha("DEL", ex);
        }
    }

    private void RegistrarFalha(string operacao, Exception ex)
    {
        disjuntor.RegistrarFalha();
        LogFalhaNoCache(logger, operacao, ex.GetType().Name, disjuntor.PermiteChamada ? "fechado" : "aberto");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache indisponível em {Operacao} ({Erro}); usando o banco. Disjuntor {Estado}.")]
    private static partial void LogFalhaNoCache(ILogger logger, string operacao, string erro, string estado);
}

/// <summary>
/// Disjuntor simples: após <see cref="LimiteDeFalhas"/> falhas consecutivas, deixa de chamar o Redis
/// por <see cref="TempoAberto"/>; depois disso a próxima chamada testa o Redis novamente.
/// </summary>
internal sealed class DisjuntorDoCache(TimeProvider tempo)
{
    public const int LimiteDeFalhas = 3;
    public static readonly TimeSpan TempoAberto = TimeSpan.FromSeconds(15);

    private int _falhasConsecutivas;
    private long _abertoAteTicks;

    public bool PermiteChamada => tempo.GetUtcNow().UtcTicks >= Interlocked.Read(ref _abertoAteTicks);

    public void RegistrarSucesso() => Interlocked.Exchange(ref _falhasConsecutivas, 0);

    public void RegistrarFalha()
    {
        if (Interlocked.Increment(ref _falhasConsecutivas) >= LimiteDeFalhas)
        {
            Interlocked.Exchange(ref _abertoAteTicks, (tempo.GetUtcNow() + TempoAberto).UtcTicks);
            Interlocked.Exchange(ref _falhasConsecutivas, 0);
        }
    }
}

/// <summary>Redis fora deixa o serviço "Degraded", nunca "Unhealthy": o cache é otimização, não dependência.</summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latencia = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis respondeu em {latencia.TotalMilliseconds:0.0} ms.");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis indisponível; consultas atendidas pelo banco.", ex);
        }
    }
}
