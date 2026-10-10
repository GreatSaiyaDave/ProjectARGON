using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;

namespace WRLDZ.UI
{
    /// <summary>
    /// Boot / onboarding chrome — Eldritch Solid Vision.
    /// Every surface here is a fragment of the failing barrier-stone seal: carved lapis with
    /// Millennium-gold inlay held by Andrax's blue light, and a breach is a hole INTO the Shadow
    /// Realm. Which fragment a step gets comes from <see cref="BeginStep"/>, so the cascade reads
    /// as the seal's own history without every call site naming a state.
    /// Every public signature here is load-bearing for BootFlowUI: internals and
    /// visuals change, the API only ever grows.
    /// </summary>
    public static class FlowChrome
    {
        // ── Palette (re-pointed at EldritchPalette; names frozen for callers) ──
        public static Color Void => EldritchPalette.Void;
        public static Color Gold => EldritchPalette.MillenniumGold;
        public static Color GoldHot => EldritchPalette.GoldHot;
        public static Color Cyan => EldritchPalette.SolidVisionCyan;
        public static Color Cream => EldritchPalette.TextCream;
        public static Color Soft => EldritchPalette.TextMuted;
        public static Color Panel => EldritchPalette.Obsidian;
        public static Color Danger => EldritchPalette.BloodDanger;
        public static Color Ok => EldritchPalette.IchorGreen;
        public static Color Magenta => EldritchPalette.AbyssalMagenta;
        public static Color Violet => EldritchPalette.EldritchViolet;

        /// <summary>Andrax's barrier light — the blue that holds the seal (shader default).</summary>
        public static Color Andrax => new Color(0.353f, 0.667f, 1f, 1f);

        // ── Step context ──
        // FlowChrome is a static factory with exactly one consumer (BootFlowUI), and UGUI
        // construction is single-threaded, so parking the step's seal here is what lets every
        // existing PanelBox / Btn / Field / Dropdown call site inherit the arc unchanged.
        static SealState _stepSeal = SealState.Sealed;
        static bool _quietStep = true;

        /// <summary>The seal state the live step is being built at.</summary>
        public static SealState StepSeal => _stepSeal;

        /// <summary>True for the transactional block (DOB / account / terms): no drama.</summary>
        public static bool QuietStep => _quietStep;

        /// <summary>
        /// Open a step. Call once at the top of the step build, before any primitive runs.
        /// <paramref name="context"/> is any transform under the boot canvas — the shared ember
        /// field's ambient rate tracks the failing seal from it.
        /// </summary>
        public static void BeginStep(SealState stepSeal, Transform context = null, bool? quiet = null)
        {
            _stepSeal = stepSeal;
            _quietStep = quiet ?? (stepSeal == SealState.Sealed || stepSeal == SealState.Intact);
            if (context == null) return;
            var field = UiEmberField.Field(context);
            if (field == null) return;
            field.Ambient = true;
            field.AmbientRate = AmbientRateFor(stepSeal);

            // Re-assert every step: the motes only read as ambient while their layer is the last
            // canvas sibling, and anything that re-seats Root (an atmosphere pass, a late canvas
            // child) puts the opaque backdrop back on top of them.
            var layer = field.Host != null ? field.Host.Find("EmberField") : null;
            if (layer != null) layer.SetAsLastSibling();
        }

        /// <summary>Sparse gold dust while the seal holds; heavy violet drift once it is breached.</summary>
        static float AmbientRateFor(SealState state) => state switch
        {
            SealState.Sealed => 0.8f,
            SealState.Intact => 1.0f,
            SealState.Fractured => 1.5f,
            SealState.Condemned => 2.2f,
            _ => 2.6f
        };

        public static void ApplyPortrait()
        {
            WrldzTheme.ApplyFrameRate(); // honours Settings → Battery Saver
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
        }

