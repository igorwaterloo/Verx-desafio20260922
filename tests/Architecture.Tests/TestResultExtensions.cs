using Shouldly;

namespace Architecture.Tests;

internal static class TestResultExtensions
{
    /// <summary>
    /// Falha listando os tipos que violaram a regra, para facilitar o diagnóstico.
    /// </summary>
    public static void DeveSerValido(this NetArchTest.Rules.TestResult resultado)
    {
        var violacoes = resultado.FailingTypeNames ?? [];
        resultado.IsSuccessful.ShouldBeTrue(
            $"Tipos que violam a regra: {string.Join(", ", violacoes)}");
    }
}
