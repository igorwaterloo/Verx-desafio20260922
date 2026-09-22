using FluxoCaixa.SharedKernel.Tenancy;

namespace Lancamentos.Domain.Planos;

/// <summary>
/// Projeção local do plano do tenant, alimentada pelos eventos da Plataforma (ADR-0017).
/// Permite aplicar a quota (RN-09) sem chamada síncrona ao Tenants.Api (RNF-01).
/// </summary>
public sealed class TenantPlano : ITenantEntity
{
    /// <summary>Plano assumido enquanto a projeção do tenant não chegou (padrão conservador).</summary>
    public const string CodigoPadrao = "free";

    public const int LimitePadrao = 1_000;

    private TenantPlano(Guid tenantId, string planoCodigo, int limiteLancamentosMes, DateTimeOffset atualizadoEm)
    {
        TenantId = tenantId;
        PlanoCodigo = planoCodigo;
        LimiteLancamentosMes = limiteLancamentosMes;
        AtualizadoEm = atualizadoEm;
    }

    public Guid TenantId { get; private set; }

    public string PlanoCodigo { get; private set; }

    public int LimiteLancamentosMes { get; private set; }

    /// <summary>Momento do último evento aplicado; protege contra eventos fora de ordem.</summary>
    public DateTimeOffset AtualizadoEm { get; private set; }

    public static TenantPlano Criar(Guid tenantId, string planoCodigo, int limiteLancamentosMes, DateTimeOffset ocorridoEm)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("O tenant não pode ser vazio.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(planoCodigo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limiteLancamentosMes);

        return new TenantPlano(tenantId, planoCodigo, limiteLancamentosMes, ocorridoEm);
    }

    /// <summary>Aplica um evento de plano. Eventos iguais ou mais antigos que o último aplicado são ignorados.</summary>
    public bool Aplicar(string planoCodigo, int limiteLancamentosMes, DateTimeOffset ocorridoEm)
    {
        if (ocorridoEm <= AtualizadoEm)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(planoCodigo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limiteLancamentosMes);

        PlanoCodigo = planoCodigo;
        LimiteLancamentosMes = limiteLancamentosMes;
        AtualizadoEm = ocorridoEm;
        return true;
    }
}
