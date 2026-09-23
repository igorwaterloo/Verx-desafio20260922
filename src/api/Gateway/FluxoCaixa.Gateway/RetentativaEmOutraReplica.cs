using Yarp.ReverseProxy.Forwarder;

namespace FluxoCaixa.Gateway;

/// <summary>
/// Retentativa em outra réplica (ADR-0009): se uma leitura (GET/HEAD) falha no transporte — réplica
/// fora do ar, conexão recusada, timeout — e nada foi enviado ao cliente, a requisição é reenviada a
/// outra réplica saudável do cluster. Escritas nunca são repetidas pelo gateway (não são idempotentes
/// do ponto de vista do proxy; a idempotência do POST de lançamentos é responsabilidade do serviço).
/// O health check passivo registra a falha e retira a réplica do balanceamento.
/// </summary>
public static class RetentativaEmOutraReplica
{
    public static async Task ExecutarAsync(HttpContext http, Func<Task> proximo)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(proximo);

        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            await proximo();
            return;
        }

        var proxy = http.GetReverseProxyFeature();
        var candidatas = proxy.AvailableDestinations.ToList();

        while (true)
        {
            await proximo();

            var falha = http.GetForwarderErrorFeature();
            var tentada = proxy.ProxiedDestination;
            if (falha is null || http.Response.HasStarted || tentada is null)
            {
                return;
            }

            candidatas.Remove(tentada);
            if (candidatas.Count == 0)
            {
                return; // nenhuma outra réplica: mantém o 502/504 original
            }

            // Prepara uma nova tentativa limpa na próxima réplica.
            http.Features.Set<IForwarderErrorFeature>(null);
            http.Response.StatusCode = StatusCodes.Status200OK;
            proxy.AvailableDestinations = candidatas.ToArray();
            proxy.ProxiedDestination = null;
        }
    }
}
