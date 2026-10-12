using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SpellTrapSealsAndImmunity batch runtime (<see cref="SpellTrapSealsAndImmunityAtoms"/>). Reads compiled programs only — no
    /// card-id branches.
    /// <list type="bullet">
    /// <item><b>Sonic Jammer</b> (<see cref="EffectActionKind.OpponentCannotActivateSpellCardsUntilEndOfNextTurn"/>): a per-player
    /// lock with an expiry turn (<see cref="RegisterSpellCardLock"/>); <see cref="ActivationLocked"/> (from
    /// ContinuousNegationRuntime.ActivationLocked, so every activation gate and the AI's legal lists ask it) refuses the
    /// activation of a Spell Card from the hand or Set; the effects of a face-up Spell stay usable.</item>
    /// <item><b>Dust Barrier</b> (<see cref="EffectActionKind.NormalMonstersUnaffectedByOpponentSpells"/>):
    /// <see cref="ShieldsFromOpponentSpell"/>, read by MonsterNegationAndControlLocksRuntime.ShieldsFromSpell, so every site
    /// that honours Non-Spellcasting Area (resolution guard, Spell auras, Equip grants, Spell attack locks) honours it too;
    /// <see cref="OnStandbyPhase"/> counts its controller's Standby Phases
    /// (<see cref="EffectActionKind.DestroyThisOnYourNthStandbyAfterActivation"/>, kept counting while negated).</item>
    /// <item><b>Armor Break</b> (<see cref="EffectClause.NegateRespondsToEquipSpellOnly"/>): <see cref="ChainAnswerAllowed"/>,
    /// from TextEffectRuntime.CanAnswerChainLinkWithClause.</item>
    /// </list>
    /// The Talismans reuse the ContinuousNegation sources (lock + negation) and FieldSpellEffects' named self-destruction
    /// (<see cref="EffectClause.NamedFaceUpInYourMonsterZone"/>).
    /// </summary>
    public static class SpellTrapSealsAndImmunityRuntime
    {
        sealed class SpellCardLock
        {
            public DuelistState Who;
            public int UntilTurn;
            public string Source;
        }

        sealed class State
        {
            /// <summary>Sonic Jammer: players who cannot activate Spell Cards through the end of turn UntilTurn.</summary>
            public readonly List<SpellCardLock> SpellCardLocks = new();
        }

        /// <summary>Dust Barrier: its controller's Standby Phases counted during one stay on the field.</summary>
        sealed class StandbyCount
        {
            public int Entry;
            public int FaceDown;
            public int Count;
            /// <summary>Lua SetCountLimit(1): the turn whose Standby Phase was last counted.</summary>
            public int LastTurn;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<CardInstance, StandbyCount> s_standby = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── Sonic Jammer ─────────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.OpponentCannotActivateSpellCardsUntilEndOfNextTurn"/>: the opponent of
        /// <paramref name="who"/> cannot activate Spell Cards until the end of the next turn's End Phase (Lua
        /// RESET_PHASE|PHASE_END, 2: this turn's End Phase counts as the first).
        /// </summary>
        public static void RegisterSpellCardLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            var st = StateOf(engine);
            st.SpellCardLocks.RemoveAll(l => l.UntilTurn < engine.TurnNumber);
            st.SpellCardLocks.Add(new SpellCardLock
            {
                Who = opp,
                UntilTurn = engine.TurnNumber + 1,
                Source = source?.Name ?? "Effect"
            });
            engine.Log($"{source?.Name ?? "Effect"}: {opp.Name} cannot activate Spell Cards until the end of the next turn.");
        }

        /// <summary>
        /// ContinuousNegationRuntime.ActivationLocked: <paramref name="who"/> may not activate the Spell Card
        /// <paramref name="card"/> (in their hand, or Set on their field) while a Sonic Jammer lock on them lasts.
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster || !card.Def.IsSpell) return false;
            if (!s_state.TryGetValue(engine, out var st) || st.SpellCardLocks.Count == 0) return false;
            if (!OwnSetOrHand(who, card)) return false;
            foreach (var l in st.SpellCardLocks)
            {
                if (l.Who != who || engine.TurnNumber > l.UntilTurn) continue;
                reason = $"{l.Source}: you cannot activate Spell Cards until the end of the next turn.";
                return true;
            }

            return false;
        }

        /// <summary>The card's activation: from the hand, or a Set card on the field (a face-up card's effect is not one).</summary>
        static bool OwnSetOrHand(DuelistState who, CardInstance card) =>
            (who.Hand != null && who.Hand.Contains(card)) ||
            ((who.TryFindSpellTrap(card, out _) ||
              (who.FieldSpellZone != null && ReferenceEquals(who.FieldSpellZone.Occupant, card))) && !card.FaceUp);

        // ───────────────────────────── Dust Barrier ─────────────────────────────

        /// <summary>
        /// Dust Barrier: <paramref name="victim"/> — a face-up Normal Monster (or Token) in a Monster Zone — is unaffected by
        /// the Spell <paramref name="source"/> when the player whose card it is (Lua re:GetOwnerPlayer: its controller, or
        /// the player holding it off the field) is the opponent of a face-up, un-negated, resolved Dust Barrier's controller.
        /// </summary>
        public static bool ShieldsFromOpponentSpell(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (engine?.Player == null || engine.Opponent == null) return false;
            if (source?.Def == null || source.Def.IsMonster || !source.Def.IsSpell) return false;
            if (victim?.Def == null || !victim.FaceUp || !victim.Def.IsMonster) return false;
            if (!victim.IsToken && !victim.Def.IsNormalMonster) return false;
            if (!InMonsterZone(engine, victim)) return false;
            var sourcePlayer = EffectPlayerOf(engine, source);
            if (sourcePlayer == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == sourcePlayer) continue; // a Dust Barrier shields only from its controller's opponent
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st == null || ReferenceEquals(st, source) || !Applies(engine, st,
                            EffectActionKind.NormalMonstersUnaffectedByOpponentSpells))
                        continue;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The player a Spell's effect belongs to: its controller on the field, else the player holding it.</summary>
        static DuelistState EffectPlayerOf(DuelEngine engine, CardInstance source)
        {
            var ctrl = engine.ControllerOf(source);
            if (ctrl != null) return ctrl;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if ((side.Hand != null && side.Hand.Contains(source)) ||
                    (side.Graveyard != null && side.Graveyard.Contains(source)))
                    return side;
            }

            return null;
        }

        /// <summary>
        /// Standby Phase hook (TextEffectRuntime.FirePhaseTriggersPass, the Standby Phase of <paramref name="who"/>): each
        /// face-up card <paramref name="who"/> controls with <see cref="EffectActionKind.DestroyThisOnYourNthStandbyAfterActivation"/>
        /// counts this Standby Phase (Lua CANNOT_DISABLE: also while negated) and is destroyed on the Nth one.
        /// </summary>
        public static void OnStandbyPhase(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            foreach (var st in who.SpellTrapsOnField().ToList())
            {
                if (st?.Def == null || !st.FaceUp || ActivationPending(engine, st)) continue;
                var clause = ClauseOf(st, EffectActionKind.DestroyThisOnYourNthStandbyAfterActivation);
                if (clause == null) continue;
                // Lua RESETS_STANDARD: a new stay on the field, or a turn face-down, starts a new count.
                var rec = s_standby.GetValue(st, _ => new StandbyCount
                {
                    Entry = st.FieldEntryStamp, FaceDown = st.FaceDownStamp, LastTurn = -1
                });
                if (rec.Entry != st.FieldEntryStamp || rec.FaceDown != st.FaceDownStamp)
                {
                    rec.Entry = st.FieldEntryStamp;
                    rec.FaceDown = st.FaceDownStamp;
                    rec.Count = 0;
                    rec.LastTurn = -1;
                }

                if (rec.LastTurn == engine.TurnNumber) continue;
                rec.LastTurn = engine.TurnNumber;
                rec.Count++;
                var need = clause.Amount > 0 ? clause.Amount : 2;
                engine.Log($"{st.Name}: {rec.Count} of your Standby Phase(s) since its activation.");
                if (rec.Count < need) continue;
                engine.Log($"{st.Name} is destroyed (your Standby Phase #{need} after its activation).");
                TextEffectRuntime.DestroyCard(engine, st, st, byPlayer: who);
            }
        }

        // ───────────────────────────── Armor Break ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CanAnswerChainLinkWithClause: an answer with <see cref="EffectClause.NegateRespondsToEquipSpellOnly"/>
        /// answers only the activation of an Equip Spell Card (from the hand or Set), never a later effect of a face-up one.
        /// </summary>
        public static bool ChainAnswerAllowed(EffectClause clause, ChainLink target, out string reason)
        {
            reason = "OK";
            if (clause == null || !clause.NegateRespondsToEquipSpellOnly) return true;
            if (target?.Card?.Def != null && target.Card.Def.IsEquipSpell && ChainLinkAnswersRuntime.IsCardActivation(target))
                return true;
            reason = "Only answers the activation of an Equip Spell Card.";
            return false;
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A face-up, un-negated Spell/Trap whose activation has resolved and whose program has <paramref name="action"/>.</summary>
        static bool Applies(DuelEngine engine, CardInstance st, EffectActionKind action) =>
            st?.Def != null && st.FaceUp && !st.IsNegated && st.EquippedTo == null && !ActivationPending(engine, st) &&
            ClauseOf(st, action) != null;

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    return c;
            return null;
        }

        static bool ActivationPending(DuelEngine engine, CardInstance card)
        {
            var links = engine.Chain?.Links;
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && ReferenceEquals(l.Card, card) && !l.Resolved)
                    return true;
            return false;
        }

        static bool InMonsterZone(DuelEngine engine, CardInstance card) =>
            (engine.Player != null && engine.Player.TryFindMonster(card, out _)) ||
            (engine.Opponent != null && engine.Opponent.TryFindMonster(card, out _));
    }
}
