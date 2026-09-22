using NetArchTest.Rules;

namespace Architecture.Tests;

/// <summary>
/// Regras de dependência da Clean Architecture (ADR-0003): as dependências apontam para dentro.
/// </summary>
public sealed class CamadasTests
{
    private static readonly string[] FrameworksDeInfraestrutura =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "MassTransit",
        "StackExchange.Redis",
    ];

    public static TheoryData<string> Servicos => new(Assemblies.Servicos);

    [Theory]
    [MemberData(nameof(Servicos))]
    public void Domain_NaoDependeDeOutrasCamadasNemDeFrameworks(string servico)
    {
        Types.InAssembly(Assemblies.Domain(servico))
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                $"{servico}.Application",
                $"{servico}.Infrastructure",
                $"{servico}.Api",
                "FluentValidation",
                .. FrameworksDeInfraestrutura,
            ])
            .GetResult()
            .DeveSerValido();
    }

    [Theory]
    [MemberData(nameof(Servicos))]
    public void Application_NaoDependeDeInfrastructureNemDeApi(string servico)
    {
        Types.InAssembly(Assemblies.Application(servico))
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                $"{servico}.Infrastructure",
                $"{servico}.Api",
                "FluxoCaixa.Infrastructure.Common",
                .. FrameworksDeInfraestrutura,
            ])
            .GetResult()
            .DeveSerValido();
    }

    [Theory]
    [MemberData(nameof(Servicos))]
    public void Infrastructure_NaoDependeDaApi(string servico)
    {
        Types.InAssembly(Assemblies.Infrastructure(servico))
            .ShouldNot()
            .HaveDependencyOn($"{servico}.Api")
            .GetResult()
            .DeveSerValido();
    }

    [Theory]
    [MemberData(nameof(Servicos))]
    public void Controllers_NaoDependemDeInfrastructureNemDoEfCore(string servico)
    {
        Types.InAssembly(Assemblies.Api(servico))
            .That()
            .Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .ShouldNot()
            .HaveDependencyOnAny(
                $"{servico}.Infrastructure",
                "FluxoCaixa.Infrastructure.Common.Persistence",
                "Microsoft.EntityFrameworkCore")
            .GetResult()
            .DeveSerValido();
    }
}
