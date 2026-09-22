using NetArchTest.Rules;

namespace Architecture.Tests;

/// <summary>
/// Os building blocks compartilhados não podem arrastar dependências para as camadas internas.
/// </summary>
public sealed class BuildingBlocksTests
{
    [Fact]
    public void SharedKernel_NaoPossuiDependenciasExternas()
    {
        Types.InAssembly(Assemblies.SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft",
                "FluentValidation",
                "FluxoCaixa.Contracts",
                "FluxoCaixa.Application.Common",
                "FluxoCaixa.Infrastructure.Common")
            .GetResult()
            .DeveSerValido();
    }

    [Fact]
    public void Contracts_ContemApenasContratosSemDependencias()
    {
        Types.InAssembly(Assemblies.Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft",
                "FluxoCaixa.SharedKernel",
                "FluxoCaixa.Application.Common",
                "FluxoCaixa.Infrastructure.Common")
            .GetResult()
            .DeveSerValido();
    }

    [Fact]
    public void ApplicationCommon_NaoDependeDeInfraestrutura()
    {
        Types.InAssembly(Assemblies.ApplicationCommon)
            .ShouldNot()
            .HaveDependencyOnAny(
                "FluxoCaixa.Infrastructure.Common",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "MassTransit",
                "StackExchange.Redis")
            .GetResult()
            .DeveSerValido();
    }
}
