# ADR-0009: API Gateway com YARP

- **Status:** Aceita — complementada por [ADR-0015](0015-multi-tenancy-banco-compartilhado.md), [ADR-0017](0017-contexto-plataforma-onboarding-planos.md)
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0008](0008-autenticacao-keycloak-oidc-jwt.md), [ADR-0010](0010-separacao-consolidado-api-worker.md)

## Contexto e problema

A SPA consome dois serviços, e o Consolidado roda com **várias réplicas** (RNF-02). Sem um ponto de entrada único, o frontend precisaria conhecer o endereço de cada serviço e réplica, e preocupações transversais (CORS, rate limiting, validação de token, timeouts) seriam repetidas em cada serviço.

## Requisitos da decisão

- **Balanceamento de carga** entre réplicas com **health check** (retirar réplica doente).
- **Rate limiting** para proteger contra abuso (segurança e RNF-02).
- Ponto único para CORS, TLS e validação de JWT.
- Mesma stack (.NET), configurável e executável localmente.

## Opções consideradas

1. Sem gateway (SPA chama cada serviço diretamente)
2. **YARP** (reverse proxy da Microsoft em ASP.NET Core)
3. Ocelot
4. Nginx / Envoy / Kong
5. Gateway gerenciado (Azure API Management / Application Gateway)

## Decisão

**Opção escolhida:** **YARP**, hospedado em um projeto ASP.NET Core (`FluxoCaixa.Gateway`).

| Função | Implementação |
|---|---|
| Roteamento | `/api/v1/lancamentos/**` → cluster `lancamentos`; `/api/v1/consolidado/**` → cluster `consolidado` |
| Balanceamento | `RoundRobin` entre `consolidado-api-1` e `consolidado-api-2` |
| Health check ativo | Consulta `/health/ready` a cada 10 s; réplica doente sai do pool |
| Health check passivo | Falhas consecutivas marcam o destino como indisponível temporariamente |
| Retry | Apenas para **GET** (idempotente), em outra réplica |
| Timeouts | Por rota (ex.: 5 s), para evitar requisições presas |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting`: token bucket **por usuário** (claim `sub`) e por IP para anônimos. Limite dimensionado **acima** do pico legítimo (50 req/s) |
| Segurança | Validação de JWT, CORS restrito à origem da SPA, headers de segurança, HSTS |
| Observabilidade | OpenTelemetry com propagação de `traceparent` |

**Por que não sem gateway:** acoplaria a SPA à topologia interna e espalharia a segurança pelos serviços. Também impediria o balanceamento transparente entre réplicas.

**Por que não Ocelot:** projeto com manutenção menos ativa. YARP é mantido pela Microsoft, tem desempenho superior e extensibilidade via middlewares ASP.NET Core.

**Por que não Nginx/Envoy/Kong:** ótimos proxies, mas rate limiting por claim do JWT, health checks customizados e telemetria integrada exigem configuração/plugins em outra linguagem. YARP mantém tudo em C#, testável com os mesmos recursos dos serviços.

**Por que não APIM:** é a escolha natural em produção no Azure (ver [implantação](../architecture/deployment.md)), mas não roda localmente.

## Consequências

### Positivas
- A SPA conhece um único endereço.
- Failover transparente entre réplicas do Consolidado (contribui para o SLO-04).
- Segurança centralizada, e os serviços ainda validam o token (defesa em profundidade).

### Negativas / trade-offs aceitos
- Um salto de rede a mais (latência de ~1 ms).
- O gateway vira **ponto único de falha** se tiver uma só instância.

### Mitigações
- O gateway é stateless e barato de replicar: em produção, 2+ réplicas atrás do load balancer da plataforma.
- Timeouts e limites bem definidos para o gateway não acumular requisições.

## Atualizações

- **2026-09-22 — SaaS:** o rate limiting passa a ser **particionado por tenant** (claim `tenant_id`), com o limite definido pela claim `plano` (Free: 20 req/s; Pro: 100 req/s), mais um limite por usuário para evitar abuso de um único usuário. As rotas públicas de onboarding (`POST /api/v1/tenants`, `GET /api/v1/planos`) têm limite **por IP** rigoroso. Nova rota: `/api/v1/tenants/**` e `/api/v1/planos/**` → cluster `tenants`. Estouro do limite responde **429** com `Retry-After`.

- **2026-09-22 — Implementação (Fase 6):**
  - **Retentativa em outra réplica** implementada como middleware no pipeline do YARP: GET/HEAD com falha de transporte e resposta ainda não iniciada são reenviados a outra réplica disponível; escritas nunca são repetidas pelo gateway. A necessidade apareceu na verificação ponta a ponta: uma réplica que acabou de atender uma rajada e cai mantém a taxa de falhas abaixo do limite do health check passivo por vários segundos, e metade das consultas voltava 502. Com a retentativa, 12 de 12 consultas responderam 200 logo após a queda.
  - **Health checks:** ativo a cada 5 s (`/health/ready`) e passivo por taxa de falhas de transporte (janela de 10 s, reativação em 15 s). *Ajustado na Fase 8 (abaixo).*
  - **Rate limiting:** token bucket **por tenant** com a vazão do plano (claim `plano`) e janela fixa **por IP** nas rotas públicas (cadastro: 10/min; catálogo: 120/min). O limite adicional por usuário ficou como evolução: o limite por tenant já atende o RNF-04 ([segurança](../seguranca.md#5-riscos-residuais-e-evolução)).
  - **Autorização por rota:** `POST /api/v1/tenants` e `GET /api/v1/planos` anônimos; demais rotas exigem token válido; os serviços validam o token de novo.
  - **Headers encaminhados:** as APIs honram `X-Forwarded-Host/Proto`, para que `Location` e links usem o endereço público do gateway.
  - **Endurecimento HTTP:** headers de segurança, CORS restrito à SPA, limite de 1 MB no corpo, sem header `Server`.

- **2026-09-23 — Teste de caos (Fase 8): réplica que sai da rede.**
  - **Problema:** um container parado sai da rede, e a conexão ao IP dele fica pendurada em vez de ser recusada. Cada leitura esperava o `ActivityTimeout` (10 s) antes da retentativa em outra réplica.
  - **Efeito medido a 50 req/s:**
    - p99 de 10 s;
    - ~4,6% das iterações perdidas por falta de VUs (no limite do requisito de 5%).
  - **Ajustes:**
    - timeout de conexão de **1 s** (`Gateway:TempoLimiteDeConexao`);
    - health check ativo a cada **2 s**, com timeout de 1 s e **uma** falha para retirar a réplica (`ConsecutiveFailuresHealthPolicy.Threshold = 1`).
  - **Resultado (mesmo teste):**
    - 0% de erro;
    - p95 de 7,4 ms e p99 de 344 ms;
    - 0,49% de iterações perdidas.
  - **Teste:** `Failover_ReplicaQueSumiuDaRede_LeiturasNaoFicamPenduradas` (destino não roteável).

## Referências
- [YARP — Yet Another Reverse Proxy](https://github.com/dotnet/yarp)
- Microsoft — *Gateway Routing / Gateway Offloading patterns*
