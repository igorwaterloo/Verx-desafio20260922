# C4 — Nível 1: Diagrama de Contexto

> Fonte formal: [`workspace.dsl`](workspace.dsl), visão `C1-Contexto`. Este diagrama em Mermaid é derivado dela para renderizar direto no GitHub.

Mostra **quem usa** o sistema e **com quais sistemas externos** ele se relaciona, sem detalhes técnicos internos.

```mermaid
flowchart LR
    comerciante["👤 <b>Comerciante</b><br/><i>[Pessoa]</i><br/>Registra créditos e débitos<br/>e consulta o saldo diário"]

    fluxo["<b>Fluxo de Caixa</b><br/><i>[Sistema de Software]</i><br/>Registra lançamentos e fornece<br/>o saldo diário consolidado"]

    keycloak["<b>Keycloak</b><br/><i>[Sistema Externo]</i><br/>Provedor de identidade OIDC.<br/>Autentica e emite tokens JWT"]

    comerciante -- "Registra lançamentos e<br/>consulta consolidado<br/><i>[HTTPS]</i>" --> fluxo
    comerciante -- "Autentica-se<br/><i>[HTTPS / OIDC]</i>" --> keycloak
    fluxo -- "Valida tokens (JWKS)<br/><i>[HTTPS]</i>" --> keycloak

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef system fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999999,stroke:#6b6b6b,color:#fff
    class comerciante person
    class fluxo system
    class keycloak external
```

## Elementos

| Elemento | Tipo | Responsabilidade |
|---|---|---|
| **Comerciante** | Pessoa | Dono do caixa. Registra lançamentos (créditos/débitos), estorna lançamentos incorretos e consulta o saldo diário consolidado. |
| **Fluxo de Caixa** | Sistema (escopo do desafio) | Mantém os lançamentos (fonte da verdade) e a projeção de saldos diários. |
| **Keycloak** | Sistema externo | Identidade e acesso: login OIDC (Authorization Code + PKCE), emissão de JWT com a role `comerciante`, publicação das chaves (JWKS) para validação. |

## Decisões refletidas neste nível

- **A identidade é delegada a um IdP padrão de mercado** em vez de autenticação própria: menos superfície de ataque e suporte a MFA, SSO e federação sem código novo. Ver ADR de autenticação em [`docs/adr`](../adr).
- **O escopo é um único comerciante por usuário**, mas os dados já são particionados por `ComercianteId`, o que prepara o sistema para multi-tenant.
