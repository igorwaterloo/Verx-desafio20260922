using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Infrastructure.Persistence;

/// <summary>
/// Banco do contexto Plataforma (TenantsDb — ADR-0007/0017). Não usa o filtro por tenant: é o
/// cadastro dos próprios tenants. Hospeda o outbox dos eventos de tenant/plano (ADR-0005).
/// </summary>
public sealed class TenantsDbContext(DbContextOptions<TenantsDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.RazaoSocial).HasMaxLength(150).IsRequired();
        builder.Property(t => t.NomeFantasia).HasMaxLength(150);
        builder.Property(t => t.Cnpj)
            .HasConversion(c => c.Numero, numero => Cnpj.Criar(numero).Value)
            .HasMaxLength(14).IsFixedLength().IsRequired();
        builder.HasIndex(t => t.Cnpj).IsUnique(); // RP-01: um tenant por CNPJ
        builder.Property(t => t.PlanoCodigo).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(t => t.OrganizationId).HasMaxLength(64);
        builder.Property(t => t.AdminEmail).HasMaxLength(254).IsRequired();
        builder.Property(t => t.MotivoFalha).HasMaxLength(500);
        builder.HasIndex(t => new { t.Status, t.CriadoEm });

        builder.Ignore(t => t.Plano);
        builder.Ignore(t => t.DomainEvents);
    }
}

internal sealed class TenantRepository(TenantsDbContext contexto) : ITenantRepository
{
    public Task<Tenant?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> ObterPorCnpjAsync(string cnpjNumero, CancellationToken cancellationToken)
    {
        var cnpj = Cnpj.Criar(cnpjNumero).Value;
        return contexto.Tenants.FirstOrDefaultAsync(t => t.Cnpj == cnpj, cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> ListarPendentesCriadosAntesDeAsync(DateTimeOffset limite, CancellationToken cancellationToken) =>
        await contexto.Tenants
            .Where(t => t.Status == StatusTenant.Pendente && t.CriadoEm < limite)
            .OrderBy(t => t.CriadoEm)
            .Take(50)
            .ToListAsync(cancellationToken);

    public void Adicionar(Tenant tenant) => contexto.Tenants.Add(tenant);
}

internal sealed class UnitOfWork(TenantsDbContext contexto) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}

/// <summary>Usado apenas pelo <c>dotnet ef</c> para gerar migrations (não conecta no banco).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TenantsDbContext>()
            .UseSqlServer("Server=localhost;Database=TenantsDb;Integrated Security=false")
            .Options);
}
