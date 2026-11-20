# Roda os testes EditMode do projeto no Unity em batchmode e mostra o resumo.
# Uso:  powershell -ExecutionPolicy Bypass -File Tools/run-unity-tests.ps1 [-Unity "C:\...\Unity.exe"]
# Observação: o Unity não pode estar aberto com este projeto ao mesmo tempo.
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe",
    [string]$Platform = "EditMode"
)

$project = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $env:TEMP "fodinha-tests"
New-Item -ItemType Directory -Force $outDir | Out-Null
$results = Join-Path $outDir "$Platform-results.xml"
$log = Join-Path $outDir "$Platform.log"
Remove-Item $results -ErrorAction SilentlyContinue

$args = @("-batchmode", "-nographics", "-projectPath", "`"$project`"", "-runTests", "-testPlatform", $Platform,
          "-testResults", "`"$results`"", "-logFile", "`"$log`"")
$proc = Start-Process -FilePath $Unity -ArgumentList $args -Wait -PassThru

if (-not (Test-Path $results)) {
    Write-Host "Os testes não rodaram (código $($proc.ExitCode)). Erros do log:" -ForegroundColor Red
    Select-String -Path $log -Pattern "error CS|Exiting|No valid Unity" | Select-Object -Last 20 | ForEach-Object { $_.Line }
    exit 1
}

$run = ([xml](Get-Content $results -Raw))."test-run"
Write-Host "Resultado: $($run.result)  total=$($run.total) passaram=$($run.passed) falharam=$($run.failed)"
if ($run.failed -ne "0") {
    $xml = [xml](Get-Content $results -Raw)
    $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object { Write-Host " FALHOU: $($_.fullname)" -ForegroundColor Red }
    exit 1
}
exit 0
