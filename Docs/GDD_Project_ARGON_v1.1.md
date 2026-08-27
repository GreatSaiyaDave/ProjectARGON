Project ARGON
Game Design Document
by
David William Jewett Jr
Higher Levels Media, LLC
August 19, 2026
David.w.jewett.jr@gmail.com/(941) 421-2621

Disclaimer: This document incorporates licensed Yu-Gi-Oh! elements used as reference for a prototype. Original creative elements include the Umbrax narrative arc, Mr. Referobot (local Ollama model), dynamic AR tear and zone mechanics, Seal of Umbrax field spell, ritual zones, expanded Dungeon Dice Monsters and Deck Master formats, custom story and lore, inventory artifact cards, Spirit Dueler (in-game and wearable), build-your-own Duel Disk, Duel-Coin, and raid expansions.

The name is Project ARGON. Until a license is in place, the icon, splash, and any listing use Project ARGON.

This Game Design Document is the gameplay authority. Story prose lives in Ultimate Story Lore v5.0. Where this document and older notes disagree, this document wins.

1. Overview

Project ARGON is a free-to-play mobile game that mixes Yu-Gi-Oh! card dueling with Pokemon Go-style walking. Players move through their real neighborhood on a stylized Domino City map, find Tears, and duel in AR or on a digital field. The fantasy is simple: the anime is real, the monsters are here, and you are the duelist.

Every launch should feel like Battle City at night: neon, tension, holograms in the air. Story screens use a different look (parchment, Egyptian motifs, slower pacing) so the living world stays Battle City.

The long-term display is AR lenses. Daily work is a Galaxy S23 Ultra and a PC. The same duel systems run on phone camera AR and on a full digital path so glasses are never required.

Core pillars:

Social Dueling: Verbal announcements can award extra XP. Real meetups, IRL prizes, flair, and medals.

AR Immersion: Life-size spirits, ground-anchored arenas, dual Duel Disks. Same rules in AR and digital. Businesses can host arena kits.

Progression: Light anti-exploit on farming speed. Rare and overpowered cards sit behind raids, tournaments, prime Tears, and rituals.

Monetization: Free to play. Cosmetics and convenience only. No pay-to-win.

Accessibility: Colorblind-safe rarity marks, voice and typed commands, skippable Referobot tutorial, era-lock toggles, digital play when GPS or AR is unavailable.

Who it is for: anime fans who want presence and declarations, TCG players who want real structure, walkers who want their city as Domino City, and casual players who can stay on the digital path. Age gate is a date of birth on the device. Tone is teen and up.

Unity project: Unity 6 (6000.4.8f1) at DMWRLDZUnityProject/ProjectARGON. Package id com.wrldz.duelmonsters.

2. Story and Narrative

Full plot is in Ultimate Story Lore v5.0. This section is only what systems need.

Holactie creates the gods. Ptah leaves twin demigod sons with a mortal woman: Umbrax (elder, shadow, guardian) and Andrax (younger, light, teacher). They found Lemuria and a contract-barrier that is the ancestor of Shadow Games. Azathot curses Umbrax. The people burn him. Andrax buries the horned skull. Ptah and Anubis seal soul and skull. The prison becomes the Shadow Realm. Fractures of Umbrax include Zorc, the Supreme King, the Wicked Gods, and human vessels. Akun, of Andrax's line, carries a barrier-stone into Egypt. That stone is the seed of the Millennium Items.

Twenty-five years after Zorc's defeat, Crawford Pretoria raises the chest, uses the skull as a battery, and opens a rift. Umbrax sends Dibbyk to the night before Pegasus versus Yugi. Possessed Pegasus paints Umbrax's engine as cards. Tears open across realities. Three Kuribohs awaken Mr. Referobot and recruit duelists from our world.

2.1 Onboarding

Five to ten minutes. Returning users who already finished onboarding go to the overworld after login.

Splash, title, date of birth, account, terms, prologue, Kuriboh pick, gift summary, then the tutorial question.

Veterans may skip only the tutorial duel with Mr. Referobot. Prologue, Kuriboh pick, gifts, and terms are required.

