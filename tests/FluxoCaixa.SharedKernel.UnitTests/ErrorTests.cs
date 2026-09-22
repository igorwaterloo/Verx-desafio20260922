using FluxoCaixa.SharedKernel;
using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class ErrorTests
{
    [Fact]
    public void FabricasDeErro_DefinemOTipoCorreto()
    {
        Error.Validation("c", "m").Type.ShouldBe(ErrorType.Validation);
        Error.NotFound("c", "m").Type.ShouldBe(ErrorType.NotFound);
        Error.Conflict("c", "m").Type.ShouldBe(ErrorType.Conflict);
        Error.Forbidden("c", "m").Type.ShouldBe(ErrorType.Forbidden);
        Error.BusinessRule("c", "m").Type.ShouldBe(ErrorType.BusinessRule);
        Error.Failure("c", "m").Type.ShouldBe(ErrorType.Failure);
        Error.Unavailable("c", "m").Type.ShouldBe(ErrorType.Unavailable);
    }

    [Fact]
    public void Erros_ComMesmosDados_SaoIguais()
    {
        Error.NotFound("lancamento.nao_encontrado", "x")
            .ShouldBe(Error.NotFound("lancamento.nao_encontrado", "x"));
    }

    [Fact]
    public void ValidationError_AgrupaErrosPorCampo()
    {
        var erro = new ValidationError(new Dictionary<string, string[]>
        {
            ["Valor"] = ["O valor deve ser maior que zero."],
        });

        erro.Type.ShouldBe(ErrorType.Validation);
        erro.Errors["Valor"].ShouldContain("O valor deve ser maior que zero.");
    }
}
