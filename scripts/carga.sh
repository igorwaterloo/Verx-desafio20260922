#!/usr/bin/env sh
# Executa um teste de carga k6 (tests/stress/k6) contra a stack do docker compose já em execução.
# Roda no container grafana/k6 com a rede do host (gateway :8080, Keycloak :8081).
# Resumos em tests/stress/k6/resultados/.
#
# Uso:
#   ./scripts/carga.sh consolidado-50rps
#   ./scripts/carga.sh lancamentos-carga -e TAXA_MAXIMA=80
#   ./scripts/carga.sh noisy-neighbor -e DURACAO=1m
set -eu

# No Git Bash (Windows) usa o caminho do Windows e desliga a conversão automática de caminhos:
# caminhos como /tmp/... ou /c/... e opções como "-w /src" não chegam ao Docker como esperado.
if pwd -W >/dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  RAIZ="$(cd "$(dirname "$0")/.." && pwd -W)"
else
  RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
fi
TESTE="${1:?informe o teste: consolidado-50rps | lancamentos-carga | noisy-neighbor | caos-lancamentos}"
shift

mkdir -p "$RAIZ/tests/stress/k6/resultados"
exec docker run --rm --network host \
  -v "$RAIZ/tests/stress/k6:/k6" -w /k6 \
  grafana/k6:2.3.0 run "$@" "$TESTE.js"
