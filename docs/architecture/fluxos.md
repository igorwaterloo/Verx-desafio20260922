# Fluxos de Dados (Diagramas de Sequência)

Comportamento dinâmico da solução nos caminhos principais e nos cenários de falha.

1. [Registrar lançamento](#1-registrar-lançamento)
2. [Consolidação assíncrona](#2-consolidação-assíncrona)
3. [Consulta do consolidado com cache](#3-consulta-do-consolidado-com-cache)
4. [Estorno](#4-estorno)
5. [Falha: Consolidado indisponível](#5-falha-consolidado-indisponível)
6. [Falha: RabbitMQ indisponível](#6-falha-rabbitmq-indisponível)
7. [Falha: mensagem com erro permanente (DLQ)](#7-falha-mensagem-com-erro-permanente-dlq)
8. [Falha: Redis indisponível](#8-falha-redis-indisponível)

---

## 1. Registrar lançamento

Lançamento e evento são gravados **na mesma transação** (Transactional Outbox). A resposta ao cliente **não depende** do broker nem do Consolidado.

```mermaid
sequenceDiagram
    autonumber
    actor C as Comerciante
    participant W as Web App
    participant G as API Gateway
    participant L as Lancamentos.Api
    participant DB as LancamentosDb
    participant O as Outbox (background)
    participant MQ as RabbitMQ

    C->>W: Preenche crédito/débito
    W->>G: POST /api/v1/lancamentos<br/>Bearer JWT + Idempotency-Key
    G->>G: Valida JWT, rate limit
    G->>L: Encaminha
    L->>L: Valida JWT, extrai ComercianteId
    L->>DB: Idempotency-Key já processada?
    alt chave já usada
        DB-->>L: resposta anterior
        L-->>G: 201 Created (mesma resposta, sem duplicar)
    else chave nova
        L->>L: Lancamento.Criar(...) valida RN-01..RN-02
        L->>DB: BEGIN TRAN<br/>INSERT Lancamento<br/>INSERT OutboxMessage(LancamentoRegistrado)<br/>INSERT IdempotencyKey<br/>COMMIT
        L-->>G: 201 Created + Location
    end
    G-->>W: 201 Created
    W-->>C: "Lançamento registrado"

    Note over O,MQ: Assíncrono, desacoplado da requisição
    O->>DB: Lê mensagens pendentes do outbox
    O->>MQ: Publica LancamentoRegistrado (publisher confirms)
    O->>DB: Marca mensagem como entregue
```

**Validação inválida:** o Lancamentos.Api responde `400` com `ValidationProblemDetails` (RFC 9457) e não grava nada.

---

## 2. Consolidação assíncrona

```mermaid
sequenceDiagram
    autonumber
    participant MQ as RabbitMQ
    participant WK as Consolidado.Worker
    participant CDB as ConsolidadoDb
    participant R as Redis

    MQ->>WK: LancamentoRegistrado (EventId, ComercianteId, Tipo, Valor, Data)
    WK->>CDB: BEGIN TRAN
    WK->>CDB: EventId existe na inbox?
    alt já processado (entrega duplicada)
        WK->>CDB: ROLLBACK
        WK-->>MQ: ACK (descarta sem efeito)
    else novo evento
        WK->>CDB: SELECT SaldoDiario (ComercianteId, Data)
        WK->>WK: saldo.Aplicar(Tipo, Valor)
        WK->>CDB: UPSERT SaldoDiario (rowversion)<br/>INSERT Inbox(EventId)
        WK->>CDB: COMMIT
        WK->>R: DEL consolidado:{comercianteId}:{data}
        WK-->>MQ: ACK
    end
```

**Garantias:**
- **At-least-once** na entrega e **exactly-once** no efeito, graças à inbox na mesma transação.
- A ordem dos eventos não importa, porque a soma é comutativa.
- Um conflito de concorrência (`rowversion`) gera exceção, e o retry reaplica a mensagem, que continua idempotente.

---

## 3. Consulta do consolidado com cache

```mermaid
sequenceDiagram
    autonumber
    actor C as Comerciante
    participant G as API Gateway
    participant A as Consolidado.Api (réplica 1 ou 2)
    participant R as Redis
    participant CDB as ConsolidadoDb

    C->>G: GET /api/v1/consolidado/2026-09-22
    G->>A: Encaminha (round-robin entre réplicas saudáveis)
    A->>R: GET consolidado:{comercianteId}:2026-09-22
    alt cache hit
        R-->>A: saldo serializado
    else cache miss
        R-->>A: (nil)
        A->>CDB: SELECT SaldoDiario (AsNoTracking)
        CDB-->>A: saldo (ou nenhum → zeros, RC-03)
        A->>R: SET com TTL (dia atual curto, dias passados longo)
    end
    A-->>G: 200 OK { data, totalCreditos, totalDebitos, saldo }
    G-->>C: 200 OK
```

---

## 4. Estorno

```mermaid
sequenceDiagram
    autonumber
    actor C as Comerciante
    participant L as Lancamentos.Api
    participant DB as LancamentosDb

    C->>L: POST /api/v1/lancamentos/{id}/estorno
    L->>DB: Carrega Lancamento {id} do ComercianteId
    alt não encontrado
        L-->>C: 404 Not Found
    else já estornado (RN-05) ou é um estorno (RN-06)
        L-->>C: 409 Conflict (ProblemDetails)
    else válido
        L->>L: original.Estornar() → novo Lancamento com tipo inverso
        L->>DB: BEGIN TRAN<br/>UPDATE original (Estornado = true)<br/>INSERT estorno<br/>INSERT OutboxMessage(LancamentoRegistrado do estorno)<br/>COMMIT
        L-->>C: 201 Created (estorno)
    end
    Note over L,DB: O Consolidado recebe um LancamentoRegistrado comum<br/>(tipo inverso), sem tratamento especial
```

---

## 5. Falha: Consolidado indisponível

Este é o cenário central do requisito não funcional: **Lançamentos continua disponível.**

```mermaid
sequenceDiagram
    autonumber
    actor C as Comerciante
    participant L as Lancamentos.Api
    participant MQ as RabbitMQ
    participant WK as Consolidado.Worker ❌
    participant A as Consolidado.Api ❌

    Note over WK,A: Consolidado fora do ar (Api e Worker)
    C->>L: POST /lancamentos (n vezes)
    L-->>C: 201 Created ✅ (não há dependência síncrona)
    L->>MQ: Publica eventos (via outbox)
    Note over MQ: Mensagens acumulam na fila durável
    C->>A: GET /consolidado
    A--xC: 503 pelo Gateway (sem réplica saudável)<br/>A SPA mostra "consolidado temporariamente indisponível"

    Note over WK,A: Consolidado volta
    MQ->>WK: Entrega o backlog
    WK->>WK: Aplica os eventos (idempotente)
    Note over A: Saldo converge: consistência eventual
```

---

## 6. Falha: RabbitMQ indisponível

```mermaid
sequenceDiagram
    autonumber
    actor C as Comerciante
    participant L as Lancamentos.Api
    participant DB as LancamentosDb
    participant O as Outbox (background)
    participant MQ as RabbitMQ ❌

    C->>L: POST /lancamentos
    L->>DB: COMMIT (lançamento + outbox)
    L-->>C: 201 Created ✅
    loop até o broker voltar
        O->>MQ: Tenta publicar
        MQ--xO: conexão recusada
        O->>O: Backoff, a mensagem continua pendente no outbox
    end
    Note over MQ: Broker volta
    O->>MQ: Publica pendentes
    O->>DB: Marca entregues
```

Nenhum lançamento se perde (RPO ≈ 0): o banco guarda o evento até a publicação.

---

## 7. Falha: mensagem com erro permanente (DLQ)

```mermaid
sequenceDiagram
    autonumber
    participant MQ as RabbitMQ
    participant WK as Consolidado.Worker
    participant DLQ as Fila _error (DLQ)
    actor Op as Operação

    MQ->>WK: LancamentoRegistrado
    WK--xWK: Exceção
    loop Retry imediato + redelivery com backoff exponencial
        MQ->>WK: Reentrega
        WK--xWK: Exceção persiste
    end
    WK->>DLQ: Move a mensagem com os headers de erro
    Note over DLQ: Alerta: profundidade da DLQ > 0
    Op->>DLQ: Analisa, corrige a causa
    Op->>MQ: Reenfileira (shovel / move messages)
    MQ->>WK: Reprocessa (idempotente)
```

---

## 8. Falha: Redis indisponível

```mermaid
sequenceDiagram
    autonumber
    participant A as Consolidado.Api
    participant R as Redis ❌
    participant CDB as ConsolidadoDb

    A->>R: GET (timeout ≈ 50 ms)
    R--xA: timeout / conexão recusada
    A->>A: Circuit breaker abre (não tenta o Redis por N segundos)
    A->>CDB: SELECT SaldoDiario
    CDB-->>A: saldo
    A-->>A: 200 OK (latência maior, sem erro para o usuário)
```

O cache é **otimização, nunca dependência**: a falha dele degrada a latência, mas não a disponibilidade.
