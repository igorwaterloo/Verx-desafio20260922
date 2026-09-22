<#
.SYNOPSIS
    Compila e executa os testes da solução dentro do container do SDK .NET 10 (mesmo ambiente do CI).

.DESCRIPTION
    Não exige o SDK .NET instalado na máquina. Útil também quando políticas do Windows
    (ex.: Smart App Control) bloqueiam DLLs recém-compiladas no host.
    O socket do Docker é repassado para que testes de integração com Testcontainers funcionem.

.EXAMPLE
    ./scripts/test.ps1
    ./scripts/test.ps1 --project tests/Architecture.Tests
#>
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Argumentos
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

if (-not $Argumentos) {
    $Argumentos = @('--solution', 'FluxoCaixa.slnx')
}

docker run --rm `
    -v "${raiz}:/src" `
    -v fluxocaixa-nuget:/root/.nuget/packages `
    -v fluxocaixa-artifacts:/artifacts `
    -v //var/run/docker.sock:/var/run/docker.sock `
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
    -w /src `
    mcr.microsoft.com/dotnet/sdk:10.0 `
    dotnet test @Argumentos --artifacts-path /artifacts

exit $LASTEXITCODE
