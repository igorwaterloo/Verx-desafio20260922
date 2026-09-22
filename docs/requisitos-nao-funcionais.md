# Requisitos Não Funcionais

Metas **mensuráveis** para cada requisito não funcional, com a forma de medição e o mecanismo arquitetural que as sustenta.

## 1. Requisitos do desafio

| ID | Requisito (enunciado) | Interpretação |
|---|---|---|
| RNF-01 | "O serviço de controle de lançamento não deve ficar indisponível se o sistema de consolidado diário cair." | Zero acoplamento síncrono de Lançamentos com o Consolidado. Falhas no Consolidado não podem produzir erros no registro de lançamentos. |
| RNF-02 | "Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições." | Consolidado.Api sustenta **50 req/s** com taxa de erro (HTTP 5xx, timeouts, conexões recusadas) **≤ 5%**. A meta interna é mais rígida: **< 1%**. |

### Requisitos derivados da decisão de SaaS ([ADR-0015](adr/0015-multi-tenancy-banco-compartilhado.md), [ADR-0017](adr/0017-contexto-plataforma-onboarding-planos.md))

| ID | Requisito |
|---|---|
| RNF-03 | **Isolamento entre tenants:** nenhum dado de um tenant pode ser lido, alterado ou inferido por outro. |
| RNF-04 | **Noisy neighbor:** o consumo excessivo de um tenant não pode degradar o serviço dos demais. |
| RNF-05 | **Onboarding em autoatendimento**, consistente (sem tenant parcialmente criado). |
| RNF-06 | **Limites por plano** (quota de lançamentos, usuários e taxa de requisições) aplicados sem criar dependência síncrona entre serviços. |
| RNF-07 | **LGPD:** dados por tenant identificáveis para exportação e exclusão; nenhum dado pessoal em logs. |

## 2. SLOs (Service Level Objectives)

| ID | SLI (indicador) | SLO (meta) | Como medir |
|---|---|---|---|
| SLO-01 | Disponibilidade de Lançamentos (respostas não-5xx / total) | **99,9%** mensal (≈ 43 min de indisponibilidade/mês) | Métrica `http.server.request.duration` (OTel) com o status code; teste de caos da seção 5 |
| SLO-02 | Disponibilidade de Lançamentos **com o Consolidado fora** | **100%** das requisições válidas retornam 201 | k6 sobre Lançamentos com `consolidado-api` e `consolidado-worker` parados |
| SLO-03 | Throughput do Consolidado | **≥ 50 req/s** sustentados por 5 min | k6 `constant-arrival-rate` |
| SLO-04 | Taxa de erro do Consolidado sob pico | **≤ 5%** (requisito); **< 1%** (meta interna) | Threshold k6 `http_req_failed` |
| SLO-05 | Latência do Consolidado | p95 **< 200 ms**, p99 **< 500 ms** | Threshold k6 `http_req_duration` |
| SLO-06 | Latência de Lançamentos (POST) | p95 **< 300 ms** | Threshold k6 |
| SLO-07 | Atraso da consolidação (lançamento → saldo atualizado) | p95 **< 5 s** em operação normal | Diferença entre `ocorridoEm` do evento e o commit do saldo (métrica customizada `consolidado.lag`) |
| SLO-08 | Integridade do saldo | **100%**: saldo = Σ créditos − Σ débitos, sem duplicidade | Testes de integração (entrega duplicada e fora de ordem) + reconciliação |
| SLO-09 | Isolamento entre tenants | **Zero** vazamentos | Testes de integração de isolamento obrigatórios em cada serviço (leitura, listagem, estorno, consolidado, cache) + teste de arquitetura (`ITenantEntity`) |
| SLO-10 | Noisy neighbor | Tenant B mantém SLO-04/05 enquanto o tenant A excede o próprio limite | k6 com dois tenants simultâneos: A recebe 429, B sem degradação |
| SLO-11 | Onboarding | p95 **< 3 s** com status `Ativo`; tenants `Pendente` resolvidos em **< 10 min** | Métricas `tenants.provisionamento.duracao` e `tenants.pendentes` |

## 3. Confiabilidade, Integridade e Disponibilidade

| Pilar | Meta | Mecanismos |
|---|---|---|
| **Disponibilidade** | Lançamentos 99,9%; Consolidado 99,5% | Serviços independentes; réplicas atrás do gateway com health check ativo; `restart` automático; fila durável como buffer. |
| **Confiabilidade** | Nenhum lançamento aceito é perdido; todo evento é aplicado | Transactional Outbox (at-least-once), retry com backoff, DLQ monitorada, consumidor idempotente. |
| **Integridade** | Saldo sempre exato; nenhum dado de outro tenant é acessível | Inbox (exactly-once no efeito), `decimal(18,2)` (sem ponto flutuante), concorrência otimista (`rowversion`), lançamentos imutáveis, filtro global por `TenantId` vindo do token, onboarding com compensação. |

## 4. Recuperação

| Métrica | Meta | Como é atingida |
|---|---|---|
| **RPO** (perda de dados aceitável) | **≈ 0** para lançamentos | O commit no banco é a confirmação; o evento é salvo na mesma transação. Em produção: Azure SQL zone-redundant + geo-replica. |
| **RTO** (tempo de recuperação de um serviço) | **< 5 min** | Healthchecks + restart automático; stateless, com múltiplas réplicas. |
| **Recuperação do Consolidado** | Backlog drenado automaticamente | Fila durável; o Worker processa o acumulado ao voltar. A projeção pode ser **reconstruída** a partir dos eventos/lançamentos (ver [evolução futura](evolucao-futura.md)). |

