# ADR-0004: Comunicação assíncrona entre contextos com RabbitMQ

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0002](0002-microsservicos-por-bounded-context.md), [ADR-0005](0005-outbox-e-consumidor-idempotente.md), [ADR-0013](0013-bibliotecas-e-licencas.md)

## Contexto e problema

O Consolidado precisa saber de cada lançamento registrado. A forma de comunicação define se o RNF-01 é atendido: se Lançamentos **chamar** o Consolidado (ou o contrário, de forma síncrona), a falha de um afeta o outro.

## Requisitos da decisão

- **RNF-01:** Lançamentos não pode depender da disponibilidade do Consolidado.
- **Durabilidade:** nenhum evento pode ser perdido enquanto o consumidor estiver fora.
- Suporte a retry, dead-letter e competing consumers.
- Execução local via Docker e tecnologia madura no ecossistema .NET.

## Opções consideradas

1. **HTTP síncrono** Lançamentos → Consolidado (com retry/circuit breaker)
2. **Consolidado consulta Lançamentos** sob demanda (sem projeção)
3. **RabbitMQ** (message broker, AMQP)
4. **Apache Kafka** (log distribuído)
5. **Azure Service Bus** (broker gerenciado)

## Decisão

**Opção escolhida:** **RabbitMQ** com eventos de integração publicados por Lançamentos e consumidos pelo Consolidado.Worker. O acesso é feito via **MassTransit** ([ADR-0013](0013-bibliotecas-e-licencas.md)).

**Topologia:**

| Elemento | Configuração |
|---|---|
| Exchange | `FluxoCaixa.Contracts:LancamentoRegistrado` (fanout, durável). Novos consumidores podem assinar sem alterar o publicador. |
| Fila | `consolidado-lancamento-registrado` (durável; em produção, **quorum queue** para replicação) |
| DLQ | `consolidado-lancamento-registrado_error` |
| Formato | JSON (envelope MassTransit com `MessageId`, `CorrelationId`, headers de trace) |
| Entrega | Publisher confirms + mensagens persistentes; consumer com ack após o commit |

**Por que não Kafka:** Kafka se destaca em **streaming de alto volume**, retenção longa e replay. O volume aqui (dezenas de eventos/s) está muito abaixo do que exige Kafka, e ele traz custo operacional maior (partições, consumer groups, offsets). RabbitMQ entrega de forma nativa o que precisamos: filas de trabalho, DLQ, retry e roteamento. Se no futuro for necessário **replay** para reconstruir projeções, a reavaliação é registrada em [evolução futura](../evolucao-futura.md).

**Por que não HTTP:** mesmo com retry e circuit breaker, a chamada síncrona acopla a disponibilidade dos dois lados. Com o Consolidado fora, o registro de lançamentos falharia ou precisaria de uma fila própria, o que reinventaria o broker.

**Por que não Azure Service Bus:** excelente em produção, mas não roda localmente de forma completa (existe emulador, com limitações) e amarra a solução a um provedor. Com MassTransit, **trocar o transporte é uma mudança de configuração**, então o Service Bus fica como opção para produção.

## Consequências

### Positivas
- Isolamento temporal: o produtor não precisa que o consumidor esteja no ar (RNF-01).
- A fila funciona como **buffer** em picos (load leveling).
- Retry, DLQ e competing consumers prontos.
- A UI de gerenciamento facilita a operação e a demonstração.

### Negativas / trade-offs aceitos
- Mais um componente de infraestrutura para operar e monitorar.
- Consistência eventual.
- Entrega **at-least-once**, o que obriga a ter consumidores idempotentes.

### Mitigações
- Outbox e inbox ([ADR-0005](0005-outbox-e-consumidor-idempotente.md)).
- Healthcheck do broker; alerta de profundidade de fila e DLQ.
- Em produção: cluster de 3 nós com quorum queues.

## Comparativo das opções

| Critério | HTTP síncrono | Consulta sob demanda | **RabbitMQ** | Kafka | Service Bus |
|---|---|---|---|---|---|
| RNF-01 (isolamento) | ❌ | ❌ | ✅ | ✅ | ✅ |
| Durabilidade | ❌ | n/a | ✅ | ✅ | ✅ |
| DLQ / retry nativos | ❌ | n/a | ✅ | ⚠️ manual | ✅ |
| Complexidade operacional | Baixa | Baixa | Média | Alta | Baixa (gerenciado) |
| Execução local | ✅ | ✅ | ✅ | ✅ | ⚠️ emulador |
| Replay de eventos | ❌ | n/a | ❌ (streams: ⚠️) | ✅ | ❌ |

## Referências
- Gregor Hohpe — *Enterprise Integration Patterns* (Publish-Subscribe Channel, Dead Letter Channel, Competing Consumers)
- [Fluxos — cenários de falha](../architecture/fluxos.md)
