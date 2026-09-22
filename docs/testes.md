# Estratégia de Testes

> Documento inicial. É detalhado nas fases de implementação, e os resultados de carga entram na Fase 8. Decisão: [ADR-0012](adr/0012-estrategia-de-testes.md).

## Pirâmide

| Nível | Ferramentas | Escopo |
|---|---|---|
| **Unitários** | xUnit, NSubstitute, Shouldly, Bogus | Domínio (invariantes), Application (handlers, validators, verificação de quota), dispatcher e decorators. Rápidos, sem I/O. |
| **Arquitetura** | NetArchTest | Regras de dependência da Clean Architecture; controllers sem acesso a Infrastructure; toda entidade de negócio implementa `ITenantEntity`. |
| **Integração** | WebApplicationFactory, Testcontainers (SQL Server, RabbitMQ, Redis) | Endpoints de ponta a ponta no serviço, persistência, outbox, consumidor idempotente, cache, **isolamento entre tenants** e quota. |
| **Frontend** | Test runner do Angular (unitários) | Services, componentes, interceptors, guards por papel. |
| **Carga / stress** | k6 | SLO-03..SLO-06: 50 req/s no Consolidado com ≤ 5% de erro (tenants no plano Pro). |
| **Noisy neighbor** | k6 | SLO-10: um tenant excedendo o limite recebe 429 sem degradar outro tenant. |
| **Resiliência (caos)** | k6 + `docker compose stop` | SLO-02: Lançamentos disponível com o Consolidado (e a Plataforma) fora. |

## Testes de isolamento entre tenants (obrigatórios — SLO-09)

Para cada serviço, com dois tenants (A e B) criados no teste:

| Cenário | Esperado |
|---|---|
| B consulta por id um recurso de A | 404 |
| B lista recursos | Somente os de B |
| B tenta estornar um lançamento de A | 404 |
| Consolidado de B após lançamentos de A | Saldo de B inalterado |
| Cache: A consulta o dia D, depois B consulta o dia D | B recebe o próprio saldo (chaves distintas) |
| Requisição sem claim `tenant_id` | 403 |
| Evento sem `tenantId` | Rejeitado (vai para a DLQ) |

Uma falha nesses testes **bloqueia o merge e o deploy**.

## Como os testes são executados

- **Runner:** Microsoft Testing Platform (MTP), habilitado em `global.json`. Comando: `dotnet test`.
- **Cobertura:** `Microsoft.Testing.Extensions.CodeCoverage` (`dotnet test -- --coverage`).
- **Em container:** `scripts/test.ps1` (Windows) ou `scripts/test.sh` (Linux/macOS) executam build e testes no `mcr.microsoft.com/dotnet/sdk:10.0`, sem exigir o SDK .NET na máquina. É o mesmo ambiente do CI.

## TDD

As regras de negócio seguem **vermelho → verde → refatorar**: o teste que descreve a regra (ex.: RN-05, "um lançamento só pode ser estornado uma vez"; RN-09, "quota do plano") é escrito antes da implementação.

## Resultados

_Serão registrados na Fase 8._
