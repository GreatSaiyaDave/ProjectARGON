namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Shared navigation labels / toasts so Overworld, hub, AR companion, and shells
    /// use the same words for the same destinations.
    /// </summary>
    public static class NavCopy
    {
        public const string HowToLeave = "× closes";

        public const string DeckShort = "DECK";
        public const string DeckLong = "DECK";
        public const string BagShort = "BAG";
        public const string BagLong = "BAG";
        public const string SystemsShort = "MENU";
        public const string SystemsLong = "MENU";
        public const string VsAi = "VS AI";
        public const string VsPvp = "VS PVP";
        public const string Settings = "SETTINGS";

        public static string TitleFor(MenuId id) => id switch
        {
            MenuId.DeckCollection => "DECK",
            MenuId.Inventory => "BAG",
            MenuId.Settings => "SETTINGS",
            MenuId.TomeRaid => "TOME",
            MenuId.Bazaar => "BAZAAR",
            MenuId.StorySeason => "STORY",
            MenuId.AvatarProfile => "PROFILE",
            MenuId.ArDuelCreate => "VS AI",
            MenuId.ArDuelPvpCreate => "VS PVP",
            MenuId.FormatSelect => "FORMATS",
            MenuId.FreeView => "FREE VIEW",
            MenuId.Tournament => "TOURNEY",
            _ => id.ToString().ToUpperInvariant()
        };

        public static string BlurbFor(MenuId id) => id switch
        {
            MenuId.DeckCollection => "Decks & collection",
            MenuId.Inventory => "What you carry",
            MenuId.Settings => "Chrome & device",
            MenuId.TomeRaid => "Raid pages",
            MenuId.Bazaar => "Tablets & packs",
            MenuId.StorySeason => "Story Zone",
            MenuId.AvatarProfile => "Avatar look",
            MenuId.ArDuelCreate => "Field distance",
            MenuId.ArDuelPvpCreate => "Scan distance",
            MenuId.FreeView => "Place cards · view models",
            MenuId.Tournament => "Host a room · start when enough join",
            _ => ""
        };

        /// <summary>One-line toast when a sheet opens.</summary>
        public static string ToastFor(MenuId id, UiPresentation? mode = null) =>
            TitleFor(id);

        /// <summary>Deck editor phase breadcrumb (Select → Options → Edit).</summary>
        public static string DeckBreadcrumb(string phase, string deckName = null)
        {
            var name = string.IsNullOrEmpty(deckName) ? "Deck" : deckName;
            return phase switch
            {
                "actions" => "DECKS  ›  " + name,
                "editor" => "DECKS  ›  " + name + "  ›  EDIT",
                _ => "DECKS"
            };
        }

        public static string DeckHint(string phase) => phase switch
        {
            "actions" => "Edit · rename · clear",
            "editor" => "Build · SAVE",
            _ => "Pick a deck"
        };
    }
}
