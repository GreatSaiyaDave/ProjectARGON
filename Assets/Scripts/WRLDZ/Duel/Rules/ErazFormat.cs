using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    [Serializable]
    public class ErazBand
    {
        public string id;
        public string name;
        public int order;
        public string lastCoreCode;
        public string lastCoreDate;
        public string nextFirstSet;
        public string banlistId;
        public string extraKinds;
    }

    [Serializable]
    public class ErazFile
    {
        public int version;
        public ErazBand[] bands;
    }

    /// <summary>
    /// ERAZ TCG snapshot bands: tray rules, Original pool wall (pre-TLM), band order.
    /// </summary>
    public static class ErazFormat
    {
        public const string Original = "original";
        public const string Gx = "gx";
        public const string FiveDs = "5ds";
        public const string Zexal = "zexal";
        public const string Arcv = "arcv";
        public const string Vrains = "vrains";
        public const string Modern = "modern";

        static readonly HashSet<string> NoTrayFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ddm", "genesys", "raid", "speed", "deckmaster"
        };

        static ErazFile _cached;
        static List<string> _bandIdsInOrder;
        static int? _tlmOrder;
        static bool _tlmOrderResolved;
        static bool? _originalReleased;
        static HashSet<int> _originalPoolIds;
        static HashSet<int> _postOriginalPoolIds;

        public static ErazBand Band(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var file = Load();
            if (file?.bands == null) return null;
            for (var i = 0; i < file.bands.Length; i++)
            {
                var b = file.bands[i];
                if (b != null && string.Equals(b.id, id, StringComparison.OrdinalIgnoreCase))
                    return b;
            }
            return null;
        }

        public static IReadOnlyList<string> BandIdsInOrder()
        {
            EnsureBandIds();
            return _bandIdsInOrder;
        }

        public static bool ShowsBadgeTray(string formatId)
        {
            if (string.IsNullOrEmpty(formatId)) return true;
            return !NoTrayFormats.Contains(formatId);
        }

        public static bool InOriginalSetList(string setCode)
        {
            if (string.IsNullOrEmpty(setCode)) return false;
            if (string.Equals(setCode, "TLM", StringComparison.OrdinalIgnoreCase))
                return false;

            var sets = CardEraCurriculum.Load()?.sets;
            if (sets == null || sets.Length == 0) return false;

            var tlmOrder = ResolveTlmOrder(sets);
            CardEraCurriculum.SetDef match = null;
            for (var i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                if (s != null && string.Equals(s.code, setCode, StringComparison.OrdinalIgnoreCase))
                {
                    match = s;
                    break;
                }
            }
            if (match == null) return false;
            if (!tlmOrder.HasValue) return false;
            return match.order < tlmOrder.Value;
        }

        public static bool InPoolBySetCode(string setCode, string eraId)
        {
            if (string.IsNullOrEmpty(setCode) || string.IsNullOrEmpty(eraId)) return false;
            if (string.Equals(eraId, Original, StringComparison.OrdinalIgnoreCase))
                return InOriginalSetList(setCode);
            return false;
        }

        /// <summary>
        /// Passcode is legal for this band's constructed pool.
        /// Original = pre-TLM <c>pre_link_sets</c> priority passcodes.
        /// </summary>
        public static bool InPool(int passcode, string eraId)
        {
            if (passcode <= 0 || string.IsNullOrEmpty(eraId)) return false;
            if (string.Equals(eraId, Original, StringComparison.OrdinalIgnoreCase))
                return OriginalPoolSet().Contains(passcode);
            return false;
        }

        /// <summary>Passcode is a TLM-or-later curriculum priority id (illegal in Original).</summary>
        public static bool IsLaterThanOriginal(int passcode) =>
            passcode > 0 && PostOriginalPoolSet().Contains(passcode);

        /// <summary>
        /// Band is released when every known set-list passcode for that band is
        /// Structural or Implemented. Missing defs do not count as compiled.
        /// Lab/starter coverage alone does not release Original.
        /// </summary>
        public static bool IsReleased(string eraId)
        {
            if (string.IsNullOrEmpty(eraId)) return false;
            if (!string.Equals(eraId, Original, StringComparison.OrdinalIgnoreCase))
                return false;
            if (_originalReleased.HasValue) return _originalReleased.Value;

            var db = CardDatabase.Instance ?? CardDatabase.Load();
            if (db == null)
                return false;

            var any = false;
            foreach (var id in OriginalPoolSet())
            {
                any = true;
                var def = db.Get(id);
                if (def == null)
                {
                    _originalReleased = false;
                    return false;
                }

                var kind = CardEffectStatus.Classify(def);
                if (kind != CardEffectStatusKind.Structural
                    && kind != CardEffectStatusKind.Implemented)
                {
                    _originalReleased = false;
                    return false;
                }
            }

            _originalReleased = any;
            return any;
        }

        /// <summary>Priority passcodes from pre_link sets with order before TLM.</summary>
        public static IEnumerable<int> OriginalPoolPasscodes()
        {
            var sets = CardEraCurriculum.Load()?.sets;
            if (sets == null) yield break;
            for (var i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                if (s == null || string.IsNullOrEmpty(s.code)) continue;
                if (!InOriginalSetList(s.code)) continue;
                var ids = s.priorityPasscodes;
                if (ids == null) continue;
                for (var j = 0; j < ids.Length; j++)
                {
                    if (ids[j] > 0)
                        yield return ids[j];
                }
            }
        }

        public static string NextBand(string eraId)
        {
            if (string.IsNullOrEmpty(eraId)) return null;
            EnsureBandIds();
            for (var i = 0; i < _bandIdsInOrder.Count; i++)
            {
                if (!string.Equals(_bandIdsInOrder[i], eraId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return i + 1 < _bandIdsInOrder.Count ? _bandIdsInOrder[i + 1] : null;
            }
            return null;
        }

        static ErazFile Load()
        {
            if (_cached != null) return _cached;
            var path = Path.Combine(Application.streamingAssetsPath, "WRLDZ", "eras", "eraz_eras.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[WRLDZ ERAZ] Missing eraz_eras.json");
                _cached = new ErazFile { bands = Array.Empty<ErazBand>() };
                return _cached;
            }

            try
            {
                _cached = JsonUtility.FromJson<ErazFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ ERAZ] Parse failed: " + ex.Message);
                _cached = new ErazFile { bands = Array.Empty<ErazBand>() };
            }

            if (_cached.bands == null)
                _cached.bands = Array.Empty<ErazBand>();
            return _cached;
        }

        static void EnsureBandIds()
        {
            if (_bandIdsInOrder != null) return;
            var file = Load();
            _bandIdsInOrder = (file.bands ?? Array.Empty<ErazBand>())
                .Where(b => b != null && !string.IsNullOrEmpty(b.id))
                .OrderBy(b => b.order)
                .Select(b => b.id)
                .ToList();
        }

        static HashSet<int> OriginalPoolSet()
        {
            if (_originalPoolIds != null) return _originalPoolIds;
            _originalPoolIds = new HashSet<int>();
            foreach (var id in OriginalPoolPasscodes())
                _originalPoolIds.Add(id);
            return _originalPoolIds;
        }

        static HashSet<int> PostOriginalPoolSet()
        {
            if (_postOriginalPoolIds != null) return _postOriginalPoolIds;
            _postOriginalPoolIds = new HashSet<int>();
            var sets = CardEraCurriculum.Load()?.sets;
            if (sets == null) return _postOriginalPoolIds;
            var tlmOrder = ResolveTlmOrder(sets);
            if (!tlmOrder.HasValue) return _postOriginalPoolIds;
            for (var i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                if (s == null || s.order < tlmOrder.Value) continue;
                var ids = s.priorityPasscodes;
                if (ids == null) continue;
                for (var j = 0; j < ids.Length; j++)
                {
                    if (ids[j] > 0)
                        _postOriginalPoolIds.Add(ids[j]);
                }
            }
            return _postOriginalPoolIds;
        }

        static int? ResolveTlmOrder(CardEraCurriculum.SetDef[] sets)
        {
            if (_tlmOrderResolved) return _tlmOrder;
            _tlmOrderResolved = true;
            for (var i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                if (s != null && string.Equals(s.code, "TLM", StringComparison.OrdinalIgnoreCase))
                {
                    _tlmOrder = s.order;
                    return _tlmOrder;
                }
            }
            _tlmOrder = null;
            return null;
        }
    }
}
