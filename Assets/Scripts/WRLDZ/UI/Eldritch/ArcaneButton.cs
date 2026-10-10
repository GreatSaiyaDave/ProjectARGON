using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Command tile with an arcane energy line racing its perimeter ("WRLDZ/UI/ArcaneEnergyFlow").
    /// Child names "Title" / "Blurb" / "Ico" match HubChrome so GoTheme.Place(...Find("Title"))
    /// tweaks keep working. Hover → _Hover + ember burst, press → _Pressed + 0.97 scale.
    /// </summary>
    // ExecuteAlways: rect sync must also run in edit mode (previews, prefab baking); guarded bodies.
    [ExecuteAlways]
    public sealed class ArcaneButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int PressedId = Shader.PropertyToID("_Pressed");
        static readonly int BloomId = Shader.PropertyToID("_Bloom");
        static readonly int LineColorId = Shader.PropertyToID("_LineColor");
        static readonly int LineColor2Id = Shader.PropertyToID("_LineColor2");

        const float HoverRate = 8f;
        const float PressRate = 16f;
        const float PressScale = 0.97f;

        public Button Button { get; private set; }
        public Image Face { get; private set; }
        public Material Mat { get; private set; }
        public Text Title { get; private set; }
        public Text Blurb { get; private set; }
        public Image Icon { get; private set; }
        public HieroglyphBorder Border { get; private set; }
        public RectTransform Rect => transform as RectTransform;
        // Serialized so a BAKED PREFAB instance can rebuild its per-instance material in Awake.
        [SerializeField] EldritchTier _tier = EldritchTier.Primary;
        public EldritchTier Tier => _tier;
        public bool Interactable => _interactable;

        float _hover, _hoverTarget;
        float _pressed, _pressedTarget;
        static int _phaseCounter;
        bool _interactable = true;
        bool _rectDirty = true;
        float _baseBloom = 1f;
        Color _baseLine, _baseLine2;
        Vector3 _baseScale = Vector3.one;

        public static ArcaneButton Create(Transform parent, string title, Action onClick,
            EldritchTier tier = EldritchTier.Primary, string blurb = null, Sprite icon = null,
            int titleSize = 22, bool centerTitle = false, bool hieroglyphBorder = false)
        {
            FreeUiKit.EnsureLoaded();
            var go = new GameObject(string.IsNullOrEmpty(title) ? "Arcane" : title,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement),
                typeof(ArcaneButton));
            go.transform.SetParent(parent, false);
            var ab = go.GetComponent<ArcaneButton>();

            var face = go.GetComponent<Image>();
            face.sprite = UiFoundation.WhiteSprite();
            face.type = Image.Type.Simple;
            face.raycastTarget = true;
            ab.Face = face;
            ab.ApplyTierMaterial(tier);
            // Stagger the energy comet so a grid of tiles never fires in lockstep.
            if (ab.Mat != null && ab.Mat.HasProperty(EldritchMaterials.PhaseId))
                ab.Mat.SetFloat(EldritchMaterials.PhaseId, (_phaseCounter++ * 0.381966f) % 1f);

            var hasBlurb = !string.IsNullOrEmpty(blurb);
            var hasIcon = icon != null;

            // Touch target: same floor as MenuCommandButton (design px).
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = hasBlurb ? 68f : 56f;
            le.preferredHeight = le.minHeight;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;

            if (hieroglyphBorder)
            {
                // Thin dim ring just inside the energy line; created before text so type draws on top.
                ab.Border = HieroglyphBorder.Create(go.transform, HieroglyphBorder.Edge.Ring, 32f,
                    GlyphGlowFor(tier), 2.5f);
                var brt = ab.Border.Rect;
                brt.offsetMin = new Vector2(8f, 8f);
                brt.offsetMax = new Vector2(-8f, -8f);
                ab.Border.SetIntensity(0.75f);
            }

            var textRight = hasIcon ? 0.68f : 0.96f;
            var titleAlign = centerTitle && !hasBlurb ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            ab.Title = MakeText(go.transform, "Title", title, TitleColorFor(tier), titleAlign,
                titleSize, display: titleSize >= 22);
            if (hasBlurb)
            {
                Place(ab.Title.rectTransform, 0.06f, 0.48f, textRight, 0.94f);
                ab.Blurb = MakeText(go.transform, "Blurb", blurb, EldritchPalette.TextMuted,
                    TextAnchor.MiddleLeft, Mathf.Max(12, titleSize - 8), display: false);
                ab.Blurb.fontStyle = FontStyle.Normal;
                Place(ab.Blurb.rectTransform, 0.06f, 0.08f, textRight, 0.46f);
            }
            else if (centerTitle)
            {
                Place(ab.Title.rectTransform, 0.04f, 0.10f, hasIcon ? textRight : 0.96f, 0.90f);
            }
            else
            {
                Place(ab.Title.rectTransform, 0.06f, 0.14f, textRight, 0.86f);
            }

            if (hasIcon)
            {
                var icoGo = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                icoGo.transform.SetParent(go.transform, false);
                var ico = icoGo.GetComponent<Image>();
                ico.sprite = icon;
                ico.preserveAspect = true;
                ico.raycastTarget = false;
                ico.color = Color.white;
                Place(ico.rectTransform, 0.70f, 0.12f, 0.97f, 0.88f);
                ab.Icon = ico;
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = face;
            btn.transition = Selectable.Transition.ColorTint;
            var block = ColorBlock.defaultColorBlock;
            block.normalColor = Color.white;
            block.highlightedColor = new Color(1.12f, 1.12f, 1.15f, 1f);
            block.pressedColor = new Color(0.80f, 0.84f, 0.88f, 1f);
            block.selectedColor = Color.white;
            block.disabledColor = new Color(0.62f, 0.66f, 0.72f, 1f);
            block.fadeDuration = 0.08f;
            btn.colors = block;
            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlayClick();
                    onClick();
                });
            }

            ab.Button = btn;
            ab._baseScale = go.transform.localScale;
            return ab;
        }

        // ------------------------------------------------------------------ tiers

        void ApplyTierMaterial(EldritchTier tier)
        {
            _tier = tier;
            if (Mat != null) ArObjectUtil.Destroy(Mat);
            Mat = EldritchMaterials.NewFlow(tier);
            EldritchMaterials.Bind(Face, Mat);
            _baseBloom = Mat != null && Mat.HasProperty(BloomId) ? Mat.GetFloat(BloomId) : 1f;
            _baseLine = Mat != null && Mat.HasProperty(LineColorId)
                ? Mat.GetColor(LineColorId)
                : EldritchPalette.LineFor(tier);
            _baseLine2 = Mat != null && Mat.HasProperty(LineColor2Id)
                ? Mat.GetColor(LineColor2Id)
                : EldritchPalette.Line2For(tier);
            SetFloat(HoverId, _hover);
            SetFloat(PressedId, _pressed);
            _rectDirty = true;
            if (!Application.isPlaying) SyncRect();
        }

        public void SetTier(EldritchTier tier)
        {
            if (Face == null) return;
            ApplyTierMaterial(tier);
            if (Title != null) Title.color = TitleColorFor(tier);
            if (Border != null) Border.SetGlow(GlyphGlowFor(tier));
            SetInteractable(_interactable);
        }

        /// <summary>Dims the energy line and bloom; text stays readable.</summary>
        public void SetInteractable(bool on)
        {
            _interactable = on;
            if (Button != null) Button.interactable = on;
            if (Mat != null)
            {
                if (Mat.HasProperty(BloomId)) Mat.SetFloat(BloomId, on ? _baseBloom : _baseBloom * 0.30f);
                if (Mat.HasProperty(LineColorId))
                    Mat.SetColor(LineColorId, on ? _baseLine : Dim(_baseLine));
                if (Mat.HasProperty(LineColor2Id))
                    Mat.SetColor(LineColor2Id, on ? _baseLine2 : Dim(_baseLine2));
            }

            if (Title != null) Title.color = WithAlpha(Title.color, on ? 1f : 0.82f);
            if (Blurb != null) Blurb.color = WithAlpha(Blurb.color, on ? 1f : 0.7f);
            if (Icon != null) Icon.color = WithAlpha(Icon.color, on ? 1f : 0.6f);
            if (!on)
            {
                _hoverTarget = 0f;
                _pressedTarget = 0f;
            }
        }

        static Color Dim(Color c) => new(c.r * 0.45f + 0.15f, c.g * 0.45f + 0.15f, c.b * 0.45f + 0.18f, c.a);

        static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        static Color TitleColorFor(EldritchTier tier) => tier switch
        {
            EldritchTier.Primary => Color.white,
            EldritchTier.Secondary => EldritchPalette.TextCream,
            EldritchTier.Tertiary => EldritchPalette.TextCream,
            EldritchTier.Eldritch => Color.Lerp(EldritchPalette.TextCream, EldritchPalette.AbyssalMagenta, 0.22f),
            EldritchTier.Danger => new Color(1f, 0.74f, 0.76f, 1f),
            _ => Color.white
        };

        static Color GlyphGlowFor(EldritchTier tier) => tier switch
        {
            EldritchTier.Eldritch => EldritchPalette.AbyssalMagenta,
            EldritchTier.Danger => EldritchPalette.BloodDanger,
            EldritchTier.Secondary => EldritchPalette.SolidVisionCyan,
            _ => EldritchPalette.MillenniumGold
        };

        // ------------------------------------------------------------------ pointer

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_interactable) return;
            _hoverTarget = 1f;
            if (Border != null) Border.SetIntensity(1.4f);
            if (!Application.isPlaying)
            {
                _hover = 1f;
                SetFloat(HoverId, 1f);
                return;
            }

            var field = UiEmberField.Field(transform);
            if (field != null)
                field.BurstFromRect(Rect, 12, UiEmberField.Kind.Ember, WithAlpha(_baseLine, 1f), 0.35f, 1f);
            // Touch has no true hover: UGUI raises pointerEnter on touch-down, so playing the
            // select blip here would double up with the click blip on every tap. Mouse only.
            if (eventData != null && eventData.pointerId < 0) FreeUiKit.PlaySelect();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hoverTarget = 0f;
            _pressedTarget = 0f;
            if (Border != null) Border.SetIntensity(0.75f);
            if (!Application.isPlaying)
            {
                _hover = 0f;
                _pressed = 0f;
                SetFloat(HoverId, 0f);
                SetFloat(PressedId, 0f);
                transform.localScale = _baseScale;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactable) return;
            _pressedTarget = 1f;
            if (!Application.isPlaying)
            {
                _pressed = 1f;
                SetFloat(PressedId, 1f);
                transform.localScale = _baseScale * PressScale;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            var wasPressed = _pressedTarget > 0.5f;
            _pressedTarget = 0f;
            if (!Application.isPlaying)
            {
                _pressed = 0f;
                SetFloat(PressedId, 0f);
                transform.localScale = _baseScale;
                return;
            }

            if (!wasPressed || !_interactable) return;
            var field = UiEmberField.Field(transform);
            if (field != null)
                field.BurstFromRect(Rect, 6, UiEmberField.Kind.Spark, EldritchPalette.GoldHot, 0.3f, 0.7f);
        }

        // ------------------------------------------------------------------ frame

        void Awake()
        {
            // Prefab instance: only _tier survived serialization — rebuild face + material.
            if (Face == null) Face = GetComponent<Image>();
            if (Title == null)
            {
                var t = transform.Find("Title");
                if (t != null) Title = t.GetComponent<Text>();
            }

            if (Blurb == null)
            {
                var b = transform.Find("Blurb");
                if (b != null) Blurb = b.GetComponent<Text>();
            }

            if (Button == null) Button = GetComponent<Button>();
            if (Border == null) Border = GetComponentInChildren<HieroglyphBorder>(true);
            if (Face != null && Mat == null)
            {
                ApplyTierMaterial(_tier);
                if (Mat != null && Mat.HasProperty(EldritchMaterials.PhaseId))
                    Mat.SetFloat(EldritchMaterials.PhaseId, (_phaseCounter++ * 0.381966f) % 1f);
            }
        }

        void OnEnable()
        {
            _rectDirty = true;
        }

        void OnDisable()
        {
            // Pointer exit never arrives for a disabled tile: reset so it re-enables at rest.
            _hover = _hoverTarget = 0f;
            _pressed = _pressedTarget = 0f;
            SetFloat(HoverId, 0f);
            SetFloat(PressedId, 0f);
            transform.localScale = _baseScale;
        }

        void OnRectTransformDimensionsChange()
        {
            _rectDirty = true;
            if (!Application.isPlaying) SyncRect();
        }

        void LateUpdate()
        {
            if (_rectDirty) SyncRect();
            var dt = Time.unscaledDeltaTime;
            if (!Mathf.Approximately(_hover, _hoverTarget))
            {
                _hover = Mathf.Lerp(_hover, _hoverTarget, 1f - Mathf.Exp(-HoverRate * dt));
                if (Mathf.Abs(_hover - _hoverTarget) < 0.002f) _hover = _hoverTarget;
                SetFloat(HoverId, _hover);
            }

            if (!Mathf.Approximately(_pressed, _pressedTarget))
            {
                _pressed = Mathf.Lerp(_pressed, _pressedTarget, 1f - Mathf.Exp(-PressRate * dt));
                if (Mathf.Abs(_pressed - _pressedTarget) < 0.002f) _pressed = _pressedTarget;
                SetFloat(PressedId, _pressed);
                var s = Mathf.Lerp(1f, PressScale, _pressed);
                transform.localScale = new Vector3(_baseScale.x * s, _baseScale.y * s, _baseScale.z);
            }
        }

        /// <summary>Push the current rect to the material now (previews / bakers after layout).</summary>
        public void SyncNow()
        {
            SyncRect();
            if (Border != null) Border.RelayoutNow();
        }

        void SyncRect()
        {
            _rectDirty = false;
            if (Mat == null) return;
            var rt = Rect;
            if (rt == null) return;
            EldritchMaterials.SetRect(Mat, rt);
        }

        void SetFloat(int id, float v)
        {
            if (Mat != null && Mat.HasProperty(id)) Mat.SetFloat(id, v);
        }

        void OnDestroy()
        {
            if (Mat != null)
            {
                ArObjectUtil.Destroy(Mat);
                Mat = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        static Text MakeText(Transform parent, string name, string value, Color color,
            TextAnchor align, int designSize, bool display)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.StyleButtonLabel(t, Mathf.Max(12, designSize), display);
            t.alignment = align;
            t.color = color;
            t.text = value ?? "";
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            t.raycastTarget = false;
            t.supportRichText = false;
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
