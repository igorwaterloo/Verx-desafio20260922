# Executa um teste de carga k6 (tests/stress/k6) contra a stack do docker compose já em execução.
# Uso: ./scripts/carga.ps1 consolidado-50rps [-e DURACAO=1m ...]
param(
  [Parameter(Mandatory = $true)][string]$Teste,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Argumentos
)
$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force (Join-Path $raiz 'tests/stress/k6/resultados') | Out-Null

docker run --rm --network host -v "${raiz}/tests/stress/k6:/k6" -w /k6 grafana/k6:2.3.0 run @Argumentos "$Teste.js"
exit $LASTEXITCODE
