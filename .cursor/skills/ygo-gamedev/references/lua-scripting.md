# Card data + Lua scripting (EDOPro / YGOPro)

Summarized from Project Ignis scrapi-book and CardScripts wiki, YGOPRO Scripting Wiki, Duelists Unite cookbook, and indexed captions from community scripting videos (see [sources.md](../sources.md)).

ARGON product duels do **not** run this Lua. Use it to understand the canonical effect model and to lint. Lab duels do run Ignis scripts through ocgcore.

## Three files, one passcode

| Piece | Name | Role |
|---|---|---|
| Database | `cards.cdb` (SQLite) | Stats, type bits, printed `desc` |
| Script | `c<passcode>.lua` | Behavior (`initial_effect`) |
| Image | `<passcode>.jpg/.png` | Presentation only |

Normal non-Pendulum monsters may omit a script. Everything else needs `initial_effect` or the core errors when the duel starts.

EDOPro expansions layout (easy to typo):

- `expansions/*.cdb`
- `expansions/script/c########.lua` (singular **script**)
- `expansions/pics/########.jpg` (no leading `c`)
- Field backgrounds: `expansions/pics/field/########.jpg` (~512²)

Do not edit EDOPro's shipped official CDBs; add your own file. Custom passcodes: **9 digits**, avoid reserved Ignis ranges. Official TCG/OCG IDs are up to 8 digits.

Community video workflow (YGOPro-era): copy `cards.cdb` into expansions so you do not mutate the stock DB; edit with SQLite Expert / DataEditorX / DB Browser; draw frames in a card maker; script in LuaEdit or Notepad++.

## CDB schema

`datas` and `texts` join on `id`.

**datas:** `id`, `ot` (region bitmask; TCG is commonly bit 2), `alias` (alternate-art / treated-as), `setcode` (archetype packed hex), `type`, `atk`, `def`, `level` (also pendulum scales in high bits), `race`, `attribute`, `category`.

**texts:** `name`, `desc`, `str1`–`str16` (prompt strings; `aux.Stringid(id, n)`).

`alias != 0` rows are usually extra arts — skip them when listing unique cards.

## Script skeleton (Ignis)

```lua
local s,id=GetID()
function s.initial_effect(c)
	local e1=Effect.CreateEffect(c)
	e1:SetType(EFFECT_TYPE_SINGLE+EFFECT_TYPE_TRIGGER_O)
	e1:SetCode(EVENT_SUMMON_SUCCESS)
	e1:SetTarget(s.target)
	e1:SetOperation(s.operation)
	c:RegisterEffect(e1)
end
function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
	-- chk==0: "could this activate?"
end
function s.operation(e,tp,eg,ep,ev,re,r,rp)
end
```

`GetID()` replaced hardcoded `function c12345678.initial_effect` so pre-release ID swaps do not rewrite every callback name.

### CCTO (map to PSCT)

| Setter | Print | Engine moment |
|---|---|---|
| `SetCondition` | Before `:` when `EVENT_*` is not enough | Activation check |
| `SetCost` | Before `;` excluding targeting | Paid at activation |
| `SetTarget` | Targeting + "is this legal?" (`chk`) | Activation; `Duel.SetOperationInfo` |
| `SetOperation` | After `;` | Resolution |

Continuous/static effects use `SetValue` instead of `SetOperation`.

Engine tests **condition → cost → target** before the link is legal.

### Types you will see constantly

- `EFFECT_TYPE_SINGLE` vs `FIELD` — event on this card vs any card
- `EFFECT_TYPE_TRIGGER_O` / `TRIGGER_F` — optional vs mandatory trigger
- `EFFECT_TYPE_IGNITION` — Speed 1, open game state, `SetRange`
- `EFFECT_TYPE_QUICK_O` + `EVENT_FREE_CHAIN` — Quick Effect
- `EFFECT_TYPE_ACTIVATE` — Spell/Trap activation
- `EFFECT_FLAG_CARD_TARGET` — print says target
- `SetCountLimit(1)` — once per turn (instance); name-based OPT is a different flag

Helpers such as `aux.AddEquipProcedure` register **both** the activate effect and `EFFECT_EQUIP_LIMIT`. Prefer helpers over reinventing.

### Target callback pattern

When `chk==0`, return whether a legal target exists (do not select yet). When `chk!=0`, ask the player, then `Duel.SetOperationInfo`. On resolve, `Duel.GetFirstTarget()` / `GetTargetCards()` and `IsRelateToEffect`. Skipping `SetOperationInfo` makes other cards (that look at categories) wrong even if your card "works".

## Events

The core raises 70+ `EVENT_*` codes (`EVENT_SUMMON_SUCCESS`, `EVENT_TO_GRAVE`, `EVENT_FREE_CHAIN`, …). `SUMMON_SUCCESS` is the summon **not negated**. Field triggers need `SetRange`.

## Testing

Project Ignis ScriptChecker loads `constant.lua` + `utility.lua` then each `cX.lua` into ocgcore and calls `initial_effect`. That catches missing `end` and crashes in init — not full ruling tests. Play the card in EDOPro (or ARGON OCG lab) for chains.

## What not to do in ARGON product code

- Do not paste `c########.lua` into `Assets/`.
- Do not grow per-card C# branches because a Lua file had a clever filter.
- Do mine Lua for **behavior facts** (`Tools/extract_ygopro_*.py`) onto closed vocabulary flags.
