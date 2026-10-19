using System;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// BUILD_PLAN D19: a DualMenuPresenter sheet still deactivates synchronously when it closes (MenuSmokeTest's sync-close
    /// checks, the stacking of BAG ↔ ARTIFACTS), so its close is played by a content-free copy of its frame: the false door's
    /// parts (no rows, no text, no fractures) and its dim, on their own canvas at the same place and sort order, rolled up and
    /// folded by the page's close curves (sheet time: the body .12-.8, the lintel .74-.98 = ~0.73 s), the clasp's jewel
    /// bursting in its lock flash, the dim easing out (1.16-2.06 sheet time), then gone. It takes no input. Play mode only (an
    /// edit-mode close is instant and leaves nothing in the scene).
    /// </summary>
    public sealed class FrameGhost : MonoBehaviour
    {
        public const string GhostName = "FrameGhost";

        FalseDoorFrameGeometry _g;
        RectMask2D _mask;
        Image _dim;
        float _dimA;
        PuzzleLock _jewel;
        double _t1;

        struct FalseDoorFrameGeometry
        {
            public Rect F;
            public float Lintel, K;
        }

        /// <summary>Copy <paramref name="frame"/> (and its dim) and play its close. Null in edit mode or when nothing can be
        /// copied; the caller deactivates the real frame either way.</summary>
        public static FrameGhost Play(FalseDoorFrame frame, Image dim)
        {
            if (!Application.isPlaying || frame == null || frame.Door == null) return null;
            try
            {
                var srcCanvas = frame.GetComponentInParent<Canvas>();
                if (srcCanvas == null) return null;
                srcCanvas = srcCanvas.rootCanvas;
                var go = new GameObject(GhostName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                var cv = go.GetComponent<Canvas>();
                cv.renderMode = RenderMode.ScreenSpaceOverlay;
                cv.sortingOrder = srcCanvas.sortingOrder;
                var srcScaler = srcCanvas.GetComponent<CanvasScaler>();
                var sc = go.GetComponent<CanvasScaler>();
                if (srcScaler != null)
                {
                    sc.uiScaleMode = srcScaler.uiScaleMode;
                    sc.referenceResolution = srcScaler.referenceResolution;
                    sc.screenMatchMode = srcScaler.screenMatchMode;
                    sc.matchWidthOrHeight = srcScaler.matchWidthOrHeight;
                    sc.referencePixelsPerUnit = srcScaler.referencePixelsPerUnit;
                    sc.scaleFactor = srcScaler.scaleFactor;
                }

                var ghost = go.AddComponent<FrameGhost>();
                var croot = (RectTransform)go.transform;
                if (dim != null)
                {
                    var d = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    d.transform.SetParent(croot, false);
                    UiTheme.Stretch(d.rectTransform);
                    d.sprite = dim.sprite;
                    d.color = dim.color;
                    d.raycastTarget = false;
                    ghost._dim = d;
                    ghost._dimA = dim.color.a;
                }

                // the frame's root at its screen place (both overlay canvases: world space is screen space)
                var src = frame.Body;
                var corners = new Vector3[4];
                src.GetWorldCorners(corners);
                Canvas.ForceUpdateCanvases();
                var a = croot.InverseTransformPoint(corners[0]);
                var b = croot.InverseTransformPoint(corners[2]);
                var root = new GameObject("Frame", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
                root.SetParent(croot, false);
                root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
                root.pivot = new Vector2(.5f, .5f);
                root.sizeDelta = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                root.anchoredPosition = new Vector2((a.x + b.x) * .5f, (a.y + b.y) * .5f);
                ghost._mask = root.GetComponent<RectMask2D>();
                var door = Instantiate(frame.Door.gameObject, root, false);
                door.name = "Door";
                UiTheme.Stretch((RectTransform)door.transform);
                var cracks = door.transform.Find("Fractures");
                if (cracks != null) Destroy(cracks.gameObject);
                var stageW = frame.F.width + 2 * FalseDoorFrame.Margin;
                ghost._g = new FalseDoorFrameGeometry
                {
                    F = frame.F, Lintel = frame.Lintel, K = stageW > 0 ? root.sizeDelta.x / stageW : 1f
                };
                // the clasp's jewel, on top and unclipped
                var space = new Rect(frame.F.x - FalseDoorFrame.Margin, frame.F.y - FalseDoorFrame.Margin, stageW,
                    frame.F.height + 2 * FalseDoorFrame.Margin);
                var jh = new GameObject("Clasp", typeof(RectTransform)).GetComponent<RectTransform>();
                jh.SetParent(croot, false);
                jh.anchorMin = jh.anchorMax = root.anchorMin;
                jh.pivot = root.pivot;
                jh.sizeDelta = root.sizeDelta;
                jh.anchoredPosition = root.anchoredPosition;
                ghost._jewel = PuzzleLock.Build(new UguiGfx(), jh, space, frame.Layout, null);
                ghost._t1 = MenuV6Clock.Now;
                ghost.Tick();
                return ghost;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] frame ghost skipped: " + ex.Message);
                return null;
            }
        }

        void LateUpdate() => Tick();

        void Tick()
        {
            var v = Math.Max(0, MenuV6Clock.Now - _t1);
            var vs = v * SheetMath.SheetSp;
            var r = SheetMath.RevealAt(SheetKind.Generic, 10, v);
            var c = SheetMath.ClipBox(_g.F.x, _g.F.y, _g.F.width, _g.F.height, _g.Lintel, r);
            var m = FalseDoorFrame.Margin;
            if (_mask != null)
                _mask.padding = new Vector4(
                    (float)Math.Max(0, (c.X0 - (_g.F.x - m)) * _g.K), (float)Math.Max(0, (_g.F.yMax + m - c.Y1) * _g.K),
                    (float)Math.Max(0, (_g.F.xMax + m - c.X1) * _g.K), 0f);
            if (_dim != null)
                SheetDraw.SetA(_dim, _dimA * (1 - MenuV6Math.InOut(MenuV6Math.Clamp((vs - 1.16) / .9, 0, 1))));
            if (_jewel != null)
                _jewel.JewelClose(new Vector2(_g.F.x + _g.F.width / 2, _g.F.y + 12), .5, Math.Max(0, vs - SheetMath.ClaspHoldSheet), MenuV6Clock.Now);
            if (vs > 2.1) Destroy(gameObject);
        }
    }
}
