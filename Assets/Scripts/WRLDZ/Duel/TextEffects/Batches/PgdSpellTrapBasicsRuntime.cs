using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// PgdSpellTrapBasics batch (v162 PGD): runtime of <see cref="PgdSpellTrapBasicsAtoms"/>.
    /// <list type="bullet">
    /// <item>Book of Taiyou: flip a face-down target to face-up Attack; its FLIP effect waits for the Chain
    /// (<see cref="FlipTargetFaceUpAttack"/>).</item>
    /// <item>Curse of Aging / Pyramid Energy: every face-up monster a side controls at resolution gains / loses ATK and DEF
    /// until the End Phase (<see cref="ModifyAllFaceUp"/>; the existing <see cref="CardInstance.UntilEndOfTurnAtk"/>).
    /// Pyramid Energy's option is picked as it is activated by the existing bullet flow
    /// (<see cref="PoisonMeteorCestusRuntime"/>).</item>
    /// <item>D. Tribe: face-up monsters you control become a Type until the End Phase (<see cref="RaceUntilEndPhase"/>,
    /// read by <see cref="TypeDeclarationRuntime.CurrentRace"/>; the latest Type change applied wins).</item>
    /// <item>Needle Ceiling: activation condition, then every face-up monster is destroyed in one event.</item>
    /// <item>Secret Pass to the Treasures: a granted direct attack (<see cref="HasSpellDirectAttackGrant"/>).</item>
    /// <item>Hieroglyph Lithograph: <see cref="DuelistState.HandSizeLimitByEffect"/> and its order against Enervating Mist
    /// (<see cref="AppliedBeforeLithograph"/>).</item>
    /// <item>Card Shuffle: the face-up ignition of <see cref="PerCountBurnsRuntime"/> (pay 300 LP, once per turn) whose link
    /// asks "your Deck or your opponent's" as it resolves (<see cref="TryResolveShuffleLink"/>).</item>
    /// </list>
    /// </summary>
    public static class PgdSpellTrapBasicsRuntime
    {
        /// <summary><see cref="EffectClause.ActivationCondition"/>: you control a face-up monster (D. Tribe, Pyramid Energy).</summary>
        public const string YouControlFaceUpMonster = "PgdYouControlFaceUpMonster";

        /// <summary><see cref="EffectClause.ActivationCondition"/>: your opponent controls a face-up monster (Curse of Aging).</summary>
        public const string OpponentControlsFaceUpMonster = "PgdOpponentControlsFaceUpMonster";

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: <see cref="EffectClause.ConditionAmount"/> or more monsters on the
        /// field, face-down ones included (GetFieldGroupCount), and at least one of them face-up (Needle Ceiling).
        /// </summary>
        public const string FieldMonstersAtLeastWithFaceUp = "PgdFieldMonstersAtLeastWithFaceUp";

        const string DeckChoiceKind = "Deck choice";

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False for any other condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause c, out bool ok,
            out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            var opp = engine.OpponentOf(who);
            switch (c.ActivationCondition)
            {
                case YouControlFaceUpMonster:
                    ok = who.MonstersOnField().Any(m => m != null && m.FaceUp);
                    reason = "You must control a face-up monster.";
                    return true;
                case OpponentControlsFaceUpMonster:
                    ok = opp != null && opp.MonstersOnField().Any(m => m != null && m.FaceUp);
                    reason = "Your opponent must control a face-up monster.";
                    return true;
                case FieldMonstersAtLeastWithFaceUp:
                {
                    var all = who.MonstersOnField().Concat(opp?.MonstersOnField() ?? Enumerable.Empty<CardInstance>())
                        .Where(m => m != null).ToList();
                    var need = c.ConditionAmount > 0 ? c.ConditionAmount : 4;
                    ok = all.Count >= need && all.Any(m => m.FaceUp);
                    reason = $"Needs {need} or more monsters on the field, one of them face-up.";
                    return true;
                }
                default:
                    return false;
            }
        }

        // ───────────────────────────── Book of Taiyou ─────────────────────────────

        /// <summary>
        /// The target, if it is still a face-down monster on the field, becomes face-up Attack Position. Its FLIP effect
        /// (and any "position changed" trigger) activates after this effect, with the Chain's other triggers.
        /// </summary>
        internal static void FlipTargetFaceUpAttack(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null) return;
            var name = source?.Name ?? "Effect";
            var ctrl = target != null ? engine.ControllerOf(target) : null;
            if (ctrl == null || !ctrl.TryFindMonster(target, out _))
            {
                engine.Log($"{name}: its target is no longer on the field.");
                return;
            }

            if (target.FaceUp)
            {
                engine.Log($"{name}: {target.Name} is no longer face-down — nothing happens.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var changed = false;
            SimultaneousTriggersRuntime.Capture(engine, () =>
            {
                if (!engine.ApplyEffectPositionChange(target, BattlePosition.Attack, faceUp: true)) return;
                changed = true;
                engine.QueueFlipEffectAfterResolution(ctrl, target);
            });
            if (!changed) return;
            engine.Log($"{name}: {target.Name} is flipped into face-up Attack Position.");
            if (target.FaceUp && engine.ControllerOf(target) != null)
                SimultaneousTriggersRuntime.QueueThisCardPositionChanged(engine, target, toDefense: false);
        }

        // ───────────────────────────── Curse of Aging / Pyramid Energy ─────────────────────────────

        /// <summary>
        /// Every face-up monster <see cref="EffectClause.Side"/> controls now gains Amount ATK and DefAmount DEF (negative
        /// = loses) until the End Phase. Monsters that arrive later are not affected.
        /// </summary>
        internal static void ModifyAllFaceUp(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || c == null) return;
            var side = c.Side == EffectSide.Opponent ? engine.OpponentOf(who) : who;
            if (side == null) return;
            var any = false;
            foreach (var m in side.MonstersOnField().ToList())
            {
                if (m?.Def == null || !m.FaceUp) continue;
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                {
                    engine.Log($"{m.Name} is unaffected.");
                    continue;
                }

                m.UntilEndOfTurnAtk += c.Amount;
                m.UntilEndOfTurnDef += c.DefAmount;
                any = true;
                engine.Log($"{m.Name}: {Signed(c.Amount)} ATK / {Signed(c.DefAmount)} DEF until the End Phase " +
                           $"(now {m.CurrentAtk}/{m.CurrentDef}).");
            }

            if (!any)
                engine.Log($"{source?.Name ?? "Effect"}: no face-up monster to affect.");
        }

        static string Signed(int n) => n >= 0 ? "+" + n : n.ToString();

        /// <summary>The picker label of a Pyramid Energy option (<see cref="PoisonMeteorCestusRuntime"/> bullet tokens).</summary>
        public static string DescribeStatBullet(EffectClause c)
        {
            if (c == null) return "that effect";
            if (c.Amount != 0 && c.DefAmount == 0)
                return $"{Signed(c.Amount)} ATK to your face-up monsters";
            if (c.Amount == 0 && c.DefAmount != 0)
                return $"{Signed(c.DefAmount)} DEF to your face-up monsters";
            return $"{Signed(c.Amount)} ATK / {Signed(c.DefAmount)} DEF to your face-up monsters";
        }

        // ───────────────────────────── D. Tribe ─────────────────────────────

        sealed class RaceChange
        {
            public WeakReference<DuelEngine> Engine;
            public string Race;
            public long Stamp;
            public int Turn;
            public int FaceDownStamp;
            public int EntryStamp;
        }

        static readonly ConditionalWeakTable<CardInstance, RaceChange> s_race = new();

        /// <summary>Every face-up monster you control now is treated as <see cref="EffectClause.BecomesRace"/> until the End Phase.</summary>
        internal static void BecomeTypeUntilEndPhase(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c)
        {
            if (engine == null || who == null || string.IsNullOrEmpty(c?.BecomesRace)) return;
            var any = false;
            foreach (var m in who.MonstersOnField().ToList())
            {
                if (m?.Def == null || !m.FaceUp) continue;
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                {
                    engine.Log($"{m.Name} is unaffected.");
                    continue;
                }

                s_race.Remove(m);
                s_race.Add(m, new RaceChange
                {
                    Engine = new WeakReference<DuelEngine>(engine),
                    Race = c.BecomesRace,
                    Stamp = TypeDeclarationRuntime.NextStamp(),
                    Turn = engine.TurnNumber,
                    FaceDownStamp = m.FaceDownStamp,
                    EntryStamp = m.FieldEntryStamp
                });
                any = true;
                engine.Log($"{m.Name} is treated as a {c.BecomesRace} monster until the End Phase.");
            }

            if (!any)
                engine.Log($"{source?.Name ?? "Effect"}: no face-up monster to affect.");
        }

        /// <summary>
        /// The Type an "until the End Phase" Type change gives <paramref name="card"/> now, with its timestamp, or null:
        /// only while the same copy stays face-up in a Monster Zone (RESETS_STANDARD) during the turn it was applied.
        /// A copy that left the field and came back (<see cref="CardInstance.FieldEntryStamp"/>: RESET_LEAVE /
        /// RESET_TOFIELD) or was turned face-down (RESET_TURN_SET) is a new card: the change is gone for good.
        /// The turn check matches ClearUntilEndOfTurnStatMods, which runs at the end of the End Phase right before
        /// BeginTurn bumps TurnNumber.
        /// </summary>
        public static string RaceUntilEndPhase(CardInstance card, out long stamp)
        {
            stamp = long.MinValue;
            if (card == null || !s_race.TryGetValue(card, out var r)) return null;
            if (!r.Engine.TryGetTarget(out var engine) || engine == null || engine.TurnNumber != r.Turn ||
                !card.FaceUp || card.FaceDownStamp != r.FaceDownStamp || card.FieldEntryStamp != r.EntryStamp)
            {
                s_race.Remove(card);
                return null;
            }

            var ctrl = engine.ControllerOf(card);
            if (ctrl == null || !ctrl.TryFindMonster(card, out _)) return null;
            stamp = r.Stamp;
            return r.Race;
        }

        // ───────────────────────────── Needle Ceiling ─────────────────────────────

        /// <summary>Every face-up monster on the field is destroyed, in one event. Face-down monsters stay.</summary>
        internal static void DestroyAllFaceUp(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance except = null)
        {
            if (engine == null || who == null) return;
            var opp = engine.OpponentOf(who);
            // SummonTriggersAndTributes batch (v163): Dark Dust Spirit destroys every face-up monster except itself.
            var victims = who.MonstersOnField().Concat(opp?.MonstersOnField() ?? Enumerable.Empty<CardInstance>())
                .Where(m => m?.Def != null && m.FaceUp && !ReferenceEquals(m, except)).ToList();
            if (victims.Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no face-up monster to destroy.");
                return;
            }

            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in victims)
                    TextEffectRuntime.DestroyCard(engine, m, source, false, who);
            });
        }

        // ───────────────────────────── Secret Pass to the Treasures ─────────────────────────────

        sealed class DirectGrant
        {
            public WeakReference<DuelEngine> Engine;
            public int Turn;
            public int FaceDownStamp;
        }

        static readonly ConditionalWeakTable<CardInstance, DirectGrant> s_direct = new();

        /// <summary>The end of TextEffectRuntime.CollectTargets: a face-up monster with ConditionAmount or less ATK.</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null) return;
            // ArchfiendRiders batch: Checkmate prints no ATK ceiling (ConditionAmount 0); its target is still face-up.
            if (c.Action == EffectActionKind.TargetCanAttackDirectlyThisTurn)
                list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp ||
                                    (c.ConditionAmount > 0 && t.CurrentAtk > c.ConditionAmount));
        }

        /// <summary>
        /// The target, if it is still a face-up monster on the field (Lua IsRelateToEffect + IsFaceup; its ATK is not
        /// checked again), can attack directly this turn.
        /// </summary>
        internal static void GrantDirectAttack(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target, bool requireFaceUp = true)
        {
            if (engine == null) return;
            var name = source?.Name ?? "Effect";
            var ctrl = target != null ? engine.ControllerOf(target) : null;
            // SummonTriggersAndTributes batch (v163): Jowls of Dark Demise grants it to a face-down monster too.
            if (ctrl == null || !ctrl.TryFindMonster(target, out _) || (requireFaceUp && !target.FaceUp))
            {
                engine.Log($"{name}: its target is no longer a face-up monster on the field.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            // Cleared on entering the field again and at the start of the next turn (DuelEngine).
            target.DirectAttackThisTurn = true;
            s_direct.Remove(target);
            s_direct.Add(target, new DirectGrant
            {
                Engine = new WeakReference<DuelEngine>(engine),
                Turn = engine.TurnNumber,
                FaceDownStamp = target.FaceDownStamp
            });
            engine.Log($"{name}: {target.Name} can attack your opponent directly this turn.");
        }

        /// <summary>
        /// DuelEngine.GrantsDirectAttack: a Secret Pass grant still applies. It comes from the Spell, so the monster's own
        /// negation does not remove it; turning the monster face-down (RESET_TURN_SET) or leaving the field ends it.
        /// </summary>
        public static bool HasSpellDirectAttackGrant(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker == null || !s_direct.TryGetValue(attacker, out var g)) return false;
            if (!g.Engine.TryGetTarget(out var e) || !ReferenceEquals(e, engine) || engine.TurnNumber != g.Turn ||
                attacker.FaceDownStamp != g.FaceDownStamp || !attacker.DirectAttackThisTurn)
            {
                // Turned face-down since: the grant is over (the flag would otherwise outlive it this turn).
                if (ReferenceEquals(e, engine) && engine.TurnNumber == g.Turn && attacker.FaceDownStamp != g.FaceDownStamp)
                    attacker.DirectAttackThisTurn = false;
                s_direct.Remove(attacker);
                return false;
            }

            return attacker.FaceUp && engine.ControllerOf(attacker) != null;
        }

        // ───────────────────────────── Hieroglyph Lithograph ─────────────────────────────

        /// <summary>The "opponent's hand size limit" cards already applying to a player when their Lithograph resolved.</summary>
        static readonly ConditionalWeakTable<DuelistState, List<(CardInstance Card, int FaceDownStamp)>> s_before = new();

        /// <summary>
        /// The activating player's hand size limit becomes Amount for the rest of the Duel. An Enervating Mist already
        /// applying to them now is overridden (the later "becomes" wins); one applied later overrides this.
        /// </summary>
        internal static void SetHandLimit(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || c == null) return;
            var limit = Math.Max(0, c.Amount);
            who.HandSizeLimitByEffect = limit;
            var other = engine.OpponentOf(who);
            var before = new List<(CardInstance, int)>();
            if (other != null)
                foreach (var src in other.SpellTrapsOnField().Concat(other.MonstersOnField()))
                    if (src != null && src.FaceUp && !src.IsNegated && HasClause(src, EffectActionKind.OpponentHandSizeLimitBecomes))
                        before.Add((src, src.FaceDownStamp));
            s_before.Remove(who);
            s_before.Add(who, before);
            engine.Log($"{source?.Name ?? "Effect"}: {who.Name}'s hand size limit becomes {limit} for the rest of the Duel.");
        }

        /// <summary>
        /// ContinuousProtections.HandSizeLimitEndPhaseFor: <paramref name="limiter"/> (a face-up "your opponent's hand size
        /// limit becomes N" card) was already applying to <paramref name="who"/> when their Hieroglyph Lithograph resolved,
        /// so the Lithograph's limit overrides it.
        /// </summary>
        public static bool AppliedBeforeLithograph(DuelistState who, CardInstance limiter)
        {
            if (who == null || limiter == null || who.HandSizeLimitByEffect <= 0) return false;
            if (!s_before.TryGetValue(who, out var list)) return false;
            foreach (var (card, stamp) in list)
                if (ReferenceEquals(card, limiter) && limiter.FaceDownStamp == stamp)
                    return true;
            return false;
        }

        static bool HasClause(CardInstance card, EffectActionKind action)
        {
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        // ───────────────────────────── Card Shuffle ─────────────────────────────

        sealed class ShuffleUse
        {
            public int Turn;
            public int EntryStamp;
        }

        sealed class ShuffleHold
        {
            public DuelistState Who;
            public CardInstance Card;
        }

        static readonly ConditionalWeakTable<CardInstance, ShuffleUse> s_shuffleUsed = new();
        static readonly ConditionalWeakTable<ChainLink, ShuffleHold> s_shuffleLinks = new();

        static EffectClause ShuffleClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.FaceUpFieldIgnition)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.ShuffleYourOrOpponentsDeck);

        /// <summary>The card's face-up ignition is Card Shuffle's paid shuffle.</summary>
        public static bool IsShuffleIgnition(CompiledCardProgram prog) => ShuffleClause(prog) != null;

        /// <summary>
        /// PerCountBurnsRuntime.CanActivateFaceUpFieldIgnition, after its open-state / own Main Phase / negation checks:
        /// once per turn, and the LP cost can be paid (Duel.CheckLPCost).
        /// </summary>
        public static bool CanUseShuffleIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            var c = ShuffleClause(prog);
            if (engine == null || who == null || card == null || c == null)
            {
                reason = "Not a Card Shuffle effect.";
                return false;
            }

            // SetCountLimit(1) with no code: a copy that left the field and came back is a new card.
            if (s_shuffleUsed.TryGetValue(card, out var used) && used.Turn == engine.TurnNumber &&
                used.EntryStamp == card.FieldEntryStamp)
            {
                reason = "This card's effect was already used this turn.";
                return false;
            }

            if (who.LifePoints < c.PayLpAmount)
            {
                reason = $"Needs {c.PayLpAmount} LP to pay.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// PerCountBurnsRuntime.TryFaceUpFieldIgnition, on its seated Chain Link: count the use and pay the LP (cost). The
        /// Deck is chosen when the link resolves.
        /// </summary>
        public static void ActivateShuffleIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, ChainLink link)
        {
            var c = ShuffleClause(prog);
            if (engine == null || who == null || card == null || c == null || link == null) return;
            s_shuffleUsed.Remove(card);
            s_shuffleUsed.Add(card, new ShuffleUse { Turn = engine.TurnNumber, EntryStamp = card.FieldEntryStamp });
            s_shuffleLinks.Remove(link);
            s_shuffleLinks.Add(link, new ShuffleHold { Who = who, Card = card });
            engine.Log($"Activate: {card.Name} (effect) — Chain Link {link.LinkNumber}.");
            var amount = Math.Max(0, c.PayLpAmount);
            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP → {who.Name} at {Math.Max(0, who.LifePoints - amount)} LP.");
        }

        /// <summary>
        /// PerCountBurnsRuntime.TryResolveFaceUpFieldIgnitionLink: false for any other link. Card Shuffle's link resolves
        /// only while the card is still face-up in its zone with its effects not negated (Lua IsRelateToEffect); then a
        /// human chooses the Deck (the Chain waits: <see cref="PendingActivation.HoldsChainResolution"/>), the AI shuffles
        /// its own.
        /// </summary>
        public static bool TryResolveShuffleLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || !s_shuffleLinks.TryGetValue(link, out var st)) return false;
            s_shuffleLinks.Remove(link);
            var card = st.Card;
            var who = st.Who;
            var name = card?.Name ?? "Card Shuffle";
            var negatedBy = ContinuousNegationRuntime.NegatedBy(engine, card);
            if (negatedBy != null || (card != null && card.IsNegated))
            {
                engine.Log($"{name}: its effect is negated{(negatedBy != null ? $" by {negatedBy.Name}" : "")}.");
                engine.NotifyPublic();
                return true;
            }

            if (who == null || card == null || !card.FaceUp || !who.TryFindSpellTrap(card, out _))
            {
                engine.Log($"{name} is no longer on the field — its effect does nothing.");
                engine.NotifyPublic();
                return true;
            }

            if (!who.IsPlayer)
            {
                Shuffle(engine, who, card, who);
                engine.NotifyPublic();
                return true;
            }

            // EngineResiduals (v163): the generic card pick (ContinuousTrapsAndSpiritSupportRuntime.AskCardPick); the choice is
            // mandatory — Cancel shuffles your own Deck. The Chain waits for it while it resolves.
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => new List<CardInstance> { Token("Your Deck", 1), Token("Opponent's Deck", 2) },
                Prompt = "shuffle your Deck or your opponent's Deck.",
                AiChoose = list => list.FirstOrDefault(),
                HoldsChain = engine.IsResolvingChain,
                Finish = chosen => Shuffle(engine, who, card,
                    chosen?.Def?.level == 2 ? engine.OpponentOf(who) ?? who : who)
            });
            engine.NotifyPublic();
            return true;
        }

        static void Shuffle(DuelEngine engine, DuelistState who, CardInstance card, DuelistState deckOwner)
        {
            if (deckOwner?.Deck != null)
                engine.Rng.Shuffle(deckOwner.Deck);
            engine.Log($"{card?.Name ?? "Card Shuffle"}: {who?.Name} shuffles " +
                       $"{(ReferenceEquals(deckOwner, who) ? "their own" : $"{deckOwner?.Name}'s")} Deck.");
        }

        static CardInstance Token(string name, int index) => new CardInstance
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = name,
                type = DeckChoiceKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = index,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        // ───────────────────────────── AI picks ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond "use it or lose it": a Set Needle Ceiling is worth activating only when the
        /// opponent loses more face-up ATK than the AI. True for every other card.
        /// </summary>
        public static bool AiWorthUsing(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return true;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.DestroyAllFaceUpMonstersOnField))
                return true;
            long FaceUpAtk(DuelistState side) =>
                side?.MonstersOnField().Where(m => m != null && m.FaceUp).Sum(m => (long)m.CurrentAtk) ?? 0;
            return FaceUpAtk(engine.OpponentOf(ai)) > FaceUpAtk(ai);
        }

        /// <summary>
        /// TextEffectRuntime.AutoPick: Book of Taiyou flips your own face-down FLIP monster first, else an opponent's
        /// face-down monster (no peeking at what it is); Secret Pass picks your strongest legal monster.
        /// </summary>
        public static bool TryAutoPick(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> targets,
            out CardInstance pick)
        {
            pick = null;
            if (engine == null || who == null || c == null || targets == null || targets.Count == 0) return false;
            switch (c.Action)
            {
                case EffectActionKind.FlipFaceDownTargetToFaceUpAttack:
                    // EngineResiduals (v163): a "flipped face-up" trigger (Royal Keeper, Cobraman Sakuzy) counts like FLIP.
                    pick = targets.FirstOrDefault(t => engine.ControllerOf(t) == who &&
                                                       ((CompiledEffectCache.GetOrCompile(t?.Def)
                                                            ?.HasTiming(EffectTiming.Flip) ?? false) ||
                                                        SpiritMonstersRuntime.HasFlippedFaceUpTrigger(t)))
                           ?? targets.FirstOrDefault(t => engine.ControllerOf(t) != who)
                           ?? targets[0];
                    return true;
                case EffectActionKind.TargetCanAttackDirectlyThisTurn:
                    pick = targets.OrderByDescending(t => t.CurrentAtk).First();
                    return true;
                default:
                    return false;
            }
        }
    }
}
