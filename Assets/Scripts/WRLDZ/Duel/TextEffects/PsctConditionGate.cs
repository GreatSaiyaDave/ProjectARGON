using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Fail-closed gate for the generic PSCT path (CardTextEffectCompiler.CompilePsctSentence and the ignition
    /// templates it tries first). The green text before a colon may only hold conditions that path really
    /// compiles — through PsctGrammar.SuggestTiming, CardTextEffectCompiler.Stamp and its "If you control" /
    /// "is on the field" gates. Anything else used to vanish while the card still counted as FullyCompiled: an
    /// "if …" rider (D.D. Scout Plane, The Agent of Wisdom - Mercury), "Each time …" (Coffin Seller, Gearfried the
    /// Swordmaster), a battle or Type condition (Cipher Soldier, Rope of Life, Michizure), a choose-one "you can
    /// either" / "activate 1 of these effects" (Union monsters, Dark Scorpions), "Once during each of your Standby
    /// Phases" (Weapon Change, whose resolution never compiled). Such a sentence now compiles to nothing, so the
    /// card stays Stub / Unimplemented until a whole-sentence atom compiles it (Tools/CardCompile/README.md,
    /// "Fail closed").
    /// </summary>
    public static class PsctConditionGate
    {
        /// <summary>
        /// One comma-separated piece of a condition, matched whole. Each maps to a timing or clause flag the
        /// generic path sets; extend this list only together with that mapping.
        /// </summary>
        static readonly Regex[] RecognizedSegments =
        {
            // PsctGrammar: FLIP → EffectTiming.Flip.
            Rx(@"FLIP"),
            // PsctGrammar.OncePerTurn → EffectClause.OncePerTurn.
            Rx(@"Once per turn"),
            // SuggestTiming ThisCardSummoned; Stamp: RequiresThisNormalSummoned / FlipSummoned / TributeSummoned.
            Rx(@"(?:When|If) this (?:card|monster) is (?:Normal |Flip |Tribute )?Summoned"),
            // SuggestTiming SentFromFieldToGy; Stamp: RequiresThisDestroyedByBattle.
            Rx(@"(?:When|If) this (?:card|monster) is destroyed by battle(?: and sent to the (?:GY|Graveyard))?"),
            Rx(@"(?:When|If) this (?:card|monster) is sent from the field to the (?:GY|Graveyard)"),
            // SuggestTiming ThisCardInflictsBattleDamage; Stamp: RequiresDirectAttack.
            Rx(@"(?:When|If) this (?:card|monster) inflicts battle damage to your opponent(?: by a direct attack)?"),
            // CompilePsctSentence: RequiresFaceUpName + RequiresControllerNamedCard / RequiresFaceUpName.
            Rx(@"If you control (?:a |an )?(?:face-up )?""[^""]+"""),
            Rx(@"(?:If|When) ""[^""]+"" is(?: face-up)? on the field"),
            // SuggestTiming StandbyPhase / EndPhase: the controller's own phase (FirePhaseTriggersFor).
            Rx(@"During (?:your|each of your) (?:Standby|End) Phases?"),
            // SuggestTiming AttackDeclared.
            Rx(@"When an opponent's monster declares an attack"),
            // Stamp: OnlyDuringMainPhase1 (Gear Golem the Moving Fortress).
            Rx(@"during your Main Phase 1"),
            // Test-only alias texts that name the compiled timing ("ContinuousWhileFaceUp: …", Fanbot aliases).
            Rx(@"ContinuousWhileFaceUp"),
        };

        static Regex Rx(string segment) =>
            new Regex("^" + segment + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True when every comma-separated piece of <paramref name="condition"/> (the text before the colon) is
        /// one the generic path compiles. An empty condition is recognized.
        /// </summary>
        public static bool IsRecognized(string condition)
        {
            var cond = (condition ?? "").Trim().TrimEnd('.').Trim();
            if (cond.Length == 0) return true;
            foreach (var segment in Segments(cond))
            {
                var piece = segment.Trim();
                var ok = false;
                foreach (var rx in RecognizedSegments)
                {
                    if (!rx.IsMatch(piece)) continue;
                    ok = true;
                    break;
                }

                if (!ok) return false;
            }

            return true;
        }

        /// <summary>Split on commas outside quoted card names.</summary>
        static IEnumerable<string> Segments(string cond)
        {
            var start = 0;
            var inQuote = false;
            for (var i = 0; i < cond.Length; i++)
            {
                if (cond[i] == '"') inQuote = !inQuote;
                else if (cond[i] == ',' && !inQuote)
                {
                    yield return cond.Substring(start, i - start);
                    start = i + 1;
                }
            }

            yield return cond.Substring(start);
        }
    }
}
