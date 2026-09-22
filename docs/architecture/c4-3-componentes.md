# C4 — Nível 3: Diagramas de Componentes

> Fonte formal: [`workspace.dsl`](workspace.dsl), visões `C3-Componentes-*`.

Cada serviço segue **Clean Architecture** com **CQRS**. As dependências apontam sempre **para dentro**:

```mermaid
flowchart LR
    api["<b>Api / Worker</b><br/>(apresentação e composição)"] --> app["<b>Application</b><br/>(casos de uso, CQRS, portas)"]
    infra["<b>Infrastructure</b><br/>(EF Core, MassTransit, Redis)"] --> app
    app --> dom["<b>Domain</b><br/>(entidades, value objects, regras)"]
    api --> infra

    classDef l fill:#85bbf0,stroke:#5d82a8,color:#000
    class api,app,infra,dom l
```

| Camada | Pode depender de | Não pode depender de |
|---|---|---|
| **Domain** | `FluxoCaixa.SharedKernel` | Application, Infrastructure, EF Core, ASP.NET |
| **Application** | Domain, SharedKernel, Contracts | Infrastructure, EF Core, MassTransit, Redis |
| **Infrastructure** | Application, Domain | Api |
| **Api / Worker** | Todas (composition root) | — |

Essas regras são **verificadas automaticamente** por testes de arquitetura (NetArchTest) em `tests/Architecture.Tests`.

---

## 3.1 Lancamentos.Api

```mermaid
flowchart TB
    gateway["<b>API Gateway</b><br/><i>[Container]</i>"]

    subgraph lapi["Lancamentos.Api [Container]"]
        endpoints["<b>Endpoints /api/v1/lancamentos</b><br/><i>[Minimal API]</i><br/>HTTP → command/query,<br/>Idempotency-Key, ProblemDetails"]
        dispatcher["<b>Dispatcher CQRS</b><br/><i>[Application]</i><br/>Decorators: validação,<br/>logging, métricas"]
        cmd["<b>Command Handlers</b><br/><i>[Application]</i><br/>RegistrarLancamento<br/>EstornarLancamento"]
        qry["<b>Query Handlers</b><br/><i>[Application]</i><br/>ObterLancamentoPorId<br/>ListarLancamentos"]
        dom["<b>Domínio</b><br/><i>[Domain]</i><br/>Lancamento, Dinheiro,<br/>TipoLancamento, RN-01..08"]
        repo["<b>Repositório + UoW</b><br/><i>[Infrastructure / EF Core]</i>"]
        outbox["<b>Outbox Publisher</b><br/><i>[Infrastructure / MassTransit]</i><br/>Grava na transação,<br/>publica em background"]
    end

    ldb[("<b>LancamentosDb</b><br/><i>[SQL Server]</i>")]
    broker{{"<b>RabbitMQ</b>"}}

    gateway -- "HTTP/JSON" --> endpoints
    endpoints --> dispatcher
    dispatcher --> cmd
    dispatcher --> qry
    cmd --> dom
    cmd --> repo
    cmd -- "evento de integração" --> outbox
    qry -- "AsNoTracking + projeção" --> repo
    repo --> ldb
    outbox -- "tabela OutboxMessage" --> ldb
    outbox -- "AMQP" --> broker

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef ext fill:#438dd5,stroke:#2e6295,color:#fff
    classDef queue fill:#f08c00,stroke:#a86200,color:#fff
    class endpoints,dispatcher,cmd,qry,dom,repo,outbox comp
    class gateway,ldb ext
    class broker queue
```

| Componente | Responsabilidade | Padrões |
|---|---|---|
| Endpoints | Traduz HTTP para commands/queries; extrai `ComercianteId` do JWT; aplica `Idempotency-Key`; mapeia `Result` para status HTTP/ProblemDetails (RFC 9457). | Minimal API, Adapter |
| Dispatcher CQRS | Resolve `ICommandHandler<TCommand, TResult>` e `IQueryHandler<TQuery, TResult>` via DI; encadeia os decorators. | Mediator, Decorator (Chain of Responsibility) |
| Command Handlers | Orquestram o caso de uso: carregam o agregado, executam a regra, persistem e registram o evento. | Unit of Work, Result pattern |
| Query Handlers | Leitura direta, sem tracking, projetada em DTOs. Não passam pelo agregado. | CQRS (lado de leitura) |
| Domínio | Invariantes RN-01..RN-08; factory methods (`Lancamento.Criar`, `lancamento.Estornar()`); eventos de domínio. | DDD: Aggregate, Value Object, Domain Event |
| Repositório + UoW | Abstrai o EF Core atrás de interfaces (portas) definidas na Application. | Repository, Dependency Inversion |
| Outbox Publisher | Garante **at-least-once** sem transação distribuída. | Transactional Outbox |

---

## 3.2 Consolidado.Api (leitura)

