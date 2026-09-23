#!/usr/bin/env sh
# Testes de resiliência (caos): gera carga com k6 e derruba componentes no meio da execução.
#
#   consolidado  Consolidado (réplicas da API + worker) fora por 60 s durante a escrita de lançamentos.
#                Esperado: 0% de erro nos lançamentos e saldo convergindo após religar (RNF-01).
#   broker       RabbitMQ fora por 60 s durante a escrita. Esperado: 0% de erro (outbox) e convergência.
#   replica      Uma réplica do Consolidado.Api fora por 60 s sob 50 req/s. Esperado: < 5% de erro.
#
# Uso: ./scripts/caos.sh consolidado | broker | replica
set -eu

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
MODO="${1:?informe o modo: consolidado | broker | replica}"
K6="fluxo-caixa-k6-caos"

case "$MODO" in
  consolidado) TESTE=caos-lancamentos; ALVO="consolidado-api-1 consolidado-api-2 consolidado-worker"; ENV="-e DURACAO=3m" ;;
  broker)      TESTE=caos-lancamentos; ALVO="rabbitmq"; ENV="-e DURACAO=3m" ;;
  replica)     TESTE=consolidado-50rps; ALVO="consolidado-api-1"; ENV="-e DURACAO=3m -e DURACAO_PICO=1s" ;;
  *) echo "modo inválido: $MODO" >&2; exit 2 ;;
esac

# docker compose a partir de deploy/ (o caminho do arquivo não precisa ser convertido no Git Bash).
compose() { (cd "$RAIZ/deploy" && docker compose "$@"); }

mkdir -p "$RAIZ/tests/stress/k6/resultados"
docker rm -f "$K6" >/dev/null 2>&1 || true
# shellcheck disable=SC2086
docker run -d --name "$K6" --network host -v "$RAIZ/tests/stress/k6:/k6" -w /k6 \
  grafana/k6:2.3.0 run -q $ENV "$TESTE.js" >/dev/null

echo "[caos] carga iniciada ($TESTE); derrubando '$ALVO' em 40 s"
sleep 40
# shellcheck disable=SC2086
compose stop $ALVO
echo "[caos] $(date +%T) '$ALVO' parado; religando em 60 s"
sleep 60
# shellcheck disable=SC2086
compose start $ALVO
echo "[caos] $(date +%T) '$ALVO' religado; aguardando o fim da carga e a verificação"

CODIGO=$(docker wait "$K6")
docker logs "$K6" 2>&1 | tail -80
docker rm "$K6" >/dev/null
cp "$RAIZ/tests/stress/k6/resultados/$TESTE.txt" "$RAIZ/tests/stress/k6/resultados/caos-$MODO.txt" 2>/dev/null || true
exit "$CODIGO"
