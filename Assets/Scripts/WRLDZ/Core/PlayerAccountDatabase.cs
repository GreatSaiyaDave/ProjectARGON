using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
            /// <summary>Google account subject (sub). Empty for password-only players.</summary>
            public string googleSub;
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
                EnsureProgress();
                ArtifactService.MigrateFromLegacy(progress, inventory);
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

        /// <summary>
        /// New usernames: 3–24 chars of a–z, 0–9, '_', '-', '.', starting with a letter or digit. The name is
        /// its own file name (no two names can share a save), and a leading '_' can never collide with
        /// _index.json or the __test* fixtures (QC F10). Existing accounts outside the rule still log in.
        /// </summary>
        static readonly Regex UsernamePattern = new("^[a-z0-9][a-z0-9_.-]{2,23}$", RegexOptions.CultureInvariant);

        public const string UsernameRuleMessage =
            "Username: 3–24 letters, digits, '_', '-' or '.', starting with a letter or digit.";

        /// <summary>Names players can never register or be given by Google sign-in (lab door, staff-looking names).</summary>
        static readonly HashSet<string> ReservedUsernames = new(StringComparer.Ordinal)
        {
            AppSession.LabTestUsername, "admin", "administrator", "root", "system", "moderator", "referobot"
        };

        public static bool IsReservedUsername(string username) =>
            ReservedUsernames.Contains(NormalizeUser(username));

        public static bool IsValidNewUsername(string username) =>
            UsernamePattern.IsMatch(NormalizeUser(username)) && !IsReservedUsername(username);

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
            var u = NormalizeUser(username);
            if (u.Length == 0) return false;
            if (_index.Contains(u)) return true;
            // Another name can own this save file (legacy names sanitize: "a b" and "a_b" → a_b.json).
            var path = AccountPath(u);
            return File.Exists(path) || File.Exists(path + ".tmp") || File.Exists(path + ".bak");
        }

        public static bool TryCreate(string username, string password, string displayName, string email,
            out Account account, out string error) =>
            TryCreateCore(username, password, displayName, email, allowReserved: false, out account, out error);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Lab door only (Editor / development builds): may create the reserved lab_tester name.</summary>
        public static bool TryCreateLabAccount(string username, string password, string displayName, string email,
            out Account account, out string error) =>
            TryCreateCore(username, password, displayName, email, allowReserved: true, out account, out error);
