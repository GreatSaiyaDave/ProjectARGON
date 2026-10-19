using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvCountersAndPhaseTriggers" runtime (atoms: <see cref="CrvCountersAndPhaseTriggersAtoms"/>):
    /// <list type="bullet">
    /// <item>B.E.S. Crystal Core: <see cref="ChangeFaceUpAttackTargetToDefense"/>.</item>
    /// <item>B.E.S. Tetran: its counter cost (<see cref="CostCounters"/>); a counter cost paid for a monster ignition is
    /// refunded when its target pick is cancelled (<see cref="NoteCounterCostPaid"/> / <see cref="RefundCounterCost"/>).</item>
    /// <item>Dark Catapulter: <see cref="CatapulterReady"/> and <see cref="BeginCatapulter"/>.</item>
    /// <item>Cyber Archfiend: <see cref="OfferPreDrawExtraDraw"/> (DuelEngine.ContinueDrawPhase) and
    /// <see cref="DestroyThisIfHandNotEmpty"/>.</item>
    /// <item>Rapid-Fire Magician: <see cref="OnSpellResolved"/> (TextEffectRuntime.NotifySpellResolved).</item>
    /// </list>
    /// </summary>
    public static class CrvCountersAndPhaseTriggersRuntime
    {
        /// <summary>
        /// Dark Catapulter's ignition (Lua descost / destg chk): N = its counters, N &gt;= 1, N cards in your GY that can be
        /// banished and N Spell/Trap Cards on the field that can be targeted.
        /// </summary>
        public const string CatapulterReady = "CrvCounters.CatapulterReady";

        sealed class State
        {
            public int PreDrawTurn = -1;
            public readonly List<CardInstance> PreDrawQueue = new();
        }

        sealed class CounterCost
        {
            public CardInstance Card;
            public int Count;
        }

        sealed class CatapulterRun
        {
            public DuelEngine Engine;
            public DuelistState Who;
            public CardInstance Source;
            public int Count;
            public bool Inline;
            public bool Done;
            public readonly List<CardInstance> Cost = new();
            public readonly List<CardInstance> Targets = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<ChainLink, CounterCost> s_counterCosts = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>A face-up, unnegated monster <paramref name="side"/> controls.</summary>
        static bool Active(DuelistState side, CardInstance card) =>
            card?.Def != null && side != null && card.FaceUp && !card.IsNegated && side.TryFindMonster(card, out _);

        /// <summary>The <paramref name="action"/> marker of a fully compiled program (a Stub's fragment never applies).</summary>
        static EffectClause MarkerOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        // ───────────────────────────── B.E.S. Crystal Core ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ChangeFaceUpAttackTargetToFaceUpDefense"/> (Lua posop): the target, still face-up in
        /// Attack Position on the field, becomes face-up Defense Position; otherwise nothing happens.
        /// </summary>
        public static void ChangeFaceUpAttackTargetToDefense(DuelEngine engine, CardInstance source, CardInstance target)
        {
            if (engine == null || target == null) return;
            var controller = engine.ControllerOf(target);
            if (controller == null || !controller.TryFindMonster(target, out _) || !target.FaceUp ||
                target.Position != BattlePosition.Attack)
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer face-up in Attack Position — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            TextEffectRuntime.SetFaceUpPosition(engine, target, BattlePosition.Defense);
        }

        // ───────────────────────────── counter costs (B.E.S. Tetran) ─────────────────────────────

        /// <summary>
        /// The counters <paramref name="card"/> can remove for <paramref name="c"/>'s cost: its own non-Spell counters of
        /// this field presence (<see cref="EffectClause.CounterCostIsNotSpellCounter"/>), else its Spell Counters.
        /// </summary>
        public static int CostCounters(CardInstance card, EffectClause c)
        {
            if (card == null) return 0;
            return c != null && c.CounterCostIsNotSpellCounter
                ? TokensDeclarationsAndPicksRuntime.CountersOfLastPresence(card)
                : card.Counters;
        }

        /// <summary>
        /// TextEffectRuntime.PayAutoIgnitionCosts: <paramref name="n"/> counters were removed from <paramref name="card"/>
        /// as the cost of the monster ignition being activated (its "manual" Chain Link).
        /// </summary>
        internal static void NoteCounterCostPaid(DuelEngine engine, CardInstance card, int n)
        {
            var link = engine?.Chain?.Last;
            if (link == null || n <= 0 || !ReferenceEquals(link.Card, card) || link.Resolved || link.EffectKey != "manual")
                return;
            s_counterCosts.Remove(link);
            s_counterCosts.Add(link, new CounterCost { Card = card, Count = n });
        }

        /// <summary>
        /// EngineResidualsRuntime.UndoMonsterIgnition: Cancel at the target pick undid the activation of
        /// <paramref name="card"/> (<paramref name="link"/>) — the counters it removed as the cost come back.
        /// </summary>
        internal static void RefundCounterCost(DuelEngine engine, CardInstance card, ChainLink link)
        {
            if (engine == null || card == null || link == null || !s_counterCosts.TryGetValue(link, out var cost) ||
                !ReferenceEquals(cost.Card, card))
                return;
            s_counterCosts.Remove(link);
            card.Counters += cost.Count;
            engine.Log($"{card.Name}: {cost.Count} counter(s) removed as the cost come back ({card.Counters}).");
        }

        // ───────────────────────────── Dark Catapulter ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet: <see cref="CatapulterReady"/>.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c?.ActivationCondition != CatapulterReady) return false;
            var n = TokensDeclarationsAndPicksRuntime.CountersOfLastPresence(card);
            if (n <= 0)
            {
                reason = "There is no counter on this card.";
                return true;
            }

            if (CatapulterCostCards(engine, who, card).Count < n)
            {
                reason = $"Needs {n} card(s) in your GY to banish.";
                return true;
            }

            if (CatapulterTargets(engine, who, card).Count < n)
            {
                reason = $"Needs {n} Spell/Trap Card(s) on the field.";
                return true;
            }

            ok = true;
            return true;
        }

        /// <summary>The cards in your GY the cost can banish (Card.IsAbleToRemove: Necrovalley, Kycoo, Banisher locks).</summary>
        static List<CardInstance> CatapulterCostCards(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine == null || who?.Graveyard == null) return list;
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, who) || RuleLocksRuntime.BanishFromGyLocked(engine, who))
                return list;
            foreach (var g in who.Graveyard)
                if (g?.Def != null && !ContinuousProtections.GyMovementLocked(engine, source, g) && !list.Contains(g))
                    list.Add(g);
            return list;
        }

        /// <summary>The Spell/Trap Cards on either field the ignition can target (the shared FieldSpellTraps picker).</summary>
        static List<CardInstance> CatapulterTargets(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return new List<CardInstance>();
            var probe = new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                TargetCount = 1
            };
            return TextEffectRuntime.CollectTargets(engine, who, probe, source)
                .Where(t => t?.Def != null && engine.ControllerOf(t) != null).Distinct().ToList();
        }

        /// <summary>
        /// <see cref="EffectActionKind.BanishGyCardsPerCounterDestroyThatManySpellTraps"/>, as the ignition is activated
        /// (monster ignitions resolve as they are made): N = the counters. A human chooses the N GY cards (cost) and then the
        /// N Spell/Trap targets one by one (Cancel undoes the activation, nothing paid); the AI and a forced choice take
        /// them at once. Then the cost is paid, the targets are destroyed only if all of them are still on the field, and
        /// every counter is removed from this card.
        /// </summary>
        public static void BeginCatapulter(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            var n = TokensDeclarationsAndPicksRuntime.CountersOfLastPresence(source);
            var gy = CatapulterCostCards(engine, who, source);
            var field = CatapulterTargets(engine, who, source);
            if (n <= 0 || gy.Count < n || field.Count < n)
            {
                engine.Log($"{source.Name}: it cannot be activated now (counters {n}, GY {gy.Count}, Spell/Trap {field.Count}).");
                return;
            }

            var run = new CatapulterRun { Engine = engine, Who = who, Source = source, Count = n, Inline = true };
            if (!engine.IsHumanControlled(who))
            {
                run.Cost.AddRange(AiCost(gy, n));
                run.Targets.AddRange(AiTargets(engine, who, field, n));
                Complete(run);
                return;
            }

            if (gy.Count == n) run.Cost.AddRange(gy);
            NextPick(run);
            run.Inline = false;
        }

        static void NextPick(CatapulterRun r)
        {
            if (r.Done) return;
            var engine = r.Engine;
            if (r.Cost.Count < r.Count)
            {
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, r.Who, r.Source, new CardPickRequest
                {
                    Candidates = () => CatapulterCostCards(engine, r.Who, r.Source).Where(g => !r.Cost.Contains(g)).ToList(),
                    Kind = EffectTargetKind.BanishFromYourGy,
                    Prompt = $"{r.Source.Name}: banish {r.Count - r.Cost.Count} more card(s) from your GY (cost), or " +
                             "Cancel.",
                    AiChoose = list => AiCost(list, 1).FirstOrDefault(),
                    Finish = c =>
                    {
                        if (c == null)
                        {
                            Undo(r);
                            return;
                        }

                        r.Cost.Add(c);
                        NextPick(r);
                    },
                    Cancel = () => Undo(r),
                    DuringActivation = true
                });
                return;
            }

            if (r.Targets.Count < r.Count)
            {
                var left = CatapulterTargets(engine, r.Who, r.Source).Where(t => !r.Targets.Contains(t)).ToList();
                if (left.Count <= r.Count - r.Targets.Count)
                {
                    // Every remaining Spell/Trap Card must be targeted: no choice to make.
                    r.Targets.AddRange(left);
                    if (r.Targets.Count < r.Count) Undo(r);
                    else Complete(r);
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, r.Who, r.Source, new CardPickRequest
                {
                    Candidates = () => CatapulterTargets(engine, r.Who, r.Source).Where(t => !r.Targets.Contains(t))
                        .ToList(),
                    Kind = EffectTargetKind.SpellTrapOnField,
                    Prompt = $"{r.Source.Name}: target {r.Count - r.Targets.Count} more Spell/Trap Card(s) on the field " +
                             "to destroy, or Cancel.",
                    AiChoose = list => AiTargets(engine, r.Who, list, 1).FirstOrDefault(),
                    Finish = t =>
                    {
                        if (t == null)
                        {
                            Undo(r);
                            return;
                        }

                        r.Targets.Add(t);
                        NextPick(r);
                    },
                    Cancel = () => Undo(r),
                    DuringActivation = true
                });
                return;
            }

            Complete(r);
        }

        /// <summary>Cancel: the activation never happened (nothing was paid yet).</summary>
        static void Undo(CatapulterRun r)
        {
            if (r.Done) return;
            r.Done = true;
            r.Engine.DropUnresolvedLink(r.Source);
            r.Engine.Log($"Cancelled {r.Source.Name}'s effect — the activation is undone.");
            if (!r.Inline) r.Engine.ClearIdleChain();
            r.Engine.NotifyPublic();
        }

        static void Complete(CatapulterRun r)
        {
            if (r.Done) return;
            r.Done = true;
            var engine = r.Engine;
            var who = r.Who;
            var source = r.Source;
            // Cost: banish the N chosen GY cards (one banish event).
            using (BanishEvents.Group(engine))
            {
                foreach (var g in r.Cost)
                    if (who.Graveyard.Contains(g))
                        engine.BanishCard(who, g, by: who);
            }

            engine.Log($"{source.Name}: banished {r.Cost.Count} card(s) from the GY; targets " +
                       string.Join(", ", r.Targets.Select(t => t.Name)) + ".");
            // Resolution: destroy only if every target is still on the field (Lua: #relating == #targets).
            if (TargetedClassicsRuntime.AllStillOnField(engine, r.Targets))
            {
                foreach (var t in r.Targets)
                    if (engine.ControllerOf(t) != null)
                        TextEffectRuntime.DestroyCard(engine, t, source, byPlayer: who);
            }
            else
                engine.Log($"{source.Name}: not every target is still on the field — nothing is destroyed.");

            // Then remove every counter from this card (always).
            if (engine.ControllerOf(source) != null && source.Counters > 0)
            {
                source.Counters = 0;
                engine.Log($"{source.Name}: every counter is removed from it.");
            }

            if (!r.Inline) engine.ClearIdleChain();
            engine.NotifyPublic();
        }

        /// <summary>The AI banishes Spell/Trap Cards first, then its weakest monsters.</summary>
        static List<CardInstance> AiCost(List<CardInstance> gy, int n) =>
            (gy ?? new List<CardInstance>())
            .OrderBy(g => g?.Def != null && g.Def.IsMonster ? 1 : 0)
            .ThenBy(g => g?.Def?.atk ?? 0)
            .Take(n).ToList();

        /// <summary>The AI targets the opponent's cards first (face-up ones first), its own only when it must.</summary>
        static List<CardInstance> AiTargets(DuelEngine engine, DuelistState who, List<CardInstance> field, int n) =>
            (field ?? new List<CardInstance>())
            .OrderBy(t => ReferenceEquals(engine.ControllerOf(t), who) ? 1 : 0)
            .ThenBy(t => t.FaceUp ? 0 : 1)
            .Take(n).ToList();

        // ───────────────────────────── AI (SimpleAi monster ignitions) ─────────────────────────────

        /// <summary>
        /// SimpleAi (monster ignitions): the rules let both cards target your own Spell/Traps (Lua: either field), but the AI
        /// uses B.E.S. Tetran only while a Spell/Trap its opponent controls is a legal target, and Dark Catapulter only while
        /// N of them are (N = its counters). Otherwise <see cref="TextEffectRuntime.AutoPick"/> / <see cref="AiTargets"/>
        /// would fall back to its own cards.
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance m)
        {
            if (engine == null || ai == null || m?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(m);
            if (prog == null || !prog.FullyCompiled) return false;
            var opp = engine.OpponentOf(ai);
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
            {
                if (c == null) continue;
                if (c.Action == EffectActionKind.BanishGyCardsPerCounterDestroyThatManySpellTraps)
                {
                    var n = TokensDeclarationsAndPicksRuntime.CountersOfLastPresence(m);
                    return opp == null || n <= 0 ||
                           CatapulterTargets(engine, ai, m).Count(t => opp.TryFindSpellTrap(t, out _)) < n;
                }

                if (c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldSpellTraps &&
                    c.CounterCostIsNotSpellCounter)
                    return opp == null ||
                           !TextEffectRuntime.CollectTargets(engine, ai, c, m).Exists(t => opp.TryFindSpellTrap(t, out _));
            }

            return false;
        }

        // ───────────────────────────── Cyber Archfiend ─────────────────────────────

        /// <summary>
        /// DuelEngine.ContinueDrawPhase, before the normal draw (ygopro EVENT_PREDRAW): each face-up Cyber Archfiend of the
        /// turn player whose hand was empty as the Draw Phase began may draw 1 card (TRIGGER_O; Lua drtg
        /// IsPlayerCanDraw(tp,1)). A human answers yes/no (tap the card / Cancel) and the Draw Phase waits (true); the AI
        /// draws when its Deck still holds the normal draw. The activated draw is a Chain Link the opponent may respond to
        /// (the Draw Phase goes on once the Chain has resolved, true); it draws even if the card has left the field
        /// (Lua drop has no relation check). Each copy is offered once per Draw Phase.
        /// </summary>
        public static bool OfferPreDrawExtraDraw(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || engine.GameOver || engine.TurnPlayer != who) return false;
            var st = StateOf(engine);
            if (st.PreDrawTurn != engine.TurnNumber)
            {
                st.PreDrawTurn = engine.TurnNumber;
                st.PreDrawQueue.Clear();
                // A Draw Phase skipped by a card effect has no start of the Draw Phase.
                if (who.SkipNextDrawPhaseCount > 0 || who.SkipNextDrawPhase) return false;
                // Lua drcon: no card in the hand as the Draw Phase begins.
                if (who.Hand.Count > 0) return false;
                foreach (var m in who.MonstersOnField())
                    if (Active(who, m) && MarkerOf(m, EffectActionKind.DrawOneAtDrawPhaseStartIfHandEmpty) != null)
                        st.PreDrawQueue.Add(m);
            }

            while (st.PreDrawQueue.Count > 0 && !engine.GameOver)
            {
                var source = st.PreDrawQueue[0];
                st.PreDrawQueue.RemoveAt(0);
                if (!Active(who, source)) continue;
                if (who.Deck == null || who.Deck.Count == 0 || ChoiceAmongEffectsRuntime.CannotDrawNow(engine, who))
                    continue;
                if (engine.IsHumanControlled(who) && engine.PendingActivation == null)
                {
                    ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                    {
                        Candidates = () => Active(who, source) ? new List<CardInstance> { source } : new List<CardInstance>(),
                        Kind = EffectTargetKind.YourMonster,
                        Prompt = $"{source.Name}: your hand is empty — draw 1 card in addition to your normal draw? Tap " +
                                 "it to draw, or Cancel.",
                        AskWithOneCandidate = true,
                        HoldsChain = false,
                        HoldsStandby = false,
                        AiChoose = _ => AiWantsExtraDraw(who) ? source : null,
                        Finish = c =>
                        {
                            if (c != null && ActivateExtraDraw(engine, who, source)) return;
                            if (c == null) engine.Log($"{source.Name}: not activated.");
                            engine.ContinueDrawPhase(who);
                        },
                        Cancel = () =>
                        {
                            engine.Log($"{source.Name}: not activated.");
                            engine.ContinueDrawPhase(who);
                        }
                    });
                    return true;
                }

                if (!AiWantsExtraDraw(who)) continue;
                if (ActivateExtraDraw(engine, who, source)) return true;
            }

            return false;
        }

        /// <summary>The AI draws while its Deck still holds the normal draw afterwards.</summary>
        static bool AiWantsExtraDraw(DuelistState who) => who?.Deck != null && who.Deck.Count >= 2;

        /// <summary>
        /// The activated draw: a Chain Link the opponent may respond to (true: the Draw Phase continues once the Chain has
        /// resolved), or resolved at once (false: the caller continues the Draw Phase).
        /// </summary>
        static bool ActivateExtraDraw(DuelEngine engine, DuelistState who, CardInstance source)
        {
            engine.Log($"{source.Name}: {who.Name}'s hand is empty — draw 1 more card.");
            var seat = new TriggerChainResponsesRuntime.Seat
            {
                Controller = who,
                Card = source,
                From = CardLocation.MonsterZone,
                Resolve = () => DrawOne(engine, who, source)
            };
            if (TriggerChainResponsesRuntime.TrySeat(engine, new[] { seat }, () => engine.ContinueDrawPhase(who)))
                return true;
            DrawOne(engine, who, source);
            return false;
        }

        static void DrawOne(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine.GameOver) return;
            engine.Log($"{source.Name}: {who.Name} draws 1 card in addition to the normal draw.");
            engine.Draw(who, 1);
        }

        /// <summary>
        /// <see cref="EffectActionKind.DestroyThisAtYourEndPhaseIfHandNotEmpty"/> (TextEffectRuntime.FirePhaseTriggersFor, its
        /// controller's End Phase): with 1 or more cards in that player's hand, this card (still face-up on the field) is
        /// destroyed.
        /// </summary>
        public static void DestroyThisIfHandNotEmpty(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            if (who.Hand.Count == 0) return;
            if (!source.FaceUp || !who.TryFindMonster(source, out _)) return;
            engine.Log($"{source.Name}: {who.Name} holds {who.Hand.Count} card(s) in the End Phase — it is destroyed.");
            TextEffectRuntime.DestroyCard(engine, source, source, byPlayer: who);
        }

        // ───────────────────────────── Rapid-Fire Magician ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifySpellResolved: <paramref name="activator"/>'s activation of <paramref name="spell"/>
        /// resolved (a negated activation never gets here). For a Normal Spell Card, each face-up, unnegated Rapid-Fire
        /// Magician <paramref name="activator"/> controls deals its damage to the opponent (Lua EVENT_CHAIN_SOLVED
        /// continuous effect: no Chain Link).
        /// </summary>
        public static void OnSpellResolved(DuelEngine engine, DuelistState activator, CardInstance spell)
        {
            if (engine == null || activator == null || engine.GameOver || spell?.Def == null) return;
            if (!SpellTrapEffects.IsNormalSpell(spell.Def)) return;
            var opp = engine.OpponentOf(activator);
            if (opp == null) return;
            foreach (var m in activator.MonstersOnField().ToList())
            {
                if (!Active(activator, m)) continue;
                var c = MarkerOf(m, EffectActionKind.InflictDamageToOpponentWhenYourNormalSpellResolves);
                if (c == null) continue;
                var dmg = c.Amount > 0 ? c.Amount : 400;
                engine.Log($"{m.Name}: {activator.Name} activated the Normal Spell {spell.Name} — {dmg} damage.");
                engine.ApplyEffectDamage(opp, dmg, m.Name, m);
                if (engine.GameOver) return;
            }
        }
    }
}