Flow: a Tear rends the sky; Kuribohs guide the player; a fortuneteller hologram (the three Kuribohs fused) briefs the Umbrax threat; the player picks a mascot; they receive the Spirit Dueler and starter kit; they may duel Mr. Referobot (easy rules or full TCG); the disguise drops; the chosen Kuriboh becomes a silent companion; the other two leave to recruit; Referobot stays as referee and tutor.

Kuriboh teams (Kuribandit replaces the older Magickuriboh name):

Junkuriboh. Starter deck starter_junkuriboh.json. Story bond: Jamal Kaiba (Millennium Puzzle and the Pharaoh). EARTH scavenger. Highlights overlooked Tears. Scrap Squad: +10% Set Energy from Tears.

Galactikuriboh. Starter deck starter_galactikuriboh.json. Story bond: Jake Ryder, a spiritual echo of Andrax, not a one-to-one reincarnation. LIGHT cosmic. Trails to distant zones. Faster raid matchmaking later.

Kuribandit. Starter deck starter_kuribandit.json. Story bond: Layla Ishtar (Millennium Rod). DARK spell-thief. Hidden lore and artifact spots. +15% XP from story duels.

Players pick one. The others join later through story and events. All three unlock a Trio Fusion raid cosmetic later. After Season 5, players can make their own emblems and teams.

Starter kit: backpack, Spirit Dueler disk, Tome, home card box (1000), starter binder (5 free pages, 18 cards per page, max 20 pages / 360 cards), play deck box (Main / Extra / Side only), team starter deck, 500 Digizeni.

Travel is life-like. You take what fits in the pack and pockets. Collection can be viewed anywhere. Home cards do not jump into trades without a Trade Transport item. Default pockets: 2. Story unlocks a third. Max 6.

2.2 Seasons

Older plans listed nine anime-mirror seasons. The schedule is four story seasons, a cult climax, then live events.

Season 1, Duelist Kingdom, Possession of Pegasus. Players interrupt shadow Duelist Kingdom, hunt starchips. DK Mode fields. Climax: 50,000 LP team raid versus Umbrax-Pegasus. Codex: Umbrax origin. Bakura becomes aware; players do not know. Only the Pharaoh is aware of the player.

Season 2, Battle City, the Billionaire's Gambit. Rare Hunters. Artifact passes into Neo Battle City. Climax: Marik raid and 2v2. Crawford builds a biomechanical body around the skull. Umbrax absorbs Dibbyk. Duke Devlin, possessed by Orgoth, unlocks Dungeon Dice. Bakura escapes.

Season 3, Duel Academy, the Illusionist Demon. Amalek Onryo (Wicked God souls) corrupts the Sacred Beasts. Walking fills a purification meter. Climax: Herald of Andrax raid and Umbrax's cyber vessel. Andrax lore. Beast ally buffs.

Season 4, Virtual World, Sword of Damocles. Six locators, three sword fragments, Big Five and Noah mazes, Deck Master preview. Climax: Kuribohs, Herald, Andrax and the Sword versus Umbrax. The skull falls. Bakura crowns himself with it.

Season 5, Cult of Bakura. Zone retakes, ritual portals, Seal of Umbrax / Seal of Bakura, Deck Masters. Climax: defeat Bakura. The gods take notice.

After that: Tournament of the Creator Gods, New Game+ and live ops, later-era distortions, endless towers.

Cadence target: quarterly seasons, omens one week prior, a short gap after Season 4 for tuning.

2.3 Deck Master (Season 5 and after)

Off-field partner. Special Summon during Main Phase. If it is destroyed, that player loses unless it is replaced. Keeps its card effects plus a once-per-turn costed ability.

Story override: Kuriboh as Deck Master (negate opponent card damage once per turn, Special Summon fluffy tokens, team LP shield in story). Portal spirits cannot be Deck Masters without evolution (Andrax essence after Season 3).

Unlocks: post-Season 4 Noah token, cult rituals, rare raid drops, secret packs.

