namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Official Yu-Gi-Oh! Main Monster Zone layout projected onto a duel disk surface
    /// (Rulebook field structure — Master Rule / modern field).
    /// </summary>
    public enum ArDuelZoneKind
    {
        Monster = 0,
        SpellTrap = 1,
        FieldSpell = 2,
        PendulumLeft = 3,
        PendulumRight = 4,
        /// <summary>Main Deck cradle on disk body (draw / shuffle).</summary>
        MainDeck = 5,
        /// <summary>Graveyard slot on disk body.</summary>
        Graveyard = 6,
        /// <summary>Extra Deck chamber (modern disks).</summary>
        ExtraDeck = 7
    }

    /// <summary>Intended card orientation when locking into a zone.</summary>
    public enum ArZoneOrientation
    {
        /// <summary>Face-up Attack (monsters) or face-up activate (spells).</summary>
        FaceUpAttack,
        /// <summary>Face-up Defense.</summary>
        FaceUpDefense,
        /// <summary>Face-down Defense (set monster) or face-down Set S/T.</summary>
        FaceDownSet
    }
}
