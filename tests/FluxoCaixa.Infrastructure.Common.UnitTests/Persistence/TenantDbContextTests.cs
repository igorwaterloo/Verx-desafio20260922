using FluxoCaixa.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FluxoCaixa.Infrastructure.Common.UnitTests.Persistence;

/// <summary>
/// Isolamento entre tenants na persistência (ADR-0015): filtro global de leitura e regras de gravação.
/// </summary>
public sealed class TenantDbContextTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("0192f79e-0001-7000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("0192f79e-0002-7000-8000-000000000002");

    private readonly BancoEmMemoria _banco = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Inserir_PreencheOTenantIdComOTenantCorrente()
    {
        await using var contexto = _banco.Criar(TenantA);
        var item = new Item("Caneta");

        contexto.Itens.Add(item);
        await contexto.SaveChangesAsync(Ct);

        item.TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task Consultar_RetornaSomenteDadosDoTenantCorrente()
    {
        await SemearAsync(TenantA, "A1", "A2");
        await SemearAsync(TenantB, "B1");

        await using var contextoA = _banco.Criar(TenantA);
        await using var contextoB = _banco.Criar(TenantB);

        (await contextoA.Itens.Select(i => i.Nome).ToListAsync(Ct)).ShouldBe(["A1", "A2"], ignoreOrder: true);
        (await contextoB.Itens.Select(i => i.Nome).ToListAsync(Ct)).ShouldBe(["B1"]);
    }

    [Fact]
    public async Task ConsultarPorId_RegistroDeOutroTenant_NaoEncontra()
    {
        var idDeB = (await SemearAsync(TenantB, "B1")).Single();

        await using var contextoA = _banco.Criar(TenantA);

        (await contextoA.Itens.FirstOrDefaultAsync(i => i.Id == idDeB, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Consultar_SemTenantDefinido_NaoRetornaNada()
    {
        await SemearAsync(TenantA, "A1");

        await using var contexto = _banco.Criar(null);

        (await contexto.Itens.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Consultar_EntidadeGlobal_NaoRecebeFiltro()
    {
        await using (var semear = _banco.Criar(TenantA))
        {
            semear.Categorias.Add(new Categoria(Guid.NewGuid(), "Geral"));
            await semear.SaveChangesAsync(Ct);
        }

        await using var contextoB = _banco.Criar(TenantB);

        (await contextoB.Categorias.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Inserir_EntidadeComTenantDeOutro_LancaExcecao()
    {
        await using var contexto = _banco.Criar(TenantA);
        contexto.Itens.Add(new Item("Intruso", TenantB));

        await Should.ThrowAsync<TenantIsolationException>(() => contexto.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Gravar_SemTenantDefinido_LancaExcecao()
    {
        await using var contexto = _banco.Criar(null);
        contexto.Itens.Add(new Item("Sem dono"));

        await Should.ThrowAsync<TenantIsolationException>(() => contexto.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Alterar_EntidadeDeOutroTenant_LancaExcecao()
    {
        var idDeB = (await SemearAsync(TenantB, "B1")).Single();

        await using var contextoA = _banco.Criar(TenantA);
        var itemDeB = await contextoA.Itens.IgnoreQueryFilters().SingleAsync(i => i.Id == idDeB, Ct);
        itemDeB.Nome = "alterado por A";

        await Should.ThrowAsync<TenantIsolationException>(() => contextoA.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Excluir_EntidadeDeOutroTenant_LancaExcecao()
    {
        var idDeB = (await SemearAsync(TenantB, "B1")).Single();

        await using var contextoA = _banco.Criar(TenantA);
        var itemDeB = await contextoA.Itens.IgnoreQueryFilters().SingleAsync(i => i.Id == idDeB, Ct);
        contextoA.Itens.Remove(itemDeB);

        await Should.ThrowAsync<TenantIsolationException>(() => contextoA.SaveChangesAsync(Ct));
    }

    [Fact]
    public void Gravar_Sincrono_TambemAplicaAsRegras()
    {
        using var contexto = _banco.Criar(TenantA);
        contexto.Itens.Add(new Item("Intruso", TenantB));

        Should.Throw<TenantIsolationException>(() => contexto.SaveChanges());
    }

    private async Task<List<Guid>> SemearAsync(Guid tenantId, params string[] nomes)
    {
        await using var contexto = _banco.Criar(tenantId);
        var itens = nomes.Select(n => new Item(n)).ToList();
        contexto.Itens.AddRange(itens);
        await contexto.SaveChangesAsync(Ct);
        return itens.Select(i => i.Id).ToList();
    }
}
