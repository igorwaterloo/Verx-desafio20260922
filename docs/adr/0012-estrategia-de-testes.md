# ADR-0012: Estratégia de testes (TDD, pirâmide, Testcontainers e k6)

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0003](0003-clean-architecture-cqrs.md), [ADR-0007](0007-sql-server-ef-core-database-per-service.md), [ADR-0013](0013-bibliotecas-e-licencas.md)

## Contexto e problema

Testes são requisito obrigatório do desafio. Além de cobrir as regras de negócio, precisamos **provar** os requisitos não funcionais: isolamento de falhas (RNF-01) e 50 req/s com ≤ 5% de erro (RNF-02). Também é preciso decidir como testar a integração com SQL Server, RabbitMQ e Redis de forma fiel e reprodutível.

## Requisitos da decisão

- Feedback rápido no ciclo de desenvolvimento (TDD).
- Testes de integração fiéis à produção: transações, constraints, entrega de mensagens.
- Evidência mensurável dos SLOs.
- Execução local e no CI com um comando.

## Opções consideradas

**Integração:**
1. EF Core InMemory / mocks de broker
2. Banco e broker compartilhados de um ambiente de teste
3. **Testcontainers** (containers efêmeros por execução)

**Carga:**
1. JMeter
2. NBomber (C#)
3. **k6**
4. Locust

## Decisão

**Pirâmide de testes:**

| Nível | Ferramentas | O que cobre | Quando roda |
|---|---|---|---|
| **Unitário** | xUnit, NSubstitute, Shouldly, Bogus | Domínio (RN/RC), handlers, validators, decorators | A cada build; em segundos |
| **Arquitetura** | NetArchTest | Regras de dependência da Clean Architecture | A cada build |
| **Integração** | `WebApplicationFactory` + **Testcontainers** (MsSql, RabbitMq, Redis) | Endpoints ponta a ponta no serviço, migrations, outbox, consumidor idempotente, cache e fallback | A cada PR / CI |
| **Frontend** | Test runner do Angular | Services, componentes, interceptor, guards | A cada PR / CI |
| **Carga** | **k6** (container `grafana/k6`) | SLO-03..06 com thresholds que falham o teste | Sob demanda / pré-release |
| **Resiliência** | k6 + `docker compose stop` | SLO-02 (Lançamentos com o Consolidado fora), convergência após retorno | Sob demanda / pré-release |

**TDD:** as regras de negócio são desenvolvidas em ciclos **vermelho → verde → refatorar**. No histórico git, o teste aparece antes ou junto da implementação (`test:` → `feat:`).

**Convenções:**
- Nome: `Metodo_Cenario_ResultadoEsperado` (ex.: `Estornar_QuandoJaEstornado_RetornaErroConflito`).
- Estrutura **Arrange / Act / Assert**; um comportamento por teste.
- Test Data Builders + Bogus para dados válidos por padrão.
- Integração: um container por *collection fixture*, com reset de dados entre testes para isolamento e velocidade.
- Cobertura medida com coverlet. Meta orientativa: **≥ 80% em Domain e Application**, sem perseguir 100% em Infrastructure.

**Por que Testcontainers e não InMemory:** o provider InMemory do EF Core não tem transações, constraints nem o comportamento real de concorrência, justamente o que o outbox, a inbox e o `rowversion` exigem. Um teste verde no InMemory pode esconder um bug real.

**Por que k6:** scripts versionáveis em JavaScript, modelo **open model** (`constant-arrival-rate`), que mede exatamente "50 req/s" (em vez de "N usuários"), **thresholds** que transformam o SLO em critério pass/fail, e execução via container sem instalar nada. NBomber seria a alternativa em C#, mas o k6 é o padrão de mercado para esse tipo de validação.

## Consequências

### Positivas
- Regras de negócio protegidas por testes rápidos.
- Integração validada contra os mesmos motores de produção.
- Os RNFs do desafio viram **testes automatizados com critério objetivo**.

### Negativas / trade-offs aceitos
- Testes de integração exigem Docker e são mais lentos (containers).
- Testes de carga locais são limitados pelo hardware da máquina (não representam produção).

### Mitigações
- Reuso de containers por fixture; unitários separados para feedback rápido.
- Resultados de carga registrados com a especificação da máquina em [`testes.md`](../testes.md).

## Referências
- Martin Fowler — *The Practical Test Pyramid*
- Kent Beck — *Test-Driven Development: By Example*
- [Testcontainers for .NET](https://dotnet.testcontainers.org/) · [k6](https://grafana.com/docs/k6/)
