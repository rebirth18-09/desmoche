# Pruebas del modo Singleplayer

Esta aplicación de consola comprueba el motor local y los bots usando los mismos archivos del juego. No necesita iniciar Godot, abrir puertos ni instalar un framework de pruebas.

Coloca esta carpeta junto a la carpeta `desmoche`, conservando esta estructura:

```text
Documentos/
├── desmoche/
│   └── Scripts/
└── Desmoche.Singleplayer.Pruebas/
    ├── Pruebas.csproj
    └── Program.cs
```

Abre PowerShell en `Desmoche.Singleplayer.Pruebas` y ejecuta:

```powershell
dotnet run --project .\Pruebas.csproj
```

Debe terminar con `PRUEBAS COMPLETADAS` y devolver código de salida cero. La comprobación rápida usa una semilla por combinación de dificultad y cantidad de jugadores:

```powershell
dotnet run --project .\Pruebas.csproj -- --quick
```

La prueba completa simula 168 partidas con las cuatro dificultades y de dos a cuatro jugadores. Incluye reproducción determinista y revancha. Verifica reparto, conservación de 52 cartas, privacidad, combinaciones publicadas, diez cartas exactas para desmoche sin carta suelta, victorias iniciales, pozo cuando el mazo está vacío, pagos, rechazos de acciones inválidas, cierre, memoria pública del nivel Experto y diferencias de decisión entre niveles. No avanza en nombre de una persona real: solo en estas pruebas otro `BotBrain` representa al participante humano.

Los tiempos impresos son mediciones del equipo donde se ejecutan, no límites universales ni una prueba de fluidez de la interfaz. Las pruebas visuales del juego se describen en la documentación principal de la entrega.
