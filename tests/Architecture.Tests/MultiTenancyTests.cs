using FluxoCaixa.SharedKernel.Domain;
using FluxoCaixa.SharedKernel.Tenancy;
using Shouldly;

namespace Architecture.Tests;

/// <summary>
/// Isolamento entre tenants (ADR-0015): toda entidade de negócio dos contextos multi-tenant
/// precisa implementar <see cref="ITenantEntity"/>, para receber o filtro global por TenantId.
/// O contexto Plataforma (Tenants) é o dono do cadastro de tenants e fica fora desta regra.
/// </summary>
public sealed class MultiTenancyTests
{
    public static TheoryData<string> ContextosMultiTenant => new("Lancamentos", "Consolidado");

    [Theory]
    [MemberData(nameof(ContextosMultiTenant))]
    public void EntidadesDeNegocio_ImplementamITenantEntity(string servico)
    {
        var semTenant = Assemblies.Domain(servico)
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && HerdaDeEntity(t))
            .Where(t => !typeof(ITenantEntity).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        semTenant.ShouldBeEmpty($"Entidades sem ITenantEntity: {string.Join(", ", semTenant)}");
    }

    private static bool HerdaDeEntity(Type tipo)
    {
        for (var atual = tipo.BaseType; atual is not null; atual = atual.BaseType)
        {
            if (atual.IsGenericType && atual.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }
}
