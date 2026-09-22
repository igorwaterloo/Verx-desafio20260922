namespace Lancamentos.Domain.Lancamentos;

public enum TipoLancamento
{
    /// <summary>Aumenta o saldo (ex.: venda recebida).</summary>
    Credito = 1,

    /// <summary>Diminui o saldo (ex.: pagamento a fornecedor).</summary>
    Debito = 2,
}

public static class TipoLancamentoExtensions
{
    public static TipoLancamento Inverso(this TipoLancamento tipo) =>
        tipo == TipoLancamento.Credito ? TipoLancamento.Debito : TipoLancamento.Credito;
}