2.4 Seal of Umbrax / Seal of Bakura

One field, two names. Systems name: Seal of Umbrax. Season 5 story name: Seal of Bakura (Bakura distorts the Seal of Orichalcos).

In ritual zones it turns on by itself. All Fiend monsters gain 500 ATK/DEF. Once per turn: Special Summon 1-2 Madness Husk Tokens (Fiend/DARK/Level 1/1000/1000, no effects) or Tribute any number of Husks so that many monsters you control each gain 1000 ATK/DEF for the rest of the duel. Cultist bosses make extra tokens. The field also raises ritual density on the map.

Loss penalty (PvE): forfeit 50% accumulated card energy (rounded down) and 25% current Duelist XP. Level never drops. Recover through Referobot quests, dailies, or purification.

After Season 5, a PvP variant can be turned on in custom and training. Cost: 1000 LP per activation or effect. Tokens summon in Defense. The opponent may Tribute them under the written conditions.

3. Gameplay Mechanics

3.1 Core loop

The overworld map is home. Walk with GPS. Near a Tear, encounter, or arena the game always asks: Enter AR, Digital, or Cancel. It never auto-forces AR and never requires lenses.

AR loads a ground-anchored arena, dual disks, life-size (or stage-scale) holograms, and AR menus. Digital uses the same duel engine on a portrait field.

After the duel, rewards return the player to the map. Story and CPU grant Set Energy tagged to sets. PvP grants no Set Energy. Bazaar altars turn extra cards into Set Energy of that card's set.

A typical day: open into Battle City, walk, clear one to three Tears or a practice duel, spend or bank Set Energy, optionally hit a story node, raid window, or PvP, and leave with a visible next hook.

Scenes: Boot (onboarding only), Overworld (home), MainMenu (systems hub), DuelSlice (the duel). Desktop Lab opens in the Unity Editor when there is no phone or GPS.

3.2 Duels

The card engine is an anime TCG simulator. Structural rules stay honest (phases, summons, battle math, win conditions). Presentation leans on declarations and timed reactions rather than a frozen tournament pause menu.

Tap, voice, and typed announce all become the same command. Illegal lines are refused with a clear line such as "You can't Normal Summon twice this turn." There is no second rules path for voice.

Combat reactions race the animation. Example: the opponent declares Blue-Eyes White Dragon, direct attack. The charge starts immediately. Impact is about 5 seconds in the current slice, longer for heavier monsters. Before impact the defender may tap a Set trap, say "Activate Mirror Force!", or pass. No reaction: the hit lands. A trap that fires in time cuts the animation. Summons use a shorter appear window (about 1.5-3 seconds) for Trap Hole.

Rule backbone is Official Rulebook v10. Default LP 8000, opening hand 5, End Phase hand size 6. First player does not draw and has no Battle Phase on turn 1. Phases: Draw, Standby, Main 1, Battle, Main 2, End. Normal Summon or Set once per turn, tributes for Level 5-6 and 7+. Battle: ATK vs ATK, ATK vs DEF, direct; no default piercing. Win by LP 0 or deck-out. Set Traps and Quick-Plays cannot activate the turn they are Set.

If a card has official text but no registered script, it cannot be activated. The engine does not invent an effect.

Card text and art come from a local YGOPRODeck cache (about 1556 Season 1 cards). No hotlinking.

Life points by content: player and PvP 8,000; weak Tear spirits 4,000; after three Tear wins in a row 8,000; Tear Boss 10,000; raids 50,000-100,000; Duelist Kingdom Mode 2,000; Season 1 Umbrax-Pegasus raid 50,000 team.

Auto-wins such as Exodia: PvE gives base XP plus a unique bonus, no streak multiplier. Raids: no auto-win. A manual Exodia is bonus XP only.

Verbal announce examples: "I summon Dark Magician in Attack Position", "Set Celtic Guardian", "Activate Mirror Force!", "Attack directly", "I end my turn", "Pass". Names match against cards that are legal for that action right now.

