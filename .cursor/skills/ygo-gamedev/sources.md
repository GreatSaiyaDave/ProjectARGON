# Sources (training set)

Collected 2026-09-07 for `.cursor/skills/ygo-gamedev`. Knowledge in the references is **paraphrased**. Do not paste copyrighted rulebooks, wikis, or video transcripts into game assets or chat dumps.

## Method

1. **Web scrape (public docs):** Konami Fast Effect Timing, YGOrganization PSCT, Yugipedia PSCT, Project Ignis scrapi-book + CardScripts wiki, YGOPRO Scripting Wiki, YGOPRODeck API v7, edo9300/Fluorohydride ocgcore READMEs, Duelists Unite scripting cookbook, ARGON engine docs already in this repo.
2. **YouTube captions:** Cloud IPs are blocked by YouTube timedtext (`youtube-transcript-api` → `RequestBlocked`). Used **search-indexed captions and descriptions** for the videos below, not a full timedtext dump.
3. **Out of scope:** DuelingBook private replays, Master Duel packet capture, shipping AGPL Lua as product rules.

## Videos (indexed captions / descriptions)

| ID | Title | What was used |
|---|---|---|
| [T0kRHJq02DY](https://www.youtube.com/watch?v=T0kRHJq02DY) | Ygo Custom Card scripting tutorial Part 1 | Indexed caption: copy `cards.cdb` into expansions, SQLite Expert vs stock DB, LuaEdit/Notepad++, TCG Editor / card maker, do not edit the live official CDB |
| [V_6qse-hSOg](https://www.youtube.com/watch?v=V_6qse-hSOg) | How to Lua script cards in YGOPro (Jack Ward, 2013) | Description: LuaEdit 2010; classic `cID.initial_effect` era |
| [vlbDDTQmmeI](https://www.youtube.com/watch?v=vlbDDTQmmeI) | EDOPro Tip: Making Custom Skill Cards | Indexed caption: card maker → copy an example skill Lua → mark custom skill in the GitHub CDB |
| [8JM0g-e4CXM](https://www.youtube.com/watch?v=8JM0g-e4CXM) | Reverse engineering Edo's Yu-Gi-Oh PC game | Indexed caption: early fan client, source-available, VS6-era; historical only |
| [i0omT5dG6w4](https://www.youtube.com/watch?v=i0omT5dG6w4) | Card game animations, DoTween + Mirror | Indexed caption: authority command then client RPC/tween; spawn scale-0 then ease |
| [gx0Lt4tCDE0](https://www.youtube.com/watch?v=gx0Lt4tCDE0) | Unity custom event system | Indexed caption: parameterized actions, unsubscribe on destroy — **UI bus, not the YGO chain** |
| [5W9sNk5ffrk](https://www.youtube.com/watch?v=5W9sNk5ffrk) | Basic Unity card battle | Indexed caption: hand CanvasGroup raycasts gated by turn; deck as data on a component |
| [jeK_5XD1XBM](https://www.youtube.com/watch?v=jeK_5XD1XBM) | Deckbuilding card game in Unity (ep. 1–3) | Listing only; timedtext blocked |

## Written docs

- https://www.yugioh-card.com/en/play/fast-effect-timing/
- https://ygorganization.com/summary-of-psct/
- https://yugipedia.com/wiki/Problem-Solving_Card_Text
- https://yugiohblog.konami.com/articles/?tag=problem-solving-card-text
- https://projectignis.github.io/scrapi-book/getting-started/setup.html
- https://github.com/ProjectIgnis/CardScripts/wiki/A-basic-scripting-tutorial
- https://ygoproscripting.miraheze.org/wiki/Structure_of_a_card_script
- https://ygoproscripting.miraheze.org/wiki/Functions_for_conditions,_costs,_activation_procedures,_and_resolution_of_an_effect
- https://ygoproscripting.miraheze.org/wiki/Setup_for_adding_cards_into_a_simulator
- https://forum.duelistsunite.org/t/ygopro-scripting-cookbook-compatible-with-omega/24782
- https://ygoprodeck.com/api-guide/
- https://github.com/Fluorohydride/ygopro-core
- https://github.com/edo9300/ygopro-core
- Repo: `MASTER_ENGINE_SPEC.md`, `OCGCORE_LAB.md`, ERAZ compile-gate spec, `EXTERNAL_LOG_SOURCES.md`

## Refresh

If YouTube timedtext is reachable (local machine, not a datacenter IP), fetch captions with `youtube_transcript_api` for the IDs above, **summarize**, and update the video table. Do not commit raw `.vtt` or full transcripts.
