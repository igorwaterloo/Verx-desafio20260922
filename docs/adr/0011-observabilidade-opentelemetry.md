# ADR-0011: Observabilidade com OpenTelemetry

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0002](0002-microsservicos-por-bounded-context.md), [requisitos não funcionais — observabilidade](../requisitos-nao-funcionais.md#8-observabilidade)

## Contexto e problema

Com múltiplos serviços, um broker e processamento assíncrono, entender **por que um saldo não atualizou** ou **onde está a latência** exige correlacionar eventos entre processos. O desafio também pede **monitoramento proativo** e **métricas operacionais** de confiabilidade e disponibilidade.

## Requisitos da decisão

- Traces distribuídos que **atravessem o RabbitMQ** (do POST até a atualização do saldo).
- Métricas para medir os SLOs (RPS, latência, erros, lag, filas).
- Logs estruturados correlacionados ao trace.
- Neutralidade de fornecedor (trocar o backend sem mudar o código).
- Execução local leve.

## Opções consideradas

1. Logs em arquivo/console apenas
2. **OpenTelemetry SDK + Aspire Dashboard** (local) / Azure Monitor (produção)
3. Prometheus + Grafana + Jaeger + Loki
4. Elastic Stack (ELK/APM)
5. SDKs proprietários (Application Insights SDK, Datadog)

## Decisão

**Opção escolhida:** **OpenTelemetry** como padrão de instrumentação, exportando via **OTLP**:
- **Local:** **.NET Aspire Dashboard** (um container, com traces, métricas e logs na mesma UI).
- **Produção:** Azure Monitor / Application Insights (ou Grafana), **só trocando o endpoint OTLP**.

| Sinal | Instrumentação |
|---|---|
| Traces | ASP.NET Core, HttpClient, EF Core/SqlClient, MassTransit (propaga `traceparent` nas mensagens), Redis |
| Métricas | Runtime, ASP.NET Core, Kestrel + métricas de negócio: `lancamentos.registrados`, `consolidado.lag`, `consolidado.cache.hits/misses`, `outbox.pendentes` |
| Logs | `ILogger` com Serilog, JSON estruturado, exportado via OTLP com `TraceId`/`SpanId` |
| Health | `/health/live` (liveness) e `/health/ready` (readiness: banco, broker, Redis conforme o serviço) |

## Consequências

### Positivas
- Um único trace mostra o caminho completo: Gateway → Lancamentos.Api → SQL → Outbox → RabbitMQ → Worker → SQL → Redis.
- Os SLOs viram métricas observáveis (base para alertas e error budget).
- Padrão CNCF, sem aprisionamento a fornecedor.
- O Aspire Dashboard é leve e não exige configurar Prometheus, Grafana e Jaeger separadamente.

### Negativas / trade-offs aceitos
- O Aspire Dashboard guarda dados **em memória** (sem retenção). Serve para desenvolvimento, não para produção.
- Overhead de instrumentação (pequeno; sampling configurável).

### Mitigações
- Sampling em produção (ex.: parent-based, 10%, e 100% em erros).
- Os alertas propostos ficam documentados nos requisitos não funcionais para configuração no backend de produção.

## Referências
- [OpenTelemetry](https://opentelemetry.io/)
- [.NET Aspire Dashboard standalone](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone)
- Google — *Site Reliability Engineering* (SLIs, SLOs, error budgets)
