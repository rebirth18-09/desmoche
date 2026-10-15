# Ajustes de partida

## 1. Objetivo de la fase

Añadir un acceso con engranaje a Jugar contra bots, Crear partida y la sala multijugador. Permite ajustar los tiempos y activar o desactivar individualmente las victorias iniciales. Mantiene los cambios acumulados de Mesa, arrastre, reparto, revelación final y burbujas.

## 2. Decisiones arquitectónicas

`Domain/MatchSettings` es un valor inmutable sin Godot. `GameManager` guarda las opciones y rechaza modificar una partida ya iniciada. `ServerSession` utiliza esos mismos valores para los vencimientos reales. Los clientes solo reciben las opciones y muestran sus relojes; nunca envían duraciones al elegir una carta ni al decir Pasa.

El creador puede configurar antes de crear la sala y mientras espera jugadores. Solo el host autenticado puede enviar `UpdateSettings`, y únicamente durante `Preparing`. Los invitados consultan el panel en modo lectura. El protocolo pasa a **8**: todos los participantes deben instalar esta revisión. Se reutiliza el RPC existente de intenciones, sin añadir otra autoridad.

Las opciones se conservan durante las revanchas y al volver al mismo formulario durante la ejecución. No se añaden archivos de preferencias permanentes. En bots, `LocalMatchOptions` entrega la misma configuración a `ServerSession`; `BotBrain` solo recibe opciones públicas y respeta los interruptores.

## 3. Dependencias

Godot 4.7.2 .NET, SDK .NET 9.0.308 y runtime net8.0 de las entregas anteriores. Se reutilizan `SalonTheme.tres` y `Assets/UI/Salon/Gear.svg`. No se incorporan bibliotecas ni recursos externos nuevos al juego.

## 4. Implementación

| Opción | Predeterminado | Valores disponibles |
| --- | --- | --- |
| Elegir carta de la oferta o del pozo | 5 s | 1–120 s |
| Armar y confirmar el juego | 20 s | 5–300 s |
| Denunciar con Pasa | 3 s | 1–30 s |
| Apuntarse a revancha multijugador | 15 s | 5–120 s |

Los límites son decisiones de interfaz para esta entrega, ajustables posteriormente; no agregan plazos al cambio inicial, declaraciones iniciales ni pago. Los segundos se eligen con flechas o escribiendo. Aplicar confirma también el número escrito que todavía tenga el foco. Cancelar o Escape descarta el borrador; Restablecer carga los valores originales y exige Aplicar para guardarlos.

Las cinco victorias iniciales aparecen activadas por defecto:

- Desmoche · A–2–3–4 del mismo palo.
- Desmoche · J–Q–K–A del mismo palo.
- Desmoche · Cuarta.
- Color.
- Peladilla.

El nombre Desmoche corresponde al botón inicial existente que agrupa las tres primeras. Cada una se puede desactivar por separado. La victoria habitual con exactamente diez cartas combinadas continúa activa. Se conservan las ventanas y validaciones anteriores: las escaleras iniciales antes/después del cambio; la cuarta después; Color/Peladilla después del cambio; las tres combinaciones permitidas con la primera oferta.

Una opción desactivada desaparece de las declaraciones de Mesa, los bots no la proponen y el motor rechaza su solicitud sin eliminar al jugador ni consumir cartas. Desactivar una victoria especial no impide utilizar esa combinación como juego ordinario.

Archivos principales:

| Ruta | Cambio |
| --- | --- |
| `Scripts/Domain/MatchSettings.cs` | Valores, límites e interruptores. |
| `Scripts/Application/GameManager.cs` | Configuración en preparación y validación de declaraciones. |
| `Scripts/Networking/Core/NetworkProtocol.cs` | Intención UpdateSettings, opciones públicas y protocolo 8. |
| `Scripts/Networking/Core/ServerSession*.cs` | Permisos y todos los plazos personalizados. |
| `Scripts/Networking/Core/StateReplication.cs` | Transporte y validación de opciones y tiempos. |
| `Scripts/Networking/NetworkManager.cs` | Opciones al crear sala y solicitud del host. |
| `Scripts/Singleplayer/Core/{LocalMatchOptions,LocalMatchController,BotBrain}.cs` | Configuración local y decisiones de bots. |
| `Scripts/Presentation/MatchSettingsPanel.cs` | Panel común, engranaje, resumen, edición y consulta. |
| `Scripts/Presentation/{SingleplayerSetup,Lobby}.cs` | Integración en formularios y sala. |
| `Scripts/Presentation/Mesa.InitialDeclarations.cs` e `Interaction/InitialHandPresentation.cs` | Visibilidad y detección según interruptores. |
| `Scripts/Presentation/Mesa.TableActions.cs` | Aros calculados con la duración de la sala. |
| `Scripts/Presentation/{Mesa,Mesa.Dynamic}.cs`, `Scenes/MenuPrincipal.tscn` | Mensajes y ayuda coherentes con tiempos variables. |

