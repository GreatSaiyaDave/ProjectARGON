using System;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The sky-lid apparition (BUILD_PLAN §6 P5 step 5, story only per CRACKS §3; anime_v61_eye.js:206-250): his eye through a
    /// torn hole in Nut's sky, wholly inside her band, at the page's place (inside Nut's clip, after her fracture). The hole
    /// is a 12-frame sequence (step = clamp(round(12 eOut(lid)), 1, 12); dread_&lt;layout&gt;.json skylid): under (soot + torn
    /// flaps) → the Shadow Realm through the hole (the crack gaps' RealmTile route, drifting 4 stage px / s on the crack
    /// clock) → the band → his eye (skylid_23, his pixels, straight out: shut, the ep.1 open at 1.3 s, closed again from
    /// 2.45 s; positioned by the page's track) → over (ink rim + lit sliver). The grade skips the hole (DreadLadder).
    /// </summary>
    public sealed class SkyLid
    {
        DreadData _d;
        RawImage _under, _band, _over, _realm, _holeR, _holeE;
        QuadLayer _eye;
        RigTex _eyeTex;
        int _shown = -1;
        readonly CrackBeat[] _stut = CrackTime.Schedule(912, 21.5, 43, 71, 200);
        GameObject _root;

        public static SkyLid Build(RectTransform host, Rect frame, DreadData d, RigContext ctx)
        {
            if (host == null || d == null || d.Sky.Count == 0 || d.SkyRect.width <= 0) return null;
            var s = new SkyLid { _d = d };
            var root = new GameObject("SkyLid", typeof(RectTransform));
            root.transform.SetParent(host, false);
            UiTheme.Stretch(root.GetComponent<RectTransform>());
            s._root = root;
            s._under = Part(root.transform, "SkyLid_under", d, "under", frame);
            s._holeR = Part(root.transform, "SkyLid_holeRealm", d, "hole", frame);
            if (s._holeR != null)
            {
                s._holeR.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var realm = MenuV6Art.Texture(CrackLayer.RealmTile);
                if (realm != null)
                {
                    var go = new GameObject("SkyLid_realm", typeof(RectTransform), typeof(RawImage));
                    go.transform.SetParent(s._holeR.transform, false);
                    s._realm = go.GetComponent<RawImage>();
                    s._realm.texture = realm;
                    s._realm.raycastTarget = false;
                    UiTheme.Stretch(s._realm.rectTransform);
                }
            }

            s._band = Part(root.transform, "SkyLid_band", d, "band", frame);
            s._holeE = Part(root.transform, "SkyLid_holeEye", d, "hole", frame);
            if (s._holeE != null && ctx != null && d.Books.TryGetValue("skylid_23", out var book))
            {
                s._holeE.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                s._eyeTex = ctx.Gfx.Sheet(book.File);
                if (s._eyeTex != null) s._eye = ctx.Gfx.Quads(s._holeE.transform, "SkyLid_eye", d.SkyRect, s._eyeTex);
            }

            s._over = Part(root.transform, "SkyLid_over", d, "over", frame);
            if (s._under == null || s._band == null || s._over == null)
            {
                DestroyNow(root);
                return null;
            }

            root.SetActive(false);
            return s;
        }

        static RawImage Part(Transform parent, string name, DreadData d, string key, Rect frame)
        {
            if (!d.SkyParts.TryGetValue(key, out var p) || string.IsNullOrEmpty(p.file)) return null;
            var tex = MenuV6Art.Texture(p.file);
            if (tex == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            MenuV6Layout.Place(img.rectTransform, d.SkyRect, frame);
            return img;
        }

        static void DestroyNow(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        void Cell(RawImage img, string key, int i)
        {
            if (img == null || !_d.SkyParts.TryGetValue(key, out var p) || i < 0 || i >= p.pos.Count) return;
            var rows = Math.Max(1, (p.pos.Count + Math.Max(1, p.cols) - 1) / Math.Max(1, p.cols));
            var tw = (int)Math.Round(p.cols * p.pw);
            var th = (int)Math.Round(rows * p.ph);
            img.uvRect = QuadList.Cell(tw, th, p.pos[i].x, p.pos[i].y, _d.SkyRect.width, _d.SkyRect.height);
        }

        /// <summary>Draw the apparition for the state (nothing while the lid is shut, <paramref name="off"/>: the idle,
        /// Battery Saver); returns its quads.</summary>
        public int Apply(double t, in DreadState S, bool off)
        {
            var step = off ? 0 : DreadMath.SkyLidStep(S, out _);
            var on = step > 0;
            if (_root.activeSelf != on) _root.SetActive(on);
            if (!on)
            {
                _shown = -1;
                return 0;
            }

            if (step != _shown)
            {
                _shown = step;
                Cell(_under, "under", step - 1);
                Cell(_band, "band", step - 1);
                Cell(_over, "over", step - 1);
                Cell(_holeR, "hole", step - 1);
                Cell(_holeE, "hole", step - 1);
            }

            // the realm drifts on the crack clock (CrackLayer.RealmDrift), UV = stage px / the tile
            if (_realm != null)
            {
                var tc = CrackTime.StutterTime(_stut, t, out _, out _);
                var tile = CrackLayer.RealmTileSize;
                var r = _d.SkyRect;
                var du = tc * CrackLayer.RealmDrift / tile;
                _realm.uvRect = new Rect((float)(r.x / tile + du), (float)(1 - (r.y + r.height) / tile), (float)(r.width / tile), (float)(r.height / tile));
            }

            // his eye: shut, the ep.1 open at apparU 1.3, then closed from 2.45 (EYE61.blinkAt(aU, 2.45, 99))
            if (_eye != null && _d.Books.TryGetValue("skylid_23", out var book))
            {
                var aU = S.ApparU;
                var st = aU < 1.3 ? EyeMath.Shut : aU < 2.45 ? EyeMath.Open(aU - 1.3) : EyeMath.BlinkAt(aU, 2.45, 99);
                var p = _d.SkyEyeAt(aU);
                var fi = book.Nearest(st, out _);
                var f = book.Frames[fi];
                var s = _d.SkyPx / Math.Max(1e-6, book.Px);
                var uv = QuadList.Cell((int)(book.SheetW > 0 ? book.SheetW : _eyeTex.W), (int)(book.SheetH > 0 ? book.SheetH : _eyeTex.H), f.x, f.y,
                    book.CellW, book.CellH);
                _eye.Clear();
                _eye.Pivoted(p.x, p.y, -book.AnchorX * s, -book.AnchorY * s, book.CellW * s, book.CellH * s, uv, Color.white, 0);
                _eye.Commit();
            }

            return _realm != null ? 5 : 4;
        }
    }
}
