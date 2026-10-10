using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BothSideAndMonsterEquip" (v161): a monster Card treated as an Equip Spell Card with a granted effect.
    /// <list type="bullet">
    /// <item>Sword Hunter (c51345461): at the end of the Battle Phase it equips every monster it destroyed by battle and
    /// sent to the GY this Battle Phase to itself, each "as an Equip Spell Card with this effect: the equipped monster
    /// gains 200 ATK". Runtime: <see cref="BothSideAndMonsterEquipRuntime"/>.</item>
    /// </list>
    /// The atom is the card's whole text (start of text or after a sentence end, through its final period), so a longer
    /// card that only contains part of it stays uncompiled. Royal Oppression and Fusion Gate ("Either player can" / "The
    /// turn player can" use a face-up card the opponent controls) are deferred: the DuelUI cannot start an activation of
    /// a card in the opponent's zones, so they have no atom.
    /// </summary>
    public static class BothSideAndMonsterEquipAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Sword Hunter (c51345461): EVENT_PHASE+PHASE_BATTLE mandatory trigger in the Monster Zone, once per turn.
            // s.filter: REASON_BATTLE, GetReasonCard()==this card, sent this turn, in either GY. s.eqop: nothing when
            // there are more of them than free Spell & Trap Zones; else Duel.Equip each, with an EFFECT_EQUIP_LIMIT
            // (this card only) and an EFFECT_UPDATE_ATTACK +200 registered on the equipped card.
            new AtomRegistration.Atom(
                S + @"At the end of the Battle Phase, if this card destroyed a monster\(s\) by battle and sent it to the " +
                @"(?:Graveyard|GY) this Battle Phase: Equip all those monsters from the (?:Graveyard|GY) to this card as " +
                @"an Equip Spell Card\(s\) with this effect\.\s*●\s*The equipped monster gains (\d+) ATK" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndOfBattlePhase,
                    Action = EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells,
                    Amount = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),
        };
    }
}
