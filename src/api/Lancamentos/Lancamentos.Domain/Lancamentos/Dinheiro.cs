using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Domain;

namespace Lancamentos.Domain.Lancamentos;

/// <summary>
/// Quantia monetária positiva em BRL, com até 2 casas decimais (RN-01).
/// O sinal do movimento é dado pelo tipo do lançamento, nunca pelo valor.
/// </summary>
public sealed class Dinheiro : ValueObject
{
    public const decimal Maximo = 999_999_999.99m;

    private Dinheiro(decimal valor) => Valor = valor;

    public decimal Valor { get; }

    public static Result<Dinheiro> Criar(decimal valor)
    {
        if (valor <= 0)
        {
            return LancamentoErros.ValorDeveSerPositivo;
        }

        if (valor > Maximo)
        {
            return LancamentoErros.ValorAcimaDoLimite;
        }

        if (decimal.Round(valor, 2) != valor)
        {
            return LancamentoErros.ValorComMaisDeDuasCasas;
        }

        return new Dinheiro(valor);
    }

    public override string ToString() => Valor.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        // decimal 1.5m e 1.50m são iguais; o componente normalizado evita diferença de escala no hash.
        yield return decimal.Round(Valor, 2);
    }
}
