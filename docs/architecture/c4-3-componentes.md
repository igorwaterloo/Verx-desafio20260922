# C4 — Nível 3: Diagramas de Componentes

> Fonte formal: [`workspace.dsl`](workspace.dsl), visões `C3-Componentes-*`.

Cada serviço segue **Clean Architecture** com **CQRS**. As dependências apontam sempre **para dentro**:

```mermaid
flowchart LR
    api["<b>Api / Worker</b><br/>(controllers, consumers,<br/>composition root)"] --> app["<b>Application</b><br/>(casos de uso, CQRS, portas)"]
    infra["<b>Infrastructure</b><br/>(EF Core, MassTransit, Redis,<br/>Keycloak, TenantContext)"] --> app
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

Essas regras são **verificadas automaticamente** por testes de arquitetura (NetArchTest) em `tests/Architecture.Tests`. Os testes também verificam que **controllers não dependem de Infrastructure/`DbContext`** e que **toda entidade de negócio implementa `ITenantEntity`**. Decisões: [ADR-0003](../adr/0003-clean-architecture-cqrs.md), [ADR-0015](../adr/0015-multi-tenancy-banco-compartilhado.md), [ADR-0016](../adr/0016-web-api-com-controllers.md). Bibliotecas: [ADR-0013](../adr/0013-bibliotecas-e-licencas.md).

## Multi-tenancy transversal (todos os serviços)

```mermaid
flowchart LR
    req["Requisição HTTP<br/>Bearer JWT"] --> mw["<b>TenantResolutionMiddleware</b><br/>lê claim tenant_id"]
    msg["Mensagem AMQP<br/>tenantId no evento"] --> cf["<b>Consume filter</b><br/>lê tenantId da mensagem"]
    mw --> ctx["<b>ITenantContext</b> (scoped)<br/>TenantId, UsuarioId, Roles"]
    cf --> ctx
    ctx --> qf["<b>EF Core</b><br/>HasQueryFilter(TenantId)<br/>+ interceptor de gravação"]
    ctx --> ck["<b>Cache</b><br/>chaves {tenantId}:..."]
    ctx --> tel["<b>Telemetria</b><br/>atributo tenant.id"]

    classDef c fill:#85bbf0,stroke:#5d82a8,color:#000
    class mw,cf,ctx,qf,ck,tel c
```

| Componente | Local | Responsabilidade |
|---|---|---|
| `ITenantContext` | SharedKernel (abstração) / Infrastructure (implementação) | Expõe `TenantId`, `UsuarioId` e papéis da execução corrente. |
| `TenantResolutionMiddleware` | Infrastructure (web) | Preenche o contexto pelo JWT; sem `tenant_id` responde 403 (MT-04). |
| `TenantConsumeFilter` | Infrastructure (mensageria) | Preenche o contexto pelo `tenantId` do evento. |
| `TenantSaveChangesInterceptor` | Infrastructure (EF Core) | Preenche o `TenantId` em inserções e bloqueia gravação de outro tenant. |
| Filtro global | `DbContext` | `HasQueryFilter(e => e.TenantId == tenant.TenantId)` em toda `ITenantEntity`. |

---

## 3.1 Tenants.Api (Plataforma)

```mermaid
flowchart TB
    gateway["<b>API Gateway</b><br/><i>[Container]</i>"]

    subgraph tapi["Tenants.Api [Container]"]
        ctrl["<b>TenantsController / PlanosController</b><br/><i>[Controllers]</i><br/>Onboarding público, tenant atual,<br/>plano e usuários (admin)"]
        handlers["<b>Command/Query Handlers</b><br/><i>[Application]</i><br/>ProvisionarTenant, AlterarPlano,<br/>AdicionarUsuario, ListarPlanos"]
        dom["<b>Domínio</b><br/><i>[Domain]</i><br/>Tenant, Cnpj, Plano,<br/>RP-01..05"]
        kc["<b>Keycloak Admin Client</b><br/><i>[Infrastructure]</i><br/>Organization, usuários,<br/>retry + compensação"]
        job["<b>Job de Provisionamento</b><br/><i>[BackgroundService]</i><br/>Reprocessa Pendentes"]
        repo["<b>Repositório + Outbox</b><br/><i>[Infrastructure / EF Core]</i>"]
    end

    keycloak["<b>Keycloak</b><br/><i>[Externo]</i>"]
    tdb[("<b>TenantsDb</b>")]
    broker{{"<b>RabbitMQ</b>"}}

    gateway -- "HTTP/JSON" --> ctrl
    ctrl --> handlers
    handlers --> dom
    handlers --> kc
    handlers --> repo
    job --> handlers
    kc -- "Admin REST API" --> keycloak
    repo --> tdb
    repo -- "TenantProvisionado<br/>PlanoDoTenantAlterado" --> broker

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef ext fill:#438dd5,stroke:#2e6295,color:#fff
    classDef ex2 fill:#999999,stroke:#6b6b6b,color:#fff
    classDef queue fill:#f08c00,stroke:#a86200,color:#fff
    class ctrl,handlers,dom,kc,job,repo comp
    class gateway,tdb ext
    class keycloak ex2
    class broker queue
