using FluxoCaixa.Application.Common.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenants.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTenantsApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(DependencyInjection).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
