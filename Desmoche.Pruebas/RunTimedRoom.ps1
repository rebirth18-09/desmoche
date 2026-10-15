param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [ValidateRange(2,4)][int]$Players = 3,
    [ValidateSet('AllReturn','GuestTimeout','HostSpectator')][string]$Scenario = 'GuestTimeout',
    [ValidateRange(1024,65535)][int]$Port = 7010
)
$ErrorActionPreference = 'Stop'
if ($Players -lt 3 -and $Scenario -ne 'AllReturn') { throw 'Los casos de exclusion necesitan al menos tres jugadores.' }
$engine = (Resolve-Path -LiteralPath $GodotPath).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
& dotnet build (Join-Path $project 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilacion fallo.' }
$logs = Join-Path $PSScriptRoot ('TestResults\Revancha-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$processes = @()
$returning = if ($Scenario -eq 'AllReturn') { $Players } else { $Players - 1 }
function Start-Peer([int]$Index) {
    $role = if ($Index -eq 1) { 'host' } else { 'client' }
    $skip = if (($Scenario -eq 'HostSpectator' -and $Index -eq 1) -or
        ($Scenario -eq 'GuestTimeout' -and $Index -eq $Players)) { ' --no-rematch' } else { '' }
    $log = Join-Path $logs ('Jugador' + $Index + '.log')
    $arguments = '--headless --path "{0}" --log-file "{1}" res://Scenes/Diagnostics/TimedRoomSmoke.tscn -- --mode {2} --players {3} --returning {4} --port {5} --name Jugador{6}{7}' -f $project, $log, $role, $Players, $returning, $Port, $Index, $skip
    $process = Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
    return [pscustomobject]@{ Process = $process; Log = $log }
}
try {
    $hostPeer = Start-Peer 1
    $processes += $hostPeer
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $hostPeer.Process.Refresh()
        if ($hostPeer.Process.HasExited) { throw 'El host cerro antes de crear la sala.' }
        if ((Test-Path -LiteralPath $hostPeer.Log) -and
            (Get-Content -LiteralPath $hostPeer.Log -Raw) -match 'TIMED ROOM LISTEN') { break }
        Start-Sleep -Milliseconds 150
    }
    if (!(Test-Path -LiteralPath $hostPeer.Log) -or
        (Get-Content -LiteralPath $hostPeer.Log -Raw) -notmatch 'TIMED ROOM LISTEN') { throw 'Host no disponible.' }
    for ($index = 2; $index -le $Players; $index++) { $processes += Start-Peer $index }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @($processes | Where-Object { $_.Process.Refresh(); !$_.Process.HasExited })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    }
    foreach ($entry in $processes) {
        $entry.Process.Refresh()
        if (!$entry.Process.HasExited) { throw 'Tiempo agotado en un proceso de prueba.' }
        $text = Get-Content -LiteralPath $entry.Log -Raw
        if ($entry.Process.ExitCode -ne 0 -or $text -notmatch 'TIMED ROOM PASS:' -or
            $text -match 'TIMED ROOM FAIL:|ERROR:') { throw ('Prueba fallida: ' + $entry.Log) }
    }
    Write-Host ('REVANCHA ENET CORRECTA: ' + $Scenario + ', ' + $Players + ' procesos.')
}
finally {
    foreach ($entry in $processes) {
        $entry.Process.Refresh()
        if (!$entry.Process.HasExited) { Stop-Process -Id $entry.Process.Id }
        $entry.Process.Dispose()
    }
    Write-Host ('Registros: ' + $logs)
}