Verbal announcements can add +10-50% XP, with extra for first damage, a hit over 2000 ATK, or a full field. That bonus is a Settings toggle. When the toggle is off, announce still plays the move. It just does not multiply XP. Mute, tap, and typed input always work.

Unity demo opponents: rule-based Easy / Medium / Hard (SimpleAi, AiHeuristicPolicy). The trained opponent and referee brain is Mr. Referobot, a local Ollama model under Master A.I. (ygo-agent). Story opponents should play in character. AI does not look at face-down cards or unused hand cards unless an effect allows it.

3.3 Formats and banlists

Standard TCG is the default. Banlists are end-of-era. Use the official Forbidden, Limited, and Semi-Limited list as it stood at the end of the latest set in the chosen era. Story PvE, constructed of that era, and ranked on that era token all share that snapshot. When a new list publishes, older eras do not change. Custom, GENESYS, and training may turn bans off.

Duelist Kingdom Mode: Season 1 PvE, some random NPCs, unlock token after Umbrax-Pegasus. No direct attacks, no LP damage from Spells, Traps, or effects, tribute-free summons, 2,000 LP, permanent fields.

Speed Duel: smaller decks and zones.

Dungeon Dice Monsters: unlock through Duke Devlin in Season 2.

Virtual World / Deck Master: preview in Season 4, token after Noah, full use in Season 5.

Raid format: shared contribution, Tome pages legal.

Pendulum and later Extra Deck tools stay story-locked until Season 5. Seasons 1-4 use era-legal pools. Season 5 opens a wider Extra Monster pool.

Overworld and Tear duels can roll a permanent field (DK Mode, Toon World, Yellow Luster Shield, and others) that cannot be overwritten. The Zone Mode prompt should say so.

3.4 Zones, Tears, and encounters

A pin on the map always has a job.

Tear: spirit waves and a Tear Boss. Duel.

Story / Time Distortion: level-gated chapters. Duel.

Raid: three-player bosses. Duel.

Tournament: brackets and ranked. Duel.

Bazaar: tablets, altars, vendors. No required duel.

Training: Referobot and Silent Magician. Duel, no or low XP.

Alliance Hub: guild space.

Prime Tear: rare, 24 hour.

Ritual / Cult (Season 5+): pop-up portals. Duel.

Archetype Invasion: group possession waves. Duel.

Tears: approach, Zone Mode prompt, AR or digital. Weak spirits at 4,000 LP. After three wins in a row, 8,000 LP. Beat three, then the Tear Boss at 10,000 LP with rares. Refreshes like a Pokestop. Prime Tears can drop locator cards (six locators point to a Millennium site within about two miles). After Season 4, Tear density drops and cult rituals take over the street.

Story zones: reality freezes, a Tear opens, a spirit related to the opponent's cards possesses them, the Pharaoh (through Jamal's Puzzle) or the era guide speaks, the player's Kuriboh volunteers them, they duel, the timeline reverts. Defeated spirits become energy for stone tablets.

Random encounters while walking: possessed civilians, random duelists, immunity spirits. Blueprint rate was about 20-30% per 100 meters; tune in playtest. No prompt while the player is in a menu or moving at vehicle speed. Prompts wait until the player is still or confirms. Do not ask people to stare at the phone in traffic.

3.5 Overworld

Default state after login. Stylized Domino City on real GPS, not a satellite photo.

Card shops become Kame Game Shop (tutorial and Bazaar). Towers become Kaiba Corp (raid and tournament). Plazas become Clock Tower Plaza. Parks become Domino Park (Tear hotspots). Waterfronts become the pier. Museums become Millennium and lore hubs. Schools become PvP arenas. Arcades become mini-games.

Pins may start from public points of interest, then shift through partnerships and Referobot placement so rural areas are not empty.

Avatar follows walking and cycling. Indoor and Editor use a walk pad. Night favors Fiends and Rare Hunters.

Walk rewards: 1-2 card-energy per mile, tagged to the current season or rotation, daily cap about 20. Weekly distance can grant an ultra pack and flair (example: 50 miles). Landmark scans grant XP, lore, and occasional packs.

3.6 Millennium Items

