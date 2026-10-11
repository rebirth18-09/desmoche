param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche')
)
$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath $GodotPath).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
& dotnet build (Join-Path $project 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilación del proyecto falló.' }
$logRoot = Join-Path $PSScriptRoot ('TestResults\Fase8-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$log = Join-Path $logRoot 'Mesa.log'
$arguments = '--headless --path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/MesaVisualSmoke.tscn -- --auto' -f $project, $log
$process = Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    if (!$process.HasExited) { throw 'Tiempo agotado. Revisar Mesa.log.' }
    $text = Get-Content -LiteralPath $log -Raw
    if ($process.ExitCode -ne 0 -or $text -notmatch 'MESA VISUAL OK:' -or $text -match 'MESA VISUAL ERROR:') {
        throw ('Prueba visual fallida. Revisar ' + $log)
    }
    Write-Host 'PRUEBA MESA COMPLETADA.'
}
finally {
    $process.Refresh()
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
    Write-Host ('Registro: ' + $log)
}
