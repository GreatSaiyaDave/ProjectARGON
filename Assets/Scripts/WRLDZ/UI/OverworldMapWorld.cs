using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// One painted city. GPS pans the same map under a fixed avatar.
    /// Bazaar pins sit on gold plaza discs — never a grid of photos.
    /// </summary>
    public static class OverworldMapWorld
    {
        public static void Build(Transform mapContent)
        {
            if (mapContent == null) return;
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_overworld_map.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_overworld_district.png");

            var live = mapContent.GetComponent<OverworldAnimeCity>()
                       ?? mapContent.gameObject.AddComponent<OverworldAnimeCity>();
            live.BuildSurface();
        }

        public static void StampLandmarks(Transform mapContent,
            System.Collections.Generic.IEnumerable<(float x, float y, MapZoneKind kind, string id)> pins)
        {
            if (mapContent == null) return;
            var live = mapContent.GetComponent<OverworldAnimeCity>();
            live?.StampLandmarks(pins);
        }

        public class OverworldAnimeCity : MonoBehaviour
        {
            Image _map;
            Transform _marks;
            MapEnvironment _env;
            (float x, float y, MapZoneKind kind, string id)[] _pins;

            void OnEnable()
            {
                _env = MapEnvironment.Ensure();
                if (_env != null)
                {
                    _env.OnEnvironmentChanged -= TintFromEnv;
                    _env.OnEnvironmentChanged += TintFromEnv;
                }

                TintFromEnv();
            }

            void OnDisable()
            {
                if (_env != null)
                    _env.OnEnvironmentChanged -= TintFromEnv;
            }

            public void BuildSurface()
            {
                // Wipe only our layers — pins may already exist if rebuilt
                Wipe("NightBase");
                Wipe("ImagineMap");
                Wipe("XmHaze");
                Wipe("GoldHaze");
                Wipe("VignetteN");
                Wipe("VignetteS");
                Wipe("Landmarks");

                Fill(transform, "NightBase", 0f, 0f, 1f, 1f, new Color(0.06f, 0.08f, 0.14f, 1f));

                var art = ImagineAssets.BgOverworldMap();
                var mapGo = new GameObject("ImagineMap", typeof(RectTransform), typeof(Image));
                mapGo.transform.SetParent(transform, false);
                var rt = mapGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _map = mapGo.GetComponent<Image>();
                _map.sprite = art ?? UiFoundation.WhiteSprite();
                _map.type = Image.Type.Simple;
                _map.preserveAspect = false;
                _map.raycastTarget = false;
                _map.color = Color.white;

                // Quiet atmosphere — do not draw a second city on top
                Fill(transform, "XmHaze", 0f, 0f, 1f, 1f, new Color(0.20f, 0.70f, 0.90f, 0.04f));
                Fill(transform, "VignetteN", 0f, 0.93f, 1f, 1f, new Color(0.02f, 0.03f, 0.08f, 0.30f));
                Fill(transform, "VignetteS", 0f, 0f, 1f, 0.07f, new Color(0.02f, 0.03f, 0.08f, 0.34f));

                TintFromEnv();
                if (_pins != null)
                    StampLandmarks(_pins);
            }

            public void StampLandmarks(
                System.Collections.Generic.IEnumerable<(float x, float y, MapZoneKind kind, string id)> pins)
            {
                if (pins != null)
                {
                    var list = new System.Collections.Generic.List<(float x, float y, MapZoneKind kind, string id)>();
                    foreach (var p in pins) list.Add(p);
                    _pins = list.ToArray();
                }

                if (_marks != null)
                    Destroy(_marks.gameObject);
                _marks = new GameObject("Landmarks", typeof(RectTransform)).transform;
                _marks.SetParent(transform, false);
                var tr = _marks.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = Vector2.zero;
                tr.offsetMax = Vector2.zero;
                // Sit above the painting, below pins (pins are added after this in OverworldUI if we stamp first)
                var map = transform.Find("ImagineMap");
                if (map != null)
                    _marks.SetSiblingIndex(map.GetSiblingIndex() + 1);

                if (_pins == null) return;
                var ring = ImagineAssets.LevelRing() ?? ImagineAssets.HudChip();
                foreach (var p in _pins)
                {
                    if (p.kind != MapZoneKind.Bazaar) continue;
                    const float s = 0.05f;
                    var go = new GameObject("BazaarPlaza_" + p.id, typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(_marks, false);
                    var prt = go.GetComponent<RectTransform>();
                    prt.anchorMin = new Vector2(p.x - s, p.y - s);
                    prt.anchorMax = new Vector2(p.x + s, p.y + s);
                    prt.offsetMin = Vector2.zero;
                    prt.offsetMax = Vector2.zero;
                    var img = go.GetComponent<Image>();
                    img.sprite = ring ?? UiFoundation.WhiteSprite();
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.color = new Color(1f, 0.82f, 0.30f, 0.35f);
                }
            }

            void TintFromEnv()
            {
                if (_map == null) return;
                _env ??= MapEnvironment.Ensure();
                if (_env == null)
                {
                    _map.color = Color.white;
                    return;
                }

                var day = Mathf.Clamp01(_env.Daylight01);
                // Night: cooler and a touch darker. Day: warm paper. Never a second overlay city.
                var night = new Color(0.62f, 0.70f, 0.92f, 1f);
                var noon = new Color(1.05f, 1.00f, 0.92f, 1f);
                _map.color = Color.Lerp(night, noon, day);
            }

            void Wipe(string name)
            {
                var t = transform.Find(name);
                if (t != null)
                    Destroy(t.gameObject);
            }
        }

        static void Fill(Transform parent, string name, float x0, float y0, float x1, float y1, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = c;
            img.raycastTarget = false;
        }
    }
}
