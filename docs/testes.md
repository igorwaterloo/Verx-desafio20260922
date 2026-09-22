# Estratégia de Testes

> Documento inicial. É detalhado nas fases de implementação, e os resultados de carga entram na Fase 8. Decisão: [ADR-0012](adr/0012-estrategia-de-testes.md).

## Pirâmide

| Nível | Ferramentas | Escopo |
|---|---|---|
| **Unitários** | xUnit, NSubstitute, Shouldly, Bogus | Domínio (invariantes), Application (handlers, validators, verificação de quota), dispatcher e decorators. Rápidos, sem I/O. |
| **Arquitetura** | NetArchTest | Regras de dependência da Clean Architecture; controllers sem acesso a Infrastructure; toda entidade de negócio implementa `ITenantEntity`. |
| **Contrato** | xUnit + System.Text.Json | JSON dos eventos de integração igual ao publicado em `dominio.md`; mudança incompatível quebra o build antes de quebrar um consumidor. |
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

## Situação atual das suítes

| Suíte | Testes | Destaques |
|---|---|---|
| Arquitetura | 20 | Camadas, contextos isolados, building blocks sem dependências, `ITenantEntity` |
| SharedKernel / Application.Common / Infrastructure.Common / Contracts | 54 | Result, primitivas de domínio, dispatcher e decorators, isolamento de tenant no EF Core, contratos JSON |
| Lançamentos — domínio | 30 | RN-01 a RN-06, borda de fuso (23h30 em São Paulo = dia seguinte em UTC), projeção do plano |
| Lançamentos — aplicação | 21 | Quota RN-09 (mês em São Paulo, padrão Free), idempotência inclusive em corrida, estorno RN-10 |
| Lançamentos — integração | 12 | Outbox → RabbitMQ, 400/401/403/404/409/422, isolamento entre tenants, quota alimentada por evento, **POST com o RabbitMQ pausado retorna 201 e o evento é entregue quando o broker volta (RNF-01)** |

| Consolidado — domínio | 10 | Aplicação comutativa (RC-02), estorno anulando o efeito, inbox |
| Consolidado — aplicação | 14 | Inbox, invalidação após o commit, cache-aside com TTL por tipo de dia, zeros (RC-03), período (máx. 93 dias) |
| Consolidado — integração | 11 | Evento → worker → consulta; **evento duplicado aplicado uma vez**; isolamento; cache populado e invalidado; **Redis pausado sem erro** |

| Tenants — domínio | 26 | CNPJ com dígitos verificadores, catálogo de planos, ciclo Pendente → Ativo/Falhou, RP-05 |
| Tenants — aplicação | 17 | Saga de onboarding (compensação, retomada, 503), troca de plano, limite de usuários, expiração |
| Tenants — integração | 9 | **Keycloak real**: admin criado faz login com as claims do tenant; compensação; Keycloak pausado → 503 → reenvio conclui; plano refletido no token |

### Cenários de integração de Lançamentos

| Cenário | Resultado esperado |
|---|---|
| Registrar válido | 201 + `Location`; evento `LancamentoRegistrado` recebido no broker |
| Validação de campos / regra de domínio | 400 com erros por campo / `codigo` estável |
| Sem autenticação / sem `tenant_id` | 401 / 403 |
| Mesma `Idempotency-Key` duas vezes | Mesmo lançamento, sem duplicidade |
| Tenant B acessa dados do tenant A | 404 em consulta e estorno; listagem vazia |
| Estorno por operador / admin / segundo estorno | 403 / 201 + evento com tipo inverso / 409 |
| Plano com limite 2 publicado por evento | 3º lançamento do mês retorna 422 |
| RabbitMQ pausado durante o registro | 201; evento entregue após o broker voltar |

### Cenários de integração do Consolidado

| Cenário | Resultado esperado |
|---|---|
| Créditos e débitos publicados | Saldo do dia com totais corretos |
| Mesmo evento entregue duas vezes | Aplicado uma única vez (inbox) |
| Crédito seguido do estorno | Saldo zero, 2 lançamentos |
| Dia sem movimento / período com dias vazios | Zeros / todos os dias preenchidos e totais |
| Período invertido ou maior que 93 dias | 400 |
| Tenant B consulta o dia com movimento do tenant A | Saldo zero |
| Consulta → novo evento | Cache populado e invalidado; nova consulta reflete o evento |
| Redis pausado | 200 pelo banco, em menos de 2 s (disjuntor) |

### Cenários de integração do Tenants

| Cenário | Resultado esperado |
|---|---|
| Onboarding válido | 201 `Ativo`; o admin faz login com `tenant_id`, `plano` e papéis `admin`/`operador`; `TenantProvisionado` publicado |
| CNPJ já cadastrado | 409 `tenant.cnpj_ja_cadastrado` |
| E-mail de admin de outro tenant | 409, compensação, tenant `Falhou` (novo reenvio: `tenant.provisionamento_falhou`) |
| Keycloak pausado | 503 `tenant.identidade_indisponivel`; reenvio após o retorno conclui (201) |
| Troca de plano | 403 para operador; admin: 200, evento publicado e claim `plano` atualizada no novo token |
| Usuários no plano Free | Admin + 1 operador; o terceiro retorna 422 `tenant.limite_usuarios` |
| Tenants de demonstração | Semeados na inicialização com os planos do realm |

## Verificação ponta a ponta (compose, tokens reais)

| Verificação | Resultado |
|---|---|
| POST no Lançamentos → saldo visível no Consolidado (SLO-07 < 5 s) | 250–350 ms em regime; ~4,9 s na primeira requisição após subir os containers (cold start) |
| Consolidado inteiro parado (api-1, api-2, worker) | Consulta indisponível; **10/10 lançamentos com 201** (RNF-01) |
| Consolidado religado | Backlog processado; saldo convergiu com os 10 lançamentos |
| Empresa nova por autoatendimento | 201 `Ativo` → login do admin com as claims do tenant → lançamento 201 → saldo no Consolidado |
| Upgrade Free → Pro | Novo token com `plano=pro` e projeção do plano no LancamentosDb atualizada por evento (limite 50.000) |

## Resultados de carga

_Serão registrados na Fase 8._
