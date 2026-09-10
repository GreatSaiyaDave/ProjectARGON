namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Table-law overlay on an ERAZ snapshot. Not a second card database.
        /// Duelist Kingdom (GDD 3.3): 2000 LP, no direct attacks. Live for badge-gated
        /// Format Select PvAI only. Story stays 8000 / no overlay. Tribute-free and
        /// no S/T LP damage ship only when the engine can enforce them honestly.
    /// </summary>
    public class DuelRulesOverlay
    {
        public bool DuelistKingdom;
        public bool ForbidDirectAttacks;
        public int OverrideStartingLp;

        public static DuelRulesOverlay DuelistKingdomTable() => new()
        {
            DuelistKingdom = true,
            ForbidDirectAttacks = true,
            OverrideStartingLp = 2000
        };
    }
}
