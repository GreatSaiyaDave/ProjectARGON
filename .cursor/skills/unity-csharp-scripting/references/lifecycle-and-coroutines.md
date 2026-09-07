# MonoBehaviour lifecycle & coroutines (Unity 6)

Vendored from gamedev-skills/awesome-gamedev-agent-skills (Apache-2.0). ARGON Editor is 6000.5.10f1.

## Execution order (the parts that matter for gameplay)

Per object, the engine calls these in this order:

| Phase | Callback | Runs | Use for |
|-------|----------|------|---------|
| Load | `Awake` | once, when the object loads (even if disabled) | cache `GetComponent`, set up self |
| Enable | `OnEnable` | each time the object/script enables | subscribe to events, re-arm coroutines |
| Init | `Start` | once, before first `Update`, after every `Awake` | wiring that depends on other objects |
| Physics | `FixedUpdate` | every fixed step (default 0.02s) | rigidbody forces, `MovePosition` |
| Physics | `OnTriggerXXX` / `OnCollisionXXX` | during the physics step | collision/trigger response |
| Frame | `Update` | once per rendered frame | input polling, non-physics logic |
| Frame | `LateUpdate` | once per frame, after all `Update`s | camera follow, IK fix-up |
| Disable | `OnDisable` | each time the object/script disables | unsubscribe from events |
| Teardown | `OnDestroy` | once, when destroyed | release native handles, save |

Key consequences:

- `FixedUpdate` may run zero, one, or several times per frame. Read input in `Update`, consume it in `FixedUpdate` if you use physics.
- All `Awake` calls finish before any `Start`. Order among `Awake`s (and among `Start`s) is undefined unless Script Execution Order is set.
- Pair every `OnEnable` subscription with an `OnDisable` unsubscription.

## Coroutine yield instructions

```csharp
yield return null;                          // next frame
yield return new WaitForSeconds(2f);        // scaled time
yield return new WaitForSecondsRealtime(2f);// ignores timeScale
yield return new WaitForFixedUpdate();
yield return new WaitUntil(() => isReady);
yield return new WaitWhile(() => isLoading);
yield return StartCoroutine(OtherRoutine());
```

## Stopping coroutines deterministically

Keep the handle so you can stop exactly the right routine:

```csharp
private Coroutine _spawnLoop;

private void OnEnable()  => _spawnLoop = StartCoroutine(SpawnLoop());
private void OnDisable() { if (_spawnLoop != null) StopCoroutine(_spawnLoop); }

private System.Collections.IEnumerator SpawnLoop()
{
    var wait = new WaitForSeconds(1f);
    while (true)
    {
        Spawn();
        yield return wait;
    }
}
```

`StopAllCoroutines()` is blunt; prefer stopping by handle.

## Gotchas

- Coroutines are killed when the GameObject is deactivated. Re-arm in `OnEnable` if needed.
- A coroutine started from `Awake` will not advance past its first `yield` until the object is active.
- Cache `WaitForSeconds` instances in loops to avoid GC.
- ARGON: do not implement Fast Effect Timing as a Unity event in `Update` — that is `ygo-gamedev` / `ChainStack`.
