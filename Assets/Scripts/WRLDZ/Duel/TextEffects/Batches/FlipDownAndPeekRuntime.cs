using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FlipDownAndPeek" (v162) runtime: flip-face-down ignition monsters and "look at" effects. Reads compiled
    /// programs only — no card-id branches.
    /// <list type="bullet">
    /// <item>8-Claws Scorpion: attacking a monster whose battle position is face-down Defense (it was Set when the Damage
    /// Step started), its ATK becomes N for that damage calculation (<see cref="ApplyDamageCalculationAtk"/>, from
    /// DuelEngine.FinishDeferredDamageCalculation).</item>
    /// <item>Giant Axe Mummy: attacked in face-down Defense Position by a monster whose ATK is lower than its DEF, that
    /// attacker is destroyed at the end of the Damage Step (<see cref="ResolveDestroyLowerAtkAttacker"/>).</item>
    /// <item>Wandering Mummy: after it is flipped face-down, its controller's face-down monsters in the Main Monster Zones are
    /// rearranged at random (<see cref="ShuffleFaceDownMainZoneMonsters"/>; no player pick, as ygopro Duel.ShuffleSetCard).</item>
    /// <item>"Look at" (Cobraman Sakuzy, A Man with Wdjat, Trap Dustshoot): the looked-at names are written to the duel log
    /// for the looking player (<see cref="LogLook"/>); the cards stay where they are, face-down, and are not revealed
    /// otherwise. Trap Dustshoot's monster and Wdjat's Standby Phase Set card are chosen at resolution through
    /// ContinuousTrapsAndSpiritSupportRuntime.AskCardPick.</item>
    /// </list>
    /// </summary>
    public static class FlipDownAndPeekRuntime
    {
        /// <summary>The battle whose damage calculation was performed last (Giant Axe Mummy reads it at the end of the Damage Step).</summary>
        sealed class BattleNote
        {
            public CardInstance Attacker;
            public int AttackerEntry;
            /// <summary>The attack target, when its battle position was face-down Defense; otherwise null.</summary>
            public CardInstance FaceDownTarget;
            public int TargetEntry;
        }

        static readonly ConditionalWeakTable<DuelEngine, BattleNote> s_battle = new();

        // ───────────────────────────── "look at" ─────────────────────────────

        /// <summary>
        /// The one "look at" helper: <paramref name="who"/> looks at <paramref name="cards"/> (ygopro Duel.ConfirmCards).
        /// The names go to the duel log for the looking player; nothing else changes (face, position, zone).
        /// </summary>
        public static void LogLook(DuelEngine engine, DuelistState who, CardInstance source, string what,
            IEnumerable<CardInstance> cards)
        {
            if (engine == null) return;
            var names = cards?.Where(c => c != null).Select(c => c.Name).ToList() ?? new List<string>();
            engine.Log($"{source?.Name ?? "Effect"}: {who?.Name} looks at {what} — " +
                       (names.Count == 0 ? "none" : string.Join(", ", names)) + ".");
        }

        /// <summary>Cobraman Sakuzy: look at every Set card in the opponent's Spell &amp; Trap Zones (Field Zone included); they stay Set.</summary>
        public static void LookAtOpponentSetSpellTraps(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var set = opp.SpellTrapsOnField().Where(c => c != null && !c.FaceUp).ToList();
            if (set.Count == 0)
            {
                engine.Log($"{source?.Name}: {opp.Name} has no Set Spell/Trap Card.");
                return;
            }

            LogLook(engine, who, source, $"{opp.Name}'s Set Spell/Trap Cards", set);
            engine.Log($"{source?.Name}: they return to their original position.");
        }

        /// <summary>The opponent's face-down cards (monsters and Spell &amp; Trap Zones): A Man with Wdjat's candidates.</summary>
        static List<CardInstance> OpponentSetCards(DuelEngine engine, DuelistState who, EffectClause clause,
            CardInstance source) =>
            TextEffectRuntime.CollectTargets(engine, who, clause, source);

        /// <summary>
        /// A Man with Wdjat. Summon trigger: <paramref name="chosenTarget"/> was targeted when it activated; it is looked at if it
        /// is still a face-down card the opponent controls. Standby Phase trigger (no target yet): only while this card is
        /// face-up in its controller's Monster Zone, in that player's own Standby Phase; the controller targets 1 face-down card
        /// the opponent controls (a human picks among 2+; the Standby Phase waits) and looks at it.
        /// </summary>
        public static void ResolveLookAtSetCard(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance chosenTarget)
        {
            if (engine == null || who == null || clause == null) return;
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            if (clause.Timing == EffectTiming.StandbyPhase)
            {
                if (source == null || !source.FaceUp || !who.TryFindMonster(source, out _) || source.IsNegated) return;
                if (!ReferenceEquals(engine.TurnPlayer, who)) return;
                var now = OpponentSetCards(engine, who, clause, source);
                if (now.Count == 0)
                {
                    engine.Log($"{source.Name}: {opp.Name} has no Set card.");
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                    () => OpponentSetCards(engine, who, clause, source),
                    EffectTargetKind.AnyCardOnField,
                    "select 1 Set card your opponent controls to look at.",
                    list => TextEffectRuntime.AutoPick(clause, list, who, engine),
                    picked => LookAtSetCard(engine, who, source, picked));
                return;
            }

            LookAtSetCard(engine, who, source, chosenTarget);
        }

        static void LookAtSetCard(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target)
        {
            var opp = engine.OpponentOf(who);
            if (target == null || opp == null) return;
            var onField = opp.TryFindMonster(target, out _) || opp.TryFindSpellTrap(target, out _) ||
                          ReferenceEquals(opp.FieldSpellZone?.Occupant, target);
            if (!onField || target.FaceUp)
            {
                engine.Log($"{source?.Name}: the target is no longer a Set card — nothing is looked at.");
                return;
            }

            LogLook(engine, who, source, $"a Set card {opp.Name} controls", new[] { target });
            engine.Log($"{source?.Name}: it returns to its original position.");
        }

        // ───────────────────────────── Trap Dustshoot ─────────────────────────────

        static List<CardInstance> HandMonsters(DuelistState side) =>
            side?.Hand?.Where(h => h?.Def != null && h.Def.IsMonster).ToList() ?? new List<CardInstance>();

        /// <summary>
        /// Trap Dustshoot: <paramref name="who"/> looks at the opponent's whole hand, chooses 1 monster in it (a human picks
        /// among 2+; the AI takes the highest ATK) and it is shuffled into its owner's Deck. No monster: nothing else happens.
        /// </summary>
        public static void ResolveHandMonsterToDeck(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp?.Hand == null) return;
            if (opp.Hand.Count == 0)
            {
                engine.Log($"{source?.Name}: {opp.Name} has no card in hand.");
                return;
            }

            LogLook(engine, who, source, $"{opp.Name}'s hand", opp.Hand);
            if (HandMonsters(opp).Count == 0)
            {
                engine.Log($"{source?.Name}: no monster in {opp.Name}'s hand — nothing is returned.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                () => HandMonsters(opp),
                EffectTargetKind.CardInOppHand,
                "select 1 monster in your opponent's hand to return to the Deck.",
                list => list.OrderByDescending(m => m.Def?.atk ?? 0).ThenByDescending(m => m.Def?.level ?? 0)
                    .FirstOrDefault(),
                picked => ReturnHandCardToDeck(engine, opp, source, picked));
        }

        static void ReturnHandCardToDeck(DuelEngine engine, DuelistState opp, CardInstance source, CardInstance card)
        {
            if (card == null || opp?.Hand == null || !opp.Hand.Remove(card)) return;
            opp.Deck.Add(card.CardId);
            engine.Rng.Shuffle(opp.Deck);
            engine.Log($"{source?.Name}: {card.Name} returns to {opp.Name}'s Deck, which is shuffled.");
        }

        // ───────────────────────────── Wandering Mummy ─────────────────────────────

        /// <summary>
        /// Wandering Mummy, after it was flipped face-down: every face-down monster in <paramref name="who"/>'s Main Monster
        /// Zones swaps zones at random (they stay face-down Defense Position).
        /// </summary>
        public static void ShuffleFaceDownMainZoneMonsters(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who?.MonsterZones == null) return;
            var faceDown = new List<CardInstance>();
            for (var i = 0; i < who.MonsterZones.Length && i < TcgRules.MonsterZones; i++)
            {
                var occ = who.MonsterZones[i]?.Occupant;
                if (occ != null && !occ.FaceUp) faceDown.Add(occ);
            }

            if (faceDown.Count < 2)
            {
                engine.Log($"{source?.Name}: no other face-down monster to rearrange.");
                return;
            }

            TextEffectRuntime.ShuffleFaceDownMonsters(engine, who, faceDown);
            engine.Log($"{source?.Name}: {who.Name}'s {faceDown.Count} face-down monsters are rearranged.");
        }

        // ───────────────────────────── battle: 8-Claws Scorpion, Giant Axe Mummy ─────────────────────────────

        static bool LiveClauses(CardInstance card, EffectActionKind action, out List<EffectClause> clauses)
        {
            clauses = null;
            if (card?.Def == null || !card.FaceUp || card.IsNegated) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            clauses = prog.ClauseList.Where(c => c != null && c.Action == action).ToList();
            return clauses.Count > 0;
        }

        /// <summary>
        /// DuelEngine.FinishDeferredDamageCalculation, once this Damage Step's own ATK bonuses are set and before damage is
        /// calculated. Notes the battle (Giant Axe Mummy), then: the attacker with "its ATK becomes N" against a monster whose
        /// battle position was face-down Defense (<paramref name="flippedByBattle"/> is the target that was Set when the
        /// Damage Step started) has that ATK for this damage calculation (ygopro EFFECT_SET_ATTACK_FINAL: earlier
        /// modifiers no longer count; the engine's damage-step bonus makes up the difference and is cleared after the
        /// calculation).
        /// </summary>
        public static void ApplyDamageCalculationAtk(DuelEngine engine, CardInstance attacker, CardInstance target,
            CardInstance flippedByBattle)
        {
            if (engine == null) return;
            var note = s_battle.GetOrCreateValue(engine);
            var faceDown = target != null && ReferenceEquals(flippedByBattle, target);
            note.Attacker = attacker;
            note.AttackerEntry = attacker?.FieldEntryStamp ?? 0;
            note.FaceDownTarget = faceDown ? target : null;
            note.TargetEntry = faceDown ? target.FieldEntryStamp : 0;
            // A later "ATK becomes 0 during this damage calculation" (Suijin, activated in the damage calculation window
            // after this effect) wins; CardInstance.CurrentAtk already gives it priority.
            if (!faceDown || attacker == null || attacker.AtkBecomesZeroThisCalculation ||
                attacker.AtkZeroUntilEndOfBattlePhase)
                return;
            if (!LiveClauses(attacker, EffectActionKind.AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation,
                    out var clauses))
                return;
            var value = clauses[clauses.Count - 1].Amount;
            if (!RawAtkWithoutDamageStepBonus(attacker, out var others)) return;
            // CurrentAtk = (others + DamageStepAtkBonus) halved once per Mirror Wall mark: pick the bonus that lands on value.
            var halvings = System.Math.Min(attacker.AtkHalvings, 20);
            var sumWanted = halvings > 0 ? value << halvings : value;
            attacker.DamageStepAtkBonus = sumWanted - others;
            engine.Log($"{attacker.Name} attacks a face-down Defense Position monster: its ATK becomes {value} " +
                       $"during this damage calculation ({attacker.CurrentAtk}).");
        }

        /// <summary>
        /// The sum CardInstance.CurrentAtk halves and clamps (original ATK + every modifier), without this Damage Step's
        /// bonus. False for a "?" ATK monster (its ATK is 0 whatever the modifiers).
        /// </summary>
        static bool RawAtkWithoutDamageStepBonus(CardInstance m, out int raw)
        {
            const int Lift = 1_000_000; // keeps the sum above the 0 clamp
            var halvings = m.AtkHalvings;
            var bonus = m.DamageStepAtkBonus;
            m.AtkHalvings = 0;
            m.DamageStepAtkBonus = Lift;
            var lifted = m.CurrentAtk;
            m.AtkHalvings = halvings;
            m.DamageStepAtkBonus = bonus;
            raw = lifted - Lift;
            return lifted > 0;
        }

        /// <summary>
        /// Giant Axe Mummy (EndOfDamageStep, from TextEffectRuntime.NotifyEndOfDamageStep): <paramref name="source"/> was the
        /// attack target in face-down Defense Position this battle and the attacker's ATK is lower than its DEF → that
        /// attacker is destroyed, if it is still on the field (it still relates to the battle).
        /// </summary>
        public static void ResolveDestroyLowerAtkAttacker(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance attacker)
        {
            if (engine == null || who == null || source == null || attacker == null) return;
            if (!s_battle.TryGetValue(engine, out var note) || !ReferenceEquals(note.FaceDownTarget, source) ||
                !ReferenceEquals(note.Attacker, attacker) || source.FieldEntryStamp != note.TargetEntry)
                return;
            if (source.IsNegated || !who.TryFindMonster(source, out _)) return;
            var opp = engine.OpponentOf(who);
            if (opp == null || !opp.TryFindMonster(attacker, out _) || attacker.FieldEntryStamp != note.AttackerEntry)
            {
                engine.Log($"{source.Name}: the attacking monster is no longer on the field.");
                return;
            }

            var atk = attacker.CurrentAtk;
            var def = source.CurrentDef;
            if (atk >= def) return;
            engine.Log($"{source.Name}: {attacker.Name}'s ATK ({atk}) is lower than its DEF ({def}) — the attacking " +
                       "monster is destroyed.");
            TextEffectRuntime.DestroyCard(engine, attacker, source, byPlayer: who);
        }
    }
}
