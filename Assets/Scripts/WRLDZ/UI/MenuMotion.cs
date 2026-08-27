using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI
{
    /// <summary>
    /// Shared unscaled-time motion for overworld menus: fade, rise, pop, stagger.
    /// </summary>
    public static class MenuMotion
    {
        public const float Snap = 0.16f;
        public const float Sheet = 0.26f;
        public const float Pop = 0.20f;
        public const float Glass = 0.24f;
        public const float Eye = 0.36f;

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            var u = 1f - t;
            return 1f - u * u * u;
        }

        public static float InCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float s = 1.575f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        public static CanvasGroup EnsureGroup(GameObject go)
        {
            if (go == null) return null;
            var g = go.GetComponent<CanvasGroup>();
            return g != null ? g : go.AddComponent<CanvasGroup>();
        }

        public static void Play(MonoBehaviour host, IEnumerator co)
        {
            if (host != null && host.isActiveAndEnabled && co != null)
                host.StartCoroutine(co);
        }

        /// <summary>Run motion on a GameObject that has no existing behaviour (adds a driver).</summary>
        public static void PlayOn(GameObject go, IEnumerator co)
        {
            if (go == null || co == null) return;
            var host = go.GetComponent<MenuMotionDriver>();
            if (host == null) host = go.AddComponent<MenuMotionDriver>();
            if (host.isActiveAndEnabled)
                host.StartCoroutine(co);
        }

        public static IEnumerator PunchScale(RectTransform rt, float peak = 1.14f, float dur = Pop)
        {
            if (rt == null) yield break;
            var from = rt.localScale;
            if (from.sqrMagnitude < 0.01f) from = Vector3.one;
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var u = t / dur;
                float s;
                if (u < 0.45f)
                    s = Mathf.Lerp(1f, peak, OutCubic(u / 0.45f));
                else
                    s = Mathf.Lerp(peak, 1f, OutCubic((u - 0.45f) / 0.55f));
                rt.localScale = new Vector3(from.x * s, from.y * s, 1f);
                yield return null;
            }

            rt.localScale = from;
        }

        public static IEnumerator FadeGraphic(Graphic g, Color from, Color to, float dur)
        {
            if (g == null) yield break;
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            g.color = from;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                g.color = Color.Lerp(from, to, OutCubic(t / dur));
                yield return null;
            }

            g.color = to;
        }

        public static IEnumerator Fade(CanvasGroup g, float from, float to, float dur)
        {
            if (g == null) yield break;
            g.alpha = from;
            var blocks = to > 0.05f;
            g.blocksRaycasts = blocks || from > 0.05f;
            g.interactable = g.blocksRaycasts;
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, OutCubic(t / dur));
                yield return null;
            }

            g.alpha = to;
            g.blocksRaycasts = to > 0.05f;
            g.interactable = to > 0.05f;
        }

        public static IEnumerator SheetIn(GameObject go, float dur = Sheet)
        {
            if (go == null) yield break;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            var g = EnsureGroup(go);
            g.alpha = 0f;
            g.blocksRaycasts = false;
            var origin = rt != null ? rt.anchoredPosition : Vector2.zero;
            if (rt != null) rt.anchoredPosition = origin + new Vector2(0f, -42f);
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = OutCubic(t / dur);
                g.alpha = k;
                if (rt != null)
                    rt.anchoredPosition = Vector2.Lerp(origin + new Vector2(0f, -42f), origin, k);
                yield return null;
            }

            g.alpha = 1f;
            if (rt != null) rt.anchoredPosition = origin;
            g.blocksRaycasts = true;
            g.interactable = true;
        }

        public static IEnumerator SheetOut(GameObject go, float dur = Snap)
        {
            if (go == null || !go.activeSelf) yield break;
            var rt = go.GetComponent<RectTransform>();
            var g = EnsureGroup(go);
            var origin = rt != null ? rt.anchoredPosition : Vector2.zero;
            var fromA = g.alpha;
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            g.interactable = false;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = OutCubic(t / dur);
                g.alpha = Mathf.Lerp(fromA, 0f, k);
                if (rt != null)
                    rt.anchoredPosition = Vector2.Lerp(origin, origin + new Vector2(0f, -28f), k);
                yield return null;
            }

            g.alpha = 0f;
            g.blocksRaycasts = false;
            if (rt != null) rt.anchoredPosition = origin;
            go.SetActive(false);
        }

        public static IEnumerator PopIn(GameObject go, float dur = Pop)
        {
            if (go == null) yield break;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            var g = EnsureGroup(go);
            g.alpha = 0f;
            if (rt != null) rt.localScale = new Vector3(0.86f, 0.86f, 1f);
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            while (t < dur)
            {
                if (go == null || g == null) yield break;
                t += Time.unscaledDeltaTime;
                var u = t / dur;
                g.alpha = OutCubic(u);
                if (rt != null)
                {
                    var s = Mathf.Lerp(0.86f, 1f, OutBack(u));
                    rt.localScale = new Vector3(s, s, 1f);
                }

                yield return null;
            }

            if (go == null || g == null) yield break;
            g.alpha = 1f;
            if (rt != null) rt.localScale = Vector3.one;
            g.blocksRaycasts = true;
            g.interactable = true;
        }

        public static IEnumerator StaggerChildren(Transform parent, float dur = 0.22f, float delay = 0.028f)
        {
            if (parent == null) yield break;
            var n = parent.childCount;
            if (n <= 0) yield break;
            var cgs = new CanvasGroup[n];
            var rts = new RectTransform[n];
            for (var i = 0; i < n; i++)
            {
                var ch = parent.GetChild(i).gameObject;
                rts[i] = ch.GetComponent<RectTransform>();
                cgs[i] = EnsureGroup(ch);
                cgs[i].alpha = 0f;
                if (rts[i] != null)
                    rts[i].localScale = new Vector3(0.82f, 0.82f, 1f);
            }

            var total = dur + delay * Mathf.Max(0, n - 1);
            var t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                for (var i = 0; i < n; i++)
                {
                    var u = Mathf.Clamp01((t - i * delay) / dur);
                    if (cgs[i] != null) cgs[i].alpha = OutCubic(u);
                    if (rts[i] != null)
                    {
                        var s = Mathf.Lerp(0.82f, 1f, OutBack(u));
                        rts[i].localScale = new Vector3(s, s, 1f);
                    }
                }

                yield return null;
            }

            for (var i = 0; i < n; i++)
            {
                if (cgs[i] != null)
                {
                    cgs[i].alpha = 1f;
                    cgs[i].blocksRaycasts = true;
                    cgs[i].interactable = true;
                }

                if (rts[i] != null) rts[i].localScale = Vector3.one;
            }
        }

        public static IEnumerator LerpAnchorY1(RectTransform rt, float x0, float y0, float x1,
            float fromY1, float toY1, float dur)
        {
            if (rt == null) yield break;
            dur = Mathf.Max(0.01f, dur);
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var y = Mathf.Lerp(fromY1, toY1, OutCubic(t / dur));
                GoTheme.Place(rt, x0, y0, x1, y);
                yield return null;
            }

            GoTheme.Place(rt, x0, y0, x1, toY1);
        }
    }
}
