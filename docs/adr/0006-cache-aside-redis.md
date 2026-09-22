# ADR-0006: Cache-aside com Redis e fallback para o banco

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0010](0010-separacao-consolidado-api-worker.md), [requisitos não funcionais](../requisitos-nao-funcionais.md)

## Contexto e problema

O Consolidado recebe **50 req/s em pico** (RNF-02), com um padrão de acesso muito favorável ao cache: o comerciante consulta **repetidamente os mesmos dias** (hoje, ontem, a semana), e o dado só muda quando chega um novo lançamento daquele dia. Sem cache, cada requisição vai ao SQL Server, que se torna o gargalo e o ponto de contenção com o Worker.

## Requisitos da decisão

- **SLO-04/05:** erro < 1% e p95 < 200 ms a 50 req/s.
- Cache **compartilhado entre réplicas** da Api (o dado invalidado deve sumir para todas).
- Invalidação disparada pelo Worker quando o saldo muda.
- A **falha do cache não pode derrubar a consulta** (o cache é otimização, não dependência).

## Opções consideradas

1. Sem cache (somente banco com índices)
2. Cache em memória por instância (`IMemoryCache`)
3. **Redis distribuído com cache-aside**
4. Output caching HTTP / CDN
5. Cache em dois níveis (memória L1 + Redis L2, ex.: `HybridCache`)

## Decisão

**Opção escolhida:** **Redis com padrão cache-aside**, fallback para o banco e invalidação ativa pelo Worker.

| Aspecto | Definição |
|---|---|
| Chave | `consolidado:{comercianteId}:{yyyy-MM-dd}` (dia) e `consolidado:{comercianteId}:{inicio}:{fim}` (período) |
| Leitura | GET no Redis; em miss, lê do banco e grava com TTL |
| TTL | Dia corrente: **60 s**; dias passados: **10 min** (mudam raramente, só via estorno) |
| Invalidação | O Worker, **após o commit**, remove a chave do dia afetado. Os períodos expiram por TTL curto (60 s) |
| Falha do Redis | Timeout de ≈ 50 ms + **circuit breaker**; em falha, lê do banco e responde normalmente |
| Serialização | JSON (System.Text.Json source-generated) |

**Por que não só memória local:** com 2+ réplicas, cada uma teria sua cópia, e a invalidação precisaria de broadcast (pub/sub). Os dados ficariam inconsistentes entre réplicas, com o usuário vendo saldos diferentes a cada requisição balanceada.

**Por que não output cache/CDN:** a resposta é **por usuário** (autenticada) e precisa de invalidação por evento, o que um cache HTTP genérico não oferece facilmente.

**Sobre o `HybridCache` (L1 + L2):** ótima evolução para reduzir ainda mais a latência (menos ida à rede). Fica registrado como melhoria futura porque exige coordenar a invalidação do L1 entre réplicas.

## Consequências

### Positivas
- A maioria das leituras é atendida em memória (sub-milissegundo no Redis), longe do SQL Server.
- Reduz a contenção entre leitura (Api) e escrita (Worker) no ConsolidadoDb.
- Com o fallback, o cache nunca é ponto único de falha (fluxo 8).

### Negativas / trade-offs aceitos
- **Dado potencialmente desatualizado** por no máximo o TTL, se a invalidação falhar. É aceitável para um relatório com consistência eventual.
- Mais um componente para operar.
- Risco de **cache stampede** em chaves quentes após a invalidação.

### Mitigações
- TTL curto no dia corrente limita o dado desatualizado.
- Stampede: o volume (50 req/s) torna o risco baixo. Se necessário, entra lock por chave (single-flight) ou `HybridCache`, que já oferece essa proteção.
- Métrica de hit ratio e latência do Redis.

## Comparativo das opções

| Critério | Sem cache | Memória local | **Redis cache-aside** | Output cache | Híbrido L1+L2 |
|---|---|---|---|---|---|
| Latência | ⚠️ | ✅ | ✅ | ✅ | ✅✅ |
| Consistente entre réplicas | ✅ | ❌ | ✅ | ⚠️ | ⚠️ |
| Invalidação por evento | n/a | ⚠️ | ✅ | ❌ | ⚠️ |
| Resiliência a falha do cache | n/a | ✅ | ✅ (fallback) | ✅ | ✅ |
| Complexidade | Baixa | Baixa | Média | Baixa | Alta |

## Referências
- Microsoft — *Cache-Aside pattern* (Azure Architecture Center)
- [Fluxos 3 e 8](../architecture/fluxos.md)
