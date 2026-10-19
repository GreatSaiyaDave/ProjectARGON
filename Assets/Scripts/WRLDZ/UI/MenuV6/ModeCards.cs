using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Core;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// CSS rotateY in a perspective (wrldz.css:122-127: the card flips in a 1400 px perspective): each vertex of the card's
    /// graphics is projected about the card's centre for <see cref="AngleDeg"/>, so the near edge grows as it turns. Applied
    /// to the back and the face (each sees its own angle).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FakePerspective : BaseMeshEffect
    {
        public float AngleDeg;
        public float Distance = 1400f;
        public Vector2 Centre;   // the card centre in this graphic's local space
        static readonly List<UIVertex> Verts = new();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Abs(AngleDeg) < .01f) return;
            Verts.Clear();
            vh.GetUIVertexStream(Verts);
            var a = AngleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            for (var i = 0; i < Verts.Count; i++)
            {
                var v = Verts[i];
                var x = v.position.x - Centre.x;
                var y = v.position.y - Centre.y;
                // rotate about the vertical axis: x' = x cos, z = x sin (toward the viewer); project at Distance
                var z = x * s;
                var k = Distance / Mathf.Max(1f, Distance - z);
                v.position = new Vector3(Centre.x + x * c * k, Centre.y + y * k, v.position.z);
                Verts[i] = v;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(Verts);
        }

        public void Set(float deg, Vector2 centre)
        {
            if (Mathf.Abs(deg - AngleDeg) < .01f && Centre == centre) return;
            AngleDeg = deg;
            Centre = centre;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    /// <summary>
    /// CHOOSE YOUR DUEL (BUILD_PLAN §6 P3 step 2, SPEC §8.5; the page's own beats, menu.js:804-830, :1023-1074, anime_v61_vs.js):
    /// the DUEL door parts (the halves slide apart and jolt as the lock gives, the barrier's depth, its stars and receding
    /// rings, the warm light, the gap's glow and the rays), the radial dim with its hole at the door, the false door unrolled by
    /// the Puzzle-lock from the winged crest's jewel, CHOOSE YOUR DUEL tracking in, three Vs cards dealt from the door's light
    /// and turned (VS AI Referobot, VS PLAYER, ZONE MODE the Tear; the faces and back as displayed are local Konami frames, the
    /// tracked fallback a plain own-art face), their captions, the armed row (the equipped deck and the format's LP: real),
    /// YOU · STAY · OPP and the stand-in note. A pick lifts the card and holds the duel-begins beat (.95 s), then the sheet
    /// locks and the pick routes as today (VS AI → opponent select, VS PLAYER → PvP create, ZONE MODE → the Zone session);
    /// STAY closes. The page's timing (sp 1), not SPEC §8.5's stale table.
    /// </summary>
    public sealed class ModeCards : StoneSheet
    {
        public static readonly string[] Names = { "VS AI", "VS PLAYER", "ZONE MODE" };
        static readonly string[] Caps = { "Referobot · training", "One on one · PvP", "At a Tear · AR or Digital" };
        public const string TitleText = "CHOOSE YOUR DUEL";
        public const string SubText = "pick a duel · AR or Digital";
        public const string NoteText = "◇ stand-in · VS AI street backdrop: our drawing · ZONE MODE: the game’s own Tear art";

        Action<int> _route;
        int _pick = -1;
        double _pickAt = double.NaN;
        bool _routed;

        // geometry
        Vector2 _door;
        double _B0, _k1 = 1, _cw, _ch, _cy;
        double[] _xs = new double[3], _rot = new double[3];
        Rect _F;

        // the door's open parts (on the Stone, under the dim)
        RectTransform _doorHost, _halfTop, _halfBot, _halfTopImg, _halfBotImg;
        Image _depth, _light, _rays;
        QuadLayer _stars, _gap, _glows;
        ShapeLayer _rings;
        Rect _stoneA, _topClip, _botClip;
        Rect _lightRect, _raysRect;

        // the dim (radial at the door)
        RectTransform _dimSpace;
        Image _dimPlate;
        readonly List<Image> _dimQuads = new();

        // the contract
        RectTransform _crest;
        Rect _crestRect;
        Vector2 _crestPivot;
        Image _crestImg, _doorLight;
        Text _title, _sub, _begins;
        Rect _titleBox;
        float _titleSize = 46f;
        LetterSpacingEffect _titleLs, _beginsLs;
        float _beginsSize = 21f;
        readonly Card[] _cards = new Card[3];
        readonly Text[] _caps = new Text[3];
        CanvasGroup _armed, _sig, _note;
        Image _stayPlate;
        Sprite _stayRest, _stayHover, _stayPress;
        Rect _stayRect, _stayHoverRect, _stayPressRect;
        bool _stayOver;
        double _stayDown = double.NaN;

        sealed class Card
        {
            public RectTransform Node;
            public CanvasGroup Group;
            public Image Back, Face, Glow;
            public FakePerspective BackP, FaceP;
            public readonly List<Graphic> FaceText = new();
            public readonly List<FakePerspective> FaceTextP = new();
            public bool Over;
            public double HoverT = double.NaN, HoverFrom;
            public Button Btn;
        }

        public static ModeCards Build(MenuV6Stage stone, RectTransform host, Image dim, Action<int> route)
        {
            if (stone == null || host == null) return null;
            var s = host.gameObject.AddComponent<ModeCards>();
            if (!s.Init(stone, host, dim, SheetKind.Duel))
            {
                Destroy(s);
                return null;
            }

            s._route = route;
            s._k1 = s.Lay == MenuLayoutKind.Landscape ? 1 : 1.4;
            var geo = RJ.D(s.J, "geo");
            s._door = RJ.V(geo, "door");
            s._B0 = RJ.N(RJ.D(s.J, "door"), "B0", 104 * s._k1);
            var cf = RJ.D(geo, "cf");
            s._F = SheetData.Frame(RJ.D(cf, "frame"));
            s._cw = RJ.N(cf, "cw", 232);
            s._ch = RJ.N(cf, "ch", 338);
            s._cy = RJ.N(cf, "y", 548);
            var xs = RJ.L(cf, "xs");
            var rot = RJ.L(cf, "rot");
            for (var i = 0; i < 3; i++)
            {
                s._xs[i] = RJ.At(xs, i);
                s._rot[i] = RJ.At(rot, i);
            }

            if (dim != null)
            {
                // the flat dim stays the tap catcher; the radial plate draws the DUEL dim (its hole at the door)
                var c = dim.color;
                c.a = 0f;
                dim.color = c;
            }

            s.BuildDim();
            s.BuildDoor();
            s.Frame = FalseDoorFrame.Duel(s.Space, s.Lay);
            s.BuildContract();
            s.BuildLock(RJ.V(geo, "crestA"), RJ.N(geo, "crestS", .62));
            if (!Application.isPlaying) s.ShowAt(4.0);
            else s.Step();
            return s;
        }

        // ------------------------------------------------------------------ the dim (radial at the door)

        void BuildDim()
        {
            var fx = RJ.D(J, "fx");
            var d = RJ.D(fx, "dim");
            _dimSpace = SheetDraw.StageSpace(Host, Stone, "DimRadial");
            if (DimImg != null) _dimSpace.SetSiblingIndex(DimImg.transform.GetSiblingIndex() + 1);
            var r = RJ.R(d, "rect");
            _dimPlate = SheetDraw.Img(_dimSpace, "Plate", SheetData.Sprite(d), r, StageRect);
            const float far = 20000f;
            foreach (var q in new[]
                     {
                         new Rect(-far, -far, r.x + far, 2 * far), new Rect(r.xMax, -far, far, 2 * far),
                         new Rect(r.x, -far, r.width, r.y + far), new Rect(r.x, r.yMax, r.width, far)
                     })
            {
                var img = SheetDraw.Img(_dimSpace, "Flat", UiFoundation.WhiteSprite(), q, StageRect);
                if (img != null) _dimQuads.Add(img);
            }
        }

        void DrawDim(double u, double v)
        {
            var o = SheetMath.DimOpacity(SheetKind.Duel, u, v);   // #dim opacity (.8 dim)
            var flat = MenuV6Linear.Shade(.92 * o);
            if (_dimPlate != null) _dimPlate.color = new Color(1, 1, 1, Mathf.Clamp01(flat / .92f));
            foreach (var q in _dimQuads) q.color = new Color(3 / 255f, 2 / 255f, 6 / 255f, flat);
        }

        // ------------------------------------------------------------------ the door's open parts (on the Stone)

        void BuildDoor()
        {
            var p = Stone.Parts;
            if (p == null || p.Fx == null) return;
            var door = RJ.D(J, "door");
            var parts = RJ.D(door, "parts");
            var units = RJ.D(door, "units");
            _doorHost = SheetDraw.Stretch(p.Fx, "DuelDoorOpen");
            if (p.Hosts != null && p.Hosts.TryGetValue("door", out var dh) && dh != null && dh.parent == p.Fx)
                _doorHost.SetSiblingIndex(dh.GetSiblingIndex() + 1);
            var ctx = p.Ctx;
            var gfx = ctx != null && ctx.Gfx != null ? ctx.Gfx : new UguiGfx();
            var B0 = (float)_B0;
            var circle = new Rect(_door.x - B0, _door.y - B0, 2 * B0, 2 * B0);
            // everything but the glows and the rays is inside the door's circle
            var clipGo = new GameObject("DoorCircle", typeof(RectTransform), typeof(Image), typeof(Mask));
            clipGo.transform.SetParent(_doorHost, false);
            var ci = clipGo.GetComponent<Image>();
            ci.sprite = SheetDraw.Disc();
            ci.raycastTarget = false;
            clipGo.GetComponent<Mask>().showMaskGraphic = false;
            MenuV6Layout.Place(ci.rectTransform, circle, StageRect);
            var inner = SheetDraw.Stretch(clipGo.transform, "Full");
            MenuV6Layout.Place(inner, StageRect, circle);
            _depth = SheetDraw.Part(inner, "Depth", RJ.D(parts, "depth"), StageRect);
            var starTex = gfx.Sprite(RJ.S(RJ.D(units, "star"), "sprite"));
            if (starTex != null)
            {
                _stars = gfx.Quads(inner, "Stars", StageRect, starTex);
                _stars.Add = true;
            }

            _rings = gfx.Shapes(inner, "Rings", StageRect);
            _rings.Add = true;
            var lp = RJ.D(parts, "light");
            _light = SheetDraw.Part(inner, "Light", lp, StageRect);
            _lightRect = RJ.R(lp, "rect");
            // the halves: Stone A in two clip rects, moved up / down by open B0 + the lock's jolt
            if (p.Static != null && p.Static.TryGetValue("stone_A", out var sa) && sa != null && sa.sprite != null)
            {
                _stoneA = StageRectOf(sa.rectTransform);
                var k12 = 12 * (float)_k1;
                _topClip = new Rect(_door.x - B0, _door.y - B0, 2 * B0, B0 + k12);
                _botClip = new Rect(_door.x - B0, _door.y + k12, 2 * B0, B0);
                (_halfTop, _halfTopImg) = Half(inner, "HalfTop", _topClip, sa.sprite);
                (_halfBot, _halfBotImg) = Half(inner, "HalfBottom", _botClip, sa.sprite);
            }

            var gapTex = gfx.Sprite(RJ.S(RJ.D(units, "gap"), "sprite"));
            if (gapTex != null)
            {
                _gap = gfx.Quads(inner, "Gap", StageRect, gapTex);
                _gap.Add = true;
            }

            _glows = gfx.Quads(_doorHost, "Glows", StageRect, gfx.Glow(MenuV6Linear.Glow));
            _glows.Add = true;
            var rp = RJ.D(parts, "rays");
            _rays = SheetDraw.Part(_doorHost, "Rays", rp, StageRect);
            _raysRect = RJ.R(rp, "rect");
            if (_rays != null) _rays.rectTransform.pivot = new Vector2(.5f, .5f);
        }

        (RectTransform clip, RectTransform img) Half(Transform parent, string name, Rect clip, Sprite sp)
        {
            var c = new GameObject(name, typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            c.SetParent(parent, false);
            MenuV6Layout.Place(c, clip, StageRect);
            var img = SheetDraw.Img(c, "StoneA", sp, _stoneA, clip);
            return (c, img != null ? img.rectTransform : null);
        }

        Rect StageRectOf(RectTransform rt)
        {
            var S = StageRect.size;
            float x0 = rt.anchorMin.x * S.x, x1 = rt.anchorMax.x * S.x;
            float y0 = (1 - rt.anchorMax.y) * S.y, y1 = (1 - rt.anchorMin.y) * S.y;
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        void DrawDoor(double u, double v, double t)
        {
            if (_doorHost == null) return;
            var open = SheetMath.DoorOpen(u, v);
            var on = open > .001;
            _doorHost.gameObject.SetActive(on);
            if (!on) return;
            var k1 = _k1;
            double cx = _door.x, cy = _door.y, B0 = _B0;
            SheetDraw.SetA(_depth, 1);
            if (_stars != null)
            {
                _stars.Clear();
                for (var i = 0; i < 40; i++)
                {
                    var a = MenuV6Math.Hash2(i, 5, 7) * Math.PI * 2 + t * .05;
                    var d = B0 * (.15 + .8 * Math.Sqrt(MenuV6Math.Hash2(i, 6, 7)));
                    var r = 1.6 + 1.6 * MenuV6Math.Hash2(i, 8, 7);
                    var tw = .4 + .6 * Math.Pow(Math.Max(0, Math.Sin(t * 1.3 + i)), 4);
                    var al = (.25 + .45 * MenuV6Math.Hash2(i, 7, 7)) * tw * open;
                    var size = 32 * r / 14;
                    _stars.Sprite(cx + Math.Cos(a) * d, cy + Math.Sin(a) * d, size, size, QuadList.Full,
                        new Color(226 / 255f, 178 / 255f, 74 / 255f, MenuV6Linear.Alpha(Math.Min(1, al))));
                }

                _stars.Commit();
            }

            _rings.Clear();
            for (var q = 1; q <= 6; q++)
            {
                var uu = (q / 6.0 + t * .12) % 1;
                var r = B0 * Math.Pow(1 - uu, 1.6) * .96;
                _rings.Circle(cx, cy, r, (2.6 - 1.8 * uu) * k1, new Color(235 / 255f, 198 / 255f, 106 / 255f, MenuV6Linear.Alpha((.5 * (1 - uu) + .1) * open)));
            }

            _rings.Commit();
            SheetDraw.SetA(_light, MenuV6Linear.Alpha(open));
            var dd = open * B0 + SheetMath.DoorJolt(u) * k1;
            if (_halfTopImg != null)
                MenuV6Layout.Place(_halfTopImg, new Rect(_stoneA.x, (float)(_stoneA.y - dd), _stoneA.width, _stoneA.height), _topClip);
            if (_halfBotImg != null)
                MenuV6Layout.Place(_halfBotImg, new Rect(_stoneA.x, (float)(_stoneA.y + dd), _stoneA.width, _stoneA.height), _botClip);
            if (_gap != null)
            {
                _gap.Clear();
                var gap = open * B0 * 2;
                var gy = cy + 12 * k1;
                var h = gap + 60 * k1;
                _gap.Rect(new Rect((float)(cx - B0), (float)(gy - h / 2), (float)(2 * B0), (float)h), QuadList.Full,
                    new Color(1, 1, 1, MenuV6Linear.Alpha(open)));
                _gap.Commit();
            }

            _glows.Clear();
            LockFx.Spr(_glows, MenuV6Glow.Kind.GoldHot, cx, cy, 420 * k1 * open, .28 * open);
            LockFx.Spr(_glows, MenuV6Glow.Kind.Blue, cx, cy, 640 * k1 * open, .28 * open);
            if (v >= 0) LockFx.Spr(_glows, MenuV6Glow.Kind.GoldHot, cx, cy + 12 * k1, 240 * k1, .32 * MenuV6Math.Win(v, .3, .42, .48, .66));
            _glows.Commit();
            if (_rays != null)
            {
                SheetDraw.SetA(_rays, MenuV6Linear.Alpha(open));
                _rays.rectTransform.localRotation = Quaternion.Euler(0, 0, (float)(-t * .15 * Mathf.Rad2Deg));
            }
        }

        // ------------------------------------------------------------------ the contract

        void BuildContract()
        {
            var c = MakeContent();
            var duel = RJ.D(J, "duel");
            var parts = RJ.D(duel, "parts");
            var texts = RJ.L(RJ.D(J, "texts"), "duel");
            // the crest (the winged sun) scales about its element's centre (F.cx, F.y + 8)
            var cp = RJ.D(parts, "crest");
            _crestRect = RJ.R(cp, "rect");
            _crestPivot = new Vector2(_F.center.x, _F.y + 8);
            _crest = SheetDraw.Node(c, "Crest", new Rect(_crestPivot.x - 1, _crestPivot.y - 1, 2, 2), StageRect);
            _crestImg = SheetDraw.Img(_crest, "Wings", SheetData.Sprite(cp), _crestRect, new Rect(_crestPivot.x - 1, _crestPivot.y - 1, 2, 2));
            var tspec = SheetData.Spec(texts, "ph");
            _title = Label(c, "Title", tspec, TitleText, StageRect);
            _titleBox = RJ.R(tspec, "rect");
            if (_title != null)
            {
                _titleSize = _title.fontSize;
                _titleLs = _title.GetComponent<LetterSpacingEffect>();
                if (_titleLs == null) _titleLs = TextFx.Spacing(_title, 0);
            }

            _sub = Label(c, "Subtitle", SheetData.Spec(texts, "phs"), SubText, StageRect);
            // the door's light seen through the sheet
            _doorLight = SheetDraw.Part(c, "DoorLight", RJ.D(parts, "door_light"), StageRect);
            // the cards, clipped to the inner line (its bounding rect)
            var clip = new GameObject("Cards", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            clip.SetParent(c, false);
            var inner = new Rect(_F.x + 12, _F.y + 12, _F.width - 24, _F.height - 24);
            MenuV6Layout.Place(clip, inner, StageRect);
            var cards = RJ.L(duel, "cards");
            var faces = RJ.L(RJ.D(J, "texts"), "cardFaces");
            var glow = RJ.D(duel, "glow");
            for (var i = 0; i < 3; i++) _cards[i] = BuildCard(clip, inner, i, cards != null && i < cards.Count ? cards[i] : null, glow,
                faces != null && i < faces.Count ? faces[i] : null);
            for (var i = 0; i < 3; i++) _caps[i] = Label(c, "Cap" + i, SheetData.Spec(texts, "c3cap", null, i), Caps[i], StageRect);
            var bspec = SheetData.Spec(texts, "beg");
            if (bspec == null) bspec = RJ.D(RJ.D(J, "texts"), "duelBegins");
            if (bspec != null)
            {
                var slot = SheetData.Slot(bspec);
                var r = slot.Rect;
                slot.Rect = new Rect(_F.center.x - 420, r.y, 840, r.height);
                slot.Align = "center";
                _begins = TextFx.Label(c, "Begins", slot, "", StageRect);
                if (_begins != null)
                {
                    _beginsSize = _begins.fontSize;
                    _beginsLs = _begins.GetComponent<LetterSpacingEffect>();
                    if (_beginsLs == null) _beginsLs = TextFx.Spacing(_begins, 0);
                }
            }

            BuildArmed(c, parts, texts);
            BuildSig(c, parts, texts);
            // the stand-in note (the placeholder rule, D17)
            var noteHost = SheetDraw.Stretch(c, "Note");
            _note = SheetDraw.Group(noteHost.gameObject);
            SheetDraw.Part(noteHost, "Plate", RJ.D(parts, "vsnote_plate"), StageRect);
            Label(noteHost, "Text", SheetData.Spec(texts, "vsnote"), NoteText, StageRect);
        }

        Card BuildCard(RectTransform clip, Rect inner, int i, object spec, object glow, object faceTexts)
        {
            var card = new Card();
            var cw = (float)_cw;
            var ch = (float)_ch;
            var go = new GameObject("Card" + i + "_" + Names[i].Replace(" ", ""), typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
            go.transform.SetParent(clip, false);
            card.Node = go.GetComponent<RectTransform>();
            card.Node.anchorMin = card.Node.anchorMax = new Vector2(0f, 1f);   // positioned in the clip's stage px (y down)
            card.Node.pivot = new Vector2(.5f, .5f);
            card.Node.sizeDelta = new Vector2(cw, ch);
            card.Group = go.GetComponent<CanvasGroup>();
            var hit = go.GetComponent<Image>();
            hit.sprite = UiFoundation.WhiteSprite();
            hit.color = new Color(0, 0, 0, 0);   // the card's tap target
            card.Btn = go.GetComponent<Button>();
            card.Btn.targetGraphic = hit;
            card.Btn.transition = Selectable.Transition.None;
            var idx = i;
            card.Btn.onClick.AddListener(() => OnPick(idx));
            var ptr = go.AddComponent<CardPointer>();
            ptr.Owner = this;
            ptr.Index = i;
            Live.Add(card.Btn);
            var box = RJ.L(spec, "box");
            var bx = (float)RJ.At(box, 0, 40);
            var by = (float)RJ.At(box, 1, 40);
            var local = new Rect(0, 0, cw, ch);
            Rect At(object part)
            {
                var r = RJ.R(part, "rect");
                return new Rect(r.x - bx, r.y - by, r.width, r.height);
            }

            // the glow ring under the card
            var gr = RJ.R(glow, "rect");
            card.Glow = SheetDraw.Img(card.Node, "Glow", SheetData.Sprite(glow), new Rect(gr.x - bx, gr.y - by, gr.width, gr.height), local);
            var back = RJ.D(spec, "back");
            card.Back = SheetDraw.Img(card.Node, "Back", SheetData.Sprite(back), At(back), local);
            var face = RJ.D(spec, "face");
            var localFace = SheetData.IsLocal(face);
            card.Face = SheetDraw.Img(card.Node, "Face", SheetData.Sprite(face), At(face), local);
            if (card.Back != null) card.BackP = card.Back.gameObject.AddComponent<FakePerspective>();
            if (card.Face != null) card.FaceP = card.Face.gameObject.AddComponent<FakePerspective>();
            if (!localFace)
            {
                // the plain own-art fallback face carries no text: the mode's name, its line and its terms are live, in the
                // cream of the slate card (decided: the recorded ink is for the Konami frames)
                var cardRect = RJ.R(faceTexts, "rect");
                foreach (var t in RJ.L(faceTexts, "texts") ?? new List<object>())
                {
                    var slot = SheetData.Slot(t);
                    if (slot == null) continue;
                    var r = slot.Rect;
                    slot.Rect = new Rect(r.x - cardRect.x, r.y - cardRect.y, r.width, r.height);
                    slot.Color = new Color(233 / 255f, 223 / 255f, 198 / 255f, 1f);
                    var shown = (slot.Text ?? "").Replace(' ', ' ');
                    var tx = TextFx.Label(card.Node, "FaceText", slot, shown, new Rect(0, 0, cardRect.width, cardRect.height));
                    if (tx == null) continue;
                    card.FaceText.Add(tx);
                    card.FaceTextP.Add(tx.gameObject.AddComponent<FakePerspective>());
                }
            }

            return card;
        }

        void BuildArmed(RectTransform c, Dictionary<string, object> parts, List<object> texts)
        {
            var host = SheetDraw.Stretch(c, "Armed");
            _armed = SheetDraw.Group(host.gameObject);
            var row = SheetData.Spec(texts, "armed");
            if (row == null) return;
            var rr = RJ.R(row, "rect");
            var lines = RJ.L(row, "lines");
            var bSpec = SheetData.Spec(texts, "", "SPIRIT DUELER · EQUIPPED");
            var emSpec = SheetData.Spec(texts, "", "40/40");
            var snap = Stone.Data;
            var deck = snap != null && !string.IsNullOrEmpty(snap.DeckName) ? snap.DeckName : null;
            var count = snap != null ? MenuV6Data.DeckCountLine(snap) : null;
            var lp = snap != null && snap.StartingLp > 0 ? snap.StartingLp.ToString(CultureInfo.InvariantCulture) : null;
            // the pieces in order, each measured: [icon] SPIRIT DUELER · EQUIPPED [sep] <deck> <n>/40 [sep] <LP> LP · TCG
            var items = new List<(string kind, object spec, string text)>
            {
                ("icon", null, null), ("text", bSpec, "SPIRIT DUELER · EQUIPPED")
            };
            if (deck != null)
            {
                items.Add(("sep", null, null));
                items.Add(("text", row, deck));
                if (count != null) items.Add(("text", emSpec, count));
            }

            if (lp != null)
            {
                items.Add(("sep", null, null));
                items.Add(("text", emSpec, lp));
                items.Add(("text", row, "LP · TCG"));
            }

            const float gap = 11.5f;
            var y0 = rr.y;
            var h = rr.height;
            var sep = RJ.D(parts, "armed_sep");
            var sepR = RJ.R(sep, "rect");
            var icon = RJ.D(parts, "armed_ic");
            var built = new List<(RectTransform rt, float w)>();
            foreach (var (kind, spec, text) in items)
            {
                if (kind == "icon")
                {
                    var ir = lines != null && lines.Count > 0 ? lines[0] as List<object> : null;
                    var w = (float)RJ.At(ir, 2, 28);
                    var sp = SheetData.Sprite(icon);
                    var n = SheetDraw.Node(host, "Icon", new Rect(0, (float)RJ.At(ir, 1, y0), w, (float)RJ.At(ir, 3, 24)), StageRect);
                    if (sp != null)
                    {
                        var img = SheetDraw.Img(n, "Box", sp, new Rect(0, 0, w, (float)RJ.At(ir, 3, 24)), new Rect(0, 0, w, (float)RJ.At(ir, 3, 24)));
                        if (img != null) img.preserveAspect = true;
                    }

                    built.Add((n, w));
                }
                else if (kind == "sep")
                {
                    var n = SheetDraw.Node(host, "Sep", new Rect(0, sepR.y, sepR.width, sepR.height), StageRect);
                    SheetDraw.Img(n, "Dot", SheetData.Sprite(sep), new Rect(0, 0, sepR.width, sepR.height), new Rect(0, 0, sepR.width, sepR.height));
                    built.Add((n, sepR.width));
                }
                else
                {
                    var slot = SheetData.Slot(spec);
                    if (slot == null) continue;
                    var sr = slot.Rect;
                    slot.Rect = new Rect(0, sr.y, 600, sr.height);
                    slot.NoWrap = true;
                    var t = TextFx.Label(host, "Item", slot, text, StageRect);
                    if (t == null) continue;
                    var ls = t.GetComponent<LetterSpacingEffect>();
                    var w = t.preferredWidth + (ls != null ? ls.Spacing * text.Length : 0f);
                    built.Add((t.rectTransform, w));
                }
            }

            // centred on the frame's axis, the page's gaps between the pieces
            float total = 0;
            foreach (var (_, w) in built) total += w;
            total += gap * Math.Max(0, built.Count - 1);
            var x = _F.center.x - total / 2;
            foreach (var (rt, w) in built)
            {
                var r = StageRectOf(rt);
                MenuV6Layout.Place(rt, new Rect(x, r.y, w, r.height), StageRect);
                x += w + gap;
            }
        }

        void BuildSig(RectTransform c, Dictionary<string, object> parts, List<object> texts)
        {
            var host = SheetDraw.Stretch(c, "Sig");
            _sig = SheetDraw.Group(host.gameObject);
            SheetDraw.Part(host, "You", RJ.D(parts, "sig_you"), StageRect);
            Label(host, "YouText", SheetData.Spec(texts, "c"), "YOU", StageRect);
            var ln = RJ.D(parts, "sig_ln");
            var lr = RJ.R(ln, "rect");
            SheetDraw.Part(host, "LineL", ln, StageRect);
            SheetDraw.PartAt(host, "LineR", ln, new Rect(2 * _F.center.x - lr.xMax, lr.y, lr.width, lr.height), StageRect);
            SheetDraw.Part(host, "Opp", RJ.D(parts, "sig_opp"), StageRect);
            Label(host, "OppText", SheetData.Spec(texts, "c o"), "OPP", StageRect);
            // STAY: rest, hover and press plates (the page's .stay, :hover, .press)
            var stay = RJ.D(parts, "stay");
            _stayRect = RJ.R(stay, "rect");
            _stayHoverRect = RJ.R(RJ.D(parts, "stay_hover"), "rect");
            _stayPressRect = RJ.R(RJ.D(parts, "stay_press"), "rect");
            _stayRest = SheetData.Sprite(stay);
            _stayHover = SheetData.Sprite(RJ.D(parts, "stay_hover"));
            _stayPress = SheetData.Sprite(RJ.D(parts, "stay_press"));
            // the button is the plate's rest rect (its tap target); the plate under its label changes with the state
            var go = new GameObject("Stay", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            var hit = go.GetComponent<Image>();
            hit.sprite = UiFoundation.WhiteSprite();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            MenuV6Layout.Place(hit.rectTransform, _stayRect, StageRect);
            _stayPlate = SheetDraw.Img(go.transform, "Plate", _stayRest, _stayRect, _stayRect, sliced: true);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnStay);
            var sp = go.AddComponent<StayPointer>();
            sp.Owner = this;
            Live.Add(btn);
            var t = SheetDraw.Label(go.transform, "Title", SheetData.Spec(texts, "stay"), "STAY", _stayRect);
            if (t != null) t.alignment = TextAnchor.MiddleCenter;
        }

        // ------------------------------------------------------------------ input

        internal void CardHover(int i, bool on)
        {
            var c = _cards[i];
            if (c == null || c.Over == on) return;
            var t = MenuV6Clock.Now;
            c.HoverFrom = Lift(c, t);
            c.Over = on;
            c.HoverT = t;
        }

        double Lift(Card c, double t)
        {
            if (double.IsNaN(c.HoverT)) return 0;
            var k = MenuV6Math.Out((t - c.HoverT) / .2);
            return MenuV6Math.Lerp(c.HoverFrom, c.Over ? 1 : 0, k);
        }

        internal void StayHover(bool on) => _stayOver = on;
        internal void StayDown() => _stayDown = MenuV6Clock.Now;

        void OnStay()
        {
            if (Closing || _pick >= 0) return;
            FreeUiKit.PlaySelect();
            Close();
        }

        void OnPick(int i)
        {
            if (Closing || _pick >= 0) return;
            _pick = i;
            _pickAt = MenuV6Clock.Now;
            FreeUiKit.PlayConfirm();
            foreach (var s in Live)
                if (s != null)
                    s.interactable = false;
        }

        /// <summary>The door's open parts live on the Stone (its Fx node, outside this sheet's host): they go with the sheet,
        /// however it ends (else every DUEL left one behind, and a sheet killed mid-close left the door parted).</summary>
        protected override void OnRestore()
        {
            if (_doorHost == null) return;
            var go = _doorHost.gameObject;
            _doorHost = null;
            MenuEntrance.Kill(go);
        }

        protected override double CardFlare()
        {
            for (var i = 0; i < 3; i++)
                if (_cards[i] != null && (Lift(_cards[i], MenuV6Clock.Now) > .5 || (_pick == i)))
                    return 1;
            return 0;
        }

        // ------------------------------------------------------------------ the frame

        protected override void Draw(double u, double v, double t)
        {
            // a pick holds its duel-begins beat, then the sheet locks; the route runs once the contract has sunk (v 1.3)
            if (Application.isPlaying && _pick >= 0 && !Closing && t - _pickAt >= SheetMath.PickBeat) Close(() => Route(), 1.3);
            DrawDim(u, v);
            DrawDoor(u, v, t);
            var pk = _pick >= 0 ? t - _pickAt : -1;
            var pickK = _pick >= 0 ? SheetMath.PickK(pk) : 0;
            // the crest
            var crest = SheetMath.Crest(u, v);
            if (_crest != null)
            {
                _crest.localScale = new Vector3((float)Math.Max(0, crest), (float)Math.Max(0, Math.Min(1, crest * 1.2)), 1f);
                SheetDraw.SetA(_crestImg, MenuV6Math.Clamp(crest * 4, 0, 1));
            }

            // the title tracks in (.36 em land / .32 port → .22 em) and fades out on the close
            var to = SheetMath.Title(u, v, out var tkIn);
            if (_title != null)
            {
                var em = MenuV6Math.Lerp(Lay == MenuLayoutKind.Landscape ? .36 : .32, .22, tkIn);
                var ls = (float)(em * _titleSize);
                if (_titleLs != null) _titleLs.Spacing = ls;
                MenuV6Layout.Place(_title.rectTransform, new Rect(_titleBox.x + ls, _titleBox.y, _titleBox.width - ls, _titleBox.height), StageRect);
                SheetDraw.SetA(_title, MenuV6Linear.Fade(to));
            }

            SheetDraw.SetA(_sub, MenuV6Linear.Fade(SheetMath.Subtitle(u, v, pickK)));
            if (_begins != null)
            {
                var want = _pick >= 0 ? "THE DUEL BEGINS · " + Names[_pick] : "";
                if (_begins.text != want) _begins.text = want;
                if (_beginsLs != null) _beginsLs.Spacing = (float)(MenuV6Math.Lerp(.3, .2, pickK) * _beginsSize);
                SheetDraw.SetA(_begins, MenuV6Linear.Fade(SheetMath.Begins(v, pickK)));
            }

            SheetDraw.SetA(_doorLight, MenuV6Linear.Alpha(MenuV6Math.Clamp(SheetMath.DoorLight(u, v, _pick >= 0), 0, 1)));
            // the cards
            for (var i = 0; i < 3; i++)
            {
                var c = _cards[i];
                if (c == null) continue;
                var lift = Lift(c, t);
                var cs = SheetMath.CardAt(i, u, v, _door.x, _door.y, _xs[i], _cy, _rot[i], lift, _pick, pickK, t);
                // the card node sits in the clip (inner rect): place its centre in stage px
                var inner = new Rect(_F.x + 12, _F.y + 12, _F.width - 24, _F.height - 24);
                c.Node.anchoredPosition = new Vector2((float)(cs.X - inner.x), (float)-(cs.Y - inner.y));
                c.Node.localScale = new Vector3((float)cs.Scale, (float)cs.Scale, 1f);
                c.Node.localRotation = Quaternion.Euler(0, 0, (float)-cs.RotDeg);
                c.Group.alpha = MenuV6Linear.Fade(cs.Opacity);
                var deg = (float)(180 * cs.Flip);
                var front = deg > 90f;
                if (c.Back != null)
                {
                    c.Back.enabled = !front;
                    if (c.BackP != null) c.BackP.Set(deg, CentreIn(c.Back.rectTransform));
                    c.Back.color = Color.white;
                }

                if (c.Face != null)
                {
                    c.Face.enabled = front;
                    if (c.FaceP != null) c.FaceP.Set(deg - 180f, CentreIn(c.Face.rectTransform));
                    c.Face.color = Color.white;
                }

                for (var j = 0; j < c.FaceText.Count; j++)
                {
                    c.FaceText[j].enabled = front;
                    c.FaceTextP[j].Set(deg - 180f, CentreIn(c.FaceText[j].rectTransform));
                }

                SheetDraw.SetA(c.Glow, MenuV6Linear.Alpha(MenuV6Math.Clamp(cs.Glow, 0, 1)));
                if (_caps[i] != null)
                    SheetDraw.SetA(_caps[i], MenuV6Linear.Fade(SheetMath.Caption(i, u, v, _pick == i && (v >= 0 || pickK > 0) ? 1 : lift)));
            }

            var ar = MenuV6Linear.Fade(SheetMath.Armed(u, v));
            SheetDraw.SetA(_armed, ar);
            SheetDraw.SetA(_sig, ar);
            SheetDraw.SetA(_note, ar);
            // STAY's plate: hover / press (the page's CSS states)
            if (_stayPlate != null && _stayRest != null)
            {
                var pressed = !double.IsNaN(_stayDown) && t - _stayDown < .25;
                var sp = pressed && _stayPress != null ? _stayPress : _stayOver && _stayHover != null ? _stayHover : _stayRest;
                var r = sp == _stayPress ? _stayPressRect : sp == _stayHover ? _stayHoverRect : _stayRect;
                if (_stayPlate.sprite != sp)
                {
                    _stayPlate.sprite = sp;
                    _stayPlate.type = sp.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                    MenuV6Layout.Place(_stayPlate.rectTransform, r, _stayRect);
                }
            }
        }

        /// <summary>A card's centre in a child graphic's local space (the perspective's axis).</summary>
        static Vector2 CentreIn(RectTransform rt)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return Vector2.zero;
            var pc = parent.rect.center;
            return (Vector2)rt.InverseTransformPoint(parent.TransformPoint(pc));
        }

        void Route()
        {
            if (_routed || _pick < 0) return;
            _routed = true;
            try { _route?.Invoke(_pick); }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ MenuV6] CHOOSE YOUR DUEL route: " + ex.Message); }
        }

        /// <summary>Tests / captures: pick card <paramref name="i"/> as if tapped (no route in edit mode).</summary>
        public void ForcePick(int i, double since)
        {
            _pick = Mathf.Clamp(i, 0, 2);
            _pickAt = MenuV6Clock.Now - since;
        }

        /// <summary>Tests / captures: hover card <paramref name="i"/> (-1 = none) for <paramref name="since"/> s.</summary>
        public void ForceHover(int i, double since)
        {
            var t = MenuV6Clock.Now;
            for (var j = 0; j < 3; j++)
            {
                var c = _cards[j];
                if (c == null) continue;
                c.Over = j == i;
                c.HoverFrom = c.Over ? 0 : 1;
                c.HoverT = j == i ? t - since : double.NaN;
            }
        }

        /// <summary>The cards' state for tests: (opacity, flip, hl) per card.</summary>
        public (double op, bool face)[] CardStates()
        {
            var r = new (double, bool)[3];
            for (var i = 0; i < 3; i++)
                r[i] = _cards[i] != null ? (_cards[i].Group.alpha, _cards[i].Face != null && _cards[i].Face.enabled) : (0, false);
            return r;
        }
    }

    /// <summary>A mode card's pointer: hover lifts it (the page's .2 s tween).</summary>
    public sealed class CardPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ModeCards Owner;
        public int Index;

        public void OnPointerEnter(PointerEventData e)
        {
            if (Owner != null) Owner.CardHover(Index, true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (Owner != null) Owner.CardHover(Index, false);
        }
    }

    /// <summary>STAY's pointer states (hover, press).</summary>
    public sealed class StayPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        public ModeCards Owner;

        public void OnPointerEnter(PointerEventData e)
        {
            if (Owner != null) Owner.StayHover(true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (Owner != null) Owner.StayHover(false);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (Owner != null) Owner.StayDown();
        }
    }
}
