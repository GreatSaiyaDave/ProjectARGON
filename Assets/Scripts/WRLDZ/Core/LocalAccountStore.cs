using System;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Compatibility facade over <see cref="PlayerAccountDatabase"/>.
    /// Keeps existing Boot/AppSession call sites working.
    /// </summary>
    public static class LocalAccountStore
    {
        /// <summary>Alias for DB account record.</summary>
        [Serializable]
        public class Account
        {
            public string id;
            public string username;
            public string displayName;
            public string passwordHash;
            public string email;
            public string avatarColor = "#3ECFFF";
            public AvatarAppearance avatar;
            public string createdUtc;
            public string lastLoginUtc;
            public int spiritRank = 1;
            public int sealsUnlocked;
            public float pathKm;
            public int duelsCompleted;
            public int cardsCollected;
            public bool deactivated;
            public string deactivatedUtc = "";
            public string deactivatedReason = "";
            public PlayerProgress progress;
            public PlayerInventory inventory;

            public static Account FromDb(PlayerAccountDatabase.Account a)
            {
                if (a == null) return null;
                a.EnsureProgress();
                a.EnsureInventory();
                return new Account
                {
                    id = a.id,
                    username = a.username,
                    displayName = a.displayName,
                    passwordHash = a.passwordHash,
                    email = a.email,
                    avatarColor = a.avatarColor,
                    avatar = a.avatar != null ? a.avatar.Clone() : null,
                    createdUtc = a.createdUtc,
                    lastLoginUtc = a.lastLoginUtc,
                    spiritRank = a.spiritRank,
                    sealsUnlocked = a.sealsUnlocked,
                    pathKm = a.pathKm,
                    duelsCompleted = a.duelsCompleted,
                    cardsCollected = a.cardsCollected,
                    deactivated = a.deactivated,
                    deactivatedUtc = a.deactivatedUtc,
                    deactivatedReason = a.deactivatedReason,
                    progress = a.progress,
                    inventory = a.inventory
                };
            }

            public PlayerAccountDatabase.Account ToDb()
            {
                EnsureProgress();
                EnsureInventory();
                return new PlayerAccountDatabase.Account
                {
                    id = id,
                    username = username,
                    displayName = displayName,
                    passwordHash = passwordHash,
                    email = email,
                    avatarColor = avatarColor,
                    avatar = avatar != null ? avatar.Clone() : null,
                    createdUtc = createdUtc,
                    lastLoginUtc = lastLoginUtc,
                    spiritRank = spiritRank,
                    sealsUnlocked = sealsUnlocked,
                    pathKm = pathKm,
                    duelsCompleted = duelsCompleted,
                    cardsCollected = cardsCollected,
                    deactivated = deactivated,
                    deactivatedUtc = deactivatedUtc,
                    deactivatedReason = deactivatedReason,
                    progress = progress,
                    inventory = inventory
                };
            }

            public void EnsureProgress()
            {
                if (progress == null)
                    progress = PlayerProgress.DefaultNew();
                progress.EnsureValid();
                SetOrbService.Ensure(progress);
                if (spiritRank < progress.level)
                    spiritRank = progress.level;
            }

            public void EnsureInventory()
            {
                if (inventory == null)
                    inventory = PlayerInventory.Empty();
                inventory.EnsureValid();
                EnsureProgress();
                ArtifactService.MigrateFromLegacy(progress, inventory);
                if (avatar == null) avatar = AvatarAppearance.Default();
                ClothingService.EnsureOutfit(avatar, inventory);
            }

            public AvatarAppearance GetAvatarOrDefault()
            {
                if (avatar != null)
                {
                    avatar.ClampToCatalog();
                    return avatar;
                }

                var a = AvatarAppearance.Default();
                if (!string.IsNullOrEmpty(avatarColor))
                    a.accentHex = avatarColor;
                return a;
            }
        }

        public static void EnsureLoaded() => PlayerAccountDatabase.Initialize();

        public static bool AgeGatePassed => PlayerAccountDatabase.AgeGatePassed;

        public static string StoredDobIso => PlayerAccountDatabase.DobIso;

        public static bool TermsAccepted
        {
            get => PlayerAccountDatabase.TermsAccepted;
            set => PlayerAccountDatabase.TermsAccepted = value;
        }

        public static bool TryPassAgeGate(int year, int month, int day, int minAge, out string error) =>
            PlayerAccountDatabase.TryPassAgeGate(year, month, day, minAge, out error);

        public static bool UsernameExists(string username) =>
            PlayerAccountDatabase.UsernameExists(username);

        public static bool TryCreate(string username, string password, string displayName, string email,
            out Account account, out string error)
        {
            account = null;
            if (!PlayerAccountDatabase.TryCreate(username, password, displayName, email, out var dbAcc, out error))
                return false;
            account = Account.FromDb(dbAcc);
            return true;
        }

        public static bool TryLogin(string username, string password, out Account account, out string error)
        {
            account = null;
            if (!PlayerAccountDatabase.TryLogin(username, password, out var dbAcc, out error))
                return false;
            account = Account.FromDb(dbAcc);
            TickSoulCards(account);
            return true;
        }

        public static Account GetSessionAccount()
        {
            var acc = Account.FromDb(PlayerAccountDatabase.GetSessionAccount());
            TickSoulCards(acc);
            return acc;
        }

        static void TickSoulCards(Account acc)
        {
            if (acc == null) return;
            acc.EnsureInventory();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var paused = acc.inventory != null && acc.inventory.soulPauseStartedUnix > 0;
            var gone = SoulCardService.Tick(acc.inventory, now, paused);
            if (gone > 0)
            {
                Debug.Log($"[WRLDZ] {gone} soul card(s) returned to the aether.");
                PlayerAccountDatabase.UpdateAccount(acc.ToDb());
            }
        }

        public static void SetSession(string username)
        {
            var acc = PlayerAccountDatabase.GetSessionAccount();
            if (acc != null && acc.username == (username ?? "").Trim().ToLowerInvariant())
            {
                PlayerAccountDatabase.SetSession(acc);
                return;
            }

            PlayerPrefs.SetString("wrldz_v2_session_user", (username ?? "").Trim().ToLowerInvariant());
            PlayerPrefs.Save();
        }

        public static void ClearSession() => PlayerAccountDatabase.ClearSession();

        public static void UpdateAccount(Account account)
        {
            if (account == null) return;
            PlayerAccountDatabase.UpdateAccount(account.ToDb());
        }

        public static void UpdateAvatarColor(string username, string hex)
        {
            var acc = PlayerAccountDatabase.GetSessionAccount();
            if (acc == null || acc.username != (username ?? "").Trim().ToLowerInvariant())
                return;
            acc.avatarColor = hex;
            if (acc.avatar == null) acc.avatar = AvatarAppearance.Default();
            acc.avatar.accentHex = hex;
            PlayerAccountDatabase.UpdateAccount(acc);
        }

        /// <summary>Persist full account (display name, avatar look, etc.) and refresh session.</summary>
        public static bool TrySaveAccount(Account account, out string error)
        {
            error = null;
            if (account == null)
            {
                error = "No account.";
                return false;
            }

            if (account.avatar != null)
            {
                account.avatar.ClampToCatalog();
                account.avatarColor = account.avatar.accentHex;
            }

            account.EnsureProgress();
            account.EnsureInventory();

            try
            {
                PlayerAccountDatabase.UpdateAccount(account.ToDb());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string DatabasePath => PlayerAccountDatabase.DatabasePath;
    }
}
