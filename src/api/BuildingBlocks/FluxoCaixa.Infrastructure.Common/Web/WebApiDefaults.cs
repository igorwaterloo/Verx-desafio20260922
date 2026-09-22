using System.Text.Json.Serialization;
using Asp.Versioning;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace FluxoCaixa.Infrastructure.Common.Web;

/// <summary>Políticas de autorização por papel do tenant (RN-10).</summary>
public static class Politicas
{
    public const string Operador = "Operador";
    public const string Admin = "Admin";
}

/// <summary>
/// Configuração padrão das Web APIs da plataforma: controllers, versionamento na URL,
/// ProblemDetails (RFC 9457), OpenAPI/Scalar, health checks (ADR-0016), autenticação JWT do
/// Keycloak com autorização por papel (ADR-0008) e resolução de tenant (ADR-0015).
/// </summary>
public static class WebApiDefaults
{
    public static IServiceCollection AddWebApiDefaults(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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
        services.AddAutenticacaoKeycloak(configuration);

        return services;
    }

    public static WebApplication UseWebApiDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.UseAuthentication();
        app.UseTenancy();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();
            app.MapScalarApiReference(options =>
            {
                foreach (var description in app.DescribeApiVersions())
                {
                    options.AddDocument(description.GroupName, description.GroupName);
                }
            }).AllowAnonymous();
        }

        // Liveness: só o processo. Readiness: dependências marcadas com a tag "ready" (banco, broker).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        app.MapControllers();

        return app;
    }

    private static void AddAutenticacaoKeycloak(this IServiceCollection services, IConfiguration configuration)
    {
        var opcoes = configuration.GetSection("Autenticacao");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Metadados/JWKS obtidos pelo endereço interno do Keycloak; o emissor (iss) é o público.
                options.Authority = opcoes["Authority"];
                options.Audience = opcoes["Audiencia"] ?? "fluxo-caixa-api";
                options.RequireHttpsMetadata = opcoes.GetValue("RequireHttpsMetadata", defaultValue: true);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = TenantClaims.Roles;
                if (opcoes["EmissorValido"] is { Length: > 0 } emissor)
                {
                    options.TokenValidationParameters.ValidIssuer = emissor;
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Operador, p => p.RequireRole(TenantRoles.Operador, TenantRoles.Admin))
            .AddPolicy(Politicas.Admin, p => p.RequireRole(TenantRoles.Admin))
            // Seguro por padrão: todo endpoint exige autenticação, salvo [AllowAnonymous].
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
    }
}
