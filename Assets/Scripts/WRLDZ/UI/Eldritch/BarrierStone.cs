using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// How badly this surface's seal has failed. Canon: Ptah and Anubis sealed Umbrax behind the
    /// barrier-stone; Crawford cracked it. Primary actions are near-intact gold; the deeper you go
    /// toward the Shadow Realm, the more of Umbrax shows through.
    /// </summary>
    public enum SealState
    {
        /// <summary>Hairline only. Millennium gold dominant. Primary CTAs.</summary>
        Intact = 0,
        /// <summary>A real fracture. The workhorse for command tiles and sheets.</summary>
        Fractured = 1,
        /// <summary>Quiet chrome: title bars, footers, wells. Barely marked.</summary>
        Sealed = 2,
        /// <summary>Shattered. Zone Mode, Tears, anything touching the Shadow Realm.</summary>
        Breached = 3,
        /// <summary>Blood-lit fracture. Destructive actions.</summary>
        Condemned = 4
    }

    /// <summary>
    /// Material factory + per-instance driver for "WRLDZ/UI/BarrierStoneSeal".
    /// The shader animates on its own (Umbrax breathes, stars drift, smoke rises); this component
    /// only feeds it rect size, hover/press strain, and the materialize reveal.
    /// </summary>
    [ExecuteAlways]
    public sealed class BarrierStone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public const string ShaderName = "WRLDZ/UI/BarrierStoneSeal";
        const string Tag = "[WRLDZ Eldritch]";
        const float HoverRate = 7f;
        const float PressRate = 16f;

        static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        static readonly int RectPivotId = Shader.PropertyToID("_RectPivot");
        static readonly int StoneTexId = Shader.PropertyToID("_StoneTex");
        static readonly int CrackTexId = Shader.PropertyToID("_CrackTex");
        static readonly int StarTexId = Shader.PropertyToID("_StarTex");
        static readonly int SwirlTexId = Shader.PropertyToID("_SwirlTex");
        static readonly int CrackCellId = Shader.PropertyToID("_CrackCell");
        static readonly int CrackScaleId = Shader.PropertyToID("_CrackScale");
        static readonly int CrackOffsetId = Shader.PropertyToID("_CrackOffset");
        static readonly int BreachId = Shader.PropertyToID("_Breach");
        static readonly int StoneTintId = Shader.PropertyToID("_StoneTint");
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        static readonly int PressureId = Shader.PropertyToID("_PressureColor");
        static readonly int BleedId = Shader.PropertyToID("_BleedColor");
        static readonly int BarrierId = Shader.PropertyToID("_BarrierColor");
        static readonly int SealColorId = Shader.PropertyToID("_SealColor");
        static readonly int BarrierGlowId = Shader.PropertyToID("_BarrierGlow");
        static readonly int SmokeAmountId = Shader.PropertyToID("_SmokeAmount");
        static readonly int PhaseId = Shader.PropertyToID("_Phase");
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int PressedId = Shader.PropertyToID("_Pressed");
        static readonly int OpenId = Shader.PropertyToID("_Open");

        static Shader _shader;
        static bool _warned;
        static int _phaseCounter;

        public Image Face { get; private set; }
        public Material Mat { get; private set; }
        public RectTransform Rect => transform as RectTransform;

        [SerializeField] SealState _state = SealState.Fractured;
        public SealState State => _state;

        /// <summary>Pointer enter/exit strains the seal when true.</summary>
        public bool Reactive = true;

        /// <summary>
        /// Selection strains the seal when true — for an <see cref="InputField"/> sharing this
        /// GameObject, whose ColorTint transition is inert against a shader that ignores vertex RGB.
        /// Off for buttons: UGUI leaves a tapped button selected, which would latch the strain on.
        /// </summary>
        public bool FocusStrain;

        /// <summary>
        /// Re-fit the fracture to the rect's aspect on every <see cref="SyncRect"/>. Default OFF so
        /// already-approved surfaces keep the scale their material was born with; boot turns it on
        /// because its plates run to 6:1, where one fracture sized off the long side smears.
        /// </summary>
        public bool FitCrackToAspect;

        /// <summary>
        /// Re-applied after every <see cref="Apply"/> / <see cref="SetState"/> so a caller's own
        /// property profile survives a restate instead of being silently reset to the state table.
        /// </summary>
        public System.Action<Material, SealState> Dress;

        float _hover, _hoverTarget, _pressed, _pressedTarget, _strain;
        float _crackJitter = 1f;
        Color? _barrierOverride, _sealOverride;
        bool _rectDirty = true;

        // ───────────────────────── factory ─────────────────────────

        public static Shader SealShader()
        {
            if (_shader != null) return _shader;
            var sh = Shader.Find(ShaderName);
            if (sh != null) { _shader = sh; return sh; }
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning($"{Tag} shader '{ShaderName}' not found — falling back to UI/Default.");
            }
            return Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        }

        /// <summary>New per-instance seal material for a state. UGUI has no MaterialPropertyBlock.</summary>
        public static Material NewMaterial(SealState state)
        {
            var m = new Material(SealShader()) { name = "M_BarrierStone_" + state };
            Tune(m, state);
            Vary(m);
            SetF(m, OpenId, 1f);
            return m;
        }

        /// <summary>
        /// The state's own look. Deliberately split from <see cref="Vary"/>: a restate has to be
        /// able to widen the SAME crack, and rebuilding the material re-rolls the fracture so an
        /// escalation read as four unrelated breaks.
        /// </summary>
        static void Tune(Material m, SealState state)
        {
            if (m == null) return;
            SetTex(m, StoneTexId, EldritchAssets.SealStone());
            SetTex(m, CrackTexId, EldritchAssets.SealCracks());
            SetTex(m, StarTexId, EldritchAssets.SealStars());
            SetTex(m, SwirlTexId, EldritchAssets.EldritchNoise());

            // How open the seal is, and how much of Umbrax shows. Which fracture is NOT decided
            // here: _Breach carries severity on its own (it scales every channel of the fracture
            // sheet), so the cell index is free to be per-instance variety further down.
            float breach, glow, smoke, bright;
            Color pressure = EldritchPalette.AbyssalMagenta;
            Color bleed = EldritchPalette.EldritchViolet;
            Color barrier = new Color(0.353f, 0.667f, 1f, 1f);
            Color seal = EldritchPalette.MillenniumGold;
            Color tint = Color.white;

            switch (state)
            {
                case SealState.Intact:
                    breach = 0.55f; glow = 0.75f; smoke = 0.30f; bright = 1.00f;
                    break;
                case SealState.Sealed:
                    breach = 0.28f; glow = 0.45f; smoke = 0.12f; bright = 0.80f;
                    break;
                case SealState.Breached:
                    breach = 1.45f; glow = 0.55f; smoke = 1.05f; bright = 0.72f;
                    tint = new Color(0.86f, 0.84f, 1f, 1f);
                    break;
                case SealState.Condemned:
                    breach = 1.15f; glow = 0.60f; smoke = 0.70f; bright = 0.82f;
                    pressure = EldritchPalette.BloodDanger;
                    bleed = new Color(1f, 0.35f, 0.30f, 1f);
                    seal = EldritchPalette.BloodDanger;
                    break;
                default: // Fractured
                    breach = 0.95f; glow = 0.70f; smoke = 0.55f; bright = 0.92f;
                    break;
            }

            SetF(m, BreachId, breach);
            SetF(m, BarrierGlowId, glow);
            SetF(m, SmokeAmountId, smoke);
            SetF(m, BrightnessId, bright);
            SetC(m, StoneTintId, tint);
            SetC(m, PressureId, pressure);
            SetC(m, BleedId, bleed);
            SetC(m, BarrierId, barrier);
            SetC(m, SealColorId, seal);
        }

        /// <summary>
        /// Per-instance identity: stagger the breathing and pick a different corner of the fracture
        /// sheet, so a grid of tiles never pulses in lockstep or shows the same crack. The sheet's
        /// four cells are distinct networks of comparable severity, so cycling them per instance
        /// changes the shape without changing how broken the seal reads.
        /// </summary>
        static void Vary(Material m)
        {
            if (m == null) return;
            var n = _phaseCounter++;
            SetF(m, CrackCellId, n % 4);
            SetF(m, PhaseId, (n * 0.381966f) % 1f);
            SetV(m, CrackOffsetId, new Vector4(((n * 0.2793f) % 1f - 0.5f) * 0.22f,
                                               ((n * 0.6180f) % 1f - 0.5f) * 0.22f, 0f, 0f));
            SetF(m, CrackScaleId, 0.92f + ((n * 0.4142f) % 1f) * 0.30f);
        }

        // Null-tolerant: the public setters below are reachable before Awake has built a material.
        static void SetF(Material m, int id, float v) { if (m != null && m.HasProperty(id)) m.SetFloat(id, v); }
        static void SetC(Material m, int id, Color c) { if (m != null && m.HasProperty(id)) m.SetColor(id, c); }
        static void SetV(Material m, int id, Vector4 v) { if (m != null && m.HasProperty(id)) m.SetVector(id, v); }
        static void SetTex(Material m, int id, Texture t) { if (t != null && m.HasProperty(id)) m.SetTexture(id, t); }

        // ───────────────────────── builders ─────────────────────────

        /// <summary>Dress an existing Image as a fragment of the seal.</summary>
        public static BarrierStone Attach(Image image, SealState state)
        {
            if (image == null) return null;
            var bs = image.GetComponent<BarrierStone>();
            if (bs == null) bs = image.gameObject.AddComponent<BarrierStone>();
            bs.Face = image;
            bs.Apply(state);
            return bs;
        }

        public static BarrierStone Create(Transform parent, string name, SealState state,
            bool reactive = true, bool raycast = true)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Seal" : name,
                typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.raycastTarget = raycast;
            var bs = Attach(img, state);
            if (bs != null) bs.Reactive = reactive;
            return bs;
        }

        void Apply(SealState state)
        {
            _state = state;
            if (Mat != null) ArObjectUtil.Destroy(Mat);
            Mat = NewMaterial(state);
            _crackJitter = Mat != null && Mat.HasProperty(CrackScaleId) ? Mat.GetFloat(CrackScaleId) : 1f;
            if (Face != null)
            {
                if (Face.sprite == null) Face.sprite = UiFoundation.WhiteSprite();
                Face.type = Image.Type.Simple;      // rect maths assumes a full-rect quad
                Face.material = Mat;
                Face.color = Color.white;           // the shader owns the colour
            }
            _hover = _hoverTarget = _pressed = _pressedTarget = _strain = 0f;
            SetF(Mat, HoverId, 0f);
            SetF(Mat, PressedId, 0f);
            ReapplyOverrides();
            Dress?.Invoke(Mat, state);
            SyncRect();
        }

        /// <summary>
        /// Restate. <paramref name="keepFracture"/> re-tunes the live material, so an escalation is
        /// the same crack widening (and a running <see cref="PlayOpen"/> is not snapped shut);
        /// false rebuilds it and re-rolls the fracture.
        /// </summary>
        public void SetState(SealState state, bool keepFracture = true)
        {
            if (Face == null) return;
            if (!keepFracture || Mat == null)
            {
                Apply(state);
                return;
            }

            _state = state;
            Tune(Mat, state);
            ReapplyOverrides();
            Dress?.Invoke(Mat, state);
        }

        /// <summary>Widen the breach without material churn (ramps, load bars, scripted beats).</summary>
        public void SetBreach(float breach) => SetF(Mat, BreachId, Mathf.Clamp(breach, 0f, 2f));

        /// <summary>How hard the Shadow Realm seeps out of the seam. 0–2.</summary>
        public void SetSmoke(float smoke) => SetF(Mat, SmokeAmountId, Mathf.Clamp(smoke, 0f, 2f));

        /// <summary>Andrax's barrier light for this fragment — survives a later restate.</summary>
        public void SetBarrierColor(Color c)
        {
            _barrierOverride = c;
            SetC(Mat, BarrierId, c);
        }

        /// <summary>Millennium inlay colour for this fragment — survives a later restate.</summary>
        public void SetSealColor(Color c)
        {
            _sealOverride = c;
            SetC(Mat, SealColorId, c);
        }

        /// <summary>
        /// External strain (focus, scripted beats). Held separately from hover so a pointer-exit —
        /// which UGUI raises on every touch-up — cannot clear a field's focus glow.
        /// </summary>
        public void SetStrain(float strain01)
        {
            _strain = Mathf.Clamp01(strain01);
            if (Application.isPlaying && isActiveAndEnabled) return;
            _hover = Mathf.Max(_hoverTarget, _strain);
            SetF(Mat, HoverId, _hover);
        }

        void ReapplyOverrides()
        {
            if (Mat == null) return;
            if (_barrierOverride.HasValue) SetC(Mat, BarrierId, _barrierOverride.Value);
            if (_sealOverride.HasValue) SetC(Mat, SealColorId, _sealOverride.Value);
        }

        /// <summary>Materialize: 0 -> 1 sweeps the stone in. Snaps in edit mode.</summary>
        public void PlayOpen(float duration = 0.45f)
        {
            if (Mat == null) return;
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                SetF(Mat, OpenId, 1f);
                return;
            }

            StopAllCoroutines();
            StartCoroutine(OpenRoutine(Mathf.Max(0.05f, duration)));
        }

        System.Collections.IEnumerator OpenRoutine(float dur)
        {
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                SetF(Mat, OpenId, MenuMotion.OutCubic(t / dur));
                yield return null;
            }

            SetF(Mat, OpenId, 1f);
        }

        /// <summary>Push the current rect to the material now (previews / bakers after layout).</summary>
        public void SyncNow() => SyncRect();

        void SyncRect()
        {
            _rectDirty = false;
            var rt = Rect;
            if (Mat == null || rt == null) return;
            var r = rt.rect;
            var w = Mathf.Max(r.width, 1f);
            var h = Mathf.Max(r.height, 1f);
            SetV(Mat, RectSizeId, new Vector4(w, h, 0f, 0f));
            SetV(Mat, RectPivotId, new Vector4(rt.pivot.x, rt.pivot.y, 0f, 0f));
            if (!FitCrackToAspect) return;

            // The shader squares its fracture tile off the LONG side (unit = max(sz) * 0.55 *
            // _CrackScale), so on a 6:1 slab one fracture is stretched over six times the plate's
            // height and reads as a smear. Shrink the scale by the aspect so the tile stays close
            // to the SHORT side and the run carries several separate breaks instead. 2.9 is the
            // constant that reproduces the approved menu's look at its own ~3:1 tiles.
            var aspect = Mathf.Max(w / h, h / w);
            SetF(Mat, CrackScaleId, Mathf.Clamp(_crackJitter * 2.9f / aspect, 0.4f, 3f));
        }

        void Awake()
        {
            if (Face == null) Face = GetComponent<Image>();
            if (Face != null && Mat == null) Apply(_state);
        }

        void OnEnable() => _rectDirty = true;

        void OnRectTransformDimensionsChange()
        {
            _rectDirty = true;
            if (!Application.isPlaying) SyncRect();
        }

        void LateUpdate()
        {
            if (_rectDirty) SyncRect();
            if (Mat == null) return;
            var dt = Time.unscaledDeltaTime;

            var strain = Mathf.Max(_hoverTarget, _strain);
            if (!Mathf.Approximately(_hover, strain))
            {
                _hover = Mathf.Lerp(_hover, strain, 1f - Mathf.Exp(-HoverRate * dt));
                if (Mathf.Abs(_hover - strain) < 0.002f) _hover = strain;
                SetF(Mat, HoverId, _hover);
            }

            if (!Mathf.Approximately(_pressed, _pressedTarget))
            {
                _pressed = Mathf.Lerp(_pressed, _pressedTarget, 1f - Mathf.Exp(-PressRate * dt));
                if (Mathf.Abs(_pressed - _pressedTarget) < 0.002f) _pressed = _pressedTarget;
                SetF(Mat, PressedId, _pressed);
            }
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (!Reactive) return;
            _hoverTarget = 1f;
            if (!Application.isPlaying) { _hover = 1f; SetF(Mat, HoverId, 1f); }
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hoverTarget = 0f;
            _pressedTarget = 0f;
            if (!Application.isPlaying)
            {
                _hover = _strain;
                _pressed = 0f;
                SetF(Mat, HoverId, _hover);
                SetF(Mat, PressedId, 0f);
            }
        }

        public void OnSelect(BaseEventData e)
        {
            if (FocusStrain) SetStrain(1f);
        }

        public void OnDeselect(BaseEventData e)
        {
            if (FocusStrain) SetStrain(0f);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Reactive) return;
            _pressedTarget = 1f;
            if (!Application.isPlaying) { _pressed = 1f; SetF(Mat, PressedId, 1f); }
        }

        public void OnPointerUp(PointerEventData e)
        {
            _pressedTarget = 0f;
            if (!Application.isPlaying) { _pressed = 0f; SetF(Mat, PressedId, 0f); }
        }

        void OnDestroy()
        {
            if (Mat != null)
            {
                ArObjectUtil.Destroy(Mat);
                Mat = null;
            }
        }
    }
}
