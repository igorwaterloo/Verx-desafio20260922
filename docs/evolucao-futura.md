# Evolução Futura

> Documento inicial. É consolidado na Fase 10.

| Tema | Evolução |
|---|---|
| **Fechamento de caixa** | Fechar o dia; estornos de dias fechados passam a ser lançados na data corrente. |
| **Reconstrução da projeção** | Rotina para recalcular o `SaldoDiario` a partir dos lançamentos (reconciliação e recuperação). |
| **Saldo acumulado** | Saldo de abertura e de fechamento por dia, além do saldo do dia. |
| **Multi-tenant** | Vários estabelecimentos por comerciante; isolamento por tenant. |
| **Plataforma** | Kubernetes (AKS) com HPA/KEDA, infraestrutura como código, deploy canário. |
| **Mensageria gerenciada** | Avaliar Azure Service Bus ou RabbitMQ gerenciado com quorum queues. |
| **Segurança** | SAST/DAST e varredura de dependências e imagens no pipeline; mTLS entre serviços. |
| **Relatórios** | Exportação (CSV/PDF), gráficos de período e categorias de lançamento. |
