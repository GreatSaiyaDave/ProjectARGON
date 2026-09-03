using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    public static class OcgReplayCompare
    {
        public static bool TryLoadGoldenIds(string streamingRelative, out List<int> ids, out string reason)
        {
            ids = new List<int>();
            reason = null;
            var path = Path.Combine(Application.streamingAssetsPath, streamingRelative);
            if (!File.Exists(path))
            {
                reason = "golden missing (skip): " + streamingRelative;
                return false;
            }
            var json = File.ReadAllText(path);
            var file = JsonUtility.FromJson<GoldenFile>(json);
            if (file?.ids == null || file.ids.Length == 0)
            {
                reason = "golden empty";
                return false;
            }
            ids.AddRange(file.ids);
            return true;
        }

        public static string FirstDivergence(IList<int> got, IList<int> golden)
        {
            var n = Mathf.Min(got.Count, golden.Count);
            for (var i = 0; i < n; i++)
            {
                if (got[i] != golden[i])
                    return $"index {i}: got {got[i]} golden {golden[i]}";
            }
            if (got.Count != golden.Count)
                return $"length got {got.Count} golden {golden.Count}";
            return null;
        }

        public static List<int> CollectIds(IEnumerable<OcgMessage> msgs)
        {
            var list = new List<int>();
            if (msgs == null) return list;
            foreach (var m in msgs)
                if (m != null) list.Add(m.MsgId);
            return list;
        }

        [System.Serializable]
        class GoldenFile
        {
            public int[] ids;
        }
    }
}