Tear-boss artifacts. Three uses. Activate at 500 LP remaining, once per duel. Always on in PvE. Optional in PvP. Not allowed in official tournaments.

Puzzle: draw a chosen card in Draw Phase, and/or 500 LP to a possessed opponent (can end the duel).

Rod: 500 LP stands in for a tribute.

Scale: 1000 LP damage if the opponent has more than 3000 LP.

Eye: reveal opponent hand and top deck.

Key: take an opponent monster for one turn.

Ring: Special Summon from the opponent Graveyard (no attack or tribute that turn).

Necklace: look at the next three cards of both decks.

Season 5 adds more artifacts on the same charge model. Show remaining uses on the card. Activation is a confirmed tap or announce, never an accident at 500 LP.

3.7 Raids

Three players. AI can fill with weak premade decks. 20-40 minutes. Six-hour daily windows, unlimited attempts in the window.

IRL matchmaking: about 50 meters GPS.

Remote raids are allowed. Same encounter, same rules, same length, same phases, same boss. Lower rewards than IRL so remote play cannot become the better farm.

Tiers: Normal (free), Heroic (artifacts), Mythic (Level 40+, leaderboards). Boss LP 50,000-100,000 in three phases. Bosses may use Forbidden or Limited cards as boss-only tools. Before the raid the team votes three shared Spells or Traps (Own Magic). Tome pages are legal. Silent Magician hosts training raids.

3.8 Tournaments

Mr. Referobot referees. Cheat path: warning, then disqualification or a temp ban.

Official: after a season, IRL and remote. Remote prizes are smaller so people still show up.

Custom: player brackets. No official Duel-Coin. Optional item gambles with Referobot holding the stake.

Ranked: weekly reset, monthly invitational, style points for theatrics.

Season 1 official: Level 20+, gloves and starchips, DK Mode, island overlay.

Hotseat PvP with a distance scan is in the current build. Networked PvP is later.

3.9 Progression

Duelist Level follows a Pokemon Go pace. Levels 1-49 move quickly then steady. 50+ slows hard. Soft cap 100 until the story is finished.

Content gates (separate from the 1-100 curve): Season 1 around 20, Season 2 around 30, Season 3 around 35, Season 4 around 40, Season 5 around 50 with prestige.

Perks every five levels. Example: Level 5 +10% Tear XP, Level 20 mascot upgrade, Level 40 custom disk flair. Spirit rank mirrors level. Level-up grants Digizeni, with extra at multiples of 10, Level 50, and Level 100.

XP mainly from duels. Wins pay more than losses. Longer duels pay a little more. Practice duels grant 0 XP. Streaks: 2x on a win, up to 5x, reset on a loss. Speed duels +20%. Zero-damage wins +50%. Full zone clears +30%. Theatrics +10-50% when the toggle is on.

Daily: three easy tasks. Weekly: archetype or format. Seasonal: story-tied. Battle pass: free and premium (premium costs Duel-Coin). Fifty tiers. Free track includes pack energy. Premium does not sell unique ladder cards.

Tome: real Spell and Trap ids as raid-only pages. Capacity is level divided by 10 (0 before Level 10, max 10). Each Kuriboh team seeds three classics at 10, 20, and 30. Show a lock badge outside Raid.

Rare Hunters (Season 2+): wander, gamble an uncommon or better for packs, taper after Season 2. On a player loss they steal one unused binder card that is not locked in a deck box, or 250 Digizeni if there is no card.

4. Characters and AI

4.1 Player avatar

Body, hair, eyes, outfit, accessory, tints, and title. Opened from the overworld profile orb and the hub. Use original pack-ins under StreamingAssets/WRLDZ/. Do not use Duel Monsters Online or Konami UI as game textures.

4.2 Mr. Referobot

KaibaCorp REFEREE ROBOT - BETA, woken by the three Kuribohs. Invisible to Umbrax spirits. Referee, quest giver, tutor, announcer, and table duelist when a match needs an AI seat.

Mr. Referobot is a local Ollama model in training. The training project on this machine is Master A.I. (folder ygo-agent, also linked as DM-WRLDZ-Master-AI).

