param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [ValidateRange(2,4)][int]$Players = 2,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath $GodotPath).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (!(Test-Path -LiteralPath (Join-Path $project 'Scenes\Diagnostics\LobbySmoke.tscn'))) {
    throw 'Falta copiar LobbySmoke.tscn y los archivos de fase 7.'
}
if (!$SkipBuild) {
    & dotnet build (Join-Path $project 'Desmoche.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'La compilación del proyecto falló.' }
}
$logRoot = Join-Path $PSScriptRoot ('TestResults\Fase7-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$processes = @()
$testRoomCode = 'AAAAAA'
function Start-LobbyPeer([string]$Role, [string]$Name) {
    $log = Join-Path $logRoot ($Name + '.log')
    $arguments = '--headless --path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/LobbySmoke.tscn -- --mode {2} --players {3} --code {5} --name {4}' -f $project, $log, $Role, $Players, $Name, $testRoomCode
    $process = Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
    return [pscustomobject]@{ Process = $process; Log = $log; Name = $Name }
}
try {
    $hostPeer = Start-LobbyPeer 'host' 'Host'
    $processes += $hostPeer
    $hostDeadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $hostDeadline) {
        $hostPeer.Process.Refresh()
        if ($hostPeer.Process.HasExited) { throw 'Godot cerró antes de crear la sala. Revisar Host.log.' }
        if ((Test-Path -LiteralPath $hostPeer.Log) -and
            (Get-Content -LiteralPath $hostPeer.Log -Raw) -match 'LOBBY HOST LISTO') { break }
        Start-Sleep -Milliseconds 200
    }
    if (!(Test-Path -LiteralPath $hostPeer.Log) -or
        (Get-Content -LiteralPath $hostPeer.Log -Raw) -notmatch 'LOBBY HOST LISTO') {
        throw 'El host no creó la sala en 30 segundos.'
    }
    $codeMatch = [regex]::Match((Get-Content -LiteralPath $hostPeer.Log -Raw), 'LOBBY CODE: ([A-Z2-9]{6})')
    if (!$codeMatch.Success) { throw 'El host no publicó su código de sala.' }
    $testRoomCode = $codeMatch.Groups[1].Value
    for ($i = 2; $i -le $Players; $i++) { $processes += Start-LobbyPeer 'client' ('Jugador' + $i) }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @($processes | Where-Object { $_.Process.Refresh(); !$_.Process.HasExited })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    }
    foreach ($entry in $processes) {
        $entry.Process.Refresh()
        if (!$entry.Process.HasExited) { throw ('Tiempo agotado: ' + $entry.Name) }
        $text = Get-Content -LiteralPath $entry.Log -Raw
        if ($entry.Process.ExitCode -ne 0 -or $text -notmatch 'LOBBY OK:' -or
            $text -match 'LOBBY ERROR:' -or $text -notmatch 'LOBBY MESA OK') {
            throw ('Prueba fallida: ' + $entry.Name + '. Revisar ' + $entry.Log)
        }
        Write-Host ('OK: ' + $entry.Name)
    }
    Write-Host ('PRUEBA LOBBY COMPLETADA: ' + $Players + ' jugadores.')
}
finally {
    foreach ($entry in $processes) {
        $entry.Process.Refresh()
        if (!$entry.Process.HasExited) { Stop-Process -Id $entry.Process.Id }
        $entry.Process.Dispose()
    }
    Write-Host ('Registros: ' + $logRoot)
}
