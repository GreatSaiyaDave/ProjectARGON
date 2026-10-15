using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleTriggersAndSwarm" runtime (atoms: <see cref="SimpleTriggersAndSwarmAtoms"/>):
    /// <list type="bullet">
    /// <item><b>"When this card is sent to the GY"</b> from any location (<see cref="EffectTiming.SentToGyFromAnywhere"/>:
    /// Burning Algae, Outstanding Dog Marron). Every GY entry stamps the card (<see cref="GyEntryStamps.Entered"/>), which
    /// queues it here (<see cref="NoteEnteredGy"/>); once play is idle (DuelEngine.Notify) the queue fires
    /// (<see cref="FireQueuedSentToGy"/>): each card still in that GY on the same trip activates its trigger for its owner
    /// (mandatory, ygopro SINGLE+TRIGGER_F on EVENT_TO_GRAVE). They go on one Chain (the turn player's first) when the
    /// opponent may respond (<see cref="TriggerChainResponsesRuntime.TrySeat"/>), else they resolve at once.</item>
    /// <item><b>Outstanding Dog Marron</b> (<see cref="EffectActionKind.ShuffleThisFromGyIntoDeck"/>): still in its owner's GY
    /// on that trip (IsRelateToEffect), it is shuffled into the Deck; Necrovalley stops it.</item>
    /// <item><b>Little-Winguard</b>: its optional End Phase position change asks its controller (a yes/no tap, the AI
    /// decides), once per turn (<see cref="TryOfferOptionalPhaseClause"/>).</item>
    /// <item><b>Don Turtle / Mustering of the Dark Scorpions</b> (<see cref="EffectActionKind.SpecialSummonAnyNumberFromHand"/>):
    /// the player picks 1 to (free Monster Zones) monsters from the hand one at a time (each tap asks its face-up Attack
    /// or Defense Position and zone; CANCEL after the first = done), then they are Special Summoned together.</item>
    /// <item><b>Dark Scorpion Combination</b> (<see cref="EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn"/>):
    /// flags the named monsters; a flagged monster can attack directly (<see cref="GrantsDirectAttack"/>) and the battle
    /// damage it inflicts to the activator's opponent by a direct attack becomes N (<see cref="ChangeBattleDamage"/>).</item>
    /// </list>
    /// </summary>
    public static class SimpleTriggersAndSwarmRuntime
    {
        /// <summary>Mustering of the Dark Scorpions: a face-up <see cref="EffectClause.ConditionName"/> on your field, a free
        /// Monster Zone and a monster in your hand the clause can Special Summon (Lua s.con + s.tg chk).</summary>
        public const string FaceUpNamedAndHandSummonable = "SimpleTriggersFaceUpNamedAndHandSummonable";

        /// <summary>Dark Scorpion Combination: every <see cref="EffectClause.ConditionName"/> name ("|"-separated) face-up
        /// in your Monster Zones (Lua s.con, LOCATION_MZONE).</summary>
        public const string AllNamedFaceUpInYourMonsterZones = "SimpleTriggersAllNamedFaceUpInYourMonsterZones";

        // ───────────────────────────── "When this card is sent to the GY" ─────────────────────────────

        sealed class Queued
        {
            public CardInstance Card;
            public DuelistState Owner;
            public int Stamp;
        }

        sealed class State
        {
            public readonly List<Queued> SentToGy = new();
            public bool Firing;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        /// <summary>The GY trip whose trigger activated (Lua IsRelateToEffect for Marron's resolution).</summary>
        static readonly ConditionalWeakTable<CardInstance, StrongBox<int>> s_activatedTrip = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static List<EffectClause> SentToGyClauses(CardInstance card)
        {
            if (card?.Def == null || card.IsToken) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            var clauses = prog.ClausesFor(EffectTiming.SentToGyFromAnywhere);
            return clauses != null && clauses.Count > 0 ? clauses : null;
        }

        static DuelistState GyHolder(DuelEngine engine, CardInstance card)
        {
            if (engine?.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) return engine.Player;
            if (engine?.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card)) return engine.Opponent;
            return null;
        }

        /// <summary>
        /// <see cref="GyEntryStamps.Entered"/>: <paramref name="card"/> just entered a GY (from the hand, the Deck, the field
        /// or anywhere else). A card with a compiled "When this card is sent to the GY" trigger is queued for its owner.
        /// </summary>
        public static void NoteEnteredGy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || engine.GameOver) return;
            if (SentToGyClauses(card) == null) return;
            var holder = GyHolder(engine, card);
            if (holder == null) return;
            var st = StateOf(engine);
            st.SentToGy.RemoveAll(q => ReferenceEquals(q.Card, card));
            st.SentToGy.Add(new Queued { Card = card, Owner = holder, Stamp = card.GyEntryStamp });
        }

        /// <summary>True while a queued "sent to the GY" trigger waits for play to be idle.</summary>
        public static bool HasQueuedSentToGy(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.SentToGy.Count > 0;

        /// <summary>
        /// DuelEngine.Notify, once play is idle: the queued "When this card is sent to the GY" triggers activate. A card
        /// that left that GY (or came back on a new trip) before then has no trigger (its single effect was reset). The
        /// turn player's triggers are Chain Link 1 … then the other player's.
        /// </summary>
        public static void FireQueuedSentToGy(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st) || st.Firing ||
                st.SentToGy.Count == 0)
                return;
            st.Firing = true;
            try
            {
                var due = st.SentToGy.ToList();
                st.SentToGy.Clear();
                var live = due.Where(q => q.Owner?.Graveyard != null && q.Owner.Graveyard.Contains(q.Card) &&
                                          q.Card.GyEntryStamp == q.Stamp && SentToGyClauses(q.Card) != null &&
                                          !NegationMonstersRuntime.NegatedInGy(engine, q.Card))
                    .OrderBy(q => ReferenceEquals(q.Owner, engine.TurnPlayer) ? 0 : 1)
                    .ToList();
                if (live.Count == 0) return;
                foreach (var q in live)
                {
                    s_activatedTrip.Remove(q.Card);
                    s_activatedTrip.Add(q.Card, new StrongBox<int>(q.Stamp));
                    engine.Log($"{q.Card.Name} was sent to the GY — its effect activates.");
                }

                var seats = live.Select(q => new TriggerChainResponsesRuntime.Seat
                {
                    Controller = q.Owner,
                    Card = q.Card,
                    From = CardLocation.Graveyard,
                    Resolve = () => ResolveSentToGy(engine, q.Owner, q.Card)
                }).ToList();
                if (TriggerChainResponsesRuntime.TrySeat(engine, seats)) return;
                foreach (var q in live)
                {
                    if (engine.GameOver) return;
                    ResolveSentToGy(engine, q.Owner, q.Card);
                }

                engine.NotifyPublic();
            }
            finally
            {
                st.Firing = false;
            }
        }

        static void ResolveSentToGy(DuelEngine engine, DuelistState owner, CardInstance card)
        {
            var clauses = SentToGyClauses(card);
            if (engine == null || owner == null || clauses == null) return;
            var d = false;
            foreach (var c in clauses)
            {
                if (engine.GameOver) return;
                TextEffectRuntime.ApplyClause(engine, owner, card, c, null, ref d, ref d);
            }
        }

        /// <summary>
        /// Outstanding Dog Marron: this card, while still in <paramref name="who"/>'s GY on the trip whose trigger activated,
        /// is added to the Deck, which is then shuffled (SEQ_DECKSHUFFLE). Necrovalley keeps it in the GY.
        /// </summary>
        public static void ShuffleThisFromGyIntoDeck(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who?.Graveyard == null || who.Deck == null || source == null) return;
            // Outstanding Dog Marron's trip (SentToGyFromAnywhere) stamps the card that activated it. Roc from the
            // Valley of Haze fires on SentFromHandToGy instead, so it has no stamp — GyHandAndSummonEventTriggers
            // records the hand trip for that timing, and it is an equally strong claim on the card.
            var onThisTrip = s_activatedTrip.TryGetValue(source, out var trip) && trip.Value == source.GyEntryStamp;
            if (!who.Graveyard.Contains(source) ||
                (!onThisTrip && !GyHandAndSummonEventTriggersRuntime.CameFromHand(source)))
            {
                engine.Log($"{source.Name} is no longer in the GY — it is not shuffled into the Deck.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, source))
            {
                engine.Log($"{source.Name} cannot leave the GY (Necrovalley).");
                return;
            }

            who.Graveyard.Remove(source);
            s_activatedTrip.Remove(source);
            if (source.Def != null && source.Def.IsExtraDeck)
            {
                who.ExtraDeck?.Add(source.CardId);
                engine.Log($"{source.Name} returns to {who.Name}'s Extra Deck.");
                return;
            }

            who.Deck.Add(source.CardId);
            engine.Rng.Shuffle(who.Deck);
            engine.Log($"{source.Name} is added to {who.Name}'s Deck, which is shuffled.");
        }

        // ───────────────────────────── Little-Winguard ─────────────────────────────

        /// <summary>Once per turn: the turn and field presence an optional End Phase position change was used in.</summary>
        static readonly ConditionalWeakTable<CardInstance, Tuple<int, int>> s_phaseUsed = new();

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor: an optional "during your End Phase: You can change the battle position of
        /// this card" (<see cref="EffectActionKind.ChangeThisBattlePosition"/>, <see cref="EffectClause.IsOptional"/>). Its
        /// controller decides (a yes/no tap on the card; the AI moves an Attack Position monster to Defense), once per turn.
        /// False for any other clause.
        /// </summary>
        public static bool TryOfferOptionalPhaseClause(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c)
        {
            if (engine == null || who == null || card == null || c == null) return false;
            if (c.Timing != EffectTiming.EndPhase || c.Action != EffectActionKind.ChangeThisBattlePosition ||
                !c.IsOptional)
                return false;
            if (!card.FaceUp || !who.TryFindMonster(card, out _)) return true;
            if (c.OncePerTurn && s_phaseUsed.TryGetValue(card, out var used) && used.Item1 == engine.TurnNumber &&
                used.Item2 == card.FieldEntryStamp)
                return true;

            List<CardInstance> Candidates() =>
                card.FaceUp && !card.IsNegated && who.TryFindMonster(card, out _)
                    ? new List<CardInstance> { card }
                    : new List<CardInstance>();

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.YourMonster,
                Prompt = $"{card.Name}: tap it to change its battle position (CANCEL = keep it).",
                AskWithOneCandidate = true,
                AiChoose = list => list.FirstOrDefault(m => m.Position == BattlePosition.Attack),
                Cancel = () => engine.Log($"{card.Name}: its position is not changed."),
                Finish = chosen =>
                {
                    if (chosen == null || !card.FaceUp || !who.TryFindMonster(card, out _))
                    {
                        if (chosen == null) engine.Log($"{card.Name}: its position is not changed.");
                        return;
                    }

                    if (c.OncePerTurn)
                    {
                        s_phaseUsed.Remove(card);
                        s_phaseUsed.Add(card, Tuple.Create(engine.TurnNumber, card.FieldEntryStamp));
                    }

                    var d = false;
                    TextEffectRuntime.ApplyClause(engine, who, card, c, card, ref d, ref d);
                }
            });
            return true;
        }

        // ───────────── "During each player's End Phase, the turn player can Special Summon 1 Level 4 Normal Monster" ─────────────

        /// <summary>
        /// Two-Man Cell Battle (c25578802): an optional field trigger with EFFECT_FLAG_BOTH_SIDE. <paramref name="who"/>
        /// is the card's controller (FirePhaseTriggersFor's pass player), but the acting player is the <b>turn player</b>
        /// — the Lua's <c>Duel.IsTurnPlayer(tp)</c> over the both-side registration — and the Level 4 Normal Monster
        /// comes from that player's hand. A human is asked with a yes/no tap over that hand (declining is legal).
        /// </summary>
        public static bool TryOfferEndPhaseNormalLevelFromHand(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || source == null || c == null) return false;
            var actor = engine.TurnPlayer ?? who;
            var level = c.ExactLevel > 0 ? c.ExactLevel : 4;

            List<CardInstance> Candidates() =>
                actor?.Hand == null
                    ? new List<CardInstance>()
                    : actor.Hand
                        .Where(x => x?.Def != null && x.Def.IsMonster && !x.Def.IsExtraDeck &&
                                    x.Def.IsNormalMonster && x.Level == level)
                        .ToList();

            // The pick is the ACTOR's (the turn player): during the opponent's End Phase the AI decides for itself,
            // not this card's controller.
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, actor, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInYourHand,
                Prompt = $"{source.Name}: Special Summon 1 Level {level} Normal Monster from " +
                         $"{actor?.Name ?? "the turn player"}'s hand (CANCEL = do not)?",
                // Optional: a human is asked even when only one card qualifies.
                AskWithOneCandidate = true,
                AiChoose = list => list.Count > 0 ? list[0] : null,
                Cancel = () => engine.Log($"{source.Name}: no monster is Special Summoned."),
                Finish = chosen =>
                {
                    if (chosen == null || actor == null) return;
                    SummonChosenFromHand(engine, actor, source, chosen, level);
                }
            });
            return true;
        }

        static void SummonChosenFromHand(DuelEngine engine, DuelistState actor, CardInstance source,
            CardInstance chosen, int level)
        {
            if (source == null || chosen?.Def == null) return;
            var controller = engine.ControllerOf(source);
            if (controller == null || !controller.TryFindSpellTrap(source, out _) || !source.FaceUp) return;
            if (actor.Hand == null || !actor.Hand.Contains(chosen)) return;
            if (!chosen.Def.IsNormalMonster || chosen.Level != level) return;
            if (engine.FirstEmpty(actor.MonsterZones) < 0)
            {
                engine.Log($"{source.Name}: {actor.Name} has no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            actor.Hand.Remove(chosen);
            engine.MarkSpecialSummonFromHand(chosen);
            if (!engine.SpecialSummonToField(actor, chosen, BattlePosition.Attack, true))
            {
                actor.Hand.Add(chosen);
                engine.Log($"{source.Name}: the Special Summon of {chosen.Name} failed.");
                return;
            }

            engine.Log($"{source.Name}: {actor.Name} Special Summons {chosen.Name} from the hand.");
        }

        // ───────────────────────────── "Special Summon any number of X from your hand" ─────────────────────────────

        sealed class SwarmPick
        {
            public CardInstance Card;
            public BattlePosition Position;
            public int Zone;
        }

        static bool MatchesSwarmFilter(CardInstance h, EffectClause clause)
        {
            if (h?.Def == null || !h.Def.IsMonster || clause == null || string.IsNullOrEmpty(clause.NamedCard))
                return false;
            return clause.NamedCardIsSeries
                ? TextEffectRuntime.CardMatchesSeries(h.Def, clause.NamedCard)
                : h.IsNamed(clause.NamedCard);
        }

        static int FreeMonsterZones(DuelistState who) =>
            who?.MonsterZones == null ? 0 : who.MonsterZones.Count(z => z != null && z.IsEmpty);

        /// <summary>The hand monsters <paramref name="clause"/> can Special Summon now (Lua s.filter).</summary>
        static List<CardInstance> SwarmCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(h => !ReferenceEquals(h, source) && MatchesSwarmFilter(h, clause) &&
                                      SpecialSummonLegality.CanBeSpecialSummoned(engine, who, h, source,
                                          SummonOrigin.Hand)).ToList();

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonAnyNumberFromHand"/>: <paramref name="who"/> picks 1 to (free Monster
        /// Zones) matching monsters from the hand (with <see cref="EffectClause.SummonOnlyOneOfEachName"/>, no two with the
        /// same name), then they are Special Summoned together, face-up in the position chosen for each. An optional
        /// trigger (Don Turtle) may be declined at the first pick; a Spell's resolution (Mustering) takes at least one.
        /// </summary>
        public static void StartAnyNumberSummon(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var ft = FreeMonsterZones(who);
            if (ft <= 0 || SwarmCandidates(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: nothing in {who.Name}'s hand can be Special Summoned.");
                return;
            }

            var picks = new List<SwarmPick>();
            AskNext(engine, who, source, clause, ft, picks);
        }

        static void AskNext(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause, int ft,
            List<SwarmPick> picks)
        {
            List<CardInstance> Candidates()
            {
                if (picks.Count >= ft) return new List<CardInstance>();
                return SwarmCandidates(engine, who, source, clause)
                    .Where(h => picks.All(p => !ReferenceEquals(p.Card, h)) &&
                                (!clause.SummonOnlyOneOfEachName ||
                                 picks.All(p => !string.Equals(p.Card.Name, h.Name, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
            }

            if (Candidates().Count == 0)
            {
                SummonPicked(engine, who, source, picks);
                return;
            }

            var first = picks.Count == 0;
            // An optional trigger (Don Turtle's "you can") may be declined at the first pick; a Spell's resolution
            // (Mustering: the compiler marks its "You can" optional too) Special Summons at least one (Lua min 1).
            var optionalFirst = first && clause.IsOptional && clause.Timing != EffectTiming.Activate;
            var what = clause.NamedCardIsSeries ? $"\"{clause.NamedCard}\" monster" : $"\"{clause.NamedCard}\"";
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInYourHand,
                Prompt = first
                    ? $"{source?.Name}: choose a {what} in your hand to Special Summon" +
                      (optionalFirst ? " (CANCEL = none)." : ".")
                    : $"{source?.Name}: choose another {what} to Special Summon (CANCEL = done, {picks.Count} chosen).",
                // A lone card is still asked: a later pick may be declined (done), and every pick lets the human choose
                // its face-up Attack or Defense Position and zone (Lua POS_FACEUP). A mandatory first pick (a Spell's
                // resolution) has no Cancel: CANCEL there takes the AI's choice.
                AskWithOneCandidate = true,
                AiChoose = AiPick,
                Cancel = first && !optionalFirst
                    ? null
                    : () =>
                    {
                        if (picks.Count == 0)
                            engine.Log($"{source?.Name}: no monster is Special Summoned.");
                        SummonPicked(engine, who, source, picks);
                    },
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        SummonPicked(engine, who, source, picks);
                        return;
                    }

                    var aiPos = chosen.Def != null && chosen.Def.def > chosen.Def.atk
                        ? BattlePosition.Defense
                        : BattlePosition.Attack;
                    picks.Add(new SwarmPick
                    {
                        Card = chosen,
                        Position = engine.PendingSpecialSummonPosition ?? aiPos,
                        Zone = engine.PendingSpecialSummonZone
                    });
                    engine.ClearPendingSpecialSummonPlacement();
                    AskNext(engine, who, source, clause, ft, picks);
                }
            });
        }

        /// <summary>The AI Special Summons every monster it can, strongest first.</summary>
        static CardInstance AiPick(List<CardInstance> list) =>
            list?.OrderByDescending(h => Math.Max(h.Def?.atk ?? 0, h.Def?.def ?? 0)).FirstOrDefault();

        static void SummonPicked(DuelEngine engine, DuelistState who, CardInstance source, List<SwarmPick> picks)
        {
            if (picks.Count == 0) return;
            NegationsAndGySummonsRuntime.BeginGroupSpecialSummon(engine);
            try
            {
                foreach (var p in picks)
                {
                    if (engine.GameOver) return;
                    if (!who.Hand.Contains(p.Card))
                    {
                        engine.Log($"{p.Card.Name} is no longer in the hand — it is not Special Summoned.");
                        continue;
                    }

                    who.Hand.Remove(p.Card);
                    engine.MarkSpecialSummonFromHand(p.Card); // Chain Energy toll (RuleLocks batch)
                    if (engine.SpecialSummonToField(who, p.Card, p.Position, true, SummonKind.SpecialSummon, p.Zone))
                        engine.Log($"{source?.Name}: {who.Name} Special Summons {p.Card.Name} from the hand in face-up " +
                                   $"{p.Card.Position} Position.");
                    else
                    {
                        who.Hand.Add(p.Card);
                        engine.Log($"{p.Card.Name} could not be Special Summoned — it stays in the hand.");
                    }
                }
            }
            finally
            {
                NegationsAndGySummonsRuntime.EndGroupSpecialSummon(engine);
            }

            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False for another batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case FaceUpNamedAndHandSummonable:
                    ok = engine != null && who != null &&
                         FieldSpellEffects.ControlsFaceUpNamed(who, c.ConditionName) &&
                         FreeMonsterZones(who) > 0 && SwarmCandidates(engine, who, card, c).Count > 0;
                    reason = $"Needs a face-up \"{c.ConditionName}\" on your field, a free Monster Zone and a " +
                             $"\"{c.NamedCard}\" monster in your hand that can be Special Summoned.";
                    return true;
                case AllNamedFaceUpInYourMonsterZones:
                {
                    var names = NamesOf(c.ConditionName);
                    ok = who != null && names.Count > 0 &&
                         names.All(n => who.MonstersOnField().Any(m => m != null && m.FaceUp && IsListedAs(m, n)));
                    reason = "Only while " + string.Join(", ", names.Select(n => "\"" + n + "\"")) +
                             " are face-up on your side of the field.";
                    return true;
                }
            }

            return false;
        }

        static List<string> NamesOf(string names) =>
            string.IsNullOrEmpty(names)
                ? new List<string>()
                : names.Split('|').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

        /// <summary>
        /// <paramref name="m"/> is the card a printed name lists: its name (or rules name), or the name the card was
        /// printed with before its archetype prefix was added ("Cliff the Trap Remover" is now "Dark Scorpion - Cliff the
        /// Trap Remover"; the prefix must be the card's own archetype).
        /// </summary>
        static bool IsListedAs(CardInstance m, string printed)
        {
            if (m?.Def == null || string.IsNullOrEmpty(printed)) return false;
            if (m.IsNamed(printed)) return true;
            var arch = m.Def.archetype;
            return !string.IsNullOrEmpty(arch) &&
                   string.Equals(m.Name, arch + " - " + printed, StringComparison.OrdinalIgnoreCase);
        }

        // ───────────────────────────── Dark Scorpion Combination ─────────────────────────────

        sealed class CombinationFlag
        {
            public WeakReference<DuelEngine> Engine;
            public int Turn;
            public int FaceDownStamp;
            public int FieldEntryStamp;
            public DuelistState Activator;
            public int Damage;
            public string SourceName;
        }

        static readonly ConditionalWeakTable<CardInstance, CombinationFlag> s_combination = new();

        /// <summary>
        /// <see cref="EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn"/>: the face-up monsters
        /// <paramref name="who"/> controls with a listed name are flagged until the End Phase (RESETS_STANDARD: leaving the
        /// field or being turned face-down ends it; a control change does not).
        /// </summary>
        public static void ApplyCombination(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var names = NamesOf(clause.ConditionName);
            var flagged = new List<string>();
            foreach (var m in who.MonstersOnField().ToList())
            {
                if (m == null || !m.FaceUp || !names.Any(n => IsListedAs(m, n))) continue;
                s_combination.Remove(m);
                s_combination.Add(m, new CombinationFlag
                {
                    Engine = new WeakReference<DuelEngine>(engine),
                    Turn = engine.TurnNumber,
                    FaceDownStamp = m.FaceDownStamp,
                    FieldEntryStamp = m.FieldEntryStamp,
                    Activator = who,
                    Damage = Math.Max(0, clause.Amount),
                    SourceName = source?.Name ?? "Effect"
                });
                flagged.Add(m.Name);
            }

            engine.Log(flagged.Count == 0
                ? $"{source?.Name}: none of the listed monsters is face-up on {who.Name}'s field."
                : $"{source?.Name}: {string.Join(", ", flagged)} can attack directly this turn " +
                  $"(their direct-attack battle damage becomes {clause.Amount}).");
        }

        static CombinationFlag LiveFlag(DuelEngine engine, CardInstance m)
        {
            if (engine == null || m == null || !s_combination.TryGetValue(m, out var f)) return null;
            if (!f.Engine.TryGetTarget(out var e) || !ReferenceEquals(e, engine) || f.Turn != engine.TurnNumber ||
                f.FaceDownStamp != m.FaceDownStamp || f.FieldEntryStamp != m.FieldEntryStamp)
            {
                s_combination.Remove(m);
                return null;
            }

            return m.FaceUp && engine.ControllerOf(m) != null ? f : null;
        }

        /// <summary>DuelEngine.GrantsDirectAttack: a Dark Scorpion Combination flag (the Trap's field effect, so the
        /// monster's own negation does not remove it). Lua e1 is registered to the activator with
        /// SetTargetRange(LOCATION_MZONE,0), so it covers a flagged monster only while the activator controls it (the
        /// flag itself survives a control change: RESETS_STANDARD has no RESET_CONTROL).</summary>
        public static bool GrantsDirectAttack(DuelEngine engine, CardInstance attacker)
        {
            var f = LiveFlag(engine, attacker);
            return f != null && ReferenceEquals(engine.ControllerOf(attacker), f.Activator);
        }

        /// <summary>
        /// DuelEngine damage calculation: the battle damage a flagged <paramref name="attacker"/> inflicts to
        /// <paramref name="victim"/> by a direct attack (<paramref name="targetOrNull"/> null) becomes the flag's amount when
        /// <paramref name="victim"/> is the opponent of the Combination's activator (Lua s.rdcon, PLAYER_TARGET 1-tp).
        /// </summary>
        public static int ChangeBattleDamage(DuelEngine engine, CardInstance attacker, CardInstance targetOrNull,
            DuelistState victim, int damage)
        {
            if (damage <= 0 || targetOrNull != null || victim == null) return damage;
            var f = LiveFlag(engine, attacker);
            if (f == null || f.Damage <= 0 || !ReferenceEquals(engine.OpponentOf(f.Activator), victim)) return damage;
            if (f.Damage != damage)
                engine.Log($"{f.SourceName}: the battle damage {attacker.Name} inflicts becomes {f.Damage} ({damage} → {f.Damage}).");
            return f.Damage;
        }
    }
}
