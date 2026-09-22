using Asp.Versioning;
using FluxoCaixa.Infrastructure.Common.Web;
using FluxoCaixa.SharedKernel.Cqrs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tenants.Application.Abstractions;
using Tenants.Application.Tenants;
using Tenants.Application.Tenants.AlterarPlano;
using Tenants.Application.Tenants.Consultas;
using Tenants.Application.Tenants.Provisionar;
using Tenants.Application.Tenants.Usuarios;
using Tenants.Domain.Tenants;

namespace Tenants.Api.Controllers;

/// <summary>Onboarding e gestão do tenant corrente (o tenant vem do token, exceto no cadastro).</summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/tenants")]
public sealed class TenantsController(IDispatcher dispatcher) : ApiControllerBase
{
    /// <summary>
    /// Cadastro da empresa em autoatendimento (público). Com o provedor de identidade indisponível,
    /// responde 503 e o cadastro fica pendente: reenvie a mesma requisição para concluí-lo.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<TenantDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarTenantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resultado = await dispatcher.SendAsync(
            new ProvisionarTenantCommand(request.RazaoSocial, request.NomeFantasia, request.Cnpj, request.Plano,
                request.Administrador.Nome, request.Administrador.Email, request.Administrador.Senha),
            cancellationToken);

        return resultado.IsSuccess
            ? CreatedAtAction(nameof(ObterAtual), new { version = VersaoAtual() }, resultado.Value)
            : Problema(resultado.Error);
    }

    [HttpGet("atual")]
    [Authorize(Policy = Politicas.Operador)]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ObterAtual(CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.QueryAsync(new ObterTenantAtualQuery(), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }

    /// <summary>Troca o plano do tenant (admin). A quota do Lançamentos é atualizada por evento.</summary>
    [HttpPut("atual/plano")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AlterarPlano([FromBody] AlterarPlanoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resultado = await dispatcher.SendAsync(new AlterarPlanoCommand(request.Plano), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }

    [HttpGet("atual/usuarios")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType<IReadOnlyList<UsuarioDoTenant>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarUsuarios(CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.QueryAsync(new ListarUsuariosQuery(), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }

    /// <summary>Cria um usuário na empresa (admin), respeitando o limite de usuários do plano.</summary>
    [HttpPost("atual/usuarios")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType<UsuarioDoTenant>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AdicionarUsuario([FromBody] NovoUsuarioRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resultado = await dispatcher.SendAsync(
            new AdicionarUsuarioCommand(request.Nome, request.Email, request.Senha, request.Papel), cancellationToken);

        return resultado.IsSuccess
            ? CreatedAtAction(nameof(ListarUsuarios), new { version = VersaoAtual() }, resultado.Value)
            : Problema(resultado.Error);
    }

    private string VersaoAtual() => RouteData.Values["version"]?.ToString() ?? "1.0";
}

/// <summary>Catálogo público de planos.</summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/planos")]
[AllowAnonymous]
public sealed class PlanosController(IDispatcher dispatcher) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<Plano>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken) =>
        Ok((await dispatcher.QueryAsync(new ListarPlanosQuery(), cancellationToken)).Value);
}

public sealed record CadastrarTenantRequest(string RazaoSocial, string? NomeFantasia, string Cnpj, string Plano, AdministradorRequest Administrador);

/// <param name="Senha">Repassada ao Keycloak; nunca persistida nem logada (RP-03).</param>
public sealed record AdministradorRequest(string Nome, string Email, string Senha)
{
    public override string ToString() => $"AdministradorRequest {{ Nome = {Nome}, Email = {Email} }}";
}

public sealed record AlterarPlanoRequest(string Plano);

public sealed record NovoUsuarioRequest(string Nome, string Email, string Senha, string Papel)
{
    public override string ToString() => $"NovoUsuarioRequest {{ Nome = {Nome}, Email = {Email}, Papel = {Papel} }}";
}
