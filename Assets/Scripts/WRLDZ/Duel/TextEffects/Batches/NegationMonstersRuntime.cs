using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// NegationMonsters batch (v161) runtime.
    /// <list type="bullet">
    /// <item><b>Target negation</b> (<see cref="EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy"/>; Freed the
    /// Matchless General, Frontier Wiseman, Tyrant Dragon; ygopro EVENT_CHAIN_SOLVING): as a Chain Link resolves, a Spell
    /// (Trap) effect whose targets include a protected monster — this card, or a face-up monster of the named Type its
    /// controller controls — is negated, and its card is destroyed while it is still on the field
    /// (<see cref="TryNegateTargetingLink"/>, from DuelEngine.ResolveChainStack). Either player's effect.</item>
    /// <item><b>Search instead of the draw</b> (<see cref="EffectActionKind.AddFromDeckInsteadOfNormalDraw"/>; Freed):
    /// before the turn player's normal draw (<see cref="OfferSearchInsteadOfDraw"/>, from DuelEngine.ContinueDrawPhase)
    /// a face-up copy may add a matching monster from the Deck instead of drawing. A human picks from the Deck (Cancel:
    /// draw normally) while the Draw Phase waits; the AI searches whenever it can.</item>
    /// <item><b>Battle negation</b> (<see cref="EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle"/>; Dark Ruler
    /// Ha Des, Twin-Headed Wolf; ygopro EVENT_BATTLED + EFFECT_DISABLE, RESETS_STANDARD_EXC_GRAVE): after damage
    /// calculation (<see cref="OnBattled"/>) a monster about to be destroyed by battle has its effects negated —
    /// on the field (its FLIP effect from this attack, its after-damage-calculation effects) and while it stays in the
    /// GY (<see cref="NegatedInGy"/>: its "sent to the GY" / "destroyed by battle" triggers and GY effects).</item>
    /// <item><b>Banish instead</b> (<see cref="EffectActionKind.BanishMonstersDestroyedByBattleWithThis"/>; Lesser Fiend;
    /// ygopro EFFECT_BATTLE_DESTROY_REDIRECT): read as the monster is destroyed by battle (DuelEngine.DestroyMonster,
    /// <see cref="BanishesBattleDestroyed"/>) — only while Lesser Fiend's effect still applies: face-up on the field and
    /// not negated, or destroyed by the same battle at the same time.</item>
    /// <item><b>Double Snare's target class</b> (<see cref="EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects"/>): a
    /// face-up card whose compiled clauses include an effect that negates Trap effects (<see cref="NegatesTrapEffects"/>).</item>
    /// <item><b>Tyrant Dragon's second attack</b>: an <see cref="EffectActionKind.ExtraAttacks"/> clause with
    /// <see cref="OpponentControlsMonster"/> counts only while the opponent controls a monster
    /// (<see cref="ExtraAttackConditionMet"/>, from DuelEngine.ExtraAttacksAllowed).</item>
    /// </list>
    /// Only a FullyCompiled program's markers apply (<see cref="Markers"/>): a Stub's or a partial program's fragment never
    /// runs (Tyrant Dragon, Fiend Skull Dragon).
    /// </summary>
    public static class NegationMonstersRuntime
    {
        /// <summary><see cref="EffectClause.ActivationCondition"/> of Tyrant Dragon's second attack (ygopro s.atkcon).</summary>
        public const string OpponentControlsMonster = "NegationMonsters.OpponentControlsMonster";

        sealed class State
        {
            /// <summary>The battlers of the battle being applied (<see cref="OnBattled"/>).</summary>
            public CardInstance Attacker, Defender;
            /// <summary>Lesser Fiend: the battlers this battle's destruction has already destroyed (at the same time).</summary>
            public readonly HashSet<CardInstance> DestroyedThisBattle = new();
            /// <summary>Freed: the turn whose Draw Phase already offered the search (offered once per Draw Phase).</summary>
            public int OfferedTurn = -1;
            /// <summary>Freed: the turn whose normal draw was replaced by the search.</summary>
            public int DrawReplacedTurn = -1;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>A human's open Freed search: the turn player whose Draw Phase waits for it.</summary>
        sealed class DrawSearch
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
        }

        static readonly ConditionalWeakTable<PendingActivation, DrawSearch> s_searches = new();

        static IEnumerable<EffectClause> Markers(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card);
            // FullyCompiled only: a Stub's / partial program's fragment cannot invent an effect (Tyrant Dragon's Trap
            // sentence without its summon cost; Fiend Skull Dragon's without its Flip negation, whose ygopro script also
            // has no SZONE disable / self-destroy).
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    yield return c;
        }

        static bool Active(DuelEngine engine, DuelistState side, CardInstance card) =>
            card?.Def != null && side != null && card.FaceUp && !card.IsNegated && side.TryFindMonster(card, out _);

        // ───────────────────────────── target negation ─────────────────────────────

        /// <summary>
        /// DuelEngine.ResolveChainStack, as <paramref name="link"/> starts resolving (ygopro EVENT_CHAIN_SOLVING): when it is
        /// the effect of a Spell (Trap) Card that targets a monster a face-up protector covers, the effect is negated and its
        /// card, still on the field, is destroyed. True when the link was negated (it applies nothing).
        /// </summary>
        public static bool TryNegateTargetingLink(DuelEngine engine, ChainLink link)
        {
            var card = link?.Card;
            var def = card?.Def;
            if (engine?.Player == null || engine.Opponent == null || def == null) return false;
            if (link.Negated || link.NegatesActivation) return false;
            if (!def.IsSpell && !def.IsTrap) return false;
            var targets = TargetsOf(link);
            if (targets.Count == 0 || !EffectTargets(link)) return false;
            // ygopro Duel.NegateEffect fails on an effect that cannot be negated.
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.ClauseList.Exists(c => c != null && !c.EffectNegatable)) return false;

            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                foreach (var protector in side.MonstersOnField().ToList())
                {
                    if (!Active(engine, side, protector)) continue;
                    foreach (var clause in Markers(protector, EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy))
                    {
                        if (clause.NegateRespondsToSpell && !def.IsSpell) continue;
                        if (clause.NegateRespondsToTrap && !def.IsTrap) continue;
                        if (!clause.NegateRespondsToSpell && !clause.NegateRespondsToTrap) continue;
                        var covered = targets.FirstOrDefault(t => Covers(engine, side, protector, clause, t));
                        if (covered == null) continue;
                        Negate(engine, link, side, protector, covered);
                        return true;
                    }
                }
            }

            return false;
        }

        static void Negate(DuelEngine engine, ChainLink link, DuelistState side, CardInstance protector,
            CardInstance covered)
        {
            var card = link.Card;
            link.DeferredEffect = null; // its recorded effect never applies
            Presentation.ArInteraction.ActivationBeat.NoteNegated(card.InstanceId);
            engine.Log($"{protector.Name}: the effect of {card.Name} that targets {covered.Name} is negated.");
            // Lua: NegateEffect(ev) and re:GetHandler():IsRelateToEffect(re) → Destroy (REASON_EFFECT).
            var holder = engine.ControllerOf(card);
            if (holder != null && (holder.TryFindSpellTrap(card, out _) ||
                                   ReferenceEquals(holder.FieldSpellZone?.Occupant, card)))
            {
                engine.Log($"{protector.Name}: {card.Name} is destroyed.");
                TextEffectRuntime.DestroyCard(engine, card, protector, byPlayer: side);
            }
        }

        /// <summary>The cards the link's effect targets (its chosen target and target list).</summary>
        static List<CardInstance> TargetsOf(ChainLink link)
        {
            var list = new List<CardInstance>();
            if (link.Target != null) list.Add(link.Target);
            foreach (var t in link.Targets)
                if (t != null && !list.Contains(t))
                    list.Add(t);
            return list;
        }

        /// <summary>
        /// The link's effect targets (ygopro EFFECT_FLAG_CARD_TARGET): a recorded compiled activation lists only its
        /// targets; otherwise a registered staple that targets (Enemy Controller, Ring of Destruction …) or a compiled
        /// program with a targeting clause.
        /// </summary>
        static bool EffectTargets(ChainLink link)
        {
            if (link.DeferredEffect != null) return true;
            var def = link.Card?.Def;
            if (def == null) return false;
            if (SpellTrapEffects.GetTargetKind(def.id) != EffectTargetKind.None) return true;
            var prog = CompiledEffectCache.GetOrCompile(def);
            return prog != null && prog.ClauseList.Exists(c => c != null && c.RequiresTargetChoice && !c.ChoiceDoesNotTarget);
        }

        static bool Covers(DuelEngine engine, DuelistState side, CardInstance protector, EffectClause clause,
            CardInstance target)
        {
            if (target?.Def == null || !target.Def.IsMonster) return false;
            if (clause.AffectsThisCardOnly) return ReferenceEquals(target, protector);
            if (!side.TryFindMonster(target, out _)) return false;
            if (clause.RequiresTargetFaceUp && !target.FaceUp) return false;
            return string.IsNullOrEmpty(clause.RaceFilter) || ClassicEraTemplates.RaceMatches(target.Race, clause.RaceFilter);
        }

        static bool s_sweeping;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (ygopro EFFECT_DISABLE + EFFECT_SELF_DESTROY on LOCATION_SZONE cards whose card
        /// targets include a protected monster): a face-up Continuous Spell (Trap) that keeps targeting a monster a face-up
        /// protector now covers — it bound the monster while the protector's effect did not apply, e.g. a Set Tyrant Dragon
        /// later flipped face-up, or a monster Call of the Haunted revived — is negated and destroyed. Negated, a Call of the
        /// Haunted leaves without destroying its monster (its Lua checks IsDisabled() on EVENT_LEAVE_FIELD_P;
        /// DuelEngine.UnlinkCardFromHostOnLeave). Such a card's lasting target is the monster it is linked to
        /// (<see cref="CardInstance.EquippedTo"/>: Spellbinding Circle, Call of the Haunted); an Equip Card has no lasting
        /// card target.
        /// </summary>
        public static void SweepPersistentTargets(DuelEngine engine)
        {
            if (s_sweeping || engine?.Player == null || engine.Opponent == null || engine.GameOver ||
                engine.PendingActivation != null)
                return;
            var doomed = new List<(CardInstance card, CardInstance protector, DuelistState side)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                foreach (var protector in side.MonstersOnField())
                {
                    if (!Active(engine, side, protector)) continue;
                    foreach (var clause in Markers(protector, EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy))
                    foreach (var holder in new[] { engine.Player, engine.Opponent })
                    foreach (var st in holder.SpellTrapsOnField())
                    {
                        if (st?.Def == null || !st.FaceUp || st.EquippedTo == null || !IsLastingTargetCard(st)) continue;
                        if (clause.NegateRespondsToSpell ? !st.Def.IsSpell : !st.Def.IsTrap) continue;
                        if (!Covers(engine, side, protector, clause, st.EquippedTo)) continue;
                        if (!doomed.Exists(d => ReferenceEquals(d.card, st))) doomed.Add((st, protector, side));
                    }
                }
            }

            if (doomed.Count == 0) return;
            s_sweeping = true;
            try
            {
                foreach (var (card, protector, side) in doomed)
                {
                    var holder = engine.ControllerOf(card);
                    if (holder == null || !holder.TryFindSpellTrap(card, out _)) continue;
                    engine.Log($"{protector.Name}: {card.Name} targets {card.EquippedTo?.Name} — its effect is negated and it is destroyed.");
                    // EFFECT_DISABLE applies while the card is on the field; it ends once the card leaves.
                    var wasNegated = card.IsNegated;
                    card.IsNegated = true;
                    try
                    {
                        TextEffectRuntime.DestroyCard(engine, card, protector, byPlayer: side);
                    }
                    finally
                    {
                        if (!wasNegated) card.IsNegated = false;
                    }
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }

        /// <summary>A Continuous Spell/Trap that keeps its target (not an Equip Card).</summary>
        static bool IsLastingTargetCard(CardInstance st) =>
            st.Def != null && !st.Def.IsEquipSpell &&
            string.Equals(st.Def.race, "Continuous", StringComparison.OrdinalIgnoreCase);

        // ───────────────────────────── Tyrant Dragon's second attack ─────────────────────────────

        /// <summary>DuelEngine.ExtraAttacksAllowed: an extra attack whose condition does not hold now counts for nothing.</summary>
        public static bool ExtraAttackConditionMet(DuelEngine engine, CardInstance attacker, EffectClause clause)
        {
            if (clause == null || clause.ActivationCondition != OpponentControlsMonster) return true;
            var controller = engine?.ControllerOf(attacker);
            var opp = controller != null ? engine.OpponentOf(controller) : null;
            return opp != null && opp.MonstersOnField().Any(m => m != null);
        }

        // ───────────────────────────── Freed: search instead of the draw ─────────────────────────────

        /// <summary>
        /// DuelEngine.ContinueDrawPhase, before the turn player's normal draw (ygopro EVENT_PREDRAW): a face-up copy of a
        /// card with <see cref="EffectActionKind.AddFromDeckInsteadOfNormalDraw"/> may search instead. The AI searches at
        /// once (the draw is then skipped through <see cref="ConsumeDrawReplaced"/>); a human gets a Deck pick and the Draw
        /// Phase waits (returns true). Offered once per Draw Phase, never when the draw is skipped.
        /// </summary>
        public static bool OfferSearchInsteadOfDraw(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who?.Deck == null || engine.TurnPlayer != who) return false;
            var st = StateOf(engine);
            if (st.OfferedTurn == engine.TurnNumber) return false;
            st.OfferedTurn = engine.TurnNumber;
            // ygopro s.condition: a draw count above 0 (no first-turn skip, no Draw Phase skip) and a card in the Deck.
            if (engine.TurnNumber == 1 && who == engine.FirstPlayer) return false;
            if (who.SkipNextDrawPhaseCount > 0 || who.SkipNextDrawPhase || who.Deck.Count == 0) return false;

            foreach (var source in who.MonstersOnField().ToList())
            {
                if (!Active(engine, who, source)) continue;
                foreach (var clause in Markers(source, EffectActionKind.AddFromDeckInsteadOfNormalDraw))
                {
                    var legal = TextEffectRuntime.CollectTargets(engine, who, clause, source);
                    if (legal.Count == 0) continue;
                    if (!engine.IsHumanControlled(who))
                    {
                        var pick = legal.OrderByDescending(c => c.Def?.atk ?? 0).First();
                        // TriggerChainResponses batch (v163): a Chain the opponent may respond to; the Draw Phase waits.
                        if (TrySeatSearch(engine, who, source, pick)) return true;
                        Search(engine, who, source, pick);
                        return false;
                    }

                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = source,
                        TargetKind = TextEffectRuntime.MapTargetKind(clause),
                        IsMonsterEffect = true,
                        UsesTextProgram = true,
                        TargetPicksRemaining = 1,
                        DeclarationPrompt = $"{source.Name}: instead of your normal draw, you can add 1 Level " +
                                            $"{clause.Amount} or lower {clause.RaceFilter} monster from your Deck to " +
                                            "your hand. Choose it, or CANCEL to draw normally."
                    };
                    pending.LegalTargets.AddRange(legal);
                    s_searches.Add(pending, new DrawSearch { Who = who, Source = source, Clause = clause });
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    return true;
                }
            }

            return false;
        }

        /// <summary>DuelEngine.ContinueDrawPhase: this turn's normal draw was replaced by the search (consumed once).</summary>
        public static bool ConsumeDrawReplaced(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || engine.TurnPlayer != who) return false;
            var st = StateOf(engine);
            if (st.DrawReplacedTurn != engine.TurnNumber) return false;
            st.DrawReplacedTurn = -1;
            return true;
        }

        static void Search(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            StateOf(engine).DrawReplacedTurn = engine.TurnNumber;
            engine.Log($"{source.Name}: {who.Name} adds a monster from the Deck instead of conducting the normal draw.");
            // Resolution: this card must still be face-up on the field (nothing can change while the Draw Phase waits).
            if (!Active(engine, who, source)) return;
            MonsterEffects.AddCardIdFromDeckToHand(engine, who, pick.CardId, source.Name);
        }

        /// <summary>TextEffectRuntime.TryResolveMonsterTextTarget hook: the chosen monster is added instead of the draw.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_searches.TryGetValue(pending, out var search)) return false;
            var legal = engine.ResolveLegalEffectTarget(target);
            if (legal == null)
            {
                engine.Log("Choose a monster from your Deck, or CANCEL to draw normally.");
                return true;
            }

            ok = true;
            s_searches.Remove(pending);
            engine.ClearPendingActivation();
            // TriggerChainResponses batch (v163): the activated search goes on a Chain the opponent may respond to; the
            // Draw Phase goes on once it has resolved.
            if (TrySeatSearch(engine, search.Who, search.Source, legal))
            {
                engine.NotifyPublic();
                return true;
            }

            Search(engine, search.Who, search.Source, legal);
            engine.ContinueDrawPhase(search.Who);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// TriggerChainResponses batch (v163): the search is a trigger effect (ygopro EVENT_PREDRAW, EFFECT_TYPE_TRIGGER_O)
        /// and makes a Chain Link the opponent may respond to (Book of Moon on this card). The normal draw is replaced as it
        /// activates (Lua s.target sets the draw count to 0, so a negated or fizzled search still costs the draw); the card
        /// chosen as it activated is added when it resolves while this card is still face-up on the field (Lua
        /// s.operation). The Draw Phase goes on once the Chain has resolved. False: no response is possible — resolve now.
        /// </summary>
        static bool TrySeatSearch(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            var seat = new TriggerChainResponsesRuntime.Seat
            {
                Controller = who,
                Card = source,
                From = Rules.CardLocation.MonsterZone,
                Resolve = () =>
                {
                    if (!Active(engine, who, source))
                    {
                        engine.Log($"{source.Name} is no longer face-up on the field — nothing is added.");
                        return;
                    }

                    Search(engine, who, source, pick);
                }
            };
            var st = StateOf(engine);
            var before = st.DrawReplacedTurn;
            st.DrawReplacedTurn = engine.TurnNumber;
            if (TriggerChainResponsesRuntime.TrySeat(engine, new[] { seat }, () => engine.ContinueDrawPhase(who)))
                return true;
            st.DrawReplacedTurn = before;
            return false;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the effect is not activated — the normal draw happens.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_searches.TryGetValue(pending, out var search)) return false;
            s_searches.Remove(pending);
            engine.ClearPendingActivation();
            engine.Log($"{search.Source?.Name}: not activated — {search.Who.Name} draws normally.");
            engine.ContinueDrawPhase(search.Who);
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── battle: negate / banish ─────────────────────────────

        /// <summary>
        /// DuelEngine, right after damage calculation and before any after-damage effect or battle destruction (ygopro
        /// EVENT_BATTLED): Ha Des / Twin-Headed Wolf negate the monsters this battle destroys, and the battle whose
        /// destruction Lesser Fiend may redirect is recorded.
        /// </summary>
        public static void OnBattled(DuelEngine engine, DuelistState atkPlayer, DuelistState defPlayer,
            CardInstance attacker, CardInstance defender, bool destroyAttacker, bool destroyDefender)
        {
            if (engine == null) return;
            var st = StateOf(engine);
            st.Attacker = attacker;
            st.Defender = defender;
            st.DestroyedThisBattle.Clear();
            if (attacker == null || defender == null || atkPlayer == null || defPlayer == null) return;
            Battled(engine, st, atkPlayer, attacker, defPlayer, defender, destroyDefender);
            Battled(engine, st, defPlayer, defender, atkPlayer, attacker, destroyAttacker);
        }

        /// <summary><paramref name="battler"/> (controlled by <paramref name="side"/>) battled <paramref name="other"/>.</summary>
        static void Battled(DuelEngine engine, State st, DuelistState side, CardInstance battler, DuelistState otherSide,
            CardInstance other, bool otherDestroyed)
        {
            if (!otherDestroyed || other?.Def == null || !otherSide.TryFindMonster(other, out _)) return;
            if (!side.TryFindMonster(battler, out _)) return;

            // Negated now, even when Lesser Fiend banishes it later: the negation (RESETS_STANDARD_EXC_GRAVE) ends once it
            // is banished (BanishesBattleDestroyed), and the opposing Ha Des may negate Lesser Fiend first.
            var negator = BattleNegator(engine, side, battler, other);
            if (negator == null) return;
            other.EffectsNegatedAfterBattle = true;
            engine.Log($"{negator.Name}: the effects of {other.Name} are negated (destroyed by battle with {battler.Name}).");
        }

        /// <summary>The face-up card whose effect negates <paramref name="other"/>, destroyed by battle with <paramref name="battler"/>.</summary>
        static CardInstance BattleNegator(DuelEngine engine, DuelistState side, CardInstance battler, CardInstance other)
        {
            if (battler?.Def == null || !battler.FaceUp) return null;
            foreach (var m in side.MonstersOnField().ToList())
            {
                if (!Active(engine, side, m)) continue;
                foreach (var c in Markers(m, EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle))
                {
                    if (c.AffectsThisCardOnly)
                    {
                        // Twin-Headed Wolf: this card battled; the destroyed monster is of the printed card type; while
                        // you control another face-up monster of the named Type.
                        if (!ReferenceEquals(m, battler)) continue;
                        if (!string.IsNullOrEmpty(c.TargetCardKind) &&
                            (other.Def.type ?? "").IndexOf(c.TargetCardKind, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (!string.IsNullOrEmpty(c.CountRaceFilter) &&
                            !side.MonstersOnField().Any(o => o != null && o.FaceUp &&
                                                             (!c.ExceptThisCard || !ReferenceEquals(o, m)) &&
                                                             ClassicEraTemplates.RaceMatches(o.Race, c.CountRaceFilter)))
                            continue;
                        return m;
                    }

                    // Dark Ruler Ha Des: the battling monster you control is of the named Type.
                    if (!string.IsNullOrEmpty(c.RaceFilter) && !ClassicEraTemplates.RaceMatches(battler.Race, c.RaceFilter))
                        continue;
                    return m;
                }
            }

            return null;
        }

        /// <summary>
        /// DuelEngine.DestroyMonster (destroyed by battle): Lesser Fiend banishes <paramref name="card"/> instead of sending
        /// it to the GY. ygopro reads EFFECT_BATTLE_DESTROY_REDIRECT from the destroyer as the destruction happens, so the
        /// redirect applies only while Lesser Fiend's effect does: it is face-up on the field and not negated (Ha Des
        /// negated it at EVENT_BATTLED; Man-Eater Bug's FLIP destroyed it before the deferred battle destruction), or it
        /// was destroyed by this same battle at the same time (the engine destroys the two battlers one after the other).
        /// </summary>
        public static bool BanishesBattleDestroyed(DuelEngine engine, CardInstance card, CardInstance battleDestroyer)
        {
            if (engine == null || card == null || battleDestroyer?.Def == null) return false;
            var st = StateOf(engine);
            var sameBattle = (ReferenceEquals(card, st.Attacker) && ReferenceEquals(battleDestroyer, st.Defender)) ||
                             (ReferenceEquals(card, st.Defender) && ReferenceEquals(battleDestroyer, st.Attacker));
            if (!sameBattle) return false;
            st.DestroyedThisBattle.Add(card);
            if (!Markers(battleDestroyer, EffectActionKind.BanishMonstersDestroyedByBattleWithThis).Any()) return false;

            var side = engine.ControllerOf(battleDestroyer);
            var applies = side != null && side.TryFindMonster(battleDestroyer, out _)
                ? Active(engine, side, battleDestroyer)
                : st.DestroyedThisBattle.Contains(battleDestroyer) && !battleDestroyer.IsNegated;
            if (!applies) return false;

            // Banished: a Ha Des / Twin-Headed Wolf negation ends (RESETS_STANDARD_EXC_GRAVE).
            card.EffectsNegatedAfterBattle = false;
            engine.Log($"{battleDestroyer.Name}: {card.Name} is banished instead of going to the GY.");
            return true;
        }

        /// <summary>
        /// DuelEngine's battle FLIP step: the FLIP effect (and "flipped face-up" effect) of a monster this battle flipped
        /// does not activate when Ha Des / Twin-Headed Wolf negated it.
        /// </summary>
        public static bool SuppressesBattleFlip(DuelEngine engine, CardInstance flipped)
        {
            if (flipped == null || !flipped.EffectsNegatedAfterBattle) return false;
            engine?.Log($"{flipped.Name}: its effects are negated — its FLIP effect does not activate.");
            return true;
        }

        /// <summary>
        /// <paramref name="card"/> is in a GY with its effects negated by Ha Des / Twin-Headed Wolf (until it leaves the GY):
        /// its "sent to the GY" triggers and GY effects do not apply.
        /// </summary>
        public static bool NegatedInGy(DuelEngine engine, CardInstance card)
        {
            if (engine?.Player == null || card == null || !card.EffectsNegatedAfterBattle) return false;
            return engine.Player.Graveyard.Contains(card) || (engine.Opponent?.Graveyard.Contains(card) ?? false);
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: a negated card that left the GY for a hand or a banished pile is no longer
        /// negated (a card that returns to the field is reset by DuelEngine.ResetOnFieldEntry).
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                foreach (var c in side.Hand)
                    if (c != null) c.EffectsNegatedAfterBattle = false;
                foreach (var c in side.Banished)
                    if (c != null) c.EffectsNegatedAfterBattle = false;
            }
        }

        // ───────────────────────────── Double Snare ─────────────────────────────

        /// <summary>
        /// Double Snare's target class (ygopro aux.DoubleSnareValidity, EFFECT_FLAG_CANNOT_DISABLE): the card has an effect
        /// that negates Trap effects — read from its compiled clause kinds (Jinzo / Royal Decree, Sorcerer of Dark Magic,
        /// Tyrant Dragon's target negation, The Dragon's Bead). A negated copy still has the effect.
        /// </summary>
        public static bool NegatesTrapEffects(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null) return false;
            if (FaceUpQuickEffectsRuntime.NegatesTrapEffects(prog)) return true;
            return prog.ClauseList.Exists(c => c != null &&
                (c.Action == EffectActionKind.ContinuousNegateTrapEffectsOnField ||
                 c.Action == EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt ||
                 (c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy && c.NegateRespondsToTrap) ||
                 (c.Action == EffectActionKind.NegateActivation && c.NegateRespondsToTrap && card.Def.IsMonster)));
        }

        /// <summary>CollectTargets (FieldFaceUpCardsNegatingTrapEffects): every such face-up card on either field.</summary>
        public static void CollectDoubleSnareTargets(DuelEngine engine, DuelistState who, DuelistState opp,
            CardInstance except, List<CardInstance> list)
        {
            foreach (var side in new[] { who, opp })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null && m != except && m.FaceUp && NegatesTrapEffects(m)) list.Add(m);
                // A monster Card in a Spell & Trap Zone (equipped by Sword Hunter, a Union, Kiseitai …) has no Monster Zone
                // effect there (aux.DoubleSnareValidity is ranged LOCATION_MZONE for Jinzo): not a target.
                foreach (var s in side.SpellTrapsOnField())
                    if (s != null && s != except && s.FaceUp && s.Def?.IsMonster != true && NegatesTrapEffects(s)) list.Add(s);
                var field = side.FieldSpellZone?.Occupant;
                if (field != null && field != except && field.FaceUp && NegatesTrapEffects(field)) list.Add(field);
            }
        }

        /// <summary>AutoPick (FieldFaceUpCardsNegatingTrapEffects): the opponent's card first (a monster with the most ATK).</summary>
        public static CardInstance AutoPickDoubleSnare(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            var opp = engine.OpponentOf(who);
            bool Theirs(CardInstance c) => engine.ControllerOf(c) == opp;
            return targets.Where(Theirs).OrderByDescending(c => c.Def != null && c.Def.IsMonster ? c.CurrentAtk : -1)
                       .FirstOrDefault()
                   ?? targets[0];
        }
    }
}
