---
name: unity-csharp-scripting
description: >
  Write Unity 6 C# gameplay scripts: MonoBehaviour lifecycle
  (Awake/OnEnable/Start/Update/FixedUpdate/LateUpdate), GameObject and component
  access, coroutines, and Inspector serialization. Use when creating or editing .cs
  scripts in this Unity project. ARGON is 6000.5.10f1; Cloud VMs have no Editor Play Mode.
license: Apache-2.0
---

# ARGON overlay (read first)

This file is vendored from [gamedev-skills/awesome-gamedev-agent-skills](https://github.com/gamedev-skills/awesome-gamedev-agent-skills) (Apache-2.0). Modifications 2026-09-07: this overlay.

| ARGON fact | Do this |
|---|---|
| Editor version | `6000.5.10f1` (not 6000.3 LTS in the upstream text) |
| Input | `WrldzInput` only — see `unity-input-system` |
| Data | Cards / progress are JSON + `PlayerAccountDatabase`, not ScriptableObject catalogs |
| Cloud VM | No Play Mode. Verify rules with `wrldz-cloud-verify` |
| Namespaces | `WRLDZ.Core`, `WRLDZ.Duel`, `WRLDZ.Presentation`, `WRLDZ.UI` |
| No asmdefs | Runtime scripts live under `Assets/Scripts/WRLDZ/` |

When the task is TCG rules, AR sessions, or Hub chrome, prefer `ygo-gamedev` / `wrldz-ar-lenses` / `ygo-ui-lore` over this generic lifecycle skill.

# Unity C# Scripting (MonoBehaviour)

Write correct, idiomatic gameplay scripts in Unity 6. Get the lifecycle, component
access, serialization, and coroutines right so behaviour is deterministic and the
Inspector stays useful. Targets **Unity 6**, C# / .NET Standard 2.1.

## When to use

- Use when authoring or fixing a `MonoBehaviour`: choosing the right lifecycle callback,
  reading/caching components, exposing fields to the Inspector, or running timed logic
  with coroutines.
- Use when the project has `*.cs` files, an `Assembly-CSharp` or `*.asmdef`, and a
  `ProjectSettings/` folder.

**When *not* to use:** moving rigidbodies / collision response is rare in ARGON (AR disks are kinematic snaps). Reading player input → `unity-input-system`. Shared card data → JSON / `CardDatabase`, not a new ScriptableObject architecture. Animator parameters are not the duel chain.

## Core workflow

1. **Pick the callback by purpose, not habit.** `Awake` (cache references, runs once on
   load), `OnEnable` (subscribe to events), `Start` (init that depends on other objects'
   `Awake`), `Update` (per-frame logic/input polling), `FixedUpdate` (physics), `LateUpdate`
   (camera follow after movement), `OnDisable`/`OnDestroy` (unsubscribe/cleanup).
2. **Cache component lookups in `Awake`** — never call `GetComponent` every frame.
3. **Expose tunables with `[SerializeField] private`**, not public fields, so other code
   can't mutate them but designers can edit them in the Inspector.
4. **Scale per-frame values by `Time.deltaTime`** in `Update` (and `Time.fixedDeltaTime`
   semantics are automatic in `FixedUpdate`).
5. **Use coroutines for time-sequenced logic** (delays, tweens, "do X then wait then Y");
   start them with `StartCoroutine` and stop them deterministically.
6. **Verify:** owner Play Mode on Linux Hub, or `wrldz-cloud-verify` on Cloud.

## Patterns

### 1. Lifecycle + cached components

```csharp
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    private Rigidbody _rb;

    private void Awake() => _rb = GetComponent<Rigidbody>();

    private void Update()
    {
        transform.Rotate(0f, 90f * Time.deltaTime, 0f);
    }

    private void FixedUpdate()
    {
        _rb.MovePosition(_rb.position + transform.forward * moveSpeed * Time.fixedDeltaTime);
    }
}
```

ARGON note: most WRLDZ UI is built in code (`new GameObject` + `RectTransform`). Still cache `GetComponent` after add; still unsubscribe in `OnDisable`.

### 2. Safe component access with `TryGetComponent`

```csharp
if (other.TryGetComponent<Health>(out var health))
    health.Apply(-10);
```

### 3. Serialization that shows up correctly in the Inspector

```csharp
[SerializeField, Range(0f, 1f)] private float volume = 0.8f;
[SerializeField] private string playerName = "Hero";

[System.Serializable]
public class Stats { public int hp = 100; public int mana = 50; }

[SerializeField] private Stats stats = new();
```

ARGON progress is account JSON, not a `Stats` SO on the avatar.

### 4. Coroutines for time-sequenced logic

```csharp
private void Start() => StartCoroutine(FlashThenHide());

private System.Collections.IEnumerator FlashThenHide()
{
    yield return new WaitForSeconds(0.5f);
    GetComponent<Renderer>().enabled = false;
    yield return null;
}
```

## Pitfalls

- **`GetComponent` in `Update`** — cache in `Awake`/`Start`.
- **Physics in `Update`** — `Rigidbody` motion belongs in `FixedUpdate`.
- **Relying on `Start` order across objects** — all `Awake`s finish first; order among `Start`s is undefined.
- **`public` fields just for the Inspector** — use `[SerializeField] private`.
- **`gameObject.tag == "Enemy"`** — use `CompareTag`.
- **Coroutines stop when the GameObject is disabled** — re-`StartCoroutine` in `OnEnable` if needed.
- **`Input.GetKey` in ARGON** — throws. Use `WrldzInput`.

## References

- Full event-execution-order table and coroutine yields: [references/lifecycle-and-coroutines.md](references/lifecycle-and-coroutines.md)
- Unity Manual "Event function execution order"

## Related skills

- `unity-input-system` — `WrldzInput` in this repo
- `wrldz-cloud-verify` — no Editor on Cloud
- `ygo-gamedev` — do not put chain resolution in `Update`
