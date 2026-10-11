using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// BattleDestroyedGyRecovery batch (v162): the GY-entry stamp of a card (ygopro GetTurnID / GetReason /
    /// GetReasonPlayer while it is in the GY). Every move into a GY stamps it: RedirectAndReplaceRuntime.PutInGraveyard
    /// (the hand / Deck helpers), DuelEngine.SendCardToGrave (sent by a card's effect, or for an unknown reason) and
    /// DuelEngine.DestroyMonster (destroyed by battle or by a card's effect). The discard event
    /// (<see cref="DiscardEvents.Record"/> / <see cref="DiscardEvents.NoteCause"/>) and TextEffectRuntime.DestroyCard
    /// add the reason and the reason player of that same trip. A card entering the field clears it
    /// (DuelEngine.ResetOnFieldEntry). A stamp is read only while the card is still in that GY; any later trip into a GY
    /// stamps it again, so a card that left and came back is never read with its old stamp.
    /// </summary>
    public static class GyEntryStamps
    {
        /// <summary><paramref name="card"/> just entered a GY: a new stamp for this turn.</summary>
        public static void Entered(DuelEngine engine, CardInstance card, GyEntryReason reason = GyEntryReason.None,
            DuelistState reasonPlayer = null)
        {
            if (card == null) return;
            card.GyEntryTurn = engine?.TurnNumber ?? 0;
            card.GyEntryReason = reason;
            card.GyEntryReasonPlayer = reasonPlayer;
            card.GyEntryStamp++;
            // SimpleTriggersAndSwarm batch: a "When this card is sent to the GY" (from anywhere) trigger is queued here.
            SimpleTriggersAndSwarmRuntime.NoteEnteredGy(engine, card);
        }

        /// <summary>
        /// Add a reason (and the reason player, when known) to the stamp of the trip <paramref name="card"/> just made
        /// into a GY this turn. Nothing when it is not in a GY (banished instead) or was not stamped this turn.
        /// </summary>
        public static void AddReason(DuelEngine engine, CardInstance card, GyEntryReason reason, DuelistState reasonPlayer)
        {
            if (engine == null || card == null || card.GyEntryTurn == 0 || card.GyEntryTurn != engine.TurnNumber) return;
            if (!(engine.Player?.Graveyard?.Contains(card) ?? false) &&
                !(engine.Opponent?.Graveyard?.Contains(card) ?? false))
                return;
            card.GyEntryReason |= reason;
            if (reasonPlayer != null) card.GyEntryReasonPlayer = reasonPlayer;
        }

        /// <summary>The card left the GY for the field: its stamp ends.</summary>
        public static void Left(CardInstance card)
        {
            if (card == null) return;
            card.GyEntryTurn = 0;
            card.GyEntryReason = GyEntryReason.None;
            card.GyEntryReasonPlayer = null;
        }

        /// <summary>The player who holds <paramref name="card"/> (field controller, else the pile it is in); null if none.</summary>
        public static DuelistState HolderOf(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            var c = engine.ControllerOf(card);
            if (c != null) return c;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.Graveyard.Contains(card) || side.Hand.Contains(card) || side.Banished.Contains(card))
                    return side;
            }

            return null;
        }

        /// <summary><paramref name="card"/> is in <paramref name="who"/>'s GY and was sent there this turn.</summary>
        public static bool EnteredThisTurn(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine != null && who?.Graveyard != null && card != null && who.Graveyard.Contains(card) &&
            card.GyEntryTurn != 0 && card.GyEntryTurn == engine.TurnNumber;

        /// <summary>
        /// A monster in <paramref name="who"/>'s GY that was destroyed by battle and sent there this turn (ygopro
        /// GetTurnID() == Duel.GetTurnCount() and IsReason(REASON_BATTLE)).
        /// </summary>
        public static bool DestroyedByBattleThisTurn(DuelEngine engine, DuelistState who, CardInstance card) =>
            EnteredThisTurn(engine, who, card) && card.Def != null && card.Def.IsMonster &&
            (card.GyEntryReason & GyEntryReason.Battle) != 0;

        /// <summary>
        /// A card in <paramref name="who"/>'s GY that was sent there this turn by the other player (ygopro GetTurnID()
        /// == turn and GetReasonPlayer() == 1-tp): destroyed, discarded or sent by that player's card effect.
        /// </summary>
        public static bool SentByOpponentThisTurn(DuelEngine engine, DuelistState who, CardInstance card) =>
            EnteredThisTurn(engine, who, card) && card.GyEntryReasonPlayer != null &&
            !ReferenceEquals(card.GyEntryReasonPlayer, who);

        /// <summary>
        /// "If this card is in the GY because it was destroyed by battle" (Helpoemer): in <paramref name="who"/>'s GY,
        /// and its trip there was a destruction by battle, in any turn.
        /// </summary>
        public static bool InGyBecauseDestroyedByBattle(DuelistState who, CardInstance card) =>
            who?.Graveyard != null && card != null && who.Graveyard.Contains(card) && card.GyEntryTurn != 0 &&
            (card.GyEntryReason & GyEntryReason.Battle) != 0;
    }

    /// <summary>
    /// Card batch "BattleDestroyedGyRecovery" (v162) runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item>Crimson Sentry / Lady Panther / The Forgiving Maiden / Fairy Guardian: the targets are filtered by the
    /// GY-entry stamp (<see cref="FilterTargets"/>); <see cref="EffectActionKind.PlaceGyTargetOnBottomOfDeck"/> puts the
    /// target on the bottom of the Deck.</item>
    /// <item>Return of the Doomed: <see cref="RegisterEndPhaseReturn"/>, then <see cref="OnEndPhase"/> (DuelEngine.EndTurn):
    /// the AI or a lone candidate resolves at once; a human with 2+ candidates picks (mandatory), queued behind other End
    /// Phase picks (<see cref="OfferQueuedPick"/>, DuelEngine.ResumeEndTurnAfterTrigger).</item>
    /// <item>Revival Jam: its optional trigger asks its controller to pay the LP (the LP-cost picker, Cancel = no) and
    /// flags it for as long as it stays in that GY (<see cref="GyEntryStamps"/> stamp); at its controller's next Standby
    /// Phase <see cref="OnStandby"/> Special Summons it in face-up Defense Position (no Chain Link).</item>
    /// <item>Helpoemer: <see cref="FireEndOfBattlePhaseFromGy"/> (BattlePhaseEndRuntime.FireEndOfBattlePhase) — at the end
    /// of each of the opponent's Battle Phases, while it is in its owner's GY because it was destroyed by battle, the
    /// opponent discards 1 random card. It resolves inline like the engine's other end-of-Battle-Phase effects.</item>
    /// <item>Rope of Life: answers the end-of-Damage-Step / "monster sent to your GY" windows of BattleWindowsRuntime for
    /// monsters destroyed by battle (<see cref="TryActivationCondition"/>); the whole hand is discarded as the response is
    /// seated (<see cref="PaySeatCost"/>, refunded if the target pick is cancelled); the human's Attack / Defense pick is
    /// kept with the link (<see cref="TryResolvePick"/>) and used at resolution (<see cref="ResolveRope"/>).</item>
    /// </list>
    /// </summary>
    public static class BattleDestroyedGyRecoveryRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Rope of Life): in your end-of-Damage-Step or "monster sent to
        /// your GY" window, a monster destroyed by battle was sent to your GY.
        /// </summary>
        public const string MonsterDestroyedByBattleSentToYourGy = "MonsterDestroyedByBattleSentToYourGy";

        sealed class EndPhaseReturn
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Turn;
            public bool Fired;
        }

        sealed class Revival
        {
            public DuelistState Who;
            public CardInstance Card;
            public int Stamp;
        }

        sealed class RevivalPrompt
        {
            public DuelistState Who;
            public CardInstance Card;
            public int Stamp;
            public int Cost;
        }

        sealed class Placement
        {
            public int Zone = -1;
            public BattlePosition? Position;
        }

        sealed class State
        {
            public readonly List<EndPhaseReturn> Returns = new();
            public readonly List<EndPhaseReturn> Queue = new();
            public readonly List<Revival> Armed = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, EndPhaseReturn> s_returnPicks = new();
        static readonly ConditionalWeakTable<PendingActivation, RevivalPrompt> s_revivalPrompts = new();
        /// <summary>Rope of Life: the cards discarded as it was seated, until its target pick is made or cancelled.</summary>
        static readonly ConditionalWeakTable<CardInstance, List<CardInstance>> s_seatPaid = new();
        /// <summary>Rope of Life: the human's Monster Zone and position, chosen with the target.</summary>
        static readonly ConditionalWeakTable<CardInstance, Placement> s_placements = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static bool HasClause(CardInstance card, System.Predicate<EffectClause> match)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && match(c));
        }

        // ───────────────────────────── targets ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CollectTargets tail: "destroyed as a result of battle during this turn" (monsters in your GY
        /// sent there this turn by battle), "sent to your GY by your opponent's card effect during this turn", and Rope
        /// of Life's "those monsters" (the battle sends its window answers, that can be Special Summoned).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null) return;
            if (c.RequiresTargetSentToGyByBattleThisTurn)
                list.RemoveAll(t => !GyEntryStamps.DestroyedByBattleThisTurn(engine, who, t));
            if (c.RequiresTargetSentToGyByOpponentThisTurn)
                list.RemoveAll(t => !GyEntryStamps.SentByOpponentThisTurn(engine, who, t));
            if (c.TargetsThoseMonstersDestroyedByBattle)
            {
                var those = ThoseMonsters(engine, who);
                list.RemoveAll(t => !those.Contains(t) || !CanBeSummonedFromGy(engine, who, t));
            }
        }

        // ───────────────────── Crimson Sentry / Fairy Guardian ─────────────────────

        /// <summary>
        /// SimpleAi: a self-Tributing GY recovery of this batch is used only when it brings back a monster with more ATK
        /// than the one it Tributes, to the hand or the top of the Deck; never a bottom-of-Deck return (Crimson Sentry,
        /// Fairy Guardian), which only trades a monster for nothing.
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance monster)
        {
            var prog = monster?.Def != null ? CompiledEffectCache.GetOrCompile(monster.Def) : null;
            var clause = prog?.ClausesFor(EffectTiming.Activate)?.Find(c => c != null && c.RequiresTributeThis &&
                (c.RequiresTargetSentToGyByBattleThisTurn || c.RequiresTargetSentToGyByOpponentThisTurn));
            if (clause == null) return false;
            if (clause.Action == EffectActionKind.PlaceGyTargetOnBottomOfDeck) return true;
            var targets = ai.Graveyard.Where(g => g?.Def != null && g.Def.IsMonster).ToList();
            FilterTargets(engine, ai, clause, targets);
            return !targets.Any(t => (t.Def?.atk ?? 0) > monster.CurrentAtk);
        }

        /// <summary>
        /// <see cref="EffectActionKind.PlaceGyTargetOnBottomOfDeck"/>: the target goes from the GY to the bottom of its
        /// owner's Deck (a Fusion Monster to the Extra Deck), if it is still in a GY (ygopro IsRelateToEffect).
        /// </summary>
        public static void PlaceGyTargetOnBottomOfDeck(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null || target == null) return;
            var opp = engine.OpponentOf(who);
            var pile = who.Graveyard.Contains(target) ? who
                : opp != null && opp.Graveyard.Contains(target) ? opp
                : null;
            if (pile == null)
            {
                engine.Log($"{target.Name} is no longer in the GY — nothing is returned to the Deck.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, target))
            {
                engine.Log("Necrovalley prevents the target from leaving the GY.");
                return;
            }

            pile.Graveyard.Remove(target);
            GyEntryStamps.Left(target);
            if (target.Def != null && target.Def.IsExtraDeck)
            {
                pile.ExtraDeck.Add(target.CardId);
                engine.Log($"{target.Name} returns from the GY to the Extra Deck.");
            }
            else
            {
                pile.Deck.Add(target.CardId);
                engine.Log($"{target.Name} is placed on the bottom of {pile.Name}'s Deck.");
            }
        }

        // ───────────────────────────── Return of the Doomed ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.AddMonsterDestroyedByBattleThisTurnFromGyToHandAtEndPhase"/>: the effect applies
        /// at the end of this turn (<see cref="OnEndPhase"/>), once.
        /// </summary>
        public static void RegisterEndPhaseReturn(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            StateOf(engine).Returns.Add(new EndPhaseReturn { Who = who, Source = source, Turn = engine.TurnNumber });
            engine.Log($"{source?.Name}: at the end of this turn, {who.Name} adds 1 monster destroyed by battle this " +
                       "turn from the GY to the hand.");
        }

        static List<CardInstance> ReturnCandidates(DuelEngine engine, EndPhaseReturn r) =>
            r.Who.Graveyard.Where(g => GyEntryStamps.DestroyedByBattleThisTurn(engine, r.Who, g) &&
                                       !ContinuousProtections.GyMovementLocked(engine, r.Source, g)).ToList();

        static CardInstance AiReturnPick(List<CardInstance> candidates) =>
            candidates.OrderByDescending(c => c.Def?.atk ?? 0).ThenByDescending(c => c.Level).First();

        /// <summary>DuelEngine.EndTurn, after the End Phase triggers: every Return of the Doomed effect of this turn.</summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st)) return;
            st.Returns.RemoveAll(r => r.Turn != engine.TurnNumber);
            foreach (var r in st.Returns.ToList())
            {
                if (engine.GameOver) return;
                if (r.Fired) continue;
                r.Fired = true;
                var candidates = ReturnCandidates(engine, r);
                if (candidates.Count == 0)
                {
                    engine.Log($"{r.Source?.Name}: no monster destroyed by battle this turn in the GY — no effect.");
                    continue;
                }

                if (!engine.IsHumanControlled(r.Who) || candidates.Count == 1)
                {
                    AddToHand(engine, r, AiReturnPick(candidates));
                    continue;
                }

                st.Queue.Add(r);
            }

            OfferQueuedPick(engine);
        }

        static void AddToHand(DuelEngine engine, EndPhaseReturn r, CardInstance pick)
        {
            if (pick == null || !r.Who.Graveyard.Contains(pick)) return;
            r.Who.Graveyard.Remove(pick);
            r.Who.Hand.Add(pick);
            engine.Log($"{r.Source?.Name}: {pick.Name} is added from the GY to {r.Who.Name}'s hand.");
        }

        /// <summary>
        /// A human's queued Return of the Doomed pick (mandatory: choose 1 of the monsters destroyed by battle this
        /// turn), asked when no other pick or window is open (OnEndPhase, DuelEngine.ResumeEndTurnAfterTrigger).
        /// </summary>
        public static bool OfferQueuedPick(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Queue.Count == 0) return false;
            if (engine.PendingActivation != null || engine.IsAwaitingResponse || engine.GameOver) return false;
            while (st.Queue.Count > 0)
            {
                var r = st.Queue[0];
                st.Queue.RemoveAt(0);
                if (r.Turn != engine.TurnNumber || engine.Phase != DuelPhase.End) continue;
                var candidates = ReturnCandidates(engine, r);
                if (candidates.Count == 0) continue;
                if (candidates.Count == 1)
                {
                    AddToHand(engine, r, candidates[0]);
                    continue;
                }

                var pending = new PendingActivation
                {
                    Controller = r.Who,
                    Card = r.Source,
                    TargetKind = EffectTargetKind.CardInEitherGy,
                    IsMonsterEffect = false,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = "choose 1 monster destroyed by battle this turn in your GY to add to your hand."
                };
                pending.LegalTargets.AddRange(candidates);
                s_returnPicks.Add(pending, r);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            return false;
        }

        static void GoOn(DuelEngine engine)
        {
            if (engine.GameOver) return;
            if (OfferQueuedPick(engine))
            {
                engine.NotifyPublic();
                return;
            }

            engine.ResumeEndTurnAfterTrigger();
            engine.NotifyPublic();
        }

        // ───────────────────────────── picks (hooks) ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.TryResolveWithTarget hook: Return of the Doomed's End Phase pick. Rope of Life's target pick
        /// (a seated response) keeps the Monster Zone and position its controller chose, then lets the seat go on
        /// (returns false).
        /// </summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null) return false;
            if (s_returnPicks.TryGetValue(pending, out var r))
            {
                var pick = engine.ResolveLegalEffectTarget(target);
                if (pick == null)
                {
                    engine.Log("Choose one of the highlighted monsters (this effect is mandatory).");
                    return true;
                }

                s_returnPicks.Remove(pending);
                engine.ClearPendingActivation();
                AddToHand(engine, r, pick);
                ok = true;
                GoOn(engine);
                return true;
            }

            if (pending.ChainActivation && pending.SeatTiming != ResponseTiming.None && pending.Card != null &&
                HasClause(pending.Card, c => c.Action == EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk))
            {
                s_placements.Remove(pending.Card);
                s_placements.Add(pending.Card, new Placement
                {
                    Zone = engine.PendingSpecialSummonZone,
                    Position = engine.PendingSpecialSummonPosition
                });
                // Used when the link resolves (ResolveRope), not by the next Special Summon of anyone else.
                engine.ClearPendingSpecialSummonPlacement();
            }

            return false;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook: Revival Jam's LP cost declined (no flag); Return of the Doomed's
        /// mandatory pick made automatically.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null) return false;
            if (s_revivalPrompts.TryGetValue(pending, out var prompt))
            {
                s_revivalPrompts.Remove(pending);
                engine.ClearPendingActivation();
                engine.Log($"{prompt.Card?.Name}: {prompt.Who?.Name} does not pay {prompt.Cost} LP.");
                engine.NotifyPublic();
                return true;
            }

            if (!s_returnPicks.TryGetValue(pending, out var r)) return false;
            s_returnPicks.Remove(pending);
            engine.ClearPendingActivation();
            var candidates = ReturnCandidates(engine, r);
            if (candidates.Count > 0)
                AddToHand(engine, r, AiReturnPick(candidates));
            GoOn(engine);
            return true;
        }

        // ───────────────────────────── Revival Jam ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ArmThisSpecialSummonAtNextStandby"/> (its optional trigger, TryResolveSentToGy):
        /// "You can pay N LP". The AI pays when it keeps LP; a human answers through the LP-cost picker (PAY N, or Cancel).
        /// </summary>
        public static void ArmRevival(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null || engine.GameOver) return;
            if (!who.Graveyard.Contains(source)) return;
            var cost = clause.PayLpAmount;
            if (cost > 0 && who.LifePoints < cost)
            {
                engine.Log($"{source.Name}: {who.Name} cannot pay {cost} LP.");
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                if (cost > 0 && who.LifePoints <= cost) return; // the AI never pays its last LP
                Arm(engine, who, source, cost);
                return;
            }

            if (engine.PendingActivation != null)
            {
                engine.Log($"{source.Name}: another choice is open — its optional effect is not used.");
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                AwaitingLpCost = true,
                CostNumeric = cost,
                PromptOverride = $"pay {cost} LP to Special Summon this card in your next Standby Phase, or Cancel."
            };
            pending.LpCostChoices.Add(cost);
            s_revivalPrompts.Add(pending,
                new RevivalPrompt { Who = who, Card = source, Stamp = source.GyEntryStamp, Cost = cost });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
        }

        static void Arm(DuelEngine engine, DuelistState who, CardInstance card, int cost)
        {
            if (cost > 0)
                engine.PayLifePoints(who, cost,
                    $"Cost: pay {cost} LP → {who.Name} at {System.Math.Max(0, who.LifePoints - cost)} LP.");
            var st = StateOf(engine);
            st.Armed.RemoveAll(a => ReferenceEquals(a.Card, card));
            st.Armed.Add(new Revival { Who = who, Card = card, Stamp = card.GyEntryStamp });
            engine.Log($"{card.Name} will be Special Summoned in face-up Defense Position in {who.Name}'s next Standby Phase.");
        }

        /// <summary>TextEffectRuntime.TryFinishLpCost hook: the human paid Revival Jam's LP cost.</summary>
        public static bool TryFinishLpCost(DuelEngine engine, int amount, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_revivalPrompts.TryGetValue(pending, out var prompt)) return false;
            if (!pending.LpCostChoices.Contains(amount)) return true;
            s_revivalPrompts.Remove(pending);
            engine.ClearPendingActivation();
            if (prompt.Who.Graveyard.Contains(prompt.Card) && prompt.Card.GyEntryStamp == prompt.Stamp &&
                prompt.Who.LifePoints >= prompt.Cost)
            {
                Arm(engine, prompt.Who, prompt.Card, prompt.Cost);
                ok = true;
            }
            else
                engine.Log($"{prompt.Card.Name} is no longer in the GY — its effect is not used.");

            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor (<see cref="EffectActionKind.SpecialSummonArmedThisFromGyInDefenseAtStandby"/>):
        /// at its controller's Standby Phase, a flagged card still in the same stay in its GY is Special Summoned in
        /// face-up Defense Position; the flag is used up either way.
        /// </summary>
        public static void OnStandby(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null || engine.GameOver) return;
            if (!s_state.TryGetValue(engine, out var st)) return;
            st.Armed.RemoveAll(a => a.Card == null || a.Who == null || !a.Who.Graveyard.Contains(a.Card) ||
                                    a.Card.GyEntryStamp != a.Stamp);
            if (!ReferenceEquals(engine.TurnPlayer, who)) return;
            var armed = st.Armed.Find(a => ReferenceEquals(a.Card, card) && ReferenceEquals(a.Who, who));
            if (armed == null) return;
            st.Armed.Remove(armed);
            if (ContinuousProtections.GyMovementLocked(engine, card, card))
            {
                engine.Log("Necrovalley prevents the card from leaving the GY.");
                return;
            }

            who.Graveyard.Remove(card);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(who, card, BattlePosition.Defense, true))
                engine.Log($"{card.Name} is Special Summoned from the GY in face-up Defense Position.");
            else
            {
                engine.NextSpecialSummonFromGraveyard = false;
                card.ControlOwner = null;
                if (!who.Graveyard.Contains(card)) who.Graveyard.Add(card);
                engine.Log($"{card.Name} cannot be Special Summoned — it stays in the GY.");
            }
        }

        // ───────────────────────────── Helpoemer ─────────────────────────────

        /// <summary>
        /// BattlePhaseEndRuntime.FireEndOfBattlePhase: the end of the turn player's Battle Phase — every card in the
        /// other player's GY with a compiled <see cref="EffectTiming.EndOfBattlePhase"/> GY clause
        /// (<see cref="EffectClause.ResolvesFromGy"/> + <see cref="EffectClause.OpponentTurnOnly"/>) resolves it, while it
        /// is in the GY because it was destroyed by battle when the clause asks so. Returns true when one applied.
        /// </summary>
        public static bool FireEndOfBattlePhaseFromGy(DuelEngine engine)
        {
            var turn = engine?.TurnPlayer;
            var owner = turn != null ? engine.OpponentOf(turn) : null;
            if (owner?.Graveyard == null || engine.GameOver) return false;
            var any = false;
            foreach (var card in owner.Graveyard.ToList())
            {
                if (engine.GameOver) break;
                if (card?.Def == null || !owner.Graveyard.Contains(card)) continue;
                if (NegationMonstersRuntime.NegatedInGy(engine, card)) continue;
                var prog = CompiledEffectCache.GetOrCompile(card.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.EndOfBattlePhase))
                {
                    if (c == null || !c.ResolvesFromGy || !c.OpponentTurnOnly) continue;
                    if (c.Action != EffectActionKind.DiscardRandomFromOpponentHand) continue;
                    if (!owner.Graveyard.Contains(card)) break;
                    if (c.RequiresThisDestroyedByBattle && !GyEntryStamps.InGyBecauseDestroyedByBattle(owner, card))
                        continue;
                    engine.Log($"{card.Name}: its effect activates in the GY at the end of the Battle Phase.");
                    TextEffectRuntime.DiscardRandomFromOpponentHand(engine, turn, card, c);
                    any = true;
                }
            }

            return any;
        }

        // ───────────────────────────── Rope of Life ─────────────────────────────

        /// <summary>The monsters destroyed by battle and sent to <paramref name="who"/>'s GY that the open window answers.</summary>
        static List<CardInstance> ThoseMonsters(DuelEngine engine, DuelistState who) =>
            BattleWindowsRuntime.AnswerableSends(engine, who)
                .Where(m => GyEntryStamps.DestroyedByBattleThisTurn(engine, who, m)).ToList();

        static bool CanBeSummonedFromGy(DuelEngine engine, DuelistState who, CardInstance card) =>
            TextEffectRuntime.CanSpecialSummonFromGyTarget(card) &&
            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, null, SummonOrigin.Graveyard);

        static List<CardInstance> HandToDiscard(DuelistState who, CardInstance card) =>
            who?.Hand == null ? new List<CardInstance>() : who.Hand.Where(h => h != null && h != card).ToList();

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet default chain: <see cref="MonsterDestroyedByBattleSentToYourGy"/>.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c?.ActivationCondition != MonsterDestroyedByBattleSentToYourGy) return false;
            reason = "Only when a monster is destroyed by battle and sent to your GY.";
            var timing = engine?.PendingResponse?.Timing;
            if (engine?.PendingResponse == null || engine.PendingResponse.Responder != who ||
                (timing != ResponseTiming.EndOfDamageStep && timing != ResponseTiming.MonsterSentFromFieldToYourGy))
                return true;
            if (!ThoseMonsters(engine, who).Any(m => CanBeSummonedFromGy(engine, who, m)))
                return true;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                reason = "No free Monster Zone to Special Summon to.";
                return true;
            }

            if (c.DiscardEntireHandCost && HandToDiscard(who, card).Count == 0)
            {
                reason = "Discard your entire hand (min. 1): your hand is empty.";
                return true;
            }

            ok = true;
            reason = "OK";
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.SeatResponseOnChain: "Discard your entire hand (min. 1)" (<see cref="EffectClause.DiscardEntireHandCost"/>)
        /// — every card in the hand except this one, as one discard event (a cost).
        /// </summary>
        public static void PaySeatCost(DuelEngine engine, DuelistState who, CardInstance card, List<EffectClause> clauses)
        {
            if (engine == null || who == null || card == null || clauses == null) return;
            if (!clauses.Exists(c => c != null && c.DiscardEntireHandCost)) return;
            var paid = new List<CardInstance>();
            DiscardEvents.Begin(engine);
            try
            {
                foreach (var h in HandToDiscard(who, card))
                {
                    var gone = MonsterEffects.DiscardFromHandByInstance(who, h, DiscardCause.Cost);
                    if (gone != null) paid.Add(gone);
                }
            }
            finally
            {
                DiscardEvents.End(engine);
            }

            engine.Log($"Cost: {who.Name} discards the entire hand ({paid.Count} card(s)).");
            s_seatPaid.Remove(card);
            s_seatPaid.Add(card, paid);
            s_placements.Remove(card);
        }

        /// <summary>TextEffectRuntime.SeatResponseOnChain: the seat cost paid for <paramref name="card"/> is refunded if its target pick is cancelled.</summary>
        public static void AttachSeatCost(CardInstance card, PendingActivation pending)
        {
            if (card == null || pending == null || !s_seatPaid.TryGetValue(card, out var paid)) return;
            s_seatPaid.Remove(card);
            pending.PaidDiscards.AddRange(paid);
        }

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk"/>: if the target is still in your GY, it
        /// is Special Summoned face-up (the human's chosen position; the AI: Attack unless its DEF is higher), and if it
        /// is, it gains <see cref="EffectClause.Amount"/> ATK while it stays face-up on the field.
        /// </summary>
        public static void ResolveRope(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            if (engine == null || who == null || clause == null) return;
            if (source != null) s_seatPaid.Remove(source);
            Placement placement = null;
            if (source != null && s_placements.TryGetValue(source, out placement))
                s_placements.Remove(source);
            if (target == null || !who.Graveyard.Contains(target))
            {
                engine.Log($"{source?.Name}: the target is no longer in the GY — nothing is Special Summoned.");
                return;
            }

            if (!CanBeSummonedFromGy(engine, who, target))
            {
                engine.Log($"{target.Name} cannot be Special Summoned.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, target))
            {
                engine.Log("Necrovalley prevents the target from leaving the GY.");
                return;
            }

            var pos = placement?.Position ??
                      ((target.Def?.atk ?? 0) + clause.Amount >= (target.Def?.def ?? 0)
                          ? BattlePosition.Attack
                          : BattlePosition.Defense);
            who.Graveyard.Remove(target);
            DuelEngine.RecordOwnerOnFieldEntry(target, who, who);
            engine.NextSpecialSummonFromGraveyard = true;
            if (!engine.SpecialSummonToField(who, target, pos, true, SummonKind.SpecialSummon, placement?.Zone ?? -1))
            {
                engine.NextSpecialSummonFromGraveyard = false;
                target.ControlOwner = null;
                if (!who.Graveyard.Contains(target)) who.Graveyard.Add(target);
                engine.Log($"{target.Name} cannot be Special Summoned — it stays in the GY.");
                return;
            }

            engine.Log($"{source?.Name}: {target.Name} is Special Summoned in face-up " +
                       $"{(target.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
            SpecialSummonEffectsRuntime.ApplyGainWhileFaceUp(engine, who, source, clause, target);
        }

        /// <summary>
        /// AI (SpellTrapEffects.AiAutoRespond): Rope of Life when it is legal and worth the hand — a monster of 1500+ ATK
        /// comes back, or the hand is 2 cards or fewer.
        /// </summary>
        public static bool AiWantsRope(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            var clause = prog?.ClauseList.Find(c => c != null && c.ActivationCondition == MonsterDestroyedByBattleSentToYourGy);
            if (clause == null || !prog.FullyCompiled) return false;
            if (!TryActivationCondition(engine, who, card, clause, out var ok, out _) || !ok) return false;
            var best = ThoseMonsters(engine, who).Where(m => CanBeSummonedFromGy(engine, who, m))
                .Select(m => m.Def?.atk ?? 0).DefaultIfEmpty(-1).Max();
            return best >= 1500 || HandToDiscard(who, card).Count <= 2;
        }
    }
}
