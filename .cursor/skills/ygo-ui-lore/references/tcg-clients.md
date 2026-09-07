# TCG video-game UI (layout grammar)

ARGON is not a Konami client. These titles share a *grammar* we reuse: Life Points always visible, phase as a gold/center status, field zones as the board, GY/Deck as glanceable piles, card actions on the card — not a wall of menus.

Do not copy art, fonts, or pixel layouts. Copy the information architecture.

## Shared duel HUD (every honest TCG client)

| Element | Where it lives | Why |
|---------|----------------|-----|
| **Your LP** | Your corner (usually left / near you) | Win condition; pulse on damage |
| **Opponent LP** | Opposite corner | Same; color-coded vs you |
| **Phase** | Center, short name | Rulebook turn structure, not a debug string |
| **Phase advance** | Thumb / bottom, only when legal | Battle / Main 2 / End — GBA “phase menu” |
| **Hand** | Near you, faces toward you | Opponent sees backs |
| **Monster row / S/T row** | Your half of the field | ATK upright, DEF sideways (or a DEF badge) |
| **Deck / Extra / GY** | Piles at the edge of your half | Counts + tap GY to browse |
| **Card inspect** | On select only | Full text; auto-dismiss |

Forbidden during an active duel: full settings sheets, dense binder grids, overlapping modals.

## Master Duel (full TCG, desktop/console)

- Stage is a dark arena. Chrome is thin.
- LP on opposite sides; mate/avatar as identity, not chrome.
- 5 Main Monster Zones + 5 S/T + Field Zone + Deck + Extra + GY + Banished + 2 Extra Monster Zones in the middle.
- Phase buttons highlight the current phase; you tap to advance when legal.
- Hand fans at the bottom; inspect is a large face + text, not a second board.
- ARGON takeaway: **void stage + opposite LP + gold phase**. Do not import the full 2D zone grid onto the AR street.

## Duel Links (phone, Speed Duel)

- 3 monster + 3 S/T, 4000 LP, 20–30 card decks, Skill card beside the field.
- HUD is thumb-first: LP bars, compact field, character skill as a portrait ability.
- ARGON takeaway: **phone glanceability**. Speed Duel is a *format token* in ARGON, not the default living-world duel (default is 8000 LP / 5 zones unless the format says otherwise).

## Legacy of the Duelist / World Championship (home)

- 3D field, cinematic summons, then snap back to a readable board.
- Campaign maps and deck editor are separate rooms from the duel.
- ARGON takeaway: **summon theater, then HUD**. Combat reactions race the animation (`ANIME_TCG_ENGINE.md`); do not freeze a tournament pause menu over the holo.

## Nightmare Troubadour (DS) / Tag Force (PSP)

- Touch/tap a card → command list (Summon / Set / Activate / Attack).
- B / phase menu advances the turn. Details are a sidebar, not a full-screen takeover.
- ARGON takeaway: **actions live on the card**; phase CTAs are a second, smaller cluster.

## Sacred Cards / Worldwide Edition / 7 Trials (GBA)

- Overworld is a city map (Domino / Battle City districts) with Start = Status / Trunk / Deck.
- Duel screen: cursor on cards; A = command; B = sub-menu (Details, Turn End); LP and GY visible in that sub-menu or a persistent strip.
- PRODUCT_VISION nostalgia: **command windows**, not beige Kenney panels. Use piano-glass chips with cream type.

## Forbidden Memories / early PS1

- Ritual/fusion as a diegetic act, heavy atmosphere, weak HUD.
- ARGON takeaway: **story duels can dim chrome**; ranked/TCG duels cannot hide LP.

## Dungeon Dice Monsters / other formats

- Different board, different HUD. Unlock as a format token (Duke / Orgoth in Season 2). Do not reuse TCG zone chrome for dice rooms.

## Deck editor (Neuron / MD / Links)

- Collection fills **from the left** of the well. Preview + add/remove on the other pane.
- Main / Extra / Side stay on screen together when possible (EDOPro / Master Duel construction table).
- Search is a single bar; filters are chips, not a second column that clips the Game view.
- Do not pin scroll **content** with phase-normalized anchors (that packed ARGON's CARD LIST to the far right).

## Menus outside the duel

- **Hub**: destination grid (duel first, then collection / bag / story). Tag Force / GBA “card shop + map” energy, Battle City rain behind glass tiles.
- **Overworld**: Pokémon GO template (`GO_CHROME.md`) — map is the hero; YGO flavor is pins, disks, Tears.
- **Story**: slower, parchment, gold. Never the same sheet as the street HUD.
