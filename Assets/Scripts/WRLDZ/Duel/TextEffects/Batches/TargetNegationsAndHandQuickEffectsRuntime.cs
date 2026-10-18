using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TargetNegationsAndHandQuickEffects" (v163) runtime.
    /// <list type="bullet">
    /// <item>Curse of Royal (c2926176): a Counter Trap answering the activation of a Spell/Trap Card that targets exactly 1
    /// card, a Spell/Trap on the field, and destroys it (<see cref="TargetsOneSpellTrapToDestroy"/>, its activation
    /// condition <see cref="LinkTargetsOneSpellTrapToDestroy"/>). The shared Counter Trap path seats and resolves it.</item>
    /// <item>Gravekeeper's Watcher (c26084285): a monster Quick Effect activated from the hand in a Chain window
    /// (<see cref="IsHandChainAnswer"/>; DuelEngine.TryActivateChainResponse pays <see cref="PayHandAnswerCost"/>, then the
    /// shared negation link negates and destroys). It answers the opponent's activation of a Spell/Trap Card, or a monster
    /// effect, whose resolution makes its activating player (or both players) discard
    /// (<see cref="LinkMakesActivatorDiscard"/>).</item>
    /// <item>Charm of Shabti (c50412166): a free-chain Quick Effect from the hand (<see cref="EffectTiming.HandQuickEffect"/>,
    /// <see cref="CanActivateHandQuick"/>, <see cref="TryActivate"/>, <see cref="TryResolveLink"/>): discard it (cost); until
    /// the End Phase, "Gravekeeper's" monsters its player controls cannot be destroyed by battle
    /// (<see cref="BattleIndestructible"/>, asked by both damage calculations).</item>
    /// <item>Great Dezard (c88989706) / Fushioh Richie (c39711336): a mandatory Quick Effect (ygopro QUICK_F on
    /// EVENT_CHAINING) seated as a real Chain Link right after a Spell/Trap Card's activation that targets the face-up
    /// monster (<see cref="TrySeatForcedNegations"/>, from DuelEngine.OpenChainResponseWindow); Dezard's needs 1 monster
    /// destroyed by battle (<see cref="NoteBattleDestroying"/>), its ignition (Tribute it: Special Summon "Fushioh Richie"
    /// from the hand or Deck, which completes Richie's procedure) needs 2.</item>
    /// </list>
    /// </summary>
    public static class TargetNegationsAndHandQuickEffectsRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> of Curse of Royal: the last Chain Link is a Spell/Trap Card's
        /// activation that targets exactly 1 card, a Spell/Trap on the field, and destroys 1 card (<see cref="TargetsOneSpellTrapToDestroy"/>).
        /// </summary>
        public const string LinkTargetsOneSpellTrapToDestroy = "TargetNegations.LinkTargetsOneSpellTrapToDestroy";

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> of Gravekeeper's Watcher: the last Chain Link is the opponent's,
        /// and its resolution would make them (or both players) discard (<see cref="LinkMakesActivatorDiscard"/>).
        /// </summary>
        public const string OpponentLinkMakesActivatorDiscard = "TargetNegations.OpponentLinkMakesActivatorDiscard";

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> of Great Dezard's ignition: this card has destroyed at least
        /// <see cref="EffectClause.ConditionAmount"/> monsters by battle since it was last face-up on the field.
        /// </summary>
        public const string ThisDestroyedMonstersByBattle = "TargetNegations.ThisDestroyedMonstersByBattle";

        /// <summary><see cref="ChainLink.EffectKey"/> of a mandatory negation link (Great Dezard, Fushioh Richie).</summary>
        public const string ForcedNegationLinkKey = "TargetNegations.ForcedNegation";

        /// <summary><see cref="ChainLink.EffectKey"/> of a hand Quick Effect link (Charm of Shabti).</summary>
        public const string HandQuickLinkKey = "TargetNegations.HandQuickEffect";

        // ───────────────────────────── state ─────────────────────────────

        /// <summary>Monsters destroyed by battle during one face-up field presence (ygopro flag effect, RESETS_STANDARD).</summary>
        sealed class BattleCount
        {
            public int EntryStamp;
            public int FaceDownStamp;
            public int Count;
        }

        static readonly ConditionalWeakTable<CardInstance, BattleCount> s_kills = new();

        /// <summary>A Chain Link the mandatory negations already answered (they activate once per activation).</summary>
        static readonly ConditionalWeakTable<ChainLink, object> s_answered = new();

        /// <summary>The clause each hand Quick Effect link resolves.</summary>
        static readonly ConditionalWeakTable<ChainLink, EffectClause> s_handLinks = new();

        sealed class Grant
        {
            public DuelistState Player;
            public int Turn;
            public string Series;
        }

        sealed class EngineState
        {
            /// <summary>Charm of Shabti: battle protections registered to a player until the End Phase.</summary>
            public readonly List<Grant> Grants = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();

        static EngineState StateOf(DuelEngine engine) => s_state.GetOrCreateValue(engine);

        // ───────────────────────────── shared link reading ─────────────────────────────

        /// <summary>
        /// <paramref name="link"/> is the activation of a Spell/Trap Card (ygopro EFFECT_TYPE_ACTIVATE) from the hand or
        /// Set, not an effect of a card already face-up (Wave-Motion Cannon's ignition, a face-up Quick Effect).
        /// </summary>
        static bool IsCardActivationLink(ChainLink link)
        {
            var def = link?.Card?.Def;
            if (def == null || !(def.IsSpell || def.IsTrap) || link.Resolved || link.Negated) return false;
            if (FaceUpQuickEffectsRuntime.IsQuickEffectLink(link) || PerCountBurnsRuntime.IsCardEffectLink(link))
                return false;
            return link.FromHand || link.WasSetOnField;
        }

        /// <summary>The cards <paramref name="link"/> targets (ygopro CHAININFO_TARGET_CARDS), each once.</summary>
        static List<CardInstance> LinkTargets(ChainLink link)
        {
            var list = new List<CardInstance>();
            if (link == null) return list;
            if (link.Target != null) list.Add(link.Target);
            foreach (var t in link.Targets)
                if (t != null && !list.Contains(t))
                    list.Add(t);
            return list;
        }

        /// <summary>ygopro Duel.IsChainNegatable for a card's activation: none of its Activate clauses says it cannot be negated.</summary>
        static bool ActivationNegatable(ChainLink link)
        {
            var prog = link?.Card?.Def != null ? CompiledEffectCache.GetOrCompile(link.Card.Def) : null;
            if (prog == null || !prog.FullyCompiled) return true;
            return !prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null && !c.ActivationNegatable);
        }

        static bool IsSpellTrapOnField(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindSpellTrap(card, out _)) return true;
                if (side.FieldSpellZone != null && ReferenceEquals(side.FieldSpellZone.Occupant, card)) return true;
            }

            return false;
        }

        // ───────────────────────────── Curse of Royal ─────────────────────────────

        /// <summary>
        /// Curse of Royal (Lua s.condition) on <paramref name="link"/>: the activation of a Spell/Trap Card with
        /// EFFECT_FLAG_CARD_TARGET whose target is exactly 1 card, that card is a Spell/Trap on the field, the activation can
        /// be negated, and its effect destroys exactly 1 card — the one it targets (CATEGORY_DESTROY, count 1): exactly one
        /// targeting Destroy clause (1 target) and no other Destroy clause. A Spell that destroys a monster (Ring of
        /// Destruction), or several cards (Harpie's Feather Duster), does not qualify; Raigeki Break does when it targets a
        /// Spell/Trap.
        /// </summary>
        public static bool TargetsOneSpellTrapToDestroy(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsCardActivationLink(link) || !ActivationNegatable(link)) return false;
            var targets = LinkTargets(link);
            if (targets.Count != 1 || !IsSpellTrapOnField(engine, targets[0])) return false;
            var prog = CompiledEffectCache.GetOrCompile(link.Card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var destroys = 0;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
            {
                if (c == null || EffectVocabulary.ResolutionOf(c) != EffectResolutionKind.Destroy) continue;
                if (!TargetingTruthRuntime.PicksTarget(c)) return false;
                if (c.TargetUpTo || c.RequiresSecondTarget || c.DistinctTargetGroup || c.TargetCount > 1 ||
                    c.ControllerTargetCount > 1 || c.OpponentTargetCount > 0)
                    return false;
                destroys++;
            }

            return destroys == 1;
        }

        // ───────────────────────────── Gravekeeper's Watcher ─────────────────────────────

        /// <summary>
        /// The clause's resolution makes its activating player discard (ygopro CATEGORY_HANDES with p == tp or PLAYER_ALL):
        /// Card Destruction / Morphing Jar (both players), Graceful Charity (its controller). An action not listed here
        /// fails closed (the Watcher cannot answer it).
        /// </summary>
        static bool ClauseMakesActivatorDiscard(EffectClause c) =>
            c != null &&
            (c.Action == EffectActionKind.BothPlayersDiscardAndRedraw ||
             (c.Action == EffectActionKind.DrawThenDiscard && c.Side == EffectSide.Controller));

        /// <summary>
        /// Gravekeeper's Watcher (Lua s.condition) on <paramref name="link"/>: a Spell/Trap Card's activation (its Activate
        /// clauses) or a monster effect (every chain-making clause of the monster — the link must be unambiguous) whose
        /// resolution makes the link's controller (or both players) discard, and which can be negated.
        /// </summary>
        public static bool LinkMakesActivatorDiscard(DuelEngine engine, ChainLink link)
        {
            var def = link?.Card?.Def;
            if (engine == null || def == null || link.Resolved || link.Negated) return false;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled) return false;
            List<EffectClause> effect;
            if (def.IsSpell || def.IsTrap)
            {
                if (!IsCardActivationLink(link)) return false;
                effect = prog.ClausesFor(EffectTiming.Activate);
            }
            else if (def.IsMonster)
            {
                if (FaceUpQuickEffectsRuntime.IsQuickEffectLink(link)) return false;
                effect = prog.ClauseList.Where(c => c != null && c.MakesChainLink &&
                                                     c.Timing != EffectTiming.ContinuousWhileFaceUp).ToList();
                if (effect.Count == 0 || !effect.TrueForAll(ClauseMakesActivatorDiscard)) return false;
            }
            else
                return false;

            if (effect.Exists(c => c != null && !c.ActivationNegatable)) return false;
            return effect.Exists(ClauseMakesActivatorDiscard);
        }

        /// <summary>A Chain answer activated from the hand (Gravekeeper's Watcher).</summary>
        public static bool IsHandChainAnswer(EffectClause c) =>
            c != null && c.Timing == EffectTiming.ChainLinkActivated && c.ActivatesFromHand &&
            c.Action == EffectActionKind.NegateActivation;

        /// <summary>
        /// TextEffectRuntime.CanActivateChainResponse hook. False for anything that is not a hand Chain answer. For one:
        /// <paramref name="ok"/> = the monster is in <paramref name="who"/>'s hand, the answered link is the opponent's and
        /// meets the Watcher's condition, and a Spell Speed 2 link can be added.
        /// </summary>
        public static bool TryCanAnswerFromHand(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause,
            ChainLink target, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (!IsHandChainAnswer(clause)) return false;
            // LocksRedirectsAndActivity batch: Goblin of Greed — no discard cost.
            if (LocksRedirectsAndActivityRuntime.DiscardCostLocked(engine, who, clause, out reason)) return true;
            if (engine == null || who?.Hand == null || card?.Def == null || !card.Def.IsMonster || !who.Hand.Contains(card))
            {
                reason = "Only from your hand.";
                return true;
            }

            if (target == null || target.Controller == null || target.Controller == who)
            {
                reason = "Only when your opponent activates it.";
                return true;
            }

            if (clause.ActivationCondition == OpponentLinkMakesActivatorDiscard &&
                !LinkMakesActivatorDiscard(engine, target))
            {
                reason = "Only against a card or effect that could make your opponent discard.";
                return true;
            }

            if (!engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick))
            {
                reason = "A Spell Speed 2 effect cannot be chained to that link.";
                return true;
            }

            ok = true;
            reason = "OK";
            return true;
        }

        /// <summary>DuelEngine.TryActivateChainResponse: "send this card from your hand to the GY" (a cost, not a discard).</summary>
        public static bool PayHandAnswerCost(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || who == null || card == null || clause == null) return false;
            if (!clause.RequiresSendThisToGy) return true;
            var gone = MonsterEffects.SendFromHandToGraveByInstance(who, card);
            if (gone == null) return false;
            engine.Log($"Cost: send {gone.Name} from the hand to the GY.");
            return true;
        }

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain: this batch's conditions.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case LinkTargetsOneSpellTrapToDestroy:
                {
                    var pr = engine?.PendingResponse;
                    ok = pr != null && pr.Timing == ResponseTiming.ChainResponse &&
                         TargetsOneSpellTrapToDestroy(engine, pr.ChainTarget);
                    reason = "Only when a Spell/Trap Card that targets and destroys 1 Spell/Trap Card is activated.";
                    return true;
                }
                case OpponentLinkMakesActivatorDiscard:
                {
                    var pr = engine?.PendingResponse;
                    var t = pr?.ChainTarget;
                    ok = pr != null && pr.Timing == ResponseTiming.ChainResponse && t != null && t.Controller != who &&
                         LinkMakesActivatorDiscard(engine, t);
                    reason = "Only when your opponent activates a card or effect that could make them discard.";
                    return true;
                }
                case ThisDestroyedMonstersByBattle:
                {
                    var need = Math.Max(1, c.ConditionAmount);
                    ok = BattleDestructionsOf(card) >= need;
                    reason = $"Only after this card has destroyed {need} monsters by battle.";
                    return true;
                }
                default:
                    return false;
            }
        }

        // ───────────────────────────── Great Dezard: battle count ─────────────────────────────

        static bool CountsBattleDestructions(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null &&
                ((c.Action == EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy && c.ConditionAmount > 0) ||
                 c.ActivationCondition == ThisDestroyedMonstersByBattle));

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle (ygopro EVENT_BATTLE_DESTROYING while it is still related to
        /// the battle): <paramref name="destroyer"/> destroyed a monster by battle. Counted only for a card whose text counts.
        /// </summary>
        public static void NoteBattleDestroying(DuelEngine engine, CardInstance destroyer)
        {
            if (engine == null || destroyer?.Def == null) return;
            if (!CountsBattleDestructions(CompiledEffectCache.GetOrCompile(destroyer.Def))) return;
            var k = s_kills.GetOrCreateValue(destroyer);
            if (k.EntryStamp != destroyer.FieldEntryStamp || k.FaceDownStamp != destroyer.FaceDownStamp)
            {
                k.EntryStamp = destroyer.FieldEntryStamp;
                k.FaceDownStamp = destroyer.FaceDownStamp;
                k.Count = 0;
            }

            k.Count++;
            engine.Log($"{destroyer.Name} has destroyed {k.Count} monster(s) by battle.");
        }

        /// <summary>
        /// Monsters <paramref name="card"/> destroyed by battle during its current field presence (0 once it left the field or
        /// was turned face-down: RESETS_STANDARD).
        /// </summary>
        public static int BattleDestructionsOf(CardInstance card)
        {
            if (card == null || !s_kills.TryGetValue(card, out var k)) return 0;
            return k.EntryStamp == card.FieldEntryStamp && k.FaceDownStamp == card.FaceDownStamp ? k.Count : 0;
        }

        // ───────────────────────────── Great Dezard / Fushioh Richie: mandatory negation ─────────────────────────────

        static EffectClause ForcedNegationClause(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster?.Def == null || !monster.FaceUp || monster.IsNegated) return null;
            if (ContinuousNegationRuntime.NegatedBy(engine, monster) != null) return null;
            var prog = CompiledEffectCache.GetOrCompile(monster.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).FirstOrDefault(c =>
                c != null && c.Action == EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy);
            if (clause == null) return null;
            return BattleDestructionsOf(monster) >= clause.ConditionAmount ? clause : null;
        }

        /// <summary>The face-up monster has a live mandatory negation now (Dezard: with its battle count reached).</summary>
        public static bool HasLiveForcedNegation(DuelEngine engine, CardInstance monster) =>
            ForcedNegationClause(engine, monster) != null;

        /// <summary>
        /// DuelEngine.OpenChainResponseWindow, for a new last Chain Link (ygopro EVENT_CHAINING): when it is a Spell/Trap
        /// Card's activation that targets a face-up monster with a live mandatory negation (either player's card, either
        /// player's monster), each such monster's effect becomes the next Chain Link — before anyone may respond — and
        /// then the opponent of its controller may respond to it. True when links were added; <paramref name="windowOpened"/>
        /// is what the response window returned (false: nobody responds, the caller resolves the Chain).
        /// </summary>
        public static bool TrySeatForcedNegations(DuelEngine engine, out bool windowOpened)
        {
            windowOpened = false;
            var answered = engine?.Chain?.Last;
            if (answered == null || engine.Chain.IsResolving || !IsCardActivationLink(answered)) return false;
            if (s_answered.TryGetValue(answered, out _)) return false;
            var seats = new List<CardInstance>();
            foreach (var t in LinkTargets(answered))
            {
                var ctrl = engine.ControllerOf(t);
                if (ctrl == null || !ctrl.TryFindMonster(t, out _)) continue;
                if (ForcedNegationClause(engine, t) != null) seats.Add(t);
            }

            if (seats.Count == 0) return false;
            s_answered.Add(answered, new object());
            // Duel.NegateActivation fails on an activation that cannot be negated: nothing to do.
            if (!ActivationNegatable(answered)) return false;
            ChainLink last = null;
            foreach (var m in seats)
            {
                var who = engine.ControllerOf(m);
                if (who == null || !engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick)) break;
                var link = engine.Chain.AddLink(who, m, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.MonsterZone,
                    fromHand: false, wasSet: false, effectKey: ForcedNegationLinkKey);
                if (link == null) break;
                link.TargetLink = answered;
                link.NegatesActivation = true;
                link.DestroyNegatedCard = true;
                engine.Log($"{m.Name}'s effect activates (Chain Link {link.LinkNumber}): negate the activation of " +
                           $"{answered.Card.Name}, which targets it, and destroy it.");
                last = link;
            }

            if (last == null) return false;
            windowOpened = engine.OpenChainResponseWindow(engine.OpponentOf(last.Controller));
            return true;
        }

        // ───────────────────────────── Charm of Shabti: hand Quick Effect ─────────────────────────────

        static EffectClause HandQuickClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.HandQuickEffect).FirstOrDefault(c => c != null);

        /// <summary><paramref name="card"/> is a monster in <paramref name="who"/>'s hand with a hand Quick Effect.</summary>
        public static bool IsHandQuickCard(DuelistState who, CardInstance card, bool fromHand, CompiledCardProgram prog) =>
            fromHand && who?.Hand != null && card?.Def != null && card.Def.IsMonster && who.Hand.Contains(card) &&
            prog != null && prog.FullyCompiled && prog.HasTiming(EffectTiming.HandQuickEffect);

        /// <summary>
        /// The hand Quick Effect can be activated now by <paramref name="who"/> (Lua: not Main Phase 2 or the End Phase; no
        /// EFFECT_FLAG_DAMAGE_STEP). Reachable timings: the turn player's own Main Phase 1 (the open state), and the
        /// windows the engine opens — an opponent's open-state window (start of Main Phase 1 / the Battle Phase), an attack
        /// declaration (the Battle Step) and a Chain window (Spell Speed 2) — on either turn.
        /// </summary>
        public static bool CanActivateHandQuick(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            reason = null;
            var clause = HandQuickClause(prog);
            if (engine == null || engine.GameOver || clause == null || !IsHandQuickCard(who, card, true, prog))
            {
                reason = "Not a hand Quick Effect.";
                return false;
            }

            // LocksRedirectsAndActivity batch: Goblin of Greed — no "discard this card" cost.
            if (LocksRedirectsAndActivityRuntime.DiscardCostLocked(engine, who, clause, out reason)) return false;

            if (engine.PendingActivation != null)
            {
                reason = "Finish the current choice first.";
                return false;
            }

            if (engine.Phase == DuelPhase.Main2 || engine.Phase == DuelPhase.End)
            {
                reason = "Not during Main Phase 2 or the End Phase.";
                return false;
            }

            if (engine.DamageSubStep != DamageSubStep.None)
            {
                reason = "Not during the Damage Step.";
                return false;
            }

            var pr = engine.PendingResponse;
            if (pr != null)
            {
                if (pr.Responder != who)
                {
                    reason = "Not your response.";
                    return false;
                }

                switch (pr.Timing)
                {
                    case ResponseTiming.ChainResponse:
                        if (pr.ChainAnswersOnly)
                        {
                            reason = "Only a chain answer may be activated now.";
                            return false;
                        }

                        if (!engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick))
                        {
                            reason = "A Spell Speed 2 effect cannot be chained to that link.";
                            return false;
                        }

                        break;
                    case ResponseTiming.AttackDeclared:
                    case ResponseTiming.OpponentOpenState:
                        break;
                    default:
                        reason = "Not in this window.";
                        return false;
                }
            }
            else
            {
                if (engine.TurnPlayer != who || engine.Phase != DuelPhase.Main1 || engine.HasDeclaredAttack ||
                    engine.IsResolvingChain || engine.Chain.HasLinks && engine.Chain.Last?.Card != card)
                {
                    reason = "Activate it in your Main Phase 1, or when a window opens.";
                    return false;
                }
            }

            if (clause.RequiresDiscardSelf && !who.Hand.Contains(card))
            {
                reason = "Discard this card from your hand.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.BeginOrResolveManual hook. False for anything that is not a hand Quick Effect. For one: seats
        /// its Spell Speed 2 Chain Link (the link DuelEngine.TryActivateSpellTrap opened for it, or a new one), discards
        /// the card (cost), then the opponent may respond; the effect applies when the link resolves.
        /// </summary>
        public static bool TryActivate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick, out bool ok)
        {
            ok = false;
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsHandQuickCard(who, card, fromHand, prog)) return false;
            var link = engine.Chain.Last;
            var ownManualLink = link != null && link.Card == card && !link.Resolved;
            if (!CanActivateHandQuick(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                if (ownManualLink) engine.Chain.RemoveUnresolved(link);
                return true;
            }

            var clause = HandQuickClause(prog);
            var pr = engine.PendingResponse;
            var timing = pr?.Timing ?? ResponseTiming.None;
            var answered = timing == ResponseTiming.ChainResponse ? pr.ChainTarget : null;
            if (timing == ResponseTiming.ChainResponse || !ownManualLink)
            {
                link = engine.Chain.AddLink(who, card, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.Hand,
                    fromHand: true, wasSet: false, effectKey: HandQuickLinkKey);
                if (link == null)
                {
                    engine.Log($"{card.Name}: its effect cannot be activated now.");
                    return true;
                }
            }

            link.EffectKey = HandQuickLinkKey;
            link.Speed = SpellSpeed.Speed2;
            link.Class = EffectClass.Quick;
            link.FromLocation = CardLocation.Hand;
            link.FromHand = true;
            link.WasSetOnField = false;
            if (answered != null) link.TargetLink = answered;

            if (clause.RequiresDiscardSelf)
            {
                var gone = MonsterEffects.DiscardFromHandByInstance(who, card, DiscardCause.Cost, who);
                if (gone == null)
                {
                    engine.Chain.RemoveUnresolved(link);
                    engine.Log($"{card.Name}: it could not be discarded.");
                    return true;
                }

                engine.Log($"Cost: discard {gone.Name}.");
            }

            s_handLinks.Remove(link);
            s_handLinks.Add(link, clause);
            ok = true;
            engine.Log($"Activate: {card.Name} (Quick Effect) — Chain Link {link.LinkNumber}.");
            if (timing == ResponseTiming.None || timing == ResponseTiming.ChainResponse)
            {
                engine.ClearPendingResponse();
                if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }

                return true;
            }

            TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, timing);
            return true;
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack hook. A hand Quick Effect link applies its effect (negated: nothing; its card is
        /// already in the GY). A mandatory negation link that is no longer Chain Link ev+1 of the activation it answers does
        /// nothing (Lua: Duel.GetCurrentChain()~=ev+1); otherwise false, and the shared negation applies.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null) return false;
            if (link.EffectKey == HandQuickLinkKey)
            {
                if (link.Negated)
                {
                    engine.Log($"{link.Card?.Name ?? "Effect"}: its effect is negated.");
                    return true;
                }

                s_handLinks.TryGetValue(link, out var clause);
                if (clause?.Action == EffectActionKind.SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase)
                    RegisterBattleProtection(engine, link.Controller, link.Card, clause);
                return true;
            }

            if (link.EffectKey == ForcedNegationLinkKey && !link.Negated &&
                (link.TargetLink == null || link.LinkNumber != link.TargetLink.LinkNumber + 1))
            {
                engine.Log($"{link.Card?.Name ?? "Effect"}: it is not the Chain Link right after that activation — " +
                           "nothing happens.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// The SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase effect (also the TextEffectRuntime.ApplyClause arm):
        /// registered to <paramref name="who"/> for this turn.
        /// </summary>
        public static void RegisterBattleProtection(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null || string.IsNullOrEmpty(clause.NamedCard)) return;
            StateOf(engine).Grants.Add(new Grant { Player = who, Turn = engine.TurnNumber, Series = clause.NamedCard });
            engine.Log($"{source?.Name ?? "Effect"}: until the End Phase, \"{clause.NamedCard}\" monsters {who.Name} " +
                       "controls cannot be destroyed by battle.");
        }

        /// <summary>
        /// Both damage calculations (DuelEngine, TextEffectRuntime): <paramref name="card"/> is a monster its controller's
        /// registered protection covers this turn (Charm of Shabti). Not the monster's own effect: a negated monster is
        /// still covered.
        /// </summary>
        public static bool BattleIndestructible(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !s_state.TryGetValue(engine, out var st) || st.Grants.Count == 0)
                return false;
            var ctrl = engine.ControllerOf(card);
            if (ctrl == null || !ctrl.TryFindMonster(card, out _)) return false;
            foreach (var g in st.Grants)
                if (g.Player == ctrl && g.Turn == engine.TurnNumber &&
                    TextEffectRuntime.CardMatchesSeries(card.Def, g.Series))
                    return true;
            return false;
        }

        // ───────────────────────────── windows and the AI ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.CollectLegalResponseCards: this batch's hand cards legal in the window being opened — a hand
        /// Quick Effect (Charm of Shabti) in the windows it allows, a hand Chain answer (Gravekeeper's Watcher) in a Chain
        /// window.
        /// </summary>
        public static void CollectHandResponses(DuelEngine engine, DuelistState who, ResponseTiming timing,
            List<CardInstance> list)
        {
            if (engine == null || who?.Hand == null || list == null) return;
            foreach (var h in who.Hand.ToList())
            {
                if (h?.Def == null || !h.Def.IsMonster || list.Contains(h)) continue;
                var prog = CompiledEffectCache.GetOrCompile(h.Def);
                if (prog == null || !prog.FullyCompiled || !OfficialEffectRegistry.ProgramMayActivate(h.Def)) continue;
                if (prog.HasTiming(EffectTiming.HandQuickEffect))
                {
                    if (CanActivateHandQuick(engine, who, h, prog, out _)) list.Add(h);
                    continue;
                }

                if (timing == ResponseTiming.ChainResponse &&
                    prog.ClausesFor(EffectTiming.ChainLinkActivated).Exists(IsHandChainAnswer) &&
                    TextEffectRuntime.CanActivate(engine, who, h, true, prog, out _))
                    list.Add(h);
            }
        }

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond: the AI discards Charm of Shabti when the opponent attacks one of its monsters of
        /// that series that the battle would destroy and nothing protects yet.
        /// </summary>
        public static bool AiWantsHandQuick(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsHandQuickCard(ai, card, true, prog)) return false;
            var clause = HandQuickClause(prog);
            var pr = engine.PendingResponse;
            if (clause?.Action != EffectActionKind.SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase ||
                pr == null || pr.Timing != ResponseTiming.AttackDeclared || pr.Responder != ai)
                return false;
            var attacker = pr.Attacker;
            var target = pr.AttackTarget;
            if (attacker == null || target?.Def == null || !ai.TryFindMonster(target, out _)) return false;
            if (!TextEffectRuntime.CardMatchesSeries(target.Def, clause.NamedCard)) return false;
            if (BattleIndestructible(engine, target) || OfficialEffectRegistry.CannotBeDestroyedByBattle(target))
                return false;
            return target.Position == BattlePosition.Attack
                ? attacker.CurrentAtk >= target.CurrentAtk
                : attacker.CurrentAtk > target.CurrentDef;
        }
    }
}
