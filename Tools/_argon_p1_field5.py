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

rt = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs'
replace_once(rt,
'''    public static class TextEffectRuntime
    {
        /// <summary>Can this compiled Activate program be used right now (timing + targets)?</summary>
        public static bool CanActivate(DuelEngine engine, DuelistState who, CardInstance card,
''',
'''    public static class TextEffectRuntime
    {
        /// <summary>
        /// Face-up Field Spell mandatory/trigger on NS/SS (Harpies' Hunting Ground).
        /// Fires after the summon response window so Torrential can destroy the Field first.
        /// </summary>
        public static bool TryTriggerFaceUpFieldSummon(DuelEngine engine, DuelistState summoner,
            CardInstance summoned)
        {
            if (engine == null || summoned == null) return false;
            if (engine.IsAwaitingEffectTarget) return false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                var field = who?.FieldSpellZone?.Occupant;
                if (field == null || !field.FaceUp || field.IsNegated || field.Def == null)
                    continue;
                var prog = CompiledEffectCache.GetOrCompile(field.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                var clauses = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
                if (clauses.Count == 0) continue;
                var clause = clauses[0];
                if (!SpellTrapEffects.SummonWindowClauseLegal(clause, summoned, summoner, who))
                    continue;
                engine.Log($"{field.Name} trigger.");
                if (clause.RequiresTargetChoice)
                {
                    var targets = CollectTargets(engine, who, clause, field);
                    if (targets.Count == 0)
                    {
                        engine.Log($"{field.Name}: no legal Spell/Trap target.");
                        continue;
                    }

                    if (!who.IsPlayer)
                    {
                        var pick = AutoPick(clause, targets, who, engine);
                        var dummyAi = false;
                        foreach (var c in clauses)
                            ApplyClause(engine, who, field, c, pick, ref dummyAi, ref dummyAi);
                        engine.NotifyPublic();
                        return true;
                    }

                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = field,
                        FromHand = false,
                        TargetKind = MapTargetKind(clause),
                        UsesTextProgram = true,
                        FromSummonWindow = true
                    };
                    pending.LegalTargets.AddRange(targets);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                var dummy = false;
                foreach (var c in clauses)
                    ApplyClause(engine, who, field, c, summoned, ref dummy, ref dummy);
                engine.NotifyPublic();
                return true;
            }

            return false;
        }

        /// <summary>Can this compiled Activate program be used right now (timing + targets)?</summary>
        public static bool CanActivate(DuelEngine engine, DuelistState who, CardInstance card,
''',
    'TryTriggerFaceUpFieldSummon')

replace_once(rt,
'''            var prog = CompiledEffectCache.GetOrCompile(card);
            var costNum = p.CostNumeric;
            engine.ClearPendingActivation();
            if (prog == null) return false;

            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
            {
                if (c.RequiresTargetChoice)
                    ApplyClause(engine, who, card, c, target, ref dummy, ref dummy, costNum);
                else
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy, costNum);
            }

            if (monsterIgnition)
            {
                if (prog.ClausesFor(EffectTiming.Activate).Any(x => x.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            NotifyTargetingEffectResolved(engine, target);
            engine.NotifyPublic();
            return true;
        }
''',
'''            var prog = CompiledEffectCache.GetOrCompile(card);
            var costNum = p.CostNumeric;
            var fromSummon = p.FromSummonWindow;
            engine.ClearPendingActivation();
            if (prog == null) return false;

            var dummy = false;
            var clauseSet = fromSummon
                ? prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon)
                : prog.ClausesFor(EffectTiming.Activate);
            foreach (var c in clauseSet)
            {
                if (c.RequiresTargetChoice)
                    ApplyClause(engine, who, card, c, target, ref dummy, ref dummy, costNum);
                else
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy, costNum);
            }

            if (monsterIgnition)
            {
                if (prog.ClausesFor(EffectTiming.Activate).Any(x => x.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else if (!fromSummon)
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            NotifyTargetingEffectResolved(engine, target);
            engine.NotifyPublic();
            return true;
        }
''',
    'TryResolveTextTarget FromSummonWindow')

# EffectVocabulary protection kinds
ev = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/EffectVocabulary.cs'
replace_once(ev,
'''                    EffectActionKind.CannotBeAttackTarget or
                    EffectActionKind.UnaffectedByCardEffects or
                    EffectActionKind.CannotBeTargetedByEffects or
                    EffectActionKind.ContinuousCannotTargetDragons or
                    EffectActionKind.ContinuousCannotAttack or
                    EffectActionKind.CannotBeTributedForSummon or
                    EffectActionKind.CannotBeDestroyedByBattle or
                    EffectActionKind.PiercingBattleDamage =>
                    EffectResolutionKind.Protection,
''',
'''                    EffectActionKind.CannotBeAttackTarget or
                    EffectActionKind.UnaffectedByCardEffects or
                    EffectActionKind.CannotBeTargetedByEffects or
                    EffectActionKind.ContinuousCannotTargetDragons or
                    EffectActionKind.ContinuousCannotAttack or
                    EffectActionKind.CannotBeTributedForSummon or
                    EffectActionKind.CannotBeDestroyedByBattle or
                    EffectActionKind.PiercingBattleDamage or
                    EffectActionKind.CannotBanishFromGraveyard or
                    EffectActionKind.CannotTargetCardsInGraveyard =>
                    EffectResolutionKind.Protection,
''',
    'EffectVocabulary GY locks')

# Validator schema
val = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/EffectProgramValidator.cs'
replace_once(val,
'''CannotBeDestroyedByBattle|PiercingBattleDamage|GainLifePoints''',
'''CannotBeDestroyedByBattle|PiercingBattleDamage|CannotBanishFromGraveyard|CannotTargetCardsInGraveyard|GainLifePoints''',
    'validator GY lock actions')

print('runtime trigger + vocabulary + validator done')
