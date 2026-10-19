using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ChoiceAmongEffects batch — "When &lt;condition&gt;: Apply / Activate 1 of these effects. ● … ● …" on a Normal Trap.
    /// One whole-text atom: the condition must be one this batch knows, and every bullet must compile from its own
    /// sentence (<see cref="Bullet"/>: the standalone text through <see cref="CardTextEffectCompiler.Compile(CardDef)"/>,
    /// or one of the bullet sentences below), as exactly one untargeted, cost-free Activate clause. Otherwise the atom
    /// refuses and the card stays as it was (fail closed).
    /// <list type="bullet">
    /// <item>"Apply 1 of these effects" (Fuh-Rin-Ka-Zan c1781310: Duel.SelectOption in s.activate, among the bullets that
    /// can apply then): a chooser clause (<see cref="EffectActionKind.ApplyOneOfTheseEffectsAtResolution"/>) carries the
    /// printed condition; the bullets carry <see cref="EffectClause.EffectChoiceAppliedAtResolution"/> and apply only
    /// when the chooser picks them (<see cref="ChoiceAmongEffectsRuntime.ResolveChoice"/>). Only bullets whose
    /// availability the runtime can judge compile (<see cref="ChoiceAmongEffectsRuntime.CanJudge"/>).</item>
    /// <item>"Activate 1 of these effects" (Penalty Game! c967928: Duel.SelectOption in s.target): plain
    /// <see cref="EffectClause.EffectChoiceIndex"/> bullets, chosen as the card is activated through the existing
    /// bullet flow (<see cref="PoisonMeteorCestusRuntime.OfferChainBullet"/> / <c>OfferSeatedBullet</c> /
    /// <c>OfferInlineBullet</c>); only the chosen one resolves.</item>
    /// </list>
    /// Conditions: "there are face-up A, B … and Z monster(s) on the field" (each Attribute face-up on either field) and
    /// "your opponent has N cards in their hand" (exactly N), both checked when the card is activated.
    /// </summary>
    public static class ChoiceAmongEffectsAtoms
    {
        const string Attr = "(?:LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            AtomRegistration.Atom.Sequence(
                @"^\s*When (?<cond>[^:●]+):\s*(?<verb>Apply|Activate) 1 of these effects\.\s*(?<bullets>●[^●]+(?:●[^●]+)+)$",
                Build),
        };

        static readonly Regex RxFaceUpAttributes = new(
            @"^there are face-up (?<a>" + Attr + @"(?:, " + Attr + @")*,? and " + Attr + @") monster\(s\) on the field$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxOpponentHandExactly = new(
            @"^your opponent has (?<n>\d+) cards? in their hand$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ── bullet sentences no standalone card prints (so no standalone atom compiles them) ──

        /// <summary>Fuh-Rin-Ka-Zan b3 (Lua RandomSelect(1-tp,2), SendtoGrave REASON_EFFECT|REASON_DISCARD).</summary>
        static readonly Regex RxDiscardRandomFromOpponentHand = new(
            @"^Discard (?<n>\d+) random cards? from your opponent's hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Penalty Game! b1 (EFFECT_CANNOT_DRAW + EFFECT_DRAW_COUNT 0 until their next Draw Phase ends).</summary>
        static readonly Regex RxOpponentCannotDrawNextDrawPhase = new(
            @"^Your opponent cannot draw during their next Draw Phase\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Penalty Game! b2 (EFFECT_CANNOT_ACTIVATE, aclimit re:IsHasType(EFFECT_TYPE_ACTIVATE), RESET_PHASE|PHASE_END).</summary>
        static readonly Regex RxOpponentCannotActivateSpellTrapCardsThisTurn = new(
            @"^Your opponent cannot activate Spell/Trap Cards this turn\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static EffectClause[] Build(Match m)
        {
            var cond = Condition(m.Groups["cond"].Value.Trim());
            if (cond == null) return null;
            var atResolution = string.Equals(m.Groups["verb"].Value, "Apply", System.StringComparison.OrdinalIgnoreCase);
            var texts = new List<string>();
            foreach (var part in m.Groups["bullets"].Value.Split('●'))
            {
                var t = part.Trim();
                if (t.Length > 0) texts.Add(t);
            }

            if (texts.Count < 2) return null;
            var clauses = new List<EffectClause>();
            if (atResolution)
            {
                cond.Action = EffectActionKind.ApplyOneOfTheseEffectsAtResolution;
                clauses.Add(cond);
            }

            for (var i = 0; i < texts.Count; i++)
            {
                var b = Bullet(texts[i]);
                if (b == null) return null;
                if (atResolution && !ChoiceAmongEffectsRuntime.CanJudge(b)) return null;
                b.Timing = EffectTiming.Activate;
                b.EffectChoiceIndex = i + 1;
                b.MakesChainLink = true;
                if (atResolution)
                {
                    b.EffectChoiceAppliedAtResolution = true;
                    // Lua s.target chk: at least one bullet can apply (checked once per bullet; same answer).
                    b.ActivationCondition = ChoiceAmongEffectsRuntime.AnyBulletApplicable;
                }
                else
                {
                    b.ActivationCondition = cond.ActivationCondition;
                    b.ConditionAmount = cond.ConditionAmount;
                    b.ConditionName = cond.ConditionName;
                }

                clauses.Add(b);
            }

            return clauses.ToArray();
        }

        /// <summary>The printed condition as a chooser clause (Action set by the caller), or null when unknown.</summary>
        static EffectClause Condition(string text)
        {
            var fu = RxFaceUpAttributes.Match(text);
            if (fu.Success)
            {
                var attrs = new List<string>();
                foreach (Match a in Regex.Matches(fu.Groups["a"].Value, Attr, RegexOptions.IgnoreCase))
                    if (!attrs.Contains(a.Value.ToUpperInvariant())) attrs.Add(a.Value.ToUpperInvariant());
                if (attrs.Count < 2) return null;
                return new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Side = EffectSide.Controller,
                    ActivationCondition = ChoiceAmongEffectsRuntime.FaceUpAttributesOnField,
                    ConditionName = string.Join(",", attrs),
                    MakesChainLink = true
                };
            }

            var hand = RxOpponentHandExactly.Match(text);
            if (hand.Success && int.TryParse(hand.Groups["n"].Value, out var n) && n > 0)
                return new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Side = EffectSide.Controller,
                    ActivationCondition = ChoiceAmongEffectsRuntime.OpponentHandExactly,
                    ConditionAmount = n,
                    MakesChainLink = true
                };
            return null;
        }

        /// <summary>
        /// One bullet sentence as one untargeted, cost-free, unconditioned Activate clause, or null. The sentence is
        /// compiled on its own as a Normal Trap's whole text (the templates that compile the standalone card — Raigeki,
        /// Harpie's Feather Duster, Pot of Greed), else matched against the bullet sentences above.
        /// </summary>
        internal static EffectClause Bullet(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var own = OwnBullet(text);
            if (own != null) return own;
            var prog = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 0,
                name = "",
                type = "Trap Card",
                frameType = "trap",
                race = "Normal",
                attribute = "",
                archetype = "",
                desc = text,
                atk = -1,
                def = -1
            });
            if (prog == null || !prog.FullyCompiled || prog.ClauseList.Count != 1) return null;
            var c = prog.ClauseList[0];
            if (c == null || c.Timing != EffectTiming.Activate || c.RequiresTargetChoice || c.IsPsctTarget ||
                c.EffectChoiceIndex != 0 || !string.IsNullOrEmpty(c.ActivationCondition) ||
                EffectVocabulary.CostOf(c) != EffectCostKind.None)
                return null;
            return c;
        }

        static EffectClause OwnBullet(string text)
        {
            var d = RxDiscardRandomFromOpponentHand.Match(text);
            if (d.Success && int.TryParse(d.Groups["n"].Value, out var n) && n > 0)
                return new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = n
                };
            if (RxOpponentCannotDrawNextDrawPhase.IsMatch(text))
                return new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentCannotDrawDuringNextDrawPhase,
                    Side = EffectSide.Opponent
                };
            if (RxOpponentCannotActivateSpellTrapCardsThisTurn.IsMatch(text))
                return new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentCannotActivateSpellTrapCardsThisTurn,
                    Side = EffectSide.Opponent
                };
            return null;
        }
    }
}
