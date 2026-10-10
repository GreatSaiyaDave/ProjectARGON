using System.Collections.Generic;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Activation LP costs of Spell/Trap Cards ("Activate this card by paying 1000 LP", "Pay 800 LP, then target …",
    /// "Pay 1000 LP; …"). A cost is paid when the card is activated, before the opponent can respond, and a negated
    /// activation does not refund it (Magic Jammer negating Toon World or Premature Burial, Seven Tools of the Bandit
    /// negating Archfiend's Roar).
    /// <para>Before this, the LP cost of a chain-seated activation was paid by
    /// <c>TextEffectRuntime.PayAutoIgnitionCosts</c> only when its Chain Link resolved, so a negated activation kept
    /// the LP, and a response-window activation (<c>TextEffectRuntime.SeatResponseOnChain</c>) never paid it.</para>
    /// <para>How it works:</para>
    /// <list type="bullet">
    /// <item>Each place that seats a Spell/Trap activation as a Chain Link calls <see cref="PayOnActivation"/> before
    /// its target prompt or the opponent's chain window: <c>SpellTrapEffects.BeginDeferredChainActivation</c> (Main
    /// Phase / open-state activations from the hand or the field, untargeted and targeted) and
    /// <c>TextEffectRuntime.SeatResponseOnChain</c> (answers to an attack, a Summon, an open state).</item>
    /// <item>It stamps <see cref="ChainLink.ActivationLpCostPaid"/>. <c>DuelEngine.ResolveChainStack</c> passes the stamp
    /// to <c>TextEffectRuntime.TryResolveActivation</c>, which then skips the structured cost, so the LP is not paid
    /// twice.</item>
    /// <item>A player who cancels the target pick of that activation gets the LP back
    /// (<see cref="PendingActivation.PaidLpCost"/>, <c>SpellTrapEffects.CancelPending</c>): the activation never
    /// happened.</item>
    /// <item>Spell Economics: a Spell's LP cost is skipped, and the link still counts as paid.</item>
    /// <item>It replaces the Delinquent Duo-only version of this hook that the DiscardTriggers batch (v142) added;
    /// the Duo's own checks (Magic Jammer, Imperial Order, Spell Economics) now run through it.</item>
    /// <item>Only an Activate clause whose sole cost is LP qualifies (<see cref="LpCostClause"/>). An LP cost next to
    /// another cost (discard, Tribute …) keeps the resolution-time payment. Paths that already pay at activation are
    /// unchanged: <c>StartIgnition</c> for the compiled-pending activations (Brain Control),
    /// <c>DuelEngine.TryActivateChainResponse</c> (Seven Tools), <c>SummonNegationRuntime</c> (Solemn Judgment).</item>
    /// </list>
    /// CanActivate / IgnitionCostLegal / the response legality checks still require LP &gt;= the cost.
    /// </summary>
    public static class ActivationLpCostRuntime
    {
        /// <summary>
        /// The Activate clause of <paramref name="card"/>'s FullyCompiled program whose LP cost is paid on activation:
        /// the only Activate clause with a <see cref="EffectClause.PayLpAmount"/>, with no other cost on it or on the
        /// card's other Activate clauses. With <paramref name="used"/>, the clause must also be one of the clauses the
        /// activation resolves (a response window may resolve another timing's clauses). Null when none qualifies.
        /// </summary>
        public static EffectClause LpCostClause(CardInstance card, CompiledCardProgram prog,
            IList<EffectClause> used = null)
        {
            if (card?.Def == null || (!card.Def.IsSpell && !card.Def.IsTrap)) return null;
            if (prog == null || !prog.FullyCompiled) return null;
            EffectClause lp = null;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
            {
                if (c == null) continue;
                if (HasNonLpCost(c)) return null;
                if (c.PayLpAmount <= 0) continue;
                if (lp != null) return null;
                lp = c;
            }

            if (lp == null || (used != null && !used.Contains(lp))) return null;
            return lp;
        }

        /// <summary>
        /// Pays the activation LP cost of <paramref name="card"/> (<see cref="LpCostClause"/>) as its Chain Link
        /// <paramref name="link"/> is created, and stamps <see cref="ChainLink.ActivationLpCostPaid"/>. Returns the LP
        /// actually paid: 0 when nothing qualifies, the link is already stamped, or Spell Economics skips it. Paying
        /// the last LP ends the duel at once (<see cref="DuelEngine.PayLifePoints"/>, nothing is open yet).
        /// </summary>
        public static int PayOnActivation(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link,
            CompiledCardProgram prog, IList<EffectClause> used = null)
        {
            if (engine == null || who == null || link == null || link.ActivationLpCostPaid ||
                !ReferenceEquals(link.Card, card))
                return 0;
            var clause = LpCostClause(card, prog, used);
            if (clause == null) return 0;
            link.ActivationLpCostPaid = true;
            if (ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card))
            {
                engine.Log($"{who.Name} does not pay LP to activate Spell Cards (Spell Economics).");
                return 0;
            }

            // CanActivate and the response legality checks already refused an activation the LP cannot pay.
            var amount = clause.PayLpAmount;
            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP → {who.Name} at {System.Math.Max(0, who.LifePoints - amount)} LP.");
            return amount;
        }

        static bool HasNonLpCost(EffectClause c) =>
            c.RequiresTributeThis || c.RequiresTributeCount > 0 || c.RequiresDiscardSelf ||
            c.RequiresSendThisToGy || c.RequiresSendHandToGy || c.RequiresSendOtherYouControl ||
            c.BanishFromGyCount > 0 || c.RequiresRemoveSpellCounters > 0 || c.RequiresDiscardCost ||
            c.DiscardCostCount > 0 || c.RequiresSendNamedToGy || c.RequiresTributeEquippedMonster ||
            c.RequiresLpCostMultiple > 0 || c.PayHalfLpCost || c.RequiresSendEquipSpellOnThisToGy;
    }
}
