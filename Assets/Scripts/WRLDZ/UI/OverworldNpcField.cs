using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Wandering street duelists spawned around Tear pins (Pokémon GO–style).
    /// Count and 8000 LP mix rise as the player walks closer to a Tear.
    /// </summary>
    public class OverworldNpcField : MonoBehaviour
    {
        public struct Agent
        {
            public string id;
            public string tearId;
            public string displayName;
            public StreetLpBand band;
            public float spawnX, spawnY;
            public float x, y;
            public float destX, destY;
            public float distanceM;
            public RectTransform rt;
            public Image art;
        }

        static readonly string[] StreetNames =
        {
            "Corner kid", "Night-shift clerk", "Rooftop runner", "Bus-stop duelist",
            "Alley veteran", "Park regular", "Rift-touched local", "Quiet commuter",
            "Street magician", "Late walker"
        };

        readonly List<Agent> _agents = new();
        readonly List<Vector2> _tears = new();
        readonly List<string> _tearIds = new();
        readonly List<float> _radiusUv = new();
        Func<float, float, float> _distM;
        Action<Agent> _onTap;
        float _retargetAt;
        float _metersPerMapWidth = 280f;

        public IReadOnlyList<Agent> Agents => _agents;

        public static OverworldNpcField Attach(
            RectTransform mapContent,
            MapEnvironment env,
            float metersPerMapWidth,
            IEnumerable<(string id, float x, float y)> tears,
            Func<float, float, float> distMeters,
            Action<Agent> onTap)
        {
            var go = new GameObject("TearStreetNpcs", typeof(RectTransform));
            go.transform.SetParent(mapContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var field = go.AddComponent<OverworldNpcField>();
            field._distM = distMeters;
            field._onTap = onTap;
            field._metersPerMapWidth = Mathf.Max(1f, metersPerMapWidth);
            foreach (var t in tears)
            {
                field._tearIds.Add(t.id);
                field._tears.Add(new Vector2(t.x, t.y));
            }

            field.ComputeRuralRadii();
            return field;
        }

        public void Tick()
        {
            SyncPopulation();
            Wander();
        }

        void SyncPopulation()
        {
            var acc = AppSession.Ensure()?.Account;
            acc?.EnsureProgress();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var riftOk = acc?.progress != null && acc.progress.HasStreet8000Access(now);

            for (var t = 0; t < _tears.Count; t++)
            {
                var tear = _tears[t];
                var tearId = _tearIds[t];
                var d = _distM != null ? _distM(tear.x, tear.y) : 999f;
                var ruralM = RadiusUv(t) * _metersPerMapWidth;
                var want = MapZoneCatalog.StreetSpawnBudget(d, ruralM);
                var hardShare = riftOk ? MapZoneCatalog.StreetHardShare(d) : 0f;

                var have = 0;
                for (var i = 0; i < _agents.Count; i++)
                    if (_agents[i].tearId == tearId) have++;

                while (have < want)
                {
                    Spawn(tearId, tear, hardShare);
                    have++;
                }

                while (have > want)
                {
                    DespawnOne(tearId);
                    have--;
                }
            }
        }

        void Spawn(string tearId, Vector2 tear, float hardShare)
        {
            var rng = new System.Random(unchecked(tearId.GetHashCode() * 397 + _agents.Count * 17 + Time.frameCount));
            var ang = (float)(rng.NextDouble() * Mathf.PI * 2);
            var maxUv = RadiusForTear(tearId);
            var rad = maxUv * (0.22f + (float)rng.NextDouble() * 0.78f);
            var sx = tear.x + Mathf.Cos(ang) * rad;
            var sy = tear.y + Mathf.Sin(ang) * rad;
            var hard = rng.NextDouble() < hardShare;
            var band = hard ? StreetLpBand.Street8000 : StreetLpBand.Street4000;
            var name = StreetNames[rng.Next(0, StreetNames.Length)];

            var go = new GameObject("Npc_" + tearId + "_" + _agents.Count,
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            PlaceUv(rt, sx, sy);
            var img = go.GetComponent<Image>();
            img.sprite = ImagineAssets.PinNpc() ?? GoTheme.PinNpc() ?? UiFoundation.WhiteSprite();
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var agent = new Agent
            {
                id = tearId + ".npc." + _agents.Count,
                tearId = tearId,
                displayName = name,
                band = band,
                spawnX = sx, spawnY = sy,
                x = sx, y = sy,
                destX = sx, destY = sy,
                rt = rt, art = img
            };
            var id = agent.id;
            btn.onClick.AddListener(() =>
            {
                for (var i = 0; i < _agents.Count; i++)
                {
                    if (_agents[i].id != id) continue;
                    _onTap?.Invoke(_agents[i]);
                    break;
                }
            });
            _agents.Add(agent);
        }

        void DespawnOne(string tearId)
        {
            for (var i = _agents.Count - 1; i >= 0; i--)
            {
                if (_agents[i].tearId != tearId) continue;
                if (_agents[i].rt != null)
                    Destroy(_agents[i].rt.gameObject);
                _agents.RemoveAt(i);
                return;
            }
        }

        void Wander()
        {
            if (Time.unscaledTime >= _retargetAt)
            {
                _retargetAt = Time.unscaledTime + 2.4f;
                for (var i = 0; i < _agents.Count; i++)
                {
                    var a = _agents[i];
                    var ang = (a.id.GetHashCode() * 0.013f + Time.unscaledTime) % (Mathf.PI * 2f);
                    var maxUv = RadiusForTear(a.tearId);
                    var rad = maxUv * (0.12f + (i % 5) * 0.10f);
                    a.destX = a.spawnX + Mathf.Cos(ang) * rad;
                    a.destY = a.spawnY + Mathf.Sin(ang) * rad;
                    _agents[i] = a;
                }
            }

            var step = 0.012f * Time.unscaledDeltaTime;
            for (var i = 0; i < _agents.Count; i++)
            {
                var a = _agents[i];
                a.x = Mathf.MoveTowards(a.x, a.destX, step);
                a.y = Mathf.MoveTowards(a.y, a.destY, step);
                if (a.rt != null) PlaceUv(a.rt, a.x, a.y);
                if (_distM != null) a.distanceM = _distM(a.x, a.y);
                _agents[i] = a;
            }
        }

        void ComputeRuralRadii()
        {
            _radiusUv.Clear();
            for (var t = 0; t < _tears.Count; t++)
            {
                var nearby = 0;
                for (var o = 0; o < _tears.Count; o++)
                {
                    if (o == t) continue;
                    var dm = Vector2.Distance(_tears[t], _tears[o]) * _metersPerMapWidth;
                    if (dm <= MapZoneCatalog.RuralDensitySampleM) nearby++;
                }

                var radM = MapZoneCatalog.RuralNpcRadiusM(nearby);
                var uv = radM / _metersPerMapWidth;
                // Prototype map is ~280 m across — keep rural spread visible without leaving the tile.
                _radiusUv.Add(Mathf.Clamp(uv, 0.06f, 0.42f));
            }
        }

        float RadiusUv(int tearIndex)
        {
            if (tearIndex < 0 || tearIndex >= _radiusUv.Count) return 0.08f;
            return _radiusUv[tearIndex];
        }

        float RadiusForTear(string tearId)
        {
            for (var i = 0; i < _tearIds.Count; i++)
                if (_tearIds[i] == tearId) return RadiusUv(i);
            return 0.08f;
        }

        static void PlaceUv(RectTransform rt, float x, float y)
        {
            const float h = 0.028f;
            const float w = 0.022f;
            rt.anchorMin = new Vector2(x - w, y - h * 0.15f);
            rt.anchorMax = new Vector2(x + w, y + h * 1.05f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
