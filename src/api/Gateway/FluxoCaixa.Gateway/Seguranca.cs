using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixa.Gateway;

/// <summary>
/// Endurecimento HTTP na borda (SEG-06): headers de segurança em todas as respostas e limite de
/// tamanho do corpo antes de encaminhar aos serviços.
/// </summary>
public static class Seguranca
{
    public const string PoliticaCors = "spa";

    public static IServiceCollection AddCorsDaSpa(this IServiceCollection services, IConfiguration configuration)
    {
        var origens = configuration.GetSection("Cors:OrigensPermitidas").Get<string[]>() ?? [];

        services.AddCors(cors => cors.AddPolicy(PoliticaCors, politica => politica
            .WithOrigins(origens)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "Accept")
            .WithExposedHeaders("Location", "Retry-After", "api-supported-versions")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

        return services;
    }

    public static IApplicationBuilder UseCabecalhosDeSeguranca(this IApplicationBuilder app, long tamanhoMaximoDoCorpo) =>
        app.Use(async (http, proximo) =>
        {
            var cabecalhos = http.Response.Headers;
            http.Response.OnStarting(() =>
            {
                cabecalhos.XContentTypeOptions = "nosniff";
                cabecalhos.XFrameOptions = "DENY";
                cabecalhos["Referrer-Policy"] = "no-referrer";
                cabecalhos["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                // Respostas da API são JSON: nada deve ser executado ou embutido a partir delas.
                cabecalhos.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                cabecalhos.Remove("Server");
                return Task.CompletedTask;
            });

            if (http.Request.ContentLength > tamanhoMaximoDoCorpo)
            {
                http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await http.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status413PayloadTooLarge,
                        Title = "Requisição muito grande.",
                        Type = "https://httpstatuses.io/413",
                    },
                    options: null,
                    contentType: "application/problem+json");
                return;
            }

            await proximo(http);
        });
}
