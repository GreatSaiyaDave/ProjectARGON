using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Duel.Ocg
{
    [Serializable]
    class OcgSpeedFile
    {
        public float defaultWindowScale = 1f;
        public OcgSpeedCard[] cards;
    }

    [Serializable]
    class OcgSpeedCard
    {
        public string name;
        public float animScale = 1f;
        public float windowScale = 1f;
        public int id;
    }

    public static class OcgSpeedTable
    {
        static Dictionary<int, float> _window;
        static float _default = 1f;

        public static void LoadFromStreaming()
        {
            _window = new Dictionary<int, float>();
            _default = 1f;
            if (!StreamingFiles.TryReadAllText("OcgCore/speed_table.json", out var json)) return;
            var file = JsonUtility.FromJson<OcgSpeedFile>(json);
            if (file == null) return;
            _default = file.defaultWindowScale <= 0f ? 1f : file.defaultWindowScale;
            if (file.cards == null) return;
            foreach (var c in file.cards)
            {
                if (c == null || c.id == 0) continue;
                _window[c.id] = c.windowScale <= 0f ? 1f : c.windowScale;
            }
        }

        public static float WindowSeconds(float baseSeconds, int cardId)
        {
            if (_window == null) LoadFromStreaming();
            var scale = _default;
            if (_window != null && _window.TryGetValue(cardId, out var s))
                scale = s;
            return Mathf.Max(0.5f, baseSeconds * scale);
        }
    }
}
