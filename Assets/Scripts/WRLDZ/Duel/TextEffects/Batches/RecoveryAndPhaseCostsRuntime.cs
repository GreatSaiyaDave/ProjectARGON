using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RecoveryAndPhaseCosts" runtime (<see cref="RecoveryAndPhaseCostsAtoms"/>). Reads compiled programs only —
    /// no card-id branches. Every player choice goes through
    /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(DuelEngine, DuelistState, CardInstance, CardPickRequest)"/>
    /// or an existing target flow (the AI and a lone candidate answer at once; a pick in the live Standby Phase holds it, an
    /// End Phase pick holds the End Phase).
    /// <list type="bullet">
    /// <item>Dimension Distortion (<see cref="SummonBanishedTarget"/>): condition <see cref="NoCardsInYourGy"/>; a targeted
    /// face-up monster in your banished pile that can be Special Summoned (<see cref="FilterTargets"/>, a free zone);
    /// the human's tap asks its face-up position (<see cref="TryPickAsksPlacement"/>).</item>
    /// <item>Dimension Fusion: condition <see cref="EitherPlayerCanSummonFaceUpBanished"/>; the summons are a
    /// FlipMassEffects plan (<see cref="FlipMassEffectsRuntime.ResolveEachPlayerSummonsFromOwnBanished"/>).</item>
    /// <item>Salvage: the generic AddFromGyToHand with an exact target count; <see cref="FilterTargets"/> applies the
    /// Attribute and the ATK ceiling to GY monster targets.</item>
    /// <item>Recycle (<see cref="ResolveRecycle"/>), Coach Goblin (<see cref="ResolveCoachGoblin"/>): optional own-turn
    /// Standby / End Phase triggers with a cost.</item>
    /// <item>Reload (<see cref="ShuffleHandIntoDeckThenDraw"/>): condition <see cref="AnotherCardInYourHand"/>.</item>
    /// <item>Manticore of Darkness (<see cref="TryFirePhaseClause"/>): either player's End Phase of the turn it was sent to
    /// the GY; a Type-filtered send cost from the hand or the face-up field; Special Summon it from the GY.</item>
    /// </list>
    /// </summary>
    public static class RecoveryAndPhaseCostsRuntime
    {
        /// <summary>ActivationCondition (Dimension Distortion): there are no cards in your GY (Lua GetFieldGroupCount == 0).</summary>
        public const string NoCardsInYourGy = "RecoveryNoCardsInYourGy";

        /// <summary>ActivationCondition (Dimension Fusion): either player has a free Monster Zone and a face-up banished
        /// monster of their own that can be Special Summoned (Lua s.tg chk).</summary>
        public const string EitherPlayerCanSummonFaceUpBanished = "RecoveryEitherPlayerCanSummonFaceUpBanished";

        /// <summary>ActivationCondition (Reload): a card in your hand other than this card (Lua IsExistingMatchingCard(
        /// IsAbleToDeck, tp, LOCATION_HAND, 0, 1, e:GetHandler())).</summary>
        public const string AnotherCardInYourHand = "RecoveryAnotherCardInYourHand";

        /// <summary>The AI pays Recycle's LP only while it keeps at least this many LP afterwards.</summary>
        public const int AiRecycleFloorLp = 3000;

        /// <summary>Count limit 1 per card (Lua SetCountLimit(1)): the turn, and the field / GY trip it was offered in.</summary>
        static readonly ConditionalWeakTable<CardInstance, StrongBox<(int turn, int trip)>> s_offered = new();

        static bool TryMarkOffered(DuelEngine engine, CardInstance card, int trip)
        {
            if (s_offered.TryGetValue(card, out var box) && box.Value.turn == engine.TurnNumber && box.Value.trip == trip)
                return false;
            s_offered.Remove(card);
            s_offered.Add(card, new StrongBox<(int, int)>((engine.TurnNumber, trip)));
            return true;
        }

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain: this batch's conditions. False for any other.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            switch (c.ActivationCondition)
            {
                case NoCardsInYourGy:
                    ok = who.Graveyard == null || who.Graveyard.Count == 0;
                    reason = "Only while there are no cards in your GY.";
                    return true;
                case EitherPlayerCanSummonFaceUpBanished:
                    ok = new[] { who, engine.OpponentOf(who) }.Any(side => CanSummonAnyBanished(engine, side, card));
                    reason = "Needs a player with a free Monster Zone and a face-up banished monster they can Special Summon.";
                    return true;
                case AnotherCardInYourHand:
                    ok = who.Hand != null && who.Hand.Any(h => h != null && !ReferenceEquals(h, card));
                    reason = "Needs another card in your hand.";
                    return true;
                default:
                    return false;
            }
        }

        static bool CanSummonAnyBanished(DuelEngine engine, DuelistState side, CardInstance source) =>
            side?.Banished != null && engine.FirstEmpty(side.MonsterZones) >= 0 &&
            side.Banished.Any(b => FlipMassEffectsRuntime.IsBanishedSummonable(engine, side, source, b));

        // ───────────────────────────── targets (Dimension Distortion, Salvage) ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CollectTargets tail filter. Dimension Distortion: only face-up banished monsters the controller
        /// can Special Summon, and only with a free Monster Zone (Lua s.filter + GetLocationCount &gt; 0). Salvage (a GY
        /// monster add with an Attribute or an ATK ceiling): the printed Attribute and "N or less ATK" (Lua IsAttribute /
        /// IsAttackBelow). Backup Soldier's non-Effect filter is applied in CollectTargets itself.
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance except,
            List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null) return;
            if (c.Action == EffectActionKind.SpecialSummonBanishedTarget &&
                c.Zone == EffectZoneFilter.ControllerBanishedFaceUpMonsters)
            {
                list.RemoveAll(t => !FlipMassEffectsRuntime.IsBanishedSummonable(engine, who, except, t) ||
                                    engine.FirstEmpty(who.MonsterZones) < 0);
                return;
            }

            if (c.Action == EffectActionKind.AddFromGyToHand && c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                !c.RequiresNonEffectMonster)
            {
                if (!string.IsNullOrEmpty(c.AttributeFilter))
                    list.RemoveAll(t => t?.Def == null ||
                                        !string.Equals(t.Def.attribute, c.AttributeFilter, StringComparison.OrdinalIgnoreCase));
                if (c.AmountIsAtkMax)
                    list.RemoveAll(t => t?.Def == null || t.Def.atk < 0 || t.Def.atk > c.Amount);
            }
        }

        /// <summary>
        /// SpellTrapEffects.PendingTargetIsSpecialSummon: Dimension Distortion's target pick summons the monster, so the UI
        /// asks its face-up position and a zone (kept until the Chain Link resolves). False (not handled) otherwise.
        /// </summary>
        public static bool TryPickAsksPlacement(PendingActivation pending, out bool asksPlacement)
        {
            asksPlacement = false;
            if (pending?.Card?.Def == null || pending.TargetKind != EffectTargetKind.MonsterInYourBanished) return false;
            var prog = CompiledEffectCache.GetOrCompile(pending.Card.Def);
            if (prog?.ClauseList == null ||
                !prog.ClauseList.Exists(c => c != null && c.RequiresTargetChoice &&
                                             c.Action == EffectActionKind.SpecialSummonBanishedTarget))
                return false;
            asksPlacement = true;
            return true;
        }

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonBanishedTarget"/> (Lua s.activate): if the target is still face-up in
        /// <paramref name="who"/>'s banished pile, Special Summon it to their field face-up (Attack, or the human's chosen
        /// position). A failed Summon leaves it banished.
        /// </summary>
        public static void SummonBanishedTarget(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null || target == null) return;
            if (!TargetedClassicsRuntime.IsFaceUpBanishedMonster(who, target))
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer banished — nothing is Special Summoned.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, target, source))
            {
                engine.Log($"{target.Name} is unaffected by {source?.Name ?? "this effect"}.");
                return;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, target, source, SummonOrigin.Banished))
            {
                engine.Log($"{target.Name} cannot be Special Summoned.");
                return;
            }

            who.Banished.Remove(target);
            DuelEngine.RecordOwnerOnFieldEntry(target, who, who);
            if (engine.SpecialSummonToField(who, target, BattlePosition.Attack, true))
            {
                engine.Log($"{source?.Name}: Special Summoned {target.Name} from the banished pile.");
                return;
            }

            target.ControlOwner = null;
            target.FaceUp = true;
            who.Banished.Add(target);
            engine.Log($"{target.Name} could not be Special Summoned — it stays banished.");
        }

        // ───────────────────────────── Recycle ─────────────────────────────

        static bool RecycleUsable(DuelEngine engine, DuelistState who, CardInstance source, int cost) =>
            source != null && source.FaceUp && !source.IsNegated && who.TryFindSpellTrap(source, out _) &&
            who.LifePoints >= cost && engine.Phase == DuelPhase.Standby && ReferenceEquals(engine.TurnPlayer, who);

        /// <summary>Non-Monster Cards in <paramref name="who"/>'s GY that can be targeted and returned (Lua s.filter).</summary>
        static List<CardInstance> RecycleTargets(DuelEngine engine, DuelistState who, CardInstance source) =>
            who.Graveyard
                .Where(g => g?.Def != null && !g.Def.IsMonster &&
                            !ContinuousProtections.CannotTargetCardInGy(engine, source, g) &&
                            !ContinuousProtections.GyMovementLocked(engine, source, g))
                .ToList();

        /// <summary>
        /// <see cref="EffectActionKind.PayLpReturnGyNonMonsterToDeckBottomAtYourStandby"/> (FirePhaseTriggersFor: the
        /// controller's own Standby Phase; the card is face-up and not negated there). Optional, once per turn: a human taps
        /// 1 non-Monster Card in their GY (CANCEL = not used) while the Standby Phase waits; the AI uses it while it keeps
        /// <see cref="AiRecycleFloorLp"/> LP. The LP is paid (cost), then the card goes to the bottom of the Deck if Recycle
        /// is still face-up on the field and the card is still in the GY.
        /// </summary>
        public static void ResolveRecycle(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var cost = Math.Max(0, clause.Amount);
            if (!RecycleUsable(engine, who, source, cost) || RecycleTargets(engine, who, source).Count == 0) return;
            if (!TryMarkOffered(engine, source, source.FieldEntryStamp)) return;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => RecycleUsable(engine, who, source, cost)
                    ? RecycleTargets(engine, who, source)
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.CardInEitherGy,
                Prompt = $"{source.Name}: tap 1 non-Monster Card in your GY to pay {cost} LP and return it to the bottom " +
                         "of your Deck, or CANCEL to not use it.",
                AskWithOneCandidate = true,
                AiChoose = list => who.LifePoints - cost >= AiRecycleFloorLp
                    ? list.OrderByDescending(g => g.Def.IsSpell).FirstOrDefault()
                    : null,
                Cancel = () => engine.Log($"{source.Name}: {who.Name} does not use its effect."),
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{source.Name}: {who.Name} does not use its effect.");
                        return;
                    }

                    if (!RecycleUsable(engine, who, source, cost) || !who.Graveyard.Contains(chosen))
                    {
                        engine.Log($"{source.Name}: it can no longer be used — nothing is paid.");
                        return;
                    }

                    engine.PayLifePoints(who, cost,
                        $"Cost: pay {cost} LP for {source.Name} → {who.Name} at {who.LifePoints - cost} LP.", isCost: true);
                    if (engine.GameOver) return;
                    engine.Log($"{source.Name} targets {chosen.Name} in {who.Name}'s GY.");
                    if (!source.FaceUp || !who.TryFindSpellTrap(source, out _))
                    {
                        engine.Log($"{source.Name} is no longer on the field — no effect.");
                        return;
                    }

                    BattleDestroyedGyRecoveryRuntime.PlaceGyTargetOnBottomOfDeck(engine, who, source, chosen);
                    engine.NotifyPublic();
                }
            });
        }

        // ───────────────────────────── Reload ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber"/> (Lua c22589918 s.activate): every card in
        /// <paramref name="who"/>'s hand goes to its owner's Deck, the Deck is shuffled, then <paramref name="who"/> draws as
        /// many cards as went from the hand. An empty hand: nothing happens.
        /// </summary>
        public static void ShuffleHandIntoDeckThenDraw(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who?.Hand == null) return;
            var hand = who.Hand.Where(h => h != null).ToList();
            if (hand.Count == 0)
            {
                engine.Log($"{source?.Name}: {who.Name} has no cards in hand — no effect.");
                return;
            }

            var decks = new List<DuelistState>();
            foreach (var h in hand)
            {
                who.Hand.Remove(h);
                var owner = PerCopyDeckRuntime.OwnersPile(who, h) ?? who;
                owner.Deck.Add(h.CardId);
                if (!decks.Contains(owner)) decks.Add(owner);
            }

            foreach (var d in decks)
                engine.Rng.Shuffle(d.Deck);
            engine.Log($"{source?.Name}: {who.Name} shuffles {hand.Count} card(s) from the hand into the Deck, then draws " +
                       $"{hand.Count}.");
            engine.Draw(who, hand.Count);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Coach Goblin ─────────────────────────────

        static bool IsNormalMonsterCard(CardInstance c) =>
            c?.Def != null && c.Def.IsMonster && c.Def.type != null &&
            c.Def.type.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0 &&
            c.Def.type.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) < 0;

        static bool CoachUsable(DuelEngine engine, DuelistState who, CardInstance source) =>
            source != null && source.FaceUp && !source.IsNegated && who.TryFindMonster(source, out _) &&
            engine.Phase == DuelPhase.End && ReferenceEquals(engine.TurnPlayer, who) && who.Deck.Count > 0;

        /// <summary>
        /// <see cref="EffectActionKind.ReturnHandNormalMonsterToDeckBottomDrawAtYourEndPhase"/> (FirePhaseTriggersFor: the
        /// controller's own End Phase, face-up, not negated). Optional, once per turn: a human taps 1 Normal Monster in their
        /// hand (CANCEL = not used); the AI always uses it with its weakest one. Cost: the card is revealed and put on the
        /// bottom of the Deck; then 1 card is drawn while this card is still face-up on the field.
        /// </summary>
        public static void ResolveCoachGoblin(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null) return;
            if (!CoachUsable(engine, who, source) || !who.Hand.Any(IsNormalMonsterCard)) return;
            if (!TryMarkOffered(engine, source, source.FieldEntryStamp)) return;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => CoachUsable(engine, who, source)
                    ? who.Hand.Where(IsNormalMonsterCard).ToList()
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"{source.Name}: tap 1 Normal Monster in your hand to reveal it and put it on the bottom of your " +
                         "Deck, then draw 1 card — or CANCEL to not use it.",
                AskWithOneCandidate = true,
                AiChoose = list => list.OrderBy(h => h.Def.atk).ThenBy(h => h.Name).FirstOrDefault(),
                Cancel = () => engine.Log($"{source.Name}: {who.Name} does not use its effect."),
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{source.Name}: {who.Name} does not use its effect.");
                        return;
                    }

                    if (!CoachUsable(engine, who, source) || !who.Hand.Contains(chosen))
                    {
                        engine.Log($"{source.Name}: it can no longer be used.");
                        return;
                    }

                    engine.Log($"{who.Name} reveals {chosen.Name} and places it on the bottom of the Deck (cost of " +
                               $"{source.Name}).");
                    who.Hand.Remove(chosen);
                    (PerCopyDeckRuntime.OwnersPile(who, chosen) ?? who).Deck.Add(chosen.CardId);
                    if (!source.FaceUp || !who.TryFindMonster(source, out _))
                    {
                        engine.Log($"{source.Name} is no longer face-up on the field — no card is drawn.");
                        return;
                    }

                    engine.Draw(who, 1);
                    engine.NotifyPublic();
                }
            });
        }

        // ───────────────────────────── Manticore of Darkness ─────────────────────────────

        /// <summary>The cost choices: monsters of the clause's Types in the hand, or face-up in a Monster Zone (only those
        /// when no zone is free: the cost must free one).</summary>
        static List<CardInstance> ManticoreCosts(DuelEngine engine, DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            var zoneFree = engine.FirstEmpty(who.MonsterZones) >= 0;
            if (zoneFree)
                list.AddRange(who.Hand.Where(h => h?.Def != null && h.Def.IsMonster &&
                                                  ClassicEraTemplates.RaceMatches(h.Def.race, c.RaceFilter)));
            list.AddRange(who.MonstersOnField().Where(m => m?.Def != null && m.FaceUp && m.Def.IsMonster &&
                                                           ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter)));
            return list;
        }

        static bool ManticoreLive(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.Phase == DuelPhase.End && who.Graveyard.Contains(card) &&
            !NegationMonstersRuntime.NegatedInGy(engine, card) &&
            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, card, SummonOrigin.Graveyard);

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor, before its turn filter (the trigger is either player's End Phase): true
        /// when <paramref name="c"/> is this batch's GY End Phase clause (handled, offered or not), false for any other.
        /// Manticore of Darkness (Lua c77121851): in the GY, sent there this turn (not returned from the banished pile), once
        /// per turn: a human taps the cost monster (CANCEL = not used), then taps this card to choose its face-up position;
        /// the AI uses it with a cost weaker than this card. The cost is sent to the GY, then this card is Special Summoned
        /// from the GY if it is still there.
        /// </summary>
        public static bool TryFirePhaseClause(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            EffectTiming timing)
        {
            if (c == null || c.Action != EffectActionKind.SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase)
                return false;
            if (engine == null || who == null || card == null || timing != EffectTiming.EndPhase || !c.ResolvesFromGy)
                return true;
            if (!GyEntryStamps.EnteredThisTurn(engine, who, card) || (card.GyEntryReason & GyEntryReason.Return) != 0)
                return true;
            if (!ManticoreLive(engine, who, card) || ManticoreCosts(engine, who, c).Count == 0) return true;
            if (!TryMarkOffered(engine, card, card.GyEntryStamp)) return true;

            var atk = card.Def?.atk ?? 0;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => ManticoreLive(engine, who, card) ? ManticoreCosts(engine, who, c) : new List<CardInstance>(),
                Kind = EffectTargetKind.RitualTribute,
                Prompt = $"{card.Name}: tap 1 {c.RaceFilter?.Replace("|", " / ")} monster in your hand or face-up on your " +
                         "field to send it to the GY and Special Summon this card from your GY — or CANCEL to not use it.",
                AskWithOneCandidate = true,
                AiChoose = list => list
                    .Where(m => (m.Def?.atk ?? 0) < atk)
                    .OrderBy(m => who.Hand.Contains(m) ? 0 : 1)
                    .ThenBy(m => m.Def?.atk ?? 0)
                    .FirstOrDefault(),
                Cancel = () => engine.Log($"{card.Name}: {who.Name} does not use its effect."),
                Finish = cost => PayAndRevive(engine, who, card, c, cost)
            });
            return true;
        }

        static void PayAndRevive(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c, CardInstance cost)
        {
            if (cost == null)
            {
                engine.Log($"{card.Name}: {who.Name} does not use its effect.");
                return;
            }

            if (!ManticoreLive(engine, who, card) || !ManticoreCosts(engine, who, c).Contains(cost))
            {
                engine.Log($"{card.Name}: it can no longer be used — nothing is sent.");
                return;
            }

            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                if (who.Hand.Contains(cost))
                {
                    MonsterEffects.SendFromHandToGraveByInstance(who, cost);
                    GyEntryStamps.AddReason(engine, cost, GyEntryReason.Cost, who);
                }
                else
                {
                    engine.SendCardToGrave(who, cost, sentBy: card);
                    GyEntryStamps.AddReason(engine, cost, GyEntryReason.Cost, who);
                }
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }

            engine.Log($"Cost: {who.Name} sends {cost.Name} to the GY for {card.Name}.");
            if (engine.GameOver) return;
            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => who.Graveyard.Contains(card) ? new List<CardInstance> { card } : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{card.Name}: tap it, then Summon ATK (face-up Attack Position) or Summon DEF (face-up Defense " +
                         "Position).",
                AskWithOneCandidate = true,
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.FirstOrDefault(),
                Finish = pick =>
                {
                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : BattlePosition.Attack;
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    SummonThisFromGy(engine, who, card, pos, zone);
                    ReofferAfterResolution(engine, who);
                }
            });
        }

        /// <summary>
        /// After a Manticore of Darkness resolved, ygopro raises EVENT_PHASE+PHASE_END again: a card in its controller's GY
        /// that this clause now covers (another Manticore sent there as this cost, the two-Manticore loop) is offered its own
        /// effect in this End Phase. Each GY trip is still offered once (<see cref="TryMarkOffered"/>).
        /// </summary>
        static void ReofferAfterResolution(DuelEngine engine, DuelistState who)
        {
            if (engine.GameOver || engine.Phase != DuelPhase.End || who?.Graveyard == null) return;
            foreach (var g in who.Graveyard.ToList())
            {
                if (g?.Def == null || !who.Graveyard.Contains(g)) continue;
                var clauses = CompiledEffectCache.GetOrCompile(g.Def)?.ClausesFor(EffectTiming.EndPhase);
                if (clauses == null) continue;
                foreach (var c in clauses)
                    if (c?.Action == EffectActionKind.SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase)
                        TryFirePhaseClause(engine, who, g, c, EffectTiming.EndPhase);
            }
        }

        static void SummonThisFromGy(DuelEngine engine, DuelistState who, CardInstance card, BattlePosition pos, int zone)
        {
            if (!who.Graveyard.Contains(card))
            {
                engine.Log($"{card.Name} is no longer in the GY — it is not Special Summoned.");
                return;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, card, SummonOrigin.Graveyard,
                    requireZone: true))
            {
                engine.Log($"{card.Name} cannot be Special Summoned now.");
                return;
            }

            who.Graveyard.Remove(card);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(who, card, pos, true, SummonKind.SpecialSummon, zone))
            {
                engine.Log($"{card.Name} is Special Summoned from the GY.");
                engine.NotifyPublic();
                return;
            }

            engine.NextSpecialSummonFromGraveyard = false;
            who.Graveyard.Add(card);
            engine.Log($"{card.Name} could not be Special Summoned — it stays in the GY.");
        }
    }
}
