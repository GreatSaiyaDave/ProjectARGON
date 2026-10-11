using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RedirectAndReplace" (v142) runtime.
    /// <para><b>Fairy's Hand Mirror / Shift</b> (<see cref="EffectActionKind.RedirectToAnotherAppropriateTarget"/>):
    /// a Normal Trap chained to the opponent's activation of a Spell (Mirror) or Spell/Trap Card (Shift) whose Chain
    /// Link targets exactly 1 monster on the field (Shift: one you control). It targets another monster that would
    /// be an appropriate target for that link — one in the link's own legal target list (the list its card offered
    /// when it was activated), in a Monster Zone, not the current target (Shift: one you control) — and, if that
    /// monster is still on the field when it resolves, it becomes the link's target (<see cref="ChainLink.Target"/>
    /// is what the answered link resolves against). Shift also answers an opponent's attack on a monster you
    /// control: it targets another monster you control that the attacker could attack, and the attack is redirected
    /// to it (<see cref="DuelEngine.ChangeDeclaredAttackTarget"/>, no replay) unless the attacker is unaffected by
    /// Shift or can no longer attack it.</para>
    /// <para>Only links whose resolution honours their recorded target can be redirected: a FullyCompiled Spell/Trap
    /// Card activation with one targeted clause group (no second target, no multi-target, no resolution-time cost
    /// path), not answering a Summon (Trap Hole's target is the Summoned monster). A link that picks its target at
    /// resolution records none, so it is never offered — the Trap is simply not activatable then.</para>
    /// <para>Neither card can be activated in the Damage Step. The resolving Trap finds the link it answered as the
    /// last link still on the chain (chains resolve last-in-first-out, and it was chained right on top of it); with
    /// no link left, Shift's resolution is the attack answer.</para>
    /// <para><b>Kotodama</b> (<see cref="EffectActionKind.DestroyNewFaceUpMonstersSharingAName"/>):
    /// <see cref="SweepSameNameDestruction"/> runs on every board refresh (the ygopro EVENT_ADJUST point).</para>
    /// <para><b>Banisher of the Light</b> (<see cref="EffectActionKind.BanishCardsSentToGyInstead"/>): every GY send
    /// asks <see cref="SendsToBanishedInstead"/> — DuelEngine.SendCardToGrave / DestroyMonster / the summon-negation
    /// destroy, and the hand / Deck helpers through <see cref="PutInGraveyard"/>. A card banished instead fires no
    /// "sent to the GY" event or trigger. Cards sent at the same time as a Banisher that leaves with them are banished
    /// too (<see cref="EnterEffectApplication"/>: one effect, one set of Tributes); a Banisher destroyed by battle has
    /// no effect while that battle's destructions apply (<see cref="NoteBattleDestroyed"/>); Equip Cards sent by rule
    /// after their monster left only see a Banisher still on the field (<see cref="EnterRuleSend"/>), so while the
    /// effect / Tributes / pick-by-pick cost that removed their monster is still going on, their send waits until it
    /// is over (<see cref="TryDeferRuleSend"/>). A destroyed card banished instead still activates its own "if this
    /// card is destroyed (by battle)" triggers that do not need the GY (<see cref="ResolveDestroyedTriggersOutsideGy"/>,
    /// Slate Warrior); a Token banished instead is remembered for the "… and sends it to the GY" battle triggers
    /// (<see cref="NeedsGyButWasBanished"/>).</para>
    /// <para>Summon answers acting on the Summoned monster are not legal once it has left the field
    /// (<see cref="SummonAnswerLostItsMonster"/>: Kotodama destroys a Summoned duplicate before anyone gets
    /// priority, so Trap Hole cannot target it).</para>
    /// </summary>
    public static class RedirectAndReplaceRuntime
    {
        /// <summary>ActivationCondition: the opponent activated a Spell Card targeting exactly 1 monster on the field.</summary>
        public const string OpponentSpellTargetsOneMonster = "OpponentSpellTargetsOneMonster";

        /// <summary>
        /// ActivationCondition: the opponent activated a Spell/Trap Card targeting exactly 1 monster you control, or an
        /// opponent's monster attacks a monster you control (Shift).
        /// </summary>
        public const string OpponentTargetsOneMonsterYouControl = "OpponentTargetsOneMonsterYouControl";

        /// <summary>ConditionName: the answered Chain Link is the activation of a Spell Card.</summary>
        public const string AnswersSpell = "Spell";

        /// <summary>ConditionName: the answered Chain Link is the activation of a Spell or Trap Card.</summary>
        public const string AnswersSpellOrTrap = "SpellTrap";

        static readonly Regex RxTargetFaceUp = new(@"\btarget (?:1|one) face-up\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTargetFaceDown = new(@"\btarget (?:1|one) face-down\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsRedirectCondition(string condition) =>
            condition == OpponentSpellTargetsOneMonster || condition == OpponentTargetsOneMonsterYouControl;

        static bool AnswersAttacks(EffectClause clause) =>
            clause != null && clause.ActivationCondition == OpponentTargetsOneMonsterYouControl;

        // ───────────────────────── activation window ─────────────────────────

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> check (TextEffectRuntime.ActivationConditionsMet): the
        /// window this card answers is open for <paramref name="who"/> — a chain response to a qualifying last link,
        /// or (Shift) an opponent's attack on a monster <paramref name="who"/> controls. Checked at activation only.
        /// </summary>
        public static bool ActivationWindowOpen(DuelEngine engine, DuelistState who, EffectClause clause,
            out string reason)
        {
            reason = clause?.ActivationCondition == OpponentTargetsOneMonsterYouControl
                ? "Only when your opponent targets exactly 1 monster you control with an attack or a Spell/Trap Card."
                : "Only when your opponent activates a Spell Card that targets exactly 1 monster on the field.";
            if (engine == null || who == null || clause == null) return false;
            if (engine.BattleStep == BattleStep.DamageStep)
            {
                reason = "Cannot be activated during the Damage Step.";
                return false;
            }

            var pr = engine.PendingResponse;
            if (pr == null || pr.Responder != who) return false;
            if (pr.Timing == ResponseTiming.ChainResponse)
                return AnsweredLinkQualifies(engine, who, clause, AnsweredLink(engine, who), out _);
            if (pr.Timing == ResponseTiming.AttackDeclared && AnswersAttacks(clause))
                return AttackOnYourMonster(engine, who, out _, out _, out _);
            return false;
        }

        /// <summary>
        /// The Chain Link a chain response window of <paramref name="who"/> answers (the last one). With
        /// <paramref name="ownLinkOnTop"/>, also while <paramref name="who"/>'s own answer is being seated on top of
        /// it (the automatic target pick of TextEffectRuntime.ActivateFreeChainOnChain runs after its link is added).
        /// </summary>
        static ChainLink AnsweredLink(DuelEngine engine, DuelistState who, bool ownLinkOnTop = false)
        {
            var pr = engine?.PendingResponse;
            if (pr == null || pr.Timing != ResponseTiming.ChainResponse || pr.Responder != who) return null;
            var link = pr.ChainTarget;
            var links = engine.Chain?.Links;
            if (link == null || link.Resolved || links == null || links.Count == 0) return null;
            if (ReferenceEquals(link, links[links.Count - 1])) return link;
            if (ownLinkOnTop && links.Count >= 2 && ReferenceEquals(link, links[links.Count - 2]) &&
                links[links.Count - 1].Controller == who)
                return link;
            return null;
        }

        /// <summary>
        /// <paramref name="link"/> is the opponent's activation of a card of the kind <paramref name="clause"/>
        /// answers, targeting exactly 1 monster on the field (Shift: one <paramref name="who"/> controls) that its
        /// resolution will use.
        /// </summary>
        static bool AnsweredLinkQualifies(DuelEngine engine, DuelistState who, EffectClause clause, ChainLink link,
            out CardInstance current)
        {
            current = null;
            if (link?.Card?.Def == null || link.Controller == null || link.Controller == who) return false;
            // "activates a Spell Card" (ygopro EFFECT_TYPE_ACTIVATE): the card itself is activated from the hand or
            // from a Set — not an effect of a card already face-up on the field.
            if (!link.FromHand && !link.WasSetOnField) return false;
            var def = link.Card.Def;
            var kindOk = clause.ConditionName == AnswersSpellOrTrap
                ? def.IsSpell || def.IsTrap
                : clause.ConditionName == AnswersSpell && def.IsSpell && !def.IsTrap;
            if (!kindOk) return false;
            if (!TryRedirectableTarget(engine, link, out current, out _)) return false;
            if (clause.Side == EffectSide.Controller && !who.TryFindMonster(current, out _)) return false;
            return true;
        }

        /// <summary>
        /// The single monster <paramref name="link"/> targets, and its targeted clause, when the link's resolution
        /// applies its effect to <see cref="ChainLink.Target"/> (so a new target changes what it does).
        /// </summary>
        internal static bool TryRedirectableTarget(DuelEngine engine, ChainLink link, out CardInstance current,
            out EffectClause pick)
        {
            current = null;
            pick = null;
            if (engine == null || link?.Card?.Def == null || link.Resolved) return false;
            var target = link.Target ?? (link.Targets.Count == 1 ? link.Targets[0] : null);
            if (target == null) return false;
            // "exactly 1 monster (and no other cards)".
            if (link.Targets.Count > 1 || (link.Targets.Count == 1 && !ReferenceEquals(link.Targets[0], target)))
                return false;
            if (!InMonsterZone(engine, target)) return false;
            var prog = CompiledEffectCache.GetOrCompile(link.Card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clauses = ResolutionClauses(link, prog);
            if (clauses == null || clauses.Count == 0) return false;
            foreach (var c in clauses)
                if (c != null && !SingleTargetResolution(c))
                    return false;
            var picks = clauses.Where(c => c != null && c.RequiresTargetChoice &&
                                           c.Zone != EffectZoneFilter.AttackingMonster &&
                                           c.Zone != EffectZoneFilter.OpponentBattlingMonster).ToList();
            if (picks.Count == 0) return false;
            // The effect targets the card ("Target 1 …"), not a choice at resolution (Fissure).
            if (picks.Exists(c => !c.IsPsctTarget || c.ChoiceDoesNotTarget)) return false;
            current = target;
            pick = picks[0];
            return true;
        }

        /// <summary>
        /// The clauses <paramref name="link"/> applies when it resolves (DuelEngine.ResolveChainStack →
        /// TextEffectRuntime.TryResolveActivation, or TryResolveSeatedResponse for a link seated from a response
        /// window). Null for a link answering a Summon: its target is the Summoned monster.
        /// </summary>
        static List<EffectClause> ResolutionClauses(ChainLink link, CompiledCardProgram prog)
        {
            switch (link.SeatedFrom)
            {
                case ResponseTiming.None:
                case ResponseTiming.OpponentOpenState:
                case ResponseTiming.ChainResponse:
                    return prog.ClausesFor(EffectTiming.Activate);
                case ResponseTiming.AttackDeclared:
                {
                    var list = new List<EffectClause>(prog.ClausesFor(EffectTiming.AttackDeclared));
                    var freeChain = SpellTrapEffects.IsCompiledFreeChain(link.Card.Def, prog);
                    foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                    {
                        if (c == null) continue;
                        if (freeChain || c.OpponentTurnOnly || c.Action == EffectActionKind.ApplyWabokuStyle ||
                            c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn ||
                            c.Action == EffectActionKind.PreventOpponentAttacksThisTurn ||
                            c.Action == EffectActionKind.CannotAttackFaceDownMonstersThisTurn)
                            list.Add(c);
                    }

                    return list;
                }
                case ResponseTiming.YouTakeDamage:
                    return prog.ClausesFor(EffectTiming.YouTakeLifePointDamage);
                default:
                    // MonsterSummoned / SummonNegation (the Summoned monster), DamageCalculation.
                    return null;
            }
        }

        /// <summary>
        /// A clause that resolves against the one recorded target: no second target, no multi-target group, and none
        /// of the resolution paths of TryResolveActivation that choose again or pay at resolution (multi-discard,
        /// Ritual, Tribute-equipped, structured ignition costs, Magical Hats, coin calls, send-named).
        /// </summary>
        static bool SingleTargetResolution(EffectClause c)
        {
            if (c.RequiresSecondTarget || c.ControllerTargetCount > 1 || c.OpponentTargetCount > 0) return false;
            if (c.RequiresTargetChoice && (c.TargetCount > 1 || c.TargetUpTo || c.DistinctTargetGroup)) return false;
            if (c.DiscardCostCount > 0 || c.RequiresDiscardCost || c.RequiresTributeEquippedMonster) return false;
            if (c.RequiresTributeThis || c.RequiresTributeCount > 0 || c.RequiresDiscardSelf ||
                c.RequiresSendThisToGy || c.RequiresSendHandToGy || c.RequiresSendOtherYouControl ||
                c.PayLpAmount > 0 || c.BanishFromGyCount > 0 || c.RequiresRemoveSpellCounters > 0 ||
                c.RequiresSendNamedToGy || c.RequiresSendEquipSpellOnThisToGy)
                return false;
            switch (c.Action)
            {
                case EffectActionKind.RitualSummon:
                case EffectActionKind.MagicalHatsStyle:
                case EffectActionKind.CoinCallDestroyOppOrSelf:
                case EffectActionKind.CoinCallDoubleOrHalveAtk:
                case EffectActionKind.RedirectToAnotherAppropriateTarget:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Shift's attack window: an opponent's monster attacks a monster <paramref name="who"/> controls
        /// (ygopro EVENT_BE_BATTLE_TARGET with <c>a:IsControler(1-tp) and d:IsControler(tp)</c>).
        /// </summary>
        static bool AttackOnYourMonster(DuelEngine engine, DuelistState who, out CardInstance attacker,
            out DuelistState attackingPlayer, out CardInstance current)
        {
            attacker = engine?.DeclaredAttacker;
            attackingPlayer = engine?.DeclaredAttackingPlayer;
            current = engine?.DeclaredAttackTarget;
            if (engine == null || who == null || attacker == null || attackingPlayer == null || current == null)
                return false;
            if (attackingPlayer == who || engine.OpponentOf(who) != attackingPlayer) return false;
            if (!attackingPlayer.TryFindMonster(attacker, out _)) return false;
            return who.TryFindMonster(current, out _);
        }

        // ───────────────────────── candidates (EffectZoneFilter.AnotherAppropriateTarget) ─────────────────────────

        /// <summary>
        /// The legal new targets for <paramref name="clause"/>: in a chain response window (or while this Trap
        /// resolves) another monster that would be an appropriate target for the answered link; in Shift's attack
        /// window another monster <paramref name="who"/> controls that the attacker could attack. Monsters this Trap
        /// cannot target (<see cref="ContinuousProtections.TargetedEffectBlocked"/>) are left out.
        /// </summary>
        public static List<CardInstance> Candidates(DuelEngine engine, DuelistState who, EffectClause clause,
            CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || clause == null) return list;
            var pr = engine.PendingResponse;
            if (pr != null && pr.Timing == ResponseTiming.AttackDeclared)
            {
                if (!AnswersAttacks(clause) || pr.Responder != who) return list;
                if (!AttackOnYourMonster(engine, who, out var attacker, out var attackingPlayer, out var current))
                    return list;
                foreach (var m in who.MonstersOnField())
                {
                    if (m == null || ReferenceEquals(m, current)) continue;
                    if (!AttackerCouldAttack(engine, attackingPlayer, attacker, m)) continue;
                    if (ContinuousProtections.TargetedEffectBlocked(engine, m, source)) continue;
                    list.Add(m);
                }

                return list;
            }

            // Chain mode: the window's link, or — while this Trap resolves — the link right below it.
            var link = pr != null && pr.Timing == ResponseTiming.ChainResponse
                ? AnsweredLink(engine, who)
                : engine.Chain?.Last;
            if (!AnsweredLinkQualifies(engine, who, clause, link, out var target)) return list;
            if (!TryRedirectableTarget(engine, link, out _, out var pick)) return list;
            foreach (var m in AppropriateTargets(engine, link, pick))
            {
                if (m == null || ReferenceEquals(m, target)) continue;
                if (clause.Side == EffectSide.Controller && !who.TryFindMonster(m, out _)) continue;
                if (ContinuousProtections.TargetedEffectBlocked(engine, m, source)) continue;
                if (!list.Contains(m)) list.Add(m);
            }

            return list;
        }

        /// <summary>
        /// Monsters on the field that <paramref name="link"/>'s card could target with <paramref name="pick"/> —
        /// ygopro Duel.CheckChainTarget: the same list its controller was offered when activating it (the compiled
        /// target list, narrowed to the legacy SpellTrapEffects list when that path chose the target), plus the
        /// printed face-up / face-down requirement of "Target 1 face-up monster".
        /// </summary>
        static IEnumerable<CardInstance> AppropriateTargets(DuelEngine engine, ChainLink link, EffectClause pick)
        {
            var owner = link.Controller;
            var legal = TextEffectRuntime.CollectTargets(engine, owner, pick, link.Card);
            if (link.SeatedFrom == ResponseTiming.None)
            {
                var kind = SpellTrapEffects.GetTargetKind(link.Card.Def);
                if (kind != EffectTargetKind.None)
                {
                    var legacy = SpellTrapEffects.CollectLegalTargets(engine, owner, kind, link.Card);
                    legal.RemoveAll(t => !legacy.Contains(t));
                }
            }

            var snippet = pick.SourceSnippet ?? "";
            var faceUpOnly = RxTargetFaceUp.IsMatch(snippet);
            var faceDownOnly = RxTargetFaceDown.IsMatch(snippet);
            foreach (var t in legal)
            {
                if (t == null || !InMonsterZone(engine, t)) continue;
                if (faceUpOnly && !t.FaceUp) continue;
                if (faceDownOnly && t.FaceUp) continue;
                yield return t;
            }
        }

        /// <summary>
        /// <paramref name="attacker"/> (controlled by <paramref name="attackingPlayer"/>) could attack
        /// <paramref name="candidate"/> — the target checks of DuelEngine.TryAttack (ygopro GetAttackableTarget).
        /// </summary>
        internal static bool AttackerCouldAttack(DuelEngine engine, DuelistState attackingPlayer, CardInstance attacker,
            CardInstance candidate)
        {
            var defender = engine.OpponentOf(attackingPlayer);
            if (defender == null || attacker == null || candidate == null) return false;
            if (!defender.TryFindMonster(candidate, out _)) return false;
            if (attackingPlayer.MustAttackDirectlyThisTurn) return false;
            if (ContinuousProtections.CannotBeAttackTarget(engine, candidate)) return false;
            var magnet = engine.MagnetismHosts(attackingPlayer);
            if (magnet.Count > 1 || (magnet.Count == 1 && !ReferenceEquals(magnet[0], candidate))) return false;
            var forced = TextEffectRuntime.ForcedAttackTargets(defender);
            return forced.Count == 0 || forced.Contains(candidate);
        }

        static bool InMonsterZone(DuelEngine engine, CardInstance card) =>
            card != null && ((engine.Player?.TryFindMonster(card, out _) ?? false) ||
                             (engine.Opponent?.TryFindMonster(card, out _) ?? false));

        // ───────────────────────── resolution ─────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.RedirectToAnotherAppropriateTarget"/> with its target
        /// <paramref name="newTarget"/>. Chain answer: the link right below this one (the one it answered) now
        /// targets <paramref name="newTarget"/>, if it is still in a Monster Zone (ygopro IsRelateToEffect then
        /// Duel.ChangeTargetCard). Shift's attack answer (no link left): the attack now targets it, if it is still a
        /// monster Shift's controller controls, the attacker is not unaffected by Shift and could attack it.
        /// </summary>
        public static void ResolveRedirect(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance newTarget)
        {
            if (engine == null || who == null || clause == null) return;
            var name = source?.Name ?? "Redirect";
            if (newTarget == null)
            {
                engine.Log($"{name}: no new target — no effect.");
                return;
            }

            var link = engine.Chain?.Last;
            if (link != null && !link.Resolved)
            {
                if (!InMonsterZone(engine, newTarget))
                {
                    engine.Log($"{name}: {newTarget.Name} is no longer on the field — no effect.");
                    return;
                }

                var oldTarget = link.Target ?? (link.Targets.Count == 1 ? link.Targets[0] : null);
                link.Target = newTarget;
                link.Targets.Clear();
                link.Targets.Add(newTarget);
                // EngineResiduals (v163): a compiled activation recorded its target (Offerings to the Doomed).
                CompiledActivationChainRuntime.Retarget(engine, link, oldTarget, newTarget);
                engine.Log($"{name}: {link.Card?.Name ?? "that card"} now targets " +
                           $"{(newTarget.FaceUp ? newTarget.Name : "a face-down monster")}.");
                return;
            }

            if (AnswersAttacks(clause) && engine.HasDeclaredAttack)
            {
                var attacker = engine.DeclaredAttacker;
                var attackingPlayer = engine.DeclaredAttackingPlayer;
                if (engine.DeclaredAttackTarget == null || attackingPlayer == null ||
                    !attackingPlayer.TryFindMonster(attacker, out _))
                {
                    engine.Log($"{name}: the attack no longer targets a monster — no effect.");
                    return;
                }

                if (!who.TryFindMonster(newTarget, out _))
                {
                    engine.Log($"{name}: {newTarget.Name} is no longer on your field — no effect.");
                    return;
                }

                if (ContinuousProtections.IsUnaffectedBy(engine, attacker, source))
                {
                    engine.Log($"{name}: {attacker.Name} is unaffected — the attack target does not change.");
                    return;
                }

                if (!AttackerCouldAttack(engine, attackingPlayer, attacker, newTarget))
                {
                    engine.Log($"{name}: {attacker.Name} cannot attack {newTarget.Name} — no effect.");
                    return;
                }

                engine.ChangeDeclaredAttackTarget(newTarget);
                return;
            }

            engine.Log($"{name}: nothing left to redirect — no effect.");
        }

        // ───────────────────────── AI ─────────────────────────

        static EffectClause RedirectClause(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.Activate)?.Find(c =>
                c != null && c.Action == EffectActionKind.RedirectToAnotherAppropriateTarget);
        }

        /// <summary>A resolution the targeted monster's controller would rather it missed.</summary>
        static bool IsHarmful(EffectClause pick)
        {
            switch (EffectVocabulary.ResolutionOf(pick))
            {
                case EffectResolutionKind.Destroy:
                case EffectResolutionKind.Banish:
                case EffectResolutionKind.ReturnToHand:
                case EffectResolutionKind.ReturnToDeck:
                case EffectResolutionKind.TakeControl:
                case EffectResolutionKind.ChangePosition:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// AI chain answer (SpellTrapEffects.AiAutoRespond): <paramref name="card"/> redirects the opponent's link
        /// when it would harm one of the AI's monsters and a better target exists (an opponent's monster, or a
        /// weaker monster of its own), or — Fairy's Hand Mirror — when it helps an opponent's monster and an AI
        /// monster could take it instead. The window's legality was checked when the card was offered.
        /// </summary>
        public static bool AiWantsToRedirect(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card == null) return false;
            var clause = RedirectClause(card);
            if (clause == null) return false;
            var link = AnsweredLink(engine, ai);
            if (!AnsweredLinkQualifies(engine, ai, clause, link, out var current)) return false;
            if (!TryRedirectableTarget(engine, link, out _, out var pick)) return false;
            var candidates = Candidates(engine, ai, clause, card);
            if (candidates.Count == 0) return false;
            var currentMine = ai.TryFindMonster(current, out _);
            if (IsHarmful(pick))
            {
                if (!currentMine) return false;
                return candidates.Exists(m => !ai.TryFindMonster(m, out _)) ||
                       candidates.Exists(m => Value(m) < Value(current));
            }

            return !currentMine && candidates.Exists(m => ai.TryFindMonster(m, out _));
        }

        /// <summary>
        /// AI answer to an attack on one of its monsters (Shift): redirect when the attacked monster would lose the
        /// battle and another of its monsters would not.
        /// </summary>
        public static bool AiWantsToRedirectAttack(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine?.PendingResponse == null || engine.PendingResponse.Timing != ResponseTiming.AttackDeclared)
                return false;
            var clause = RedirectClause(card);
            if (!AnswersAttacks(clause)) return false;
            if (!AttackOnYourMonster(engine, ai, out var attacker, out _, out var current)) return false;
            if (SurvivesAttack(attacker, current)) return false;
            return Candidates(engine, ai, clause, card).Exists(m => SurvivesAttack(attacker, m));
        }

        /// <summary>AutoPick for <see cref="EffectZoneFilter.AnotherAppropriateTarget"/>.</summary>
        public static CardInstance AiPick(DuelEngine engine, DuelistState who, EffectClause clause,
            List<CardInstance> targets)
        {
            if (engine == null || who == null || targets == null || targets.Count == 0) return null;
            var pr = engine.PendingResponse;
            if (pr != null && pr.Timing == ResponseTiming.AttackDeclared &&
                AttackOnYourMonster(engine, who, out var attacker, out _, out _))
            {
                return targets.Where(m => SurvivesAttack(attacker, m)).OrderByDescending(Value).FirstOrDefault()
                       ?? targets.OrderBy(Value).First();
            }

            var link = pr != null && pr.Timing == ResponseTiming.ChainResponse
                ? AnsweredLink(engine, who, ownLinkOnTop: true)
                : engine.Chain?.Last;
            var harmful = !TryRedirectableTarget(engine, link, out _, out var pick) || IsHarmful(pick);
            var theirs = targets.Where(m => !who.TryFindMonster(m, out _)).ToList();
            var mine = targets.Where(m => who.TryFindMonster(m, out _)).ToList();
            if (harmful)
                return theirs.OrderByDescending(Value).FirstOrDefault() ?? mine.OrderBy(Value).First();
            return mine.OrderByDescending(Value).FirstOrDefault() ?? theirs.OrderBy(Value).First();
        }

        static int Value(CardInstance m) =>
            m == null ? 0 : m.FaceUp ? Math.Max(m.CurrentAtk, m.CurrentDef) : Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0);

        /// <summary>Rough battle check: <paramref name="defender"/> is not destroyed by <paramref name="attacker"/>.</summary>
        static bool SurvivesAttack(CardInstance attacker, CardInstance defender)
        {
            if (attacker == null || defender == null) return false;
            var atk = attacker.CurrentAtk;
            if (defender.Position == BattlePosition.Attack)
                return defender.FaceUp && defender.CurrentAtk > atk;
            var def = defender.FaceUp ? defender.CurrentDef : Math.Max(0, defender.Def?.def ?? 0);
            return def >= atk;
        }

        // ───────────────────────── one effect at a time (Kotodama, Banisher of the Light) ─────────────────────────

        static int s_effectDepth;
        static bool s_sweeping;

        /// <summary>
        /// SummonLocksAndStatRules batch: a clause (or one set of Tributes) is being applied right now
        /// (<see cref="EnterEffectApplication"/>). EVENT_ADJUST sweeps wait until it is over.
        /// </summary>
        public static bool ApplyingEffect => s_effectDepth > 0;

        /// <summary>Engine of the outermost open <see cref="EnterEffectApplication"/> scope.</summary>
        static DuelEngine s_scopeEngine;

        /// <summary>The Banishers of the Light active when the outermost scope opened.</summary>
        static readonly List<CardInstance> s_banishersAtScopeStart = new();

        /// <summary>
        /// Marks one clause being applied (TextEffectRuntime.ApplyClause) or one set of Tributes being sent: what it
        /// does happens at the same time. Kotodama looks only once it is done (two Tokens Summoned together are both
        /// new), and a Banisher of the Light that was face-up when it began still banishes every card it sends to
        /// the GY after the Banisher itself has left the field (Dark Hole destroys it with the rest).
        /// </summary>
        public readonly struct EffectApplicationScope : IDisposable
        {
            public void Dispose()
            {
                if (s_effectDepth > 0) s_effectDepth--;
                if (s_effectDepth != 0) return;
                var engine = s_scopeEngine;
                s_scopeEngine = null;
                s_banishersAtScopeStart.Clear();
                // The Equip Cards its destructions / Tributes left behind are sent by rule now, after everything it
                // sent (a Banisher that left with them no longer applies).
                FlushDeferredRuleSends(engine);
            }
        }

        public static EffectApplicationScope EnterEffectApplication(DuelEngine engine)
        {
            if (s_effectDepth++ == 0)
            {
                s_scopeEngine = engine;
                s_banishersAtScopeStart.Clear();
                if (engine != null)
                    s_banishersAtScopeStart.AddRange(ActiveBanishers(engine));
            }

            return default;
        }

        // ───────────────────────── Banisher of the Light ─────────────────────────

        /// <summary>The engine and monsters of the last <see cref="NoteBattleDestroyed"/>.</summary>
        static DuelEngine s_battleEngine;
        static readonly List<CardInstance> s_battleDestroyed = new();

        /// <summary>
        /// DuelEngine.ApplyBattleDestructions (Damage Step, <see cref="DamageSubStep.End"/>): the monsters this battle
        /// destroys. A monster destroyed by battle has no continuous effect while its destruction is applied (ygopro
        /// STATUS_BATTLE_DESTROYED, no AVAILABLE_BD), so a Banisher of the Light destroyed by battle goes to the GY,
        /// and so does the monster it battled if that one is destroyed too.
        /// </summary>
        public static void NoteBattleDestroyed(DuelEngine engine, CardInstance destroyedAttacker,
            CardInstance destroyedDefender)
        {
            s_battleEngine = engine;
            s_battleDestroyed.Clear();
            if (destroyedAttacker != null) s_battleDestroyed.Add(destroyedAttacker);
            if (destroyedDefender != null) s_battleDestroyed.Add(destroyedDefender);
        }

        /// <summary><paramref name="m"/> is destroyed by the battle whose destructions are being applied now.</summary>
        static bool DestroyedByCurrentBattle(DuelEngine engine, CardInstance m) =>
            m != null && ReferenceEquals(s_battleEngine, engine) && engine.DamageSubStep == DamageSubStep.End &&
            s_battleDestroyed.Contains(m);

        static bool IsBanisherSource(CardInstance m)
        {
            if (m?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                c != null && c.Action == EffectActionKind.BanishCardsSentToGyInstead);
        }

        /// <summary>
        /// A Banisher of the Light whose effect applies now: face-up in a Monster Zone with its Summon done, not
        /// negated, and not destroyed by the battle being applied.
        /// </summary>
        static bool IsActiveBanisher(DuelEngine engine, CardInstance m) =>
            IsFaceUpOnField(engine, m) && !m.IsNegated && !DestroyedByCurrentBattle(engine, m) && IsBanisherSource(m);

        static List<CardInstance> ActiveBanishers(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            if (engine?.Player == null || engine.Opponent == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var m in side.MonstersOnField())
                if (IsActiveBanisher(engine, m))
                    list.Add(m);
            return list;
        }

        /// <summary>
        /// <paramref name="card"/>, about to be sent to the GY, is banished instead (Banisher of the Light, ygopro
        /// EFFECT_TO_GRAVE_REDIRECT: any card, face-down or unaffected ones included, from any location of either
        /// player). Read while the card is still where it is, so a face-up Banisher sent to the GY applies to itself.
        /// A card already in a GY or banished is not being sent.
        /// </summary>
        public static bool SendsToBanishedInstead(DuelEngine engine, CardInstance card)
        {
            if (engine?.Player == null || engine.Opponent == null || card == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side.Graveyard.Contains(card) || side.Banished.Contains(card))
                    return false;
            if (IsActiveBanisher(engine, card))
            {
                // A Banisher sent to the GY applies to itself. Sent as one of several picks of a cost the player
                // is still paying (Tribute 2 monsters), the other picks are sent at the same time: remember it.
                s_banisherLeftDuring = engine.PendingActivation is { AwaitingIgnitionCost: true } paying ? paying : null;
                return true;
            }

            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var m in side.MonstersOnField())
                if (IsActiveBanisher(engine, m))
                    return true;

            // A card sent by rule after another card left (an Equip Card whose monster is gone) is not sent at the
            // same time as it: only a Banisher still on the field applies.
            if (s_ruleSendDepth > 0) return false;

            // Sent at the same time as a Banisher that was face-up when this effect began and has left the field
            // since (Dark Hole, a Tribute Summon that Tributes it with another monster).
            if (s_effectDepth > 0 && ReferenceEquals(s_scopeEngine, engine))
                foreach (var k in s_banishersAtScopeStart)
                    if (!InMonsterZone(engine, k) && !DestroyedByCurrentBattle(engine, k))
                        return true;
            // The rest of a cost whose earlier pick was a face-up Banisher (the same pending activation).
            return engine.PendingActivation is { AwaitingIgnitionCost: true } &&
                   ReferenceEquals(engine.PendingActivation, s_banisherLeftDuring);
        }

        static int s_ruleSendDepth;

        /// <summary>Scope of <see cref="EnterRuleSend"/>.</summary>
        public readonly struct RuleSendScope : IDisposable
        {
            public void Dispose()
            {
                if (s_ruleSendDepth > 0) s_ruleSendDepth--;
            }
        }

        /// <summary>
        /// DuelEngine.UnlinkEquips: its Equip Cards are sent to the GY by rule after their monster left the field, not
        /// at the same time: a Banisher of the Light that left the field with (or as) that monster no longer applies
        /// to them.
        /// </summary>
        public static RuleSendScope EnterRuleSend()
        {
            s_ruleSendDepth++;
            return default;
        }

        /// <summary>
        /// A card DuelEngine.UnlinkEquips must send to the GY by rule once the cards sent with its monster are all
        /// gone: <see cref="Cost"/> null = at the end of the open <see cref="EnterEffectApplication"/> scope; else once
        /// that pick-by-pick cost is paid (or given up).
        /// </summary>
        readonly struct DeferredRuleSend
        {
            public readonly DuelEngine Engine;
            public readonly PendingActivation Cost;
            public readonly CardInstance Card;
            public readonly DuelistState FallbackOwner;

            public DeferredRuleSend(DuelEngine engine, PendingActivation cost, CardInstance card, DuelistState fallback)
            {
                Engine = engine;
                Cost = cost;
                Card = card;
                FallbackOwner = fallback;
            }
        }

        static readonly List<DeferredRuleSend> s_deferredRuleSends = new();

        /// <summary>
        /// DuelEngine.UnlinkEquips: <paramref name="card"/> (an Equip / link card whose monster just left the field)
        /// is sent to the GY by rule AFTER the event that removed its monster, not at the same time. While a Banisher
        /// of the Light is involved and that event is still going on — one effect / one set of Tributes
        /// (<see cref="EnterEffectApplication"/>: Dark Hole destroys the opponent's monsters before the Banisher), or a
        /// Tribute cost paid pick by pick with picks left — whether it is banished depends on whether a Banisher is
        /// still on the field when the event is over, so the send waits until then (<see cref="FlushDeferredRuleSends"/>).
        /// Returns true when the send was deferred. Without a Banisher nothing changes.
        /// </summary>
        public static bool TryDeferRuleSend(DuelEngine engine, CardInstance card, DuelistState fallbackOwner)
        {
            if (engine?.Player == null || engine.Opponent == null || card == null) return false;
            if (s_effectDepth > 0 && ReferenceEquals(s_scopeEngine, engine))
            {
                if (s_banishersAtScopeStart.Count == 0 && ActiveBanishers(engine).Count == 0) return false;
                s_deferredRuleSends.Add(new DeferredRuleSend(engine, null, card, fallbackOwner));
                return true;
            }

            if (engine.PendingActivation is { AwaitingIgnitionCost: true, CostPicksRemaining: > 1 } paying &&
                ActiveBanishers(engine).Count > 0)
            {
                s_deferredRuleSends.Add(new DeferredRuleSend(engine, paying, card, fallbackOwner));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Send the deferred rule sends of <paramref name="engine"/> whose event is over (see
        /// <see cref="TryDeferRuleSend"/>): at the end of the outermost effect scope, and on each board refresh for a
        /// cost that is no longer being paid. A card that has left its zone meanwhile is skipped. Sends deferred for
        /// another engine (an abandoned duel) are dropped.
        /// </summary>
        public static void FlushDeferredRuleSends(DuelEngine engine)
        {
            if (s_deferredRuleSends.Count == 0) return;
            var ready = new List<DeferredRuleSend>();
            for (var i = s_deferredRuleSends.Count - 1; i >= 0; i--)
            {
                var d = s_deferredRuleSends[i];
                if (!ReferenceEquals(d.Engine, engine))
                {
                    if (engine != null) s_deferredRuleSends.RemoveAt(i);
                    continue;
                }

                var over = d.Cost == null
                    ? s_effectDepth == 0
                    : !ReferenceEquals(engine.PendingActivation, d.Cost) || !d.Cost.AwaitingIgnitionCost;
                if (!over) continue;
                s_deferredRuleSends.RemoveAt(i);
                ready.Insert(0, d);
            }

            if (engine == null || ready.Count == 0) return;
            using var byRule = EnterRuleSend();
            foreach (var d in ready)
            {
                if (engine.GameOver) return;
                var holder = engine.Player.TryFindSpellTrap(d.Card, out _) ? engine.Player
                    : engine.Opponent.TryFindSpellTrap(d.Card, out _) ? engine.Opponent
                    : null;
                if (holder == null) continue; // already gone (destroyed by the same effect, returned, …)
                engine.SendCardToGrave(engine.ControllerOf(d.Card) ?? holder ?? d.FallbackOwner, d.Card);
            }
        }

        /// <summary>
        /// The pending activation (a cost being paid pick by pick) during which an active Banisher of the Light was
        /// itself sent to the GY; see <see cref="SendsToBanishedInstead"/>.
        /// </summary>
        static PendingActivation s_banisherLeftDuring;

        /// <summary>
        /// Banish <paramref name="card"/> into <paramref name="pile"/> (its owner's) instead of sending it to the
        /// GY. It never reaches the GY: the caller fires no "sent to the GY" event or trigger. A Token leaves play.
        /// </summary>
        public static void BanishInsteadOfGy(DuelEngine engine, DuelistState pile, CardInstance card)
        {
            if (pile == null || card == null) return;
            if (card.IsToken)
            {
                NoteTokenBanishedInstead(engine, card);
                engine?.Log($"{card.Name} is banished instead of being sent to the GY (a Token leaves play).");
                return;
            }

            card.FaceUp = true;
            if (!pile.Banished.Contains(card))
                pile.Banished.Add(card);
            // Every engine banish path stamps the turn (BanishedReturn: D.D. Scout Plane "was banished this turn").
            if (engine != null) card.BanishedTurnNumber = engine.TurnNumber;
            engine?.Log($"{card.Name} is banished instead of being sent to the GY.");
        }

        /// <summary>
        /// Put <paramref name="card"/> (already removed from where it was) in <paramref name="pile"/>'s GY, or
        /// banish it instead while a Banisher of the Light applies. <paramref name="engine"/> may be null for the
        /// helpers that only have the player (<see cref="DuelistState.Engine"/>). Returns true when it went to the GY.
        /// </summary>
        public static bool PutInGraveyard(DuelEngine engine, DuelistState pile, CardInstance card)
        {
            if (pile == null || card == null) return false;
            // PerCopyDeck batch (v163): a card its holder does not own (a drawn Parasite Paracide, a Spell Graverobber
            // took) goes to its owner's GY.
            pile = PerCopyDeckRuntime.OwnersPile(pile, card);
            engine ??= pile.Engine;
            if (engine != null && SendsToBanishedInstead(engine, card))
            {
                BanishInsteadOfGy(engine, pile, card);
                return false;
            }

            // FlipCostsAndFusion batch: a new trip to the GY is not as Fusion Material (TryResolveFusion stamps after).
            card.SentAsFusionMaterialFor = null;
            if (!pile.Graveyard.Contains(card))
                pile.Graveyard.Add(card);
            // BattleDestroyedGyRecovery (v162): GY-entry stamp; the caller's discard / destroy record adds the reason.
            GyEntryStamps.Entered(engine, card);
            return true;
        }

        static readonly Regex RxSendsItToGy = new(@"\bsends? it to the (?:GY|Graveyard)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle: <paramref name="c"/> triggers when this card "destroys
        /// a monster … and sends it to the GY" (Guardian Angel Joan, KA-2 Des Scissors), but
        /// <paramref name="destroyed"/> did not reach the GY (banished instead, or a Token, which leaves play), so it does
        /// not trigger (ygopro aux.bdgcon / damcon: bc:IsLocation(LOCATION_GRAVE); EngineHygiene v160 review).
        /// </summary>
        public static bool NeedsGyButWasBanished(DuelEngine engine, EffectClause c, CardInstance destroyed)
        {
            if (engine?.Player == null || engine.Opponent == null || c == null || destroyed == null) return false;
            if (!RxSendsItToGy.IsMatch(c.SourceSnippet ?? "")) return false;
            if (engine.Player.Banished.Contains(destroyed) || engine.Opponent.Banished.Contains(destroyed) ||
                (destroyed.IsToken && ReferenceEquals(s_tokensEngine, engine) && s_tokensBanishedInstead.Contains(destroyed)))
                return true;
            return !(engine.Player.Graveyard?.Contains(destroyed) ?? false) &&
                   !(engine.Opponent.Graveyard?.Contains(destroyed) ?? false);
        }

        /// <summary>Tokens banished instead of being sent to the GY (they leave play, so no Banished pile holds them).</summary>
        static DuelEngine s_tokensEngine;
        static readonly List<CardInstance> s_tokensBanishedInstead = new();

        /// <summary>Remember that Token <paramref name="token"/> was banished instead of being sent to the GY.</summary>
        public static void NoteTokenBanishedInstead(DuelEngine engine, CardInstance token)
        {
            if (engine == null || token == null) return;
            if (!ReferenceEquals(s_tokensEngine, engine) || s_tokensBanishedInstead.Count > 64)
            {
                s_tokensEngine = engine;
                s_tokensBanishedInstead.Clear();
            }

            if (!s_tokensBanishedInstead.Contains(token))
                s_tokensBanishedInstead.Add(token);
        }

        static readonly Regex RxSentToGyWording = new(@"\bsent\b[^.:;]*?\b(?:GY|Graveyard)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// A "destroyed" trigger of the destroyed card itself that does not need it in the GY ("If this card is
        /// destroyed by battle:" — Slate Warrior, Lua EVENT_BATTLE_DESTROYED, which activates wherever the card went),
        /// as opposed to "destroyed … and sent to the GY" / "sent to the Graveyard as a result of battle".
        /// </summary>
        public static bool IsDestroyedTriggerWithoutGy(EffectClause c) =>
            c != null && c.Timing == EffectTiming.SentFromFieldToGy &&
            (c.RequiresDestroyed || c.RequiresThisDestroyedByBattle) &&
            !RxSentToGyWording.IsMatch(c.SourceSnippet ?? "");

        /// <summary>
        /// DuelEngine.DestroyMonster: <paramref name="card"/> was destroyed and banished instead of being sent to the
        /// GY (Banisher of the Light, or a "destroy and banish" effect). It fires no "sent to the GY" trigger, but its
        /// own "if this card is destroyed (by battle)" triggers still activate (<see cref="IsDestroyedTriggerWithoutGy"/>).
        /// FullyCompiled programs only.
        /// </summary>
        public static void ResolveDestroyedTriggersOutsideGy(DuelEngine engine, DuelistState owner, CardInstance card,
            bool destroyedByBattle, CardInstance battleDestroyer)
        {
            if (engine == null || owner == null || card?.Def == null || engine.GameOver) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return;
            if (!prog.ClausesFor(EffectTiming.SentFromFieldToGy).Exists(IsDestroyedTriggerWithoutGy)) return;
            TextEffectRuntime.TryResolveSentToGy(engine, owner, card, prog, destroyed: true, destroyedByBattle,
                battleDestroyer, only: IsDestroyedTriggerWithoutGy);
        }

        /// <summary>
        /// A MonsterSummoned-window answer whose clauses act on the Summoned monster (zone
        /// <see cref="EffectZoneFilter.FieldAnyMonster"/>: Trap Hole, Bottomless Trap Hole, Adhesion Trap Hole, Chain
        /// Destruction, Shadow of Eyes …) needs that monster still in a Monster Zone (Lua IsOnField /
        /// IsLocation(LOCATION_MZONE)). Kotodama destroys a Summoned duplicate before anyone gets priority.
        /// </summary>
        public static bool SummonAnswerLostItsMonster(DuelEngine engine, CardInstance summoned,
            List<EffectClause> clauses)
        {
            if (engine?.Player == null || engine.Opponent == null || summoned == null || clauses == null) return false;
            if (InMonsterZone(engine, summoned)) return false;
            return clauses.Exists(c => c != null && c.Zone == EffectZoneFilter.FieldAnyMonster);
        }

        // ───────────────────────── Kotodama ─────────────────────────

        static bool IsSameNameSource(CardInstance m)
        {
            if (m?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                c != null && c.Action == EffectActionKind.DestroyNewFaceUpMonstersSharingAName);
        }

        /// <summary>A face-up monster on the field whose Summon has succeeded (not in its summon-negation window).</summary>
        static bool IsFaceUpOnField(DuelEngine engine, CardInstance m) =>
            m != null && m.FaceUp && !engine.IsSummonPending(m) && InMonsterZone(engine, m);

        static bool IsActiveSource(DuelEngine engine, CardInstance k) =>
            IsFaceUpOnField(engine, k) && !k.IsNegated && IsSameNameSource(k);

        static List<CardInstance> FaceUpMonsters(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (IsFaceUpOnField(engine, m))
                        list.Add(m);
            }

            return list;
        }

        static string RulesName(CardInstance m)
        {
            if (m == null) return null;
            FieldSpellEffects.ApplyRuleConditions(m);
            return m.RulesName;
        }

        /// <summary>
        /// <paramref name="c"/> is in <paramref name="seen"/> with the same field presence and without having been
        /// turned face-down since (one that left and came back, or was flipped face-down and up again, is new).
        /// </summary>
        static bool Saw(List<(CardInstance Card, int EntryStamp, int FaceDownStamp)> seen, CardInstance c) =>
            seen != null && seen.Exists(e =>
                ReferenceEquals(e.Card, c) && e.EntryStamp == c.FieldEntryStamp && e.FaceDownStamp == c.FaceDownStamp);

        /// <summary>
        /// Kotodama's continuous effect (FieldSpellEffects.RefreshBoard, after the auras): for each active source
        /// (face-up on the field, not negated), every face-up monster it has not seen yet that shares its name with
        /// another face-up monster is destroyed by that source's effect; then it remembers the face-up monsters it
        /// saw, less those (a monster it failed to destroy stays unseen and is tried again). The first look after the source
        /// becomes active has seen nothing, so every face-up monster sharing a name is destroyed. Sources that were
        /// already looking go first, so a second copy Summoned next to one is the new monster that is destroyed.
        /// Not while an effect is being applied, a target is being chosen, or during the Damage Step before damage
        /// calculation / during damage calculation (ygopro adjustop).
        /// </summary>
        public static void SweepSameNameDestruction(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver) return;
            // Banisher of the Light: a pick-by-pick cost that is paid (or given up) sends its deferred Equip Cards.
            if (s_effectDepth == 0) FlushDeferredRuleSends(engine);
            if (s_sweeping || s_effectDepth > 0 || engine.PendingActivation != null) return;
            if (engine.BattleStep == BattleStep.DamageStep &&
                (engine.DamageSubStep == DamageSubStep.Start ||
                 engine.DamageSubStep == DamageSubStep.BeforeDamageCalculation ||
                 engine.DamageSubStep == DamageSubStep.DamageCalculation))
                return;

            var sources = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null) continue;
                    if (IsActiveSource(engine, m))
                    {
                        if (m.SameNameSeenFaceUp != null &&
                            (m.SameNameSeenEntryStamp != m.FieldEntryStamp ||
                             m.SameNameSeenFaceDownStamp != m.FaceDownStamp))
                            m.SameNameSeenFaceUp = null; // a new field presence / flipped face-down since: start over
                        sources.Add(m);
                    }
                    else
                        m.SameNameSeenFaceUp = null; // face-down, negated or not yet Summoned: the effect resets
                }
            }

            if (sources.Count == 0) return;
            s_sweeping = true;
            try
            {
                foreach (var k in sources.OrderBy(s => s.SameNameSeenFaceUp == null ? 1 : 0).ToList())
                {
                    if (engine.GameOver) return;
                    if (!IsActiveSource(engine, k)) continue; // destroyed by an earlier source's pass
                    var seen = k.SameNameSeenFaceUp;
                    var faceUp = FaceUpMonsters(engine);
                    var doomed = faceUp.Where(c =>
                    {
                        if (Saw(seen, c)) return false;
                        var n = RulesName(c);
                        return !string.IsNullOrEmpty(n) && faceUp.Exists(o =>
                            !ReferenceEquals(o, c) &&
                            string.Equals(RulesName(o), n, StringComparison.OrdinalIgnoreCase));
                    }).ToList();

                    // They are destroyed at the same time (ygopro Duel.Destroy(dg)).
                    using (EnterEffectApplication(engine))
                    {
                        foreach (var c in doomed)
                        {
                            if (!IsFaceUpOnField(engine, c)) continue;
                            // Unaffected by Kotodama: not destroyed now, looked at again next time.
                            if (ContinuousProtections.IsUnaffectedBy(engine, c, k)) continue;
                            var owner = engine.ControllerOf(c);
                            if (owner == null) continue;
                            engine.Log($"{k.Name}: {c.Name} shares its name with another face-up monster — destroyed.");
                            engine.DestroyMonsterPublic(owner, c, k);
                            if (engine.GameOver) return;
                        }
                    }

                    if (!IsActiveSource(engine, k)) continue;
                    // What it saw this time, less what it tried to destroy (ygopro pg = g - dg). Not a fresh scan: a
                    // monster Summoned by a trigger of the destruction (resolved right away here, chained later in
                    // ygopro) arrives after this look and is new next time.
                    k.SameNameSeenFaceUp = faceUp.Where(c => !doomed.Contains(c))
                        .Select(c => (c, c.FieldEntryStamp, c.FaceDownStamp)).ToList();
                    k.SameNameSeenEntryStamp = k.FieldEntryStamp;
                    k.SameNameSeenFaceDownStamp = k.FaceDownStamp;
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }
    }
}
