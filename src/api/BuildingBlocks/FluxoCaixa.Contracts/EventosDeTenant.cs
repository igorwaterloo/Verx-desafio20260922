namespace FluxoCaixa.Contracts;

/// <summary>
/// Um tenant foi provisionado (onboarding concluído). Carrega os limites do plano para que os
/// consumidores mantenham uma projeção local, sem chamada síncrona à Plataforma (ADR-0017).
/// Produtor: Tenants. Consumidor: Lançamentos.
/// </summary>
public sealed record TenantProvisionado(
    Guid EventId,
    DateTimeOffset OcorridoEm,
    Guid TenantId,
    string PlanoCodigo,
    int LimiteLancamentosMes,
    int LimiteUsuarios) : IIntegrationEvent
{
    public int Versao => 1;
}

/// <summary>
/// O plano de um tenant foi alterado. O consumidor ignora eventos mais antigos que o último
/// aplicado (<see cref="OcorridoEm"/>), protegendo contra reordenação.
/// Produtor: Tenants. Consumidor: Lançamentos.
/// </summary>
public sealed record PlanoDoTenantAlterado(
    Guid EventId,
    DateTimeOffset OcorridoEm,
    Guid TenantId,
    string PlanoCodigo,
    int LimiteLancamentosMes,
    int LimiteUsuarios) : IIntegrationEvent
{
    public int Versao => 1;
}
