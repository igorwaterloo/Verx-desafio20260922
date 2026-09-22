using System.Security.Claims;
using FluxoCaixa.Infrastructure.Common.Web;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace FluxoCaixa.Infrastructure.Common.UnitTests.Web;

public sealed class TenantResolutionMiddlewareTests
{
    private static readonly Guid TenantA = Guid.Parse("0192f79e-0001-7000-8000-000000000001");

    private bool _proximoChamado;

    private TenantResolutionMiddleware CriarMiddleware() => new(_ =>
    {
        _proximoChamado = true;
        return Task.CompletedTask;
    });

    [Fact]
    public async Task RequisicaoAnonima_SegueSemDefinirTenant()
    {
        var http = new DefaultHttpContext();
        var tenant = new TenantContext();

        await CriarMiddleware().InvokeAsync(http, tenant);

        _proximoChamado.ShouldBeTrue();
        tenant.HasTenant.ShouldBeFalse();
    }

    [Fact]
    public async Task UsuarioAutenticadoComTenant_DefineOContextoESegue()
    {
        var http = new DefaultHttpContext { User = Usuario(new Claim(TenantClaims.TenantId, TenantA.ToString())) };
        var tenant = new TenantContext();

        await CriarMiddleware().InvokeAsync(http, tenant);

        _proximoChamado.ShouldBeTrue();
        tenant.TenantId.ShouldBe(TenantA);
        tenant.UsuarioId.ShouldBe("usuario-1");
    }

    [Fact]
    public async Task UsuarioAutenticadoSemTenant_Responde403ComProblemDetails()
    {
        var http = new DefaultHttpContext { User = Usuario() };
        http.Response.Body = new MemoryStream();
        var tenant = new TenantContext();

        await CriarMiddleware().InvokeAsync(http, tenant);

        _proximoChamado.ShouldBeFalse();
        http.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        http.Response.ContentType.ShouldNotBeNull().ShouldStartWith("application/problem+json");
    }

    private static ClaimsPrincipal Usuario(params Claim[] extras) =>
        new(new ClaimsIdentity([new Claim(TenantClaims.Usuario, "usuario-1"), .. extras], authenticationType: "Bearer"));
}
