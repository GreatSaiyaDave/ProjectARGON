using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LpLocksDelayedBurnAndGyNegation batch (AST) — runtime for <see cref="LpLocksDelayedBurnAndGyNegationAtoms"/>:
    /// <list type="bullet">
    /// <item><b>Wall of Revealing Light</b> (c17078030): its activation cost is any multiple of N LP up to the LP (Lua
    /// AnnounceNumber + PayLPCost). A human chooses the amount before the card is placed (<see cref="BeginPaidLpChoice"/>
    /// from DuelEngine.TryActivateSpellTrapCore; PAY re-enters the activation, CANCEL = not activated); the AI chooses
    /// enough to lock the opponent's strongest face-up monster without paying its last LP. The amount is paid as the link
    /// is created (<see cref="PayOnActivation"/>) and belongs to this copy's field presence; the field lock
    /// (<see cref="PaidLpLockBlocks"/>) reads the attacker's CURRENT ATK each time it is asked, so a negated or face-down
    /// Wall locks nothing (DuelEngine.ContinuousCannotAttackBlocks skips it).</item>
    /// <item><b>Weapon Change</b> (c10035717): at its controller's Standby Phase, once per copy and turn, an optional
    /// trigger (<see cref="TryFirePhaseClause"/>): pay N LP, target 1 face-up monster of the listed Types you control; its
    /// ATK becomes its current DEF and its DEF its current ATK (taken at resolution) until the end of the opponent's next
    /// turn; it also ends when the monster leaves the field or is turned face-down (RESETS_STANDARD).</item>
    /// <item><b>Dora of Fate</b> (c67464807): resolution records the target's Level L as a player effect of its
    /// controller (<see cref="ArmDora"/>); the first Normal Summon (not a Set) of a Level L-1 monster by that player during
    /// their turn, before the end of their next turn, inflicts L x N damage to the opponent
    /// (<see cref="OnNormalSummonSuccess"/>, DuelEngine.CompleteNormalSummon; not a Chain Link). Several stack.</item>
    /// </list>
    /// </summary>
    public static class LpLocksDelayedBurnAndGyNegationRuntime
    {
        // ───────────────────────────── shared state ─────────────────────────────

        sealed class State
        {
            public readonly List<SwitchRecord> Switches = new();
            public readonly List<DoraRecord> Doras = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── Wall of Revealing Light ─────────────────────────────

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> of a paid-LP lock (Lua s.cost chk: CheckLPCost(tp, N)). The AI
        /// also needs more than N LP: it never pays its last LP.
        /// </summary>
        public const string PaidLpMultipleAffordable = "LldPaidLpMultipleAffordable";

        sealed class Paid
        {
            public int Amount;
            public int Stamp;
        }

        sealed class Chosen
        {
            public int Amount;
        }

        sealed class Ask
        {
            public DuelistState Who;
            public CardInstance Card;
            public bool FromHand;
        }

        static readonly ConditionalWeakTable<CardInstance, Paid> s_paid = new();
        static readonly ConditionalWeakTable<CardInstance, Chosen> s_chosen = new();
        static readonly ConditionalWeakTable<PendingActivation, Ask> s_asks = new();

        /// <summary>The paid-LP lock clause of a FullyCompiled program, or null.</summary>
        static EffectClause PaidLpLockClause(CompiledCardProgram prog)
        {
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.CannotAttackIfAtkAtMostPaidLp && c.RequiresLpCostMultiple > 0)
                    return c;
            return null;
        }

        static EffectClause PaidLpLockClause(CardInstance card) =>
            card?.Def != null && card.Def.IsTrap ? PaidLpLockClause(CompiledEffectCache.GetOrCompile(card.Def)) : null;

        /// <summary>Every legal payment: N, 2N, … up to the LP (Lua: for i = 1, floor(LP / N)).</summary>
        static List<int> Choices(DuelistState who, int multiple)
        {
            var list = new List<int>();
            if (who == null || multiple <= 0) return list;
            for (var a = multiple; a <= who.LifePoints; a += multiple)
                list.Add(a);
            return list;
        }

        /// <summary>
        /// The AI's payment: the smallest multiple that covers the opponent's strongest face-up monster, never its last LP
        /// (at most the largest multiple below its LP). 0 when no payment leaves it above 0.
        /// </summary>
        static int AiAmount(DuelEngine engine, DuelistState who, int multiple)
        {
            if (engine == null || who == null || multiple <= 0) return 0;
            var cap = (who.LifePoints - 1) / multiple * multiple;
            if (cap < multiple) return 0;
            var opp = engine.OpponentOf(who);
            var strongest = opp?.MonstersOnField().Where(m => m != null && m.FaceUp).Select(m => m.CurrentAtk)
                .DefaultIfEmpty(0).Max() ?? 0;
            var need = strongest <= 0 ? multiple : (strongest + multiple - 1) / multiple * multiple;
            return System.Math.Max(multiple, System.Math.Min(cap, need));
        }

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain: <see cref="PaidLpMultipleAffordable"/>.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || c.ActivationCondition != PaidLpMultipleAffordable) return false;
            var m = c.RequiresLpCostMultiple > 0 ? c.RequiresLpCostMultiple : 1000;
            ok = who != null && (engine != null && engine.IsHumanControlled(who)
                ? who.LifePoints >= m
                : who.LifePoints > m);
            reason = $"Needs at least {m} LP to pay.";
            return true;
        }

        /// <summary>
        /// DuelEngine.TryActivateSpellTrapCore, before anything is placed: a human activating a paid-LP lock chooses the
        /// amount first (PAY N re-enters the activation through <see cref="TryAnswerLpChoice"/>; CANCEL leaves the card
        /// Set). True when the choice is now awaited.
        /// </summary>
        public static bool BeginPaidLpChoice(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand)
        {
            if (engine == null || who == null || card == null || !engine.IsHumanControlled(who)) return false;
            var c = PaidLpLockClause(card);
            if (c == null || s_chosen.TryGetValue(card, out _) || engine.PendingActivation != null) return false;
            var choices = Choices(who, c.RequiresLpCostMultiple);
            if (choices.Count == 0) return false;
            var p = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                UsesTextProgram = true,
                AwaitingLpCost = true,
                PromptOverride = $"{card.Name}: pay any multiple of {c.RequiresLpCostMultiple} LP to activate it " +
                                 "(PAY N), or CANCEL."
            };
            p.LpCostChoices.AddRange(choices);
            s_asks.Add(p, new Ask { Who = who, Card = card, FromHand = fromHand });
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.TryFinishLpCost hook: the human chose the amount; the activation is made again with it (it is
        /// paid as the link is created). False when the pending choice is not this batch's.
        /// </summary>
        public static bool TryAnswerLpChoice(DuelEngine engine, int amount, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || !s_asks.TryGetValue(p, out var ask)) return false;
            if (!p.LpCostChoices.Contains(amount)) return true;
            s_asks.Remove(p);
            engine.ClearPendingActivation();
            s_chosen.Remove(ask.Card);
            s_chosen.Add(ask.Card, new Chosen { Amount = amount });
            try
            {
                ok = engine.TryActivateSpellTrap(ask.Who, ask.Card, ask.FromHand);
            }
            finally
            {
                s_chosen.Remove(ask.Card);
            }

            if (!ok) engine.Log($"{ask.Card.Name} is not activated — nothing is paid.");
            engine.NotifyPublic();
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: CANCEL at the amount = the card is not activated.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_asks.TryGetValue(p, out var ask)) return false;
            s_asks.Remove(p);
            engine.ClearPendingActivation();
            engine.Log($"{ask.Card?.Name}: not activated.");
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// Every site that seats a Spell/Trap activation as a Chain Link (SpellTrapEffects.BeginDeferredChainActivation,
        /// TextEffectRuntime.SeatResponseOnChain / ChainFreeChainLink): pay the paid-LP lock's cost as the card is
        /// activated — the human's chosen amount, otherwise the AI's — and record it on this field presence. Returns the LP
        /// paid (0 for any other card or a link already paid).
        /// </summary>
        public static int PayOnActivation(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link)
        {
            if (engine == null || who == null || card == null || link == null || link.ActivationLpCostPaid ||
                !ReferenceEquals(link.Card, card))
                return 0;
            var c = PaidLpLockClause(card);
            if (c == null) return 0;
            var m = c.RequiresLpCostMultiple;
            var amount = 0;
            if (s_chosen.TryGetValue(card, out var chosen))
            {
                amount = chosen.Amount;
                s_chosen.Remove(card);
            }

            if (amount <= 0 || amount % m != 0 || amount > who.LifePoints)
                amount = AiAmount(engine, who, m);
            if (amount <= 0) amount = m; // CanActivate required LP >= N
            link.ActivationLpCostPaid = true;
            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP for {card.Name} → {who.Name} at {System.Math.Max(0, who.LifePoints - amount)} LP.");
            s_paid.Remove(card);
            s_paid.Add(card, new Paid { Amount = amount, Stamp = card.FieldEntryStamp });
            engine.Log($"{card.Name}: {amount} LP paid — monsters your opponent controls with {amount} or less ATK " +
                       "cannot attack.");
            return amount;
        }

        /// <summary>The LP this copy paid for its current field presence (0 = none).</summary>
        public static int PaidAmount(CardInstance card) =>
            card != null && s_paid.TryGetValue(card, out var p) && p.Stamp == card.FieldEntryStamp ? p.Amount : 0;

        /// <summary>
        /// DuelEngine.ContinuousCannotAttackBlocks, for a face-up, un-negated <paramref name="lockCard"/> whose clause has
        /// <see cref="EffectClause.CannotAttackIfAtkAtMostPaidLp"/> (the caller checked the side): Lua s.atktarget
        /// c:GetAttack() &lt;= e:GetLabel().
        /// </summary>
        public static bool PaidLpLockBlocks(CardInstance lockCard, CardInstance attacker)
        {
            if (lockCard == null || attacker == null) return false;
            var paid = PaidAmount(lockCard);
            return paid > 0 && attacker.CurrentAtk <= paid;
        }

        // ───────────────────────────── Weapon Change ─────────────────────────────

        sealed class SwitchRecord
        {
            public CardInstance Target;
            public int Stamp;
            public int AtkDelta;
            public int DefDelta;
            public int LastTurn;
        }

        sealed class Used
        {
            public int Turn;
            public int Stamp;
        }

        static readonly ConditionalWeakTable<CardInstance, Used> s_used = new();

        static bool IsSwitchClause(EffectClause c) =>
            c != null && c.Action == EffectActionKind.SwitchTargetCurrentAtkDefUntilOpponentsNextEndPhase;

        static bool SourceLive(DuelistState who, CardInstance card, int stamp) =>
            card != null && card.FaceUp && !card.IsNegated && card.FieldEntryStamp == stamp &&
            who.TryFindSpellTrap(card, out _);

        static List<CardInstance> SwitchTargets(DuelistState who, EffectClause c) =>
            who.MonstersOnField()
                .Where(m => m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter) &&
                            (m.Def.type == null ||
                             m.Def.type.IndexOf("Link", System.StringComparison.OrdinalIgnoreCase) < 0))
                .ToList();

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor, after the turn filter and the Cold Wave lock (the card's own Standby
        /// Phase; a negated card fires nothing): true when <paramref name="c"/> is the Weapon Change clause (offered or
        /// not). Lua: TRIGGER_O, range SZONE, count limit 1, cost PayLP(N), target 1 face-up Warrior / Machine you
        /// control. A human taps the target (CANCEL = not used); the AI uses it when the switch raises a monster's ATK.
        /// It resolves at once (no Chain window: the engine's other Standby triggers resolve inline too).
        /// </summary>
        public static bool TryFirePhaseClause(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            EffectTiming timing)
        {
            if (!IsSwitchClause(c)) return false;
            if (engine == null || who == null || card?.Def == null || timing != EffectTiming.StandbyPhase) return true;
            if (!ReferenceEquals(engine.TurnPlayer, who)) return true;
            var stamp = card.FieldEntryStamp;
            if (!SourceLive(who, card, stamp)) return true;
            if (s_used.TryGetValue(card, out var used) && used.Turn == engine.TurnNumber && used.Stamp == stamp)
                return true;
            var cost = c.PayLpAmount;
            if (who.LifePoints < cost || SwitchTargets(who, c).Count == 0) return true;
            s_used.Remove(card);
            s_used.Add(card, new Used { Turn = engine.TurnNumber, Stamp = stamp });

            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => SourceLive(who, card, stamp) && who.LifePoints >= cost
                    ? SwitchTargets(who, c)
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.YourMonster,
                Prompt = $"{card.Name}: you can pay {cost} LP and target 1 face-up {c.RaceFilter?.Replace("|", " or ")} " +
                         "monster you control to switch its current ATK and DEF until the end of your opponent's next " +
                         "turn — tap it, or CANCEL.",
                AskWithOneCandidate = true,
                AiChoose = list => human || who.LifePoints <= cost
                    ? null
                    : list.Where(m => m.CurrentDef > m.CurrentAtk)
                        .OrderByDescending(m => m.CurrentDef - m.CurrentAtk)
                        .FirstOrDefault(),
                Cancel = () => engine.Log($"{card.Name}: {who.Name} does not use its effect."),
                HoldsChain = false,
                HoldsStandby = engine.Phase == DuelPhase.Standby,
                Finish = target => ResolveSwitch(engine, who, card, stamp, c, target)
            });
            return true;
        }

        static void ResolveSwitch(DuelEngine engine, DuelistState who, CardInstance card, int stamp, EffectClause c,
            CardInstance target)
        {
            if (target == null)
            {
                engine.Log($"{card.Name}: {who.Name} does not use its effect.");
                return;
            }

            if (!SourceLive(who, card, stamp) || who.LifePoints < c.PayLpAmount ||
                !SwitchTargets(who, c).Contains(target))
            {
                engine.Log($"{card.Name}: it can no longer be used — nothing is paid.");
                return;
            }

            engine.PayLifePoints(who, c.PayLpAmount,
                $"Cost: pay {c.PayLpAmount} LP for {card.Name} → {who.Name} at " +
                $"{System.Math.Max(0, who.LifePoints - c.PayLpAmount)} LP.");
            if (engine.GameOver) return;
            engine.Log($"{card.Name} targets {target.Name}.");
            // Resolution: Weapon Change still on the field (IsRelateToEffect) and the target face-up and related.
            if (!SourceLive(who, card, stamp) || !target.FaceUp || !who.TryFindMonster(target, out _))
            {
                engine.Log($"{card.Name}: the effect does not resolve.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, card))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            ApplySwitch(engine, target);
            engine.NotifyPublic();
        }

        /// <summary>
        /// SET_ATTACK_FINAL = the current DEF and SET_DEFENSE_FINAL = the current ATK, both read now: the difference is
        /// kept as a lasting modifier (later boosts add to it, as later-timestamp UPDATE effects do in ygopro) until the end
        /// of the opponent's next turn. A second switch of the same monster folds the first one into it (its values win).
        /// </summary>
        static void ApplySwitch(DuelEngine engine, CardInstance target)
        {
            var st = StateOf(engine);
            var atk = target.CurrentAtk;
            var def = target.CurrentDef;
            var rec = new SwitchRecord
            {
                Target = target, Stamp = target.FieldEntryStamp, LastTurn = engine.TurnNumber + 1
            };
            var earlier = st.Switches.Where(r => ReferenceEquals(r.Target, target) && r.Stamp == target.FieldEntryStamp)
                .ToList();
            foreach (var r in earlier)
            {
                rec.AtkDelta += r.AtkDelta;
                rec.DefDelta += r.DefDelta;
                st.Switches.Remove(r);
            }

            for (var i = 0; i < 4 && target.CurrentAtk != def; i++)
            {
                var d = def - target.CurrentAtk;
                target.LingeringAtkModifier += d;
                rec.AtkDelta += d;
            }

            for (var i = 0; i < 4 && target.CurrentDef != atk; i++)
            {
                var d = atk - target.CurrentDef;
                target.LingeringDefModifier += d;
                rec.DefDelta += d;
            }

            st.Switches.Add(rec);
            engine.Log($"{target.Name}'s ATK and DEF are switched until the end of your opponent's next turn " +
                       $"({target.CurrentAtk}/{target.CurrentDef}).");
        }

        static void EndSwitch(DuelEngine engine, SwitchRecord r, string why)
        {
            var t = r.Target;
            if (t == null || t.FieldEntryStamp != r.Stamp) return; // left the field: its modifiers were reset there
            t.LingeringAtkModifier -= r.AtkDelta;
            t.LingeringDefModifier -= r.DefDelta;
            engine.Log($"{t.Name}: Weapon Change's switch ends ({why}; {t.CurrentAtk}/{t.CurrentDef}).");
        }

        static bool OnField(DuelEngine engine, CardInstance card) =>
            card != null && (engine.Player.TryFindMonster(card, out _) || engine.Opponent.TryFindMonster(card, out _));

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: a switched monster that left the field, or is face-down now (RESET_TURN_SET),
        /// loses the switch; a switch past its last turn ends.
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || !s_state.TryGetValue(engine, out var st)) return;
            for (var i = st.Switches.Count - 1; i >= 0; i--)
            {
                var r = st.Switches[i];
                if (r.Target == null || r.Target.FieldEntryStamp != r.Stamp || !OnField(engine, r.Target))
                {
                    st.Switches.RemoveAt(i);
                    continue;
                }

                if (!r.Target.FaceUp)
                {
                    st.Switches.RemoveAt(i);
                    EndSwitch(engine, r, "it was turned face-down");
                    continue;
                }

                if (engine.TurnNumber > r.LastTurn)
                {
                    st.Switches.RemoveAt(i);
                    EndSwitch(engine, r, "its duration ended");
                }
            }
        }

        /// <summary>DuelEngine End Phase (next to PhaseTriggersAndIgnitionCostsRuntime.OnEndPhase): RESET_PHASE|PHASE_END,2.</summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            for (var i = st.Switches.Count - 1; i >= 0; i--)
            {
                var r = st.Switches[i];
                if (engine.TurnNumber < r.LastTurn) continue;
                st.Switches.RemoveAt(i);
                EndSwitch(engine, r, "the end of the turn");
            }

            st.Doras.RemoveAll(d => engine.TurnNumber >= d.LastTurn);
        }

        // ───────────────────────────── Dora of Fate ─────────────────────────────

        sealed class DoraRecord
        {
            public DuelistState Who;
            public int Level;
            public int PerLevel;
            public int LastTurn;
            public CardInstance Source;
        }

        static bool IsDoraClause(EffectClause c) =>
            c != null && c.Action == EffectActionKind.BurnWhenYouNormalSummonOneLevelLowerThanTargetNextTurn;

        /// <summary>TextEffectRuntime.CollectTargets (batch filters): Dora of Fate's target has a Level above 1 (s.filter).</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (!IsDoraClause(c) || list == null || c.MonsterLevelMin <= 0) return;
            list.RemoveAll(m => m == null || m.Level < c.MonsterLevelMin);
        }

        /// <summary>SpellTrapEffects.CollectLegalTargets (legacy mirror): the same filter for the card being activated.</summary>
        public static void FilterLegacyTargets(CardInstance activating, List<CardInstance> list)
        {
            if (activating?.Def == null || list == null || (!activating.Def.IsTrap && !activating.Def.IsSpell)) return;
            var prog = CompiledEffectCache.GetOrCompile(activating.Def);
            if (prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                FilterTargets(c, list);
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause: the target is still a face-up monster the opponent controls with Level &gt; 1, so
        /// its Level is recorded for its controller until the end of their next turn (Lua EFFECT_TYPE_CONTINUOUS
        /// EVENT_SUMMON_SUCCESS, count 1, RESET_PHASE|PHASE_END,2).
        /// </summary>
        public static void ArmDora(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance target)
        {
            if (engine == null || who == null || c == null) return;
            var opp = engine.OpponentOf(who);
            if (target == null || !target.FaceUp || opp == null || !opp.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name}: the selected monster is gone — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var lv = target.Level;
            if (lv <= 1)
            {
                engine.Log($"{source?.Name}: {target.Name} is not above Level 1 — no effect.");
                return;
            }

            StateOf(engine).Doras.Add(new DoraRecord
            {
                Who = who, Level = lv, PerLevel = c.Amount > 0 ? c.Amount : 500, LastTurn = engine.TurnNumber + 1,
                Source = source
            });
            engine.Log($"{source?.Name}: if {who.Name} Normal Summons a Level {lv - 1} monster during their next turn, " +
                       $"{opp.Name} takes {lv * (c.Amount > 0 ? c.Amount : 500)} damage.");
        }

        /// <summary>
        /// DuelEngine.CompleteNormalSummon (a Normal or Tribute Summon that succeeded, never a Set): each live Dora of Fate
        /// record of the summoner whose Level is 1 above the summoned monster's fires once, during the summoner's turn.
        /// </summary>
        public static void OnNormalSummonSuccess(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Doras.RemoveAll(d => engine.TurnNumber > d.LastTurn);
            if (st.Doras.Count == 0 || !ReferenceEquals(engine.TurnPlayer, who)) return;
            foreach (var d in st.Doras.ToList())
            {
                if (!ReferenceEquals(d.Who, who) || card.Level != d.Level - 1) continue;
                st.Doras.Remove(d);
                var opp = engine.OpponentOf(who);
                var dmg = d.Level * d.PerLevel;
                engine.Log($"{d.Source?.Name ?? "Dora of Fate"}: {card.Name} is Level {card.Level} — {opp.Name} takes " +
                           $"{dmg} damage.");
                engine.ApplyEffectDamage(opp, dmg, d.Source?.Name ?? "Dora of Fate", d.Source);
                if (engine.GameOver) return;
            }
        }
    }
}
