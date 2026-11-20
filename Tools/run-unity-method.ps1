# Executa um método estático de Editor no Unity em batchmode (ex.: montar cenas, capturar telas, gerar build).
# Uso:  powershell -ExecutionPolicy Bypass -File Tools/run-unity-method.ps1 -Method Fodinha.EditorTools.SceneBuilder.BuildAll [-Graphics] [-Extra "-snapshotDir C:\x"]
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [switch]$Graphics,
    [string]$Extra = "",
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe"
)

$project = Split-Path -Parent $PSScriptRoot
# Usa sempre o mesmo caminho do projeto: o atalho C:\Fodinha (sem acento) quando existir.
# Alternar entre dois caminhos pode deixar scripts "perdidos" no build, e o Android não aceita acento no caminho.
$ascii = "C:\Fodinha"
if ((Test-Path $ascii) -and ((Get-Item $ascii).Target -eq (Resolve-Path $project).Path -or (Resolve-Path $project).Path -eq $ascii)) { $project = $ascii }
$outDir = Join-Path $env:TEMP "fodinha-tests"
New-Item -ItemType Directory -Force $outDir | Out-Null
$log = Join-Path $outDir ("method-" + ($Method -replace '[^A-Za-z0-9]', '_') + ".log")

$unityArgs = @("-batchmode", "-quit", "-projectPath", "`"$project`"", "-executeMethod", $Method, "-logFile", "`"$log`"")
if (-not $Graphics) { $unityArgs = @("-nographics") + $unityArgs }
if ($Extra) { $unityArgs += $Extra }

$proc = Start-Process -FilePath $Unity -ArgumentList $unityArgs -PassThru
$proc.WaitForExit()

Select-String -Path $log -Pattern "error CS|\[Fodinha\]|Exception|Scripts have compiler errors" | Select-Object -First 40 | ForEach-Object { $_.Line }
Write-Host "Código de saída: $($proc.ExitCode)  (log: $log)"
exit $proc.ExitCode
