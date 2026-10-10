using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Crash / swipe-away forfeit (owner rule 2026-09-23 #3, QC forfeit dodge). A rewarded duel writes a
    /// marker into the account save when it starts (<see cref="Arm"/>); claiming its result
    /// (<see cref="ProgressionService.AwardDuelRewards"/>) clears it in the same save. A marker that survives
    /// into a later app launch belongs to a duel that never finished — the app was killed, swiped from
    /// recents or reaped in the background — and <see cref="SettleAbandoned"/> records it as a forfeit loss
    /// (streaks, stakes, Shadow Game soul rules), exactly like leaving through Map.
    ///
    /// Duels started in this process are never settled here: the live duel flow (DuelUI / AppSession.LeaveDuel)
    /// owns them, and <see cref="AppSession.RefreshFromStore"/> runs many times while a duel is up.
    /// </summary>
    public static class PendingDuelMarker
    {
        static readonly HashSet<string> ArmedThisProcess = new(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ArmedThisProcess.Clear();

        /// <summary>
        /// Record the running rewarded duel on the account and save. Practice / lab / hotseat duels and lab
        /// test mode are not rewarded, so they are never marked.
        /// </summary>
        public static bool Arm(LocalAccountStore.Account acc, ArDuelMatchConfig match, bool testDuelMode)
        {
            if (testDuelMode || !ProgressionService.IsForfeitable(acc, match)) return false;
            acc.EnsureProgress();
            var p = acc.progress;
            string json;
            try
            {
                // Copy through JSON so the saved config carries no GPS fix (the forfeit does not need it).
                var copy = JsonUtility.FromJson<ArDuelMatchConfig>(JsonUtility.ToJson(match));
                copy.HostLatitude = copy.HostLongitude = 0;
                copy.GuestLatitude = copy.GuestLongitude = 0;
                copy.HostGpsValid = copy.GuestGpsValid = false;
                json = JsonUtility.ToJson(copy);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Pending duel marker: config copy failed — " + ex.Message);
                json = "";
            }

            ArmedThisProcess.Add(match.DuelResultId);
            p.pendingDuelId = match.DuelResultId;
            p.pendingDuelMatchJson = json;
            p.pendingDuelStartedUnix = ClockGuard.NowUnix(p);
            ProgressionService.Persist(acc);
            return true;
        }

        /// <summary>The duel <paramref name="duelResultId"/> was claimed: drop its marker (caller saves).</summary>
        public static void ClearIfMatches(PlayerProgress p, string duelResultId)
        {
            if (p == null || string.IsNullOrEmpty(duelResultId)) return;
            if (!string.Equals(p.pendingDuelId, duelResultId, StringComparison.Ordinal)) return;
            Clear(p);
        }

        static void Clear(PlayerProgress p)
        {
            p.pendingDuelId = "";
            p.pendingDuelMatchJson = "";
            p.pendingDuelStartedUnix = 0;
        }

        /// <summary>
        /// Settle a rewarded duel left unfinished by an earlier process as a forfeit loss. Returns true when
        /// the account changed (forfeit recorded or a stale marker dropped); the forfeit path saves itself.
        /// </summary>
        public static bool SettleAbandoned(LocalAccountStore.Account acc)
        {
            if (acc == null) return false;
            acc.EnsureProgress();
            var p = acc.progress;
            var id = p.pendingDuelId;
            if (string.IsNullOrEmpty(id) || ArmedThisProcess.Contains(id)) return false;

            if (p.HasClaimedDuel(id))
            {
                Clear(p);
                return true;
            }

            ArDuelMatchConfig match = null;
            if (!string.IsNullOrEmpty(p.pendingDuelMatchJson))
            {
                try
                {
                    match = JsonUtility.FromJson<ArDuelMatchConfig>(p.pendingDuelMatchJson);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] Pending duel marker unreadable — " + ex.Message);
                }
            }

            // An unreadable config still costs the duel: forfeit it as a plain rewarded CPU duel.
            match ??= ArDuelMatchConfig.DefaultQuick();
            match.DuelResultId = id;
            if (match.IsPracticeOrLab || match.IsSameDeviceHotseat)
            {
                Clear(p);
                return true;
            }

            var r = ProgressionService.ForfeitDuel(acc, match, turnNumber: 0);
            Clear(p); // AwardDuelRewards already cleared it; this also covers an already-claimed result
            ProgressionService.Persist(acc);
            Debug.Log($"[WRLDZ] Unfinished duel from an earlier session ({match.Launch} · {match.FormatTitle}) " +
                      $"settled as a forfeit · {r.SummaryLine}");
            return true;
        }
    }
}