```mermaid
flowchart TB
    gateway["<b>API Gateway</b><br/><i>[Container]</i>"]

    subgraph capi["Consolidado.Api [Container] ×2"]
        endpoints["<b>Endpoints /api/v1/consolidado</b><br/><i>[Minimal API]</i><br/>/{data} e ?inicio=&fim="]
        qry["<b>Query Handlers</b><br/><i>[Application]</i><br/>ObterSaldoDiario<br/>ObterConsolidadoPeriodo"]
        cache["<b>Cache Service</b><br/><i>[Infrastructure / Redis]</i><br/>Cache-aside, TTL,<br/>fallback se Redis falhar"]
        repo["<b>Repositório de leitura</b><br/><i>[Infrastructure / EF Core]</i><br/>AsNoTracking"]
    end

    redis[("<b>Redis</b>")]
    cdb[("<b>ConsolidadoDb</b><br/><i>[SQL Server]</i>")]

    gateway -- "HTTP/JSON" --> endpoints
    endpoints --> qry
    qry -- "1. tenta cache" --> cache
    qry -- "2. miss ou falha" --> repo
    cache --> redis
    repo --> cdb

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef ext fill:#438dd5,stroke:#2e6295,color:#fff
    classDef cachec fill:#d6336c,stroke:#962447,color:#fff
    class endpoints,qry,cache,repo comp
    class gateway,cdb ext
    class redis cachec
```

| Componente | Responsabilidade | Padrões |
|---|---|---|
| Endpoints | Validação de parâmetros (data, período máx. 93 dias) e autorização. | Minimal API |
| Query Handlers | Montam a resposta; dias sem movimento retornam saldo zero (RC-03). | CQRS (leitura) |
| Cache Service | Chaves `consolidado:{comercianteId}:{yyyy-MM-dd}`; TTL curto para o dia corrente e mais longo para dias passados; timeout agressivo (≈50 ms) com **fallback para o banco**. | Cache-aside, Circuit Breaker |
| Repositório de leitura | Consultas indexadas por `(ComercianteId, Data)`. | Repository |

---

## 3.3 Consolidado.Worker (escrita da projeção)

```mermaid
flowchart TB
    broker{{"<b>RabbitMQ</b><br/>fila consolidado.lancamento-registrado"}}
    dlq{{"<b>DLQ</b><br/>..._error"}}

    subgraph worker["Consolidado.Worker [Container]"]
        consumer["<b>LancamentoRegistradoConsumer</b><br/><i>[MassTransit]</i><br/>Retry exponencial,<br/>prefetch/concorrência"]
        handler["<b>AplicarLancamentoNoSaldo</b><br/><i>[Application]</i><br/>Idempotente por EventId"]
        dom["<b>Domínio</b><br/><i>[Domain]</i><br/>SaldoDiario, RC-01..04"]
        repo["<b>Repositório + Inbox</b><br/><i>[Infrastructure / EF Core]</i><br/>Upsert + EventId na<br/>mesma transação"]
        inval["<b>Invalidador de cache</b><br/><i>[Infrastructure / Redis]</i>"]
    end

    cdb[("<b>ConsolidadoDb</b>")]
    redis[("<b>Redis</b>")]

    broker -- "AMQP" --> consumer
    consumer -. "esgotou retentativas" .-> dlq
    consumer --> handler
    handler --> dom
    handler --> repo
    handler -- "após commit" --> inval
    repo --> cdb
    inval -- "DEL" --> redis

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef ext fill:#438dd5,stroke:#2e6295,color:#fff
    classDef queue fill:#f08c00,stroke:#a86200,color:#fff
    classDef cachec fill:#d6336c,stroke:#962447,color:#fff
    class consumer,handler,dom,repo,inval comp
    class cdb ext
    class broker,dlq queue
    class redis cachec
```

| Componente | Responsabilidade | Padrões |
|---|---|---|
| Consumer | Adapter do broker para o caso de uso; retry com backoff exponencial para falhas transitórias; mensagens que falham de vez vão para a DLQ. | Competing Consumers, Retry, Dead Letter Channel |
| AplicarLancamentoNoSaldo | Verifica se o `EventId` já está na inbox e ignora o evento se já foi processado. Se não, aplica no `SaldoDiario` e grava inbox + saldo na mesma transação. | Idempotent Receiver |
| Domínio | `SaldoDiario.Aplicar(tipo, valor)`, com operação comutativa. | DDD Aggregate |
| Repositório + Inbox | Upsert com concorrência otimista (`rowversion`); conflito leva a retry. | Inbox, Optimistic Concurrency |
| Invalidador | Remove a chave do dia afetado após o commit. Se falhar, o TTL limita o tempo de dado desatualizado. | Cache invalidation |

---

## Building blocks compartilhados

| Projeto | Conteúdo | Regra |
|---|---|---|
| `FluxoCaixa.SharedKernel` | `Entity`, `AggregateRoot`, `ValueObject`, `Result<T>`, `Error`, abstrações CQRS (`ICommand`, `IQuery`, handlers, `IDispatcher`). | Sem dependências externas. |
| `FluxoCaixa.Contracts` | Eventos de integração versionados (`LancamentoRegistrado`). | Só records imutáveis e serializáveis; nenhum comportamento. |
