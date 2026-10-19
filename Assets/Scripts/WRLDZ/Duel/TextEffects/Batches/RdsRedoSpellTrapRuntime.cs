using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RdsRedoSpellTrap" runtime (<see cref="RdsRedoSpellTrapAtoms"/>): the RDS / FET Spell and Trap Cards the v208
    /// QC failed closed, redone from their printed text and ygopro scripts. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Trap Card activation resolved</b> (Chain Burst; the Spell twin is TextEffectRuntime.NotifySpellResolved, read
    /// by Curse of Darkness and Spell Absorption): ocgcore raises EVENT_CHAIN_SOLVED for every Chain Link that resolves, its
    /// effect negated or not, but not for one whose activation was negated. A Trap Card's activation (ygopro
    /// re:IsHasType(EFFECT_TYPE_ACTIVATE), <see cref="ChainLinkAnswersRuntime.IsCardActivation"/>) is noted as its link
    /// starts to resolve in DuelEngine.ResolveChainStack (<see cref="NoteLinkResolving"/>, every resolver branch: compiled
    /// activations, Counter Trap negations, seated response answers, legacy paths) and reported once that resolution is over
    /// (<see cref="FlushResolvedTrap"/>: before the next link starts, or as the Chain ends). An activation that resolves
    /// inline, with no Chain Link (an old-text answer in a response window: Trap Hole), is reported as its card finishes
    /// (<see cref="NotifyTrapFinished"/>, from TextEffectRuntime.FinishSpellTrap / SpellTrapEffects.FinishCard), when
    /// SpellTrapEffects.BeginOrResolveManual saw it start as a card activation (<see cref="NoteActivationStart"/>). Each
    /// activation is reported once.</item>
    /// <item><b>Forced Ceasefire / Triangle Ecstasy Spark</b> (<see cref="EffectActionKind.TrapCardActivationsLockedThisTurn"/>):
    /// the shared card activation lock of <see cref="SpellTrapSealsAndImmunityRuntime.RegisterCardActivationLock"/>.</item>
    /// <item><b>"The equipped monster is destroyed" GY triggers</b> (Necklace of Command, Flint): ygopro SINGLE TRIGGER_F on
    /// EVENT_TO_GRAVE with c:IsReason(REASON_LOST_TARGET) and a check on c:GetPreviousEquipTarget(). DuelEngine.DestroyMonster
    /// notes the Equip Spell Cards of the monster it destroys (<see cref="NoteHostDestroyed"/>, while they are still
    /// equipped) and, once the rule has sent them on (UnlinkEquips), queues the trigger of each one that reached a GY
    /// (<see cref="AfterHostDestroyed"/>) with the other simultaneous triggers (SimultaneousTriggersRuntime.QueueTrigger: it
    /// activates as it resolves, as every queued trigger of this engine). A card banished instead (Banisher of the Light)
    /// or kept on the field does not trigger. A Spell/Trap trigger lock (Cold Wave) stops it.</item>
    /// </list>
    /// </summary>
    public static class RdsRedoSpellTrapRuntime
    {
        sealed class State
        {
            /// <summary>The Trap Card activation whose Chain Link is resolving now (reported when it is over).</summary>
            public ChainLink Pending;
            /// <summary>
            /// Trap Cards whose card activation (from the hand, or a Set card) started in SpellTrapEffects.BeginOrResolveManual
            /// and is not reported yet — an inline answer (no Chain Link: Trap Hole in the summon window) is reported as
            /// its card finishes.
            /// </summary>
            public readonly HashSet<CardInstance> Activating = new();
        }

        sealed class Reported
        {
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        /// <summary>Trap Card activation links already reported (the inline path and the Chain path never both report one).</summary>
        static readonly ConditionalWeakTable<ChainLink, Reported> s_reported = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────── Trap Card activation resolved ─────────────────────

        /// <summary>
        /// DuelEngine.ResolveChainStack, right after <paramref name="link"/> is taken off the Chain to resolve: a Trap Card's
        /// activation that was not negated is reported once its resolution is over.
        /// </summary>
        public static void NoteLinkResolving(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null) return;
            FlushResolvedTrap(engine);
            if (link.Negated || !IsTrapCardActivation(link)) return;
            StateOf(engine).Pending = link;
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack: before the next link starts to resolve, and as the Chain ends (before the Chain's
        /// own resolution state is cleared, so <see cref="SpellEventsAndHandSendTriggersRuntime.WasApplyingWhenActivated"/>
        /// still sees which cards applied when that Trap was activated): the Trap Card activation that resolved is reported.
        /// </summary>
        public static void FlushResolvedTrap(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Pending == null) return;
            var link = st.Pending;
            st.Pending = null;
            Report(engine, link);
        }

        /// <summary>
        /// TextEffectRuntime.FinishSpellTrap / SpellTrapEffects.FinishCard: <paramref name="card"/>'s activation finished
        /// resolving. A Trap Card activation that resolved inline (its card activation link is still on the Chain, which is
        /// not resolving it) is reported now; the link a Chain resolution is resolving is reported by
        /// <see cref="FlushResolvedTrap"/>.
        /// </summary>
        public static void NotifyTrapFinished(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine?.Chain == null || card?.Def == null || card.Def.IsMonster || !card.Def.IsTrap) return;
            if (!s_state.TryGetValue(engine, out var st)) return;
            if (st.Pending != null && ReferenceEquals(st.Pending.Card, card)) return;
            if (!st.Activating.Remove(card)) return; // a face-up Trap's effect, or already reported
            // Its idle card activation link (if any) must not be reported again.
            foreach (var l in engine.Chain.Links)
                if (l != null && ReferenceEquals(l.Card, card) && !s_reported.TryGetValue(l, out _))
                    s_reported.Add(l, new Reported());
            Fire(engine, who ?? engine.ControllerOf(card), card);
        }

        /// <summary>
        /// SpellTrapEffects.BeginOrResolveManual (every Spell/Trap activation: the player's TryActivateSpellTrap and the AI's
        /// response answers): <paramref name="card"/>'s card activation starts — a Trap Card from the hand or Set on the
        /// field (a face-up Trap's effect is not one).
        /// </summary>
        public static void NoteActivationStart(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand)
        {
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster || !card.Def.IsTrap) return;
            var setOnField = who.TryFindSpellTrap(card, out _) && !card.FaceUp;
            var inHand = fromHand && who.Hand != null && who.Hand.Contains(card);
            if (!setOnField && !inHand) return;
            StateOf(engine).Activating.Add(card);
        }

        static void Report(DuelEngine engine, ChainLink link)
        {
            if (link?.Card == null || engine.GameOver) return;
            if (s_reported.TryGetValue(link, out _)) return;
            s_reported.Add(link, new Reported());
            if (s_state.TryGetValue(engine, out var st)) st.Activating.Remove(link.Card);
            Fire(engine, link.Controller ?? engine.ControllerOf(link.Card), link.Card);
        }

        static void Fire(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (who == null || engine.GameOver) return;
            SpellEventsAndHandSendTriggersRuntime.OnCardActivationResolved(engine, who, card, trap: true);
            engine.NotifyPublic();
        }

        static bool IsTrapCardActivation(ChainLink link) =>
            link?.Card?.Def != null && link.Card.Def.IsTrap && !link.Card.Def.IsMonster &&
            ChainLinkAnswersRuntime.IsCardActivation(link);

        // ───────────────────── Necklace of Command / Flint ─────────────────────

        /// <summary>The Equip Spell Cards of a monster being destroyed whose GY trigger watches that destruction.</summary>
        public sealed class LostTargets
        {
            internal readonly List<(CardInstance Equip, EffectClause Clause)> Items = new();
        }

        /// <summary>
        /// DuelEngine.DestroyMonster, right before the destroyed <paramref name="host"/>'s Equip Cards are unlinked: each
        /// face-up Equip Spell Card equipped to it whose program carries Necklace of Command's trigger (the host was destroyed
        /// by battle and its controller <paramref name="hostController"/> also controls the Equip Spell: Lua
        /// ec:IsReason(REASON_BATTLE) and ec:IsPreviousControler(tp)) or Flint's (ec:IsReason(REASON_DESTROY)). Null: none.
        /// </summary>
        public static LostTargets NoteHostDestroyed(DuelEngine engine, DuelistState hostController, CardInstance host,
            bool byBattle)
        {
            if (engine == null || host?.Equips == null || host.Equips.Count == 0) return null;
            LostTargets found = null;
            foreach (var eq in host.Equips)
            {
                if (eq?.Def == null || eq.Def.IsMonster || !eq.Def.IsEquipSpell || !eq.FaceUp) continue;
                var ctrl = engine.ControllerOf(eq);
                if (ctrl == null || !ctrl.TryFindSpellTrap(eq, out _)) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null) continue;
                    var fires = c.Action switch
                    {
                        EffectActionKind.DrawOrDiscardRandomFromOpponentHand =>
                            byBattle && ReferenceEquals(ctrl, hostController),
                        EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed => true,
                        _ => false
                    };
                    if (!fires) continue;
                    found ??= new LostTargets();
                    found.Items.Add((eq, c));
                }
            }

            return found;
        }

        /// <summary>
        /// DuelEngine.DestroyMonster, right after the destroyed monster's Equip Cards were unlinked: each noted Equip Spell
        /// Card that the rule sent to a GY (REASON_LOST_TARGET) queues its mandatory trigger for that GY's owner.
        /// </summary>
        public static void AfterHostDestroyed(DuelEngine engine, LostTargets lost)
        {
            if (engine == null || lost == null || engine.GameOver) return;
            foreach (var (eq, clause) in lost.Items)
            {
                var who = GraveyardOf(engine, eq);
                if (who == null) continue; // banished instead, or still on the field
                var stamp = eq.GyEntryStamp;
                var c = clause;
                var card = eq;
                SimultaneousTriggersRuntime.QueueTrigger(engine, who, card, optional: false, () =>
                {
                    if (engine.GameOver) return;
                    // Its TRIGGER_F cannot be activated under Cold Wave (re:IsSpellTrapEffect()).
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) return;
                    if (c.Action == EffectActionKind.DrawOrDiscardRandomFromOpponentHand)
                        ResolveNecklace(engine, who, card, c);
                    else
                        ResolveReEquip(engine, who, card, stamp);
                });
                engine.Log($"{card.Name} was sent to the GY because its monster was destroyed — its effect activates.");
            }
        }

        static DuelistState GraveyardOf(DuelEngine engine, CardInstance card)
        {
            if (engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) return engine.Player;
            if (engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card)) return engine.Opponent;
            return null;
        }

        /// <summary>
        /// Necklace of Command (Lua s.efftg / s.effop): <paramref name="who"/> draws 1 card or discards 1 random card from the
        /// opponent's hand. Both possible: <paramref name="who"/> chooses (a human taps the card to draw, CANCEL discards; the
        /// AI draws unless its Deck is nearly empty); only one possible: that one; neither: nothing.
        /// </summary>
        static void ResolveNecklace(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            var opp = engine.OpponentOf(who);
            var n = clause.Amount > 0 ? clause.Amount : 1;
            // Lua Duel.IsPlayerCanDraw(tp, 1) / GetFieldGroupCount(tp, 0, LOCATION_HAND) > 0.
            var canDraw = who.Deck != null && who.Deck.Count >= n &&
                          !ContinuousProtections.BlocksDrawOutsideDrawPhase(engine, who);
            var canDiscard = opp?.Hand != null && opp.Hand.Count > 0;
            void Draw()
            {
                engine.Log($"{card.Name}: {who.Name} draws {n} card.");
                engine.Draw(who, n);
            }

            void Discard() => TextEffectRuntime.DiscardRandomFromOpponentHand(engine, opp, card, clause);
            if (!canDraw && !canDiscard)
            {
                engine.Log($"{card.Name}: {who.Name} cannot draw and the opponent's hand is empty — nothing happens.");
                return;
            }

            if (!canDiscard)
            {
                Draw();
                return;
            }

            if (!canDraw)
            {
                Discard();
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => new List<CardInstance> { card },
                Kind = EffectTargetKind.CardInEitherGy,
                Prompt = $"{card.Name}: tap it to draw {n} card, or CANCEL to discard {n} random card from your " +
                         "opponent's hand.",
                AskWithOneCandidate = true,
                // The AI draws, unless its Deck is nearly empty.
                AiChoose = list => who.Deck.Count > 5 ? list.FirstOrDefault() : null,
                Cancel = Discard,
                Finish = chosen =>
                {
                    if (chosen != null) Draw();
                    else Discard();
                }
            });
        }

        /// <summary>
        /// Flint (Lua s.target / s.operation): <paramref name="who"/> targets 1 face-up monster on either field and equips this
        /// card to it from the GY — only while this card is still in the GY on this trip (c:IsRelateToEffect) and the target is
        /// still a face-up monster on the field. No face-up monster: the trigger is not activated. A human picks (mandatory);
        /// the AI takes the opponent's strongest face-up monster, else its own weakest.
        /// </summary>
        static void ResolveReEquip(DuelEngine engine, DuelistState who, CardInstance card, int stamp)
        {
            bool StillInGy() => card.GyEntryStamp == stamp && who.Graveyard != null && who.Graveyard.Contains(card);
            List<CardInstance> Candidates()
            {
                var list = new List<CardInstance>();
                if (!StillInGy()) return list;
                foreach (var side in new[] { engine.Player, engine.Opponent })
                {
                    if (side == null) continue;
                    foreach (var m in side.MonstersOnField())
                        if (m != null && m.FaceUp && !ContinuousProtections.TargetedEffectBlocked(engine, m, card))
                            list.Add(m);
                }

                return list;
            }

            if (Candidates().Count == 0)
            {
                engine.Log($"{card.Name}: no face-up monster on the field — it is not equipped.");
                return;
            }

            var opp = engine.OpponentOf(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.FaceUpMonsterOnField,
                Prompt = $"{card.Name}: target 1 face-up monster on the field to equip this card to.",
                AiChoose = list =>
                    list.Where(m => opp != null && opp.TryFindMonster(m, out _)).OrderByDescending(m => m.CurrentAtk)
                        .FirstOrDefault() ??
                    list.OrderBy(m => m.CurrentAtk).FirstOrDefault(),
                Finish = target =>
                {
                    if (target == null) return;
                    var side = engine.ControllerOf(target);
                    if (!StillInGy() || side == null || !side.TryFindMonster(target, out _) || !target.FaceUp)
                    {
                        engine.Log($"{card.Name}: the target or this card is gone — it is not equipped.");
                        return;
                    }

                    GuardiansAndEquipEventsRuntime.ApplyEquipFromGy(engine, who, target, card);
                }
            });
        }

        // ───────────────────── Forced Ceasefire / Triangle Ecstasy Spark ─────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn"/> (Triangle Ecstasy Spark): the
        /// ATK of every face-up monster named <see cref="EffectClause.ConditionName"/> in either Monster Zone right now becomes
        /// <see cref="EffectClause.Amount"/> until the end of this turn (an until-end-of-turn delta; a final halving such as
        /// Mirror Wall's is undone in the delta, so the shown ATK is exactly the amount).
        /// </summary>
        public static void SetNamedAtkUntilEndOfTurn(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (engine == null || clause == null || string.IsNullOrEmpty(clause.ConditionName) || clause.Amount < 0) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m?.Def == null || !m.FaceUp || !m.IsNamed(clause.ConditionName)) continue;
                    if (ContinuousProtections.IsUnaffectedBy(engine, m, source)) continue;
                    var halvings = System.Math.Min(System.Math.Max(m.AtkHalvings, 0), 30);
                    var delta = (clause.Amount - m.CurrentAtk) << halvings;
                    m.UntilEndOfTurnAtk += delta;
                    engine.Log($"{source?.Name ?? "Effect"}: {m.Name}'s ATK becomes {m.CurrentAtk} until the end of this turn.");
                }
            }
        }

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.TrapCardActivationsLockedThisTurn"/>: the players the clause names
        /// (<see cref="EffectSide.Both"/>: both; <see cref="EffectSide.Opponent"/>: the opponent of <paramref name="who"/>)
        /// cannot activate Trap Cards for the rest of this turn (Lua RESET_PHASE|PHASE_END).
        /// </summary>
        public static void LockTrapCardActivationsThisTurn(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var locked = clause.Side == EffectSide.Both ? null : engine.OpponentOf(who);
            if (clause.Side != EffectSide.Both && locked == null) return;
            SpellTrapSealsAndImmunityRuntime.RegisterCardActivationLock(engine, locked, spells: false, traps: true,
                engine.TurnNumber, source);
            engine.Log(locked == null
                ? $"{source?.Name ?? "Effect"}: no Trap Cards can be activated until the end of this turn."
                : $"{source?.Name ?? "Effect"}: {locked.Name} cannot activate Trap Cards until the end of this turn.");
        }
    }
}