How it works on this computer: the phone or Unity client is the mic, speaker, and mat. The PC runs the Referobot server (port 8788). Speech is turned into a legal option. The rules engine is the last word. If speech is unclear, he asks again. He never plays a move that is not on the legal list.

In Unity today: portrait, 3D stand-in (StreamingAssets/Models/Referobot), and scripted phase / illegal / win lines (MrReferobot.cs). The trained policy, speech matcher, Whisper STT, and TTS live in ygo-agent/referobot. A phone page already talks to that server. Hooking the same server into the ARGON Unity client is the next link.

Game personality modes (unlock at 100 and 1,000 duels, switchable): Strict Kaiba, Friendly Willow, Sassy Rival. The current training persona file is Mr. Referobot (Sal voice theme).

4.3 Immunity spirits

Witty Phantom: auctions and free cards at the Bazaar.

Time Wizard: timeline challenge duels.

Copycat: mirror-deck duels.

Kaibaman: zone challenges for Kaiba and Blue-Eyes energy.

Out-of-time Yugi: hunted by Bakura; friendly duels for Yugi and Dark Magician energy.

Duke Devlin (Season 2): possessed by Orgoth; unlocks Dungeon Dice.

Silent Magician: training-raid NPC.

4.4 Story champions

These are NPCs, not the player.

Layla Ishtar: Kuribandit and the Rod. Archaeologist.

Jake Ryder: Galactikuriboh. Astronaut trainee from Starbase, Texas. Echo of Andrax.

Jamal Kaiba: Junkuriboh and the Puzzle. Seto's son. The Pharaoh speaks through the Puzzle.

Crawford Pretoria: tycoon who raises the skull and builds the biomechanical body.

Dibbyk: Umbrax's avatar. Possesses Pegasus, then Marik. Absorbed when Umbrax takes the bio-body.

Bakura: aware from Season 1. Feigns the old timeline. Steals the skull at the Season 4 climax. Season 5 cult lord.

The Pharaoh (Atem): player-facing guide through Jamal's Puzzle.

5. Inventory and Economy

5.1 Currencies

Set Energy (SE): collection fuel, stored per set id. Earned from story, CPU decks, walking, Tears, raids, and altars. Spent at stone tablets for 10-card packs of that set. PvP never grants SE. Practice never grants SE.

Digizeni: soft cash from levels, duels, and dailies. Spent on Rare Hunter fees, extra storage, and convenience.

Duel-Coin: premium and cosmetics. Earned from tournaments, ranked, challenges, and optional ads. Spent on cosmetics, battle pass premium, and auctions.

Starchips and gloves: Season 1 tournament entry.

The HUD may show total SE. Real balances live per set (LOB, MRD, SDK, story packs, and so on).

5.2 Stone tablets

Earn SE of set X, offer it at a tablet, open a 10-card pack of set X.

Cost: 1,000 Set Energy of that set for one pack. Flat. Not scaled by set rarity. Pity still applies.

Altars turn extra cards into SE of that card's set, scaled by rarity, capped at about 500 SE (half a pack). Quest-bound cards and last copies locked in equipped decks need a confirm.

Other card sources: every 3 non-PvP wins grant a 10-card pack, with energy streaks up to 5x; trades and Witty Phantom auctions; raid monster-energy (about 10 packs of energy to unlock a copy); dailies; free battle pass; mascot item boxes.

Sets are visible in the catalog after the tutorial. Legal use follows era until Season 5. Target release cadence: about six months chronological, 10-set daily rotations with double-bonus windows.

5.3 Bazaar

A pure economic zone, not a duel of record. Place it at participating card shops, malls, and small businesses (Kame Game Shop flavor).

Floor: stone tablets, altars, shopkeepers, Witty Phantom auctions, a challenge board, seasonal vendors (Season 1 starchips).

The hub Bazaar is the same systems without GPS for indoor and lab play. The hub stub exists. Per-set SE, tablet opening, altar convert, and partner geofences are not finished.

5.4 Inventory limits