## 5. Configuración Godot

No hay autoloads, señales manuales ni ajustes nuevos del Inspector. Copiar las fuentes y sus `.uid`, permitir la importación y recompilar.

El código crea `MatchSettingsAccess` (resumen + `SettingsButton`) dentro del contenido de Singleplayer, de `CreatePanel` y de `SessionPanel` del Lobby. Conserva todas las rutas de los controles anteriores.

Cada pantalla tiene `MatchSettingsOverlay : ColorRect`, con `Panel/Padding/Content`. El contenido contiene título, explicación, `Scroll/Fields` y un `Footer` con Cancelar/Aplicar siempre fuera del desplazamiento. Fields contiene cuatro filas con SpinBox, cinco CheckButton y Restablecer. La capa modal intercepta el ratón; Tab recorre solo sus controles y Escape la cierra antes de navegar al menú anterior.

## 6. Integración

Contra bots: formulario → copia de `MatchSettings` → `LocalMatchOptions` → controlador local → autoridad compartida. En LAN: Crear partida → `NetworkManager.Host(settings)`; los cambios posteriores recorren `UpdateMatchSettings` → cola habitual → `ServerSession.Receive` → `GameManager.ConfigureSettings` → revisión pública para todos.

La sala espera la confirmación autoritativa antes de habilitar de nuevo Iniciar. Una partida iniciada fija la configuración hasta su cierre. La revancha conserva el valor, incluyendo su tiempo de inscripción. Las manos privadas y el mazo oculto no forman parte de los ajustes.

## 7. Prueba de aceptación

Validación automática aprobada: compilación sin errores ni advertencias; consola general (incluidas 53 comprobaciones nuevas de ajustes), interacción (750), bots (11 984 comprobaciones y 38 partidas), MatchSettingsSmoke (128), MenuNavigationSmoke (223) y TableActionsSmoke (1671). Panel y acciones verificados en siete tamaños: 640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440. Tres procesos ENet confirmaron actualización pública de ajustes, exclusión del no confirmado y revancha conservando configuración; manos/mazo finales coincidentes y nueva partida sin revelaciones. No sustituye la aceptación táctil ni LAN entre dispositivos físicos.

Resultados completos: `VERIFICACION.md` del ZIP. Consolas ejecutadas: compilación de `desmoche/Desmoche.csproj`, `Desmoche.Pruebas`, `Desmoche.Interaccion.Pruebas` y `Desmoche.Singleplayer.Pruebas -- --quick`.

`MatchSettingsTests` comprueba permisos, rangos, bloqueo durante partida, serialización, las cinco victorias activadas/desactivadas, primera oferta, plazos personalizados, vencimientos exactos y revancha. Las pruebas de bots comprueban las cinco desactivaciones en las cuatro dificultades. `MatchSettingsSmoke.tscn` verifica los controles reales y la creación de sesiones. `TableActionsSmoke.tscn` comprueba que los aros usan el total configurado. `TimedRoomSmoke.tscn -- --settings` verifica la transmisión real entre procesos y conservación durante revancha.

Aceptación manual: abrir Ajustes en bots y Crear partida, cambiar 5/20 por otros tiempos, desactivar una victoria, aplicar y jugar. Confirmar que los círculos y los vencimientos coinciden. En LAN, consultar desde un invitado, cambiar antes de iniciar desde el host y revisar que ambas pantallas coincidan. Comprobar una revancha con los mismos ajustes.

## 8. Posibles errores

Si el engranaje no aparece, comprobar la copia de los scripts del formulario y recompilar. Un mensaje de protocolo incompatible indica que algún equipo conserva una versión anterior. Los invitados consultan los ajustes; para modificarlos hay que hacerlo desde el creador antes de comenzar. La revancha de bots continúa siendo inmediata: el ajuste de revancha corresponde a multijugador.

## 9. Estado de la fase

Implementación y verificación completadas. Los cambios se entregan en un ZIP acumulativo; no se realizaron commits ni publicaciones en GitHub.

FASE COMPLETADA: SÍ.
BLOQUEADORES: ninguno.
SIGUIENTE FASE: la que indique el usuario.
