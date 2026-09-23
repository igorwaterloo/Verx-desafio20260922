# Evolução Futura

O que eu faria a seguir, em ordem de prioridade, e por quê. Os itens vêm de três fontes:
- os riscos residuais da [análise de segurança](seguranca.md#5-riscos-residuais-e-evolução);
- os limites encontrados nos [testes de carga](testes.md#limites-conhecidos);
- as necessidades de negócio de um produto de fluxo de caixa.

## 1. Antes de produção

Sem estes itens o sistema não deveria atender clientes reais.

| Tema | Evolução | Por quê |
|---|---|---|
| **TLS e rede** | TLS na borda (Azure Front Door com WAF); somente o gateway exposto; serviços em rede privada; mTLS entre serviços (service mesh) | Hoje o compose usa HTTP e expõe portas de depuração ([segurança](seguranca.md)) |
| **Segredos e identidades** | Azure Key Vault + Managed Identity; um login de banco por serviço com permissões mínimas; remover o client `fluxo-caixa-testes` do realm de produção | O `.env` local e o login `sa` são simplificações de desenvolvimento |
| **Infraestrutura como código e CD** | Bicep/Terraform para o ambiente ([visão de produção](architecture/deployment.md)); pipeline de CD publicando as imagens que o CI já constrói, com deploy canário | O CI valida e constrói as imagens, mas não publica |
| **Segurança no pipeline** | SAST (CodeQL), varredura de dependências e de imagens (Trivy), DAST (OWASP ZAP) contra homologação | O Dependabot atualiza as versões, mas não bloqueia vulnerabilidades |
| **MFA** | Obrigatório para o papel `admin` (política do Keycloak) | O admin estorna lançamentos e troca o plano |
| **Carga em homologação** | Os mesmos scripts k6 contra um ambiente dedicado, com o gerador de carga em outra máquina, como critério de release | Os números atuais foram medidos numa única máquina ([testes](testes.md#resultados-dos-testes-de-carga-fase-8)) |

## 2. Escala e operação

| Tema | Evolução | Por quê |
|---|---|---|
| **Worker do Consolidado em várias instâncias** | *Consistent hash exchange* no RabbitMQ (uma fila por partição tenant + dia) ou sessões no Azure Service Bus | O particionamento atual vale dentro de uma instância ([ADR-0010](adr/0010-separacao-consolidado-api-worker.md)); com mais instâncias, a mesma linha de saldo volta a ser disputada (o retry mantém a correção, mas com atraso) |
| **Kubernetes** | AKS com HPA para as APIs (CPU e requisições) e KEDA para o worker (profundidade da fila) | Escala automática por métrica, em vez de réplicas fixas |
| **Mensageria gerenciada** | Azure Service Bus ou RabbitMQ gerenciado com *quorum queues* | Menos operação e alta disponibilidade do broker |
| **Observabilidade em produção** | Coletor OpenTelemetry com *tail sampling* (100% dos erros, ~10% do resto); alertas da [lista proposta](observabilidade.md#alertas-propostos-produção); SLOs com *error budget* | Hoje todo trace iniciado é gravado e os alertas estão só documentados |
| **Réplica de leitura** | Consolidado.Api lendo de uma réplica do `ConsolidadoDb` | Leitura e escrita da projeção ainda dividem o mesmo banco |
| **Reconstrução da projeção** | Rotina para recalcular o `SaldoDiario` a partir dos lançamentos (reconciliação periódica e recuperação) | Rede de segurança para a projeção, que hoje depende só da entrega dos eventos |

## 3. Segurança e dados

| Tema | Evolução | Por quê |
|---|---|---|
| **BFF para a SPA** | Tokens mantidos no servidor, com cookie de sessão | Elimina tokens no `sessionStorage` ([ADR-0018](adr/0018-frontend-angular-spa.md)) |
| **CSP sem `unsafe-inline`** | Nonce de CSP para estilos (`ngCspNonce`) | Endurece a CSP da SPA |
| **Row-Level Security** | RLS do SQL Server com `SESSION_CONTEXT('TenantId')` | Camada extra de isolamento para consultas fora do EF Core (ex.: relatórios) |
| **Limite por usuário** | Rate limit por `sub` dentro do tenant | Hoje o limite é por tenant e por IP |
| **LGPD** | Exportação e exclusão de dados por tenant, retenção configurável, registro de consentimento | Obrigação legal num SaaS com dados de clientes |
| **Tenancy híbrida** | Tenants enterprise em banco dedicado, com catálogo `TenantId → connection string` resolvido pelo `ITenantContext` | Requisito comum de clientes grandes ([ADR-0015](adr/0015-multi-tenancy-banco-compartilhado.md)) |

## 4. Produto

| Tema | Evolução |
|---|---|
| **Fechamento de caixa** | Fechar o dia; estornos de dias fechados passam a ser lançados na data corrente ([ADR-0014](adr/0014-lancamentos-imutaveis-com-estorno.md)) |
| **Saldo acumulado** | Saldo de abertura e de fechamento por dia, além do saldo do dia |
| **Categorias e relatórios** | Categorias de lançamento, exportação (CSV/PDF), gráficos por categoria |
| **Múltiplos estabelecimentos** | Várias lojas ou caixas por tenant, com consolidado por estabelecimento e total |
| **Billing** | Cobrança recorrente dos planos, faturas, período de teste, upgrade/downgrade com pró-rata (no contexto Plataforma) |
| **Ciclo de vida do tenant** | Suspensão por inadimplência (evento `TenantSuspenso` bloqueando escrita), reativação e encerramento |
| **Quotas rígidas** | Contadores atômicos no Redis, se o negócio exigir limite exato em vez da quota suave atual |

## Já entregue além do plano inicial

Itens que surgiram durante o desenvolvimento e já foram feitos:
- **Observabilidade ponta a ponta:** OpenTelemetry, métricas de negócio e amostragem.
- **CI completo:** GitHub Actions e Dependabot.
- **Testes de carga e caos** automatizados, que encontraram e corrigiram dois defeitos: disputa na mesma linha de saldo e failover lento com uma réplica fora da rede.
- **Frontend completo**, com smoke E2E.
