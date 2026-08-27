using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Efficient local player account database for WRLDZ / Project ARGON.
    ///
    /// Design (device-local, no server yet):
    ///   persistentDataPath/wrldz_db/
    ///     meta.json              — age gate, terms, schema version
    ///     session.json           — active login (full account snapshot)
    ///     accounts/&lt;user&gt;.json  — one file per account (O(1) load by username)
    ///     accounts/_index.json   — username list for existence checks
    ///
    /// Why not a single JsonUtility List file?
    ///   Unity JsonUtility has historically dropped List&lt;T&gt; contents in some cases;
    ///   we observed empty "accounts":[] after create. Per-record files are
    ///   reliable, fast for single-user login, and easy to extend.
    ///
    /// Session is dual-written to PlayerPrefs for instant cold-start restore.
    /// </summary>
    public static class PlayerAccountDatabase
    {
        public const int SchemaVersion = 3;
        const string RootFolder = "wrldz_db";
        const string PrefsSessionUser = "wrldz_v2_session_user";
        const string PrefsSessionBlob = "wrldz_v2_session_blob";

        [Serializable]
        public class Account
        {
            public string id;           // stable guid
            public string username;     // lower-case unique key
            public string displayName;
            public string passwordHash;
            public string email;
            public string avatarColor = "#3ECFFF"; // legacy accent; mirrored from avatar.accentHex
            /// <summary>Full Spirit Dueler look (body/hair/outfit/…). Null on very old saves.</summary>
            public WRLDZ.Data.AvatarAppearance avatar;
            public string createdUtc;
            public string lastLoginUtc;
            public int spiritRank = 1;
            public int sealsUnlocked;
            public float pathKm;        // distance explored
            public int duelsCompleted;
            public int cardsCollected;

            /// <summary>True after every soul fracture is spent in a Shadow Game.</summary>
            public bool deactivated;
            public string deactivatedUtc = "";
            public string deactivatedReason = "";

            /// <summary>Level, XP, currencies, Kuriboh, onboarding flags.</summary>
            public WRLDZ.Data.PlayerProgress progress;
            /// <summary>Backpack, binder, deck boxes, Tome.</summary>
            public WRLDZ.Data.PlayerInventory inventory;

            public void EnsureProgress()
            {
                if (progress == null)
                    progress = WRLDZ.Data.PlayerProgress.DefaultNew();
                progress.EnsureValid();
                SetOrbService.Ensure(progress);
                if (spiritRank < progress.level)
                    spiritRank = progress.level;
            }

            public void EnsureInventory()
            {
                if (inventory == null)
                    inventory = WRLDZ.Data.PlayerInventory.Empty();
                inventory.EnsureValid();
                if (avatar == null) avatar = WRLDZ.Data.AvatarAppearance.Default();
                ClothingService.EnsureOutfit(avatar, inventory);
            }
        }

        [Serializable]
        class Meta
        {
            public int schemaVersion = SchemaVersion;
            public string dobIso = "";
            public bool ageGatePassed;
            public bool termsAccepted;
        }

        [Serializable]
        class IndexFile
        {
            public string[] usernames = Array.Empty<string>();
        }

        static Meta _meta;
        static HashSet<string> _index;
        static bool _ready;
        static readonly object Gate = new();

        static string Root => Path.Combine(Application.persistentDataPath, RootFolder);
        static string MetaPath => Path.Combine(Root, "meta.json");
        static string SessionPath => Path.Combine(Root, "session.json");
        static string AccountsDir => Path.Combine(Root, "accounts");
        static string IndexPath => Path.Combine(AccountsDir, "_index.json");

        static string AccountPath(string username) =>
            Path.Combine(AccountsDir, SanitizeFileName(NormalizeUser(username)) + ".json");

        public static string DatabasePath => Root;

        public static void Initialize()
        {
            lock (Gate)
            {
                if (_ready) return;
                Directory.CreateDirectory(AccountsDir);
                LoadMeta();
                LoadIndex();
                MigrateLegacyJsonIfNeeded();
                _ready = true;
                Debug.Log($"[WRLDZ] Account DB ready at {Root} (accounts={_index.Count}, age={_meta.ageGatePassed}, terms={_meta.termsAccepted})");
            }
        }

        // ── Meta / age / terms ──────────────────────────────────────────────

        public static bool AgeGatePassed
        {
            get { Initialize(); return _meta.ageGatePassed; }
        }

        public static string DobIso
        {
            get { Initialize(); return _meta.dobIso ?? ""; }
        }

        public static bool TermsAccepted
        {
            get { Initialize(); return _meta.termsAccepted; }
            set
            {
                Initialize();
                _meta.termsAccepted = value;
                SaveMeta();
            }
        }

        public static bool TryPassAgeGate(int year, int month, int day, int minAge, out string error)
        {
            error = null;
            Initialize();
            try
            {
                var dob = new DateTime(year, month, day);
                if (dob > DateTime.UtcNow.Date)
                {
                    error = "That date is in the future.";
                    return false;
                }

                var age = DateTime.UtcNow.Date.Year - dob.Year;
                if (dob.Date > DateTime.UtcNow.Date.AddYears(-age)) age--;
                if (age < minAge)
                {
                    error = $"You must be at least {minAge} to play WRLDZ.";
                    return false;
                }

                _meta.dobIso = dob.ToString("yyyy-MM-dd");
                _meta.ageGatePassed = true;
                SaveMeta();
                return true;
            }
            catch
            {
                error = "Enter a valid date of birth.";
                return false;
            }
        }

        // ── Accounts ────────────────────────────────────────────────────────

        public static bool UsernameExists(string username)
        {
            Initialize();
            return _index.Contains(NormalizeUser(username));
        }

        public static bool TryCreate(string username, string password, string displayName, string email,
            out Account account, out string error)
        {
            account = null;
            error = null;
            Initialize();

            username = NormalizeUser(username);
            displayName = (displayName ?? "").Trim();
            if (username.Length < 3)
            {
                error = "Username must be at least 3 characters.";
                return false;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                error = "Password must be at least 4 characters.";
                return false;
            }

            if (string.IsNullOrEmpty(displayName))
                displayName = username;

            if (UsernameExists(username))
            {
                error = "That username is already taken.";
                return false;
            }

            var accent = RandomAvatarHex();
            account = new Account
            {
                id = Guid.NewGuid().ToString("N"),
                username = username,
                displayName = displayName,
                passwordHash = HashPassword(password),
                email = (email ?? "").Trim(),
                avatarColor = accent,
                avatar = new WRLDZ.Data.AvatarAppearance { accentHex = accent },
                createdUtc = DateTime.UtcNow.ToString("o"),
                lastLoginUtc = DateTime.UtcNow.ToString("o"),
                spiritRank = 1,
                progress = WRLDZ.Data.PlayerProgress.DefaultNew(),
                inventory = WRLDZ.Data.PlayerInventory.Empty()
            };

            if (!WriteAccount(account))
            {
                error = "Failed to write account to database.";
                account = null;
                return false;
            }

            _index.Add(username);
            SaveIndex();
            // Terms are accepted only via Boot Terms step (menu structure).
            SetSession(account);

            Debug.Log($"[WRLDZ] Account created: {username} → {AccountPath(username)}");
            return true;
        }

        public static bool TryLogin(string username, string password, out Account account, out string error)
        {
            account = null;
            error = null;
            Initialize();
            username = NormalizeUser(username);
            account = ReadAccount(username);
            if (account == null || account.passwordHash != HashPassword(password ?? ""))
            {
                error = "Incorrect username or password.";
                account = null;
                return false;
            }

            account.EnsureProgress();
            SoulFractureService.SyncDeactivation(account);
            if (account.deactivated)
            {
                WriteAccount(account);
                error = SoulFractureService.DeactivatedLoginMessage;
                account = null;
                return false;
            }

            account.lastLoginUtc = DateTime.UtcNow.ToString("o");
            WriteAccount(account);
            SetSession(account);
            Debug.Log($"[WRLDZ] Login OK: {username}");
            return true;
        }

        public static Account GetSessionAccount()
        {
            Initialize();

            // 1) Full blob in PlayerPrefs (fastest, survives list bugs)
            var blob = PlayerPrefs.GetString(PrefsSessionBlob, "");
            if (!string.IsNullOrEmpty(blob))
            {
                try
                {
                    var fromPrefs = JsonUtility.FromJson<Account>(blob);
                    if (fromPrefs != null && !string.IsNullOrEmpty(fromPrefs.username))
                    {
                        // Rehydrate from disk if file exists (fresh stats)
                        var disk = ReadAccount(fromPrefs.username);
                        return disk ?? fromPrefs;
                    }
                }
                catch { /* ignore */ }
            }

            // 2) session.json
            if (File.Exists(SessionPath))
            {
                try
                {
                    var fromFile = JsonUtility.FromJson<Account>(File.ReadAllText(SessionPath));
                    if (fromFile != null && !string.IsNullOrEmpty(fromFile.username))
                        return ReadAccount(fromFile.username) ?? fromFile;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] session.json read: " + ex.Message);
                }
            }

            // 3) username key only
            var user = PlayerPrefs.GetString(PrefsSessionUser, "");
            if (!string.IsNullOrEmpty(user))
                return ReadAccount(user);

            return null;
        }

        public static void SetSession(Account account)
        {
            if (account == null)
            {
                ClearSession();
                return;
            }

            Initialize();
            var json = JsonUtility.ToJson(account, false);
            try
            {
                File.WriteAllText(SessionPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] session.json write: " + ex.Message);
            }

            PlayerPrefs.SetString(PrefsSessionUser, account.username);
            // Never stuff a full catalog inventory into PlayerPrefs — that freeze-locks the editor.
            PlayerPrefs.DeleteKey(PrefsSessionBlob);
            PlayerPrefs.Save();
        }

        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionPath)) File.Delete(SessionPath);
            }
            catch { /* ignore */ }

            PlayerPrefs.DeleteKey(PrefsSessionUser);
            PlayerPrefs.DeleteKey(PrefsSessionBlob);
            // legacy keys
            PlayerPrefs.DeleteKey("wrldz_session_user");
            PlayerPrefs.Save();
        }

        public static void UpdateAccount(Account account)
        {
            if (account == null) return;
            Initialize();
            WriteAccount(account);
            if (GetSessionAccount()?.username == account.username)
                SetSession(account);
        }

        public static IReadOnlyCollection<string> ListUsernames()
        {
            Initialize();
            return _index;
        }

        // ── IO ──────────────────────────────────────────────────────────────

        static bool WriteAccount(Account account)
        {
            try
            {
                Directory.CreateDirectory(AccountsDir);
                var path = AccountPath(account.username);
                var json = JsonUtility.ToJson(account, true);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] WriteAccount: " + ex.Message);
                return false;
            }
        }

        static Account ReadAccount(string username)
        {
            username = NormalizeUser(username);
            if (string.IsNullOrEmpty(username)) return null;
            var path = AccountPath(username);
            if (!File.Exists(path)) return null;
            try
            {
                var acc = JsonUtility.FromJson<Account>(File.ReadAllText(path));
                if (acc != null)
                {
                    acc.EnsureProgress();
                    acc.EnsureInventory();
                }

                return acc;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] ReadAccount: " + ex.Message);
                return null;
            }
        }

        static void LoadMeta()
        {
            if (File.Exists(MetaPath))
            {
                try
                {
                    _meta = JsonUtility.FromJson<Meta>(File.ReadAllText(MetaPath)) ?? new Meta();
                }
                catch
                {
                    _meta = new Meta();
                }
            }
            else
                _meta = new Meta();

            _meta.schemaVersion = SchemaVersion;
        }

        static void SaveMeta()
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(MetaPath, JsonUtility.ToJson(_meta, true));
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] SaveMeta: " + ex.Message);
            }
        }

        static void LoadIndex()
        {
            _index = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(IndexPath))
            {
                try
                {
                    var idx = JsonUtility.FromJson<IndexFile>(File.ReadAllText(IndexPath));
                    if (idx?.usernames != null)
                        foreach (var u in idx.usernames)
                            if (!string.IsNullOrEmpty(u)) _index.Add(u);
                }
                catch { /* rebuild below */ }
            }

            // Rebuild index from files on disk (source of truth)
            try
            {
                if (Directory.Exists(AccountsDir))
                {
                    foreach (var f in Directory.GetFiles(AccountsDir, "*.json"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        if (name.StartsWith("_")) continue;
                        _index.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] index scan: " + ex.Message);
            }

            SaveIndex();
        }

        static void SaveIndex()
        {
            try
            {
                Directory.CreateDirectory(AccountsDir);
                var arr = new string[_index.Count];
                _index.CopyTo(arr);
                Array.Sort(arr, StringComparer.Ordinal);
                File.WriteAllText(IndexPath, JsonUtility.ToJson(new IndexFile { usernames = arr }, true));
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] SaveIndex: " + ex.Message);
            }
        }

        /// <summary>Import broken legacy wrldz_accounts.json if present.</summary>
        static void MigrateLegacyJsonIfNeeded()
        {
            var legacy = Path.Combine(Application.persistentDataPath, "wrldz_accounts.json");
            if (!File.Exists(legacy)) return;

            try
            {
                var text = File.ReadAllText(legacy);
                // Pull meta flags even if accounts list is empty
                if (text.Contains("\"ageGatePassed\": true") || text.Contains("\"ageGatePassed\":true"))
                    _meta.ageGatePassed = true;
                if (text.Contains("\"termsAccepted\": true") || text.Contains("\"termsAccepted\":true"))
                    _meta.termsAccepted = true;

                var dobKey = "\"dobIso\":";
                var di = text.IndexOf(dobKey, StringComparison.Ordinal);
                if (di >= 0)
                {
                    var q1 = text.IndexOf('"', di + dobKey.Length);
                    var q2 = text.IndexOf('"', q1 + 1);
                    if (q1 >= 0 && q2 > q1)
                        _meta.dobIso = text.Substring(q1 + 1, q2 - q1 - 1);
                }

                SaveMeta();

                // Rename so we don't re-migrate
                var bak = legacy + ".migrated";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(legacy, bak);
                Debug.Log("[WRLDZ] Migrated legacy account meta from wrldz_accounts.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Legacy migrate: " + ex.Message);
            }

            // Legacy session key
            var oldUser = PlayerPrefs.GetString("wrldz_session_user", "");
            if (!string.IsNullOrEmpty(oldUser) && string.IsNullOrEmpty(PlayerPrefs.GetString(PrefsSessionUser, "")))
                PlayerPrefs.SetString(PrefsSessionUser, oldUser);
        }

        static string NormalizeUser(string u) => (u ?? "").Trim().ToLowerInvariant();

        static string SanitizeFileName(string u)
        {
            var sb = new StringBuilder(u.Length);
            foreach (var c in u)
            {
                if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            return sb.Length > 0 ? sb.ToString() : "player";
        }

        static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("wrldz.v2|" + password));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static string RandomAvatarHex()
        {
            var colors = new[]
            {
                "#3ECFFF", "#FFD84A", "#FF5AA5", "#5CFF9A", "#B07CFF", "#FF7A3C", "#FFFFFF"
            };
            return colors[UnityEngine.Random.Range(0, colors.Length)];
        }
    }
}