#endif

        static bool TryCreateCore(string username, string password, string displayName, string email,
            bool allowReserved, out Account account, out string error)
        {
            account = null;
            error = null;
            Initialize();

            username = NormalizeUser(username);
            displayName = (displayName ?? "").Trim();
            if (!UsernamePattern.IsMatch(username))
            {
                error = UsernameRuleMessage;
                return false;
            }

            if (!allowReserved && IsReservedUsername(username))
            {
                error = "That username is reserved.";
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
            if (!WrldzLab.AccountAllowedInThisBuild(account.progress))
            {
                error = WrldzLab.LabAccountBlockedMessage;
                account = null;
                return false;
            }

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

        /// <summary>
        /// Sign in with a verified Google subject. Finds the player already bound to that subject, or forges a
        /// new player. Never binds to an existing local account by email — the email field is free text and
        /// unverified locally, so matching on it would hand one player's save to anyone whose Google address
        /// matches (QC F8). Binding an existing password account is explicit: <see cref="TryLinkGoogle"/>.
        /// </summary>
        public static bool TryLoginGoogle(string sub, string email, string displayName,
            out Account account, out string error)
        {
            account = null;
            error = null;
            Initialize();
            sub = (sub ?? "").Trim();
            email = (email ?? "").Trim();
            if (sub.Length < 4)
            {
                error = "Google did not return an account id.";
                return false;
            }

            account = FindByGoogleSub(sub);
            if (account == null)
            {
                var user = UsernameFromGoogle(email, sub);
                var accent = RandomAvatarHex();
                account = new Account
                {
                    id = Guid.NewGuid().ToString("N"),
                    username = user,
                    displayName = string.IsNullOrEmpty(displayName) ? user : displayName.Trim(),
                    passwordHash = HashPassword("google|" + sub + "|" + Guid.NewGuid().ToString("N")),
                    email = email,
                    googleSub = sub,
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
                    error = "Failed to write the Google player.";
                    account = null;
                    return false;
                }

                _index.Add(user);
                SaveIndex();
                SetSession(account);
                Debug.Log("[WRLDZ] Google player created: " + user);
                return true;
            }

            if (!string.IsNullOrEmpty(email))
                account.email = email;
            account.EnsureProgress();
            if (!WrldzLab.AccountAllowedInThisBuild(account.progress))
            {
                error = WrldzLab.LabAccountBlockedMessage;
                account = null;
                return false;
            }

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
            Debug.Log("[WRLDZ] Google login OK: " + account.username);
            return true;
        }

        /// <summary>
        /// Explicit link: bind Google subject <paramref name="sub"/> to an existing password account after the
        /// player proves ownership with that account's password. Refuses when the account is already bound to
        /// a different Google account, or when <paramref name="sub"/> already belongs to another player.
        /// </summary>
        public static bool TryLinkGoogle(string username, string password, string sub, string email,
            out Account account, out string error)
        {
            account = null;
            error = null;
            Initialize();
            sub = (sub ?? "").Trim();
            if (sub.Length < 4)
            {
                error = "Google did not return an account id.";
                return false;
            }

            var owner = FindByGoogleSub(sub);
            if (owner != null && owner.username != NormalizeUser(username))
            {
                error = "That Google account already belongs to another Spirit Dueler.";
                return false;
            }

            if (!TryLogin(username, password, out account, out error))
                return false;

            if (!string.IsNullOrEmpty(account.googleSub) &&
                !string.Equals(account.googleSub, sub, StringComparison.Ordinal))
            {
                error = "This Spirit Dueler is already linked to a different Google account.";
                account = null;
                return false;
            }

            account.googleSub = sub;
            if (!string.IsNullOrEmpty(email))
                account.email = email.Trim();
            WriteAccount(account);
            SetSession(account);
            Debug.Log("[WRLDZ] Google account linked to " + account.username);
            return true;
        }

        static Account FindByGoogleSub(string sub)
        {
            if (string.IsNullOrEmpty(sub)) return null;
            foreach (var name in _index)
            {
                var acc = ReadAccountRaw(name);
                if (acc == null || string.IsNullOrEmpty(acc.googleSub)) continue;
                if (!string.Equals(acc.googleSub, sub, StringComparison.Ordinal)) continue;
                acc.EnsureProgress();
                acc.EnsureInventory();
                return acc;
            }

            return null;
        }

        static string UsernameFromGoogle(string email, string sub)
        {
            var local = (email ?? "").Contains("@") ? email.Split('@')[0] : email ?? "";
            var sb = new StringBuilder();
            foreach (var c in local.ToLowerInvariant())
            {
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9' || c == '_')
                    sb.Append(c);
            }

            var baseName = sb.ToString().TrimStart('_');
            if (baseName.Length < 3) baseName = "duelist";
            if (baseName.Length > 16)
                baseName = baseName.Substring(0, 16);
            var name = baseName;
            var n = 2;
            while (UsernameExists(name) || !IsValidNewUsername(name))
            {
                name = baseName + n;
                n++;
                if (n > 40)
                {
                    var tail = sub.Length > 6 ? sub.Substring(sub.Length - 6) : sub;
                    var sbTail = new StringBuilder("g");
                    foreach (var c in tail.ToLowerInvariant())
                        if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sbTail.Append(c);
                    name = sbTail.ToString();
                    while (name.Length < 3) name += "0";
                    var k = 2;
                    var stem = name;
                    while (UsernameExists(name))
                        name = stem + k++;
                    break;
                }
            }

            return NormalizeUser(name);
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
                WriteFileAtomic(SessionPath, json, keepBackup: false);
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
            // The session user key is enough to know whether this is the signed-in player; re-reading and
            // re-parsing session.json + the whole account file on every save was pure overhead (QC F13).
            if (string.Equals(PlayerPrefs.GetString(PrefsSessionUser, ""), account.username, StringComparison.Ordinal))
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
                // Compact: the pretty form was 4.2x larger (237 KB vs 56 KB for a full-catalog account, QC F13).
                WriteFileAtomic(AccountPath(account.username), JsonUtility.ToJson(account, false), keepBackup: true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] WriteAccount: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Crash-safe replace (QC F3): write <c>path.tmp</c>, flush it to the device, then swap it in with
        /// File.Replace (the previous file becomes <c>path.bak</c> when <paramref name="keepBackup"/>). There is
        /// no moment where neither the old nor the new file exists under a readable name: a crash leaves either
        /// the old file, or the new one, or <c>.tmp</c>/<c>.bak</c> that <see cref="ReadAccount"/> recovers.
        /// </summary>
        static void WriteFileAtomic(string path, string text, bool keepBackup)
        {
            var tmp = path + ".tmp";
            var bytes = new UTF8Encoding(false).GetBytes(text ?? "");
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }

            var bak = keepBackup ? path + ".bak" : null;
            try
            {
                File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
            {
                // Fallback for file systems without an atomic replace. The old file is copied aside first, so
                // the delete→move gap still leaves .bak and .tmp for ReadAccount to recover from.
                File.Copy(path, bak ?? path + ".bak", overwrite: true);
                File.Delete(path);
                File.Move(tmp, path);
                if (bak == null) TryDelete(path + ".bak");
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort */ }
        }

        /// <summary>Account with migrations applied (EnsureProgress / EnsureInventory).</summary>
        static Account ReadAccount(string username)
        {
            var acc = ReadAccountRaw(username);
            if (acc != null)
            {
                acc.EnsureProgress();
                acc.EnsureInventory();
            }

            return acc;
        }

        /// <summary>
        /// Parse the account file; when it is missing or unreadable, recover from <c>.tmp</c> (a write that
        /// finished but was not swapped in) and then <c>.bak</c> (the previous save), and put the recovered
        /// save back in place.
        /// </summary>
        static Account ReadAccountRaw(string username)
        {
            username = NormalizeUser(username);
            if (string.IsNullOrEmpty(username)) return null;
            var path = AccountPath(username);
            var primary = TryParseAccountFile(path);
            if (primary != null) return primary;

            foreach (var candidate in new[] { path + ".tmp", path + ".bak" })
            {
                var acc = TryParseAccountFile(candidate);
                if (acc == null) continue;
                Debug.LogWarning($"[WRLDZ] Account '{username}' recovered from {Path.GetFileName(candidate)}.");
                try
                {
                    WriteFileAtomic(path, JsonUtility.ToJson(acc, false), keepBackup: false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] Account restore write: " + ex.Message);
                }

                return acc;
            }

            return null;
        }

        static Account TryParseAccountFile(string file)
        {
            if (!File.Exists(file)) return null;
            try
            {
                var acc = JsonUtility.FromJson<Account>(File.ReadAllText(file));
                return acc != null && !string.IsNullOrEmpty(acc.username) ? acc : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WRLDZ] ReadAccount {Path.GetFileName(file)}: " + ex.Message);
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
                WriteFileAtomic(MetaPath, JsonUtility.ToJson(_meta, true), keepBackup: false);
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
                    foreach (var f in Directory.GetFiles(AccountsDir))
                    {
                        // <name>.json, plus <name>.json.tmp / .json.bak left by an interrupted save.
                        var file = Path.GetFileName(f);
                        var cut = file.EndsWith(".json", StringComparison.Ordinal) ? file.Length - 5
                            : file.EndsWith(".json.tmp", StringComparison.Ordinal) ? file.Length - 9
                            : file.EndsWith(".json.bak", StringComparison.Ordinal) ? file.Length - 9
                            : -1;
                        if (cut <= 0) continue;
                        var name = file.Substring(0, cut);
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
                WriteFileAtomic(IndexPath, JsonUtility.ToJson(new IndexFile { usernames = arr }, true), keepBackup: false);
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