```

| Componente | Responsabilidade | Padrões |
|---|---|---|
| Controllers | Onboarding (`[AllowAnonymous]` + rate limit por IP), tenant atual, troca de plano e usuários (`[Authorize(Policy = "Admin")]`). | ASP.NET Core Controllers |
| Handlers | `ProvisionarTenant` orquestra a saga: `Pendente` → Keycloak → `Ativo` + evento, ou compensação. | Saga orquestrada, Result pattern |
| Domínio | Validação de CNPJ, transições de status, limites do plano. | DDD Aggregate, Value Object |
| Keycloak Admin Client | Porta `IIdentityProvisioner` implementada com HttpClient + resiliência; conta de serviço com permissões mínimas. | Adapter, Retry, Compensating Transaction |
| Job de Provisionamento | Reconciliação periódica de tenants `Pendente`. | Scheduler Agent Supervisor |
| Repositório + Outbox | Persistência e publicação confiável dos eventos. | Transactional Outbox |

---

## 3.2 Lancamentos.Api

```mermaid
flowchart TB
    gateway["<b>API Gateway</b><br/><i>[Container]</i>"]
    broker{{"<b>RabbitMQ</b>"}}

    subgraph lapi["Lancamentos.Api [Container]"]
        ctrl["<b>LancamentosController</b><br/><i>[Controllers]</i><br/>Filtros: tenant, Idempotency-Key<br/>ProblemDetails"]
        tctx["<b>Tenant Context</b><br/><i>[Infrastructure]</i><br/>TenantId, usuário, roles"]
        dispatcher["<b>Dispatcher CQRS</b><br/><i>[Application]</i><br/>Decorators: validação,<br/>logging, métricas"]
        cmd["<b>Command Handlers</b><br/><i>[Application]</i><br/>RegistrarLancamento (quota)<br/>EstornarLancamento"]
        qry["<b>Query Handlers</b><br/><i>[Application]</i><br/>ObterLancamentoPorId<br/>ListarLancamentos"]
        dom["<b>Domínio</b><br/><i>[Domain]</i><br/>Lancamento, Dinheiro,<br/>TipoLancamento, RN-01..10"]
        repo["<b>Repositório + UoW</b><br/><i>[Infrastructure / EF Core]</i><br/>Filtro global por TenantId"]
        outbox["<b>Outbox Publisher</b><br/><i>[Infrastructure / MassTransit]</i>"]
        planoc["<b>TenantPlanoConsumer</b><br/><i>[MassTransit]</i><br/>Projeção TenantPlano"]
    end

    ldb[("<b>LancamentosDb</b><br/><i>[SQL Server]</i>")]

    gateway -- "HTTP/JSON" --> ctrl
    ctrl --> tctx
    ctrl --> dispatcher
    dispatcher --> cmd
    dispatcher --> qry
    cmd --> dom
    cmd -- "persiste; conta uso do mês" --> repo
    cmd -- "evento de integração" --> outbox
    qry -- "AsNoTracking + projeção" --> repo
    repo --> tctx
    repo --> ldb
    outbox -- "tabela OutboxMessage" --> ldb
    outbox -- "LancamentoRegistrado" --> broker
    broker -- "TenantProvisionado<br/>PlanoDoTenantAlterado" --> planoc
    planoc --> repo

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef ext fill:#438dd5,stroke:#2e6295,color:#fff
    classDef queue fill:#f08c00,stroke:#a86200,color:#fff
    class ctrl,tctx,dispatcher,cmd,qry,dom,repo,outbox,planoc comp
    class gateway,ldb ext
    class broker queue
