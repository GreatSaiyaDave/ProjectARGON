using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpecialSummonEffects" (v161, LOD). Runtime for the clauses of <see cref="SpecialSummonEffectsAtoms"/>:
    /// <list type="bullet">
    /// <item>Skull Knight #2: Tributed for the Tribute Summon of a face-up monster of the Type → Special Summon 1 copy of
    /// the named card from its owner's Deck (<see cref="NotifyTributeSummonMaterials"/>, called by
    /// DuelEngine.CompleteNormalSummon).</item>
    /// <item>Twin-Headed Behemoth: an End Phase trigger in the GY after being destroyed on the field this turn, optional,
    /// once per Duel per player (<see cref="OnEndPhaseTrigger"/>, a human answers a Yes/Cancel pick of the card itself,
    /// <see cref="OfferQueuedPick"/>); its ATK/DEF become N while it stays face-up and not negated.</item>
    /// <item>Winged Minion: the target gains ATK/DEF while it stays face-up on the field (<see cref="ApplyBoard"/>).</item>
    /// <item>The Puppet Magic of Dark Ruler: target filter (<see cref="FilterTargets"/>) and the resolution's banish of
    /// monsters whose Levels total exactly the target's Level, then its Special Summon (<see cref="ResolvePuppetMagic"/>,
    /// a human picks the monsters one by one while the Chain waits).</item>
    /// <item>Gradius' Option: its own hand procedure selects 1 face-up "Gradius" (a human with 2+ picks it first,
    /// <see cref="TrySummonBySelectingProcedure"/>); the link copies that monster's ATK/DEF and destroys this card once
    /// the selected monster leaves the field (<see cref="ApplyBoard"/>). No other Special Summon
    /// (<see cref="OnlyByOwnProcedure"/>).</item>
    /// </list>
    /// </summary>
    public static class SpecialSummonEffectsRuntime
    {
        // ───────────────────────────── state ─────────────────────────────

        /// <summary>A lasting ATK/DEF change on one field presence of a monster.</summary>
        sealed class StatGrant
        {
            public CardInstance Card;
            public int Atk;
            public int Def;
            /// <summary>"ATK/DEF become N" (ygopro EFFECT_SET_ATTACK / _DEFENSE) instead of "gains N".</summary>
            public bool SetsBase;
            /// <summary>RESET_DISABLE: the change ends for good once the monster's effects are negated.</summary>
            public bool EndsOnNegation;
            /// <summary>
            /// DestroyWatchersAndSpellShields batch: a change this monster gave itself with no RESET_DISABLE (Firebird's
            /// RESET_EVENT|RESETS_STANDARD) does not apply while its effects are negated, and applies again afterwards.
            /// </summary>
            public bool SuppressedWhileNegated;
            public int FieldEntry;
            public int FaceDown;
        }

        sealed class QueuedRevive
        {
            public DuelistState Who;
            public CardInstance Card;
            public EffectClause Clause;
            public int Turn;
        }

        enum PickKind
        {
            BehemothConfirm,
            PuppetBanish,
            OptionSelect
        }

        /// <summary>Gradius' Option and the monster it selected, on one field presence of each.</summary>
        sealed class OptionLink
        {
            public CardInstance Option;
            public CardInstance Selected;
            public int OptionEntry;
            public int SelectedEntry;
            /// <summary>Lua flag (RESETS_STANDARD_DISABLE): the self-destruction applies until it is negated or turned face-down.</summary>
            public bool Flag = true;
        }

        sealed class Pick
        {
            public PickKind Kind;
            public QueuedRevive Revive;
            public CardInstance Target;
            public int Level;
            public readonly List<CardInstance> Picked = new();
        }

        sealed class State
        {
            public readonly List<StatGrant> Grants = new();
            public readonly List<(DuelistState Who, string Name)> OncePerDuel = new();
            public readonly List<(CardInstance Card, int Turn)> Fired = new();
            public readonly List<QueuedRevive> Queue = new();
            public readonly List<OptionLink> Links = new();
            public readonly List<(CardInstance Option, CardInstance Selected)> PreSelected = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Pick> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── lasting ATK / DEF ─────────────────────────────

        /// <summary>
        /// Record a lasting ATK/DEF change on <paramref name="card"/>. <paramref name="fieldEntry"/> /
        /// <paramref name="faceDown"/> are the stamps of the field presence it belongs to (by default the current one):
        /// it ends when the monster leaves the field or is turned face-down (RESETS_STANDARD), never on a control change.
        /// </summary>
        static StatGrant AddGrant(DuelEngine engine, CardInstance card, int atk, int def, bool setsBase,
            bool endsOnNegation, int? fieldEntry = null)
        {
            var g = new StatGrant
            {
                Card = card,
                Atk = atk,
                Def = def,
                SetsBase = setsBase,
                EndsOnNegation = endsOnNegation,
                FieldEntry = fieldEntry ?? card.FieldEntryStamp,
                FaceDown = card.FaceDownStamp
            };
            StateOf(engine).Grants.Add(g);
            return g;
        }

        /// <summary>
        /// BattleOutcomeTriggers batch (v162): a lasting "gains / loses N ATK" on the current field presence of
        /// <paramref name="card"/> (Zombyra the Dark, Mucus Yolk): it ends when the monster leaves the field or is turned
        /// face-down (RESETS_STANDARD) and, with <paramref name="endsOnNegation"/>, for good once its effects are negated
        /// (RESET_DISABLE). Applied by <see cref="ApplyBoard"/> on each board refresh.
        /// </summary>
        internal static void AddLastingStatChange(DuelEngine engine, CardInstance card, int atk, int def,
            bool endsOnNegation, bool suppressedWhileNegated = false)
        {
            if (engine == null || card == null) return;
            var g = AddGrant(engine, card, atk, def, setsBase: false, endsOnNegation: endsOnNegation);
            g.SuppressedWhileNegated = suppressedWhileNegated && !endsOnNegation;
        }

        static bool GrantAlive(DuelEngine engine, StatGrant g)
        {
            var c = g?.Card;
            if (c == null || !c.FaceUp) return false;
            // Recorded for a Summon still in progress: not on the field yet (see ResolveRevive).
            if (c.FieldEntryStamp < g.FieldEntry) return true;
            var controller = engine.ControllerOf(c);
            if (controller == null || !controller.TryFindMonster(c, out _)) return false;
            if (c.FieldEntryStamp != g.FieldEntry || c.FaceDownStamp != g.FaceDown) return false;
            return !(g.EndsOnNegation && c.IsNegated);
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after the auras): add every live grant to the cleared-and-recomputed
        /// <see cref="CardInstance.AtkModifier"/> / <see cref="CardInstance.DefModifier"/>; drop the ended ones for good.
        /// "Become N" sets the monster's ATK/DEF before every other modifier (the base the auras add to).
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            for (var i = st.Grants.Count - 1; i >= 0; i--)
            {
                var g = st.Grants[i];
                if (!GrantAlive(engine, g))
                {
                    st.Grants.RemoveAt(i);
                    continue;
                }

                var c = g.Card;
                if (c.FieldEntryStamp < g.FieldEntry) continue;
                if (g.SuppressedWhileNegated && c.IsNegated) continue;
                if (g.SetsBase)
                {
                    c.AtkModifier += g.Atk - ShownBase(c, atk: true);
                    c.DefModifier += g.Def - ShownBase(c, atk: false);
                }
                else
                {
                    c.AtkModifier += g.Atk;
                    c.DefModifier += g.Def;
                }
            }

            ApplyOptionLinks(engine, st);
        }

        /// <summary>The ATK (or DEF) <see cref="CardInstance.CurrentAtk"/> starts from (original, swapped by Shield &amp; Sword).</summary>
        static int ShownBase(CardInstance c, bool atk)
        {
            var printedAtk = c.OriginalAtkOverride != int.MinValue ? c.OriginalAtkOverride : c.Def?.atk ?? 0;
            var printedDef = c.OriginalDefOverride != int.MinValue ? c.OriginalDefOverride : c.Def?.def ?? 0;
            if (c.SwapOriginalAtkDefThisTurn) (printedAtk, printedDef) = (printedDef, printedAtk);
            return Math.Max(0, atk ? printedAtk : printedDef);
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause: Winged Minion's target gains the ATK/DEF while it stays face-up on the field, if
        /// it is still a face-up monster of the Type (Lua s.filter at resolution).
        /// </summary>
        internal static void ApplyGainWhileFaceUp(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || clause == null) return;
            var controller = target != null ? engine.ControllerOf(target) : null;
            if (target == null || !target.FaceUp || controller == null || !controller.TryFindMonster(target, out _) ||
                (!string.IsNullOrEmpty(clause.RaceFilter) &&
                 !ClassicEraTemplates.RaceMatches(target.Race, clause.RaceFilter)))
            {
                engine.Log($"{source?.Name}: the target is no longer a face-up {clause.RaceFilter} monster — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            AddGrant(engine, target, clause.Amount, clause.DefAmount, setsBase: false, endsOnNegation: false);
            engine.NotifyPublic();
            engine.Log($"{target.Name} gains {clause.Amount} ATK and {clause.DefAmount} DEF while it stays face-up " +
                       $"(now {target.CurrentAtk}/{target.CurrentDef}).");
        }

        /// <summary>AI: Winged Minion goes on its controller's strongest face-up monster of the Type.</summary>
        internal static CardInstance AiPickBoostTarget(DuelEngine engine, DuelistState who, List<CardInstance> targets) =>
            targets.Where(t => who.TryFindMonster(t, out _)).OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
            ?? targets.OrderByDescending(t => t.CurrentAtk).First();

        /// <summary>
        /// SimpleAi: skip a self-Tributing boost (Winged Minion) unless the AI controls another face-up monster it could
        /// boost (otherwise its only targets are the opponent's).
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance monster)
        {
            var prog = CompiledEffectCache.GetOrCompile(monster);
            var clause = prog?.ClausesFor(EffectTiming.Activate)
                ?.FirstOrDefault(c => c != null && c.Action == EffectActionKind.TargetGainsAtkDefWhileFaceUp);
            if (clause == null) return false;
            return !ai.MonstersOnField().Any(m => m != null && !ReferenceEquals(m, monster) && m.FaceUp &&
                                                  (string.IsNullOrEmpty(clause.RaceFilter) ||
                                                   ClassicEraTemplates.RaceMatches(m.Race, clause.RaceFilter)));
        }

        // ───────────────────────────── once per Duel ─────────────────────────────

        static string DuelKey(EffectClause clause, CardInstance card) =>
            (string.IsNullOrEmpty(clause?.NamedCard) ? card?.Name ?? "" : clause.NamedCard).ToLowerInvariant();

        /// <summary><paramref name="who"/> already activated this "once per Duel" effect of a card with this name.</summary>
        public static bool OncePerDuelUsed(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance card)
        {
            if (engine == null || who == null || clause == null || !clause.OncePerDuel) return false;
            if (!s_state.TryGetValue(engine, out var st)) return false;
            var key = DuelKey(clause, card);
            return st.OncePerDuel.Exists(e => ReferenceEquals(e.Who, who) && e.Name == key);
        }

        static void MarkOncePerDuel(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance card)
        {
            if (clause == null || !clause.OncePerDuel) return;
            StateOf(engine).OncePerDuel.Add((who, DuelKey(clause, card)));
        }

        // ───────────────────────────── Skull Knight #2 ─────────────────────────────

        /// <summary>
        /// DuelEngine.CompleteNormalSummon (a Tribute Summon, not a Set, that was not negated): each Tribute whose text
        /// is "If you Tribute Summon a [Type] monster by Tributing this monster, Special Summon 1 "X" from your Deck."
        /// activates if the summoned monster is face-up and of that Type (mandatory; ygopro EVENT_BE_MATERIAL with
        /// REASON_SUMMON). Its owner (the player whose GY or banishment holds it) Special Summons the first copy of X
        /// from their Deck in face-up Attack Position, then shuffles; no free Monster Zone or no copy: nothing.
        /// </summary>
        public static void NotifyTributeSummonMaterials(DuelEngine engine, DuelistState summoner, CardInstance summoned,
            List<CardInstance> tributes, Dictionary<CardInstance, DuelistState> tributeOwners)
        {
            if (engine == null || summoned == null || tributes == null || tributes.Count == 0 || engine.GameOver) return;
            foreach (var t in tributes.ToList())
            {
                if (t?.Def == null || engine.GameOver) continue;
                var prog = CompiledEffectCache.GetOrCompile(t.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.SpecialSummonNamedFromDeckWhenTributedForRaceSummon)
                        continue;
                    if (!summoned.FaceUp || engine.ControllerOf(summoned) == null ||
                        (!string.IsNullOrEmpty(c.RaceFilter) && !ClassicEraTemplates.RaceMatches(summoned.Race, c.RaceFilter)))
                        continue;
                    var owner = HolderOf(engine, t) ??
                                (tributeOwners != null && tributeOwners.TryGetValue(t, out var o) && o != null ? o : summoner);
                    engine.Log($"{t.Name}: Tributed for the Tribute Summon of {summoned.Name} — Special Summon 1 " +
                               $"\"{c.NamedCard}\" from the Deck.");
                    SpecialSummonNamedFromDeck(engine, owner, c.NamedCard);
                }
            }
        }

        static DuelistState HolderOf(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.Graveyard.Contains(card) || side.Banished.Contains(card)) return side;
            }

            return null;
        }

        static void SpecialSummonNamedFromDeck(DuelEngine engine, DuelistState who, string name)
        {
            if (who?.Deck == null || engine.Database == null || string.IsNullOrEmpty(name)) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log("No free Monster Zone — nothing is Special Summoned.");
                return;
            }

            for (var i = 0; i < who.Deck.Count; i++)
            {
                var id = who.Deck[i];
                if (!engine.Database.TryGet(id, out var def) || def == null ||
                    !string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                var inst = engine.CreateCardInstance(id);
                // PerCopyDeck batch (v163): a failed Summon puts the same copy back (owner, face-up state).
                var copy = PerCopyDeckRuntime.TakeFromDeck(who, i, inst);
                if (!engine.SpecialSummonToField(who, inst, BattlePosition.Attack, true))
                {
                    who.Deck.Insert(i, id, copy);
                    engine.Log($"{name} cannot be Special Summoned.");
                    return;
                }

                engine.Log($"Special Summoned {inst.Name} from the Deck.");
                engine.Rng.Shuffle(who.Deck);
                return;
            }

            engine.Log($"No \"{name}\" in the Deck — nothing is Special Summoned.");
        }

        // ───────────────────────────── Twin-Headed Behemoth ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor (either player's End Phase): the optional trigger of a card in
        /// <paramref name="who"/>'s GY that was destroyed on the field and sent there this turn, once per Duel for that
        /// player. The AI activates it at once; a human is asked (Yes = select the card, Cancel = decline) once the End
        /// Phase's other triggers have fired (<see cref="OfferQueuedPick"/>). Not offered while it could not Special
        /// Summon (no free Monster Zone, Necrovalley, a summon lock).
        /// </summary>
        public static void OnEndPhaseTrigger(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || who == null || card == null || clause == null || engine.GameOver) return;
            if (!who.Graveyard.Contains(card)) return;
            if (!EquipFamilyRuntime.DestroyedOnFieldThisTurn(engine, card)) return;
            var st = StateOf(engine);
            if (st.Fired.Exists(f => ReferenceEquals(f.Card, card) && f.Turn == engine.TurnNumber)) return;
            if (OncePerDuelUsed(engine, who, clause, card)) return;
            if (!CanRevive(engine, who, card)) return;
            st.Fired.Add((card, engine.TurnNumber));
            st.Fired.RemoveAll(f => f.Turn < engine.TurnNumber - 1);
            var q = new QueuedRevive { Who = who, Card = card, Clause = clause, Turn = engine.TurnNumber };
            if (!engine.IsHumanControlled(who))
            {
                ActivateRevive(engine, q);
                return;
            }

            st.Queue.Add(q);
        }

        static bool CanRevive(DuelEngine engine, DuelistState who, CardInstance card) =>
            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, card, SummonOrigin.Graveyard,
                requireZone: true);

        /// <summary>Ask the next queued human Twin-Headed Behemoth trigger (the End Phase waits). False when nothing is asked.</summary>
        public static bool OfferQueuedPick(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Queue.Count == 0) return false;
            if (engine.PendingActivation != null || engine.IsAwaitingResponse || engine.GameOver) return false;
            while (st.Queue.Count > 0)
            {
                var q = st.Queue[0];
                st.Queue.RemoveAt(0);
                if (q.Turn != engine.TurnNumber || engine.Phase != DuelPhase.End) continue;
                if (q.Who?.Graveyard == null || !q.Who.Graveyard.Contains(q.Card)) continue;
                if (OncePerDuelUsed(engine, q.Who, q.Clause, q.Card) || !CanRevive(engine, q.Who, q.Card)) continue;
                var pending = new PendingActivation
                {
                    Controller = q.Who,
                    Card = q.Card,
                    TargetKind = EffectTargetKind.MonsterInEitherGy,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"Special Summon this card from your GY (its ATK/DEF become {q.Clause.Amount}; " +
                                     "once per Duel)? Select it to Special Summon it, or CANCEL."
                };
                pending.LegalTargets.Add(q.Card);
                s_picks.Add(pending, new Pick { Kind = PickKind.BehemothConfirm, Revive = q });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            return false;
        }

        static void ActivateRevive(DuelEngine engine, QueuedRevive q)
        {
            MarkOncePerDuel(engine, q.Who, q.Clause, q.Card);
            engine.Log($"{q.Card.Name}: its effect activates in the End Phase (once per Duel).");
            ResolveRevive(engine, q.Who, q.Card, q.Clause);
        }

        /// <summary>Special Summon it from the GY; its ATK/DEF become N (set before the Summon's response window).</summary>
        static void ResolveRevive(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (!who.Graveyard.Contains(card))
            {
                engine.Log($"{card.Name} is no longer in the GY — nothing is Special Summoned.");
                return;
            }

            if (!CanRevive(engine, who, card))
            {
                engine.Log($"{card.Name} cannot be Special Summoned now.");
                return;
            }

            who.Graveyard.Remove(card);
            DuelEngine.RecordOwnerOnFieldEntry(card, who, who);
            engine.NextSpecialSummonFromGraveyard = true;
            // The ATK/DEF change belongs to the field presence the Summon starts (its entry stamp + 1).
            var grant = AddGrant(engine, card, clause.Amount, clause.DefAmount, setsBase: true, endsOnNegation: true,
                fieldEntry: card.FieldEntryStamp + 1);
            if (!engine.SpecialSummonToField(who, card, BattlePosition.Attack, true))
            {
                StateOf(engine).Grants.Remove(grant);
                card.ControlOwner = null;
                if (!who.Graveyard.Contains(card)) who.Graveyard.Add(card);
                engine.Log($"{card.Name} cannot be Special Summoned.");
                return;
            }

            engine.NotifyPublic();
            engine.Log($"Special Summoned {card.Name} from the GY; its ATK/DEF become {clause.Amount}/{clause.DefAmount}.");
        }

        // ───────────────────────────── The Puppet Magic of Dark Ruler ─────────────────────────────

        /// <summary>Monsters <paramref name="who"/> controls that can be banished for the Level total (Level &gt; 0).</summary>
        static List<CardInstance> BanishPool(DuelistState who) =>
            who.MonstersOnField().Where(m => m != null && m.Level > 0).ToList();

        /// <summary>Every group of <paramref name="pool"/> (at least 1 monster) whose Levels total exactly <paramref name="level"/>.</summary>
        static List<List<CardInstance>> ExactSets(List<CardInstance> pool, int level, List<CardInstance> mustInclude = null)
        {
            var sets = new List<List<CardInstance>>();
            var n = Math.Min(pool.Count, 12);
            for (var mask = 1; mask < (1 << n); mask++)
            {
                var sum = 0;
                var set = new List<CardInstance>();
                for (var i = 0; i < n; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    sum += pool[i].Level;
                    set.Add(pool[i]);
                }

                if (sum != level) continue;
                if (mustInclude != null && !mustInclude.All(set.Contains)) continue;
                sets.Add(set);
            }

            return sets;
        }

        /// <summary>
        /// TextEffectRuntime.CollectTargets (batch filter tail): The Puppet Magic of Dark Ruler targets only a monster in
        /// your GY with a Level that can be Special Summoned and whose Level your monsters' Levels can total exactly.
        /// </summary>
        internal static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance source,
            List<CardInstance> list)
        {
            if (c == null || c.Action != EffectActionKind.BanishLevelSumThenSpecialSummonTarget || list == null) return;
            var pool = BanishPool(who);
            list.RemoveAll(t => t?.Def == null || t.Level <= 0 || !who.Graveyard.Contains(t) ||
                                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, t, source,
                                    SummonOrigin.Graveyard) ||
                                ExactSets(pool, t.Level).Count == 0);
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause: the target must still be in your GY and able to be Special Summoned; banish
        /// monsters you control whose Levels total exactly its Level (the AI, or a lone group, at once; a human selects
        /// them one by one while the Chain waits), then Special Summon it. No such group: nothing happens.
        /// </summary>
        internal static void ResolvePuppetMagic(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            if (target == null || !who.Graveyard.Contains(target))
            {
                engine.Log($"{source?.Name}: the target is no longer in the GY — no effect.");
                return;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, target, source, SummonOrigin.Graveyard))
            {
                engine.Log($"{source?.Name}: {target.Name} cannot be Special Summoned — no effect.");
                return;
            }

            var level = target.Level;
            var sets = ExactSets(BanishPool(who), level);
            if (sets.Count == 0)
            {
                engine.Log($"{source?.Name}: no monsters you control total Level {level} — no effect.");
                return;
            }

            if (sets.Count == 1 || !engine.IsHumanControlled(who) || engine.PendingActivation != null)
            {
                FinishPuppetMagic(engine, who, source, target, AiBanishSet(sets));
                return;
            }

            var pick = new Pick { Kind = PickKind.PuppetBanish, Target = target, Level = level };
            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                TargetKind = EffectTargetKind.YourMonster,
                UsesTextProgram = true,
                HoldsChainResolution = true,
                TargetPicksRemaining = 1
            };
            s_picks.Add(pending, pick);
            RefreshPuppetPick(engine, who, pending, pick);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
        }

        static List<CardInstance> AiBanishSet(List<List<CardInstance>> sets) =>
            sets.OrderBy(s => s.Sum(m => m.FaceUp ? m.CurrentAtk : 0))
                .ThenBy(s => s.Count)
                .First();

        static void RefreshPuppetPick(DuelEngine engine, DuelistState who, PendingActivation pending, Pick pick)
        {
            var pool = BanishPool(who);
            var sets = ExactSets(pool, pick.Level, pick.Picked);
            pending.LegalTargets.Clear();
            foreach (var m in pool)
                if (!pick.Picked.Contains(m) && sets.Exists(s => s.Contains(m)))
                    pending.LegalTargets.Add(m);
            var have = pick.Picked.Sum(m => m.Level);
            var picked = pick.Picked.Count == 0
                ? ""
                : " — " + string.Join(", ", pick.Picked.Select(m => $"{(m.FaceUp ? m.Name : "a face-down monster")} (Lv{m.Level})"));
            pending.PromptOverride = $"banish monsters you control whose Levels total exactly {pick.Level} " +
                                     $"(Levels {have}/{pick.Level}{picked}), then Special Summon {pick.Target.Name}. " +
                                     (pick.Picked.Count == 0 ? "Select a monster." : "Select the next monster, or CANCEL to start over.");
        }

        static void FinishPuppetMagic(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target,
            List<CardInstance> banish)
        {
            if (!who.Graveyard.Contains(target) || banish == null || banish.Count == 0) return;
            foreach (var m in banish)
            {
                var controller = engine.ControllerOf(m);
                if (controller != null) engine.BanishCard(controller, m, by: who);
            }

            engine.Log($"{source?.Name}: banished {string.Join(", ", banish.Select(m => m.Name))} (Levels " +
                       $"{banish.Sum(m => m.Level)}).");
            engine.NotifyPublic();
            if (engine.GameOver || !who.Graveyard.Contains(target)) return;
            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, target, source, SummonOrigin.Graveyard,
                    requireZone: true))
            {
                engine.Log($"{target.Name} cannot be Special Summoned.");
                return;
            }

            who.Graveyard.Remove(target);
            DuelEngine.RecordOwnerOnFieldEntry(target, who, who);
            engine.NextSpecialSummonFromGraveyard = true;
            if (!engine.SpecialSummonToField(who, target, BattlePosition.Attack, true))
            {
                target.ControlOwner = null;
                if (!who.Graveyard.Contains(target)) who.Graveyard.Add(target);
                engine.Log($"{target.Name} cannot be Special Summoned.");
                return;
            }

            engine.Log($"Special Summoned {target.Name} from the GY.");
        }

        // ───────────────────────────── Gradius' Option ─────────────────────────────

        static EffectClause SelectedStatsClause(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsMonster) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClauseList.FirstOrDefault(c => c != null && c.Action == EffectActionKind.AtkDefBecomeSelectedNamed);
        }

        /// <summary>
        /// DuelEngine.SpecialSummonToField: "This monster can only be Special Summoned by selecting 1 face-up "X" …" —
        /// any Special Summon other than its own hand procedure is refused (also SpecialSummonLegality's own-way-only Nomi).
        /// </summary>
        public static bool OnlyByOwnProcedure(CardInstance card) =>
            SelectedStatsClause(card) != null && ToonsRuntime.HasHandProcedure(card);

        static List<CardInstance> SelectableNamed(DuelistState who, string name) =>
            who.MonstersOnField().Where(m => m != null && m.FaceUp && m.IsNamed(name)).ToList();

        /// <summary>
        /// ToonsRuntime.TrySummonByHandProcedure (legality already checked): Gradius' Option's procedure. One face-up
        /// "Gradius" (or the AI): it is selected and the card is Special Summoned at once. A human with 2+ selects one first
        /// (Cancel = no Summon). False when the card has no such procedure.
        /// </summary>
        public static bool TrySummonBySelectingProcedure(DuelEngine engine, DuelistState who, CardInstance card, out bool ok)
        {
            ok = false;
            var clause = SelectedStatsClause(card);
            if (clause == null || engine == null || who == null) return false;
            var candidates = SelectableNamed(who, clause.NamedCard);
            if (candidates.Count == 0)
            {
                engine.Log($"Cannot Special Summon {card.Name}: no face-up \"{clause.NamedCard}\" on your side of the field.");
                return true;
            }

            if (candidates.Count == 1 || !engine.IsHumanControlled(who))
            {
                ok = SummonSelecting(engine, who, card, candidates.OrderByDescending(m => m.CurrentAtk).First());
                return true;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                TargetKind = EffectTargetKind.YourMonster,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                PromptOverride = $"select 1 face-up \"{clause.NamedCard}\" on your side of the field to Special Summon " +
                                 "this card (its ATK/DEF become that monster's), or CANCEL."
            };
            pending.LegalTargets.AddRange(candidates);
            s_picks.Add(pending, new Pick { Kind = PickKind.OptionSelect, Target = card });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            ok = true;
            return true;
        }

        static bool SummonSelecting(DuelEngine engine, DuelistState who, CardInstance card, CardInstance selected)
        {
            var st = StateOf(engine);
            st.PreSelected.RemoveAll(p => ReferenceEquals(p.Option, card));
            st.PreSelected.Add((card, selected));
            engine.Log($"{who.Name} Special Summons {card.Name} from the hand by selecting {selected.Name}.");
            var ok = engine.SpecialSummonByOwnHandProcedure(who, card, null);
            st.PreSelected.RemoveAll(p => ReferenceEquals(p.Option, card));
            return ok;
        }

        /// <summary>
        /// DuelEngine.SpecialSummonToField, right after the card is placed: Gradius' Option links to the monster its
        /// procedure selected (ygopro EVENT_SPSUMMON_SUCCESS → SetCardTarget), if that is still a face-up monster with the
        /// name on its controller's field; otherwise to such a monster with the highest ATK; none: no link (it is then
        /// destroyed on the next refresh).
        /// </summary>
        public static void OnSpecialSummoned(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var clause = SelectedStatsClause(card);
            if (clause == null || engine == null || who == null) return;
            var st = StateOf(engine);
            st.Links.RemoveAll(l => ReferenceEquals(l.Option, card));
            CardInstance selected = null;
            var pre = st.PreSelected.FirstOrDefault(p => ReferenceEquals(p.Option, card)).Selected;
            var candidates = SelectableNamed(who, clause.NamedCard);
            if (pre != null && candidates.Contains(pre)) selected = pre;
            selected ??= candidates.OrderByDescending(m => m.CurrentAtk).FirstOrDefault();
            st.Links.Add(new OptionLink
            {
                Option = card,
                Selected = selected,
                OptionEntry = card.FieldEntryStamp,
                SelectedEntry = selected?.FieldEntryStamp ?? 0
            });
            if (selected != null)
                engine.Log($"{card.Name} selects {selected.Name}: its ATK/DEF become {selected.CurrentAtk}/{selected.CurrentDef}.");
        }

        static bool OnMonsterZone(DuelEngine engine, CardInstance c)
        {
            var controller = c != null ? engine.ControllerOf(c) : null;
            return controller != null && controller.TryFindMonster(c, out _);
        }

        /// <summary>
        /// Board refresh: each linked Gradius' Option on the field copies its selected monster's current ATK/DEF (final,
        /// over every other modifier) while it is face-up and not negated. The link ends when the selected monster leaves
        /// the field (any field presence change; a control change keeps it); then the Option is destroyed, unless its summon
        /// flag ended (it was negated or turned face-down since).
        /// </summary>
        static void ApplyOptionLinks(DuelEngine engine, State st)
        {
            if (st.Links.Count == 0) return;
            List<CardInstance> doomed = null;
            for (var i = st.Links.Count - 1; i >= 0; i--)
            {
                var l = st.Links[i];
                var o = l.Option;
                if (!OnMonsterZone(engine, o) || o.FieldEntryStamp != l.OptionEntry)
                {
                    st.Links.RemoveAt(i);
                    continue;
                }

                if (l.Selected != null &&
                    (!OnMonsterZone(engine, l.Selected) || l.Selected.FieldEntryStamp != l.SelectedEntry))
                    l.Selected = null;
                if (!o.FaceUp || o.IsNegated) l.Flag = false;
                if (!o.FaceUp || o.IsNegated) continue;
                if (l.Selected == null)
                {
                    if (!l.Flag) continue;
                    st.Links.RemoveAt(i);
                    (doomed ??= new List<CardInstance>()).Add(o);
                    continue;
                }

                var g = l.Selected;
                o.OriginalAtkOverride = 0;
                o.OriginalDefOverride = 0;
                o.AtkModifier = g.CurrentAtk - (o.UntilEndOfTurnAtk + o.LingeringAtkModifier + o.DamageStepAtkBonus);
                o.DefModifier = g.CurrentDef - (o.UntilEndOfTurnDef + o.LingeringDefModifier);
            }

            if (doomed == null) return;
            foreach (var o in doomed)
            {
                if (!OnMonsterZone(engine, o)) continue;
                engine.Log($"{o.Name}: the selected monster left the field — {o.Name} is destroyed.");
                TextEffectRuntime.DestroyCard(engine, o, source: o, byPlayer: engine.ControllerOf(o));
            }
        }

        // ───────────────────────────── picks ─────────────────────────────

        /// <summary>SpellTrapEffects.TryResolveWithTarget hook: a Twin-Headed Behemoth Yes, or a Puppet Magic banish pick.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick)) return false;
            var chosen = engine.ResolveLegalEffectTarget(target);
            if (chosen == null)
            {
                engine.Log("Choose one of the highlighted cards.");
                return true;
            }

            if (pick.Kind == PickKind.BehemothConfirm)
            {
                s_picks.Remove(pending);
                engine.ClearPendingActivation();
                ActivateRevive(engine, pick.Revive);
                ok = true;
                GoOnAfterEndPhasePick(engine);
                return true;
            }

            if (pick.Kind == PickKind.OptionSelect)
            {
                s_picks.Remove(pending);
                engine.ClearPendingActivation();
                var owner = pending.Controller;
                ok = owner != null && owner.Hand.Contains(pick.Target) &&
                     ToonsRuntime.CanSummonByHandProcedure(engine, owner, pick.Target, out _) &&
                     SummonSelecting(engine, owner, pick.Target, chosen);
                engine.NotifyPublic();
                return true;
            }

            pick.Picked.Add(chosen);
            var who = pending.Controller;
            if (pick.Picked.Sum(m => m.Level) == pick.Level)
            {
                s_picks.Remove(pending);
                engine.ClearPendingActivation();
                FinishPuppetMagic(engine, who, pending.Card, pick.Target, pick.Picked.ToList());
                ok = true;
                engine.ResumeChainAfterChoice();
                engine.NotifyPublic();
                return true;
            }

            RefreshPuppetPick(engine, who, pending, pick);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            ok = true;
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook: Twin-Headed Behemoth declined (not used up); a Puppet Magic selection
        /// starts over (with nothing selected yet, the automatic group is taken — the banish is not optional).
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick)) return false;
            if (pick.Kind == PickKind.BehemothConfirm)
            {
                s_picks.Remove(pending);
                engine.ClearPendingActivation();
                engine.Log($"{pick.Revive.Card.Name}: its effect is not activated.");
                GoOnAfterEndPhasePick(engine);
                return true;
            }

            if (pick.Kind == PickKind.OptionSelect)
            {
                s_picks.Remove(pending);
                engine.ClearPendingActivation();
                engine.Log($"{pick.Target?.Name}: not Special Summoned.");
                engine.NotifyPublic();
                return true;
            }

            var who = pending.Controller;
            if (pick.Picked.Count > 0)
            {
                pick.Picked.Clear();
                RefreshPuppetPick(engine, who, pending, pick);
                engine.Log($"{pending.Card?.Name}: selection cleared — choose again.");
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            var sets = ExactSets(BanishPool(who), pick.Level);
            if (sets.Count > 0)
                FinishPuppetMagic(engine, who, pending.Card, pick.Target, AiBanishSet(sets));
            engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
            return true;
        }

        static void GoOnAfterEndPhasePick(DuelEngine engine)
        {
            if (engine.GameOver) return;
            if (OfferQueuedPick(engine))
            {
                engine.NotifyPublic();
                return;
            }

            engine.ResumeEndTurnAfterTrigger();
            engine.NotifyPublic();
        }
    }
}
