param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [int]$Port = 7129
)
$ErrorActionPreference = 'Stop'
$mesaNetEngine = (Resolve-Path -LiteralPath $GodotPath).Path
$mesaNetProject = (Resolve-Path -LiteralPath $ProjectPath).Path
& dotnet build (Join-Path $mesaNetProject 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilación del proyecto falló.' }
$mesaNetResults = Join-Path $PSScriptRoot ('TestResults\MesaRed-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $mesaNetResults -Force | Out-Null
$mesaNetProcesses = @()
try {
    foreach ($mesaNetMode in @('host', 'client')) {
        $mesaNetLog = Join-Path $mesaNetResults ($mesaNetMode + '.log')
        $mesaNetArguments = '--headless --path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/MesaNetworkSmoke.tscn -- --mode {2} --port {3}' -f $mesaNetProject, $mesaNetLog, $mesaNetMode, $Port
        $mesaNetProcesses += Start-Process -FilePath $mesaNetEngine -ArgumentList $mesaNetArguments -WindowStyle Hidden -PassThru
        if ($mesaNetMode -eq 'host') { Start-Sleep -Milliseconds 600 }
    }
    $mesaNetDeadline = [DateTime]::UtcNow.AddSeconds(55)
    while ([DateTime]::UtcNow -lt $mesaNetDeadline) {
        $mesaNetProcesses | ForEach-Object { $_.Refresh() }
        if (@($mesaNetProcesses | Where-Object { !$_.HasExited }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    }
    foreach ($mesaNetMode in @('host', 'client')) {
        $mesaNetIndex = if ($mesaNetMode -eq 'host') { 0 } else { 1 }
        $mesaNetProcess = $mesaNetProcesses[$mesaNetIndex]
        if (!$mesaNetProcess.HasExited) { throw ('Tiempo agotado para ' + $mesaNetMode) }
        $mesaNetText = Get-Content -LiteralPath (Join-Path $mesaNetResults ($mesaNetMode + '.log')) -Raw
        if ($mesaNetProcess.ExitCode -ne 0 -or $mesaNetText -notmatch 'MESA NETWORK PASS:' -or $mesaNetText -match 'MESA NETWORK FAIL:') {
            throw ('Prueba ENet de mesa fallida para ' + $mesaNetMode + '. Revisar los registros.')
        }
    }
    Write-Host 'PRUEBA MESA ENET COMPLETADA: dos procesos reales, preparación y confirmación de acciones.'
}
finally {
    foreach ($mesaNetProcess in $mesaNetProcesses) {
        $mesaNetProcess.Refresh()
        if (!$mesaNetProcess.HasExited) { Stop-Process -Id $mesaNetProcess.Id }
        $mesaNetProcess.Dispose()
    }
    Write-Host ('Resultados: ' + $mesaNetResults)
}
