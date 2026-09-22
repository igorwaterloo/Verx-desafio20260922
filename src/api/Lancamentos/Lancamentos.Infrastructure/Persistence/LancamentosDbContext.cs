using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Infrastructure.Persistence;

/// <summary>
/// Banco do contexto Lançamentos (LancamentosDb — ADR-0007). Herda o isolamento por tenant de
/// <see cref="TenantDbContext"/> e hospeda as tabelas do Transactional Outbox do MassTransit (ADR-0005).
/// </summary>
public sealed class LancamentosDbContext(DbContextOptions<LancamentosDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    public DbSet<TenantPlano> TenantsPlanos => Set<TenantPlano>();

    public DbSet<ChaveIdempotencia> ChavesIdempotencia => Set<ChaveIdempotencia>();

    protected override void ConfigurarModelo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LancamentosDbContext).Assembly);

        // Outbox/Inbox do MassTransit: tabelas técnicas, sem tenant.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