Default 1000-card box and 3 deck slots. Expand with Digizeni or Duel-Coin. Max 10 decks and 20,000 cards unless expanded further. Play decks never hold bulk.

6. Monetization

No pay-to-win. Do not sell win rate, Tear density, scripted effects, unique competitive cards, or raid power.

Revenue: cosmetics (avatar, mascot, monster skins, summon effects, sleeves, disk flair), battle pass premium (cosmetics and convenience energy), inventory expansion, optional rewarded ads, partnerships, optional Spirit Dueler hardware.

Duel-Coin for the demo is a normal premium currency. Earn it in game. Optional IAP or ads. No crypto in the S23 demo. An on-chain cosmetics layer can sit beside it later after legal review. It must never be the only path to power.

7. Hardware, AR, and UI

Minimum: Android 10+ or iOS 14+, ARCore or ARKit, GPS, optional microphone.

Lab device: Galaxy S23 Ultra. Product target: OpenXR / Android XR lenses. Wearable Spirit Dueler is optional and never required.

Presentation modes: Non-AR or AR. AR backend: Lens, Phone Camera, or none.

Every menu has a non-AR portrait version and an AR version. The duel engine does not import GPS or AR packages.

7.1 Spatial duel

The field is spatial. Cards live on the disks. Holograms sit in the arena between the players.

Your disk sits on the left arm. A mirror disk in front shows the opponent field only as the rules allow. The hand floats at the torso with backs toward the opponent. Card text appears on select only, on that player's screen. The arena between you shows holograms from face-up cards. Mr. Referobot stands as referee.

Selecting a card never reveals hidden information to the opponent.

Battle City disk layout (anime V2):

Five Monster slots on top of the blade stages, hub to tip. ATK upright, DEF sideways.

Five Spell and Trap slots directly under each monster stage, wearer side, tip still visible.

Field Spell in a drawer at the blade tip.

Main Deck in the hub well facing the wearer.

Graveyard beside the deck. Extra Deck inboard of the Graveyard.

Hand is not on the disk.

Empty field is the sculpted mesh only. Cards appear when played. No overlay pads or zone labels waiting for a card.

During Enter AR, the phone screen is companion systems only (profile, decks, bag, Tome, settings). No 2D duel board. Digital mode uses the full 2D field.

Phone now: rear camera passthrough plus a spatial stage. Lenses later: the same scene on the wrist and the floor. Fidelity may drop on phone. Size and presence stay. Do not add lens-only powers.

Optional wearable (after launch, about $50 target): holds Main, Side, and Extra, haptic button, custom LEDs and grips. Year two: physical card scan into digital. F2P never requires it.

7.2 UI

Readable first: white or soft gold on near-black. No gray on gray.

Minimal chrome. One job per screen. One-handed: primary actions in the lower 40% or right thumb.

Outdoor body text at least 16 design pixels, heavy outline.

Non-AR is portrait 1080x2340. GBA Sacred Cards / Worldwide Edition windows for non-AR chrome. Pokemon Go orbs and trays for map chrome. Yu-Gi-Oh energy lives on disks, arena, and cards.

Overmap top: level, Digizeni, Duel-Coin, Set Energy, Kuriboh, settings. Bottom: DECK, BAG, MENU, STORY.

Map icon priority when pins overlap: Tear, Event, Portal, NPC, Treasure.

Live duel HUD: LP, phase, one-line hint, hand, phase buttons, last one or two log lines. No settings sheets or binder grids during an active duel.

Collection tabs in this order: Main Deck, Extra Deck, Side Deck, Tome Deck, Binder, Trade.

Colors: Void #05070F, Panel #0C1220, Text #FAF8F4, Gold #FFD647, Cyan #33EBFF (player), Magenta #FF4798 (opponent).

7.3 Accessibility

Colorblind-safe rarity (pips, not color only).

Voice, typed announce, and tap are all valid.

Skip only the Referobot tutorial, not the rest of onboarding or story.

Subtitles on all voice-over.

Reduce-motion: skip attack flights, keep the impact timer.

