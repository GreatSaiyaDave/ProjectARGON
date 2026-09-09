import json,re
from pathlib import Path
root=Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')
# Verify C# files contain expected fragments
checks=[
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs','public const int Version = 55'),
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs','target 1 ""([^""]+)"" monster in'),
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs','unaffected by \\"necrovalley\\"'),
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/LegacyTextTemplates.cs','(?:^|(?<=\\.\\s))Add 1 ""([^""]+)""'),
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/LegacyTextTemplates.cs','(?:^|(?<=\\.\\s))Add 1 Level'),
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/PhaseTriggerTemplates.cs','RxOppStandbyTheyGainLp'),
 (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs','clause.Side == EffectSide.Opponent ? opp : who'),
 (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs','Deck-Grok P1 wander searchers'),
 (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs','CheckFilterSs(83011278, "Mystic Tomato"'),
 (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs','CheckParked(81210420, "Magical Hats")'),
]
for path,needle in checks:
    t=path.read_text(encoding='utf-8')
    ok=needle in t
    print(('OK' if ok else 'MISSING'), path.name, needle[:60])

# Simulate Birdface: legacy anchored should NOT match mid-text
leg_named=re.compile(r'(?:^|(?<=\.\s))Add 1 "([^"]+)"(?: card)? from your Deck to your hand(?:\. Your Deck is then shuffled)?\.?', re.I)
bird='When this card is destroyed by battle and sent to the GY: You can add 1 "Harpie Lady" from your Deck to your hand.'
print('Birdface legacy named match?', bool(leg_named.search(bird)))
toon='Add 1 "Toon" card from your Deck to your hand.'
print('Toon Table legacy named match?', bool(leg_named.search(toon)))
rota=re.compile(r'(?:^|(?<=\.\s))Add 1 Level (\d+) or lower (\w+)(?:-Type)? monster from your Deck to your hand\.?', re.I)
print('ROTA match?', bool(rota.search('Add 1 Level 4 or lower Warrior monster from your Deck to your hand.')))

# Rite PSCT target
rite_tgt=re.compile(r'target 1 "([^"]+)" monster in (?:your|the) (?:GY|Graveyard)', re.I)
rite='Target 1 "Gravekeeper\'s" monster in your GY; Special Summon that target. This card\'s activation and effect are unaffected by "Necrovalley".'
print('Rite target', rite_tgt.search(rite).group(1) if rite_tgt.search(rite) else None)
boiler=re.compile(r'unaffected by "necrovalley"', re.I)
print('Rite boiler', bool(boiler.search(rite)))

snatch=re.compile(r"During each of your opponent's Standby Phases:\s*They gain (\d+) Life Points\.?", re.I)
# Necrovalley leftover + HHG summon
gy_move=re.compile(r'Negate any card effect that would move a card in the (?:GY|Graveyard) to a different place', re.I)
gy_tgt=re.compile(r'Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?"([^"]+)")?', re.I)
nv_psct='All "Gravekeeper\'s" monsters on the field gain 500 ATK/DEF. Neither player can banish cards from the GYs. Cards in the GY cannot be targeted, except by the effect of "Necrovalley".'
print('NV target except', gy_tgt.search(nv_psct).group(1) if gy_tgt.search(nv_psct) else None)
hhg=re.compile(r'If any "([^"]+)" or "([^"]+)" is Normal or Special Summoned:', re.I)
print('HHG names', hhg.search('If any "Harpie Lady" or "Harpie Lady Sisters" is Normal or Special Summoned: The player who conducted the Summon targets 1 Spell/Trap on the field; that player destroys that target.').groups())
print('Snatch LP', snatch.search("Equip only to a monster your opponent controls. Take control of the equipped monster. During each of your opponent's Standby Phases: They gain 1000 Life Points.").group(1))
