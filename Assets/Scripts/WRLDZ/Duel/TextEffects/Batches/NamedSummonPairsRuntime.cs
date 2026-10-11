using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "NamedSummonPairs" runtime: Spells that Special Summon a named Nomi monster ("Must be Special Summoned
    /// with "X" and cannot be Special Summoned by other ways", ygopro EFFECT_SPSUMMON_CONDITION aux.FALSE, summoned by X
    /// with Duel.SpecialSummon(..., nocheck=true)) and those monsters' riders. Reads compiled programs only — no card-id
    /// branches.
    /// <list type="bullet">
    /// <item>The Nomi bypass (<see cref="NamedSummonerIsSummoning"/>): DuelEngine.SpecialSummonToField lets a
    /// "cannot be Special Summoned by other ways" monster through only while the card its text names is doing a named
    /// Special Summon (TributeSummonsRuntime.CurrentNamedSummonSource) — A Deal with Dark Ruler → Berserk Dragon,
    /// Contract with Exodia → Exodia Necross, Dark Flare Knight → Mirage Knight. Every other Special Summon stays
    /// refused, properly summoned before or not.</item>
    /// <item>A Deal with Dark Ruler (c6850209 checkop): a per-player turn record set when a Level 8 or higher monster
    /// leaves a Monster Zone for the GY, keyed by its previous controller (<see cref="NoteMonsterSentToGy"/>, from the
    /// three DuelEngine field → GY sites); activation condition
    /// <see cref="YourLevelOrHigherMonsterSentToGyThisTurn"/>.</item>
    /// <item>Contract with Exodia (c33244944 condition): every name of <see cref="EffectClause.ConditionName"/>
    /// ('|'-separated) is in your GY (<see cref="AllNamedInYourGy"/>).</item>
    /// <item>Exodia Necross: <see cref="PreventsSpellTrapEffectDestruction"/> (DuelEngine.DestroyMonster) and
    /// <see cref="SweepSelfDestroy"/> (FieldSpellEffects.RefreshBoard).</item>
    /// <item>Mirage Knight: <see cref="ApplyDamageCalculationAtk"/> (DuelEngine damage calculation, after the "before
    /// damage calculation" effects), <see cref="NoteBattled"/> (EVENT_BATTLED) and the End Phase banish
    /// (<see cref="FiresAtEachEndPhase"/>, <see cref="ApplyBanishIfBattled"/>).</item>
    /// </list>
    /// </summary>
    public static class NamedSummonPairsRuntime
    {
        /// <summary>ActivationCondition (A Deal with Dark Ruler): a monster of Level <see cref="EffectClause.ConditionAmount"/>
        /// or higher that you controlled was sent from the Monster Zone to the GY this turn.</summary>
        public const string YourLevelOrHigherMonsterSentToGyThisTurn = "YourLevelOrHigherMonsterSentToGyThisTurn";

        /// <summary>ActivationCondition (Contract with Exodia): every '|'-separated name of
        /// <see cref="EffectClause.ConditionName"/> is in your GY.</summary>
        public const string AllNamedInYourGy = "AllNamedInYourGy";

        sealed class BattledMark
        {
            public CardInstance Card;
            public int Turn;
            public int Stamp;
        }

        sealed class State
        {
            /// <summary>Per previous controller: the turn and the highest Level sent from its Monster Zone to a GY.</summary>
            public readonly Dictionary<DuelistState, (int Turn, int Level)> HighLevelSent = new();
            public readonly List<BattledMark> Battled = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── Nomi named summoner ─────────────────────────────

        static readonly Regex RxNomiNamedSummoner = new(
            @"\bMust be Special Summoned (?:with|by) ""([^""]+)"",? and cannot be Special Summoned by other ways\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The card whose effect is this Nomi monster's only Special Summon, or null.</summary>
        public static string NomiSummonerName(string officialText)
        {
            if (string.IsNullOrEmpty(officialText)) return null;
            var m = RxNomiNamedSummoner.Match(officialText);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// DuelEngine.SpecialSummonToField: <paramref name="card"/> is a "Must be Special Summoned with "X" and cannot be
        /// Special Summoned by other ways" monster, and X's own named Special Summon is in progress (ygopro
        /// Duel.SpecialSummon(..., nocheck=true) from X's operation). Rule locks (Jowgen, Chain Energy, Rivalry …) are
        /// still asked by the caller.
        /// </summary>
        public static bool NamedSummonerIsSummoning(CardInstance card)
        {
            var named = NomiSummonerName(card?.Def == null ? null : OfficialCardAuthority.OfficialText(card));
            if (named == null) return false;
            var source = TributeSummonsRuntime.CurrentNamedSummonSource;
            if (source?.Def == null) return false;
            return string.Equals(source.Def.name, named, StringComparison.OrdinalIgnoreCase) || source.IsNamed(named);
        }

        // ───────────────────────────── A Deal with Dark Ruler ─────────────────────────────

        static bool HasLevel(CardDef def)
        {
            if (def == null || !def.IsMonster || def.level <= 0) return false;
            var type = def.type ?? "";
            return type.IndexOf("XYZ", StringComparison.OrdinalIgnoreCase) < 0 &&
                   type.IndexOf("Link", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// DuelEngine (SendCardToGrave, DestroyMonster, a negated Summon's destruction): <paramref name="card"/> left
        /// <paramref name="previousController"/>'s Monster Zone and is now in a GY (ygopro EVENT_TO_GRAVE with
        /// IsPreviousLocation(LOCATION_MZONE), keyed by GetPreviousControler()).
        /// </summary>
        public static void NoteMonsterSentToGy(DuelEngine engine, DuelistState previousController, CardInstance card)
        {
            if (engine == null || previousController == null || card?.Def == null || card.IsToken) return;
            if (!HasLevel(card.Def)) return;
            var inGy = (engine.Player?.Graveyard?.Contains(card) ?? false) ||
                       (engine.Opponent?.Graveyard?.Contains(card) ?? false);
            if (!inGy) return;
            var st = StateOf(engine);
            var level = card.Def.level;
            if (st.HighLevelSent.TryGetValue(previousController, out var rec) && rec.Turn == engine.TurnNumber &&
                rec.Level >= level)
                return;
            st.HighLevelSent[previousController] = (engine.TurnNumber, level);
        }

        static bool SentLevelOrHigherThisTurn(DuelEngine engine, DuelistState who, int level) =>
            engine != null && who != null && s_state.TryGetValue(engine, out var st) &&
            st.HighLevelSent.TryGetValue(who, out var rec) && rec.Turn == engine.TurnNumber && rec.Level >= level;

        static string[] Names(string joined) =>
            (joined ?? "").Split('|').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();

        static bool AllInGy(DuelistState who, string joined)
        {
            var names = Names(joined);
            if (names.Length == 0 || who?.Graveyard == null) return false;
            return names.All(n => who.Graveyard.Exists(g => g?.Def != null &&
                                                            (g.IsNamed(n) || string.Equals(g.Def.name, n,
                                                                StringComparison.OrdinalIgnoreCase))));
        }

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause c, out bool ok,
            out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case YourLevelOrHigherMonsterSentToGyThisTurn:
                {
                    var level = c.ConditionAmount > 0 ? c.ConditionAmount : 8;
                    ok = SentLevelOrHigherThisTurn(engine, who, level);
                    reason = $"Only if a Level {level} or higher monster you controlled was sent to the GY this turn.";
                    return true;
                }
                case AllNamedInYourGy:
                    ok = AllInGy(who, c.ConditionName);
                    reason = "Needs " + string.Join(", ", Names(c.ConditionName).Select(n => $"\"{n}\"")) +
                             " in your GY.";
                    return true;
                default:
                    return false;
            }
        }

        // ───────────────────────────── Exodia Necross ─────────────────────────────

        static EffectClause ActiveClause(DuelEngine engine, CardInstance m, EffectActionKind action)
        {
            if (m?.Def == null || !m.FaceUp || m.IsNegated) return null;
            if (engine != null && engine.IsSummonPending(m)) return null;
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        /// <summary>
        /// DuelEngine.DestroyMonster (not by battle): <paramref name="card"/> is a face-up, un-negated monster with
        /// <see cref="EffectActionKind.CannotBeDestroyedBySpellTrapEffects"/> and <paramref name="byEffect"/> is a Spell or
        /// Trap Card (ygopro re:IsSpellTrapEffect()).
        /// </summary>
        public static bool PreventsSpellTrapEffectDestruction(DuelEngine engine, CardInstance card, CardInstance byEffect)
        {
            if (engine == null || card == null || byEffect?.Def == null) return false;
            if (!byEffect.Def.IsSpell && !byEffect.Def.IsTrap) return false;
            var controller = engine.ControllerOf(card);
            if (controller == null || !controller.TryFindMonster(card, out _)) return false;
            if (ActiveClause(engine, card, EffectActionKind.CannotBeDestroyedBySpellTrapEffects) == null) return false;
            engine.Log($"{card.Name} cannot be destroyed by Spell/Trap effects ({byEffect.Name}).");
            return true;
        }

        [ThreadStatic] static bool s_sweeping;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: a face-up, un-negated monster with
        /// <see cref="EffectActionKind.SelfDestroyUnlessAllNamedInYourGy"/> is destroyed by its own effect while any of the
        /// named cards is missing from its controller's GY (ygopro EFFECT_SELF_DESTROY). Not during a pick, nor in the
        /// Damage Step before damage calculation is over (the Thunder Nyan Nyan sweep's guards).
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
            var doomed = new List<(DuelistState Side, CardInstance Card)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var m in side.MonstersOnField())
            {
                var c = ActiveClause(engine, m, EffectActionKind.SelfDestroyUnlessAllNamedInYourGy);
                if (c == null || Names(c.NamedCard).Length == 0) continue;
                if (!AllInGy(side, c.NamedCard)) doomed.Add((side, m));
            }

            if (doomed.Count == 0) return;
            s_sweeping = true;
            try
            {
                foreach (var (side, card) in doomed)
                {
                    if (engine.GameOver) return;
                    if (!side.TryFindMonster(card, out _) || !card.FaceUp) continue;
                    engine.Log($"{card.Name}: a named card is missing from {side.Name}'s GY — it is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, card, card, byPlayer: side);
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }

        // ───────────────────────────── Mirage Knight ─────────────────────────────

        /// <summary>The original ATK (ygopro GetBaseAttack) of a monster on the field.</summary>
        static int OriginalAtk(CardInstance m)
        {
            if (m?.Def == null) return 0;
            int printedAtk = m.OriginalAtkOverride != int.MinValue ? m.OriginalAtkOverride : m.Def.atk;
            int printedDef = m.OriginalDefOverride != int.MinValue ? m.OriginalDefOverride : m.Def.def;
            return Math.Max(0, m.SwapOriginalAtkDefThisTurn ? printedDef : printedAtk);
        }

        /// <summary>
        /// DuelEngine damage calculation (after the "before damage calculation" effects, before BattleMechanics.Calculate):
        /// each battler with <see cref="EffectActionKind.GainBattleTargetOriginalAtkDuringDamageCalculation"/> (face-up,
        /// un-negated) gains the original ATK of the opponent's monster it is battling for this calculation only
        /// (<see cref="CardInstance.DamageStepAtkBonus"/>, cleared after the calculation).
        /// </summary>
        public static void ApplyDamageCalculationAtk(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return;
            ApplyFor(engine, attacker, target);
            ApplyFor(engine, target, attacker);
        }

        static void ApplyFor(DuelEngine engine, CardInstance self, CardInstance other)
        {
            var selfSide = engine.ControllerOf(self);
            var otherSide = engine.ControllerOf(other);
            if (selfSide == null || otherSide == null || ReferenceEquals(selfSide, otherSide)) return;
            if (!selfSide.TryFindMonster(self, out _)) return;
            if (ActiveClause(engine, self, EffectActionKind.GainBattleTargetOriginalAtkDuringDamageCalculation) == null)
                return;
            var gain = OriginalAtk(other);
            if (gain <= 0) return;
            self.DamageStepAtkBonus += gain;
            engine.Log($"{self.Name} gains {gain} ATK ({other.Name}'s original ATK) during this damage calculation " +
                       $"({self.CurrentAtk}).");
        }

        /// <summary>DuelEngine, after damage calculation (ygopro EVENT_BATTLED): both battlers battled this turn.</summary>
        public static void NoteBattled(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return;
            var st = StateOf(engine);
            st.Battled.RemoveAll(b => b.Turn != engine.TurnNumber || b.Card == null);
            foreach (var m in new[] { attacker, target })
            {
                if (st.Battled.Exists(b => ReferenceEquals(b.Card, m) && b.Stamp == m.FieldEntryStamp)) continue;
                st.Battled.Add(new BattledMark { Card = m, Turn = engine.TurnNumber, Stamp = m.FieldEntryStamp });
            }
        }

        /// <summary>Mirage Knight's rmcon: <paramref name="card"/> (this field entry) battled this turn.</summary>
        public static bool BattledThisTurn(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_state.TryGetValue(engine, out var st) &&
            st.Battled.Exists(b => ReferenceEquals(b.Card, card) && b.Turn == engine.TurnNumber &&
                                   b.Stamp == card.FieldEntryStamp);

        /// <summary>TextEffectRuntime.FirePhaseTriggersFor: this clause fires at either player's End Phase.</summary>
        public static bool FiresAtEachEndPhase(EffectClause c, EffectTiming timing) =>
            c != null && timing == EffectTiming.EndPhase &&
            c.Action == EffectActionKind.BanishThisAtEndPhaseIfBattledThisTurn;

        /// <summary>ApplyClause arm (Mirage Knight): banish this face-up card if it battled this turn.</summary>
        public static void ApplyBanishIfBattled(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source == null) return;
            var controller = engine.ControllerOf(source);
            if (controller == null || !source.FaceUp || !controller.TryFindMonster(source, out _)) return;
            if (!BattledThisTurn(engine, source)) return;
            engine.Log($"{source.Name} battled this turn — it is banished.");
            engine.BanishCard(controller, source, destroyed: false, by: controller);
        }
    }
}
