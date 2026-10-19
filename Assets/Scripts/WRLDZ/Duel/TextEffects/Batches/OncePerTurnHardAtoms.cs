using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// OncePerTurnHard batch: the hard once per turn by card name, "You can only use this effect of "X" once per turn."
    /// (ygopro <c>SetCountLimit(1, id)</c>: one use per player per turn across every copy of that name).
    /// <para>Before this batch <c>CardTextEffectCompiler.IsBoilerplate</c> dropped that sentence ("you can only use"), so
    /// Sangan, Witch of the Black Forest, Guerilla Kite, RAM Clouder and 11 more were FullyCompiled with no
    /// <see cref="EffectClause.OncePerTurn"/>: two Sangans sent to the GY in one turn both searched. Now:</para>
    /// <list type="bullet">
    /// <item>the sentence is no longer boilerplate (<see cref="IsHardNameOptSentence"/>): a card whose sentence nothing
    /// consumed keeps it unparsed and fails closed;</item>
    /// <item>an ignition (Activate) carries the sentence inside its own whole-text atom (<see cref="Atoms"/>): the generic
    /// PSCT ignition parser (IgnitionTemplates) is not a whole-sentence template (it keeps the action and drops a cost or a
    /// rider it does not know: Valerifawn's target filter, Lightning's second discard, Xyz-Raypierce's banish cost, the Xyz
    /// detach costs), so it never vouches for a hard once per turn;</item>
    /// <item>a trigger compiled by a whole-sentence template (<see cref="StampNameOpt"/>: "sent from the field to the GY",
    /// "If this card is Summoned") gets the flag from the sentence that follows its effect.</item>
    /// </list>
    /// Every stamped clause gets <see cref="EffectClause.OncePerTurn"/> and <see cref="OncePerTurnScope.PerCardName"/>;
    /// <see cref="OncePerTurnHardRuntime"/> enforces it per player and card name.
    /// </summary>
    public static class OncePerTurnHardAtoms
    {
        const string Gy = @"(?:GY|Graveyard)";
        const string Q = @"[""“”]";

        /// <summary>The once-per-turn sentence that closes an atom's text: the card's own name, "this" or "the" effect.</summary>
        const string OptTail = @"\s*You can only use (?:this|the) effect of " + Q + @"[^""“”]+" + Q + @" once per turn\.?\s*$";

        /// <summary>The hard once-per-turn-by-name sentence ("this" / "the" / "each" effect), anywhere in a text.</summary>
        static readonly Regex RxHardNameOpt = new(
            @"(?<=^|[.!?)]\s*)You can only use (?<which>this|the|each) effect of " + Q + @"(?<n>[^""“”]+)" + Q +
            @" once per turn\.?(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The same sentence as a lone (lower-cased, mask-collapsed) residual fragment.</summary>
        static readonly Regex RxHardNameOptFragment = new(
            @"^\s*you can only use (?:this|the|each) effect of " + Q + @"[^""“”]+" + Q + @" once per turn\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // RAM Clouder (c9190563): IGNITION in the Monster Zone, CARD_TARGET, SetCountLimit(1, id); cost: Release 1
            // monster you control (itself allowed, CheckReleaseGroupCost); target 1 Cyberse in your GY
            // (IsCanBeSpecialSummoned); SpecialSummon(tc, POS_FACEUP) if it still relates. Same clause the PSCT ignition
            // parser built before, now with its once per turn.
            new AtomRegistration.Atom(
                @"^\s*You can Tribute 1 monster, then target 1 (?<race>[A-Za-z]+)(?:-Type)? monster in your " + Gy +
                @";\s*Special Summon it\." + OptTail,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    RaceFilter = m.Groups["race"].Value,
                    RequiresTributeCount = 1,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    IsOptional = true,
                    MakesChainLink = true,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerCardName
                }),

            // Golden Dragon Summoner (c28565527): IGNITION in the Monster Zone, CARD_TARGET, SetCountLimit(1, id); cost:
            // Release 1 monster (one that leaves another monster to target); target 1 monster on either field
            // (IsAbleToHand, face-down too); SendtoHand if it still relates.
            new AtomRegistration.Atom(
                @"^\s*You can Tribute 1 monster, then target 1 monster on the field;\s*return it to the hand\." + OptTail,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnToHand,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTributeCount = 1,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    IsOptional = true,
                    MakesChainLink = true,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerCardName
                }),

            // Garbage Ogre (c18698739): IGNITION in the hand, SetCountLimit(1, id), cost Cost.SelfToGrave (a send, not a
            // discard); search 1 "Garbage Lord" (SelectMatchingCard from the Deck, SendtoHand + ConfirmCards).
            new AtomRegistration.Atom(
                @"^\s*You can send this card from your hand to the " + Gy + @";\s*add 1 " + Q + @"(?<named>[^""“”]+)" + Q +
                @" from your Deck to your hand\." + OptTail,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = m.Groups["named"].Value,
                    Amount = 1,
                    FromDeck = true,
                    ActivatesFromHand = true,
                    RequiresDiscardSelf = true,
                    DiscardCostIsSend = true,
                    IsOptional = true,
                    MakesChainLink = true,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerCardName
                }),
        };

        /// <summary>
        /// CardTextEffectCompiler.IsBoilerplate: true for the hard once-per-turn-by-name sentence, which is never
        /// boilerplate (it must be consumed by an atom or by <see cref="StampNameOpt"/>, or the card fails closed).
        /// </summary>
        public static bool IsHardNameOptSentence(string fragment) =>
            !string.IsNullOrEmpty(fragment) && RxHardNameOptFragment.IsMatch(fragment);

        /// <summary>The timings whose activation <see cref="OncePerTurnHardRuntime"/> gates by card name.</summary>
        static bool TriggerTimingEnforced(EffectTiming t) =>
            t == EffectTiming.SentFromFieldToGy || t == EffectTiming.ThisCardSummoned;

        /// <summary>
        /// Whole-text pass, after every template ran and before the residual audit: a hard once-per-turn sentence no atom
        /// consumed is stamped onto the one effect it limits and marked matched, when all of these hold:
        /// it says "this" or "the" effect (not "each"); it names the card itself; every clause of the program comes from one
        /// effect sentence (one SourceSnippet) placed before it; that effect is a trigger whose activation the runtime
        /// gates by name (<see cref="TriggerTimingEnforced"/>); and that sentence was compiled whole (a PSCT sentence with a
        /// cost or target segment went through the loose generic cost parse, so it is not vouched for).
        /// Otherwise the sentence stays unparsed and the card fails closed.
        /// </summary>
        public static void StampNameOpt(CardDef def, string text, List<EffectClause> clauses,
            List<(int start, int length)> spans)
        {
            if (def == null || string.IsNullOrEmpty(text) || clauses == null || spans == null) return;
            foreach (Match m in RxHardNameOpt.Matches(text))
            {
                if (AtomRegistration.SpanCovered(spans, m.Index, m.Length)) continue; // an atom owns it (and its flag)
                if (!string.Equals(m.Groups["which"].Value, "this", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(m.Groups["which"].Value, "the", StringComparison.OrdinalIgnoreCase))
                    continue; // "each effect": one count per effect — not stamped here (fails closed)
                if (!string.Equals(m.Groups["n"].Value.Trim(), (def.name ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                string snippet = null;
                var ok = clauses.Count > 0;
                foreach (var c in clauses)
                {
                    if (c == null) { ok = false; break; }
                    if (snippet == null) snippet = c.SourceSnippet ?? "";
                    else if (!string.Equals(snippet, c.SourceSnippet ?? "", StringComparison.Ordinal)) { ok = false; break; }
                    if (!TriggerTimingEnforced(c.Timing) || !c.MakesChainLink) { ok = false; break; }
                }

                if (!ok || string.IsNullOrWhiteSpace(snippet)) continue;
                var at = text.IndexOf(snippet, StringComparison.Ordinal);
                if (at < 0 || at + snippet.Length > m.Index) continue; // the effect must precede its once-per-turn sentence
                if (!CompiledWhole(def, text, snippet)) continue;

                foreach (var c in clauses)
                {
                    c.OncePerTurn = true;
                    c.OptScope = OncePerTurnScope.PerCardName;
                }

                spans.Add((m.Index, m.Length));
            }
        }

        /// <summary>
        /// The effect sentence <paramref name="snippet"/> is a whole-sentence compile: either a whole-text template's
        /// match (no PSCT sentence of the text equals it), or a PSCT sentence with no cost/target segment (condition and
        /// resolution only, both of which the generic path matches whole).
        /// </summary>
        static bool CompiledWhole(CardDef def, string text, string snippet)
        {
            foreach (var sent in PsctGrammar.Parse(text, def))
            {
                if (sent == null) continue;
                if (!string.Equals((sent.Raw ?? "").Trim(), snippet.Trim(), StringComparison.Ordinal)) continue;
                return string.IsNullOrWhiteSpace(sent.Activation) && !sent.HasSemicolon;
            }

            return true;
        }
    }
}
