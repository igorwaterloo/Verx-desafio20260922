# ADR-0017: Contexto Plataforma — onboarding de tenants, planos e quotas

- **Status:** Aceita — atualizada em 2026-09-22 (ver Atualizações)
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0002](0002-microsservicos-por-bounded-context.md), [ADR-0005](0005-outbox-e-consumidor-idempotente.md), [ADR-0008](0008-autenticacao-keycloak-oidc-jwt.md), [ADR-0009](0009-api-gateway-yarp.md), [ADR-0015](0015-multi-tenancy-banco-compartilhado.md)

## Contexto e problema

Como SaaS, o produto precisa:
1. **cadastrar novas empresas (tenants) em autoatendimento**, criando a organização e o usuário administrador no Keycloak;
2. oferecer **planos** com **limites de uso** (quotas) e taxas de requisição diferentes;
3. permitir ao **admin do tenant** gerenciar os usuários da empresa.

Essas responsabilidades não pertencem a Lançamentos nem ao Consolidado. Também não podem criar **dependência síncrona** que ameace o RNF-01 (Lançamentos disponível).

## Requisitos da decisão

- **RNF-01 preservado:** aplicar a quota em Lançamentos **sem chamar** outro serviço em tempo de requisição.
- Onboarding **consistente**: nunca um tenant "meio criado" (banco sem Keycloak, ou o contrário).
- O rate limit por plano é aplicado no gateway ([ADR-0009](0009-api-gateway-yarp.md)).
- O catálogo de planos tem **uma fonte da verdade**.

## Opções consideradas

**Onde fica a gestão de tenants:**
1. Dentro de Lançamentos
2. **Novo bounded context "Plataforma" (serviço `Tenants.Api`)**
3. Somente no Keycloak (sem serviço próprio)

**Como Lançamentos conhece o plano/quota do tenant:**
1. Chamada síncrona ao `Tenants.Api` a cada lançamento
2. Somente a claim `plano` do token, com o catálogo duplicado em configuração
3. **Projeção local alimentada por eventos** (`TenantProvisionado`, `PlanoDoTenantAlterado`)

**Consistência do onboarding (banco + Keycloak):**
1. Chamadas em sequência, sem compensação
2. **Estado `Pendente` + provisionamento com retry e compensação** (saga simples orquestrada)

## Decisão

### 1. Novo contexto **Plataforma** — serviço `Tenants.Api` + `TenantsDb`

| Recurso | Endpoint | Acesso |
|---|---|---|
| Catálogo de planos | `GET /api/v1/planos` | Público |
| Onboarding | `POST /api/v1/tenants` (empresa, CNPJ, plano, admin: nome/e-mail/senha) | Público, com **rate limit por IP** rigoroso |
| Tenant atual | `GET /api/v1/tenants/atual` | Autenticado |
| Trocar plano | `PUT /api/v1/tenants/atual/plano` | `admin` |
| Usuários | `GET/POST /api/v1/tenants/atual/usuarios` | `admin` (respeita o limite de usuários do plano) |

### 2. Planos (catálogo inicial)

| Plano | Lançamentos/mês | Usuários | Rate limit (por tenant, no gateway) | Preço (ilustrativo) |
|---|---|---|---|---|
| **Free** | 1.000 | 2 | 20 req/s | R$ 0 |
| **Pro** | 50.000 | 20 | 100 req/s | R$ 99/mês |

> O teste de carga do RNF-02 (50 req/s no Consolidado) roda com tenants no plano **Pro**. O limite do Free é **proteção contra noisy neighbor**, não perda de requisição legítima.

### 3. Quota em Lançamentos via projeção local (sem chamada síncrona)

- O `Tenants.Api` publica **`TenantProvisionado`** e **`PlanoDoTenantAlterado`** (com os limites do plano) via **outbox** ([ADR-0005](0005-outbox-e-consumidor-idempotente.md)).
- O Lancamentos.Api consome esses eventos e mantém a tabela **`TenantsPlanos`** (`TenantId`, `LimiteLancamentosMes`).
- Ao registrar, compara a contagem de lançamentos do mês (índice `(TenantId, CriadoEm)`) com o limite. Se exceder, responde **`422`** com `ProblemDetails` (`type: quota-excedida`). **Estornos não consomem quota.**
- **Tenant ainda sem projeção** (evento em trânsito): aplica os **limites do Free** (conservador) e registra um aviso. A janela dura segundos.

