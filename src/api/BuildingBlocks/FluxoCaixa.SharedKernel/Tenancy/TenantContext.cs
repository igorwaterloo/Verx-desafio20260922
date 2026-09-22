using System.Security.Claims;

namespace FluxoCaixa.SharedKernel.Tenancy;

/// <summary>Nomes das claims emitidas pelo Keycloak (ADR-0008, ADR-0015).</summary>
public static class TenantClaims
{
    public const string TenantId = "tenant_id";
    public const string Plano = "plano";
    public const string Usuario = "sub";
    public const string Roles = "roles";
}

/// <summary>Papéis do tenant (RN-10).</summary>
public static class TenantRoles
{
    public const string Admin = "admin";
    public const string Operador = "operador";
}

/// <summary>
/// Identidade da execução corrente: tenant, usuário e papéis.
/// Preenchido a partir do token (HTTP) ou do evento (mensageria) — nunca do corpo da requisição (MT-02).
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    /// <exception cref="InvalidOperationException">Quando não há tenant definido.</exception>
    Guid TenantId { get; }

    string? UsuarioId { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}

/// <summary>Entidade de negócio que pertence a um tenant (MT-01).</summary>
public interface ITenantEntity
{
    Guid TenantId { get; }
}

/// <summary>
/// Implementação do contexto de tenant, com escopo por requisição/mensagem.
/// O tenant é definido uma única vez por escopo: tentar trocá-lo indica um erro de programação.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;
    private HashSet<string> _roles = new(StringComparer.Ordinal);

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("Nenhum tenant definido para a execução corrente.");

    public string? UsuarioId { get; private set; }

    public IReadOnlyCollection<string> Roles => _roles;

    public bool IsInRole(string role) => _roles.Contains(role);

    public void Definir(Guid tenantId, string? usuarioId, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("O tenant não pode ser vazio.", nameof(tenantId));
        }

        if (_tenantId.HasValue && _tenantId.Value != tenantId)
        {
            throw new InvalidOperationException("O tenant da execução corrente não pode ser alterado.");
        }

        _tenantId = tenantId;
        UsuarioId = usuarioId;
        _roles = new HashSet<string>(roles, StringComparer.Ordinal);
    }

    /// <summary>
    /// Define o contexto a partir das claims do token. Retorna <c>false</c> se não houver
    /// uma claim <c>tenant_id</c> válida.
    /// </summary>
    public bool TentarDefinirAPartirDe(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var valor = principal.FindFirst(TenantClaims.TenantId)?.Value;
        if (!Guid.TryParse(valor, out var tenantId) || tenantId == Guid.Empty)
        {
            return false;
        }

        var roles = principal.FindAll(TenantClaims.Roles).Select(c => c.Value);
        Definir(tenantId, principal.FindFirst(TenantClaims.Usuario)?.Value, roles);
        return true;
    }
}
