using FluxoCaixa.SharedKernel;
using Lancamentos.Domain.Lancamentos;
using Shouldly;

namespace Lancamentos.Domain.UnitTests;

public sealed class DinheiroTests
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("150.75")]
    [InlineData("999999999.99")]
    public void Criar_ValorValido_RetornaDinheiro(string valor)
    {
        var resultado = Dinheiro.Criar(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture));

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.Valor.ShouldBe(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public void Criar_ValorZeroOuNegativo_RetornaErro(string valor)
    {
        var resultado = Dinheiro.Criar(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture));

        resultado.Error.ShouldBe(LancamentoErros.ValorDeveSerPositivo);
    }

    [Fact]
    public void Criar_ComMaisDeDuasCasasDecimais_RetornaErro()
    {
        Dinheiro.Criar(10.555m).Error.ShouldBe(LancamentoErros.ValorComMaisDeDuasCasas);
    }

    [Fact]
    public void Criar_AcimaDoLimite_RetornaErro()
    {
        Dinheiro.Criar(1_000_000_000m).Error.ShouldBe(LancamentoErros.ValorAcimaDoLimite);
    }

    [Fact]
    public void Dinheiro_ComMesmoValor_SaoIguaisIndependenteDaEscala()
    {
        Dinheiro.Criar(1.5m).Value.ShouldBe(Dinheiro.Criar(1.50m).Value);
    }

    [Fact]
    public void Erros_SaoDeValidacao()
    {
        LancamentoErros.ValorDeveSerPositivo.Type.ShouldBe(ErrorType.Validation);
    }
}
