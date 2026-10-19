using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmCostLinkedSpells" (v211-tlm-fet) runtime. Reads compiled clauses only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Battery Charger</b>: the shared SpecialSummonFromGy target pick, filtered by
    /// <see cref="EffectClause.TargetSeriesName"/>; the activation takes the compiled flow
    /// (<see cref="UsesCompiledActivationPending"/>: the legacy GY list holds both GYs and no series filter).</item>
    /// <item><b>Minefield Eruption</b>: <see cref="BurnCount"/> (the face-up named monsters you control, counted at
    /// resolution) and <see cref="DestroyAllFaceUpNamed"/> (one destruction event).</item>
    /// <item><b>Card of Sanctity</b>: <see cref="OtherCardsInHandAndOnField"/> (min. 1 card in the hand and 1 on the field) and
    /// <see cref="PayActivationCost"/> (every other card banished together as the card is activated).</item>
    /// <item><b>Gift of the Martyr</b>: <see cref="FilterSendCost"/> (a printed ATK above 0, and a face-up monster left to
    /// target), <see cref="ApplyCostAtkGain"/> (the sent monster's printed ATK, carried as the cost numeric), and
    /// <see cref="TryCancelPick"/> (the cost is paid: the target pick is mandatory).</item>
    /// <item><b>Double Attack</b>: <see cref="FilterDiscardCost"/>, <see cref="NoteDiscardCost"/> and
    /// <see cref="FilterTargets"/> (the target's Level is below the discarded monster's), and
    /// <see cref="GrantExtraAttack"/> (the BattleBoosts turn grant on the target).</item>
    /// <item><b>Hero Signal</b>: the battle-destroyed-and-previously-yours condition
    /// (<see cref="BattleWindowsRuntime.BattleDestroyedFormerlyControlledBy"/>) in the end-of-Damage-Step and "monster sent
    /// to the GY" windows (and a Chain built in them, before a link resolves), and <see cref="ResolveHeroSignal"/> (a pick at resolution from the hand or Deck).</item>
    /// </list>
    /// </summary>
    public static class TlmCostLinkedSpellsRuntime
    {
        /// <summary><see cref="EffectClause.CountSource"/>: face-up monsters named ConditionName you control (Minefield Eruption).</summary>
        public const string FaceUpNamedYouControl = "TlmCostLinkedSpells.FaceUpNamedYouControl";

        /// <summary>ActivationCondition: you control a face-up monster named ConditionName (Minefield Eruption's target chk).</summary>
        public const string YouControlFaceUpNamed = "TlmCostLinkedSpells.YouControlFaceUpNamed";

        /// <summary>ActivationCondition (Double Attack, Lua Duel.IsAbleToEnterBP): your Main Phase 1, Battle Phase allowed.</summary>
        public const string CanEnterBattlePhase = "TlmCostLinkedSpells.CanEnterBattlePhase";

        /// <summary>
        /// ActivationCondition (Card of Sanctity's printed "(min. 1 from each)"): another card in your hand and another on
        /// your field to banish (checked when the card is activated; the cost empties both).
        /// </summary>
        public const string OtherCardsInHandAndOnField = "TlmCostLinkedSpells.OtherCardsInHandAndOnField";

        /// <summary>
        /// ActivationCondition (Hero Signal): in your end-of-Damage-Step or "monster sent to the GY" window (or a chain
        /// response window of the Chain built there, before any link resolves), a monster you controlled was destroyed by
        /// battle and is now in a GY; plus a free Monster Zone and a summonable candidate.
        /// </summary>
        public const string MonsterYouControlledDestroyedByBattle = "TlmCostLinkedSpells.MonsterYouControlledDestroyedByBattle";

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || engine == null || who == null) return false;
            switch (c.ActivationCondition)
            {
                case YouControlFaceUpNamed:
                    ok = FaceUpNamed(who, c.ConditionName).Count > 0;
                    reason = $"You must control a face-up \"{c.ConditionName}\".";
                    return true;
                case OtherCardsInHandAndOnField:
                    ok = OtherHandCards(who, card).Count > 0 && OtherFieldCards(who, card).Count > 0;
                    reason = "Needs at least 1 other card in your hand and 1 on your field to banish.";
                    return true;
                case CanEnterBattlePhase:
                    ok = engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.CanConductBattlePhase;
                    reason = "Only during your Main Phase 1 of a turn you can conduct your Battle Phase.";
                    return true;
                case MonsterYouControlledDestroyedByBattle:
                    reason = "Only when a monster you control is destroyed by battle and sent to the GY.";
                    var timing = engine.PendingResponse?.Timing;
                    // Its own windows, or a chain response window of the Chain built in them (EVENT_BATTLE_DESTROYED is
                    // still current until a link resolves: BattleDestroyedFormerlyControlledBy reads only those sends).
                    if (engine.PendingResponse == null || engine.PendingResponse.Responder != who ||
                        (timing != ResponseTiming.EndOfDamageStep && timing != ResponseTiming.MonsterSentFromFieldToYourGy &&
                         timing != ResponseTiming.ChainResponse))
                        return true;
                    if (BattleWindowsRuntime.BattleDestroyedFormerlyControlledBy(engine, who).Count == 0)
                        return true;
                    if (ZoneCapsRuntime.UsableMonsterZones(who) <= 0)
                    {
                        reason = "No free Monster Zone to Special Summon to.";
                        return true;
                    }

                    if (HeroCandidates(engine, who, card, c).Count == 0)
                    {
                        reason = $"No Level {c.SummonLevelMax} or lower \"{c.TargetSeriesName}\" monster in your hand or " +
                                 "Deck can be Special Summoned.";
                        return true;
                    }

                    ok = true;
                    reason = "OK";
                    return true;
                default:
                    return false;
            }
        }

        // ───────────────────────────── Battery Charger ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.UsesCompiledActivationPending: a targeted Special Summon of a series monster from your own GY
        /// (Battery Charger). The legacy MonsterInEitherGy list carries both GYs and no series filter, so the compiled flow
        /// asks the pick (TextEffectRuntime.CollectTargets applies TargetSeriesName).
        /// </summary>
        public static bool UsesCompiledActivationPending(EffectClause c) =>
            c != null && c.Action == EffectActionKind.SpecialSummonFromGy && c.RequiresTargetChoice && c.IsPsctTarget &&
            c.Zone == EffectZoneFilter.ControllerGyMonsters && !string.IsNullOrEmpty(c.TargetSeriesName);

        // ───────────────────────────── Minefield Eruption ─────────────────────────────

        static List<CardInstance> FaceUpNamed(DuelistState who, string name) =>
            who == null || string.IsNullOrEmpty(name)
                ? new List<CardInstance>()
                : who.MonstersOnField().Where(m => m != null && m.FaceUp && m.IsNamed(name)).ToList();

        /// <summary>PerCountBurnsRuntime.BurnCount fallback: -1 when the clause's CountSource is not this batch's.</summary>
        public static int BurnCount(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            if (clause == null || clause.CountSource != FaceUpNamedYouControl) return -1;
            return FaceUpNamed(who, clause.ConditionName).Count;
        }

        /// <summary>The <see cref="EffectActionKind.DestroyAllFaceUpNamedYouControl"/> arm: one destruction event.</summary>
        public static void DestroyAllFaceUpNamed(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var victims = FaceUpNamed(who, clause.ConditionName);
            if (victims.Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no face-up \"{clause.ConditionName}\" to destroy.");
                return;
            }

            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in victims)
                    if (who.TryFindMonster(m, out _))
                        TextEffectRuntime.DestroyCard(engine, m, source, byPlayer: who);
            });
        }

        // ───────────────────────────── Card of Sanctity ─────────────────────────────

        static List<CardInstance> OtherHandCards(DuelistState who, CardInstance card) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(h => h != null && !ReferenceEquals(h, card)).ToList();

        /// <summary>Every other card <paramref name="who"/> controls: monsters, Spell &amp; Trap / Field and Pendulum Zones.</summary>
        static List<CardInstance> OtherFieldCards(DuelistState who, CardInstance card)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            list.AddRange(who.MonstersOnField().Where(m => m != null && !ReferenceEquals(m, card)));
            list.AddRange(who.SpellTrapsOnField().Where(s => s != null && !ReferenceEquals(s, card) && !list.Contains(s)));
            if (who.PendulumZones != null)
                foreach (var z in who.PendulumZones)
                    if (z?.Occupant != null && !ReferenceEquals(z.Occupant, card) && !list.Contains(z.Occupant))
                        list.Add(z.Occupant);
            return list;
        }

        /// <summary>
        /// CorpusTriggerStressTests.NeedsHardBoard: this batch's activation conditions need a prepared board or a battle
        /// window (TlmCostLinkedSpellsRegressionTests drive them).
        /// </summary>
        public static bool NeedsHardBoard(EffectClause c) =>
            c != null && (c.ActivationCondition == YouControlFaceUpNamed ||
                          c.ActivationCondition == OtherCardsInHandAndOnField ||
                          c.ActivationCondition == CanEnterBattlePhase ||
                          c.ActivationCondition == MonsterYouControlledDestroyedByBattle);

        /// <summary>
        /// SpellTrapEffects.BeginDeferredChainActivation (an untargeted activation, as the card is placed): the cost
        /// <see cref="EffectClause.CostBanishAllOtherHandAndField"/> — every other card in the hand and on the field is
        /// banished face-up, together (one EVENT_REMOVE, Lua Duel.Remove(g, POS_FACEUP, REASON_COST)).
        /// </summary>
        public static void PayActivationCost(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog)
        {
            if (engine == null || who == null || card == null || prog == null) return;
            if (!prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null && c.CostBanishAllOtherHandAndField))
                return;
            var hand = OtherHandCards(who, card);
            var field = OtherFieldCards(who, card);
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                using var together = BanishEvents.Group(engine);
                foreach (var h in hand)
                    engine.BanishCard(who, h, by: who);
                foreach (var f in field)
                {
                    if (!ReferenceEquals(engine.ControllerOf(f), who) && !IsInPendulumZone(who, f)) continue;
                    if (IsInPendulumZone(who, f))
                        foreach (var z in who.PendulumZones)
                            if (ReferenceEquals(z?.Occupant, f))
                                z.Occupant = null;
                    engine.BanishCard(who, f, by: who);
                    // A Token banished as a cost ceases to exist.
                    if (f.IsToken)
                    {
                        engine.Player?.Banished?.Remove(f);
                        engine.Opponent?.Banished?.Remove(f);
                    }
                }
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }

            engine.Log($"Cost: {who.Name} banishes {hand.Count} card(s) from the hand and {field.Count} from the field.");
        }

        static bool IsInPendulumZone(DuelistState who, CardInstance c) =>
            who?.PendulumZones != null && who.PendulumZones.Any(z => ReferenceEquals(z?.Occupant, c));

        // ───────────────────────────── Gift of the Martyr ─────────────────────────────

        static bool IsGift(EffectClause c) =>
            c != null && c.Action == EffectActionKind.TargetGainsCostTextAtkUntilEndOfTurn && c.RequiresSendOtherYouControl;

        /// <summary>
        /// TextEffectRuntime.CollectOtherYouControl: Gift of the Martyr's cost monster has a printed ATK above 0 (Lua
        /// GetTextAttack() &gt; 0; face-down allowed) and leaves a face-up monster you control to target. The weakest
        /// monster on the field comes first (the AI's automatic pick).
        /// </summary>
        public static void FilterSendCost(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            List<CardInstance> list)
        {
            if (!IsGift(c) || list == null || list.Count == 0) return;
            var targets = engine != null
                ? TextEffectRuntime.CollectTargets(engine, who, c, source)
                : who.MonstersOnField().Where(m => m != null && m.FaceUp).ToList();
            list.RemoveAll(m => m?.Def == null || m.Def.atk <= 0 || !targets.Any(t => t != null && !ReferenceEquals(t, m)));
            list.Sort((a, b) => a.CurrentAtk.CompareTo(b.CurrentAtk));
        }

        /// <summary>
        /// The <see cref="EffectActionKind.TargetGainsCostTextAtkUntilEndOfTurn"/> arm: the face-up target you still control
        /// gains <paramref name="costNumeric"/> (the sent monster's printed ATK) until the end of the turn.
        /// </summary>
        public static void ApplyCostAtkGain(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target, int costNumeric)
        {
            if (engine == null || who == null || target == null) return;
            if (!target.FaceUp || !who.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name ?? "Effect"}: {target.Name} is no longer a face-up monster you control.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            if (costNumeric <= 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no ATK was recorded from the sent monster.");
                return;
            }

            target.UntilEndOfTurnAtk += costNumeric;
            engine.Log($"{target.Name} gains {costNumeric} ATK until the end of this turn (now {target.CurrentAtk}).");
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: Gift of the Martyr's target pick comes after its cost was paid (the monster is
        /// in the GY), so the activation cannot be undone — Cancel picks the target automatically.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.UsesTextProgram || p.AwaitingIgnitionCost || p.IsMonsterEffect || p.Card == null ||
                p.CostNumeric <= 0 || p.LegalTargets == null || p.LegalTargets.Count == 0)
                return false;
            var prog = CompiledEffectCache.GetOrCompile(p.Card);
            if (prog?.ClausesFor(EffectTiming.Activate).Exists(IsGift) != true) return false;
            var pick = AiPickGiftTarget(p.LegalTargets);
            engine.Log($"{p.Card.Name}: the cost is paid — {pick.Name} is chosen as the target.");
            return SpellTrapEffects.TryResolveWithTarget(engine, pick);
        }

        static CardInstance AiPickGiftTarget(List<CardInstance> targets) =>
            targets.OrderByDescending(t => t?.CurrentAtk ?? -1).First();

        // ───────────────────────────── Double Attack ─────────────────────────────

        sealed class DiscardRecord
        {
            public CardInstance Discarded;
            public DuelistState Owner;
            public int Turn;
            public int GyStamp;
            public int Level;
        }

        static readonly ConditionalWeakTable<CardInstance, DiscardRecord> s_discards = new();

        static bool IsDoubleAttack(EffectClause c) =>
            c != null && c.Action == EffectActionKind.TargetGainsExtraAttackThisTurn && c.RequiresDiscardCost;

        static bool CanGainExtraAttack(DuelEngine engine, CardInstance t, int belowLevel) =>
            t?.Def != null && t.FaceUp && t.Def.IsMonster && t.Level >= 1 && t.Level < belowLevel &&
            engine.ExtraAttacksAllowed(t) <= 0;

        /// <summary>The source whose discard was just paid on the AI's inline path (its target is collected right after).</summary>
        [System.ThreadStatic] static CardInstance s_justDiscardedFor;

        /// <summary>
        /// The discarded monster's Level recorded for <paramref name="source"/>'s activation being made, or 0: the discard
        /// was paid this turn and is still in the GY from that trip, and that activation is still choosing (its pending
        /// pick, its recorded Chain Link, or the AI's inline pick right after the discard).
        /// </summary>
        static int RecordedLevel(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source == null || !s_discards.TryGetValue(source, out var r)) return 0;
            if (r.Turn != engine.TurnNumber || r.Owner?.Graveyard == null || !r.Owner.Graveyard.Contains(r.Discarded) ||
                r.Discarded.GyEntryStamp != r.GyStamp)
                return 0;
            var choosing = ReferenceEquals(engine.PendingActivation?.Card, source) ||
                           CompiledActivationChainRuntime.IsRecording(source) ||
                           ReferenceEquals(s_justDiscardedFor, source);
            return choosing ? r.Level : 0;
        }

        /// <summary>
        /// TextEffectRuntime (the discard cost of a targeted activation, AI and player paths): Double Attack records the
        /// discarded monster's Level (Lua e:SetLabel(cg:GetFirst():GetLevel())) for its target filter.
        /// </summary>
        public static void NoteDiscardCost(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance discarded)
        {
            if (engine == null || source == null || discarded?.Def == null || !IsDoubleAttack(c)) return;
            s_discards.Remove(source);
            s_discards.Add(source, new DiscardRecord
            {
                Discarded = discarded,
                Owner = who,
                Turn = engine.TurnNumber,
                GyStamp = discarded.GyEntryStamp,
                Level = discarded.Def.level
            });
            s_justDiscardedFor = source;
        }

        /// <summary>
        /// TextEffectRuntime.StartOrPayDiscardThenTarget: Double Attack's discard is a Monster Card whose original Level is
        /// above 1, while a face-up monster you control has a lower Level and no extra attack yet (Lua s.filter1).
        /// </summary>
        public static void FilterDiscardCost(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> list)
        {
            if (!IsDoubleAttack(c) || engine == null || who == null || list == null) return;
            s_justDiscardedFor = null; // a new activation is choosing its discard
            var field = who.MonstersOnField().Where(m => m != null).ToList();
            list.RemoveAll(d => d?.Def == null || !d.Def.IsMonster || d.Def.level <= 1 ||
                                !field.Any(t => CanGainExtraAttack(engine, t, d.Def.level)));
        }

        /// <summary>
        /// End of TextEffectRuntime.CollectTargets: Double Attack's target is a face-up monster with a Level below the
        /// discarded monster's and no extra attack yet. Before the discard (the activation check) the highest Level a legal
        /// discard could have is the bound.
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance except,
            List<CardInstance> list)
        {
            if (!IsDoubleAttack(c) || engine == null || who == null || list == null || list.Count == 0) return;
            var bound = RecordedLevel(engine, except);
            if (bound <= 0)
            {
                var fodder = TextEffectRuntime.CollectDiscardCost(who, c.DiscardCostAttribute, except);
                FilterDiscardCost(engine, who, c, fodder);
                bound = fodder.Count == 0 ? 0 : fodder.Max(d => d.Def.level);
            }

            list.RemoveAll(t => !CanGainExtraAttack(engine, t, bound));
        }

        /// <summary>
        /// The <see cref="EffectActionKind.TargetGainsExtraAttackThisTurn"/> arm: the target can attack 1 + Amount times
        /// this turn (BattleBoostsRuntime's turn grant: kept while the target stays face-up on the field).
        /// </summary>
        public static void GrantExtraAttack(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            if (source != null) s_discards.Remove(source);
            if (ReferenceEquals(s_justDiscardedFor, source)) s_justDiscardedFor = null;
            if (engine == null || who == null || target == null) return;
            if (!who.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name ?? "Effect"}: {target.Name} is no longer on your field.");
                return;
            }

            BattleBoostsRuntime.GrantExtraAttack(engine, who, target, clause);
        }

        // ───────────────────────────── Hero Signal ─────────────────────────────

        static bool HeroFits(CardDef d, EffectClause c) =>
            d != null && d.IsMonster && !d.IsExtraDeck && d.level >= 1 &&
            (c.SummonLevelMax <= 0 || d.level <= c.SummonLevelMax) &&
            TextEffectRuntime.CardMatchesSeries(d, c.TargetSeriesName);

        /// <summary>
        /// The monsters Hero Signal may Special Summon: one instance per hand card and per distinct Deck passcode that can
        /// be Special Summoned face-up by <paramref name="source"/>.
        /// </summary>
        static List<CardInstance> HeroCandidates(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who == null || c == null) return list;
            foreach (var h in who.Hand)
                if (h?.Def != null && HeroFits(h.Def, c) && (c.SummonLevelMax <= 0 || h.Level <= c.SummonLevelMax) &&
                    SpecialSummonLegality.CanBeSpecialSummoned(engine, who, h, source, SummonOrigin.Hand))
                    list.Add(h);
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || !HeroFits(def, c)) continue;
                var inst = engine.CreateCardInstance(id);
                if (SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck))
                    list.Add(inst);
            }

            return list;
        }

        /// <summary>
        /// The <see cref="EffectActionKind.SpecialSummonLowLevelSeriesFromHandOrDeck"/> arm: with a free Monster Zone, the
        /// controller selects 1 candidate (no target) and Special Summons it face-up (the player picks Attack or Defense).
        /// </summary>
        public static void ResolveHeroSignal(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var name = source?.Name ?? "Effect";
            if (ZoneCapsRuntime.UsableMonsterZones(who) <= 0)
            {
                engine.Log($"{name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            if (HeroCandidates(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{name}: no Level {clause.SummonLevelMax} or lower \"{clause.TargetSeriesName}\" monster in " +
                           "the hand or Deck can be Special Summoned.");
                return;
            }

            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => HeroCandidates(engine, who, source, clause),
                Kind = EffectTargetKind.MonsterInHandOrDeck,
                Prompt = $"Special Summon 1 Level {clause.SummonLevelMax} or lower \"{clause.TargetSeriesName}\" monster " +
                         "from your hand or Deck (face-up).",
                AiChoose = l => l.OrderByDescending(m => System.Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0)).FirstOrDefault(),
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                Finish = pick =>
                {
                    if (pick == null) return;
                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : DeckSummonsAndSarcophagiRuntime.AiPosition(pick);
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    engine.ClearPendingSpecialSummonPlacement();
                    DeckSummonsAndSarcophagiRuntime.SummonFromHandOrDeck(engine, who, source, pick, pos, zone);
                }
            });
        }

        /// <summary>AI (SpellTrapEffects.AiAutoRespond): Hero Signal whenever it is legal (a free Special Summon).</summary>
        public static bool AiWantsHeroSignal(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            var clause = prog?.ClauseList.Find(c => c != null && c.ActivationCondition == MonsterYouControlledDestroyedByBattle);
            if (clause == null || !prog.FullyCompiled) return false;
            return TryActivationCondition(engine, who, card, clause, out var ok, out _) && ok;
        }
    }
}
