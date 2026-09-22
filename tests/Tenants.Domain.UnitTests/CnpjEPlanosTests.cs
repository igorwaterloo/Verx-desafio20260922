using Shouldly;
using Tenants.Domain.Tenants;

namespace Tenants.Domain.UnitTests;

public sealed class CnpjEPlanosTests
{
    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    [InlineData(" 11.222.333/0001-81 ")]
    public void Cnpj_ValidoComOuSemMascara_NormalizaParaDigitos(string valor)
    {
        var cnpj = Cnpj.Criar(valor);

        cnpj.IsSuccess.ShouldBeTrue();
        cnpj.Value.Numero.ShouldBe("11222333000181");
        cnpj.Value.Formatado.ShouldBe("11.222.333/0001-81");
    }

    [Theory]
    [InlineData("11222333000182")] // dígito verificador errado
    [InlineData("11111111111111")] // todos os dígitos iguais
    [InlineData("1122233300018")] // tamanho
    [InlineData("")]
    [InlineData("abc")]
    public void Cnpj_Invalido_RetornaErro(string valor)
    {
        Cnpj.Criar(valor).Error.ShouldBe(TenantErros.CnpjInvalido);
    }

    [Fact]
    public void Cnpj_ComMesmoNumero_SaoIguais()
    {
        Cnpj.Criar("11.222.333/0001-81").Value.ShouldBe(Cnpj.Criar("11222333000181").Value);
    }

    [Fact]
    public void Catalogo_DefineOsPlanosFreeEPro()
    {
        CatalogoDePlanos.Free.ShouldBe(new Plano("free", "Free", 1_000, 2, 20, 0m));
        CatalogoDePlanos.Pro.ShouldBe(new Plano("pro", "Pro", 50_000, 20, 100, 99m));
        CatalogoDePlanos.Todos.Select(p => p.Codigo).ShouldBe(["free", "pro"]);
    }

    [Fact]
    public void Catalogo_Obter_PlanoInexistenteRetornaNulo()
    {
        CatalogoDePlanos.Obter("pro").ShouldBe(CatalogoDePlanos.Pro);
        CatalogoDePlanos.Obter("enterprise").ShouldBeNull();
    }
}