```

| Componente | Responsabilidade | Padrões |
|---|---|---|
| LancamentosController | Traduz HTTP para commands/queries; aplica `Idempotency-Key` (escopo tenant); mapeia `Result` para status HTTP/ProblemDetails (RFC 9457); políticas `Operador`/`Admin` (RN-10). | Controllers finos, Adapter |
| Tenant Context | Identidade da execução (MT-02). | Context Object |
| Dispatcher CQRS | Resolve `ICommandHandler<TCommand, TResult>` e `IQueryHandler<TQuery, TResult>` via DI; encadeia os decorators. | Mediator, Decorator |
| Command Handlers | Carregam o agregado, verificam a **quota** (RN-09), executam a regra, persistem e registram o evento. | Unit of Work, Result pattern |
| Query Handlers | Leitura direta, sem tracking, projetada em DTOs. | CQRS (lado de leitura) |
| Domínio | Invariantes RN-01..RN-06; factory methods (`Lancamento.Criar`, `lancamento.Estornar()`); eventos de domínio. | DDD: Aggregate, Value Object, Domain Event |
| Repositório + UoW | Abstrai o EF Core atrás de portas definidas na Application; filtro global por tenant. | Repository, Dependency Inversion |
| Outbox Publisher | Garante **at-least-once** sem transação distribuída. | Transactional Outbox |
| TenantPlanoConsumer | Mantém a projeção do plano (idempotente; ignora eventos mais antigos). | Event-carried State Transfer |

---

## 3.3 Consolidado.Api (leitura)

```mermaid
flowchart TB
    gateway["<b>API Gateway</b><br/><i>[Container]</i>"]

    subgraph capi["Consolidado.Api [Container] ×2"]
        ctrl["<b>ConsolidadoController</b><br/><i>[Controllers]</i><br/>/{data} e ?inicio=&fim="]
        tctx["<b>Tenant Context</b><br/><i>[Infrastructure]</i>"]
        qry["<b>Query Handlers</b><br/><i>[Application]</i><br/>ObterSaldoDiario<br/>ObterConsolidadoPeriodo"]
        cache["<b>Cache Service</b><br/><i>[Infrastructure / Redis]</i><br/>Chaves por tenant, TTL,<br/>fallback se Redis falhar"]
        repo["<b>Repositório de leitura</b><br/><i>[Infrastructure / EF Core]</i><br/>AsNoTracking + filtro por tenant"]
    end

    redis[("<b>Redis</b>")]
    cdb[("<b>ConsolidadoDb</b><br/><i>[SQL Server]</i>")]

    gateway -- "HTTP/JSON" --> ctrl
    ctrl --> tctx
    ctrl --> qry
    qry -- "1. tenta cache" --> cache
    qry -- "2. miss ou falha" --> repo
    cache --> redis
    repo --> cdb

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef ext fill:#438dd5,stroke:#2e6295,color:#fff
    classDef cachec fill:#d6336c,stroke:#962447,color:#fff
    class ctrl,tctx,qry,cache,repo comp
    class gateway,cdb ext
    class redis cachec
```

| Componente | Responsabilidade | Padrões |
|---|---|---|
| ConsolidadoController | Validação de parâmetros (data, período máx. 93 dias) e autorização (`Operador`). | Controllers |
| Query Handlers | Montam a resposta; dias sem movimento retornam saldo zero (RC-03). | CQRS (leitura) |
| Cache Service | Chaves `consolidado:{tenantId}:{yyyy-MM-dd}`; TTL curto para o dia corrente e mais longo para dias passados; timeout agressivo (≈50 ms) com **fallback para o banco**. | Cache-aside, Circuit Breaker |
| Repositório de leitura | Consultas indexadas por `(TenantId, Data)`. | Repository |

---

## 3.4 Consolidado.Worker (escrita da projeção)

```mermaid
flowchart TB
    broker{{"<b>RabbitMQ</b><br/>fila consolidado-lancamento-registrado"}}
    dlq{{"<b>DLQ</b><br/>..._error"}}

    subgraph worker["Consolidado.Worker [Container]"]
        consumer["<b>LancamentoRegistradoConsumer</b><br/><i>[MassTransit]</i><br/>Retry exponencial,<br/>TenantContext da mensagem"]
        handler["<b>AplicarLancamentoNoSaldo</b><br/><i>[Application]</i><br/>Idempotente por EventId"]
        dom["<b>Domínio</b><br/><i>[Domain]</i><br/>SaldoDiario, RC-01..05"]
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
| Consumer | Adapter do broker para o caso de uso; define o tenant a partir do evento; retry com backoff exponencial; mensagens que falham de vez vão para a DLQ. | Competing Consumers, Retry, Dead Letter Channel |
| AplicarLancamentoNoSaldo | Verifica se o `EventId` já está na inbox e ignora o evento se já foi processado. Se não, aplica no `SaldoDiario` e grava inbox + saldo na mesma transação. | Idempotent Receiver |
| Domínio | `SaldoDiario.Aplicar(tipo, valor)`, com operação comutativa. | DDD Aggregate |
| Repositório + Inbox | Upsert com concorrência otimista (`rowversion`); conflito leva a retry. | Inbox, Optimistic Concurrency |
| Invalidador | Remove a chave do tenant/dia afetado após o commit. Se falhar, o TTL limita o tempo de dado desatualizado. | Cache invalidation |

---

## Building blocks compartilhados

| Projeto | Conteúdo | Regra |
|---|---|---|
| `FluxoCaixa.SharedKernel` | `Entity`, `AggregateRoot`, `ValueObject`, `Result<T>`, `Error`, abstrações CQRS (`ICommand`, `IQuery`, handlers, `IDispatcher`), `ITenantContext`, `ITenantEntity`. | Sem dependências externas. |
| `FluxoCaixa.Application.Common` | Implementação do `IDispatcher`, decorators de validação (FluentValidation) e logging, registro dos handlers via DI. | Referenciado pelas camadas Application; sem dependência de infraestrutura (EF, ASP.NET, broker). |
| `FluxoCaixa.Contracts` | Eventos de integração versionados (`LancamentoRegistrado`, `TenantProvisionado`, `PlanoDoTenantAlterado`), todos com `TenantId`. | Só records imutáveis e serializáveis; nenhum comportamento. |
| `FluxoCaixa.Infrastructure.Common` | Implementações transversais: middleware/filtro de tenant, interceptor EF, configuração de OpenTelemetry, autenticação JWT, ProblemDetails. | Referenciado apenas por Infrastructure e Api/Worker. |
