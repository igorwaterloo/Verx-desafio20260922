namespace FluxoCaixa.SharedKernel;

/// <summary>
/// Categoria do erro. Define o status HTTP na borda (ProblemDetails) sem acoplar o domínio ao HTTP.
/// </summary>
public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>Regra de negócio violada (ex.: quota do plano excedida) — HTTP 422.</summary>
    BusinessRule,
}

/// <summary>
/// Erro esperado de negócio ou de aplicação. Fluxos esperados retornam <see cref="Error"/> via
/// <see cref="Result"/> em vez de lançar exceções (ADR-0003).
/// </summary>
public record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}

/// <summary>
/// Erro de validação com as mensagens agrupadas por campo (vira ValidationProblemDetails na API).
/// </summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validacao", "Um ou mais campos são inválidos.", ErrorType.Validation);
