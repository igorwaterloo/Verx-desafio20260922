using FluxoCaixa.Infrastructure.Common.Persistence;
using FluxoCaixa.SharedKernel.Domain;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixa.Infrastructure.Common.UnitTests.Persistence;

public sealed class Item : Entity<Guid>, ITenantEntity
{
    public Item(string nome, Guid tenantId = default)
        : base(Guid.NewGuid())
    {
        Nome = nome;
        TenantId = tenantId;
    }

    private Item()
    {
    }

    public Guid TenantId { get; private set; }

    public string Nome { get; set; } = string.Empty;
}

/// <summary>Catálogo global: não pertence a tenant, portanto não recebe filtro.</summary>
public sealed class Categoria(Guid id, string nome) : Entity<Guid>(id)
{
    public string Nome { get; private set; } = nome;
}

public sealed class TesteDbContext(DbContextOptions<TesteDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Item> Itens => Set<Item>();

    public DbSet<Categoria> Categorias => Set<Categoria>();

    protected override void ConfigurarModelo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>().HasKey(i => i.Id);
        modelBuilder.Entity<Categoria>().HasKey(c => c.Id);
    }
}

/// <summary>Banco SQLite em memória compartilhado entre contextos de tenants diferentes.</summary>
public sealed class BancoEmMemoria : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");

    public BancoEmMemoria()
    {
        _conexao.Open();
        using var contexto = Criar(null);
        contexto.Database.EnsureCreated();
    }

    public TesteDbContext Criar(Guid? tenantId)
    {
        var tenant = new TenantContext();
        if (tenantId.HasValue)
        {
            tenant.Definir(tenantId.Value, "usuario-teste", []);
        }

        var options = new DbContextOptionsBuilder<TesteDbContext>().UseSqlite(_conexao).Options;
        return new TesteDbContext(options, tenant);
    }

    public void Dispose() => _conexao.Dispose();
}
