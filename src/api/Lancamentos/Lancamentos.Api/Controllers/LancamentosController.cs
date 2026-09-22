using Asp.Versioning;
using FluxoCaixa.Infrastructure.Common.Web;
using FluxoCaixa.SharedKernel.Cqrs;
using Lancamentos.Application.Lancamentos;
using Lancamentos.Application.Lancamentos.Estornar;
using Lancamentos.Application.Lancamentos.Listar;
using Lancamentos.Application.Lancamentos.Obter;
using Lancamentos.Application.Lancamentos.Registrar;
using Lancamentos.Domain.Lancamentos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lancamentos.Api.Controllers;

/// <summary>Lançamentos do caixa do tenant. O tenant vem do token; nunca do corpo da requisição.</summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/lancamentos")]
[Authorize(Policy = Politicas.Operador)]
public sealed class LancamentosController(IDispatcher dispatcher) : ApiControllerBase
{
    /// <summary>Registra um crédito ou débito. Envie <c>Idempotency-Key</c> para retentativas seguras.</summary>
    [HttpPost]
    [ProducesResponseType<LancamentoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Registrar(
        [FromBody] RegistrarLancamentoRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resultado = await dispatcher.SendAsync(
            new RegistrarLancamentoCommand(request.Tipo, request.Valor, request.DataCompetencia, request.Descricao, idempotencyKey),
            cancellationToken);

        return resultado.IsSuccess
            ? CreatedAtAction(nameof(ObterPorId), new { id = resultado.Value.Id, version = VersaoAtual() }, resultado.Value)
            : Problema(resultado.Error);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<LancamentoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.QueryAsync(new ObterLancamentoPorIdQuery(id), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }

    /// <summary>Lista os lançamentos de uma data de competência (paginado).</summary>
    [HttpGet]
    [ProducesResponseType<Pagina<LancamentoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly data,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        var resultado = await dispatcher.QueryAsync(new ListarLancamentosQuery(data, pagina, tamanhoPagina), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }

    /// <summary>Estorna um lançamento (somente admin). Gera um lançamento com o tipo inverso.</summary>
    [HttpPost("{id:guid}/estorno")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType<LancamentoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Estornar(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.SendAsync(new EstornarLancamentoCommand(id), cancellationToken);

        return resultado.IsSuccess
            ? CreatedAtAction(nameof(ObterPorId), new { id = resultado.Value.Id, version = VersaoAtual() }, resultado.Value)
            : Problema(resultado.Error);
    }

    private string VersaoAtual() => RouteData.Values["version"]?.ToString() ?? "1.0";
}

/// <summary>Corpo do registro de lançamento.</summary>
public sealed record RegistrarLancamentoRequest(TipoLancamento Tipo, decimal Valor, DateOnly DataCompetencia, string Descricao);
