param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\desmoche'),
    [string]$InteractionTestsPath = (Join-Path $PSScriptRoot '..\Desmoche.Interaccion.Pruebas\Pruebas.csproj')
)
$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath $GodotPath).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$tests = (Resolve-Path -LiteralPath $InteractionTestsPath).Path
& dotnet build (Join-Path $project 'Desmoche.csproj')
if ($LASTEXITCODE -ne 0) { throw 'La compilacion del juego fallo.' }
& dotnet run --project $tests "-p:GameSourceRoot=$project"
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas de interaccion sin Godot fallaron.' }
$logRoot = Join-Path $PSScriptRoot ('TestResults\Fase9-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$cases = @(
    @{ Name = 'Carta'; Scene = 'CardInputSmoke'; Pass = 'CARD INPUT PASS:'; Fail = 'CARD INPUT FAIL:' },
    @{ Name = 'InteraccionMesa'; Scene = 'MesaInteractionSmoke'; Pass = 'MESA INTERACTION PASS:'; Fail = 'MESA INTERACTION FAIL:' },
    @{ Name = 'PrivacidadMesa'; Scene = 'MesaVisualSmoke'; Pass = 'MESA VISUAL OK:'; Fail = 'MESA VISUAL ERROR:' }
)
foreach ($case in $cases) {
    $log = Join-Path $logRoot ($case.Name + '.log')
    $arguments = '--headless --path "{0}" --log-file "{1}" --scene res://Scenes/Diagnostics/{2}.tscn -- --auto' -f $project, $log, $case.Scene
    $process = Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while ([DateTime]::UtcNow -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 200
        }
        if (!$process.HasExited) { throw ('Tiempo agotado. Revisar ' + $log) }
        if (!(Test-Path -LiteralPath $log)) { throw ('Godot no genero el registro ' + $log) }
        $text = Get-Content -LiteralPath $log -Raw
        if ($process.ExitCode -ne 0 -or $text -notmatch [regex]::Escape($case.Pass) -or
                $text -match [regex]::Escape($case.Fail) -or $text -match '(?m)^\s*(ERROR:|SCRIPT ERROR:)') {
            throw ('Prueba fallida: ' + $case.Name + '. Revisar ' + $log)
        }
        Write-Host ('OK: ' + $case.Name)
    }
    finally {
        $process.Refresh()
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
        $process.Dispose()
    }
}
Write-Host ('PRUEBAS FASE 9 COMPLETADAS. Registros: ' + $logRoot)
Write-Host 'Todavia debes probar el drag nativo con F6 y una partida LAN real con F5.'
