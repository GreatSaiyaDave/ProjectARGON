using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EndOfDamageStepRiders batch runtime (atoms: <see cref="EndOfDamageStepRidersAtoms"/>).
    /// <list type="bullet">
    /// <item><b>The battle</b> (<see cref="NoteBattle"/>, from DamageStepRulesRuntime.BeginBattle at damage calculation):
    /// both battlers, their sides, field-entry stamps, Types and battle positions. "Related to the battle" (ygopro
    /// IsRelateToBattle) = still on the field of the same side with the same stamp, during that Damage Step.</item>
    /// <item><b>Battle-destroyed triggers</b> (<see cref="DestroyedByThisBattle"/> / <see cref="ActivatesAfterBattleDestroyed"/>,
    /// read by TextEffectRuntime.FireEndOfDamageFor): ygopro aux.dsercon — a "battled" end-of-Damage-Step trigger activates
    /// when this card is related to the battle OR was destroyed by it (it activates from the GY).</item>
    /// <item>Getsu Fuhma / Ryu Kokki: <see cref="DestroyBattledMonster"/>; Prickle Fairy: <see cref="ChangeBattledMonsterToDefense"/>
    /// and <see cref="CannotBeAttackTarget"/>; Legendary Jujitsu Master: <see cref="PlaceAttackerOnTopOfDeck"/>.</item>
    /// <item>Insect Princess: <see cref="ApplyContinuous"/> (FieldSpellEffects.ApplyCompiledContinuous) and
    /// <see cref="SkipsBattleDestroying"/> (TextEffectRuntime.NotifyDestroyedOpponentByBattle).</item>
    /// <item>Enraged Battle Ox: <see cref="MonsterGrantsPiercing"/> (BattleBoostsRuntime.FieldGrantsPiercing).</item>
    /// <item>Drillago: <see cref="GrantsDirectAttack"/> (DuelEngine.GrantsDirectAttack).</item>
    /// </list>
    /// End-of-Damage-Step triggers resolve inline, as every other end-of-Damage-Step trigger (known residual).
    /// </summary>
    public static class EndOfDamageStepRidersRuntime
    {
        sealed class Battle
        {
            public CardInstance Attacker;
            public CardInstance Target;
            public DuelistState AttackerSide;
            public DuelistState TargetSide;
            public int AttackerEntry;
            public int TargetEntry;
            public string AttackerRace;
            public string TargetRace;
            public bool AttackerInDefense;
            public bool TargetInDefense;
        }

        static readonly ConditionalWeakTable<DuelEngine, Battle> s_battle = new();

        static IEnumerable<EffectClause> LiveContinuous(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    yield return c;
        }

        static bool OnField(DuelistState who, CardInstance card) =>
            who != null && card != null && who.TryFindMonster(card, out _);

        static bool InDamageStep(DuelEngine engine) =>
            engine != null && engine.BattleStep == BattleStep.DamageStep && engine.DamageSubStep != DamageSubStep.None;

        // ───────────────────────────── the battle ─────────────────────────────

        /// <summary>DamageStepRulesRuntime.BeginBattle (damage calculation): note the battlers.</summary>
        public static void NoteBattle(DuelEngine engine, DuelistState attackingPlayer, CardInstance attacker,
            CardInstance target)
        {
            if (engine == null) return;
            var b = s_battle.GetOrCreateValue(engine);
            b.Attacker = attacker;
            b.Target = target;
            b.AttackerSide = attackingPlayer;
            b.TargetSide = attackingPlayer != null ? engine.OpponentOf(attackingPlayer) : null;
            b.AttackerEntry = attacker?.FieldEntryStamp ?? 0;
            b.TargetEntry = target?.FieldEntryStamp ?? 0;
            b.AttackerRace = attacker?.Race;
            b.TargetRace = target?.Race;
            b.AttackerInDefense = attacker != null && attacker.Position == BattlePosition.Defense;
            b.TargetInDefense = target != null && target.Position == BattlePosition.Defense;
        }

        static Battle Current(DuelEngine engine) =>
            engine != null && s_battle.TryGetValue(engine, out var b) ? b : null;

        /// <summary>ygopro IsRelateToBattle: a battler of the noted battle, still on its side's field since then.</summary>
        static bool RelatesToBattle(DuelEngine engine, CardInstance card)
        {
            var b = Current(engine);
            if (b == null || card == null || !InDamageStep(engine)) return false;
            if (ReferenceEquals(card, b.Attacker))
                return OnField(b.AttackerSide, card) && card.FieldEntryStamp == b.AttackerEntry;
            if (ReferenceEquals(card, b.Target))
                return OnField(b.TargetSide, card) && card.FieldEntryStamp == b.TargetEntry;
            return false;
        }

        /// <summary>BattleRiders batch: ygopro IsRelateToBattle of <paramref name="card"/> (B.E.S. Big Core's removal trigger).</summary>
        public static bool IsRelatedToBattle(DuelEngine engine, CardInstance card) => RelatesToBattle(engine, card);

        /// <summary>
        /// BattleRiders batch: ygopro GetBattleTarget — the monster <paramref name="card"/> battles in the Damage Step under
        /// way (null for a direct attack, outside a Damage Step, or when <paramref name="card"/> is not a battler).
        /// </summary>
        public static CardInstance BattleTargetOf(DuelEngine engine, CardInstance card)
        {
            var b = Current(engine);
            if (b == null || card == null || !InDamageStep(engine)) return null;
            if (ReferenceEquals(card, b.Attacker)) return b.Target;
            if (ReferenceEquals(card, b.Target)) return b.Attacker;
            return null;
        }

        /// <summary>
        /// TextEffectRuntime.FireEndOfDamageFor: <paramref name="self"/> left the field because <paramref name="other"/>
        /// destroyed it by battle in this Damage Step (ygopro STATUS_BATTLE_DESTROYED).
        /// </summary>
        public static bool DestroyedByThisBattle(DuelEngine engine, CardInstance self, CardInstance other)
        {
            var b = Current(engine);
            if (b == null || self == null || other == null || !InDamageStep(engine)) return false;
            var pair = (ReferenceEquals(b.Attacker, self) && ReferenceEquals(b.Target, other)) ||
                       (ReferenceEquals(b.Target, self) && ReferenceEquals(b.Attacker, other));
            return pair && engine.ControllerOf(self) == null && self.WasDestroyedByBattle &&
                   ReferenceEquals(self.BattleDestroyer, other);
        }

        /// <summary>The end-of-Damage-Step clauses of this batch that use ygopro aux.dsercon (activate from the GY too).</summary>
        public static bool ActivatesAfterBattleDestroyed(EffectClause c) =>
            c != null && (c.Action == EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep ||
                          c.Action == EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep ||
                          c.Action == EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep);

        /// <summary>The player whose GY (or banished pile) holds <paramref name="card"/>; null when neither does.</summary>
        public static DuelistState HolderOffField(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            foreach (var p in new[] { engine.Player, engine.Opponent })
                if (p != null && (p.Graveyard.Contains(card) || p.Banished.Contains(card)))
                    return p;
            return null;
        }

        /// <summary>
        /// This card activates the trigger: related to the battle (on the field, its effects not negated) or destroyed by
        /// it (<see cref="DestroyedByThisBattle"/>).
        /// </summary>
        static bool SourceMayResolve(DuelEngine engine, CardInstance source, CardInstance other)
        {
            if (source == null || source.IsNegated) return false;
            if (engine.ControllerOf(source) != null) return source.FaceUp && RelatesToBattle(engine, source);
            return DestroyedByThisBattle(engine, source, other);
        }

        // ───────────────────────────── Getsu Fuhma / Ryu Kokki ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep"/>: the monster <paramref name="source"/>
        /// battled (<paramref name="other"/>), still related to the battle and of a printed Type
        /// (<see cref="EffectClause.BattledMonsterRaceFilter"/>, its live Type), is destroyed.
        /// </summary>
        public static void DestroyBattledMonster(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance other)
        {
            if (engine == null || who == null || source == null || clause == null || other == null) return;
            if (!SourceMayResolve(engine, source, other)) return;
            // Lua condition: the battle target is of the Types and still related to the battle.
            if (!RelatesToBattle(engine, other) ||
                !ClassicEraTemplates.RaceMatches(other.Race, clause.BattledMonsterRaceFilter))
                return;
            engine.Log($"{source.Name}: it battled {other.Name} ({other.Race}) — destroy that monster at the end of the " +
                       "Damage Step.");
            TextEffectRuntime.DestroyCard(engine, other, source, byPlayer: who);
        }

        // ───────────────────────────── Prickle Fairy ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep"/>: the monster
        /// <paramref name="source"/> battled, still related to the battle and in Attack Position → face-up Defense Position.
        /// </summary>
        public static void ChangeBattledMonsterToDefense(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance other)
        {
            if (engine == null || who == null || source == null || clause == null || other == null) return;
            if (!SourceMayResolve(engine, source, other)) return;
            if (!RelatesToBattle(engine, other))
            {
                engine.Log($"{source.Name}: the monster it battled is no longer on the field.");
                return;
            }

            if (!other.FaceUp || other.Position != BattlePosition.Attack) return;
            if (ContinuousProtections.IsUnaffectedBy(engine, other, source)) return;
            if (!engine.ApplyEffectPositionChange(other, BattlePosition.Defense, faceUp: true)) return;
            engine.Log($"{source.Name}: {other.Name} is changed to Defense Position at the end of the Damage Step.");
            TextEffectRuntime.NotifyPositionChanged(engine, other, toDefense: true);
        }

        /// <summary>
        /// ContinuousProtections.CannotBeAttackTarget: a face-up <paramref name="monster"/> whose controller controls a
        /// face-up, unnegated <see cref="EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters"/> card (itself
        /// included) of its live Type cannot be selected as an attack target.
        /// </summary>
        public static bool CannotBeAttackTarget(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster?.Def == null || !monster.FaceUp) return false;
            var ctrl = engine.ControllerOf(monster);
            if (ctrl == null) return false;
            foreach (var src in ctrl.MonstersOnField())
            {
                if (src == null || engine.IsSummonPending(src)) continue;
                foreach (var c in LiveContinuous(src, EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters))
                    if (ClassicEraTemplates.RaceMatches(monster.Race, c.RaceFilter))
                        return true;
            }

            return false;
        }

        // ───────────────────────────── Legendary Jujitsu Master ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep"/>: <paramref name="source"/> was the attack
        /// target and battled in Defense Position (ygopro GetBattlePosition); the attacker (<paramref name="other"/>), still
        /// related to the battle, goes on top of its owner's Deck (a Fusion Monster to the Extra Deck).
        /// </summary>
        public static void PlaceAttackerOnTopOfDeck(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance other)
        {
            if (engine == null || who == null || source == null || clause == null || other == null) return;
            var b = Current(engine);
            if (b == null || !ReferenceEquals(b.Target, source) || !ReferenceEquals(b.Attacker, other) ||
                !b.TargetInDefense)
                return;
            if (!SourceMayResolve(engine, source, other)) return;
            if (!RelatesToBattle(engine, other))
            {
                engine.Log($"{source.Name}: the attacking monster is no longer on the field.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, other, source)) return;
            engine.Log($"{source.Name}: {other.Name} battled this Defense Position card — place it on top of the Deck.");
            engine.ReturnCardToDeck(other, onTop: true);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Insect Princess ─────────────────────────────

        /// <summary>
        /// FieldSpellEffects.ApplyCompiledContinuous, <see cref="EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack"/>:
        /// each monster of the Type the opponent of <paramref name="source"/>'s controller controls is in face-up Attack
        /// Position (ygopro EFFECT_SET_POSITION, re-applied at each adjust): a face-down one is flipped face-up and its FLIP
        /// effect activates once play is idle (DuelEngine.QueueFlipEffectAfterResolution, SimultaneousTriggers), and none of
        /// them can change its battle position while this applies (<see cref="CardInstance.PositionLockedByEffect"/>,
        /// reset by FieldSpellEffects.ClearOne). False for any other clause.
        /// </summary>
        public static bool ApplyContinuous(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (clause == null || clause.Action != EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack) return false;
            if (engine == null || source == null) return true;
            var ctrl = engine.ControllerOf(source);
            var opp = ctrl != null ? engine.OpponentOf(ctrl) : null;
            if (opp == null) return true;
            var changed = new List<(CardInstance Monster, bool WasFaceDown)>();
            SimultaneousTriggersRuntime.Capture(engine, () =>
            {
                foreach (var m in opp.MonstersOnField().ToList())
                {
                    if (m?.Def == null || engine.IsSummonPending(m)) continue;
                    if (!ClassicEraTemplates.RaceMatches(m.Race, clause.RaceFilter)) continue;
                    if (ContinuousProtections.IsUnaffectedBy(engine, m, source)) continue;
                    if (!m.FaceUp || m.Position != BattlePosition.Attack)
                    {
                        var wasFaceDown = !m.FaceUp;
                        if (engine.ApplyEffectPositionChange(m, BattlePosition.Attack, faceUp: true))
                        {
                            changed.Add((m, wasFaceDown));
                            if (wasFaceDown)
                                engine.QueueFlipEffectAfterResolution(opp, m);
                        }
                    }

                    m.PositionLockedByEffect = true;
                }
            });

            foreach (var (m, wasFaceDown) in changed)
            {
                engine.Log(wasFaceDown
                    ? $"{source.Name}: {m.Name} is flipped face-up in Attack Position."
                    : $"{source.Name}: {m.Name} is changed to Attack Position.");
                if (m.FaceUp && engine.ControllerOf(m) != null)
                    SimultaneousTriggersRuntime.QueueThisCardPositionChanged(engine, m, toDefense: false);
            }

            return true;
        }

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle: true when <paramref name="clause"/> of
        /// <paramref name="source"/> does not activate — <see cref="EffectClause.BattledMonsterRaceFilter"/> and the
        /// destroyed monster was not of that Type when it battled (ygopro EVENT_BATTLED flag, Insect Princess).
        /// </summary>
        public static bool SkipsBattleDestroying(DuelEngine engine, EffectClause clause, CardInstance source,
            CardInstance destroyed)
        {
            if (clause == null || string.IsNullOrEmpty(clause.BattledMonsterRaceFilter)) return false;
            if (destroyed?.Def == null) return true;
            var b = Current(engine);
            string race;
            if (b != null && ReferenceEquals(b.Attacker, source) && ReferenceEquals(b.Target, destroyed))
                race = b.TargetRace;
            else if (b != null && ReferenceEquals(b.Target, source) && ReferenceEquals(b.Attacker, destroyed))
                race = b.AttackerRace;
            else
                race = destroyed.Race;
            return !ClassicEraTemplates.RaceMatches(race, clause.BattledMonsterRaceFilter);
        }

        // ───────────────────────────── Enraged Battle Ox ─────────────────────────────

        /// <summary>
        /// BattleBoostsRuntime.FieldGrantsPiercing: a face-up, unnegated monster of the attacker's controller with
        /// <see cref="EffectActionKind.GrantPiercingToYourRaceMonsters"/> whose Types include the attacker's live Type
        /// (the source itself included).
        /// </summary>
        public static bool MonsterGrantsPiercing(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker?.Def == null || !attacker.FaceUp) return false;
            var ctrl = engine.ControllerOf(attacker);
            if (ctrl == null) return false;
            foreach (var src in ctrl.MonstersOnField())
            {
                if (src == null || engine.IsSummonPending(src)) continue;
                foreach (var c in LiveContinuous(src, EffectActionKind.GrantPiercingToYourRaceMonsters))
                    if (ClassicEraTemplates.RaceMatches(attacker.Race, c.RaceFilter))
                        return true;
            }

            return false;
        }

        // ───────────────────────────── Drillago ─────────────────────────────

        /// <summary>
        /// DuelEngine.GrantsDirectAttack: <paramref name="attacker"/>'s own
        /// <see cref="EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast"/> applies — the
        /// opponent's Spell &amp; Trap Zones and Field Zone are empty and every monster they control is face-up with ATK at
        /// least <see cref="EffectClause.Amount"/>.
        /// </summary>
        public static bool GrantsDirectAttack(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker?.Def == null) return false;
            var clauses = LiveContinuous(attacker,
                EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast).ToList();
            if (clauses.Count == 0) return false;
            var ctrl = engine.ControllerOf(attacker);
            var opp = ctrl != null ? engine.OpponentOf(ctrl) : null;
            if (opp == null || opp.SpellTrapsOnField().Any()) return false;
            var monsters = opp.MonstersOnField().Where(m => m != null).ToList();
            foreach (var c in clauses)
                if (monsters.TrueForAll(m => m.FaceUp && m.CurrentAtk >= c.Amount))
                    return true;
            return false;
        }
    }
}
