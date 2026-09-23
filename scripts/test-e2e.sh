#!/usr/bin/env sh
# Executa o smoke E2E (Playwright) da SPA contra a stack do docker compose já em execução:
#   docker compose -f deploy/docker-compose.yml up -d --build
# Roda no container oficial do Playwright com a rede do host (SPA :4200, gateway :8080, Keycloak :8081).
#
# Uso:
#   ./scripts/test-e2e.sh
set -eu

# No Git Bash (Windows) usa o caminho do Windows e desliga a conversão automática de caminhos:
# caminhos como /tmp/... ou /c/... e opções como "-w /src" não chegam ao Docker como esperado.
if pwd -W >/dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  RAIZ="$(cd "$(dirname "$0")/.." && pwd -W)"
else
  RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
fi

exec docker run --rm --network host --ipc host \
  -v "$RAIZ/src/app/fluxo-caixa-web/e2e:/e2e" \
  -v fluxocaixa-e2e-node-modules:/e2e/node_modules \
  -e CI="${CI:-}" \
  -w /e2e \
  mcr.microsoft.com/playwright:v1.63.0-noble \
  sh -c "npm ci --no-audit --no-fund && npx playwright test"
