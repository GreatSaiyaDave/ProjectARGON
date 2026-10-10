using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Live 3D body for the customizer: the same <see cref="OverworldAvatar3d"/> the map uses, spawned on an
    /// off-screen stage (layer <see cref="Layer"/>, far below the world) and drawn by its own camera into a
    /// RenderTexture shown in a RawImage. Drag to turn the body; the corner chip toggles full body / face.
    /// The framing is FIXED (a 2.1 m column), so the height slider visibly changes the figure's size.
    /// Returns false from <see cref="Apply"/> when the 3D base is missing so the caller can keep the 2D portrait.
    /// </summary>
    public sealed class AvatarPreview3d : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        public const int Layer = 27;
        static readonly Vector3 StagePos = new(0f, -5000f, 0f);
        const float FullColumn = 2.12f;   // metres of body shown top to bottom (tallest body 2.03 m + hair)

        RawImage _img;
        RenderTexture _rt;
        Camera _cam;
        GameObject _stage;
        Transform _pivot;
        OverworldAvatar3d _av;
        bool _female;
        bool _faceZoom;
        float _yaw;
        float _headY = 1.6f;
        Text _zoomLabel;

        public bool HasBody => _av != null;

        public static AvatarPreview3d Create(Transform parent)
        {
            var go = new GameObject("AvatarPreview3d", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<AvatarPreview3d>();
            p._img = go.GetComponent<RawImage>();
            p._img.color = Color.white;
            p._img.raycastTarget = true;

            var chip = new GameObject("ZoomChip", typeof(RectTransform), typeof(Image), typeof(Button));
            chip.transform.SetParent(go.transform, false);
            var crt = chip.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.74f, 0.90f); crt.anchorMax = new Vector2(0.98f, 0.99f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            var ci = chip.GetComponent<Image>();
            ci.sprite = UiFoundation.WhiteSprite();
            ci.color = new Color(0.04f, 0.06f, 0.10f, 0.62f);
            var t = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            t.transform.SetParent(chip.transform, false);
            var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            WrldzType.Style(t, 14, display: false);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = DuelystUi.GoldHot;
            t.raycastTarget = false;
            p._zoomLabel = t;
            chip.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                p._faceZoom = !p._faceZoom;
                p.Frame();
            });
            p.UpdateZoomLabel();
            return p;
        }

        void UpdateZoomLabel() { if (_zoomLabel != null) _zoomLabel.text = _faceZoom ? "BODY" : "FACE"; }

        /// <summary>Show a look. Respawns the body when the base changes (male / female), otherwise restyles in place.</summary>
        public bool Apply(AvatarAppearance look)
        {
            if (look == null) return _av != null;
            EnsureStage();
            if (_av == null || look.IsFemale != _female)
            {
                if (_av != null) Destroy(_av.gameObject);
                _av = OverworldAvatar3d.AttachWorld(_pivot, look, Layer);
                _female = look.IsFemale;
                if (_av == null) { _img.enabled = false; return false; }
                _av.Speed = 0f;
                _av.IgnoreBodyHide = true;   // the customizer is never a duel; show the body even if a hide reason lingers
                _av.SetPresence(1f);
            }
            else _av.Apply(look);
            _headY = 1.6f * look.HeightScale * (_female ? 0.95f : 1f);
            _img.enabled = true;
            Frame();
            return true;
        }

        void EnsureStage()
        {
            if (_stage != null) return;
            _stage = new GameObject("AvatarPreviewStage");
            _stage.transform.position = StagePos;
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(_stage.transform, false);
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            var camGo = new GameObject("PreviewCam", typeof(Camera));
            camGo.transform.SetParent(_stage.transform, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = 1 << Layer;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 20f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            // body faces +Z: stand in front of it looking back along -Z
            camGo.transform.localPosition = new Vector3(0f, 1f, 6f);
            camGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            EnsureTexture();
        }

        void EnsureTexture()
        {
            if (_cam == null) return;
            var rect = ((RectTransform)transform).rect;
            var w = Mathf.Clamp(Mathf.RoundToInt(rect.width * 1.5f), 128, 1600);
            var h = Mathf.Clamp(Mathf.RoundToInt(rect.height * 1.5f), 128, 1600);
            if (_rt != null && _rt.width == w && _rt.height == h) return;
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "AvatarPreviewRT", antiAliasing = 4 };
            _cam.targetTexture = _rt;
            _img.texture = _rt;
            Frame();
        }

        void Frame()
        {
            UpdateZoomLabel();
            if (_cam == null) return;
            _cam.aspect = _rt != null ? (float)_rt.width / _rt.height : 0.75f;
            var fitH = _faceZoom ? 0.62f : FullColumn;
            var cy = _faceZoom ? _headY : FullColumn * 0.5f - 0.02f;
            // keep the column inside a narrow viewport too
            var size = fitH * 0.5f;
            if (_cam.aspect < 0.5f) size = Mathf.Max(size, (_faceZoom ? 0.36f : 0.9f) / _cam.aspect * 0.5f);
            _cam.orthographicSize = size;
            var p = _cam.transform.localPosition; p.y = cy; _cam.transform.localPosition = p;
        }

        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) EnsureTexture(); }

        public void OnPointerDown(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            _yaw -= e.delta.x * 0.45f;
            if (_pivot != null) _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        void OnEnable() { if (_stage != null) _stage.SetActive(true); }
        void OnDisable() { if (_stage != null) _stage.SetActive(false); }

        void OnDestroy()
        {
            if (_stage != null) Destroy(_stage);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
