using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "StandbyBurnsAndDice" (v162). The Standby clauses (<see cref="StandbyBurnsAndDiceAtoms"/>) fire from
    /// TextEffectRuntime.FirePhaseTriggersFor and resolve here:
    /// <list type="bullet">
    /// <item><b>Blind Destruction</b>: a die (<see cref="CoinDiceInterruptsRuntime.RollDie"/>, so Dice Re-Roll is offered
    /// first); every face-up monster on the field with that Level (Level 6+ on a 6) is destroyed in one event
    /// (<see cref="FlipMassEffectsRuntime.Simultaneously"/>).</item>
    /// <item><b>Needle Wall</b>: a die, rolled again while it shows 6; the monster in the opponent's Main Monster Zone
    /// numbered by it, counting from your right, is destroyed. The engine's Monster Zone index is the ygopro sequence
    /// (Ocg/OcgBoardView maps <c>loc.Sequence</c> to <c>MonsterZones[Sequence]</c>), and the opponent's sequence 0 is
    /// the zone on your right, so the result d is <c>MonsterZones[d - 1]</c> (Lua GetFieldCard(1-tp,MZONE,d-1)).</item>
    /// <item><b>Dark Snake Syndrome</b>: both players take the same damage at once
    /// (<see cref="DuelEngine.ApplyEffectDamageBoth"/>): 200, then double the last amount. The amount belongs to this
    /// field presence (Lua flag label, RESETS_STANDARD): <see cref="OnBoardSettled"/> drops it once the card is no longer
    /// face-up on the field.</item>
    /// <item><b>Mask of Dispel</b>: its activation links the targeted face-up Spell (<see cref="LinkMask"/>); in your
    /// Standby Phase that Spell's current controller takes 500; when the Spell leaves the field this card is destroyed
    /// (<see cref="OnBoardSettled"/>, a continuous effect: no Chain Link, not while this card is negated).</item>
    /// <item><b>Nightmare Wheel</b>: the Spellbinding Circle bind (EquippedTo) with
    /// <see cref="EffectClause.DestroyThisWhenBoundHostLeaves"/> (DuelEngine.UnlinkEquips); its Standby burn needs the
    /// bound monster on the field (<see cref="ResolveWheelBurn"/>).</item>
    /// <item><b>Inspection</b>: an optional trigger in the opponent's Standby Phase. The AI pays at once while it keeps
    /// at least <see cref="AiInspectionFloorLp"/> LP; a human is asked through the LP-cost picker (PAY N / CANCEL) once the
    /// phase's other triggers are done (<see cref="OfferQueuedInspection"/>, the Aqua Spirit precedent: the opponent's
    /// turn goes on to Main Phase 1 and SimpleAi waits for the answer). Paying looks at 1 random card of their hand (a
    /// log line, as Fengsheng Mirror), then they shuffle their hand.</item>
    /// </list>
    /// The Unfriendly Amazon's upkeep is <see cref="TypeDeclarationRuntime"/>'s Standby Tribute upkeep with
    /// <see cref="EffectClause.TributeExceptThis"/> and <see cref="EffectClause.UpkeepTributeMandatory"/>.
    /// Graverobber's Retribution is the generic per-count burn with
    /// <see cref="PerCountBurnsRuntime.OppBanishedFaceUpMonsters"/>.
    /// </summary>
    public static class StandbyBurnsAndDiceRuntime
    {
        /// <summary>The AI pays for Inspection only while it keeps at least this many LP afterwards.</summary>
        public const int AiInspectionFloorLp = 4000;

        sealed class QueuedInspection
        {
            public DuelistState Who;
            public CardInstance Card;
            public int Amount;
            public int Turn;
        }

        sealed class State
        {
            /// <summary>Dark Snake Syndrome: the damage its last resolution dealt (this field presence).</summary>
            public readonly Dictionary<CardInstance, int> SnakeDamage = new();
            /// <summary>Mask of Dispel: the linked Spell of each face-up Mask.</summary>
            public readonly Dictionary<CardInstance, CardInstance> MaskLinks = new();
            /// <summary>Human Inspection choices of this Standby Phase, in trigger order.</summary>
            public readonly List<QueuedInspection> Inspections = new();
            public bool Settling;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, QueuedInspection> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary><paramref name="card"/> is face-up on the field (ygopro IsRelateToEffect for a Standby trigger).</summary>
        static bool FaceUpOnField(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && card.FaceUp && engine.ControllerOf(card) != null &&
            !InAnyPile(engine, card);

        /// <summary>A Continuous Spell/Trap destroyed in response to its own activation may sit in a zone and a GY.</summary>
        static bool InAnyPile(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side != null && (side.Graveyard.Contains(card) || side.Banished.Contains(card)))
                    return true;
            return false;
        }

        /// <summary>
        /// TextEffectRuntime.IsContinuousTrapPlacementOnly: a Standby trigger of the face-up card (not part of its
        /// activation, which only places the card).
        /// </summary>
        public static bool IsFaceUpTriggerOnly(EffectClause c) =>
            c != null && c.Timing == EffectTiming.StandbyPhase &&
            (c.Action == EffectActionKind.RollDieDestroyFaceUpMonstersWithRolledLevel ||
             c.Action == EffectActionKind.RollDieDestroyOppMonsterInRolledZone ||
             c.Action == EffectActionKind.InflictDoublingDamageToBothPlayers ||
             c.Action == EffectActionKind.PayLpLookAtRandomOppHandCard ||
             (c.Action == EffectActionKind.InflictDamageToOpponent &&
              c.CountSource == PerCountBurnsRuntime.OppBanishedFaceUpMonsters));

        // ───────────────────────────── Blind Destruction ─────────────────────────────

        /// <summary>Face-up monsters on both fields that a roll of <paramref name="d"/> destroys.</summary>
        public static List<CardInstance> BlindDestructionVictims(DuelEngine engine, int d)
        {
            var list = new List<CardInstance>();
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m?.Def == null || !m.FaceUp) continue;
                    if (d < 6 ? m.Level == d : m.Level >= 6) list.Add(m);
                }
            }

            return list;
        }

        /// <summary>The roller's gain from a roll: ATK destroyed on the opponent's side minus on the roller's side.</summary>
        static int BlindValue(DuelEngine engine, DuelistState roller, int d)
        {
            var v = 0;
            foreach (var m in BlindDestructionVictims(engine, d))
                v += ReferenceEquals(engine.ControllerOf(m), roller) ? -Math.Max(1, m.CurrentAtk) : Math.Max(1, m.CurrentAtk);
            return v;
        }

        public static void ResolveBlindDestruction(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || !FaceUpOnField(engine, source)) return;
            CoinDiceInterruptsRuntime.RollDie(engine, who, source,
                d => BlindValue(engine, who, d) >= 0,
                d => ApplyBlindDestruction(engine, who, source, d));
        }

        static void ApplyBlindDestruction(DuelEngine engine, DuelistState who, CardInstance source, int d)
        {
            if (engine.GameOver) return;
            var victims = BlindDestructionVictims(engine, d);
            var what = d < 6 ? $"Level {d}" : "Level 6 or higher";
            engine.Log($"{source?.Name}: {who.Name} rolled {d} — destroy every face-up {what} monster on the field" +
                       (victims.Count == 0 ? " (none)." : $": {string.Join(", ", victims.Select(v => v.Name))}."));
            if (victims.Count == 0) return;
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var v in victims)
                    if (engine.ControllerOf(v) != null)
                        TextEffectRuntime.DestroyCard(engine, v, source, byPlayer: who);
            });
        }

        // ───────────────────────────── Needle Wall ─────────────────────────────

        /// <summary>The monster in <paramref name="opp"/>'s Main Monster Zone numbered <paramref name="d"/> (1-5) from the
        /// roller's right: ygopro sequence d-1, the engine's <c>MonsterZones[d - 1]</c>.</summary>
        public static CardInstance NeedleWallZoneMonster(DuelistState opp, int d)
        {
            if (opp?.MonsterZones == null || d < 1 || d > 5 || d > opp.MonsterZones.Length) return null;
            return opp.MonsterZones[d - 1]?.Occupant;
        }

        public static void ResolveNeedleWall(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || !FaceUpOnField(engine, source)) return;
            RollNeedleWall(engine, who, source, 0);
        }

        static void RollNeedleWall(DuelEngine engine, DuelistState who, CardInstance source, int sixes)
        {
            var opp = engine.OpponentOf(who);
            CoinDiceInterruptsRuntime.RollDie(engine, who, source,
                d => d == 6 || NeedleWallZoneMonster(opp, d) != null,
                d =>
                {
                    if (engine.GameOver) return;
                    if (d == 6)
                    {
                        engine.Log($"{source?.Name}: {who.Name} rolled 6 — roll again.");
                        if (sixes < 1000) RollNeedleWall(engine, who, source, sixes + 1);
                        return;
                    }

                    var victim = NeedleWallZoneMonster(opp, d);
                    engine.Log($"{source?.Name}: {who.Name} rolled {d} — " +
                               (victim == null
                                   ? $"{opp?.Name}'s Main Monster Zone {d} is empty."
                                   : $"destroy {victim.Name} in {opp?.Name}'s Main Monster Zone {d}."));
                    if (victim != null)
                        TextEffectRuntime.DestroyCard(engine, victim, source, byPlayer: who);
                });
        }

        // ───────────────────────────── Dark Snake Syndrome ─────────────────────────────

        public static void ResolveDarkSnake(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || !FaceUpOnField(engine, source)) return;
            var st = StateOf(engine);
            var first = clause != null && clause.Amount > 0 ? clause.Amount : 200;
            var dmg = st.SnakeDamage.TryGetValue(source, out var last) && last > 0
                ? (int)Math.Min(int.MaxValue / 2, 2L * last)
                : first;
            st.SnakeDamage[source] = dmg;
            engine.ApplyEffectDamageBoth(who, dmg, engine.OpponentOf(who), dmg, source.Name);
        }

        /// <summary>Tests / UI: the damage Dark Snake Syndrome dealt last in this field presence (0 = not yet).</summary>
        public static int DarkSnakeLastDamage(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_state.TryGetValue(engine, out var st) &&
            st.SnakeDamage.TryGetValue(card, out var d)
                ? d
                : 0;

        // ───────────────────────────── Mask of Dispel ─────────────────────────────

        static bool IsFaceUpSpellOnField(DuelEngine engine, CardInstance card) =>
            card?.Def != null && card.Def.IsSpell && FaceUpOnField(engine, card);

        /// <summary>CollectTargets (<see cref="EffectZoneFilter.FieldFaceUpSpells"/>): face-up Spells in both players'
        /// Spell &amp; Trap Zones and Field Zones, never the activating card (Lua LOCATION_SZONE both sides, except c).</summary>
        public static void CollectFaceUpSpells(DuelistState who, DuelistState opp, CardInstance except,
            List<CardInstance> into)
        {
            if (into == null) return;
            EquipAndSpellProtectionRuntime.CollectControllerFaceUpSpells(who, except, into);
            var mine = into.Count;
            EquipAndSpellProtectionRuntime.CollectControllerFaceUpSpells(opp, except, into);
            // Keep each candidate once (a Field Spell is listed by its side only).
            for (var i = into.Count - 1; i >= mine; i--)
                if (into.IndexOf(into[i]) != i) into.RemoveAt(i);
        }

        /// <summary>AutoPick (<see cref="EffectZoneFilter.FieldFaceUpSpells"/>): an opponent's Spell (its controller takes
        /// the damage), the one most likely to stay (a Field / Continuous Spell); else the first legal one.</summary>
        public static CardInstance AutoPickSpell(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var theirs = targets.Where(t => !ReferenceEquals(engine?.ControllerOf(t), who)).ToList();
            var pool = theirs.Count > 0 ? theirs : targets;
            return pool.FirstOrDefault(t => t.Def != null && (t.Def.IsFieldSpell || t.Def.IsContinuousSpellOrTrap)) ??
                   pool[0];
        }

        /// <summary>Mask of Dispel resolves: link the target if this card and the target are still face-up on the field.</summary>
        public static void LinkMask(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target)
        {
            if (engine == null || source == null) return;
            if (target == null || ReferenceEquals(target, source) || !FaceUpOnField(engine, source) ||
                !IsFaceUpSpellOnField(engine, target))
            {
                engine.Log($"{source.Name}: the selected Spell is no longer face-up on the field — nothing is linked.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            StateOf(engine).MaskLinks[source] = target;
            engine.Log($"{source.Name} selects {target.Name}.");
        }

        /// <summary>The Spell linked to a face-up Mask of Dispel, or null.</summary>
        public static CardInstance LinkedSpellOf(DuelEngine engine, CardInstance mask) =>
            engine != null && mask != null && s_state.TryGetValue(engine, out var st) &&
            st.MaskLinks.TryGetValue(mask, out var t)
                ? t
                : null;

        public static void ResolveMaskBurn(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || !FaceUpOnField(engine, source)) return;
            var target = LinkedSpellOf(engine, source);
            var holder = target != null ? engine.ControllerOf(target) : null;
            if (holder == null) return; // Lua damcon: no card target, no trigger
            engine.ApplyEffectDamage(holder, clause != null && clause.Amount > 0 ? clause.Amount : 500, source.Name);
        }

        static bool HasLeaveFieldMarker(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog != null && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                c.Action == EffectActionKind.DestroyThisWhenLinkedSpellLeavesField);
        }

        // ───────────────────────────── Nightmare Wheel ─────────────────────────────

        public static void ResolveWheelBurn(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || !FaceUpOnField(engine, source)) return;
            var bound = source.EquippedTo;
            if (bound == null || engine.ControllerOf(bound) == null || !engine.ControllerOf(bound).TryFindMonster(bound, out _))
            {
                engine.Log($"{source.Name}: the monster it targets is not on the field — no damage.");
                return;
            }

            engine.ApplyEffectDamage(engine.OpponentOf(who), clause != null && clause.Amount > 0 ? clause.Amount : 500,
                source.Name);
        }

        // ───────────────────────────── Inspection ─────────────────────────────

        /// <summary>The AI pays for Inspection only while it keeps <see cref="AiInspectionFloorLp"/> LP or more.</summary>
        public static bool AiUsesInspection(DuelistState who, int amount) =>
            who != null && who.LifePoints - amount >= AiInspectionFloorLp;

        static bool InspectionUsable(DuelEngine engine, DuelistState who, CardInstance card, int amount)
        {
            var opp = engine?.OpponentOf(who);
            return who != null && FaceUpOnField(engine, card) && ReferenceEquals(engine.ControllerOf(card), who) &&
                   opp?.Hand != null && opp.Hand.Count > 0 && who.LifePoints >= amount;
        }

        /// <summary>
        /// FirePhaseTriggersFor (the opponent's Standby Phase): the AI decides at once; a human's choice is queued and asked
        /// by <see cref="OfferQueuedInspection"/> once the phase's other triggers are done.
        /// </summary>
        public static void ResolveInspection(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null) return;
            var amount = clause != null && clause.Amount > 0 ? clause.Amount : 500;
            if (ReferenceEquals(engine.TurnPlayer, who) || !InspectionUsable(engine, who, source, amount)) return;
            if (!engine.IsHumanControlled(who))
            {
                if (AiUsesInspection(who, amount)) Inspect(engine, who, source, amount);
                return;
            }

            var st = StateOf(engine);
            st.Inspections.RemoveAll(q => q.Turn != engine.TurnNumber);
            if (st.Inspections.Exists(q => ReferenceEquals(q.Card, source))) return;
            st.Inspections.Add(new QueuedInspection { Who = who, Card = source, Amount = amount, Turn = engine.TurnNumber });
        }

        /// <summary>
        /// Ask the next queued human Inspection (PAY N / CANCEL). False when nothing is asked: none queued, another choice
        /// or window is open, or no queued one can still be used (it left the field, the hand is empty, too few LP).
        /// </summary>
        public static bool OfferQueuedInspection(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Inspections.Count == 0) return false;
            if (engine.GameOver)
            {
                st.Inspections.Clear();
                return false;
            }

            if (engine.PendingActivation != null || engine.IsAwaitingResponse) return false;
            while (st.Inspections.Count > 0)
            {
                var q = st.Inspections[0];
                st.Inspections.RemoveAt(0);
                if (q.Turn != engine.TurnNumber || !InspectionUsable(engine, q.Who, q.Card, q.Amount)) continue;
                var p = new PendingActivation
                {
                    Controller = q.Who,
                    Card = q.Card,
                    UsesTextProgram = true,
                    AwaitingLpCost = true,
                    CostNumeric = q.Amount,
                    PromptOverride = $"you can pay {q.Amount} LP to look at 1 random card in your opponent's hand — " +
                                     $"PAY {q.Amount}, or CANCEL."
                };
                p.LpCostChoices.Add(q.Amount);
                s_picks.Add(p, q);
                engine.SetPendingActivation(p);
                engine.Log(p.Prompt);
                return true;
            }

            return false;
        }

        /// <summary>TextEffectRuntime.TryFinishLpCost hook: the human paid for Inspection. False for other LP choices.</summary>
        public static bool TryAnswerLpChoice(DuelEngine engine, int amount)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var q)) return false;
            if (!p.LpCostChoices.Contains(amount)) return true;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            if (InspectionUsable(engine, q.Who, q.Card, q.Amount))
                Inspect(engine, q.Who, q.Card, q.Amount);
            else
                engine.Log($"{q.Card?.Name}: it can no longer be used — nothing is paid.");
            GoOn(engine);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the human does not use Inspection.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var q)) return false;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            engine.Log($"{q.Card?.Name}: {q.Who?.Name} does not use its effect.");
            GoOn(engine);
            return true;
        }

        static void GoOn(DuelEngine engine)
        {
            if (!engine.GameOver && OfferQueuedInspection(engine))
            {
                engine.NotifyPublic();
                return;
            }

            engine.ContinueStandbyAfterUpkeepChoices();
            engine.NotifyPublic();
        }

        /// <summary>Pay the cost, look at 1 random card of the opponent's hand, then they shuffle their hand.</summary>
        static void Inspect(DuelEngine engine, DuelistState who, CardInstance source, int amount)
        {
            var opp = engine.OpponentOf(who);
            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP for {source.Name} → {who.Name} at {who.LifePoints - amount} LP.", isCost: true);
            if (engine.GameOver || opp?.Hand == null || opp.Hand.Count == 0) return;
            var seen = opp.Hand[engine.Rng.PickIndex(opp.Hand.Count)];
            engine.Log($"{source.Name}: {who.Name} looks at 1 random card in {opp.Name}'s hand — {seen?.Name}.");
            opp.ShuffleHand(engine.Rng);
            engine.Log($"{opp.Name} shuffles their hand.");
        }

        // ───────────────────────────── board settle (DuelEngine.Notify) ─────────────────────────────

        /// <summary>
        /// DuelEngine.Notify, after each change: a Dark Snake Syndrome amount and a Mask of Dispel link belong to the card's
        /// field presence (dropped once it is no longer face-up on the field); a Mask whose linked Spell left the field is
        /// destroyed (not while it is negated: the link just ends).
        /// </summary>
        public static void OnBoardSettled(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || !s_state.TryGetValue(engine, out var st) ||
                st.Settling)
                return;
            st.Settling = true;
            try
            {
                foreach (var card in st.SnakeDamage.Keys.ToList())
                    if (!FaceUpOnField(engine, card))
                        st.SnakeDamage.Remove(card);

                List<CardInstance> doomed = null;
                foreach (var kv in st.MaskLinks.ToList())
                {
                    var mask = kv.Key;
                    if (!FaceUpOnField(engine, mask))
                    {
                        st.MaskLinks.Remove(mask);
                        continue;
                    }

                    if (engine.ControllerOf(kv.Value) != null && !InAnyPile(engine, kv.Value)) continue;
                    st.MaskLinks.Remove(mask);
                    if (engine.GameOver || mask.IsNegated || !HasLeaveFieldMarker(mask))
                    {
                        engine.Log($"{mask.Name}: {kv.Value?.Name} left the field — the link ends.");
                        continue;
                    }

                    (doomed ??= new List<CardInstance>()).Add(mask);
                }

                if (doomed == null) return;
                foreach (var mask in doomed)
                {
                    var holder = engine.ControllerOf(mask);
                    if (holder == null) continue;
                    engine.Log($"{mask.Name} is destroyed (the selected card left the field).");
                    TextEffectRuntime.DestroyCard(engine, mask, mask, byPlayer: holder);
                }
            }
            finally
            {
                st.Settling = false;
            }
        }
    }
}
