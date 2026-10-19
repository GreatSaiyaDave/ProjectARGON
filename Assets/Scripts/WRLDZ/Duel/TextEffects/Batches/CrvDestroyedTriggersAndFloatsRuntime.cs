using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// CrvDestroyedTriggersAndFloats batch: the destroyed / sent-to-GY triggers of Poison Draw Frog, T.A.D.P.O.L.E., UFOroid,
    /// Hydrogeddon, Wroughtweiler, Winged Kuriboh and Giant Kozaky (atoms: <see cref="CrvDestroyedTriggersAndFloatsAtoms"/>).
    /// <list type="bullet">
    /// <item>Send gates (<see cref="SendMatches"/>, read by TextEffectRuntime.SentToGySendMatches): face-up as it left the
    /// field (<see cref="LeftFieldFaceUp"/>, recorded by DuelEngine.DestroyMonster / SendCardToGrave), not flipped face-up
    /// by the attack that destroyed it (<see cref="FlippedByTheBattleThatDestroyedIt"/>, recorded at the start of each
    /// Damage Step), and sent to the GY of the player who controlled it.</item>
    /// <item>Winged Kuriboh: a per-turn player record read by FieldSpellEffects.ControllerAvoidsBattleDamage.</item>
    /// <item>T.A.D.P.O.L.E.: any number of copies from the Deck, one tap each (<see cref="StartAddAnyNumberFromDeck"/>).</item>
    /// <item>Wroughtweiler: two GY targets of different kinds, chosen as the trigger activates
    /// (<see cref="ActivateSentToGyTargets"/>, from the SimultaneousTriggersRuntime activation step) and added when it
    /// resolves (<see cref="ResolveSeriesAndNamedFromGy"/>).</item>
    /// <item>Giant Kozaky: the monster self-destruction sweep (<see cref="SweepSelfDestroy"/>) and the original-ATK burn
    /// to its previous controller (<see cref="ApplyOriginalAtkDamage"/>).</item>
    /// </list>
    /// </summary>
    public static class CrvDestroyedTriggersAndFloatsRuntime
    {
        // ───────────────────────────── leave-field records ─────────────────────────────

        sealed class Box
        {
            public bool FaceUp;
        }

        sealed class FlipRecord
        {
            public int FaceUpStamp;
        }

        static readonly ConditionalWeakTable<CardInstance, Box> s_leftFaceUp = new();
        static readonly ConditionalWeakTable<CardInstance, FlipRecord> s_flippedByAttack = new();

        /// <summary>
        /// DuelEngine.DestroyMonster / SendCardToGrave: <paramref name="card"/> is leaving a Monster Zone, face-up when
        /// <paramref name="faceUp"/> (read before a "banished instead" path turns it face-up).
        /// </summary>
        public static void NoteLeavingField(CardInstance card, bool faceUp)
        {
            if (card == null) return;
            s_leftFaceUp.GetOrCreateValue(card).FaceUp = faceUp;
        }

        /// <summary>
        /// <paramref name="card"/> was face-up on the field when it last left a Monster Zone (Lua
        /// IsPreviousPosition(POS_FACEUP)). Without a record (it never left through the engine's leave paths): its current
        /// face-up state.
        /// </summary>
        public static bool LeftFieldFaceUp(CardInstance card) =>
            card != null && (s_leftFaceUp.TryGetValue(card, out var b) ? b.FaceUp : card.FaceUp);

        /// <summary>
        /// DuelEngine (start of each Damage Step, the attack and RunReplacementBattle): <paramref name="target"/> was
        /// flipped face-up by this attack when <paramref name="flipped"/>. A battler's earlier record ends here.
        /// </summary>
        public static void NoteDamageStepStart(CardInstance attacker, CardInstance target, bool flipped)
        {
            if (attacker != null) s_flippedByAttack.Remove(attacker);
            if (target == null) return;
            s_flippedByAttack.Remove(target);
            if (flipped) s_flippedByAttack.Add(target, new FlipRecord { FaceUpStamp = target.FaceUpStamp });
        }

        /// <summary>
        /// <paramref name="card"/> was attacked while face-down and flipped face-up by the Damage Step of its latest battle
        /// (no later flip, no later battle of it).
        /// </summary>
        public static bool FlippedByTheBattleThatDestroyedIt(CardInstance card) =>
            card != null && s_flippedByAttack.TryGetValue(card, out var r) && r.FaceUpStamp == card.FaceUpStamp;

        /// <summary>
        /// TextEffectRuntime.SentToGySendMatches: this batch's send gates of a "sent from the field to the GY" /
        /// "destroyed" clause. False: this send does not meet the clause's condition.
        /// </summary>
        public static bool SendMatches(DuelEngine engine, CardInstance card, EffectClause c, bool destroyedByBattle)
        {
            if (c == null) return false;
            if (c.RequiresLeftFieldFaceUp && !LeftFieldFaceUp(card)) return false;
            if (c.RequiresNotAttackedFaceDownAndDestroyedByBattle && destroyedByBattle &&
                FlippedByTheBattleThatDestroyedIt(card))
                return false;
            if (c.RequiresSentToPreviousControllersGy && !InPreviousControllersGy(engine, card)) return false;
            return true;
        }

        static bool InPreviousControllersGy(DuelEngine engine, CardInstance card)
        {
            if (engine?.Player == null || engine.Opponent == null || card == null) return false;
            var gy = engine.Player.Graveyard.Contains(card) ? engine.Player
                : engine.Opponent.Graveyard.Contains(card) ? engine.Opponent
                : null;
            return gy != null && ReferenceEquals(gy, card.GyEntryPreviousHolder);
        }

        // ───────────────────────────── Winged Kuriboh ─────────────────────────────

        sealed class Shield
        {
            public DuelistState Who;
            public int Turn;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<Shield>> s_shields = new();

        /// <summary><see cref="EffectActionKind.ControllerTakesNoBattleDamageForRestOfTurn"/>.</summary>
        public static void GrantNoBattleDamageThisTurn(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var list = s_shields.GetValue(engine, _ => new List<Shield>());
            list.RemoveAll(s => s.Turn != engine.TurnNumber);
            if (!list.Exists(s => ReferenceEquals(s.Who, who)))
                list.Add(new Shield { Who = who, Turn = engine.TurnNumber });
            engine.Log($"{source?.Name ?? "Effect"}: {who.Name} takes no battle damage for the rest of this turn.");
        }

        /// <summary>FieldSpellEffects.ControllerAvoidsBattleDamage: <paramref name="who"/> takes no battle damage this turn.</summary>
        public static bool AvoidsBattleDamage(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && s_shields.TryGetValue(engine, out var list) &&
            list.Exists(s => ReferenceEquals(s.Who, who) && s.Turn == engine.TurnNumber);

        // ───────────────────────────── T.A.D.P.O.L.E. ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.AddAnyNumberOfNamedFromDeckToHand"/>: <paramref name="who"/> chooses copies of the
        /// named card from the Deck one at a time (a human: tap one copy, CANCEL = done; at the first pick CANCEL adds none,
        /// the trigger's "you can"; the AI takes every copy), then they are added to the hand together and the Deck is
        /// shuffled.
        /// </summary>
        public static void StartAddAnyNumberFromDeck(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine?.Database == null || who?.Deck == null || string.IsNullOrEmpty(clause?.NamedCard)) return;
            var name = clause.NamedCard;
            if (DeckCopies(engine, who, name).Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no \"{name}\" in {who.Name}'s Deck.");
                return;
            }

            AskNextCopy(engine, who, source, name, new List<int>());
        }

        /// <summary>The Deck ids of copies of <paramref name="name"/>, one entry per copy.</summary>
        static List<int> DeckCopies(DuelEngine engine, DuelistState who, string name)
        {
            var list = new List<int>();
            foreach (var id in who.Deck)
                if (engine.Database.TryGet(id, out var def) && def != null &&
                    string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase))
                    list.Add(id);
            return list;
        }

        static void AskNextCopy(DuelEngine engine, DuelistState who, CardInstance source, string name, List<int> picks)
        {
            List<CardInstance> Candidates()
            {
                var copies = DeckCopies(engine, who, name);
                foreach (var id in picks) copies.Remove(id);
                return copies.Distinct().Select(engine.CreateCardInstance).ToList();
            }

            if (Candidates().Count == 0)
            {
                AddPicked(engine, who, source, picks);
                return;
            }

            var first = picks.Count == 0;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = first
                    ? $"{source?.Name}: you can add any number of \"{name}\" from your Deck to your hand — tap one copy. " +
                      "CANCEL adds none."
                    : $"{source?.Name}: tap another \"{name}\" to add it too (CANCEL = done, {picks.Count} chosen).",
                AskWithOneCandidate = true,
                AiChoose = list => list.FirstOrDefault(),
                Cancel = () =>
                {
                    if (picks.Count == 0) engine.Log($"{source?.Name}: no card is added.");
                    AddPicked(engine, who, source, picks);
                },
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        AddPicked(engine, who, source, picks);
                        return;
                    }

                    picks.Add(chosen.CardId);
                    AskNextCopy(engine, who, source, name, picks);
                }
            });
        }

        static void AddPicked(DuelEngine engine, DuelistState who, CardInstance source, List<int> picks)
        {
            if (picks.Count == 0) return;
            foreach (var id in picks)
                MonsterEffects.AddCardIdFromDeckToHand(engine, who, id, source?.Name ?? "Effect");
            engine.Rng.Shuffle(who.Deck);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Hydrogeddon ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonNamedFromDeckInChosenFaceUpPosition"/>: the first copy of the named
        /// monster in <paramref name="who"/>'s Deck that can be Special Summoned (Lua GetFirstMatchingCard), with a free
        /// Monster Zone. A human taps it and chooses face-up ATK or DEF (and the zone); CANCEL declines. The AI summons it
        /// in Attack Position. Then the Deck is shuffled.
        /// </summary>
        public static void SpecialSummonNamedFromDeck(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine?.Database == null || who?.Deck == null || string.IsNullOrEmpty(clause?.NamedCard)) return;
            var name = clause.NamedCard;

            List<CardInstance> Candidates()
            {
                var list = new List<CardInstance>();
                if (ZoneCapsRuntime.UsableMonsterZones(who) <= 0) return list;
                foreach (var id in who.Deck.Distinct().ToList())
                {
                    if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster || def.IsExtraDeck ||
                        !string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var inst = engine.CreateCardInstance(id);
                    if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck)) continue;
                    list.Add(inst);
                    break;
                }

                return list;
            }

            if (Candidates().Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no \"{name}\" in {who.Name}'s Deck can be Special Summoned" +
                           (ZoneCapsRuntime.UsableMonsterZones(who) <= 0 ? " (no free Monster Zone)." : "."));
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInYourDeckToSummon,
                Prompt = $"{source?.Name}: you can Special Summon 1 \"{name}\" from your Deck — tap it, then Summon ATK or " +
                         "Summon DEF (face-up). CANCEL summons none.",
                AskWithOneCandidate = true,
                AiChoose = list => list.FirstOrDefault(),
                Cancel = () =>
                {
                    engine.ClearPendingSpecialSummonPlacement();
                    engine.Log($"{source?.Name}: no monster is Special Summoned.");
                },
                Finish = chosen =>
                {
                    var pos = engine.PendingSpecialSummonPosition ?? BattlePosition.Attack;
                    var zone = engine.PendingSpecialSummonZone;
                    engine.ClearPendingSpecialSummonPlacement();
                    if (chosen == null) return;
                    var i = who.Deck.IndexOf(chosen.CardId);
                    if (i < 0)
                    {
                        engine.Log($"{source?.Name}: {chosen.Name} is no longer in the Deck.");
                        return;
                    }

                    var copy = PerCopyDeckRuntime.TakeFromDeck(who, i, chosen);
                    if (engine.SpecialSummonToField(who, chosen, pos, true, SummonKind.SpecialSummon, zone))
                        engine.Log($"{source?.Name}: {who.Name} Special Summons {chosen.Name} from the Deck in face-up " +
                                   $"{chosen.Position} Position.");
                    else
                    {
                        who.Deck.Insert(i, chosen.CardId, copy);
                        engine.Log($"{source?.Name}: {chosen.Name} could not be Special Summoned — it stays in the Deck.");
                    }

                    engine.Rng.Shuffle(who.Deck);
                    engine.FlushQueuedSummonResponses();
                    engine.NotifyPublic();
                }
            });
        }

        // ───────────────────────────── Wroughtweiler ─────────────────────────────

        /// <summary>
        /// One Wroughtweiler trigger: its 2 targets are chosen as it activates (Lua thtg: SelectUnselectGroup +
        /// SetTargetCard) and used when it resolves (thop: GetTargetCards, the targets still in the GY).
        /// </summary>
        sealed class PairActivation
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Turn;
            public int SourceGyStamp;
            public bool ResolveAfter;
            public bool NoTarget;
            public CardInstance Series;
            public int SeriesGyStamp;
            public CardInstance Named;
            public int NamedGyStamp;
        }

        static readonly ConditionalWeakTable<DuelEngine, Dictionary<CardInstance, PairActivation>> s_pairs = new();

        static Dictionary<CardInstance, PairActivation> Pairs(DuelEngine engine) =>
            s_pairs.GetValue(engine, _ => new Dictionary<CardInstance, PairActivation>());

        static EffectClause PairClause(CardInstance card)
        {
            var prog = card != null ? CompiledEffectCache.GetOrCompile(card) : null;
            return prog != null && prog.FullyCompiled
                ? prog.ClausesFor(EffectTiming.SentFromFieldToGy).FirstOrDefault(c =>
                    c != null && c.Action == EffectActionKind.AddTargetedSeriesCardAndNamedCardFromYourGyToHand)
                : null;
        }

        /// <summary>
        /// SimultaneousTriggersRuntime (a "sent to the GY" trigger activating as its Chain Link): a Wroughtweiler trigger's
        /// controller targets the E-HERO card and the Polymerization now, as it activates (Lua EFFECT_FLAG_CARD_TARGET).
        /// They are used when the link resolves (<see cref="ResolveSeriesAndNamedFromGy"/>).
        /// </summary>
        public static void ActivateSentToGyTargets(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null || !who.Graveyard.Contains(card)) return;
            var clause = PairClause(card);
            if (clause == null || NegationMonstersRuntime.NegatedInGy(engine, card)) return;
            BeginPair(engine, who, card, clause, resolveAfter: false);
        }

        /// <summary>The cards a Wroughtweiler trigger targeted as it activated (shown to responders on its Chain Link).</summary>
        public static IEnumerable<CardInstance> ActivationTargets(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_pairs.TryGetValue(engine, out var pairs) ||
                !pairs.TryGetValue(card, out var act))
                yield break;
            if (act.Series != null) yield return act.Series;
            if (act.Named != null) yield return act.Named;
        }

        /// <summary>
        /// <see cref="EffectActionKind.AddTargetedSeriesCardAndNamedCardFromYourGyToHand"/> (Lua thop): the targets chosen as
        /// the trigger activated that are still in <paramref name="who"/>'s GY are added to the hand; a target that left the
        /// GY is not replaced. With no legal pair at activation nothing was targeted and nothing happens. Activated outside
        /// a Chain (no activation step ran), <paramref name="who"/> targets 1 <see cref="EffectClause.NamedCard"/> series
        /// card and 1 <see cref="EffectClause.AltNamedCard"/> now (cards a Necrovalley-style lock keeps from being targeted
        /// are skipped) and it resolves at once.
        /// </summary>
        public static void ResolveSeriesAndNamedFromGy(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            if (source != null && s_pairs.TryGetValue(engine, out var pairs) && pairs.TryGetValue(source, out var act))
            {
                pairs.Remove(source);
                if (act.Turn == engine.TurnNumber && act.SourceGyStamp == source.GyEntryStamp &&
                    ReferenceEquals(act.Who, who))
                {
                    ResolvePair(engine, act);
                    return;
                }
            }

            BeginPair(engine, who, source, clause, resolveAfter: true);
        }

        static void BeginPair(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            bool resolveAfter)
        {
            var act = new PairActivation
            {
                Who = who,
                Source = source,
                Turn = engine.TurnNumber,
                SourceGyStamp = source?.GyEntryStamp ?? 0,
                ResolveAfter = resolveAfter
            };
            List<CardInstance> Series() => who.Graveyard.Where(g =>
                g?.Def != null && SeriesMatch(g.Def, clause.NamedCard) &&
                !ContinuousProtections.CannotTargetCardInGy(engine, source, g)).ToList();
            List<CardInstance> Named(CardInstance except) => who.Graveyard.Where(g =>
                g?.Def != null && !ReferenceEquals(g, except) && g.IsNamed(clause.AltNamedCard) &&
                !ContinuousProtections.CannotTargetCardInGy(engine, source, g)).ToList();

            if (!Series().Any(s => Named(s).Count > 0))
            {
                engine.Log($"{source?.Name ?? "Effect"}: {who.Name}'s GY lacks a \"{clause.NamedCard}\" card or a " +
                           $"\"{clause.AltNamedCard}\" to target — nothing happens.");
                act.NoTarget = true;
                FinishPair(engine, act);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => Series().Where(s => Named(s).Count > 0).ToList(),
                Kind = EffectTargetKind.CardInEitherGy,
                Prompt = $"{source?.Name}: target 1 \"{clause.NamedCard}\" card in your GY to add to your hand.",
                AiChoose = list => list.OrderByDescending(c => c.Def?.IsMonster == true ? c.Def.atk : -1).FirstOrDefault(),
                // As a Chain Link activates, the picks are part of the activation (the Chain driver goes on after them).
                DuringActivation = !resolveAfter,
                Finish = hero =>
                {
                    if (hero == null)
                    {
                        act.NoTarget = true;
                        FinishPair(engine, act);
                        return;
                    }

                    ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                    {
                        Candidates = () => Named(hero),
                        Kind = EffectTargetKind.CardInEitherGy,
                        Prompt = $"{source?.Name}: target 1 \"{clause.AltNamedCard}\" in your GY to add to your hand.",
                        AiChoose = list => list.FirstOrDefault(),
                        DuringActivation = !resolveAfter,
                        Finish = named =>
                        {
                            if (named == null)
                                act.NoTarget = true;
                            else
                            {
                                act.Series = hero;
                                act.SeriesGyStamp = hero.GyEntryStamp;
                                act.Named = named;
                                act.NamedGyStamp = named.GyEntryStamp;
                                engine.Log($"{source?.Name}: targets {hero.Name} and {named.Name} in {who.Name}'s GY.");
                            }

                            FinishPair(engine, act);
                        }
                    });
                }
            });
        }

        static void FinishPair(DuelEngine engine, PairActivation act)
        {
            if (act.ResolveAfter)
            {
                ResolvePair(engine, act);
                return;
            }

            if (act.Source != null) Pairs(engine)[act.Source] = act;
        }

        static void ResolvePair(DuelEngine engine, PairActivation act)
        {
            if (act.NoTarget)
            {
                engine.NotifyPublic();
                return;
            }

            var added = new List<CardInstance>();
            foreach (var (target, stamp) in new[] { (act.Series, act.SeriesGyStamp), (act.Named, act.NamedGyStamp) })
            {
                if (target == null) continue;
                if (!act.Who.Graveyard.Contains(target) || target.GyEntryStamp != stamp)
                {
                    engine.Log($"{act.Source?.Name}: its target {target.Name} is no longer in the GY.");
                    continue;
                }

                if (AddFromGy(engine, act.Who, act.Source, target)) added.Add(target);
            }

            if (added.Count > 0) TextEffectRuntime.NotifyTargetingEffectsResolved(engine, added, true);
            engine.NotifyPublic();
        }

        /// <summary>Lua IsSetCard on the printed name or the archetype (TextEffectRuntime.NameMatchesQuoted, series).</summary>
        static bool SeriesMatch(CardDef def, string series)
        {
            if (def == null || string.IsNullOrEmpty(series)) return false;
            return (def.name != null && def.name.IndexOf(series, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (def.archetype != null && def.archetype.IndexOf(series, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool AddFromGy(DuelEngine engine, DuelistState who, CardInstance source, CardInstance card)
        {
            if (!who.Graveyard.Contains(card))
            {
                engine.Log($"{source?.Name}: {card.Name} is no longer in the GY.");
                return false;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, card))
            {
                engine.Log($"{source?.Name}: {card.Name} cannot leave the GY.");
                return false;
            }

            who.Graveyard.Remove(card);
            if (card.Def != null && card.Def.IsExtraDeck)
            {
                // An Extra Deck monster "added to the hand" returns to the Extra Deck (Lua SendtoHand).
                who.ExtraDeck.Add(card.CardId);
                engine.Log($"{source?.Name}: {card.Name} returns from the GY to the Extra Deck.");
                return true;
            }

            who.Hand.Add(card);
            engine.Log($"{source?.Name}: added {card.Name} from the GY to {who.Name}'s hand.");
            return true;
        }

        // ───────────────────────────── Giant Kozaky ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.PreviousControllerTakesThisOriginalAtkAsDamage"/>: <paramref name="who"/> (the player
        /// who controlled <paramref name="source"/> as it was destroyed) takes damage equal to its printed original ATK.
        /// </summary>
        public static void ApplyOriginalAtkDamage(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source?.Def == null) return;
            var amount = Math.Max(0, source.Def.atk);
            engine.Log($"{source.Name}: {who.Name} takes damage equal to its original ATK ({amount}).");
            engine.ApplyEffectDamage(who, amount, source.Name);
        }

        static bool s_sweeping;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: a face-up, non-negated monster with a ContinuousWhileFaceUp
        /// <see cref="EffectActionKind.SelfDestroyUnlessNamedFaceUp"/> clause is destroyed by its own effect while the card it
        /// needs is missing (FieldSpellEffects.SelfDestroyNamedRequirementMet, the Spell/Trap sweep's test: by default a
        /// face-up card named <see cref="EffectClause.RequiresFaceUpName"/> on either field; "you control" / series / "in your
        /// Monster Zone" when the clause says so). ygopro EFFECT_SELF_DESTROY: Giant Kozaky ("Kozaky") and Zeradias, Herald of
        /// Heaven ("The Sanctuary in the Sky"; its Duel.IsEnvironment half is the same Field Spell, as no card changes the
        /// environment to it). Not while it is being destroyed by battle, not during a pick, not in the Damage Step before
        /// damage calculation is over, not before its Summon succeeded. The Spell/Trap form is
        /// FieldSpellEffects.DestroyNamedLeavesContinuous.
        /// </summary>
        public static void SweepSelfDestroy(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver || s_sweeping) return;
            if (engine.PendingActivation != null) return;
            if (engine.BattleStep == BattleStep.DamageStep &&
                (engine.DamageSubStep == DamageSubStep.Start ||
                 engine.DamageSubStep == DamageSubStep.BeforeDamageCalculation ||
                 engine.DamageSubStep == DamageSubStep.DamageCalculation))
                return;
            var doomed = new List<(DuelistState Side, CardInstance Card, string Name)>();
            foreach (var side in new[] { engine.TurnPlayer ?? engine.Player, engine.OpponentOf(engine.TurnPlayer ?? engine.Player) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m?.Def == null || !m.FaceUp || m.IsNegated || engine.IsSummonPending(m) ||
                        engine.IsDeferredBattleDestroy(m))
                        continue;
                    var prog = CompiledEffectCache.GetOrCompile(m.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    var c = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).FirstOrDefault(x =>
                        x != null && x.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp &&
                        !string.IsNullOrEmpty(x.RequiresFaceUpName) &&
                        !FieldSpellEffects.SelfDestroyNamedRequirementMet(engine, side, x));
                    if (c == null) continue;
                    doomed.Add((side, m, c.RequiresFaceUpName));
                }
            }

            if (doomed.Count == 0) return;
            s_sweeping = true;
            try
            {
                foreach (var (side, card, name) in doomed)
                {
                    if (engine.GameOver) return;
                    if (!side.TryFindMonster(card, out _) || !card.FaceUp) continue;
                    engine.Log($"{card.Name}: the \"{name}\" it needs is not on the field — it is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, card, card, byPlayer: side);
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }
    }
}
