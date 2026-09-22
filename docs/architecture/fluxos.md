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
9. [Onboarding de tenant (saga com compensação)](#9-onboarding-de-tenant-saga-com-compensação)
10. [Quota do plano excedida](#10-quota-do-plano-excedida)
11. [Isolamento entre tenants](#11-isolamento-entre-tenants)
12. [Rate limit por tenant (noisy neighbor)](#12-rate-limit-por-tenant-noisy-neighbor)
13. [Troca de plano (propagação por evento)](#13-troca-de-plano-propagação-por-evento)

---

## 1. Registrar lançamento

Lançamento e evento são gravados **na mesma transação** (Transactional Outbox). A resposta ao cliente **não depende** do broker, do Consolidado nem da Plataforma: a quota é verificada na projeção local do plano.

```mermaid
sequenceDiagram
    autonumber
    actor C as Operador
    participant W as Web App
    participant G as API Gateway
    participant L as Lancamentos.Api
    participant DB as LancamentosDb
    participant O as Outbox (background)
    participant MQ as RabbitMQ

    C->>W: Preenche crédito/débito
    W->>G: POST /api/v1/lancamentos<br/>Bearer JWT + Idempotency-Key
    G->>G: Valida JWT, rate limit por tenant (plano)
    G->>L: Encaminha
    L->>L: Valida JWT, resolve TenantId (claim tenant_id)
    L->>DB: Idempotency-Key já processada (tenant + chave)?
    alt chave já usada
        DB-->>L: resposta anterior
        L-->>G: 201 Created (mesma resposta, sem duplicar)
    else chave nova
        L->>DB: Conta lançamentos do mês × TenantPlano (RN-09)
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

**Validação inválida:** o Lancamentos.Api responde `400` com `ValidationProblemDetails` (RFC 9457) e não grava nada. **Quota excedida:** ver o [fluxo 10](#10-quota-do-plano-excedida).

---

## 2. Consolidação assíncrona

```mermaid
sequenceDiagram
    autonumber
    participant MQ as RabbitMQ
    participant WK as Consolidado.Worker
    participant CDB as ConsolidadoDb
    participant R as Redis

    MQ->>WK: LancamentoRegistrado (EventId, TenantId, Tipo, Valor, Data)
    WK->>WK: TenantContext = evento.TenantId
    WK->>CDB: BEGIN TRAN
    WK->>CDB: EventId existe na inbox?
    alt já processado (entrega duplicada)
        WK->>CDB: ROLLBACK
        WK-->>MQ: ACK (descarta sem efeito)
    else novo evento
        WK->>CDB: SELECT SaldoDiario (TenantId, Data)
        WK->>WK: saldo.Aplicar(Tipo, Valor)
        WK->>CDB: UPSERT SaldoDiario (rowversion)<br/>INSERT Inbox(EventId)
        WK->>CDB: COMMIT
        WK->>R: DEL consolidado:{tenantId}:{data}
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
    actor C as Operador
    participant G as API Gateway
    participant A as Consolidado.Api (réplica 1 ou 2)
    participant R as Redis
    participant CDB as ConsolidadoDb

    C->>G: GET /api/v1/consolidado/2026-09-22
    G->>A: Encaminha (round-robin entre réplicas saudáveis)
    A->>R: GET consolidado:{tenantId}:2026-09-22
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
    actor C as Admin
    participant L as Lancamentos.Api
    participant DB as LancamentosDb

    C->>L: POST /api/v1/lancamentos/{id}/estorno
    L->>L: Política Admin (RN-10)
    L->>DB: Carrega Lancamento {id} (filtro global do tenant)
    alt papel operador
        L-->>C: 403 Forbidden
    else não encontrado (ou de outro tenant)
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
    actor C as Operador
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
    actor C as Operador
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

---

## 9. Onboarding de tenant (saga com compensação)

Cadastro em autoatendimento ([ADR-0017](../adr/0017-contexto-plataforma-onboarding-planos.md)). O tenant nunca fica "meio criado".

```mermaid
sequenceDiagram
    autonumber
    actor V as Visitante (futuro admin)
    participant G as API Gateway
    participant T as Tenants.Api
    participant TDB as TenantsDb
    participant KC as Keycloak (Admin API)
    participant MQ as RabbitMQ

    V->>G: POST /api/v1/tenants (empresa, CNPJ, plano, admin)
    G->>G: Rate limit por IP (rota pública)
    G->>T: Encaminha
    T->>T: Valida CNPJ e plano (RP-01)
    T->>TDB: INSERT Tenant (status Pendente)
    T->>KC: Cria Organization (atributos tenant_id, plano)
    T->>KC: Cria usuário admin + vínculo + role admin
    alt sucesso
        T->>TDB: BEGIN TRAN<br/>UPDATE Tenant = Ativo<br/>INSERT OutboxMessage(TenantProvisionado)<br/>COMMIT
        T-->>V: 201 Created (tenant Ativo)
        T->>MQ: Publica TenantProvisionado (via outbox)
    else falha transitória (Keycloak fora, timeout após retries)
        T->>TDB: Tenant continua Pendente (tentativa registrada)
        T-->>V: 503 Service Unavailable (identidade_indisponivel)
        V->>T: Reenvia o mesmo POST (mesmo CNPJ e e-mail)
        T->>KC: Retoma: organização e admin são buscados pelo tenant_id antes de criar (idempotente)
        T-->>V: 201 Created (tenant Ativo)
    else falha definitiva (ex.: e-mail já existe no Keycloak)
        T->>KC: Compensa: remove a Organization criada
        T->>TDB: UPDATE Tenant = Falhou (motivo)
        T-->>V: 409 Conflict (ProblemDetails)
    end
    Note over T: A senha do admin é repassada ao Keycloak<br/>e nunca é persistida nem logada (RP-03).<br/>Pendentes abandonados há mais de 24 h são<br/>compensados por um job e marcados Falhou.
```

---

## 10. Quota do plano excedida

```mermaid
sequenceDiagram
    autonumber
    actor C as Operador
    participant L as Lancamentos.Api
    participant DB as LancamentosDb

    C->>L: POST /api/v1/lancamentos
    L->>DB: SELECT LimiteLancamentosMes FROM TenantPlano (projeção local)
    L->>DB: SELECT COUNT(*) do mês (índice TenantId, CriadoEm, sem estornos)
    alt uso abaixo do limite
        L->>DB: Registra normalmente (fluxo 1)
        L-->>C: 201 Created
    else uso atingiu o limite (RN-09)
        L-->>C: 422 Unprocessable Entity<br/>ProblemDetails type quota-excedida<br/>(limite, uso, plano)
    end
    Note over L: Nenhuma chamada ao Tenants.Api<br/>(RNF-01 preservado)
```

---

## 11. Isolamento entre tenants

```mermaid
sequenceDiagram
    autonumber
    actor A as Usuário do Tenant A
    actor B as Usuário do Tenant B
    participant L as Lancamentos.Api
    participant DB as LancamentosDb

    A->>L: POST /lancamentos (JWT tenant_id = A)
    L->>DB: INSERT (TenantId = A, preenchido pelo interceptor)
    L-->>A: 201 Created (id = X)

    B->>L: GET /lancamentos/X (JWT tenant_id = B)
    L->>DB: SELECT ... WHERE Id = X AND TenantId = B (filtro global)
    DB-->>L: nenhum registro
    L-->>B: 404 Not Found (não revela a existência)

    B->>L: GET /lancamentos?data=hoje
    L-->>B: 200 OK, somente lançamentos do Tenant B
```

Os mesmos cenários são **testes de integração obrigatórios** em cada serviço (SLO-09).

---

## 12. Rate limit por tenant (noisy neighbor)

```mermaid
sequenceDiagram
    autonumber
    actor A as Tenant A (Free, 20 req/s)
    actor B as Tenant B (Pro, 100 req/s)
    participant G as API Gateway
    participant S as Serviços

    loop A dispara 60 req/s
        A->>G: GET /consolidado/...
        alt dentro do bucket do Tenant A
            G->>S: Encaminha
            S-->>A: 200 OK
        else bucket do Tenant A esgotado
            G-->>A: 429 Too Many Requests + Retry-After
        end
    end
    B->>G: GET /consolidado/... (50 req/s)
    G->>S: Encaminha (bucket próprio do Tenant B)
    S-->>B: 200 OK, sem impacto do Tenant A
```

---

## 13. Troca de plano (propagação por evento)

```mermaid
sequenceDiagram
    autonumber
    actor Ad as Admin
    participant T as Tenants.Api
    participant KC as Keycloak
    participant MQ as RabbitMQ
    participant L as Lancamentos.Api
    participant G as API Gateway

    Ad->>T: PUT /api/v1/tenants/atual/plano (pro)
    T->>T: Valida RP-05 (usuários atuais dentro do limite do novo plano)
    T->>KC: Atualiza atributo plano da Organization
    T->>T: COMMIT Tenant + OutboxMessage(PlanoDoTenantAlterado)
    T-->>Ad: 200 OK
    T->>MQ: Publica PlanoDoTenantAlterado
    MQ->>L: Entrega evento
    L->>L: Atualiza TenantPlano (ignora se ocorridoEm for mais antigo)
    Note over L: Nova quota vale em segundos
    Note over G: Novo rate limit vale no próximo refresh<br/>do token (claim plano, até 5 min)
```
