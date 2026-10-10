using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Anime / Tag Force duel cut-in for the opponent's disk deploy: a slanted banner slides across the upper screen with
    /// a live close-up of the AI duelist striking its deploy pose (a second camera on the stage layers, rendered into a
    /// RenderTexture), the duelist's name and "DUEL!". The street stage puts the opponent metres away, and on a phone or a
    /// headset the view cannot be pushed in, so the pose is shown here. Destroys itself when done.
    /// </summary>
    public sealed class DeployCutIn : MonoBehaviour
    {
        const float SlideIn = 0.22f, SlideOut = 0.24f;

        ArOpponentDuelist _duelist;
        Camera _cam;
        RenderTexture _rt;
        RectTransform _band;
        CanvasGroup _group;
        float _t, _length;

        /// <summary>Show the cut-in for <paramref name="seconds"/> (slides included).</summary>
        public static DeployCutIn Show(ArOpponentDuelist duelist, Camera stageCam, float seconds, Color accent)
        {
            if (duelist == null) return null;
            var go = new GameObject("DeployCutIn");
            var c = go.AddComponent<DeployCutIn>();
            c._duelist = duelist;
            c._length = Mathf.Max(0.8f, seconds);
            c.Build(stageCam, accent);
            return c;
        }

        void Build(Camera stageCam, Color accent)
        {
            _rt = new RenderTexture(1024, 440, 24) { name = "DeployCutIn" };
            var camGo = new GameObject("CutInCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            if (stageCam != null)
            {
                _cam.cullingMask = stageCam.cullingMask;
                _cam.nearClipPlane = stageCam.nearClipPlane;
                _cam.farClipPlane = stageCam.farClipPlane;
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.04f, 0.09f, 1f);
            _cam.fieldOfView = 30f;
            _cam.targetTexture = _rt;
            _cam.depth = -50;

            var canvasGo = new GameObject("CutInCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 480;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _group = canvasGo.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // the band: accent rim, the live shot, a dark name plate
            _band = NewRect("Band", canvasGo.transform);
            _band.anchorMin = _band.anchorMax = new Vector2(0.5f, 0.72f);
            _band.sizeDelta = new Vector2(1500f, 360f);
            _band.localRotation = Quaternion.Euler(0f, 0f, 4f);
            var rim = _band.gameObject.AddComponent<Image>();
            rim.color = accent;
            rim.raycastTarget = false;

            var shot = NewRect("Shot", _band);
            shot.anchorMin = Vector2.zero; shot.anchorMax = Vector2.one;
            shot.offsetMin = new Vector2(8f, 8f); shot.offsetMax = new Vector2(-8f, -8f);
            var raw = shot.gameObject.AddComponent<RawImage>();
            raw.texture = _rt;
            raw.raycastTarget = false;
            // the RT is 1024x440; the band 1484x344 — crop, never stretch
            var aspectBand = (1500f - 16f) / (360f - 16f);
            var aspectRt = 1024f / 440f;
            raw.uvRect = aspectBand > aspectRt
                ? new Rect(0f, 0.5f - aspectRt / aspectBand * 0.5f, 1f, aspectRt / aspectBand)
                : new Rect(0.5f - aspectBand / aspectRt * 0.5f, 0f, aspectBand / aspectRt, 1f);

            var plate = NewRect("Name", _band);
            plate.anchorMin = new Vector2(0f, 0f); plate.anchorMax = new Vector2(0f, 0f);
            plate.pivot = new Vector2(0f, 0f);
            plate.anchoredPosition = new Vector2(24f, 20f);
            plate.sizeDelta = new Vector2(620f, 70f);
            var plateImg = plate.gameObject.AddComponent<Image>();
            plateImg.color = new Color(0f, 0f, 0f, 0.72f);
            plateImg.raycastTarget = false;
            Label(plate, (_duelist.DisplayName ?? "").ToUpperInvariant(), 40, TextAnchor.MiddleLeft, Color.white, new Vector2(18f, 0f));

            var duel = NewRect("Duel", _band);
            duel.anchorMin = duel.anchorMax = new Vector2(1f, 1f);
            duel.pivot = new Vector2(1f, 1f);
            duel.anchoredPosition = new Vector2(-30f, -16f);
            duel.sizeDelta = new Vector2(420f, 120f);
            Label(duel, "DUEL!", 92, TextAnchor.MiddleRight, Color.Lerp(accent, Color.white, 0.55f), Vector2.zero);

            Place(0f);
            AimCamera();
            raw.enabled = _aimed;   // never show an unplaced camera's view
            _raw = raw;
        }

        RawImage _raw;
        bool _aimed;

        void AimCamera()
        {
            if (!_duelist.TryGetCutInShot(out var eye, out var look, out _)) return;
            // a slow push-in while held
            var push = Mathf.Clamp01(_t / _length) * 0.12f;
            _cam.transform.position = Vector3.Lerp(eye, look, push);
            _cam.transform.LookAt(look);
            _aimed = true;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Label(RectTransform parent, string text, int size, TextAnchor anchor, Color color, Vector2 pad)
        {
            var r = NewRect("Text", parent);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = pad; r.offsetMax = -pad;
            var t = r.gameObject.AddComponent<Text>();
            t.font = UiFoundation.BuiltinFont();
            t.fontSize = size;
            t.fontStyle = FontStyle.BoldAndItalic;
            t.alignment = anchor;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            var o = r.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f);
            o.effectDistance = new Vector2(3f, -3f);
        }

        /// <summary>Start sliding out now (the sequence moves on); no-op once already leaving.</summary>
        public void Close()
        {
            if (_t < _length - SlideOut) _length = Mathf.Max(_t, SlideIn) + SlideOut;
        }

        /// <summary>Slide the band: in from the right, a slow drift while held, out to the left.</summary>
        void Place(float t)
        {
            float x;
            if (t < SlideIn) x = Mathf.Lerp(2100f, 40f, 1f - Mathf.Pow(1f - t / SlideIn, 3f));
            else if (t < _length - SlideOut) x = Mathf.Lerp(40f, -40f, (t - SlideIn) / Mathf.Max(0.01f, _length - SlideIn - SlideOut));
            else x = Mathf.Lerp(-40f, -2100f, Mathf.Pow((t - (_length - SlideOut)) / SlideOut, 2f));
            _band.anchoredPosition = new Vector2(x, 0f);
            _group.alpha = 1f;
        }

        void LateUpdate()
        {
            _t += Time.unscaledDeltaTime;
            if (_t >= _length || _duelist == null) { Destroy(gameObject); return; }
            Place(_t);
            AimCamera();
            if (_raw != null && !_raw.enabled && _aimed) _raw.enabled = true;
        }

        void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
