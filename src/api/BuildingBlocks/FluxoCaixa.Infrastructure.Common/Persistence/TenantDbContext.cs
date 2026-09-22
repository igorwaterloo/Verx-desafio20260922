using System.Linq.Expressions;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixa.Infrastructure.Common.Persistence;

/// <summary>
/// DbContext base dos serviços multi-tenant (ADR-0015):
/// <list type="bullet">
/// <item>aplica um filtro global por <c>TenantId</c> em toda entidade <see cref="ITenantEntity"/>;</item>
/// <item>registra o <see cref="TenantSaveChangesInterceptor"/>, que preenche e protege o <c>TenantId</c> na gravação.</item>
/// </list>
/// Sem tenant definido, as consultas não retornam nada e as gravações falham (padrão seguro).
/// </summary>
public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenantContext) : DbContext(options)
{
    public const string FiltroDeTenant = "Tenant";

    internal ITenantContext TenantContext { get; } = tenantContext;

    /// <summary>
    /// Tenant usado pelo filtro global. Por ser membro da instância do contexto, o EF Core o
    /// parametriza a cada consulta (o modelo compilado é compartilhado entre tenants).
    /// </summary>
    public Guid TenantIdAtual => TenantContext.HasTenant ? TenantContext.TenantId : Guid.Empty;

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigurarModelo(modelBuilder);
        AplicarFiltroDeTenant(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.AddInterceptors(TenantSaveChangesInterceptor.Instance);
        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>Configuração do modelo do serviço (entidades, mapeamentos).</summary>
    protected abstract void ConfigurarModelo(ModelBuilder modelBuilder);

    private void AplicarFiltroDeTenant(ModelBuilder modelBuilder)
    {
        var tenantAtual = Expression.Property(Expression.Constant(this), nameof(TenantIdAtual));

        foreach (var entidade in modelBuilder.Model.GetEntityTypes()
                     .Where(e => e.BaseType is null && typeof(ITenantEntity).IsAssignableFrom(e.ClrType)))
        {
            var parametro = Expression.Parameter(entidade.ClrType, "e");
            var tenantDaEntidade = Expression.Call(
                typeof(EF), nameof(EF.Property), [typeof(Guid)], parametro, Expression.Constant(nameof(ITenantEntity.TenantId)));
            var filtro = Expression.Lambda(Expression.Equal(tenantDaEntidade, tenantAtual), parametro);

            modelBuilder.Entity(entidade.ClrType).HasQueryFilter(FiltroDeTenant, filtro);
        }
    }
}
