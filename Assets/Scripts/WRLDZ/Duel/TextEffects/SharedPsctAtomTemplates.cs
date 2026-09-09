using System;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Small, composable PSCT atoms shared by cards instead of card-specific exceptions.
    /// This pack deliberately describes only selection and resolution; trigger metadata is
    /// copied by CardTextEffectCompiler.Stamp so When/If and optional effects remain intact.
    /// </summary>
    public static class SharedPsctAtomTemplates
    {
        static readonly Regex RxSummon = new(
            @"special summon\s+" +
            @"(?:(?<up>up to)\s+)?" +
            @"(?:(?<count>\d+)\s+|(?<any>any number of|as many as possible)\s+)?" +
            @"(?:(?:a|an|1)\s+)?" +
            @"(?:(?:Level\s+(?<level>\d+)\s+or lower)\s+)?" +
            @"(?:(?<race>[A-Za-z][A-Za-z -]*?)(?:-Type)?\s+)?" +
            @"monsters?\s+from\s+(?:your|the)\s+(?<origin>hand|deck|graveyard|gy)" +
            @"(?:\s+in\s+(?<pos>Attack|Defense)\s+Position)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNegateActivation = new(
            @"(?:During either player's turn,\s*)?When a (?<kind>Spell/?Trap|Spell|Trap) Card is activated\b" +
            @"(?<mid>.*?)negate the activation(?:,\s*and if you do,\s*destroy it)?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPosition = new(
            @"(?:change|switch)\s+(?:the\s+)?(?:battle\s+)?position\s+of\s+" +
            @"(?:1\s+)?(?:(?<faceup>face-up)\s+)?monster(?:s)?(?:\s+on\s+the\s+field|\s+your opponent controls)?" +
            @"(?:\s+to\s+(?<position>Attack|Defense)\s+Position)?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Returns the chain-activation response sentence, when present.</summary>
        public static Match MatchNegateActivation(string text) =>
            RxNegateActivation.Match(text ?? string.Empty);

        /// <summary>Compiles unquoted, filtered SS-from-hand/deck/GY resolution.</summary>
        public static bool TryCompileSpecialSummon(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var m = RxSummon.Match(resolution.Trim());
            if (!m.Success) return false;
            var tail = resolution.Trim().Substring(m.Index + m.Length).Trim();
            if (tail.Length > 0 && tail != ".") return false;

            var origin = m.Groups["origin"].Value;
            clause.Action = origin.Equals("deck", StringComparison.OrdinalIgnoreCase)
                ? EffectActionKind.SpecialSummonFromDeck
                : origin.Equals("hand", StringComparison.OrdinalIgnoreCase)
                    ? EffectActionKind.SpecialSummonFromHand
                    : EffectActionKind.SpecialSummonFromGy;
            clause.Zone = origin.Equals("deck", StringComparison.OrdinalIgnoreCase)
                ? EffectZoneFilter.ControllerDeckMonsters
                : origin.Equals("hand", StringComparison.OrdinalIgnoreCase)
                    ? EffectZoneFilter.ControllerHandMonsters
                    : EffectZoneFilter.ControllerGyMonsters;
            clause.RequiresTargetChoice = true; // Select at resolution; not PSCT target.
            clause.SpecialSummonUpTo = m.Groups["up"].Success || m.Groups["any"].Success;
            clause.IsPsctTarget = false;

            if (m.Groups["count"].Success && int.TryParse(m.Groups["count"].Value, out var count))
                clause.Amount = Math.Max(1, count);
            else if (m.Groups["any"].Success)
            {
                clause.Amount = 99;
                clause.SpecialSummonUpTo = true;
            }
            else
                clause.Amount = 1;

            if (m.Groups["level"].Success && int.TryParse(m.Groups["level"].Value, out var level))
            {
                clause.Amount = level;
                clause.AmountIsLevel = true;
            }
            if (m.Groups["race"].Success)
            {
                var race = m.Groups["race"].Value.Trim();
                if (!race.Equals("Level", StringComparison.OrdinalIgnoreCase))
                    clause.RaceFilter = race;
            }
            if (m.Groups["pos"].Success)
            {
                var pos = m.Groups["pos"].Value;
                if (pos.Equals("Defense", StringComparison.OrdinalIgnoreCase))
                    clause.SummonInDefense = true;
                else
                    clause.SummonInAttack = true;
            }
            return true;
        }

        /// <summary>Compiles Select/switch/change-position wording without inventing targeting.</summary>
        public static bool TryCompileChangePosition(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var m = RxPosition.Match(resolution.Trim());
            if (!m.Success) return false;
            clause.Action = EffectActionKind.ChangeBattlePosition;
            clause.RequiresTargetChoice = true;
            clause.IsPsctTarget = false;
            clause.Zone = Regex.IsMatch(resolution, @"your opponent controls", RegexOptions.IgnoreCase)
                ? EffectZoneFilter.OppFaceUpMonsters
                : EffectZoneFilter.FieldAnyMonster;
            if (m.Groups["faceup"].Success)
                clause.Zone = clause.Zone == EffectZoneFilter.OppFaceUpMonsters
                    ? EffectZoneFilter.OppFaceUpMonsters
                    : EffectZoneFilter.FieldAnyMonster;
            if (m.Groups["position"].Success)
            {
                if (m.Groups["position"].Value.Equals("Attack", StringComparison.OrdinalIgnoreCase))
                    clause.ForceAttackPosition = true;
                else if (m.Groups["position"].Value.Equals("Defense", StringComparison.OrdinalIgnoreCase))
                    clause.ForceDefensePosition = true;
            }
            return true;
        }
    }
}
