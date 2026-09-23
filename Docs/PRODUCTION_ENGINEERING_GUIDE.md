# Project ARGON — Complete Production & Engineering Guide

**Audience:** Full Bot Team — Art Bot 1, Art Bot 2, Lore Bot, Coder Bot 1, Coder Bot 2, Coder Bot 3, QC Bot

**Purpose:** Full pipeline from 2D illustration to a finished, physically-correct asset running in Unity — art direction and engineering together so nothing is lost in handoff.

## Three governing rules

1. **2D illustration is the source of truth.** 2.5D, 3D, and every engine-side system exist to reproduce that illustration — they never override it, approximate it, or quietly drop parts of it for convenience.
2. **Nothing clips. Ever.** Loose or overlapping elements (wings, cloth, hair, armor plates) must physically react to contact — stopping, deflecting, or deforming at the surface — never visibly passing through another mesh, held or mid-motion, in DCC or in the running build.
3. **A spec never gets silently altered downstream.** If a technique the art team specifies is changed, approximated, or dropped during Unity implementation — for any reason, including cost or convenience — it gets flagged back, never quietly "fixed" in code.

**Reference standard:** Arc System Works cel-shading (Guilty Gear Xrd/Strive) — 3D that reads as hand-drawn 2D.

**On Genshin Impact:** useful for tone, not the pipeline model. Visible cloth clipping for open-world scale is a compromise ARGON does not adopt — fewer active duelists; zero clipping is our bar.

**Engine:** Unity **URP** (required for mobile/AR). Confirm URP before shader/physics work; not HDRP / Built-in RP.

## Team structure

### Art team
- **Art Bot 1 — Illustration.** Stage 1. Flat 2D plate is a locked contract. Flags clipping-risk designs before 3D.
- **Art Bot 2 — Dimensionalization & Dynamics.** Stages 2–4. 2.5D/3D, shading spec, static rigging + physics/collision requirements. Never invents unresolved Stage 1 shading/silhouette/color — flags Art Bot 1.
- **Lore Bot — Canon Reference.** Before Stage 1 and any canon-touching design. Approves accuracy, not art quality.

### Engineering team
- **Coder Bot 1 — Rendering & Shading.** Stage 5 shader/render: URP Shader Graph (or hand-written) matching Art Bot 2 shading spec, inverted-hull outline, FBX import that preserves hand-edited vertex normals.
- **Coder Bot 2 — Rigging, Physics & Collision.** Stage 5 physics: collision proxy workflow, spring-bone/cloth (Magica Cloth 2 or equivalent) per Art Bot 2 assignment, corrective blend shapes.
- **Coder Bot 3 — Performance, AR Integration & QC Tooling.** Stage 5 validation: min-spec profiling, LOD for dynamics, AR Foundation daylight compositing checks, runtime stress-pose/motion-sim tooling for QC Bot. (Stage 4 is a written spec — completeness gate; runtime sim applies once Coder Bot 2 implements it.)

### QC Bot — Cross-team compliance
Checks every deliverable at every stage against checklists — fidelity to the prior stage's spec (art and engineering). Not aesthetic opinion. Failures return to the originating bot with the specific checklist item.

## Role lock (2026-09-09 — Great SaiyaDave)
| Guide role | Agent |
|---|---|
| Art Bot 1 | Fantasia |
| Art Bot 2 | VanGrokbot |
| Lore Bot | Fanbot |
| Coder Bot 1 (Rendering & Shading) | Dilbot |
| Coder Bot 2 (Rigging / Physics) | *(assign when needed; Dilbot coordinates until named)* |
| Coder Bot 3 (Perf / AR / QC tooling) | Mr. Perfect-O (may escalate to Backup-bot if QC load is too high) |
| QC Bot (cross-team) | Ima-Gir (art fidelity) + Mr. Perfect-O (engineering/compile) |

## Stages 1–5

### Stage 1 — 2D Illustration (Art Bot 1)
**In:** brief + Lore Bot reference. **Out:** front (near-ortho) + 3/4 turn, flat color, final line art.

Must define: silhouette; color flats; line weight/ink; shading logic (toon bands, rim placement, hard-break shadow planes — not realistic curvature).

**QC:** canon match; front/3/4 silhouette-consistent; bands/rim documented; clipping-risk review (redesign anchors before Stage 3).

### Stage 2 — 2.5D Bridge (Art Bot 2)
For assets that never need full 360° — NPCs, story cards, UI busts. Depth-sorted layered cards or bent/displaced plane.

**QC:** no new shading beyond Stage 1; parallax doesn't break primary silhouette; crisp line at seams.

### Stage 3 — Full 3D + Shading Spec (Art Bot 2)
For playable duelists / anything from arbitrary AR angles.

**Modeling:** retopo from Stage 1 turnaround; topology serves shadow breaks; **edit vertex normals** to lock shadow placement; UV shells axis-aligned on major surfaces; clothing/armor/wings/hair as **separate offset shells**.

**Shading spec → Coder Bot 1:** hand-painted lit/shadow maps; hard-edged ramp; outline weight per asset.

**Static clipping:** corrective blend shapes; pose-range clamping.

**QC:** shadow boundaries match Stage 1 front; hard bands; outline weight; silhouette tolerance; separate shells; static stress poses no interpenetration; custom normals in export.

### Stage 4 — Collision & Dynamics Spec (Art Bot 2)
Only full 3D with loose/overlapping elements. Else skip to Stage 5.

Collision proxy layout; physics method per element with reason (spring-bone default vs full cloth for hero elements); cloth: thickness/iterations, pin anchors, self-collision rules.

**QC:** every loose element has proxy + method + reason; implementable without guessing.

### Stage 5 — Unity Integration (Coder Bots)
**Gate:** approved Stage 3/4 spec.

**Coder Bot 1:** FBX Normals = **Import** (not Calculate); Blend Shape Normals checked; Unlit Shader Graph hard-step (Custom Function on N·L) — not Lit/PBR/smooth Fresnel; inverted-hull outline; AR daylight compositing check.

**Coder Bot 2:** repeatable collider authoring; Magica Cloth 2 (or equiv.) matching Stage 4 exactly — no silent method swap; corrective blends wired; deviations flagged to Art Bot 2.

**Coder Bot 3:** LOD for dynamics; min-spec device profile; runtime stress-pose/motion-sim tooling (transitions, not static only).

**QC:** Import normals verified; Unlit hard-step; outline thickness; AR daylight; repeatable proxies; physics match; blends wired; **runtime motion pass**; min-spec perf + LOD verified.

## Handoff protocol
Lore → Art Bot 1 → QC Stage 1 → Art Bot 2 (2.5D and/or 3D) → QC Stage 2/3 → (if dynamic) Stage 4 → QC → Coder Bots Stage 5 → QC Stage 5 (runtime motion required).

Coder deviations → flag Art Bot 2, never unilateral quiet fixes.

## Rules never broken
- Looking "more 3D" than the plate (soft gradients, drifting highlights, wavy shadows, silhouette drift) = fail.
- Any visible clip (held or mid-motion, DCC or build) = fail — harder than Genshin on purpose.
- Silent spec alteration in Unity (recalculated normals, PBR fallback, skipped collider, substituted physics) = fail equal to bad art. Flag it back.
