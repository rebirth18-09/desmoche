param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [switch]$Capture
)
$ErrorActionPreference = 'Stop'
$casinoEngine = (Resolve-Path -LiteralPath $GodotPath).Path
$casinoProject = (Resolve-Path -LiteralPath $ProjectPath).Path
& dotnet build (Join-Path $casinoProject 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilación del proyecto falló.' }
$casinoLogRoot = Join-Path $PSScriptRoot ('TestResults\MesaCasino-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $casinoLogRoot -Force | Out-Null
$casinoLog = Join-Path $casinoLogRoot 'Casino.log'
$casinoCapture = Join-Path $casinoLogRoot 'MesaCasino.png'
$casinoArguments = '--path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/CasinoVisualSmoke.tscn ' -f $casinoProject, $casinoLog
if ($Capture) {
    $casinoArguments += '--rendering-method gl_compatibility --resolution 2016x780 -- --auto "--capture={0}"' -f $casinoCapture
}
else { $casinoArguments += '--headless -- --auto' }
$casinoProcess = Start-Process -FilePath $casinoEngine -ArgumentList $casinoArguments -WindowStyle Hidden -PassThru
try {
    $casinoDeadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $casinoDeadline) {
        $casinoProcess.Refresh()
        if ($casinoProcess.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    if (!$casinoProcess.HasExited) { throw 'Tiempo agotado. Revisar Casino.log.' }
    $casinoText = Get-Content -LiteralPath $casinoLog -Raw
    if ($casinoProcess.ExitCode -ne 0 -or $casinoText -notmatch 'CASINO VISUAL OK:' -or $casinoText -match 'CASINO VISUAL ERROR:') {
        throw ('Prueba de mesa casino fallida. Revisar ' + $casinoLog)
    }
    if ($Capture -and (!(Test-Path -LiteralPath $casinoCapture) -or $casinoText -notmatch 'CASINO CAPTURA OK:')) {
        throw ('No se generó una captura verificable. Revisar ' + $casinoLog)
    }
    Write-Host 'PRUEBA MESA CASINO COMPLETADA: tres estados en tres resoluciones.'
    if ($Capture) { Write-Host ('Captura: ' + $casinoCapture) }
}
finally {
    $casinoProcess.Refresh()
    if (!$casinoProcess.HasExited) { Stop-Process -Id $casinoProcess.Id }
    $casinoProcess.Dispose()
    Write-Host ('Registro: ' + $casinoLog)
}
