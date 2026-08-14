# AGENTS.md

Proyecto Unity 6 (URP). El editor de Unity está conectado por el **Unity MCP** (`com.coplaydev.unity-mcp`, herramienta `unityMCP_*`). Usa el MCP como fuente de verdad del estado del editor, no el código.

## Stack y versiones
- Unity **6000.5.8f1** (Unity 6), render pipeline **URP 17.5.0** (`Assets/Settings/` tiene `PC_RPAsset` y `Mobile_RPAsset`).
- **Input System nuevo** activo (`ProjectSettings.asset` → `activeInputHandler: 1`). Usa `UnityEngine.InputSystem`, NO la API legacy `UnityEngine.Input`.
- Packages clave: `com.unity.inputsystem` 1.20.0, `com.unity.ugui` 2.5.0, `com.unity.test-framework` 1.7.0, `com.unity.ai.navigation` 2.0.14.

## Estado del proyecto
- Escena única y casi vacía: `Assets/Scenes/SampleScene.unity` (solo Main Camera, Directional Light, Global Volume). No hay jugador ni gameplay aún.
- `Assets/` proviene de la plantilla URP (TutorialInfo, InputSystem_Actions.inputactions). Sin código propio de juego.

## Flujo de trabajo
- Para crear/editar escenas, GameObjects, prefabs, materiales, scripts, animaciones, UI, física o builds: usa las herramientas `unityMCP_*` en el editor conectado.
- Tras crear o modificar scripts, siempre revisa `read_console` para detectar errores de compilación y espera a que termine el domain reload (`mcpforunity://editor/state` → `compilation`/`advice.ready_for_tools`) antes de usar el nuevo tipo.
- No hay comandos CLI de build/test/lint: el equivalente es el editor vía MCP (`manage_build`, `run_tests`). No hay tests, CI ni scripts de build definidos.

## Git
- Solo `README.md` y `.gitignore` están trackeados; `Assets/`, `Packages/` y `ProjectSettings/` están **sin trackear** (proyecto aún no committeado). Antes de commitear, revisa `git status` y no subas secretos ni `.meta` huérfanos.
- Se requieren los `.meta` de Unity para los assets; no borrarlos.
