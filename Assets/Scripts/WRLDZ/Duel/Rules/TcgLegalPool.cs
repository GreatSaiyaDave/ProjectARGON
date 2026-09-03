using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TCG print pool from CDB <c>datas.ot &amp; 2</c>. OCG-only / Rush / unofficial stay out
    /// of constructed. Missing file → not loaded (do not invent membership).
    /// </summary>
    public static class TcgLegalPool
    {
        [Serializable]
        class PoolFile
        {
            public string playPool;
            public int otTcgBit;
            public int count;
            public int[] ids;
            public int ocgOnlyCount;
            public int[] ocgOnly;
        }

        static bool _tried;
        static HashSet<int> _tcg;
        static HashSet<int> _ocgOnly;

        public static bool IsLoaded
        {
            get
            {
                Ensure();
                return _tcg != null && _tcg.Count > 0;
            }
        }

        public static int Count
        {
            get
            {
                Ensure();
                return _tcg != null ? _tcg.Count : 0;
            }
        }

        public static bool IsTcgPrint(int passcode)
        {
            Ensure();
            return _tcg != null && _tcg.Contains(passcode);
        }

        public static bool IsOcgOnly(int passcode)
        {
            Ensure();
            return _ocgOnly != null && _ocgOnly.Contains(passcode);
        }

        static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            var path = Path.Combine(Application.streamingAssetsPath, "OcgCore", "tcg_pool.json");
            if (!File.Exists(path)) return;
            try
            {
                var file = JsonUtility.FromJson<PoolFile>(File.ReadAllText(path));
                if (file == null) return;
                _tcg = new HashSet<int>();
                if (file.ids != null)
                    foreach (var id in file.ids)
                        _tcg.Add(id);
                _ocgOnly = new HashSet<int>();
                if (file.ocgOnly != null)
                    foreach (var id in file.ocgOnly)
                        _ocgOnly.Add(id);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] tcg_pool.json parse failed: " + ex.Message);
            }
        }
    }
}
