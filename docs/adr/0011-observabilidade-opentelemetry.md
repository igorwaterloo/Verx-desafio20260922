# ADR-0011: Observabilidade com OpenTelemetry

- **Status:** Aceita — atualizada em 2026-09-23 (ver Atualizações)
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

## Atualizações

- **2026-09-23 — Implementação (Fase 9).** Guia de uso em [observabilidade.md](../observabilidade.md).
  - **Configuração comum:** `AddObservabilidade` no `Infrastructure.Common`, usado pelos cinco executáveis. A exportação OTLP só é ligada quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definido.
  - **Sem Serilog:** o `ILogger` exporta direto pelo OpenTelemetry (logs estruturados, com `TraceId` e escopos). Serilog não acrescentaria nada, já que o destino é o mesmo OTLP; é uma dependência a menos.
  - **Somente instrumentações estáveis (1.19):**
    - EF Core e Redis ainda são *beta*. O SqlClient cobre todas as consultas do EF Core.
    - O Redis é medido pela métrica `consolidado.cache.leituras` (acerto, falta, indisponível).
    - MassTransit emite traces e métricas nativos.
  - **Nomes finais das métricas de negócio:**
    - `lancamentos.registrados` e `lancamentos.quota_excedida`;
    - `consolidado.atraso` (antes chamado `consolidado.lag`), histograma do SLO-07;
    - `consolidado.eventos`, `consolidado.cache.leituras` e `consolidado.retentativas`;
    - `gateway.limite_excedido`.

    A profundidade do outbox e das filas fica com o RabbitMQ (plugin Prometheus), não com a aplicação.
  - **Cardinalidade:** `tenant.id` vai nos spans e nos logs, **nunca** nas métricas, que usam o plano.
  - **Amostragem:**
    - Parent-based: um trace só começa em operação de entrada (Server, Consumer, Producer).
    - Descarta a atividade de fundo que dominava o painel: varredura do outbox e do inbox no SQL a cada segundo e sondas de health check do gateway.
    - A fonte do YARP não é assinada: ela só gerava o span das sondas.
  - **Verificado no compose:** um único trace liga gateway → Lançamentos → outbox → RabbitMQ → worker → SQL (imagem em [observabilidade.md](../observabilidade.md)). Teste de integração: o consumo continua o trace da publicação e carrega o `tenant.id`.

## Referências
- [OpenTelemetry](https://opentelemetry.io/)
- [.NET Aspire Dashboard standalone](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone)
- Google — *Site Reliability Engineering* (SLIs, SLOs, error budgets)