### 4. Plano no token (para o gateway)

- O Keycloak emite as claims **`tenant_id`** e **`plano`** (atributos da organização via protocol mapper).
- O gateway aplica o **rate limit por tenant** conforme a claim `plano`, sem consultar serviço nenhum.
- Uma troca de plano vale no gateway a partir do **próximo refresh do token** (≤ 5 min). É aceitável.

### 5. Onboarding consistente (saga orquestrada simples)

1. Valida (CNPJ único, plano existente) e grava o `Tenant` com status **`Pendente`**.
2. Cria a **Organization**, o usuário admin (role `admin`) e o vínculo no Keycloak (Admin API, com Polly retry).
3. Sucesso: status **`Ativo`** + `TenantProvisionado` no outbox (mesma transação). Responde `201`.
4. Falha transitória: responde **`202 Accepted`** (status `Pendente`). Um job em background repete o passo 2 com backoff.
5. Falha definitiva (ex.: e-mail já existe no Keycloak): **compensa**, removendo a organização criada parcialmente. O status vira `Falhou`, com o motivo.

A senha do admin só é **repassada ao Keycloak**; ela nunca é persistida nem logada.

**Por que não dentro de Lançamentos:** misturaria o core domain com a gestão da plataforma e faria Lançamentos depender do Keycloak Admin API.
**Por que não só no Keycloak:** planos, quotas, CNPJ e status de provisionamento são **regras de negócio do SaaS**, não de identidade.
**Por que não uma chamada síncrona para a quota:** criaria dependência em tempo de requisição, o que contraria o RNF-01.

## Consequências

### Positivas
- Lançamentos continua sem dependência síncrona de nenhum outro serviço.
- Uma única fonte da verdade para planos (Tenants), propagada por eventos.
- Onboarding em autoatendimento sem tenant inconsistente.
- Base pronta para billing (o contexto Plataforma é o lugar natural).

### Negativas / trade-offs aceitos
- Mais um serviço e mais um banco.
- **Consistência eventual** do plano: por alguns segundos (Lançamentos) ou até 5 min (gateway), um tenant pode operar com os limites anteriores.
- O Tenants.Api depende do Keycloak para provisionar. Se o Keycloak cair, novos cadastros ficam `Pendente` até ele voltar.

### Mitigações
- Retry com backoff + job de reconciliação de tenants `Pendente`.
- Janelas de inconsistência documentadas e aceitáveis para o negócio (quotas mensais, não transacionais).
- Métricas de onboarding (tempo, falhas, pendentes).

## Atualizações

- **2026-09-22 — Implementação (Fase 5.5):**
  - **Falha transitória → 503 retomável, em vez de 202 + job que conclui.** Para o job concluir o cadastro sozinho, ele precisaria da senha do admin, e isso exigiria persisti-la (mesmo cifrada), contrariando o RP-03. A decisão foi: com o Keycloak indisponível (após timeout de 3 s e 2 retries), a API responde **503** e o tenant fica `Pendente`; **reenviar o mesmo cadastro retoma** o provisionamento. Todas as operações no Keycloak são idempotentes: organização e usuário são buscados pelo atributo `tenant_id` antes de criar, e um usuário existente do mesmo tenant é reaproveitado.
  - **Job de expiração:** a cada 10 min, tenants `Pendente` há mais de 24 h são compensados (remoção dos usuários e da organização) e marcados `Falhou`.
  - **Compensação completa:** remove os usuários com o `tenant_id` do tenant e a organização, cobrindo falhas depois da criação do usuário.
  - **Claim `plano`:** vem do atributo do usuário (não da Organization). A troca de plano atualiza a organização e **cada membro** (no máximo 20), e só então grava o plano e publica `PlanoDoTenantAlterado`; se o Keycloak falhar, nada muda (503).
  - **Tenants de demonstração:** na inicialização local, os tenants do realm são registrados no TenantsDb e `TenantProvisionado` é publicado, para que o Lançamentos aplique as quotas corretas (Padaria Free, Mercado Pro).
  - Validado em integração com Keycloak real (Testcontainers) e ponta a ponta no compose: onboarding → login → lançamento → consolidado → upgrade para Pro refletido no token e na quota.

## Referências
- Chris Richardson — *Saga pattern* (orquestração com compensação)
- Keycloak Admin REST API — Organizations, Users
- [Fluxos de onboarding e quota](../architecture/fluxos.md)
