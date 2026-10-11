# Instrucciones de desarrollo

Leer [CONTEXTO_PROYECTO.md](CONTEXTO_PROYECTO.md) y [README.md](README.md) antes de modificar este repositorio. El contexto registra reglas, arquitectura, decisiones provisionales y alcance actual. Consultar también los documentos de la capa afectada en `desmoche/Docs`.

- Trabajar únicamente en la fase u objetivo que indique el usuario. No implementar automáticamente fases posteriores ni añadir sistemas preventivos.
- Analizar los archivos existentes, dependencias, autoridad y desconexiones antes de editar. Explicar brevemente cambios arquitectónicos y no alterar decisiones anteriores silenciosamente.
- Conservar C# compatible con Godot .NET, namespaces, nombres, escenas y contratos coherentes. Verificar documentación oficial cuando una API o configuración dependa de su versión.
- Mantener dominio, reglas y núcleos de aplicación sin Godot UI. Reutilizar `GameManager` y `RulesValidator` tanto en LAN como en Singleplayer.
- En LAN, solo el host modifica la partida. Validar cada intención a partir del solicitante autenticado; nunca confiar en el ID de jugador o validez declarada por un cliente.
- Mantener manos ajenas y orden del mazo fuera de snapshots públicos, sincronizadores, datos de aparición, nombres/metadatos de nodos y logs. Cada bot recibe solo su mano y datos públicos.
- Mesa consume `IGameSession`; Carta visual y Drag & Drop solo preparan intenciones y estado de presentación. El snapshot confirmado determina el estado real. Conservar `/root/Main/NetworkManager` estable en todos los peers.
- No inventar reglas. Preguntar ante una ambigüedad que bloquee el cambio; identificar expresamente decisiones PROVISIONALES. Preservar las reglas resumidas en el contexto y comprobar sus casos límite.
- Ante un error, diagnosticar y corregir el componente mínimo responsable antes de plantear una reescritura.
- Dejar cada entrega compilable y verificable. Ejecutar pruebas relevantes para el cambio y distinguir evidencia automática de aceptación manual. Las pruebas de consola permanecen fuera de `desmoche` y enlazan el código mediante rutas relativas.
- Documentar los cambios completos, archivos/rutas, árboles de nodos, Inspector, señales y RPC cuando corresponda. Cada entrega de fase usa las nueve secciones definidas en el contexto y termina indicando fase completada, bloqueadores y siguiente fase elegida por el usuario.
- No incluir `.godot`, `.vs`, `bin`, `obj`, registros de ejecución ni secretos en los commits. Mantener los recursos fuente, escenas y archivos de identidad `.uid` necesarios para importar el proyecto.

Comandos de validación desde la raíz: `dotnet build ./desmoche/Desmoche.csproj`, `dotnet run --project ./Desmoche.Pruebas/Pruebas.csproj`, `dotnet run --project ./Desmoche.Interaccion.Pruebas/Pruebas.csproj` y `dotnet run --project ./Desmoche.Singleplayer.Pruebas/Pruebas.csproj -- --quick`. Ampliar pruebas cuando lo justifique el cambio; la guía Singleplayer detalla los diagnósticos de Godot.
