# Fluxo de Caixa — Web App (Angular)

SPA do produto: cadastro da empresa, login OIDC no Keycloak, lançamentos, consolidado e administração do tenant. Decisões em [ADR-0018](../../../docs/adr/0018-frontend-angular-spa.md). O uso da aplicação está descrito no [README da raiz](../../../README.md#aplicação-web).

## Estrutura

```
src/app/
├─ core/
│  ├─ api/        clientes das APIs (via gateway), modelos e conversão de erros (ErroApi)
│  ├─ auth/       AuthService (claims do token) e guards (autenticado, admin)
│  ├─ config/     configuração em tempo de execução (/config.json) e locale pt-BR
│  └─ util/       validadores (CNPJ, senha), datas no fuso de São Paulo, chave de idempotência
├─ layout/        shell das páginas autenticadas
├─ shared/        notificações e diálogo de confirmação
└─ features/
   ├─ publico/      início e cadastro da empresa
   ├─ lancamentos/  registro, listagem e estorno
   ├─ consolidado/  saldo do dia, período e gráfico SVG
   └─ empresa/      plano e usuários (admin)
```

## Comandos

```bash
npm ci
npx ng serve                 # http://localhost:4200 (usa public/config.json)
npx ng test --watch=false    # testes unitários (Vitest)
npx ng build                 # build de produção em dist/
```

Smoke E2E (Playwright, contra a stack do compose): `scripts/test-e2e.ps1` ou `scripts/test-e2e.sh` na raiz do repositório.

## Imagem Docker

`Dockerfile` (Node → nginx sem privilégios, porta 8080). Variáveis de ambiente:

| Variável | Padrão |
|---|---|
| `API_URL` | `http://localhost:8080` |
| `OIDC_AUTHORITY` | `http://localhost:8081/realms/fluxo-caixa` |
| `OIDC_CLIENT_ID` | `fluxo-caixa-web` |
