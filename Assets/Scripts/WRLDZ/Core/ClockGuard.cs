using System;
using System.Globalization;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Monotonic, per-account wall clock for rewards and cooldowns (QC F7).
    ///
    /// There is no server time yet (backend pending), so every daily / weekly reset, loot lock and harvest
    /// cooldown runs on the device clock. This guard remembers the latest UTC second each account has seen
    /// (<see cref="PlayerProgress.clockHighWaterUnix"/>) and never reports anything earlier: rolling the
    /// device date back does not regenerate dailies, re-open loot, add stamps or restart cooldowns. A forward
    /// jump only borrows from the future — after setting the clock back the account waits for real time to
    /// catch up. The high-water mark lives in the account save, so it is per account, not per device.
    ///
    /// Swap <see cref="DeviceNowUnix"/> for trusted server time when the backend exists.
    /// </summary>
    public static class ClockGuard
    {
        /// <summary>Backward steps up to this size (NTP corrections) are absorbed without a warning.</summary>
        public const long RollbackToleranceSec = 5 * 60;

        static bool _warned;

        public static long DeviceNowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Trusted "now" for <paramref name="p"/>: device UTC, but never earlier than the account has seen.</summary>
        public static long NowUnix(PlayerProgress p) => NowUnix(p, DeviceNowUnix());

        /// <summary>
        /// Same, with an explicit device reading (tests, callers that already sampled the clock).
        /// Advances the account's high-water mark; the caller's next save persists it.
        /// </summary>
        public static long NowUnix(PlayerProgress p, long deviceNowUnix)
        {
            if (p == null) return deviceNowUnix;
            if (deviceNowUnix >= p.clockHighWaterUnix)
            {
                p.clockHighWaterUnix = deviceNowUnix;
                return deviceNowUnix;
            }

            if (!_warned && p.clockHighWaterUnix - deviceNowUnix > RollbackToleranceSec)
            {
                _warned = true;
                Debug.LogWarning(
                    $"[WRLDZ] Device clock is {(p.clockHighWaterUnix - deviceNowUnix) / 60} min behind this " +
                    "account's last seen time — daily resets and cooldowns hold until it catches up.");
            }

            return p.clockHighWaterUnix;
        }

        public static DateTime UtcNow(PlayerProgress p) =>
            DateTimeOffset.FromUnixTimeSeconds(NowUnix(p)).UtcDateTime;

        /// <summary>UTC day key (yyyy-MM-dd, invariant culture) on the guarded clock.</summary>
        public static string DayKey(PlayerProgress p) => DayKey(UtcNow(p));

        public static string DayKey(DateTime utc) =>
            utc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>True when the device clock currently reads earlier than this account has already seen.</summary>
        public static bool DeviceClockBehind(PlayerProgress p) =>
            p != null && DeviceNowUnix() + RollbackToleranceSec < p.clockHighWaterUnix;
    }
}
