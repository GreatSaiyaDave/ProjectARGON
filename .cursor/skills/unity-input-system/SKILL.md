---
name: unity-input-system
description: >
  Wire player input with Unity Input System (com.unity.inputsystem). In Project ARGON
  always go through WrldzInput — never Input.GetKey/GetAxis. Use when adding keyboard,
  mouse, touch, or XR controller reads, or when fixing InvalidOperationException from
  the old Input Manager API.
license: Apache-2.0
---

# ARGON overlay (read first)

This file is vendored from [gamedev-skills/awesome-gamedev-agent-skills](https://github.com/gamedev-skills/awesome-gamedev-agent-skills) (Apache-2.0). Modifications 2026-09-07: this overlay.

**This project is Input System–only.** `WrldzInput` (`Assets/Scripts/WRLDZ/Core/WrldzInput.cs`) wraps `Keyboard.current` / `Mouse.current`. Calling `UnityEngine.Input.GetKey*` throws `InvalidOperationException`.

Do not add a parallel `.inputactions` + `PlayerInput` stack for map WASD or menu keys unless the owner asks. GPS / permission / AR tracking stay in `WrldzLab` and the AR session classes (`wrldz-ar-lenses`).

Quest controllers are bound in `ArLensesSession` (left = player disk), not via a generic `PlayerInput` prefab.

# Unity Input System (new)

Read input through Unity's **Input System package** (`com.unity.inputsystem` **1.20.0** in this repo). Targets Unity 6. This is the replacement for the legacy `Input.GetAxis` / `Input.GetKey` Input Manager.

## When to use

- Use when setting up movement/jump/fire input, defining an `.inputactions` asset, wiring `PlayerInput`, or handling gamepad + keyboard + touch from one set of actions.
- Use when `Packages/manifest.json` contains `com.unity.inputsystem`.

**When *not* to use in ARGON:** TCG chain timing is not an Input Action. Map chrome click handlers are uGUI. Prefer extending `WrldzInput.Map(KeyCode)` if you need another key.

## Core workflow (upstream, still true)

1. **Check Active Input Handling** (Project Settings → Player). Must be `Input System Package (New)` or `Both`. ARGON expects New.
2. **ARGON default:** call `WrldzInput.KeyHeld` / `KeyDown` / `MouseButtonHeld`.
3. If you add a raw `InputAction`, **Enable()** it and Disable on teardown.
4. Switch action maps for gameplay vs UI instead of guarding every handler — only if you introduce maps; the overworld currently does not.

## ARGON pattern (preferred)

```csharp
using WRLDZ.Core;

if (WrldzInput.KeyDown(KeyCode.W) || WrldzInput.KeyHeld(KeyCode.W))
{
    // walk pad
}
```

## Upstream patterns (only if you add a new action asset)

### `PlayerInput` Send Messages

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputReceiver : MonoBehaviour
{
    private Vector2 _move;
    private void OnMove(InputValue value) => _move = value.Get<Vector2>();
    private void OnJump(InputValue value) { if (value.isPressed) Jump(); }
    private void Jump() { }
}
```

### Direct polling

```csharp
[SerializeField] private InputActionReference moveAction;

private void OnEnable()  => moveAction.action.Enable();
private void OnDisable() => moveAction.action.Disable();

private void Update()
{
    Vector2 move = moveAction.action.ReadValue<Vector2>();
}
```

## Pitfalls

- **`InvalidOperationException` about the old input backend** — some script still calls `Input.GetKey`. Port it to `WrldzInput`.
- **No input at all** — action not `Enable()`d, or Active Input Handling still Old.
- **Buttons as `ReadValue`** — use `performed` / `WasPressedThisFrame` for presses.
- **Leaking subscriptions** — `-=` in `OnDisable`.

## References

- Interactive rebinding / local coop: [references/rebinding.md](references/rebinding.md) (not used by ARGON yet)
- Unity Manual Input System package

## Related skills

- `unity-csharp-scripting` — MonoBehaviour these reads live in
- `wrldz-ar-lenses` — XR controllers / AR session
- `wrldz-overworld` — WASD walk pad / GPS
