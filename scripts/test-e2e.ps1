# Executa o smoke E2E (Playwright) da SPA contra a stack do docker compose já em execução:
#   docker compose -f deploy/docker-compose.yml up -d --build
# Roda no container oficial do Playwright com a rede do host (SPA :4200, gateway :8080, Keycloak :8081).
$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')

docker run --rm --network host --ipc host `
  -v "${raiz}/src/app/fluxo-caixa-web/e2e:/e2e" `
  -v fluxocaixa-e2e-node-modules:/e2e/node_modules `
  -e "CI=$env:CI" `
  -w /e2e `
  mcr.microsoft.com/playwright:v1.63.0-noble `
  sh -c "npm ci --no-audit --no-fund && npx playwright test"
exit $LASTEXITCODE