        public static RectTransform CreateCanvas(string name, int sort = 40)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sort;
            UiTheme.ApplyPortraitPhone(go.GetComponent<CanvasScaler>());
            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// Boot root plate plus the shared ambient ember field.
        ///
        /// The field goes on the CANVAS, not on Root: BootFlowUI parents its panel host under Root
        /// straight after this returns, and every step paints an opaque backdrop as that host's
        /// first child — a field on Root is buried under the painting, which is why boot had no
        /// ambient motion at all. Canvas level also makes this the same field
        /// <see cref="UiEmberField.Field"/> resolves, so ArcaneButton's own hover bursts stop
        /// quietly building a second one.
        /// </summary>
        public static RectTransform RootAtmosphere(Transform canvas)
        {
            // Opaque, because the Egyptian-ages atmosphere that used to sit behind this is gone:
            // its Attach forces sibling 0 so it could never be above the backdrop, every step's
            // art switched it off anyway, and its glyph stream is legacy Text over the U+13000
            // block (tofu). The seal arc carries the per-step variation now.
            var root = MakeImage(canvas, "Root", null, EldritchPalette.Void);
            Stretch(root);
            root.GetComponent<Image>().raycastTarget = true;

            var field = UiEmberField.Attach(canvas as RectTransform, 48, ambient: true);
            if (field != null) field.AmbientRate = AmbientRateFor(_stepSeal);
            return root;
        }

        /// <summary>
        /// Full-bleed Imagine scene behind boot UI. Center-anchored cover-crop
        /// (never stretch, never fight layout with Stretch+Fitter). Opaque navy
        /// behind so mismatched Game views letterbox as night, not atmosphere.
        /// Same sprite is reused so Ken Burns can keep running; a new sprite
        /// demotes the live plate to BootBackdropPrev for a crossfade.
        /// </summary>
        public static RectTransform SetBootBackdrop(Transform host, Sprite art)
        {
            if (host == null) return null;

            if (host.GetComponent<RectMask2D>() == null)
                host.gameObject.AddComponent<RectMask2D>();

            var existing = host.Find("BootBackdrop") as RectTransform;
            if (existing != null)
            {
                var live = existing.GetComponent<Image>();
                if (live != null && live.sprite == art)
                {
                    TintScrims(existing);
                    existing.SetAsFirstSibling();
                    return existing;
                }

                var stale = host.Find("BootBackdropPrev");
                if (stale != null)
                    Object.Destroy(stale.gameObject);
                existing.name = "BootBackdropPrev";
            }

            var go = MakeImage(host, "BootBackdrop", art, Color.white);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.type = Image.Type.Simple;
            var fitter = go.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = go.gameObject.AddComponent<AspectRatioFitter>();
            if (art != null && art.rect.height > 1f)
            {
                img.preserveAspect = true;
                img.color = Color.white;
                go.anchorMin = go.anchorMax = new Vector2(0.5f, 0.5f);
                go.pivot = new Vector2(0.5f, 0.5f);
                go.anchoredPosition = Vector2.zero;
                go.sizeDelta = Vector2.zero;
                fitter.enabled = true;
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = art.rect.width / Mathf.Max(1f, art.rect.height);
            }
            else
            {
                Stretch(go);
                fitter.enabled = false;
                img.preserveAspect = false;
                img.sprite = UiFoundation.WhiteSprite();
                img.color = EldritchPalette.Void;
            }

            var cg = MenuMotion.EnsureGroup(go.gameObject);
            cg.alpha = 1f;
            cg.blocksRaycasts = false;
            cg.interactable = false;
            if (go.GetComponent<BootArtDrift>() == null)
                go.gameObject.AddComponent<BootArtDrift>().enabled = false;

            // Scrims are children of the art plate, so the fitter's pose is untouched.
            if (go.Find("BottomScrim") == null)
            {
                var scrim = MakeImage(go, "BottomScrim", null, Color.clear);
                Place(scrim, 0f, 0f, 1f, 0.20f);
            }

            // Header plates sit at y 0.86–0.98; without a top wash gold-on-art loses contrast.
            if (go.Find("TopScrim") == null)
            {
                var top = MakeImage(go, "TopScrim", null, Color.clear);
                Place(top, 0f, 0.80f, 1f, 1f);
            }

            TintScrims(go);
            go.SetAsFirstSibling();
            var prev = host.Find("BootBackdropPrev");
            if (prev != null)
                prev.SetAsFirstSibling();
            go.SetSiblingIndex(prev != null ? 1 : 0);
            return go;
        }