## 5. Estimativa de capacidade (dimensionamento)

**Consolidado (50 req/s em pico):**
- Com 2 réplicas, cada uma atende **≈ 25 req/s**. Uma instância ASP.NET Core com Kestrel lida com milhares de req/s em leituras simples, então a folga é mais de 10×.
- Com o cache aquecido, a maior parte das consultas é **cache hit** (Redis na casa de 100 mil ops/s), e o SQL Server só recebe os misses e os dias recém-invalidados.
- **Falha de uma réplica:** a outra absorve os 50 req/s sozinha, ainda com folga. O gateway retira a réplica doente pelo health check ativo e reenvia requisições idempotentes (GET) que falharem.
- **Disponibilidade composta** (aproximação com falhas independentes): duas réplicas a 99% cada dão `1 − 0,01² = 99,99%` na camada de API.

**Lançamentos:**
- Premissa de pico de **20 escritas/s** por tenant ativo de alto movimento, com a soma de todos os tenants na ordem de centenas de escritas/s. Cada escrita é uma transação curta (quota + 3 INSERTs); a contagem da quota usa o índice `(TenantId, CriadoEm)`.
- O outbox publica em lote. O RabbitMQ absorve dezenas de milhares de mensagens/s, então o broker não é gargalo nessa escala.

> As premissas acima são **validadas nos testes de stress** (Fase 8), e os resultados são registrados em [`testes.md`](testes.md).

## 6. Estratégias de desempenho e escalabilidade

| Estratégia | Onde | Efeito |
|---|---|---|
| Escala horizontal stateless | Gateway, APIs, Worker | Adicionar réplicas aumenta a capacidade de forma linear. |
| Balanceamento de carga | Gateway (YARP: round-robin + health check ativo/passivo) | Distribui carga e isola réplicas com falha. |
| Cache-aside com TTL + invalidação por evento | Consolidado.Api / Worker | Reduz a latência e a carga no banco; dado desatualizado é limitado pelo TTL. |
| CQRS: leitura separada da escrita | Consolidado.Api × Worker | Picos de leitura não competem com o processamento de eventos. |
| Consultas sem tracking + índices | EF Core / SQL Server | Leituras baratas; PKs e índices iniciados por `TenantId` (ex.: `(TenantId, Data)`). |
| Rate limiting por tenant/plano | Gateway | Isola tenants entre si (RNF-04); limites Free 20 req/s e Pro 100 req/s. |
| Connection pooling / `DbContext` pooling | Todos os serviços | Reduz o custo por requisição. |
| Rate limiting | Gateway | Protege contra abuso sem descartar o tráfego legítimo de pico. |
| Timeouts, retry e circuit breaker | Gateway → serviços; serviços → Redis | Evita falhas em cascata e requisições presas. |

## 7. Segurança (requisitos)

| ID | Requisito |
|---|---|
| SEG-01 | Toda API exige **JWT válido** (emissor Keycloak, audiência, assinatura, expiração), validado no gateway e nos serviços. |
| SEG-02 | Autorização por papel (`admin`, `operador`) e por **tenant** (`TenantId` = claim `tenant_id`; nunca aceito do corpo, da query ou de headers). Recurso de outro tenant responde 404. |
| SEG-03 | Tráfego externo somente via **HTTPS/TLS 1.2+**. |
| SEG-04 | Nenhum segredo no repositório; configuração por variáveis de ambiente/secret store. |
| SEG-05 | Validação de entrada em todos os endpoints; acesso a dados apenas parametrizado (EF Core). |
| SEG-06 | Rate limiting por tenant (plano), por usuário e por IP nas rotas públicas (onboarding); headers de segurança (HSTS, `X-Content-Type-Options`, CSP na SPA). |
| SEG-07 | Logs sem dados sensíveis (tokens e senhas nunca são logados; a senha do admin no onboarding só é repassada ao Keycloak). |
| SEG-08 | Conta de serviço do Tenants.Api no Keycloak com **permissões mínimas** (gerenciar organizações e usuários do realm). |
| SEG-09 | Isolamento de tenant em profundidade: filtro global do EF Core, interceptor de gravação, chaves de cache por tenant, testes de isolamento ([ADR-0015](adr/0015-multi-tenancy-banco-compartilhado.md)). |

Detalhamento de ameaças e controles: `seguranca.md` (Fase 6).

## 8. Observabilidade

| Sinal | Ferramenta | Exemplos |
|---|---|---|
| **Traces** distribuídos | OpenTelemetry → Aspire Dashboard | Um único trace do POST de lançamento até a atualização do saldo, com o contexto propagado pelo RabbitMQ. |
| **Métricas** | OpenTelemetry (Meter) | RPS, latência p95/p99, taxa de erro, cache hit ratio, profundidade da fila e da DLQ, `consolidado.lag`, uso de quota e 429 **por tenant/plano**, `tenants.pendentes`. |
| **Logs** estruturados | Serilog → OTLP | Correlacionados por `TraceId`. |
| **Health checks** | ASP.NET Core HealthChecks | `/health/live` (processo) e `/health/ready` (dependências). |

**Alertas propostos (produção):**
- Taxa de erro > 1% por 5 min.
- p95 do Consolidado > 200 ms.
- Profundidade da DLQ > 0.
- `consolidado.lag` p95 > 30 s.
- Réplicas saudáveis < 2.
- Tenants em `Pendente` há mais de 10 min.
- Qualquer falha nos testes de isolamento no pipeline **bloqueia o deploy**.
