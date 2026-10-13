using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LvChain batch runtime (atoms: <see cref="LvChainAtoms"/>). Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>"[This card is] unaffected by Spell effects"</b> — the shared
    /// <see cref="EffectActionKind.UnaffectedByCardEffects"/> clause with a Spell filter and no named requirement. It
    /// is applied by <see cref="ContinuousProtections.IsUnaffectedBy"/> exactly like every other source of that
    /// action, so a monster's own Spell immunity (Horus the Black Flame Dragon LV6, Ultimate Insect LV1) needs no
    /// further engine work.</item>
    /// <item><b>An LV progression clause</b> — "send this card to the GY, Special Summon the next LV from your hand or
    /// Deck", carried by the generic <see cref="EffectActionKind.SpecialSummonNamed"/> arm. Its rider, "(You cannot
    /// activate this effect the turn this card was Normal Summoned, Special Summoned, or flipped face-up.)", is the
    /// <see cref="NotTurnSummoned"/> activation condition, read off the card's own
    /// <see cref="CardInstance.SummonedThisTurn"/> / <see cref="CardInstance.FlippedFaceUpThisTurn"/> flags.</item>
    /// <item><b>A conditional draw</b> (Dark Mimic LV3) — destroyed by battle and sent to the GY: draw
    /// <see cref="EffectClause.Amount"/>, or <see cref="EffectClause.AmountIfSpecialSummonedByNamed"/> when this
    /// card was Special Summoned by the effect of <see cref="EffectClause.SpecialSummonedByNamedCard"/>.</item>
    /// </list>
    /// </summary>
    public static class LvChainRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: the LV progression clause of a card that was already on the
        /// field before this turn began. "You cannot activate this effect the turn this card was Normal Summoned,
        /// Special Summoned, or flipped face-up" — a monster on the field since a previous turn (or one that was only
        /// flipped face-up on a previous turn) is eligible; a monster summoned or flipped face-up this turn is not.
        /// </summary>
        public const string NotTurnSummoned = "LvChain.NotTurnSummoned";

        /// <summary>The names of the cards whose effects have Special Summoned a given card (its "summoned by" list).</summary>
        static readonly ConditionalWeakTable<CardInstance, List<string>> s_summonedBy = new();

        // ───────────────────────────── activation condition ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet: <see cref="NotTurnSummoned"/>. Returns false when the clause is
        /// not this batch's, so another batch's condition still gets its turn.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (c == null || c.ActivationCondition != NotTurnSummoned) return false;
            ok = card != null && !card.SummonedThisTurn && !card.FlippedFaceUpThisTurn;
            reason = ok ? null : "This effect cannot be activated the turn this card was summoned or flipped face-up.";
            return true;
        }

        // ───────────────────────────── conditional draw ─────────────────────────────

        /// <summary>
        /// The copies of the clause's <see cref="EffectClause.NamedCard"/> on <paramref name="who"/>'s field right now.
        /// Taken just before <see cref="EffectActionKind.SpecialSummonNamed"/> resolves so that
        /// <see cref="NoteSpecialSummoned"/> can tell which copy the summon actually placed.
        /// </summary>
        public static List<CardInstance> SnapshotNamed(DuelistState who, EffectClause clause)
        {
            if (who == null || clause == null || string.IsNullOrEmpty(clause.NamedCard)) return null;
            return who.MonstersOnField().Where(m => m != null && m.IsNamed(clause.NamedCard)).ToList();
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause's <see cref="EffectActionKind.SpecialSummonNamed"/> arm, after the summon:
        /// every copy of <see cref="EffectClause.NamedCard"/> that was not in <paramref name="before"/> was placed by
        /// <paramref name="source"/>'s effect, so it records <paramref name="source"/>'s name on it. Dark Mimic LV1
        /// → Dark Mimic LV3's draw 2 instead of 1.
        /// </summary>
        public static void NoteSpecialSummoned(DuelistState who, EffectClause clause, CardInstance source,
            List<CardInstance> before)
        {
            if (who == null || clause == null || source?.Def == null) return;
            if (string.IsNullOrEmpty(clause.NamedCard)) return;
            foreach (var m in who.MonstersOnField())
            {
                if (m?.Def == null || !m.IsNamed(clause.NamedCard)) continue;
                if (before != null && before.Contains(m)) continue;
                var names = s_summonedBy.GetOrCreateValue(m);
                if (!names.Contains(source.Def.name)) names.Add(source.Def.name);
            }
        }

        /// <summary>True when <paramref name="card"/> was Special Summoned by the effect of the card named <paramref name="named"/>.</summary>
        public static bool SpecialSummonedByNamed(CardInstance card, string named)
        {
            if (card == null || string.IsNullOrEmpty(named)) return false;
            return s_summonedBy.TryGetValue(card, out var names) && names.Any(x =>
                string.Equals(x, named, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// ApplyClause arm (<see cref="EffectActionKind.DrawAmountWhenSpecialSummonedByNamed"/>): the draw count is
        /// read at resolution, so a card that left the field while the Chain resolves still draws its printed amount,
        /// and the "summoned by the named card" state is the one this card had.
        /// </summary>
        public static void ResolveDraw(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null || engine.GameOver) return;
            var n = clause.Amount > 0 ? clause.Amount : 1;
            if (clause.AmountIfSpecialSummonedByNamed > 0 &&
                !string.IsNullOrEmpty(clause.SpecialSummonedByNamedCard) &&
                SpecialSummonedByNamed(source, clause.SpecialSummonedByNamedCard))
                n = clause.AmountIfSpecialSummonedByNamed;
            engine.Draw(who, n, silent: false);
        }
    }
}
