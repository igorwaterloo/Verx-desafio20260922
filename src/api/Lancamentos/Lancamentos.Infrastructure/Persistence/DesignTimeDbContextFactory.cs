using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lancamentos.Infrastructure.Persistence;

/// <summary>Usado apenas pelo <c>dotnet ef</c> para gerar migrations (não conecta no banco).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LancamentosDbContext>
{
    public LancamentosDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LancamentosDbContext>()
            .UseSqlServer("Server=localhost;Database=LancamentosDb;Integrated Security=false")
            .Options;

        return new LancamentosDbContext(options, new TenantContext());
    }
}
