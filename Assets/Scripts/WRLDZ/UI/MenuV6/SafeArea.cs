using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// <c>Screen.safeArea</c> on the v6.3 screens (BUILD_PLAN §6 P5 step 2; portrait_p5.json safeArea): the safe area is
    /// converted to stage px on each layout pass and the DOM-layer bands move by the exported rule — the background stack
    /// (Nut, the room, the Stone, the cracks, the smoke) stays full-bleed and never moves.
    /// <list type="bullet">
    /// <item>portrait menu: the identity row (sky register, y &lt; 250) moves down by the top inset T (≤ 100 px of slack); the
    /// footer (events + QUIT, the placeholder strip, the Editor's home bar) moves up by s = max(0, B − 22) (the 22 px the
    /// mockup keeps under its home bar); for s &gt; 49 the NEWS strip and the plaque / WORLD MAP row move with it by s − 49
    /// (up to 49 + 99 = 148 px);</item>
    /// <item>landscape menu: the left / right pylon groups move in by the side insets;</item>
    /// <item>the deck hall: its header (y &lt; 300) down by max(0, T + 16 − 192) (the page's header already sits 192 px down),
    /// its thumb band (y &gt; 2140) up by max(0, B − 56) (decided: the grid keeps its viewport; it is only reached when an
    /// inset passes the page's own slack).</item>
    /// </list>
    /// The bands are containers (stretched over their group) holding the band's own nodes, so a band move never fights an
    /// element's own animation (the entrance's rises, the stagger). CHOOSE YOUR DUEL and the recess sheets need no inset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeArea : MonoBehaviour
    {
        /// <summary>Tests and captures: a simulated safe area in screen px (bottom-left origin); null = Screen.safeArea.</summary>
        public static Rect? Override;

        enum Side { Top, Bottom, Left, Right }

        sealed class Band
        {
            public RectTransform Node;
            public Vector2 Base;
            public Side Side;
            public Func<SafeArea, float> Shift;
        }

        readonly List<Band> _bands = new();
        RectTransform _stage;
        Rect _stageRect;
        Vector4 _applied = new(-1, -1, -1, -1);

        /// <summary>The current insets in stage px (top, bottom, left, right).</summary>
        public float T { get; private set; }

        public float B { get; private set; }
        public float L { get; private set; }
        public float R { get; private set; }

        public int Bands => _bands.Count;

        // ------------------------------------------------------------------ the menu

        /// <summary>The Stone's bands (MenuV6Stage, after the build: its entrance and stagger already hold their nodes).</summary>
        internal static SafeArea ForMenu(MenuV6Stage stage, StageParts p, MenuLayoutKind kind)
        {
            if (stage == null || stage.Stage == null || p == null) return null;
            var sa = stage.gameObject.AddComponent<SafeArea>();
            sa._stage = stage.Stage;
            sa._stageRect = stage.Layout != null ? stage.Layout.StageRect : new Rect(0, 0, 1080, 2340);
            var S = sa._stageRect;
            if (kind == MenuLayoutKind.Portrait)
            {
                foreach (var g in new[] { p.SlateL, p.Tiles, p.Ph5 })
                {
                    if (g == null) continue;
                    sa.Wrap(g, "Safe_top", Side.Top, r => r.center.y < 250f, a => Mathf.Min(a.T, 100f));
                    sa.Wrap(g, "Safe_footer", Side.Bottom, r => r.center.y >= 2160f, a => Foot(a));
                    sa.Wrap(g, "Safe_mid", Side.Bottom, r => r.center.y >= 1760f && r.center.y < 2160f, a => Mathf.Clamp(Foot(a) - 49f, 0f, 99f));
                }

                if (stage.Status != null && stage.Status.rectTransform.parent == stage.Stage)
                    sa.Add(stage.Status.rectTransform, Side.Bottom, a => Mathf.Clamp(Foot(a) - 49f, 0f, 99f));
            }
            else
            {
                if (p.Slates != null)
                {
                    sa.Wrap(p.Slates, "Safe_left", Side.Left, r => r.center.x < S.width * .5f, a => Mathf.Min(a.L, 120f));
                    sa.Wrap(p.Slates, "Safe_right", Side.Right, r => r.center.x >= S.width * .5f, a => Mathf.Min(a.R, 120f));
                }

                if (p.Tiles != null)
                {
                    sa.Wrap(p.Tiles, "Safe_left", Side.Left, r => r.center.x < 560f, a => Mathf.Min(a.L, 120f));
                    sa.Wrap(p.Tiles, "Safe_right", Side.Right, r => r.center.x > S.width - 560f, a => Mathf.Min(a.R, 120f));
                }
            }

            sa.Apply(true);
            return sa;
        }

        static float Foot(SafeArea a) => Mathf.Clamp(a.B - 22f, 0f, 148f);

        // ------------------------------------------------------------------ the deck hall

        public static SafeArea ForHall(DeckHall hall)
        {
            if (hall == null || hall.Stage == null || hall.BodyWell == null || hall.Kind != MenuLayoutKind.Portrait) return null;
            var sa = hall.gameObject.AddComponent<SafeArea>();
            sa._stage = hall.Stage;
            sa._stageRect = hall.StageRect;
            sa.Wrap(hall.BodyWell, "Safe_header", Side.Top, r => r.center.y < 300f, a => Mathf.Clamp(a.T + 16f - 192f, 0f, 120f));
            sa.Wrap(hall.BodyWell, "Safe_band", Side.Bottom, r => r.center.y > 2140f, a => Mathf.Clamp(a.B - 56f, 0f, 120f));
            sa.Apply(true);
            return sa;
        }

        // ------------------------------------------------------------------ bands

        /// <summary>Move the children of <paramref name="group"/> whose stage rect passes <paramref name="test"/> into one
        /// container stretched over the group, at the first one's place (the band's own draw order kept).</summary>
        void Wrap(RectTransform group, string name, Side side, Func<Rect, bool> test, Func<SafeArea, float> shift)
        {
            if (group == null) return;
            var gr = StageRectOf(group, _stageRect, null);
            var hits = new List<Transform>();
            for (var i = 0; i < group.childCount; i++)
            {
                var c = group.GetChild(i) as RectTransform;
                if (c == null || c.name.StartsWith("Safe_", StringComparison.Ordinal)) continue;
                var r = StageRectOf(c, gr, group);
                if (r.width <= 0 || r.height <= 0) continue;
                if (r.width >= gr.width * .9f && r.height >= gr.height * .9f) continue;   // stretched hosts stay (whole-stage layers)
                if (test(r)) hits.Add(c);
            }

            if (hits.Count == 0) return;
            var box = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(group, false);
            box.SetSiblingIndex(hits[0].GetSiblingIndex());
            UiTheme.Stretch(box);
            foreach (var h in hits) h.SetParent(box, false);
            Add(box, side, shift);
        }

        void Add(RectTransform node, Side side, Func<SafeArea, float> shift)
        {
            if (node == null) return;
            _bands.Add(new Band { Node = node, Base = node.anchoredPosition, Side = side, Shift = shift });
        }

        /// <summary>A child's rect in stage px from its anchors in a parent that covers <paramref name="parentStage"/>.</summary>
        static Rect StageRectOf(RectTransform rt, Rect parentStage, RectTransform parent)
        {
            if (parent == null) return parentStage;
            var a0 = rt.anchorMin;
            var a1 = rt.anchorMax;
            var x0 = parentStage.x + a0.x * parentStage.width;
            var x1 = parentStage.x + a1.x * parentStage.width;
            var y0 = parentStage.y + (1 - a1.y) * parentStage.height;
            var y1 = parentStage.y + (1 - a0.y) * parentStage.height;
            if (x1 - x0 < 1 || y1 - y0 < 1)
            {
                // a point-anchored child: its size around the anchor
                var sz = rt.rect.size;
                var cx = (x0 + x1) * .5f + rt.anchoredPosition.x;
                var cy = (y0 + y1) * .5f - rt.anchoredPosition.y;
                return new Rect(cx - sz.x * .5f, cy - sz.y * .5f, Mathf.Max(1, sz.x), Mathf.Max(1, sz.y));
            }

            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        // ------------------------------------------------------------------ insets

        void LateUpdate() => Apply(false);

        /// <summary>Recompute the insets (stage px) and move the bands when they changed.</summary>
        public void Apply(bool force)
        {
            Measure();
            var v = new Vector4(T, B, L, R);
            if (!force && v == _applied) return;
            _applied = v;
            foreach (var b in _bands)
            {
                if (b.Node == null) continue;
                var s = b.Shift != null ? b.Shift(this) : 0f;
                var d = b.Side switch
                {
                    Side.Top => new Vector2(0, -s),
                    Side.Bottom => new Vector2(0, s),
                    Side.Left => new Vector2(s, 0),
                    _ => new Vector2(-s, 0)
                };
                b.Node.anchoredPosition = b.Base + d;
            }
        }

        static readonly Vector3[] Corners = new Vector3[4];

        void Measure()
        {
            T = B = L = R = 0;
            if (_stage == null) return;
            var safe = Override ?? Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) return;
            _stage.GetWorldCorners(Corners);
            var cv = _stage.GetComponentInParent<Canvas>();
            var cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
            var p0 = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            var p2 = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
            var w = p2.x - p0.x;
            if (w <= 1f) return;
            var k = _stageRect.width / w;   // stage px per screen px
            T = Mathf.Max(0f, p2.y - safe.yMax) * k;
            B = Mathf.Max(0f, safe.yMin - p0.y) * k;
            L = Mathf.Max(0f, safe.xMin - p0.x) * k;
            R = Mathf.Max(0f, p2.x - safe.xMax) * k;
        }
    }

    /// <summary>
    /// SPEC §9.4: every tap ≥ 56 design px (portrait_p5.json taps: the deck's scope 48, active chip 48, CLEAR ALL 33,
    /// inspect − 38, +1 MAIN 46). A target under 56 px gets an invisible raycast pad centred on it, grown to 56 px on its
    /// short side: a child under its labels, so a tap on it reaches the target's Button and nothing it draws changes.
    /// </summary>
    public static class TapPad
    {
        public const float Min = 56f;

        public static void Ensure(RectTransform target, Rect stageRect)
        {
            if (target == null) return;
            float w = stageRect.width, h = stageRect.height;
            if (w >= Min && h >= Min) return;
            if (target.Find("TapPad") != null) return;
            var go = new GameObject("TapPad", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(target, false);
            go.transform.SetSiblingIndex(0);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(1, 1, 1, 0);
            img.raycastTarget = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(Mathf.Max(w, Min), Mathf.Max(h, Min));
        }
    }
}
