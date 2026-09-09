#!/usr/bin/env python3
"""File-only verify for Deck-Grok P1 battle shared kinds (no Unity Editor)."""
import re, json
from pathlib import Path

root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')
checks = [
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs',
     'public const int Version = 55'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/MonsterTriggerTemplates.cs',
     'RxAttackedAfterDmgReturn'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/MonsterTriggerTemplates.cs',
     'RxEndDmgStepBattledNotDestroyedReturn'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/MonsterTriggerTemplates.cs',
     'RxAfterDmgBattlesBanishBoth'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/MonsterTriggerTemplates.cs',
     'AlsoBanishThis = true'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledCardProgram.cs',
     'AfterDamageCalculation'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledCardProgram.cs',
     'EndOfDamageStep'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledCardProgram.cs',
     'RequiresBattledMonsterNotDestroyed'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs',
     'NotifyAfterDamageCalculation'),
    (root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs',
     'NotifyEndOfDamageStep'),
    (root/'Assets/Scripts/WRLDZ/Duel/DuelEngine.cs',
     'NotifyAfterDamageCalculation'),
    (root/'Assets/Scripts/WRLDZ/Duel/DuelEngine.cs',
     'NotifyEndOfDamageStep'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Wall of Illusion FullyCompiled after-dmg bounce attacker'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Hyper Hammerhead FullyCompiled end-DS bounce if survived'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'D.D. Warrior Lady FullyCompiled after-dmg optional banish-both'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Exiled Force FullyCompiled Tribute this'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Cannon Soldier FullyCompiled Tribute 1'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Harpie Lady Sisters FullyCompiled summon-restriction-only'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'CheckParked(93920745, "Penguin Soldier")'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'CheckParked(94004268, "Amazoness Swords Woman")'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'CheckParked(65240384, "Big Shield Gardna")'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'CheckParked(68540059, "Metalmorph")'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'CheckParked(2460565, "Marauding Captain")'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'uniqueOnly'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Necrovalley FullyCompiled GK aura'),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     "Harpies' Hunting Ground FullyCompiled"),
    (root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
     'Tornado Wall FullyCompiled'),
]
ok = True
for path, needle in checks:
    t = path.read_text(encoding='utf-8')
    hit = needle in t
    print(('OK' if hit else 'MISSING'), path.name, needle[:70])
    ok = ok and hit

db = json.loads((root/'Assets/StreamingAssets/Cards/cards_db.json').read_text())
idx = {int(c['id']): c for c in db['cards']}
anchors = {
    13945283: 'after damage calculation: Return that monster to the hand',
    2671330: "battled this card is not destroyed: Return that opponent's monster",
    7572887: 'banish that monster, also banish this card',
    74131780: 'Tribute this card to target 1 monster on the field; destroy that target',
    11384280: 'Tribute 1 monster; inflict 500 damage to your opponent',
    12206212: 'Must first be Special Summoned with "Elegant Egotist"',
    93920745: 'target up to 2 monsters on the field; return those targets to the hand',
    94004268: 'opponent takes any battle damage you would have taken',
    2460565: 'Special Summon 1 Level 4 or lower monster from your hand',
}
for i, frag in anchors.items():
    d = re.sub(r'\s+', ' ', (idx[i].get('desc') or '').replace('\n', ' '))
    hit = frag.lower() in d.lower()
    print(('OK' if hit else 'BAD TEXT'), i, idx[i]['name'], frag[:50])
    ok = ok and hit

print('RESULT', 'PASS' if ok else 'FAIL')
