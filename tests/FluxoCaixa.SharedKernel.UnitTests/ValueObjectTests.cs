using FluxoCaixa.SharedKernel.Domain;
using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class ValueObjectTests
{
    private sealed class Endereco(string rua, string cidade) : ValueObject
    {
        public string Rua { get; } = rua;

        public string Cidade { get; } = cidade;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Rua;
            yield return Cidade;
        }
    }

    [Fact]
    public void ValueObjects_ComMesmosComponentes_SaoIguais()
    {
        var a = new Endereco("Rua A", "São Paulo");
        var b = new Endereco("Rua A", "São Paulo");

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ValueObjects_ComComponentesDiferentes_SaoDiferentes()
    {
        (new Endereco("Rua A", "São Paulo") != new Endereco("Rua B", "São Paulo")).ShouldBeTrue();
    }

    [Fact]
    public void ValueObject_ComparadoComNull_EhDiferente()
    {
        new Endereco("Rua A", "São Paulo").Equals(null).ShouldBeFalse();
    }
}
