namespace WRLDZ.Data
{
    /// <summary>
    /// The spirit companions (3D models: Resources/WRLDZ/Companions/&lt;id&gt;.fbx + &lt;id&gt;.png, built by
    /// ~/argon-avatar/tools/custom/kuri_build.py from Meshy generations of the game's own navi art).
    /// Darkuriboh (card 1035143) is built from the Kuriboh model by ~/argon-avatar/tools/custom/darkuriboh_build.py
    /// (2026-10-04): the same furball with the card's angry half-disc eyes and its bat wings (Wing_L / Wing_R bones). The
    /// wings are the card's own, traced at 3x and lifted to 3D by darkuriboh_wing_trace.py (one mirrored wing fitted to
    /// both wings on the card), mounted sweeping BACK from the crown as the owner directed (violet seen from the front, dark
    /// navy from behind); the
    /// FBX carries BodyMin / BodyMax markers so <see cref="WRLDZ.Presentation.SpiritCompanionModel"/> sizes the furball
    /// like the plain Kuriboh and the wings stand above it.
    /// Each Kuriboh team brings its Kuriboh; Kuriboh, Winged Kuriboh and Darkuriboh are extra spirits (lab admin can pick any).
    /// </summary>
    public static class SpiritCompanions
    {
        public sealed class Def
        {
            public string Id, Name, Blurb;
            public KuribohTeam Team;
            /// <summary>Wing meshes flap (the model carries Wing_L / Wing_R parts).</summary>
            public bool Winged;
        }

        public static readonly Def[] All =
        {
            new() { Id = "kuriboh", Name = "Kuriboh", Blurb = "The original furball. Always in your corner.", Team = KuribohTeam.None },
            new() { Id = "winged_kuriboh", Name = "Winged Kuriboh", Blurb = "Kuriboh with a hero's wings.", Team = KuribohTeam.None, Winged = true },
            new() { Id = "junkuriboh", Name = "Junkuriboh", Blurb = "Scrap-plated and stubborn.", Team = KuribohTeam.Junkuriboh },
            new() { Id = "galactikuriboh", Name = "Galactikuriboh", Blurb = "Star-dusted cosmic fluff.", Team = KuribohTeam.Galactikuriboh },
            new() { Id = "kuribandit", Name = "Kuribandit", Blurb = "Masked, sly, and carrying loot.", Team = KuribohTeam.Kuribandit },
            new() { Id = "darkuriboh", Name = "Darkuriboh", Blurb = "A Kuriboh from the Shadow Realm.", Team = KuribohTeam.None, Winged = true },
        };

        public static Def Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in All) if (d.Id == id) return d;
            return null;
        }

        public static string ForTeam(KuribohTeam team) => team switch
        {
            KuribohTeam.Galactikuriboh => "galactikuriboh",
            KuribohTeam.Kuribandit => "kuribandit",
            KuribohTeam.Junkuriboh => "junkuriboh",
            _ => ""
        };

        /// <summary>The companion to show: the chosen one when allowed (admin: any; players: their team's), else the team's.</summary>
        public static string Resolve(PlayerProgress p, bool isAdmin)
        {
            if (p == null) return "";
            var team = ForTeam(p.Team);
            var chosen = p.companionId ?? "";
            if (chosen.Length > 0 && Find(chosen) != null && (isAdmin || chosen == team)) return chosen;
            return team;
        }
    }
}
