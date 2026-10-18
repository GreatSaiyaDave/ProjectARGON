using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// v208 QC "HollowActions": runtime arms for RDS-era actions that were classified (EffectVocabulary) and emitted by an
    /// atom but read by nothing, so the card entered decks and did nothing. Each arm follows the card's ygopro script.
    /// <list type="bullet">
    /// <item>Xing Zhen Hu (c76515293, <see cref="EffectActionKind.TargetTwoFaceDownSpellTrapsLockActivation"/>): its two
    /// Set targets become its persistent card targets (e2 EVENT_CHAIN_SOLVED, only those still face-down); while it stays
    /// face-up in the Spell &amp; Trap Zone with its effects applying, they cannot be activated (e3 EFFECT_CANNOT_TRIGGER,
    /// aux.PersistentTargetFilter). A target that leaves the field, or Xing Zhen Hu leaving or being turned face-down, ends
    /// the link for good; a negation only suspends it (<see cref="SetCardActivationLocked"/>, asked by
    /// ContinuousNegationRuntime.ActivationLocked).</item>
    /// <item>Ballista of Rampart Smashing (c242146, <see cref="EffectActionKind.EquippedGainsAtkWhenAttackingFaceDownDefense"/>):
    /// during damage calculation, when the equipped monster is the attacker and the attack target's battle position was
    /// face-down Defense (it was Set when the Damage Step started), it gains N ATK (<see cref="EquipFaceDownDefenseBonus"/>).</item>
    /// <item>Harpie Lady 3 (c54415063, <see cref="EffectActionKind.OpponentMonsterThatBattledThisCannotAttackForTwoTurns"/>):
    /// EVENT_BATTLED — the monster that battled it gets EFFECT_CANNOT_ATTACK_ANNOUNCE with
    /// RESETS_STANDARD_PHASE_END|RESET_SELF_TURN, 2: it ends at the End Phase of its controller's 2nd turn counted from
    /// now (the current turn counts when it is theirs), or when it leaves the field or is turned face-down
    /// (<see cref="OnBattled"/>, <see cref="AttackLocked"/>).</item>
    /// <item>Thestalos the Firestorm Monarch (c26205777, <see cref="EffectActionKind.DiscardRandomThenDamageByLevelIfMonster"/>):
    /// the opponent discards 1 random card; if it was a Monster Card, they take its Level x Amount damage.</item>
    /// <item>Riryoku (c34016756, <see cref="EffectActionKind.HalfAtkAddToOtherUntilEndOfTurn"/>): at resolution, of its two
    /// targets the ones still face-up count; with two, its controller chooses the one whose ATK is halved and the other
    /// gains the ATK it lost; with one, that one is halved; until the end of the turn.</item>
    /// </list>
    /// </summary>
    public static class QcHollowActionsRuntime
    {
        // ───────────────────────────── Xing Zhen Hu ─────────────────────────────

        sealed class SetCardLock
        {
            public CardInstance Source;
            public int SourceEntry;
            public int SourceFaceUp;
            public CardInstance Target;
            public int TargetEntry;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<SetCardLock>> s_setLocks = new();

        static bool OnSpellTrapRow(DuelistState side, CardInstance card) =>
            side != null && card != null &&
            (side.TryFindSpellTrap(card, out _) || (side.FieldSpellZone != null && side.FieldSpellZone.Occupant == card));

        static string ZoneLabel(DuelistState side, CardInstance card)
        {
            if (side != null && side.TryFindSpellTrap(card, out var i))
                return $"the Set card in {side.Name}'s Spell & Trap Zone {i + 1}";
            return side != null ? $"the Set card in {side.Name}'s Field Zone" : "a Set card";
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause, <see cref="EffectActionKind.TargetTwoFaceDownSpellTrapsLockActivation"/>, once per
        /// target as the activation resolves (Lua s.tgop): the target becomes a card target of <paramref name="source"/> only
        /// while this card is still face-up on the field and the target is still a Set card there.
        /// </summary>
        internal static void LockTargetedSetCard(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || source?.Def == null || target?.Def == null) return;
            var srcSide = engine.ControllerOf(source);
            if (!OnSpellTrapRow(srcSide, source) || !source.FaceUp)
            {
                engine.Log($"{source.Name} is no longer on the field — it locks nothing.");
                return;
            }

            var side = engine.ControllerOf(target);
            if (!OnSpellTrapRow(side, target) || target.FaceUp)
            {
                engine.Log($"{source.Name}: a target is no longer a Set card on the field — not locked.");
                return;
            }

            var list = s_setLocks.GetValue(engine, _ => new List<SetCardLock>());
            list.RemoveAll(l => ReferenceEquals(l.Source, source) && ReferenceEquals(l.Target, target));
            list.Add(new SetCardLock
            {
                Source = source,
                SourceEntry = source.FieldEntryStamp,
                SourceFaceUp = source.FaceUpStamp,
                Target = target,
                TargetEntry = target.FieldEntryStamp
            });
            engine.Log($"{source.Name}: {ZoneLabel(side, target)} cannot be activated.");
        }

        static bool SourcePresent(DuelEngine engine, SetCardLock l) =>
            l.Source != null && l.Source.FaceUp && l.Source.FieldEntryStamp == l.SourceEntry &&
            l.Source.FaceUpStamp == l.SourceFaceUp && OnSpellTrapRow(engine.ControllerOf(l.Source), l.Source);

        static bool TargetPresent(DuelEngine engine, SetCardLock l) =>
            l.Target != null && l.Target.FieldEntryStamp == l.TargetEntry &&
            OnSpellTrapRow(engine.ControllerOf(l.Target), l.Target);

        /// <summary>
        /// ContinuousNegationRuntime.ActivationLocked: <paramref name="card"/> is a live card target of a face-up Xing Zhen Hu
        /// whose effects apply. A link whose source or target left (or whose source was turned face-down) is dropped.
        /// </summary>
        public static bool SetCardActivationLocked(DuelEngine engine, CardInstance card, out string reason)
        {
            reason = null;
            if (engine == null || card?.Def == null || !s_setLocks.TryGetValue(engine, out var list) || list.Count == 0)
                return false;
            list.RemoveAll(l => !SourcePresent(engine, l) || !TargetPresent(engine, l));
            foreach (var l in list)
            {
                if (!ReferenceEquals(l.Target, card) || l.Source.IsNegated) continue;
                reason = $"{l.Source.Name}: this card cannot be activated.";
                return true;
            }

            return false;
        }

        // ───────────────────────────── Ballista of Rampart Smashing ─────────────────────────────

        /// <summary>
        /// DuelEngine damage calculation, with this Damage Step's own ATK bonus: the ATK the attacker's face-up Equip Cards
        /// add because the attack target was face-down Defense when the Damage Step started (<paramref name="flippedByBattle"/>;
        /// Lua d:GetBattlePosition()==POS_FACEDOWN_DEFENSE, a==eqc, PHASE_DAMAGE_CAL). 0 otherwise.
        /// </summary>
        public static int EquipFaceDownDefenseBonus(DuelEngine engine, CardInstance attacker, CardInstance target,
            CardInstance flippedByBattle)
        {
            if (engine == null || attacker?.Equips == null || target == null || !ReferenceEquals(flippedByBattle, target))
                return 0;
            var n = 0;
            foreach (var eq in attacker.Equips.ToList())
            {
                if (eq?.Def == null || !ReferenceEquals(eq.EquippedTo, attacker) || !eq.FaceUp) continue;
                if (ContinuousProtections.EquipCardEffectsAreNegated(engine, eq) || eq.IsNegated) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    if (c != null && c.Action == EffectActionKind.EquippedGainsAtkWhenAttackingFaceDownDefense)
                        n += Math.Max(0, c.Amount);
            }

            return n;
        }

        // ───────────────────────────── Harpie Lady 3 ─────────────────────────────

        sealed class AttackLock
        {
            public int ThroughTurn;
            public int Entry;
            public int FaceUp;
            public string SourceName;
        }

        static readonly ConditionalWeakTable<CardInstance, AttackLock> s_attackLocks = new();

        /// <summary>DuelEngine, right after damage calculation (ygopro EVENT_BATTLED), before any battle destruction.</summary>
        public static void OnBattled(DuelEngine engine, DuelistState atkPlayer, DuelistState defPlayer,
            CardInstance attacker, CardInstance defender)
        {
            if (engine == null || atkPlayer == null || defPlayer == null || attacker == null || defender == null) return;
            Battled(engine, atkPlayer, attacker, defPlayer, defender);
            Battled(engine, defPlayer, defender, atkPlayer, attacker);
        }

        static void Battled(DuelEngine engine, DuelistState side, CardInstance card, DuelistState otherSide,
            CardInstance other)
        {
            if (card?.Def == null || other?.Def == null || !card.FaceUp || card.IsNegated) return;
            if (!side.TryFindMonster(card, out _) || !otherSide.TryFindMonster(other, out _)) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return;
            if (!prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                    c != null && c.Action == EffectActionKind.OpponentMonsterThatBattledThisCannotAttackForTwoTurns))
                return;
            // RESET_SELF_TURN counts the End Phases of the battled monster's controller's turns, this turn included.
            var through = ReferenceEquals(engine.TurnPlayer, otherSide) ? engine.TurnNumber + 2 : engine.TurnNumber + 3;
            if (s_attackLocks.TryGetValue(other, out var old) && old.Entry == other.FieldEntryStamp &&
                old.FaceUp == other.FaceUpStamp)
                through = Math.Max(through, old.ThroughTurn);
            s_attackLocks.Remove(other);
            s_attackLocks.Add(other, new AttackLock
            {
                ThroughTurn = through,
                Entry = other.FieldEntryStamp,
                FaceUp = other.FaceUpStamp,
                SourceName = card.Name
            });
            engine.Log($"{card.Name}: {other.Name} cannot declare an attack during {otherSide.Name}'s next 2 turns.");
        }

        /// <summary>TextEffectRuntime.AttackForbiddenByEffect: a Harpie Lady 3 lock on <paramref name="attacker"/> still lasts.</summary>
        public static bool AttackLocked(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker == null || !s_attackLocks.TryGetValue(attacker, out var l)) return false;
            if (l.Entry != attacker.FieldEntryStamp || l.FaceUp != attacker.FaceUpStamp || engine.TurnNumber > l.ThroughTurn)
            {
                s_attackLocks.Remove(attacker);
                return false;
            }

            return true;
        }

        // ───────────────────────────── Thestalos the Firestorm Monarch ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.ApplyClause, <see cref="EffectActionKind.DiscardRandomThenDamageByLevelIfMonster"/> (Lua
        /// s.operation): the opponent's hand loses 1 random card (REASON_EFFECT|REASON_DISCARD); a Monster Card makes them
        /// take its Level x <see cref="EffectClause.Amount"/> damage.
        /// </summary>
        internal static void ResolveDiscardRandomThenDamageByLevel(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var opp = engine.OpponentOf(who);
            var name = source?.Name ?? "Effect";
            if (opp?.Hand == null || opp.Hand.Count == 0)
            {
                engine.Log($"{name}: {opp?.Name ?? "the opponent"} has no cards in hand to discard.");
                return;
            }

            CardInstance gone;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                var i = engine.Rng.PickIndex(opp.Hand.Count);
                if (i < 0 || i >= opp.Hand.Count) i = 0;
                gone = MonsterEffects.DiscardFromHandByInstance(opp, opp.Hand[i]);
                if (gone != null)
                {
                    engine.Log($"{name}: {opp.Name} discards {gone.Name} at random.");
                    DiscardTriggersRuntime.NoteDiscard(engine, opp, gone, who);
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            if (gone?.Def == null || !gone.Def.IsMonster) return;
            var dmg = Math.Max(0, gone.Def.level) * Math.Max(0, clause.Amount);
            if (dmg > 0)
                engine.ApplyEffectDamage(opp, dmg, name, source);
        }

        // ───────────────────────────── Riryoku ─────────────────────────────

        sealed class FirstPick
        {
            public CardInstance Target;
            public int Turn;
        }

        static readonly ConditionalWeakTable<CardInstance, FirstPick> s_riryokuFirst = new();
        static readonly ConditionalWeakTable<object, object> s_riryokuDone = new();

        static bool FaceUpMonsterOnField(DuelEngine engine, CardInstance c) =>
            c?.Def != null && c.FaceUp &&
            (engine.Player.TryFindMonster(c, out _) || engine.Opponent.TryFindMonster(c, out _));

        /// <summary>
        /// TextEffectRuntime.ApplyClause, <see cref="EffectActionKind.HalfAtkAddToOtherUntilEndOfTurn"/>. A recorded Chain Link
        /// applies one step per target; the first step that runs resolves the whole effect from the link's targets that are
        /// still legal (CompiledActivationChainRuntime.ResolvingTargetsOf), so a target that left still lets the other be
        /// halved (Lua: GetTargetCards():Filter(IsFaceup); #g==1 halves that one). Applied directly (no link), the first
        /// call remembers its target and the second resolves the pair.
        /// </summary>
        internal static void ResolveRiryoku(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance chosenTarget)
        {
            if (engine == null || who == null || source == null) return;
            List<CardInstance> group;
            if (CompiledActivationChainRuntime.ResolvingTargetsOf(source, out var targets, out var token))
            {
                if (s_riryokuDone.TryGetValue(token, out _)) return;
                s_riryokuDone.Add(token, token);
                group = targets.Where(t => FaceUpMonsterOnField(engine, t)).Distinct().ToList();
            }
            else
            {
                if (!s_riryokuFirst.TryGetValue(source, out var first) || first.Turn != engine.TurnNumber)
                {
                    s_riryokuFirst.Remove(source);
                    s_riryokuFirst.Add(source, new FirstPick { Target = chosenTarget, Turn = engine.TurnNumber });
                    return;
                }

                s_riryokuFirst.Remove(source);
                group = new[] { first.Target, chosenTarget }.Where(t => FaceUpMonsterOnField(engine, t)).Distinct()
                    .ToList();
            }

            if (group.Count == 0)
            {
                engine.Log($"{source.Name}: no target is still a face-up monster — no effect.");
                return;
            }

            if (group.Count == 1)
            {
                HalveAndGive(engine, source, group[0], null);
                return;
            }

            CardInstance AiChoose(List<CardInstance> list)
            {
                var opp = engine.OpponentOf(who);
                var theirs = list.Where(t => opp != null && opp.TryFindMonster(t, out _)).ToList();
                if (theirs.Count == 1) return theirs[0];
                if (theirs.Count == 2) return theirs.OrderByDescending(t => t.CurrentAtk).First();
                return list.OrderBy(t => t.CurrentAtk).First();
            }

            var pair = group;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => pair.Where(t => FaceUpMonsterOnField(engine, t)).ToList(),
                Kind = EffectTargetKind.FaceUpMonsterOnField,
                Prompt = $"{source.Name}: choose the monster whose ATK is halved (the other gains the ATK it loses).",
                AiChoose = AiChoose,
                Finish = halved =>
                {
                    if (halved == null) return;
                    var other = pair.FirstOrDefault(t => !ReferenceEquals(t, halved) && FaceUpMonsterOnField(engine, t));
                    HalveAndGive(engine, source, halved, other);
                    engine.NotifyPublic();
                }
            });
        }

        /// <summary>
        /// Lua s.activate: <paramref name="halved"/>'s ATK becomes half of what it is now (EFFECT_SET_ATTACK_FINAL, until the
        /// End Phase); unless it is unaffected, <paramref name="other"/> gains that half (EFFECT_UPDATE_ATTACK).
        /// </summary>
        static void HalveAndGive(DuelEngine engine, CardInstance source, CardInstance halved, CardInstance other)
        {
            if (halved == null) return;
            if (ContinuousProtections.IsUnaffectedBy(engine, halved, source))
            {
                engine.Log($"{source.Name}: {halved.Name} is unaffected — no ATK changes.");
                return;
            }

            var atk = Math.Max(0, halved.CurrentAtk);
            var half = atk / 2;
            halved.UntilEndOfTurnAtk -= atk - half;
            engine.Log($"{source.Name}: {halved.Name}'s ATK is halved until the end of this turn ({halved.CurrentAtk}).");
            if (other == null || half <= 0 || ContinuousProtections.IsUnaffectedBy(engine, other, source)) return;
            other.UntilEndOfTurnAtk += half;
            engine.Log($"{source.Name}: {other.Name} gains {half} ATK until the end of this turn ({other.CurrentAtk}).");
        }
    }
}
