using FluentValidation;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Tenants.Application.Abstractions;
using Tenants.Application.Tenants.Provisionar;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Tenants.Usuarios;

public sealed record AdicionarUsuarioCommand(string Nome, string Email, string Senha, string Papel) : ICommand<UsuarioDoTenant>
{
    public override string ToString() => $"AdicionarUsuarioCommand {{ Email = {Email}, Papel = {Papel} }}";
}

public sealed class AdicionarUsuarioValidator : AbstractValidator<AdicionarUsuarioCommand>
{
    public AdicionarUsuarioValidator()
    {
        RuleFor(c => c.Nome).Must(n => n?.Trim().Length is >= 3 and <= 100).WithMessage("O nome deve ter entre 3 e 100 caracteres.");
        RuleFor(c => c.Email).NotEmpty().EmailAddress().WithMessage("O e-mail é inválido.");
        RuleFor(c => c.Senha).SenhaForte();
        RuleFor(c => c.Papel).Must(p => p is TenantRoles.Admin or TenantRoles.Operador)
            .WithMessage($"O papel deve ser '{TenantRoles.Admin}' ou '{TenantRoles.Operador}'.");
    }
}

/// <summary>Cria um usuário na organização do tenant (admin), respeitando o limite do plano (RP-04).</summary>
public sealed class AdicionarUsuarioHandler(
    ITenantRepository tenants,
    IProvedorDeIdentidade identidade,
    ITenantContext tenantContext) : ICommandHandler<AdicionarUsuarioCommand, UsuarioDoTenant>
{
    public async Task<Result<UsuarioDoTenant>> HandleAsync(AdicionarUsuarioCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!tenantContext.IsInRole(TenantRoles.Admin))
        {
            return TenantErros.SomenteAdmin;
        }

        var tenant = await tenants.ObterPorIdAsync(tenantContext.TenantId, cancellationToken);
        if (tenant?.OrganizationId is null)
        {
            return TenantErros.NaoEncontrado;
        }

        var email = command.Email.Trim().ToLowerInvariant();
        try
        {
            var usuarios = await identidade.ListarUsuariosAsync(tenant.OrganizationId, cancellationToken);
            if (usuarios.Count >= tenant.Plano.LimiteUsuarios)
            {
                return TenantErros.UsuariosAcimaDoLimite(tenant.Plano);
            }

            var criacao = await identidade.CriarUsuarioAsync(
                new NovoUsuarioDoTenant(tenant.OrganizationId, tenant.Id, tenant.PlanoCodigo, command.Nome.Trim(), email, command.Senha, command.Papel),
                cancellationToken);

            return criacao.IsSuccess
                ? new UsuarioDoTenant(criacao.Value, email, command.Nome.Trim(), true)
                : criacao.Error;
        }
        catch (ProvedorDeIdentidadeIndisponivelException)
        {
            return TenantErros.ProvedorDeIdentidadeIndisponivel;
        }
    }
}

public sealed record ListarUsuariosQuery : IQuery<IReadOnlyList<UsuarioDoTenant>>;

public sealed class ListarUsuariosHandler(
    ITenantRepository tenants,
    IProvedorDeIdentidade identidade,
    ITenantContext tenantContext) : IQueryHandler<ListarUsuariosQuery, IReadOnlyList<UsuarioDoTenant>>
{
    public async Task<Result<IReadOnlyList<UsuarioDoTenant>>> HandleAsync(ListarUsuariosQuery query, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsInRole(TenantRoles.Admin))
        {
            return TenantErros.SomenteAdmin;
        }

        var tenant = await tenants.ObterPorIdAsync(tenantContext.TenantId, cancellationToken);
        if (tenant?.OrganizationId is null)
        {
            return TenantErros.NaoEncontrado;
        }

        try
        {
            return Result.Success(await identidade.ListarUsuariosAsync(tenant.OrganizationId, cancellationToken));
        }
        catch (ProvedorDeIdentidadeIndisponivelException)
        {
            return TenantErros.ProvedorDeIdentidadeIndisponivel;
        }
    }
}
