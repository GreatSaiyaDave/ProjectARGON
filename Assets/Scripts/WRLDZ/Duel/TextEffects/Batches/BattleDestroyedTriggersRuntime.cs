using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleDestroyedTriggers". Atoms: <see cref="BattleDestroyedTriggersAtoms"/>.
    /// <list type="bullet">
    /// <item>Agido: battle-destroyed in the GY, its controller rolls a die (CoinDiceInterrupts) when they have a free
    /// Monster Zone, then may Special Summon 1 monster of the clause's Type from their GY whose Level is the roll (Level 6 or
    /// higher on a 6): a select at resolution through ContinuousTrapsAndSpiritSupportRuntime.AskCardPick (tap = yes, Cancel =
    /// no), which asks the face-up position and the zone (<see cref="RollDieThenSummonFromGy"/>).</item>
    /// <item>Keldo: battle-destroyed in the GY, its controller targets exactly N cards in the opponent's GY (only when N can
    /// be targeted), picked one after the other, and shuffles those still there into the Deck
    /// (<see cref="ShuffleOpponentGyTargetsIntoDeck"/>).</item>
    /// <item>Kaiser Glider / Different Dimension Dragon: battle protections read at damage calculation
    /// (<see cref="BattleIndestructible"/>, from DamageStepRulesRuntime.BattleIndestructible).</item>
    /// <item>Different Dimension Dragon: not destroyed by a Spell/Trap effect that does not target it
    /// (<see cref="PreventsUntargetedSpellTrapDestruction"/>, from DuelEngine.DestroyMonster and
    /// TextEffectRuntime.DestroyCard; the target is EquipAndSpellProtectionRuntime's targeted-resolution scope).</item>
    /// <item>Shinato, King of a Higher Plane: its battle-destroying burn needs the destroyed monster's battle position to
    /// have been Defense (<see cref="NoteBattle"/> at damage calculation, <see cref="SkipsBattleDestroying"/>).</item>
    /// </list>
    /// Kelbek and Des Kangaroo reuse existing clauses (Wall of Illusion's return, Thousand Needles' destroy).
    /// </summary>
    public static class BattleDestroyedTriggersRuntime
    {
        sealed class BattleNote
        {
            public CardInstance Attacker;
            public CardInstance Target;
            public bool AttackerInDefense;
            public bool TargetInDefense;
        }

        static readonly ConditionalWeakTable<DuelEngine, BattleNote> s_battle = new();

        static IEnumerable<EffectClause> LiveClauses(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    yield return c;
        }

        // ───────────────────────────── the battle ─────────────────────────────

        /// <summary>
        /// DamageStepRulesRuntime.BeginBattle (damage calculation): the battle positions of both battlers (ygopro
        /// GetBattlePosition), read later by <see cref="SkipsBattleDestroying"/>.
        /// </summary>
        public static void NoteBattle(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null) return;
            var note = s_battle.GetOrCreateValue(engine);
            note.Attacker = attacker;
            note.Target = target;
            note.AttackerInDefense = attacker != null && attacker.Position == BattlePosition.Defense;
            note.TargetInDefense = target != null && target.Position == BattlePosition.Defense;
        }

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle: true when <paramref name="clause"/> of <paramref name="source"/>
        /// does not activate — Shinato: the monster it destroyed (<paramref name="destroyed"/>) must be in a GY and must have
        /// battled it in Defense Position.
        /// </summary>
        public static bool SkipsBattleDestroying(DuelEngine engine, EffectClause clause, CardInstance source,
            CardInstance destroyed)
        {
            if (clause == null || !clause.RequiresDestroyedWasDefensePosition) return false;
            if (engine?.Player == null || engine.Opponent == null || source == null || destroyed?.Def == null) return true;
            if (source.IsNegated) return true;
            if (!engine.Player.Graveyard.Contains(destroyed) && !engine.Opponent.Graveyard.Contains(destroyed)) return true;
            if (!s_battle.TryGetValue(engine, out var note)) return true;
            var wasDefense =
                (ReferenceEquals(note.Attacker, source) && ReferenceEquals(note.Target, destroyed) && note.TargetInDefense) ||
                (ReferenceEquals(note.Target, source) && ReferenceEquals(note.Attacker, destroyed) && note.AttackerInDefense);
            if (!wasDefense)
                engine.Log($"{source.Name}: {destroyed.Name} was not in Defense Position — no damage.");
            return !wasDefense;
        }

        /// <summary>
        /// DamageStepRulesRuntime.BattleIndestructible (damage calculation, this calculation's ATK): <paramref name="mine"/>
        /// cannot be destroyed by battle with <paramref name="other"/> — its own clause applies: equal ATK (Kaiser Glider) or
        /// <paramref name="other"/>'s ATK at most the clause's Amount (Different Dimension Dragon).
        /// </summary>
        public static bool BattleIndestructible(DuelEngine engine, DuelistState side, CardInstance mine,
            CardInstance other)
        {
            if (engine == null || side == null || mine == null || other == null) return false;
            if (!side.TryFindMonster(mine, out _)) return false;
            if (mine.CurrentAtk == other.CurrentAtk &&
                LiveClauses(mine, EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster).Any())
            {
                engine.Log($"{mine.Name} cannot be destroyed by battle with {other.Name} (equal ATK {mine.CurrentAtk}).");
                return true;
            }

            foreach (var c in LiveClauses(mine, EffectActionKind.CannotBeDestroyedByBattleWithMonsterAtkAtMost))
            {
                if (other.CurrentAtk > c.Amount) continue;
                engine.Log($"{mine.Name} cannot be destroyed by battle with {other.Name} ({other.CurrentAtk} ATK, " +
                           $"{c.Amount} or less).");
                return true;
            }

            return false;
        }

        // ───────────────────────────── Different Dimension Dragon ─────────────────────────────

        /// <summary>
        /// DuelEngine.DestroyMonster (not by battle) and TextEffectRuntime.DestroyCard: <paramref name="monster"/> is face-up
        /// on the field with an un-negated "cannot be destroyed by Spell/Trap effects that do not target it", the effect
        /// destroying it is a Spell/Trap Card's (<paramref name="byEffect"/>) and it is not a target of that effect — it is
        /// not destroyed.
        /// </summary>
        public static bool PreventsUntargetedSpellTrapDestruction(DuelEngine engine, CardInstance monster,
            CardInstance byEffect)
        {
            if (engine == null || monster?.Def == null || byEffect?.Def == null) return false;
            if (!byEffect.Def.IsSpell && !byEffect.Def.IsTrap) return false;
            var ctrl = engine.ControllerOf(monster);
            if (ctrl == null || !ctrl.TryFindMonster(monster, out _)) return false;
            if (!LiveClauses(monster, EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis).Any())
                return false;
            if (EquipAndSpellProtectionRuntime.IsTargetOfResolvingEffect(monster, byEffect)) return false;
            engine.Log($"{monster.Name} cannot be destroyed by {byEffect.Name}: a Spell/Trap effect that does not target it.");
            return true;
        }

        // ───────────────────────────── Agido ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.RollDieSpecialSummonFromGyWithLevelOfRoll"/>: no free Monster Zone, nothing (no roll);
        /// otherwise roll 1 die, then <paramref name="who"/> may Special Summon 1 monster of
        /// <see cref="EffectClause.RaceFilter"/> from their GY whose Level is the roll (6: Level 6 or higher).
        /// </summary>
        public static void RollDieThenSummonFromGy(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: {who.Name} has no free Monster Zone — no die is rolled.");
                return;
            }

            CoinDiceInterruptsRuntime.RollDie(engine, who, source,
                roll => SummonCandidates(engine, who, source, clause, roll).Count > 0,
                roll => OfferSummon(engine, who, source, clause, roll));
        }

        static bool LevelMatchesRoll(CardInstance m, int roll) =>
            roll >= 6 ? m.Level >= 6 : m.Level == roll;

        static List<CardInstance> SummonCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, int roll)
        {
            var race = clause.RaceFilter;
            return who.Graveyard
                .Where(m => m?.Def != null && m.Def.IsMonster &&
                            (string.IsNullOrEmpty(race) ||
                             string.Equals(m.Def.race, race, StringComparison.OrdinalIgnoreCase)) &&
                            LevelMatchesRoll(m, roll) &&
                            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, m, source, SummonOrigin.Graveyard,
                                requireZone: true))
                .ToList();
        }

        static void OfferSummon(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause, int roll)
        {
            var name = source?.Name ?? "Effect";
            if (engine.GameOver) return;
            var what = roll >= 6 ? "Level 6 or higher" : $"Level {roll}";
            if (SummonCandidates(engine, who, source, clause, roll).Count == 0)
            {
                engine.Log($"{name}: {who.Name} rolled {roll} — no {what} {clause.RaceFilter} monster in the GY can be " +
                           "Special Summoned.");
                return;
            }

            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => SummonCandidates(engine, who, source, clause, roll),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"rolled {roll}: you can Special Summon 1 {what} {clause.RaceFilter} monster from your GY " +
                         "(Cancel = no).",
                AiChoose = list => list.OrderByDescending(m => m.Def.atk).FirstOrDefault(),
                AskWithOneCandidate = true,
                AsksSpecialSummonPlacement = true,
                Cancel = () => engine.Log($"{name}: {who.Name} does not Special Summon a monster."),
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{name}: {who.Name} does not Special Summon a monster.");
                        return;
                    }

                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : BattlePosition.Attack;
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    SummonFromGy(engine, who, source, pick, pos, zone);
                }
            });
        }

        static void SummonFromGy(DuelEngine engine, DuelistState who, CardInstance source, CardInstance card,
            BattlePosition pos, int zone)
        {
            var name = source?.Name ?? "Effect";
            if (!who.Graveyard.Contains(card))
            {
                engine.Log($"{name}: {card.Name} is no longer in the GY — it is not Special Summoned.");
                return;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, source, SummonOrigin.Graveyard,
                    requireZone: true))
            {
                engine.Log($"{name}: {card.Name} cannot be Special Summoned now.");
                return;
            }

            who.Graveyard.Remove(card);
            DuelEngine.RecordOwnerOnFieldEntry(card, who, who);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(who, card, pos, true, SummonKind.SpecialSummon, zone))
            {
                engine.Log($"{name}: {who.Name} Special Summons {card.Name} from the GY in face-up " +
                           $"{(card.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                return;
            }

            card.ControlOwner = null;
            if (!who.Graveyard.Contains(card)) who.Graveyard.Add(card);
            engine.Log($"{name}: {card.Name} could not be Special Summoned — it stays in the GY.");
        }

        // ───────────────────────────── Keldo ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck"/>: fewer than <see cref="EffectClause.TargetCount"/>
        /// cards in the opponent's GY can be targeted — no target, nothing happens. Otherwise <paramref name="who"/> targets
        /// that many (one pick after the other; the AI takes the strongest monsters), then the targets still in the GY go to
        /// the Deck (an Extra Deck monster to the Extra Deck) and the Deck is shuffled.
        /// </summary>
        public static void ShuffleOpponentGyTargetsIntoDeck(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var opp = engine?.OpponentOf(who);
            if (opp?.Graveyard == null || clause == null) return;
            var name = source?.Name ?? "Effect";
            var need = Math.Max(1, clause.TargetCount);
            var picked = new List<CardInstance>();

            List<CardInstance> Legal() => opp.Graveyard
                .Where(c => c?.Def != null && !picked.Contains(c) &&
                            !ContinuousProtections.CannotTargetCardInGy(engine, source, c))
                .ToList();

            if (Legal().Count < need)
            {
                engine.Log($"{name}: fewer than {need} cards in {opp.Name}'s GY can be targeted — nothing happens.");
                return;
            }

            void PickNext()
            {
                if (picked.Count >= need)
                {
                    ShuffleIntoDeck(engine, opp, source, picked);
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                {
                    Candidates = Legal,
                    Kind = EffectTargetKind.CardInEitherGy,
                    Prompt = $"target {need - picked.Count} more card(s) in your opponent's GY to shuffle into their Deck.",
                    AiChoose = AiGyPick,
                    Finish = c =>
                    {
                        if (c == null)
                        {
                            ShuffleIntoDeck(engine, opp, source, picked);
                            return;
                        }

                        picked.Add(c);
                        PickNext();
                    }
                });
            }

            PickNext();
        }

        /// <summary>The AI's target: the opponent's strongest monster first (no revival), then any card.</summary>
        static CardInstance AiGyPick(List<CardInstance> list) =>
            list?.OrderByDescending(c => c.Def != null && c.Def.IsMonster ? 1 : 0)
                .ThenByDescending(c => c.Def?.atk ?? 0)
                .FirstOrDefault();

        static void ShuffleIntoDeck(DuelEngine engine, DuelistState gyOwner, CardInstance source, List<CardInstance> targets)
        {
            var name = source?.Name ?? "Effect";
            var toDeck = 0;
            foreach (var t in targets)
            {
                if (t == null || !gyOwner.Graveyard.Contains(t))
                {
                    if (t != null) engine.Log($"{name}: {t.Name} is no longer in the GY.");
                    continue;
                }

                if (ContinuousProtections.GyMovementLocked(engine, source, t))
                {
                    engine.Log($"{name}: {t.Name} cannot leave the GY.");
                    continue;
                }

                gyOwner.Graveyard.Remove(t);
                if (t.Def != null && t.Def.IsExtraDeck)
                {
                    gyOwner.ExtraDeck?.Add(t.CardId);
                    engine.Log($"{name}: {t.Name} returns to {gyOwner.Name}'s Extra Deck.");
                }
                else
                {
                    gyOwner.Deck.Add(t.CardId);
                    toDeck++;
                    engine.Log($"{name}: {t.Name} goes from the GY to {gyOwner.Name}'s Deck.");
                }
            }

            if (toDeck > 0)
            {
                engine.Rng.Shuffle(gyOwner.Deck);
                engine.Log($"{name}: {gyOwner.Name}'s Deck is shuffled.");
            }
        }
    }
}
