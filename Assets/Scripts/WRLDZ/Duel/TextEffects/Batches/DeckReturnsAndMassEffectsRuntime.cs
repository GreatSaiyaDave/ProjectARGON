using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DeckReturnsAndMassEffects" (v161 LOD), checked against the ygopro scripts:
    /// <list type="bullet">
    /// <item><b>The "returned from the field to the Deck" event</b> (<see cref="NoteReturnedToDeck"/>): raised by
    /// DuelEngine.ReturnCardToDeck, the one path every field-to-Deck effect takes (Mysterious Guard, Fiber Jar,
    /// Dark Scorpion - Gorg / Phoenix Wing Wind Blast's PlaceTargetOnTopOfDeck, Gift of the Mystical Elf-style
    /// ShuffleTargetAndHandIntoDeckDraw, Morphing Jar #2). A card with an <see cref="EffectTiming.ReturnedFromFieldToDeck"/>
    /// trigger that was face-up there (Serpentine Princess, Lua EVENT_TO_DECK + IsPreviousPosition(POS_FACEUP) +
    /// IsPreviousLocation(LOCATION_ONFIELD)) and reached its owner's Main Deck queues a mandatory trigger on the
    /// simultaneous-trigger Chain (<see cref="SimultaneousTriggersRuntime.QueueTrigger"/>): it activates once the
    /// effect that returned it (and its Chain) is over and play is idle.</item>
    /// <item><b>Serpentine Princess</b> (c71829750): its owner selects 1 Level N or lower monster in their Deck that
    /// can be Special Summoned and Special Summons it face-up (a human picks Attack or Defense and the zone), then the
    /// Deck is shuffled.</item>
    /// <item><b>Fiber Jar</b> (c78706415): every card in both hands, on both fields and in both GYs goes to its owner's
    /// Deck (Extra Deck monsters to the Extra Deck, Tokens vanish), except a monster the current battle is destroying
    /// (Lua removes STATUS_BATTLE_DESTROYED), a card unaffected by the effect and a GY card Necrovalley locks (judged
    /// before anything moves: it is one simultaneous move, Necrovalley included); both Decks are shuffled; the
    /// controller draws N, then the opponent draws N.</item>
    /// <item><b>Mysterious Guard</b> (c1347977): FLIP. Targets are chosen as the effect activates (the SEGOC Chain's
    /// activation step, <see cref="ActivateFlipTargets"/>; or at once when it activates outside one): 1 face-up
    /// monster on the field, then — only while its controller controls a face-up monster of the printed Type — 1
    /// additional face-up monster (optional; Cancel = none). At resolution, as the Lua does: a lone target, if still
    /// face-up on the field, goes on top of its owner's Deck. With two targets, the ones still face-up count: a lone
    /// one goes on top of the Deck; with both, the activator chooses which goes on top of the Deck, then the other
    /// returns to the hand only if the first reached the Main Deck and a face-up monster of that Type is still
    /// controlled.</item>
    /// <item><b>Creature Swap</b> (c31036355): not targeted. Needs a monster on each side to activate. At resolution the
    /// activator, then the opponent, chooses 1 monster they control (a human answers through a pick that holds the
    /// Chain, also during the other player's Creature Swap); they switch control into each other's zone
    /// (DuelEngine.TrySwapControl) and cannot change battle position (nor be Flip Summoned) for the rest of the turn
    /// (<see cref="PositionLocksRuntime.LockRestOfTurn"/>).</item>
    /// <item><b>Bubble Crash</b> (c61622107): needs a player with N or more cards in hand + on the field (this card
    /// counts). At resolution each such player — the activator first — selects cards there (the activator never this
    /// card) and the selected cards of both are sent to the GY together, by rule (not destroyed, not discarded), so that
    /// M remain (counting this card while it is on the field, as the Lua does).</item>
    /// </list>
    /// Every pick is mandatory: Cancel makes it automatically, except Mysterious Guard's optional additional target.
    /// The AI decides at once. State is kept per engine and per open pick.
    /// </summary>
    public static class DeckReturnsAndMassEffectsRuntime
    {
        /// <summary><see cref="EffectClause.ActivationCondition"/> (Creature Swap): each player controls a monster.</summary>
        public const string EachPlayerControlsMonster = "EachPlayerControlsMonster";

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Bubble Crash): a player has
        /// <see cref="EffectClause.ConditionAmount"/> or more cards in hand + on the field.
        /// </summary>
        public const string AnyPlayerHandAndFieldAtLeast = "AnyPlayerHandAndFieldAtLeast";

        enum PickKind
        {
            GuardFirst,
            GuardSecond,
            SwapOwn,
            BubbleSend,
            SerpentineSummon,
            GuardDeck
        }

        /// <summary>A target and the field presence it was chosen in.</summary>
        sealed class Snap
        {
            public CardInstance Card;
            public int Stamp;
        }

        /// <summary>
        /// Mysterious Guard's activation: its target, or its two targets (an unordered pair: which one goes to the Deck is
        /// chosen at resolution).
        /// </summary>
        sealed class GuardActivation
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public bool ResolveAfter;
            public Snap First;
            public Snap Second;
        }

        sealed class SwapPlan
        {
            public DuelistState Who;
            public CardInstance Source;
            public CardInstance Mine;
            public CardInstance Theirs;
        }

        sealed class BubblePlan
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Keep;
            public readonly List<DuelistState> Order = new();
            public readonly Dictionary<DuelistState, int> Need = new();
            public readonly Dictionary<DuelistState, List<CardInstance>> Offer = new();
            public readonly Dictionary<DuelistState, List<CardInstance>> Chosen = new();
        }

        sealed class Pick
        {
            public PickKind Kind;
            public GuardActivation Guard;
            public SwapPlan Swap;
            public BubblePlan Bubble;
            public DuelistState Chooser;
            public CardInstance Source;
            public EffectClause Clause;
        }

        sealed class State
        {
            /// <summary>Mysterious Guard activations made on a SEGOC Chain, waiting for their resolution.</summary>
            public readonly Dictionary<CardInstance, GuardActivation> Guard = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Pick> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────── activation conditions ─────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet hook. False when <paramref name="c"/> is not this batch's.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || engine == null || who == null) return false;
            var opp = engine.OpponentOf(who);
            switch (c.ActivationCondition)
            {
                case EachPlayerControlsMonster:
                    ok = who.MonstersOnField().Any() && opp != null && opp.MonstersOnField().Any();
                    reason = "Each player must control a monster.";
                    return true;
                case AnyPlayerHandAndFieldAtLeast:
                {
                    var n = c.ConditionAmount > 0 ? c.ConditionAmount : 6;
                    ok = HandAndField(who).Count >= n || (opp != null && HandAndField(opp).Count >= n);
                    reason = $"A player must have {n} or more cards in their hand and on the field.";
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Every card <paramref name="who"/> holds in their hand or controls on the field.</summary>
        static List<CardInstance> HandAndField(DuelistState who)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            list.AddRange(who.Hand.Where(h => h != null));
            list.AddRange(who.MonstersOnField());
            list.AddRange(who.SpellTrapsOnField());
            if (who.PendulumZones != null)
                foreach (var z in who.PendulumZones)
                    if (z?.Occupant != null && !list.Contains(z.Occupant))
                        list.Add(z.Occupant);
            return list;
        }

        // ───────────────────────── returned from the field to the Deck ─────────────────────────

        /// <summary>
        /// DuelEngine.ReturnCardToDeck: <paramref name="card"/> left the field for <paramref name="owner"/>'s Deck
        /// (<paramref name="wasFaceUp"/> on the field). Queues its "returned from the field to your Deck" trigger.
        /// </summary>
        public static void NoteReturnedToDeck(DuelEngine engine, DuelistState owner, CardInstance card, bool wasFaceUp)
        {
            if (engine == null || owner == null || card?.Def == null || !wasFaceUp) return;
            // "to your Deck": the Main Deck (an Extra Deck monster goes to the Extra Deck, a Token vanishes).
            if (card.IsToken || card.Def.IsExtraDeck) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return;
            var clauses = prog.ClausesFor(EffectTiming.ReturnedFromFieldToDeck);
            if (clauses == null || clauses.Count == 0) return;
            var clause = clauses.FirstOrDefault(c => c != null && c.Action == EffectActionKind.SpecialSummonFromDeck);
            if (clause == null) return;
            engine.Log($"{card.Name} was returned from the field to {owner.Name}'s Deck — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, owner, card, clauses.TrueForAll(c => c.IsOptional),
                () => ResolveSerpentine(engine, owner, card, clause));
        }

        /// <summary>Serpentine Princess: select and Special Summon 1 Level N or lower monster from the Deck, then shuffle.</summary>
        static void ResolveSerpentine(DuelEngine engine, DuelistState owner, CardInstance source, EffectClause clause)
        {
            if (engine == null || owner?.Deck == null || engine.GameOver) return;
            engine.Log($"{source.Name}: Special Summon 1 Level {clause.Amount} or lower monster from the Deck.");
            if (engine.FirstEmpty(owner.MonsterZones) < 0)
            {
                engine.Log($"{source.Name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            var legal = DeckSummonCandidates(engine, owner, source, clause);
            if (legal.Count == 0)
            {
                engine.Log($"{source.Name}: no Level {clause.Amount} or lower monster in the Deck can be Special Summoned.");
                return;
            }

            if (engine.IsHumanControlled(owner) && engine.PendingActivation == null)
            {
                var p = new PendingActivation
                {
                    Controller = owner,
                    Card = source,
                    TargetKind = EffectTargetKind.MonsterInYourDeckToSummon,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"select 1 Level {clause.Amount} or lower monster in your Deck to Special Summon " +
                                     "(then your Deck is shuffled)."
                };
                p.LegalTargets.AddRange(legal);
                s_picks.Add(p, new Pick { Kind = PickKind.SerpentineSummon, Chooser = owner, Source = source, Clause = clause });
                engine.SetPendingActivation(p);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return;
            }

            SummonFromDeck(engine, owner, source, AiBestMonster(legal));
        }

        static List<CardInstance> DeckSummonCandidates(DuelEngine engine, DuelistState owner, CardInstance source,
            EffectClause clause)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in owner.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!def.IsMonster || def.IsExtraDeck || def.level > clause.Amount) continue;
                var inst = engine.CreateCardInstance(id);
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, owner, inst, source, SummonOrigin.Deck))
                    continue;
                list.Add(inst);
            }

            return list;
        }

        static void SummonFromDeck(DuelEngine engine, DuelistState owner, CardInstance source, CardInstance pick)
        {
            if (pick == null) return;
            var index = owner.Deck.IndexOf(pick.CardId);
            if (index < 0)
            {
                engine.Log($"{source?.Name}: {pick.Name} is no longer in the Deck.");
                return;
            }

            // PerCopyDeck batch (v163): the pick takes the copy's owner; a failed Summon puts the same copy back.
            var copy = PerCopyDeckRuntime.TakeFromDeck(owner, index, pick);
            if (!engine.SpecialSummonToField(owner, pick, BattlePosition.Attack, true))
            {
                owner.Deck.Insert(index, pick.CardId, copy);
                engine.Log($"{source?.Name}: the Special Summon of {pick.Name} failed.");
            }
            else
                engine.Log($"{source?.Name}: {owner.Name} Special Summons {pick.Name} from the Deck.");

            engine.Rng.Shuffle(owner.Deck);
            engine.Log($"{owner.Name} shuffles their Deck.");
        }

        static CardInstance AiBestMonster(List<CardInstance> legal) =>
            legal.OrderByDescending(c => c.Def?.atk ?? 0).ThenByDescending(c => c.Def?.def ?? 0).FirstOrDefault();

        // ───────────────────────── Fiber Jar ─────────────────────────

        /// <summary>
        /// Fiber Jar: hand, field and GY of both players into the Deck (see the class summary), then the controller
        /// draws <see cref="EffectClause.Amount"/>, then the opponent.
        /// </summary>
        public static void ResolveFiberJar(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var opp = engine.OpponentOf(who);
            var sides = new[] { who, opp }.Where(s => s != null).ToList();
            var draws = clause != null && clause.Amount > 0 ? clause.Amount : 5;

            // The Lua moves the whole group in one Duel.SendtoDeck: the hands and GYs are fixed now, and a GY card
            // Necrovalley locks is judged before any card leaves (Necrovalley itself goes back to the Deck with the rest).
            var hands = sides.ToDictionary(s => s, s => s.Hand.Where(h => h != null).ToList());
            var gys = sides.ToDictionary(s => s, s => s.Graveyard
                .Where(g => g != null && !ContinuousProtections.GyMovementLocked(engine, source, g)).ToList());

            var field = new List<CardInstance>();
            foreach (var side in sides)
            foreach (var c in HandAndField(side))
            {
                if (side.Hand.Contains(c)) continue;
                if (engine.IsBattleDestroyPending(c) || ReferenceEquals(c, engine.FlipSourceBattleDestroyPending))
                {
                    engine.Log($"{c.Name} is being destroyed by battle — it is not shuffled into the Deck.");
                    continue;
                }

                if (ContinuousProtections.IsUnaffectedBy(engine, c, source))
                {
                    engine.Log($"{c.Name} is unaffected by {source?.Name}.");
                    continue;
                }

                field.Add(c);
            }

            // One simultaneous move: links between cards that both leave are dropped first, so no Equip Card or Call of
            // the Haunted is sent to the GY (or destroys its monster) because its partner left a moment earlier.
            foreach (var c in field)
            {
                var host = c.EquippedTo;
                if (host == null || !field.Contains(host)) continue;
                host.Equips.Remove(c);
                c.EquippedTo = null;
            }

            var shuffled = new HashSet<DuelistState>();
            var counts = new Dictionary<DuelistState, int>();
            void Count(DuelistState s)
            {
                counts.TryGetValue(s, out var n);
                counts[s] = n + 1;
            }

            foreach (var c in field)
            {
                var controller = engine.ControllerOf(c);
                if (controller == null) continue;
                var owner = c.ControlOwner ?? controller;
                var toMain = !c.IsToken && c.Def != null && !c.Def.IsExtraDeck;
                if (!engine.ReturnCardToDeck(c)) continue;
                if (toMain) shuffled.Add(owner);
                Count(owner);
            }

            foreach (var side in sides)
            {
                foreach (var h in hands[side])
                {
                    if (!side.Hand.Remove(h)) continue;
                    if (h.Def != null && h.Def.IsExtraDeck)
                        side.ExtraDeck?.Add(h.CardId);
                    else
                    {
                        side.Deck.Add(h.CardId);
                        shuffled.Add(side);
                    }

                    Count(side);
                }

                foreach (var g in gys[side])
                {
                    if (!side.Graveyard.Remove(g)) continue;
                    if (g.IsToken) continue;
                    if (g.Def != null && g.Def.IsExtraDeck)
                        side.ExtraDeck?.Add(g.CardId);
                    else
                    {
                        side.Deck.Add(g.CardId);
                        shuffled.Add(side);
                    }

                    Count(side);
                }
            }

            foreach (var side in shuffled)
                engine.Rng.Shuffle(side.Deck);
            foreach (var side in sides)
            {
                counts.TryGetValue(side, out var n);
                engine.Log($"{source?.Name}: {side.Name} shuffles {n} card(s) from the hand, field and GY into the Deck.");
            }

            foreach (var side in sides)
            {
                if (engine.GameOver) break;
                engine.Draw(side, draws);
            }

            engine.NotifyPublic();
        }

        // ───────────────────────── Mysterious Guard ─────────────────────────

        /// <summary>
        /// SimultaneousTriggersRuntime.ActivateFlip: a FLIP effect with no generic target activating on a SEGOC Chain.
        /// Mysterious Guard's targets are chosen now; the effect resolves later on them.
        /// </summary>
        public static void ActivateFlipTargets(DuelEngine engine, DuelistState who, CardInstance monster,
            List<EffectClause> flip)
        {
            var c = flip?.FirstOrDefault(x =>
                x != null && x.Action == EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace);
            if (c == null || engine == null || who == null || monster == null) return;
            BeginGuard(engine, who, monster, c, resolveAfter: false);
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause: Mysterious Guard's FLIP effect. Resolves on the targets chosen as it activated
        /// on a SEGOC Chain; activated outside one (a FLIP flipped by an attack, a Flip Summon), the targets are chosen
        /// now and it resolves at once.
        /// </summary>
        public static void ApplyGuard(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var st = StateOf(engine);
            if (st.Guard.TryGetValue(source, out var act))
            {
                st.Guard.Remove(source);
                ResolveGuard(engine, act);
                return;
            }

            BeginGuard(engine, who, source, clause, resolveAfter: true);
        }

        static List<CardInstance> GuardCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance except)
        {
            var list = new List<CardInstance>();
            foreach (var side in new[] { who, engine.OpponentOf(who) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || !m.FaceUp || ReferenceEquals(m, except)) continue;
                    // STATUS_BATTLE_DESTROYED: a monster this battle destroys cannot be targeted.
                    if (engine.IsBattleDestroyPending(m) || ReferenceEquals(m, engine.FlipSourceBattleDestroyPending))
                        continue;
                    if (engine.IsDragonTargetProtected(m)) continue;
                    if (ContinuousProtections.TargetedEffectBlocked(engine, m, source)) continue;
                    list.Add(m);
                }
            }

            return list;
        }

        static bool ControlsFaceUpRace(DuelistState who, string race) =>
            who != null && !string.IsNullOrEmpty(race) &&
            who.MonstersOnField().Any(m => m != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, race));

        static Snap SnapOf(CardInstance c) => c == null ? null : new Snap { Card = c, Stamp = c.FieldEntryStamp };

        static void BeginGuard(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            bool resolveAfter)
        {
            var act = new GuardActivation { Who = who, Source = source, Clause = clause, ResolveAfter = resolveAfter };
            var legal = GuardCandidates(engine, who, source, null);
            if (legal.Count == 0)
            {
                engine.Log($"{source.Name} FLIP: no face-up monster on the field to target — no effect.");
                FinishGuardActivation(engine, act);
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                act.First = SnapOf(AiGuardFirst(who, legal));
                engine.Log($"{source.Name} targets {act.First.Card.Name}.");
                if (ControlsFaceUpRace(who, clause.ConditionName))
                {
                    var second = AiGuardSecond(engine, who, legal.Where(m => !ReferenceEquals(m, act.First.Card)).ToList());
                    if (second != null)
                    {
                        act.Second = SnapOf(second);
                        engine.Log($"{source.Name} also targets {second.Name}.");
                    }
                }

                FinishGuardActivation(engine, act);
                return;
            }

            OpenGuardPick(engine, act, PickKind.GuardFirst, legal,
                ControlsFaceUpRace(who, clause.ConditionName)
                    ? "target 1 face-up monster on the field (you can then target 1 more)."
                    : "target 1 face-up monster on the field (it returns to the top of its owner's Deck).");
        }

        static void OpenGuardPick(DuelEngine engine, GuardActivation act, PickKind kind, List<CardInstance> legal,
            string prompt)
        {
            var p = new PendingActivation
            {
                Controller = act.Who,
                Card = act.Source,
                TargetKind = EffectTargetKind.FaceUpMonsterOnField,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                TargetPicksAreUpTo = kind == PickKind.GuardSecond,
                PromptOverride = prompt
            };
            p.LegalTargets.AddRange(legal);
            s_picks.Add(p, new Pick { Kind = kind, Guard = act, Chooser = act.Who, Source = act.Source, Clause = act.Clause });
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
        }

        /// <summary>The first target is chosen: offer the additional one, or finish the activation.</summary>
        static void AfterGuardFirst(DuelEngine engine, GuardActivation act)
        {
            if (ControlsFaceUpRace(act.Who, act.Clause.ConditionName))
            {
                var more = GuardCandidates(engine, act.Who, act.Source, act.First?.Card);
                if (more.Count > 0)
                {
                    OpenGuardPick(engine, act, PickKind.GuardSecond, more,
                        $"you control a face-up {act.Clause.ConditionName} monster: you can target 1 additional face-up " +
                        "monster (at resolution you choose which target returns to the top of the Deck and which to the " +
                        "hand), or Cancel (only the first target, to the top of the Deck).");
                    return;
                }
            }

            FinishGuardActivation(engine, act);
        }

        static void FinishGuardActivation(DuelEngine engine, GuardActivation act)
        {
            if (act.ResolveAfter)
            {
                ResolveGuard(engine, act);
                return;
            }

            StateOf(engine).Guard[act.Source] = act;
            engine.NotifyPublic();
        }

        static bool StillFaceUp(DuelEngine engine, Snap s) =>
            s?.Card != null && engine.ControllerOf(s.Card) != null && s.Card.FieldEntryStamp == s.Stamp && s.Card.FaceUp &&
            s.Card.Def != null && s.Card.Def.IsMonster;

        /// <summary>
        /// Mysterious Guard resolves (Lua c1347977 s.activate). One target: if still face-up, to the top of its owner's
        /// Deck. Two targets: the ones still face-up on the field; a lone one goes to the top of the Deck; with both, the
        /// activator chooses which one goes to the Deck (a human through a pick), then the other returns to the hand —
        /// only if the first reached the Main Deck and a face-up monster of the printed Type is still controlled.
        /// </summary>
        static void ResolveGuard(DuelEngine engine, GuardActivation act)
        {
            var source = act.Source;
            var name = source?.Name ?? "Mysterious Guard";
            if (act.First == null)
            {
                engine.NotifyPublic();
                return;
            }

            var live = new[] { act.First, act.Second }.Where(s => s != null && StillFaceUp(engine, s)).Select(s => s.Card)
                .ToList();
            if (live.Count == 0)
            {
                engine.Log($"{name}: no target is still face-up on the field — no effect.");
                FinishGuard(engine, act, null, null);
                return;
            }

            if (act.Second == null || live.Count == 1)
            {
                if (act.Second != null)
                    engine.Log($"{name}: only {live[0].Name} is still face-up on the field — it returns to the Deck.");
                FinishGuard(engine, act, live[0], null);
                return;
            }

            // Lua s.selfilter: the card for the Deck must be able to go there (here: not protected from this effect).
            var toDeck = live.Where(m => !ContinuousProtections.TargetedEffectBlocked(engine, m, source)).ToList();
            if (toDeck.Count == 0)
            {
                engine.Log($"{name}: both targets are protected at resolution — no effect.");
                FinishGuard(engine, act, null, null);
                return;
            }

            if (toDeck.Count > 1 && engine.IsHumanControlled(act.Who) && engine.PendingActivation == null)
            {
                var p = new PendingActivation
                {
                    Controller = act.Who,
                    Card = source,
                    TargetKind = EffectTargetKind.FaceUpMonsterOnField,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = "choose which target returns to the top of its owner's Deck (the other returns to " +
                                     "the hand if you still control a face-up " + act.Clause.ConditionName + " monster)."
                };
                p.LegalTargets.AddRange(toDeck);
                s_picks.Add(p, new Pick { Kind = PickKind.GuardDeck, Guard = act, Chooser = act.Who, Source = source, Clause = act.Clause });
                engine.SetPendingActivation(p);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return;
            }

            var dc = AiGuardDeck(act.Who, toDeck);
            FinishGuard(engine, act, dc, live.FirstOrDefault(m => !ReferenceEquals(m, dc)));
        }

        /// <summary>
        /// <paramref name="toDeck"/> goes to the top of its owner's Deck; then, if it reached the Main Deck,
        /// <paramref name="toHand"/> returns to the hand if a face-up monster of the printed Type is still controlled.
        /// </summary>
        static void FinishGuard(DuelEngine engine, GuardActivation act, CardInstance toDeck, CardInstance toHand)
        {
            var source = act.Source;
            var name = source?.Name ?? "Mysterious Guard";
            var targets = new List<CardInstance> { act.First.Card };
            if (act.Second != null) targets.Add(act.Second.Card);
            try
            {
                if (toDeck == null) return;
                if (ContinuousProtections.TargetedEffectBlocked(engine, toDeck, source))
                {
                    engine.Log($"{name}: {toDeck.Name} is protected at resolution — no effect.");
                    return;
                }

                var toMain = !toDeck.IsToken && toDeck.Def != null && !toDeck.Def.IsExtraDeck;
                if (!engine.ReturnCardToDeck(toDeck, onTop: true) || !toMain)
                {
                    if (toHand != null)
                        engine.Log($"{name}: {toDeck.Name} did not go to the Deck — {toHand.Name} stays.");
                    return;
                }

                if (toHand == null) return;
                var snap = ReferenceEquals(act.First.Card, toHand) ? act.First : act.Second;
                if (!StillFaceUp(engine, snap))
                {
                    engine.Log($"{name}: {toHand.Name} is no longer face-up on the field — it stays where it is.");
                    return;
                }

                if (!ControlsFaceUpRace(act.Who, act.Clause.ConditionName))
                {
                    engine.Log($"{name}: {act.Who.Name} no longer controls a face-up {act.Clause.ConditionName} " +
                               $"monster — {toHand.Name} stays on the field.");
                    return;
                }

                if (ContinuousProtections.TargetedEffectBlocked(engine, toHand, source))
                {
                    engine.Log($"{name}: {toHand.Name} is protected at resolution.");
                    return;
                }

                engine.ReturnCardToHand(toHand, by: act.Who);
            }
            finally
            {
                TextEffectRuntime.NotifyTargetingEffectsResolved(engine, targets, true);
                engine.NotifyPublic();
            }
        }

        /// <summary>AI at resolution: the opponent's strongest target goes to the Deck (its own, the weakest).</summary>
        static CardInstance AiGuardDeck(DuelistState who, List<CardInstance> options)
        {
            var theirs = options.Where(m => !who.TryFindMonster(m, out _)).OrderByDescending(m => m.CurrentAtk).FirstOrDefault();
            return theirs ?? options.OrderBy(m => m.CurrentAtk).First();
        }

        /// <summary>AI: the opponent's strongest face-up monster; with none, its own weakest.</summary>
        static CardInstance AiGuardFirst(DuelistState who, List<CardInstance> legal)
        {
            var theirs = legal.Where(m => !who.TryFindMonster(m, out _)).OrderByDescending(m => m.CurrentAtk).FirstOrDefault();
            return theirs ?? legal.OrderBy(m => m.CurrentAtk).First();
        }

        /// <summary>AI: an additional opponent's face-up monster, the strongest left; never its own.</summary>
        static CardInstance AiGuardSecond(DuelEngine engine, DuelistState who, List<CardInstance> legal) =>
            legal.Where(m => !who.TryFindMonster(m, out _)).OrderByDescending(m => m.CurrentAtk).FirstOrDefault();

        // ───────────────────────── Creature Swap ─────────────────────────

        /// <summary>TextEffectRuntime.ApplyClause: Creature Swap resolves.</summary>
        public static void ResolveCreatureSwap(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null) return;
            var opp = engine.OpponentOf(who);
            if (!who.MonstersOnField().Any() || opp == null || !opp.MonstersOnField().Any())
            {
                engine.Log($"{source?.Name}: each player must control a monster — no effect.");
                return;
            }

            ContinueSwap(engine, new SwapPlan { Who = who, Source = source });
        }

        /// <summary>Asks the next chooser; true when the swap is complete (no pick left open).</summary>
        static bool ContinueSwap(DuelEngine engine, SwapPlan plan)
        {
            var opp = engine.OpponentOf(plan.Who);
            while (plan.Mine == null || plan.Theirs == null)
            {
                var chooser = plan.Mine == null ? plan.Who : opp;
                var options = chooser.MonstersOnField().Where(m => m != null).ToList();
                if (options.Count == 0)
                {
                    engine.Log($"{plan.Source?.Name}: {chooser.Name} controls no monster — no effect.");
                    return true;
                }

                CardInstance pick;
                if (options.Count > 1 && engine.IsHumanControlled(chooser))
                {
                    var p = new PendingActivation
                    {
                        Controller = chooser,
                        Card = plan.Source,
                        TargetKind = EffectTargetKind.YourMonster,
                        UsesTextProgram = true,
                        HoldsChainResolution = true,
                        TargetPicksRemaining = 1,
                        PromptOverride = "choose 1 monster you control; it switches control with the monster your " +
                                         "opponent chooses (neither can change its battle position this turn)."
                    };
                    p.LegalTargets.AddRange(options);
                    s_picks.Add(p, new Pick { Kind = PickKind.SwapOwn, Swap = plan, Chooser = chooser, Source = plan.Source });
                    engine.SetPendingActivation(p);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return false;
                }

                pick = options.Count == 1 ? options[0] : AiSwapPick(options);
                SetSwapPick(engine, plan, chooser, pick);
            }

            FinishSwap(engine, plan);
            return true;
        }

        static void SetSwapPick(DuelEngine engine, SwapPlan plan, DuelistState chooser, CardInstance pick)
        {
            if (plan.Mine == null && ReferenceEquals(chooser, plan.Who)) plan.Mine = pick;
            else plan.Theirs = pick;
            engine.Log($"{plan.Source?.Name}: {chooser.Name} chooses {(pick.FaceUp ? pick.Name : "a Set monster")}.");
        }

        /// <summary>AI: give away its weakest monster.</summary>
        static CardInstance AiSwapPick(List<CardInstance> options) =>
            options.OrderBy(m => m.CurrentAtk).ThenBy(m => m.CurrentDef).First();

        static void FinishSwap(DuelEngine engine, SwapPlan plan)
        {
            var opp = engine.OpponentOf(plan.Who);
            var a = plan.Mine;
            var b = plan.Theirs;
            if (a == null || b == null || !plan.Who.TryFindMonster(a, out _) || opp == null || !opp.TryFindMonster(b, out _))
            {
                engine.Log($"{plan.Source?.Name}: a chosen monster left — no control switch.");
                return;
            }

            if (!engine.TrySwapControl(a, b))
            {
                engine.Log($"{plan.Source?.Name}: the control switch failed.");
                return;
            }

            PositionLocksRuntime.LockRestOfTurn(engine, a);
            PositionLocksRuntime.LockRestOfTurn(engine, b);
            engine.Log($"{plan.Source?.Name}: neither monster can change its battle position for the rest of this turn.");
            engine.NotifyPublic();
        }

        // ───────────────────────── Bubble Crash ─────────────────────────

        /// <summary>TextEffectRuntime.ApplyClause: Bubble Crash resolves.</summary>
        public static void ResolveBubbleCrash(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null) return;
            var keep = Math.Max(0, clause.Amount);
            var min = clause.ConditionAmount > 0 ? clause.ConditionAmount : keep + 1;
            var plan = new BubblePlan { Who = who, Source = source, Keep = keep };
            foreach (var side in new[] { who, engine.OpponentOf(who) })
            {
                if (side == null) continue;
                var cards = HandAndField(side);
                var need = cards.Count >= min ? cards.Count - keep : 0;
                // The activator never selects this card (Lua: SelectMatchingCard(..., exc)); it still counts.
                var offer = cards.Where(c => !ReferenceEquals(c, source)).ToList();
                need = Math.Min(need, offer.Count);
                if (need <= 0)
                {
                    engine.Log($"{source?.Name}: {side.Name} has {cards.Count} card(s) in hand and on the field — " +
                               "nothing to send.");
                    continue;
                }

                plan.Order.Add(side);
                plan.Need[side] = need;
                plan.Offer[side] = offer;
                engine.Log($"{source?.Name}: {side.Name} has {cards.Count} card(s) — sends {need} to the GY.");
            }

            if (plan.Order.Count == 0) return;
            ContinueBubble(engine, plan);
        }

        /// <summary>Asks the next player; true when every card is chosen and sent (no pick left open).</summary>
        static bool ContinueBubble(DuelEngine engine, BubblePlan plan)
        {
            foreach (var side in plan.Order)
            {
                if (plan.Chosen.ContainsKey(side)) continue;
                var need = plan.Need[side];
                var offer = plan.Offer[side];
                if (offer.Count > need && engine.IsHumanControlled(side))
                {
                    var p = new PendingActivation
                    {
                        Controller = side,
                        Card = plan.Source,
                        TargetKind = EffectTargetKind.None,
                        UsesTextProgram = true,
                        HoldsChainResolution = true,
                        TargetPicksRemaining = need,
                        PromptOverride = $"select {need} card(s) from your hand and field to send to the GY " +
                                         $"(until {plan.Keep} remain)."
                    };
                    p.LegalTargets.AddRange(offer);
                    s_picks.Add(p, new Pick { Kind = PickKind.BubbleSend, Bubble = plan, Chooser = side, Source = plan.Source });
                    engine.SetPendingActivation(p);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return false;
                }

                plan.Chosen[side] = offer.Count <= need ? offer.ToList() : AiBubbleSends(side, offer, need);
            }

            SendBubble(engine, plan);
            return true;
        }

        /// <summary>
        /// AI (and Cancel): hand cards first (weakest monsters first), then Spells/Traps on the field, then its weakest
        /// monsters on the field.
        /// </summary>
        static List<CardInstance> AiBubbleSends(DuelistState side, List<CardInstance> offer, int need)
        {
            bool InHand(CardInstance c) => side?.Hand != null && side.Hand.Contains(c);
            int Group(CardInstance c) => InHand(c) ? 0 : c.Def != null && c.Def.IsMonster ? 2 : 1;
            int Atk(CardInstance c) => c.Def == null || !c.Def.IsMonster ? 0 : InHand(c) ? c.Def.atk : c.CurrentAtk;
            return offer.OrderBy(Group).ThenBy(Atk).Take(Math.Max(0, need)).ToList();
        }

        static void SendBubble(DuelEngine engine, BubblePlan plan)
        {
            var sent = 0;
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var side in plan.Order)
                {
                    if (!plan.Chosen.TryGetValue(side, out var cards)) continue;
                    foreach (var c in cards)
                    {
                        if (side.Hand.Contains(c))
                        {
                            if (MonsterEffects.SendFromHandToGraveByInstance(side, c) != null) sent++;
                            continue;
                        }

                        var controller = engine.ControllerOf(c);
                        if (controller != side) continue;
                        engine.SendCardToGrave(controller, c);
                        sent++;
                    }
                }
            });
            engine.Log($"{plan.Source?.Name}: {sent} card(s) are sent to the GY.");
            engine.NotifyPublic();
        }

        // ───────────────────────── picks ─────────────────────────

        /// <summary>SpellTrapEffects.TryResolveWithTarget: a pick this batch asked.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || target == null || !s_picks.TryGetValue(p, out var pick)) return false;
            if (!p.LegalTargets.Contains(target)) return true;
            ok = true;
            switch (pick.Kind)
            {
                case PickKind.GuardFirst:
                    Close(engine, p);
                    pick.Guard.First = SnapOf(target);
                    engine.Log($"{pick.Source.Name} targets {target.Name}.");
                    AfterGuardFirst(engine, pick.Guard);
                    return true;
                case PickKind.GuardSecond:
                    Close(engine, p);
                    pick.Guard.Second = SnapOf(target);
                    engine.Log($"{pick.Source.Name} also targets {target.Name}.");
                    FinishGuardActivation(engine, pick.Guard);
                    return true;
                case PickKind.SwapOwn:
                    Close(engine, p);
                    SetSwapPick(engine, pick.Swap, pick.Chooser, target);
                    if (ContinueSwap(engine, pick.Swap))
                        engine.ResumeChainAfterChoice();
                    engine.NotifyPublic();
                    return true;
                case PickKind.BubbleSend:
                {
                    p.LegalTargets.Remove(target);
                    p.ChosenTargets.Add(target);
                    p.TargetPicksRemaining--;
                    engine.Log($"{pick.Source?.Name}: {target.Name} will be sent to the GY.");
                    if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                    {
                        engine.Log(p.Prompt);
                        engine.NotifyPublic();
                        return true;
                    }

                    var chosen = p.ChosenTargets.ToList();
                    Close(engine, p);
                    pick.Bubble.Chosen[pick.Chooser] = chosen;
                    if (ContinueBubble(engine, pick.Bubble))
                        engine.ResumeChainAfterChoice();
                    engine.NotifyPublic();
                    return true;
                }
                case PickKind.SerpentineSummon:
                    Close(engine, p);
                    SummonFromDeck(engine, pick.Chooser, pick.Source, target);
                    engine.NotifyPublic();
                    return true;
                case PickKind.GuardDeck:
                {
                    Close(engine, p);
                    var act = pick.Guard;
                    var other = ReferenceEquals(act.First.Card, target) ? act.Second?.Card : act.First.Card;
                    engine.Log($"{pick.Source?.Name}: {target.Name} returns to the top of the Deck.");
                    FinishGuard(engine, act, target, other);
                    return true;
                }
                default:
                    ok = false;
                    return false;
            }
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: every pick of this batch is mandatory and is made automatically, except
        /// Mysterious Guard's additional target, which Cancel declines.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick)) return false;
            switch (pick.Kind)
            {
                case PickKind.GuardFirst:
                {
                    var auto = AiGuardFirst(pick.Chooser, p.LegalTargets.ToList());
                    engine.Log($"{pick.Source.Name}: the target is mandatory — {auto.Name} is targeted.");
                    TryResolvePick(engine, auto, out _);
                    return true;
                }
                case PickKind.GuardSecond:
                    Close(engine, p);
                    engine.Log($"{pick.Source.Name}: no additional target.");
                    FinishGuardActivation(engine, pick.Guard);
                    return true;
                case PickKind.SwapOwn:
                {
                    var auto = AiSwapPick(p.LegalTargets.ToList());
                    engine.Log($"{pick.Source?.Name}: the choice is mandatory — {auto.Name} is chosen.");
                    TryResolvePick(engine, auto, out _);
                    return true;
                }
                case PickKind.BubbleSend:
                {
                    engine.Log($"{pick.Source?.Name}: the selection is mandatory — the remaining cards are chosen.");
                    var rest = AiBubbleSends(pick.Chooser, p.LegalTargets.ToList(), p.TargetPicksRemaining);
                    foreach (var c in rest)
                        if (engine.PendingActivation != p || !TryResolvePick(engine, c, out _))
                            break;
                    if (engine.PendingActivation == p)
                    {
                        var chosen = p.ChosenTargets.ToList();
                        Close(engine, p);
                        pick.Bubble.Chosen[pick.Chooser] = chosen;
                        if (ContinueBubble(engine, pick.Bubble))
                            engine.ResumeChainAfterChoice();
                        engine.NotifyPublic();
                    }

                    return true;
                }
                case PickKind.SerpentineSummon:
                {
                    var auto = AiBestMonster(p.LegalTargets.ToList());
                    engine.Log($"{pick.Source?.Name}: the Special Summon is mandatory — {auto?.Name} is selected.");
                    Close(engine, p);
                    SummonFromDeck(engine, pick.Chooser, pick.Source, auto);
                    engine.NotifyPublic();
                    return true;
                }
                case PickKind.GuardDeck:
                {
                    var auto = AiGuardDeck(pick.Chooser, p.LegalTargets.ToList());
                    engine.Log($"{pick.Source?.Name}: the choice is mandatory — {auto.Name} is chosen.");
                    TryResolvePick(engine, auto, out _);
                    return true;
                }
                default:
                    return false;
            }
        }

        static void Close(DuelEngine engine, PendingActivation p)
        {
            s_picks.Remove(p);
            if (ReferenceEquals(engine.PendingActivation, p))
                engine.ClearPendingActivation();
        }
    }
}
