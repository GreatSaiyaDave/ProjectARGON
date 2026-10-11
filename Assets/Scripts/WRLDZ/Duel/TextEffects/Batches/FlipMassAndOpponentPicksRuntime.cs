using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FlipMassAndOpponentPicks" runtime (atoms: <see cref="FlipMassAndOpponentPicksAtoms"/>). Reads compiled
    /// programs only — no card-id branches. Every player choice goes through
    /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(DuelEngine, DuelistState, CardInstance, CardPickRequest)"/>
    /// addressed to whichever player makes it — also the player who did NOT activate the effect (Pixie Knight, Pineapple
    /// Blast, Dimension Jar): a human answers through the card picker, the AI through <see cref="CardPickRequest.AiChoose"/>.
    /// <list type="bullet">
    /// <item><b>Dark Cat with White Tail</b> (c8634636): FLIP with targets (Lua EFFECT_FLAG_CARD_TARGET). The targets are
    /// chosen as the effect activates — on a SEGOC Chain at its activation step (<see cref="ActivateFlipTargets"/>, from
    /// SimultaneousTriggersRuntime.ActivateFlip), otherwise (a FLIP flipped by an attack, a Flip Summon with no
    /// response) at once before it resolves: N monsters the opponent controls, then M you control (this card counts).
    /// Without both groups the effect takes no targets and does nothing (Lua). At resolution the targets still on the
    /// field (same field entry) and not protected return to their owners' hands together.</item>
    /// <item><b>Huge Revolution</b> (c65396880): activation condition <see cref="YourMainPhaseWithNamedFaceUp"/> (the turn
    /// player's Main Phase 1 or 2, every name in <see cref="EffectClause.ConditionName"/> face-up on their field, and a
    /// card in the opponent's hand or on their field — Lua s.target). Resolution: the opponent's hand goes to the GY,
    /// then every card they control is destroyed; one event for the sent-to-GY triggers.</item>
    /// <item><b>Magical Merchant</b> (c32362575): FLIP, excavate down to the first Spell/Trap; it is added to the hand and
    /// the cards above it go to the GY. No Spell/Trap: the whole Deck is revealed and shuffled, nothing goes to the
    /// GY. An empty Deck: nothing.</item>
    /// <item><b>Pixie Knight</b> (c35429292): sent to the GY by battle; the controller's opponent targets 1 Spell in the
    /// controller's GY as the effect activates (<see cref="ActivateSentToGyTargets"/>, from the SimultaneousTriggersRuntime
    /// Chain every battle's "sent to the GY" triggers go on; no Spell: no target, no effect). At resolution that target,
    /// if still in the GY (same stay), is placed on top of that Deck (the PlaceTargetOnTopOfDeck arm: Necrovalley,
    /// protection).</item>
    /// <item><b>Pineapple Blast</b> (c90669991): answers only its controller's own Normal Summon
    /// (<see cref="YourNormalSummonOpponentControlsMore"/>, <see cref="SummonAnswerLegal"/>: face-up, not a Special or Flip
    /// Summon) while the opponent controls more monsters. Resolution (Lua s.activate): if the opponent still controls
    /// more, they choose which of their monsters are destroyed until both control the same number; the chosen ones are
    /// destroyed together.</item>
    /// <item><b>Dimension Jar</b> (c73414375): FLIP, the turn player and then the other player can each banish up to N
    /// monsters from their opponent's GY (a human picks one at a time, Cancel stops; the AI takes the strongest).
    /// Spirit Elimination substitutes field monsters (Lua aux.SpElimFilter); Kycoo / Necrovalley locks apply. The
    /// printed text says "monsters"; the Lua filter accepts any card — ARGON follows the print.</item>
    /// <item><b>The Spell Absorbing Life</b> needs no runtime here: FlipAllFaceDownDefenseNoFlipEffects, then the
    /// GainLifePoints arm's <see cref="EffectClause.ScaleAmountByFaceUpEffectMonsters"/> branch.</item>
    /// </list>
    /// </summary>
    public static class FlipMassAndOpponentPicksRuntime
    {
        /// <summary>Huge Revolution: the turn player's Main Phase with every <see cref="EffectClause.ConditionName"/> name
        /// ("|"-separated) face-up on their field.</summary>
        public const string YourMainPhaseWithNamedFaceUp = "YourMainPhaseWithNamedFaceUp";

        /// <summary>Pineapple Blast: answering your own Normal Summon while the opponent controls more monsters.</summary>
        public const string YourNormalSummonOpponentControlsMore = "YourNormalSummonOpponentControlsMore";

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False for another batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause c, out bool ok,
            out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case YourMainPhaseWithNamedFaceUp:
                {
                    var names = NamesOf(c.ConditionName);
                    var opp = engine?.OpponentOf(who);
                    ok = engine != null && who != null && opp != null && ReferenceEquals(engine.TurnPlayer, who) &&
                         engine.InMainPhase && names.Count > 0 &&
                         names.All(n => FieldSpellEffects.ControlsFaceUpNamed(who, n)) &&
                         (opp.Hand.Count > 0 || OpponentCardsOnField(opp).Count > 0);
                    reason = "Only during your Main Phase while " +
                             string.Join(", ", names.Select(n => "\"" + n + "\"")) +
                             " are face-up on your field (and your opponent has a card in hand or on the field).";
                    return true;
                }
                case YourNormalSummonOpponentControlsMore:
                {
                    var pr = engine?.PendingResponse;
                    ok = pr != null && pr.Timing == ResponseTiming.MonsterSummoned &&
                         OwnNormalSummonAnswerLegal(engine, who, pr.Summoner, pr.Summoned);
                    reason = "Only right after you Normal Summon a monster, while your opponent controls more " +
                             "monsters than you.";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// SpellTrapEffects.IsLegalResponseCard (MonsterSummoned): a Pineapple Blast clause answers only its controller's
        /// own Normal Summon (face-up, not Special Summoned, not Flip Summoned) while the opponent controls more monsters.
        /// True for every other clause.
        /// </summary>
        public static bool SummonAnswerLegal(DuelEngine engine, DuelistState who, EffectClause clause,
            CardInstance summoned)
        {
            if (clause == null ||
                clause.Action != EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses)
                return true;
            return OwnNormalSummonAnswerLegal(engine, who, engine?.PendingResponse?.Summoner, summoned);
        }

        static bool OwnNormalSummonAnswerLegal(DuelEngine engine, DuelistState who, DuelistState summoner,
            CardInstance summoned)
        {
            if (engine == null || who == null || summoned == null || !ReferenceEquals(summoner, who)) return false;
            if (!summoned.FaceUp || summoned.WasSpecialSummoned ||
                SummonTriggersAndTributesRuntime.WasFlipSummoned(engine, summoned))
                return false;
            var opp = engine.OpponentOf(who);
            return opp != null && opp.MonsterCount > who.MonsterCount;
        }

        static List<string> NamesOf(string list) =>
            (list ?? string.Empty).Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>Every card <paramref name="side"/> controls: monsters, Spell/Trap Zones, Field Zone, Pendulum Zones.</summary>
        static List<CardInstance> OpponentCardsOnField(DuelistState side)
        {
            var list = new List<CardInstance>();
            if (side == null) return list;

            void Add(CardInstance c)
            {
                if (c != null && !list.Contains(c)) list.Add(c);
            }

            foreach (var m in side.MonstersOnField()) Add(m);
            foreach (var st in side.SpellTrapsOnField()) Add(st);
            Add(side.FieldSpellZone?.Occupant);
            if (side.PendulumZones != null)
                foreach (var z in side.PendulumZones)
                    Add(z?.Occupant);
            return list;
        }

        // ───────────────────────────── Dark Cat with White Tail ─────────────────────────────

        sealed class Snap
        {
            public CardInstance Card;
            public int Stamp;
        }

        sealed class CatActivation
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public int Turn;
            public int SourceStamp;
            public bool ResolveAfter;
            public bool NoTargets;
            public readonly List<Snap> Opp = new();
            public readonly List<Snap> Own = new();
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, CatActivation> Cats = new();
            public readonly Dictionary<CardInstance, PixieActivation> Pixies = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static Snap SnapOf(CardInstance c) => c == null ? null : new Snap { Card = c, Stamp = c.FieldEntryStamp };

        /// <summary>
        /// SimultaneousTriggersRuntime.ActivateFlip (a FLIP with no generic targeted clause): Dark Cat with White Tail
        /// chooses its targets as it activates on the SEGOC Chain; they are used when the link resolves.
        /// </summary>
        public static void ActivateFlipTargets(DuelEngine engine, DuelistState who, CardInstance monster,
            List<EffectClause> flip)
        {
            var c = flip?.FirstOrDefault(x =>
                x != null && x.Action == EffectActionKind.ReturnOpponentAndOwnTargetsToOwnersHands);
            if (c == null || engine == null || who == null || monster == null) return;
            BeginCat(engine, who, monster, c, resolveAfter: false);
        }

        /// <summary>
        /// The <see cref="EffectActionKind.ReturnOpponentAndOwnTargetsToOwnersHands"/> arm: resolves on the targets chosen
        /// as it activated on a SEGOC Chain; activated outside one, the targets are chosen now and it resolves at once.
        /// </summary>
        public static void ApplyReturnTargets(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var st = StateOf(engine);
            if (st.Cats.TryGetValue(source, out var act))
            {
                st.Cats.Remove(source);
                if (act.Turn == engine.TurnNumber && act.SourceStamp == source.FieldEntryStamp)
                {
                    ResolveCat(engine, act);
                    return;
                }
            }

            BeginCat(engine, who, source, clause, resolveAfter: true);
        }

        /// <summary>
        /// Monsters on <paramref name="side"/>'s field the effect can target (any position): not one this battle is
        /// destroying (STATUS_BATTLE_DESTROYED), not Lord of D.-protected, not protected from targeting/this effect.
        /// </summary>
        static List<CardInstance> CatCandidates(DuelEngine engine, DuelistState side, CardInstance source,
            CatActivation act)
        {
            var list = new List<CardInstance>();
            if (engine == null || side == null) return list;
            foreach (var m in side.MonstersOnField())
            {
                if (m == null) continue;
                if (act != null && (act.Opp.Exists(s => ReferenceEquals(s.Card, m)) ||
                                    act.Own.Exists(s => ReferenceEquals(s.Card, m))))
                    continue;
                if (engine.IsBattleDestroyPending(m) || ReferenceEquals(m, engine.FlipSourceBattleDestroyPending))
                    continue;
                if (engine.IsDragonTargetProtected(m)) continue;
                if (ContinuousProtections.TargetedEffectBlocked(engine, m, source)) continue;
                // EquipLocksAndLeaveField batch: Metalsilver Armor (the source's player cannot target it).
                if (EquipLocksAndLeaveFieldRuntime.ShieldedFromSource(engine, m, source)) continue;
                list.Add(m);
            }

            return list;
        }

        static int OppCount(EffectClause c) => Math.Max(1, c?.OpponentTargetCount ?? 0);
        static int OwnCount(EffectClause c) => Math.Max(1, c?.ControllerTargetCount ?? 0);

        static void BeginCat(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            bool resolveAfter)
        {
            var act = new CatActivation
            {
                Who = who,
                Source = source,
                Clause = clause,
                Turn = engine.TurnNumber,
                SourceStamp = source.FieldEntryStamp,
                ResolveAfter = resolveAfter
            };
            var opp = engine.OpponentOf(who);
            var nOpp = OppCount(clause);
            var nOwn = OwnCount(clause);
            if (opp == null || CatCandidates(engine, opp, source, act).Count < nOpp ||
                CatCandidates(engine, who, source, act).Count < nOwn)
            {
                engine.Log($"{source.Name} FLIP: {nOpp} monster(s) your opponent controls and {nOwn} you control " +
                           "cannot all be targeted — no targets, no effect.");
                act.NoTargets = true;
                FinishCatActivation(engine, act);
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                foreach (var m in AiOppPicks(CatCandidates(engine, opp, source, act), nOpp))
                    act.Opp.Add(SnapOf(m));
                foreach (var m in AiOwnPicks(source, CatCandidates(engine, who, source, act), nOwn))
                    act.Own.Add(SnapOf(m));
                LogTargets(engine, act);
                FinishCatActivation(engine, act);
                return;
            }

            AskCatPick(engine, act);
        }

        /// <summary>The human's next target: the opponent's monsters first, then their own (a forced group is taken whole).</summary>
        static void AskCatPick(DuelEngine engine, CatActivation act)
        {
            var who = act.Who;
            var opp = engine.OpponentOf(who);
            var nOpp = OppCount(act.Clause);
            var nOwn = OwnCount(act.Clause);
            var oppStage = act.Opp.Count < nOpp;
            if (!oppStage && act.Own.Count >= nOwn)
            {
                LogTargets(engine, act);
                FinishCatActivation(engine, act);
                return;
            }

            var side = oppStage ? opp : who;
            var group = oppStage ? act.Opp : act.Own;
            var remaining = (oppStage ? nOpp : nOwn) - group.Count;
            var now = CatCandidates(engine, side, act.Source, act);
            if (now.Count < remaining)
            {
                engine.Log($"{act.Source.Name}: not enough monsters can be targeted — no targets, no effect.");
                act.Opp.Clear();
                act.Own.Clear();
                act.NoTargets = true;
                FinishCatActivation(engine, act);
                return;
            }

            if (now.Count == remaining)
            {
                foreach (var m in now) group.Add(SnapOf(m));
                AskCatPick(engine, act);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, act.Source, new CardPickRequest
            {
                Candidates = () => CatCandidates(engine, side, act.Source, act),
                Kind = oppStage ? EffectTargetKind.OppMonster : EffectTargetKind.YourMonster,
                Prompt = (oppStage ? $"target {remaining}{(group.Count > 0 ? " more" : "")} monster(s) your opponent controls"
                                   : $"target {remaining} monster(s) you control") +
                         " (they return to their owners' hands).",
                AiChoose = list => oppStage
                    ? AiOppPicks(list, 1).FirstOrDefault()
                    : AiOwnPicks(act.Source, list, 1).FirstOrDefault(),
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{act.Source.Name}: not enough monsters can be targeted — no targets, no effect.");
                        act.Opp.Clear();
                        act.Own.Clear();
                        act.NoTargets = true;
                        FinishCatActivation(engine, act);
                        return;
                    }

                    group.Add(SnapOf(chosen));
                    AskCatPick(engine, act);
                },
                // On a SEGOC Chain the picks are part of the activation (the Chain driver goes on after them).
                DuringActivation = !act.ResolveAfter
            });
        }

        static void LogTargets(DuelEngine engine, CatActivation act)
        {
            var names = act.Opp.Concat(act.Own).Select(s => s.Card?.Name ?? "?");
            engine.Log($"{act.Source.Name} targets {string.Join(", ", names)}.");
        }

        static void FinishCatActivation(DuelEngine engine, CatActivation act)
        {
            if (act.ResolveAfter)
            {
                ResolveCat(engine, act);
                return;
            }

            StateOf(engine).Cats[act.Source] = act;
        }

        static bool StillOnField(DuelEngine engine, Snap s) =>
            s?.Card != null && engine.ControllerOf(s.Card) != null && s.Card.FieldEntryStamp == s.Stamp &&
            s.Card.Def != null && s.Card.Def.IsMonster;

        /// <summary>Lua s.operation: the targets still related to the effect return to their owners' hands together.</summary>
        static void ResolveCat(DuelEngine engine, CatActivation act)
        {
            var source = act.Source;
            var name = source?.Name ?? "Dark Cat with White Tail";
            if (act.NoTargets)
            {
                engine.NotifyPublic();
                return;
            }

            var live = act.Opp.Concat(act.Own).Where(s => StillOnField(engine, s)).Select(s => s.Card).ToList();
            if (live.Count == 0)
            {
                engine.Log($"{name}: none of its targets is still on the field — no effect.");
                engine.NotifyPublic();
                return;
            }

            TextEffectRuntime.BeginMonsterReturnToOwnerHandBatch();
            try
            {
                foreach (var t in live)
                {
                    if (ContinuousProtections.TargetedEffectBlocked(engine, t, source))
                    {
                        engine.Log($"{name}: {t.Name} is protected at resolution.");
                        continue;
                    }

                    engine.ReturnCardToHand(t, by: act.Who);
                }
            }
            finally
            {
                TextEffectRuntime.EndMonsterReturnToOwnerHandBatch(engine);
            }

            TextEffectRuntime.NotifyTargetingEffectsResolved(engine, live, true);
            engine.NotifyPublic();
        }

        static int AiValue(CardInstance m) =>
            m == null ? 0 : m.FaceUp ? Math.Max(m.CurrentAtk, m.CurrentDef) : Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0);

        /// <summary>The opponent's strongest monsters (face-down ones count at a flat 1500: unknown to the AI).</summary>
        static IEnumerable<CardInstance> AiOppPicks(List<CardInstance> list, int n) =>
            (list ?? new List<CardInstance>())
            .OrderByDescending(m => m.FaceUp ? Math.Max(m.CurrentAtk, m.CurrentDef) : 1500)
            .Take(n);

        /// <summary>Its own: this card first (its FLIP can be used again), then the weakest.</summary>
        static IEnumerable<CardInstance> AiOwnPicks(CardInstance source, List<CardInstance> list, int n) =>
            (list ?? new List<CardInstance>())
            .OrderBy(m => ReferenceEquals(m, source) ? 0 : 1)
            .ThenBy(AiValue)
            .Take(n);

        // ───────────────────────────── Huge Revolution ─────────────────────────────

        /// <summary>
        /// The <see cref="EffectActionKind.SendOpponentHandToGyAndDestroyAllOpponentCards"/> arm (Lua s.activate): the
        /// opponent's hand to the GY, then every card they control destroyed — one event for the GY triggers.
        /// </summary>
        public static void ApplyRevolution(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var name = source?.Name ?? "Huge Revolution";
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                var hand = opp.Hand.Where(c => c != null).ToList();
                foreach (var h in hand)
                    engine.SendCardToGrave(opp, h, sentBy: source);
                if (hand.Count > 0)
                    engine.Log($"{name}: {opp.Name}'s hand ({hand.Count} card(s)) is sent to the GY.");
                var field = OpponentCardsOnField(opp);
                foreach (var c in field)
                    TextEffectRuntime.DestroyCard(engine, c, source, false, who);
                if (field.Count > 0)
                    engine.Log($"{name}: every card {opp.Name} controls is destroyed.");
            });
            engine.NotifyPublic();
        }

        /// <summary>SimpleAi (its Main Phase): activate a Set Huge Revolution (CanActivateSpellTrap checks its condition).</summary>
        public static bool AiWantsToActivateSetTrap(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null || card.FaceUp || card.SetThisTurn) return false;
            if (!ai.TryFindSpellTrap(card, out _)) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.SendOpponentHandToGyAndDestroyAllOpponentCards);
        }

        // ───────────────────────────── Magical Merchant ─────────────────────────────

        /// <summary>
        /// The <see cref="EffectActionKind.ExcavateUntilSpellTrapAddItSendRestToGy"/> arm (Lua s.operation): the first
        /// Spell/Trap from the top is added to the hand, the cards excavated above it go to the GY. No Spell/Trap: the
        /// whole Deck is revealed and shuffled.
        /// </summary>
        public static void ApplyExcavateUntilSpellTrap(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who?.Deck == null) return;
            var name = source?.Name ?? "Magical Merchant";
            var count = who.Deck.Count;
            if (count == 0)
            {
                engine.Log($"{name}: {who.Name}'s Deck is empty — nothing to excavate.");
                return;
            }

            var idx = -1;
            for (var i = 0; i < count; i++)
            {
                var def = engine.Database?.Get(who.Deck[i]);
                if (def == null || def.IsMonster || !(def.IsSpell || def.IsTrap)) continue;
                idx = i;
                break;
            }

            string NameAt(int id) => engine.Database?.Get(id)?.name ?? "?";
            if (idx < 0)
            {
                engine.Log($"{name}: no Spell/Trap — {who.Name} reveals the Deck " +
                           $"({string.Join(", ", who.Deck.Select(NameAt))}) and shuffles it.");
                engine.Rng.Shuffle(who.Deck);
                engine.NotifyPublic();
                return;
            }

            engine.Log($"{name}: {who.Name} excavates {idx + 1} card(s): " +
                       $"{string.Join(", ", who.Deck.Take(idx + 1).Select(NameAt))}.");
            var id = who.Deck[idx];
            who.Deck.RemoveAt(idx);
            var added = engine.CreateCardInstance(id);
            who.Hand.Add(added);
            engine.Log($"{name}: {who.Name} adds {added.Name} to the hand (revealed); the other {idx} excavated " +
                       "card(s) go to the GY.");
            if (idx > 0)
            {
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    TextEffectRuntime.SendTopOfDeckToGy(engine, who, idx, byEffectOf: who);
                }
                finally
                {
                    DiscardTriggersRuntime.EndScope(engine);
                }
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── Pixie Knight ─────────────────────────────

        sealed class PixieActivation
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Turn;
            public int SourceGyStamp;
            public bool ResolveAfter;
            public bool NoTarget;
            public CardInstance Target;
            public int TargetGyStamp;
        }

        /// <summary>
        /// SimultaneousTriggersRuntime (a "sent to the GY" trigger activating as its Chain Link): Pixie Knight's opponent
        /// targets the Spell now, as the effect activates (Lua EFFECT_FLAG_CARD_TARGET, SelectTarget by 1-tp in s.target;
        /// s.condition: in the GY, sent there by battle). It is used when the link resolves.
        /// </summary>
        public static void ActivateSentToGyTargets(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return;
            if (!card.WasDestroyedByBattle || !who.Graveyard.Contains(card)) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            var gy = prog != null && prog.FullyCompiled ? prog.ClausesFor(EffectTiming.SentFromFieldToGy) : null;
            if (gy == null || !gy.Exists(c =>
                    c != null && c.Action == EffectActionKind.OpponentPlacesSpellFromYourGyOnTopOfYourDeck))
                return;
            if (NegationMonstersRuntime.NegatedInGy(engine, card)) return;
            BeginPixie(engine, who, card, resolveAfter: false);
        }

        /// <summary>
        /// The <see cref="EffectActionKind.OpponentPlacesSpellFromYourGyOnTopOfYourDeck"/> arm (Lua s.operation): the
        /// Spell <paramref name="who"/>'s opponent targeted as the effect activated goes on top of <paramref name="who"/>'s
        /// Deck if it is still in that GY. Activated outside a Chain, the target is chosen now and it resolves at once.
        /// </summary>
        public static void ApplyOpponentPlacesSpellOnTop(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine?.OpponentOf(who) == null) return;
            var st = StateOf(engine);
            if (source != null && st.Pixies.TryGetValue(source, out var act))
            {
                st.Pixies.Remove(source);
                if (act.Turn == engine.TurnNumber && act.SourceGyStamp == source.GyEntryStamp &&
                    ReferenceEquals(act.Who, who))
                {
                    ResolvePixie(engine, act);
                    return;
                }
            }

            BeginPixie(engine, who, source, resolveAfter: true);
        }

        static List<CardInstance> SpellsInGy(DuelistState who) =>
            who.Graveyard.Where(g => g?.Def != null && g.Def.IsSpell && !g.Def.IsMonster).ToList();

        static void BeginPixie(DuelEngine engine, DuelistState who, CardInstance source, bool resolveAfter)
        {
            var opp = engine.OpponentOf(who);
            var name = source?.Name ?? "Pixie Knight";
            var act = new PixieActivation
            {
                Who = who,
                Source = source,
                Turn = engine.TurnNumber,
                SourceGyStamp = source?.GyEntryStamp ?? 0,
                ResolveAfter = resolveAfter
            };
            if (opp == null || SpellsInGy(who).Count == 0)
            {
                engine.Log($"{name}: no Spell in {who.Name}'s GY — no target, no effect.");
                act.NoTarget = true;
                FinishPixie(engine, act);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, opp, source, new CardPickRequest
            {
                Candidates = () => SpellsInGy(who),
                Kind = EffectTargetKind.CardInEitherGy,
                Prompt = $"your opponent's {name} — target 1 Spell in their GY; it is placed on top of their Deck.",
                AiChoose = list => list.FirstOrDefault(),
                Finish = chosen =>
                {
                    if (chosen == null)
                        act.NoTarget = true;
                    else
                    {
                        act.Target = chosen;
                        act.TargetGyStamp = chosen.GyEntryStamp;
                        engine.Log($"{name}: {opp.Name} targets {chosen.Name} in {who.Name}'s GY.");
                    }

                    FinishPixie(engine, act);
                },
                // As a Chain Link activates, the pick is part of the activation (the Chain driver goes on after it).
                DuringActivation = !resolveAfter
            });
        }

        static void FinishPixie(DuelEngine engine, PixieActivation act)
        {
            if (act.ResolveAfter)
            {
                ResolvePixie(engine, act);
                return;
            }

            if (act.Source != null) StateOf(engine).Pixies[act.Source] = act;
        }

        static void ResolvePixie(DuelEngine engine, PixieActivation act)
        {
            var name = act.Source?.Name ?? "Pixie Knight";
            if (act.NoTarget)
            {
                engine.NotifyPublic();
                return;
            }

            var t = act.Target;
            if (t == null || !act.Who.Graveyard.Contains(t) || t.GyEntryStamp != act.TargetGyStamp)
            {
                engine.Log($"{name}: its target is no longer in the GY — no effect.");
                engine.NotifyPublic();
                return;
            }

            var op = new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.PlaceTargetOnTopOfDeck,
                Side = EffectSide.Controller,
                RequiresTargetChoice = true,
                IsPsctTarget = true
            };
            var d = false;
            TextEffectRuntime.ApplyClause(engine, act.Who, act.Source, op, t, ref d, ref d);
            TextEffectRuntime.NotifyTargetingEffectsResolved(engine, new[] { t }, true);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Pineapple Blast ─────────────────────────────

        /// <summary>
        /// The <see cref="EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses"/> arm (Lua s.activate):
        /// if the opponent still controls more monsters than <paramref name="who"/>, they choose the excess one at a time
        /// (a forced remainder is taken whole) and those are destroyed together.
        /// </summary>
        public static void ApplyMatchMonsterCounts(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var name = source?.Name ?? "Pineapple Blast";
            var keep = who.MonsterCount;
            var excess = opp.MonsterCount - keep;
            if (excess <= 0)
            {
                engine.Log($"{name}: {opp.Name} no longer controls more monsters — no effect.");
                return;
            }

            var chosen = new List<CardInstance>();

            void DestroyChosen()
            {
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var m in chosen)
                        if (engine.ControllerOf(m) != null)
                            TextEffectRuntime.DestroyCard(engine, m, source, false, who);
                });
                engine.NotifyPublic();
            }

            List<CardInstance> Left() => opp.MonstersOnField().Where(m => m != null && !chosen.Contains(m)).ToList();

            void Next()
            {
                var left = excess - chosen.Count;
                var now = Left();
                if (left <= 0 || now.Count == 0)
                {
                    DestroyChosen();
                    return;
                }

                if (now.Count <= left)
                {
                    chosen.AddRange(now);
                    DestroyChosen();
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, opp, source, new CardPickRequest
                {
                    Candidates = Left,
                    Kind = EffectTargetKind.YourMonster,
                    Prompt = $"your opponent's {name} — choose {left} more monster(s) you control to destroy " +
                             $"(you keep {keep}).",
                    AiChoose = list => list.OrderBy(AiValue).FirstOrDefault(),
                    Finish = c =>
                    {
                        if (c == null)
                        {
                            DestroyChosen();
                            return;
                        }

                        chosen.Add(c);
                        Next();
                    }
                });
            }

            engine.Log($"{name}: {opp.Name} chooses {excess} of their monsters to destroy (they keep {keep}).");
            Next();
        }

        /// <summary>SpellTrapEffects.AiAutoRespond (MonsterSummoned): Pineapple Blast whenever it is offered.</summary>
        public static bool AiWantsSummonAnswer(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses);
        }

        // ───────────────────────────── Dimension Jar ─────────────────────────────

        /// <summary>
        /// The <see cref="EffectActionKind.EachPlayerMayBanishUpToMonstersFromOpponentGy"/> arm (Lua s.operation): the
        /// turn player, then the other player, can choose up to Amount monsters in their opponent's GY; all the chosen
        /// cards are banished together.
        /// </summary>
        public static void ApplyEachPlayerBanishes(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null) return;
            var max = clause != null && clause.Amount > 0 ? clause.Amount : 3;
            var first = engine.TurnPlayer ?? who;
            var second = engine.OpponentOf(first);
            var picks = new List<(DuelistState By, CardInstance Card)>();
            PlayerPicks(engine, source, first, max, picks,
                () => PlayerPicks(engine, source, second, max, picks, () => BanishAll(engine, source, picks)));
        }

        /// <summary>
        /// The monsters <paramref name="picker"/> can banish from their opponent's GY (Lua s.rmfilter): none while Kycoo
        /// stops them or Necrovalley guards that GY; Spirit Elimination swaps that player's field monsters in.
        /// </summary>
        static List<CardInstance> JarCandidates(DuelEngine engine, DuelistState picker, DuelistState gyOwner,
            List<CardInstance> chosen)
        {
            var list = new List<CardInstance>();
            if (engine == null || picker == null || gyOwner == null) return list;
            if (!RuleLocksRuntime.BanishFromGyLocked(engine, picker) &&
                !ContinuousProtections.CannotBanishFromGraveyard(engine, gyOwner))
                list.AddRange(gyOwner.Graveyard.Where(g => g?.Def != null && g.Def.IsMonster));
            SpiritEliminationRuntime.SubstituteInList(engine, list, null, false, null, false, gyOwner);
            list.RemoveAll(c => chosen.Contains(c));
            return list;
        }

        static void PlayerPicks(DuelEngine engine, CardInstance source, DuelistState picker, int max,
            List<(DuelistState By, CardInstance Card)> picks, Action done)
        {
            var gyOwner = engine.OpponentOf(picker);
            if (picker == null || gyOwner == null)
            {
                done();
                return;
            }

            var name = source?.Name ?? "Dimension Jar";
            var mine = new List<CardInstance>();
            var finished = false;

            void Done()
            {
                if (finished) return;
                finished = true;
                engine.Log(mine.Count == 0
                    ? $"{name}: {picker.Name} banishes nothing."
                    : $"{name}: {picker.Name} chooses {string.Join(", ", mine.Select(m => m.Name))}.");
                done();
            }

            List<CardInstance> Candidates() => JarCandidates(engine, picker, gyOwner, mine);

            void Ask()
            {
                if (mine.Count >= max || Candidates().Count == 0)
                {
                    Done();
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, picker, source, new CardPickRequest
                {
                    Candidates = Candidates,
                    Kind = EffectTargetKind.MonsterInEitherGy,
                    Prompt = mine.Count == 0
                        ? $"you can banish up to {max} monsters from your opponent's GY — choose one, or Cancel for none."
                        : $"choose another monster to banish ({max - mine.Count} more at most), or Cancel to stop.",
                    AiChoose = list => list.OrderByDescending(AiGyValue).FirstOrDefault(),
                    AskWithOneCandidate = true,
                    Cancel = Done,
                    Finish = c =>
                    {
                        if (c == null)
                        {
                            Done();
                            return;
                        }

                        mine.Add(c);
                        picks.Add((picker, c));
                        Ask();
                    }
                });
            }

            Ask();
        }

        static int AiGyValue(CardInstance c) => c?.Def == null ? 0 : Math.Max(c.Def.atk, c.Def.def);

        static void BanishAll(DuelEngine engine, CardInstance source, List<(DuelistState By, CardInstance Card)> picks)
        {
            foreach (var (by, card) in picks)
            {
                if (card == null) continue;
                DuelistState holder = null;
                foreach (var d in new[] { engine.Player, engine.Opponent })
                    if (d?.Graveyard != null && d.Graveyard.Contains(card))
                        holder = d;
                holder ??= engine.ControllerOf(card); // a Spirit Elimination substitute on the field
                if (holder == null) continue;
                engine.BanishCard(holder, card, by: by);
            }

            engine.NotifyPublic();
        }
    }
}
