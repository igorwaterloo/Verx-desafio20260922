#!/usr/bin/env sh
# Compila e executa os testes da solução dentro do container do SDK .NET 10 (mesmo ambiente do CI).
# Não exige o SDK .NET instalado. O socket do Docker é repassado para os testes com Testcontainers.
#
# Uso:
#   ./scripts/test.sh
#   ./scripts/test.sh --project tests/Architecture.Tests
set -eu

# No Git Bash (Windows) usa o caminho do Windows e desliga a conversão automática de caminhos:
# caminhos como /tmp/... ou /c/... e opções como "-w /src" não chegam ao Docker como esperado.
if pwd -W >/dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  RAIZ="$(cd "$(dirname "$0")/.." && pwd -W)"
else
  RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
fi

if [ "$#" -eq 0 ]; then
  set -- --solution FluxoCaixa.slnx
fi

exec docker run --rm \
  -v "$RAIZ:/src" \
  -v fluxocaixa-nuget:/root/.nuget/packages \
  -v fluxocaixa-artifacts:/artifacts \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test "$@" --artifacts-path /artifacts
