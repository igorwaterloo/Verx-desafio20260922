using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lancamentos.Infrastructure.Persistence;

internal sealed class LancamentoConfiguration : IEntityTypeConfiguration<Lancamento>
{
    public void Configure(EntityTypeBuilder<Lancamento> builder)
    {
        builder.ToTable("Lancamentos");

        // PK iniciada pelo tenant: localidade dos dados do tenant e isolamento (ADR-0015).
        builder.HasKey(l => new { l.TenantId, l.Id });
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Tipo).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.ComplexProperty(l => l.Valor, valor =>
            valor.Property(d => d.Valor).HasColumnName("Valor").HasPrecision(18, 2).IsRequired());
        builder.Property(l => l.DataCompetencia).IsRequired();
        builder.Property(l => l.Descricao).HasMaxLength(Lancamento.DescricaoMaximo).IsRequired();
        builder.Property(l => l.CriadoPor).HasMaxLength(100).IsRequired();
        builder.Property(l => l.CriadoEm).IsRequired();

        builder.Ignore(l => l.EhEstorno);
        builder.Ignore(l => l.DomainEvents);

        builder.HasIndex(l => new { l.TenantId, l.DataCompetencia });
        builder.HasIndex(l => new { l.TenantId, l.CriadoEm });

        // RN-05 garantida também pelo banco: no máximo um estorno por lançamento, mesmo sob concorrência.
        builder.HasIndex(l => new { l.TenantId, l.LancamentoOriginalId })
            .IsUnique()
            .HasFilter("[LancamentoOriginalId] IS NOT NULL");
    }
}

internal sealed class TenantPlanoConfiguration : IEntityTypeConfiguration<TenantPlano>
{
    public void Configure(EntityTypeBuilder<TenantPlano> builder)
    {
        builder.ToTable("TenantsPlanos");
        builder.HasKey(p => p.TenantId);
        builder.Property(p => p.PlanoCodigo).HasMaxLength(20).IsRequired();
    }
}

internal sealed class ChaveIdempotenciaConfiguration : IEntityTypeConfiguration<ChaveIdempotencia>
{
    public void Configure(EntityTypeBuilder<ChaveIdempotencia> builder)
    {
        builder.ToTable("ChavesIdempotencia");
        builder.HasKey(c => new { c.TenantId, c.Chave });
        builder.Property(c => c.Chave).HasMaxLength(100).IsRequired();
    }
}

/// <summary>Chave do header Idempotency-Key associada ao lançamento criado (RN-08).</summary>
public sealed class ChaveIdempotencia(Guid tenantId, string chave, Guid lancamentoId, DateTimeOffset criadaEm) : ITenantEntity
{
    public Guid TenantId { get; private set; } = tenantId;

    public string Chave { get; private set; } = chave;

    public Guid LancamentoId { get; private set; } = lancamentoId;

    public DateTimeOffset CriadaEm { get; private set; } = criadaEm;
}
