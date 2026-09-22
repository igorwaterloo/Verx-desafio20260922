using NetArchTest.Rules;

namespace Architecture.Tests;

/// <summary>
/// Isolamento entre bounded contexts (ADR-0002): um contexto nunca referencia outro.
/// A integração acontece apenas por eventos de FluxoCaixa.Contracts.
/// </summary>
public sealed class ContextosTests
{
    public static TheoryData<string> Servicos => new(Assemblies.Servicos);

    [Theory]
    [MemberData(nameof(Servicos))]
    public void Contexto_NaoReferenciaOutrosContextos(string servico)
    {
        var outrosContextos = Assemblies.Servicos
            .Where(s => s != servico)
            .ToArray();

        foreach (var assembly in Assemblies.TodasDoServico(servico))
        {
            Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(outrosContextos)
                .GetResult()
                .DeveSerValido();
        }
    }
}
