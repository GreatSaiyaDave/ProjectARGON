using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LastTurn batch (v163, LOD) — Last Turn (c28566710) and the duel-ending pieces it is built from, each usable on its own:
    /// <list type="bullet">
    /// <item><see cref="SendAllCardsOnFieldAndInHandsToGy"/>: every card on both fields and in both hands (but one) to its
    /// owner's GY as ONE event (ygopro one SendtoGrave of a group): GY triggers form one SEGOC Chain once play is idle.</item>
    /// <item><see cref="AskOpponentDeckSummon"/>: a player selects 1 monster in their own Deck that can be Special Summoned
    /// (a human through the existing Deck pick, the AI by highest ATK) and Special Summons it; the Deck is shuffled.</item>
    /// <item><see cref="QueueForcedAttack"/>: ygopro Duel.ForceAttack — the battle starts once the Chain (and the triggers it
    /// left) is over, through <see cref="DuelEngine.BeginForcedAttack"/>; optionally with no battle damage for either player
    /// (EFFECT_AVOID_BATTLE_DAMAGE, both players, until the end of that Damage Step).</item>
    /// <item><see cref="ScheduleLoneMonsterCheck"/>: at the End Phase of this turn, the player who alone controls monsters wins
    /// the Duel; otherwise it is a DRAW (<see cref="DuelEngine.EndGameByCardEffect"/> with no winner).</item>
    /// </list>
    /// Hooks: TextEffectRuntime.ApplyClause, TextEffectRuntime.ActivationConditionsMet (<see cref="TryActivationCondition"/>),
    /// DuelEngine.Notify (<see cref="OnPlayIdle"/>), DuelEngine.EndTurn (<see cref="OnEndPhase"/>), SpellTrapEffects.AiAutoRespond
    /// (<see cref="AiWantsLastTurn"/>).
    /// </summary>
    public static class LastTurnRuntime
    {
        /// <summary>
        /// ActivationCondition: "during your opponent's turn when your Life Points are N or less" (Lua GetLP(tp) &lt;= N and
        /// GetTurnPlayer() ~= tp; N = <see cref="EffectClause.ConditionAmount"/>). On a
        /// <see cref="EffectActionKind.SelectedMonsterShowdownLoneMonsterWins"/> clause it also needs a monster you control
        /// (the Lua target check).
        /// </summary>
        public const string OpponentTurnYourLpAtMost = "OpponentTurnYourLpAtMost";

        sealed class ForcedBattle
        {
            public DuelistState Who;
            public CardInstance Attacker;
            public int AttackerEntry;
            public CardInstance Target;
            public int TargetEntry;
            public CardInstance Source;
            public bool NoBattleDamage;
        }

        sealed class EndPhaseCheck
        {
            public int Turn;
            public DuelistState TurnPlayer;
            public CardInstance Source;
        }

        sealed class State
        {
            public readonly List<ForcedBattle> Queued = new List<ForcedBattle>();
            public ForcedBattle Running;
            public readonly List<EndPhaseCheck> Checks = new List<EndPhaseCheck>();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new ConditionalWeakTable<DuelEngine, State>();

        static State StateOf(DuelEngine engine) => s_state.GetOrCreateValue(engine);

        // ───────────────────────────── activation ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet (default chain): <see cref="OpponentTurnYourLpAtMost"/>.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause clause, out bool ok,
            out string reason)
        {
            ok = false;
            reason = null;
            if (clause == null || clause.ActivationCondition != OpponentTurnYourLpAtMost) return false;
            var limit = clause.ConditionAmount > 0 ? clause.ConditionAmount : 1000;
            ok = engine != null && who != null && engine.TurnPlayer != null && !ReferenceEquals(engine.TurnPlayer, who) &&
                 who.LifePoints <= limit;
            reason = $"Only during your opponent's turn while you have {limit} or less LP.";
            if (ok && clause.Action == EffectActionKind.SelectedMonsterShowdownLoneMonsterWins && who.MonsterCount == 0)
            {
                ok = false;
                reason = "You must control a monster to select.";
            }

            return true;
        }

        /// <summary>
        /// AI (SpellTrapEffects.AiAutoRespond, the opponent-turn open window and the attack-declared window): Last Turn turns
        /// a lost position into a battle — the AI activates it (only offered while legal) when the opponent threatens to
        /// take its last LP: the declared attack would, or (open window) a face-up Attack Position monster of theirs has ATK
        /// at least the AI's LP.
        /// </summary>
        public static bool AiWantsLastTurn(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled ||
                !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SelectedMonsterShowdownLoneMonsterWins))
                return false;
            if (ai.MonsterCount == 0) return false;
            var pr = engine.PendingResponse;
            var opp = engine.OpponentOf(ai);
            if (pr != null && pr.Timing == ResponseTiming.AttackDeclared && pr.Attacker != null)
            {
                var atk = pr.Attacker.CurrentAtk;
                var target = engine.DeclaredAttackTarget;
                if (target == null) return atk >= ai.LifePoints;
                return target.FaceUp && target.Position == BattlePosition.Attack && atk - target.CurrentAtk >= ai.LifePoints;
            }

            return opp != null && opp.MonstersOnField().Any(m =>
                m != null && m.FaceUp && m.Position == BattlePosition.Attack && m.CurrentAtk >= ai.LifePoints);
        }

        // ───────────────────────────── Last Turn ─────────────────────────────

        /// <summary>
        /// Last Turn resolves for <paramref name="who"/>: they select 1 monster they control (a human picks among 2+; the AI
        /// keeps its strongest), every other card on the field and in both hands goes to the GY, the End Phase check of this
        /// turn is registered, then the opponent Special Summons 1 monster from their Deck, which attacks the selected one with
        /// no battle damage. No monster to select (it left before resolution): everything is sent; no attack.
        /// </summary>
        public static void ResolveShowdown(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                () => who.MonstersOnField().Where(m => m != null).ToList(),
                EffectTargetKind.YourMonster,
                "select 1 monster you control to keep — every other card on the field and in both hands is sent to the GY.",
                AiKeep,
                kept => AfterSelect(engine, who, source, kept));
        }

        static void AfterSelect(DuelEngine engine, DuelistState who, CardInstance source, CardInstance kept)
        {
            if (engine.GameOver) return;
            if (kept != null && !who.TryFindMonster(kept, out _)) kept = null;
            var keptEntry = kept?.FieldEntryStamp ?? 0;
            engine.Log(kept == null
                ? $"{source?.Name}: {who.Name} controls no monster to select."
                : $"{source?.Name}: {who.Name} selects {(kept.FaceUp ? kept.Name : "a face-down monster")}.");
            SendAllCardsOnFieldAndInHandsToGy(engine, source, kept);
            if (engine.GameOver) return;
            ScheduleLoneMonsterCheck(engine, source);
            var opp = engine.OpponentOf(who);
            AskOpponentDeckSummon(engine, opp, source,
                "select 1 monster in your Deck to Special Summon in face-up Attack Position — it attacks the monster your " +
                "opponent selected (no battle damage).",
                summoned =>
                {
                    if (summoned == null || kept == null) return;
                    QueueForcedAttack(engine, opp, summoned, kept, keptEntry, source, noBattleDamage: true);
                });
        }

        /// <summary>The AI keeps the monster that holds best against an unknown attacker: ATK in Attack Position, else DEF.</summary>
        static CardInstance AiKeep(List<CardInstance> mine) =>
            mine?.OrderByDescending(m => m.FaceUp && m.Position == BattlePosition.Attack ? m.CurrentAtk : m.CurrentDef)
                .ThenByDescending(m => m.CurrentAtk).FirstOrDefault();

        // ───────────────────────────── shared pieces ─────────────────────────────

        /// <summary>
        /// Every card on both fields (Monster, Spell &amp; Trap, Field and Pendulum Zones) and in both hands, except
        /// <paramref name="except"/> and <paramref name="source"/> (a resolving Spell/Trap goes to the GY with its Chain Link),
        /// is sent to its owner's GY by <paramref name="source"/>'s effect as one event. Cards unaffected by
        /// <paramref name="source"/> stay. Links between two leaving cards are dropped first (Fiber Jar precedent), so no Equip
        /// Card or Call of the Haunted is destroyed because its partner left a moment earlier. A Token ceases to exist.
        /// Returns how many cards were sent.
        /// </summary>
        public static int SendAllCardsOnFieldAndInHandsToGy(DuelEngine engine, CardInstance source, CardInstance except)
        {
            if (engine == null || engine.GameOver) return 0;
            var sides = new[] { engine.Player, engine.Opponent }.Where(s => s != null).ToList();
            var field = new List<CardInstance>();
            foreach (var side in sides)
            {
                var onField = side.MonstersOnField().Concat(side.SpellTrapsOnField()).ToList();
                if (side.PendulumZones != null)
                    foreach (var z in side.PendulumZones)
                        if (z?.Occupant != null && !onField.Contains(z.Occupant))
                            onField.Add(z.Occupant);
                foreach (var c in onField)
                {
                    if (c == null || ReferenceEquals(c, except) || ReferenceEquals(c, source) || field.Contains(c)) continue;
                    if (ContinuousProtections.IsUnaffectedBy(engine, c, source))
                    {
                        engine.Log($"{c.Name} is unaffected by {source?.Name} — it stays on the field.");
                        continue;
                    }

                    field.Add(c);
                }
            }

            var hands = sides.ToDictionary(s => s,
                s => s.Hand.Where(h => h != null && !ReferenceEquals(h, source)).ToList());

            foreach (var c in field)
            {
                var host = c.EquippedTo;
                if (host == null || !field.Contains(host)) continue;
                host.Equips.Remove(c);
                c.EquippedTo = null;
            }

            var sent = 0;
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var c in field)
                {
                    var controller = engine.ControllerOf(c);
                    if (controller == null) continue;
                    engine.SendCardToGrave(controller, c, source);
                    if (c.IsToken)
                        foreach (var side in sides)
                            side.Graveyard.Remove(c);
                    sent++;
                }

                foreach (var side in sides)
                foreach (var h in hands[side])
                {
                    if (!side.Hand.Contains(h)) continue;
                    engine.SendCardToGrave(side, h, source);
                    sent++;
                }
            });

            engine.Log($"{source?.Name}: {sent} card(s) on the field and in the hands are sent to the GY.");
            engine.NotifyPublic();
            return sent;
        }

        /// <summary>
        /// <paramref name="chooser"/> selects 1 monster in their Deck that can be Special Summoned (not an Extra Deck monster;
        /// <see cref="SpecialSummonLegality.CanBeSpecialSummoned"/> from the Deck) and Special Summons it in face-up Attack
        /// Position; the Deck is shuffled. A human picks through the Deck pick (MonsterInYourDeckToSummon; Cancel takes the
        /// AI's choice); the AI takes the highest ATK. <paramref name="then"/> runs with the summoned monster (null: none).
        /// </summary>
        public static void AskOpponentDeckSummon(DuelEngine engine, DuelistState chooser, CardInstance source, string prompt,
            System.Action<CardInstance> then)
        {
            if (engine == null || chooser?.Deck == null || engine.GameOver)
            {
                then?.Invoke(null);
                return;
            }

            if (engine.FirstEmpty(chooser.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: {chooser.Name} has no free Monster Zone — nothing is Special Summoned.");
                then?.Invoke(null);
                return;
            }

            if (DeckSummonCandidates(engine, chooser, source).Count == 0)
            {
                engine.Log($"{source?.Name}: {chooser.Name} has no monster in the Deck that can be Special Summoned.");
                then?.Invoke(null);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, chooser, source,
                () => DeckSummonCandidates(engine, chooser, source),
                EffectTargetKind.MonsterInYourDeckToSummon,
                prompt,
                list => list.OrderByDescending(c => c.Def?.atk ?? 0).ThenByDescending(c => c.Def?.def ?? 0)
                    .FirstOrDefault(),
                picked => then?.Invoke(SummonFromDeck(engine, chooser, source, picked)));
        }

        static List<CardInstance> DeckSummonCandidates(DuelEngine engine, DuelistState owner, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in owner.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!def.IsMonster || def.IsExtraDeck) continue;
                var inst = engine.CreateCardInstance(id);
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, owner, inst, source, SummonOrigin.Deck))
                    continue;
                list.Add(inst);
            }

            return list;
        }

        static CardInstance SummonFromDeck(DuelEngine engine, DuelistState owner, CardInstance source, CardInstance pick)
        {
            if (pick == null || engine.GameOver) return null;
            var index = owner.Deck.IndexOf(pick.CardId);
            if (index < 0)
            {
                engine.Log($"{source?.Name}: {pick.Name} is no longer in the Deck.");
                return null;
            }

            owner.Deck.RemoveAt(index);
            CardInstance summoned = null;
            if (!engine.SpecialSummonToField(owner, pick, BattlePosition.Attack, true))
            {
                owner.Deck.Insert(index, pick.CardId);
                engine.Log($"{source?.Name}: the Special Summon of {pick.Name} failed.");
            }
            else
            {
                summoned = pick;
                engine.Log($"{source?.Name}: {owner.Name} Special Summons {pick.Name} from the Deck in face-up Attack Position.");
            }

            engine.Rng.Shuffle(owner.Deck);
            engine.Log($"{owner.Name} shuffles their Deck.");
            return summoned;
        }

        /// <summary>
        /// ygopro Duel.ForceAttack: <paramref name="attacker"/> (controlled by <paramref name="who"/>) attacks
        /// <paramref name="target"/> once play is idle — the current Chain and the triggers it left have resolved
        /// (<see cref="OnPlayIdle"/>). <paramref name="noBattleDamage"/>: neither player takes battle damage from that battle.
        /// Either card gone (or no longer the same card on the field) by then: no attack.
        /// </summary>
        public static void QueueForcedAttack(DuelEngine engine, DuelistState who, CardInstance attacker,
            CardInstance target, int targetEntry, CardInstance source, bool noBattleDamage)
        {
            if (engine == null || who == null || attacker == null || target == null) return;
            StateOf(engine).Queued.Add(new ForcedBattle
            {
                Who = who,
                Attacker = attacker,
                AttackerEntry = attacker.FieldEntryStamp,
                Target = target,
                TargetEntry = targetEntry,
                Source = source,
                NoBattleDamage = noBattleDamage
            });
        }

        /// <summary>
        /// DuelEngine.Notify, once play is idle (no Chain, window, pick, battle or queued trigger Chain): a forced battle that
        /// is over hands the duel back to the phase it interrupted (<see cref="DuelEngine.EndForcedBattle"/>); a queued one
        /// starts.
        /// </summary>
        public static void OnPlayIdle(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            if (st.Running != null)
            {
                var done = st.Running;
                st.Running = null;
                if (done.NoBattleDamage) ClearNoBattleDamage(engine, done);
                engine.EndForcedBattle();
            }

            while (st.Queued.Count > 0 && st.Running == null && !engine.GameOver)
            {
                var fb = st.Queued[0];
                st.Queued.RemoveAt(0);
                var opp = engine.OpponentOf(fb.Who);
                var attackerThere = fb.Who.TryFindMonster(fb.Attacker, out _) &&
                                    fb.Attacker.FieldEntryStamp == fb.AttackerEntry;
                var targetThere = opp != null && opp.TryFindMonster(fb.Target, out _) &&
                                  fb.Target.FieldEntryStamp == fb.TargetEntry;
                if (!attackerThere || !targetThere || !engine.CanBattleByEffect(fb.Who, fb.Attacker))
                {
                    engine.Log($"{fb.Source?.Name}: the attack does not happen — " +
                               (!attackerThere || !targetThere ? "a monster of that battle left the field." :
                                   $"{fb.Attacker.Name} cannot attack."));
                    continue;
                }

                st.Running = fb;
                if (fb.NoBattleDamage)
                {
                    fb.Who.PreventBattleDamageThisBattle = true;
                    opp.PreventBattleDamageThisBattle = true;
                }

                engine.Log($"{fb.Source?.Name}: {fb.Attacker.Name} must attack " +
                           $"{(fb.Target.FaceUp ? fb.Target.Name : "the face-down monster")}" +
                           (fb.NoBattleDamage ? " (any battle damage from this battle is 0)." : "."));
                if (engine.BeginForcedAttack(fb.Who, fb.Attacker, fb.Target)) return;
                // Refused (nothing was declared): nothing to wait for.
                if (ReferenceEquals(st.Running, fb))
                {
                    st.Running = null;
                    if (fb.NoBattleDamage) ClearNoBattleDamage(engine, fb);
                    engine.EndForcedBattle();
                }
            }
        }

        static void ClearNoBattleDamage(DuelEngine engine, ForcedBattle fb)
        {
            if (fb.Who != null) fb.Who.PreventBattleDamageThisBattle = false;
            var opp = engine.OpponentOf(fb.Who);
            if (opp != null) opp.PreventBattleDamageThisBattle = false;
        }

        /// <summary>
        /// "The player whose monster remains alone on the field at the End Phase of this turn wins the Duel. Any other case
        /// results in a DRAW." — checked once, at this turn's End Phase (<see cref="OnEndPhase"/>).
        /// </summary>
        public static void ScheduleLoneMonsterCheck(DuelEngine engine, CardInstance source)
        {
            if (engine == null) return;
            StateOf(engine).Checks.Add(new EndPhaseCheck
                { Turn = engine.TurnNumber, TurnPlayer = engine.TurnPlayer, Source = source });
        }

        /// <summary>
        /// DuelEngine.EndTurn, as the End Phase begins (a continuous effect applies before the End Phase's triggers), and
        /// again before the turn passes (a check registered during the End Phase itself): this turn's lone-monster check.
        /// </summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st) || st.Checks.Count == 0)
                return;
            var due = st.Checks.FirstOrDefault(c => c.Turn == engine.TurnNumber && ReferenceEquals(c.TurnPlayer, engine.TurnPlayer));
            st.Checks.RemoveAll(c => c.Turn <= engine.TurnNumber);
            if (due == null) return;
            var mine = engine.Player?.MonsterCount ?? 0;
            var theirs = engine.Opponent?.MonsterCount ?? 0;
            var name = due.Source?.Name ?? "Last Turn";
            if (mine > 0 && theirs == 0)
                engine.EndGameByCardEffect(engine.Player, $"{name} — only their monster remains on the field at the End Phase");
            else if (theirs > 0 && mine == 0)
                engine.EndGameByCardEffect(engine.Opponent, $"{name} — only their monster remains on the field at the End Phase");
            else
                engine.EndGameByCardEffect(null, mine == 0
                    ? $"{name}: no monster remains on the field at the End Phase — the Duel is a DRAW."
                    : $"{name}: monsters remain on both sides of the field at the End Phase — the Duel is a DRAW.");
        }
    }
}
