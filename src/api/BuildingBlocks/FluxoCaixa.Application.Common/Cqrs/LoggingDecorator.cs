using System.Diagnostics;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Application.Common.Cqrs;

/// <summary>
/// Registra a execução de cada requisição (nome, duração e resultado) com logs estruturados.
/// O conteúdo da requisição não é logado, para não expor dados sensíveis (SEG-07).
/// </summary>
public sealed partial class LoggingDecorator<TRequest, TResponse>(ILogger<LoggingDecorator<TRequest, TResponse>> logger)
    : IRequestDecorator<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var nome = typeof(TRequest).Name;
        var inicio = Stopwatch.GetTimestamp();

        var resultado = await next();

        var duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
        if (resultado.IsSuccess)
        {
            LogSucesso(logger, nome, duracaoMs);
        }
        else
        {
            LogFalha(logger, nome, resultado.Error.Code, resultado.Error.Type, duracaoMs);
        }

        return resultado;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Requisicao} concluída em {DuracaoMs:0.0} ms")]
    private static partial void LogSucesso(ILogger logger, string requisicao, double duracaoMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Requisicao} falhou com {CodigoErro} ({TipoErro}) em {DuracaoMs:0.0} ms")]
    private static partial void LogFalha(ILogger logger, string requisicao, string codigoErro, ErrorType tipoErro, double duracaoMs);
}
