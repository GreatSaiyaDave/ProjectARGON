from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def patch(rel, old, new, label):
    p = root / rel
    t = p.read_text(encoding='utf-8')
    if old not in t:
        print('MISSING', label)
        return False
    n = t.count(old)
    if n != 1:
        print('AMBIGUOUS', label, n)
        return False
    p.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)
    return True

patch(
 'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
 '''                            c.Zone == EffectZoneFilter.FieldSpellTraps &&
                            string.Equals(c.NamedCard, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase)));
''',
 '''                            c.Zone == EffectZoneFilter.FieldSpellTraps &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase)));
''',
 'HHG CheckSearcher series')

patch(
 'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
 '''                    CheckSearcher(63224564, "Cyber Shield FullyCompiled Equip Harpie Lady or Sisters +500",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 500 &&
                            string.Equals(c.EquipHostName, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.AltNamedCard, "Harpie Lady Sisters",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckParked(26185991, "Pinch Hopper");
''',
 '''                    CheckSearcher(63224564, "Cyber Shield FullyCompiled Equip Harpie Lady or Sisters +500",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 500 &&
                            string.Equals(c.EquipHostName, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.AltNamedCard, "Harpie Lady Sisters",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(18605135, "Tornado Wall FullyCompiled Umi activate + no battle damage + self-destroy",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate &&
                            string.Equals(c.RequiresFaceUpName, "Umi",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.StaysOnField) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.PreventControllerBattleDamage) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp));

                    CheckParked(26185991, "Pinch Hopper");
''',
 'Tornado Wall CheckSearcher')

patch(
 'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs',
 '''                    const int reborn = 83764718;
''',
 '''                    const int reborn = 83764719;
''',
 'Monster Reborn id')

patch(
 'Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs',
 '''                    nvp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CannotTargetCardsInGraveyard),
                    nvp == null
                        ? "null"
                        : $"full={nvp.FullyCompiled} n={nvp.ClauseList.Count} unparsed={string.Join("|", nvp.UnparsedFragments ?? Array.Empty<string>())}");
''',
 '''                    nvp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CannotTargetCardsInGraveyard &&
                        string.Equals(c.ExceptNamedCard, "Necrovalley", StringComparison.OrdinalIgnoreCase)),
                    nvp == null
                        ? "null"
                        : $"full={nvp.FullyCompiled} n={nvp.ClauseList.Count} unparsed={string.Join("|", nvp.UnparsedFragments ?? Array.Empty<string>())}");

                var nvOld = new CardDef
                {
                    id = 47355498,
                    name = "Necrovalley",
                    type = "Spell Card",
                    race = "Field",
                    desc =
                        "All \\"Gravekeeper's\\" monsters gain 500 ATK and DEF. Cards in the Graveyard cannot be banished. Negate any card effect that would move a card in the Graveyard to a different place. Negate any card effect that changes Types or Attributes in the Graveyard."
                };
                var nvOldp = CardTextEffectCompiler.Compile(nvOld);
                Check("Older Necrovalley leftover GY-move/type negate compiles as GY cannot-target",
                    nvOldp != null && nvOldp.FullyCompiled &&
                    nvOldp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CannotBanishFromGraveyard) &&
                    nvOldp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CannotTargetCardsInGraveyard &&
                        string.Equals(c.ExceptNamedCard, "Necrovalley", StringComparison.OrdinalIgnoreCase)),
                    nvOldp == null
                        ? "null"
                        : $"full={nvOldp.FullyCompiled} n={nvOldp.ClauseList.Count} unparsed={string.Join("|", nvOldp.UnparsedFragments ?? Array.Empty<string>())}");
''',
 'Tcg leftover Necrovalley')

# verify script
p = root / 'Tools/_argon_p1_verify.py'
t = p.read_text(encoding='utf-8')
t = t.replace("public const int Version = 53", "public const int Version = 54")
if 'RxGyNegateMove' not in t:
    t = t.replace(
        "print('Snatch LP', snatch.search",
        '''# Necrovalley leftover + HHG summon
gy_move=re.compile(r'Negate any card effect that would move a card in the (?:GY|Graveyard) to a different place', re.I)
gy_tgt=re.compile(r'Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?"([^"]+)")?', re.I)
nv_psct='All "Gravekeeper\\'s" monsters on the field gain 500 ATK/DEF. Neither player can banish cards from the GYs. Cards in the GY cannot be targeted, except by the effect of "Necrovalley".'
print('NV target except', gy_tgt.search(nv_psct).group(1) if gy_tgt.search(nv_psct) else None)
hhg=re.compile(r'If any "([^"]+)" or "([^"]+)" is Normal or Special Summoned:', re.I)
print('HHG names', hhg.search('If any "Harpie Lady" or "Harpie Lady Sisters" is Normal or Special Summoned: The player who conducted the Summon targets 1 Spell/Trap on the field; that player destroys that target.').groups())
print('Snatch LP', snatch.search'''
    )
p.write_text(t, encoding='utf-8')
print('OK verify script')
print('done')
