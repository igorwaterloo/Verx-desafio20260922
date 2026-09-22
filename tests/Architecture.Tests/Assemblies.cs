using System.Reflection;

namespace Architecture.Tests;

/// <summary>
/// Localiza os assemblies da solução para as regras de arquitetura.
/// </summary>
internal static class Assemblies
{
    public static readonly string[] Servicos = ["Tenants", "Lancamentos", "Consolidado"];

    public static Assembly SharedKernel => typeof(FluxoCaixa.SharedKernel.AssemblyReference).Assembly;

    public static Assembly Contracts => typeof(FluxoCaixa.Contracts.AssemblyReference).Assembly;

    public static Assembly ApplicationCommon => typeof(FluxoCaixa.Application.Common.AssemblyReference).Assembly;

    public static Assembly Domain(string servico) => Assembly.Load($"{servico}.Domain");

    public static Assembly Application(string servico) => Assembly.Load($"{servico}.Application");

    public static Assembly Infrastructure(string servico) => Assembly.Load($"{servico}.Infrastructure");

    public static Assembly Api(string servico) => Assembly.Load($"{servico}.Api");

    public static IEnumerable<Assembly> TodasDoServico(string servico)
    {
        yield return Domain(servico);
        yield return Application(servico);
        yield return Infrastructure(servico);
        yield return Api(servico);

        if (servico == "Consolidado")
        {
            yield return Assembly.Load("Consolidado.Worker");
        }
    }
}
