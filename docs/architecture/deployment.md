# Visão de Implantação

> Fonte formal: [`workspace.dsl`](workspace.dsl), visões `Deploy-Local` e `Deploy-Producao`.

## 1. Ambiente local (Docker Compose)

Um único `docker compose up` sobe toda a solução, incluindo a infraestrutura.

```mermaid
flowchart TB
    browser["🖥️ <b>Navegador</b><br/>Angular SPA"]

    subgraph host["Docker Desktop — rede bridge fluxo-caixa"]
        direction TB
        web["<b>web</b><br/>nginx:alpine<br/>:4200"]
        kc["<b>keycloak</b><br/>quay.io/keycloak/keycloak<br/>:8081"]
        gw["<b>gateway</b><br/>.NET 10 + YARP<br/>:8080"]

        subgraph svc["Serviços"]
            tapi["<b>tenants-api</b><br/>.NET 10<br/>:5301"]
            lapi["<b>lancamentos-api</b><br/>.NET 10<br/>:5101"]
            capi1["<b>consolidado-api-1</b><br/>.NET 10"]
            capi2["<b>consolidado-api-2</b><br/>.NET 10"]
            wk["<b>consolidado-worker</b><br/>.NET 10"]
        end

        subgraph infra["Infraestrutura"]
            sql[("<b>sqlserver</b><br/>mssql/server:2022<br/>:1433<br/>TenantsDb · LancamentosDb<br/>ConsolidadoDb")]
            mq{{"<b>rabbitmq</b><br/>rabbitmq:4-management<br/>:5672 · UI :15672"}}
            redis[("<b>redis</b><br/>redis:7-alpine<br/>:6379")]
            aspire["<b>aspire-dashboard</b><br/>UI :18888 · OTLP :4317"]
        end
    end

    browser --> web
    browser --> kc
    browser --> gw
    gw --> tapi
    gw --> lapi
    tapi --> sql
    tapi --> mq
    tapi --> kc
    mq --> lapi
    gw --> capi1
    gw --> capi2
    lapi --> sql
    lapi --> mq
    mq --> wk
    wk --> sql
    wk --> redis
    capi1 & capi2 --> redis
    capi1 & capi2 --> sql
```

| Serviço | Imagem | Porta no host | Healthcheck | Observação |
|---|---|---|---|---|
| `web` | nginx:alpine (build multi-stage) | 4200 | `GET /` | SPA |
| `gateway` | .NET 10 (build) | 8080 | `/health/ready` | Única porta de API exposta para a SPA |
| `tenants-api` | .NET 10 (build) | 5301 (debug) | `/health/ready` | Onboarding, planos, usuários; usa a Admin API do Keycloak |
| `lancamentos-api` | .NET 10 (build) | 5101 (debug) | `/health/ready` | Também consome eventos de tenant/plano |
| `consolidado-api-1/2` | .NET 10 (build) | — | `/health/ready` | Acesso **somente via gateway**; duas instâncias explícitas para demonstrar balanceamento e failover |
| `consolidado-worker` | .NET 10 (build) | — | `/health/live` | |
| `sqlserver` | mcr.microsoft.com/mssql/server:2022-latest | 1433 | `sqlcmd SELECT 1` | Um banco por serviço: `TenantsDb`, `LancamentosDb`, `ConsolidadoDb` |
| `rabbitmq` | rabbitmq:4-management | 5672 / 15672 | `rabbitmq-diagnostics ping` | Definições (exchanges/filas) importadas no start |
| `redis` | redis:7-alpine | 6379 | `redis-cli ping` | |
| `keycloak` | quay.io/keycloak/keycloak | 8081 | `/health/ready` | Realm `fluxo-caixa` com Organizations habilitado, clientes (SPA, API, conta de serviço do Tenants.Api) e **dois tenants de demonstração** (Free e Pro), com usuários `admin` e `operador` |
| `aspire-dashboard` | mcr.microsoft.com/dotnet/aspire-dashboard | 18888 / 4317 | — | Traces, métricas e logs |

**Resiliência local:**
- `restart: unless-stopped` em todos os serviços.
- `depends_on` com `condition: service_healthy`.
- Migrations do EF aplicadas na inicialização, com retry até o SQL Server ficar saudável.

## 2. Produção (visão alvo — Azure)

Não faz parte da entrega executável. Documenta **como a mesma arquitetura escalaria** em um ambiente real.

```mermaid
flowchart TB
    user["👤 Comerciante"]
    afd["<b>Azure Front Door + WAF</b><br/>TLS, OWASP rules, DDoS, CDN"]
    swa["<b>Static Web App</b><br/>Angular SPA"]
    entra["<b>Keycloak gerenciado</b><br/>ou Microsoft Entra ID"]

    subgraph region["Azure Brazil South — 3 zonas de disponibilidade"]
        subgraph aks["AKS / Azure Container Apps"]
            gw["gateway ×2+<br/>rate limit por tenant"]
            tapi["tenants-api ×2"]
            lapi["lancamentos-api ×3<br/>HPA (CPU/RPS)"]
            capi["consolidado-api ×3<br/>HPA (CPU/RPS)"]
            wk["consolidado-worker ×2<br/>KEDA (tamanho da fila)"]
        end
        sql[("Azure SQL (elastic pool)<br/>Business Critical<br/>zone-redundant")]
        mq{{"RabbitMQ cluster 3 nós<br/>quorum queues<br/>(ou Azure Service Bus)"}}
        redis[("Azure Cache for Redis<br/>Premium, zone-redundant")]
        kv["Azure Key Vault<br/>segredos e certificados"]
        mon["Azure Monitor / App Insights<br/>OpenTelemetry"]
    end

    subgraph dr["Região secundária (DR)"]
        sqlgeo[("Azure SQL<br/>geo-replica")]
    end

    user --> afd
    afd --> swa
    afd --> gw
    user --> entra
    gw --> tapi & lapi & capi
    tapi --> sql
    tapi --> mq
    tapi --> entra
    lapi --> sql
    lapi --> mq
    mq --> wk
    wk --> sql
    wk --> redis
    capi --> redis
    capi --> sql
    sql -. "failover group" .-> sqlgeo
    aks -. "Managed Identity" .-> kv
    aks -. "OTLP" .-> mon
```

| Preocupação | Estratégia |
|---|---|
| **Escalabilidade** | Pods stateless com HPA; o Worker escala por KEDA conforme o tamanho da fila. Bancos em **elastic pool**; tenants grandes podem migrar para banco dedicado (modelo híbrido, [ADR-0015](../adr/0015-multi-tenancy-banco-compartilhado.md)). |
| **Alta disponibilidade** | Réplicas distribuídas em 3 zonas; banco, cache e broker zone-redundant. |
| **Disaster recovery** | Azure SQL com failover group para a região secundária; infraestrutura como código (Bicep/Terraform) para recriar o cluster. |
| **Segurança** | WAF na borda; Managed Identity para acessar SQL e Key Vault (sem senha em configuração); rede privada (Private Endpoints) para dados. |
| **Multi-tenancy** | Métricas e custos por `tenant.id`; rate limit por plano no gateway (ou APIM com políticas por produto/assinatura). |
| **Deploy** | GitHub Actions: build, testes, imagem, deploy **blue/green ou canário** com rollback automático por SLO. |
