using FluxoCaixa.SharedKernel;

namespace Lancamentos.Domain.Lancamentos;

/// <summary>Erros de negócio do contexto Lançamentos (códigos estáveis, expostos na API).</summary>
public static class LancamentoErros
{
    public static readonly Error TipoInvalido =
        Error.Validation("lancamento.tipo_invalido", "O tipo deve ser Credito ou Debito.");

    public static readonly Error ValorDeveSerPositivo =
        Error.Validation("lancamento.valor_invalido", "O valor deve ser maior que zero.");

    public static readonly Error ValorComMaisDeDuasCasas =
        Error.Validation("lancamento.valor_invalido", "O valor deve ter no máximo 2 casas decimais.");

    public static readonly Error ValorAcimaDoLimite =
        Error.Validation("lancamento.valor_invalido", $"O valor deve ser no máximo {Dinheiro.Maximo:N2}.");

    public static readonly Error DescricaoInvalida =
        Error.Validation("lancamento.descricao_invalida", $"A descrição deve ter entre {Lancamento.DescricaoMinimo} e {Lancamento.DescricaoMaximo} caracteres.");

    public static readonly Error DataFutura =
        Error.Validation("lancamento.data_futura", "A data de competência não pode ser futura.");

    public static readonly Error NaoEncontrado =
        Error.NotFound("lancamento.nao_encontrado", "Lançamento não encontrado.");

    public static readonly Error JaEstornado =
        Error.Conflict("lancamento.ja_estornado", "O lançamento já foi estornado.");

    public static readonly Error EstornoNaoPodeSerEstornado =
        Error.Conflict("lancamento.estorno_nao_estornavel", "Um estorno não pode ser estornado.");

    public static readonly Error EstornoRestritoAoAdmin =
        Error.Forbidden("lancamento.estorno_restrito", "Somente o papel admin pode estornar lançamentos.");

    public static Error QuotaExcedida(string plano, int limite) =>
        Error.BusinessRule("lancamento.quota_excedida", $"O plano '{plano}' permite {limite} lançamentos por mês e o limite foi atingido.");
}
