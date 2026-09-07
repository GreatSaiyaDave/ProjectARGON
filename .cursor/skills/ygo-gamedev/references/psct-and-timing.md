# PSCT and Fast Effect Timing

Summarized from Konami Fast Effect Timing, YGOrganization PSCT digest, Yugipedia PSCT, and TCGPlayer's PSCT explainer. See [sources.md](../sources.md).

## Does it start a Chain?

An effect starts a Chain **if and only if** its text contains a colon `:` or a semicolon `;`.

```
[activation condition]: [cost and targeting]; [resolution]
```

- Text before `:` is the **activation condition**. It must be true to activate. Unless the card says otherwise, it need not still be true at resolution.
- Text between `:` and `;` is **activation procedure**: costs and targeting. Targeting is **not** a cost.
- Text after `;` (or after `:` when there is no cost/target) is **resolution**.

No colon and no semicolon → not an activated effect (continuous, lingering, summon procedure, etc.).

## Targeting

An effect targets **if and only if** the print uses the word **target** (at activation, so before `;`).

At resolution, wording decides whether the target still must meet those conditions:

| Print at resolution | If the target is no longer legal |
|---|---|
| "destroy **that target**" | Effect fails on that card |
| "destroy **it** / **them**" | Often still applies if the card is the same object |
| "**both/all those targets**" | All must still be legal or the whole effect does nothing |
| "**those targets**" | Check each target separately |
| "**them**" | Does not re-check targeting conditions |

If a targeted card **changes location**, game mechanics usually prevent resolving against it.

Gagagabolt-style example: "If you control a Gagaga: Target 1 card; destroy that target." The Gagaga is only required **to activate**. Missing it at resolution does not stop the destroy.

## Conjunctions (resolution only)

These do **not** change activation legality. They answer two questions: are A and B simultaneous for "when" timing, and does B depend on A succeeding?

| Conjunction | Timing | If A fails |
|---|---|---|
| **and if you do** | Simultaneous | Skip B |
| **also** | Simultaneous | Still do B |
| **then** | Sequential (A then B) | Skip B |
| **also, after that** | Sequential | Still do B |
| **and** (alone) | Simultaneous | If either part fails, do **nothing** |

If B cannot be done, A is still done (except **and**).

This is why "when … leaves the field" optional triggers **miss timing** after a `then` (A and B are not both last things to happen) but can still see `and if you do`.

## When vs if

- **When** optional: the trigger event must be the last thing that happened. Something else resolving first → missed timing.
- **If**: the event happened this window; it does not miss that way.

Store this on the clause (`ConditionCheckedAt` in ARGON). Do not collapse both to a Unity `OnEnable`.

## Spell speeds

| Speed | Typical | May chain to |
|---|---|---|
| 1 | Ignition, Normal/Field/Equip/Continuous Spell (activation), Trigger | Only an open game state (not to a faster link) |
| 2 | Quick Effects, Quick-Play Spells, Traps (and Trap effects) | Speed 1 or 2 |
| 3 | Counter Traps | Speed 1–3 |

Fast effects = Speed 2+. Either player may use them on the opponent's turn when legal.

## Fast Effect Timing (open vs closed)

Official chart boxes, paraphrased:

- **Box A — open game state.** Only here may the turn player Normal Summon/Set, Ignition, Speed 1 Spell, non-chain Extra summons, battle position, declare attack, normal draw, or try to leave the phase.
- After a **non-chain action** (successful Normal Summon, Set, attack declaration, …): check **triggered** effects (yellow). If any, they go on the Chain (SEGOC), then **Box D** chain-building. If none, turn player may use a fast effect (**B**), then opponent (**C**).
- After a **chain resolves**: again check triggers, then B/C. The game is **not** immediately open.
- Open again only when the turn player could go next, both players pass, and no chain is forming.

SEGOC: mandatory then optional, turn player then opponent, then the opponent of the last trigger may fast-effect. Never let a Unity `event` fire Torrential before SEGOC finishes.

Turn player passing a phase still gives the opponent a fast-effect window.

## Damage Step

Its own restricted legality, not "Battle Phase = true". ATK/DEF modifiers and a short list of cards are legal; generic Normal Spells are not. ARGON must keep Damage Step sub-steps, not a boolean.

## Cost vs effect (engine)

Costs happen at activation, before the link exists. Negating the activation or the effect never refunds the cost. Targeting happens at activation but is not a cost. Resolution primitives (`Destroy`, `Draw`, …) never run in the cost function.
