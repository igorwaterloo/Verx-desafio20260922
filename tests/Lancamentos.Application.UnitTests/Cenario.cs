using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Telemetria;
using Lancamentos.Domain.Lancamentos;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Lancamentos.Application.UnitTests;

/// <summary>Dublês e dados comuns aos testes dos handlers.</summary>
internal sealed class Cenario
{
    public static readonly Guid Tenant = Guid.Parse("0192f79e-0002-7000-8000-000000000002");

    // 22/09/2026 15:00 em São Paulo.
    public static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Hoje = new(2026, 9, 22);

    public Cenario(params string[] roles)
    {
        Tenancy.Definir(Tenant, "usuario-1", roles.Length == 0 ? [TenantRoles.Operador] : roles);
    }

    public ILancamentoRepository Lancamentos { get; } = Substitute.For<ILancamentoRepository>();

    public ITenantPlanoRepository Planos { get; } = Substitute.For<ITenantPlanoRepository>();

    public IIdempotenciaRepository Idempotencia { get; } = Substitute.For<IIdempotenciaRepository>();

    public IIntegrationEventPublisher Publicador { get; } = Substitute.For<IIntegrationEventPublisher>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public TenantContext Tenancy { get; } = new();

    public FakeTimeProvider Tempo { get; } = new(Agora);

    public LancamentosMetricas Metricas { get; } = new(new MedidoresDeTeste());

    public static Lancamento LancamentoExistente(TipoLancamento tipo = TipoLancamento.Credito) =>
        Lancamento.Criar(Tenant, tipo, 100m, Hoje, "Venda existente", "usuario-0", Agora.AddHours(-1)).Value;

    public FluxoCaixa.Contracts.LancamentoRegistrado? UltimoEventoPublicado() =>
        Publicador.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<FluxoCaixa.Contracts.LancamentoRegistrado>()
            .LastOrDefault();
}
