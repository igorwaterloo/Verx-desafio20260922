# ADR-0010: Separar o Consolidado em Api (leitura) e Worker (escrita)

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0003](0003-clean-architecture-cqrs.md), [ADR-0006](0006-cache-aside-redis.md), [ADR-0009](0009-api-gateway-yarp.md)

## Contexto e problema

O contexto Consolidado tem duas cargas de natureza diferente:
- **Leitura:** consultas HTTP com picos de 50 req/s (RNF-02), sensíveis a latência.
- **Escrita:** consumo de eventos da fila, sensível a throughput, sem SLA de latência por requisição.

Colocar as duas no mesmo processo (API + `BackgroundService` consumindo a fila) faz com que concorram por CPU, threads e conexões. Escalar a leitura também multiplicaria os consumidores, e vice-versa.

## Requisitos da decisão

- **RNF-02 / SLO-05:** latência de leitura estável durante picos e durante a drenagem de backlog.
- Escala independente de leitura e escrita.
- Uma falha no consumo não deve afetar as consultas, e vice-versa.

## Opções consideradas

1. **Um processo:** API com consumer como hosted service
2. **Dois processos:** `Consolidado.Api` (leitura) + `Consolidado.Worker` (escrita), compartilhando Domain/Application/Infrastructure

## Decisão

**Opção escolhida:** **dois processos implantáveis** a partir do **mesmo bounded context**:

| Processo | Papel | Escala |
|---|---|---|
| `Consolidado.Api` | Queries (com cache) | Por CPU/RPS; **2 réplicas** localmente |
| `Consolidado.Worker` | Consumer + command `AplicarLancamentoNoSaldo` | Pelo tamanho da fila (KEDA em produção); concorrência configurável |

Ambos referenciam os mesmos projetos `Consolidado.Domain`, `Consolidado.Application` e `Consolidado.Infrastructure`. É a aplicação **física** do CQRS ([ADR-0003](0003-clean-architecture-cqrs.md)): **o mesmo banco, com processos distintos por responsabilidade**.

## Consequências

### Positivas
- Quando o Consolidado volta após uma queda (fluxo 5), o Worker drena o backlog **sem degradar** as consultas.
- Cada lado escala pela métrica certa (RPS × profundidade da fila).
- Um deploy ou crash de um lado não afeta o outro.
- Ciclos de vida e health checks específicos.

### Negativas / trade-offs aceitos
- Um container a mais para operar e implantar.
- Leitura e escrita ainda compartilham o `ConsolidadoDb` (contenção possível no banco).

### Mitigações
- O cache ([ADR-0006](0006-cache-aside-redis.md)) retira a maior parte da leitura do banco.
- Transações curtas no Worker (upsert de uma linha + inbox).
- Evolução: réplica de leitura do banco para a Api, se necessário.

## Referências
- Microsoft — *CQRS pattern*; *Competing Consumers pattern*
- [C4 — Componentes do Consolidado](../architecture/c4-3-componentes.md)
