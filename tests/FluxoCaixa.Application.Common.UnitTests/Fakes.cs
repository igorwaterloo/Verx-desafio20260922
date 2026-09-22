using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;

namespace FluxoCaixa.Application.Common.UnitTests;

/// <summary>Registra quantas vezes os handlers de teste foram executados.</summary>
public sealed class Espiao
{
    public int Execucoes { get; private set; }

    public List<string> Ordem { get; } = [];

    public void Registrar(string etapa)
    {
        Execucoes++;
        Ordem.Add(etapa);
    }
}

public sealed record CriarItem(string Nome) : ICommand<Guid>;

public sealed class CriarItemHandler(Espiao espiao) : ICommandHandler<CriarItem, Guid>
{
    public static readonly Guid IdGerado = Guid.Parse("0192f7a0-0000-7000-8000-000000000001");

    public Task<Result<Guid>> HandleAsync(CriarItem command, CancellationToken cancellationToken)
    {
        espiao.Registrar("handler");
        return Task.FromResult<Result<Guid>>(IdGerado);
    }
}

public sealed class CriarItemValidator : AbstractValidator<CriarItem>
{
    public CriarItemValidator() => RuleFor(c => c.Nome).NotEmpty().WithMessage("O nome é obrigatório.");
}

public sealed record ObterItem(Guid Id) : IQuery<string>;

public sealed class ObterItemHandler : IQueryHandler<ObterItem, string>
{
    public Task<Result<string>> HandleAsync(ObterItem query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(query.Id == Guid.Empty
            ? Error.NotFound("item.nao_encontrado", "Item não encontrado.")
            : $"item-{query.Id}");
}

public sealed record ComandoSemHandler : ICommand<Unit>;
