# Estratégia de Testes

> Documento inicial. É detalhado nas fases de implementação, e os resultados de carga entram na Fase 8.

## Pirâmide

| Nível | Ferramentas | Escopo |
|---|---|---|
| **Unitários** | xUnit, NSubstitute, Shouldly, Bogus | Domínio (invariantes) e Application (handlers, validators). Rápidos, sem I/O. |
| **Arquitetura** | NetArchTest | Regras de dependência da Clean Architecture. |
| **Integração** | WebApplicationFactory, Testcontainers (SQL Server, RabbitMQ, Redis) | Endpoints de ponta a ponta no serviço, persistência, outbox, consumidor idempotente, cache. |
| **Frontend** | Test runner do Angular (unitários) | Services, componentes, interceptors. |
| **Carga / stress** | k6 | SLO-03..SLO-06: 50 req/s no Consolidado com ≤ 5% de erro. |
| **Resiliência (caos)** | k6 + `docker compose stop` | SLO-02: Lançamentos disponível com o Consolidado fora. |

## TDD

As regras de negócio seguem **vermelho → verde → refatorar**: o teste que descreve a regra (ex.: RN-05, "um lançamento só pode ser estornado uma vez") é escrito antes da implementação.

## Resultados

_Serão registrados na Fase 8._
