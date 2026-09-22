using FluxoCaixa.SharedKernel;

namespace Tenants.Domain.Tenants;

/// <summary>Erros de negócio do contexto Plataforma (códigos estáveis, expostos na API).</summary>
public static class TenantErros
{
    public static readonly Error CnpjInvalido =
        Error.Validation("tenant.cnpj_invalido", "O CNPJ informado é inválido.");

    public static readonly Error CnpjJaCadastrado =
        Error.Conflict("tenant.cnpj_ja_cadastrado", "Já existe uma empresa cadastrada com este CNPJ.");

    public static readonly Error RazaoSocialInvalida =
        Error.Validation("tenant.razao_social_invalida", "A razão social deve ter entre 3 e 150 caracteres.");

    public static readonly Error EmailInvalido =
        Error.Validation("tenant.email_invalido", "O e-mail do administrador é inválido.");

    public static readonly Error PlanoInexistente =
        Error.Validation("tenant.plano_inexistente", "O plano informado não existe.");

    public static readonly Error PlanoJaAtivo =
        Error.Conflict("tenant.plano_ja_ativo", "O tenant já está neste plano.");

    public static readonly Error TenantNaoAtivo =
        Error.Conflict("tenant.nao_ativo", "A operação exige um tenant ativo.");

    public static readonly Error NaoEncontrado =
        Error.NotFound("tenant.nao_encontrado", "Tenant não encontrado.");

    public static readonly Error EmailJaCadastrado =
        Error.Conflict("tenant.email_ja_cadastrado", "Já existe um usuário com este e-mail.");

    public static readonly Error ProvisionamentoFalhou =
        Error.Conflict("tenant.provisionamento_falhou", "O cadastro desta empresa falhou anteriormente; entre em contato com o suporte.");

    public static readonly Error ProvedorDeIdentidadeIndisponivel =
        Error.Unavailable("tenant.identidade_indisponivel", "O provedor de identidade está indisponível. Repita a requisição em instantes para concluir o cadastro.");

    public static readonly Error SomenteAdmin =
        Error.Forbidden("tenant.somente_admin", "Somente o papel admin pode executar esta operação.");

    public static Error UsuariosAcimaDoLimite(Plano plano) =>
        Error.BusinessRule("tenant.limite_usuarios", $"O plano '{plano.Nome}' permite no máximo {plano.LimiteUsuarios} usuários.");
}
