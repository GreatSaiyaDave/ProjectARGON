# Headless engine harness

Runs the WRLDZ rules engine (the "referee": `DuelEngine`, `CardTextEffectCompiler`,
and the `Duel/Rules` suites) **outside the Unity Editor**, so engine + card-effect
changes can be compiled and regression-tested from the command line / CI.

This makes the card-authoring loop fast: change a template or effect, run this,
and see the full `TcgRegressionTests` / `InteractionRegressionTests` /
`CorpusTriggerStressTests` report in a couple of seconds.

## Run

```bash
Tools/HeadlessEngine/run.sh --quiet
```

Requires the .NET 8 SDK. If `dotnet` isn't installed:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
```

Exit code is non-zero if any suite prints a `FAIL  ` line.

## How it works

- It does **not** vendor or fork the engine. The `.csproj` compiles the real
  engine sources straight from `Assets/Scripts/WRLDZ/{Data,Duel}` (excluding the
  AGPL native-OCG `Duel/Ocg` path).
- `Shim/UnityEngineShim.cs` provides the small `UnityEngine` API surface the
  engine uses headlessly (`Debug`, `Mathf`, `Application` paths, `JsonUtility`
  backed by `System.Text.Json`, `Random`, `Time`, `PlayerPrefs`, math/geometry
  structs, and inert graphics/coroutine stand-ins). It lives outside `Assets/`,
  so Unity never sees it and there is no conflict with the real engine build.
- `Shim/WrldzStubs.cs` stubs the AR presentation hooks (`SpellActivationPresentation`,
  `CardArtFocus`) and the session/GPS glue (`AppSession`, `ArDuelMatchConfig`,
  `LocalAccountStore`, `ArtifactService`, `OcgLabDuelHost`) that the rules core
  calls into. These are inert headlessly — the engine always uses its own rules.

Because the shims only implement APIs the engine already relies on, a passing
headless run exercises the same rules code the Unity build runs.
