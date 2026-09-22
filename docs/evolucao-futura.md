# Evolução Futura

> Documento inicial. É consolidado na Fase 10.

| Tema | Evolução |
|---|---|
| **Fechamento de caixa** | Fechar o dia; estornos de dias fechados passam a ser lançados na data corrente. |
| **Reconstrução da projeção** | Rotina para recalcular o `SaldoDiario` a partir dos lançamentos (reconciliação e recuperação). |
| **Saldo acumulado** | Saldo de abertura e de fechamento por dia, além do saldo do dia. |
| **Billing** | Cobrança recorrente dos planos (gateway de pagamento), faturas, período de teste, upgrade/downgrade com pró-rata. O contexto Plataforma já é o lugar natural. |
| **Ciclo de vida do tenant** | Suspensão por inadimplência (evento `TenantSuspenso` bloqueando escrita), reativação e encerramento. |
| **Tenancy híbrida** | Tenants enterprise em banco dedicado, com um catálogo `TenantId → connection string` resolvido pelo `ITenantContext`. |
| **Row-Level Security** | RLS do SQL Server com `SESSION_CONTEXT('TenantId')` como camada extra de isolamento. |
| **LGPD** | Exportação e exclusão de dados por tenant, retenção configurável, registro de consentimento. |
| **Múltiplos estabelecimentos** | Várias lojas/caixas por tenant, com consolidado por estabelecimento e total. |
| **Quotas rígidas** | Contadores atômicos (Redis) se o negócio exigir limite exato em vez de quota suave. |
| **Plataforma** | Kubernetes (AKS) com HPA/KEDA, infraestrutura como código, deploy canário. |
| **Mensageria gerenciada** | Avaliar Azure Service Bus ou RabbitMQ gerenciado com quorum queues. |
| **Segurança** | SAST/DAST e varredura de dependências e imagens no pipeline; mTLS entre serviços; MFA obrigatório para `admin`. |
| **Relatórios** | Exportação (CSV/PDF), gráficos de período e categorias de lançamento. |
