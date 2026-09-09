using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using WRLDZ.Core;

namespace WRLDZ.UI
{
    /// <summary>
    /// OSM raster layer under the stylized Imagine city. GPS pan stays on MapEnvironment
    /// meters. This is the map BASE — haze/pins stay on top. Not a satellite photo.
    /// </summary>
    public class OverworldOsmTiles : MonoBehaviour
    {
        const int Zoom = 16;
        const int Radius = 1;
        RawImage[,] _tiles;
        int _cx = int.MinValue, _cy = int.MinValue;

        public static OverworldOsmTiles Ensure(Transform mapContent)
        {
            if (mapContent == null) return null;
            var existing = mapContent.GetComponent<OverworldOsmTiles>();
            if (existing != null) return existing;
            return mapContent.gameObject.AddComponent<OverworldOsmTiles>();
        }

        public void BuildUnderMap(Transform mapContent)
        {
            if (mapContent == null) return;
            var old = mapContent.Find("OsmTiles");
            if (old != null) Destroy(old.gameObject);
            var root = new GameObject("OsmTiles", typeof(RectTransform));
            root.transform.SetParent(mapContent, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var imagine = mapContent.Find("ImagineMap");
            if (imagine != null)
                root.transform.SetSiblingIndex(imagine.GetSiblingIndex());

            var n = Radius * 2 + 1;
            _tiles = new RawImage[n, n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var go = new GameObject($"t{x}_{y}", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(root.transform, false);
                var irt = go.GetComponent<RectTransform>();
                var x0 = x / (float)n;
                var y0 = 1f - (y + 1) / (float)n;
                irt.anchorMin = new Vector2(x0, y0);
                irt.anchorMax = new Vector2((x + 1) / (float)n, 1f - y / (float)n);
                irt.offsetMin = Vector2.zero;
                irt.offsetMax = Vector2.zero;
                var img = go.GetComponent<RawImage>();
                img.color = new Color(1f, 1f, 1f, 0.55f);
                img.raycastTarget = false;
                _tiles[x, y] = img;
            }

            if (imagine != null)
            {
                var overlay = imagine.GetComponent<Image>();
                if (overlay != null)
                {
                    var c = overlay.color;
                    c.a = 0.42f;
                    overlay.color = c;
                }
            }

            RefreshFromGps();
        }

        public void RefreshFromGps()
        {
            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env == null) return;
            var lat = env.Latitude;
            var lon = env.Longitude;
            LatLonToTile(lat, lon, Zoom, out var tx, out var ty);
            if (tx == _cx && ty == _cy) return;
            _cx = tx;
            _cy = ty;
            StopAllCoroutines();
            StartCoroutine(LoadAround(tx, ty));
        }

        IEnumerator LoadAround(int cx, int cy)
        {
            var n = Radius * 2 + 1;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var tileX = cx + (x - Radius);
                var tileY = cy + (y - Radius);
                var url = $"https://tile.openstreetmap.org/{Zoom}/{tileX}/{tileY}.png";
                using var req = UnityWebRequestTexture.GetTexture(url);
                req.SetRequestHeader("User-Agent", "DuelMonstersWRLDZ/1.0 (overworld; personal)");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) continue;
                var tex = DownloadHandlerTexture.GetContent(req);
                if (tex != null && _tiles != null && _tiles[x, y] != null)
                    _tiles[x, y].texture = tex;
            }
        }

        static void LatLonToTile(double lat, double lon, int z, out int x, out int y)
        {
            var n = System.Math.Pow(2, z);
            x = (int)System.Math.Floor((lon + 180.0) / 360.0 * n);
            var latRad = lat * System.Math.PI / 180.0;
            y = (int)System.Math.Floor((1.0 - System.Math.Log(System.Math.Tan(latRad) +
                1.0 / System.Math.Cos(latRad)) / System.Math.PI) / 2.0 * n);
        }

        void OnEnable()
        {
            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env == null) return;
            env.OnLocationMoved -= RefreshFromGps;
            env.OnLocationMoved += RefreshFromGps;
        }

        void OnDisable()
        {
            if (MapEnvironment.Instance != null)
                MapEnvironment.Instance.OnLocationMoved -= RefreshFromGps;
        }
    }
}
