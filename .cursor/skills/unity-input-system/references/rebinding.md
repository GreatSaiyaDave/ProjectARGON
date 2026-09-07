# Interactive rebinding & local multiplayer (Input System 1.x)

Vendored from gamedev-skills/awesome-gamedev-agent-skills (Apache-2.0).

ARGON does not ship rebinding UI yet. Nearby PvP is GPS / hotseat (`ArDuelOpponentKind`), not `PlayerInputManager` split-screen. Read this only if the owner asks for remappable keys or local device join.

## Interactive rebinding

`InputActionRebindingExtensions.PerformInteractiveRebinding` listens for the next control and stores a **binding override** (asset on disk is unchanged).

- Disable the action before rebinding.
- Always `Dispose()` the `RebindingOperation`.
- Persist with `SaveBindingOverridesAsJson` / `LoadBindingOverridesFromJson`.
- Composite WASD: rebind each part by index.

## Local multiplayer with `PlayerInputManager`

Spawns one player prefab per joining device. ARGON face-to-face duels use one spatial stage and `ArDuelMatchConfig.Opponent`, not two `PlayerInput` instances driving two engines.

## Gotchas

- Rebinding an enabled action throws.
- Do not leak the rebind operation.
