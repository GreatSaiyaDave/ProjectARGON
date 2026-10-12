using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Toons batch (v142) runtime for the sentences compiled by <see cref="ToonsAtoms"/>. Every rule reads the
    /// card's own FullyCompiled program, never a card id:
    /// <list type="bullet">
    /// <item><see cref="EffectActionKind.SpecialSummonFromHandProcedure"/> — the printed Special Summon from the hand
    /// (DuelEngine.CanSpecialSummonProcedure / TrySpecialSummonProcedure → DuelEngine.SpecialSummonByOwnHandProcedure,
    /// with a summon-negation window), and the "Must first be" gate: a copy this procedure has not summoned
    /// (<see cref="CardInstance.ProcedureCompleted"/>) cannot be Special Summoned any other way — never from the hand
    /// or Deck by an effect, and from the GY / banishment only once properly summoned.</item>
    /// <item><see cref="EffectActionKind.CannotAttackTurnSpecialSummoned"/> — set on every face-up Special Summon.</item>
    /// <item><see cref="EffectActionKind.DestroyThisWhenNamedDestroyed"/> — destruction of a face-up named card.</item>
    /// <item><see cref="EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon"/> — the direct-attack / Toon
    /// attack-target rule (DuelEngine.CanAttackDirectly / GrantsDirectAttack / TryAttack, RulesValidator,
    /// LegalIntentService, SimpleAi).</item>
    /// </list>
    /// The 500 LP attack cost is <see cref="EffectActionKind.PayLpToDeclareAttack"/> (Dark Elf's).
    /// </summary>
    public static class ToonsRuntime
    {
        // ───────────────────── Compiled clause lookups ─────────────────────

        /// <summary>First clause of <paramref name="action"/> in the card's FullyCompiled program, else null.</summary>
        static EffectClause ClauseOf(CardDef def, EffectActionKind action)
        {
            if (def == null || !def.IsMonster) return null;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action)
                    return c;
            return null;
        }

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action) =>
            card?.Def == null ? null : ClauseOf(card.Def, action);

        /// <summary>A face-up monster whose effects are not negated and whose program carries <paramref name="action"/>.</summary>
        static EffectClause ActiveClause(CardInstance monster, EffectActionKind action)
        {
            if (monster?.Def == null || !monster.FaceUp || monster.IsNegated) return null;
            return ClauseOf(monster, action);
        }

        /// <summary>The card's own "Must first be Special Summoned (from your hand) …" procedure, else null.</summary>
        public static EffectClause HandProcedureOf(CardInstance card) =>
            ClauseOf(card, EffectActionKind.SpecialSummonFromHandProcedure);

        public static bool HasHandProcedure(CardInstance card) => HandProcedureOf(card) != null;

        /// <summary>The card is face-up in a Monster Zone, Spell &amp; Trap Zone or Field Zone.</summary>
        public static bool IsFaceUpOnField(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && card.FaceUp && engine.ControllerOf(card) != null;

        // ───────────────────── "Must first be Special Summoned (from your hand) …" ─────────────────────

        /// <summary>
        /// The printed procedure is legal right now: the card is in the controller's hand, it is their Main Phase
        /// with no Chain, response window or pick open, they control the named card face-up (ygopro
        /// aux.FaceupFilter(Card.IsCode) on LOCATION_ONFIELD — a negated copy still counts), and either a Monster
        /// Zone is free (no Tribute) or enough monsters can be Tributed and one of their zones frees up
        /// (Duel.CheckReleaseGroup with check_field).
        /// </summary>
        public static bool CanSummonByHandProcedure(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = "No printed Special Summon procedure.";
            var proc = HandProcedureOf(card);
            if (engine == null || who == null || proc == null) return false;
            if (engine.GameOver)
            {
                reason = "The Duel is over.";
                return false;
            }

            if (who.Hand == null || !who.Hand.Contains(card))
            {
                reason = "Not in hand.";
                return false;
            }

            if (engine.TurnPlayer != who || !engine.InMainPhase)
            {
                reason = "Only during your Main Phase.";
                return false;
            }

            if (engine.Chain.HasLinks || engine.Chain.IsResolving || engine.IsResolvingChain ||
                engine.IsAwaitingResponse || engine.IsAwaitingEffectTarget || engine.SummonAwaitingSuccess != null)
            {
                reason = "Not while a Chain, a response or a choice is open.";
                return false;
            }

            // SanctuaryAndZera batch: "… except by Tributing 1 face-up "X" on your side of the field while "Y" is on the field"
            // (Archlord Zerato, Mazera DeVille).
            if (SanctuaryAndZeraRuntime.IsNamedTributeProcedure(proc))
                return SanctuaryAndZeraRuntime.CanSummonByNamedTribute(engine, who, card, proc, out reason);

            if (!string.IsNullOrEmpty(proc.RequiresFaceUpName) &&
                !FieldSpellEffects.ControlsFaceUpNamed(who, proc.RequiresFaceUpName))
            {
                reason = $"Requires a face-up \"{proc.RequiresFaceUpName}\" you control.";
                return false;
            }

            // EngineHygiene batch: "… by banishing N ATTRIBUTE monster(s) from your GY" (Gigantes, Spirit of Flames,
            // Inferno) — enough monsters to banish as the cost.
            if (!EngineHygieneRuntime.CanPayProcedureBanish(engine, who, card, proc, out reason))
                return false;

            var need = Math.Max(0, proc.Amount);
            if (need == 0)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0)
                {
                    reason = "No free Monster Zone.";
                    return false;
                }

                reason = "OK";
                return true;
            }

            if (ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                reason = "Neither player can Tribute cards.";
                return false;
            }

            var candidates = TributeCandidates(who);
            if (candidates.Count < need)
            {
                reason = $"Need {need} monster(s) to Tribute.";
                return false;
            }

            // A full field needs one of your own monsters among the Tributes (a Soul Exchange target frees none).
            if (engine.FirstEmpty(who.MonsterZones) < 0 &&
                TributeSummonsRuntime.ForcedTributesForSummon(who).Count(candidates.Contains) >= need)
            {
                reason = "No free Monster Zone after those Tributes.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// Perform the printed procedure: Tribute the monsters (cost), then Special Summon the card from the hand in
        /// face-up Attack Position (DuelEngine.SpecialSummonByOwnHandProcedure). A human with a real choice of
        /// Tributes picks them first by tapping their monsters (<see cref="DuelEngine.PendingTributes"/>, the
        /// Tribute Summon flow) and Special Summons again; the AI Tributes its lowest-ATK monsters.
        /// </summary>
        public static bool TrySummonByHandProcedure(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (!CanSummonByHandProcedure(engine, who, card, out var reason))
            {
                engine?.Log($"Cannot Special Summon {card?.Name}: {reason}");
                return false;
            }

            var proc = HandProcedureOf(card);
            // SanctuaryAndZera batch: the named face-up Tribute (Archlord Zerato, Mazera DeVille).
            if (SanctuaryAndZeraRuntime.IsNamedTributeProcedure(proc))
                return SanctuaryAndZeraRuntime.SummonByNamedTribute(engine, who, card, proc);
            // EngineHygiene batch: the banish-from-GY procedure (no Tribute) pays and Summons there.
            if (proc.BanishFromGyCount > 0)
                return EngineHygieneRuntime.SummonByBanishProcedure(engine, who, card, proc);
            // SpecialSummonEffects batch (v161): Gradius' Option selects 1 face-up "Gradius" (a pick for a human with 2+).
            if (SpecialSummonEffectsRuntime.TrySummonBySelectingProcedure(engine, who, card, out var selectedOk))
                return selectedOk;
            var need = Math.Max(0, proc.Amount);
            List<CardInstance> tributes = null;
            if (need > 0)
            {
                tributes = ResolveTributes(engine, who, need);
                if (tributes == null)
                {
                    engine.Log(
                        $"Choose {need} monster(s) to Tribute for {card.Name} — tap your monster(s), including " +
                        $"face-down Sets (selected {engine.PendingTributes.Count}/{need}), then Special Summon again.");
                    return false;
                }

                if (engine.FirstEmpty(who.MonsterZones) < 0 && !tributes.Any(t => who.TryFindMonster(t, out _)))
                {
                    engine.Log("No free Monster Zone after those Tributes.");
                    return false;
                }
            }

            engine.Log(need > 0
                ? $"{who.Name} Special Summons {card.Name} from the hand by Tributing " +
                  $"{string.Join(", ", tributes.Select(t => t.FaceUp ? t.Name : "a face-down monster"))} " +
                  $"(controls \"{proc.RequiresFaceUpName}\")."
                : $"{who.Name} Special Summons {card.Name} from the hand (controls \"{proc.RequiresFaceUpName}\").");
            return engine.SpecialSummonByOwnHandProcedure(who, card, tributes);
        }

        /// <summary>
        /// Monsters <paramref name="who"/> can Tribute for the procedure: their own, face-up or face-down, then the Soul
        /// Exchange targets they must Tribute as if they controlled them, first. The procedure is not a Tribute Summon
        /// (ygopro Duel.CheckReleaseGroup / SelectReleaseGroup, EFFECT_UNRELEASABLE_NONSUM): a monster that only
        /// "cannot be Tributed for a Tribute Summon" (Scapegoat / Ojama Trio / Multiply Tokens, Fox Fire) may be
        /// Tributed; one that "cannot be Tributed" at all (<see cref="CardInstance.CannotBeTributed"/>, Archfiend's
        /// Roar) may not.
        /// </summary>
        static List<CardInstance> TributeCandidates(DuelistState who)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            foreach (var m in who.MonstersOnField())
                if (m != null && !(m.CannotBeTributed && m.FaceUp))
                    list.Add(m);
            var at = 0;
            foreach (var f in TributeSummonsRuntime.ForcedTributesForSummon(who))
            {
                if (f == null) continue;
                list.Remove(f);
                list.Insert(at++, f);
            }

            return list;
        }

        /// <summary>
        /// The Tributes for the procedure (each monster counts as 1 — Double Coston's "2 Tributes" is for a Tribute
        /// Summon only). Soul Exchange targets come first (a tapped one first when there are more of them than
        /// needed), then the tapped <see cref="DuelEngine.PendingTributes"/>; the rest is taken automatically only
        /// when there is no choice left, or for the AI (lowest ATK first). Null when a human still has to choose.
        /// </summary>
        static List<CardInstance> ResolveTributes(DuelEngine engine, DuelistState who, int need)
        {
            var legal = TributeCandidates(who);
            var pending = engine.PendingTributes;
            var forced = TributeSummonsRuntime.ForcedTributesForSummon(who)
                .Where(legal.Contains)
                .OrderBy(f => pending.Contains(f) ? 0 : 1)
                .ToList();
            var chosen = forced.Take(need).ToList();
            foreach (var t in pending)
            {
                if (chosen.Count >= need) break;
                if (t != null && legal.Contains(t) && !chosen.Contains(t))
                    chosen.Add(t);
            }

            if (chosen.Count >= need) return chosen;
            var remaining = legal.Where(m => !chosen.Contains(m)).ToList();
            var stillNeed = need - chosen.Count;
            if (remaining.Count < stillNeed) return null;
            if (remaining.Count == stillNeed)
            {
                chosen.AddRange(remaining);
                return chosen;
            }

            if (engine.IsHumanControlled(who)) return null;
            chosen.AddRange(remaining
                .OrderBy(m => m.CurrentAtk)
                .ThenBy(m => m.Level)
                .Take(stillNeed));
            return chosen;
        }

        /// <summary>
        /// "Must first be Special Summoned …": a copy its own procedure has not summoned
        /// (<see cref="CardInstance.ProcedureCompleted"/>) cannot be Special Summoned by anything else. Once properly
        /// summoned it may be Special Summoned from the GY or banishment (Monster Reborn).
        /// </summary>
        public static bool BlocksSpecialSummon(CardInstance card) =>
            card != null && !card.ProcedureCompleted && HasHandProcedure(card);

        /// <summary>Hand / Deck candidates of a Special Summon effect: a copy there was never properly summoned.</summary>
        public static bool DefNeedsOwnProcedure(CardDef def) =>
            ClauseOf(def, EffectActionKind.SpecialSummonFromHandProcedure) != null;

        // ───────────────────── "Cannot attack the turn it is Special Summoned." ─────────────────────

        /// <summary>
        /// Called by DuelEngine right after any Special Summon places the card. A face-up copy with the sentence
        /// cannot declare an attack for the rest of this turn (EVENT_SPSUMMON_SUCCESS registers EFFECT_CANNOT_ATTACK
        /// until the End Phase).
        /// </summary>
        public static void OnSpecialSummoned(DuelEngine engine, CardInstance card, bool faceUp)
        {
            if (engine == null || card == null || !faceUp) return;
            if (ClauseOf(card, EffectActionKind.CannotAttackTurnSpecialSummoned) == null) return;
            card.CannotAttackDuringTurnNumber = engine.TurnNumber;
        }

        // ───────────────────── "If "X" on the field is destroyed, destroy this card." ─────────────────────

        /// <summary>
        /// A card that was face-up on the field (<paramref name="wasFaceUp"/>) has just been destroyed — by battle or
        /// by a card effect, to the GY or banished. Every face-up, non-negated monster on either field whose clause
        /// names it is destroyed at once (a continuous effect: no chain link). A face-down named card, and one that
        /// left the field without being destroyed, destroy nothing.
        /// </summary>
        public static void NotifyFaceUpCardDestroyed(DuelEngine engine, CardInstance destroyed, bool wasFaceUp)
        {
            if (engine == null || destroyed?.Def == null || !wasFaceUp || engine.GameOver) return;
            // DcrSpellTrapUtility batch: Different Dimension Gate destroyed and sent to the GY returns its monsters.
            DcrSpellTrapUtilityRuntime.OnCardDestroyed(engine, destroyed);
            if (engine.GameOver) return;
            List<(DuelistState side, CardInstance monster, string name)> doomed = null;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || m == destroyed) continue;
                    var c = ActiveClause(m, EffectActionKind.DestroyThisWhenNamedDestroyed);
                    if (c == null || string.IsNullOrEmpty(c.NamedCard) || !destroyed.IsNamed(c.NamedCard))
                        continue;
                    (doomed ??= new List<(DuelistState, CardInstance, string)>()).Add((side, m, c.NamedCard));
                }
            }

            if (doomed == null) return;
            foreach (var (side, m, name) in doomed)
            {
                if (engine.GameOver) return;
                if (!side.TryFindMonster(m, out _)) continue;
                engine.Log($"\"{name}\" was destroyed — {m.Name} is destroyed.");
                engine.DestroyMonsterPublic(side, m, m);
            }
        }

        // ───────────────────── Toon attack rule ─────────────────────

        /// <summary>A face-up Toon monster (printed card type Toon — ygopro TYPE_TOON).</summary>
        public static bool IsFaceUpToonMonster(CardInstance m) =>
            m?.Def != null && m.FaceUp && m.Def.IsMonster && m.Def.type != null &&
            m.Def.type.IndexOf("Toon", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool OpponentControlsFaceUpToon(DuelEngine engine, CardInstance attacker)
        {
            var ctrl = engine?.ControllerOf(attacker);
            var opp = ctrl != null ? engine.OpponentOf(ctrl) : null;
            return opp != null && opp.MonstersOnField().Any(IsFaceUpToonMonster);
        }

        static bool HasToonAttackRule(CardInstance attacker) =>
            ActiveClause(attacker, EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon) != null;

        /// <summary>EFFECT_DIRECT_ATTACK: the rule applies and the opponent controls no face-up Toon monster.</summary>
        public static bool GrantsDirectAttack(DuelEngine engine, CardInstance attacker) =>
            engine != null && HasToonAttackRule(attacker) && !OpponentControlsFaceUpToon(engine, attacker);

        /// <summary>EFFECT_CANNOT_DIRECT_ATTACK: the rule applies and the opponent controls a face-up Toon monster.</summary>
        public static bool ForbidsDirectAttack(DuelEngine engine, CardInstance attacker) =>
            engine != null && HasToonAttackRule(attacker) && OpponentControlsFaceUpToon(engine, attacker);

        /// <summary>
        /// EFFECT_CANNOT_SELECT_BATTLE_TARGET: while the opponent controls a face-up Toon monster, this card can only
        /// attack a face-up Toon monster (a face-down one or a non-Toon is not a legal attack target).
        /// </summary>
        public static bool ForbidsAttackTarget(DuelEngine engine, CardInstance attacker, CardInstance target) =>
            target != null && ForbidsDirectAttack(engine, attacker) && !IsFaceUpToonMonster(target);

        // ───────────────────── AI ─────────────────────

        /// <summary>
        /// Simple AI: use the procedure when it is legal and the Tributes it would pay (lowest ATK first) are worth
        /// less ATK than the monster they bring out.
        /// </summary>
        public static bool AiWantsHandProcedure(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            var proc = HandProcedureOf(card);
            if (proc == null || !CanSummonByHandProcedure(engine, ai, card, out _)) return false;
            var need = Math.Max(0, proc.Amount);
            if (need == 0) return true;
            var cost = TributeCandidates(ai)
                .Where(m => ai.TryFindMonster(m, out _))
                .OrderBy(m => m.CurrentAtk)
                .Take(need)
                .Sum(m => m.CurrentAtk);
            return cost < card.CurrentAtk;
        }
    }
}
