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

# Simplify Tornado leave assertion
irt = root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs'
replace_once(irt,
'''                        Check("Tornado Wall destroyed when Umi leaves",
                            p.Graveyard.Contains(wall) &&
                            (p.SpellTrapZones == null ||
                             !System.Linq.Enumerable.Any(p.SpellTrapZones,
                                 z => z != null && z.Occupant == wall)),
                            $"gy={p.Graveyard.Contains(wall)} face={wall.FaceUp}");
''',
'''                        Check("Tornado Wall destroyed when Umi leaves",
                            p.Graveyard.Contains(wall) && !p.TryFindSpellTrap(wall, out _),
                            $"gy={p.Graveyard.Contains(wall)} onField={p.TryFindSpellTrap(wall, out _)}");
''',
    'Tornado leave assertion')

tcg = root/'Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs'
replace_once(tcg,
'''                    twp == null
                        ? "null"
                        : $"full={twp.FullyCompiled} n={twp.ClauseList.Count} unparsed={string.Join("|", twp.UnparsedFragments ?? Array.Empty<string>())}");

                var daedalus = new CardDef
''',
'''                    twp == null
                        ? "null"
                        : $"full={twp.FullyCompiled} n={twp.ClauseList.Count} unparsed={string.Join("|", twp.UnparsedFragments ?? Array.Empty<string>())}");

                var nv = new CardDef
                {
                    id = 47355498,
                    name = "Necrovalley",
                    type = "Spell Card",
                    race = "Field",
                    desc =
                        "All \\"Gravekeeper's\\" monsters on the field gain 500 ATK/DEF. Neither player can banish cards from the GYs. Cards in the GY cannot be targeted, except by the effect of \\"Necrovalley\\"."
                };
                var nvp = CardTextEffectCompiler.Compile(nv);
                Check("PSCT Necrovalley: GK aura + GY cannot banish/target",
                    nvp != null && nvp.FullyCompiled &&
                    nvp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                        c.NamedCardIsSeries &&
                        string.Equals(c.NamedCard, "Gravekeeper's", StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == 500) &&
                    nvp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CannotBanishFromGraveyard) &&
                    nvp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CannotTargetCardsInGraveyard),
                    nvp == null
                        ? "null"
                        : $"full={nvp.FullyCompiled} n={nvp.ClauseList.Count} unparsed={string.Join("|", nvp.UnparsedFragments ?? Array.Empty<string>())}");

                var hhg = new CardDef
                {
                    id = 75782277,
                    name = "Harpies' Hunting Ground",
                    type = "Spell Card",
                    race = "Field",
                    desc =
                        "All Winged Beast monsters gain 200 ATK/DEF. If any \\"Harpie Lady\\" or \\"Harpie Lady Sisters\\" is Normal or Special Summoned: The player who conducted the Summon targets 1 Spell/Trap on the field; that player destroys that target."
                };
                var hhgp = CardTextEffectCompiler.Compile(hhg);
                Check("PSCT Harpies' Hunting Ground: Winged Beast aura + named NS/SS destroy S/T",
                    hhgp != null && hhgp.FullyCompiled &&
                    hhgp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                        string.Equals(c.RaceFilter, "Winged Beast", StringComparison.OrdinalIgnoreCase)) &&
                    hhgp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy &&
                        c.AnswersSpecialSummon && c.RequiresTargetChoice),
                    hhgp == null
                        ? "null"
                        : $"full={hhgp.FullyCompiled} n={hhgp.ClauseList.Count} unparsed={string.Join("|", hhgp.UnparsedFragments ?? Array.Empty<string>())}");

                var daedalus = new CardDef
''',
    'TcgRegressionTests PSCT Necrovalley/HHG')

print('tests2 done')
