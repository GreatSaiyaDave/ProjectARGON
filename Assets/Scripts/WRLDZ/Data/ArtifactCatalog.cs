using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Data
{
    /// <summary>Loads <c>artifacts.json</c>. DuelEngine never reads this file.</summary>
    public static class ArtifactCatalog
    {
        public const string RelativePath = "WRLDZ/Artifacts/artifacts.json";

        static ArtifactCatalogFile _cached;
        static Dictionary<string, ArtifactDef> _byId;

        public static void Invalidate()
        {
            _cached = null;
            _byId = null;
        }

        public static ArtifactCatalogFile Load()
        {
            if (_cached != null) return _cached;
            if (!StreamingFiles.TryReadAllText(RelativePath, out var json))
            {
                Debug.LogWarning("[WRLDZ Artifacts] Missing " + StreamingFiles.FullPathForLog(RelativePath));
                _cached = new ArtifactCatalogFile { defs = Array.Empty<ArtifactDef>() };
                Index();
                return _cached;
            }

            try
            {
                _cached = JsonUtility.FromJson<ArtifactCatalogFile>(json)
                          ?? new ArtifactCatalogFile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Artifacts] Parse failed: " + ex.Message);
                _cached = new ArtifactCatalogFile { defs = Array.Empty<ArtifactDef>() };
            }

            _cached.defs ??= Array.Empty<ArtifactDef>();
            Index();
            return _cached;
        }

        public static ArtifactDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Load();
            return _byId != null && _byId.TryGetValue(id, out var def) ? def : null;
        }

        public static IReadOnlyList<ArtifactDef> All()
        {
            var file = Load();
            return file.defs ?? Array.Empty<ArtifactDef>();
        }

        public static bool IdsUnique(out string duplicate)
        {
            duplicate = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in All())
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                if (!seen.Add(d.id))
                {
                    duplicate = d.id;
                    return false;
                }
            }

            return true;
        }

        static void Index()
        {
            _byId = new Dictionary<string, ArtifactDef>(StringComparer.OrdinalIgnoreCase);
            if (_cached?.defs == null) return;
            foreach (var d in _cached.defs)
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                _byId[d.id] = d;
            }
        }
    }
}
