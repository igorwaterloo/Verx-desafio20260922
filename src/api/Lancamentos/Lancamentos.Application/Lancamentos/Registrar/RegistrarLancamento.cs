using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Abstractions;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;

namespace Lancamentos.Application.Lancamentos.Registrar;

/// <param name="ChaveIdempotencia">Valor do header <c>Idempotency-Key</c> (RN-08), opcional.</param>
public sealed record RegistrarLancamentoCommand(
    TipoLancamento Tipo,
    decimal Valor,
    DateOnly DataCompetencia,
    string Descricao,
    string? ChaveIdempotencia) : ICommand<LancamentoDto>;

/// <summary>Validação de formato da entrada (erros por campo). As invariantes ficam no domínio.</summary>
public sealed class RegistrarLancamentoValidator : AbstractValidator<RegistrarLancamentoCommand>
{
    public const int ChaveIdempotenciaMaximo = 100;

    public RegistrarLancamentoValidator()
    {
        RuleFor(c => c.Tipo).IsInEnum().WithMessage("O tipo deve ser Credito ou Debito.");
        RuleFor(c => c.Valor)
            .GreaterThan(0).WithMessage("O valor deve ser maior que zero.")
            .LessThanOrEqualTo(Dinheiro.Maximo).WithMessage($"O valor deve ser no máximo {Dinheiro.Maximo:N2}.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage("O valor deve ter no máximo 2 casas decimais.");
        RuleFor(c => c.DataCompetencia).NotEmpty().WithMessage("A data de competência é obrigatória.");
        RuleFor(c => c.Descricao)
            .Must(d => d?.Trim().Length is >= Lancamento.DescricaoMinimo and <= Lancamento.DescricaoMaximo)
            .WithMessage($"A descrição deve ter entre {Lancamento.DescricaoMinimo} e {Lancamento.DescricaoMaximo} caracteres.");
        RuleFor(c => c.ChaveIdempotencia)
            .MaximumLength(ChaveIdempotenciaMaximo)
            .WithMessage($"O header Idempotency-Key deve ter no máximo {ChaveIdempotenciaMaximo} caracteres.");
    }
}

/// <summary>
/// Registra um lançamento: idempotência (RN-08) → quota do plano (RN-09) → regras do agregado →
/// persistência + evento no outbox na mesma transação (ADR-0005). Não depende de nenhum outro serviço (RNF-01).
/// </summary>
public sealed class RegistrarLancamentoHandler(
    ILancamentoRepository lancamentos,
    ITenantPlanoRepository planos,
    IIdempotenciaRepository idempotencia,
    IIntegrationEventPublisher publicador,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider tempo) : ICommandHandler<RegistrarLancamentoCommand, LancamentoDto>
{
    public async Task<Result<LancamentoDto>> HandleAsync(RegistrarLancamentoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await ObterJaRegistradoAsync(command.ChaveIdempotencia, cancellationToken) is { } jaRegistrado)
        {
            return jaRegistrado;
        }

        var agora = tempo.GetUtcNow();

        var plano = await planos.ObterAsync(cancellationToken);
        var (codigoPlano, limite) = plano is null
            ? (TenantPlano.CodigoPadrao, TenantPlano.LimitePadrao)
            : (plano.PlanoCodigo, plano.LimiteLancamentosMes);

        var usoNoMes = await lancamentos.ContarCriadosDesdeAsync(Calendario.InicioDoMes(agora), cancellationToken);
        if (usoNoMes >= limite)
        {
            return LancamentoErros.QuotaExcedida(codigoPlano, limite);
        }

        var criacao = Lancamento.Criar(
            tenant.TenantId, command.Tipo, command.Valor, command.DataCompetencia, command.Descricao,
            tenant.UsuarioId ?? "desconhecido", agora);
        if (criacao.IsFailure)
        {
            return criacao.Error;
        }

        var lancamento = criacao.Value;
        lancamentos.Adicionar(lancamento);
        if (command.ChaveIdempotencia is { } chave)
        {
            idempotencia.Registrar(chave, lancamento.Id, agora);
        }

        await publicador.PublishAsync(EventosDeIntegracao.LancamentoRegistrado(lancamento, agora), cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflitoDePersistenciaException) when (command.ChaveIdempotencia is not null)
        {
            // Outra requisição com a mesma chave venceu a corrida: devolve o lançamento dela.
            if (await ObterJaRegistradoAsync(command.ChaveIdempotencia, cancellationToken) is { } vencedor)
            {
                return vencedor;
            }

            throw;
        }

        return LancamentoDto.De(lancamento);
    }

    private async Task<LancamentoDto?> ObterJaRegistradoAsync(string? chave, CancellationToken cancellationToken)
    {
        if (chave is null || await idempotencia.ObterLancamentoIdAsync(chave, cancellationToken) is not { } id)
        {
            return null;
        }

        return await lancamentos.ObterPorIdAsync(id, cancellationToken) is { } existente
            ? LancamentoDto.De(existente)
            : null;
    }
}
