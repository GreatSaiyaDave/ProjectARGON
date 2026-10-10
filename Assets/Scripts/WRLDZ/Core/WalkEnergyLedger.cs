using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Walk Set Energy daily cap kept in the account save on the guarded clock (QC F7): the count follows
    /// the player across devices / reinstalls, and setting the date back cannot reset it. Callers reserve
    /// before granting, then grant and persist in the same save.
    /// </summary>
    public static class WalkEnergyLedger
    {
        /// <summary>Walk SE already credited today (guarded UTC day).</summary>
        public static int EarnedToday(PlayerProgress p)
        {
            if (p == null) return 0;
            var day = ClockGuard.DayKey(p);
            return string.Equals(p.walkSeDayUtc, day, System.StringComparison.Ordinal) ? p.walkSeToday : 0;
        }

        /// <summary>
        /// Reserve up to <paramref name="amount"/> SE under <paramref name="dailyCap"/> and record it. Returns
        /// the amount actually allowed (0 when capped). The day only moves forward (a later guarded day resets).
        /// </summary>
        public static int Reserve(PlayerProgress p, int amount, int dailyCap)
        {
            if (p == null || amount <= 0 || dailyCap <= 0) return 0;
            var day = ClockGuard.DayKey(p);
            if (string.CompareOrdinal(day, p.walkSeDayUtc ?? "") > 0)
            {
                p.walkSeDayUtc = day;
                p.walkSeToday = 0;
            }

            var room = dailyCap - p.walkSeToday;
            if (room <= 0) return 0;
            var allowed = amount < room ? amount : room;
            p.walkSeToday += allowed;
            return allowed;
        }
    }
}
