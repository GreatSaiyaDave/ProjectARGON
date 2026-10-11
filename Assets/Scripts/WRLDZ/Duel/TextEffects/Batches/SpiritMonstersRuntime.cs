using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpiritMonsters" runtime: Asura Priest (attack all), Inaba White Rabbit (direct attacks only), Susa
    /// Soldier (halved battle damage), Great Long Nose (skip the next Battle Phase), the "Normal Summoned or flipped
    /// face-up" triggers of Otohime and Maharaghi on flips by battle or by a card effect, and Maharaghi's look at the top
    /// card before the next draw. Fushi No Tori's LP gain is the shared GainLifePoints arm with
    /// <see cref="EffectClause.AmountIsEquippedBattleDamage"/> (TextEffectRuntime.NotifyInflictedBattleDamage passes the
    /// damage). Atoms: <see cref="SpiritMonstersAtoms"/>.
    /// </summary>
    public static class SpiritMonstersRuntime
    {
        /// <summary>A face-up, non-negated monster whose fully compiled program has this continuous marker.</summary>
        static bool HasLiveMarker(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .Exists(c => c != null && c.Action == action);
        }

        // ───────────────────────────── Asura Priest: attack all ─────────────────────────────

        /// <summary>The opponent's monsters an attack-all monster declared an attack on, this Battle Phase.</summary>
        sealed class AttackRecord
        {
            public int Turn;
            public readonly List<(CardInstance Card, int Entry)> Targets = new List<(CardInstance, int)>();
        }

        static readonly ConditionalWeakTable<CardInstance, AttackRecord> s_attacked =
            new ConditionalWeakTable<CardInstance, AttackRecord>();

        /// <summary>"This card can attack all monsters your opponent controls once each." (EFFECT_ATTACK_ALL).</summary>
        public static bool AttacksAllOpponentMonsters(CardInstance attacker) =>
            HasLiveMarker(attacker, EffectActionKind.AttackAllOpponentMonstersOnceEach);

        /// <summary>
        /// The attack-all monster has used its ordinary attacks this Battle Phase (1, plus any extra attacks): every
        /// further attack is an attack-all one (ygopro get_attack_target atype 2) — only on an opponent's monster it has
        /// not attacked yet, never directly.
        /// </summary>
        public static bool IsAttackAllFollowUp(DuelEngine engine, CardInstance attacker) =>
            engine != null && attacker != null && AttacksAllOpponentMonsters(attacker) &&
            attacker.AttacksDeclaredThisTurn >= 1 + engine.ExtraAttacksAllowed(attacker);

        /// <summary>
        /// The attacker declared an attack on <paramref name="target"/> earlier this Battle Phase (ygopro
        /// announced_cards: a negated attack counts). A copy that left the field and came back is a new card.
        /// </summary>
        static bool AlreadyAttacked(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return false;
            // The counter is reset each turn and for a second Battle Phase; a declaration that was replayed was not
            // counted either. A record older than the counter means nothing.
            if (attacker.AttacksDeclaredThisTurn == 0) return false;
            if (!s_attacked.TryGetValue(attacker, out var rec) || rec.Turn != engine.TurnNumber) return false;
            return rec.Targets.Exists(t => ReferenceEquals(t.Card, target) && t.Entry == target.FieldEntryStamp);
        }

        /// <summary>DuelEngine.TryAttack, at the declaration: remember the attack-all monster's target.</summary>
        public static void NoteAttackDeclared(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || !AttacksAllOpponentMonsters(attacker)) return;
            var rec = s_attacked.GetOrCreateValue(attacker);
            if (rec.Turn != engine.TurnNumber || attacker.AttacksDeclaredThisTurn == 0)
            {
                rec.Turn = engine.TurnNumber;
                rec.Targets.Clear();
            }

            if (target != null)
                rec.Targets.Add((target, target.FieldEntryStamp));
        }

        /// <summary>
        /// DuelEngine.MaxAttacksThisTurn: past its ordinary attacks, an attack-all monster may declare one more attack
        /// while the opponent controls a monster it has not attacked yet this Battle Phase.
        /// </summary>
        public static int AdjustMaxAttacks(DuelEngine engine, CardInstance attacker, int max)
        {
            if (!IsAttackAllFollowUp(engine, attacker)) return max;
            var opp = engine.OpponentOf(engine.ControllerOf(attacker));
            if (opp == null) return max;
            foreach (var m in opp.MonstersOnField())
            {
                if (m != null && !AlreadyAttacked(engine, attacker, m))
                    return Math.Max(max, attacker.AttacksDeclaredThisTurn + 1);
            }

            return max;
        }

        // ───────────────────────────── attack legality (Asura Priest, Inaba White Rabbit) ─────────────────────────────

        /// <summary>
        /// <paramref name="attacker"/> may not declare an attack on <paramref name="target"/>: Inaba White Rabbit never
        /// attacks a monster (EFFECT_CANNOT_SELECT_BATTLE_TARGET); an attack-all follow-up never attacks the same monster
        /// twice in a Battle Phase.
        /// </summary>
        public static bool ForbidsAttackTarget(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return false;
            if (HasLiveMarker(attacker, EffectActionKind.CannotSelectMonsterAsAttackTarget)) return true;
            return IsAttackAllFollowUp(engine, attacker) && AlreadyAttacked(engine, attacker, target);
        }

        /// <summary>An attack-all follow-up attack cannot be a direct attack.</summary>
        public static bool ForbidsDirectAttack(DuelEngine engine, CardInstance attacker) =>
            IsAttackAllFollowUp(engine, attacker);

        // ───────────────────────────── Susa Soldier: halved battle damage ─────────────────────────────

        /// <summary>
        /// DuelEngine damage calculation: battle damage <paramref name="inflictor"/> inflicts to its controller's
        /// opponent (aux.ChangeBattleDamage(1, HALF_DAMAGE): never damage to its own controller) is halved, rounded down.
        /// </summary>
        public static int HalveBattleDamage(DuelEngine engine, CardInstance inflictor, DuelistState victim, int damage)
        {
            if (engine == null || inflictor == null || victim == null || damage <= 0) return damage;
            var controller = engine.ControllerOf(inflictor);
            if (controller == null || controller == victim) return damage;
            if (!HasLiveMarker(inflictor, EffectActionKind.HalveBattleDamageThisInflictsToOpponent)) return damage;
            var halved = damage / 2;
            engine.Log($"{inflictor.Name}: the battle damage it inflicts is halved ({damage} → {halved}).");
            return halved;
        }

        // ───────────────────────────── Great Long Nose: skip the next Battle Phase ─────────────────────────────

        /// <summary>The OpponentSkipsNextBattlePhase arm: the opponent's next turn has no Battle Phase.</summary>
        public static void ScheduleBattlePhaseSkip(DuelEngine engine, DuelistState victim, CardInstance source)
        {
            if (engine == null || victim == null) return;
            // DcrSpellTrapUtility batch — Frozen Soul (free-chain Trap): during the victim's own turn before its Battle
            // Phase (Draw, Standby, Main Phase 1) the next Battle Phase is this turn's (Lua EFFECT_SKIP_BP applies at
            // once, reset at the end of their Battle Phase); later in their turn, or in the other player's turn, it is
            // the one of their next turn.
            if (ReferenceEquals(engine.TurnPlayer, victim) &&
                (engine.Phase == DuelPhase.Draw || engine.Phase == DuelPhase.Standby || engine.Phase == DuelPhase.Main1))
            {
                victim.BattlePhaseForbiddenTurn = engine.TurnNumber;
                engine.Log($"{source?.Name ?? "Effect"}: {victim.Name} skips their Battle Phase this turn.");
                return;
            }

            victim.SkipNextBattlePhase = true;
            victim.SkipNextBattlePhaseSource = source?.Name;
            engine.Log($"{source?.Name ?? "Effect"}: {victim.Name} skips their next Battle Phase.");
        }

        /// <summary>
        /// DuelEngine.BeginTurn, once the turn is played (not skipped): a pending skip makes this turn's Battle Phase
        /// impossible (<see cref="DuelistState.BattlePhaseForbiddenTurn"/>, read by DuelEngine.CanConductBattlePhase and
        /// TryEnterBattlePhase). Set during this player's own turn, the flag was raised after that turn's Battle Phase
        /// could still be entered, so it waits for this, their next turn (the Lua turn-count condition).
        /// </summary>
        public static void OnTurnStart(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || !who.SkipNextBattlePhase) return;
            who.SkipNextBattlePhase = false;
            who.BattlePhaseForbiddenTurn = engine.TurnNumber;
            engine.Log($"{who.Name} skips their Battle Phase this turn ({who.SkipNextBattlePhaseSource ?? "an effect"}).");
            who.SkipNextBattlePhaseSource = null;
        }

        /// <summary>
        /// HandDisruptionRuntime.ConsumeSkippedTurn: a turn skipped entirely still runs ygopro's phase resets, so the
        /// pending Battle Phase skip (RESET_PHASE+PHASE_END+RESET_OPPO_TURN) and the pending look at the top card
        /// (RESET_PHASE+PHASE_DRAW+RESET_SELF_TURN) end with it.
        /// </summary>
        public static void OnSkippedTurn(DuelEngine engine, DuelistState who)
        {
            if (who == null) return;
            who.SkipNextBattlePhase = false;
            who.SkipNextBattlePhaseSource = null;
            who.LookAtTopCardAtNextDrawPhase = false;
            who.LookAtTopCardSource = null;
        }

        // ───────────────────────────── "Normal Summoned or flipped face-up" (Otohime, Maharaghi) ─────────────────────────────

        /// <summary>The monster has a summon trigger that also fires when it is flipped face-up (EVENT_FLIP).</summary>
        public static bool HasFlippedFaceUpTrigger(CardInstance monster)
        {
            if (monster?.Def == null || !monster.Def.IsMonster) return false;
            var prog = CompiledEffectCache.GetOrCompile(monster);
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(EffectTiming.ThisCardSummoned).Exists(c => c != null && c.AlsoWhenFlippedFaceUp);
        }

        /// <summary>
        /// A card effect flipped <paramref name="monster"/> face-up (DuelEngine.ApplyEffectPositionChange, the targeted
        /// position change): like a FLIP effect, its "flipped face-up" trigger activates once that effect and its chain
        /// have resolved (DuelEngine.QueueFlipEffectAfterResolution → <see cref="NotifyFlippedFaceUp"/>).
        /// </summary>
        public static void NoteFlippedFaceUpByEffect(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null || !HasFlippedFaceUpTrigger(monster)) return;
            var controller = engine.ControllerOf(monster);
            if (controller == null || !controller.TryFindMonster(monster, out _)) return;
            engine.QueueFlipEffectAfterResolution(controller, monster);
        }

        /// <summary>
        /// <paramref name="monster"/> was flipped face-up by battle or by a card effect (not a Flip Summon, which fires
        /// the same clauses from DuelEngine.CompleteFlipSummon): its "When/If this card is Normal Summoned or flipped
        /// face-up" clauses fire, through the ordinary summon-trigger path.
        /// </summary>
        public static bool NotifyFlippedFaceUp(DuelEngine engine, DuelistState controller, CardInstance monster)
        {
            if (engine == null || controller == null || monster == null || !monster.FaceUp) return false;
            if (!controller.TryFindMonster(monster, out _) || !HasFlippedFaceUpTrigger(monster)) return false;
            return TextEffectRuntime.TryResolveThisCardSummoned(engine, controller, monster, flipSummon: true,
                only: c => c != null && c.AlsoWhenFlippedFaceUp);
        }

        // ───────────────────────────── Maharaghi: look at the top card before the draw ─────────────────────────────

        /// <summary>The LookAtTopCardAtNextDrawPhaseTopOrBottom arm: register the look for the controller's next Draw Phase.</summary>
        public static void ScheduleTopCardLook(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            if (who.LookAtTopCardAtNextDrawPhase)
            {
                engine.Log($"{source?.Name ?? "Effect"}: {who.Name} already looks at the top card of their Deck during their next Draw Phase.");
                return;
            }

            who.LookAtTopCardAtNextDrawPhase = true;
            who.LookAtTopCardSource = source?.Name;
            engine.Log($"{source?.Name ?? "Effect"}: during {who.Name}'s next Draw Phase, before the draw, they look at the top card of their Deck and put it on the top or the bottom.");
        }

        /// <summary>A human's open top-or-bottom choice: the turn player whose Draw Phase waits for it.</summary>
        sealed class TopCardLook
        {
            public DuelistState Who;
            public int CardId;
            public string Source;
        }

        static readonly ConditionalWeakTable<PendingActivation, TopCardLook> s_looks =
            new ConditionalWeakTable<PendingActivation, TopCardLook>();

        /// <summary>
        /// DuelEngine.BeginTurn, at the start of the turn player's Draw Phase, before the draw (EVENT_PREDRAW): a pending
        /// look at the top card applies now. A Draw Phase skipped by a card effect has no EVENT_PREDRAW; the look ends
        /// with it (RESET_PHASE+PHASE_DRAW). An empty Deck: nothing to look at (the Lua condition). The AI decides at
        /// once; a human gets a one-card pick of the top card — tap it to put it on the bottom, CANCEL to leave it on
        /// top — and the Draw Phase waits: true, and <see cref="DuelEngine.ContinueDrawPhase"/> runs once answered.
        /// </summary>
        public static bool OnDrawPhaseStart(DuelEngine engine, DuelistState who, bool skippedByEffect)
        {
            if (engine == null || who == null || !who.LookAtTopCardAtNextDrawPhase) return false;
            who.LookAtTopCardAtNextDrawPhase = false;
            var src = who.LookAtTopCardSource ?? "Effect";
            who.LookAtTopCardSource = null;
            if (skippedByEffect)
            {
                engine.Log($"The Draw Phase is skipped — {who.Name}'s look at the top card ends ({src}).");
                return false;
            }

            if (who.Deck == null || who.Deck.Count == 0) return false;
            var topId = who.Deck[0];
            if (!engine.IsHumanControlled(who))
            {
                var top = engine.CreateCardInstance(topId);
                var bottom = !AiKeepsOnTop(engine, who, top);
                if (bottom) MoveTopToBottom(who);
                engine.Log($"{src}: {who.Name} looks at the top card of their Deck and puts it on the " +
                           (bottom ? "bottom." : "top."));
                return false;
            }

            var topCard = engine.CreateCardInstance(topId);
            var pending = new PendingActivation
            {
                Controller = who,
                Card = topCard,
                TargetKind = EffectTargetKind.CardInYourDeck,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                // The whole line (no card-name prefix: Card is the looked-at Deck card, not the source card).
                DeclarationPrompt = $"{src}: the top card of your Deck is {topCard?.Name ?? "unknown"}. Tap it to " +
                                    "put it on the bottom of your Deck, or CANCEL to leave it on top. Then you draw."
            };
            pending.LegalTargets.Add(pending.Card);
            s_looks.Add(pending, new TopCardLook { Who = who, CardId = topId, Source = src });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            return true;
        }

        /// <summary>TextEffectRuntime.TryResolveMonsterTextTarget hook: the top card goes to the bottom.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_looks.TryGetValue(pending, out var look)) return false;
            if (engine.ResolveLegalEffectTarget(target) == null)
            {
                engine.Log("Tap the top card to put it on the bottom of your Deck, or CANCEL to leave it on top.");
                return true;
            }

            ok = true;
            Finish(engine, pending, look, bottom: true);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the top card stays on top.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_looks.TryGetValue(pending, out var look)) return false;
            Finish(engine, pending, look, bottom: false);
            return true;
        }

        static void Finish(DuelEngine engine, PendingActivation pending, TopCardLook look, bool bottom)
        {
            s_looks.Remove(pending);
            engine.ClearPendingActivation();
            var who = look.Who;
            // The Deck cannot change while the Draw Phase waits; check anyway before moving a card.
            if (bottom && who.Deck != null && who.Deck.Count > 0 && who.Deck[0] == look.CardId)
                MoveTopToBottom(who);
            engine.Log($"{look.Source ?? "Effect"}: {who.Name} puts the card on the {(bottom ? "bottom" : "top")} of their Deck.");
            engine.ContinueDrawPhase(who);
            engine.NotifyPublic();
        }

        public static void MoveTopToBottomPublic(DuelistState who) => MoveTopToBottom(who);

        static void MoveTopToBottom(DuelistState who)
        {
            if (who?.Deck == null || who.Deck.Count < 2) return;
            var id = who.Deck[0];
            who.Deck.RemoveAt(0);
            who.Deck.Add(id);
        }

        /// <summary>
        /// AI: leave the top card where it is when it is useful now — a Spell or Trap, an Exodia piece, or a monster it
        /// can Normal Summon with the monsters it controls whose best stat is 1000 or more; otherwise to the bottom.
        /// </summary>
        public static bool AiKeepsOnTop(DuelEngine engine, DuelistState ai, CardInstance top)
        {
            var def = top?.Def;
            if (def == null) return true;
            if (!def.IsMonster) return true;
            if (AiHeuristicPolicy.DiscardPriority(top) >= 1000) return true;
            var tributes = def.level >= 7 ? 2 : def.level >= 5 ? 1 : 0;
            if (tributes > ai.MonsterCount) return false;
            return Math.Max(def.atk, def.def) >= 1000;
        }
    }
}
