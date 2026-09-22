using FluxoCaixa.Contracts;
using Lancamentos.Application.Lancamentos;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;

namespace Lancamentos.Application.Abstractions;

// Portas da camada de aplicação (Dependency Inversion — ADR-0003). Todas operam no tenant corrente:
// o isolamento é garantido pela infraestrutura (filtro global por TenantId — ADR-0015).

/// <summary>Repositório do agregado Lancamento (lado de escrita).</summary>
public interface ILancamentoRepository
{
    Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Quantidade de lançamentos (sem estornos) criados a partir de <paramref name="desde"/> — base da quota.</summary>
    Task<int> ContarCriadosDesdeAsync(DateTimeOffset desde, CancellationToken cancellationToken);

    void Adicionar(Lancamento lancamento);
}

/// <summary>Projeção local do plano do tenant.</summary>
public interface ITenantPlanoRepository
{
    Task<TenantPlano?> ObterAsync(CancellationToken cancellationToken);

    void Adicionar(TenantPlano plano);
}

/// <summary>Chaves de idempotência do registro de lançamentos (RN-08).</summary>
public interface IIdempotenciaRepository
{
    Task<Guid?> ObterLancamentoIdAsync(string chave, CancellationToken cancellationToken);

    void Registrar(string chave, Guid lancamentoId, DateTimeOffset criadaEm);
}

/// <summary>
/// Publica eventos de integração. A implementação grava no Transactional Outbox, na mesma
/// transação do <see cref="IUnitOfWork"/> (ADR-0005).
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvento>(TEvento evento, CancellationToken cancellationToken)
        where TEvento : class, IIntegrationEvent;
}

public interface IUnitOfWork
{
    /// <exception cref="ConflitoDePersistenciaException">Violação de unicidade ou de concorrência.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Leituras otimizadas (lado de leitura do CQRS), projetadas diretamente em DTOs.</summary>
public interface ILancamentosLeitura
{
    Task<LancamentoDto?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Pagina<LancamentoDto>> ListarPorDataAsync(DateOnly data, int pagina, int tamanhoPagina, CancellationToken cancellationToken);
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
