# ADR-0018: Frontend em Angular (SPA standalone, OIDC com PKCE e configuração em tempo de execução)

- **Status:** Aceita
- **Data:** 2026-09-23
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0008](0008-autenticacao-keycloak-oidc-jwt.md), [ADR-0009](0009-api-gateway-yarp.md), [ADR-0012](0012-estrategia-de-testes.md), [ADR-0017](0017-contexto-plataforma-onboarding-planos.md)

## Contexto e problema

O comerciante precisa de uma interface para cadastrar a empresa, registrar lançamentos, consultar o saldo consolidado e (se for admin) gerenciar o plano e os usuários. A stack do frontend já foi definida como **Angular**. Este ADR registra **como** a SPA é construída, autenticada, configurada e publicada.

## Requisitos da decisão

- Login no Keycloak sem guardar senha nem *client secret* no navegador.
- O token só pode ser enviado às APIs do produto, nunca a outras origens.
- A mesma imagem deve rodar em qualquer ambiente (local, homologação, produção), sem novo build.
- Mostrar a indisponibilidade do Consolidado sem afetar os lançamentos (RNF-01).
- Reenvios de formulário não podem duplicar lançamentos (RN-08).
- Content-Security-Policy estrita e cabeçalhos de segurança.

## Opções consideradas

| Tema | Opções |
|---|---|
| Estrutura | **Componentes standalone + signals, sem Zone.js** · NgModules + Zone.js |
| Autenticação | **Authorization Code + PKCE (`angular-auth-oidc-client`)** · BFF (sessão no servidor) · Implicit flow |
| Configuração | **`/config.json` carregado antes do bootstrap** · `environment.ts` por build |
| UI | **Angular Material 3** · biblioteca de terceiros · CSS próprio |
| Gráfico | **SVG próprio** · Chart.js / ngx-charts |
| Publicação | **nginx sem privilégios** · Node servindo arquivos |

## Decisão

1. **Standalone, signals e zoneless** (padrão do Angular 22). Estado local das páginas em `signal`/`computed`, rotas com *lazy loading* por página e guards funcionais (`autenticadoGuard`, `adminGuard`).
2. **OIDC Authorization Code + PKCE** com o client público `fluxo-caixa-web`. A renovação usa *refresh token* (sem iframe). O interceptor do `angular-auth-oidc-client` só anexa o Bearer às URLs em `secureRoutes` (o gateway). O gateway continua sendo a barreira de segurança (ADR-0009). Após a troca de plano, a SPA força a renovação da sessão para receber a nova claim `plano`.
3. **Configuração em tempo de execução:** `main.ts` busca `/config.json` (`apiUrl`, `authority`, `clientId`) antes do `bootstrapApplication`. No container, o script de inicialização gera esse arquivo a partir de `API_URL`, `OIDC_AUTHORITY` e `OIDC_CLIENT_ID`.
4. **Tratamento de erros centralizado:** um interceptor converte o `ProblemDetails` em `ErroApi`, com mensagem por campo, `codigo` estável, `Retry-After` do 429 e o indicador `indisponivel` (0/502/503/504). A página do Consolidado usa esse indicador para mostrar um banner que lembra que os lançamentos continuam sendo aceitos.
5. **Idempotência no cliente:** cada preenchimento do formulário de lançamento tem uma `Idempotency-Key` (`crypto.randomUUID`). A chave é reutilizada no reenvio após uma falha e renovada quando os dados mudam ou após o sucesso.
6. **Angular Material 3** com os ícones Material Symbols servidos pela própria aplicação (sem CDN). **Gráfico em SVG próprio:** um componente pequeno, sem dependência, compatível com a CSP.
7. **nginx sem privilégios** (`nginxinc/nginx-unprivileged`, porta 8080), com:
   - fallback de rotas para `index.html`;
   - cache longo só para arquivos com hash;
   - cabeçalhos `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` e `COOP`;
   - **CSP** `script-src 'self'`, com `connect-src` limitado ao gateway e ao Keycloak (origens derivadas das variáveis de ambiente).

   O *critical CSS inline* do build foi desativado: ele usa um `onload=` inline, que a CSP bloquearia.

## Consequências

### Positivas
- Nenhum segredo no navegador. O token fica restrito ao gateway, e a CSP reduz o impacto de uma eventual injeção de script.
- Uma única imagem para todos os ambientes; trocar URL do gateway ou do IdP é só mudar variável.
- Sem Zone.js e com *lazy loading*: menos JavaScript inicial e detecção de mudanças previsível.
- Os testes unitários (Vitest) e o smoke E2E (Playwright contra o compose) cobrem os fluxos críticos: idempotência, 503 do cadastro, banner de indisponibilidade, guards por papel, login OIDC real e convergência do saldo.

### Negativas / trade-offs aceitos
- Tokens ficam no `sessionStorage` do navegador (padrão da biblioteca). Um BFF eliminaria isso, mas acrescentaria um serviço com sessão e estado. A CSP estrita e o token de vida curta mitigam o risco.
- `style-src 'unsafe-inline'` é necessário para os estilos de componentes do Angular/Material.
- O bundle inicial (~180 kB transferidos) é dominado por Material e OIDC; o *budget* foi ajustado para 1 MB bruto.

### Evolução
- BFF (tokens só no servidor) se o perfil de risco exigir.
- Nonce de CSP para estilos (`ngCspNonce`) e remoção do `'unsafe-inline'`.
- Internacionalização (`@angular/localize`) se houver clientes fora do Brasil.
