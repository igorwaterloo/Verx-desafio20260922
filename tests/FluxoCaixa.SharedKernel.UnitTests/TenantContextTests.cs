using System.Security.Claims;
using FluxoCaixa.SharedKernel.Tenancy;
using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class TenantContextTests
{
    private static readonly Guid TenantA = Guid.Parse("0192f79e-0001-7000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("0192f79e-0002-7000-8000-000000000002");

    [Fact]
    public void NovoContexto_NaoPossuiTenant()
    {
        var contexto = new TenantContext();

        contexto.HasTenant.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => contexto.TenantId);
    }

    [Fact]
    public void Definir_PreencheTenantUsuarioEPapeis()
    {
        var contexto = new TenantContext();

        contexto.Definir(TenantA, "usuario-1", [TenantRoles.Admin]);

        contexto.HasTenant.ShouldBeTrue();
        contexto.TenantId.ShouldBe(TenantA);
        contexto.UsuarioId.ShouldBe("usuario-1");
        contexto.IsInRole(TenantRoles.Admin).ShouldBeTrue();
        contexto.IsInRole(TenantRoles.Operador).ShouldBeFalse();
    }

    [Fact]
    public void Definir_ComTenantVazio_LancaExcecao()
    {
        Should.Throw<ArgumentException>(() => new TenantContext().Definir(Guid.Empty, "u", []));
    }

    [Fact]
    public void Definir_OutroTenantNoMesmoEscopo_LancaExcecao()
    {
        var contexto = new TenantContext();
        contexto.Definir(TenantA, "u", []);

        Should.Throw<InvalidOperationException>(() => contexto.Definir(TenantB, "u", []));
    }

    [Fact]
    public void TentarDefinirAPartirDe_ClaimsValidas_PreencheOContexto()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(TenantClaims.TenantId, TenantA.ToString()),
            new Claim(TenantClaims.Usuario, "usuario-1"),
            new Claim(TenantClaims.Roles, TenantRoles.Operador),
            new Claim(TenantClaims.Roles, TenantRoles.Admin),
        ], authenticationType: "Bearer"));
        var contexto = new TenantContext();

        var definido = contexto.TentarDefinirAPartirDe(principal);

        definido.ShouldBeTrue();
        contexto.TenantId.ShouldBe(TenantA);
        contexto.UsuarioId.ShouldBe("usuario-1");
        contexto.Roles.ShouldBe([TenantRoles.Operador, TenantRoles.Admin], ignoreOrder: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TentarDefinirAPartirDe_SemTenantValido_RetornaFalso(string? tenantId)
    {
        var claims = new List<Claim> { new(TenantClaims.Usuario, "usuario-1") };
        if (tenantId is not null)
        {
            claims.Add(new Claim(TenantClaims.TenantId, tenantId));
        }

        var contexto = new TenantContext();

        contexto.TentarDefinirAPartirDe(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))).ShouldBeFalse();
        contexto.HasTenant.ShouldBeFalse();
    }
}
