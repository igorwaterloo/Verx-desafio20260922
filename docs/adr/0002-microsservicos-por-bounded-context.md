# ADR-0002: Microsserviços por bounded context

- **Status:** Aceita — complementada por [ADR-0017](0017-contexto-plataforma-onboarding-planos.md)
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0004](0004-comunicacao-assincrona-rabbitmq.md), [ADR-0007](0007-sql-server-ef-core-database-per-service.md), [ADR-0010](0010-separacao-consolidado-api-worker.md)

## Contexto e problema

O domínio tem dois contextos com características opostas (ver [modelo de domínio](../dominio.md)):

| | Lançamentos | Consolidado |
|---|---|---|
| Natureza | Escrita, fonte da verdade | Leitura, projeção derivada |
| Carga | Moderada, transacional | Picos de 50 req/s de leitura |
| Criticidade | **Não pode cair** (RNF-01) | Tolera indisponibilidade curta e consistência eventual |

O requisito **RNF-01** exige que uma falha no Consolidado **não afete** Lançamentos. Precisamos escolher o estilo arquitetural que garanta esse isolamento com o menor custo de complexidade.

## Requisitos da decisão

- **RNF-01:** isolamento de falhas entre os contextos.
- **RNF-02:** escalar o Consolidado de forma independente para 50 req/s.
- Simplicidade de desenvolvimento e operação (equipe pequena, prazo limitado).
- Evolução independente (deploy, versão, tecnologia).

## Opções consideradas

1. **Monólito** (um processo, um banco)
2. **Monólito modular** (um processo, módulos isolados, comunicação por eventos in-process)
3. **Microsserviços por bounded context** (processos e bancos separados, eventos via broker)
4. **Serverless** (Azure Functions por caso de uso)

## Decisão

**Opção escolhida:** **microsserviços alinhados aos bounded contexts** Lançamentos e Consolidado, comunicando-se **apenas por eventos assíncronos**. A granularidade é **grossa**: um serviço por contexto, e não um por entidade ou caso de uso.

**Justificativa:** o RNF-01 é um requisito de **isolamento de falhas em tempo de execução**. Em um monólito ou monólito modular, os dois contextos compartilham processo, pool de threads, memória e ciclo de deploy. Um pico de consultas no consolidado (RNF-02), um vazamento de memória ou um deploy com defeito derrubaria também o registro de lançamentos. Só a **separação de processos**, com **comunicação assíncrona**, entrega o isolamento exigido.

## Consequências

### Positivas
- Isolamento real de falhas (RNF-01): Lançamentos depende apenas do próprio banco.
- Escala independente: o Consolidado ganha réplicas sem escalar Lançamentos (RNF-02).
- Deploys independentes, com menor raio de impacto de cada mudança.
- Fronteiras explícitas impedem o acoplamento acidental entre contextos.

### Negativas / trade-offs aceitos
- **Consistência eventual** entre lançamentos e saldo.
- **Complexidade operacional:** mais processos, broker, observabilidade distribuída.
- Não há transações entre serviços: é preciso tratar entrega de mensagens, duplicidade e ordem.
- Testes de ponta a ponta mais elaborados.

### Mitigações
- Transactional Outbox + consumidor idempotente ([ADR-0005](0005-outbox-e-consumidor-idempotente.md)).
- OpenTelemetry com trace distribuído atravessando o broker ([ADR-0011](0011-observabilidade-opentelemetry.md)).
- Docker Compose sobe tudo com um comando; Testcontainers nos testes de integração.
- Meta explícita de atraso de consolidação (SLO-07 < 5 s).

## Comparativo das opções

| Critério | Monólito | Monólito modular | **Microsserviços** | Serverless |
|---|---|---|---|---|
| Isolamento de falhas (RNF-01) | ❌ | ⚠️ lógico, não físico | ✅ | ✅ |
| Escala independente (RNF-02) | ❌ | ❌ | ✅ | ✅ |
| Simplicidade | ✅ | ✅ | ⚠️ | ⚠️ |
| Custo operacional | Baixo | Baixo | Médio | Baixo/variável |
| Latência previsível (cold start) | ✅ | ✅ | ✅ | ⚠️ |
| Execução local simples | ✅ | ✅ | ✅ (Compose) | ⚠️ emuladores |
| Aprisionamento a fornecedor | Nenhum | Nenhum | Nenhum | Alto |

> **Nota:** para um produto em estágio inicial sem o RNF-01, o **monólito modular** seria a recomendação, com extração de serviços quando necessário. O requisito explícito de isolamento é o que justifica pagar o custo dos microsserviços desde o início.

## Atualizações

- **2026-09-22 — SaaS:** com a decisão de oferecer o produto como SaaS multi-tenant, surge um terceiro contexto, **Plataforma** (`Tenants.Api`: onboarding, planos, quotas), detalhado na [ADR-0017](0017-contexto-plataforma-onboarding-planos.md). O princípio desta ADR se mantém: **nenhuma dependência síncrona entre contextos**. A quota chega a Lançamentos por evento.

## Referências
- Eric Evans — *Domain-Driven Design* (bounded contexts)
- Sam Newman — *Monolith to Microservices*
- [C4 — Containers](../architecture/c4-2-containers.md)
