using FluxoCaixa.Contracts;
using FluxoCaixa.SharedKernel;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Abstractions;

public interface ITenantRepository
{
    Task<Tenant?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tenant?> ObterPorCnpjAsync(string cnpjNumero, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tenant>> ListarPendentesCriadosAntesDeAsync(DateTimeOffset limite, CancellationToken cancellationToken);

    void Adicionar(Tenant tenant);
}

/// <summary>
/// Provedor de identidade (Keycloak — ADR-0008/0017). Operações idempotentes para permitir retomar
/// um onboarding interrompido. Indisponibilidade é sinalizada por
/// <see cref="ProvedorDeIdentidadeIndisponivelException"/>.
/// </summary>
public interface IProvedorDeIdentidade
{
    /// <summary>Retorna a Organization do tenant, criando-a se ainda não existir.</summary>
    Task<string> GarantirOrganizacaoAsync(Guid tenantId, string nome, string planoCodigo, CancellationToken cancellationToken);

    /// <summary>
    /// Cria o usuário na organização com o papel informado. Se o e-mail já pertence a um usuário do
    /// mesmo tenant (retomada), retorna esse usuário; se pertence a outro, retorna conflito.
    /// </summary>
    Task<Result<string>> CriarUsuarioAsync(NovoUsuarioDoTenant usuario, CancellationToken cancellationToken);

    Task<IReadOnlyList<UsuarioDoTenant>> ListarUsuariosAsync(string organizationId, CancellationToken cancellationToken);

    /// <summary>Atualiza o plano na organização e nos usuários (claim <c>plano</c> usada pelo gateway).</summary>
    Task AtualizarPlanoAsync(string organizationId, Guid tenantId, string planoCodigo, CancellationToken cancellationToken);

    /// <summary>Compensação do onboarding: remove a organização do tenant, se existir.</summary>
    Task RemoverOrganizacaoAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <param name="Senha">Repassada ao provedor de identidade; nunca persistida nem logada (RP-03).</param>
public sealed record NovoUsuarioDoTenant(
    string OrganizationId, Guid TenantId, string Plano, string Nome, string Email, string Senha, string Papel)
{
    public override string ToString() => $"NovoUsuarioDoTenant {{ Email = {Email}, Papel = {Papel}, TenantId = {TenantId} }}";
}

public sealed record UsuarioDoTenant(string Id, string Email, string Nome, bool Habilitado);

public sealed class ProvedorDeIdentidadeIndisponivelException : Exception
{
    public ProvedorDeIdentidadeIndisponivelException()
    {
    }

    public ProvedorDeIdentidadeIndisponivelException(string message)
        : base(message)
    {
    }

    public ProvedorDeIdentidadeIndisponivelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Grava no Transactional Outbox, na mesma transação do <see cref="IUnitOfWork"/> (ADR-0005).</summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvento>(TEvento evento, CancellationToken cancellationToken)
        where TEvento : class, IIntegrationEvent;
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
