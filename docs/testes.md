# Estratégia de Testes

> Decisão: [ADR-0012](adr/0012-estrategia-de-testes.md). Resultados de carga e caos na [seção final](#resultados-dos-testes-de-carga-fase-8).

## Pirâmide

| Nível | Ferramentas | Escopo |
|---|---|---|
| **Unitários** | xUnit, NSubstitute, Shouldly, Bogus | Domínio (invariantes), Application (handlers, validators, verificação de quota), dispatcher e decorators. Rápidos, sem I/O. |
| **Arquitetura** | NetArchTest | Regras de dependência da Clean Architecture; controllers sem acesso a Infrastructure; toda entidade de negócio implementa `ITenantEntity`. |
| **Contrato** | xUnit + System.Text.Json | JSON dos eventos de integração igual ao publicado em `dominio.md`; mudança incompatível quebra o build antes de quebrar um consumidor. |
| **Integração** | WebApplicationFactory, Testcontainers (SQL Server, RabbitMQ, Redis) | Endpoints de ponta a ponta no serviço, persistência, outbox, consumidor idempotente, cache, **isolamento entre tenants** e quota. |
| **Frontend (unitários)** | Vitest + jsdom (`ng test`), TestBed, `HttpTestingController` | Validadores, conversão de erros (ProblemDetails, 429, 503), clientes das APIs (`Idempotency-Key`), guards por papel, páginas (idempotência no reenvio, cadastro pendente, banner de consolidado indisponível). |
| **E2E (smoke)** | Playwright em container, contra o `docker compose` | Cadastro da empresa → login OIDC real no Keycloak → crédito e débito → saldo consolidado converge. Roda com a CSP de produção. |
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
- **Frontend:** `npx ng test --watch=false` em `src/app/fluxo-caixa-web` (49 testes).
- **E2E:** `scripts/test-e2e.ps1` / `scripts/test-e2e.sh`, com a stack do compose no ar. A imagem `mcr.microsoft.com/playwright` usa a rede do host para acessar a SPA (:4200), o gateway (:8080) e o Keycloak (:8081) pelos mesmos endereços do navegador. Cada execução cadastra uma empresa nova com CNPJ gerado.

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

| Gateway | 14 | Roteamento, JWT (401), rotas públicas, **rate limit por tenant sem afetar outro tenant**, limite por IP, balanceamento, **failover com retentativa em outra réplica**, headers, CORS, 413 |

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

### Verificação do gateway no compose

| Verificação | Resultado |
|---|---|
| Rotas protegidas sem token | 401 no gateway |
| Cadastro → login → lançamento → consolidado, tudo pela porta 8080 | 201 / 201 / saldo atualizado; `Location` com o endereço público |
| Rajada de 60 requisições simultâneas por tenant | Padaria (Free, 20 req/s): 20×200 e 40×429 · Mercado (Pro, 100 req/s): 60×200 |
| `consolidado-api-1` parado logo após a rajada | 12 de 12 consultas com 200 (retentativa na réplica 2) |

## Resultados dos testes de carga (Fase 8)

Execução em 2026-09-23 com a stack completa do `docker compose` numa única máquina:
- **Máquina:** Intel Core 7 240H, 10 núcleos / 16 threads, 16 GB de RAM.
- **Docker Desktop 29.8** (WSL2): 16 vCPUs e 7,6 GB.
- **k6 2.3.0** no mesmo host.

Tudo passa pelo gateway, com tokens reais do Keycloak. Os números valem como **ordem de grandeza e comparação entre execuções**, não como capacidade de produção: gerador de carga e sistema dividem a mesma CPU.

Como rodar (com a stack no ar):

```powershell
./scripts/carga.ps1 consolidado-50rps       # também: lancamentos-carga, noisy-neighbor
./scripts/caos.ps1 consolidado              # também: broker, replica
```

Cada execução cria os **próprios tenants** pelo onboarding. O login com senha é feito uma vez; os VUs renovam o token pelo refresh token, como a SPA. Os resumos ficam em `tests/stress/k6/resultados/` (fora do Git).

### Resultado final

| Teste | Carga | Resultado | SLO |
|---|---|---|---|
| **Consolidado sustentado** (`consolidado-50rps`) | 50 req/s por 5 min, um tenant Pro, 70% consulta do dia e 30% período de 7 ou 30 dias | 15.001 requisições, **0% de erro**, 0 iterações perdidas; p95 **6,8 ms**, p99 **9,4 ms** | RNF-02 (≤ 5% de perda) e meta interna (< 1%) ✅ · SLO-05 (p95 < 200 ms, p99 < 500 ms) ✅ |
| **Consolidado em pico** | 100 req/s por 1 min, dois tenants Pro | 6.001 requisições, **0% de erro**; p95 **6,1 ms** | SLO-04 ✅ |
| **Escrita de lançamentos** (`lancamentos-carga`) | Rampa até 50 req/s + 3 min, um tenant, mesma data | 10.075 lançamentos, **0% de erro**; p95 **33 ms**, p99 45 ms; 471 reenvios com a mesma `Idempotency-Key` devolveram o mesmo lançamento; consolidado **igual à soma dos lançamentos 0,57 s** após o fim da carga | SLO-06 (p95 < 300 ms) ✅ · SLO-07 (< 5 s) ✅ · SLO-08 ✅ |
| **Vizinho barulhento** (`noisy-neighbor`) | Free a 60 req/s (3× o limite) + Pro a 50 req/s, por 2 min | Free: exatamente o limite aceito (~20 req/s), **66% com 429** + `Retry-After`, nenhum outro erro. Pro: **0% de erro**, p95 **7,9 ms**, igual à execução isolada | SLO-10 ✅ |
| **Caos: Consolidado fora** (`caos consolidado`) | 20 lançamentos/s por 3 min; API (2 réplicas) e worker parados por 60 s | 3.601 lançamentos, **0% de erro**, p95 37 ms; após religar, saldo **idêntico** à soma dos lançamentos | RNF-01 / SLO-02 ✅ |
| **Caos: broker fora** (`caos broker`) | 20 lançamentos/s por 3 min; RabbitMQ parado por 60 s | 3.600 lançamentos, **0% de erro** (outbox); todos os eventos entregues depois do retorno; saldo idêntico | RNF-01 / SLO-02 ✅ |
| **Caos: uma réplica fora** (`caos replica`) | 50 req/s por 3 min; `consolidado-api-1` parado por 60 s | **0% de erro**; p95 7,4 ms, p99 344 ms; 0,49% de iterações perdidas | RNF-02 ✅ · meta interna < 1% ✅ |

### Problemas encontrados pelos testes e corrigidos

| Problema | Antes | Correção | Depois |
|---|---|---|---|
| Consumidores concorrentes disputando a **mesma linha de saldo** (tenant + dia) com concorrência otimista; os conflitos caíam no retry exponencial | Consolidado convergiu **14 s** após 50 lançamentos/s (SLO-07: < 5 s); centenas de conflitos e risco de DLQ | Consumo **particionado por tenant + data** no worker ([ADR-0010](adr/0010-separacao-consolidado-api-worker.md)); teste de integração de rajada com 300 eventos | **0,57 s**, zero conflitos |
| Réplica que sai da rede: a conexão fica **pendurada** em vez de ser recusada, e o gateway só tentava a outra réplica após o timeout da requisição (10 s) | p99 **10 s**; **4,6%** das iterações perdidas (no limite dos 5%) | Timeout de conexão de 1 s e health check ativo a cada 2 s, que retira a réplica na primeira falha ([ADR-0009](adr/0009-api-gateway-yarp.md)); teste do gateway com destino não roteável | p99 **344 ms**, **0,49%** perdidas |
| Muitos logins simultâneos do mesmo usuário no k6 | Keycloak bloqueou os usuários (proteção contra força bruta) | Problema do **teste**, não do sistema: login uma vez no `setup` e renovação por refresh token | — |

### Limites conhecidos
- O particionamento do worker vale **dentro de uma instância**. Para escalar o worker horizontalmente sem conflitos: *consistent hash exchange* no RabbitMQ ou sessões no Azure Service Bus.
- Com todas as réplicas do Consolidado fora, as consultas retornam 502/503 (esperado). A SPA mostra o aviso de indisponibilidade, e os lançamentos seguem funcionando.
- Carga medida numa única máquina. Em produção, repetir os mesmos scripts contra o ambiente de homologação, com o gerador de carga separado.
