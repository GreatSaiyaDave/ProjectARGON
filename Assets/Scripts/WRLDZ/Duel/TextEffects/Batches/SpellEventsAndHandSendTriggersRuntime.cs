using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellEventsAndHandSendTriggers" runtime.
    /// <list type="bullet">
    /// <item><b>aux.chainreg</b> (<see cref="WasApplyingWhenActivated"/>): an effect that counts Spell Card activations
    /// "when that Spell resolves" (ygopro EVENT_CHAINING flag + EVENT_CHAIN_SOLVED) only counts a Spell activated while the
    /// card was already face-up and applying. A Continuous / Field card whose own activation resolves in the middle of a
    /// Chain (<see cref="NoteLinkResolving"/>, from DuelEngine.ResolveChainStack) did not apply when the lower links were
    /// activated, so the Spells of that Chain do not count for it. Read by Curse of Darkness, Tower of Babel and every
    /// "place 1 Spell Counter when that Spell resolves" card (TextEffectRuntime.TryPlaceSpellCounterOnSpell).</item>
    /// <item><b>Curse of Darkness</b> (<see cref="OnSpellResolved"/>, from TextEffectRuntime.NotifySpellResolved): the
    /// player who activated the Spell takes the damage at once (a continuous effect, no Chain Link). An activation that was
    /// negated never resolves (no damage); a Spell whose effect was negated still resolves (Jar Robber), as in ocgcore
    /// (EVENT_CHAIN_SOLVED).</item>
    /// <item><b>Tower of Babel</b>: its "place 1 Spell Counter" clause is the shared PlaceCounterOnSpellActivate one; when
    /// that placement makes exactly 4 (<see cref="AfterOwnSpellCounter"/>) a mandatory trigger (Lua RaiseSingleEvent,
    /// TRIGGER_F) goes on the next simultaneous-trigger Chain: destroy this card and, if it is destroyed, 3000 damage to
    /// the player who activated that Spell. Another card's effect bringing it to 4 or more
    /// (<see cref="AfterSpellCounterAddedByEffect"/>, Lua e5) only destroys it.</item>
    /// <item><b>Spirit of the Pot of Greed</b> (<see cref="OnCardActivationPlaced"/>, from
    /// SpellTrapEffects.BeginDeferredChainActivation): the card activation of the named card while this card is face-up in
    /// Attack Position puts this card's trigger right above it on the Chain (Lua EVENT_CHAINING TRIGGER_F). As that link
    /// resolves, if this card is still face-up in Attack Position, the player who activated the named card may draw 1
    /// (a yes/no tap through <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick"/>; the AI decides inline).</item>
    /// </list>
    /// </summary>
    public static class SpellEventsAndHandSendTriggersRuntime
    {
        sealed class State
        {
            /// <summary>Cards whose own activation resolved while a Chain resolved, with that Chain's activation count.</summary>
            public readonly Dictionary<CardInstance, long> EnabledMidChain = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────── aux.chainreg ─────────────────────

        /// <summary>
        /// DuelEngine.ResolveChainStack, as <paramref name="link"/> starts to resolve (before its card's
        /// <see cref="CardInstance.ActivationUnresolvedOnChain"/> is cleared): a Continuous / Field card whose own activation
        /// this is starts applying now, after every lower link of this Chain was activated.
        /// </summary>
        public static void NoteLinkResolving(DuelEngine engine, ChainLink link)
        {
            if (engine?.Chain == null || link?.Card == null || !link.Card.ActivationUnresolvedOnChain) return;
            StateOf(engine).EnabledMidChain[link.Card] = engine.Chain.ActivationCount;
        }

        /// <summary>
        /// <paramref name="card"/> was face-up and applying when the Spell Card resolving now was activated: false only
        /// when its own activation resolved earlier in the Chain that is resolving now (a link above that Spell's).
        /// </summary>
        public static bool WasApplyingWhenActivated(DuelEngine engine, CardInstance card)
        {
            if (engine?.Chain == null || card == null) return false;
            if (!engine.IsResolvingChain) return true;
            if (!s_state.TryGetValue(engine, out var st) || !st.EnabledMidChain.TryGetValue(card, out var at))
                return true;
            return at != engine.Chain.ActivationCount;
        }

        // ───────────────────── Curse of Darkness ─────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifySpellResolved: <paramref name="activator"/>'s activation of a Spell Card resolved. Each
        /// face-up Curse of Darkness that applied when it was activated deals its damage to that player.
        /// </summary>
        public static void OnSpellResolved(DuelEngine engine, DuelistState activator, CardInstance spell) =>
            OnCardActivationResolved(engine, activator, spell, trap: false);

        /// <summary>
        /// A Spell Card's (<paramref name="trap"/> false: TextEffectRuntime.NotifySpellResolved) or a Trap Card's
        /// (<paramref name="trap"/> true: RdsRedoSpellTrapRuntime, as its Chain Link finishes resolving) activation by
        /// <paramref name="activator"/> resolved — ygopro aux.chainreg on EVENT_CHAINING + a CONTINUOUS EVENT_CHAIN_SOLVED
        /// effect, no Chain Link. Each face-up, applying card of either player that applied when <paramref name="card"/>
        /// was activated (<see cref="WasApplyingWhenActivated"/>), other than <paramref name="card"/> itself, applies its
        /// clause: Curse of Darkness (the Spell's activator takes the damage), Spell Absorption (RdsRedoSpellTrap batch:
        /// its controller gains the LP), Chain Burst (RdsRedoSpellTrap batch: the Trap's activator takes the damage).
        /// </summary>
        public static void OnCardActivationResolved(DuelEngine engine, DuelistState activator, CardInstance card, bool trap)
        {
            if (engine == null || activator == null || engine.GameOver) return;
            var actions = trap
                ? new[] { EffectActionKind.InflictDamageToTrapActivatorAfterResolve }
                : new[]
                {
                    EffectActionKind.InflictDamageToSpellActivatorAfterResolve,
                    EffectActionKind.GainLpWhenSpellCardActivationResolves
                };
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField().ToList())
                {
                    if (ReferenceEquals(st, card)) continue;
                    foreach (var action in actions)
                    {
                        var c = LiveClause(engine, side, st, action);
                        if (c == null || !WasApplyingWhenActivated(engine, st)) continue;
                        var what = card?.Name ?? (trap ? "a Trap Card" : "a Spell Card");
                        if (action == EffectActionKind.GainLpWhenSpellCardActivationResolves)
                        {
                            var gain = c.Amount > 0 ? c.Amount : 500;
                            engine.Log($"{st.Name}: {activator.Name} activated {what} — {side.Name} gains {gain} LP.");
                            TextEffectRuntime.ApplyLifePointGain(engine, side, gain, st.Name);
                        }
                        else
                        {
                            var dmg = c.Amount > 0 ? c.Amount : 1000;
                            engine.Log($"{st.Name}: {activator.Name} activated {what} — {dmg} damage.");
                            engine.ApplyEffectDamage(activator, dmg, st.Name, st);
                        }

                        if (engine.GameOver) return;
                    }
                }
            }
        }

        /// <summary>The <paramref name="action"/> clause of a face-up, applying Spell/Trap <paramref name="side"/> controls.</summary>
        static EffectClause LiveClause(DuelEngine engine, DuelistState side, CardInstance st, EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || side == null) return null;
            if (!side.TryFindSpellTrap(st, out _)) return null;
            if (ContinuousNegationRuntime.NegatedBy(engine, st) != null) return null;
            return ClauseOf(st, action);
        }

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        // ───────────────────── Tower of Babel ─────────────────────

        /// <summary>
        /// TextEffectRuntime.TryPlaceSpellCounterOnSpell, after <paramref name="card"/>'s own "place 1 Spell Counter when
        /// that Spell resolves" placement for <paramref name="activator"/>'s Spell (it had <paramref name="before"/>): its
        /// Spell Counters are now exactly N (Lua acop: GetCounter(COUNTER_SPELL)==4) — its destroy-and-damage trigger.
        /// </summary>
        public static void AfterOwnSpellCounter(DuelEngine engine, CardInstance card, int before, DuelistState activator)
        {
            if (engine == null || card == null) return;
            var c = ClauseOf(card, EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator);
            if (c == null) return;
            var n = c.ConditionAmount > 0 ? c.ConditionAmount : 4;
            if (before >= n || card.Counters != n) return;
            QueueTowerTrigger(engine, card, c, activator, damage: true);
        }

        /// <summary>
        /// SpellCounterSpendersRuntime.AddSpellCounters: another card's effect (<paramref name="source"/>) placed Spell
        /// Counters on <paramref name="card"/>; with N or more now, its self-destruction trigger (Lua e5, EVENT_ADD_COUNTER
        /// with re:GetHandler()~=c).
        /// </summary>
        public static void AfterSpellCounterAddedByEffect(DuelEngine engine, CardInstance card, CardInstance source)
        {
            if (engine == null || card == null || ReferenceEquals(source, card)) return;
            var c = ClauseOf(card, EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator);
            if (c == null) return;
            var n = c.ConditionAmount > 0 ? c.ConditionAmount : 4;
            if (card.Counters < n) return;
            QueueTowerTrigger(engine, card, c, null, damage: false);
        }

        static void QueueTowerTrigger(DuelEngine engine, CardInstance card, EffectClause clause, DuelistState activator,
            bool damage)
        {
            var controller = engine.ControllerOf(card);
            if (controller == null || LiveClause(engine, controller, card, clause.Action) == null) return;
            // A Spell/Trap trigger cannot be activated under a Spell/Trap activation lock.
            if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) return;
            var stamp = card.FieldEntryStamp;
            var amount = clause.Amount > 0 ? clause.Amount : 3000;
            engine.Log(damage
                ? $"{card.Name}: Spell Counter {card.Counters} placed — its effect activates."
                : $"{card.Name}: {card.Counters} Spell Counters by another card's effect — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, controller, card, optional: false, () =>
            {
                if (engine.GameOver) return;
                // Lua IsRelateToEffect: the same card, still on the field.
                if (card.FieldEntryStamp != stamp || !controller.TryFindSpellTrap(card, out _) || !card.FaceUp)
                {
                    engine.Log($"{card.Name} is no longer on the field — its effect does not resolve.");
                    return;
                }

                if (ContinuousNegationRuntime.NegatedBy(engine, card) is { } negator)
                {
                    engine.Log($"{card.Name}: its effect is negated by {negator.Name}.");
                    return;
                }

                TextEffectRuntime.DestroyCard(engine, card, source: card);
                if (controller.TryFindSpellTrap(card, out _))
                {
                    engine.Log($"{card.Name} was not destroyed — no damage.");
                    return;
                }

                if (!damage || activator == null) return;
                engine.Log($"{card.Name}: {activator.Name} activated the Spell Card that placed it — {amount} damage.");
                engine.ApplyEffectDamage(activator, amount, card.Name, card);
            });
        }

        // ───────────────────── Spirit of the Pot of Greed ─────────────────────

        /// <summary>
        /// SpellTrapEffects.BeginDeferredChainActivation: <paramref name="activator"/>'s card activation of
        /// <paramref name="card"/> is now the last Chain Link, before anyone may respond. Each face-up Attack Position monster
        /// whose trigger names that card puts its trigger link right above it (turn player's first).
        /// </summary>
        public static void OnCardActivationPlaced(DuelEngine engine, DuelistState activator, CardInstance card)
        {
            if (engine?.Chain == null || activator == null || card?.Def == null || engine.GameOver) return;
            var link = engine.Chain.Last;
            if (link == null || !ReferenceEquals(link.Card, card) || link.Resolved) return;
            // A card activation (ygopro EFFECT_TYPE_ACTIVATE): the card itself, from the hand or a Set.
            if (!link.FromHand && !link.WasSetOnField) return;
            var turn = engine.TurnPlayer ?? activator;
            foreach (var side in new[] { turn, engine.OpponentOf(turn) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m?.Def == null || !m.FaceUp || m.Position != BattlePosition.Attack) continue;
                    var c = ClauseOf(m, EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition);
                    if (c == null || string.IsNullOrEmpty(c.NamedCard) || !card.IsNamed(c.NamedCard)) continue;
                    if (m.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, m) != null) continue;
                    var stamp = m.FieldEntryStamp;
                    var owner = side;
                    var monster = m;
                    var amount = c.Amount > 0 ? c.Amount : 1;
                    var seat = new TriggerChainResponsesRuntime.Seat
                    {
                        Controller = owner,
                        Card = monster,
                        From = CardLocation.MonsterZone,
                        Resolve = () => ResolveSpiritDraw(engine, owner, monster, stamp, activator, amount)
                    };
                    if (TriggerChainResponsesRuntime.AddOnBuildingChain(engine, seat) != null)
                        engine.Log($"{monster.Name}'s effect activates: Chain Link {engine.Chain.Count} above {card.Name}.");
                }
            }
        }

        static void ResolveSpiritDraw(DuelEngine engine, DuelistState controller, CardInstance monster, int stamp,
            DuelistState drawer, int amount)
        {
            if (engine == null || engine.GameOver || drawer == null) return;
            bool StillApplies() =>
                monster.FieldEntryStamp == stamp && controller.TryFindMonster(monster, out _) && monster.FaceUp &&
                monster.Position == BattlePosition.Attack;
            if (!StillApplies())
            {
                engine.Log($"{monster.Name} is no longer face-up in Attack Position — no draw.");
                return;
            }

            if (monster.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, monster) != null)
            {
                engine.Log($"{monster.Name}: its effect is negated.");
                return;
            }

            // Lua Duel.IsPlayerCanDraw(p, 1).
            if (drawer.Deck == null || drawer.Deck.Count < amount ||
                ContinuousProtections.BlocksDrawOutsideDrawPhase(engine, drawer))
            {
                engine.Log($"{monster.Name}: {drawer.Name} cannot draw.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, drawer, monster, new CardPickRequest
            {
                Candidates = () => StillApplies() ? new List<CardInstance> { monster } : new List<CardInstance>(),
                Kind = ReferenceEquals(controller, drawer) ? EffectTargetKind.YourMonster : EffectTargetKind.OppMonster,
                Prompt = $"{monster.Name}: tap it to draw {amount} more card (CANCEL = do not draw).",
                AskWithOneCandidate = true,
                // The AI keeps enough cards for the 2 draws of the card below this link.
                AiChoose = list => drawer.Deck.Count > amount + 1 ? list.FirstOrDefault() : null,
                Cancel = () => engine.Log($"{drawer.Name} does not draw with {monster.Name}."),
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{drawer.Name} does not draw with {monster.Name}.");
                        return;
                    }

                    engine.Log($"{monster.Name}: {drawer.Name} draws {amount} more card.");
                    engine.Draw(drawer, amount);
                }
            });
        }
    }
}
