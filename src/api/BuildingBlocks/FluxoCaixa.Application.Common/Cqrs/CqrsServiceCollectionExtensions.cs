using System.Reflection;
using FluentValidation;
using FluxoCaixa.SharedKernel.Cqrs;
using Microsoft.Extensions.DependencyInjection;

namespace FluxoCaixa.Application.Common.Cqrs;

public static class CqrsServiceCollectionExtensions
{
    private static readonly Type[] InterfacesDeHandler = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    /// <summary>
    /// Registra o dispatcher, os handlers e validadores encontrados nos assemblies informados
    /// e os decorators padrão (log → validação → handler).
    /// </summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        services.AddScoped<IDispatcher, Dispatcher>();

        foreach (var tipo in assemblies.SelectMany(a => a.DefinedTypes).Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contrato in tipo.ImplementedInterfaces.Where(EhHandler))
            {
                services.AddScoped(contrato, tipo);
            }
        }

        services.AddValidatorsFromAssemblies(assemblies, ServiceLifetime.Scoped, includeInternalTypes: true);

        services.AddScoped(typeof(IRequestDecorator<,>), typeof(LoggingDecorator<,>));
        services.AddScoped(typeof(IRequestDecorator<,>), typeof(ValidationDecorator<,>));

        return services;
    }

    private static bool EhHandler(Type contrato) =>
        contrato.IsGenericType && InterfacesDeHandler.Contains(contrato.GetGenericTypeDefinition());
}
