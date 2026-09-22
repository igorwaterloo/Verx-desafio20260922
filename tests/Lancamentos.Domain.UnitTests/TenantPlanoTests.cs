using Lancamentos.Domain.Planos;
using Shouldly;

namespace Lancamentos.Domain.UnitTests;

public sealed class TenantPlanoTests
{
    private static readonly Guid Tenant = Guid.Parse("0192f79e-0002-7000-8000-000000000002");
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criar_DefineOPlanoDoTenant()
    {
        var plano = TenantPlano.Criar(Tenant, "pro", 50_000, T0);

        plano.TenantId.ShouldBe(Tenant);
        plano.PlanoCodigo.ShouldBe("pro");
        plano.LimiteLancamentosMes.ShouldBe(50_000);
        plano.AtualizadoEm.ShouldBe(T0);
    }

    [Fact]
    public void Aplicar_EventoMaisRecente_AtualizaOPlano()
    {
        var plano = TenantPlano.Criar(Tenant, "free", 1_000, T0);

        plano.Aplicar("pro", 50_000, T0.AddMinutes(1)).ShouldBeTrue();

        plano.PlanoCodigo.ShouldBe("pro");
        plano.LimiteLancamentosMes.ShouldBe(50_000);
    }

    [Fact]
    public void Aplicar_EventoMaisAntigoOuRepetido_EhIgnorado()
    {
        var plano = TenantPlano.Criar(Tenant, "pro", 50_000, T0);

        plano.Aplicar("free", 1_000, T0.AddMinutes(-1)).ShouldBeFalse();
        plano.Aplicar("free", 1_000, T0).ShouldBeFalse();

        plano.PlanoCodigo.ShouldBe("pro");
    }

    [Fact]
    public void PlanoPadrao_EhOFree()
    {
        TenantPlano.CodigoPadrao.ShouldBe("free");
        TenantPlano.LimitePadrao.ShouldBe(1_000);
    }
}
