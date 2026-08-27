# WRLDZ / Project ARGON — Product Vision

**Canon sources:** Blueprint v1.1 (design bible) + this file (implementation implications made explicit).  
**Genre:** Free-to-play **AR lenses-first** duel MMO — **Yu-Gi-GO next gen** (Niantic loop + spatial YGO).  
**Presentation:** Product **north star = AR lenses**; **lab/demo device = Galaxy S23 Ultra + PC** (no headset yet).  
**Near target:** Playable **phone AR demo on S23 in ~30 days** — architecture stays OpenXR-ready — see `LENSES_FIRST_ROADMAP.md`.

---

## North star (what we’re building)

**People are going Yu-Gi-GO in this next generation — on AR lenses.**

What Niantic did for Pokémon on phones, we do for Duel Monsters **in mixed reality** — then go further.

| Pokémon GO (phone gen) | WRLDZ / Project ARGON (lenses gen) |
|------------------------|-------------------------------------|
| Real-world map + GPS walking | Same living world (phone map OK early; MR map later) |
| Spawns / stops / raids at places | Tears, zones, arenas in **real space** |
| Small camera AR creatures | **Life-size** spirits + floor-anchored duel arenas |
| Phone-first | **Lenses north star**; **S23 Ultra phone AR demo now** (no HMD required) |
| Catch → pocket | Walk / clear space → **Zone Mode** → **duel in the room** |

This is **not** “Master Duel with a map skin.”  
It is a **spatial AR duel MMO**: TCG engine is authority; **presence in the real world** is the product.

