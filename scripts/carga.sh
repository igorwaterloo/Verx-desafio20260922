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

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
TESTE="${1:?informe o teste: consolidado-50rps | lancamentos-carga | noisy-neighbor | caos-lancamentos}"
shift

mkdir -p "$RAIZ/tests/stress/k6/resultados"
exec docker run --rm --network host \
  -v "$RAIZ/tests/stress/k6:/k6" -w /k6 \
  grafana/k6:2.3.0 run "$@" "$TESTE.js"
