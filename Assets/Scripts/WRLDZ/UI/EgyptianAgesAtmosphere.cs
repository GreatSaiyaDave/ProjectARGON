using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI
{
    /// <summary>
    /// Which “age” of the night desert this menu paints.
    /// Each age has a unique palette, silhouette set, and motion profile.
    /// </summary>
    public enum MenuAge
    {
        /// <summary>Boot / splash — deep void, slow stars, distant monolith.</summary>
        PrimordialNight = 0,
        /// <summary>Title / auth — moon high, gold dust, calm dunes.</summary>
        OldKingdom = 1,
        /// <summary>Hub / systems — twin pylons, hieroglyph stream, active torches.</summary>
        IntermediateKingdom = 2,
        /// <summary>Duel disk menu / command — brighter gold, temple facade energy.</summary>
        NewKingdom = 3,
        /// <summary>Desktop Lab / tools — cooler teal sand + cyber-hieroglyph glitch.</summary>
        LabNecropolis = 4,
        /// <summary>Story / tome — warm parchment wash, slower scroll glyphs.</summary>
        ScrollAges = 5,
        /// <summary>AR / create duel — violet rift, Umbrax edge, faster dust.</summary>
        UmbraxRift = 6,
        /// <summary>Settings / profile — quiet courtyard, soft pulse only.</summary>
        Courtyard = 7
    }

    /// <summary>
    /// Animated Egyptian / night / ages atmosphere for menu canvases.
    /// Pure UI (Images + text), no external art required — varies + animates per <see cref="MenuAge"/>.
    /// </summary>
    public class EgyptianAgesAtmosphere : MonoBehaviour
    {
        public MenuAge Age = MenuAge.OldKingdom;

        Image _skyTop;
        Image _skyBot;
        Image _moon;
        Image _horizonGlow;
        Image _sandNear;
        Image _sandFar;
        Image _torchL;
        Image _torchR;
        Image _vignette;
        Image _veil;
        readonly List<RectTransform> _stars = new();
        readonly List<float> _starPhase = new();
        readonly List<RectTransform> _glyphs = new();
        readonly List<float> _glyphSpeed = new();
        readonly List<RectTransform> _dust = new();
        readonly List<Vector2> _dustVel = new();
        readonly List<RectTransform> _sils = new();
        float _t;
        float _torchPhase;
        Text _ageLabel;

        static readonly string[] GlyphPool =
        {
            "𓂀", "𓃭", "𓆣", "𓇳", "𓈖", "𓊖", "𓋹", "𓌳", "𓍯", "𓎛",
            "†", "※", "◈", "◇", "▣", "◆", "✦", "✧", "☉", "☾", "△", "▽", "⬡"
        };

        /// <summary>Attach a full-screen atmosphere as first child of a canvas/root. Returns the driver.</summary>
        public static EgyptianAgesAtmosphere Attach(Transform parent, MenuAge age, bool showAgeCaption = false)
        {
            if (parent == null) return null;

            // Replace prior atmosphere this frame — deferred Destroy left two skies stacked
            var existing = parent.Find("EgyptianAgesAtmosphere");
            if (existing != null)
            {
                existing.name = "EgyptianAgesAtmosphere_dead";
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject("EgyptianAgesAtmosphere", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var driver = go.AddComponent<EgyptianAgesAtmosphere>();
            driver.Age = age;
            driver.Build(showAgeCaption);
            return driver;
        }

        /// <summary>Map common menu hosts → an age for variety.</summary>
        public static MenuAge AgeForHost(string hostName)
        {
            if (string.IsNullOrEmpty(hostName)) return MenuAge.OldKingdom;
            var n = hostName.ToLowerInvariant();
            if (n.Contains("boot") || n.Contains("splash")) return MenuAge.PrimordialNight;
            if (n.Contains("lab") || n.Contains("desktop")) return MenuAge.LabNecropolis;
            if (n.Contains("disk") || n.Contains("hub") || n.Contains("menu")) return MenuAge.NewKingdom;
            if (n.Contains("shell") || n.Contains("systems")) return MenuAge.IntermediateKingdom;
            if (n.Contains("story") || n.Contains("tome") || n.Contains("scroll")) return MenuAge.ScrollAges;
            if (n.Contains("duel") || n.Contains("create") || n.Contains("ar")) return MenuAge.UmbraxRift;
            if (n.Contains("settings") || n.Contains("profile") || n.Contains("avatar"))
                return MenuAge.Courtyard;
            if (n.Contains("auth") || n.Contains("title") || n.Contains("login"))
                return MenuAge.OldKingdom;
            // Stable variety from name hash
            var h = Mathf.Abs(hostName.GetHashCode());
            return (MenuAge)(h % 8);
        }

        void Build(bool showAgeCaption)
        {
            Palette(out var skyA, out var skyB, out var sandA, out var sandB, out var gold,
                out var accent, out var moonC, out var silC, out var dustC);

            // Sky gradient (two stacked rects)
            _skyTop = Img("SkyTop", skyA);
            Place(_skyTop.rectTransform, 0f, 0.42f, 1f, 1f);
            _skyBot = Img("SkyBot", skyB);
            Place(_skyBot.rectTransform, 0f, 0f, 1f, 0.55f);

            // Horizon gold/violet band
            _horizonGlow = Img("Horizon", new Color(gold.r, gold.g, gold.b, 0.22f));
            Place(_horizonGlow.rectTransform, 0f, 0.28f, 1f, 0.42f);

            // Stars
            var starN = Age switch
            {
                MenuAge.PrimordialNight => 28,
                MenuAge.UmbraxRift => 18,
                MenuAge.ScrollAges => 12,
                MenuAge.Courtyard => 14,
                _ => 22
            };
            for (var i = 0; i < starN; i++)
            {
                var s = Img("Star" + i, Color.white);
                var x = Mathf.Repeat(i * 0.173f + AgeSeed() * 0.07f, 1f);
                var y = 0.45f + Mathf.Repeat(i * 0.091f + AgeSeed() * 0.13f, 0.5f);
                var sz = 0.004f + (i % 5) * 0.0018f;
                Place(s.rectTransform, x, y, x + sz, y + sz * 1.6f);
                s.color = new Color(1f, 0.95f, 0.85f, 0.15f + (i % 4) * 0.12f);
                _stars.Add(s.rectTransform);
                _starPhase.Add(i * 0.7f + AgeSeed());
            }

            // Moon / scarab sun disk
            _moon = Img("Moon", moonC);
            var mx = Age == MenuAge.UmbraxRift ? 0.72f : 0.78f;
            var my = Age == MenuAge.PrimordialNight ? 0.78f : 0.74f;
            Place(_moon.rectTransform, mx, my, mx + 0.12f, my + 0.09f);
            // Crescent mask (second disk)
            if (Age != MenuAge.ScrollAges)
            {
                var mask = Img("MoonMask", skyA);
                Place(mask.rectTransform, mx + 0.03f, my + 0.015f, mx + 0.13f, my + 0.095f);
            }

            // Far silhouettes (pyramids / pylons / temple)
            BuildSilhouettes(silC);

            // Far sand
            _sandFar = Img("SandFar", sandA);
            Place(_sandFar.rectTransform, -0.05f, 0.12f, 1.05f, 0.36f);

            // Near sand dune
            _sandNear = Img("SandNear", sandB);
            Place(_sandNear.rectTransform, -0.08f, 0f, 1.08f, 0.22f);

            // Torch glows (animated)
            _torchL = Img("TorchL", new Color(1f, 0.55f, 0.15f, 0.0f));
            Place(_torchL.rectTransform, 0f, 0.15f, 0.28f, 0.55f);
            _torchR = Img("TorchR", new Color(1f, 0.55f, 0.15f, 0.0f));
            Place(_torchR.rectTransform, 0.72f, 0.15f, 1f, 0.55f);

            // Floating hieroglyphs
            var gN = Age switch
            {
                MenuAge.IntermediateKingdom => 14,
                MenuAge.LabNecropolis => 16,
                MenuAge.ScrollAges => 10,
                MenuAge.Courtyard => 6,
                _ => 11
            };
            for (var i = 0; i < gN; i++)
            {
                var g = Glyph("G" + i, GlyphPool[(i + (int)Age * 3) % GlyphPool.Length],
                    new Color(gold.r, gold.g, gold.b, 0.12f + (i % 3) * 0.06f));
                var x = Mathf.Repeat(0.05f + i * 0.08f + AgeSeed() * 0.02f, 0.92f);
                var y = 0.2f + Mathf.Repeat(i * 0.11f, 0.55f);
                Place(g.rectTransform, x, y, x + 0.08f, y + 0.06f);
                _glyphs.Add(g.rectTransform);
                _glyphSpeed.Add(0.012f + (i % 5) * 0.006f + (Age == MenuAge.LabNecropolis ? 0.02f : 0f));
            }

            // Gold dust motes
            var dN = Age == MenuAge.UmbraxRift ? 20 : 14;
            for (var i = 0; i < dN; i++)
            {
                var d = Img("Dust" + i, dustC);
                var x = Mathf.Repeat(i * 0.13f, 1f);
                var y = Mathf.Repeat(i * 0.19f + 0.1f, 0.7f);
                var s = 0.008f + (i % 4) * 0.003f;
                Place(d.rectTransform, x, y, x + s, y + s * 1.2f);
                _dust.Add(d.rectTransform);
                _dustVel.Add(new Vector2(
                    (i % 2 == 0 ? 1f : -1f) * (0.01f + (i % 3) * 0.008f),
                    0.015f + (i % 4) * 0.01f));
            }

            // Accent veil (age tint)
            _veil = Img("AgeVeil", new Color(accent.r, accent.g, accent.b, AgeVeilAlpha()));
            Place(_veil.rectTransform, 0f, 0f, 1f, 1f);

            // Vignette
            _vignette = Img("Vig", new Color(0f, 0f, 0f, 0.55f));
            // fake vignette with edge bars
            var vigB = Img("VigB", new Color(0f, 0f, 0f, 0.55f));
            Place(vigB.rectTransform, 0f, 0f, 1f, 0.12f);
            var vigT = Img("VigT", new Color(0f, 0f, 0f, 0.4f));
            Place(vigT.rectTransform, 0f, 0.88f, 1f, 1f);
            var vigL = Img("VigL", new Color(0f, 0f, 0f, 0.28f));
            Place(vigL.rectTransform, 0f, 0f, 0.08f, 1f);
            var vigR = Img("VigR", new Color(0f, 0f, 0f, 0.28f));
            Place(vigR.rectTransform, 0.92f, 0f, 1f, 1f);

            if (showAgeCaption)
            {
                _ageLabel = Glyph("AgeCap", AgeTitle(), new Color(gold.r, gold.g, gold.b, 0.45f));
                _ageLabel.fontSize = WrldzType.Readable(12);
                Place(_ageLabel.rectTransform, 0.15f, 0.015f, 0.85f, 0.045f);
            }

            // Non-blocking
            foreach (var img in GetComponentsInChildren<Image>(true))
                img.raycastTarget = false;
            foreach (var t in GetComponentsInChildren<Text>(true))
                t.raycastTarget = false;
        }

        void BuildSilhouettes(Color silC)
        {
            switch (Age)
            {
                case MenuAge.PrimordialNight:
                    // Single distant monolith
                    AddSil("Mono", silC, 0.42f, 0.22f, 0.58f, 0.52f);
                    break;
                case MenuAge.OldKingdom:
                    // Classic triple pyramids
                    AddTri("P1", silC, 0.12f, 0.18f, 0.38f, 0.48f);
                    AddTri("P2", silC, 0.38f, 0.20f, 0.62f, 0.58f);
                    AddTri("P3", silC, 0.62f, 0.18f, 0.88f, 0.46f);
                    break;
                case MenuAge.IntermediateKingdom:
                    // Twin pylons + lintel
                    AddSil("PyL", silC, 0.18f, 0.18f, 0.32f, 0.55f);
                    AddSil("PyR", silC, 0.68f, 0.18f, 0.82f, 0.55f);
                    AddSil("Lint", silC, 0.28f, 0.48f, 0.72f, 0.56f);
                    break;
                case MenuAge.NewKingdom:
                    // Temple facade steps
                    AddSil("Base", silC, 0.15f, 0.15f, 0.85f, 0.28f);
                    AddSil("Mid", silC, 0.22f, 0.28f, 0.78f, 0.40f);
                    AddSil("Top", silC, 0.32f, 0.40f, 0.68f, 0.52f);
                    AddSil("ObL", silC, 0.20f, 0.28f, 0.26f, 0.62f);
                    AddSil("ObR", silC, 0.74f, 0.28f, 0.80f, 0.62f);
                    break;
                case MenuAge.LabNecropolis:
                    // Broken stepped skyline + grid glow already in veil
                    AddTri("LP", silC, 0.05f, 0.16f, 0.28f, 0.42f);
                    AddSil("Block", silC, 0.40f, 0.16f, 0.70f, 0.38f);
                    AddTri("RP", silC, 0.72f, 0.16f, 0.98f, 0.50f);
                    break;
                case MenuAge.ScrollAges:
                    // Low continuous wall + one pyramid
                    AddSil("Wall", silC, 0f, 0.14f, 1f, 0.26f);
                    AddTri("P", silC, 0.55f, 0.22f, 0.85f, 0.50f);
                    break;
                case MenuAge.UmbraxRift:
                    // Jagged reverse pyramids / rift teeth
                    AddTri("T1", silC, 0.05f, 0.18f, 0.25f, 0.55f);
                    AddTri("T2", new Color(silC.r * 0.6f, silC.g * 0.4f, silC.b * 0.8f, silC.a),
                        0.35f, 0.16f, 0.65f, 0.62f);
                    AddTri("T3", silC, 0.70f, 0.18f, 0.95f, 0.48f);
                    break;
                default: // Courtyard
                    AddSil("ColL", silC, 0.12f, 0.16f, 0.20f, 0.55f);
                    AddSil("ColR", silC, 0.80f, 0.16f, 0.88f, 0.55f);
                    AddSil("Roof", silC, 0.10f, 0.52f, 0.90f, 0.58f);
                    break;
            }
        }

        void AddSil(string name, Color c, float x0, float y0, float x1, float y1)
        {
            var img = Img(name, c);
            Place(img.rectTransform, x0, y0, x1, y1);
            _sils.Add(img.rectTransform);
        }

        void AddTri(string name, Color c, float x0, float y0, float x1, float y1)
        {
            // Approximate pyramid with stacked shrinking rects
            var layers = 5;
            for (var i = 0; i < layers; i++)
            {
                var t = i / (float)(layers - 1);
                var inset = t * 0.42f;
                var yy0 = Mathf.Lerp(y0, y1, t * 0.92f);
                var yy1 = Mathf.Lerp(y0, y1, Mathf.Min(1f, t * 0.92f + 0.2f));
                var xx0 = Mathf.Lerp(x0, x0 + (x1 - x0) * 0.5f * inset, 1f);
                var xx1 = Mathf.Lerp(x1, x1 - (x1 - x0) * 0.5f * inset, 1f);
                // better: symmetric inset
                xx0 = Mathf.Lerp(x0, (x0 + x1) * 0.5f, inset);
                xx1 = Mathf.Lerp(x1, (x0 + x1) * 0.5f, inset);
                var img = Img(name + i, new Color(c.r, c.g, c.b, c.a * (0.55f + t * 0.45f)));
                Place(img.rectTransform, xx0, yy0, xx1, yy1);
                _sils.Add(img.rectTransform);
            }
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            _torchPhase += Time.unscaledDeltaTime * TorchSpeed();

            // Star twinkle
            for (var i = 0; i < _stars.Count; i++)
            {
                var img = _stars[i].GetComponent<Image>();
                if (img == null) continue;
                var a = 0.12f + 0.55f * (0.5f + 0.5f * Mathf.Sin(_t * 1.7f + _starPhase[i]));
                // Rare meteor flash
                if (Age == MenuAge.PrimordialNight && Mathf.Sin(_t * 0.3f + i) > 0.995f)
                    a = 1f;
                var c = img.color;
                c.a = a;
                img.color = c;
            }

            // Moon breathe
            if (_moon != null)
            {
                var s = 1f + 0.04f * Mathf.Sin(_t * 0.6f);
                _moon.rectTransform.localScale = new Vector3(s, s, 1f);
                var mc = _moon.color;
                mc.a = 0.75f + 0.15f * Mathf.Sin(_t * 0.5f);
                _moon.color = mc;
            }

            // Horizon pulse
            if (_horizonGlow != null)
            {
                var hc = _horizonGlow.color;
                hc.a = HorizonBaseAlpha() + 0.06f * Mathf.Sin(_t * 0.8f);
                _horizonGlow.color = hc;
            }

            // Sand parallax drift
            DriftSand(_sandFar, 0.006f, 0.3f);
            DriftSand(_sandNear, 0.012f, 0.55f);

            // Torches
            AnimateTorch(_torchL, 0f);
            AnimateTorch(_torchR, 1.7f);

            // Glyphs float upward / drift
            for (var i = 0; i < _glyphs.Count; i++)
            {
                var g = _glyphs[i];
                if (g == null) continue;
                var a = g.anchorMin;
                var b = g.anchorMax;
                var h = b.y - a.y;
                var w = b.x - a.x;
                var speed = _glyphSpeed[i];
                if (Age == MenuAge.LabNecropolis)
                {
                    // Glitch sideways
                    a.x += Mathf.Sin(_t * 8f + i) * 0.0008f;
                }

                a.y += speed * Time.unscaledDeltaTime;
                if (a.y > 0.82f)
                {
                    a.y = 0.18f;
                    a.x = Mathf.Repeat(a.x + 0.17f, 0.9f);
                }

                b.x = a.x + w;
                b.y = a.y + h;
                g.anchorMin = a;
                g.anchorMax = b;
                var ti = g.GetComponent<Text>();
                if (ti != null)
                {
                    var col = ti.color;
                    col.a = 0.1f + 0.12f * (0.5f + 0.5f * Mathf.Sin(_t * 2f + i));
                    ti.color = col;
                }
            }

            // Dust float
            for (var i = 0; i < _dust.Count; i++)
            {
                var d = _dust[i];
                if (d == null) continue;
                var a = d.anchorMin;
                var b = d.anchorMax;
                var w = b.x - a.x;
                var h = b.y - a.y;
                var v = _dustVel[i];
                var mult = Age == MenuAge.UmbraxRift ? 1.8f : 1f;
                a.x += v.x * Time.unscaledDeltaTime * mult;
                a.y += v.y * Time.unscaledDeltaTime * mult;
                if (a.y > 0.85f || a.x < -0.05f || a.x > 1.05f)
                {
                    a.x = Mathf.Repeat(i * 0.17f + _t * 0.01f, 1f);
                    a.y = 0.05f + (i % 5) * 0.04f;
                }

                b.x = a.x + w;
                b.y = a.y + h;
                d.anchorMin = a;
                d.anchorMax = b;
            }

            // Silhouette heat shimmer (subtle x scale)
            for (var i = 0; i < _sils.Count; i++)
            {
                var s = _sils[i];
                if (s == null) continue;
                var sx = 1f + 0.008f * Mathf.Sin(_t * 1.2f + i * 0.4f);
                s.localScale = new Vector3(sx, 1f, 1f);
            }

            // Age veil breathe
            if (_veil != null)
            {
                var vc = _veil.color;
                vc.a = AgeVeilAlpha() + 0.03f * Mathf.Sin(_t * 0.45f);
                _veil.color = vc;
            }
        }

        void DriftSand(Image sand, float amp, float speed)
        {
            if (sand == null) return;
            var rt = sand.rectTransform;
            var shift = Mathf.Sin(_t * speed + AgeSeed()) * amp;
            // nudge anchors slightly
            var min = rt.anchorMin;
            var max = rt.anchorMax;
            // keep height, shift x
            var w = max.x - min.x;
            var cx = 0.5f + shift;
            min.x = cx - w * 0.5f;
            max.x = cx + w * 0.5f;
            rt.anchorMin = min;
            rt.anchorMax = max;
        }

        void AnimateTorch(Image torch, float phaseOff)
        {
            if (torch == null) return;
            var flicker = 0.08f + 0.14f * Mathf.Abs(Mathf.Sin(_torchPhase * 6f + phaseOff))
                          + 0.05f * Mathf.Abs(Mathf.Sin(_torchPhase * 13f + phaseOff * 2f));
            if (Age == MenuAge.Courtyard || Age == MenuAge.PrimordialNight)
                flicker *= 0.45f;
            if (Age == MenuAge.NewKingdom || Age == MenuAge.IntermediateKingdom)
                flicker *= 1.25f;
            var c = torch.color;
            c.a = flicker;
            // Umbrax torches go violet
            if (Age == MenuAge.UmbraxRift)
                c = new Color(0.7f, 0.25f, 1f, flicker);
            else if (Age == MenuAge.LabNecropolis)
                c = new Color(0.2f, 0.85f, 0.95f, flicker * 0.8f);
            torch.color = c;
            var s = 1f + 0.08f * Mathf.Sin(_torchPhase * 5f + phaseOff);
            torch.rectTransform.localScale = new Vector3(s, 1f + (s - 1f) * 0.5f, 1f);
        }

        float TorchSpeed() => Age switch
        {
            MenuAge.UmbraxRift => 1.4f,
            MenuAge.LabNecropolis => 1.2f,
            MenuAge.Courtyard => 0.55f,
            MenuAge.PrimordialNight => 0.4f,
            _ => 0.85f
        };

        float HorizonBaseAlpha() => Age switch
        {
            MenuAge.ScrollAges => 0.28f,
            MenuAge.NewKingdom => 0.26f,
            MenuAge.UmbraxRift => 0.18f,
            MenuAge.PrimordialNight => 0.08f,
            _ => 0.18f
        };

        float AgeVeilAlpha() => Age switch
        {
            MenuAge.ScrollAges => 0.14f,
            MenuAge.UmbraxRift => 0.12f,
            MenuAge.LabNecropolis => 0.10f,
            MenuAge.PrimordialNight => 0.06f,
            _ => 0.07f
        };

        float AgeSeed() => (int)Age * 1.618f;

        string AgeTitle() => Age switch
        {
            MenuAge.PrimordialNight => "— PRIMORDIAL NIGHT —",
            MenuAge.OldKingdom => "— OLD KINGDOM —",
            MenuAge.IntermediateKingdom => "— INTERMEDIATE AGE —",
            MenuAge.NewKingdom => "— NEW KINGDOM —",
            MenuAge.LabNecropolis => "— LAB NECROPOLIS —",
            MenuAge.ScrollAges => "— SCROLL AGES —",
            MenuAge.UmbraxRift => "— UMBRAX RIFT —",
            MenuAge.Courtyard => "— TEMPLE COURTYARD —",
            _ => "— AGES —"
        };

        void Palette(out Color skyA, out Color skyB, out Color sandA, out Color sandB,
            out Color gold, out Color accent, out Color moonC, out Color silC, out Color dustC)
        {
            gold = new Color(0.95f, 0.78f, 0.35f, 1f);
            switch (Age)
            {
                case MenuAge.PrimordialNight:
                    skyA = new Color(0.02f, 0.02f, 0.06f, 1f);
                    skyB = new Color(0.04f, 0.03f, 0.10f, 1f);
                    sandA = new Color(0.08f, 0.06f, 0.12f, 1f);
                    sandB = new Color(0.05f, 0.04f, 0.08f, 1f);
                    accent = new Color(0.35f, 0.15f, 0.55f, 1f);
                    moonC = new Color(0.75f, 0.8f, 1f, 0.85f);
                    silC = new Color(0.02f, 0.02f, 0.04f, 0.92f);
                    dustC = new Color(0.6f, 0.5f, 0.9f, 0.35f);
                    break;
                case MenuAge.OldKingdom:
                    skyA = new Color(0.04f, 0.05f, 0.14f, 1f);
                    skyB = new Color(0.10f, 0.07f, 0.16f, 1f);
                    sandA = new Color(0.22f, 0.14f, 0.08f, 1f);
                    sandB = new Color(0.14f, 0.09f, 0.05f, 1f);
                    accent = new Color(0.85f, 0.55f, 0.15f, 1f);
                    moonC = new Color(1f, 0.95f, 0.75f, 0.9f);
                    silC = new Color(0.04f, 0.03f, 0.05f, 0.95f);
                    dustC = new Color(1f, 0.85f, 0.45f, 0.4f);
                    break;
                case MenuAge.IntermediateKingdom:
                    skyA = new Color(0.06f, 0.04f, 0.12f, 1f);
                    skyB = new Color(0.12f, 0.06f, 0.10f, 1f);
                    sandA = new Color(0.18f, 0.10f, 0.08f, 1f);
                    sandB = new Color(0.12f, 0.06f, 0.05f, 1f);
                    accent = new Color(1f, 0.4f, 0.2f, 1f);
                    moonC = new Color(1f, 0.88f, 0.65f, 0.88f);
                    silC = new Color(0.05f, 0.03f, 0.04f, 0.94f);
                    dustC = new Color(1f, 0.7f, 0.35f, 0.38f);
                    break;
                case MenuAge.NewKingdom:
                    skyA = new Color(0.05f, 0.06f, 0.16f, 1f);
                    skyB = new Color(0.14f, 0.08f, 0.12f, 1f);
                    sandA = new Color(0.28f, 0.16f, 0.07f, 1f);
                    sandB = new Color(0.16f, 0.09f, 0.04f, 1f);
                    accent = new Color(1f, 0.82f, 0.25f, 1f);
                    moonC = new Color(1f, 0.96f, 0.7f, 0.92f);
                    silC = new Color(0.06f, 0.04f, 0.05f, 0.96f);
                    dustC = new Color(1f, 0.9f, 0.4f, 0.42f);
                    break;
                case MenuAge.LabNecropolis:
                    skyA = new Color(0.03f, 0.08f, 0.12f, 1f);
                    skyB = new Color(0.04f, 0.10f, 0.14f, 1f);
                    sandA = new Color(0.08f, 0.14f, 0.16f, 1f);
                    sandB = new Color(0.04f, 0.08f, 0.10f, 1f);
                    accent = new Color(0.2f, 0.9f, 0.95f, 1f);
                    moonC = new Color(0.7f, 0.95f, 1f, 0.85f);
                    silC = new Color(0.02f, 0.05f, 0.07f, 0.94f);
                    dustC = new Color(0.4f, 0.95f, 1f, 0.35f);
                    gold = new Color(0.5f, 0.95f, 1f, 1f);
                    break;
                case MenuAge.ScrollAges:
                    skyA = new Color(0.12f, 0.08f, 0.06f, 1f);
                    skyB = new Color(0.18f, 0.12f, 0.08f, 1f);
                    sandA = new Color(0.35f, 0.24f, 0.12f, 1f);
                    sandB = new Color(0.22f, 0.15f, 0.08f, 1f);
                    accent = new Color(0.75f, 0.55f, 0.25f, 1f);
                    moonC = new Color(1f, 0.9f, 0.7f, 0.7f);
                    silC = new Color(0.08f, 0.05f, 0.03f, 0.9f);
                    dustC = new Color(0.9f, 0.75f, 0.4f, 0.3f);
                    break;
                case MenuAge.UmbraxRift:
                    skyA = new Color(0.06f, 0.02f, 0.12f, 1f);
                    skyB = new Color(0.12f, 0.03f, 0.14f, 1f);
                    sandA = new Color(0.12f, 0.05f, 0.14f, 1f);
                    sandB = new Color(0.06f, 0.02f, 0.08f, 1f);
                    accent = new Color(0.85f, 0.2f, 1f, 1f);
                    moonC = new Color(0.85f, 0.55f, 1f, 0.9f);
                    silC = new Color(0.04f, 0.01f, 0.06f, 0.95f);
                    dustC = new Color(0.9f, 0.4f, 1f, 0.4f);
                    break;
                default: // Courtyard
                    skyA = new Color(0.05f, 0.06f, 0.12f, 1f);
                    skyB = new Color(0.09f, 0.08f, 0.12f, 1f);
                    sandA = new Color(0.16f, 0.12f, 0.10f, 1f);
                    sandB = new Color(0.10f, 0.08f, 0.07f, 1f);
                    accent = new Color(0.7f, 0.65f, 0.45f, 1f);
                    moonC = new Color(0.95f, 0.92f, 0.85f, 0.75f);
                    silC = new Color(0.05f, 0.04f, 0.05f, 0.9f);
                    dustC = new Color(0.9f, 0.8f, 0.55f, 0.28f);
                    break;
            }
        }

        Image Img(string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        Text Glyph(string name, string glyph, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, 18, display: true, heavyOutline: false);
            t.text = glyph;
            t.color = c;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            return t;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
