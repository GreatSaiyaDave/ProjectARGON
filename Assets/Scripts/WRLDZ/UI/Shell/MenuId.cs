namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Production menu destinations — see UI_SPEC.md information architecture.
    /// </summary>
    public enum MenuId
    {
        None = 0,
        OvermapHome,
        SystemsHub,
        DeckCollection,
        Inventory,
        StorySeason,
        FormatSelect,
        /// <summary>Player vs AI create sheet (distance / field size → AR duel).</summary>
        ArDuelCreate,
        /// <summary>Player vs Player create + auto distance scan → AR duel.</summary>
        ArDuelPvpCreate,
        Bazaar,
        AvatarProfile,
        Settings,
        ZonePrompt,
        DuelLive,
        TomeRaid,
        Trade,
        /// <summary>Sandbox: seat cards on the Spirit Dueler to preview models.</summary>
        FreeView,
        /// <summary>Host / join tournament rooms (not a map pin).</summary>
        Tournament
    }

    /// <summary>How a menu is presented (dual UI rule).</summary>
    public enum UiPresentation
    {
        /// <summary>Phone / Editor portrait canvas (S23 lab default).</summary>
        NonArPortrait = 0,
        /// <summary>Holographic panels locked to left-arm duel disk.</summary>
        ArDiskHolo = 1,
        /// <summary>World-locked spatial prompt (zone entry).</summary>
        ArWorldPanel = 2
    }
}
