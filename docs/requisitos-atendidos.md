# Requisitos do desafio × solução

Rastreabilidade de cada ponto do enunciado até a decisão (ADR), a implementação e a **evidência** (teste automatizado ou medição). Os caminhos são relativos à raiz do repositório.

## Requisitos de negócio

| Enunciado | Solução | Evidência |
|---|---|---|
| Serviço que faça o **controle de lançamentos** (débitos e créditos) | `Lancamentos.Api`: registro, consulta por dia com paginação e estorno. Lançamentos imutáveis; correção por estorno ([ADR-0014](adr/0014-lancamentos-imutaveis-com-estorno.md)); idempotência por `Idempotency-Key`; regras RN-01 a RN-10 em [dominio.md](dominio.md) | `tests/Lancamentos.Domain.UnitTests`, `tests/Lancamentos.Application.UnitTests`, `tests/Lancamentos.IntegrationTests` (ex.: `Registrar_MesmaIdempotencyKey_RetornaOMesmoLancamentoSemDuplicar`, `Estornar_ComAdmin_CriaOEstornoEBloqueiaUmSegundo`) |
| Serviço do **consolidado diário** (relatório com o saldo diário) | `Consolidado.Worker` mantém a projeção `SaldoDiario` a partir dos eventos; `Consolidado.Api` responde o saldo do dia e o de períodos de até 93 dias, com cache Redis ([ADR-0010](adr/0010-separacao-consolidado-api-worker.md), [ADR-0006](adr/0006-cache-aside-redis.md)) | `tests/Consolidado.*` (entrega duplicada, estorno, período com dias vazios, isolamento, cache e Redis fora do ar) |
| Uso pelo comerciante | SPA Angular: cadastro da empresa, login, lançamentos, consolidado com gráfico, plano e usuários ([ADR-0018](adr/0018-frontend-angular-spa.md)) | 49 testes Vitest; smoke E2E Playwright (`src/app/fluxo-caixa-web/e2e`) |

## Requisitos técnicos obrigatórios

| Enunciado | Onde está |
|---|---|
| **Desenho da solução** | C4 níveis 1, 2 e 3 ([contexto](architecture/c4-1-contexto.md), [containers](architecture/c4-2-containers.md), [componentes](architecture/c4-3-componentes.md)); [fluxos de dados](architecture/fluxos.md) com cenários de falha; [implantação](architecture/deployment.md) local e em produção; modelo formal em [Structurizr DSL](architecture/workspace.dsl) |
| **Feito em C#** | Backend inteiro em .NET 10 / C# (`src/api`): 4 serviços, gateway e building blocks |
| **Testes** | 259 testes .NET (unitários, arquitetura, contrato, integração com containers reais), 49 do frontend, smoke E2E e testes de carga e caos com k6. Cobertura de 93,1% das linhas. Tudo no CI ([testes.md](testes.md)) |
| **Boas práticas** (Design Patterns, padrões de arquitetura, SOLID) | Clean Architecture + CQRS ([ADR-0003](adr/0003-clean-architecture-cqrs.md)) com as regras de dependência verificadas por testes de arquitetura (`tests/Architecture.Tests`). Padrões aplicados: Result, Decorator (validação e log no dispatcher), Repository, Unit of Work, Transactional Outbox, Idempotent Consumer, Cache-Aside, Circuit Breaker, API Gateway, Competing Consumers e eventos de integração versionados. TDD, `TreatWarningsAsErrors`, analisadores e `.editorconfig` |
| **README** com como funciona e como rodar localmente | [README.md](../README.md): visão geral, arquitetura, execução em 3 comandos, usuários de demonstração, APIs, testes |
| **Repositório público no GitHub** | https://github.com/igorwaterloo/Verx-desafio20260922 |
| **Toda a documentação no repositório** | Pasta [`docs/`](README.md): domínio, C4, fluxos, 18 ADRs, requisitos não funcionais, segurança, observabilidade, testes, evolução futura |

## Requisitos não funcionais

