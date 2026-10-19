using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DeckSwarmsAndFusionMaterials" runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item>Hyena: <see cref="StartDeckSwarm"/> — 1 to (free Monster Zones) copies of the card's own name from the Deck,
    /// chosen one at a time (CANCEL = done; at the first pick CANCEL declines the optional trigger), Special Summoned
    /// together face-up.</item>
    /// <item>Rescue Cat: <see cref="ResolveNegatedPair"/> — exactly N matching Deck monsters, face-up, with their effects
    /// negated while they stay (<see cref="CardInstance.SummonedWithEffectsNegated"/>, <see cref="RefreshNegatedSummons"/>)
    /// and destroyed during this End Phase (<see cref="CardInstance.TempDestroyOnEndOfTurn"/>); the name's hard once per
    /// turn is recorded here.</item>
    /// <item>Sacred Phoenix of Nephthys: <see cref="NoteEnteredGy"/> records the trip, <see cref="TryFirePhaseClause"/>
    /// seats its Standby Phase revival and the revival seats its own "destroy all Spells and Traps" trigger.</item>
    /// <item>Centrifugal Field: <see cref="OnSentToGy"/> / <see cref="NoteHeldSend"/> + <see cref="TakeHeldTriggers"/> —
    /// one mandatory trigger per Fusion Monster destroyed by a card effect, activated by that Fusion's owner, who targets
    /// the listed material as it activates (<see cref="ActivateQueuedTrigger"/>).</item>
    /// <item>The Hex-Sealed Fusions: <see cref="ResolveHexSealed"/> — while activating, choose the Fusion Monster, then its
    /// materials (this card included), Tribute them (cost, one event); the opponent may respond; the link
    /// (<see cref="TryResolveHexLink"/>) Special Summons the Fusion Monster from the Extra Deck.</item>
    /// </list>
    /// </summary>
    public static class DeckSwarmsAndFusionMaterialsRuntime
    {
        /// <summary>Rescue Cat: <see cref="EffectClause.Amount"/> usable zones (this card's own counted) and as many Deck
        /// monsters (Lua GetMZoneCount(tp,c) &gt; 1 and IsExistingMatchingCard(…, 2)); the name is unused this turn.</summary>
        public const string ZonesAndDeckMonstersForPair = "DeckSwarms.ZonesAndDeckMonstersForPair";

        /// <summary>Hex-Sealed: a Fusion Monster of the Attribute in the Extra Deck whose named materials are monsters you
        /// control, this face-up card included (Lua s.target chk).</summary>
        public const string AttributeFusionMaterialsOnField = "DeckSwarms.AttributeFusionMaterialsOnField";

        static int FreeZones(DuelistState who) => ZoneCapsRuntime.UsableMonsterZones(who);

        static bool IsFusion(CardDef def) =>
            def != null && def.IsMonster && def.type != null &&
            def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0;

        static BattlePosition AiPosition(CardDef def) =>
            def != null && def.def > def.atk ? BattlePosition.Defense : BattlePosition.Attack;

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False for another batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case ZonesAndDeckMonstersForPair:
                {
                    var n = Math.Max(1, c.Amount);
                    var own = who != null && card != null && who.TryFindMonster(card, out _) ? 1 : 0;
                    ok = engine != null && who != null && !NameUsedThisTurn(engine, who, c.NamedCard) &&
                         FreeZones(who) + own >= n && DeckCopies(engine, who, card, d => PairFilter(d, c)) >= n;
                    reason = NameUsedThisTurn(engine, who, c.NamedCard)
                        ? $"You already used the effect of \"{c.NamedCard}\" this turn."
                        : $"Needs {n} free Monster Zones (this card's counted) and {n} Level {c.SummonLevelMax} or lower " +
                          $"{c.RaceFilter} monsters in your Deck that can be Special Summoned.";
                    return true;
                }
                case AttributeFusionMaterialsOnField:
                    ok = engine != null && who != null && card != null && card.FaceUp && who.TryFindMonster(card, out _) &&
                         HexFusionCandidates(engine, who, card, c).Count > 0;
                    reason = $"Needs a {c.AttributeFilter} Fusion Monster in your Extra Deck whose named Fusion Materials " +
                             "are monsters you control, this face-up card included, that can be Tributed.";
                    return true;
                default:
                    return false;
            }
        }

        // ───────────────────────────── Deck copies ─────────────────────────────

        sealed class DeckPick
        {
            public CardInstance Card;
            public BattlePosition Position;
            public int Zone = -1;
        }

        /// <summary>How many Deck cards match <paramref name="filter"/> and can be Special Summoned (one probe per id).</summary>
        static int DeckCopies(DuelEngine engine, DuelistState who, CardInstance source, Func<CardDef, bool> filter)
        {
            if (engine?.Database == null || who?.Deck == null) return 0;
            var legal = new Dictionary<int, bool>();
            var n = 0;
            foreach (var id in who.Deck)
            {
                if (!legal.TryGetValue(id, out var ok))
                {
                    ok = engine.Database.TryGet(id, out var def) && filter(def) &&
                         SpecialSummonLegality.CanBeSpecialSummoned(engine, who, engine.CreateCardInstance(id), source,
                             SummonOrigin.Deck);
                    legal[id] = ok;
                }

                if (ok) n++;
            }

            return n;
        }

        /// <summary>
        /// One fresh instance per distinct Deck id that matches <paramref name="filter"/>, still has a copy not yet chosen
        /// (<paramref name="picks"/>) and can be Special Summoned.
        /// </summary>
        static List<CardInstance> DeckCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            Func<CardDef, bool> filter, List<DeckPick> picks)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.Deck == null) return list;
            foreach (var id in who.Deck.Distinct().ToList())
            {
                if (!engine.Database.TryGet(id, out var def) || !filter(def)) continue;
                var copies = who.Deck.Count(x => x == id);
                if (copies <= picks.Count(p => p.Card.CardId == id)) continue;
                var inst = engine.CreateCardInstance(id);
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck)) continue;
                list.Add(inst);
            }

            return list;
        }

        /// <summary>
        /// Special Summons the chosen Deck copies together (one event), face-up in the chosen positions. A copy that
        /// cannot be Summoned goes back where it was. <paramref name="each"/> runs before each Summon (and its undo when the
        /// Summon fails, with false); returns the summoned monsters.
        /// </summary>
        static List<CardInstance> SummonFromDeck(DuelEngine engine, DuelistState who, CardInstance source,
            List<DeckPick> picks, Action<CardInstance, bool> each = null)
        {
            var done = new List<CardInstance>();
            if (picks.Count == 0) return done;
            NegationsAndGySummonsRuntime.BeginGroupSpecialSummon(engine);
            try
            {
                foreach (var p in picks)
                {
                    if (engine.GameOver) break;
                    var i = who.Deck.IndexOf(p.Card.CardId);
                    if (i < 0)
                    {
                        engine.Log($"{source?.Name}: {p.Card.Name} is no longer in the Deck.");
                        continue;
                    }

                    each?.Invoke(p.Card, true);
                    var copy = PerCopyDeckRuntime.TakeFromDeck(who, i, p.Card);
                    if (engine.SpecialSummonToField(who, p.Card, p.Position, true, SummonKind.SpecialSummon, p.Zone))
                    {
                        engine.Log($"{source?.Name}: {who.Name} Special Summons {p.Card.Name} from the Deck in face-up " +
                                   $"{p.Card.Position} Position.");
                        done.Add(p.Card);
                        continue;
                    }

                    each?.Invoke(p.Card, false);
                    who.Deck.Insert(i, p.Card.CardId, copy);
                    engine.Log($"{source?.Name}: {p.Card.Name} could not be Special Summoned — it stays in the Deck.");
                }
            }
            finally
            {
                NegationsAndGySummonsRuntime.EndGroupSpecialSummon(engine);
            }

            engine.Rng.Shuffle(who.Deck);
            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
            return done;
        }

        static CardInstance AiStrongest(List<CardInstance> list) =>
            list?.OrderByDescending(c => Math.Max(c.Def?.atk ?? 0, c.Def?.def ?? 0)).ThenBy(c => c.Name).FirstOrDefault();

        static DeckPick PickFrom(DuelEngine engine, CardInstance chosen)
        {
            var pick = new DeckPick
            {
                Card = chosen,
                Position = engine.PendingSpecialSummonPosition ?? AiPosition(chosen.Def),
                Zone = engine.PendingSpecialSummonZone
            };
            engine.ClearPendingSpecialSummonPlacement();
            return pick;
        }

        // ───────────────────────────── Hyena ─────────────────────────────

        /// <summary>
        /// The printed name as the card's own name: "Hyenas" (the printed plural) or "Hyena" on Hyena reads "Hyena"
        /// (the ClassicEra SingularName rule, Lua IsCode(id)); any other name is kept as printed.
        /// </summary>
        public static string SwarmName(string printed, CardInstance source)
        {
            var own = source?.Def?.name;
            if (!string.IsNullOrEmpty(own) && !string.IsNullOrEmpty(printed) &&
                (string.Equals(printed, own, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(printed, own + "s", StringComparison.OrdinalIgnoreCase)))
                return own;
            return printed;
        }

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonAnyNumberOfOwnNameFromDeck"/>: <paramref name="who"/> chooses 1 to (free
        /// Monster Zones) copies from the Deck one at a time — a human taps one per Summon and its face-up position (CANCEL
        /// = done; at the first pick CANCEL Special Summons none, the trigger's "You can"); the AI takes as many as fit —
        /// then they are Special Summoned together, face-up (Lua SelectMatchingCard 1..ft, POS_FACEUP).
        /// </summary>
        public static void StartDeckSwarm(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var name = SwarmName(clause.NamedCard, source);
            bool Filter(CardDef d) => d != null && d.IsMonster && !d.IsExtraDeck &&
                                      string.Equals(d.name, name, StringComparison.OrdinalIgnoreCase);
            var ft = FreeZones(who);
            var picks = new List<DeckPick>();
            if (ft <= 0 || DeckCandidates(engine, who, source, Filter, picks).Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no \"{name}\" in {who.Name}'s Deck can be Special Summoned" +
                           (ft <= 0 ? " (no free Monster Zone)." : "."));
                return;
            }

            AskSwarm(engine, who, source, name, Filter, ft, picks);
        }

        static void AskSwarm(DuelEngine engine, DuelistState who, CardInstance source, string name,
            Func<CardDef, bool> filter, int ft, List<DeckPick> picks)
        {
            List<CardInstance> Candidates() =>
                picks.Count >= ft ? new List<CardInstance>() : DeckCandidates(engine, who, source, filter, picks);

            if (Candidates().Count == 0)
            {
                SummonFromDeck(engine, who, source, picks);
                return;
            }

            var first = picks.Count == 0;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInYourDeckToSummon,
                Prompt = first
                    ? $"{source?.Name}: you can Special Summon any number of \"{name}\" from your Deck — tap one, then " +
                      "Summon ATK or Summon DEF (face-up). CANCEL summons none."
                    : $"{source?.Name}: tap another \"{name}\" to Special Summon it too (CANCEL = done, {picks.Count} " +
                      "chosen).",
                AskWithOneCandidate = true,
                AiChoose = list => list.FirstOrDefault(),
                Cancel = () =>
                {
                    if (picks.Count == 0) engine.Log($"{source?.Name}: no monster is Special Summoned.");
                    SummonFromDeck(engine, who, source, picks);
                },
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        SummonFromDeck(engine, who, source, picks);
                        return;
                    }

                    picks.Add(PickFrom(engine, chosen));
                    AskSwarm(engine, who, source, name, filter, ft, picks);
                }
            });
        }

        // ───────────────────────────── Rescue Cat ─────────────────────────────

        static bool PairFilter(CardDef d, EffectClause c) =>
            d != null && d.IsMonster && !d.IsExtraDeck && d.level >= 1 &&
            (c.SummonLevelMax <= 0 || d.level <= c.SummonLevelMax) &&
            (string.IsNullOrEmpty(c.RaceFilter) ||
             string.Equals(d.race, c.RaceFilter, StringComparison.OrdinalIgnoreCase));

        sealed class NameUse
        {
            public DuelistState Who;
            public string Name;
            public int Turn;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<NameUse>> s_nameUses = new();

        static bool NameUsedThisTurn(DuelEngine engine, DuelistState who, string name) =>
            engine != null && !string.IsNullOrEmpty(name) && s_nameUses.TryGetValue(engine, out var list) &&
            list.Exists(u => ReferenceEquals(u.Who, who) && u.Turn == engine.TurnNumber &&
                             string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));

        static void MarkNameUsed(DuelEngine engine, DuelistState who, string name)
        {
            if (engine == null || string.IsNullOrEmpty(name)) return;
            var list = s_nameUses.GetValue(engine, _ => new List<NameUse>());
            list.RemoveAll(u => u.Turn != engine.TurnNumber);
            list.Add(new NameUse { Who = who, Name = name, Turn = engine.TurnNumber });
        }

        sealed class NegatedMark
        {
            public CardInstance Card;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<NegatedMark>> s_negated = new();

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after the continuous negations): a monster Special Summoned "but their effects are
        /// negated" stays negated while it is face-up on the field in that same presence (Lua RESETS_STANDARD); once it left
        /// the field, was turned face-down or came back, the negation is gone.
        /// </summary>
        public static void RefreshNegatedSummons(DuelEngine engine)
        {
            if (engine == null || !s_negated.TryGetValue(engine, out var marks) || marks.Count == 0) return;
            for (var i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                var holder = engine.ControllerOf(m.Card);
                var live = holder != null && holder.TryFindMonster(m.Card, out _) && m.Card.FaceUp &&
                           m.Card.FieldEntryStamp == m.EntryStamp && m.Card.FaceDownStamp == m.FaceDownStamp;
                m.Card.SummonedWithEffectsNegated = live;
                if (!live) marks.RemoveAt(i);
            }
        }

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonRaceFromDeckEffectsNegatedDestroyAtEndPhase"/> (Rescue Cat, cost already
        /// paid): with fewer than Amount usable zones nothing happens; otherwise exactly Amount matching Deck monsters are
        /// chosen (a human taps each and its face-up position; the AI takes the strongest) and Special Summoned together,
        /// face-up, their effects negated, destroyed during this turn's End Phase. Fewer than Amount left: nothing.
        /// </summary>
        public static void ResolveNegatedPair(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            MarkNameUsed(engine, who, clause.NamedCard); // SetCountLimit(1,id): used as it is activated
            var n = Math.Max(1, clause.Amount);
            bool Filter(CardDef d) => PairFilter(d, clause);
            if (FreeZones(who) < n)
            {
                engine.Log($"{source?.Name}: fewer than {n} free Monster Zones — nothing is Special Summoned.");
                return;
            }

            if (DeckCopies(engine, who, source, Filter) < n)
            {
                engine.Log($"{source?.Name}: fewer than {n} Level {clause.SummonLevelMax} or lower {clause.RaceFilter} " +
                           "monsters in the Deck — nothing is Special Summoned.");
                return;
            }

            AskPair(engine, who, source, clause, n, Filter, new List<DeckPick>());
        }

        static void AskPair(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause, int n,
            Func<CardDef, bool> filter, List<DeckPick> picks)
        {
            List<CardInstance> Candidates() =>
                picks.Count >= n ? new List<CardInstance>() : DeckCandidates(engine, who, source, filter, picks);

            if (picks.Count >= n || Candidates().Count == 0)
            {
                if (picks.Count < n)
                {
                    engine.Log($"{source?.Name}: fewer than {n} monsters can be Special Summoned — nothing happens.");
                    return;
                }

                SummonPair(engine, who, source, picks);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInYourDeckToSummon,
                Prompt = $"{source?.Name}: choose a Level {clause.SummonLevelMax} or lower {clause.RaceFilter} monster in " +
                         $"your Deck to Special Summon ({picks.Count + 1} of {n}), then Summon ATK or Summon DEF " +
                         "(face-up); its effects are negated and it is destroyed during the End Phase.",
                // A human chooses the face-up position of each, even for a lone candidate.
                AskWithOneCandidate = engine.IsHumanControlled(who),
                AiChoose = AiStrongest,
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{source?.Name}: fewer than {n} monsters can be Special Summoned — nothing happens.");
                        return;
                    }

                    picks.Add(PickFrom(engine, chosen));
                    AskPair(engine, who, source, clause, n, filter, picks);
                }
            });
        }

        static void SummonPair(DuelEngine engine, DuelistState who, CardInstance source, List<DeckPick> picks)
        {
            // EFFECT_DISABLE / EFFECT_DISABLE_EFFECT are registered with each SpecialSummonStep, before the Summon completes:
            // the monsters are negated as they arrive.
            var summoned = SummonFromDeck(engine, who, source, picks,
                (card, before) => card.SummonedWithEffectsNegated = before);
            if (summoned.Count == 0) return;
            var marks = s_negated.GetValue(engine, _ => new List<NegatedMark>());
            foreach (var m in summoned)
            {
                m.SummonedWithEffectsNegated = true;
                m.TempDestroyOnEndOfTurn = engine.TurnNumber;
                marks.Add(new NegatedMark { Card = m, EntryStamp = m.FieldEntryStamp, FaceDownStamp = m.FaceDownStamp });
            }

            engine.Log($"{source?.Name}: {string.Join(", ", summoned.Select(m => m.Name))} — effects negated; destroyed " +
                       "during the End Phase.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Sacred Phoenix of Nephthys ─────────────────────────────

        sealed class GyTrip
        {
            public int Stamp;
            public int Turn;
            public bool DuringOwnStandby;
        }

        static readonly ConditionalWeakTable<CardInstance, GyTrip> s_trips = new();

        static EffectClause PhoenixClause(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsMonster) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClauseList.FirstOrDefault(c => c != null &&
                c.Action == EffectActionKind.SpecialSummonThisFromGyNextStandbyAfterEffectDestroyThenDestroyAllSpellTraps);
        }

        /// <summary>
        /// GyEntryStamps.Entered: a card with the revival clause just entered a GY — record the trip, and whether it happened
        /// during its holder's own Standby Phase (Lua s.spr: then the revival waits for the following one).
        /// </summary>
        public static void NoteEnteredGy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || PhoenixClause(card) == null) return;
            var holder = GyEntryStamps.HolderOf(engine, card);
            s_trips.Remove(card);
            s_trips.Add(card, new GyTrip
            {
                Stamp = card.GyEntryStamp,
                Turn = engine.TurnNumber,
                DuringOwnStandby = engine.Phase == DuelPhase.Standby && holder != null &&
                                   ReferenceEquals(engine.TurnPlayer, holder)
            });
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor: true when <paramref name="c"/> is the revival clause (handled, fired or
        /// not), false for any other. In its controller's Standby Phase, while it is in that GY on the trip it made by being
        /// destroyed by a card effect (Lua REASON_DESTROY|REASON_EFFECT, either player's), the first such Standby Phase after
        /// the trip (not the one it was destroyed in) seats the mandatory revival; that Standby Phase spends the flag either
        /// way (Lua RESET_PHASE|PHASE_STANDBY|RESET_SELF_TURN).
        /// </summary>
        public static bool TryFirePhaseClause(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            EffectTiming timing)
        {
            if (c == null ||
                c.Action != EffectActionKind.SpecialSummonThisFromGyNextStandbyAfterEffectDestroyThenDestroyAllSpellTraps)
                return false;
            if (engine == null || who == null || card == null || timing != EffectTiming.StandbyPhase ||
                !ReferenceEquals(engine.TurnPlayer, who) || who.Graveyard == null || !who.Graveyard.Contains(card))
                return true;
            if (!s_trips.TryGetValue(card, out var trip) || trip.Stamp != card.GyEntryStamp) return true;
            const GyEntryReason destroyedByEffect = GyEntryReason.Destroyed | GyEntryReason.Effect;
            if ((card.GyEntryReason & destroyedByEffect) != destroyedByEffect ||
                (card.GyEntryReason & GyEntryReason.Battle) != 0)
                return true;
            if (trip.DuringOwnStandby && trip.Turn == engine.TurnNumber) return true; // the following Standby Phase
            s_trips.Remove(card);

            var seat = new TriggerChainResponsesRuntime.Seat
            {
                Controller = who,
                Card = card,
                From = CardLocation.Graveyard,
                Resolve = () => ResolvePhoenixRevival(engine, who, card)
            };
            engine.Log($"{card.Name}: its Standby Phase effect activates in the GY.");
            if (!TriggerChainResponsesRuntime.TrySeatOrCollect(engine, seat, seat.Resolve))
                seat.Resolve();
            return true;
        }

        static void ResolvePhoenixRevival(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || engine.GameOver) return;
            if (!who.Graveyard.Contains(card))
            {
                engine.Log($"{card.Name} is no longer in the GY — it is not Special Summoned.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, card, card))
            {
                engine.Log("Necrovalley prevents the card from leaving the GY.");
                return;
            }

            if (FreeZones(who) <= 0 ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, card, SummonOrigin.Graveyard))
            {
                engine.Log($"{card.Name} cannot be Special Summoned now — it stays in the GY.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => who.Graveyard.Contains(card) ? new List<CardInstance> { card } : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{card.Name}: Special Summon it from the GY — tap it, then Summon ATK or Summon DEF (face-up).",
                AskWithOneCandidate = engine.IsHumanControlled(who),
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.FirstOrDefault(),
                Finish = pick =>
                {
                    var pos = engine.PendingSpecialSummonPosition ?? AiPosition(card.Def);
                    var zone = engine.PendingSpecialSummonZone;
                    engine.ClearPendingSpecialSummonPlacement();
                    if (pick == null || !who.Graveyard.Contains(card)) return;
                    who.Graveyard.Remove(card);
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (!engine.SpecialSummonToField(who, card, pos, true, SummonKind.SpecialSummon, zone))
                    {
                        engine.NextSpecialSummonFromGraveyard = false;
                        card.ControlOwner = null;
                        if (!who.Graveyard.Contains(card)) who.Graveyard.Add(card);
                        engine.Log($"{card.Name} cannot be Special Summoned — it stays in the GY.");
                        return;
                    }

                    engine.Log($"{card.Name} is Special Summoned from the GY by its own effect.");
                    var stamp = card.FieldEntryStamp;
                    // e3: a separate mandatory trigger of that Summon (SUMMON_TYPE_SPECIAL+1), on a new Chain once the
                    // revival's Chain is over.
                    TriggerChainResponsesRuntime.RunAfterChain(engine, () => SeatPhoenixDestroy(engine, who, card, stamp));
                    engine.FlushQueuedSummonResponses();
                    engine.NotifyPublic();
                }
            });
        }

        static void SeatPhoenixDestroy(DuelEngine engine, DuelistState who, CardInstance card, int stamp)
        {
            if (engine == null || engine.GameOver) return;
            var onField = who.TryFindMonster(card, out _) && card.FieldEntryStamp == stamp;
            if (onField && (card.IsNegated || ContinuousNegationRuntime.NegatesMonsterEffectOnField(engine, card)))
            {
                engine.Log($"{card.Name}: its effects are negated — its Special Summon trigger does not activate.");
                return;
            }

            var seat = new TriggerChainResponsesRuntime.Seat
            {
                Controller = who,
                Card = card,
                From = onField ? CardLocation.MonsterZone : TriggerChainResponsesRuntime.LocationOf(engine, card),
                Resolve = () => DestroyAllSpellTraps(engine, who, card)
            };
            engine.Log($"{card.Name}: Special Summoned by its effect — destroy all Spells and Traps on the field.");
            if (!TriggerChainResponsesRuntime.TrySeatOrCollect(engine, seat, seat.Resolve))
                seat.Resolve();
        }

        static void DestroyAllSpellTraps(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || engine.GameOver) return;
            var clause = new EffectClause
            {
                Timing = EffectTiming.ThisCardSummoned,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldSpellTraps
            };
            var d = false;
            TextEffectRuntime.ApplyClause(engine, who, card, clause, null, ref d, ref d);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Centrifugal Field ─────────────────────────────

        sealed class HeldFusion
        {
            public DuelistState Owner;
            public CardInstance Fusion;
            public int Turn;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<HeldFusion>> s_held = new();

        /// <summary>The live Centrifugal Fields: face-up in a Field Zone, its activation resolved, not negated, its trigger
        /// not locked (Cold Wave).</summary>
        static List<CardInstance> LiveCentrifugalFields(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            if (engine == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                var f = side?.FieldSpellZone?.Occupant;
                if (f?.Def == null || !f.FaceUp || f.IsNegated || f.ActivationUnresolvedOnChain) continue;
                var prog = CompiledEffectCache.GetOrCompile(f.Def);
                if (prog == null || !prog.FullyCompiled || !prog.ClauseList.Exists(c => c != null &&
                        c.Action == EffectActionKind.OwnerSpecialSummonsListedMaterialWhenFusionDestroyedByEffect))
                    continue;
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, f)) continue;
                list.Add(f);
            }

            return list;
        }

        static bool QualifyingFusion(CardInstance card, bool destroyed, bool byBattle) =>
            destroyed && !byBattle && card?.Def != null && IsFusion(card.Def);

        /// <summary>The monsters in <paramref name="owner"/>'s GY named as a material of <paramref name="fusion"/> (Lua
        /// ListsCodeAsMaterial: the recipe's quoted names) that the Field Spell can Special Summon.</summary>
        static List<CardInstance> ListedMaterialTargets(DuelEngine engine, DuelistState owner, CardInstance field,
            CardInstance fusion)
        {
            var list = new List<CardInstance>();
            if (engine == null || owner?.Graveyard == null || fusion == null) return list;
            if (!SpellTrapEffects.TryGetFusionRecipe(fusion.CardId, out var names) || names == null) return list;
            foreach (var g in owner.Graveyard)
            {
                if (g?.Def == null || ReferenceEquals(g, fusion) || !g.Def.IsMonster) continue;
                if (!names.Any(n => g.IsNamed(n) || string.Equals(g.Name, n, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (ContinuousProtections.GyMovementLocked(engine, field, g)) continue;
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, owner, g, field, SummonOrigin.Graveyard)) continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>
        /// MonsterEffects.OnSentFromFieldToGy, when a simultaneous event holds the send (Dark Hole): a Fusion Monster
        /// destroyed by a card effect is remembered until the event's held triggers go on their Chain
        /// (<see cref="TakeHeldTriggers"/>).
        /// </summary>
        public static void NoteHeldSend(DuelEngine engine, DuelistState owner, CardInstance card, bool destroyed,
            bool byBattle)
        {
            if (engine == null || owner == null || !QualifyingFusion(card, destroyed, byBattle)) return;
            var list = s_held.GetValue(engine, _ => new List<HeldFusion>());
            list.RemoveAll(h => h.Turn != engine.TurnNumber);
            list.Add(new HeldFusion { Owner = owner, Fusion = card, Turn = engine.TurnNumber });
        }

        /// <summary>
        /// SimultaneousTriggersRuntime.RunHeld: one mandatory trigger per Fusion Monster the held event destroyed and live
        /// Centrifugal Field (a Field Spell that left the field in that same event does not activate), controlled by that
        /// Fusion's owner (EFFECT_FLAG_EVENT_PLAYER), for the same Chain as the held "sent to the GY" triggers.
        /// </summary>
        public static List<(DuelistState Who, CardInstance Card, Action Run)> TakeHeldTriggers(DuelEngine engine)
        {
            var result = new List<(DuelistState, CardInstance, Action)>();
            if (engine == null || !s_held.TryGetValue(engine, out var list) || list.Count == 0) return result;
            var held = list.Where(h => h.Turn == engine.TurnNumber).ToList();
            list.Clear();
            var fields = LiveCentrifugalFields(engine);
            foreach (var h in held)
                foreach (var f in fields)
                {
                    if (!CanActivateFor(engine, h.Owner, f, h.Fusion)) continue;
                    result.Add((h.Owner, f, CentrifugalRun(engine, h.Owner, f, h.Fusion)));
                }

            return result;
        }

        /// <summary>
        /// MonsterEffects.OnSentFromFieldToGy (a send nothing held): a Fusion Monster destroyed by a card effect queues the
        /// mandatory trigger of each live Centrifugal Field for its owner on the next simultaneous-trigger Chain. A held send
        /// replaying was taken by <see cref="TakeHeldTriggers"/>.
        /// </summary>
        public static void OnSentToGy(DuelEngine engine, DuelistState owner, CardInstance card, bool destroyed, bool byBattle)
        {
            if (engine == null || owner == null || card == null) return;
            if (SimultaneousTriggersRuntime.IsReplaying(engine, card)) return;
            if (!QualifyingFusion(card, destroyed, byBattle)) return;
            foreach (var f in LiveCentrifugalFields(engine))
            {
                if (!CanActivateFor(engine, owner, f, card)) continue;
                SimultaneousTriggersRuntime.QueueTrigger(engine, owner, f, optional: false,
                    CentrifugalRun(engine, owner, f, card));
            }
        }

        /// <summary>Lua s.sptg chk: the owner has a usable Monster Zone and a listed material to target.</summary>
        static bool CanActivateFor(DuelEngine engine, DuelistState owner, CardInstance field, CardInstance fusion) =>
            owner != null && FreeZones(owner) > 0 && ListedMaterialTargets(engine, owner, field, fusion).Count > 0;

        /// <summary>One Centrifugal Field trigger: its target is chosen as it activates (Lua EFFECT_FLAG_CARD_TARGET,
        /// SelectTarget in s.sptg) and used when it resolves (s.spop).</summary>
        sealed class CentrifugalActivation
        {
            public DuelistState Owner;
            public CardInstance Field;
            public CardInstance Fusion;
            public bool Activated;
            public CardInstance Target;
            public int TargetGyStamp;
            public BattlePosition Position;
            public int Zone = -1;
        }

        static readonly ConditionalWeakTable<Action, CentrifugalActivation> s_centrifugalRuns = new();

        /// <summary>The queued trigger's resolution, registered so <see cref="ActivateQueuedTrigger"/> can find its record.</summary>
        static Action CentrifugalRun(DuelEngine engine, DuelistState owner, CardInstance field, CardInstance fusion)
        {
            var act = new CentrifugalActivation { Owner = owner, Field = field, Fusion = fusion };
            Action run = () => RunCentrifugal(engine, act);
            s_centrifugalRuns.Add(run, act);
            return run;
        }

        /// <summary>
        /// SimultaneousTriggersRuntime, a queued / held trigger activating as its Chain Link: a Centrifugal Field trigger's
        /// owner targets 1 listed material in their GY now (a human taps it and its face-up position; the AI takes the
        /// highest ATK). False for any other trigger.
        /// </summary>
        public static bool ActivateQueuedTrigger(DuelEngine engine, Action run)
        {
            if (engine == null || run == null || !s_centrifugalRuns.TryGetValue(run, out var act) || act.Activated)
                return false;
            act.Activated = true;
            AskCentrifugalTarget(engine, act, duringActivation: true, then: null);
            return true;
        }

        /// <summary>The target a queued trigger chose as it activated (shown to responders on its Chain Link).</summary>
        public static IEnumerable<CardInstance> ActivationTargets(Action run)
        {
            if (run != null && s_centrifugalRuns.TryGetValue(run, out var act) && act.Target != null)
                yield return act.Target;
        }

        static void AskCentrifugalTarget(DuelEngine engine, CentrifugalActivation act, bool duringActivation,
            Action then)
        {
            var owner = act.Owner;
            var field = act.Field;
            var fusion = act.Fusion;
            if (owner == null || field == null || FreeZones(owner) <= 0 ||
                ListedMaterialTargets(engine, owner, field, fusion).Count == 0)
            {
                engine.Log($"{field?.Name}: {owner?.Name} has no Fusion Material listed on {fusion?.Name} to target — " +
                           "nothing happens.");
                then?.Invoke();
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, owner, field, new CardPickRequest
            {
                Candidates = () => ListedMaterialTargets(engine, owner, field, fusion),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{field.Name}: target 1 Fusion Material Monster listed on {fusion.Name} in your GY, then Summon " +
                         "ATK or Summon DEF (face-up) — you Special Summon it.",
                AskWithOneCandidate = engine.IsHumanControlled(owner),
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.OrderByDescending(g => g.Def?.atk ?? 0).ThenBy(g => g.Name).FirstOrDefault(),
                // As a Chain Link activates, the pick is part of the activation (the Chain driver goes on after it).
                DuringActivation = duringActivation,
                Finish = target =>
                {
                    act.Position = engine.PendingSpecialSummonPosition ?? AiPosition(target?.Def);
                    act.Zone = engine.PendingSpecialSummonZone;
                    engine.ClearPendingSpecialSummonPlacement();
                    act.Target = target;
                    act.TargetGyStamp = target?.GyEntryStamp ?? 0;
                    if (target != null) engine.Log($"{field.Name}: {owner.Name} targets {target.Name} in the GY.");
                    then?.Invoke();
                }
            });
        }

        /// <summary>
        /// The trigger resolves (Lua s.spop): its owner Special Summons the monster targeted as it activated, if it is still
        /// in that GY (IsRelateToEffect). Activated with no Chain Link (no activation step ran), the target is chosen now.
        /// A Field Spell still face-up whose effects are now negated: no effect.
        /// </summary>
        static void RunCentrifugal(DuelEngine engine, CentrifugalActivation act)
        {
            if (engine == null || engine.GameOver || act == null) return;
            if (!act.Activated)
            {
                act.Activated = true;
                if (!LiveCentrifugalFields(engine).Contains(act.Field))
                {
                    engine.Log($"{act.Field?.Name}: it is no longer face-up in the Field Zone — nothing happens.");
                    return;
                }

                AskCentrifugalTarget(engine, act, duringActivation: false, then: () => SummonCentrifugalTarget(engine, act));
                return;
            }

            SummonCentrifugalTarget(engine, act);
        }

        static void SummonCentrifugalTarget(DuelEngine engine, CentrifugalActivation act)
        {
            var owner = act.Owner;
            var field = act.Field;
            var target = act.Target;
            if (target == null) return;
            var fieldThere = field != null && (ReferenceEquals(engine.Player?.FieldSpellZone?.Occupant, field) ||
                                               ReferenceEquals(engine.Opponent?.FieldSpellZone?.Occupant, field));
            if (fieldThere && field.FaceUp &&
                (field.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, field) != null))
            {
                engine.Log($"{field.Name}: its effect is negated — no effect.");
                return;
            }

            if (!owner.Graveyard.Contains(target) || target.GyEntryStamp != act.TargetGyStamp ||
                ContinuousProtections.GyMovementLocked(engine, field, target) || FreeZones(owner) <= 0 ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, owner, target, field, SummonOrigin.Graveyard))
            {
                engine.Log($"{field?.Name}: its target {target.Name} cannot be Special Summoned — no effect.");
                engine.NotifyPublic();
                return;
            }

            owner.Graveyard.Remove(target);
            engine.NextSpecialSummonFromGraveyard = true;
            if (!engine.SpecialSummonToField(owner, target, act.Position, true, SummonKind.SpecialSummon, act.Zone))
            {
                engine.NextSpecialSummonFromGraveyard = false;
                target.ControlOwner = null;
                if (!owner.Graveyard.Contains(target)) owner.Graveyard.Add(target);
                engine.Log($"{field?.Name}: the Special Summon of {target.Name} failed.");
                return;
            }

            engine.Log($"{field?.Name}: {owner.Name} Special Summons {target.Name} from the GY.");
            TextEffectRuntime.NotifyTargetingEffectsResolved(engine, new[] { target }, true);
            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        // ───────────────────────────── The Hex-Sealed Fusions ─────────────────────────────

        /// <summary>The monsters <paramref name="who"/> controls that can be Tributed as materials (Lua fil2: IsReleasable).</summary>
        static List<CardInstance> MaterialPool(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || ContinuousProtections.NeitherPlayerCanTribute(engine))
                return new List<CardInstance>();
            return who.MonstersOnField().Where(m => m?.Def != null && !m.CannotBeTributed).ToList();
        }

        static bool NamedAs(CardInstance c, string name) =>
            c != null && (c.IsNamed(name) || string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Every set of monsters from <paramref name="pool"/> that is exactly the named materials <paramref name="names"/>
        /// with <paramref name="self"/> among them, at most one material being a substitute (the "You can substitute this
        /// card for any 1 Fusion Material Monster" ability: Lua CheckFusionMaterial with gc = this card).
        /// </summary>
        static List<List<CardInstance>> MaterialSets(List<CardInstance> pool, CardInstance self, string[] names)
        {
            var sets = new List<List<CardInstance>>();
            if (pool == null || self == null || names == null || names.Length == 0 || !pool.Contains(self)) return sets;
            var used = new List<CardInstance>();

            void Fill(int slot, bool substituted)
            {
                if (slot == names.Length)
                {
                    if (!used.Contains(self)) return;
                    if (sets.Exists(s => s.Count == used.Count && s.All(used.Contains))) return;
                    sets.Add(used.ToList());
                    return;
                }

                foreach (var c in pool)
                {
                    if (used.Contains(c)) continue;
                    var byName = NamedAs(c, names[slot]);
                    var bySub = !byName && !substituted && FusionSubstitutesRuntime.IsFusionSubstitute(c);
                    if (!byName && !bySub) continue;
                    used.Add(c);
                    Fill(slot + 1, substituted || bySub);
                    used.RemoveAt(used.Count - 1);
                }
            }

            Fill(0, false);
            return sets;
        }

        /// <summary>
        /// One instance per distinct Fusion Monster of the clause's Attribute in the Extra Deck with named materials (2–3
        /// quoted names: SpellTrapEffects.TryGetFusionRecipe; generic materials stay ineligible) that can be Special
        /// Summoned (SpecialSummonLegality, from the Extra Deck) and whose materials are on the field with this card.
        /// </summary>
        static List<CardInstance> HexFusionCandidates(DuelEngine engine, DuelistState who, CardInstance self,
            EffectClause clause)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.ExtraDeck == null || self == null || clause == null) return list;
            if (self.CannotBeTributed || !self.FaceUp) return list;
            var pool = MaterialPool(engine, who);
            if (!pool.Contains(self)) return list;
            foreach (var id in who.ExtraDeck.Distinct().ToList())
            {
                if (!engine.Database.TryGet(id, out var def) || !IsFusion(def)) continue;
                if (!string.Equals(def.attribute, clause.AttributeFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SpellTrapEffects.TryGetFusionRecipe(id, out var names) || names == null) continue;
                var sets = MaterialSets(pool, self, names);
                if (sets.Count == 0) continue;
                var inst = engine.CreateCardInstance(id);
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, self, SummonOrigin.ExtraDeck)) continue;
                if (!sets.Exists(s => engine.MonsterZoneAfterLeaving(who, s))) continue;
                list.Add(inst);
            }

            return list;
        }

        static void UndoHexActivation(DuelEngine engine, CardInstance source, string why)
        {
            engine.DropUnresolvedLink(source);
            engine.Log($"{source?.Name}: its effect is not activated — {why}.");
            engine.ClearIdleChain();
            engine.NotifyPublic();
        }

        /// <summary>
        /// <see cref="EffectActionKind.TributeMaterialsSpecialSummonCorrespondingAttributeFusion"/>: the controller chooses
        /// the Fusion Monster (a human through the Special Summon picker: its face-up position and zone), then its other
        /// materials one at a time (only monsters that still complete a material set with this card are offered; a lone set
        /// is taken as is). CANCEL at either pick takes the activation back (nothing is Tributed). The materials are
        /// Tributed together (cost, EVENT_RELEASE) and the opponent may respond to the link; when it resolves
        /// (<see cref="TryResolveHexLink"/>) the Fusion Monster, if still in the Extra Deck, is Special Summoned face-up (not
        /// a Fusion Summon: it is not properly summoned).
        /// </summary>
        public static void ResolveHexSealed(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            if (HexFusionCandidates(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{source.Name}: no {clause.AttributeFilter} Fusion Monster's materials are on the field — " +
                           "nothing happens.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => HexFusionCandidates(engine, who, source, clause),
                Kind = EffectTargetKind.MonsterInYourDeckToSummon,
                Prompt = $"{source.Name}: choose 1 {clause.AttributeFilter} Fusion Monster in your Extra Deck to Special " +
                         "Summon (Summon ATK or Summon DEF, face-up); its materials, this card included, are Tributed. " +
                         "CANCEL = do not activate.",
                AskWithOneCandidate = engine.IsHumanControlled(who),
                AiChoose = list => list.OrderByDescending(f => f.Def?.atk ?? 0).ThenBy(f => f.Name).FirstOrDefault(),
                DuringActivation = true,
                Cancel = () => UndoHexActivation(engine, source, "no Fusion Monster was chosen"),
                Finish = fusion =>
                {
                    if (fusion == null)
                    {
                        UndoHexActivation(engine, source, "no Fusion Monster was chosen");
                        return;
                    }

                    var pick = PickFrom(engine, fusion);
                    AskHexMaterials(engine, who, source, clause, pick, new List<CardInstance> { source });
                }
            });
        }

        static List<List<CardInstance>> HexSetsWith(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance fusion, List<CardInstance> chosen)
        {
            if (!SpellTrapEffects.TryGetFusionRecipe(fusion.CardId, out var names) || names == null)
                return new List<List<CardInstance>>();
            return MaterialSets(MaterialPool(engine, who), source, names)
                .Where(s => chosen.All(s.Contains) && engine.MonsterZoneAfterLeaving(who, s))
                .ToList();
        }

        static void AskHexMaterials(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            DeckPick fusion, List<CardInstance> chosen)
        {
            var sets = HexSetsWith(engine, who, source, fusion.Card, chosen);
            if (sets.Count == 0)
            {
                UndoHexActivation(engine, source, $"the materials of {fusion.Card.Name} are no longer on the field");
                return;
            }

            var complete = sets.FirstOrDefault(s => s.Count == chosen.Count);
            if (complete != null || sets.Count == 1)
            {
                PayAndSummonHex(engine, who, source, clause, fusion, complete ?? sets[0]);
                return;
            }

            List<CardInstance> Candidates() =>
                HexSetsWith(engine, who, source, fusion.Card, chosen).SelectMany(s => s).Distinct()
                    .Where(c => !chosen.Contains(c)).ToList();

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.TributeMonsterYouControl,
                Prompt = $"{source.Name}: choose a Fusion Material Monster of {fusion.Card.Name} to Tribute with this card " +
                         "(cost). CANCEL = do not activate.",
                AiChoose = list => list.OrderBy(c => c.FaceUp ? c.CurrentAtk : (c.Def?.atk ?? 0)).FirstOrDefault(),
                DuringActivation = true,
                Cancel = () => UndoHexActivation(engine, source, "no material was chosen"),
                Finish = m =>
                {
                    if (m == null)
                    {
                        UndoHexActivation(engine, source, "no material was chosen");
                        return;
                    }

                    chosen.Add(m);
                    AskHexMaterials(engine, who, source, clause, fusion, chosen);
                }
            });
        }

        static void PayAndSummonHex(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            DeckPick fusion, List<CardInstance> materials)
        {
            // Cost: Duel.Release(mat, REASON_COST) — one Tribute event.
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var m in materials)
                    {
                        var holder = engine.ControllerOf(m);
                        if (holder == null) continue;
                        ReleaseEvents.Record(engine, m, who); // EngineHygiene: EVENT_RELEASE
                        engine.SendCardToGrave(holder, m);
                    }
                });
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }

            engine.Log($"Cost: {who.Name} Tributes {string.Join(", ", materials.Select(m => m.Name))} for {source.Name}.");
            if (engine.GameOver) return;

            // Lua s.target stores the chosen code (e:SetLabel); the opponent may respond to this Chain Link, and s.operation
            // Summons when it resolves (TryResolveHexLink). With nothing to chain, the link resolves now.
            var link = engine.Chain.Last;
            if (link == null || !ReferenceEquals(link.Card, source) || link.Resolved)
            {
                SummonHexFusion(engine, who, source, fusion);
                engine.ClearIdleChain();
                engine.FlushQueuedSummonResponses();
                engine.NotifyPublic();
                return;
            }

            s_hexLinks.Remove(link);
            s_hexLinks.Add(link, new HexHold { Who = who, Source = source, Fusion = fusion });
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

        sealed class HexHold
        {
            public DuelistState Who;
            public CardInstance Source;
            public DeckPick Fusion;
        }

        static readonly ConditionalWeakTable<ChainLink, HexHold> s_hexLinks = new();

        /// <summary>
        /// DuelEngine.ResolveChainStack: a Hex-Sealed Fusion's ignition link (its materials already Tributed as the cost)
        /// Special Summons the Fusion Monster chosen as it activated (Lua s.operation). False for any other link.
        /// </summary>
        public static bool TryResolveHexLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || !s_hexLinks.TryGetValue(link, out var hold)) return false;
            s_hexLinks.Remove(link);
            SummonHexFusion(engine, hold.Who, hold.Source, hold.Fusion);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>Lua s.operation / filter2: the chosen Fusion Monster, if it is still in the Extra Deck and can be Special
        /// Summoned with a free zone, is Special Summoned face-up (not a Fusion Summon).</summary>
        static void SummonHexFusion(DuelEngine engine, DuelistState who, CardInstance source, DeckPick fusion)
        {
            var f = fusion?.Card;
            if (engine == null || who == null || f == null || engine.GameOver) return;
            if (!who.ExtraDeck.Contains(f.CardId) || FreeZones(who) <= 0 ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, f, source, SummonOrigin.ExtraDeck))
            {
                engine.Log($"{source?.Name}: {f.Name} cannot be Special Summoned — nothing happens.");
                return;
            }

            who.ExtraDeck.Remove(f.CardId);
            if (!engine.SpecialSummonToField(who, f, fusion.Position, true, SummonKind.SpecialSummon, fusion.Zone))
            {
                who.ExtraDeck.Add(f.CardId);
                engine.Log($"{source?.Name}: the Special Summon of {f.Name} failed.");
            }
            else
                engine.Log($"{source?.Name}: {who.Name} Special Summons {f.Name} from the Extra Deck (not a Fusion Summon).");
        }
    }
}
