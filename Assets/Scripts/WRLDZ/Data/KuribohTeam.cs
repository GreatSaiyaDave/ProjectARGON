namespace WRLDZ.Data
{
    /// <summary>
    /// Player Navi / team spirit. Sets starter deck bias and early Tome path.
    /// </summary>
    public enum KuribohTeam
    {
        None = 0,
        /// <summary>Galactikuriboh — cosmic / raid-leaning path.</summary>
        Galactikuriboh = 1,
        /// <summary>Kuribandit — trickster / tempo path.</summary>
        Kuribandit = 2,
        /// <summary>Junkuriboh — scrap / underdog path.</summary>
        Junkuriboh = 3
    }

    public static class KuribohTeamInfo
    {
        public static string DisplayName(KuribohTeam t) => t switch
        {
            KuribohTeam.Galactikuriboh => "Galactikuriboh",
            KuribohTeam.Kuribandit => "Kuribandit",
            KuribohTeam.Junkuriboh => "Junkuriboh",
            _ => "Unbound"
        };

        public static string Blurb(KuribohTeam t) => t switch
        {
            KuribohTeam.Galactikuriboh => "Cosmic Navi. Guides raid spirits and the stars above Tears.",
            KuribohTeam.Kuribandit => "Trickster Navi. Finds angles in the bazaar and the shadow.",
            KuribohTeam.Junkuriboh => "Scrap Navi. Turns castoffs into champions.",
            _ => "Choose a Kuriboh spirit to grant you sorcerer rights."
        };

        /// <summary>StreamingAssets/Decks file for digital starter (40/15/15).</summary>
        public static string StarterDeckFile(KuribohTeam t) => t switch
        {
            KuribohTeam.Galactikuriboh => "starter_galactikuriboh.json",
            KuribohTeam.Kuribandit => "starter_kuribandit.json",
            KuribohTeam.Junkuriboh => "starter_junkuriboh.json",
            _ => "player_starter.json"
        };
    }
}
