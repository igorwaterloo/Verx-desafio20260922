using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using Tenants.Application.Abstractions;

namespace Tenants.Application.Tenants.Expirar;

/// <summary>Executado periodicamente: onboarding abandonado (Pendente há muito tempo) é compensado.</summary>
public sealed record ExpirarProvisionamentosPendentesCommand(TimeSpan IdadeMaxima) : ICommand<int>;

public sealed class ExpirarProvisionamentosPendentesHandler(
    ITenantRepository tenants,
    IProvedorDeIdentidade identidade,
    IUnitOfWork unitOfWork,
    TimeProvider tempo) : ICommandHandler<ExpirarProvisionamentosPendentesCommand, int>
{
    public async Task<Result<int>> HandleAsync(ExpirarProvisionamentosPendentesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var agora = tempo.GetUtcNow();
        var expirados = 0;

        foreach (var tenant in await tenants.ListarPendentesCriadosAntesDeAsync(agora - command.IdadeMaxima, cancellationToken))
        {
            try
            {
                await identidade.RemoverOrganizacaoAsync(tenant.Id, cancellationToken);
            }
            catch (ProvedorDeIdentidadeIndisponivelException)
            {
                continue; // tenta de novo na próxima execução
            }

            tenant.MarcarFalha("Provisionamento não concluído dentro do prazo.", agora);
            expirados++;
        }

        if (expirados > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return expirados;
    }
}
