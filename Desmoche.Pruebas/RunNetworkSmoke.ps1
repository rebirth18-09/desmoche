param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [ValidateRange(2,4)][int]$Players = 2,
    [ValidateSet('Normal','ClientDisconnect','HostDisconnect')][string]$Scenario = 'Normal',
    [ValidateRange(1024,65535)][int]$Port = 7000
)
$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath $GodotPath).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (!(Test-Path -LiteralPath (Join-Path $project 'Scenes\Diagnostics\NetworkSmoke.tscn'))) {
    throw 'Falta copiar la escena NetworkSmoke.tscn al proyecto.'
}
& dotnet build (Join-Path $project 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilacion del proyecto fallo.' }
$logRoot = Join-Path $PSScriptRoot ('TestResults\Fase5-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$processes = @()
function Start-TestPeer([string]$Role, [string]$Name) {
    $flag = if ($Scenario -eq 'ClientDisconnect') { ' --drop-client' }
        elseif ($Scenario -eq 'HostDisconnect') { ' --close-host' } else { '' }
    $log = Join-Path $logRoot ($Name + '.log')
    $arguments = '--headless --path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/NetworkSmoke.tscn -- --mode {2} --players {3} --port {4} --ip 127.0.0.1 --name {5}{6}' -f $project, $log, $Role, $Players, $Port, $Name, $flag
    $process = Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
    return [pscustomobject]@{ Process = $process; Log = $log; Name = $Name }
}
try {
    $hostPeer = Start-TestPeer 'host' 'Host'
    $processes += $hostPeer
    $hostDeadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $hostDeadline) {
        $hostPeer.Process.Refresh()
        if ($hostPeer.Process.HasExited) { throw 'Godot cerro antes de crear la sala. Revisar Host.log.' }
        if ((Test-Path -LiteralPath $hostPeer.Log) -and
            (Get-Content -LiteralPath $hostPeer.Log -Raw) -match 'CONEXION: Hosting') { break }
        Start-Sleep -Milliseconds 300
    }
    if (!(Test-Path -LiteralPath $hostPeer.Log) -or
        (Get-Content -LiteralPath $hostPeer.Log -Raw) -notmatch 'CONEXION: Hosting') {
        throw 'El host no pudo crear la sala en 60 segundos.'
    }
    for ($i = 2; $i -le $Players; $i++) { $processes += Start-TestPeer 'client' ('Jugador' + $i) }
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @($processes | Where-Object { $_.Process.Refresh(); !$_.Process.HasExited })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    }
    foreach ($entry in $processes) {
        $entry.Process.Refresh()
        if (!$entry.Process.HasExited) { throw ('Tiempo agotado: ' + $entry.Name) }
        $text = Get-Content -LiteralPath $entry.Log -Raw
        if ($text -notmatch 'RED ENET OK:' -or $text -match 'RED ENET ERROR:') {
            throw ('Prueba fallida: ' + $entry.Name + '. Revisar ' + $entry.Log)
        }
        Write-Host ('OK: ' + $entry.Name)
    }
    Write-Host ('PRUEBA ENET COMPLETADA: ' + $Players + ' jugadores, ' + $Scenario)
}
finally {
    foreach ($entry in $processes) {
        $entry.Process.Refresh()
        if (!$entry.Process.HasExited) { Stop-Process -Id $entry.Process.Id }
        $entry.Process.Dispose()
    }
    Write-Host ('Registros: ' + $logRoot)
}
