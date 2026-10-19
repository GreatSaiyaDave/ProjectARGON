using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvHandProceduresAndNamedSummons" runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item>Activation conditions: <see cref="OnlyOpponentControlsMonsters"/> (Cyber Dragon's procedure) and
    /// <see cref="NoOtherCardsOnFieldOrInHand"/> (Elemental HERO Bubbleman's draw), read by the
    /// TextEffectRuntime.ActivationConditionsMet chain (<see cref="TryActivationCondition"/>) and, for "When this card is
    /// Summoned" triggers, by TextEffectRuntime.TryResolveThisCardSummoned (<see cref="SummonTriggerConditionMet"/>, which
    /// also reads King's Knight's <see cref="TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed"/>).</item>
    /// <item>Des Frog: <see cref="StartDesFrog"/> — 1 to min(usable zones, named cards in the GY) copies from the hand and/or
    /// Deck, chosen one at a time (CANCEL = done; at the first pick CANCEL declines the optional trigger), Special Summoned
    /// together face-up.</item>
    /// <item>Water Dragon: <see cref="ApplyBoard"/> (each board refresh: "ATK becomes 0" for FIRE or Pyro monsters on the
    /// field) and <see cref="ResolveGyGroups"/> (its destroyed-and-sent trigger: target 2 + 1 named monsters in the GY,
    /// Special Summon them together).</item>
    /// <item>Winged Kuriboh LV10: a Spell Speed 2 monster Quick Effect in the opponent's Battle Phase (not in the Damage
    /// Step): <see cref="CanActivateLv10"/>, <see cref="TryActivateLv10"/> (Tribute this card as the cost, seat the link),
    /// <see cref="TryResolveLink"/> / <see cref="ResolveLv10"/> (destroy the opponent's Attack Position monsters together,
    /// then burn their printed ATK).</item>
    /// </list>
    /// </summary>
    public static class CrvHandProceduresAndNamedSummonsRuntime
    {
        /// <summary>
        /// ActivationCondition (Cyber Dragon, Lua s.spcon): you control no monster, your opponent controls 1 or more, and you
        /// have a usable Monster Zone.
        /// </summary>
        public const string OnlyOpponentControlsMonsters = "Crv.OnlyOpponentControlsMonsters";

        /// <summary>
        /// ActivationCondition (Elemental HERO Bubbleman, Lua s.condition + IsPlayerCanDraw): you control no other card
        /// (a resolved Spell/Trap that is about to leave the field, STATUS_LEAVE_CONFIRMED, does not count), you have no card
        /// in your hand, and your Deck holds the cards to draw.
        /// </summary>
        public const string NoOtherCardsOnFieldOrInHand = "Crv.NoOtherCardsOnFieldOrInHand";

        /// <summary><see cref="ChainLink.EffectKey"/> of Winged Kuriboh LV10's Quick Effect link.</summary>
        public const string Lv10LinkKey = "CrvHandProceduresAndNamedSummons.MonsterQuickEffect";

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False for another batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case OnlyOpponentControlsMonsters:
                    // The condition of the card's own hand procedure only (checked by OwnHandProcedureAllows); it never
                    // gates the card's other effects.
                    ok = c.Action == EffectActionKind.SpecialSummonThisFromHandByOwnProcedure || OnlyOpponentControls(engine, who);
                    reason = "Only while your opponent controls a monster and you control none (and a Monster Zone is free).";
                    return true;
                case NoOtherCardsOnFieldOrInHand:
                    ok = ControlsNothingElse(engine, who, card) && CanDraw(who, c);
                    reason = "You must control no other cards and have no cards in your hand.";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// NegationsAndGySummonsRuntime.CanSummonByOwnHandProcedure: Cyber Dragon's procedure condition (Lua s.spcon) on the
        /// card's own hand procedure clause. True for a procedure without it.
        /// </summary>
        public static bool OwnHandProcedureAllows(DuelEngine engine, DuelistState who, EffectClause proc, out string reason)
        {
            reason = "OK";
            if (!IsConditionalOwnHandProcedure(proc) || OnlyOpponentControls(engine, who)) return true;
            reason = "Only while your opponent controls a monster and you control none (and a Monster Zone is free).";
            return false;
        }

        /// <summary>The card's own hand procedure carries Cyber Dragon's condition (AI: always worth it when legal).</summary>
        public static bool IsConditionalOwnHandProcedure(EffectClause proc) =>
            proc != null && proc.Action == EffectActionKind.SpecialSummonThisFromHandByOwnProcedure &&
            proc.ActivationCondition == OnlyOpponentControlsMonsters;

        static bool OnlyOpponentControls(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            var opp = engine.OpponentOf(who);
            return who.MonsterCount == 0 && opp != null && opp.MonsterCount > 0 &&
                   ZoneCapsRuntime.UsableMonsterZones(who) > 0;
        }

        static bool CanDraw(DuelistState who, EffectClause c) =>
            who?.Deck != null && who.Deck.Count >= Math.Max(1, c?.Amount ?? 1);

        /// <summary>
        /// Lua s.condition: no card other than <paramref name="card"/> in <paramref name="who"/>'s hand or on their field
        /// (Monster Zones, Spell &amp; Trap Zones, Field Zone), except a card whose departure is already confirmed
        /// (<see cref="LeaveConfirmed"/>).
        /// </summary>
        public static bool ControlsNothingElse(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (who == null) return false;
            if (who.Hand.Any(h => h != null && !ReferenceEquals(h, card))) return false;
            if (who.MonstersOnField().Any(m => m != null && !ReferenceEquals(m, card))) return false;
            return !who.SpellTrapsOnField().Any(st => st != null && !ReferenceEquals(st, card) && !LeaveConfirmed(engine, st));
        }

        /// <summary>
        /// ygopro STATUS_LEAVE_CONFIRMED: while a Chain resolves, a face-up Spell/Trap that does not stay on the field once
        /// activated and is no longer waiting on the Chain (DuelEngine.ResolveChainStack pops each link as it resolves) has
        /// resolved or is resolving now — it is sent to the GY when the Chain ends.
        /// </summary>
        static bool LeaveConfirmed(DuelEngine engine, CardInstance st)
        {
            if (engine?.Chain == null || st?.Def == null || !st.FaceUp || !(st.Def.IsSpell || st.Def.IsTrap)) return false;
            if (!engine.IsResolvingChain && !engine.Chain.IsResolving) return false;
            if (SpellTrapEffects.StaysOnFieldAfterActivate(st, CompiledEffectCache.GetOrCompile(st.Def))) return false;
            return !engine.Chain.Links.Any(l => l != null && ReferenceEquals(l.Card, st));
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveThisCardSummoned: a "When this card is Summoned" clause whose condition (King's
        /// Knight's face-up "Queen's Knight", Bubbleman's empty hand and field) does not hold now cannot be activated. Every
        /// other clause passes.
        /// </summary>
        public static bool SummonTriggerConditionMet(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (engine == null || who == null || c == null) return true;
            switch (c.ActivationCondition)
            {
                case NoOtherCardsOnFieldOrInHand:
                    if (!ControlsNothingElse(engine, who, card))
                    {
                        engine.Log($"{card?.Name}: you control another card or have a card in your hand — its effect " +
                                   "cannot be activated.");
                        return false;
                    }

                    if (!CanDraw(who, c))
                    {
                        engine.Log($"{card?.Name}: not enough cards in the Deck to draw {c.Amount}.");
                        return false;
                    }

                    return true;
                case TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed:
                    if (who.MonstersOnField().Any(m => m != null && m.FaceUp && m.IsNamed(c.ConditionName)))
                        return true;
                    engine.Log($"{card?.Name}: you control no face-up \"{c.ConditionName}\" — its effect cannot be activated.");
                    return false;
                default:
                    return true;
            }
        }

        // ───────────────────────────── Des Frog ─────────────────────────────

        sealed class Pick
        {
            public CardInstance Card;
            public BattlePosition Position;
            public int Zone = -1;
        }

        static bool NameIs(CardDef def, string name) =>
            def != null && !string.IsNullOrEmpty(name) && string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The hand copies (each instance) and the Deck copies (one fresh instance per passcode that still has a copy not yet
        /// chosen) named <paramref name="name"/> that can be Special Summoned by <paramref name="source"/>.
        /// </summary>
        static List<CardInstance> HandOrDeckCandidates(DuelEngine engine, DuelistState who, CardInstance source, string name,
            List<Pick> picks)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null) return list;
            foreach (var h in who.Hand)
                if (h?.Def != null && h.Def.IsMonster && NameIs(h.Def, name) && !picks.Any(p => ReferenceEquals(p.Card, h)) &&
                    SpecialSummonLegality.CanBeSpecialSummoned(engine, who, h, source, SummonOrigin.Hand))
                    list.Add(h);
            if (engine.Database == null || who.Deck == null) return list;
            foreach (var id in who.Deck.Distinct().ToList())
            {
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster || !NameIs(def, name)) continue;
                var copies = who.Deck.Count(x => x == id);
                var chosen = picks.Count(p => p.Card.CardId == id && !who.Hand.Contains(p.Card));
                if (copies <= chosen) continue;
                var inst = engine.CreateCardInstance(id);
                if (SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck))
                    list.Add(inst);
            }

            return list;
        }

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonOwnNameFromHandOrDeckUpToNamedInGy"/>: <paramref name="who"/> chooses 1
        /// to min(usable Monster Zones, copies of ConditionName in their GY) monsters named NamedCard from the hand and/or
        /// Deck — a human taps one per Summon with its face-up position (CANCEL = done; at the first pick CANCEL Special
        /// Summons none, the trigger's "you can"); the AI takes as many as fit — then they are Special Summoned together.
        /// </summary>
        public static void StartDesFrog(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var name = clause.NamedCard;
            var counted = who.Graveyard.Count(g => g?.Def != null && NameIs(g.Def, clause.ConditionName));
            var max = Math.Min(ZoneCapsRuntime.UsableMonsterZones(who), counted);
            var picks = new List<Pick>();
            if (max <= 0 || HandOrDeckCandidates(engine, who, source, name, picks).Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: nothing is Special Summoned (" +
                           (counted <= 0 ? $"no \"{clause.ConditionName}\" in the GY" :
                               max <= 0 ? "no free Monster Zone" : $"no \"{name}\" in the hand or Deck") + ").");
                return;
            }

            AskFrog(engine, who, source, name, max, picks);
        }

        static void AskFrog(DuelEngine engine, DuelistState who, CardInstance source, string name, int max, List<Pick> picks)
        {
            List<CardInstance> Candidates() =>
                picks.Count >= max ? new List<CardInstance>() : HandOrDeckCandidates(engine, who, source, name, picks);

            if (Candidates().Count == 0)
            {
                SummonHandOrDeckPicks(engine, who, source, picks);
                return;
            }

            var first = picks.Count == 0;
            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInHandOrDeck,
                Prompt = first
                    ? $"{source?.Name}: you can Special Summon up to {max} \"{name}\" from your hand or Deck — tap one, then " +
                      "Summon ATK or Summon DEF (face-up). CANCEL summons none."
                    : $"{source?.Name}: tap another \"{name}\" to Special Summon it too (up to {max - picks.Count} more; " +
                      "CANCEL = done).",
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                // The AI keeps its hand: Deck copies first.
                AiChoose = list => list.OrderBy(c => who.Hand.Contains(c) ? 1 : 0).FirstOrDefault(),
                Cancel = () =>
                {
                    if (picks.Count == 0) engine.Log($"{source?.Name}: no monster is Special Summoned.");
                    SummonHandOrDeckPicks(engine, who, source, picks);
                },
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        SummonHandOrDeckPicks(engine, who, source, picks);
                        return;
                    }

                    picks.Add(new Pick
                    {
                        Card = chosen,
                        Position = human && engine.PendingSpecialSummonPosition.HasValue
                            ? engine.PendingSpecialSummonPosition.Value
                            : DeckSummonsAndSarcophagiRuntime.AiPosition(chosen),
                        Zone = human ? engine.PendingSpecialSummonZone : -1
                    });
                    engine.ClearPendingSpecialSummonPlacement();
                    AskFrog(engine, who, source, name, max, picks);
                }
            });
        }

        static void SummonHandOrDeckPicks(DuelEngine engine, DuelistState who, CardInstance source, List<Pick> picks)
        {
            if (picks.Count == 0) return;
            NegationsAndGySummonsRuntime.BeginGroupSpecialSummon(engine);
            try
            {
                foreach (var p in picks)
                {
                    if (engine.GameOver) break;
                    DeckSummonsAndSarcophagiRuntime.SummonFromHandOrDeck(engine, who, source, p.Card, p.Position, p.Zone);
                }
            }
            finally
            {
                NegationsAndGySummonsRuntime.EndGroupSpecialSummon(engine);
            }

            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        // ───────────────────────────── Water Dragon ─────────────────────────────

        static bool AuraMatches(CardInstance m, EffectClause c) =>
            m?.Def != null &&
            ((!string.IsNullOrEmpty(c.AttributeFilter) &&
              string.Equals(m.Attribute, c.AttributeFilter, StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrEmpty(c.RaceFilter) && ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter)));

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after the cleared modifiers and the other "ATK becomes" writers): each face-up,
        /// not negated monster with <see cref="EffectActionKind.SetAtkZeroOfAttributeOrRaceOnField"/> (Water Dragon) sets the
        /// ATK of every face-up monster on either field of that Attribute or Type to 0 (<see cref="CardInstance.SelfSetAtk"/>:
        /// the base every other modifier still adds to), unless that monster is unaffected by it.
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var sides = new[] { engine.Player, engine.Opponent };
            foreach (var side in sides)
            foreach (var src in side.MonstersOnField().ToList())
            {
                if (src?.Def == null || !src.FaceUp || src.IsNegated || engine.IsSummonPending(src)) continue;
                var prog = CompiledEffectCache.GetOrCompile(src);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c?.Action != EffectActionKind.SetAtkZeroOfAttributeOrRaceOnField) continue;
                    foreach (var victimSide in sides)
                    foreach (var m in victimSide.MonstersOnField())
                    {
                        if (m?.Def == null || !m.FaceUp || engine.IsSummonPending(m) || !AuraMatches(m, c)) continue;
                        if (ContinuousProtections.IsUnaffectedBy(engine, m, src)) continue;
                        m.SelfSetAtk = 0;
                    }
                }
            }
        }

        static List<CardInstance> GyNamed(DuelEngine engine, DuelistState who, CardInstance source, string name,
            ICollection<CardInstance> exclude) =>
            who.Graveyard
                .Where(g => g?.Def != null && g.Def.IsMonster && NameIs(g.Def, name) && !exclude.Contains(g) &&
                            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, g, source, SummonOrigin.Graveyard))
                .ToList();

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonTargetedNamedGroupsFromYourGy"/> (Water Dragon's trigger; Lua s.sptg /
        /// s.spop): with Amount + ConditionAmount usable Monster Zones and enough of each named monster in the GY,
        /// <paramref name="who"/> targets Amount NamedCard and ConditionAmount AltNamedCard (a human taps each with its
        /// face-up position; CANCEL at the first tap declines the optional trigger) and Special Summons them together.
        /// </summary>
        public static void ResolveGyGroups(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var name = source?.Name ?? "Effect";
            var firstN = Math.Max(1, clause.Amount);
            var secondN = Math.Max(1, clause.ConditionAmount);
            var none = new List<CardInstance>();
            if (ZoneCapsRuntime.UsableMonsterZones(who) < firstN + secondN ||
                GyNamed(engine, who, source, clause.NamedCard, none).Count < firstN ||
                GyNamed(engine, who, source, clause.AltNamedCard, none).Count < secondN)
            {
                engine.Log($"{name}: needs {firstN} \"{clause.NamedCard}\" and {secondN} \"{clause.AltNamedCard}\" in the GY " +
                           $"and {firstN + secondN} free Monster Zones — its effect cannot be activated.");
                return;
            }

            var picks = new List<Pick>();
            AskGyTarget(engine, who, source, clause, firstN, secondN, picks);
        }

        static void AskGyTarget(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause, int firstN,
            int secondN, List<Pick> picks)
        {
            if (engine.GameOver) return;
            if (picks.Count >= firstN + secondN)
            {
                SummonGyPicks(engine, who, source, picks);
                return;
            }

            var named = picks.Count < firstN ? clause.NamedCard : clause.AltNamedCard;
            var chosen = picks.Select(p => p.Card).ToList();
            List<CardInstance> Candidates() => GyNamed(engine, who, source, named, chosen);
            if (Candidates().Count == 0)
            {
                engine.Log($"{source?.Name}: no \"{named}\" left to target — nothing is Special Summoned.");
                return;
            }

            var first = picks.Count == 0;
            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{source?.Name}: target {firstN} \"{clause.NamedCard}\" and {secondN} \"{clause.AltNamedCard}\" in " +
                         $"your GY to Special Summon (face-up) — tap a \"{named}\" ({picks.Count + 1}/{firstN + secondN})" +
                         (first ? "; CANCEL = do not activate." : "."),
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.FirstOrDefault(),
                // Only the first tap may decline the optional trigger; once activated the targets are all chosen.
                Cancel = first ? () => engine.Log($"{source?.Name}: its effect is not activated.") : null,
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        if (first) engine.Log($"{source?.Name}: its effect is not activated.");
                        return;
                    }

                    picks.Add(new Pick
                    {
                        Card = pick,
                        Position = human && engine.PendingSpecialSummonPosition.HasValue
                            ? engine.PendingSpecialSummonPosition.Value
                            : DeckSummonsAndSarcophagiRuntime.AiPosition(pick),
                        Zone = human ? engine.PendingSpecialSummonZone : -1
                    });
                    engine.ClearPendingSpecialSummonPlacement();
                    AskGyTarget(engine, who, source, clause, firstN, secondN, picks);
                }
            });
        }

        /// <summary>
        /// Lua s.spop: the targets still in the GY are Special Summoned together, unless they no longer fit the usable
        /// Monster Zones (then none is).
        /// </summary>
        static void SummonGyPicks(DuelEngine engine, DuelistState who, CardInstance source, List<Pick> picks)
        {
            var name = source?.Name ?? "Effect";
            var live = picks.Where(p => who.Graveyard.Contains(p.Card)).ToList();
            if (live.Count == 0 || live.Count > ZoneCapsRuntime.UsableMonsterZones(who))
            {
                engine.Log($"{name}: the targets cannot all be Special Summoned — nothing is Special Summoned.");
                return;
            }

            NegationsAndGySummonsRuntime.BeginGroupSpecialSummon(engine);
            try
            {
                foreach (var p in live)
                {
                    if (engine.GameOver) break;
                    var card = p.Card;
                    if (!who.Graveyard.Contains(card) ||
                        !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, source, SummonOrigin.Graveyard,
                            requireZone: true))
                    {
                        engine.Log($"{name}: {card.Name} cannot be Special Summoned now.");
                        continue;
                    }

                    who.Graveyard.Remove(card);
                    DuelEngine.RecordOwnerOnFieldEntry(card, who, who);
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (engine.SpecialSummonToField(who, card, p.Position, true, SummonKind.SpecialSummon, p.Zone))
                    {
                        engine.Log($"{name}: {who.Name} Special Summons {card.Name} from the GY in face-up " +
                                   $"{(card.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                        continue;
                    }

                    engine.NextSpecialSummonFromGraveyard = false;
                    if (!who.Graveyard.Contains(card) && engine.ControllerOf(card) == null) who.Graveyard.Add(card);
                    engine.Log($"{name}: {card.Name} could not be Special Summoned — it stays in the GY.");
                }
            }
            finally
            {
                NegationsAndGySummonsRuntime.EndGroupSpecialSummon(engine);
            }

            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        // ───────────────────────────── Winged Kuriboh LV10 ─────────────────────────────

        static EffectClause Lv10Clause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.MonsterQuickEffect)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.DestroyOppAttackPositionThenBurnPrintedAtk);

        /// <summary>A face-up monster on <paramref name="who"/>'s field whose Quick Effect is this batch's (Winged Kuriboh LV10).</summary>
        public static bool IsLv10Card(DuelistState who, CardInstance card, CompiledCardProgram prog = null)
        {
            if (who == null || card?.Def == null || !card.Def.IsMonster || !card.FaceUp) return false;
            prog ??= CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled || Lv10Clause(prog) == null) return false;
            return who.TryFindMonster(card, out _);
        }

        /// <summary><paramref name="link"/> is Winged Kuriboh LV10's Quick Effect link.</summary>
        public static bool IsLv10Link(ChainLink link) => link != null && link.EffectKey == Lv10LinkKey;

        static List<CardInstance> OppAttackPosition(DuelEngine engine, DuelistState who)
        {
            var opp = engine?.OpponentOf(who);
            return opp == null
                ? new List<CardInstance>()
                : opp.MonstersOnField().Where(m => m != null && m.FaceUp && m.Position == BattlePosition.Attack).ToList();
        }

        /// <summary>
        /// Winged Kuriboh LV10's Quick Effect can be activated now by <paramref name="who"/> (Lua s.descon, Cost.SelfTribute,
        /// s.destg): it is the opponent's Battle Phase but not the Damage Step (no EFFECT_FLAG_DAMAGE_STEP), in a response
        /// window of <paramref name="who"/> (the opponent's open state, an attack declaration, a Summon, or a Chain that a
        /// Spell Speed 2 effect may join); its effects are not negated; it can be Tributed; the opponent controls an Attack
        /// Position monster.
        /// </summary>
        public static bool CanActivateLv10(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog,
            out string reason)
        {
            reason = "Not a face-up monster with this Quick Effect.";
            if (engine == null || !IsLv10Card(who, card, prog)) return false;
            if (engine.GameOver)
            {
                reason = "Not now.";
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

            if (card.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, card) != null)
            {
                reason = "This card's effects are negated.";
                return false;
            }

            if (ContinuousNegationRuntime.ActivationLocked(engine, who, card, out var lockReason))
            {
                reason = lockReason;
                return false;
            }

            if (engine.TurnPlayer == who || engine.Phase != DuelPhase.Battle)
            {
                reason = "Only during your opponent's Battle Phase.";
                return false;
            }

            if (engine.DamageSubStep != DamageSubStep.None)
            {
                reason = "Not during the Damage Step.";
                return false;
            }

            var pr = engine.PendingResponse;
            if (pr == null || pr.Responder != who)
            {
                reason = "Respond in a window of your opponent's Battle Phase.";
                return false;
            }

            switch (pr.Timing)
            {
                case ResponseTiming.ChainResponse:
                    if (pr.ChainAnswersOnly || !engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick))
                    {
                        reason = "A Spell Speed 2 effect cannot be chained to this Chain Link.";
                        return false;
                    }

                    break;
                case ResponseTiming.OpponentOpenState:
                    break;
                case ResponseTiming.AttackDeclared:
                    if (pr.AttackingPlayer == who)
                    {
                        reason = "Not in your own attack's window.";
                        return false;
                    }

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

            if (!NegationsAndGySummonsRuntime.CanTributeThisForChainAnswer(engine, who, card, out var tributeReason))
            {
                reason = tributeReason;
                return false;
            }

            if (OppAttackPosition(engine, who).Count == 0)
            {
                reason = "Your opponent controls no Attack Position monster.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>SpellTrapEffects.IsLegalResponseCard hook (before BattleBoosts): false when not Winged Kuriboh LV10.</summary>
        public static bool TryIsLegalResponse(DuelEngine engine, DuelistState who, CardInstance card, out bool legal)
        {
            legal = false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (!IsLv10Card(who, card, prog)) return false;
            legal = CanActivateLv10(engine, who, card, prog, out _);
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CollectLegalResponseCards: the response windows that do not list monsters themselves (the
        /// opponent's open state, an attack declaration, a Summon) offer Winged Kuriboh LV10's Quick Effect.
        /// </summary>
        public static void CollectResponses(DuelEngine engine, DuelistState who, ResponseTiming timing,
            List<CardInstance> list)
        {
            if (engine == null || who == null || list == null) return;
            if (timing != ResponseTiming.OpponentOpenState && timing != ResponseTiming.AttackDeclared &&
                timing != ResponseTiming.MonsterSummoned)
                return;
            foreach (var m in who.MonstersOnField())
                if (m != null && !list.Contains(m) && IsLv10Card(who, m) && CanActivateLv10(engine, who, m, null, out _))
                    list.Add(m);
        }

        /// <summary>
        /// SpellTrapEffects.BeginOrResolveManual hook (before BattleBoosts): Winged Kuriboh LV10's Quick Effect seats a Spell
        /// Speed 2 Chain Link, Tributes the card (cost), then the opponent may respond; the effect applies when the link
        /// resolves (<see cref="TryResolveLink"/>).
        /// </summary>
        public static bool TryActivateLv10(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick, out bool ok)
        {
            ok = false;
            if (engine == null || who == null || card?.Def == null || fromHand) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (!IsLv10Card(who, card, prog)) return false;
            if (!CanActivateLv10(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                return true;
            }

            var pr = engine.PendingResponse;
            var timing = pr != null && pr.Responder == who ? pr.Timing : ResponseTiming.None;
            var answered = pr?.ChainTarget;
            var link = engine.Chain.AddLink(who, card, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.MonsterZone,
                fromHand: false, wasSet: false, effectKey: Lv10LinkKey);
            if (link == null)
            {
                engine.Log($"{card.Name}: its effect cannot be activated now.");
                return true;
            }

            link.EffectKey = Lv10LinkKey;
            link.Speed = SpellSpeed.Speed2;
            link.Class = EffectClass.Quick;
            link.WasSetOnField = false;
            link.FromHand = false;
            if (timing == ResponseTiming.ChainResponse)
                link.TargetLink = answered;
            ok = true;

            // Cost: Tribute this card (Cost.SelfTribute; EVENT_RELEASE, REASON_COST).
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                NegationsAndGySummonsRuntime.TributeThisForChainAnswer(engine, who, card);
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }

            Seat(engine, who, card, link, timing);
            return true;
        }

        static void Seat(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link, ResponseTiming timing)
        {
            engine.Log($"Activate: {card.Name} (Quick Effect) — Chain Link {link.LinkNumber}.");
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

        /// <summary>
        /// DuelEngine.ResolveChainStack hook. False for any other link. A negated link does nothing; otherwise
        /// <see cref="ResolveLv10"/> (the card itself was Tributed as the cost, so it is not checked).
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsLv10Link(link)) return false;
            if (link.Negated)
            {
                engine.Log($"{link.Card?.Name ?? "Effect"}: its effect is negated.");
                return true;
            }

            ResolveLv10(engine, link.Controller ?? engine.ControllerOf(link.Card), link.Card);
            return true;
        }

        /// <summary>
        /// Lua s.desop: every Attack Position monster the opponent controls now is destroyed together (Duel.Destroy, by this
        /// card's effect); the damage is the sum of the printed ATK of the monsters actually destroyed (GetOperatedGroup,
        /// GetTextAttack, a negative value counts as 0), dealt to the opponent before their "sent to the GY" triggers.
        /// </summary>
        public static void ResolveLv10(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var opp = engine.OpponentOf(who);
            var hits = OppAttackPosition(engine, who);
            if (opp == null || hits.Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: your opponent controls no Attack Position monster.");
                return;
            }

            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in hits)
                    TextEffectRuntime.DestroyCard(engine, m, source, byPlayer: who);
                var damage = hits.Where(m => engine.ControllerOf(m) == null).Sum(m => Math.Max(0, m.Def?.atk ?? 0));
                if (damage > 0)
                    engine.ApplyEffectDamage(opp, damage, source?.Name, source);
            });
        }

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond: the AI Tributes its Winged Kuriboh LV10 at the player's attack declaration (the
        /// attacker is an Attack Position monster, so it is destroyed and its ATK burns).
        /// </summary>
        public static bool AiWantsLv10(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || !IsLv10Card(ai, card)) return false;
            var pr = engine.PendingResponse;
            return pr != null && pr.Responder == ai && pr.Timing == ResponseTiming.AttackDeclared &&
                   CanActivateLv10(engine, ai, card, null, out _);
        }
    }
}
