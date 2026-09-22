using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Infrastructure.Common.Persistence;

public static partial class MigracoesExtensions
{
    /// <summary>
    /// Aplica as migrations pendentes na inicialização (ambiente local/containers), aguardando o
    /// banco ficar disponível. Em produção as migrations rodam no pipeline de deploy.
    /// </summary>
    public static async Task AplicarMigracoesAsync<TContext>(this IHost host, int tentativas = 20, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigracoesExtensions));

        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await contexto.Database.MigrateAsync(cancellationToken);
                LogMigracoesAplicadas(logger, typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (tentativa < tentativas && ex is not OperationCanceledException)
            {
                LogBancoIndisponivel(logger, typeof(TContext).Name, tentativa, tentativas, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations de {Contexto} aplicadas.")]
    private static partial void LogMigracoesAplicadas(ILogger logger, string contexto);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Banco de {Contexto} indisponível (tentativa {Tentativa}/{Total}): {Erro}")]
    private static partial void LogBancoIndisponivel(ILogger logger, string contexto, int tentativa, int total, string erro);
}
