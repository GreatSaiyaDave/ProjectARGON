using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// AttackShieldsAndDamageZero batch. Shared pieces, read live (no card-id branches):
    /// <list type="bullet">
    /// <item><b>Conditional continuous effects</b> (<see cref="EffectClause.WhileCondition"/>, <see cref="WhileConditionMet"/>):
    /// a self "cannot be attacked" (ContinuousProtections.CannotBeAttackTarget — Lua EFFECT_CANNOT_BE_BATTLE_TARGET with a
    /// condition, aux.imval1/imval2) while its controller controls another face-up monster of a Type (Solar Flare Dragon,
    /// Goblin King, Soul-Absorbing Bone Tower), a face-up monster of a Type (Arcane Archer of the Forest) or a face-up Level
    /// N or lower Normal Monster (Emissary of the Oasis); a self ATK gain while its controller has no cards in hand nor in
    /// the Spell &amp; Trap Zones (Theban Nightmare; FieldSpellEffects.ApplyStatClause, the Field Zone does not count).</item>
    /// <item><b>Goblin King's ATK/DEF</b>: GainSelfAtkPerCount + SelfStatIsSet with the count source
    /// <see cref="FaceUpRaceOnFieldExceptThis"/> (FieldSpellEffects.ApplySelfScale).</item>
    /// <item><b>Battle damage 0</b> (<see cref="EffectActionKind.AvoidBattleDamageFromBattlesInvolving"/>,
    /// <see cref="AvoidsBattleDamage"/>, read by FieldSpellEffects.ControllerAvoidsBattleDamage for each player of a damage
    /// calculation): Emissary of the Oasis (its controller, battles involving a Level 3 or lower Normal Monster on either
    /// side: Lua AVOID_BATTLE_DAMAGE on its own, NO_BATTLE_DAMAGE on the opponent's), The Sanctuary in the Sky (the
    /// controller of each Fairy battler).</item>
    /// <item><b>Soul-Absorbing Bone Tower's mill</b> (<see cref="EffectActionKind.MillOpponentWhenRaceSpecialSummoned"/>):
    /// DuelEngine.CompleteSpecialSummon calls <see cref="OnSpecialSummoned"/>; a group Special Summon
    /// (NegationsAndGySummonsRuntime.BeginGroupSpecialSummon) is one event; an event during a Chain's resolution is held and
    /// resolved once the Chain is over (<see cref="FlushHeld"/>, from DuelEngine.ResolveChainStack). The mandatory mill
    /// resolves inline (no Chain Link), like Card of Safe Return's draw.</item>
    /// <item><b>Tribute cost by Type only</b> (<see cref="EffectClause.TributeRaceStrict"/>,
    /// <see cref="FilterTributeCostCandidates"/>): Arcane Archer of the Forest.</item>
    /// </list>
    /// </summary>
    public static class AttackShieldsAndDamageZeroRuntime
    {
        public const string YouControlAnotherFaceUpRace = "YouControlAnotherFaceUpRace";
        public const string YouControlFaceUpRace = "YouControlFaceUpRace";
        public const string YouControlFaceUpLowLevelNormal = "YouControlFaceUpLowLevelNormal";
        public const string NoCardsInHandOrSpellTrapZones = "NoCardsInHandOrSpellTrapZones";

        /// <summary>GainSelfAtkPerCount <see cref="EffectClause.CountSource"/>: face-up <see cref="EffectClause.RaceFilter"/>
        /// monsters on the whole field, this card excluded (Goblin King).</summary>
        public const string FaceUpRaceOnFieldExceptThis = "FaceUpRaceOnFieldExceptThis";

        // ───────────────────── Conditions ─────────────────────

        /// <summary>
        /// True when <paramref name="c"/> has no <see cref="EffectClause.WhileCondition"/>, or when it holds for
        /// <paramref name="source"/>'s controller now. An unknown condition is never met (fail closed).
        /// </summary>
        public static bool WhileConditionMet(DuelEngine engine, CardInstance source, EffectClause c)
        {
            if (c == null || string.IsNullOrEmpty(c.WhileCondition)) return true;
            var who = engine?.ControllerOf(source);
            if (who == null) return false;
            switch (c.WhileCondition)
            {
                case YouControlAnotherFaceUpRace:
                    return who.MonstersOnField().Any(m => m != null && !ReferenceEquals(m, source) && OnFieldFaceUp(engine, m) &&
                                                          ClassicEraTemplates.RaceMatches(m.Race, c.ConditionName));
                case YouControlFaceUpRace:
                    return who.MonstersOnField().Any(m => m != null && OnFieldFaceUp(engine, m) &&
                                                          ClassicEraTemplates.RaceMatches(m.Race, c.ConditionName));
                case YouControlFaceUpLowLevelNormal:
                    return who.MonstersOnField().Any(m => m != null && OnFieldFaceUp(engine, m) &&
                                                          IsLowLevelNormal(m, c.ConditionAmount));
                case NoCardsInHandOrSpellTrapZones:
                    // Lua: hand count + LOCATION_SZONE cards with GetSequence() < 5 == 0 (the Field Zone is sequence 5;
                    // Pendulum Zones are the end Spell & Trap Zones).
                    if (who.Hand != null && who.Hand.Count > 0) return false;
                    if (who.SpellTrapZones != null && who.SpellTrapZones.Any(z => z?.Occupant != null)) return false;
                    if (who.PendulumZones != null && who.PendulumZones.Any(z => z?.Occupant != null)) return false;
                    return true;
                // TlmBattleShieldsAndBurnZero batch: Grave Ohja (Lua IsPosition(POS_FACEDOWN_DEFENSE) in its Monster Zone).
                case TlmBattleShieldsAndBurnZeroRuntime.YouControlFaceDownDefense:
                    return TlmBattleShieldsAndBurnZeroRuntime.ControlsFaceDownDefense(who);
                default:
                    return false;
            }
        }

        static bool OnFieldFaceUp(DuelEngine engine, CardInstance m) =>
            m.FaceUp && !engine.IsSummonPending(m);

        /// <summary>
        /// A Normal Monster (Lua IsType(TYPE_NORMAL): an effectless Token too — ygopro Tokens carry TYPE_NORMAL) whose
        /// current Level is <paramref name="maxLevel"/> or lower.
        /// </summary>
        static bool IsLowLevelNormal(CardInstance m, int maxLevel)
        {
            if (m?.Def == null || maxLevel <= 0) return false;
            if (!m.Def.IsNormalMonster && !m.IsToken) return false;
            return m.Level > 0 && m.Level <= maxLevel;
        }

        /// <summary>Goblin King's count: face-up monsters of <paramref name="race"/> on both fields, except <paramref name="self"/>.</summary>
        public static int CountFaceUpRaceExcept(DuelEngine engine, CardInstance self, string race)
        {
            if (engine == null || string.IsNullOrEmpty(race)) return 0;
            var n = 0;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (m == null || ReferenceEquals(m, self) || !OnFieldFaceUp(engine, m)) continue;
                    if (ClassicEraTemplates.RaceMatches(m.Race, race)) n++;
                }
            }

            return n;
        }

        /// <summary>TextEffectRuntime.CollectTributeCost: a strict Type filter drops a candidate matched only by its name.</summary>
        public static void FilterTributeCostCandidates(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || !c.TributeRaceStrict || string.IsNullOrEmpty(c.TributeRaceFilter)) return;
            list.RemoveAll(m => m == null || !ClassicEraTemplates.RaceMatches(m.Race, c.TributeRaceFilter));
        }

        // ───────────────────── Battle damage ─────────────────────

        /// <summary>
        /// FieldSpellEffects.ControllerAvoidsBattleDamage: <paramref name="who"/> takes no battle damage from the battle
        /// between <paramref name="attacker"/> and <paramref name="defender"/> because a face-up, un-negated
        /// <see cref="EffectActionKind.AvoidBattleDamageFromBattlesInvolving"/> source applies to it.
        /// </summary>
        public static bool AvoidsBattleDamage(DuelEngine engine, DuelistState who, CardInstance attacker,
            CardInstance defender)
        {
            if (engine == null || who == null || (attacker == null && defender == null)) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var src in side.MonstersOnField().Concat(side.SpellTrapsOnField()))
                {
                    if (src?.Def == null || !src.FaceUp || src.IsNegated || engine.IsSummonPending(src)) continue;
                    var prog = CompiledEffectCache.GetOrCompile(src.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null || c.Action != EffectActionKind.AvoidBattleDamageFromBattlesInvolving) continue;
                        if (c.Side == EffectSide.Controller)
                        {
                            // Emissary: only its controller, whichever side the matching monster is on.
                            if (!ReferenceEquals(side, who)) continue;
                            if (Matches(attacker, c) || Matches(defender, c)) return true;
                            continue;
                        }

                        // Sanctuary: the controller of each matching battler.
                        if (Matches(attacker, c) && ReferenceEquals(engine.ControllerOf(attacker), who)) return true;
                        if (Matches(defender, c) && ReferenceEquals(engine.ControllerOf(defender), who)) return true;
                    }
                }
            }

            return false;
        }

        static bool Matches(CardInstance m, EffectClause c)
        {
            if (m?.Def == null || !m.Def.IsMonster && !m.IsToken) return false;
            if (!string.IsNullOrEmpty(c.RaceFilter) && !ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter)) return false;
            if (c.NormalMonsterMaxLevel > 0 && !IsLowLevelNormal(m, c.NormalMonsterMaxLevel)) return false;
            return !string.IsNullOrEmpty(c.RaceFilter) || c.NormalMonsterMaxLevel > 0;
        }

        // ───────────────────── Special Summon trigger ─────────────────────

        sealed class Held
        {
            public CardInstance Source;
            public DuelistState Controller;
            public EffectClause Clause;
        }

        sealed class State
        {
            public int GroupDepth;
            public readonly List<(CardInstance card, bool faceUp)> Group = new();
            public readonly List<Held> Held = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>NegationsAndGySummonsRuntime.BeginGroupSpecialSummon: the monsters Special Summoned until
        /// <see cref="EndGroup"/> form one event.</summary>
        public static void BeginGroup(DuelEngine engine)
        {
            if (engine != null) StateOf(engine).GroupDepth++;
        }

        /// <summary>NegationsAndGySummonsRuntime.EndGroupSpecialSummon: the outermost group's one event.</summary>
        public static void EndGroup(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.GroupDepth <= 0) return;
            if (--st.GroupDepth > 0) return;
            var ev = st.Group.ToList();
            st.Group.Clear();
            if (ev.Count > 0) OnEvent(engine, ev);
        }

        /// <summary>DuelEngine.CompleteSpecialSummon: <paramref name="card"/> was Special Summoned (face-up or not).</summary>
        public static void OnSpecialSummoned(DuelEngine engine, CardInstance card, bool faceUp)
        {
            if (engine == null || card == null || engine.GameOver) return;
            var st = StateOf(engine);
            if (st.GroupDepth > 0)
            {
                st.Group.Add((card, faceUp));
                return;
            }

            OnEvent(engine, new List<(CardInstance, bool)> { (card, faceUp) });
        }

        static void OnEvent(DuelEngine engine, List<(CardInstance card, bool faceUp)> ev)
        {
            var st = StateOf(engine);
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var src in side.MonstersOnField().ToList())
                {
                    if (src?.Def == null || !src.FaceUp || src.IsNegated || engine.IsSummonPending(src)) continue;
                    // Lua: not eg:IsContains(e:GetHandler()).
                    if (ev.Any(x => ReferenceEquals(x.card, src))) continue;
                    var prog = CompiledEffectCache.GetOrCompile(src.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null || c.Action != EffectActionKind.MillOpponentWhenRaceSpecialSummoned) continue;
                        // Lua: eg:IsExists(aux.FaceupFilter(Card.IsRace, RACE_x)).
                        if (!ev.Any(x => x.faceUp && x.card.FaceUp &&
                                         ClassicEraTemplates.RaceMatches(x.card.Race, c.RaceFilter)))
                            continue;
                        st.Held.Add(new Held { Source = src, Controller = side, Clause = c });
                    }
                }
            }

            FlushHeld(engine);
        }

        /// <summary>
        /// Resolves the held mills once no Chain is resolving (DuelEngine.ResolveChainStack calls this after the Chain).
        /// A source that left the field, was turned face-down, changed control or was negated meanwhile does nothing.
        /// </summary>
        public static void FlushHeld(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Held.Count == 0) return;
            if (engine.IsResolvingChain) return;
            var batch = st.Held.ToList();
            st.Held.Clear();
            foreach (var h in batch)
            {
                if (engine.GameOver) return;
                var src = h.Source;
                if (src == null || !h.Controller.TryFindMonster(src, out _) || !src.FaceUp || src.IsNegated) continue;
                var opp = engine.OpponentOf(h.Controller);
                if (opp == null) continue;
                var n = h.Clause.Amount > 0 ? h.Clause.Amount : 2;
                engine.Log($"{src.Name}: a {h.Clause.RaceFilter} monster was Special Summoned — send the top {n} cards of " +
                           $"{opp.Name}'s Deck to the GY.");
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    TextEffectRuntime.SendTopOfDeckToGy(engine, opp, n, byEffectOf: h.Controller);
                }
                finally
                {
                    DiscardTriggersRuntime.EndScope(engine);
                }
            }

            engine.NotifyPublic();
        }
    }
}
