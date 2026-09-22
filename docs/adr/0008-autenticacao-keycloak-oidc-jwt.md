# ADR-0008: Autenticação com Keycloak (OIDC) e autorização por JWT

- **Status:** Aceita — complementada por [ADR-0015](0015-multi-tenancy-banco-compartilhado.md), [ADR-0017](0017-contexto-plataforma-onboarding-planos.md)
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0009](0009-api-gateway-yarp.md), [requisitos não funcionais — segurança](../requisitos-nao-funcionais.md#7-segurança-requisitos)

## Contexto e problema

O desafio pede proteção de dados e sistemas com **autenticação, autorização e criptografia**. Dados financeiros de um comerciante não podem ser acessados por terceiros, e cada comerciante só pode ver os próprios dados (RN-07).

## Requisitos da decisão

- Padrão aberto e amplamente adotado (OAuth 2.1 / OpenID Connect).
- SPA sem armazenar segredo de cliente (fluxo seguro para aplicações públicas).
- Validação de token **stateless** nos serviços (escala horizontal).
- Execução local via Docker, sem dependência de nuvem.
- Evolução para MFA, SSO e federação sem reescrever código.

## Opções consideradas

1. **Keycloak** (IdP open source, OIDC)
2. ASP.NET Core Identity + emissão de JWT própria
3. Endpoint próprio simples emitindo JWT
4. Microsoft Entra ID / Auth0 (IdP gerenciado)

## Decisão

**Opção escolhida:** **Keycloak** como Identity Provider, com **OpenID Connect**.

| Aspecto | Definição |
|---|---|
| Realm | `fluxo-caixa` (importado automaticamente no `docker compose up`) |
| Cliente SPA | `fluxo-caixa-web`: **público**, Authorization Code + **PKCE** (sem client secret no browser) |
| Audiência da API | `fluxo-caixa-api` |
| Role | `comerciante` (realm role), exigida nas APIs |
| Token | JWT RS256, curta duração (5 min) + refresh token |
| Validação | Assinatura via **JWKS**, `iss`, `aud`, `exp`, no **gateway e em cada serviço** (defesa em profundidade) |
| Identidade do dado | `ComercianteId` = claim `sub`. **Nunca aceito do corpo da requisição** |
| Usuário de teste | `comerciante` / senha documentada no README (apenas ambiente local) |

**Por que não ASP.NET Identity / JWT próprio:** implementar login, armazenamento de senhas, rotação de chaves, refresh token, MFA e bloqueio por tentativas é código **sensível e fora do domínio do negócio**. Um IdP dedicado reduz a superfície de ataque e segue padrões auditados.

**Por que não Entra ID/Auth0:** exigiriam conta em nuvem para rodar o projeto localmente. Como a integração é **OIDC padrão**, trocar o Keycloak por Entra ID em produção é **configuração** (authority/audience), não código.

## Consequências

### Positivas
- Segurança baseada em padrões (OIDC, PKCE, JWT assinado).
- APIs stateless: qualquer réplica valida o token sozinha.
- MFA, políticas de senha, SSO e federação disponíveis sem código.
- Troca de IdP é configuração.

### Negativas / trade-offs aceitos
- Mais um container (Keycloak consome ~500 MB de RAM).
- O Keycloak vira dependência para **novos logins**. Tokens já emitidos continuam válidos, e as APIs dependem só das chaves JWKS em cache.
- Configuração inicial do realm mais trabalhosa.

### Mitigações
- Realm exportado em JSON e importado no start (reprodutível).
- JWKS em cache nos serviços (sem chamada ao IdP a cada requisição).
- Em produção: Keycloak em cluster ou IdP gerenciado.

## Atualizações

- **2026-09-22 — SaaS multi-tenant:** a decisão por Keycloak/OIDC se mantém, com os ajustes abaixo.

| Aspecto | Antes | Agora |
|---|---|---|
| Tenant | — | Cada empresa é uma **Organization** do Keycloak |
| Identidade do dado | `ComercianteId` = `sub` | **`TenantId`** = claim `tenant_id`; `sub` identifica o **usuário** (auditoria: `CriadoPor`) |
| Plano | — | Claim `plano` (usada pelo gateway no rate limit) |
| Roles | `comerciante` | **`admin`** (gerencia usuários/plano, estorna) e **`operador`** (lança e consulta) |
| Usuários de teste | `comerciante` | Tenants de demonstração com um usuário `admin` e um `operador` cada (documentados no README) |
| Provisionamento | Manual (realm importado) | Autoatendimento via `Tenants.Api` + Keycloak Admin API (conta de serviço com permissões mínimas) |

## Referências
- RFC 6749 (OAuth 2.0), RFC 7636 (PKCE), OpenID Connect Core 1.0
- OAuth 2.0 for Browser-Based Apps (IETF draft) — recomenda Authorization Code + PKCE
