#!/usr/bin/env python3
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

# Enum kinds
prog = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledCardProgram.cs'
replace_once(prog,
'''        /// <summary>
        /// Continuous: this card (or the equipped monster) inflicts piercing battle damage.
        /// Airknight Parshath / Fairy Meteor Crush family. Honored via CardInstance.HasPiercing.
        /// </summary>
        PiercingBattleDamage
    }
''',
'''        /// <summary>
        /// Continuous: this card (or the equipped monster) inflicts piercing battle damage.
        /// Airknight Parshath / Fairy Meteor Crush family. Honored via CardInstance.HasPiercing.
        /// </summary>
        PiercingBattleDamage,
        /// <summary>
        /// Neither player can banish cards from the GYs (Necrovalley). Shared GY lock.
        /// </summary>
        CannotBanishFromGraveyard,
        /// <summary>
        /// Cards in the GY cannot be targeted. ExceptNamedCard may allow that card's
        /// effects (Necrovalley except by the effect of "Necrovalley").
        /// </summary>
        CannotTargetCardsInGraveyard
    }
''',
    'enum GY lock kinds')

replace_once(prog,
'''        /// <summary>
        /// Target cannot have this printed / rules name
        /// (Lord Poison: except "Lord Poison"). Distinct from TributeExceptThis.
        /// </summary>
        public string ExceptNamedCard;
''',
'''        /// <summary>
        /// Target cannot have this printed / rules name
        /// (Lord Poison: except "Lord Poison"). Distinct from TributeExceptThis.
        /// </summary>
        public string ExceptNamedCard;
        /// <summary>
        /// Rite of Spirit: this card's activation and effect ignore GY locks from
        /// a face-up card named UnaffectedByNamedCard (Necrovalley).
        /// </summary>
        public string UnaffectedByNamedCard;
''',
    'UnaffectedByNamedCard field')

# ContinuousRestrictionTemplates
crt = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/ContinuousRestrictionTemplates.cs'
replace_once(crt,
'''        static readonly Regex RxPrematureBurial = new(
            @"Activate this card by paying (\\d+) (?:LP|Life Points), then target 1 monster in your (?:GY|Graveyard);\\s*" +
            @"Special Summon that target in Attack Position and equip it with this card\\.\\s*" +
            @"When this card is destroyed, destroy the equipped monster\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
'''        static readonly Regex RxPrematureBurial = new(
            @"Activate this card by paying (\\d+) (?:LP|Life Points), then target 1 monster in your (?:GY|Graveyard);\\s*" +
            @"Special Summon that target in Attack Position and equip it with this card\\.\\s*" +
            @"When this card is destroyed, destroy the equipped monster\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Necrovalley / Imperial Iron Wall-shape: Cards in the GY cannot be banished,
        /// or Neither player can banish cards from the GYs.
        /// </summary>
        static readonly Regex RxGyCannotBanish = new(
            @"(?:Cards in the (?:GY|Graveyard)s? cannot be banished|" +
            @"Neither player can banish (?:cards? )?from (?:the )?(?:GYs?|Graveyards?))\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Official Necrovalley: Cards in the GY cannot be targeted, except by the
        /// effect of "Necrovalley". Except group is optional (hard lock).
        /// </summary>
        static readonly Regex RxGyCannotTarget = new(
            @"Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?""([^""]+)"")?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    'GY lock regexes')

replace_once(crt,
'''            var premature = RxPrematureBurial.Match(text);
            Add(premature, premature.Success
                ? GyReviveClause(defense: false, normalOnly: false, payLp: Parse(premature, 1, 800))
                : null);
        }
''',
'''            var premature = RxPrematureBurial.Match(text);
            Add(premature, premature.Success
                ? GyReviveClause(defense: false, normalOnly: false, payLp: Parse(premature, 1, 800))
                : null);

            Add(RxGyCannotBanish.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CannotBanishFromGraveyard,
                StaysOnField = true,
                MakesChainLink = false
            });

            var gyT = RxGyCannotTarget.Match(text);
            Add(gyT, gyT.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotTargetCardsInGraveyard,
                    ExceptNamedCard = gyT.Groups[1].Success ? gyT.Groups[1].Value : null,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);
        }
''',
    'GY lock Collect')

replace_once(crt,
'''            if (RxCallOfTheHaunted.IsMatch(text) || RxSoulResurrection.IsMatch(text) ||
                RxPrematureBurial.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
        }
''',
'''            if (RxCallOfTheHaunted.IsMatch(text) || RxSoulResurrection.IsMatch(text) ||
                RxPrematureBurial.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
            if (RxGyCannotBanish.IsMatch(text))
                need.Add(EffectActionKind.CannotBanishFromGraveyard);
            if (RxGyCannotTarget.IsMatch(text))
                need.Add(EffectActionKind.CannotTargetCardsInGraveyard);
        }
''',
    'GY lock ExpectedActions')

# FieldSpellEffects ApplyStatClause named series
fs = root/'Assets/Scripts/WRLDZ/Duel/FieldSpellEffects.cs'
replace_once(fs,
'''                    if (!IsFaceUpMonster(m)) continue;
                    if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                    if (!MatchesRace(m, clause.RaceFilter)) continue;
                    m.AtkModifier += clause.Amount;
                    m.DefModifier += clause.DefAmount;
''',
'''                    if (!IsFaceUpMonster(m)) continue;
                    if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                    if (!MatchesRace(m, clause.RaceFilter)) continue;
                    if (!string.IsNullOrEmpty(clause.NamedCard) &&
                        !CardMatchesNamed(m, clause.NamedCard, clause.NamedCardIsSeries))
                        continue;
                    m.AtkModifier += clause.Amount;
                    m.DefModifier += clause.DefAmount;
''',
    'ApplyStatClause named series')

print('vocab structs + GY templates + aura named done')
