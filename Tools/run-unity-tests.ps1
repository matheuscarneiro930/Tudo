# Roda os testes do projeto no Unity em batchmode e mostra o resumo.
# Uso:  powershell -ExecutionPolicy Bypass -File Tools/run-unity-tests.ps1 [-Platform EditMode|PlayMode] [-Unity "C:\...\Unity.exe"]
# Observação: o Unity não pode estar aberto com este projeto ao mesmo tempo.
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe",
    [string]$Platform = "EditMode"
)

$project = Split-Path -Parent $PSScriptRoot
# Usa sempre o mesmo caminho do projeto: o atalho C:\Fodinha (sem acento) quando existir.
# Alternar entre dois caminhos pode deixar scripts "perdidos" no build, e o Android não aceita acento no caminho.
$ascii = "C:\Fodinha"
if ((Test-Path $ascii) -and ((Get-Item $ascii).Target -eq (Resolve-Path $project).Path -or (Resolve-Path $project).Path -eq $ascii)) { $project = $ascii }
$outDir = Join-Path $env:TEMP "fodinha-tests"
New-Item -ItemType Directory -Force $outDir | Out-Null
$results = Join-Path $outDir "$Platform-results.xml"
$log = Join-Path $outDir "$Platform.log"
Remove-Item $results -ErrorAction SilentlyContinue

$unityArgs = @("-batchmode", "-projectPath", "`"$project`"", "-runTests", "-testPlatform", $Platform,
               "-testResults", "`"$results`"", "-logFile", "`"$log`"")
if ($Platform -eq "EditMode") { $unityArgs = @("-nographics") + $unityArgs }

# WaitForExit espera só o Unity (Start-Process -Wait esperaria também o processo de licença que fica aberto).
$proc = Start-Process -FilePath $Unity -ArgumentList $unityArgs -PassThru
$proc.WaitForExit()

if (-not (Test-Path $results)) {
    Write-Host "Os testes não rodaram (código $($proc.ExitCode)). Erros do log:" -ForegroundColor Red
    Select-String -Path $log -Pattern "error CS|Exiting|No valid Unity|Exception" | Select-Object -Last 20 | ForEach-Object { $_.Line }
    exit 1
}

$xml = [xml](Get-Content $results -Raw)
$run = $xml."test-run"
Write-Host "Resultado: $($run.result)  total=$($run.total) passaram=$($run.passed) falharam=$($run.failed)"
if ($run.failed -ne "0") {
    $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
        Write-Host " FALHOU: $($_.fullname)" -ForegroundColor Red
        Write-Host ("   " + ($_.failure.message.InnerText -replace "`r?`n", " "))
    }
    exit 1
}
exit 0
