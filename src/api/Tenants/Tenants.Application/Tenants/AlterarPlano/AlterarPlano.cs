using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Tenants.AlterarPlano;

public sealed record AlterarPlanoCommand(string PlanoCodigo) : ICommand<TenantDto>;

/// <summary>
/// Troca de plano do tenant corrente (admin). A identidade é atualizada antes do commit: se o
/// Keycloak estiver fora, nada muda (503). O novo plano chega ao Lançamentos pelo evento
/// PlanoDoTenantAlterado (quota) e ao gateway pela claim <c>plano</c> no próximo token (ADR-0017).
/// </summary>
public sealed class AlterarPlanoHandler(
    ITenantRepository tenants,
    IProvedorDeIdentidade identidade,
    IIntegrationEventPublisher publicador,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    TimeProvider tempo) : ICommandHandler<AlterarPlanoCommand, TenantDto>
{
    public async Task<Result<TenantDto>> HandleAsync(AlterarPlanoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!tenantContext.IsInRole(TenantRoles.Admin))
        {
            return TenantErros.SomenteAdmin;
        }

        var tenant = await tenants.ObterPorIdAsync(tenantContext.TenantId, cancellationToken);
        if (tenant is null)
        {
            return TenantErros.NaoEncontrado;
        }

        var agora = tempo.GetUtcNow();
        try
        {
            var usuarios = tenant.OrganizationId is null
                ? 0
                : (await identidade.ListarUsuariosAsync(tenant.OrganizationId, cancellationToken)).Count;

            var alteracao = tenant.AlterarPlano(command.PlanoCodigo, usuarios, agora);
            if (alteracao.IsFailure)
            {
                return alteracao.Error;
            }

            await identidade.AtualizarPlanoAsync(tenant.OrganizationId!, tenant.Id, tenant.PlanoCodigo, cancellationToken);
        }
        catch (ProvedorDeIdentidadeIndisponivelException)
        {
            return TenantErros.ProvedorDeIdentidadeIndisponivel;
        }

        await publicador.PublishAsync(EventosDeIntegracao.PlanoDoTenantAlterado(tenant, agora), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TenantDto.De(tenant);
    }
}
