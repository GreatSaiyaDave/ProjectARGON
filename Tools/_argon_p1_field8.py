from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path, old, new, label):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found')
    n = t.count(old)
    if n != 1:
        raise SystemExit(f'FAIL {label}: count={n}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

rt = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs'
replace_once(rt,
'''                case EffectActionKind.PreventControllerBattleDamage:
                case EffectActionKind.FieldTreatedAsName:
                case EffectActionKind.SelfDestroyUnlessNamedFaceUp:
                case EffectActionKind.DestroyThisAfterResolvingTargetingEffect:
                    // Continuous — applied by FieldSpellEffects / post-resolve notify while face-up.
                    break;
''',
'''                case EffectActionKind.PreventControllerBattleDamage:
                case EffectActionKind.FieldTreatedAsName:
                case EffectActionKind.SelfDestroyUnlessNamedFaceUp:
                case EffectActionKind.DestroyThisAfterResolvingTargetingEffect:
                case EffectActionKind.CannotBanishFromGraveyard:
                case EffectActionKind.CannotTargetCardsInGraveyard:
                    // Continuous — applied by FieldSpellEffects / ContinuousProtections while face-up.
                    break;
''',
    'ApplyClause GY lock no-op')

# cards_db Necrovalley official PSCT
db = root/'Assets/StreamingAssets/Cards/cards_db.json'
replace_once(db,
'''      "desc": "All \\"Gravekeeper's\\" monsters gain 500 ATK and DEF. Cards in the Graveyard cannot be banished. Negate any card effect that would move a card in the Graveyard to a different place. Negate any card effect that changes Types or Attributes in the Graveyard.",
''',
'''      "desc": "All \\"Gravekeeper's\\" monsters on the field gain 500 ATK/DEF. Neither player can banish cards from the GYs. Cards in the GY cannot be targeted, except by the effect of \\"Necrovalley\\".",
''',
    'cards_db Necrovalley official text')

print('noop + cards_db done')
