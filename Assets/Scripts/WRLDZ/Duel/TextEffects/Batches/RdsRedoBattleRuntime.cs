using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// RdsRedoBattle batch runtime (atoms: <see cref="RdsRedoBattleAtoms"/>). The v208 QC failed these cards closed
    /// because their runtimes were hollow or wrong; each piece below follows the card's official Lua script.
    /// <list type="bullet">
    /// <item><b>Negate only</b> (<see cref="TryNegateSpellTargetingLink"/>, DuelEngine.ResolveChainStack right after
    /// Freed's NegationMonstersRuntime.TryNegateTargetingLink): ygopro EVENT_CHAIN_SOLVING + Duel.NegateEffect. Silent
    /// Swordsman LV3 answers its opponent's Spell effects that target it; Rare Metalmorph answers the first Spell effect
    /// (either player's) that targets the monster it is linked to, once per stay on the field (e:Reset()). The Spell is
    /// not destroyed (Freed's answer destroys it).</item>
    /// <item><b>Silent Swordsman LV5</b>: <see cref="SourceIsOpponents"/> gates its UnaffectedByCardEffects clause
    /// (ContinuousProtections.IsUnaffectedBy); <see cref="NoteBattleDamage"/> records its direct attack's battle damage
    /// (EVENT_BATTLE_DAMAGE flag, RESET_SELF_TURN count 2) and <see cref="NextStandbyAfterDirectBattleDamage"/> opens its
    /// Standby level-up in its controller's next turn, while it stays face-up on the field.</item>
    /// <item><b>Mystic Swordsman LV6</b> (<see cref="DestroyAttackedFaceDownMayPlaceOnDeckTop"/>, from
    /// TextEffectRuntime.TryStartOfDamageStepDestroy): the attacked face-down monster is destroyed; when the opponent owns
    /// it, the controller may place it on top of that Deck instead (DuelEngine.DestroyMonster's deck-top branch).</item>
    /// <item><b>Sasuke Samurai #4</b> (<see cref="TryBeginBattleStartCoinCalls"/>, DuelEngine.ResolveDeclaredAttack before
    /// the face-down flip): attacking or attacked (face-up), its controller calls a coin toss (a human through
    /// PendingActivation.AwaitingCoinCall; the toss is a CoinDiceInterrupts event); a right call destroys the battle
    /// partner if it is still on the field. The Damage Step then goes on (DuelEngine.ContinueStartOfDamageStep).</item>
    /// <item><b>Covering Fire / Cross Counter</b> (<see cref="DamageStepClauseLegal"/> / <see cref="TryResolveDamageStepClauses"/>):
    /// Set Normal Traps answering the attacked player's Damage Calculation window (the engine's Damage Step window for
    /// Traps). Covering Fire's gain is <see cref="CardInstance.DamageStepAtkBonus"/> (cleared after damage calculation);
    /// Cross Counter doubles the battle damage the attacking player takes from that battle
    /// (BattleDamageEventsRuntime.DoubleBattleDamage → <see cref="DoublesBattleDamageTo"/>) and destroys the attacker at
    /// the end of the Damage Step (<see cref="OnEndOfDamageStep"/>).</item>
    /// </list>
    /// </summary>
    public static class RdsRedoBattleRuntime
    {
        /// <summary>ActivationCondition (Silent Swordsman LV5): its controller's next turn after this card inflicted battle
        /// damage to the opponent by a direct attack, while it stayed face-up on the field.</summary>
        public const string NextStandbyAfterDirectBattleDamage = "RdsRedoBattle.NextStandbyAfterDirectBattleDamage";

        sealed class DirectDamageRecord
        {
            public int Turn;
            public int FieldEntryStamp;
            public int FaceUpStamp;
        }

        sealed class StampBox
        {
            public int FieldEntryStamp;
        }

        sealed class CrossCounterRecord
        {
            public CardInstance Card;
            public DuelistState Controller;
            public CardInstance Defender;
            public CardInstance Attacker;
            public int AttackerStamp;
            public int Turn;
            public bool DoublesDamage;
        }

        sealed class CoinCallRun
        {
            public DuelistState AttackingPlayer;
            public DuelistState DefendingPlayer;
            public CardInstance Attacker;
            public CardInstance Target;
            public readonly List<(CardInstance sasuke, DuelistState controller)> Queue = new();
            public PendingActivation Pending;
            public CardInstance CurrentSasuke;
            public DuelistState CurrentController;
        }

        sealed class BattleState
        {
            public readonly List<CrossCounterRecord> CrossCounters = new();
            public readonly List<CardInstance> Boosted = new();
            public CoinCallRun Run;
        }

        static readonly ConditionalWeakTable<CardInstance, DirectDamageRecord> s_directDamage = new();
        static readonly ConditionalWeakTable<CardInstance, StampBox> s_metalmorphUsed = new();
        static readonly ConditionalWeakTable<DuelEngine, BattleState> s_battle = new();

        static BattleState StateOf(DuelEngine engine) => s_battle.GetValue(engine, _ => new BattleState());

        static string Shown(CardInstance c) => c == null ? "nothing" : c.FaceUp ? c.Name : "a face-down monster";

        /// <summary>A FullyCompiled program of <paramref name="card"/> holds a clause of <paramref name="action"/>.</summary>
        static bool Has(CardInstance card, EffectActionKind action, EffectTiming? timing = null)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == action && (timing == null || c.Timing == timing.Value));
        }

        // ───────────────────────────── negate only (EVENT_CHAIN_SOLVING) ─────────────────────────────

        /// <summary>
        /// DuelEngine.ResolveChainStack: the resolving <paramref name="link"/> is a Spell effect that targets a monster a
        /// face-up Silent Swordsman LV3 (itself, the link's controller being its opponent) or a face-up Rare Metalmorph (its
        /// linked monster, any controller, once) covers — its effect is negated (it resolves with no effect). True when
        /// negated; the caller then skips the link and sends a one-shot Spell to the GY.
        /// </summary>
        public static bool TryNegateSpellTargetingLink(DuelEngine engine, ChainLink link)
        {
            var card = link?.Card;
            var def = card?.Def;
            if (engine?.Player == null || engine.Opponent == null || def == null) return false;
            if (link.Negated || link.NegatesActivation || !def.IsSpell) return false;
            var targets = TargetingTruthRuntime.LinkTargets(link);
            if (targets.Count == 0) return false;
            // ygopro Duel.NegateEffect fails on an effect that cannot be negated.
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.ClauseList.Exists(c => c != null && !c.EffectNegatable)) return false;

            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                // Silent Swordsman LV3 (s.disop): rp==tp returns — only the opponent's Spell effects.
                if (link.Controller != null && !ReferenceEquals(link.Controller, side))
                {
                    foreach (var m in side.MonstersOnField().ToList())
                    {
                        if (m == null || !m.FaceUp || m.IsNegated || !targets.Contains(m)) continue;
                        if (!Has(m, EffectActionKind.NegateOpponentSpellEffectsThatTargetThis)) continue;
                        NegateOnly(engine, link, m, m);
                        return true;
                    }
                }

                // Rare Metalmorph (s.discon / s.disop): any Spell effect whose targets contain its linked monster; once.
                foreach (var st in side.SpellTrapsOnField().ToList())
                {
                    var host = st?.EquippedTo;
                    if (host == null || !st.FaceUp || st.IsNegated || !targets.Contains(host)) continue;
                    if (!Has(st, EffectActionKind.NegateOnceSpellEffectTargetingBoundMonster)) continue;
                    if (s_metalmorphUsed.TryGetValue(st, out var used) && used.FieldEntryStamp == st.FieldEntryStamp)
                        continue;
                    s_metalmorphUsed.Remove(st);
                    s_metalmorphUsed.Add(st, new StampBox { FieldEntryStamp = st.FieldEntryStamp });
                    NegateOnly(engine, link, st, host);
                    return true;
                }
            }

            return false;
        }

        static void NegateOnly(DuelEngine engine, ChainLink link, CardInstance protector, CardInstance covered)
        {
            var card = link.Card;
            link.DeferredEffect = null; // its recorded effect never applies
            Presentation.ArInteraction.ActivationBeat.NoteNegated(card.InstanceId);
            engine.Log($"{protector.Name}: the effect of {card.Name} that targets {covered.Name} is negated.");
            // An Equip Spell whose equip was negated equips nothing and is sent to the GY (a one-shot Spell is sent by the
            // caller; a Continuous Spell stays face-up).
            var holder = engine.ControllerOf(card);
            if (card.Def.IsEquipSpell && card.EquippedTo == null && holder != null && holder.TryFindSpellTrap(card, out _))
                engine.SendCardToGrave(holder, card);
        }

        // ───────────────────────────── Silent Swordsman LV5 ─────────────────────────────

        /// <summary>
        /// ContinuousProtections.IsUnaffectedBy, on an UnaffectedOnlyByOpponentEffects clause: <paramref name="source"/> is
        /// held by the opponent of <paramref name="victim"/>'s controller (ygopro te:GetOwnerPlayer()~=handler player).
        /// </summary>
        public static bool SourceIsOpponents(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (engine == null || victim == null || source == null) return false;
            var mine = engine.ControllerOf(victim);
            var theirs = HolderOf(engine, source);
            return mine != null && theirs != null && ReferenceEquals(theirs, engine.OpponentOf(mine));
        }

        static DuelistState HolderOf(DuelEngine engine, CardInstance card)
        {
            var ctrl = engine.ControllerOf(card);
            if (ctrl != null) return ctrl;
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side != null && (side.Graveyard.Contains(card) || side.Hand.Contains(card) || side.Banished.Contains(card)))
                    return side;
            return null;
        }

        static bool HasDirectDamageLevelUp(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.ActivationCondition == NextStandbyAfterDirectBattleDamage);
        }

        /// <summary>
        /// TextEffectRuntime.NotifyInflictedBattleDamage: <paramref name="source"/> (controlled by <paramref name="who"/>)
        /// inflicted battle damage to <paramref name="opp"/>. A face-up, non-negated Silent Swordsman LV5 that did it by a
        /// direct attack records the turn (Lua s.damop: ep~=tp and GetAttackTarget()==nil).
        /// </summary>
        public static void NoteBattleDamage(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source,
            bool wasDirect)
        {
            if (engine == null || who == null || opp == null || ReferenceEquals(who, opp) || !wasDirect) return;
            if (source == null || !source.FaceUp || source.IsNegated || !HasDirectDamageLevelUp(source)) return;
            if (!who.TryFindMonster(source, out _)) return;
            s_directDamage.Remove(source);
            s_directDamage.Add(source, new DirectDamageRecord
            {
                Turn = engine.TurnNumber,
                FieldEntryStamp = source.FieldEntryStamp,
                FaceUpStamp = source.FaceUpStamp
            });
        }

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet: <see cref="NextStandbyAfterDirectBattleDamage"/> — the flag lasts until
        /// the End Phase of its controller's next turn (RESET_PHASE|PHASE_END with RESET_SELF_TURN, count 2) and is lost when
        /// the card leaves the field or is turned face-down (RESETS_STANDARD); the trigger needs its controller's turn.
        /// False when the clause is not this batch's.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (c == null || c.ActivationCondition != NextStandbyAfterDirectBattleDamage) return false;
            ok = engine != null && card != null && who != null && ReferenceEquals(engine.TurnPlayer, who) &&
                 s_directDamage.TryGetValue(card, out var r) && r.FieldEntryStamp == card.FieldEntryStamp &&
                 r.FaceUpStamp == card.FaceUpStamp && card.FaceUp &&
                 engine.TurnNumber > r.Turn && engine.TurnNumber <= r.Turn + 2;
            reason = ok
                ? null
                : "Only during your next Standby Phase after this card inflicted battle damage by a direct attack.";
            return true;
        }

        // ───────────────────────────── Mystic Swordsman LV6 ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.TryStartOfDamageStepDestroy: <paramref name="attacker"/> (controlled by <paramref name="who"/>)
        /// attacked the face-down Defense Position <paramref name="defender"/> (controlled by <paramref name="holder"/>).
        /// It is destroyed; when the opponent of <paramref name="who"/> owns it (Lua d:GetOwner()~=tp), <paramref name="who"/>
        /// may place it on top of that Deck instead of the GY (SelectYesNo: a human taps it, Cancel = the GY; the AI places
        /// it). False when the effect does not apply (the attacker is negated): the caller goes on with the battle.
        /// </summary>
        public static bool DestroyAttackedFaceDownMayPlaceOnDeckTop(DuelEngine engine, DuelistState who,
            DuelistState holder, CardInstance attacker, CardInstance defender)
        {
            if (engine == null || who == null || holder == null || attacker == null || defender == null) return false;
            if (!attacker.FaceUp || attacker.IsNegated) return false;
            engine.Log($"{attacker.Name}: destroys the face-down Defense Position monster at the start of the Damage Step.");
            var owner = defender.ControlOwner ?? holder;
            if (defender.IsToken || !ReferenceEquals(owner, engine.OpponentOf(who)))
            {
                Destroy(false);
                return true;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskOptionalCardPick(engine, who, attacker,
                () => holder.TryFindMonster(defender, out _)
                    ? new List<CardInstance> { defender }
                    : new List<CardInstance>(),
                EffectTargetKind.OppMonster,
                $"{attacker.Name}: tap the destroyed monster to place it on top of your opponent's Deck instead of " +
                "sending it to the GY, or Cancel to send it to the GY.",
                list => list.FirstOrDefault(),
                chosen => Destroy(chosen != null));
            return true;

            void Destroy(bool deckTop)
            {
                if (!holder.TryFindMonster(defender, out _)) return;
                TextEffectRuntime.DestroyCard(engine, defender, attacker, byPlayer: who,
                    placeOnOwnerDeckTopInsteadOfGy: deckTop);
                engine.NotifyPublic();
            }
        }

        // ───────────────────────────── Sasuke Samurai #4 ─────────────────────────────

        static bool HasCoinCall(CardInstance m) =>
            m != null && m.FaceUp && !m.IsNegated &&
            Has(m, EffectActionKind.CoinCallDestroyBattledMonsterOnCorrect, EffectTiming.StartOfDamageStep);

        /// <summary>
        /// DuelEngine.ResolveDeclaredAttack at the start of the Damage Step (ygopro EVENT_BATTLE_START), before the face-down
        /// flip: a face-up Sasuke Samurai #4 that attacks <paramref name="target"/> or is attacked by
        /// <paramref name="attacker"/>. Both activate as one Chain (the turn player's is Chain Link 1), so the attacked one
        /// resolves first. True when this batch took the Damage Step over (it resumes it through
        /// DuelEngine.ContinueStartOfDamageStep); false when no coin call applies.
        /// </summary>
        public static bool TryBeginBattleStartCoinCalls(DuelEngine engine, DuelistState who, DuelistState opp,
            CardInstance attacker, CardInstance target)
        {
            if (engine == null || engine.GameOver || who == null || opp == null || attacker == null || target == null)
                return false;
            var run = new CoinCallRun
            {
                AttackingPlayer = who,
                DefendingPlayer = opp,
                Attacker = attacker,
                Target = target
            };
            if (HasCoinCall(target)) run.Queue.Add((target, opp));
            if (HasCoinCall(attacker)) run.Queue.Add((attacker, who));
            if (run.Queue.Count == 0) return false;
            StateOf(engine).Run = run;
            Step(engine, run);
            return true;
        }

        static void Step(DuelEngine engine, CoinCallRun run)
        {
            while (run.Queue.Count > 0 && !engine.GameOver)
            {
                var (sasuke, ctrl) = run.Queue[0];
                run.Queue.RemoveAt(0);
                var partner = PartnerOf(run, sasuke, out var partnerHolder);
                // Lua s.desop: if not tc:IsRelateToBattle() then return end — before the call.
                if (partner == null || partnerHolder == null || !partnerHolder.TryFindMonster(partner, out _))
                {
                    engine.Log($"{sasuke.Name}: its battle partner left the field — no coin toss.");
                    continue;
                }

                engine.Log($"{sasuke.Name} battles {Shown(partner)} — {ctrl.Name} tosses a coin and calls it.");
                run.CurrentSasuke = sasuke;
                run.CurrentController = ctrl;
                if (engine.IsHumanControlled(ctrl) && engine.PendingActivation == null)
                {
                    var p = new PendingActivation
                    {
                        Controller = ctrl,
                        Card = sasuke,
                        IsMonsterEffect = true,
                        UsesTextProgram = true,
                        AwaitingCoinCall = true,
                        PromptOverride = $"{sasuke.Name}: call the coin toss — if you call it right, destroy " +
                                         $"{Shown(partner)}."
                    };
                    run.Pending = p;
                    engine.SetPendingActivation(p);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return;
                }

                Toss(engine, run, callHeads: true);
                return; // the toss continues the run (it may wait on a Second Coin Toss choice)
            }

            Finish(engine, run);
        }

        static CardInstance PartnerOf(CoinCallRun run, CardInstance sasuke, out DuelistState holder)
        {
            // Lua: tc = GetAttackTarget(); if c == tc then tc = GetAttacker().
            if (ReferenceEquals(sasuke, run.Target))
            {
                holder = run.AttackingPlayer;
                return run.Attacker;
            }

            holder = run.DefendingPlayer;
            return run.Target;
        }

        static void Toss(DuelEngine engine, CoinCallRun run, bool callHeads)
        {
            var sasuke = run.CurrentSasuke;
            var ctrl = run.CurrentController;
            CoinDiceInterruptsRuntime.TossCoin(engine, ctrl, sasuke, toss => toss == callHeads, toss =>
            {
                engine.Log($"{ctrl.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(toss ? "Heads" : "Tails")}.");
                var partner = PartnerOf(run, sasuke, out var partnerHolder);
                if (toss != callHeads)
                    engine.Log($"{sasuke.Name}: wrong call — no effect.");
                else if (partner != null && partnerHolder != null && partnerHolder.TryFindMonster(partner, out _))
                {
                    engine.Log($"{sasuke.Name}: right call — {Shown(partner)} is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, partner, sasuke, byPlayer: ctrl);
                }

                Step(engine, run);
            });
        }

        static void Finish(DuelEngine engine, CoinCallRun run)
        {
            var st = StateOf(engine);
            if (ReferenceEquals(st.Run, run)) st.Run = null;
            if (engine.GameOver)
            {
                engine.NotifyPublic();
                return;
            }

            engine.ContinueStartOfDamageStep(run.AttackingPlayer, run.DefendingPlayer, run.Attacker, run.Target);
        }

        /// <summary>TextEffectRuntime.TryFinishCoinCall hook: Sasuke Samurai #4's call. False when the open call is not ours.</summary>
        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_battle.TryGetValue(engine, out var st) || st.Run == null ||
                !ReferenceEquals(st.Run.Pending, p))
                return false;
            var run = st.Run;
            run.Pending = null;
            engine.ClearPendingActivation();
            Toss(engine, run, callHeads);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the call is mandatory (Cancel calls Heads).</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_battle.TryGetValue(engine, out var st) || st.Run == null ||
                !ReferenceEquals(st.Run.Pending, p))
                return false;
            engine.Log($"{p.Card?.Name}: the call is mandatory — Heads is called.");
            return TryFinishCoinCall(engine, callHeads: true);
        }

        // ───────────────────────────── Covering Fire / Cross Counter ─────────────────────────────

        /// <summary>
        /// The battle of the open Damage Calculation window, from <paramref name="who"/>'s side: an opponent's monster
        /// attacks <paramref name="who"/>'s monster, both still on the field.
        /// </summary>
        static bool AttackedBattle(DuelEngine engine, DuelistState who, out CardInstance attacker, out CardInstance attacked,
            out DuelistState attackingPlayer)
        {
            var pr = engine?.PendingResponse;
            attacker = pr?.Attacker;
            attacked = pr?.AttackTarget;
            attackingPlayer = pr?.AttackingPlayer;
            if (pr == null || pr.Timing != ResponseTiming.DamageCalculation || who == null || attacker == null ||
                attacked == null || attackingPlayer == null || ReferenceEquals(attackingPlayer, who))
                return false;
            return who.TryFindMonster(attacked, out _) && attackingPlayer.TryFindMonster(attacker, out _);
        }

        /// <summary>Covering Fire's targets: another face-up monster you control with ATK above 0 (Card.HasNonZeroAttack)
        /// that can be targeted.</summary>
        static List<CardInstance> CoveringFireTargets(DuelEngine engine, DuelistState who, CardInstance card,
            CardInstance attacked) =>
            who.MonstersOnField()
                .Where(m => m != null && !ReferenceEquals(m, attacked) && m.FaceUp && m.CurrentAtk > 0 &&
                            !ContinuousProtections.TargetedEffectBlocked(engine, m, card))
                .ToList();

        static bool CoveringFireLegal(DuelEngine engine, DuelistState who, CardInstance card) =>
            AttackedBattle(engine, who, out _, out var attacked, out _) && attacked.FaceUp &&
            CoveringFireTargets(engine, who, card, attacked).Count > 0;

        static bool CrossCounterLegal(DuelEngine engine, DuelistState who)
        {
            if (!AttackedBattle(engine, who, out var attacker, out var attacked, out _)) return false;
            // Lua: bc:IsPosition(POS_FACEUP_DEFENSE) and Duel.GetAttacker():GetAttack() < bc:GetDefense().
            return attacked.FaceUp && attacked.Position == BattlePosition.Defense &&
                   attacker.CurrentAtk < attacked.CurrentDef;
        }

        /// <summary>TextEffectRuntime.DamageStepClausesLegal: Covering Fire's and Cross Counter's battle conditions; true for
        /// every other clause.</summary>
        public static bool DamageStepClauseLegal(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (c == null) return true;
            if (c.Action == EffectActionKind.TargetGainsAtkEqualToAnotherTargetDuringDamageStep)
                return CoveringFireLegal(engine, who, card);
            if (c.Action == EffectActionKind.DoubleBattleDamageToAttackerThenDestroyAttacker)
                return CrossCounterLegal(engine, who);
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.ResolveDamageStepClauses, after the Trap was placed face-up: Covering Fire (target pick, then the
        /// gain) and Cross Counter resolve, then damage calculation goes on (DuelEngine.ContinueAfterResponseActivation).
        /// False for every other card.
        /// </summary>
        public static bool TryResolveDamageStepClauses(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> clauses)
        {
            var cover = clauses?.Find(c => c?.Action == EffectActionKind.TargetGainsAtkEqualToAnotherTargetDuringDamageStep);
            var cross = clauses?.Find(c => c?.Action == EffectActionKind.DoubleBattleDamageToAttackerThenDestroyAttacker);
            if (engine == null || who == null || card == null || (cover == null && cross == null)) return false;
            var pr = engine.PendingResponse;
            var attacker = pr?.Attacker;
            var attacked = pr?.AttackTarget;

            if (cross != null)
            {
                ResolveCrossCounter(engine, who, card, attacker, attacked);
                TextEffectRuntime.FinishSpellTrap(engine, who, card, stays: false);
                engine.ContinueAfterResponseActivation(false, false);
                return true;
            }

            engine.ClearPendingResponse();
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => CoveringFireTargets(engine, who, card, attacked),
                Kind = EffectTargetKind.YourMonster,
                Prompt = $"{card.Name}: target 1 other face-up monster you control — {attacked?.Name} gains its ATK " +
                         "during this Damage Step.",
                AiChoose = list => list.OrderByDescending(m => m.CurrentAtk).FirstOrDefault(),
                HoldsChain = false,
                Finish = tgt =>
                {
                    ApplyCoveringFire(engine, who, card, attacked, tgt);
                    TextEffectRuntime.FinishSpellTrap(engine, who, card, stays: false);
                    engine.ContinueAfterResponseActivation(false, false);
                }
            });
            return true;
        }

        static void ApplyCoveringFire(DuelEngine engine, DuelistState who, CardInstance card, CardInstance attacked,
            CardInstance tgt)
        {
            engine.Log(tgt == null
                ? $"{card.Name}: no monster to target."
                : $"{card.Name} targets {tgt.Name}.");
            // Lua s.activate: bc:IsFaceup() and bc:IsRelateToBattle() and tc:IsRelateToEffect(e) and tc:IsFaceup() and
            // tc:IsControler(tp).
            if (tgt == null || attacked == null || !attacked.FaceUp || !who.TryFindMonster(attacked, out _) ||
                !tgt.FaceUp || !who.TryFindMonster(tgt, out _))
            {
                engine.Log($"{card.Name}: no effect.");
                return;
            }

            if (ContinuousNegationRuntime.NegatesOnFieldResolution(engine, card))
            {
                engine.Log($"{card.Name}: its effect is negated.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, attacked, card))
            {
                engine.Log($"{attacked.Name} is unaffected by {card.Name}.");
                return;
            }

            var gain = tgt.CurrentAtk;
            attacked.DamageStepAtkBonus += gain;
            StateOf(engine).Boosted.Add(attacked);
            engine.Log($"{attacked.Name} gains {gain} ATK during this Damage Step (now {attacked.CurrentAtk}).");
        }

        static void ResolveCrossCounter(DuelEngine engine, DuelistState who, CardInstance card, CardInstance attacker,
            CardInstance attacked)
        {
            if (ContinuousNegationRuntime.NegatesOnFieldResolution(engine, card))
            {
                engine.Log($"{card.Name}: its effect is negated.");
                return;
            }

            // Lua: the damage change needs bc face-up and still in the battle; the end-of-Damage-Step destruction is
            // registered either way.
            var doubles = attacked != null && attacked.FaceUp && who.TryFindMonster(attacked, out _);
            StateOf(engine).CrossCounters.Add(new CrossCounterRecord
            {
                Card = card,
                Controller = who,
                Defender = attacked,
                Attacker = attacker,
                AttackerStamp = attacker?.FieldEntryStamp ?? 0,
                Turn = engine.TurnNumber,
                DoublesDamage = doubles
            });
            engine.Log(doubles
                ? $"{card.Name}: the battle damage {engine.OpponentOf(who)?.Name} takes from this battle is doubled; " +
                  $"{attacker?.Name} is destroyed at the end of the Damage Step."
                : $"{card.Name}: {attacker?.Name} is destroyed at the end of the Damage Step.");
        }

        /// <summary>
        /// BattleDamageEventsRuntime.DoubleBattleDamage: Cross Counter resolved for this battle, <paramref name="target"/> is
        /// its face-up attacked monster and <paramref name="victim"/> is the opponent of Cross Counter's controller.
        /// </summary>
        public static bool DoublesBattleDamageTo(DuelEngine engine, CardInstance target, DuelistState victim)
        {
            if (engine == null || target == null || victim == null || !target.FaceUp ||
                !s_battle.TryGetValue(engine, out var st))
                return false;
            return st.CrossCounters.Exists(r => r.DoublesDamage && ReferenceEquals(r.Defender, target) &&
                                                r.Turn == engine.TurnNumber &&
                                                ReferenceEquals(victim, engine.OpponentOf(r.Controller)));
        }

        /// <summary>
        /// DuelEngine at the end of the Damage Step of <paramref name="attacker"/>'s battle (ApplyBattleDestructions, or a
        /// damage calculation that did not happen because a battler left): Covering Fire's gains end, and Cross Counter
        /// destroys that attacker if it is still on the field from that battle (Lua EVENT_DAMAGE_STEP_END,
        /// ac:IsRelateToBattle()). Records of another battle are dropped.
        /// </summary>
        public static void OnEndOfDamageStep(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || !s_battle.TryGetValue(engine, out var st)) return;
            foreach (var m in st.Boosted)
                if (m != null) m.DamageStepAtkBonus = 0;
            st.Boosted.Clear();
            if (st.CrossCounters.Count == 0) return;
            var records = st.CrossCounters.ToList();
            st.CrossCounters.Clear();
            foreach (var r in records)
            {
                if (engine.GameOver) break;
                var a = r.Attacker;
                if (a == null || !ReferenceEquals(a, attacker) || r.Turn != engine.TurnNumber) continue;
                var holder = engine.ControllerOf(a);
                if (holder == null || !holder.TryFindMonster(a, out _) || a.FieldEntryStamp != r.AttackerStamp) continue;
                engine.Log($"{r.Card.Name}: {a.Name} is destroyed at the end of the Damage Step.");
                TextEffectRuntime.DestroyCard(engine, a, r.Card, byPlayer: r.Controller);
            }
        }

        /// <summary>SpellTrapEffects.AiAutoRespond, Damage Calculation window: Cross Counter whenever it is legal; Covering
        /// Fire when the attacked monster is in Attack Position and the gain turns the battle in its favour.</summary>
        public static bool AiWantsDamageCalculationAnswer(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            var dmg = prog.ClausesFor(EffectTiming.DamageCalculation);
            if (dmg.Exists(c => c?.Action == EffectActionKind.DoubleBattleDamageToAttackerThenDestroyAttacker))
                return CrossCounterLegal(engine, who);
            if (!dmg.Exists(c => c?.Action == EffectActionKind.TargetGainsAtkEqualToAnotherTargetDuringDamageStep))
                return false;
            if (!AttackedBattle(engine, who, out var attacker, out var attacked, out _) || !attacked.FaceUp ||
                attacked.Position != BattlePosition.Attack)
                return false;
            var best = CoveringFireTargets(engine, who, card, attacked).Select(m => m.CurrentAtk).DefaultIfEmpty(0).Max();
            var atk = attacker.CurrentAtk;
            return best > 0 && attacked.CurrentAtk <= atk && attacked.CurrentAtk + best > atk;
        }
    }
}
