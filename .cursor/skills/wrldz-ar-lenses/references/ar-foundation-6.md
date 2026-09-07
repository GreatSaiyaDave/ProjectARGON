# AR Foundation 6.6 (paraphrased)

Packages in this repo: `com.unity.xr.arfoundation` **6.6.1**, `com.unity.xr.arcore` **6.6.1**, `com.unity.xr.openxr` **1.17.1**, `com.unity.xr.interaction.toolkit` **3.5.1**. Unity **6000.5.10f1**.

Public docs (see [sources.md](../sources.md)) — do not paste package manuals into game assets.

## Split of responsibility

| Layer | What it is |
|---|---|
| AR Foundation | Cross-platform managers (`ARSession`, `ARPlaneManager`, `XROrigin`, …) |
| Provider plug-in | ARCore (Android phone), OpenXR (Quest). Foundation does **not** implement tracking itself |
| XR Plug-in Management | Chooses the active `XRLoader` per platform |

Android tab: **ARCore** for the S23 product path. OpenXR stays enabled for Quest. Do not replace ARCore with OpenXR on the phone demo.

`WRLDZ_HAS_ARFOUNDATION` is a scripting define (Android / iPhone / Standalone in Player settings, plus `ArFoundationDefine.cs` when the package is actually loaded). `ArFoundationSession` has a stub class when the define is off — keep both sides compiling.

## Subsystems (what agents get wrong)

A subsystem is created/destroyed by the active loader. Trackable managers **Start/Stop** work when enabled/disabled. You rarely `new` a subsystem.

Plane detection is `XRPlaneSubsystem` → `ARPlane` components wrapping `BoundedPlane` trackables (GUID `TrackableId`). Device tracking is Input System–based, not a plane subsystem.

Before using an optional capability, check the descriptor at runtime (`supportsMutableLibrary`, etc.). Do not assume every phone has every ARCore feature.

## XR Origin

Phone Full AR builds `XROrigin` + AR camera + plane manager + floor/arena anchors in `ArFoundationSession`. Quest builds a separate origin in `ArLensesSession`. Do not parent disks to the camera rig.

## Android / ARCore gotchas (public getting-started, paraphrased)

- Graphics: ARCore does not want Vulkan as the only API for the AR camera path.
- Scripting backend for device builds: **IL2CPP**, architecture **ARM64**.
- Package name / min API: Quest wants API 29+; phone demo follows the project Player settings.
- ARCore **optional** in the manifest so DIGITAL / no-Play-Services devices still boot.

## XRI vs ARGON

`unity-skills` module `xr` drives XR Interaction Toolkit REST (ray/direct/socket interactors) in a running Editor. ARGON’s duel interaction is **custom** (`ArCardDragSystem`, `ArSnapRules`, disk zones). Do not replace the disk graph with stock `XRGrabInteractable` cards unless the owner asks for XRI prototyping.

Generic XRI class names (`XRRayInteractor`, `XROrigin`, …) are fine for the Quest rig. Do not invent `XRHand`, `GrabInteractor`, or `XRTeleporter` as the ARGON API.
