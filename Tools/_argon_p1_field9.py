from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def patch(rel, old, new, label):
    p = root / rel
    t = p.read_text(encoding='utf-8')
    if old not in t:
        print('MISSING', label)
        return False
    if t.count(old) != 1:
        print('AMBIGUOUS', label, t.count(old))
        return False
    p.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)
    return True

# 1) Necrovalley leftover negate-move / type-attr absorb
patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/ContinuousRestrictionTemplates.cs',
 '''        /// <summary>
        /// Official Necrovalley: Cards in the GY cannot be targeted, except by the
        /// effect of "Necrovalley". Except group is optional (hard lock).
        /// </summary>
        static readonly Regex RxGyCannotTarget = new(
            @"Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?""([^""]+)"")?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
 '''        /// <summary>
        /// Official Necrovalley: Cards in the GY cannot be targeted, except by the
        /// effect of "Necrovalley". Except group is optional (hard lock).
        /// </summary>
        static readonly Regex RxGyCannotTarget = new(
            @"Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?""([^""]+)"")?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Older Necrovalley leftover: Negate any card effect that would move a
        /// card in the GY to a different place. Shared CannotTargetCardsInGraveyard
        /// (except this card / Necrovalley). Fail-loud if only the ATK aura compiles.
        /// </summary>
        static readonly Regex RxGyNegateMove = new(
            @"Negate any card effect that would move a card in the (?:GY|Graveyard) to a different place\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Older Necrovalley leftover: Types/Attributes in the GY.</summary>
        static readonly Regex RxGyNegateTypeAttr = new(
            @"Negate any card effect that changes Types or Attributes in the (?:GY|Graveyard)\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
 'restriction leftover regexes')

patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/ContinuousRestrictionTemplates.cs',
 '''            var gyT = RxGyCannotTarget.Match(text);
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
 '''            var gyT = RxGyCannotTarget.Match(text);
            Add(gyT, gyT.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotTargetCardsInGraveyard,
                    ExceptNamedCard = gyT.Groups[1].Success ? gyT.Groups[1].Value : def?.name,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var gyMove = RxGyNegateMove.Match(text);
            var gyType = RxGyNegateTypeAttr.Match(text);
            if (!gyT.Success && (gyMove.Success || gyType.Success))
            {
                var first = gyMove.Success ? gyMove : gyType;
                Add(first, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotTargetCardsInGraveyard,
                    ExceptNamedCard = def?.name,
                    StaysOnField = true,
                    MakesChainLink = false
                });
                if (gyMove.Success && gyType.Success)
                    spans?.Add((gyType.Index, gyType.Length));
            }
            else
            {
                if (gyMove.Success) spans?.Add((gyMove.Index, gyMove.Length));
                if (gyType.Success) spans?.Add((gyType.Index, gyType.Length));
            }
        }
''',
 'restriction leftover collect')

patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/ContinuousRestrictionTemplates.cs',
 '''            if (RxGyCannotTarget.IsMatch(text))
                need.Add(EffectActionKind.CannotTargetCardsInGraveyard);
''',
 '''            if (RxGyCannotTarget.IsMatch(text) || RxGyNegateMove.IsMatch(text) ||
                RxGyNegateTypeAttr.IsMatch(text))
                need.Add(EffectActionKind.CannotTargetCardsInGraveyard);
''',
 'restriction leftover expected')

# 2) HHG NamedCardIsSeries
patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs',
 '''                    NamedCard = hhg.Groups[1].Value,
                    AltNamedCard = hhg.Groups[2].Value,
                    StaysOnField = true,
                    MakesChainLink = true
''',
 '''                    NamedCard = hhg.Groups[1].Value,
                    AltNamedCard = hhg.Groups[2].Value,
                    NamedCardIsSeries = true,
                    StaysOnField = true,
                    MakesChainLink = true
''',
 'HHG NamedCardIsSeries')

# 3) Cyber Shield series flag
patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/LegacyTextTemplates.cs',
 '''                    eq.EquipHostName = namedOr.Groups[1].Value;
                    eq.AltNamedCard = namedOr.Groups[2].Value;
                    Add(namedOr, eq);
''',
 '''                    eq.EquipHostName = namedOr.Groups[1].Value;
                    eq.AltNamedCard = namedOr.Groups[2].Value;
                    eq.NamedCardIsSeries = true;
                    Add(namedOr, eq);
''',
 'Cyber Shield series')

print('done phase 1')
