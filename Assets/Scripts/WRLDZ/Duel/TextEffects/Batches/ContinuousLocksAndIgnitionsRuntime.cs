using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ContinuousLocksAndIgnitions batch (v163 PGD / MFC) runtime (atoms: <see cref="ContinuousLocksAndIgnitionsAtoms"/>):
    /// <list type="bullet">
    /// <item><b>Gora Turtle</b>: a face-up monster's <see cref="EffectActionKind.ContinuousCannotAttack"/> with an ATK
    /// threshold (<see cref="MonsterAttackLockBlocks"/>, read by DuelEngine.ContinuousCannotAttackBlocks).</item>
    /// <item><b>Arsenal Bug</b>: "ATK and DEF become N" while its controller has no other face-up monster of the Type
    /// (<see cref="ApplySelfStatSet"/>, from FieldSpellEffects.ApplyCompiledContinuous on every refresh).</item>
    /// <item><b>Timidity</b>: face-down cards in either Spell &amp; Trap Zone cannot be destroyed by card effects until the
    /// 2nd End Phase after it resolved (<see cref="SetCardProtected"/>, read by
    /// EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction).</item>
    /// <item><b>Non Aggression Area</b>: the opponent cannot Normal Summon, Set or Special Summon during their next turn
    /// (<see cref="SummonOrSetLocked"/>, read by SummonProcedures.CheckNormalOrTribute and RuleLocksRuntime).</item>
    /// <item><b>Call of the Mummy / Frontline Base</b>: once-per-turn face-up ignition that Special Summons 1 monster from
    /// the hand (PerCountBurnsRuntime's face-up ignition hooks: <see cref="CanUseHandIgnition"/>,
    /// <see cref="ActivateHandIgnition"/>, <see cref="TryResolveHandIgnitionLink"/>, <see cref="AiWantsHandIgnition"/>;
    /// the human's pick goes through ContinuousTrapsAndSpiritSupportRuntime.AskCardPick).</item>
    /// <item><b>Buster Rancher</b>: the ATK-limit equip target (<see cref="FilterTargets"/>, EquipFamilyRuntime.IsCorrectEquipTarget,
    /// <see cref="SweepEquipLimits"/>) and the damage-calculation ATK (<see cref="ApplyDamageCalculationAtk"/>).</item>
    /// </list>
    /// State is kept per engine.
    /// </summary>
    public static class ContinuousLocksAndIgnitionsRuntime
    {
        /// <summary><see cref="EffectClause.TargetCardKind"/> of a Union monster filter (Frontline Base).</summary>
        public const string UnionKind = "Union";

        sealed class SetProtection
        {
            public DuelistState Activator;
            public CardInstance Source;
            /// <summary>The last turn it applies (the turn after the activation: its End Phase is the 2nd one).</summary>
            public int LastTurn;
        }

        sealed class SummonLock
        {
            public DuelistState Locked;
            public CardInstance Source;
            /// <summary>The only turn it applies: the turn after the activation.</summary>
            public int Turn;
        }

        sealed class EngineState
        {
            public readonly List<SetProtection> Protections = new();
            public readonly List<SummonLock> SummonLocks = new();
        }

        sealed class IgnitionUse
        {
            public int Turn;
            public int EntryStamp;
        }

        sealed class IgnitionHold
        {
            public DuelistState Who;
            public CardInstance Card;
            public EffectClause Clause;
            public int EntryStamp;
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();
        static readonly ConditionalWeakTable<CardInstance, IgnitionUse> s_ignitionUsed = new();
        static readonly ConditionalWeakTable<ChainLink, IgnitionHold> s_ignitionLinks = new();

        static EngineState State(DuelEngine engine) => s_state.GetValue(engine, _ => new EngineState());

        static CompiledCardProgram ProgOf(CardInstance card) =>
            card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;

        // ───────────────────────────── Gora Turtle ─────────────────────────────

        /// <summary>
        /// DuelEngine.ContinuousCannotAttackBlocks: a face-up monster whose complete program has an ATK-threshold
        /// <see cref="EffectActionKind.ContinuousCannotAttack"/> (no Type, Level or "Summoned this turn" condition) forbids
        /// <paramref name="attacker"/> with that much ATK or more to declare an attack (ygopro EFFECT_CANNOT_ATTACK_ANNOUNCE,
        /// range MZONE; the monster itself included). Not while its effects are negated.
        /// </summary>
        public static bool MonsterAttackLockBlocks(DuelEngine engine, DuelistState attackerController, CardInstance attacker)
        {
            if (engine == null || attacker?.Def == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var mon in side.MonstersOnField())
                {
                    if (mon?.Def == null || !mon.FaceUp || mon.IsNegated || engine.IsSummonPending(mon)) continue;
                    var prog = ProgOf(mon);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null || c.Action != EffectActionKind.ContinuousCannotAttack ||
                            c.RequiresSummonedOrFlippedThisTurn || c.AmountIsLevel || c.RaceFilterIsDeclared ||
                            !string.IsNullOrEmpty(c.RaceFilter) || c.Amount <= 0)
                            continue;
                        if (c.Side == EffectSide.Opponent && side == attackerController) continue;
                        if (c.Side == EffectSide.Controller && side != attackerController) continue;
                        if (attacker.CurrentAtk >= c.Amount) return true;
                    }
                }
            }

            return false;
        }

        // ───────────────────────────── Arsenal Bug ─────────────────────────────

        /// <summary>
        /// FieldSpellEffects.ApplyCompiledContinuous (inside RefreshBoard, after the modifiers were cleared): while
        /// <paramref name="self"/>'s controller controls no other face-up monster of the clause's Type, its ATK and DEF
        /// become the clause's values (ygopro EFFECT_SET_ATTACK / _DEFENSE of TYPE_SINGLE: the base every other ATK/DEF
        /// change adds to). True when the clause was this action.
        /// </summary>
        public static bool ApplySelfStatSet(DuelEngine engine, CardInstance self, EffectClause c)
        {
            if (c == null || c.Action != EffectActionKind.ThisCardAtkDefBecomeUnlessControllerHasAnotherFaceUpRace)
                return false;
            if (engine == null || self?.Def == null || !self.FaceUp) return true;
            var who = engine.ControllerOf(self);
            if (who == null || !who.TryFindMonster(self, out _)) return true;
            var another = who.MonstersOnField().Any(m =>
                m != null && !ReferenceEquals(m, self) && m.FaceUp && !engine.IsSummonPending(m) &&
                ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter));
            if (another) return true;
            self.AtkModifier += c.Amount - ShownBase(self, atk: true);
            self.DefModifier += c.DefAmount - ShownBase(self, atk: false);
            return true;
        }

        /// <summary>The ATK (or DEF) <see cref="CardInstance.CurrentAtk"/> starts from (original, swapped by Shield &amp; Sword).</summary>
        static int ShownBase(CardInstance c, bool atk)
        {
            var printedAtk = c.OriginalAtkOverride != int.MinValue ? c.OriginalAtkOverride : c.Def?.atk ?? 0;
            var printedDef = c.OriginalDefOverride != int.MinValue ? c.OriginalDefOverride : c.Def?.def ?? 0;
            if (c.SwapOriginalAtkDefThisTurn) (printedAtk, printedDef) = (printedDef, printedAtk);
            return Math.Max(0, atk ? printedAtk : printedDef);
        }

        // ───────────────────────────── Timidity ─────────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.SetSpellTrapsCannotBeDestroyedUntilOpponentsNextEndPhase"/>: from now
        /// until the end of the next turn's End Phase (RESET_PHASE_END, 2), no face-down card in either Spell &amp; Trap
        /// Zone can be destroyed by a card effect, including cards Set later (EFFECT_FLAG_SET_AVAILABLE).
        /// </summary>
        public static void ApplySetProtection(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var st = State(engine);
            st.Protections.RemoveAll(p => p.LastTurn < engine.TurnNumber);
            st.Protections.Add(new SetProtection { Activator = who, Source = source, LastTurn = engine.TurnNumber + 1 });
            engine.Log($"{source?.Name ?? "Timidity"}: Set Spell and Trap Cards cannot be destroyed by card effects until " +
                       $"{engine.OpponentOf(who)?.Name ?? "the opponent"}'s next End Phase.");
        }

        /// <summary>
        /// EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction: <paramref name="card"/> is face-down in a
        /// Spell &amp; Trap Zone (or Field Zone) of either player while a Timidity applies, so a card effect cannot destroy it.
        /// </summary>
        public static bool SetCardProtected(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || card.FaceUp || !s_state.TryGetValue(engine, out var st)) return false;
            st.Protections.RemoveAll(p => p.LastTurn < engine.TurnNumber || !IsThisDuel(engine, p.Activator));
            if (st.Protections.Count == 0) return false;
            var inZone = false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindSpellTrap(card, out _) || ReferenceEquals(side.FieldSpellZone?.Occupant, card))
                    inZone = true;
            }

            if (!inZone) return false;
            var by = st.Protections[st.Protections.Count - 1].Source;
            engine.Log($"{card.Name} is Set and cannot be destroyed by card effects ({by?.Name ?? "Timidity"}).");
            return true;
        }

        static bool IsThisDuel(DuelEngine engine, DuelistState who) =>
            who != null && (ReferenceEquals(who, engine.Player) || ReferenceEquals(who, engine.Opponent));

        // ───────────────────────────── Non Aggression Area ─────────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.OpponentCannotSummonOrSetDuringNextTurn"/>: during the next turn (the
        /// opponent's), the opponent cannot Normal Summon (Tribute Summons included), Set a monster or Special Summon.
        /// </summary>
        public static void ApplySummonLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var st = State(engine);
            st.SummonLocks.RemoveAll(l => l.Turn < engine.TurnNumber);
            st.SummonLocks.Add(new SummonLock { Locked = opp, Source = source, Turn = engine.TurnNumber + 1 });
            engine.Log($"{source?.Name ?? "Non Aggression Area"}: {opp.Name} cannot Normal Summon, Set or Special Summon " +
                       "during their next turn.");
        }

        /// <summary>
        /// <paramref name="who"/> is under a Non Aggression Area lock this turn: no Normal Summon, monster Set or Special
        /// Summon (SummonProcedures.CheckNormalOrTribute for both, RuleLocksRuntime for the Special Summons).
        /// </summary>
        public static bool SummonOrSetLocked(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = null;
            if (engine == null || who == null || !s_state.TryGetValue(engine, out var st)) return false;
            foreach (var l in st.SummonLocks)
            {
                if (l == null || !ReferenceEquals(l.Locked, who) || l.Turn != engine.TurnNumber) continue;
                reason = $"You cannot Normal Summon, Set or Special Summon this turn ({l.Source?.Name ?? "Non Aggression Area"}).";
                return true;
            }

            return false;
        }

        // ───────────────────────────── Call of the Mummy / Frontline Base ─────────────────────────────

        static EffectClause HandIgnitionClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.FaceUpFieldIgnition)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.SpecialSummonFromHandByFaceUpIgnition);

        /// <summary>The card's face-up ignition Special Summons a monster from the hand (Call of the Mummy, Frontline Base).</summary>
        public static bool IsHandIgnition(CompiledCardProgram prog) => HandIgnitionClause(prog) != null;

        /// <summary>A hand card the clause may Special Summon (Type / Union + Level; and it can be Special Summoned now).</summary>
        static bool HandCandidate(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c, CardInstance m)
        {
            if (m?.Def == null || !m.Def.IsMonster || who?.Hand == null || !who.Hand.Contains(m)) return false;
            if (!string.IsNullOrEmpty(c.RaceFilter) && !ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter)) return false;
            if (string.Equals(c.TargetCardKind, UnionKind, StringComparison.OrdinalIgnoreCase) &&
                (m.Def.type == null || m.Def.type.IndexOf(UnionKind, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (c.AmountIsLevel && c.Amount > 0 && m.Level > c.Amount) return false;
            return SpecialSummonLegality.CanBeSpecialSummoned(engine, who, m, source, SummonOrigin.Hand);
        }

        static List<CardInstance> HandCandidates(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(m => HandCandidate(engine, who, source, c, m)).ToList();

        /// <summary>
        /// PerCountBurnsRuntime.CanActivateFaceUpFieldIgnition, after its open-state / own Main Phase / negation checks:
        /// once per turn (SetCountLimit(1), per card), you control no monsters (Call of the Mummy), a free Monster Zone and
        /// a monster in your hand the effect can Special Summon (Lua s.target chk).
        /// </summary>
        public static bool CanUseHandIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            var c = HandIgnitionClause(prog);
            if (engine == null || who == null || card == null || c == null)
            {
                reason = "Not a Special Summon ignition.";
                return false;
            }

            if (s_ignitionUsed.TryGetValue(card, out var used) && used.Turn == engine.TurnNumber &&
                used.EntryStamp == card.FieldEntryStamp)
            {
                reason = "This card's effect was already used this turn.";
                return false;
            }

            if (c.RequiresControllerControlsNoMonsters && who.MonsterCount > 0)
            {
                reason = "You must control no monsters.";
                return false;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                reason = "No free Monster Zone.";
                return false;
            }

            if (HandCandidates(engine, who, card, c).Count == 0)
            {
                reason = "No monster in your hand this effect can Special Summon.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// PerCountBurnsRuntime.TryFaceUpFieldIgnition, on its seated Chain Link: count the use; the monster is chosen when
        /// the link resolves (Lua s.operation).
        /// </summary>
        public static void ActivateHandIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, ChainLink link)
        {
            var c = HandIgnitionClause(prog);
            if (engine == null || who == null || card == null || c == null || link == null) return;
            s_ignitionUsed.Remove(card);
            s_ignitionUsed.Add(card, new IgnitionUse { Turn = engine.TurnNumber, EntryStamp = card.FieldEntryStamp });
            s_ignitionLinks.Remove(link);
            s_ignitionLinks.Add(link, new IgnitionHold { Who = who, Card = card, Clause = c, EntryStamp = card.FieldEntryStamp });
            engine.Log($"Activate: {card.Name} (effect) — Chain Link {link.LinkNumber}.");
        }

        /// <summary>
        /// PerCountBurnsRuntime.TryResolveFaceUpFieldIgnitionLink: false for any other link. The effect is negated while the
        /// card's effects are negated; Call of the Mummy also needs itself still face-up in its zone and its controller still
        /// controlling no monsters. Then 1 legal monster from the hand is Special Summoned (a human picks it and its face-up
        /// position through the target picker; the AI takes the highest ATK).
        /// </summary>
        public static bool TryResolveHandIgnitionLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || !s_ignitionLinks.TryGetValue(link, out var hold)) return false;
            s_ignitionLinks.Remove(link);
            var card = hold.Card;
            var who = hold.Who;
            var c = hold.Clause;
            var name = card?.Name ?? "effect";
            var onField = card != null && who != null && card.FaceUp && who.TryFindSpellTrap(card, out _) &&
                          card.FieldEntryStamp == hold.EntryStamp;
            var negatedBy = ContinuousNegationRuntime.NegatedBy(engine, card);
            if (negatedBy != null || (onField && card.IsNegated))
            {
                engine.Log($"{name}: its effect is negated{(negatedBy != null ? $" by {negatedBy.Name}" : "")}.");
                engine.NotifyPublic();
                return true;
            }

            if (c.RequiresThisRelatedAtResolution && !onField)
            {
                engine.Log($"{name} is no longer on the field — its effect does nothing.");
                engine.NotifyPublic();
                return true;
            }

            if (who == null || engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{name}: no free Monster Zone — nothing is Special Summoned.");
                engine.NotifyPublic();
                return true;
            }

            if (c.RequiresControllerControlsNoMonsters && who.MonsterCount > 0)
            {
                engine.Log($"{name}: {who.Name} controls a monster — its effect does nothing.");
                engine.NotifyPublic();
                return true;
            }

            var kind = !string.IsNullOrEmpty(c.RaceFilter)
                ? $"{c.RaceFilter} monster"
                : $"Level {c.Amount} or lower Union monster";
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card,
                () => HandCandidates(engine, who, card, c),
                EffectTargetKind.MonsterInYourHand,
                $"Special Summon 1 {kind} from your hand.",
                list => list.OrderByDescending(m => m.CurrentAtk).FirstOrDefault(),
                chosen => SummonFromHand(engine, who, card, c, chosen));
            engine.NotifyPublic();
            return true;
        }

        static void SummonFromHand(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance chosen)
        {
            var name = source?.Name ?? "effect";
            if (chosen == null)
            {
                engine.Log($"{name}: no monster in the hand to Special Summon.");
                return;
            }

            if (!HandCandidate(engine, who, source, c, chosen) || engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{name}: {chosen.Name} cannot be Special Summoned now.");
                return;
            }

            // Lua POS_FACEUP: a human's position comes from the picker (DuelEngine's pending placement); otherwise Attack,
            // or Defense when the monster's DEF is higher.
            var pos = chosen.CurrentDef > chosen.CurrentAtk ? BattlePosition.Defense : BattlePosition.Attack;
            who.Hand.Remove(chosen);
            engine.MarkSpecialSummonFromHand(chosen); // Chain Energy toll (RuleLocks batch)
            if (!engine.SpecialSummonToField(who, chosen, pos, true))
            {
                if (!who.Hand.Contains(chosen)) who.Hand.Add(chosen);
                engine.Log($"{name}: the Special Summon of {chosen.Name} failed.");
                return;
            }

            engine.Log($"{name}: {chosen.Name} is Special Summoned from the hand.");
        }

        /// <summary>
        /// PerCountBurnsRuntime.AiWantsFaceUpFieldIgnition: the AI Special Summons whenever the effect can be used (it only
        /// adds a monster to its field).
        /// </summary>
        public static bool AiWantsHandIgnition(DuelEngine engine, DuelistState ai, CardInstance card,
            CompiledCardProgram prog) =>
            IsHandIgnition(prog) && CanUseHandIgnition(engine, ai, card, prog, out _);

        // ───────────────────────────── Buster Rancher ─────────────────────────────

        /// <summary>The Damage Step's damage calculation is being performed (ygopro IsPhase(PHASE_DAMAGE_CAL)).</summary>
        public static bool InDamageCalculation(DuelEngine engine) =>
            engine != null && engine.BattleStep == BattleStep.DamageStep &&
            engine.DamageSubStep == DamageSubStep.DamageCalculation;

        /// <summary>
        /// TextEffectRuntime.CollectTargets: an Equip Spell with an ATK equip limit (Buster Rancher) only targets a face-up
        /// monster with that much ATK or less.
        /// </summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || c.Action != EffectActionKind.EquipThisToTarget || c.EquipLimitAtkMax <= 0) return;
            list.RemoveAll(t => t == null || !t.FaceUp || t.CurrentAtk > c.EquipLimitAtkMax);
        }

        /// <summary>
        /// EquipFamilyRuntime.IsCorrectEquipTarget / the EquipThisToTarget resolution: <paramref name="monster"/> breaks
        /// the ATK equip limit of <paramref name="clause"/> (outside damage calculation: Lua s.eqlimit).
        /// </summary>
        public static bool BreaksAtkEquipLimit(DuelEngine engine, EffectClause clause, CardInstance monster) =>
            clause != null && clause.EquipLimitAtkMax > 0 && monster != null && !InDamageCalculation(engine) &&
            monster.CurrentAtk > clause.EquipLimitAtkMax;

        static EffectClause EquipLimitClause(CardInstance equip)
        {
            var prog = ProgOf(equip);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.Activate).FirstOrDefault(c =>
                c != null && c.Action == EffectActionKind.EquipThisToTarget && c.EquipLimitAtkMax > 0);
        }

        [ThreadStatic] static bool s_sweeping;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: the equip limit is checked continuously except during damage calculation. A
        /// face-up Equip Card with an ATK limit whose monster now has more ATK is destroyed by the game rules (ygopro
        /// adjust "equip check", REASON_RULE; the limit is not negated with the card's effects).
        /// </summary>
        public static void SweepEquipLimits(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver || s_sweeping) return;
            if (InDamageCalculation(engine) || engine.PendingActivation != null) return;
            List<(DuelistState Holder, CardInstance Equip, CardInstance Host)> doomed = null;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var eq in side.SpellTrapsOnField())
            {
                if (eq?.Def == null || !eq.FaceUp || eq.EquippedTo == null) continue;
                var c = EquipLimitClause(eq);
                if (c == null || !BreaksAtkEquipLimit(engine, c, eq.EquippedTo)) continue;
                (doomed ??= new List<(DuelistState, CardInstance, CardInstance)>()).Add((side, eq, eq.EquippedTo));
            }

            if (doomed == null) return;
            s_sweeping = true;
            try
            {
                foreach (var (holder, eq, host) in doomed)
                {
                    if (engine.GameOver) return;
                    if (!holder.TryFindSpellTrap(eq, out _) || eq.EquippedTo != host) continue;
                    engine.Log($"{eq.Name}: {host.Name} has {host.CurrentAtk} ATK, more than {EquipLimitClause(eq)?.EquipLimitAtkMax} " +
                               "— it can no longer be equipped, and this card is destroyed.");
                    engine.SendCardToGrave(holder, eq);
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }

        /// <summary>
        /// DuelEngine damage calculation (after the other damage-calculation ATK changes): each battling monster equipped
        /// with a face-up, not negated Buster Rancher gains its ATK when the opponent's monster battling it is in Attack
        /// Position with the threshold ATK or more, or in Defense Position with the threshold DEF or more.
        /// </summary>
        public static void ApplyDamageCalculationAtk(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return;
            ApplyFor(engine, attacker, target);
            ApplyFor(engine, target, attacker);
        }

        static void ApplyFor(DuelEngine engine, CardInstance self, CardInstance other)
        {
            if (self?.Def == null || other?.Def == null || !self.FaceUp || self.Equips == null) return;
            var selfSide = engine.ControllerOf(self);
            var otherSide = engine.ControllerOf(other);
            if (selfSide == null || otherSide == null || ReferenceEquals(selfSide, otherSide)) return;
            foreach (var eq in self.Equips.ToList())
            {
                if (eq?.Def == null || !eq.FaceUp || !ReferenceEquals(eq.EquippedTo, self) || eq.IsNegated) continue;
                var holder = engine.ControllerOf(eq);
                if (holder == null || !holder.TryFindSpellTrap(eq, out _)) continue;
                if (ContinuousProtections.EquipCardEffectsAreNegated(engine, eq)) continue;
                var prog = ProgOf(eq);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.EquippedGainsAtkInDamageCalcVsStrongBattler) continue;
                    var strong = (other.Position == BattlePosition.Attack && other.CurrentAtk >= c.ConditionAmount) ||
                                 (other.Position == BattlePosition.Defense && other.CurrentDef >= c.ConditionAmount);
                    if (!strong) continue;
                    self.DamageStepAtkBonus += c.Amount;
                    engine.Log($"{eq.Name}: {self.Name} battles {other.Name} — +{c.Amount} ATK during this damage " +
                               $"calculation ({self.CurrentAtk}).");
                }
            }
        }
    }
}
