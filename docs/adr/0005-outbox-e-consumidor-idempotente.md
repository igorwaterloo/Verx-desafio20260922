# ADR-0005: Transactional Outbox e consumidor idempotente

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0004](0004-comunicacao-assincrona-rabbitmq.md), [ADR-0007](0007-sql-server-ef-core-database-per-service.md)

## Contexto e problema

Ao registrar um lançamento, duas coisas precisam acontecer: **gravar no banco** e **publicar o evento** no broker. São dois recursos diferentes, sem transação comum:

- **Grava e depois publica:** se o processo cair entre os dois passos, ou o broker estiver fora, o evento **se perde** e o saldo fica errado para sempre.
- **Publica e depois grava:** se a gravação falhar, o Consolidado soma um lançamento **que não existe**.

Do lado do consumidor, brokers garantem **at-least-once**: a mesma mensagem pode chegar mais de uma vez (redelivery após falha de ack, retry). Somar duas vezes corrompe o saldo.

## Requisitos da decisão

- **SLO-08:** integridade do saldo em 100%, sem perda nem duplicidade.
- **RPO ≈ 0** para lançamentos.
- **RNF-01:** o registro não pode falhar se o broker estiver fora.

## Opções consideradas

**Produtor:**
1. Publicação direta após o commit (best effort)
2. Transação distribuída (2PC / MSDTC)
3. **Transactional Outbox**
4. Change Data Capture (Debezium / CDC do SQL Server)

**Consumidor:**
1. **Consumidor idempotente com inbox** persistida
2. Deduplicação apenas em memória/cache
3. Confiar na entrega exactly-once do broker

## Decisão

**Produtor: Transactional Outbox.**
- O command handler grava o `Lancamento` **e** uma linha `OutboxMessage` com o evento serializado **na mesma transação** do SQL Server.
- Um processo em background (**MassTransit Entity Framework Outbox**) lê as mensagens pendentes, publica com publisher confirms e as marca como entregues.
- Garantia: **todo lançamento commitado gera o seu evento** (at-least-once), mesmo com o broker fora durante horas.

**Consumidor: Idempotent Receiver com inbox.**
- O Worker grava o `EventId` na tabela `MensagensProcessadas` (inbox) **na mesma transação** do upsert do `SaldoDiario`.
- A PK em `EventId` impede uma segunda aplicação. O **efeito é exactly-once**.
- A aplicação é **comutativa** (somas), então a ordem de chegada não importa e não é preciso sequenciamento.

**Por que não 2PC:** o RabbitMQ não participa de transações distribuídas XA/MSDTC de forma prática. Além disso, 2PC reduz a disponibilidade (todos os participantes precisam estar no ar), o que contraria o RNF-01.

**Por que não CDC:** robusto e sem alterar o código de escrita, mas exige infraestrutura adicional pesada (Kafka Connect / Debezium) e acopla o contrato do evento ao **schema da tabela**. O outbox mantém o contrato explícito em `FluxoCaixa.Contracts`.

**Por que não deduplicar em memória/cache:** o estado se perde em restart ou failover, e não é atômico com a escrita do saldo.

## Consequências

### Positivas
- Nenhum evento perdido e nenhum evento fantasma.
- O registro de lançamentos funciona com o broker fora (RNF-01 reforçado).
- Duplicatas são inofensivas, então retries agressivos são seguros.

### Negativas / trade-offs aceitos
- Latência extra de publicação (o intervalo de polling do outbox) entra no SLO-07.
- Tabelas extras (outbox, inbox) que precisam de limpeza periódica.
- Escrita adicional por transação.

### Mitigações
- Intervalo de polling curto (≈ 1 s) e publicação em lote.
- Limpeza periódica (retenção de 7 dias para mensagens entregues/processadas).
- Métrica de mensagens pendentes no outbox, com alerta.

## Comparativo das opções (produtor)

| Critério | Publicação direta | 2PC | **Outbox** | CDC |
|---|---|---|---|---|
| Sem perda de evento | ❌ | ✅ | ✅ | ✅ |
| Sem evento fantasma | ⚠️ | ✅ | ✅ | ✅ |
| Funciona com broker fora | ❌ | ❌ | ✅ | ✅ |
| Infra adicional | Nenhuma | Coordenador | Nenhuma (tabela) | Alta |
| Contrato explícito | ✅ | ✅ | ✅ | ❌ (schema) |

## Referências
- Chris Richardson — *Microservices Patterns* (Transactional Outbox, Idempotent Consumer)
- [Fluxos 1, 2 e 6](../architecture/fluxos.md)
