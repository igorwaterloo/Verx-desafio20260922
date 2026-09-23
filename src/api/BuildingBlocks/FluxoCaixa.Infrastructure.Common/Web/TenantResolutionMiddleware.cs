using FluxoCaixa.Infrastructure.Common.Telemetria;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Infrastructure.Common.Web;

/// <summary>
/// Resolve o tenant da requisição a partir do token (claim <c>tenant_id</c>), MT-02 e MT-04.
/// Requisições anônimas seguem (a autorização de cada endpoint decide); usuário autenticado
/// sem tenant válido recebe 403.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext, ILogger<TenantResolutionMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (httpContext.User.Identity?.IsAuthenticated == true
            && !tenantContext.TentarDefinirAPartirDe(httpContext.User))
        {
            var problema = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Tenant não identificado",
                Detail = "O token não possui a claim 'tenant_id' válida.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            };

            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
            return;
        }

        // Tenant no span da requisição e em todos os logs dela (ADR-0011).
        using var escopo = tenantContext.HasTenant ? Observabilidade.MarcarTenant(logger, tenantContext.TenantId) : null;
        await next(httpContext);
    }
}

public static class TenancyExtensions
{
    /// <summary>Registra o contexto de tenant com escopo por requisição.</summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        return services;
    }

    /// <summary>Deve ser chamado após a autenticação e antes da autorização.</summary>
    public static IApplicationBuilder UseTenancy(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
