using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Tenants.Provisionar;

/// <param name="AdminSenha">Repassada ao Keycloak; nunca persistida nem logada (RP-03).</param>
public sealed record ProvisionarTenantCommand(
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string PlanoCodigo,
    string AdminNome,
    string AdminEmail,
    string AdminSenha) : ICommand<TenantDto>
{
    public override string ToString() => $"ProvisionarTenantCommand {{ Cnpj = {Cnpj}, PlanoCodigo = {PlanoCodigo}, AdminEmail = {AdminEmail} }}";
}

public sealed class ProvisionarTenantValidator : AbstractValidator<ProvisionarTenantCommand>
{
    public ProvisionarTenantValidator()
    {
        RuleFor(c => c.RazaoSocial).NotEmpty().WithMessage("A razão social é obrigatória.").MaximumLength(150);
        RuleFor(c => c.NomeFantasia).MaximumLength(150);
        RuleFor(c => c.Cnpj).NotEmpty().WithMessage("O CNPJ é obrigatório.");
        RuleFor(c => c.PlanoCodigo).NotEmpty().WithMessage("O plano é obrigatório.");
        RuleFor(c => c.AdminNome).Must(n => n?.Trim().Length is >= 3 and <= 100)
            .WithMessage("O nome do administrador deve ter entre 3 e 100 caracteres.");
        RuleFor(c => c.AdminEmail).EmailAddress().WithMessage("O e-mail do administrador é inválido.").NotEmpty();
        RuleFor(c => c.AdminSenha).SenhaForte();
    }
}

/// <summary>
/// Onboarding em autoatendimento como saga orquestrada (ADR-0017, RP-02):
/// 1) grava o tenant Pendente; 2) garante a Organization e cria o admin no Keycloak;
/// 3) ativa e publica TenantProvisionado pelo outbox. Com o provedor fora, responde 503 e o tenant
/// fica Pendente: reenviar a mesma requisição retoma de onde parou (operações idempotentes).
/// Falha definitiva (e-mail de outro tenant) compensa removendo a organização e marca Falhou.
/// </summary>
public sealed class ProvisionarTenantHandler(
    ITenantRepository tenants,
    IProvedorDeIdentidade identidade,
    IIntegrationEventPublisher publicador,
    IUnitOfWork unitOfWork,
    TimeProvider tempo) : ICommandHandler<ProvisionarTenantCommand, TenantDto>
{
    public async Task<Result<TenantDto>> HandleAsync(ProvisionarTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var agora = tempo.GetUtcNow();

        var cnpj = Cnpj.Criar(command.Cnpj);
        if (cnpj.IsFailure)
        {
            return cnpj.Error;
        }

        var tenant = await tenants.ObterPorCnpjAsync(cnpj.Value.Numero, cancellationToken);
        switch (tenant?.Status)
        {
            case StatusTenant.Ativo:
                return TenantErros.CnpjJaCadastrado;
            case StatusTenant.Falhou:
                return TenantErros.ProvisionamentoFalhou;
            case StatusTenant.Pendente when !string.Equals(tenant.AdminEmail, command.AdminEmail.Trim(), StringComparison.OrdinalIgnoreCase):
                return TenantErros.CnpjJaCadastrado;
            case null:
                var criacao = Tenant.Criar(command.RazaoSocial, command.NomeFantasia, command.Cnpj, command.PlanoCodigo, command.AdminEmail, agora);
                if (criacao.IsFailure)
                {
                    return criacao.Error;
                }

                tenant = criacao.Value;
                tenants.Adicionar(tenant);
                await unitOfWork.SaveChangesAsync(cancellationToken); // passo 1: Pendente persistido antes de tocar a identidade
                break;
        }

        tenant.RegistrarTentativa(agora);

        try
        {
            var organizationId = await identidade.GarantirOrganizacaoAsync(
                tenant.Id, tenant.NomeFantasia ?? tenant.RazaoSocial, tenant.PlanoCodigo, cancellationToken);

            var admin = await identidade.CriarUsuarioAsync(
                new NovoUsuarioDoTenant(organizationId, tenant.Id, tenant.PlanoCodigo, command.AdminNome.Trim(), tenant.AdminEmail, command.AdminSenha, TenantRoles.Admin),
                cancellationToken);

            if (admin.IsFailure)
            {
                // Compensação: nada do tenant permanece no provedor de identidade.
                await identidade.RemoverOrganizacaoAsync(tenant.Id, cancellationToken);
                tenant.MarcarFalha(admin.Error.Message, agora);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return admin.Error;
            }

            tenant.Ativar(organizationId, agora);
        }
        catch (ProvedorDeIdentidadeIndisponivelException)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return TenantErros.ProvedorDeIdentidadeIndisponivel;
        }

        await publicador.PublishAsync(EventosDeIntegracao.TenantProvisionado(tenant, agora), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TenantDto.De(tenant);
    }
}

internal static class RegrasDeSenha
{
    /// <summary>Mínimo de 8 caracteres, com letra e número (a política final também é aplicada pelo Keycloak).</summary>
    public static IRuleBuilderOptions<T, string> SenhaForte<T>(this IRuleBuilder<T, string> regra) =>
        regra.Must(s => s is { Length: >= 8 and <= 128 } && s.Any(char.IsLetter) && s.Any(char.IsDigit))
            .WithMessage("A senha deve ter de 8 a 128 caracteres, com letras e números.");
}
