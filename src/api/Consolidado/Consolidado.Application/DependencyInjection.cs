using Consolidado.Application.Telemetria;
using FluxoCaixa.Application.Common.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consolidado.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddConsolidadoApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(DependencyInjection).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        // IMeterFactory vem do host (registrado pelo ASP.NET Core).
        services.TryAddSingleton<ConsolidadoMetricas>();
        return services;
    }
}