        /// <summary>
        /// The scrims exist for gold-on-art legibility, so hue is a bonus and luminance is the
        /// budget: violet pressure seeping in around the whole frame at or below the old pure-Void
        /// washes. Re-run per step — a reused art plate keeps the scrims it already has.
        /// </summary>
        static void TintScrims(RectTransform plate)
        {
            if (plate == null) return;
            var breached = _stepSeal == SealState.Breached;

            var bottom = plate.Find("BottomScrim");
            if (bottom != null)
            {
                var c = Color.Lerp(EldritchPalette.Void, EldritchPalette.EldritchViolet,
                    breached ? 0.38f : 0.22f);
                Paint(bottom, EldritchPalette.WithAlpha(c, breached ? 0.66f : 0.58f));
            }

            var top = plate.Find("TopScrim");
            if (top != null)
            {
                var c = Color.Lerp(EldritchPalette.Void, EldritchPalette.AbyssalMagenta, 0.14f);
                Paint(top, EldritchPalette.WithAlpha(c, 0.46f));
            }
        }

        static void Paint(Transform t, Color c)
        {
            var img = t != null ? t.GetComponent<Image>() : null;
            if (img == null) return;
            img.color = c;
            img.raycastTarget = false;
        }

        /// <summary>
        /// Persistent dressing that has to sit ABOVE the painted backdrop and BELOW the panels:
        /// screen tentacles, both hieroglyph rails, and the violet pressure veil. Built once and
        /// left alone by the per-step panel wipe — but call it again after every
        /// <see cref="SetBootBackdrop"/>, which reshuffles the host's first two children, so it
        /// re-seats itself above the art.
        /// </summary>
        public static RectTransform BootDressing(Transform panelHost)
        {
            if (panelHost == null) return null;
            var dress = panelHost.Find("BootDressing") as RectTransform;
            if (dress == null)
            {
                var go = new GameObject("BootDressing", typeof(RectTransform));
                go.transform.SetParent(panelHost, false);
                dress = go.GetComponent<RectTransform>();
                Stretch(dress);
                BuildDressing(dress);
            }

            var seat = 0;
            for (var i = 0; i < panelHost.childCount; i++)
            {
                var n = panelHost.GetChild(i).name;
                if (n == "BootBackdrop" || n == "BootBackdropPrev")
                    seat = Mathf.Max(seat, i + 1);
            }

            dress.SetSiblingIndex(Mathf.Clamp(seat, 0, Mathf.Max(0, panelHost.childCount - 1)));
            return dress;
        }

        static void BuildDressing(RectTransform dress)
        {
            EldritchDressing.ScreenTentacles(dress, 0.20f);

            var railGlow = EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.55f);
            var left = HieroglyphBorder.Create(dress, HieroglyphBorder.Edge.Left, 28f, railGlow, 1.2f);
            if (left != null) left.SetIntensity(0.6f);
            var right = HieroglyphBorder.Create(dress, HieroglyphBorder.Edge.Right, 28f, railGlow, 1.2f);
            if (right != null)
            {
                right.Reverse = true;
                right.SetIntensity(0.6f);
            }

            // Violet pressure rising off the bottom edge. Three flat bands stand in for the
            // gradient sprite the project does not ship.
            var veil = Color.Lerp(EldritchPalette.Void, EldritchPalette.EldritchViolet, 0.5f);
            Band(dress, "Veil0", 0f, 0.09f, EldritchPalette.WithAlpha(veil, 0.18f));
            Band(dress, "Veil1", 0.09f, 0.17f, EldritchPalette.WithAlpha(veil, 0.10f));
            Band(dress, "Veil2", 0.17f, 0.26f, EldritchPalette.WithAlpha(veil, 0.05f));
        }

        static void Band(RectTransform parent, string name, float y0, float y1, Color c)
        {
            Place(MakeImage(parent, name, null, c), 0f, y0, 1f, y1);
        }

        /// <summary>
        /// A fragment of the barrier-stone seal. The returned shell is a transparent raycast
        /// blocker — callers anchor their content straight onto it, exactly as before.
        /// Pass <paramref name="seal"/> to override the step's own state for one panel.
        /// </summary>
        public static RectTransform PanelBox(Transform parent, string name, SealState? seal = null)
        {
            var isHeader = !string.IsNullOrEmpty(name) &&
                           name.IndexOf("Header", System.StringComparison.OrdinalIgnoreCase) >= 0;

            var shell = MakeImage(parent, name, null, new Color(0f, 0f, 0f, 0f));
            shell.GetComponent<Image>().raycastTarget = true;

            // Carved face. The rect shaders draw their own edge / inlay / breach, so the plate
            // must stay a full-rect Simple white sprite (sliced sprites are unsupported).
            var inner = MakeImage(shell, "Inner", null, Color.white);
            Stretch(inner);
            var img = inner.GetComponent<Image>();
            img.raycastTarget = true;

            var state = seal ?? SealForPanel(name);
            // Not reactive: a panel is not a button and a stray pointer must not strain it.
            var stone = EldritchDressing.SealPlate(img, state, reactive: false);
            if (stone != null)
            {
                stone.FitCrackToAspect = true;
                Dread(stone, state);
            }

            // No gold hairline any more: the seal lays a Millennium inlay right around the rect
            // (exp(-d/_SealWidth)) and correctly pulls it back near a breach, so a painted rule
            // double-draws it — and paints gold straight across a hole into the Shadow Realm.
            if (isHeader)
            {
                CartoucheCaps(inner);
                HeaderGlyphStrip(shell);
            }

            return shell;
        }

