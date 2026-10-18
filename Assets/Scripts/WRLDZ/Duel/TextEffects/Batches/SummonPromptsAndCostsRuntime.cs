using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonPromptsAndCosts" (v163) runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Optional end-of-Battle-Phase Special Summon</b> (Vampire Baby,
    /// <see cref="EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn"/>). The card's "destroyed a monster by
    /// battle" flag is set by <see cref="NoteBattleDestroying"/> (TextEffectRuntime.NotifyDestroyedOpponentByBattle, ygopro
    /// EVENT_BATTLE_DESTROYING) for its current face-up field presence and cleared at the end of every Battle Phase
    /// (<see cref="ResetBattleDestroyingFlags"/>). At the End Step (BattlePhaseEndRuntime.FireEndOfBattlePhase →
    /// <see cref="OnEndOfBattlePhase"/>) the AI decides at once; a human's offer is queued and opened as soon as play is idle
    /// (<see cref="OnNotify"/>, DuelEngine.Notify) — after Main Phase 2 is entered, or, when the turn was ended straight from the
    /// Battle Phase, before the End Phase: DuelEngine.EndTurn stops (<see cref="HoldsEndTurn"/>) and the turn ends once the
    /// offer is answered (<see cref="ContinueEndTurn"/>). The human taps 1 to "free Monster Zones" monsters (Attack /
    /// Defense and a zone through the Special Summon placement UI, <see cref="TryPickAsksPlacement"/>); CANCEL before the
    /// first tap declines (the effect is not used), after it summons the ones chosen.</item>
    /// <item><b>Special Summon cost with a human pick</b> (Tyrant Dragon, <see cref="EffectActionKind.GySpecialSummonNeedsTributeCost"/>,
    /// ygopro EFFECT_SPSUMMON_COST). <see cref="GyTributeSummonCost"/> is registered in <see cref="SpecialSummonLegality"/>:
    /// a Special Summon of the card from the GY is legal only while its summoner controls a monster of the Type to Tribute
    /// (targets, picks and DuelEngine.SpecialSummonToField all ask it), and that Tribute is paid as the Summon happens
    /// (<see cref="PayGyTribute"/>). A human with 2+ choices picks it before the Summon: as the Chain Link that Special
    /// Summons the card from the GY starts to resolve (<see cref="HoldBeforeLinkResolves"/>, DuelEngine.ResolveChainStack,
    /// the Chain is held), or before Vampire Baby's Summon of it. The pick has no CANCEL (CANCEL picks automatically). Any
    /// other path (a Summon chosen inside another resolution) Tributes the lowest-ATK candidate.</item>
    /// </list>
    /// </summary>
    public static class SummonPromptsAndCostsRuntime
    {
        // ───────────────────────────── state ─────────────────────────────

        /// <summary>ygopro flag effect registered on EVENT_BATTLE_DESTROYING (RESETS_STANDARD + RESET_PHASE+PHASE_BATTLE).</summary>
        sealed class BattleDestroying
        {
            public int Turn;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        sealed class Pick
        {
            public CardInstance Card;
            public BattlePosition Position;
            public int Zone;
        }

        /// <summary>A human's end-of-Battle-Phase Special Summon offer (one card's trigger).</summary>
        sealed class Offer
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Turn;
            public int EntryStamp;
            public int FaceDownStamp;
            public readonly List<Pick> Picks = new();
        }

        /// <summary>The Tribute a human chose for the Special Summon of <see cref="TributePrompt.Card"/>.</summary>
        sealed class TributeChoice
        {
            public DuelistState Who;
            public CardInstance Tribute;
            public int GyStamp;
            public int Turn;
        }

        sealed class TributePrompt
        {
            public DuelistState Who;
            public CardInstance Card;
            public Action Then;
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, BattleDestroying> Destroying = new();
            public readonly Dictionary<CardInstance, int> UsedTurn = new();
            public readonly List<Offer> Queue = new();
            public readonly Dictionary<CardInstance, TributeChoice> Chosen = new();
            /// <summary>DuelEngine.EndTurn stopped for an offer: this player's turn ends once it is answered.</summary>
            public DuelistState EndTurnAfter;
            public int EndTurnAfterTurn;
            public bool InNotify;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Offer> s_offers = new();
        static readonly ConditionalWeakTable<PendingActivation, TributePrompt> s_tributes = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static EffectClause ClauseOf(CardDef def, EffectActionKind action)
        {
            if (def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog?.ClauseList == null) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action)
                    return c;
            return null;
        }

        static DuelistState GyOwnerOf(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            if (engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) return engine.Player;
            if (engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card)) return engine.Opponent;
            return null;
        }

        static int FreeZones(DuelistState who) =>
            ZoneCapsRuntime.UsableMonsterZones(who); // ZoneCapsAndPhaseWindows: caps

        /// <summary>
        /// Play is idle for a new choice: the Battle Phase is over (its End Step work is done: Main Phase 2, or the held
        /// turn end), nothing resolving or chained, no window, pick, declared attack, held Summon or paused phase
        /// (DuelEngine.PlayIdleForTriggerChain), and no Chain of simultaneous triggers being built.
        /// </summary>
        static bool Idle(DuelEngine engine) =>
            engine != null && !engine.GameOver && engine.Phase != DuelPhase.Battle && engine.PendingActivation == null &&
            !engine.IsAwaitingResponse && engine.PlayIdleForTriggerChain() && !SimultaneousTriggersRuntime.Busy(engine);

        // ───────────────────────────── Vampire Baby ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle (the destroyer is still on the field): a card with
        /// <see cref="EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn"/> destroyed a monster by battle.
        /// The flag belongs to this face-up field presence (ygopro RESETS_STANDARD).
        /// </summary>
        public static void NoteBattleDestroying(DuelEngine engine, CardInstance destroyer)
        {
            if (engine == null || destroyer?.Def == null || !destroyer.FaceUp) return;
            if (ClauseOf(destroyer.Def, EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn) == null) return;
            StateOf(engine).Destroying[destroyer] = new BattleDestroying
            {
                Turn = engine.TurnNumber,
                EntryStamp = destroyer.FieldEntryStamp,
                FaceDownStamp = destroyer.FaceDownStamp
            };
        }

        /// <summary>BattlePhaseEndRuntime.FireEndOfBattlePhase, after every clause: the flags reset (RESET_PHASE+PHASE_BATTLE).</summary>
        public static void ResetBattleDestroyingFlags(DuelEngine engine)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st))
                st.Destroying.Clear();
        }

        /// <summary>
        /// The monsters <paramref name="source"/> destroyed by battle this turn that are in a GY since then (either GY,
        /// ygopro REASON_BATTLE + GetReasonCard() + GetTurnID()) and that <paramref name="who"/> can Special Summon.
        /// </summary>
        static List<CardInstance> Candidates(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side?.Graveyard == null) continue;
                foreach (var g in side.Graveyard)
                    if (g?.Def != null && g.Def.IsMonster && g.WasDestroyedByBattle &&
                        ReferenceEquals(g.BattleDestroyer, source) &&
                        GyEntryStamps.DestroyedByBattleThisTurn(engine, side, g) &&
                        SpecialSummonLegality.CanBeSpecialSummoned(engine, who, g, source, SummonOrigin.Graveyard))
                        list.Add(g);
            }

            return list;
        }

        static bool FlagSet(DuelEngine engine, State st, CardInstance card) =>
            st.Destroying.TryGetValue(card, out var d) && d.Turn == engine.TurnNumber &&
            d.EntryStamp == card.FieldEntryStamp && d.FaceDownStamp == card.FaceDownStamp;

        static bool UsedThisTurn(DuelEngine engine, State st, CardInstance card) =>
            st.UsedTurn.TryGetValue(card, out var t) && t == engine.TurnNumber;

        /// <summary>
        /// BattlePhaseEndRuntime.ResolveEndOfBattlePhase arm (<paramref name="card"/> is face-up, not negated, on
        /// <paramref name="who"/>'s field). True when the effect was used or offered.
        /// </summary>
        public static bool OnEndOfBattlePhase(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || who == null || card?.Def == null || clause == null || engine.GameOver) return false;
            var st = StateOf(engine);
            if (!FlagSet(engine, st, card) || UsedThisTurn(engine, st, card)) return false;
            var candidates = Candidates(engine, who, card);
            var room = FreeZones(who);
            if (candidates.Count == 0 || room <= 0) return false;
            var offer = new Offer
            {
                Who = who,
                Source = card,
                Turn = engine.TurnNumber,
                EntryStamp = card.FieldEntryStamp,
                FaceDownStamp = card.FaceDownStamp
            };
            if (engine.IsHumanControlled(who))
            {
                st.Queue.Add(offer);
                engine.Log($"{card.Name}: at the end of the Battle Phase {who.Name} can Special Summon a monster it " +
                           "destroyed by battle this turn.");
                return true;
            }

            // AI: the strongest of them, as many as fit, face-up in the better position.
            st.UsedTurn[card] = engine.TurnNumber;
            engine.Log($"{card.Name}: its effect activates at the end of the Battle Phase.");
            foreach (var g in candidates.OrderByDescending(c => Math.Max(c.Def.atk, c.Def.def)).ThenBy(c => c.Name).Take(room))
                offer.Picks.Add(new Pick
                {
                    Card = g,
                    Position = g.Def.def > g.Def.atk ? BattlePosition.Defense : BattlePosition.Attack,
                    Zone = -1
                });
            SummonPicks(engine, offer, 0);
            return true;
        }

        static bool StillOffered(DuelEngine engine, State st, Offer o)
        {
            var c = o.Source;
            if (o.Turn != engine.TurnNumber || c == null || UsedThisTurn(engine, st, c)) return false;
            return o.Who.TryFindMonster(c, out _) && c.FaceUp && !c.IsNegated &&
                   c.FieldEntryStamp == o.EntryStamp && c.FaceDownStamp == o.FaceDownStamp;
        }

        static string PromptFor(Offer o, int room) =>
            o.Picks.Count == 0
                ? $"you can Special Summon a monster it destroyed by battle this turn: tap one (Summon ATK or DEF, face-up; " +
                  $"up to {room}), or CANCEL to not use this effect."
                : $"tap another monster to Special Summon too (room for {room} more), or CANCEL to Special Summon the " +
                  "selected one(s).";

        /// <summary>Open the next queued offer that still applies. True when a choice is now open.</summary>
        static bool TryOpenNext(DuelEngine engine)
        {
            if (!s_state.TryGetValue(engine, out var st)) return false;
            while (st.Queue.Count > 0)
            {
                if (!Idle(engine)) return false;
                var o = st.Queue[0];
                st.Queue.RemoveAt(0);
                if (!StillOffered(engine, st, o)) continue;
                var candidates = Candidates(engine, o.Who, o.Source);
                var room = FreeZones(o.Who);
                if (candidates.Count == 0 || room <= 0)
                {
                    engine.Log($"{o.Source.Name}: nothing it destroyed by battle can be Special Summoned now — no effect.");
                    continue;
                }

                var pending = new PendingActivation
                {
                    Controller = o.Who,
                    Card = o.Source,
                    FromHand = false,
                    WasSetOnField = false,
                    TargetKind = EffectTargetKind.MonsterInEitherGy,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = Math.Min(room, candidates.Count),
                    PromptOverride = PromptFor(o, room)
                };
                pending.LegalTargets.AddRange(candidates);
                s_offers.Add(pending, o);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Special Summon the picks in order from their GY to the chooser's field (face-up, the chosen position and zone);
        /// a Summon whose card has a human-picked summon cost waits for that pick first. Then the next offer / the turn end.
        /// </summary>
        static void SummonPicks(DuelEngine engine, Offer o, int index)
        {
            for (var i = index; i < o.Picks.Count; i++)
            {
                if (engine.GameOver) return;
                var pick = o.Picks[i];
                var next = i + 1;
                if (AskTributePick(engine, o.Who, pick.Card, () =>
                    {
                        SummonOne(engine, o, pick);
                        SummonPicks(engine, o, next);
                    }))
                    return;
                SummonOne(engine, o, pick);
            }

            if (engine.IsHumanControlled(o.Who))
                AfterOffer(engine);
        }

        static void SummonOne(DuelEngine engine, Offer o, Pick pick)
        {
            var card = pick.Card;
            var who = o.Who;
            var gyOwner = GyOwnerOf(engine, card);
            if (gyOwner == null)
            {
                engine.Log($"{o.Source?.Name}: {card?.Name} is no longer in the GY — it is not Special Summoned.");
                return;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, o.Source, SummonOrigin.Graveyard,
                    requireZone: true))
            {
                engine.Log($"{o.Source?.Name}: {card.Name} cannot be Special Summoned now.");
                return;
            }

            gyOwner.Graveyard.Remove(card);
            DuelEngine.RecordOwnerOnFieldEntry(card, gyOwner, who);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(who, card, pick.Position, true, SummonKind.SpecialSummon, pick.Zone))
            {
                engine.Log($"{o.Source?.Name}: {who.Name} Special Summons {card.Name} from the GY in face-up " +
                           $"{(card.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                return;
            }

            card.ControlOwner = null;
            if (!gyOwner.Graveyard.Contains(card)) gyOwner.Graveyard.Add(card);
            engine.Log($"{o.Source?.Name}: {card.Name} could not be Special Summoned — it stays in the GY.");
        }

        static void AfterOffer(DuelEngine engine)
        {
            if (engine.GameOver) return;
            if (TryOpenNext(engine)) return;
            ContinueEndTurn(engine);
            engine.NotifyPublic();
        }

        /// <summary>
        /// DuelEngine.EndTurn, right after the Battle Phase ended: a human's end-of-Battle-Phase offer is waiting, so the turn
        /// stops before its End Phase (the engine moves to Main Phase 2 and opens the offer); it ends once the offer is
        /// answered. False when no offer is queued.
        /// </summary>
        public static bool HoldsEndTurn(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || !s_state.TryGetValue(engine, out var st)) return false;
            st.Queue.RemoveAll(o => o.Turn != engine.TurnNumber);
            if (st.Queue.Count == 0) return false;
            st.EndTurnAfter = who;
            st.EndTurnAfterTurn = engine.TurnNumber;
            return true;
        }

        /// <summary>The held turn ends once no offer is queued or open and play is idle.</summary>
        static void ContinueEndTurn(DuelEngine engine)
        {
            if (!s_state.TryGetValue(engine, out var st) || st.EndTurnAfter == null) return;
            var who = st.EndTurnAfter;
            if (engine.GameOver || st.EndTurnAfterTurn != engine.TurnNumber || engine.TurnPlayer != who)
            {
                st.EndTurnAfter = null;
                return;
            }

            if (st.Queue.Count > 0 || !Idle(engine)) return; // DuelEngine.Notify (OnNotify) tries again once idle
            st.EndTurnAfter = null;
            engine.EndTurn(who);
        }

        /// <summary>DuelEngine.Notify: open a queued offer, or end a held turn, once play is idle.</summary>
        public static void OnNotify(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st)) return;
            if (st.InNotify || (st.Queue.Count == 0 && st.EndTurnAfter == null)) return;
            if (!Idle(engine)) return;
            st.InNotify = true;
            try
            {
                if (!TryOpenNext(engine))
                    ContinueEndTurn(engine);
            }
            finally
            {
                st.InNotify = false;
            }
        }

        /// <summary>SpellTrapEffects.PendingTargetIsSpecialSummon: an offer's pick asks Attack / Defense and a zone.</summary>
        public static bool TryPickAsksPlacement(PendingActivation pending, out bool asksPlacement)
        {
            asksPlacement = pending != null && s_offers.TryGetValue(pending, out _);
            return asksPlacement;
        }

        // ───────────────────────────── picks (hooks) ─────────────────────────────

        /// <summary>SpellTrapEffects.TryResolveWithTarget hook: an offer's monster tap or a Tribute pick. False for other picks.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null) return false;
            if (s_tributes.TryGetValue(p, out var tp))
            {
                var tribute = engine.ResolveLegalEffectTarget(target);
                if (tribute == null || !p.LegalTargets.Contains(tribute))
                {
                    engine.Log($"Choose one of the highlighted {RaceOf(tp.Card)} monsters to Tribute.");
                    return true;
                }

                ok = true;
                AnswerTribute(engine, p, tp, tribute);
                return true;
            }

            if (!s_offers.TryGetValue(p, out var o)) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            var position = engine.PendingSpecialSummonPosition;
            var zone = engine.PendingSpecialSummonZone;
            engine.ClearPendingSpecialSummonPlacement();
            if (pick == null || !p.LegalTargets.Contains(pick))
            {
                engine.Log("Choose one of the highlighted monsters, or CANCEL.");
                return true;
            }

            var st = StateOf(engine);
            if (o.Picks.Count == 0)
            {
                st.UsedTurn[o.Source] = engine.TurnNumber;
                engine.Log($"{o.Source.Name}: its effect activates at the end of the Battle Phase.");
            }

            o.Picks.Add(new Pick
            {
                Card = pick,
                Position = position == BattlePosition.Defense ? BattlePosition.Defense : BattlePosition.Attack,
                Zone = zone
            });
            p.LegalTargets.Remove(pick);
            ok = true;
            var room = FreeZones(o.Who) - o.Picks.Count;
            if (room > 0 && p.LegalTargets.Count > 0)
            {
                p.TargetPicksRemaining = Math.Min(room, p.LegalTargets.Count);
                p.PromptOverride = PromptFor(o, room);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            s_offers.Remove(p);
            engine.ClearPendingActivation();
            SummonPicks(engine, o, 0);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook. An offer: CANCEL before the first tap declines (not used this turn), after it
        /// Special Summons the selected monsters. A Tribute pick has no CANCEL: the lowest-ATK candidate is Tributed.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null) return false;
            if (s_tributes.TryGetValue(p, out var tp))
            {
                var auto = AutoTribute(TributeCandidates(engine, tp.Who, tp.Card));
                engine.Log($"The Tribute for {tp.Card?.Name} cannot be cancelled — it is chosen automatically.");
                AnswerTribute(engine, p, tp, auto);
                return true;
            }

            if (!s_offers.TryGetValue(p, out var o)) return false;
            s_offers.Remove(p);
            engine.ClearPendingActivation();
            if (o.Picks.Count == 0)
            {
                engine.Log($"{o.Source?.Name}: {o.Who?.Name} does not use its effect.");
                AfterOffer(engine);
                return true;
            }

            SummonPicks(engine, o, 0);
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── Tyrant Dragon: the summon cost ─────────────────────────────

        static EffectClause GyTributeClause(CardInstance card) =>
            card?.Def == null ? null : ClauseOf(card.Def, EffectActionKind.GySpecialSummonNeedsTributeCost);

        static string RaceOf(CardInstance card) => GyTributeClause(card)?.TributeRaceFilter ?? "";

        /// <summary>
        /// The monsters <paramref name="who"/> can Tribute for <paramref name="card"/>'s cost (ygopro release group for a
        /// cost: monsters they control, face-up or face-down, of the printed Type; not one that cannot be Tributed).
        /// </summary>
        static List<CardInstance> TributeCandidates(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var list = new List<CardInstance>();
            var race = RaceOf(card);
            if (who == null || string.IsNullOrEmpty(race)) return list;
            foreach (var m in who.MonstersOnField())
                if (m?.Def != null && m != card && !m.CannotBeTributed &&
                    string.Equals(m.Race, race, StringComparison.OrdinalIgnoreCase))
                    list.Add(m);
            return list;
        }

        static CardInstance AutoTribute(List<CardInstance> candidates) =>
            candidates?.OrderBy(m => m.FaceUp ? m.CurrentAtk : m.Def?.atk ?? 0).ThenBy(m => m.Level).ThenBy(m => m.Name)
                .FirstOrDefault();

        /// <summary>
        /// The summon cost <see cref="SpecialSummonLegality"/> applies (EFFECT_SPSUMMON_COST): a card with
        /// <see cref="EffectActionKind.GySpecialSummonNeedsTributeCost"/>, Special Summoned from the GY.
        /// </summary>
        public static readonly SpecialSummonLegality.SummonCost GyTributeSummonCost = new()
        {
            Name = "SummonPromptsAndCosts: Tribute to Special Summon from the GY",
            AppliesTo = (card, from) => from == SummonOrigin.Graveyard && GyTributeClause(card) != null,
            CanPay = (engine, who, card, from) =>
                TributeCandidates(engine, who, card).Count >= Math.Max(1, GyTributeClause(card)?.Amount ?? 1),
            Pay = PayGyTribute
        };

        /// <summary>
        /// DuelEngine.SpecialSummonToField (SpecialSummonLegality.PaySummonCosts): Tribute the chosen monster — the one the
        /// human picked for this Summon (<see cref="AskTributePick"/>), else the lowest-ATK candidate.
        /// </summary>
        static bool PayGyTribute(DuelEngine engine, DuelistState who, CardInstance card, SummonOrigin from)
        {
            engine ??= who?.Engine;
            var candidates = TributeCandidates(engine, who, card);
            if (engine == null || candidates.Count == 0) return false;
            CardInstance tribute = null;
            if (s_state.TryGetValue(engine, out var st) && st.Chosen.TryGetValue(card, out var chosen))
            {
                st.Chosen.Remove(card);
                if (Valid(engine, chosen, who, card, candidates))
                    tribute = chosen.Tribute;
            }

            if (tribute == null)
            {
                tribute = AutoTribute(candidates);
                if (engine.IsHumanControlled(who) && candidates.Count > 1)
                    engine.Log($"{card.Name}: the {RaceOf(card)} monster to Tribute is chosen automatically.");
            }

            // EngineHygiene: EVENT_RELEASE (Super Rejuvenation counts a Tributed Dragon).
            ReleaseEvents.Record(engine, tribute, who);
            engine.SendCardToGrave(engine.ControllerOf(tribute) ?? who, tribute);
            engine.Log($"{card.Name}: {who.Name} Tributes {tribute.Name} to Special Summon it from the GY.");
            return true;
        }

        /// <summary>
        /// Before <paramref name="card"/> (in a GY) is Special Summoned by <paramref name="who"/>: a human with 2+ monsters
        /// to Tribute for its summon cost picks one now (<see cref="PendingActivation.HoldsChainResolution"/> while a Chain
        /// resolves); <paramref name="then"/> runs once it is picked, then the held Chain goes on. False (and nothing runs)
        /// when no pick is needed — the card has no such cost, the summoner is the AI, a lone candidate, or one is chosen.
        /// </summary>
        public static bool AskTributePick(DuelEngine engine, DuelistState who, CardInstance card, Action then)
        {
            if (engine == null || who == null || card == null || engine.GameOver) return false;
            if (!engine.IsHumanControlled(who) || engine.PendingActivation != null || engine.IsAwaitingResponse) return false;
            if (GyTributeClause(card) == null || GyOwnerOf(engine, card) == null) return false;
            var candidates = TributeCandidates(engine, who, card);
            if (candidates.Count < 2) return false;
            var st = StateOf(engine);
            if (st.Chosen.TryGetValue(card, out var chosen) && Valid(engine, chosen, who, card, candidates))
                return false;
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.TributeMonsterYouControl,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                HoldsChainResolution = engine.IsResolvingChain,
                PromptOverride = $"Tribute 1 {RaceOf(card)} monster you control to Special Summon {card.Name} from the GY " +
                                 "(its summon cost)."
            };
            pending.LegalTargets.AddRange(candidates);
            s_tributes.Add(pending, new TributePrompt { Who = who, Card = card, Then = then });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>A recorded pick still applies: same summoner, same GY stay of the card, this turn, still a candidate.</summary>
        static bool Valid(DuelEngine engine, TributeChoice c, DuelistState who, CardInstance card, List<CardInstance> candidates) =>
            c != null && c.Who == who && c.GyStamp == card.GyEntryStamp && c.Turn == engine.TurnNumber &&
            candidates.Contains(c.Tribute);

        static void AnswerTribute(DuelEngine engine, PendingActivation p, TributePrompt tp, CardInstance tribute)
        {
            s_tributes.Remove(p);
            engine.ClearPendingActivation();
            if (tribute != null)
            {
                StateOf(engine).Chosen[tp.Card] = new TributeChoice
                {
                    Who = tp.Who,
                    Tribute = tribute,
                    GyStamp = tp.Card.GyEntryStamp,
                    Turn = engine.TurnNumber
                };
                engine.Log($"{tp.Card?.Name}: {tp.Who?.Name} will Tribute {tribute.Name}.");
            }

            tp.Then?.Invoke();
            engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack, before <paramref name="link"/> (the next to resolve) is taken: when it is a
        /// Special Summon of its target from a GY (a compiled GY Special Summon clause, or Monster Reborn's script) and that
        /// target's summon cost needs a human's Tribute pick, the pick opens now and the Chain waits (true).
        /// </summary>
        public static bool HoldBeforeLinkResolves(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link?.Card?.Def == null || link.Negated || link.Controller == null) return false;
            if (engine.PendingActivation != null || engine.IsAwaitingResponse) return false;
            var targets = new List<CardInstance>();
            if (link.Target != null) targets.Add(link.Target);
            foreach (var t in link.Targets)
                if (t != null && !targets.Contains(t))
                    targets.Add(t);
            if (targets.Count == 0) return false;
            if (!SummonsGyTarget(link.Card.Def, out var toTargetsOwner)) return false;
            foreach (var t in targets)
            {
                var owner = GyOwnerOf(engine, t);
                if (owner == null || GyTributeClause(t) == null) continue;
                var summoner = toTargetsOwner ? owner : link.Controller;
                if (AskTributePick(engine, summoner, t, null)) return true;
            }

            return false;
        }

        static bool SummonsGyTarget(CardDef def, out bool toTargetsOwner)
        {
            toTargetsOwner = false;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.FullyCompiled && prog.ClauseList != null)
            {
                foreach (var c in prog.ClauseList)
                {
                    if (c == null || !c.RequiresTargetChoice) continue;
                    if (c.Zone != EffectZoneFilter.EitherGyMonsters && c.Zone != EffectZoneFilter.ControllerGyMonsters &&
                        c.Zone != EffectZoneFilter.OppGyMonsters)
                        continue;
                    if (c.Action != EffectActionKind.SpecialSummonFromGy &&
                        c.Action != EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk)
                        continue;
                    toTargetsOwner = c.SummonToTargetsOwner;
                    return true;
                }

                return false;
            }

            // A Monster Reborn resolved by its registered script (SpellTrapEffects.BeginOrResolveManual).
            return def.id == SpellTrapEffects.MonsterReborn;
        }

        /// <summary>
        /// TextEffectRuntime.CollectTargets (a compiled "Special Summon it" GY target): <paramref name="target"/>'s summon
        /// costs can be paid by the player who would Special Summon it.
        /// </summary>
        public static bool GyTargetSummonCostPayable(DuelEngine engine, DuelistState who, EffectClause c, CardInstance target)
        {
            if (target == null) return false;
            var summoner = c != null && c.SummonToTargetsOwner ? GyOwnerOf(engine, target) ?? who : who;
            return SpecialSummonLegality.CanPaySummonCosts(engine, summoner, target, SummonOrigin.Graveyard);
        }
    }
}
