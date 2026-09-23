# Architecture Decision Records (ADRs)

As decisões arquiteturais são registradas no formato [MADR](https://adr.github.io/madr/) ([ADR-0001](0001-registrar-decisoes-arquiteturais.md)). Cada ADR descreve o contexto, as opções consideradas, a decisão e as consequências. Para uma nova decisão, use o [template](template.md).

| # | Decisão | Tema | Status |
|---|---|---|---|
| [0001](0001-registrar-decisoes-arquiteturais.md) | Registrar decisões arquiteturais com ADR | Processo | Aceita |
| [0002](0002-microsservicos-por-bounded-context.md) | Microsserviços por bounded context | Estilo arquitetural | Aceita (complementada pela 0017) |
| [0003](0003-clean-architecture-cqrs.md) | Clean Architecture + CQRS | Design interno | Aceita |
| [0004](0004-comunicacao-assincrona-rabbitmq.md) | Comunicação assíncrona com RabbitMQ | Integração | Aceita |
| [0005](0005-outbox-e-consumidor-idempotente.md) | Transactional Outbox e consumidor idempotente | Integração / confiabilidade | Aceita |
| [0006](0006-cache-aside-redis.md) | Cache-aside com Redis e fallback | Desempenho | Aceita (complementada pela 0015) |
| [0007](0007-sql-server-ef-core-database-per-service.md) | SQL Server + EF Core, database-per-service | Persistência | Aceita (complementada pelas 0015 e 0017) |
| [0008](0008-autenticacao-keycloak-oidc-jwt.md) | Autenticação com Keycloak (OIDC/JWT) | Segurança | Aceita (complementada pelas 0015 e 0017) |
| [0009](0009-api-gateway-yarp.md) | API Gateway com YARP | Integração / segurança | Aceita (complementada pelas 0015 e 0017) |
| [0010](0010-separacao-consolidado-api-worker.md) | Separação Consolidado Api / Worker | Escalabilidade | Aceita |
| [0011](0011-observabilidade-opentelemetry.md) | Observabilidade com OpenTelemetry | Operação | Aceita |
| [0012](0012-estrategia-de-testes.md) | Estratégia de testes (TDD, pirâmide, k6) | Qualidade | Aceita |
| [0013](0013-bibliotecas-e-licencas.md) | Escolha de bibliotecas e licenças | Governança | Aceita |
| [0014](0014-lancamentos-imutaveis-com-estorno.md) | Lançamentos imutáveis com estorno | Domínio | Aceita |
| [0015](0015-multi-tenancy-banco-compartilhado.md) | SaaS multi-tenant com banco compartilhado e `TenantId` | SaaS / isolamento | Aceita |
| [0016](0016-web-api-com-controllers.md) | Web APIs com controllers | Design de API | Aceita |
| [0017](0017-contexto-plataforma-onboarding-planos.md) | Contexto Plataforma: onboarding, planos e quotas | SaaS | Aceita |
| [0018](0018-frontend-angular-spa.md) | Frontend Angular: SPA standalone, OIDC com PKCE e configuração em tempo de execução | Frontend / segurança | Aceita |

## Mapa: requisito → decisões

| Requisito | Decisões que o atendem |
|---|---|
| **RNF-01:** Lançamentos disponível se o Consolidado cair | [0002](0002-microsservicos-por-bounded-context.md), [0004](0004-comunicacao-assincrona-rabbitmq.md), [0005](0005-outbox-e-consumidor-idempotente.md), [0007](0007-sql-server-ef-core-database-per-service.md) |
| **RNF-02:** 50 req/s com ≤ 5% de perda | [0006](0006-cache-aside-redis.md), [0009](0009-api-gateway-yarp.md), [0010](0010-separacao-consolidado-api-worker.md) |
| **Integridade** do saldo | [0005](0005-outbox-e-consumidor-idempotente.md), [0014](0014-lancamentos-imutaveis-com-estorno.md) |
| **Segurança** | [0008](0008-autenticacao-keycloak-oidc-jwt.md), [0009](0009-api-gateway-yarp.md), [0015](0015-multi-tenancy-banco-compartilhado.md), [0018](0018-frontend-angular-spa.md) |
| **RNF-03:** isolamento entre tenants | [0015](0015-multi-tenancy-banco-compartilhado.md) |
| **RNF-04:** noisy neighbor | [0009](0009-api-gateway-yarp.md), [0015](0015-multi-tenancy-banco-compartilhado.md), [0017](0017-contexto-plataforma-onboarding-planos.md) |
| **RNF-05/06:** onboarding e limites por plano | [0017](0017-contexto-plataforma-onboarding-planos.md) |
| **Monitoramento / resiliência** | [0011](0011-observabilidade-opentelemetry.md), [0009](0009-api-gateway-yarp.md) |
| **Boas práticas / testes** | [0003](0003-clean-architecture-cqrs.md), [0012](0012-estrategia-de-testes.md), [0013](0013-bibliotecas-e-licencas.md), [0016](0016-web-api-com-controllers.md) |