| Enunciado | Decisões | Evidência medida |
|---|---|---|
| **Lançamentos não pode ficar indisponível se o consolidado cair** (RNF-01) | Nenhuma chamada síncrona Lançamentos → Consolidado. Comunicação por eventos no RabbitMQ ([ADR-0004](adr/0004-comunicacao-assincrona-rabbitmq.md)), com Transactional Outbox, que também isola a queda do broker ([ADR-0005](adr/0005-outbox-e-consumidor-idempotente.md)). Bancos separados por serviço ([ADR-0007](adr/0007-sql-server-ef-core-database-per-service.md)) | Caos com o Consolidado inteiro parado por 60 s: 3.601 lançamentos, **0% de erro**. Com o RabbitMQ parado: 3.600 lançamentos, **0% de erro**. Saldo idêntico à soma após a recuperação. Teste `Registrar_ComRabbitMqIndisponivel_Retorna201EPublicaQuandoOBrokerVolta` ([testes.md](testes.md#resultados-dos-testes-de-carga-fase-8)) |
| **Consolidado recebe 50 req/s com no máximo 5% de perda** (RNF-02) | Leitura separada da escrita ([ADR-0010](adr/0010-separacao-consolidado-api-worker.md)); duas réplicas com balanceamento, health checks e retentativa no gateway ([ADR-0009](adr/0009-api-gateway-yarp.md)); cache-aside no Redis com fallback ao banco ([ADR-0006](adr/0006-cache-aside-redis.md)) | 5 min a 50 req/s: **0% de erro**, p95 de 6,8 ms, p99 de 9,4 ms. Pico de 100 req/s: 0% de erro. Uma réplica derrubada durante a carga: **0,49%** de perda, contra o limite de 5% e a meta interna de 1% |

## Objetivos de arquitetura do enunciado

| Tema | Como foi tratado | Onde |
|---|---|---|
| **Escalabilidade** (horizontal, balanceamento, cache) | Serviços sem estado; Consolidado.Api com réplicas atrás do gateway (round-robin); worker separado; cache-aside; consumo particionado por tenant + dia; estimativa de capacidade | [ADR-0010](adr/0010-separacao-consolidado-api-worker.md), [ADR-0006](adr/0006-cache-aside-redis.md), [RNF §5](requisitos-nao-funcionais.md#5-estimativa-de-capacidade-dimensionamento), [deployment](architecture/deployment.md) |
| **Resiliência** (redundância, failover, monitoramento, recuperação) | Outbox e retentativas com DLQ; consumidor idempotente; failover entre réplicas com timeout de conexão curto; disjuntor no cache; health checks; `restart: unless-stopped`; RPO ≈ 0 para lançamentos | [ADR-0005](adr/0005-outbox-e-consumidor-idempotente.md), [fluxos](architecture/fluxos.md), testes de caos em [testes.md](testes.md) |
| **Segurança** (autenticação, autorização, criptografia, proteção contra ataques) | Keycloak com OIDC e PKCE; JWT validado no gateway e nos serviços; papéis; isolamento por tenant em várias camadas; rate limit por tenant, plano e IP; CORS, CSP e cabeçalhos; TLS na borda em produção; segredos fora do Git; análise STRIDE | [ADR-0008](adr/0008-autenticacao-keycloak-oidc-jwt.md), [ADR-0015](adr/0015-multi-tenancy-banco-compartilhado.md), [segurança](seguranca.md) |
| **Padrões arquiteturais** (e trade-offs) | Microsserviços por bounded context, comparados com monólito modular e serverless | [ADR-0002](adr/0002-microsservicos-por-bounded-context.md) |
| **Integração** (protocolos, formatos, ferramentas) | REST/JSON versionado (`/api/v1`) com ProblemDetails; eventos JSON versionados em RabbitMQ (MassTransit); OIDC; OTLP | [ADR-0004](adr/0004-comunicacao-assincrona-rabbitmq.md), [ADR-0016](adr/0016-web-api-com-controllers.md), [domínio — eventos](dominio.md) |
| **Requisitos não funcionais** (métricas e metas claras) | 11 SLOs com meta, forma de medição e valor medido | [requisitos-nao-funcionais.md](requisitos-nao-funcionais.md) |
| **Documentação** (decisões, diagramas, fluxos) | 18 ADRs (MADR), C4 em Mermaid e Structurizr, 13 diagramas de sequência | [docs/](README.md) |
| **Métricas de confiabilidade, integridade e disponibilidade** | Disponibilidade (SLO-01/02), integridade do saldo (SLO-08: soma = consolidado, verificada em todas as execuções de carga), confiabilidade (erros, retentativas, DLQ); telemetria OpenTelemetry com métricas de negócio e alertas propostos | [RNF](requisitos-nao-funcionais.md), [observabilidade](observabilidade.md) |
| **Evoluções futuras** | Priorizadas, com o motivo de cada uma | [evolucao-futura.md](evolucao-futura.md) |

## Além do enunciado

Decisões tomadas para demonstrar o que é importante num produto real:

- **SaaS multi-tenant:** cada comerciante é um tenant, com onboarding em autoatendimento, planos e quotas ([ADR-0015](adr/0015-multi-tenancy-banco-compartilhado.md), [ADR-0017](adr/0017-contexto-plataforma-onboarding-planos.md)).
- **Testes de carga que acharam defeitos reais**, corrigidos com teste antes da correção:
  - disputa na mesma linha de saldo no worker;
  - conexões penduradas numa réplica fora da rede.
- **Observabilidade ponta a ponta:** um trace do clique no navegador até o saldo gravado ([observabilidade.md](observabilidade.md)).
- **Licenças avaliadas:** sem MediatR, FluentAssertions nem MassTransit 9 comerciais ([ADR-0013](adr/0013-bibliotecas-e-licencas.md)).
