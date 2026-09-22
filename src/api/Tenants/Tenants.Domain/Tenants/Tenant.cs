using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Domain;

namespace Tenants.Domain.Tenants;

public enum StatusTenant
{
    /// <summary>Gravado, aguardando o provisionamento da identidade (Organization + admin no Keycloak).</summary>
    Pendente = 1,
    Ativo = 2,
    Falhou = 3,
}

/// <summary>
/// Empresa cliente do SaaS (contexto Plataforma — ADR-0017). O <see cref="Entity{TId}.Id"/> é o
/// <c>TenantId</c> de todo o sistema. Não implementa ITenantEntity: é o próprio cadastro de tenants.
/// </summary>
public sealed class Tenant : AggregateRoot<Guid>
{
    private Tenant(Guid id, string razaoSocial, string? nomeFantasia, Cnpj cnpj, string planoCodigo, string adminEmail, DateTimeOffset agora)
        : base(id)
    {
        RazaoSocial = razaoSocial;
        NomeFantasia = nomeFantasia;
        Cnpj = cnpj;
        PlanoCodigo = planoCodigo;
        AdminEmail = adminEmail;
        Status = StatusTenant.Pendente;
        CriadoEm = agora;
        AtualizadoEm = agora;
    }

    /// <summary>Construtor para materialização pelo EF Core.</summary>
    private Tenant()
    {
        RazaoSocial = null!;
        Cnpj = null!;
        PlanoCodigo = null!;
        AdminEmail = null!;
    }

    public string RazaoSocial { get; private set; }

    public string? NomeFantasia { get; private set; }

    public Cnpj Cnpj { get; private set; }

    public string PlanoCodigo { get; private set; }

    public Plano Plano => CatalogoDePlanos.Obter(PlanoCodigo)
        ?? throw new InvalidOperationException($"Plano '{PlanoCodigo}' fora do catálogo.");

    public StatusTenant Status { get; private set; }

    public string? OrganizationId { get; private set; }

    public string AdminEmail { get; private set; }

    public string? MotivoFalha { get; private set; }

    public int TentativasDeProvisionamento { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public static Result<Tenant> Criar(
        string razaoSocial, string? nomeFantasia, string cnpj, string planoCodigo, string adminEmail, DateTimeOffset agora)
    {
        var razao = razaoSocial?.Trim() ?? string.Empty;
        if (razao.Length is < 3 or > 150)
        {
            return TenantErros.RazaoSocialInvalida;
        }

        var documento = Cnpj.Criar(cnpj);
        if (documento.IsFailure)
        {
            return documento.Error;
        }

        if (CatalogoDePlanos.Obter(planoCodigo) is null)
        {
            return TenantErros.PlanoInexistente;
        }

        var email = adminEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length > 254 || email.IndexOf('@', StringComparison.Ordinal) is < 1 || email.EndsWith('@'))
        {
            return TenantErros.EmailInvalido;
        }

        var fantasia = string.IsNullOrWhiteSpace(nomeFantasia) ? null : nomeFantasia.Trim();
        return new Tenant(Guid.CreateVersion7(agora), razao, fantasia, documento.Value, planoCodigo, email, agora);
    }

    /// <summary>Tenant já existente na identidade (ex.: tenants de demonstração do realm local).</summary>
    public static Tenant Importar(
        Guid id, string razaoSocial, string cnpj, string planoCodigo, string organizationId, string adminEmail, DateTimeOffset agora)
    {
        var documento = Cnpj.Criar(cnpj);
        if (documento.IsFailure || CatalogoDePlanos.Obter(planoCodigo) is null)
        {
            throw new ArgumentException("Dados de importação inválidos.");
        }

        return new Tenant(id, razaoSocial, null, documento.Value, planoCodigo, adminEmail, agora)
        {
            Status = StatusTenant.Ativo,
            OrganizationId = organizationId,
        };
    }

    public void RegistrarTentativa(DateTimeOffset agora)
    {
        TentativasDeProvisionamento++;
        AtualizadoEm = agora;
    }

    /// <summary>Conclui o onboarding: a Organization e o admin existem no provedor de identidade (RP-02).</summary>
    public void Ativar(string organizationId, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        GarantirPendente();

        Status = StatusTenant.Ativo;
        OrganizationId = organizationId;
        AtualizadoEm = agora;
        RaiseDomainEvent(new TenantAtivado(Id, PlanoCodigo, agora));
    }

    /// <summary>Falha definitiva do onboarding, após a compensação (RP-02).</summary>
    public void MarcarFalha(string motivo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        GarantirPendente();

        Status = StatusTenant.Falhou;
        MotivoFalha = motivo;
        AtualizadoEm = agora;
    }

    /// <summary>Troca de plano (ADR-0017), respeitando o limite de usuários do novo plano (RP-05).</summary>
    public Result AlterarPlano(string planoCodigo, int usuariosAtuais, DateTimeOffset agora)
    {
        if (Status != StatusTenant.Ativo)
        {
            return Result.Failure(TenantErros.TenantNaoAtivo);
        }

        var novo = CatalogoDePlanos.Obter(planoCodigo);
        if (novo is null)
        {
            return Result.Failure(TenantErros.PlanoInexistente);
        }

        if (novo.Codigo == PlanoCodigo)
        {
            return Result.Failure(TenantErros.PlanoJaAtivo);
        }

        if (usuariosAtuais > novo.LimiteUsuarios)
        {
            return Result.Failure(TenantErros.UsuariosAcimaDoLimite(novo));
        }

        PlanoCodigo = novo.Codigo;
        AtualizadoEm = agora;
        return Result.Success();
    }

    private void GarantirPendente()
    {
        if (Status != StatusTenant.Pendente)
        {
            throw new InvalidOperationException($"Transição inválida: o tenant está {Status}.");
        }
    }
}

public sealed record TenantAtivado(Guid TenantId, string PlanoCodigo, DateTimeOffset OcorridoEm) : IDomainEvent;
