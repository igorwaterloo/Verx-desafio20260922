using FluxoCaixa.Contracts;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Tenants;

public sealed record TenantDto(
    Guid Id,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    Plano Plano,
    StatusTenant Status,
    DateTimeOffset CriadoEm)
{
    public static TenantDto De(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new TenantDto(tenant.Id, tenant.RazaoSocial, tenant.NomeFantasia, tenant.Cnpj.Formatado, tenant.Plano, tenant.Status, tenant.CriadoEm);
    }
}

internal static class EventosDeIntegracao
{
    public static TenantProvisionado TenantProvisionado(Tenant tenant, DateTimeOffset agora) =>
        new(Guid.CreateVersion7(agora), agora, tenant.Id, tenant.Plano.Codigo, tenant.Plano.LimiteLancamentosMes, tenant.Plano.LimiteUsuarios);

    public static PlanoDoTenantAlterado PlanoDoTenantAlterado(Tenant tenant, DateTimeOffset agora) =>
        new(Guid.CreateVersion7(agora), agora, tenant.Id, tenant.Plano.Codigo, tenant.Plano.LimiteLancamentosMes, tenant.Plano.LimiteUsuarios);
}
