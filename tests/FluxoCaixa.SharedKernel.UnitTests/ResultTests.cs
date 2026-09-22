using FluxoCaixa.SharedKernel;
using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class ResultTests
{
    private static readonly Error ErroQualquer = Error.Conflict("lancamento.ja_estornado", "O lançamento já foi estornado.");

    [Fact]
    public void Success_SemValor_EhSucessoComErroNone()
    {
        var resultado = Result.Success();

        resultado.IsSuccess.ShouldBeTrue();
        resultado.IsFailure.ShouldBeFalse();
        resultado.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_ComErro_EhFalhaEExpoeOErro()
    {
        var resultado = Result.Failure(ErroQualquer);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error.ShouldBe(ErroQualquer);
    }

    [Fact]
    public void Failure_ComErroNone_LancaExcecao()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void SuccessGenerico_ExpoeOValor()
    {
        var resultado = Result.Success(42);

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.ShouldBe(42);
    }

    [Fact]
    public void Value_QuandoFalha_LancaExcecao()
    {
        var resultado = Result.Failure<int>(ErroQualquer);

        Should.Throw<InvalidOperationException>(() => resultado.Value);
    }

    [Fact]
    public void ConversaoImplicita_DeValor_CriaSucesso()
    {
        Result<string> resultado = "ok";

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.ShouldBe("ok");
    }

    [Fact]
    public void ConversaoImplicita_DeErro_CriaFalha()
    {
        Result<string> resultado = ErroQualquer;

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error.ShouldBe(ErroQualquer);
    }

    [Fact]
    public void Match_ExecutaOCaminhoCorrespondente()
    {
        Result.Success(10).Match(v => v * 2, _ => -1).ShouldBe(20);
        Result.Failure<int>(ErroQualquer).Match(v => v * 2, e => e.Code.Length).ShouldBe(ErroQualquer.Code.Length);
    }
}
