param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [switch]$Capture
)
$ErrorActionPreference = 'Stop'
$dynamicEngine = (Resolve-Path -LiteralPath $GodotPath).Path
$dynamicProject = (Resolve-Path -LiteralPath $ProjectPath).Path
& dotnet build (Join-Path $dynamicProject 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilación del proyecto falló.' }
$dynamicLogRoot = Join-Path $PSScriptRoot ('TestResults\MesaDinamica-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $dynamicLogRoot -Force | Out-Null
$dynamicLog = Join-Path $dynamicLogRoot 'MesaDynamic.log'
$dynamicArguments = '--path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/MesaDynamicSmoke.tscn ' -f $dynamicProject, $dynamicLog
if ($Capture) {
    $dynamicArguments += '--rendering-method gl_compatibility --resolution 2016x780 -- --auto "--capture-dir={0}"' -f $dynamicLogRoot
}
else { $dynamicArguments += '--headless -- --auto' }
$dynamicProcess = Start-Process -FilePath $dynamicEngine -ArgumentList $dynamicArguments -WindowStyle Hidden -PassThru
try {
    $dynamicDeadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $dynamicDeadline) {
        $dynamicProcess.Refresh()
        if ($dynamicProcess.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    if (!$dynamicProcess.HasExited) { throw 'Tiempo agotado. Revisar MesaDynamic.log.' }
    $dynamicText = Get-Content -LiteralPath $dynamicLog -Raw
    if ($dynamicProcess.ExitCode -ne 0 -or $dynamicText -notmatch 'MESA DYNAMIC PASS:' -or $dynamicText -match 'MESA DYNAMIC FAIL:') {
        throw ('Prueba de mesa dinámica fallida. Revisar ' + $dynamicLog)
    }
    if ($Capture -and $dynamicText -notmatch 'MESA DYNAMIC CAPTURE:') {
        throw ('No se generó una captura verificable. Revisar ' + $dynamicLog)
    }
    Write-Host 'PRUEBA MESA DINÁMICA COMPLETADA: gestos, confirmación contextual, posición y privacidad.'
}
finally {
    $dynamicProcess.Refresh()
    if (!$dynamicProcess.HasExited) { Stop-Process -Id $dynamicProcess.Id }
    $dynamicProcess.Dispose()
    Write-Host ('Resultados: ' + $dynamicLogRoot)
}
