using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxoCaixa.Infrastructure.Common.Persistence;

/// <summary>
/// Garante o isolamento entre tenants na gravação (MT-01, ADR-0015):
/// preenche o <c>TenantId</c> das inclusões e bloqueia qualquer gravação sem tenant ou que
/// envolva dados de outro tenant. É stateless (lê o tenant do próprio contexto), por isso
/// uma única instância é compartilhada.
/// </summary>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly TenantSaveChangesInterceptor Instance = new();

    private TenantSaveChangesInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Aplicar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Aplicar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Aplicar(DbContext? contexto)
    {
        if (contexto is not TenantDbContext tenantDbContext)
        {
            return;
        }

        var tenant = tenantDbContext.TenantContext;

        foreach (var entrada in contexto.ChangeTracker.Entries<ITenantEntity>())
        {
            if (entrada.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (!tenant.HasTenant)
            {
                throw new TenantIsolationException(
                    $"Gravação de '{entrada.Metadata.ClrType.Name}' sem tenant definido na execução corrente.");
            }

            var propriedade = entrada.Property(e => e.TenantId);
            switch (entrada.State)
            {
                case EntityState.Added:
                    PreencherOuValidar(propriedade, tenant.TenantId, entrada);
                    break;

                case EntityState.Modified or EntityState.Deleted:
                    ValidarDono(propriedade, tenant.TenantId, entrada);
                    break;
            }
        }
    }

    private static void PreencherOuValidar(PropertyEntry<ITenantEntity, Guid> propriedade, Guid tenantAtual, EntityEntry entrada)
    {
        if (propriedade.CurrentValue == Guid.Empty)
        {
            propriedade.CurrentValue = tenantAtual;
        }
        else if (propriedade.CurrentValue != tenantAtual)
        {
            throw new TenantIsolationException(
                $"Tentativa de incluir '{entrada.Metadata.ClrType.Name}' pertencente a outro tenant.");
        }
    }

    private static void ValidarDono(PropertyEntry<ITenantEntity, Guid> propriedade, Guid tenantAtual, EntityEntry entrada)
    {
        if (propriedade.OriginalValue != tenantAtual || propriedade.IsModified)
        {
            throw new TenantIsolationException(
                $"Tentativa de alterar ou excluir '{entrada.Metadata.ClrType.Name}' de outro tenant.");
        }
    }
}

/// <summary>Violação do isolamento entre tenants: indica erro de programação, nunca fluxo esperado.</summary>
public sealed class TenantIsolationException : InvalidOperationException
{
    public TenantIsolationException()
    {
    }

    public TenantIsolationException(string message)
        : base(message)
    {
    }

    public TenantIsolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
