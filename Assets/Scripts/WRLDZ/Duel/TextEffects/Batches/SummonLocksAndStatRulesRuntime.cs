using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonLocksAndStatRules". Atoms: <see cref="SummonLocksAndStatRulesAtoms"/>.
    /// <list type="bullet">
    /// <item>Gyaku-Gire Panda / Amazoness Tiger reuse shared runtimes (GainSelfAtkPerCount with
    /// <see cref="SelfStatMonstersRuntime.OppMonsterCount"/>, ContinuousPiercing, the "only 1 face-up X" rule of
    /// <see cref="SummonTriggersAndTributesRuntime"/>, and ContinuousProtections.CannotBeAttackTarget with the archetype
    /// filter <see cref="IsSeriesMember"/>).</item>
    /// <item>Amazoness Spellcaster: <see cref="ApplySwitchStep"/> (the two targets' original ATK, until the End Phase:
    /// <see cref="EndOfTurn"/>; RESETS_STANDARD: <see cref="Refresh"/>).</item>
    /// <item>Rivalry of Warlords: <see cref="Refresh"/> runs the EVENT_ADJUST sweep; <see cref="BlocksFaceUpSummon"/> is
    /// the EFFECT_CANNOT_SUMMON / CANNOT_FLIP_SUMMON / FORCE_SPSUMMON_POSITION(face-down) lock.</item>
    /// </list>
    /// </summary>
    public static class SummonLocksAndStatRulesRuntime
    {
        /// <summary>An archetype member: the name (or rules name, or printed archetype) contains <paramref name="series"/>.</summary>
        public static bool IsSeriesMember(CardInstance c, string series)
        {
            if (c?.Def == null || string.IsNullOrEmpty(series)) return false;
            bool Has(string s) => s != null && s.IndexOf(series, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(c.Name) || Has(c.RulesName) || Has(c.Def.archetype);
        }

        // ───────────────────────────── Amazoness Spellcaster ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CollectTargets, zone <see cref="EffectZoneFilter.ControllerFaceUpSeriesMonsters"/>: the face-up
        /// monsters <paramref name="who"/> controls whose name contains <see cref="EffectClause.NamedCard"/>.
        /// </summary>
        internal static void CollectFaceUpSeries(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null) return;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null || !m.FaceUp || !IsSeriesMember(m, c.NamedCard)) continue;
                if (!TargetingTruthRuntime.PicksWithoutTargeting(c) && engine.IsDragonTargetProtected(m)) continue;
                list.Add(m);
            }
        }

        /// <summary>
        /// End of TextEffectRuntime.CollectTargets: the switch's two targets (either zone) are face-up monsters that can be
        /// targeted by <paramref name="source"/> (Lua SelectTarget: IsCanBeEffectTarget).
        /// </summary>
        internal static void FilterTargets(DuelEngine engine, EffectClause c, CardInstance source, List<CardInstance> list)
        {
            if (engine == null || c == null || list == null ||
                c.Action != EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn)
                return;
            list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp ||
                                ContinuousProtections.CannotBeTargetedByEffects(engine, t));
        }

        /// <summary>AI picks: its own lowest original ATK archetype monster, the opponent's highest original ATK.</summary>
        internal static bool TryAutoPick(EffectClause c, List<CardInstance> targets, out CardInstance pick)
        {
            pick = null;
            if (c == null || targets == null || targets.Count == 0 ||
                c.Action != EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn)
                return false;
            pick = c.Zone == EffectZoneFilter.ControllerFaceUpSeriesMonsters
                ? targets.OrderBy(BaseAtk).First()
                : targets.OrderByDescending(BaseAtk).First();
            return true;
        }

        sealed class FirstPick
        {
            public CardInstance Target;
            public int SourceEntry;
            public int Turn;
        }

        /// <summary>The first target of the switch being resolved, per source card (its own field presence).</summary>
        static readonly ConditionalWeakTable<CardInstance, FirstPick> s_firstPick = new();

        sealed class Switched
        {
            public CardInstance Card;
            public bool DefField;
            public int Prior;
            public int Entry;
            public int FaceDownStamp;
        }

        sealed class EngineState
        {
            public readonly List<Switched> Switched = new();
            public readonly Dictionary<DuelistState, string> KeptType = new();
            public readonly HashSet<(CardInstance, int)> SeenRivalry = new();
            public bool Sweeping;
            public bool Choosing;
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();
        static EngineState State(DuelEngine engine) => s_state.GetValue(engine, _ => new EngineState());

        /// <summary>The ATK the monster's "original ATK" reads now (ygopro GetBaseAttack), never below 0.</summary>
        static int BaseAtk(CardInstance c)
        {
            if (c?.Def == null) return 0;
            var atk = c.OriginalAtkOverride != int.MinValue ? c.OriginalAtkOverride : c.Def.atk;
            var def = c.OriginalDefOverride != int.MinValue ? c.OriginalDefOverride : c.Def.def;
            return Math.Max(0, c.SwapOriginalAtkDefThisTurn ? def : atk);
        }

        static bool FaceUpMonsterOnField(DuelEngine engine, CardInstance c) =>
            c != null && c.FaceUp &&
            (engine.Player.TryFindMonster(c, out _) || engine.Opponent.TryFindMonster(c, out _));

        /// <summary>
        /// TextEffectRuntime.ApplyClause, <see cref="EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn"/>. The
        /// first target's step (Zone <see cref="EffectZoneFilter.ControllerFaceUpSeriesMonsters"/>) only remembers it; the
        /// second target's step (TextEffectRuntime.ApplySecondTarget, Zone = SecondZone) switches both. A step whose target
        /// is gone does not run (CompiledActivationChainRuntime), so a missing first target leaves nothing to switch with
        /// and a missing second target never uses the remembered one. Lua s.activate: both face-up, then each gets the
        /// other's GetBaseAttack() as EFFECT_SET_BASE_ATTACK until the End Phase.
        /// </summary>
        internal static void ApplySwitchStep(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            if (engine == null || source == null || clause == null || target == null) return;
            if (clause.Zone == EffectZoneFilter.ControllerFaceUpSeriesMonsters)
            {
                s_firstPick.Remove(source);
                s_firstPick.Add(source, new FirstPick
                {
                    Target = target, SourceEntry = source.FieldEntryStamp, Turn = engine.TurnNumber
                });
                return;
            }

            if (!s_firstPick.TryGetValue(source, out var first)) return;
            s_firstPick.Remove(source);
            if (first.SourceEntry != source.FieldEntryStamp || first.Turn != engine.TurnNumber) return;
            var a = first.Target;
            var b = target;
            if (ReferenceEquals(a, b) || !FaceUpMonsterOnField(engine, a) || !FaceUpMonsterOnField(engine, b))
            {
                engine.Log($"{source.Name}: a target is no longer face-up — nothing is switched.");
                return;
            }

            var atkA = BaseAtk(a);
            var atkB = BaseAtk(b);
            // RegisterEffect on a card unaffected by the Spell fails for that card only.
            if (!ContinuousProtections.IsUnaffectedBy(engine, a, source)) SetBaseAtk(engine, a, atkB);
            if (!ContinuousProtections.IsUnaffectedBy(engine, b, source)) SetBaseAtk(engine, b, atkA);
            engine.Log($"{source.Name}: {a.Name} and {b.Name} switch original ATK ({atkA} ↔ {atkB}) until the end of this turn.");
            FieldSpellEffects.RefreshBoard(engine);
        }

        static void SetBaseAtk(DuelEngine engine, CardInstance c, int value)
        {
            var st = State(engine);
            // The original ATK is shown from the DEF override while its ATK/DEF are switched this turn.
            var defField = c.SwapOriginalAtkDefThisTurn;
            if (!st.Switched.Exists(s => ReferenceEquals(s.Card, c) && s.DefField == defField))
                st.Switched.Add(new Switched
                {
                    Card = c,
                    DefField = defField,
                    Prior = defField ? c.OriginalDefOverride : c.OriginalAtkOverride,
                    Entry = c.FieldEntryStamp,
                    FaceDownStamp = c.FaceDownStamp
                });
            if (defField) c.OriginalDefOverride = value;
            else c.OriginalAtkOverride = value;
        }

        static void Revert(Switched s)
        {
            if (s.DefField) s.Card.OriginalDefOverride = s.Prior;
            else s.Card.OriginalAtkOverride = s.Prior;
        }

        /// <summary>DuelEngine.ClearUntilEndOfTurnStatMods (End Phase): every switched original ATK returns.</summary>
        public static void EndOfTurn(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Switched.Count == 0) return;
            foreach (var s in st.Switched) Revert(s);
            st.Switched.Clear();
        }

        /// <summary>RESETS_STANDARD: a switched monster that left the field or was turned face-down gets its own back.</summary>
        static void SweepSwitched(DuelEngine engine, EngineState st)
        {
            for (var i = st.Switched.Count - 1; i >= 0; i--)
            {
                var s = st.Switched[i];
                if (FaceUpMonsterOnField(engine, s.Card) && s.Card.FieldEntryStamp == s.Entry &&
                    s.Card.FaceDownStamp == s.FaceDownStamp)
                    continue;
                Revert(s);
                st.Switched.RemoveAt(i);
            }
        }

        // ───────────────────────────── Rivalry of Warlords ─────────────────────────────

        /// <summary>Face-up, not negated, resolved Rivalry of Warlords (FullyCompiled) on either field.</summary>
        static List<(CardInstance card, DuelistState controller)> RivalrySources(DuelEngine engine)
        {
            var list = new List<(CardInstance, DuelistState)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated || ActivationPending(engine, st)) continue;
                    var prog = CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    if (prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                        .Exists(c => c != null && c.Action == EffectActionKind.EachPlayerKeepsOneMonsterType))
                        list.Add((st, side));
                }
            }

            return list;
        }

        static bool ActivationPending(DuelEngine engine, CardInstance card)
        {
            var links = engine.Chain?.Links;
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && l.Card == card && !l.Resolved)
                    return true;
            return false;
        }

        static List<CardInstance> FaceUpMonsters(DuelEngine engine, DuelistState who, CardInstance except = null)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            foreach (var m in who.MonstersOnField())
                if (m?.Def != null && m.FaceUp && !ReferenceEquals(m, except) && !engine.IsSummonPending(m))
                    list.Add(m);
            return list;
        }

        static string TypeOf(CardInstance c) => c?.Race ?? "";

        static HashSet<string> TypesOf(IEnumerable<CardInstance> monsters) =>
            new HashSet<string>(monsters.Select(TypeOf), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lua s.sumlimit (EFFECT_CANNOT_SUMMON / EFFECT_CANNOT_FLIP_SUMMON / EFFECT_FORCE_SPSUMMON_POSITION POS_FACEDOWN):
        /// while a Rivalry of Warlords applies and <paramref name="who"/> controls face-up monsters, a monster whose Type is
        /// not the Type of all of them cannot be put face-up under <paramref name="who"/>'s control by a Normal, Flip or
        /// Special Summon. A Set (or a face-down Special Summon) is not affected. Called where the "only 1 face-up X"
        /// rule is (SummonProcedures, DuelEngine.CanFlipSummon / SpecialSummonToField, EngineHygiene).
        /// </summary>
        public static bool BlocksFaceUpSummon(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = null;
            if (engine?.Player == null || engine.Opponent == null || who == null || card?.Def == null) return false;
            if (!card.Def.IsMonster && !card.IsToken) return false;
            var sources = RivalrySources(engine);
            if (sources.Count == 0) return false;
            var types = TypesOf(FaceUpMonsters(engine, who, card).Where(m => !ReferenceEquals(m, card)));
            if (types.Count == 0) return false;
            var mine = card.Def.race ?? "";
            if (types.Count == 1 && types.Contains(mine)) return false;
            reason = $"{sources[0].card.Name}: each player can only control 1 Type of monster.";
            return true;
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: switched original ATK resets, then the Rivalry of Warlords EVENT_ADJUST (Lua
        /// s.adjustop). Not during the Damage Step before damage is calculated, not while a choice or another effect is
        /// being applied. Each player with face-up monsters of 2+ Types keeps the Type they kept before if it is still
        /// there, otherwise chooses one (a human picks a monster of that Type: AskCardPick; the AI keeps its strongest
        /// Type); the Rivalry's controller chooses first; then every other face-up monster of both players is sent to the
        /// GY at the same time (REASON_RULE).
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var st = State(engine);
            if (st.Switched.Count > 0) SweepSwitched(engine, st);
            if (engine.GameOver || st.Sweeping) return;
            // A human's pick is open (or queued behind another prompt). A pick dropped unanswered leaves no prompt.
            if (st.Choosing)
            {
                if (engine.PendingActivation != null) return;
                st.Choosing = false;
            }
            var sources = RivalrySources(engine);
            if (sources.Count == 0) return;
            // s.acttg: a new activation forgets both players' kept Types.
            foreach (var (card, _) in sources)
                if (st.SeenRivalry.Add((card, card.FieldEntryStamp)))
                    st.KeptType.Clear();
            if (engine.PendingActivation != null || RedirectAndReplaceRuntime.ApplyingEffect) return;
            if (engine.BattleStep == BattleStep.DamageStep &&
                (engine.DamageSubStep == DamageSubStep.Start ||
                 engine.DamageSubStep == DamageSubStep.BeforeDamageCalculation ||
                 engine.DamageSubStep == DamageSubStep.DamageCalculation))
                return;

            var (source, tp) = sources[0];
            var order = new[] { tp, engine.OpponentOf(tp) };
            var needsChoice = new List<DuelistState>();
            var anyToSend = false;
            foreach (var p in order)
            {
                var types = TypesOf(FaceUpMonsters(engine, p));
                if (types.Count == 0)
                {
                    st.KeptType.Remove(p);
                    continue;
                }

                if (types.Count == 1)
                {
                    st.KeptType[p] = types.First();
                    continue;
                }

                anyToSend = true;
                if (!st.KeptType.TryGetValue(p, out var kept) || kept == null || !types.Contains(kept))
                    needsChoice.Add(p);
            }

            if (!anyToSend) return;
            st.Choosing = true;
            ChooseThenSend(engine, st, source, needsChoice, 0);
        }

        static void ChooseThenSend(DuelEngine engine, EngineState st, CardInstance source, List<DuelistState> needs, int i)
        {
            if (i >= needs.Count)
            {
                st.Choosing = false;
                SendOthers(engine, st, source);
                return;
            }

            var p = needs[i];
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, p, source, new CardPickRequest
            {
                Candidates = () => FaceUpMonsters(engine, p),
                Kind = EffectTargetKind.YourMonster,
                Prompt = $"{source.Name}: choose a monster — you keep the face-up monsters of its Type, the others are sent to the GY.",
                AiChoose = AiKeep,
                Finish = chosen =>
                {
                    var keep = chosen ?? AiKeep(FaceUpMonsters(engine, p));
                    if (keep != null)
                    {
                        st.KeptType[p] = TypeOf(keep);
                        engine.Log($"{source.Name}: {p.Name} keeps {TypeOf(keep)} monsters.");
                    }

                    ChooseThenSend(engine, st, source, needs, i + 1);
                }
            });
        }

        /// <summary>The AI keeps the Type whose face-up monsters have the most total ATK (then the most monsters).</summary>
        static CardInstance AiKeep(List<CardInstance> list)
        {
            if (list == null || list.Count == 0) return null;
            var best = list.GroupBy(TypeOf, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(m => m.CurrentAtk))
                .ThenByDescending(g => g.Count())
                .First();
            return best.First();
        }

        static void SendOthers(DuelEngine engine, EngineState st, CardInstance source)
        {
            if (engine.GameOver) return;
            var sends = new List<(DuelistState side, CardInstance card)>();
            foreach (var p in new[] { engine.Player, engine.Opponent })
            {
                var faceUp = FaceUpMonsters(engine, p);
                if (faceUp.Count == 0) continue;
                if (!st.KeptType.TryGetValue(p, out var kept) || kept == null) continue;
                foreach (var m in faceUp)
                    if (!string.Equals(TypeOf(m), kept, StringComparison.OrdinalIgnoreCase))
                        sends.Add((p, m));
            }

            if (sends.Count == 0) return;
            st.Sweeping = true;
            try
            {
                using (RedirectAndReplaceRuntime.EnterEffectApplication(engine))
                {
                    foreach (var (side, m) in sends)
                    {
                        if (engine.GameOver || !side.TryFindMonster(m, out _)) continue;
                        engine.Log($"{source.Name}: {side.Name} can only control 1 Type of monster — {m.Name} is sent to the GY.");
                        engine.SendCardToGrave(side, m);
                    }
                }
            }
            finally
            {
                st.Sweeping = false;
            }

            FieldSpellEffects.RefreshBoard(engine);
            engine.NotifyPublic();
        }
    }
}