**Reference energy:** [Dueling Dimension](https://devpost.com/software/dueling-dimension) (Quest MR TCG panels) + GO loop + anime TCG.

**Indie phase branding:** Duel Monsters: WRLDZ · files/codename **ProjectARGON**.  
**UI nostalgia (non-AR chrome):** GBA-era *Sacred Cards* / *Worldwide Edition* command windows — classic feel, modern AR spine.

---

## One-sentence pitch

Players **walk a real-world overworld** (GO-style map), get **prompted into AR near encounters/tears/arenas**, and duel with **life-size AR assets in real-world duel arenas** — menus always exist in **both non-AR and AR variants**; AR is **coded for lenses**, fully **usable on phone camera**.

---

## Core loop (explicit)

```
Phone overworld map (non-AR)          ← Niantic-style living world
        │  walk / GPS
        ▼
Near tear / encounter / duel arena
        │  ALWAYS prompt AR entry
        ▼
┌────────────────────────────┐
│  Enter AR arena?           │
│  [Enter AR] [Digital duel] │  (F2P: decline stays non-AR)
└───────────┬────────────────┘
            ▼
   AR session — NEXT-GEN EMPHASIS
   · Life-size spirit / monster presence
   · Real-world duel arena (ground-anchored field)
   · Dual duel disks (arm + mirrored opponent)
   · AR variants of all menus
            ▼
         DUEL (Anime TCG engine — vital system)
         · Structural rules + timed responses
         · Tap **or** verbal announce (legal gateway)
            ▼
   Rewards → back to overworld map
   · Story / CPU: Set Energy (set-tagged; see SET_ENERGY_AND_BAZAAR.md)
   · PvP: no SE — skill / social only
   · Bazaar zones: altars convert cards → SE of that set
```

**Required in code:**

1. Overworld is the **default** state (map first, like GO).  
2. Approaching an encounter **prompts** AR — never silent auto-force, never “lenses only.”  
3. Player may stay non-AR (2D/digital duel + map UI) — hardware never gates core play.  
4. Accepting AR loads **life-size presence + arena shell** of the same duel systems.  
5. **Card engine is an anime TCG simulator** (not a pure Konami tournament client) — see `ANIME_TCG_ENGINE.md`.

Blueprint: *static tears on the overworld map trigger AR zone mode upon approach, similar to Pokémon Go encounters.*

---

## Next-gen AR emphasis (canon differentiators)

### 1. Life-size assets
- Spirits / monsters / key props scale to **human / environment scale** in AR (not only desktop-mini holograms).  
- Fidelity may step down on phone camera; **scale and presence** do not disappear.  
- Non-AR uses portrait “stage” framing that still sells size (full-height plates, not postage stamps).

### 2. Real-world duel arenas
- Duels can anchor to **places**: park plaza, street tear, event zone, private “garage” arena.  
- Arena = spatial shell (floor field, zone markers, rim lighting) + same `DuelEngine` state.  
- Static map pins / tears **invite** arena entry; multiplayer arenas later reuse the same shell.

### 3. Dual floating Duel Disks + AR duel space (always)

See **`AR_DUEL_PRESENTATION.md`** for the full spatial model.

| Element | Placement | Shows |
|---------|-----------|--------|
| **Your disk** | **Left arm** (Spirit Dueler wearable) | Your M + S/T zones |
| **Mirror disk** | In front of you | Opponent field **as legally allowed** |
| **Hand** | Floating in front of player | **Card backs** toward opponent |
| **Card info** | On **select only** | Full legal TCG face + complete text popup |
| **Arena** | Between duelists | Holograms from face-up cards on either disk |
| **Mr. Referobot** | Physical NPC at arena | Referee + announcer |

### Non-YGO chrome = Pokémon GO template
- Map shell, soft circular orbs, encounter prompts, minimal HUD during AR.  
- YGO energy lives on disks / arena / cards — not a flat Master Duel board.

### Phone vs lenses
- Phone: simulated AR passthrough + spatial RT (`ArDuelSpace`).  
- Lenses: same graph re-parented (wrist / ground / world NPC).

---

## Dual UI rule (all menus)

**Every player-facing menu/surface has two variants:**

| Surface | Non-AR variant | AR variant |
|---------|----------------|------------|
| Home / Duel Disk | Portrait flat UI (Neuron-style Disk) | Spatial / lens-anchored Disk chrome |
| Overworld map | 2D map + pins | Optional AR “look around” overlays (still map-driven) |
| Encounter prompt | Modal on map | Spatial prompt near pin / tear |
| Duel field / hand / phases | **Dual duel disks** on portrait canvas (see below) | Same disks as AR holograms |
| Deck Lab / tabs / settings | Standard panels | AR panel set (same actions, spatial layout) |
| Log / status / LP | Text bars | Floating AR readouts |

### Implementation contract

```
IMenuSurface
  ├── NonArView   // phone portrait canvas (default)
  └── ArView      // same intents, AR presentation

IArBackend
  ├── LensBackend     // primary target (Spectacles / XR glasses / etc.)
  └── PhoneCameraBackend  // full feature parity via phone AR
```

- **One feature flag / session mode:** `PresentationMode = NonAr | Ar`  
- Controllers talk to **intents** (OpenDeckLab, ConfirmEncounter, DrawCard) — never to “the camera” directly.  
- Building a menu means shipping **both** shells or documenting a temporary single-shell exception.

---

## AR policy (lenses-first, phone-capable)

| Rule | Meaning |
|------|---------|
| **Coded for lenses** | Tracking, anchors, FOV, glanceability, input assume **wearable AR** as the primary design target |
| **Usable on phone camera** | Same AR features run via phone passthrough / camera AR when no lenses are connected |
| **Non-AR always available** | Map + duel without any AR session — complete F2P path |
| **No dual feature sets** | Do not invent “lens-only powers” that phone AR cannot approximate; scale fidelity, not rules |

```
                    ┌─ Lens device connected ──► LensBackend
Enter AR mode ──────┤
                    └─ No lenses ──────────────► PhoneCameraBackend
Stay on map / digital duel ──────────────────► NonAr only
```

**Spirit Dueler** (in-game hologram + optional wearable) anchors AR field/menus on the arm — phone UI mirrors that metaphor when lenses are absent.

---

## Device & mode summary

| Layer | Role |
|-------|------|
| **Overworld (phone, non-AR)** | GPS map, tears/zones/arena pins, navigation, inventory, social |
| **Encounter / arena prompt** | Gate between map and AR duel — required UX beat |
| **AR duel arena** | Ground-anchored field, life-size assets, dual disks, spatial menus |
| **DuelEngine** | Rules authority (phone/server) — **no** GPS or AR dependency |
| **Lenses** | Preferred AR display + spatial input (primary design target) |
| **Phone camera** | AR fallback with feature parity (scale fidelity, not rules) |

---

## Blueprint alignment

- Niantic-style overworld; tears/zones as spawn/encounter anchors  
- Approach tear → **AR arena mode** (GO-like encounter transition, richer presence)  
- AR duels via **lenses** *or* phone camera *or* full digital decline path  
- Spirit Dueler anchors arm disk / menus  
- Original systems: Umbrax, tears/zones, Mr. Referobot, Seal of Umbrax, etc.

**Engineering canon (explicit):**

1. Mandatory **AR entry prompt** near encounters / arenas  
2. **AR + non-AR** for every menu  
3. **Lenses-first** AR code, **phone-camera usable**  
4. **Life-size assets + real-world duel arenas** as the AR quality bar (not optional flavor)  
5. Non-AR chrome may use **GBA Sacred Cards / WWE** visual language for clarity  

---

## Architecture sketch

```
┌──────────────────────────────────────────────────┐
│  App session                                     │
│  PresentationMode: NonAr | Ar                    │
│  ArBackend: Lens | PhoneCamera | None            │
└───────────────┬──────────────────────────────────┘
                │
     ┌──────────┴──────────┐
     ▼                     ▼
 OverworldController    EncounterPrompt
 (map, GPS, pins)       (near tear / arena)
     │                     │
     │    accept AR        │ decline / digital
     ▼                     ▼
 ArArenaShell ────────► DuelEngine + UI intents
  · life-size assets      · Non-AR menu variants
  · arena field           · portrait dual-disk canvas
  · dual disks + menus
```

Keep **rules**, **inventory**, and **match state** outside AR backends. AR only **presents**.

---

## Free / indie constraints

- Free data: YGOPRODeck local cache — no hotlinking  
- Original branding: **WRLDZ / Project ARGON** for fan/indie phase  
- F2P: no P2W; **lenses optional**; phone camera AR + non-AR complete  
- Modular: `DuelEngine` must not import map or AR SDK packages  
- Life-size AR must degrade gracefully (LOD), never become “lens exclusive content”

---

## What exists today vs deferred

| Layer | Status |
|-------|--------|
| Card DB + art pipeline | ✅ |
| TCG-structure duel + AI | ✅ vertical slice + hotseat PvP |
| Portrait home + duel UI (GBA-inspired non-AR) | ✅ |
| Master AI / Mr. Referobot companion path | ✅ training + phone PWA slice |
| Overworld map / GPS / tears | ✅ `OverworldUI` + `MapEnvironment` |
| Encounter → AR entry prompt | ✅ `ZoneModePrompt` (ENTER AR / DIGITAL / Cancel) |
| Create AR duel (PvAI / PvP distance scan) | ✅ `ArDuelCreateScreen` / `PlayerVsPlayerCreateScreen` |
| Real-world **duel arena** shell | ✅ dual disks + midfield art (`ArDuelSpace`) |
| Life-size AR assets pipeline | ❌ (enlarged card art now; 3D models later) |
| AR menu variants | ⚠️ partial (disk/arena spatial; full dual chrome later) |
| `IArBackend` (Lens + PhoneCamera) | ✅ Phone = ARCore 6DOF (`ArFoundationSession`); lenses = OpenXR; webcam is fallback only |
| Multiplayer / events / raids | ❌ (hotseat PvP only) |

---

## Design rules of thumb

1. **Map first** — overworld is the default living world (Niantic baseline).  
2. **Prompt AR near encounters / arenas** — transition is never implicit.  
3. **AR is the emphasis** — life-size presence + real-world arenas, not a side mode.  
4. **Dual menus** — AR and non-AR for every surface.  
5. **Code AR for lenses; run it on phone camera** — one feature set, two backends.  
6. **Non-AR complete path** — hardware never gates core play.  
7. **Portrait phone** for non-AR chrome (not widescreen).  
8. **DuelEngine stays pure** — no GPS/AR imports.

---

## Suggested milestones

1. Stabilize portrait non-AR duel + GBA-style menu + deck picker  
2. Overworld map shell + tear / **arena** pins  
3. **Encounter proximity → AR entry prompt** UI beat  
4. `PresentationMode` + stub dual menu for home  
5. `IArBackend` PhoneCamera first → Lens adapter  
6. **Arena v0:** ground plane field + dual disks in AR  
7. **Life-size v0:** one spirit / one monster at human scale in arena → start duel  
8. Multiplayer / events on the same arena shell  

When adding a feature, checklist: **Non-AR?** **AR arena?** **Life-size implication?** **Intents shared?**
