using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// NormalMonsterSpells batch runtime (atoms: <see cref="NormalMonsterSpellsAtoms"/>). Reads compiled clauses only — no
    /// card-id branches.
    /// <list type="bullet">
    /// <item><b>Normal Monster.</b> ygopro TYPE_NORMAL: a Normal Monster card or a Token (every ygopro Token is
    /// TYPE_NORMAL, cards.cdb 0x4011). Each action leaves Tokens out only where its Lua filter does.</item>
    /// <item><b>Delta Attacker</b>: <see cref="ResolveDeltaAttacker"/> (<see cref="CardInstance.DirectAttackThisTurn"/>).</item>
    /// <item><b>Thousand Energy / Triangle Power</b>: <see cref="ResolveOriginalBoost"/> sets the original ATK/DEF
    /// (<see cref="CardInstance.OriginalAtkOverride"/> / <see cref="CardInstance.OriginalDefOverride"/>; put back by
    /// <see cref="Refresh"/> once the monster leaves the field or is turned face-down: RESETS_STANDARD) and registers this
    /// turn's End Phase destruction (<see cref="OnEndPhase"/>, Lua EVENT_PHASE+PHASE_END, count limit 1).</item>
    /// <item><b>The Law of the Normal</b>: <see cref="ResolveLawOfTheNormal"/>.</item>
    /// <item><b>Order to Smash</b>: <see cref="ResolveOrderToSmash"/> (a Tribute by the effect, then 1 or 2 picks through
    /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick"/>).</item>
    /// <item><b>Equip limits</b> (Sword of the Soul-Eater, Opti-Camouflage Armor): <see cref="EquipLimitAllows"/> (target
    /// filter, <see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/>, resolution) and the continuous check in
    /// <see cref="Refresh"/> (ygopro EFFECT_EQUIP_LIMIT: a monster that no longer fits loses the Equip Card).</item>
    /// <item><b>Sword of the Soul-Eater's Tribute</b>: <see cref="OnActivationSeated"/> (as the Chain Link is seated, after
    /// the target) and the ATK it gives (<see cref="NoteEquipResolved"/>, <see cref="EquipAtkOf"/>).</item>
    /// </list>
    /// </summary>
    public static class NormalMonsterSpellsRuntime
    {
        /// <summary>Delta Attacker: ConditionAmount face-up non-Token Normal Monsters of one name you control.</summary>
        public const string NormalsSharingNameFaceUp = "NmsNormalsSharingNameFaceUp";

        /// <summary>Thousand Energy / Triangle Power: a face-up non-Token Normal Monster of the clause's Level you control.</summary>
        public const string FaceUpNonTokenNormalOfLevel = "NmsFaceUpNonTokenNormalOfLevel";

        /// <summary>
        /// The Law of the Normal: ConditionAmount face-up Normal Monsters of Level MonsterLevelMax or lower you control
        /// (s.condition), another card in your hand, a card in the opponent's hand and a card to destroy (s.target chk).
        /// </summary>
        public const string LowLevelNormalsAndCardsToDiscardAndDestroy = "NmsLowLevelNormalsDiscardDestroy";

        /// <summary>Order to Smash (s.target chk): the opponent controls a Spell/Trap Card (face-up or face-down).</summary>
        public const string OpponentControlsSpellTrap = "NmsOpponentControlsSpellTrap";

        // ───────────────────────────── shared filters ─────────────────────────────

        /// <summary>ygopro IsType(TYPE_NORMAL): a Normal Monster card, or a Token.</summary>
        internal static bool IsNormalType(CardInstance m) =>
            m?.Def != null && (m.IsToken || (m.Def.IsMonster && m.Def.IsNormalMonster));

        static bool LevelFits(EffectClause c, CardInstance m) =>
            m != null && (c.MonsterLevelMin <= 0 || m.Level >= c.MonsterLevelMin) &&
            (c.MonsterLevelMax <= 0 || m.Level <= c.MonsterLevelMax);

        static bool HasEquipLimit(EffectClause c) =>
            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
            (c.MonsterLevelMin > 0 || c.MonsterLevelMax > 0 || c.RequiresNormalType);

        /// <summary>
        /// The Level / Normal Monster part of an equip limit (<see cref="EffectClause.MonsterLevelMin"/>,
        /// <see cref="EffectClause.MonsterLevelMax"/>, <see cref="EffectClause.RequiresNormalType"/>): true when
        /// <paramref name="clause"/> carries none of them.
        /// </summary>
        public static bool EquipLimitAllows(EffectClause clause, CardInstance monster)
        {
            if (!HasEquipLimit(clause)) return true;
            if (monster?.Def == null) return false;
            if (clause.RequiresNormalType && !IsNormalType(monster)) return false;
            return LevelFits(clause, monster);
        }

        static IEnumerable<CardInstance> FaceUpMonsters(DuelistState who) =>
            who?.MonstersOnField().Where(m => m?.Def != null && m.FaceUp) ?? Enumerable.Empty<CardInstance>();

        /// <summary>Delta Attacker s.filter: face-up non-Token Normal Monsters you control with N-1 others of their name.</summary>
        static List<CardInstance> SameNameGroupMembers(DuelistState who, int n)
        {
            var normals = FaceUpMonsters(who).Where(m => !m.IsToken && IsNormalType(m)).ToList();
            return normals.Where(m => normals.Count(o => string.Equals(o.RulesName, m.RulesName,
                StringComparison.OrdinalIgnoreCase)) >= Math.Max(1, n)).ToList();
        }

        static List<CardInstance> OppSpellTraps(DuelistState opp)
        {
            var list = new List<CardInstance>();
            if (opp == null) return list;
            list.AddRange(opp.SpellTrapsOnField().Where(c => c != null));
            if (opp.FieldSpellZone?.Occupant != null) list.Add(opp.FieldSpellZone.Occupant);
            return list;
        }

        static List<CardInstance> CardsOnField(DuelistState who)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            list.AddRange(who.MonstersOnField().Where(c => c != null));
            list.AddRange(who.SpellTrapsOnField().Where(c => c != null));
            if (who.FieldSpellZone?.Occupant != null) list.Add(who.FieldSpellZone.Occupant);
            return list;
        }

        /// <summary>The Law of the Normal s.dfilter keeps a face-up Normal Monster of Level MonsterLevelMax or lower.</summary>
        static bool KeptByLaw(EffectClause c, DuelEngine engine, CardInstance card) =>
            card != null && card.FaceUp && IsNormalType(card) && LevelFits(c, card) &&
            (engine.Player.TryFindMonster(card, out _) || engine.Opponent.TryFindMonster(card, out _));

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            var opp = engine.OpponentOf(who);
            switch (c.ActivationCondition)
            {
                case NormalsSharingNameFaceUp:
                    ok = SameNameGroupMembers(who, c.ConditionAmount).Count >= Math.Max(1, c.ConditionAmount);
                    reason = $"Only while {c.ConditionAmount} face-up Normal Monsters of the same name (not Tokens) are " +
                             "on your side of the field.";
                    return true;
                case FaceUpNonTokenNormalOfLevel:
                    ok = FaceUpMonsters(who).Any(m => !m.IsToken && IsNormalType(m) && LevelFits(c, m));
                    reason = $"Needs a face-up Level {c.MonsterLevelMin} Normal Monster (not a Token) you control.";
                    return true;
                case LowLevelNormalsAndCardsToDiscardAndDestroy:
                {
                    var normals = FaceUpMonsters(who).Count(m => IsNormalType(m) && LevelFits(c, m));
                    var handOk = who.Hand.Any(h => h != null && !ReferenceEquals(h, card));
                    var oppHand = opp?.Hand != null && opp.Hand.Any(h => h != null);
                    var something = CardsOnField(who).Concat(CardsOnField(opp))
                        .Any(x => !ReferenceEquals(x, card) && !KeptByLaw(c, engine, x));
                    ok = normals >= Math.Max(1, c.ConditionAmount) && handOk && oppHand && something;
                    reason = $"Only while {c.ConditionAmount} face-up Level {c.MonsterLevelMax} or lower Normal Monsters " +
                             "are on your side of the field, with another card in your hand, a card in your opponent's " +
                             "hand and a card on the field to destroy.";
                    return true;
                }
                case OpponentControlsSpellTrap:
                    ok = OppSpellTraps(opp).Count > 0;
                    reason = "Your opponent must control a Spell/Trap Card.";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>CorpusTriggerStressTests.NeedsHardBoard: these conditions need a provisioned board (this batch's tests).</summary>
        public static bool NeedsHardBoard(EffectClause c) =>
            c != null && (c.ActivationCondition == NormalsSharingNameFaceUp ||
                          c.ActivationCondition == FaceUpNonTokenNormalOfLevel ||
                          c.ActivationCondition == LowLevelNormalsAndCardsToDiscardAndDestroy ||
                          c.Action == EffectActionKind.TributeTargetedNormalThenDestroyUpToOppSpellTraps);

        /// <summary>
        /// SpellTrapEffects.UsesCompiledActivationPending: Order to Smash asks its target as it is activated (no legacy
        /// list has the Level / Normal Monster filter).
        /// </summary>
        public static bool UsesCompiledActivationPending(EffectClause c) =>
            c != null && c.Action == EffectActionKind.TributeTargetedNormalThenDestroyUpToOppSpellTraps;

        // ───────────────────────────── targets ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CollectTargets: the equip limit of this batch's Equip Spells, and Order to Smash's target
        /// (s.rfilter: face-up, non-Token TYPE_NORMAL, Level N or lower, releasable by an effect).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance except,
            List<CardInstance> list)
        {
            if (engine == null || c == null || list == null) return;
            if (HasEquipLimit(c))
                list.RemoveAll(t => !EquipLimitAllows(c, t));
            if (c.Action == EffectActionKind.TributeTargetedNormalThenDestroyUpToOppSpellTraps)
            {
                if (ContinuousProtections.NeitherPlayerCanTribute(engine))
                {
                    list.Clear();
                    return;
                }

                list.RemoveAll(t => t?.Def == null || !t.FaceUp || t.IsToken || !IsNormalType(t) || !LevelFits(c, t) ||
                                    ContinuousProtections.IsUnaffectedBy(engine, t, except));
            }
        }

        // ───────────────────────────── Delta Attacker ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.NormalsSharingNameAttackDirectlyThisTurn"/> (Lua s.activate): every face-up non-Token
        /// Normal Monster you control with ConditionAmount-1 others of its name can attack directly this turn.
        /// </summary>
        public static void ResolveDeltaAttacker(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || c == null) return;
            var group = SameNameGroupMembers(who, Math.Max(1, c.ConditionAmount));
            var name = source?.Name ?? "Delta Attacker";
            if (group.Count == 0)
            {
                engine.Log($"{name}: no {c.ConditionAmount} face-up Normal Monsters of the same name — no effect.");
                return;
            }

            foreach (var m in group)
            {
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source)) continue;
                m.DirectAttackThisTurn = true;
                engine.Log($"{name}: {m.Name} can attack directly this turn.");
            }
        }

        // ───────────────────────────── Thousand Energy / Triangle Power ─────────────────────────────

        sealed class BaseBoost
        {
            public CardInstance Card;
            public int PriorAtk;
            public int PriorDef;
            public int Entry;
            public int FaceDownStamp;
        }

        sealed class EndPhaseDestroy
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public int Turn;
        }

        sealed class State
        {
            public readonly List<BaseBoost> Boosts = new();
            public readonly List<EndPhaseDestroy> Destroys = new();
            public bool Sweeping;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>
        /// <see cref="EffectActionKind.RaiseOriginalAtkDefOfLevelNormalsDestroyThemAtEndPhase"/> (Lua s.activate): each
        /// face-up non-Token Normal Monster of the Level you control gets EFFECT_SET_BASE_ATTACK / _DEFENSE = its original
        /// ATK / DEF + Amount (RESETS_STANDARD); then this turn's End Phase destruction is registered (always).
        /// </summary>
        public static void ResolveOriginalBoost(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || c == null) return;
            var st = StateOf(engine);
            var name = source?.Name ?? "Effect";
            foreach (var m in FaceUpMonsters(who).Where(m => !m.IsToken && IsNormalType(m) && LevelFits(c, m)).ToList())
            {
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source)) continue;
                st.Boosts.Add(new BaseBoost
                {
                    Card = m,
                    PriorAtk = m.OriginalAtkOverride,
                    PriorDef = m.OriginalDefOverride,
                    Entry = m.FieldEntryStamp,
                    FaceDownStamp = m.FaceDownStamp
                });
                // Both originals rise by Amount, so a swapped ATK/DEF (Shield & Sword) shows the same raise.
                var atk = m.OriginalAtkOverride != int.MinValue ? m.OriginalAtkOverride : m.Def.atk;
                var def = m.OriginalDefOverride != int.MinValue ? m.OriginalDefOverride : m.Def.def;
                m.OriginalAtkOverride = Math.Max(0, atk) + c.Amount;
                m.OriginalDefOverride = Math.Max(0, def) + c.Amount;
                engine.Log($"{name}: {m.Name}'s original ATK/DEF become {m.OriginalAtkOverride}/{m.OriginalDefOverride}.");
            }

            st.Destroys.Add(new EndPhaseDestroy { Who = who, Source = source, Clause = c, Turn = engine.TurnNumber });
            FieldSpellEffects.RefreshBoard(engine);
        }

        /// <summary>
        /// DuelEngine End Phase (with LastTurnRuntime.OnEndPhase, at its start and again before the turn ends): each
        /// registration of this turn fires once (count limit 1) — every face-up Normal Monster (Tokens included) of the
        /// Level its player controls is destroyed, as one event. Registrations of earlier turns are gone.
        /// </summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st)) return;
            if (st.Destroys.Count == 0) return;
            var due = st.Destroys.Where(d => d.Turn == engine.TurnNumber).ToList();
            st.Destroys.Clear();
            foreach (var d in due)
            {
                if (engine.GameOver) return;
                var doomed = FaceUpMonsters(d.Who).Where(m => IsNormalType(m) && LevelFits(d.Clause, m)).ToList();
                if (doomed.Count == 0) continue;
                engine.Log($"{d.Source?.Name ?? "Effect"}: End Phase — every face-up Level {d.Clause.MonsterLevelMin} " +
                           $"Normal Monster {d.Who.Name} controls is destroyed.");
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var m in doomed)
                        if (d.Who.TryFindMonster(m, out _))
                            TextEffectRuntime.DestroyCard(engine, m, d.Source, false, d.Who);
                });
            }

            SweepBoosts(engine, st);
            engine.NotifyPublic();
        }

        /// <summary>RESETS_STANDARD: a boosted monster that left the field or was turned face-down gets its originals back.</summary>
        static void SweepBoosts(DuelEngine engine, State st)
        {
            for (var i = st.Boosts.Count - 1; i >= 0; i--)
            {
                var b = st.Boosts[i];
                var c = b.Card;
                var holder = c != null ? engine.ControllerOf(c) : null;
                if (holder != null && holder.TryFindMonster(c, out _) && c.FaceUp && c.FieldEntryStamp == b.Entry &&
                    c.FaceDownStamp == b.FaceDownStamp)
                    continue;
                if (c != null)
                {
                    c.OriginalAtkOverride = b.PriorAtk;
                    c.OriginalDefOverride = b.PriorDef;
                }

                st.Boosts.RemoveAt(i);
            }
        }

        // ───────────────────────────── The Law of the Normal ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.BothDiscardHandsDestroyAllButLowLevelNormals"/> (Lua s.activate): both hands are
        /// discarded together (REASON_EFFECT + REASON_DISCARD), then every card on the field except face-up Normal
        /// Monsters of Level MonsterLevelMax or lower is destroyed as one event. This card itself is left to go to the GY
        /// as it finishes resolving.
        /// </summary>
        public static void ResolveLawOfTheNormal(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || c == null || engine.GameOver) return;
            var opp = engine.OpponentOf(who);
            var name = source?.Name ?? "Effect";
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                foreach (var side in new[] { who, opp })
                {
                    if (side?.Hand == null) continue;
                    foreach (var h in side.Hand.Where(x => x != null).ToList())
                    {
                        var gone = MonsterEffects.DiscardFromHandByInstance(side, h, DiscardCause.Effect, who);
                        if (gone == null) continue;
                        engine.Log($"{side.Name} discards {gone.Name} ({name}).");
                        DiscardTriggersRuntime.NoteDiscard(engine, side, gone, who);
                    }
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            if (engine.GameOver) return;
            var doomed = CardsOnField(who).Concat(CardsOnField(opp))
                .Where(x => !ReferenceEquals(x, source) && !KeptByLaw(c, engine, x)).ToList();
            if (doomed.Count > 0)
            {
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var x in doomed)
                        if (engine.ControllerOf(x) != null || engine.Player.FieldSpellZone?.Occupant == x ||
                            engine.Opponent.FieldSpellZone?.Occupant == x)
                            TextEffectRuntime.DestroyCard(engine, x, source, false, who);
                });
                engine.Log($"{name}: every card on the field except face-up Level {c.MonsterLevelMax} or lower Normal " +
                           "Monsters is destroyed.");
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── Order to Smash ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.TributeTargetedNormalThenDestroyUpToOppSpellTraps"/> (Lua s.activate): the target,
        /// still face-up on your field, is Tributed by the effect (REASON_EFFECT); only if it was, its controller chooses
        /// 1 to PickUpTo Spells/Traps the opponent controls (SelectMatchingCard, not a target; a human picks one after
        /// the other, the second one optional) and destroys them together.
        /// </summary>
        public static void ResolveOrderToSmash(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance target)
        {
            if (engine == null || who == null || c == null) return;
            var name = source?.Name ?? "Effect";
            if (target == null || !who.TryFindMonster(target, out _) || !target.FaceUp)
            {
                engine.Log($"{name}: the target is no longer a face-up monster you control — nothing is Tributed.");
                return;
            }

            if (ContinuousProtections.NeitherPlayerCanTribute(engine) ||
                ContinuousProtections.IsUnaffectedBy(engine, target, source) ||
                TargetingTruthRuntime.ProtectedAtResolution(engine, c, target, source))
            {
                engine.Log($"{name}: {target.Name} cannot be Tributed — nothing is destroyed.");
                return;
            }

            ReleaseEvents.Record(engine, target, who); // EngineHygiene: EVENT_RELEASE
            engine.SendCardToGrave(who, target, sentBy: source);
            if (who.TryFindMonster(target, out _))
            {
                engine.Log($"{name}: {target.Name} was not Tributed — nothing is destroyed.");
                return;
            }

            engine.Log($"{name}: Tributed {target.Name}.");
            var opp = engine.OpponentOf(who);
            var cap = Math.Max(1, c.PickUpTo);
            var chosen = new List<CardInstance>();
            List<CardInstance> Left() => OppSpellTraps(opp).Where(x => !chosen.Contains(x)).ToList();
            if (Left().Count == 0)
            {
                engine.Log($"{name}: your opponent controls no Spell/Trap Card — nothing is destroyed.");
                return;
            }

            void Finish()
            {
                var still = chosen.Where(x => OppSpellTraps(opp).Contains(x)).ToList();
                if (still.Count > 0)
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        foreach (var x in still)
                            TextEffectRuntime.DestroyCard(engine, x, source, false, who);
                    });
                engine.Log($"{name}: destroys {still.Count} Spell/Trap Card(s) your opponent controlled.");
                engine.NotifyPublic();
            }

            // The AI destroys as many as it may, face-up Continuous cards first, then face-down Sets.
            CardInstance AiPick(List<CardInstance> list) =>
                list?.OrderByDescending(x => x.FaceUp ? 1 : 0).ThenBy(x => x.Name).FirstOrDefault();

            void PickNext()
            {
                if (chosen.Count >= cap || Left().Count == 0)
                {
                    Finish();
                    return;
                }

                var optional = chosen.Count > 0;
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                {
                    Candidates = Left,
                    Kind = EffectTargetKind.SpellTrapOnField,
                    Prompt = optional
                        ? $"{name}: destroy 1 more Spell/Trap your opponent controls (Cancel: only {chosen.Count})."
                        : $"{name}: destroy 1 Spell/Trap your opponent controls (up to {cap}).",
                    AiChoose = AiPick,
                    AskWithOneCandidate = optional,
                    Cancel = optional ? Finish : null,
                    Finish = pick =>
                    {
                        if (pick == null)
                        {
                            Finish();
                            return;
                        }

                        chosen.Add(pick);
                        PickNext();
                    }
                });
            }

            PickNext();
        }

        // ───────────────────────────── Sword of the Soul-Eater ─────────────────────────────

        sealed class SwordPaid
        {
            public ChainLink Link;
            public int Count;
            public int Turn;
        }

        sealed class SwordBonus
        {
            public int Atk;
            public int Entry;
        }

        static readonly ConditionalWeakTable<CardInstance, SwordPaid> s_paid = new();
        static readonly ConditionalWeakTable<CardInstance, SwordBonus> s_bonus = new();

        static EffectClause TributingEquipClause(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsEquipSpell) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.Activate).FirstOrDefault(c =>
                c != null && c.Action == EffectActionKind.EquipThisToTarget && c.EquipAtkPerTributedOnActivation > 0);
        }

        /// <summary>
        /// CompiledActivationChainRuntime.AfterStep, once the activation's target is on its Chain Link and before the
        /// opponent may respond (Lua s.target after the equip target, label 1 = activated): Sword of the Soul-Eater
        /// Tributes every other non-Token Normal Monster its controller controls (s.rfilter: face-down ones too; nothing
        /// while Tributes are forbidden) as a cost, at the same time, and keeps the number for its resolution.
        /// </summary>
        public static void OnActivationSeated(DuelEngine engine, ChainLink link)
        {
            var card = link?.Card;
            var who = link?.Controller;
            if (engine == null || who == null || TributingEquipClause(card) == null) return;
            var host = link.Target;
            var tributes = ContinuousProtections.NeitherPlayerCanTribute(engine)
                ? new List<CardInstance>()
                : who.MonstersOnField().Where(m => m?.Def != null && !ReferenceEquals(m, host) && !m.IsToken &&
                                                   IsNormalType(m)).ToList();
            var n = 0;
            if (tributes.Count > 0)
            {
                BattleWindowsRuntime.BeginCost(engine);
                try
                {
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        using var together = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                        foreach (var m in tributes)
                        {
                            if (!who.TryFindMonster(m, out _)) continue;
                            ReleaseEvents.Record(engine, m, who); // EngineHygiene: EVENT_RELEASE
                            engine.SendCardToGrave(who, m);
                            if (!who.TryFindMonster(m, out _)) n++;
                            engine.Log($"Cost: Tribute {m.Name}.");
                        }
                    });
                }
                finally
                {
                    BattleWindowsRuntime.EndCost(engine);
                }
            }

            s_paid.Remove(card);
            s_paid.Add(card, new SwordPaid { Link = link, Count = n, Turn = engine.TurnNumber });
            engine.Log($"{card.Name}: {n} Normal Monster(s) Tributed as it is activated.");
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause (EquipThisToTarget), after the card was equipped by its own resolving activation:
        /// the Tribute count paid for that activation becomes its Equip ATK (Lua s.operation: the host still face-up and
        /// controlled by this card's controller; 0 when nothing was Tributed or the card was not activated).
        /// </summary>
        public static void NoteEquipResolved(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance host)
        {
            if (engine == null || source == null || c == null || c.EquipAtkPerTributedOnActivation <= 0) return;
            var paid = s_paid.TryGetValue(source, out var p) ? p : null;
            s_paid.Remove(source);
            s_bonus.Remove(source);
            // A Spell Speed 1 activation resolves in the turn it was made: an older record belongs to a negated one.
            if (paid == null || paid.Turn != engine.TurnNumber || host == null || source.EquippedTo != host || !host.FaceUp ||
                !ReferenceEquals(engine.ControllerOf(host), who))
                return;
            var atk = paid.Count * c.EquipAtkPerTributedOnActivation;
            s_bonus.Add(source, new SwordBonus { Atk = atk, Entry = source.FieldEntryStamp });
            engine.Log($"{source.Name}: {host.Name} gains {atk} ATK ({paid.Count} Tributed).");
        }

        /// <summary>
        /// FieldSpellEffects.ApplyEquipStats (after the negation checks): the ATK this Equip Card gives the monster it is
        /// equipped to (EFFECT_TYPE_EQUIP, RESETS_STANDARD on the Equip Card).
        /// </summary>
        public static int EquipAtkOf(CardInstance equip, CardInstance host) =>
            equip != null && host != null && ReferenceEquals(equip.EquippedTo, host) &&
            s_bonus.TryGetValue(equip, out var b) && b.Entry == equip.FieldEntryStamp
                ? b.Atk
                : 0;

        // ───────────────────────────── board refresh ─────────────────────────────

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: originals put back (RESETS_STANDARD), Sword records of cards no longer on the
        /// field dropped, and the equip limit checked continuously — a face-up Equip Card whose monster no longer fits its
        /// Level / Normal Monster limit is sent to the GY by the rules (ygopro EFFECT_EQUIP_LIMIT, not negated with the
        /// card's effects).
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var st = StateOf(engine);
            if (st.Sweeping) return;
            SweepBoosts(engine, st);
            if (engine.GameOver || engine.PendingActivation != null) return;
            List<(DuelistState Holder, CardInstance Equip, CardInstance Host)> doomed = null;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var eq in side.SpellTrapsOnField())
            {
                if (eq?.Def == null || !eq.FaceUp || eq.EquippedTo == null || !eq.Def.IsEquipSpell) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                var c = prog.ClausesFor(EffectTiming.Activate).FirstOrDefault(HasEquipLimit);
                if (c == null || EquipLimitAllows(c, eq.EquippedTo)) continue;
                (doomed ??= new List<(DuelistState, CardInstance, CardInstance)>()).Add((side, eq, eq.EquippedTo));
            }

            if (doomed == null) return;
            st.Sweeping = true;
            try
            {
                foreach (var (holder, eq, host) in doomed)
                {
                    if (engine.GameOver) return;
                    if (!holder.TryFindSpellTrap(eq, out _) || eq.EquippedTo != host) continue;
                    engine.Log($"{eq.Name}: {host.Name} no longer fits its equip limit — this card is sent to the GY.");
                    engine.SendCardToGrave(holder, eq);
                }
            }
            finally
            {
                st.Sweeping = false;
            }
        }
    }
}
