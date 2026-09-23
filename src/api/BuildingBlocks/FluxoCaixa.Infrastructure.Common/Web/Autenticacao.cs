using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxoCaixa.Infrastructure.Common.Web;

/// <summary>Políticas de autorização por papel do tenant (RN-10).</summary>
public static class Politicas
{
    public const string Operador = "Operador";
    public const string Admin = "Admin";
}

public static class AutenticacaoExtensions
{
    /// <summary>
    /// Autenticação JWT emitida pelo Keycloak (ADR-0008) e políticas por papel (RN-10). Usada pelas
    /// Web APIs e pelo Gateway (validação em profundidade). Seguro por padrão: sem [AllowAnonymous],
    /// exige usuário autenticado.
    /// </summary>
    public static IServiceCollection AddAutenticacaoKeycloak(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
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

        return services;
    }
}
