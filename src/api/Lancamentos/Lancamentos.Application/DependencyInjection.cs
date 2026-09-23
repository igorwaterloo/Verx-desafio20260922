using FluxoCaixa.Application.Common.Cqrs;
using Lancamentos.Application.Telemetria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lancamentos.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLancamentosApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(DependencyInjection).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        // IMeterFactory vem do host (registrado pelo ASP.NET Core).
        services.TryAddSingleton<LancamentosMetricas>();
        return services;
    }
}