Rural and indoor: digital path, Desktop Lab, pop-up pins.

8. Custom Story Cards

Raid and cutscene cards. Write final card text at implementation. Do not put these in constructed PvP without a separate pass.

Herald of Andrax. Xyz/Fairy 2500/3000. 3 Kuribohs. Detach 1: +1000 LP and +1000 ATK/DEF.

Andrax, the Omniscient One. Divine Beast 3000/3000. Tribute Herald. +3000 LP and +1000 ATK/DEF. Cannot Normal Summon or Set.

Sword of Damocles. Equip Spell. +1000 ATK/DEF for each monster the players control. If the equipped monster would be destroyed by a card effect, destroy this card instead.

Umbrax, The Tearer of Universes. Xyz/Fiend 0/0. 3 Wicked monsters. Steal with materials. Vulnerable only to Traps.

Umbrax, Mad God Incarnate. Divine Beast 4500/4000. Evolves from Tearer. Steal or negate a Trap. Unaffected by opponent monster and Spell effects.

Umbrax's engine grows by season: Season 1 steal, Season 2 summoning spells, Season 3 tokens, Season 4 recycle (Tear Rift).

9. Roadmap

9.1 In the Unity project today

Boot cascade, local accounts, Kuriboh pick, starter kit, avatar customizer.

Overworld map, Tear pins, Zone Mode prompt (AR / Digital / Cancel).

Dual-disk AR stage, phone passthrough, digital field.

Structural TCG engine, starter effects, timed reactions, verbal parser.

Rule-based Unity AI. Hotseat PvP distance scan. Mr. Referobot trained separately in Master A.I. (Ollama / ygo-agent); Unity still uses scripted announcer lines.

XP, inventory model, Rare Hunter steal, Tome capacity.

Desktop Lab. Season 1 card database and art.

9.2 Shareable demo

A stranger on an S23 Ultra can install, finish onboarding (skip only the Referobot tutorial if they want), see Battle City (GPS or indoor walk pad), approach a Tear, pick AR or Digital, finish a duel against AI (summon, set, respond, attack, end turn), see Referobot call the phase and the win, receive XP and some Set Energy, and return to the map.

Out of this demo: headset build, full seasons, raids, networked PvP, crypto, wearable, full 3D monsters, live Bazaar.

9.3 After the demo (Season 1)

Prologue and Duelist Kingdom story nodes with the isolated art style. Three Tear types in a real test city. DK Mode. Starchip loop (digital first is fine). 150-300 playable scripted cards. Tablets for two or three early sets. Easy, Normal, and Hard AI.

9.4 Later

Full 3D monsters, lens shipping, raids, guilds, Solana Duel-Coin, Spirit Dueler hardware, physical card OCR, home-base 3D storage, Konami partnership, global servers.

Priority: GPS map and Tears, honest TCG subset, AR presentation, AR prompt and dual menus, rule-based AI, onboarding (those are first). Then story prologue, Set Energy and tablets, Bazaar. Then ranked and networked PvP, raids. 3D monsters, wearable, and crypto wait.

9.5 Legal

Prototype uses local card data and art. No Konami marks on the Project ARGON listing. Do not ship scraped UI as textures. Outreach after a polished demo.

Original IP: Umbrax, Andrax, Lemuria, Azathot, Dibbyk, Crawford Pretoria, Layla, Jake, Jamal as modern champions, Tear and Zone layer as designed here, Mr. Referobot, Seal of Umbrax, ritual zones, Project ARGON Deck Master and Dungeon Dice modes, artifact inventory, Spirit Dueler, Duel-Coin, raid expansions. Replace Kuriboh presentation art before a commercial ship if there is no license.

10. Decisions locked 19 August 2026

Name: Project ARGON.

Tablet pack cost: 1,000 Set Energy of set X equals one 10-card pack of set X.

Remote raids: same fight as IRL, lower rewards.

Voice XP: toggle in Settings.

Veteran skip: only the Mr. Referobot tutorial duel.

Banlist: end of era. The official list at the end of the latest set in the chosen era.
