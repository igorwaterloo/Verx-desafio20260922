using NetArchTest.Rules;

namespace Architecture.Tests;

/// <summary>
/// Os building blocks compartilhados não podem arrastar dependências para as camadas internas.
/// </summary>
public sealed class BuildingBlocksTests
{
    // Bibliotecas proibidas nos building blocks mais internos. Não se usa o prefixo "Microsoft" inteiro:
    // a cobertura de código (Microsoft.CodeCoverage) instrumenta os assemblies e injeta referências
    // próprias, que não são dependências do código.
    private static readonly string[] BibliotecasDeInfraestrutura =
    [
        "Microsoft.Extensions",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data",
        "Microsoft.IdentityModel",
        "MassTransit",
        "StackExchange.Redis",
        "OpenTelemetry",
    ];

    [Fact]
    public void SharedKernel_NaoPossuiDependenciasExternas()
    {
        Types.InAssembly(Assemblies.SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                .. BibliotecasDeInfraestrutura,
                "FluentValidation",
                "FluxoCaixa.Contracts",
                "FluxoCaixa.Application.Common",
                "FluxoCaixa.Infrastructure.Common",
            ])
            .GetResult()
            .DeveSerValido();
    }

    [Fact]
    public void Contracts_ContemApenasContratosSemDependencias()
    {
        Types.InAssembly(Assemblies.Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                .. BibliotecasDeInfraestrutura,
                "FluxoCaixa.SharedKernel",
                "FluxoCaixa.Application.Common",
                "FluxoCaixa.Infrastructure.Common",
            ])
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
