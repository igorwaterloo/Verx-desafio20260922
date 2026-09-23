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

## Atualizações

- **2026-09-22 — Implementação (Fase 5):**
  - O Worker é um **host web mínimo** (sem controllers), apenas para expor `/health/live` e `/health/ready` ao orquestrador.
  - O Worker é o **escritor** da projeção e por isso é quem aplica as migrations do `ConsolidadoDb`; as réplicas da Api só sobem depois de o Worker estar saudável.
  - A infraestrutura é composta em blocos (`AddConsolidadoPersistencia`, `AddConsolidadoCache`, `AddConsolidadoMensageria`): a Api não registra MassTransit; o Worker não registra controllers.
  - Medido localmente: do POST no Lançamentos ao saldo visível no Consolidado, **250–350 ms** em regime (SLO-07 < 5 s). Com o Consolidado inteiro parado, o Lançamentos seguiu respondendo 201 e o saldo convergiu ao religar.
- **2026-09-23 — Testes de carga (Fase 8): consumo particionado por linha de saldo.**
  - **Problema encontrado:**
    - Com 50 lançamentos/s de um mesmo comerciante (caso real: todos caem no saldo de hoje), vários consumidores concorrentes atualizavam a **mesma linha** de `SaldoDiario`.
    - A concorrência otimista (`rowversion`) gerava centenas de conflitos, cada um levado ao retry exponencial (a partir de 1 s).
    - O saldo levou **14 s** para convergir após a carga, acima do SLO-07 (< 5 s), e havia risco de mensagens esgotarem as tentativas e irem para a DLQ.
  - **Decisão:** o consumidor usa o **particionador do MassTransit** com a chave `tenant + data de competência` (`LancamentoRegistradoConsumerDefinition`, 16 partições por instância):
    - eventos da mesma linha são aplicados em série, sem conflitos;
    - linhas diferentes (outros tenants ou dias) seguem em paralelo.
  - **Alternativas descartadas:**
    - Consumo serial (`ConcurrentMessageLimit = 1`): limitaria a vazão de todos os tenants à de uma linha.
    - `UPDATE ... SET Total = Total + @valor` atômico: removeria o conflito, mas tiraria a regra de aplicação do agregado de domínio.
  - **Limite conhecido:** o particionador vale **dentro de uma instância** do Worker. Com várias instâncias, a mesma linha pode ser disputada entre processos (o retry continua garantindo a correção). Para escalar horizontalmente sem conflitos: *consistent hash exchange* no RabbitMQ (uma fila por partição) ou Azure Service Bus com sessões.
  - **Evidências:**
    - teste de integração `RajadaNoMesmoDia_EhAplicadaInteiraSemRetentativas` (300 eventos na mesma linha, **zero retentativas** medidas pela métrica `consolidado.retentativas`; sem o particionador, o teste falha);
    - resultados do k6 em [testes.md](../testes.md#resultados-dos-testes-de-carga-fase-8).

## Referências
- Microsoft — *CQRS pattern*; *Competing Consumers pattern*
- [C4 — Componentes do Consolidado](../architecture/c4-3-componentes.md)
