using FluxoCaixa.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixa.Infrastructure.Common.Web;

/// <summary>
/// Base dos controllers (ADR-0016): converte <see cref="Error"/> em ProblemDetails (RFC 9457)
/// com o status HTTP correspondente e o código estável do erro em <c>codigo</c>.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Problema(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        ProblemDetails problema = error is ValidationError validacao
            ? new ValidationProblemDetails(validacao.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problema.Status = StatusDe(error.Type);
        problema.Title = error.Message;
        problema.Type = $"https://httpstatuses.io/{problema.Status}";
        problema.Instance = HttpContext?.Request.Path;
        problema.Extensions["codigo"] = error.Code;
        if (HttpContext?.TraceIdentifier is { } traceId)
        {
            problema.Extensions["traceId"] = traceId;
        }

        return new ObjectResult(problema)
        {
            StatusCode = problema.Status,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static int StatusDe(ErrorType tipo) => tipo switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}
