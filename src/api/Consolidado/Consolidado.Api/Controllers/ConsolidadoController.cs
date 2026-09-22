using Asp.Versioning;
using Consolidado.Application.Saldos;
using Consolidado.Application.Saldos.ObterDia;
using Consolidado.Application.Saldos.ObterPeriodo;
using FluxoCaixa.Infrastructure.Common.Web;
using FluxoCaixa.SharedKernel.Cqrs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Consolidado.Api.Controllers;

/// <summary>Saldo diário consolidado do tenant (consistência eventual em relação aos lançamentos — RC-04).</summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/consolidado")]
[Authorize(Policy = Politicas.Operador)]
public sealed class ConsolidadoController(IDispatcher dispatcher) : ApiControllerBase
{
    /// <summary>Saldo de um dia. Dia sem movimento retorna zeros.</summary>
    [HttpGet("{data}")]
    [ProducesResponseType<SaldoDiarioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ObterDia(DateOnly data, CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.QueryAsync(new ObterSaldoDiarioQuery(data), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }

    /// <summary>Consolidado de um período (até 93 dias), com todos os dias e os totais.</summary>
    [HttpGet]
    [ProducesResponseType<ConsolidadoPeriodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ObterPeriodo([FromQuery] DateOnly inicio, [FromQuery] DateOnly fim, CancellationToken cancellationToken)
    {
        var resultado = await dispatcher.QueryAsync(new ObterConsolidadoPeriodoQuery(inicio, fim), cancellationToken);
        return resultado.IsSuccess ? Ok(resultado.Value) : Problema(resultado.Error);
    }
}
