using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RuleLocks": continuous rule locks read at the DuelEngine entry points and in the legality checks
    /// (<see cref="DuelEngine.CanNormalSummonOrSet"/>, <see cref="DuelEngine.CanFlipSummon"/>,
    /// <see cref="DuelEngine.CanSpecialSummonProcedure"/>, <see cref="TextEffectRuntime.CanActivate"/>), so
    /// LegalIntentService and the AI, which ask the same questions, see the same answers.
    /// <list type="bullet">
    /// <item>Jowgen the Spiritualist — <see cref="EffectActionKind.NeitherPlayerCanSpecialSummon"/> (ygopro
    /// EFFECT_CANNOT_SPECIAL_SUMMON, target range 1,1): no Special Summon of any kind while it is face-up in the Monster
    /// Zone (<see cref="SpecialSummonLocked(DuelEngine, DuelistState, CardInstance, out string)"/>; enforced in
    /// DuelEngine.SpecialSummonToField, which every Special Summon goes through: effects, procedures, Fusion, Ritual,
    /// Tokens). Its ignition: discard 1 random card (<see cref="EffectClause.DiscardCostRandom"/>), then destroy every
    /// Special Summoned monster on the field, itself included (<see cref="SpecialSummonedMonsterOnField"/> is its
    /// activation check).</item>
    /// <item>Jam Breeding Machine — <see cref="EffectActionKind.ControllerCannotSummonExceptNamed"/> (ygopro
    /// EFFECT_CANNOT_SUMMON / CANNOT_FLIP_SUMMON / CANNOT_SPECIAL_SUMMON except TOKEN_SLIME, target range 1,0): its
    /// controller cannot Normal (Tribute) Summon, Flip Summon or Special Summon a monster other than a "Slime Token";
    /// Setting stays legal.</item>
    /// <item>Kycoo the Ghost Destroyer — <see cref="EffectActionKind.OpponentCannotBanishFromGraveyards"/> (ygopro
    /// EFFECT_CANNOT_REMOVE, target range 0,1, cards in a GY): its controller's opponent cannot banish a card from either
    /// GY, by an effect or as a cost (<see cref="BanishFromGyLocked"/>, read by DuelEngine.BanishCard with the banishing
    /// player and by the GY banish cost / target collectors). Its battle-damage trigger targets up to 2 monsters in the
    /// opponent's GY (<see cref="TryOfferBattleDamageTargets"/>).</item>
    /// <item>Royal Command — <see cref="EffectActionKind.NegateFlipMonsterEffectActivations"/> (ygopro
    /// EVENT_CHAIN_ACTIVATING: NegateActivation when re:IsActiveType(TYPE_FLIP)): every activated effect of a Flip
    /// monster (its FLIP effect and its other triggers / ignitions) has its activation negated while Royal Command is
    /// face-up; continuous effects of Flip monsters still apply (<see cref="NegatesFlipMonsterActivation"/>).</item>
    /// <item>Chain Energy — <see cref="EffectActionKind.HandActionsCostLp"/> (ygopro EFFECT_ACTIVATE_COST / SUMMON_COST /
    /// SPSUMMON_COST / MSET_COST / SSET_COST, range hand, both players): each player pays Amount LP per face-up copy to
    /// Normal Summon, Special Summon, Set or activate a card from their hand, and cannot do it with less LP
    /// (<see cref="HandActionLpCost"/>, <see cref="HandActionUnaffordable"/>). Summons and Sets pay at their DuelEngine
    /// entry points; a Special Summon of a card from the hand pays in DuelEngine.SpecialSummonToField (its caller marks
    /// the card, <see cref="DuelEngine.MarkSpecialSummonFromHand"/>); an activation from the hand pays before it is made
    /// and is refunded when it fails or its target choice is cancelled (<see cref="BeginHandActivation"/>).</item>
    /// </list>
    /// </summary>
    public static class RuleLocksRuntime
    {
        /// <summary>Jowgen's activation check (Lua s.target): a Special Summoned monster is on the field.</summary>
        public const string SpecialSummonedMonsterOnField = "SpecialSummonedMonsterOnField";

        // ───────────────────────────── sources ─────────────────────────────

        /// <summary>
        /// The applying ContinuousWhileFaceUp clauses of <paramref name="action"/>: face-up, not negated, on the field
        /// (a monster whose Summon is still in its negation window, or a Spell/Trap whose activation is still an
        /// unresolved Chain Link, does not apply yet), with the program FullyCompiled.
        /// </summary>
        static IEnumerable<(CardInstance card, DuelistState controller, EffectClause clause)> Locks(DuelEngine engine,
            EffectActionKind action)
        {
            if (engine == null) yield break;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || !m.FaceUp || m.IsNegated || engine.IsSummonPending(m)) continue;
                    foreach (var c in ClausesOn(m, action))
                        yield return (m, side, c);
                }

                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st == null || !st.FaceUp || st.IsNegated || ActivationPending(engine, st)) continue;
                    foreach (var c in ClausesOn(st, action))
                        yield return (st, side, c);
                }
            }
        }

        static IEnumerable<EffectClause> ClausesOn(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action && c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    yield return c;
        }

        static bool ActivationPending(DuelEngine engine, CardInstance card)
        {
            var links = engine.Chain?.Links;
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && l.Card == card && !l.Resolved)
                    return true;
            return false;
        }

        // ───────────────────────────── Jowgen / Jam: summon locks ─────────────────────────────

        /// <summary>
        /// <paramref name="who"/> cannot Special Summon <paramref name="card"/> right now: Jowgen the Spiritualist is
        /// face-up (neither player), or <paramref name="who"/> controls a face-up Jam Breeding Machine and the card is not
        /// its named Token.
        /// </summary>
        public static bool SpecialSummonLocked(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = null;
            foreach (var (src, _, _) in Locks(engine, EffectActionKind.NeitherPlayerCanSpecialSummon))
            {
                reason = $"Neither player can Special Summon monsters ({src.Name}).";
                return true;
            }

            if (ExceptNamedLock(engine, who, card, out var jam))
            {
                reason = $"You cannot Special Summon that monster ({jam.Name}).";
                return true;
            }

            // ContinuousLocksAndIgnitions batch (v163): Non Aggression Area — no Special Summon during that turn.
            if (ContinuousLocksAndIgnitionsRuntime.SummonOrSetLocked(engine, who, out var naa))
            {
                reason = naa;
                return true;
            }

            // TokensDeclarationsAndPicks batch: Stray Lambs / Scapegoat's oath (except that card's own Tokens) binds the
            // summoning player, not the receiving field (the opponent's Ojama Trio still Summons onto the oath player's field).
            if (TokensDeclarationsAndPicksRuntime.SummonOathLocked(engine,
                    TokensDeclarationsAndPicksRuntime.SummoningPlayer(engine, who), special: true, exceptSource: null,
                    out var oath))
            {
                reason = oath;
                return true;
            }

            return false;
        }

        public static bool SpecialSummonLocked(DuelEngine engine, DuelistState who, CardInstance card) =>
            SpecialSummonLocked(engine, who, card, out _);

        /// <summary>
        /// <paramref name="who"/> cannot Special Summon at all (no card in mind): Jowgen, or Jam Breeding Machine for any
        /// monster that is not its Token (<paramref name="tokenName"/> = the Token an effect would make, or null).
        /// </summary>
        public static bool AnySpecialSummonLocked(DuelEngine engine, DuelistState who, string tokenName,
            out string reason)
        {
            reason = null;
            foreach (var (src, _, _) in Locks(engine, EffectActionKind.NeitherPlayerCanSpecialSummon))
            {
                reason = $"Neither player can Special Summon monsters ({src.Name}).";
                return true;
            }

            foreach (var (src, controller, c) in Locks(engine, EffectActionKind.ControllerCannotSummonExceptNamed))
            {
                if (controller != who) continue;
                if (!string.IsNullOrEmpty(tokenName) &&
                    string.Equals(tokenName, c.NamedCard, StringComparison.OrdinalIgnoreCase))
                    continue;
                reason = $"You cannot Special Summon monsters, except \"{c.NamedCard}\" ({src.Name}).";
                return true;
            }

            // ContinuousLocksAndIgnitions batch (v163): Non Aggression Area.
            if (ContinuousLocksAndIgnitionsRuntime.SummonOrSetLocked(engine, who, out var naa))
            {
                reason = naa;
                return true;
            }

            return false;
        }

        /// <summary>Jam Breeding Machine: <paramref name="who"/> cannot Normal (Tribute) Summon. Setting stays legal.</summary>
        public static bool NormalSummonLocked(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = null;
            // TokensDeclarationsAndPicks batch: Stray Lambs / Scapegoat's oath (Setting stays legal).
            if (TokensDeclarationsAndPicksRuntime.SummonOathLocked(engine, who, special: false, exceptSource: null,
                    out reason))
                return true;
            if (!ExceptNamedLock(engine, who, card, out var jam)) return false;
            reason = $"You cannot Summon monsters ({jam.Name}); you can still Set.";
            return true;
        }

        /// <summary>Jam Breeding Machine / Stray Lambs' oath: <paramref name="who"/> cannot Flip Summon.</summary>
        public static bool FlipSummonLocked(DuelEngine engine, DuelistState who, CardInstance card) =>
            ExceptNamedLock(engine, who, card, out _) ||
            TokensDeclarationsAndPicksRuntime.SummonOathLocked(engine, who, special: false, exceptSource: null, out _);

        /// <summary>A face-up Jam Breeding Machine <paramref name="who"/> controls forbids Summoning <paramref name="card"/>.</summary>
        static bool ExceptNamedLock(DuelEngine engine, DuelistState who, CardInstance card, out CardInstance source)
        {
            source = null;
            if (who == null) return false;
            foreach (var (src, controller, c) in Locks(engine, EffectActionKind.ControllerCannotSummonExceptNamed))
            {
                if (controller != who) continue;
                // ygopro s.sumlimit: not c:IsCode(TOKEN_SLIME) — only that Token may be Summoned.
                if (card != null && card.IsToken && card.IsNamed(c.NamedCard)) continue;
                source = src;
                return true;
            }

            return false;
        }

        /// <summary>A Special Summon action (the activation of such a card needs a player who can Special Summon).</summary>
        static bool IsSpecialSummonAction(EffectActionKind a) =>
            a == EffectActionKind.SpecialSummonFromGy ||
            a == EffectActionKind.SpecialSummonFromHand ||
            a == EffectActionKind.SpecialSummonFromDeck ||
            a == EffectActionKind.SpecialSummonNamed ||
            a == EffectActionKind.SpecialSummonToken ||
            a == EffectActionKind.SpecialSummonFusionFromExtra ||
            a == EffectActionKind.SpecialSummonThisFromHand ||
            a == EffectActionKind.RitualSummon ||
            a == EffectActionKind.FusionSummonRegistered ||
            a == EffectActionKind.SpecialSummonChosenFromHandOrDeck ||
            a == EffectActionKind.SpecialSummonFromHandProcedure ||
            a == EffectActionKind.UnequipThisSpecialSummon ||
            a == EffectActionKind.MagicalHatsStyle;

        /// <summary>
        /// TextEffectRuntime.CanActivate: an activation whose every effect clause is a Special Summon (Monster Reborn, Call
        /// of the Haunted, Scapegoat, Polymerization, a Ritual Spell, a monster's "Special Summon this card from your hand")
        /// cannot be activated by a player who cannot Special Summon (ygopro: its target / Special Summon check fails). A
        /// card that does something else as well can still be activated; its Special Summon part does nothing
        /// (DuelEngine.SpecialSummonToField refuses it).
        /// </summary>
        public static bool ActivationLockedBySummonLock(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, out string reason)
        {
            reason = null;
            if (engine == null || who == null || prog == null) return false;
            var activate = prog.ClausesFor(EffectTiming.Activate);
            if (activate == null || activate.Count == 0) return false;
            var relevant = activate.Where(c => c != null && (fromHand || !card.Def.IsMonster || !c.ActivatesFromHand))
                .ToList();
            if (relevant.Count == 0 || !relevant.All(c => IsSpecialSummonAction(c.Action))) return false;
            // TokensDeclarationsAndPicks batch: an oath of another card forbids it (this card's own oath, bound while it is
            // being activated, does not).
            if (TokensDeclarationsAndPicksRuntime.SummonOathLocked(engine, who, special: true, exceptSource: card,
                    out reason))
                return true;
            foreach (var c in relevant)
            {
                var token = c.Action == EffectActionKind.SpecialSummonToken ? c.TokenName : null;
                if (AnySpecialSummonLocked(engine, who, token, out reason))
                    return true;
            }

            // Chain Energy: an activation that only Special Summons from the hand needs the LP to pay that toll
            // (ygopro: IsCanBeSpecialSummoned checks the SPSUMMON_COST of a card in the hand).
            if (relevant.All(c => c.Action == EffectActionKind.SpecialSummonFromHand ||
                                  c.Action == EffectActionKind.SpecialSummonThisFromHand) &&
                !engine.HandActionCostPaid.Contains(card) && HandSpecialSummonUnaffordable(engine, who))
            {
                reason = $"{HandActionLpCost(engine, who)} LP must be paid to Special Summon from the hand.";
                return true;
            }

            return false;
        }

        /// <summary>Jowgen's activation condition (Lua: IsExistingMatchingCard(IsSummonType SPECIAL) on either field).</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause clause,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (clause == null || clause.ActivationCondition != SpecialSummonedMonsterOnField) return false;
            ok = false;
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                if (side.MonstersOnField().Any(m => m != null && m.WasSpecialSummoned)) ok = true;
            }

            reason = "Needs a Special Summoned monster on the field.";
            return true;
        }

        // ───────────────────────────── Kycoo: one-sided GY banish lock ─────────────────────────────

        /// <summary>
        /// <paramref name="banisher"/> (the controller of the effect, or the player paying the cost) cannot banish a card
        /// from either GY: their opponent controls a face-up Kycoo the Ghost Destroyer.
        /// </summary>
        public static bool BanishFromGyLocked(DuelEngine engine, DuelistState banisher)
        {
            if (engine == null || banisher == null) return false;
            foreach (var (_, controller, _) in Locks(engine, EffectActionKind.OpponentCannotBanishFromGraveyards))
                if (controller != banisher)
                    return true;
            return false;
        }

        /// <summary>
        /// Legacy target mirror (SpellTrapEffects.CollectLegalTargets): <paramref name="source"/>'s compiled Activate
        /// clause banishes its target, and <paramref name="who"/> cannot banish from a GY — no GY card is a legal target.
        /// </summary>
        public static void FilterGyBanishTargets(DuelEngine engine, DuelistState who, CardInstance source,
            List<CardInstance> list)
        {
            if (engine == null || who == null || source?.Def == null || list == null || list.Count == 0) return;
            var prog = CompiledEffectCache.GetOrCompile(source.Def);
            if (prog == null || !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Banish &&
                                                            c.RequiresTargetChoice))
                return;
            if (!BanishFromGyLocked(engine, who)) return;
            var opp = engine.OpponentOf(who);
            list.RemoveAll(t => t != null && (who.Graveyard.Contains(t) || (opp?.Graveyard?.Contains(t) ?? false)));
        }

        /// <summary>
        /// "When this card inflicts battle damage to your opponent: You can target up to N monsters in their Graveyard;
        /// banish those targets." (Kycoo; ygopro TRIGGER_O with CARD_TARGET, 1 to N targets.) The AI banishes the most
        /// it can; a human gets the up-to-N picker (Cancel with no pick declines the optional effect, Cancel after a
        /// pick banishes the picks). Returns true when the clause was handled here.
        /// </summary>
        public static bool TryOfferBattleDamageTargets(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return false;
            if (clause.Timing != EffectTiming.ThisCardInflictsBattleDamage || !clause.RequiresTargetChoice ||
                clause.Action != EffectActionKind.Banish)
                return false;
            var targets = TextEffectRuntime.CollectTriggerTargets(engine, who, clause, source);
            if (targets.Count == 0)
            {
                engine.Log($"{source.Name}: no monster in the opponent's GY can be banished.");
                return true;
            }

            var max = Math.Max(1, clause.Amount);
            if (!engine.IsHumanControlled(who))
            {
                var picks = TextEffectRuntime.AutoPickTriggerTargets(engine, who, clause, targets, max);
                if (picks.Count == 0) return true;
                engine.Log($"{source.Name}: banish {string.Join(", ", picks.Select(p => p.Name))}.");
                TextEffectRuntime.ApplyToTriggerTargets(engine, who, source, clause, picks);
                engine.NotifyPublic();
                return true;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = TextEffectRuntime.TargetKindOf(clause),
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = Math.Min(max, targets.Count),
                TargetPicksAreUpTo = true
            };
            pending.LegalTargets.AddRange(targets);
            engine.SetPendingActivation(pending);
            engine.Log($"{source.Name}: you can target up to {max} monsters in your opponent's GY to banish " +
                       "(Cancel to not activate).");
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── Chain Energy: LP toll on hand actions ─────────────────────────────

        /// <summary>
        /// Chain Energy: the LP <paramref name="who"/> pays now to Normal Summon, Special Summon, Set or activate one card
        /// from the hand — the clause Amount for each applying copy on either field (ygopro ct = #GetPlayerEffect(tp, id),
        /// CheckLPCost(ct × 500), each copy's costop pays 500). 0 when no copy applies.
        /// </summary>
        public static int HandActionLpCost(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return 0;
            var total = 0;
            foreach (var (_, _, c) in Locks(engine, EffectActionKind.HandActionsCostLp))
                total += Math.Max(0, c.Amount);
            return total;
        }

        /// <summary>
        /// Chain Energy: <paramref name="card"/> is in <paramref name="who"/>'s hand and they have less LP than the toll,
        /// so they cannot Summon, Set or activate it (ygopro CheckLPCost fails). An activation whose toll is already paid
        /// (<see cref="DuelEngine.HandActionCostPaid"/>) passes its own legality re-check.
        /// </summary>
        public static bool HandActionUnaffordable(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = null;
            if (engine == null || who?.Hand == null || card == null || !who.Hand.Contains(card)) return false;
            if (engine.HandActionCostPaid.Contains(card)) return false;
            var cost = HandActionLpCost(engine, who);
            if (cost <= 0 || who.LifePoints >= cost) return false;
            reason = $"{cost} LP must be paid to use a card from the hand ({who.LifePoints} LP).";
            return true;
        }

        /// <summary>
        /// Chain Energy: <paramref name="who"/> cannot pay the toll to Special Summon a card from the hand right now (no card
        /// in mind; the Special Summon checks of a "Special Summon from your hand" activation).
        /// </summary>
        public static bool HandSpecialSummonUnaffordable(DuelEngine engine, DuelistState who)
        {
            var cost = HandActionLpCost(engine, who);
            return cost > 0 && who != null && who.LifePoints < cost;
        }

        /// <summary>
        /// Chain Energy: pay the toll for a Normal Summon, Set or Special Summon of <paramref name="card"/> from the hand
        /// (<paramref name="what"/> = "Normal Summon", "Set", …). Call once the action can no longer fail. Returns the LP paid.
        /// </summary>
        public static int PayHandActionCost(DuelEngine engine, DuelistState who, CardInstance card, string what)
        {
            var cost = HandActionLpCost(engine, who);
            if (cost <= 0) return 0;
            engine.PayLifePoints(who, cost,
                $"Chain Energy: {who.Name} pays {cost} LP to {what} {card?.Name} from the hand.");
            return cost;
        }

        /// <summary>
        /// Chain Energy, before an activation of <paramref name="card"/> from the hand (DuelEngine.TryActivateSpellTrap and
        /// the AI's response activation): when the activation is legal and costs LP, the toll is taken now (a cost is paid
        /// as the card is activated) and the card is marked paid, so its own legality re-check passes. Returns the LP taken;
        /// <see cref="EndHandActivation"/> refunds it when the activation fails. A Summon procedure from the hand ("you can
        /// Special Summon this card (from your hand)", no Chain Link) is not an activation: it pays its Special Summon toll
        /// in DuelEngine.SpecialSummonToField. With too little LP nothing is taken and the activation's legality check
        /// refuses it.
        /// </summary>
        public static int BeginHandActivation(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand)
        {
            if (!fromHand || engine == null || who?.Hand == null || card?.Def == null || !who.Hand.Contains(card))
                return 0;
            if (engine.HandActionCostPaid.Contains(card)) return 0;
            var cost = HandActionLpCost(engine, who);
            if (cost <= 0 || who.LifePoints < cost || IsHandSummonProcedure(card)) return 0;
            if (!OfficialEffectRegistry.CanActivateOfficial(engine, who, card, true, out _)) return 0;
            TextEffects.ContinuousTrapsAndSpiritSupportRuntime.NoteLpCostPaid(engine, who, cost); // Life Absorbing Machine
            who.LifePoints -= cost;
            engine.HandActionCostPaid.Add(card);
            engine.Log($"Chain Energy: {who.Name} pays {cost} LP to activate {card.Name} from the hand.");
            return cost;
        }

        /// <summary>
        /// Chain Energy, after the activation that <see cref="BeginHandActivation"/> paid for: refunded when it was not
        /// made; kept on an open target choice of that card (<see cref="PendingActivation.HandActionLpPaid"/>, refunded
        /// if the choice is cancelled); 0 LP left by the toll is a loss, settled like any LP cost.
        /// </summary>
        public static void EndHandActivation(DuelEngine engine, DuelistState who, CardInstance card, int paid, bool ok)
        {
            if (engine == null || who == null || card == null || paid <= 0) return;
            engine.HandActionCostPaid.Remove(card);
            if (!ok)
            {
                who.LifePoints += paid;
                ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, who, paid);
                engine.Log($"Chain Energy: {card.Name} was not activated — {paid} LP refunded ({who.LifePoints} LP).");
                return;
            }

            var p = engine.PendingActivation;
            if (p != null && p.Card == card)
                p.HandActionLpPaid += paid;
            if (who.LifePoints <= 0)
                engine.SettleLpLossAfterCost(who);
        }

        /// <summary>A hand monster whose every hand clause is its own Special Summon procedure (no Chain Link).</summary>
        static bool IsHandSummonProcedure(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsMonster) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            var hand = prog?.ClausesFor(EffectTiming.Activate)?.Where(c => c != null && c.ActivatesFromHand).ToList();
            return hand != null && hand.Count > 0 &&
                   hand.All(c => c.Action == EffectActionKind.SpecialSummonThisFromHand && !c.MakesChainLink);
        }

        // ───────────────────────────── Royal Command ─────────────────────────────

        /// <summary>
        /// Royal Command is face-up: an activated effect of <paramref name="source"/>, a Flip monster, has its
        /// activation negated (Lua re:IsActiveType(TYPE_FLIP) — the card is a Flip Effect Monster).
        /// </summary>
        public static bool NegatesFlipMonsterActivation(DuelEngine engine, CardInstance source)
        {
            if (engine == null || !IsFlipMonster(source)) return false;
            foreach (var (rc, _, _) in Locks(engine, EffectActionKind.NegateFlipMonsterEffectActivations))
            {
                engine.Log($"{rc.Name}: the activation of {source.Name}'s effect is negated.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// MonsterEffects.OnFlipSummoned: the FLIP effect of <paramref name="monster"/> is an activation Royal Command
        /// negates — no target prompt, no effect. A FLIP that only marks the card as flipped (Spear Cretin: a continuous
        /// EVENT_FLIP effect, not an activation) is not negated: it runs, and <see cref="SkipsFlipMonsterClause"/> lets
        /// the marker through.
        /// </summary>
        public static bool NegatesFlipEffect(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || !IsFlipMonster(monster)) return false;
            var prog = CompiledEffectCache.GetOrCompile(monster);
            var flip = prog?.ClausesFor(EffectTiming.Flip);
            if (flip != null && flip.Count > 0 && flip.TrueForAll(c => c == null || IsFlipMarker(c))) return false;
            return NegatesFlipMonsterActivation(engine, monster);
        }

        /// <summary>
        /// ApplyClause gate: <paramref name="clause"/> is an activated effect of a Flip monster while Royal Command is
        /// face-up. A Flip monster's compiled FLIP, ignition and trigger clauses are all activated effects in ygopro
        /// (The Immortal of Thunder's "you lose 5000 LP" is TRIGGER_F even though its clause makes no Chain Link here),
        /// so only its continuous effects, the FLIP marker and a maintenance cost (not an activation) still apply.
        /// </summary>
        public static bool SkipsFlipMonsterClause(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (clause == null || source?.Def == null || !IsFlipMonster(source)) return false;
            if (clause.Timing == EffectTiming.ContinuousWhileFaceUp || IsFlipMarker(clause)) return false;
            if (clause.Action == EffectActionKind.StandbyMaintenancePayLp ||
                clause.Action == EffectActionKind.PayLpOrDestroyThis)
                return false;
            // TriggerChainResponses batch (v163): a FLIP effect that activated before Royal Command was chained to it
            // resolves (Royal Command negates activations only).
            if (clause.Timing == EffectTiming.Flip && SimultaneousTriggersRuntime.ResolvesActivatedFlip(engine, source))
                return false;
            return NegatesFlipMonsterActivation(engine, source);
        }

        /// <summary>Spear Cretin's FLIP marker: a continuous EVENT_FLIP effect (no Chain Link), not an activation.</summary>
        static bool IsFlipMarker(EffectClause c) =>
            c.Action == EffectActionKind.RecordThisCardFlipped && !c.MakesChainLink;

        static bool IsFlipMonster(CardInstance card) =>
            card?.Def != null && card.Def.IsMonster && !string.IsNullOrEmpty(card.Def.type) &&
            card.Def.type.IndexOf("Flip", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
