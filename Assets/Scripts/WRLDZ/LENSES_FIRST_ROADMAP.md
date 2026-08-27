# Lenses-first roadmap — “Yu-Gi-GO” next gen

**Priority lock (2026-07):** Product **north star = AR lenses**.  
**Your lab (now):** **Samsung Galaxy S23 Ultra + PC** — no headset.  
**Demo path:** ship a **playable phone AR demo** on S23; keep code **OpenXR-ready** so lenses plug in when hardware arrives.

Not rushing full MMO — **playable demo in ~30 days on S23 Ultra** is the near target.

---

## North star (one line)

People put on **lenses**, walk the real world, enter a **Tear / Zone**, and play a **life-size shadow duel** in mixed reality — Yu-Gi-GO next gen.

**Until you have lenses:** we build that **same spatial duel** on **phone camera (S23 Ultra)** so every week of work still counts toward the end product.

| Product (later) | Lab (your setup now) |
|-----------------|----------------------|
| Lenses / HMD MR | S23 Ultra AR + PC Editor |
| Floor-anchored arena in room | Phone passthrough + spatial stage (then AR Foundation planes) |
| Hand / controller disk | Touch + optional gyro; spatial layout still “disk + arena” |

Reference: Dueling Dimension (MR panels) + GO loop + our TCG engine — **architecture for lenses, daily builds for phone**.

---

## Target hardware (your reality → product)

| Tier | Device | Role |
|------|--------|------|
| **Your daily test** | **Galaxy S23 Ultra** | Primary demo device for the next month |
| **Daily code** | Current PC + Unity Editor | Iterate layout / engine / map |
| **Product north star** | Any OpenXR / Android XR **lenses** (Meta optional) | Same `ArPresentationTarget.LensesXr` later |
| Not required now | Quest / other HMD | Buy when ready; don’t block demo |

**S23 Ultra strengths for us:** excellent camera, GPS, performance, Android 14+, good Unity target.

---

## 30-day playable demo (definition of done) — **S23 Ultra**

A stranger can, on **your S23 Ultra**:

1. Install the APK / run from Unity.  
2. **Overworld map** (GPS outdoors or WASD-style offline for indoor demo).  
3. Tap a **Tear** → **Zone Mode / AR duel**.  
4. See **camera passthrough** + **spatial stage** (disks, arena, holos).  
5. Finish **one full practice duel** (summon / set / attack / end turn) + inspect.  
6. See **Referobot** phase/win call (text OK).  
7. Return to map.

**Out of scope for month 1:** headset build, all zones, PvP, tome raids, 9 seasons, economy, physical deck scan.

---

## Week plan (S23 + PC — no headset)

### Week 1 — Phone AR solid
- [x] Android build pipeline to **S23 Ultra** (`WRLDZ → Lab → Build APK for S23 Ultra`).  
- [x] Portrait 1080×2340-class + forced portrait activity / PlayerSettings.  
- [x] Duel: **AR LIVE** via `ArPhoneCamera` + permission UX (`WrldzLab`).  
- [x] `ArPresentationTarget` auto: phone on S23, sim in Editor.  
- [x] Indoor walk pad when GPS off; package `com.wrldz.duelmonsters`.

### Week 2 — Spatial duel readable on phone
- [x] AR-first layout stable (stage hero, thin HUD, hand, phase).  
- [x] Field chips + hand + inspect usable with **thumbs**.  
- [x] One starter deck + AI duel without soft-locks.  
- [x] AR Foundation **plane detection** (floor) — `ArFoundationSession` / `FULL_AR.md`. ENTER AR on S23 is 6DOF, not a webcam quad.

### Week 3 — GO loop slice
- [x] Overworld → Tear → **Zone Mode prompt** → AR/Digital duel → back.  
- [x] Onboarding short path (account → Navi → gifts → practice).  
- [x] PvAI + PvP create (distance → arena separation).  
- [ ] Performance / heat pass on S23 (target 30–60 FPS in duel).

### Week 4 — Demo polish
- [ ] Referobot lines, clearer Zone Mode copy.  
- [ ] 3-minute demo script (indoor vs outdoor).  
- [ ] Keep **OpenXR packages** in project; document “when you get lenses, Week A/B…”  
- [ ] No requirement to own a headset this month.

### When you get lenses (primary spatial validation)
- [x] OpenXR session host — `ArLensesSession` (XR Origin, floor, controllers).  
- [x] 1:1 arm tracker — `XrWorldArmTracker` (left controller → disk).  
- [x] Floor-anchored arena + midfield at separation meters.  
- [x] `ArDuelSpace.BuildLensesStage` + deploy retract/deploy.  
- [ ] Quest 3 APK playtest (device).  
- [ ] Meta XR SDK passthrough underlay (optional polish).  
- See **`LENSES_XR_SETUP.md`**.

### Salvage optical-see-through (build your own combiner)
- [x] Hunt + cardboard bench playbook — `SALVAGE_LENSES.md` (do not gut the S23).  
- [x] `ArPresentationMode.SalvageOst` — black clear, no webcam, letterboxed optical window.  
- [x] Gyro / Editor RMB head tracker + 1:1 `SalvageArmTracker`.  
- [ ] Cardboard bench: 50 mm lens + 45° combiner, white rectangle then holos.  
- [ ] Print tray (`Tools/WRLDZ/salvage/optical_bench.py`) + sunglasses frame.  
- [ ] Second eye / USB-C donor birdbath if you find dead Xreal/Rokid.

---

## Architecture rules (so we don’t thrash)

1. **One duel graph** — `ArDuelSpace` / disks / holos / engine. Never fork “phone duel” vs “lens duel” engines.  
2. **Presentation target only changes transforms + input + camera.**  
3. **World-space UI for lenses**; screen-space uGUI only for phone/Editor fallback.  
4. **Overworld GPS** can stay non-XR until after demo (map on phone or floating tablet in MR later).  
5. **No feature** that only works as stacked 2D GBA chrome without a spatial path.

```
DuelEngine (rules)  ──►  ArDuelSpace (stage)
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
        EditorSim      PhoneCamera        LensesXr
        (RT view)      (webcam plane)     (HMD + floor anchor)
```

Code: `ArPresentationTarget`, `ArLensesBootstrap` (scaffolded).

---

## Install checklist (your machine — S23 Ultra)

1. Unity 6 (project already).  
2. Android Build Support + SDK/NDK in Unity Hub.  
3. S23: **Developer options** + **USB debugging**.  
4. Build Settings → **Android** → run on device.  
5. Allow **camera** + **location** when prompted.  
6. Game view on PC: set aspect to **phone portrait** (e.g. 1080×2340) for layout checks.

OpenXR packages may already be in `manifest.json` for the future — **ignore headset setup until you buy one**.

---

## Success metrics (month 1 — S23)

| Metric | Target |
|--------|--------|
| APK on S23 → map | Works |
| Tear → AR duel with camera | Works outdoors/indoors |
| Full practice duel | Completes without stuck UI |
| Time to first summon | &lt; 3 min guided |
| “Feels like next-gen GO duel” gut check | Yes from you + 1 friend |

---

## Explicit non-goals (month 1)

- Shipping on App Lab as full product  
- Full Battle City GPS on headset  
- Multiplayer  
- Perfect anime meshes for every card  
- Coordinating every 2D menu skin  

---

## After the demo

1. GPS overworld as **companion phone** or MR mini-map.  
2. Zone types (PvP / Raid / Bazaar) as spatial places.  
3. Tome / progression / Navi in world space.  
4. Broader device matrix (phone AR Foundation, other HMDs).
