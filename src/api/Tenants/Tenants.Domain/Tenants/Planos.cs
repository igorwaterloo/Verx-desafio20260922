namespace Tenants.Domain.Tenants;

/// <summary>Plano de assinatura do SaaS (ADR-0017): quotas e taxa de requisições por tenant.</summary>
public sealed record Plano(
    string Codigo,
    string Nome,
    int LimiteLancamentosMes,
    int LimiteUsuarios,
    int RequisicoesPorSegundo,
    decimal PrecoMensal);

/// <summary>Catálogo global de planos — fonte da verdade, propagada aos outros contextos por eventos.</summary>
public static class CatalogoDePlanos
{
    public static readonly Plano Free = new("free", "Free", 1_000, 2, 20, 0m);
    public static readonly Plano Pro = new("pro", "Pro", 50_000, 20, 100, 99m);

    public static readonly IReadOnlyList<Plano> Todos = [Free, Pro];

    public static Plano? Obter(string? codigo) =>
        Todos.FirstOrDefault(p => string.Equals(p.Codigo, codigo, StringComparison.Ordinal));
}
