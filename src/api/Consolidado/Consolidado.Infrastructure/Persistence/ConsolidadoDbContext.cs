using Consolidado.Domain.Saldos;
using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Consolidado.Infrastructure.Persistence;

/// <summary>Banco do contexto Consolidado (ConsolidadoDb — ADR-0007), com isolamento por tenant.</summary>
public sealed class ConsolidadoDbContext(DbContextOptions<ConsolidadoDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<SaldoDiario> SaldosDiarios => Set<SaldoDiario>();

    public DbSet<MensagemProcessada> MensagensProcessadas => Set<MensagemProcessada>();

    protected override void ConfigurarModelo(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsolidadoDbContext).Assembly);
}

internal sealed class SaldoDiarioConfiguration : IEntityTypeConfiguration<SaldoDiario>
{
    public void Configure(EntityTypeBuilder<SaldoDiario> builder)
    {
        builder.ToTable("SaldosDiarios");
        builder.HasKey(s => new { s.TenantId, s.Data });
        builder.Property(s => s.TotalCreditos).HasPrecision(18, 2);
        builder.Property(s => s.TotalDebitos).HasPrecision(18, 2);
        builder.Property(s => s.Versao).IsRowVersion();
        builder.Ignore(s => s.Saldo);
    }
}

internal sealed class MensagemProcessadaConfiguration : IEntityTypeConfiguration<MensagemProcessada>
{
    public void Configure(EntityTypeBuilder<MensagemProcessada> builder)
    {
        builder.ToTable("MensagensProcessadas");

        // EventId é globalmente único (Guid v7): a PK impede aplicar o mesmo evento duas vezes (RC-01).
        builder.HasKey(m => m.EventId);
        builder.HasIndex(m => new { m.TenantId, m.ProcessadaEm });
    }
}

/// <summary>Usado apenas pelo <c>dotnet ef</c> para gerar migrations (não conecta no banco).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConsolidadoDbContext>
{
    public ConsolidadoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ConsolidadoDbContext>()
                .UseSqlServer("Server=localhost;Database=ConsolidadoDb;Integrated Security=false")
                .Options,
            new TenantContext());
}
