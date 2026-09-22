using FluxoCaixa.SharedKernel.Domain;
using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class EntityTests
{
    private sealed class Produto(Guid id) : Entity<Guid>(id);

    private sealed class Cliente(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Entidades_ComMesmoIdEMesmoTipo_SaoIguais()
    {
        var id = Guid.NewGuid();

        var a = new Produto(id);
        var b = new Produto(id);

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Entidades_ComIdsDiferentes_SaoDiferentes()
    {
        (new Produto(Guid.NewGuid()) != new Produto(Guid.NewGuid())).ShouldBeTrue();
    }

    [Fact]
    public void Entidades_DeTiposDiferentesComMesmoId_SaoDiferentes()
    {
        var id = Guid.NewGuid();

        new Produto(id).Equals(new Cliente(id)).ShouldBeFalse();
    }
}
