using Consolidado.Application.Saldos;
using Consolidado.Domain.Saldos;

namespace Consolidado.Application.Abstractions;

// Portas da camada de aplicação (ADR-0003). Operam no tenant corrente: o isolamento é garantido
// pelo filtro global por TenantId (ADR-0015) e pelas chaves de cache prefixadas pelo tenant.

/// <summary>Escrita da projeção de saldos (usada pelo Worker).</summary>
public interface ISaldoDiarioRepository
{
    Task<SaldoDiario?> ObterAsync(DateOnly data, CancellationToken cancellationToken);

    void Adicionar(SaldoDiario saldo);
}

/// <summary>Inbox do consumidor idempotente (RC-01).</summary>
public interface IInbox
{
    Task<bool> JaProcessadaAsync(Guid eventId, CancellationToken cancellationToken);

    void Registrar(Guid eventId, DateTimeOffset processadaEm);
}

public interface IUnitOfWork
{
    /// <exception cref="ConflitoDePersistenciaException">Violação de unicidade ou de concorrência.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Leitura da projeção (usada pela Api), sem tracking.</summary>
public interface ISaldosLeitura
{
    Task<SaldoDiarioDto?> ObterAsync(DateOnly data, CancellationToken cancellationToken);

    /// <summary>Somente os dias com movimento, em ordem de data.</summary>
    Task<IReadOnlyList<SaldoDiarioDto>> ListarPeriodoAsync(DateOnly inicio, DateOnly fim, CancellationToken cancellationToken);
}

/// <summary>
/// Cache de leitura (ADR-0006). A implementação nunca propaga falhas: se o cache estiver
/// indisponível, leituras retornam "miss" e escritas são ignoradas (fallback para o banco).
/// </summary>
public interface ICacheConsolidado
{
    Task<T?> ObterAsync<T>(string chave, CancellationToken cancellationToken)
        where T : class;

    Task DefinirAsync<T>(string chave, T valor, TimeSpan ttl, CancellationToken cancellationToken)
        where T : class;

    Task RemoverAsync(string chave, CancellationToken cancellationToken);
}

/// <summary>A gravação violou uma restrição de unicidade ou de concorrência otimista.</summary>
public sealed class ConflitoDePersistenciaException : Exception
{
    public ConflitoDePersistenciaException()
    {
    }

    public ConflitoDePersistenciaException(string message)
        : base(message)
    {
    }

    public ConflitoDePersistenciaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
