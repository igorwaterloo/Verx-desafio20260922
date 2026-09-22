using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using Lancamentos.Application.Abstractions;

namespace Lancamentos.Application.Lancamentos.Listar;

public sealed record ListarLancamentosQuery(DateOnly Data, int Pagina = 1, int TamanhoPagina = 50)
    : IQuery<Pagina<LancamentoDto>>;

public sealed class ListarLancamentosValidator : AbstractValidator<ListarLancamentosQuery>
{
    public const int TamanhoMaximo = 100;

    public ListarLancamentosValidator()
    {
        RuleFor(q => q.Data).NotEmpty().WithMessage("A data é obrigatória.");
        RuleFor(q => q.Pagina).GreaterThanOrEqualTo(1).WithMessage("A página começa em 1.");
        RuleFor(q => q.TamanhoPagina).InclusiveBetween(1, TamanhoMaximo)
            .WithMessage($"O tamanho da página deve estar entre 1 e {TamanhoMaximo}.");
    }
}

public sealed class ListarLancamentosHandler(ILancamentosLeitura leitura)
    : IQueryHandler<ListarLancamentosQuery, Pagina<LancamentoDto>>
{
    public async Task<Result<Pagina<LancamentoDto>>> HandleAsync(ListarLancamentosQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await leitura.ListarPorDataAsync(query.Data, query.Pagina, query.TamanhoPagina, cancellationToken);
    }
}
