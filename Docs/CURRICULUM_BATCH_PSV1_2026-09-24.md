# Curriculum batch — PSV tranche 1 + SRL/MRD leftovers (compiler v54)

Date: 2026-09-24. Follows `Docs/CURRICULUM_BATCH_SRL1_2026-09-23.md`.

## Result

| Set | Before | After |
|-----|-------:|------:|
| MRD — Metal Raiders | 125/144 (86.8%) | **126/144 (87.5%)** |
| SRL — Spell Ruler | 74/104 (71.2%) | **80/104 (76.9%)** |
| PSV — Pharaoh's Servant | 42/105 (40.0%) | **66/105 (62.9%)** |
| IOC — Invasion of Chaos | 36/112 | 37/112 (Dark Driceratops, shared piercing) |
| All 15 pre-Link curriculum sets | 641/1471 | 673/1471 (798 left) |

Seed re-exported at v54 (536 programs). Headless 1216 pass / 0 fail, 250 AI-vs-AI duels
completed, 0 soft-locks, 0 exceptions, Python UI guards pass.

## Cards compiled (31 + 1)

PSV: Gearfried the Iron Knight, Minor Goblin Official, Bubonic Vermin, Nobleman of
Extermination, Limiter Removal, Solomon's Lawbook, Time Seal, Solemn Wishes, Backup Soldier,
Ceasefire, Invitation to a Dark Sleep, Infinite Dismissal, Bombardment Beetle, Shadow of Eyes,
The Fiend Megacyber, Rain of Mercy, Premature Burial, Buster Blader, Mad Sword Beast, Drill Bug,
Monster Recovery, Enchanted Javelin, Fairy Meteor Crush, Gift of The Mystical Elf.
SRL: Ameba, Griggle, Dark Zebra, Darkness Approaches, Final Destiny, Hiro's Shadow Scout.
MRD: Elegant Egotist. Bonus (shared template): Dark Driceratops.

## Shared pieces added

- **"Activate only when …" conditions**, checked at activation only: opponent LP ≤ N, N+ monsters
  in your GY, a face-up named monster (honors "always treated as"), a face-up monster of a Type
  you control, the opponent controls N more monsters, a monster on the field, another card in hand.
- **Discard N cards as a cost** (Darkness Approaches 2, Final Destiny 5): the player picks each
  card; nothing is discarded until all are chosen, so Cancel is free. The AI discards its
  cheapest cards and never an Exodia piece while another card will do.
- **Control-change triggers** (Ameba / Griggle), once while face-up, fired from the one
  control-change function (Change of Heart, Snatch Steal, End-Phase return).
- **Draw trigger** (Solemn Wishes): once per draw, not per card; never for the opening hand.
- **Piercing** for "this card" (Mad Sword Beast, Dark Driceratops) and "the equipped monster"
  (Fairy Meteor Crush).
- **Skip your next Standby Phase** (Solomon's Lawbook).
- **Summoned-this-turn sweep** (Infinite Dismissal): Normal/Tribute/Flip Summoned Level ≤ 3,
  both players' End Phases; Sets, Special Summons and effect flips are not marked.
- **Attack lock tied to a face-up source** (Invitation to a Dark Sleep).
- **Face-down reveal-and-destroy** (Bombardment Beetle: Effect Monsters only; Nobleman of
  Extermination: banish, and a Trap also leaves both Decks).
- **Flip without FLIP effects** (Ceasefire, Shadow of Eyes answers a Set monster).
- **Double ATK then destroy in the End Phase** for a Type (Limiter Removal).
- **Place a named card on top of the Deck** (Drill Bug), **the opponent draws and discards the
  Spells drawn** (Hiro's Shadow Scout), **shuffle a monster + your hand into the Deck and redraw**
  (Monster Recovery, owned monsters only), **Special Summon from hand or Deck by name**
  (Elegant Egotist), **up to 3 non-Effect monsters from GY** (Backup Soldier; a non-effect
  Fusion returns to the Extra Deck).
- **Gearfried**: Equip Cards attached to it are destroyed (so Premature Burial on Gearfried
  destroys both, as ruled).

## Engine fixes found on the way

- A card leaving the field to the hand, Deck or banishment now unlinks its Equip Cards (they go
  to the GY), and an equip that leaves unlinks from its monster. Call of the Haunted-style links
  destroy the monster; Premature Burial only when Burial itself was destroyed.
- A monster that re-enters the field starts fresh: "once while face-up" limits, attack locks,
  End-Phase self-destruction and until-end-of-turn ATK changes do not carry over. Turning a
  monster face-down also ends its face-up-only marks.
- A monster Special Summoned from the hand by its own effect (The Fiend Megacyber) is no
  longer sent to the GY as if it were a finished Spell.
- Special Summoning from the Deck now shuffles the Deck.

## Deferred (39 left in PSV)

7 Completed (needs an ATK-or-DEF choice in the duel screen), Pumpking the King of Ghosts (MRD;
depends on Castle of Dark Illusions), Jinzo / Imperial Order / Prohibition / Light of
Intervention / Magic Drain (rule-lock and negation), Magical Hats, Mirror Wall, Kiseitai,
DNA Surgery, Respect Play, The Eye of Truth, Inspection, Metal Detector, Driving Snow,
Dimensionhole, Parasite Paracide, Armored Glass, Gamble, The Shallow Grave, Appropriate,
Mystic Probe, Lightforce Sword, Sword Hunter, Vampire Baby, Shift, Cold Wave, Earthshaker,
Graverobber, Gust, Forced Requisition, Goblin Attack Force, Morphing Jar #2, Ground Collapse,
Insect Imitation, Skull Invitation, Major Riot, World Suppression, The Regulation of Tribe.
