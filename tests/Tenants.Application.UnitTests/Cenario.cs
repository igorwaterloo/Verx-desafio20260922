using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.UnitTests;

internal sealed class Cenario
{
    public static readonly DateTimeOffset Agora = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    public ITenantRepository Tenants { get; } = Substitute.For<ITenantRepository>();

    public IProvedorDeIdentidade Identidade { get; } = Substitute.For<IProvedorDeIdentidade>();

    public IIntegrationEventPublisher Publicador { get; } = Substitute.For<IIntegrationEventPublisher>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public TenantContext Tenancy { get; } = new();

    public FakeTimeProvider Tempo { get; } = new(Agora);

    public static Tenant TenantAtivo(string plano = "free")
    {
        var tenant = Tenant.Criar("Loja Exemplo Ltda", null, "11222333000181", plano, "admin@loja.dev", Agora.AddDays(-1)).Value;
        tenant.Ativar("org-1", Agora.AddDays(-1));
        tenant.ClearDomainEvents();
        return tenant;
    }

    public Cenario ComoUsuarioDo(Tenant tenant, params string[] roles)
    {
        Tenancy.Definir(tenant.Id, "usuario-1", roles);
        Tenants.ObterPorIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        return this;
    }

    public Cenario ComUsuariosNoTenant(int quantidade)
    {
        IReadOnlyList<UsuarioDoTenant> usuarios = Enumerable.Range(1, quantidade)
            .Select(i => new UsuarioDoTenant($"u{i}", $"u{i}@loja.dev", $"Usuário {i}", true))
            .ToList();
        Identidade.ListarUsuariosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(usuarios);
        return this;
    }

    public void IdentidadeCriaUsuarios() =>
        Identidade.CriarUsuarioAsync(Arg.Any<NovoUsuarioDoTenant>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("usuario-novo"));

    public T? UltimoEvento<T>()
        where T : class =>
        Publicador.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<T>().LastOrDefault();
}