        /// <summary>The panel's own stone, for callers that need to escalate it later.</summary>
        public static BarrierStone SealOf(Component c) =>
            c != null ? c.GetComponentInChildren<BarrierStone>(true) : null;

        /// <summary>
        /// Cartouche end-caps — what the header gets instead of the deleted gold hairline.
        /// </summary>
        static void CartoucheCaps(RectTransform inner)
        {
            var cap = EldritchAssets.CartoucheEnd();
            if (cap == null) return;
            var gold = EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.80f);

            var l = MakeImage(inner, "CapL", cap, gold);
            Place(l, 0.005f, 0.16f, 0.060f, 0.84f);
            l.GetComponent<Image>().preserveAspect = true;

            var r = MakeImage(inner, "CapR", cap, gold);
            Place(r, 0.940f, 0.16f, 0.995f, 0.84f);
            r.GetComponent<Image>().preserveAspect = true;
            r.localScale = new Vector3(-1f, 1f, 1f);
        }

        /// <summary>
        /// Cartouche band that runs directly beneath the header plate (and therefore under
        /// the step title), tying the cascade to the main menu's hieroglyph chrome.
        /// Anchored outside the header rect on purpose — the header plate is only 12% tall.
        /// </summary>
        static void HeaderGlyphStrip(RectTransform header)
        {
            if (header == null) return;
            var go = new GameObject("GlyphStrip", typeof(RectTransform));
            go.transform.SetParent(header, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, 0.02f, -0.16f, 0.98f, -0.02f);
            var strip = HieroglyphBorder.Create(rt, HieroglyphBorder.Edge.Top, 30f,
                EldritchPalette.WithAlpha(HeaderInk(), 0.92f), 2.2f);
            if (strip != null) strip.SetIntensity(HeaderGlyphIntensity());
        }

        /// <summary>
        /// Header ink for the live step, so the one piece of chrome on thirteen screens finally
        /// says how deep into the cascade the player is.
        /// </summary>
        public static Color HeaderInk() => _stepSeal switch
        {
            SealState.Sealed => EldritchPalette.MillenniumGold,
            SealState.Intact => EldritchPalette.MillenniumGold,
            SealState.Breached => Color.Lerp(EldritchPalette.TextCream, EldritchPalette.AbyssalMagenta, 0.35f),
            SealState.Condemned => Color.Lerp(EldritchPalette.TextCream, EldritchPalette.BloodDanger, 0.35f),
            _ => EldritchPalette.TextCream
        };

        /// <summary>Glyph-strip brightness on the same curve as <see cref="HeaderInk"/>.</summary>
        public static float HeaderGlyphIntensity() => _stepSeal switch
        {
            SealState.Sealed => 0.55f,
            SealState.Intact => 0.70f,
            SealState.Fractured => 1.00f,
            _ => 1.60f
        };

        /// <summary>
        /// Panel seal by step name. The header is deliberately Sealed rather than the gold its old
        /// Primary tier implied: it is 3.4:1, the worst breach aspect in boot, and the state's own
        /// brief reserves Sealed for title bars, footers and wells. Everything unnamed follows the
        /// step, because the cascade IS the seal's history.
        /// </summary>
        static SealState SealForPanel(string name)
        {
            var n = name ?? "";
            if (Has(n, "Header")) return SealState.Sealed;
            if (Has(n, "Prologue") || Has(n, "Magic")) return SealState.Breached;
            if (Has(n, "Credits") || Has(n, "Terms")) return SealState.Sealed;
            return _stepSeal == SealState.Condemned ? SealState.Fractured : _stepSeal;
        }

        static bool Has(string s, string needle) =>
            s.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Boot runs materially hotter and darker than the approved main menu, pushed through the
        /// eleven properties <see cref="BarrierStone"/>'s state table never writes — so "more
        /// breach, more smoke, darker stone" costs nothing in the menu, which shares that table.
        /// Registered as the stone's Dress hook, so it survives every later restate.
        /// </summary>
        public static void Dread(BarrierStone stone, SealState state)
        {
            if (stone == null) return;
            stone.Dress = DreadProfile;
            DreadProfile(stone.Mat, state);
        }

        static void DreadProfile(Material m, SealState state)
        {
            if (m == null) return;

            // _Brightness multiplies ONLY the stone; void, lip, pressure, bleed, smoke, barrier and
            // inlay all add on top — so one number buys darker stone AND a relatively hotter hole.
            float breach, smoke, smokeSpeed, bright, starDrift, swirl, strain, spasm, breath;
            float stoneScale, barrierWidth, sealWidth;
            var stoneTint = Color.white;
            var glow = -1f;                         // < 0 keeps the state table's own value

            switch (state)
            {
                case SealState.Intact:
                    breach = 0.60f; smoke = 0.34f; smokeSpeed = 0.30f; bright = 0.86f;
                    starDrift = 0.05f; swirl = 0.08f; strain = 0.12f; spasm = 0.30f; breath = 0.45f;
                    stoneScale = 340f; barrierWidth = 3.2f; sealWidth = 3.0f; glow = 0.80f;
                    break;
                case SealState.Sealed:
                    breach = 0.34f; smoke = 0.18f; smokeSpeed = 0.30f; bright = 0.62f;
                    starDrift = 0.05f; swirl = 0.08f; strain = 0.14f; spasm = 0.30f; breath = 0.45f;
                    stoneScale = 340f; barrierWidth = 3.2f; sealWidth = 2.4f;
                    stoneTint = new Color(0.80f, 0.84f, 1.00f, 1f);
                    break;
                case SealState.Breached:
                    breach = 1.85f; smoke = 1.70f; smokeSpeed = 0.62f; bright = 0.56f;
                    starDrift = 0.16f; swirl = 0.26f; strain = 0.40f; spasm = 1.00f; breath = 0.85f;
                    stoneScale = 300f; barrierWidth = 5.5f; sealWidth = 1.4f;  // inlay retreats
                    stoneTint = new Color(0.72f, 0.70f, 0.95f, 1f);
                    SetC(m, "_VoidColor", new Color(0.02f, 0.01f, 0.05f, 1f));
                    break;
                case SealState.Condemned:
                    breach = 1.45f; smoke = 1.15f; smokeSpeed = 0.50f; bright = 0.64f;
                    starDrift = 0.10f; swirl = 0.20f; strain = 0.34f; spasm = 0.90f; breath = 0.75f;
                    stoneScale = 300f; barrierWidth = 4.4f; sealWidth = 1.6f;
                    stoneTint = new Color(1.00f, 0.82f, 0.80f, 1f);
                    break;
                default: // Fractured
                    breach = 1.30f; smoke = 0.95f; smokeSpeed = 0.42f; bright = 0.70f;
                    starDrift = 0.09f; swirl = 0.16f; strain = 0.26f; spasm = 0.70f; breath = 0.62f;
                    stoneScale = 320f; barrierWidth = 4.0f; sealWidth = 2.2f;
                    stoneTint = new Color(0.88f, 0.88f, 0.98f, 1f);
                    break;
            }

            SetF(m, "_Breach", breach);
            SetF(m, "_SmokeAmount", smoke);
            SetF(m, "_SmokeSpeed", smokeSpeed);
            SetF(m, "_Brightness", bright);
            SetF(m, "_StarDrift", starDrift);
            SetF(m, "_SwirlSpeed", swirl);
            SetF(m, "_Strain", strain);
            SetF(m, "_Spasm", spasm);
            SetF(m, "_Breath", breath);
            SetF(m, "_StoneScale", stoneScale);
            SetF(m, "_BarrierWidth", barrierWidth);
            SetF(m, "_SealWidth", sealWidth);
            SetC(m, "_StoneTint", stoneTint);
            if (glow >= 0f) SetF(m, "_BarrierGlow", glow);
        }

        static void SetF(Material m, string prop, float v)
        {
            if (m != null && m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        static void SetC(Material m, string prop, Color c)
        {
            if (m != null && m.HasProperty(prop)) m.SetColor(prop, c);
        }

        /// <summary>
        /// A free-standing fragment of the seal — plinths, chips, gift tiles. Already dreaded and
        /// aspect-fitted; <c>PlayOpen</c> it after the caller has placed it, never before (the
        /// rect is 0×0 until then).
        /// </summary>
        public static BarrierStone SealPlate(Transform parent, string name, SealState state,
            bool reactive = false, bool raycast = false)
        {
            var rt = MakeImage(parent, name, null, Color.white);
            var img = rt.GetComponent<Image>();
            img.raycastTarget = raycast;
            var stone = EldritchDressing.SealPlate(img, state, reactive);
            if (stone == null) return null;
            stone.FitCrackToAspect = true;
            Dread(stone, state);
            return stone;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, bool display = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            FreeUiKit.EnsureLoaded();
            // Heavy black outline throughout: gold / cream on obsidian has to survive daylight.
            WrldzType.Style(t, size, display, heavyOutline: true);
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.lineSpacing = 1.05f;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>
        /// A slot carved into the stone. The seal shader ignores vertex RGB, so the InputField's
        /// own ColorTint transition is inert here — focus feedback is the gold caret plus the
        /// strain driven off selection.
        /// </summary>
        public static InputField Field(Transform parent, string name, string placeholder,
            bool password = false, SealState seal = SealState.Sealed)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;

            // Reactive: the slot is pressable, and these rects run to 6:1 on the Create form, well
            // past where one fracture sized off the long side smears.
            var stone = EldritchDressing.SealPlate(img, seal, reactive: true);
            if (stone != null)
            {
                stone.FitCrackToAspect = true;
                stone.FocusStrain = true;
                Dread(stone, seal);
            }

            // No gold hairline: the seal draws its own inlay right around the rect.
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>();
            WrldzType.Style(text, 20, display: false, heavyOutline: false);
            text.color = Cream;
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;
            Stretch(textGo.GetComponent<RectTransform>(), 18, 10, 18, 10);

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var ph = phGo.GetComponent<Text>();
            WrldzType.Style(ph, 18, display: false);
            ph.fontStyle = FontStyle.Italic;
            ph.color = EldritchPalette.WithAlpha(EldritchPalette.TextMuted, 0.55f);
            ph.text = placeholder;
            ph.alignment = TextAnchor.MiddleLeft;
            Stretch(phGo.GetComponent<RectTransform>(), 18, 10, 18, 10);

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = password
                ? InputField.ContentType.Password
                : InputField.ContentType.Standard;
            input.customCaretColor = true;
            input.caretColor = EldritchPalette.MillenniumGold;
            input.selectionColor = EldritchPalette.WithAlpha(Andrax, 0.35f);
            return input;
        }

        /// <summary>
        /// Action carved out of the seal. <paramref name="tint"/> is kept as the caller's intent
        /// signal and mapped to a tier and a seal state (see <see cref="TierForTint"/> /
        /// <see cref="SealForTint"/>); the face itself is always the stone, never the raw tint.
        /// </summary>
        public static Button Btn(Transform parent, string name, string label, Color tint,
            System.Action onClick, SealState? seal = null)
        {
            var ab = BtnEx(parent, name, label, tint, onClick, seal);
            if (ab == null) return null;
            return ab.Button != null ? ab.Button : ab.GetComponent<Button>();
        }

        /// <summary>
        /// Same button, handle returned — for the few steps that escalate their own seal later
        /// (the failed login, the sworn contract, the chosen navi).
        /// </summary>
        public static ArcaneButton BtnEx(Transform parent, string name, string label, Color tint,
            System.Action onClick, SealState? seal = null, bool heroBorder = false)
        {
            // "●" / "○" accent swatches: the glyph IS the colour sample, so it keeps the tint —
            // but the plate must not, or six arbitrary player colours land on six different tiers
            // and therefore six different seal states side by side.
            var swatch = !string.IsNullOrEmpty(label) && label.Trim().Length <= 2;
            var tier = swatch ? EldritchTier.Secondary : TierForTint(tint);

            var ab = ArcaneButton.Create(parent, label, onClick, tier,
                blurb: null, icon: null,
                titleSize: swatch ? 30 : 20,
                centerTitle: true,
                hieroglyphBorder: heroBorder);
            if (ab == null) return null;
            ab.name = name;
            if (swatch && ab.Title != null)
                ab.Title.color = new Color(tint.r, tint.g, tint.b, 1f);

            // Order is load-bearing: CarveFace swaps Face.sprite, and the seal reads _MainTex.a as
            // the plate's SHAPE, so the grain has to land before the material swap.
            EldritchDressing.CarveFace(ab, glyphAlpha: swatch ? 0f : 0.70f);
            var state = seal ?? (swatch ? SealState.Fractured : SealForTint(tint, _quietStep));
            var stone = EldritchDressing.SealFace(ab, state);
            if (stone != null)
            {
                stone.FitCrackToAspect = true;
                Dread(stone, state);
            }

            return ab;
        }

        /// <summary>
        /// Tint → seal state, through the tier the caller already encoded. Two tables rather than
        /// one clamp: Intact is gold-LOUD but structurally calm, so a primary action reads the same
        /// in both, while the same violet "arcane" tint means a hole into the Shadow Realm on a
        /// lore step and merely a fracture on an account form.
        /// </summary>
        static SealState SealForTint(Color tint, bool quiet) => TierForTint(tint) switch
        {
            EldritchTier.Primary => SealState.Intact,
            EldritchTier.Danger => SealState.Condemned,
            EldritchTier.Tertiary => SealState.Sealed,
            EldritchTier.Secondary => quiet ? SealState.Sealed : SealState.Fractured,
            _ => quiet ? SealState.Fractured : SealState.Breached   // Eldritch
        };

        /// <summary>
        /// Legacy tint → tier. BootFlowUI encodes intent as a colour: green/blue = the step's
        /// primary action, purple = arcane, teal = side door, hot orange = lab escape hatch,
        /// neutral grey = back / skip.
        /// </summary>
        static EldritchTier TierForTint(Color tint)
        {
            float r = tint.r, g = tint.g, b = tint.b;
            var max = Mathf.Max(r, Mathf.Max(g, b));
            var min = Mathf.Min(r, Mathf.Min(g, b));

            if (max - min <= 0.12f) return EldritchTier.Tertiary;                 // BACK / SKIP
            if (r >= max - 0.001f && r > 0.55f && b < 0.35f) return EldritchTier.Danger;
            if (b >= max - 0.001f && r > g) return EldritchTier.Eldritch;          // violet
            if (r < 0.30f && g > 0.35f && b >= 0.45f && Mathf.Abs(g - b) <= 0.18f)
                return EldritchTier.Secondary;                                    // teal side door
            return EldritchTier.Primary;                                          // gold hero action
        }

        /// <summary>
        /// The seal being reassembled fragment by fragment: gold behind you, the live fragment
        /// breathing violet, dark unmarked stone ahead.
        ///
        /// Deliberately NOT sealed. The rail is ~42:1 and the pips 74×44 design px, while the
        /// shader's stone tile is 300 px per repeat and its starfield divides pixels by 420 — both
        /// would sample one flat sliver of every channel. Colour is the legible read at this size.
        /// </summary>
        public static void StepIndicator(Transform parent, int current, int total = 6)
        {
            var bar = new GameObject("Steps", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            Place(bar.GetComponent<RectTransform>(), 0.18f, 0.02f, 0.82f, 0.045f);

            // Lapis rail so an unlit step still reads as a step, not empty space.
            var rail = MakeImage(bar.transform, "Rail", null,
                EldritchPalette.WithAlpha(EldritchPalette.LapisDeep, 0.50f));
            Place(rail, 0f, 0.36f, 1f, 0.64f);

            for (var i = 0; i < total; i++)
            {
                var pip = MakeImage(bar.transform, "P" + i, null, Color.white);
                var x0 = i / (float)total + 0.03f;
                var x1 = (i + 1) / (float)total - 0.03f;
                Place(pip, x0, 0.12f, x1, 0.88f);
                var img = pip.GetComponent<Image>();

                if (i < current)
                {
                    img.color = EldritchPalette.MillenniumGold;
                }
                else if (i == current)
                {
                    img.color = EldritchPalette.AbyssalMagenta;
                    if (Application.isPlaying)
                        MenuMotion.PlayOn(pip.gameObject, PipBreathe(img));
                }
                else
                {
                    img.color = EldritchPalette.WithAlpha(EldritchPalette.LapisDeep, 0.28f);
                }
            }
        }

        static System.Collections.IEnumerator PipBreathe(Image img)
        {
            var t = 0f;
            while (img != null)
            {
                t += Time.unscaledDeltaTime;
                img.color = EldritchPalette.WithAlpha(EldritchPalette.AbyssalMagenta,
                    0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 2.2f)));
                yield return null;
            }
        }

        public static Dropdown Dropdown(
            Transform parent,
            string name,
            System.Collections.Generic.IList<string> options,
            int defaultIndex = 0,
            SealState seal = SealState.Sealed)
        {
            FreeUiKit.EnsureLoaded();
            var font = EldritchPalette.Body();

            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
            root.transform.SetParent(parent, false);
            var rootImg = root.GetComponent<Image>();
            rootImg.raycastTarget = true;
            // The closed bar is a fragment of the seal (its rects are the best-proportioned in
            // boot). The popup below stays a flat plate: Dropdown Instantiates the template on
            // every Show, its Viewport Mask drives UGUI's stencil path and caches StencilMaterial
            // variants of the graphic's material, and its 44 px rows are far under the 300 px
            // stone tile. Documented exception, not an oversight.
            var stone = EldritchDressing.SealPlate(rootImg, seal, reactive: true);
            if (stone != null) Dread(stone, seal);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            var caption = labelGo.GetComponent<Text>();
            caption.font = font;
            caption.fontSize = WrldzType.Readable(20);
            caption.fontStyle = FontStyle.Bold;
            caption.color = Cream;
            caption.alignment = TextAnchor.MiddleLeft;
            caption.raycastTarget = false;
            WrldzType.ApplyOutline(caption);
            Stretch(labelGo.GetComponent<RectTransform>(), 14, 6, 36, 6);

            // Hieroglyph tick where a chevron would be (atlas can be short — stay null-safe).
            var atlas = EldritchGlyphs.Atlas();
            if (atlas != null && atlas.Length > 0)
            {
                var mark = MakeImage(root.transform, "Mark", atlas[7 % atlas.Length],
                    EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.85f));
                mark.GetComponent<Image>().preserveAspect = true;
                Place(mark, 0.84f, 0.22f, 0.97f, 0.78f);
            }

            var template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(root.transform, false);
            var templateRt = template.GetComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0f, 0f);
            templateRt.anchorMax = new Vector2(1f, 0f);
            templateRt.pivot = new Vector2(0.5f, 1f);
            templateRt.anchoredPosition = new Vector2(0f, 2f);
            templateRt.sizeDelta = new Vector2(0f, 200f);
            var templateImg = template.GetComponent<Image>();
            templateImg.sprite = UiFoundation.WhiteSprite();
            templateImg.type = Image.Type.Simple;
            templateImg.color = EldritchPalette.WithAlpha(EldritchPalette.Obsidian, 0.97f);
            templateImg.raycastTarget = true;

            var rim = MakeImage(template.transform, "Rim", null,
                EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.55f));
            Place(rim, 0f, 0.985f, 1f, 1f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 40f);

            var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(Image));
            item.transform.SetParent(content.transform, false);
            var itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 44f);
            item.GetComponent<Image>().color = EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.16f);

            var itemLabel = new GameObject("Item Label", typeof(RectTransform), typeof(Text));
            itemLabel.transform.SetParent(item.transform, false);
            Stretch(itemLabel.GetComponent<RectTransform>(), 12, 4, 12, 4);
            var il = itemLabel.GetComponent<Text>();
            il.font = font;
            il.fontSize = WrldzType.Readable(18);
            il.color = Cream;
            il.alignment = TextAnchor.MiddleLeft;
            WrldzType.ApplyOutline(il);

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();
            toggle.isOn = true;

            var scroll = template.GetComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var dd = root.GetComponent<Dropdown>();
            dd.captionText = caption;
            dd.itemText = il;
            dd.template = templateRt;
            dd.ClearOptions();
            var opts = new System.Collections.Generic.List<Dropdown.OptionData>();
            if (options != null)
                foreach (var o in options)
                    opts.Add(new Dropdown.OptionData(o));
            dd.AddOptions(opts);
            dd.value = Mathf.Clamp(defaultIndex, 0, Mathf.Max(0, opts.Count - 1));
            dd.RefreshShownValue();
            template.SetActive(false);
            return dd;
        }

        public static RectTransform MakeImage(Transform parent, string name, Sprite sp, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp ?? UiFoundation.WhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Place(Component c, float x0, float y0, float x1, float y1)
        {
            if (c != null) Place(c.GetComponent<RectTransform>(), x0, y0, x1, y1);
        }
    }
}
