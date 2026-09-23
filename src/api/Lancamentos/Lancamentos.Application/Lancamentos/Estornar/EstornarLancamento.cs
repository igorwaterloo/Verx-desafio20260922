using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Telemetria;
using Lancamentos.Domain.Lancamentos;

namespace Lancamentos.Application.Lancamentos.Estornar;

public sealed record EstornarLancamentoCommand(Guid LancamentoId) : ICommand<LancamentoDto>;

/// <summary>
/// Estorna um lançamento (RN-04 a RN-06), restrito ao papel admin (RN-10). O estorno é publicado
/// como um <c>LancamentoRegistrado</c> comum, com o tipo inverso. Estornos concorrentes são
/// barrados pelo índice único do banco e resultam em conflito.
/// </summary>
public sealed class EstornarLancamentoHandler(
    ILancamentoRepository lancamentos,
    IIntegrationEventPublisher publicador,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider tempo,
    LancamentosMetricas metricas) : ICommandHandler<EstornarLancamentoCommand, LancamentoDto>
{
    public async Task<Result<LancamentoDto>> HandleAsync(EstornarLancamentoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!tenant.IsInRole(TenantRoles.Admin))
        {
            return LancamentoErros.EstornoRestritoAoAdmin;
        }

        var original = await lancamentos.ObterPorIdAsync(command.LancamentoId, cancellationToken);
        if (original is null)
        {
            return LancamentoErros.NaoEncontrado;
        }

        var agora = tempo.GetUtcNow();
        var estorno = original.Estornar(tenant.UsuarioId ?? "desconhecido", agora);
        if (estorno.IsFailure)
        {
            return estorno.Error;
        }

        lancamentos.Adicionar(estorno.Value);
        await publicador.PublishAsync(EventosDeIntegracao.LancamentoRegistrado(estorno.Value, agora), cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflitoDePersistenciaException)
        {
            return LancamentoErros.JaEstornado;
        }

        metricas.Registrado(estorno.Value);
        return LancamentoDto.De(estorno.Value);
    }
}
