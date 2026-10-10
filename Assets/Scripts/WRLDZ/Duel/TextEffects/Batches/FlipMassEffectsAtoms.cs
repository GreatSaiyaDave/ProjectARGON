using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FlipMassEffects" (v142): big FLIP effects that empty or refill the field — Cyber Jar (destroy
    /// all, reveal 5, summon the low Levels, the rest to the hand), Morphing Jar #2 (shuffle all monsters into
    /// the Deck, excavate, summon the low Levels face-down), Jigen Bakudan (after it is flipped: your next
    /// Standby Phase, Tribute it to destroy all your monsters and burn half their ATK) and Spear Cretin (sent to
    /// the GY after being flipped: each player revives 1 monster from their own GY). Each atom is the card's whole
    /// text (start of text or after a sentence end, through its final period), so a longer or different card that
    /// only shares part of a sentence stays uncompiled. Runtime: <see cref="FlipMassEffectsRuntime"/>.
    /// </summary>
    public static class FlipMassEffectsAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        /// <summary>
        /// Cyber Jar (c34124316), its whole text including the reminder sentence; the reveal count in the reminder
        /// must be the printed reveal count. Read by the compiler's Dark Hole branch, which must not also take the
        /// "Destroy all monsters on the field" inside this sentence.
        /// </summary>
        public static readonly Regex RxCyberJar = new Regex(
            S + @"FLIP:\s*Destroy all monsters on the field, then both players reveal the top (\d+) cards from " +
            @"their Decks, then Special Summon all revealed Level (\d+) or lower monsters in face-up Attack " +
            @"Position or face-down Defense Position, also add any remaining cards to their hand\.\s*" +
            @"\(If either player has less than \1 cards in their Deck, reveal as many as possible\.\)(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Cyber Jar (c34124316): FLIP, one clause (resolution order inside the runtime): destroy every
            // monster on the field, then the turn player and then the other reveal their top N cards (as many as
            // they have), each Special Summons their revealed Level N-or-lower monsters in face-up Attack or
            // face-down Defense Position (their choice; no room: the rest go to the GY) and adds every other
            // revealed card to their hand. Not a target, no activation condition.
            new AtomRegistration.Atom(
                RxCyberJar.ToString(),
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.CyberJarStyle,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1, 5),
                    SummonLevelMax = Parse(m, 2, 4),
                    SummonFaceUpAttackOrFaceDownDefense = true,
                    MakesChainLink = true
                }),
            // Morphing Jar #2 (c79106360): FLIP, one clause for the three sentences (the count of monsters each
            // player shuffled into their Main Deck is what they excavate for). Face-down Defense only.
            new AtomRegistration.Atom(
                S + @"FLIP:\s*Shuffle all monsters on the field into the Deck\.\s*Then, each player excavates cards " +
                @"from the top of their Deck, until they excavate the same number of monsters they shuffled into " +
                @"their Main Deck\.\s*Special Summon all excavated Level (\d+) or lower monsters in face-down " +
                @"Defense Position, also send the remaining cards to the (?:Graveyard|GY)" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon,
                    Side = EffectSide.Both,
                    SummonLevelMax = Parse(m, 1, 4),
                    SummonFaceDown = true,
                    SummonInDefense = true,
                    MakesChainLink = true
                }),
            // Jigen Bakudan (c90020065): the FLIP only registers the Standby effect (Lua e1 operation, when the
            // card is still face-up); the Standby clause is the mandatory trigger it registers: during its
            // controller's next Standby Phase, Tribute it (cost), destroy every monster that player controls and
            // burn the opponent for half the total ATK of the monsters destroyed.
            AtomRegistration.Atom.Sequence(
                S + @"FLIP:\s*After this card is flipped, Tribute it during your Standby Phase to destroy all " +
                @"monsters you control and inflict damage equal to half of the total ATK of the destroyed cards " +
                @"\(excluding this monster\) to your opponent" + E,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.RecordThisCardFlipped,
                        Side = EffectSide.Controller,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk,
                        Side = EffectSide.Controller,
                        RequiresTributeThis = true,
                        MakesChainLink = true
                    }
                }),
            // Spear Cretin (c58551308): the FLIP only marks this copy as flipped (Lua e1: a continuous EVENT_FLIP
            // effect, not an activation, EFFECT_FLAG_CANNOT_DISABLE, whose flag survives the move to the GY), so the
            // marker makes no Chain Link; the GY clause is the mandatory targeting trigger (Lua e2).
            AtomRegistration.Atom.Sequence(
                S + @"FLIP:\s*When this card is sent to the (?:Graveyard|GY) after being flipped, each player selects " +
                @"1 monster from their (?:Graveyard|GY), and Special Summons it in face-up Attack Position or " +
                @"face-down Defense Position" + E,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.RecordThisCardFlipped,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy,
                        Side = EffectSide.Both,
                        Zone = EffectZoneFilter.ControllerGyMonsters,
                        SecondZone = EffectZoneFilter.OppGyMonsters,
                        Amount = 1,
                        IsPsctTarget = true,
                        SummonToTargetsOwner = true,
                        SummonFaceUpAttackOrFaceDownDefense = true,
                        MakesChainLink = true
                    }
                }),
        };

        /// <summary>
        /// True when <paramref name="text"/> holds Cyber Jar's whole sentence: the compiler's generic "Destroy all
        /// monsters on the field" (Dark Hole) branch must leave it to this batch.
        /// </summary>
        public static bool IsCyberJarText(string text) =>
            !string.IsNullOrEmpty(text) && RxCyberJar.IsMatch(text);

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
