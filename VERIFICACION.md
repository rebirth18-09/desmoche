# Verificación — Ajustes de partida

Godot 4.7.2 .NET, SDK .NET 9.0.308 y runtime .NET 8. Pruebas ejecutadas el 14 de octubre de 2026 (America/Managua).

| Comprobación | Resultado |
| --- | --- |
| `dotnet build desmoche/Desmoche.csproj` | 0 errores / 0 advertencias |
| Consola general | Aprobada; incluye 53 comprobaciones nuevas de ajustes y 1056 casos de escalera |
| Interacción | 750 comprobaciones aprobadas |
| Bots, modo rápido | 11 984 comprobaciones; 38 partidas completas (33 Desmoche, 5 empates) |
| MatchSettingsSmoke con render nativo | 128 comprobaciones aprobadas |
| MenuNavigationSmoke con render nativo | 223 comprobaciones aprobadas |
| TableActionsSmoke con render nativo | 1671 comprobaciones aprobadas |
| ENet real en tres procesos | Configuración actualizada, pública y conservada en revancha; invitado no confirmado excluido |

El panel y los controles de mesa se verificaron en 640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440. Se inspeccionaron capturas de escritorio y móvil horizontal. Render Linux/Xvfb/OpenGL con llvmpipe. No se realizó aceptación táctil en Android/iOS ni LAN entre dispositivos físicos.

Las pruebas comprueban: host autorizado y cliente rechazado; límites de tiempos; rechazo de cambios durante la partida; todas las victorias iniciales activadas y desactivadas; desactivaciones antes/después del cambio y en la primera oferta; posibilidad de jugar normalmente la combinación desactivada; tiempos de oferta, pozo, preparación, Pasa y revancha; resolución automática y conservación de opciones. Los cuatro niveles de bots respetan los cinco interruptores.

La GUI verifica clics nativos, aplicación de un número escrito sin confirmar previamente, Cancelar, Escape, Restablecer, resumen, creación de partida local, creación/actualización de sala y consulta sin edición. Los aros usan el total configurado, no constantes antiguas.

La prueba ENet utilizó un host y dos invitados: recibió los ajustes después de modificarlos en la sala, finalizó la partida, verificó que manos y mazo finales coincidieran, excluyó al invitado que no confirmó y comenzó una revancha con los ajustes conservados y los datos ocultos nuevamente privados.

Se preservaron los archivos fuente de la entrega acumulativa anterior. No se incluyen cachés, DLL, bin/obj, registros, herramientas descargadas ni capturas de diagnóstico. No se hicieron commits ni cambios en GitHub.

## Repetir

Desde la raíz del repositorio:

```powershell
dotnet build .\desmoche\Desmoche.csproj
dotnet run --project .\Desmoche.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Interaccion.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Singleplayer.Pruebas\Pruebas.csproj -- --quick
```

En Godot, ejecutar con F6 `Scenes/Diagnostics/MatchSettingsSmoke.tscn`, `MenuNavigationSmoke.tscn` y `TableActionsSmoke.tscn`; esperar sus mensajes PASS. `TimedRoomSmoke.tscn` acepta `--settings` para comprobar opciones personalizadas en el diagnóstico de red.

FASE COMPLETADA: SÍ.
BLOQUEADORES: ninguno.
SIGUIENTE FASE: la que indique el usuario.

## Validación de integración para GitHub — 15 de octubre de 2026

Ejecutada sobre el repositorio completo después de incorporar los 184 archivos de la entrega acumulativa, conservando recursos y fuentes anteriores. SDK .NET 9.0.308 en Windows; las fuentes de juego y pruebas coinciden por SHA-256 con el paquete recibido. Solo se consolida la documentación del contexto y la publicación.

| Comprobación ejecutada | Resultado |
| --- | --- |
| Compilación de Desmoche.csproj | 0 errores / 0 advertencias. |
| Consola general | 4077 comprobaciones correctas, incluidas 53 de ajustes, más 1056 casos de escalera. |
| Interacción | 750 comprobaciones correctas. |
| Singleplayer rápido | 11 984 comprobaciones; 38 partidas completas: 33 Desmoche y 5 empates. |
| Integridad de recursos | Las referencias res:// de escenas, recursos y project.godot existen en el proyecto completo. |

No se repitieron diagnósticos visuales ni procesos ENet durante esta integración; sus resultados anteriores permanecen identificados como evidencia de la entrega del 14 de octubre. Sigue pendiente la aceptación táctil y LAN/Wi-Fi entre dispositivos físicos. La configuración vigente exige protocolo 8 en todos los participantes.
