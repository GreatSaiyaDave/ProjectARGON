using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// FaceUpQuickEffects batch (v160) runtime: Quick Effects (Spell Speed 2) of face-up Continuous Spells/Traps
    /// (<see cref="EffectTiming.FaceUpQuickEffect"/>; ygopro EFFECT_TYPE_QUICK_O + SetRange(LOCATION_SZONE)).
    ///
    /// <b>When.</b> On either player's turn: in the open game state (Main Phases, the Battle Step, the End Phase;
    /// never during an attack or the Damage Step), in the opponent's open-state window, at an attack declaration
    /// or a Summon made by the opponent, and as a chain response (<see cref="CanActivateQuick"/>). The card's
    /// effects must not be negated, and its own activation must have resolved (<see cref="CardInstance.IsNegated"/>
    /// covers <see cref="CardInstance.ActivationUnresolvedOnChain"/>).
    ///
    /// <b>Chain Link.</b> <see cref="TryActivate"/> (from SpellTrapEffects.BeginOrResolveManual) seats a Spell Speed 2
    /// Chain Link whose <see cref="ChainLink.EffectKey"/> is <see cref="QuickEffectLinkKey"/>: the activation of a
    /// card's effect, not of a card, so "When a Spell/Trap Card is activated" answers (Magic Jammer, Seven Tools,
    /// Solemn Judgment) cannot negate it. The cost is paid when it is activated (a player picks it; Cancel undoes the
    /// activation and nothing is paid), then the opponent may respond; the effect applies when the link resolves
    /// (<see cref="TryResolveLink"/>, from DuelEngine.ResolveChainStack).
    ///
    /// <b>Cards.</b> Type Zero Magic Crusher: discard 1 Spell (cost) → 500 damage to the opponent, only while this
    /// card is still on the field (Lua IsRelateToEffect). Skull Lair, once per Chain: banish N &gt;= 1 monsters from
    /// your GY (cost; a face-up Level-N monster must be on the field) → select (no target) 1 face-up Level-N monster
    /// on either field and destroy it. A player chooses N with number tokens (the declaration-token picker,
    /// <see cref="NumberTokenKind"/>) and then the N monsters; the AI decides at once. Soul Demolition, while you
    /// control a face-up Fiend: pay 500 LP (cost); the turn player targets 1 monster in their opponent's GY, then the
    /// other player does (a player picks it even when the opponent activated the effect); both are banished. The
    /// Dragon's Bead (a chain answer, ygopro EVENT_CHAINING): to the activation of a Trap Card that targets a face-up
    /// Dragon, discard 1 card (cost) → negate that effect and destroy that Trap Card.
    /// </summary>
    public static class FaceUpQuickEffectsRuntime
    {
        /// <summary><see cref="EffectClause.DiscardCostAttribute"/> of a "Discard 1 Spell Card" cost (any Spell).</summary>
        public const string DiscardSpellCard = "Spell Card";

        /// <summary>
        /// <see cref="ChainLink.EffectKey"/> of a face-up Quick Effect link (the activation of a card's effect).
        /// </summary>
        public const string QuickEffectLinkKey = "FaceUpQuickEffects.QuickEffect";

        /// <summary><see cref="CardDef.type"/> of a number token (Skull Lair's count N), a pick that is not a card.</summary>
        public const string NumberTokenKind = "Declaration (Number)";

        /// <summary>What a seated link carries from its activation to its resolution (ygopro target param).</summary>
        sealed class LinkState
        {
            public EffectClause Clause;
            /// <summary>Skull Lair: the number of monsters banished as the cost (the Level to destroy).</summary>
            public int Level;
            /// <summary>Soul Demolition: each target and the player who selected it.</summary>
            public readonly List<(CardInstance card, DuelistState selector)> Targets = new();
            /// <summary>The Dragon's Bead: the Trap Card activation it answers.</summary>
            public ChainLink Answered;
        }

        static readonly ConditionalWeakTable<ChainLink, LinkState> s_links = new();

        enum Step
        {
            /// <summary>The discard cost (Type Zero: a Spell Card).</summary>
            Discard,
            /// <summary>Skull Lair: how many monsters to banish (a number token).</summary>
            ChooseCount,
            /// <summary>Skull Lair: the monsters to banish from the GY, one at a time.</summary>
            BanishPicks,
            /// <summary>Skull Lair at resolution: the face-up monster of that Level to destroy (no target).</summary>
            ResolveDestroy,
            /// <summary>Soul Demolition: a player's target in their opponent's GY (the activator's or the other's).</summary>
            GyTarget,
            /// <summary>SpellCounterSpenders batch — Pitch-Black Power Stone: the other face-up card to move a counter to.</summary>
            MoveTarget
        }

        /// <summary>A player's pick this runtime opened (cost at activation, or a selection at resolution).</summary>
        sealed class PickState
        {
            public Step Step;
            public EffectClause Clause;
            public ChainLink Link;
            public ResponseTiming Timing;
            public int Count;
            public readonly List<CardInstance> Banish = new();
            /// <summary>Soul Demolition: the activator, and the targets chosen so far (with their selector).</summary>
            public DuelistState Activator;
            public CardInstance Source;
            /// <summary>The activator's own choices are made automatically (an AI-made or auto-picked activation).</summary>
            public bool ActivatorAuto;
            public readonly List<(CardInstance card, DuelistState selector)> Targets = new();
        }

        static readonly ConditionalWeakTable<PendingActivation, PickState> s_picks = new();

        // ───────────────────────────── recognition ─────────────────────────────

        /// <summary>
        /// <paramref name="card"/> is a face-up Spell/Trap in <paramref name="who"/>'s Spell &amp; Trap Zone whose
        /// complete program has a <see cref="EffectTiming.FaceUpQuickEffect"/> clause: activating it from there means
        /// that Quick Effect, never the card a second time.
        /// </summary>
        public static bool IsFaceUpQuickCard(DuelistState who, CardInstance card, CompiledCardProgram prog)
        {
            if (who == null || card?.Def == null || card.Def.IsMonster || !card.FaceUp) return false;
            if (!card.Def.IsSpell && !card.Def.IsTrap) return false;
            if (prog == null || !prog.FullyCompiled || !prog.HasTiming(EffectTiming.FaceUpQuickEffect)) return false;
            return who.TryFindSpellTrap(card, out _);
        }

        /// <summary><paramref name="link"/> is a face-up Quick Effect link (<see cref="QuickEffectLinkKey"/>).</summary>
        public static bool IsQuickEffectLink(ChainLink link) => link != null && link.EffectKey == QuickEffectLinkKey;

        static EffectClause QuickClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.FaceUpQuickEffect).FirstOrDefault(c => c != null);

        // ───────────────────────────── legality ─────────────────────────────

        /// <summary>
        /// The Quick Effect of this face-up card can be activated now by <paramref name="who"/> (see the class
        /// summary for the windows), and its cost can be paid.
        /// </summary>
        public static bool CanActivateQuick(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            if (engine == null || !IsFaceUpQuickCard(who, card, prog))
            {
                reason = "Not a face-up card with a Quick Effect.";
                return false;
            }

            var clause = QuickClause(prog);
            if (clause == null)
            {
                reason = "No Quick Effect.";
                return false;
            }

            // LocksRedirectsAndActivity batch: Goblin of Greed — no discard cost (Type Zero Magic Crusher's Spell discard).
            if (LocksRedirectsAndActivityRuntime.DiscardCostLocked(engine, who, clause, out reason)) return false;

            if (engine.GameOver)
            {
                reason = "The Duel is over.";
                return false;
            }

            if (engine.PendingActivation != null)
            {
                reason = "Finish the current choice first.";
                return false;
            }

            if (engine.IsResolvingChain || engine.Chain.IsResolving)
            {
                reason = "A Chain is resolving.";
                return false;
            }

            if (card.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, card) != null ||
                TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, card))
            {
                reason = "This card's effects are negated.";
                return false;
            }

            if (ContinuousNegationRuntime.ActivationLocked(engine, who, card, out var lockReason))
            {
                reason = lockReason;
                return false;
            }

            var pr = engine.PendingResponse;
            if (IsChainAnswer(clause))
            {
                // The Dragon's Bead: only right after the activation it answers (ygopro EVENT_CHAINING).
                if (pr == null || pr.Timing != ResponseTiming.ChainResponse || pr.Responder != who ||
                    pr.ChainTarget == null || pr.ChainTarget != engine.Chain.Last)
                {
                    reason = "Only in response to the activation of a Trap Card that targets the right monster.";
                    return false;
                }

                if (!engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick))
                {
                    reason = "A Spell Speed 2 effect cannot be chained to this Chain Link.";
                    return false;
                }
            }
            else if (pr == null)
            {
                if (engine.HasDeclaredAttack || engine.DamageSubStep != DamageSubStep.None)
                {
                    reason = "Finish the declared attack / response first.";
                    return false;
                }

                if (engine.Phase != DuelPhase.Main1 && engine.Phase != DuelPhase.Main2 &&
                    engine.Phase != DuelPhase.Battle && engine.Phase != DuelPhase.End)
                {
                    reason = "Not in an open game state.";
                    return false;
                }

                // Open game state: the chain is empty (the link TryActivateSpellTrap seats for this card is allowed).
                if (engine.Chain.HasLinks &&
                    !(engine.Chain.Count == 1 && engine.Chain.Last.Card == card && !engine.Chain.Last.Resolved))
                {
                    reason = "Respond in the Chain window.";
                    return false;
                }
            }
            else
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
                            reason = "Only chain answers (Counter Traps …) onto your own activation.";
                            return false;
                        }

                        if (!engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick))
                        {
                            reason = "A Spell Speed 2 effect cannot be chained to this Chain Link.";
                            return false;
                        }

                        break;
                    case ResponseTiming.OpponentOpenState:
                    case ResponseTiming.AttackDeclared:
                        break;
                    case ResponseTiming.MonsterSummoned:
                        if (pr.Summoner == who)
                        {
                            reason = "Not legal for this response timing.";
                            return false;
                        }

                        break;
                    default:
                        reason = "Not legal for this response timing.";
                        return false;
                }
            }

            if (clause.OncePerChain && UsedOnThisChain(engine, card))
            {
                reason = "This card's effect can only be activated once per Chain.";
                return false;
            }

            return ClauseReady(engine, who, card, clause, out reason);
        }

        static bool UsedOnThisChain(DuelEngine engine, CardInstance card) =>
            engine.Chain.Links.Any(l => l != null && l.Card == card && IsQuickEffectLink(l));

        /// <summary>The clause's cost can be paid and its effect can be activated (Lua chk==0).</summary>
        static bool ClauseReady(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause,
            out string reason)
        {
            switch (clause.Action)
            {
                case EffectActionKind.InflictDamageToOpponent when clause.DiscardCostAttribute == DiscardSpellCard:
                    if (SpellsInHand(who).Count == 0)
                    {
                        reason = "No Spell Card in your hand to discard.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                case EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost:
                    if (CountChoices(engine, who, card).Count == 0)
                    {
                        reason = "No face-up monster's Level can be matched by banishing monsters from your GY.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                case EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish:
                    if (!string.IsNullOrEmpty(clause.RaceFilter) && !who.MonstersOnField().Any(m =>
                            m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, clause.RaceFilter)))
                    {
                        reason = $"You must control a face-up {clause.RaceFilter} monster.";
                        return false;
                    }

                    if (who.LifePoints < clause.PayLpAmount)
                    {
                        reason = $"Not enough LP to pay {clause.PayLpAmount}.";
                        return false;
                    }

                    var opp = engine.OpponentOf(who);
                    if (GyTargets(engine, who, card, opp).Count == 0 || GyTargets(engine, who, card, who).Count == 0)
                    {
                        reason = "Needs a monster in each player's GY.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                case EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt:
                    if (!AnswersTrapActivation(engine, engine.PendingResponse?.ChainTarget, clause.RaceFilter))
                    {
                        reason = $"Only in response to the activation of a Trap Card that targets a face-up " +
                                 $"{clause.RaceFilter} monster.";
                        return false;
                    }

                    if (who.Hand == null || who.Hand.Count == 0)
                    {
                        reason = "No card in your hand to discard.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                // SpellCounterSpenders batch: Pitch-Black Power Stone — your turn, once per turn, a counter on this card and
                // another face-up card that can take one.
                case EffectActionKind.MoveSpellCounterFromThisToTarget:
                    return SpellCounterSpendersRuntime.MoveReady(engine, who, card, clause, out reason);
                default:
                    reason = "This Quick Effect is not supported.";
                    return false;
            }
        }

        /// <summary>
        /// Double Snare validity (ygopro aux.DoubleSnareValidity): <paramref name="prog"/> has a face-up effect that
        /// negates Trap effects (The Dragon's Bead). For the "a card that negates Trap effects" check of Double Snare.
        /// </summary>
        public static bool NegatesTrapEffects(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.FaceUpQuickEffect &&
                c.Action == EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt);

        /// <summary>The Dragon's Bead: a chain answer (ygopro EVENT_CHAINING), not a free-chain Quick Effect.</summary>
        static bool IsChainAnswer(EffectClause clause) =>
            clause?.Action == EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt;

        /// <summary>
        /// Soul Demolition: the monsters in <paramref name="gyOwner"/>'s GY that can be targeted to be banished by this
        /// effect (Necrovalley and Kycoo checked for the activator, ygopro IsAbleToRemove).
        /// </summary>
        static List<CardInstance> GyTargets(DuelEngine engine, DuelistState activator, CardInstance source,
            DuelistState gyOwner)
        {
            var list = new List<CardInstance>();
            if (engine == null || gyOwner?.Graveyard == null) return list;
            // ForcedActivationAndSubstitution batch (v163): Spirit Elimination on the GY's owner — the monsters they
            // control are the targets instead (Lua s.rfilter with aux.SpElimFilter(c)).
            if (SpiritEliminationRuntime.Affects(engine, gyOwner))
                return SpiritEliminationRuntime.Substitutes(engine, gyOwner, null, false,
                    m => !engine.IsDragonTargetProtected(m) &&
                         !EquipLocksAndLeaveFieldRuntime.ShieldedFromTargeting(engine, activator, m));
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, gyOwner)) return list;
            if (RuleLocksRuntime.BanishFromGyLocked(engine, activator)) return list;
            foreach (var g in gyOwner.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster) continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g)) continue;
                // EquipLocksAndLeaveField batch: Metalsilver Armor — a GY monster the activator cannot target.
                if (EquipLocksAndLeaveFieldRuntime.ShieldedFromTargeting(engine, activator, g)) continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>
        /// The Dragon's Bead's condition (Lua s.discon) on the Chain Link it would answer: the activation of a Trap Card
        /// (not an effect of a face-up card) whose targets include a face-up monster of <paramref name="race"/> on the
        /// field, and whose effect can be negated.
        /// </summary>
        public static bool AnswersTrapActivation(DuelEngine engine, ChainLink link, string race)
        {
            var def = link?.Card?.Def;
            if (engine == null || def == null || !def.IsTrap || link.Resolved || link.Negated) return false;
            if (IsQuickEffectLink(link) || PerCountBurnsRuntime.IsCardEffectLink(link)) return false;
            if (!link.WasSetOnField && !link.FromHand) return false;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled) return false;
            // EFFECT_FLAG_CARD_TARGET: one of its clauses targets; IsChainDisablable: its effect can be negated.
            if (!TargetingTruthRuntime.ProgramTargets(prog)) return false;
            if (prog.ClauseList.Exists(c => c != null && !c.EffectNegatable)) return false;
            var targets = new List<CardInstance>();
            if (link.Target != null) targets.Add(link.Target);
            targets.AddRange(link.Targets.Where(t => t != null));
            return targets.Any(t => t.Def != null && t.Def.IsMonster && t.FaceUp &&
                                    (engine.Player.TryFindMonster(t, out _) || engine.Opponent.TryFindMonster(t, out _)) &&
                                    ClassicEraTemplates.RaceMatches(t.Race, race));
        }

        static List<CardInstance> SpellsInHand(DuelistState who) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(c => c?.Def != null && c.Def.IsSpell).ToList();

        /// <summary>Monsters in <paramref name="who"/>'s GY that can be banished as a cost now (Necrovalley, Kycoo).</summary>
        static List<CardInstance> BanishableGyMonsters(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine == null || who?.Graveyard == null) return list;
            // ForcedActivationAndSubstitution batch (v163): Spirit Elimination — Skull Lair banishes monsters `who`
            // controls instead (Lua s.cfilter with aux.SpElimFilter(c)).
            if (SpiritEliminationRuntime.Affects(engine, who))
                return SpiritEliminationRuntime.Substitutes(engine, who, null, false);
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, who)) return list;
            if (RuleLocksRuntime.BanishFromGyLocked(engine, who)) return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster) continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g)) continue;
                list.Add(g);
            }

            return list;
        }

        static IEnumerable<CardInstance> FaceUpMonsters(DuelEngine engine)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m?.Def != null && m.FaceUp)
                        yield return m;
            }
        }

        /// <summary>
        /// Skull Lair: every N (1 … the monsters you can banish) for which a face-up monster of Level N is on the
        /// field (Lua s.rescon), smallest first.
        /// </summary>
        static List<int> CountChoices(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var max = BanishableGyMonsters(engine, who, source).Count;
            return FaceUpMonsters(engine).Select(m => m.Level).Where(l => l >= 1 && l <= max).Distinct()
                .OrderBy(l => l).ToList();
        }

        static List<CardInstance> FaceUpOfLevel(DuelEngine engine, int level) =>
            FaceUpMonsters(engine).Where(m => m.Level == level).ToList();

        // ───────────────────────────── activation ─────────────────────────────

        /// <summary>
        /// Called from SpellTrapEffects.BeginOrResolveManual for every activation. Returns false for anything that
        /// is not a face-up card with a Quick Effect (the normal activation goes on). For one: seats the Spell Speed
        /// 2 link, pays the cost (or opens the player's cost pick), then opens the opponent's chain window.
        /// <paramref name="ok"/> = the effect was activated (or its cost pick is open).
        /// </summary>
        public static bool TryActivate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick, out bool ok)
        {
            ok = false;
            if (engine == null || who == null || card?.Def == null || fromHand) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsFaceUpQuickCard(who, card, prog)) return false;
            if (!CanActivateQuick(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                return true;
            }

            var clause = QuickClause(prog);
            var pr = engine.PendingResponse;
            var timing = pr != null && pr.Responder == who ? pr.Timing : ResponseTiming.None;
            var link = engine.Chain.Last;
            if (timing == ResponseTiming.ChainResponse || link == null || link.Card != card || link.Resolved)
            {
                link = engine.Chain.AddLink(who, card, SpellSpeed.Speed2, EffectClass.Quick,
                    CardLocation.SpellTrapZone, fromHand: false, wasSet: false, effectKey: QuickEffectLinkKey);
                if (link == null)
                {
                    engine.Log($"{card.Name}: its effect cannot be chained now.");
                    return true;
                }
            }

            // The link TryActivateSpellTrap seated for the card becomes the effect's own link.
            link.EffectKey = QuickEffectLinkKey;
            link.Speed = SpellSpeed.Speed2;
            link.Class = EffectClass.Quick;
            link.WasSetOnField = false;
            link.FromHand = false;
            if (timing == ResponseTiming.ChainResponse)
                link.TargetLink = pr.ChainTarget;
            var human = engine.IsHumanControlled(who) && !autoPick;
            ok = true;

            switch (clause.Action)
            {
                case EffectActionKind.InflictDamageToOpponent:
                {
                    var spells = SpellsInHand(who);
                    if (human && spells.Count > 1)
                    {
                        OpenPick(engine, who, card, new PickState { Step = Step.Discard, Clause = clause, Link = link, Timing = timing },
                            EffectTargetKind.DiscardMonsterInHand, spells,
                            "discard 1 Spell Card from your hand (cost), or CANCEL.");
                        return true;
                    }

                    // The AI (or a lone Spell): a Spell it cannot activate now first, then the lowest-value one.
                    var discard = spells[0];
                    if (spells.Count > 1)
                    {
                        var dead = spells.Where(x => !engine.CanActivateSpellTrap(who, x, fromHand: true)).ToList();
                        discard = AiHeuristicPolicy.PickLowestValue(dead.Count > 0 ? dead : spells, card);
                    }
                    PayDiscard(engine, who, discard);
                    Seat(engine, who, card, link, timing, new LinkState { Clause = clause });
                    return true;
                }
                case EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost:
                {
                    var choices = CountChoices(engine, who, card);
                    var st = new PickState { Step = Step.ChooseCount, Clause = clause, Link = link, Timing = timing };
                    if (human)
                    {
                        if (choices.Count > 1)
                        {
                            OpenPick(engine, who, card, st, EffectTargetKind.DeclareMonsterType,
                                choices.Select(NumberToken).ToList(),
                                "banish how many monsters from your GY? The number is the Level of the face-up " +
                                "monster it destroys. Tap a number, or CANCEL.");
                            return true;
                        }

                        st.Count = choices[0];
                        return ContinueBanishPicks(engine, who, card, st);
                    }

                    st.Count = AiPickCount(engine, who, card, choices);
                    var fodder = BanishableGyMonsters(engine, who, card)
                        .OrderBy(AiGyValue).Take(st.Count).ToList();
                    st.Banish.AddRange(fodder);
                    PayBanish(engine, who, card, st);
                    Seat(engine, who, card, link, timing, new LinkState { Clause = clause, Level = st.Count });
                    return true;
                }
                case EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish:
                {
                    var st = new PickState
                    {
                        Step = Step.GyTarget, Clause = clause, Link = link, Timing = timing, Activator = who,
                        Source = card, ActivatorAuto = autoPick || !engine.IsHumanControlled(who)
                    };
                    return ContinueGyTargets(engine, st);
                }
                case EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt:
                {
                    var hand = who.Hand.Where(c => c != null).ToList();
                    if (human && hand.Count > 1)
                    {
                        OpenPick(engine, who, card, new PickState { Step = Step.Discard, Clause = clause, Link = link, Timing = timing },
                            EffectTargetKind.DiscardMonsterInHand, hand, "discard 1 card from your hand (cost), or CANCEL.");
                        return true;
                    }

                    PayDiscard(engine, who, hand.Count == 1 ? hand[0] : AiHeuristicPolicy.PickLowestValue(hand, card));
                    Seat(engine, who, card, link, timing, new LinkState { Clause = clause, Answered = link.TargetLink });
                    return true;
                }
                case EffectActionKind.MoveSpellCounterFromThisToTarget:
                {
                    // SpellCounterSpenders batch: Pitch-Black Power Stone targets 1 other face-up card that can take a
                    // Spell Counter (a lone one is taken with no prompt; the AI feeds its own holder).
                    var legal = SpellCounterSpendersRuntime.CollectHolderTargets(engine, clause, card);
                    if (human && legal.Count > 1)
                    {
                        OpenPick(engine, who, card,
                            new PickState { Step = Step.MoveTarget, Clause = clause, Link = link, Timing = timing },
                            EffectTargetKind.AnyCardOnField, legal,
                            "target 1 other face-up card that can take a Spell Counter, or CANCEL.");
                        return true;
                    }

                    SeatMove(engine, who, card, link, timing, clause,
                        legal.Count == 1 ? legal[0] : SpellCounterSpendersRuntime.AiPickHolder(engine, who, legal));
                    return true;
                }
                default:
                    engine.Chain.RemoveUnresolved(link);
                    ok = false;
                    return true;
            }
        }

        /// <summary>
        /// SpellCounterSpenders batch — Pitch-Black Power Stone: its target is chosen; the once-per-turn use is spent and
        /// the link is seated with the target on it.
        /// </summary>
        static void SeatMove(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link, ResponseTiming timing,
            EffectClause clause, CardInstance target)
        {
            SpellCounterSpendersRuntime.NoteMoveUsed(engine, card);
            var state = new LinkState { Clause = clause };
            if (target != null)
            {
                state.Targets.Add((target, who));
                link.Targets.Clear();
                link.Targets.Add(target);
                link.Target = target;
                engine.Log($"{card?.Name}: targets {target.Name}.");
            }

            Seat(engine, who, card, link, timing, state);
        }

        /// <summary>
        /// Soul Demolition's target step (Lua s.rmtg): the turn player selects 1 monster in their opponent's GY, then
        /// the other player selects 1 in theirs. A human selector picks it (also when the opponent activated the
        /// effect); the AI takes the best one. Once both are chosen the LP cost is paid and the link is seated.
        /// </summary>
        static bool ContinueGyTargets(DuelEngine engine, PickState st)
        {
            var who = st.Activator;
            var card = st.Source;
            var first = engine.TurnPlayer ?? who;
            var order = new[] { first, engine.OpponentOf(first) };
            while (st.Targets.Count < 2)
            {
                var selector = order[st.Targets.Count];
                var gyOwner = engine.OpponentOf(selector);
                var legal = GyTargets(engine, who, card, gyOwner).Where(c => st.Targets.All(t => t.card != c)).ToList();
                if (legal.Count == 0) break;
                var human = engine.IsHumanControlled(selector) && !(selector == who && st.ActivatorAuto);
                if (human && legal.Count > 1)
                {
                    st.Step = Step.GyTarget;
                    OpenPick(engine, selector, card, st, EffectTargetKind.MonsterInEitherGy, legal,
                        selector == who
                            ? "select 1 monster in your opponent's GY to banish, or CANCEL."
                            : $"{who.Name} activated it — select 1 monster in your opponent's GY to banish.");
                    return true;
                }

                var pick = legal.Count == 1 ? legal[0] : legal.OrderByDescending(AiGyValue).First();
                st.Targets.Add((pick, selector));
                engine.Log($"{card?.Name}: {selector.Name} selects {pick.Name} in {gyOwner.Name}'s GY.");
            }

            engine.PayLifePoints(who, st.Clause.PayLpAmount, $"Cost: pay {st.Clause.PayLpAmount} LP.");
            var state = new LinkState { Clause = st.Clause };
            state.Targets.AddRange(st.Targets);
            st.Link.Targets.Clear();
            foreach (var (c, _) in st.Targets) st.Link.Targets.Add(c);
            st.Link.Target = st.Targets.Count > 0 ? st.Targets[0].card : null;
            Seat(engine, who, card, st.Link, st.Timing, state);
            return true;
        }

        /// <summary>Skull Lair: N is chosen; banish them all when there is no choice, else the player picks them.</summary>
        static bool ContinueBanishPicks(DuelEngine engine, DuelistState who, CardInstance card, PickState st)
        {
            var fodder = BanishableGyMonsters(engine, who, card).Where(c => !st.Banish.Contains(c)).ToList();
            var need = st.Count - st.Banish.Count;
            if (need <= 0 || fodder.Count <= need)
            {
                st.Banish.AddRange(fodder.Take(System.Math.Max(0, need)));
                PayBanish(engine, who, card, st);
                Seat(engine, who, card, st.Link, st.Timing, new LinkState { Clause = st.Clause, Level = st.Count });
                return true;
            }

            st.Step = Step.BanishPicks;
            OpenPick(engine, who, card, st, EffectTargetKind.BanishFromYourGy, fodder,
                $"banish {need} more monster(s) from your GY (cost: {st.Count} in all), or CANCEL.");
            return true;
        }

        static void OpenPick(DuelEngine engine, DuelistState who, CardInstance card, PickState st,
            EffectTargetKind kind, List<CardInstance> legal, string prompt)
        {
            var p = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = kind,
                UsesTextProgram = true,
                AwaitingIgnitionCost = st.Step != Step.ResolveDestroy,
                HoldsChainResolution = st.Step == Step.ResolveDestroy,
                CostPicksRemaining = 1,
                PromptOverride = prompt
            };
            p.LegalTargets.AddRange(legal);
            s_picks.Add(p, st);
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
        }

        static CardInstance NumberToken(int n) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = $"Banish {n} (Level {n})",
                type = NumberTokenKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = n,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        static void PayDiscard(DuelEngine engine, DuelistState who, CardInstance discard)
        {
            var gone = MonsterEffects.DiscardFromHandByInstance(who, discard);
            if (gone != null) engine.Log($"Cost: discard {gone.Name}.");
        }

        static void PayBanish(DuelEngine engine, DuelistState who, CardInstance source, PickState st)
        {
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                foreach (var g in st.Banish)
                {
                    // v163: a Spirit Elimination substitute is a monster `who` controls.
                    if (g == null) continue;
                    var onField = who.TryFindMonster(g, out _);
                    if (!who.Graveyard.Contains(g) && !onField) continue;
                    engine.BanishCard(who, g, by: who);
                    engine.Log(onField
                        ? $"Cost: banish {g.Name} from the field (Spirit Elimination)."
                        : $"Cost: banish {g.Name} from the GY.");
                }
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }
        }

        /// <summary>
        /// Every choice of the activation is made and its cost is paid: record the link's state, then the opponent
        /// may respond to it (or the chain resolves). A link answering a response window closes that window
        /// (TextEffectRuntime.OpenSeatedResponseChainWindow), so play goes on from it once the chain has resolved.
        /// </summary>
        static void Seat(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link, ResponseTiming timing,
            LinkState state)
        {
            s_links.Remove(link);
            s_links.Add(link, state);
            if (!engine.IsHumanControlled(who)) NoteAiUse(engine, card);
            engine.Log($"Activate: {card.Name} (effect) — Chain Link {link.LinkNumber}.");
            if (state.Clause?.Action == EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost)
                engine.Log($"{card.Name}: {state.Level} monster(s) banished — destroys a face-up Level {state.Level} " +
                           "monster when it resolves.");
            if (timing == ResponseTiming.ChainResponse)
            {
                engine.ClearPendingResponse();
                if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }

                return;
            }

            if (timing != ResponseTiming.None)
            {
                TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, timing);
                return;
            }

            if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
            {
                if (engine.Chain.HasLinks && !engine.Chain.IsResolving && !engine.IsResolvingChain)
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }
            }
            else
                engine.NotifyPublic();
        }

        // ───────────────────────────── player picks ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.TryResolveWithTarget hook: a pick this runtime opened. Returns false for any other pick.
        /// </summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || target == null || !s_picks.TryGetValue(p, out var st)) return false;
            if (!p.LegalTargets.Contains(target)) return true;
            var who = p.Controller;
            var card = p.Card;
            switch (st.Step)
            {
                case Step.Discard:
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    PayDiscard(engine, who, target);
                    Seat(engine, who, card, st.Link, st.Timing,
                        new LinkState { Clause = st.Clause, Answered = st.Link?.TargetLink });
                    ok = true;
                    return true;
                case Step.MoveTarget:
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    SeatMove(engine, who, card, st.Link, st.Timing, st.Clause, target);
                    ok = true;
                    return true;
                case Step.GyTarget:
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    st.Targets.Add((target, who));
                    engine.Log($"{card?.Name}: {who?.Name} selects {target.Name}.");
                    ok = ContinueGyTargets(engine, st);
                    return true;
                case Step.ChooseCount:
                    if (target.CardId != 0 || target.Def?.type != NumberTokenKind) return true;
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    st.Count = target.Def.level;
                    engine.Log($"{card?.Name}: banish {st.Count}.");
                    ok = ContinueBanishPicks(engine, who, card, st);
                    return true;
                case Step.BanishPicks:
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    st.Banish.Add(target);
                    ok = ContinueBanishPicks(engine, who, card, st);
                    return true;
                case Step.ResolveDestroy:
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    DestroySelected(engine, who, card, target);
                    engine.ResumeChainAfterChoice();
                    engine.NotifyPublic();
                    ok = true;
                    return true;
            }

            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook: Cancel at a cost pick undoes the activation (nothing was paid: the
        /// banish picks are only collected until the last one); the selection at resolution is mandatory.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var st)) return false;
            if (st.Step == Step.ResolveDestroy || (st.Step == Step.GyTarget && p.Controller != st.Activator))
            {
                engine.Log($"{p.Card?.Name ?? "Effect"}: this selection is mandatory — tap a monster.");
                engine.NotifyPublic();
                return true;
            }

            s_picks.Remove(p);
            engine.ClearPendingActivation();
            if (st.Link != null && !st.Link.Resolved)
                engine.Chain.RemoveUnresolved(st.Link);
            engine.Log($"{p.Card?.Name ?? "Effect"}: activation cancelled.");
            engine.ClearIdleChain();
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── resolution ─────────────────────────────

        /// <summary>
        /// Called by the chain driver for every link it resolves, before its negation check. Returns false for any
        /// link that is not a face-up Quick Effect link. For one: applies the effect recorded at activation (a negated
        /// link, or an effect negated on the field, does nothing; the card is never sent to the GY by it).
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsQuickEffectLink(link)) return false;
            var card = link.Card;
            var who = link.Controller;
            var name = card?.Name ?? "Effect";
            if (!s_links.TryGetValue(link, out var state) || state.Clause == null)
            {
                engine.Log($"{name}: its effect has nothing to resolve.");
                return true;
            }

            s_links.Remove(link);
            if (link.Negated)
            {
                engine.Log($"{name}: its effect is negated.");
                return true;
            }

            var onField = card != null && who != null && card.FaceUp && who.TryFindSpellTrap(card, out _);
            var negatedBy = ContinuousNegationRuntime.NegatedBy(engine, card);
            if (negatedBy != null || (onField && card.IsNegated))
            {
                engine.Log($"{name}: its effect is negated{(negatedBy != null ? $" by {negatedBy.Name}" : "")}.");
                engine.NotifyPublic();
                return true;
            }

            switch (state.Clause.Action)
            {
                case EffectActionKind.InflictDamageToOpponent:
                    // Lua s.operation: if not e:GetHandler():IsRelateToEffect(e) then return end.
                    if (!onField)
                    {
                        engine.Log($"{name} is no longer on the field — its effect does nothing.");
                        break;
                    }

                    var opp = engine.OpponentOf(who);
                    engine.Log($"{name}: resolves — {state.Clause.Amount} damage.");
                    engine.ApplyEffectDamage(opp, state.Clause.Amount, name);
                    break;
                case EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost:
                    ResolveSkullLair(engine, who, card, state.Level);
                    break;
                // SpellCounterSpenders batch: Pitch-Black Power Stone moves 1 Spell Counter to its target.
                case EffectActionKind.MoveSpellCounterFromThisToTarget:
                    SpellCounterSpendersRuntime.ResolveMove(engine, who, card,
                        state.Targets.Count > 0 ? state.Targets[0].card : null);
                    break;
                case EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish:
                    // Lua s.rmop: the targets still related (in the GY) are banished; each by the player who selected it.
                    foreach (var (target, selector) in state.Targets)
                    {
                        var owner = engine.Player.Graveyard.Contains(target) ? engine.Player
                            : engine.Opponent.Graveyard.Contains(target) ? engine.Opponent : null;
                        // ForcedActivationAndSubstitution batch (v163): a Spirit Elimination substitute targeted on the
                        // field is banished from there.
                        var fieldOwner = owner == null ? engine.ControllerOf(target) : null;
                        if (fieldOwner != null && fieldOwner.TryFindMonster(target, out _))
                        {
                            engine.BanishCard(fieldOwner, target, by: selector);
                            if (fieldOwner.Banished.Contains(target) || engine.OpponentOf(fieldOwner).Banished.Contains(target))
                                engine.Log($"{name}: {target.Name} is banished.");
                            continue;
                        }

                        if (owner == null)
                        {
                            engine.Log($"{name}: {target.Name} is no longer in the GY.");
                            continue;
                        }

                        if (ContinuousProtections.GyMovementLocked(engine, card, target))
                        {
                            engine.Log($"{name}: {target.Name} cannot leave the GY.");
                            continue;
                        }

                        engine.BanishCard(owner, target, by: selector);
                        if (owner.Banished.Contains(target))
                            engine.Log($"{name}: {target.Name} is banished.");
                    }

                    break;
                case EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt:
                {
                    // Lua s.disop: this card still on the field, NegateEffect(ev), and the Trap Card still related → destroy.
                    var answered = state.Answered;
                    if (!onField)
                    {
                        engine.Log($"{name} is no longer on the field — its effect does nothing.");
                        break;
                    }

                    if (answered == null || answered.Resolved || answered.Negated)
                    {
                        engine.Log($"{name}: there is no effect left to negate.");
                        break;
                    }

                    answered.Negated = true;
                    if (answered.Card != null)
                        Presentation.ArInteraction.ActivationBeat.NoteNegated(answered.Card.InstanceId);
                    engine.Log($"{name}: negates the effect of {answered.Card?.Name}.");
                    var trap = answered.Card;
                    var trapOwner = trap != null ? engine.ControllerOf(trap) : null;
                    if (trap != null && trapOwner != null && trapOwner.TryFindSpellTrap(trap, out _))
                        TextEffectRuntime.DestroyCard(engine, trap, card, byPlayer: who);
                    break;
                }
            }

            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// Skull Lair's operation: select 1 face-up monster of Level <paramref name="level"/> on either field (not a
        /// target; chosen now) and destroy it. A player with a choice picks it while the chain waits.
        /// </summary>
        static void ResolveSkullLair(DuelEngine engine, DuelistState who, CardInstance card, int level)
        {
            var name = card?.Name ?? "Effect";
            if (level <= 0) return;
            var candidates = FaceUpOfLevel(engine, level);
            if (candidates.Count == 0)
            {
                engine.Log($"{name}: no face-up Level {level} monster — nothing is destroyed.");
                return;
            }

            if (candidates.Count > 1 && engine.IsHumanControlled(who))
            {
                OpenPick(engine, who, card, new PickState { Step = Step.ResolveDestroy, Count = level },
                    EffectTargetKind.FaceUpMonsterOnField, candidates,
                    $"select 1 face-up Level {level} monster to destroy.");
                return;
            }

            DestroySelected(engine, who, card,
                candidates.Count == 1 ? candidates[0] : AiPickVictim(engine, who, candidates));
        }

        static void DestroySelected(DuelEngine engine, DuelistState who, CardInstance card, CardInstance victim)
        {
            if (victim == null) return;
            engine.Log($"{card?.Name ?? "Effect"}: destroys {victim.Name} (Level {victim.Level}).");
            TextEffectRuntime.DestroyCard(engine, victim, card, byPlayer: who);
        }

        // ───────────────────────────── AI ─────────────────────────────

        static long AiGyValue(CardInstance c) => c?.Def == null ? 0 : System.Math.Max(0, c.Def.atk) + c.Def.level * 10L;

        /// <summary>The opponent's strongest monster first; one of the AI's own only when nothing else is left.</summary>
        static CardInstance AiPickVictim(DuelEngine engine, DuelistState who, List<CardInstance> candidates)
        {
            var opp = engine.OpponentOf(who);
            var theirs = candidates.Where(m => opp != null && opp.TryFindMonster(m, out _))
                .OrderByDescending(m => m.CurrentAtk).FirstOrDefault();
            return theirs ?? candidates.OrderBy(m => m.CurrentAtk).First();
        }

        /// <summary>The N whose face-up opponent's monster is the strongest (the smallest N on a tie).</summary>
        static int AiPickCount(DuelEngine engine, DuelistState who, CardInstance card, List<int> choices)
        {
            var best = AiBestTarget(engine, who, card, choices);
            return best?.Level ?? choices[0];
        }

        static CardInstance AiBestTarget(DuelEngine engine, DuelistState who, CardInstance card, List<int> choices)
        {
            var opp = engine.OpponentOf(who);
            if (opp == null || choices == null || choices.Count == 0) return null;
            return opp.MonstersOnField()
                .Where(m => m?.Def != null && m.FaceUp && choices.Contains(m.Level))
                .OrderByDescending(m => m.CurrentAtk).ThenBy(m => m.Level).FirstOrDefault();
        }

        /// <summary>
        /// The AI uses this face-up card's Quick Effect now (its own open state through SimpleAi, or a response
        /// window through SpellTrapEffects.AiAutoRespond). Type Zero: lethal, its card about to be removed by a
        /// chain link, or a Spell it cannot activate now in its own Main Phase 2. Skull Lair: destroy the attacker,
        /// a summoned monster with 1500+ ATK, an opponent's 1500+ ATK monster in its own Main Phase 1, or anything of
        /// the opponent's while this card is about to be removed.
        /// </summary>
        public static bool AiWantsQuickEffect(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsFaceUpQuickCard(ai, card, prog)) return false;
            if (!CanActivateQuick(engine, ai, card, prog, out _)) return false;
            var clause = QuickClause(prog);
            var opp = engine.OpponentOf(ai);
            var pr = engine.PendingResponse;
            var timing = pr != null && pr.Responder == ai ? pr.Timing : ResponseTiming.None;
            var threatened = timing == ResponseTiming.ChainResponse && ThreatenedByChain(engine, ai, card);
            switch (clause.Action)
            {
                case EffectActionKind.InflictDamageToOpponent:
                    if (opp != null && opp.LifePoints <= clause.Amount) return true;
                    if (threatened) return true;
                    return timing == ResponseTiming.None && engine.TurnPlayer == ai && engine.Phase == DuelPhase.Main2 &&
                           SpellsInHand(ai).Any(s => !engine.CanActivateSpellTrap(ai, s, fromHand: true));
                case EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost:
                {
                    var choices = CountChoices(engine, ai, card);
                    var best = AiBestTarget(engine, ai, card, choices);
                    if (best == null) return false;
                    if (threatened) return true;
                    if (timing == ResponseTiming.AttackDeclared)
                        return pr.Attacker != null && pr.Attacker.FaceUp && choices.Contains(pr.Attacker.Level) &&
                               opp.TryFindMonster(pr.Attacker, out _);
                    if (timing == ResponseTiming.MonsterSummoned)
                        return pr.Summoned != null && pr.Summoned.FaceUp && choices.Contains(pr.Summoned.Level) &&
                               pr.Summoned.CurrentAtk >= 1500 && opp.TryFindMonster(pr.Summoned, out _);
                    return timing == ResponseTiming.None && engine.TurnPlayer == ai &&
                           engine.Phase == DuelPhase.Main1 && best.CurrentAtk >= 1500;
                }
                case EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish:
                {
                    // Worth it when the opponent's best GY monster (the AI's pick) is a real one and at least as good as
                    // the AI's own best (the opponent's pick); its LP stay comfortable; once per turn per copy.
                    if (ai.LifePoints - clause.PayLpAmount < 2000 || UsedThisTurn(engine, card)) return false;
                    var theirs = GyTargets(engine, ai, card, opp).OrderByDescending(AiGyValue).FirstOrDefault();
                    var mine = GyTargets(engine, ai, card, ai).OrderByDescending(AiGyValue).FirstOrDefault();
                    if (theirs == null || mine == null) return false;
                    var worth = theirs.Def.atk >= 1500 && AiGyValue(theirs) >= AiGyValue(mine);
                    return worth && (threatened ||
                                     (timing == ResponseTiming.None && engine.TurnPlayer == ai && engine.InMainPhase));
                }
                // SpellCounterSpenders batch: Pitch-Black Power Stone feeds the AI's own holder in its Main Phase.
                case EffectActionKind.MoveSpellCounterFromThisToTarget:
                    return SpellCounterSpendersRuntime.AiWantsMove(engine, ai, card, clause);
                case EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt:
                {
                    // Protect its own Dragon from the opponent's Trap.
                    var answered = pr?.ChainTarget;
                    if (timing != ResponseTiming.ChainResponse || answered == null || answered.Controller == ai)
                        return false;
                    var targets = new List<CardInstance>();
                    if (answered.Target != null) targets.Add(answered.Target);
                    targets.AddRange(answered.Targets.Where(t => t != null));
                    return targets.Any(t => ai.TryFindMonster(t, out _) && t.FaceUp &&
                                            ClassicEraTemplates.RaceMatches(t.Race, clause.RaceFilter));
                }
            }

            return false;
        }

        /// <summary>
        /// The AI's own Main Phase: activate its Set copy of such a card (the activation only places it face-up) when
        /// the Quick Effect would have a use: Type Zero with a Spell in hand, Skull Lair with an opponent's face-up
        /// monster it can match, Soul Demolition with a face-up monster of its Type and a monster in each GY, The Dragon's
        /// Bead with a face-up monster of its Type and a card to discard. SimpleAi asks this after
        /// <see cref="AiWantsQuickEffect"/>.
        /// </summary>
        public static bool AiWantsToActivateSetCopy(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null || card.FaceUp || card.SetThisTurn) return false;
            if (engine.TurnPlayer != ai || !engine.InMainPhase || !ai.TryFindSpellTrap(card, out _)) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled || !prog.HasTiming(EffectTiming.FaceUpQuickEffect)) return false;
            var clause = QuickClause(prog);
            var opp = engine.OpponentOf(ai);
            bool OwnFaceUp(string race) => ai.MonstersOnField().Any(m =>
                m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, race));
            switch (clause?.Action)
            {
                case EffectActionKind.InflictDamageToOpponent:
                    return SpellsInHand(ai).Count > 0;
                case EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost:
                    return AiBestTarget(engine, ai, card, CountChoices(engine, ai, card)) != null;
                case EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish:
                    return OwnFaceUp(clause.RaceFilter) && GyTargets(engine, ai, card, opp).Count > 0 &&
                           GyTargets(engine, ai, card, ai).Count > 0;
                case EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt:
                    return OwnFaceUp(clause.RaceFilter) && ai.Hand.Count > 0;
                case EffectActionKind.MoveSpellCounterFromThisToTarget:
                    return SpellCounterSpendersRuntime.AiWantsActivateSetStone(engine, ai, card);
            }

            return false;
        }

        sealed class TurnMark
        {
            public int Turn = -1;
        }

        static readonly ConditionalWeakTable<CardInstance, TurnMark> s_aiUsedTurn = new();

        static bool UsedThisTurn(DuelEngine engine, CardInstance card) =>
            s_aiUsedTurn.TryGetValue(card, out var mark) && mark.Turn == engine.TurnNumber;

        /// <summary>The AI used this card's Quick Effect this turn (Soul Demolition's once-per-turn AI habit).</summary>
        static void NoteAiUse(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            s_aiUsedTurn.GetValue(card, _ => new TurnMark()).Turn = engine.TurnNumber;
        }

        /// <summary>An unresolved opponent's link on the chain targets <paramref name="card"/>.</summary>
        static bool ThreatenedByChain(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            foreach (var link in engine.Chain.Links)
            {
                if (link == null || link.Controller == ai || link.Negated || link.Resolved) continue;
                if (link.Target == card || link.Targets.Contains(card)) return true;
            }

            return false;
        }
    }
}
