using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Abstractions;
using Lancamentos.Domain.Planos;

namespace Lancamentos.Application.Planos;

/// <summary>
/// Disparado pelos eventos TenantProvisionado / PlanoDoTenantAlterado. O tenant vem do contexto,
/// definido a partir do evento (MT-02). Idempotente: eventos repetidos ou antigos não alteram nada.
/// </summary>
public sealed record AtualizarPlanoDoTenantCommand(string PlanoCodigo, int LimiteLancamentosMes, DateTimeOffset OcorridoEm)
    : ICommand<Unit>;

public sealed class AtualizarPlanoDoTenantHandler(
    ITenantPlanoRepository planos,
    IUnitOfWork unitOfWork,
    ITenantContext tenant) : ICommandHandler<AtualizarPlanoDoTenantCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AtualizarPlanoDoTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var atual = await planos.ObterAsync(cancellationToken);
        if (atual is null)
        {
            planos.Adicionar(TenantPlano.Criar(tenant.TenantId, command.PlanoCodigo, command.LimiteLancamentosMes, command.OcorridoEm));
        }
        else if (!atual.Aplicar(command.PlanoCodigo, command.LimiteLancamentosMes, command.OcorridoEm))
        {
            return Unit.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
