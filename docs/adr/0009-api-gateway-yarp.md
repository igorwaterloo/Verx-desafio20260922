# ADR-0009: API Gateway com YARP

- **Status:** Aceita
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

## Referências
- [YARP — Yet Another Reverse Proxy](https://github.com/dotnet/yarp)
- Microsoft — *Gateway Routing / Gateway Offloading patterns*
