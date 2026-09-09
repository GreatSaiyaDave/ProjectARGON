#!/usr/bin/env python3
"""P1 field/GY remaining engines. Compiler 53→54. File-only."""
from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path: Path, old: str, new: str, label: str):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found in {path}')
    n = t.count(old)
    if n != 1:
        raise SystemExit(f'FAIL {label}: old count={n} in {path}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

# ---------------------------------------------------------------------------
# 1) Compiler version
# ---------------------------------------------------------------------------
comp = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs'
replace_once(comp,
    '        public const int Version = 53;\n',
    '        public const int Version = 54;\n',
    'compiler version 54')

# ---------------------------------------------------------------------------
# 2) Aura regexes + HHG summon regex
# ---------------------------------------------------------------------------
replace_once(comp,
'''        static readonly Regex RxMultiRaceGainLoseAtkDef = new(
            @"All (.+?) monsters on the field gain (\\d+) ATK/DEF,\\s*" +
            @"also all (.+?) monsters on the field lose (\\d+) ATK/DEF\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
'''        static readonly Regex RxMultiRaceGainLoseAtkDef = new(
            @"All (.+?) monsters on the field gain (\\d+) ATK/DEF,\\s*" +
            @"also all (.+?) monsters on the field lose (\\d+) ATK/DEF\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Necrovalley family: All "Gravekeeper's" monsters (on the field) gain N ATK/DEF
        /// or "ATK and DEF". Quoted series, not a race.
        /// </summary>
        static readonly Regex RxQuotedSeriesGainAtkDef = new(
            @"All ""([^""]+)"" monsters(?: on the field)? gain (\\d+) ATK(?:/DEF| and DEF)\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Harpies' Hunting Ground / Mountain-shape single or multi-word race:
        /// All Winged Beast monsters gain 200 ATK/DEF.
        /// </summary>
        static readonly Regex RxRaceGainAtkDef = new(
            @"All ([A-Za-z]+(?:[ -][A-Za-z]+)*)(?:-Type)? monsters(?: on the field)? gain (\\d+) ATK(?:/DEF| and DEF)\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Harpies' Hunting Ground: If any "Harpie Lady" or "Harpie Lady Sisters" is
        /// Normal or Special Summoned: target 1 S/T; destroy it.
        /// </summary>
        static readonly Regex RxNamedOrNamedSummonDestroySt = new(
            @"If any ""([^""]+)"" or ""([^""]+)"" is Normal or Special Summoned:\\s*" +
            @"(?:The player who conducted (?:the|that) Summon targets 1 Spell/?Trap on the field;\\s*" +
            @"that player destroys that target|" +
            @"Target 1 Spell/?Trap on the field;\\s*destroy (?:that target|it))\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Rite of Spirit: This card's activation and effect are unaffected by "Necrovalley".</summary>
        static readonly Regex RxUnaffectedByNamed = new(
            @"This card's activation and effect are unaffected by ""([^""]+)""\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    'aura + HHG + unaffected regexes')

# Torrential Take → also HHG
replace_once(comp,
'''            Take(RxTorrentialTribute.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldMonsters,
                RequiresTargetChoice = false,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });
''',
'''            Take(RxTorrentialTribute.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldMonsters,
                RequiresTargetChoice = false,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            var hhg = RxNamedOrNamedSummonDestroySt.Match(text);
            Take(hhg, hhg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.OpponentNormalOrFlipSummon,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    AnswersSpecialSummon = true,
                    AnswersControllerSummon = true,
                    NamedCard = hhg.Groups[1].Value,
                    AltNamedCard = hhg.Groups[2].Value,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);
''',
    'HHG summon destroy S/T Take')

# Unaffected-by-named rider after template Collect
replace_once(comp,
'''            ContinuousRestrictionTemplates.Collect(text, def, clauses, matchedSpans);
            MonsterTriggerTemplates.Collect(text, def, clauses, matchedSpans);
            MarkAbsorbed(text, RxRitualSummonReminder, matchedSpans);
''',
'''            ContinuousRestrictionTemplates.Collect(text, def, clauses, matchedSpans);
            MonsterTriggerTemplates.Collect(text, def, clauses, matchedSpans);
            var una = RxUnaffectedByNamed.Match(text);
            if (una.Success)
            {
                foreach (var cl in clauses)
                {
                    if (cl != null)
                        cl.UnaffectedByNamedCard = una.Groups[1].Value;
                }
                matchedSpans.Add((una.Index, una.Length));
            }
            MarkAbsorbed(text, RxRitualSummonReminder, matchedSpans);
''',
    'unaffected-by-named rider')

# CompileAuraClauses: quoted series + race gain
replace_once(comp,
'''            var multi = RxMultiRaceGainLoseAtkDef.Match(body);
            if (multi.Success && multi.Index == 0)
            {
                var gainN = ParseInt(multi, 2, 200);
                var loseN = ParseInt(multi, 4, 200);
                foreach (var race in SplitRaceList(multi.Groups[1].Value))
                    list.Add(StatAuraClause(race, gainN, gainN, EffectSide.Both));
                foreach (var race in SplitRaceList(multi.Groups[3].Value))
                    list.Add(StatAuraClause(race, -loseN, -loseN, EffectSide.Both));
                return list;
            }

            var inc = RxIncDecAtk.Match(body);
''',
'''            var multi = RxMultiRaceGainLoseAtkDef.Match(body);
            if (multi.Success && multi.Index == 0)
            {
                var gainN = ParseInt(multi, 2, 200);
                var loseN = ParseInt(multi, 4, 200);
                foreach (var race in SplitRaceList(multi.Groups[1].Value))
                    list.Add(StatAuraClause(race, gainN, gainN, EffectSide.Both));
                foreach (var race in SplitRaceList(multi.Groups[3].Value))
                    list.Add(StatAuraClause(race, -loseN, -loseN, EffectSide.Both));
                return list;
            }

            var quoted = RxQuotedSeriesGainAtkDef.Match(body);
            if (quoted.Success && quoted.Index == 0)
            {
                var n = ParseInt(quoted, 2, 0);
                var both = quoted.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                var c = StatAuraClause(null, n, both ? n : 0, EffectSide.Both);
                c.NamedCard = quoted.Groups[1].Value;
                c.NamedCardIsSeries = true;
                list.Add(c);
                return list;
            }

            var raceGain = RxRaceGainAtkDef.Match(body);
            if (raceGain.Success && raceGain.Index == 0)
            {
                var n = ParseInt(raceGain, 2, 0);
                var both = raceGain.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(StatAuraClause(raceGain.Groups[1].Value, n, both ? n : 0, EffectSide.Both));
                return list;
            }

            var inc = RxIncDecAtk.Match(body);
''',
    'CompileAuraClauses quoted + race gain')

print('compiler done')
