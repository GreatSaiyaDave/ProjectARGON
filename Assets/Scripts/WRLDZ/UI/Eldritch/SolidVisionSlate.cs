using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Translucent holographic slate: an <see cref="Image"/> driven by a per-instance
    /// "WRLDZ/UI/SolidVisionSlate" material. Keeps _RectSize/_RectPivot in sync, eases
    /// _Hover, and materializes via _Open 0→1 with an ember burst.
    /// </summary>
    // ExecuteAlways: plain MonoBehaviours get no OnRectTransformDimensionsChange / LateUpdate in
    // edit mode, so bakers and previews rendered every slate with a 1×1 rect. Body guards isPlaying.
    [ExecuteAlways]
    public sealed class SolidVisionSlate : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int FillColorId = Shader.PropertyToID("_FillColor");

        const float HoverRate = 8f; // ~0.15 s time constant, unscaled

        public Image Face { get; private set; }
        public Material Mat { get; private set; }

        // Serialized so a BAKED PREFAB instance can rebuild its per-instance material in Awake.
        // Face/Mat are runtime-only; without this a dragged-in prefab would share one asset
        // material and never sync _RectSize / _Hover.
        [SerializeField] EldritchTier _tier = EldritchTier.Primary;
        public EldritchTier Tier => _tier;
        public RectTransform Rect => transform as RectTransform;

        /// <summary>Pointer enter/exit drives _Hover when true.</summary>
        public bool HoverGlow = true;

        /// <summary>Embers on <see cref="PlayOpen"/> (panel roots on; tiny chips off).</summary>
        public bool OpenEmbers = true;

        float _hover;
        float _hoverTarget;
        bool _rectDirty = true;
        Coroutine _openCo;

        public static SolidVisionSlate Create(Transform parent, string name, EldritchTier tier,
            bool hoverGlow = true, bool raycast = true)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Slate" : name,
                typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.raycastTarget = raycast;
            var slate = Attach(img, tier);
            slate.HoverGlow = hoverGlow;
            return slate;
        }

        /// <summary>Dress an existing Image (keeps its sprite / rect, swaps material).</summary>
        public static SolidVisionSlate Attach(Image image, EldritchTier tier)
        {
            if (image == null) return null;
            var slate = image.GetComponent<SolidVisionSlate>();
            if (slate == null) slate = image.gameObject.AddComponent<SolidVisionSlate>();
            slate.Face = image;
            slate.ApplyTier(tier);
            return slate;
        }

        void ApplyTier(EldritchTier tier)
        {
            _tier = tier;
            if (Mat != null) ArObjectUtil.Destroy(Mat);
            Mat = EldritchMaterials.NewSlate(tier);
            if (Face.sprite == null) Face.sprite = UiFoundation.WhiteSprite();
            EldritchMaterials.Bind(Face, Mat);
            SetFloat(OpenId, 1f); // materialized until PlayOpen is asked for
            _hover = _hoverTarget = 0f;
            SetFloat(HoverId, 0f);
            SyncRect();
        }

        public void SetTier(EldritchTier tier)
        {
            if (Face == null) return;
            ApplyTier(tier);
        }

        /// <summary>Eased toward the target at ~8/s (snaps in edit mode).</summary>
        public void SetHover(float target01)
        {
            _hoverTarget = Mathf.Clamp01(target01);
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                _hover = _hoverTarget;
                SetFloat(HoverId, _hover);
            }
        }

        /// <summary>
        /// Strip the animated layers off a decorative chrome plate (title bars, footers, option
        /// wells). Wide short rects stretch the 256px noise tile into blobs and bunch the
        /// scanlines, which reads as plasma rather than glass. Keeps the fill and the edge glow.
        /// </summary>
        public void Quiet()
        {
            if (Mat == null) return;
            SetFloat(Shader.PropertyToID("_ScanIntensity"), 0.02f);
            SetFloat(Shader.PropertyToID("_ScanBase"), 0.02f);   // SetRect rescales from the base
            SetFloat(Shader.PropertyToID("_SweepIntensity"), 0.06f);
            SetFloat(Shader.PropertyToID("_SweepBase"), 0.06f);
            SetFloat(Shader.PropertyToID("_VeinStrength"), 0.02f);
            SetFloat(Shader.PropertyToID("_VeinBase"), 0.02f);
            SetFloat(Shader.PropertyToID("_Warp"), 0f);
            SetFloat(Shader.PropertyToID("_Flicker"), 0f);
        }

        public void SetTint(Color fill, Color edge)
        {
            if (Mat == null) return;
            if (Mat.HasProperty(FillColorId)) Mat.SetColor(FillColorId, fill);
            if (Mat.HasProperty(EdgeColorId)) Mat.SetColor(EdgeColorId, edge);
        }

        /// <summary>_Open 0→1 (OutCubic) plus an ember burst. Snaps to 1 in edit mode.</summary>
        public void PlayOpen(float duration = 0.42f)
        {
            if (_openCo != null)
            {
                StopCoroutine(_openCo);
                _openCo = null;
            }

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                SetFloat(OpenId, 1f);
                return;
            }

            SetFloat(OpenId, 0f);
            _openCo = StartCoroutine(OpenRoutine(Mathf.Max(0.05f, duration)));
            if (OpenEmbers)
            {
                var field = UiEmberField.Field(transform);
                if (field != null)
                {
                    var edge = Mat != null && Mat.HasProperty(EdgeColorId)
                        ? Mat.GetColor(EdgeColorId)
                        : EldritchPalette.MillenniumGold;
                    edge.a = 1f;
                    field.BurstFromRect(Rect, 10, UiEmberField.Kind.Ember, edge, 0.7f, 1f);
                }
            }
        }

        IEnumerator OpenRoutine(float duration)
        {
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetFloat(OpenId, MenuMotion.OutCubic(t / duration));
                yield return null;
            }

            SetFloat(OpenId, 1f);
            _openCo = null;
        }

        void Awake()
        {
            // Prefab instance (or domain reload): nothing was serialized but the tier — rebuild.
            if (Face == null) Face = GetComponent<Image>();
            if (Face != null && Mat == null) ApplyTier(_tier);
        }

        void OnEnable()
        {
            _rectDirty = true;
            // Deactivating the GameObject kills OpenRoutine mid-reveal and Unity never resumes it,
            // which would leave _Open frozen (the shader hard-clips the plate top-to-bottom by it).
            // A live handle still parked here means exactly that — snap the reveal to done.
            if (_openCo == null) return;
            _openCo = null;
            SetFloat(OpenId, 1f);
        }

        void OnRectTransformDimensionsChange()
        {
            _rectDirty = true;
            // No LateUpdate in edit mode — sync straight away so bakers see correct values.
            if (!Application.isPlaying) SyncRect();
        }

        void LateUpdate()
        {
            if (_rectDirty) SyncRect();
            if (!Mathf.Approximately(_hover, _hoverTarget))
            {
                var k = 1f - Mathf.Exp(-HoverRate * Time.unscaledDeltaTime);
                _hover = Mathf.Lerp(_hover, _hoverTarget, k);
                if (Mathf.Abs(_hover - _hoverTarget) < 0.002f) _hover = _hoverTarget;
                SetFloat(HoverId, _hover);
            }
        }

        /// <summary>Push the current rect to the material now (previews / bakers after layout).</summary>
        public void SyncNow() => SyncRect();

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

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (HoverGlow) SetHover(1f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (HoverGlow) SetHover(0f);
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
