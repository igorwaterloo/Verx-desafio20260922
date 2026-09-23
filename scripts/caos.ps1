# Testes de resiliência (caos). Ver scripts/caos.sh para a descrição dos modos.
# Uso: ./scripts/caos.ps1 consolidado | broker | replica
param([Parameter(Mandatory = $true)][ValidateSet('consolidado', 'broker', 'replica')][string]$Modo)
$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')
$compose = Join-Path $raiz 'deploy/docker-compose.yml'
$k6 = 'fluxo-caixa-k6-caos'

switch ($Modo) {
  'consolidado' { $teste = 'caos-lancamentos'; $alvo = @('consolidado-api-1', 'consolidado-api-2', 'consolidado-worker'); $envs = @('-e', 'DURACAO=3m') }
  'broker'      { $teste = 'caos-lancamentos'; $alvo = @('rabbitmq'); $envs = @('-e', 'DURACAO=3m') }
  'replica'     { $teste = 'consolidado-50rps'; $alvo = @('consolidado-api-1'); $envs = @('-e', 'DURACAO=3m', '-e', 'DURACAO_PICO=1s') }
}

New-Item -ItemType Directory -Force (Join-Path $raiz 'tests/stress/k6/resultados') | Out-Null
try { docker rm -f $k6 2>$null | Out-Null } catch {}
docker run -d --name $k6 --network host -v "${raiz}/tests/stress/k6:/k6" -w /k6 grafana/k6:2.3.0 run @envs "$teste.js" | Out-Null

Write-Host "[caos] carga iniciada ($teste); derrubando '$alvo' em 40 s"
Start-Sleep -Seconds 40
docker compose -f $compose stop @alvo
Write-Host "[caos] '$alvo' parado; religando em 60 s"
Start-Sleep -Seconds 60
docker compose -f $compose start @alvo
Write-Host "[caos] '$alvo' religado; aguardando o fim da carga e a verificação"

$codigo = docker wait $k6
docker logs $k6 2>&1 | Select-Object -Last 80
docker rm $k6 | Out-Null
exit [int]$codigo
