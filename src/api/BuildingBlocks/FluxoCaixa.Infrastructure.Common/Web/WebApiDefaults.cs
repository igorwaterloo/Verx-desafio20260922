using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace FluxoCaixa.Infrastructure.Common.Web;

/// <summary>
/// Configuração padrão das Web APIs da plataforma: controllers, versionamento na URL,
/// ProblemDetails (RFC 9457), OpenAPI/Scalar, health checks (ADR-0016) e resolução de tenant (ADR-0015).
/// </summary>
public static class WebApiDefaults
{
    public static IServiceCollection AddWebApiDefaults(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddProblemDetails();

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            })
            .AddOpenApi();

        services.AddHealthChecks();
        services.AddTenancy();

        return services;
    }

    public static WebApplication UseWebApiDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().WithDocumentPerVersion();
            app.MapScalarApiReference(options =>
            {
                foreach (var description in app.DescribeApiVersions())
                {
                    options.AddDocument(description.GroupName, description.GroupName);
                }
            });
        }

        // Liveness: só o processo. Readiness: inclui as dependências registradas por cada serviço.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");

        // Autenticação/autorização entram na Fase 4; o tenant é resolvido logo após a autenticação.
        app.UseTenancy();
        app.MapControllers();

        return app;
    }
}
