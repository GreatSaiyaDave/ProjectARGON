using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// The live 3D duelist of the PROFILE and CUSTOMIZE screens: the same <see cref="OverworldAvatar3d"/> the map uses,
    /// on a gold-ringed basalt plinth on an off-screen stage (layer <see cref="Layer"/>, far below the world), filmed by
    /// its own perspective camera into a RenderTexture shown in this RawImage (transparent background, so the sanctuary
    /// wall behind the UI shows through).
    /// Framing (2026-10-18 AAA pass): the camera frames the body from its live bones and renderer bounds, so it can never
    /// cut the duelist off whatever the height, build or pose; <see cref="SetFocus"/> eases between full body, bust, face,
    /// legs and feet. Drag turns the duelist (with inertia); <see cref="Turn"/> eases a step; the wheel zooms a little.
    /// </summary>
    public sealed class AvatarPreview3d : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        public const int Layer = 27;
        static readonly Vector3 StagePos = new(0f, -5000f, 0f);

        public enum Focus { Full, Bust, Face, Legs, Feet }

        const float Fov = 21f;          // vertical, degrees: a long lens, little perspective distortion on the body
        const float Pitch = 5.5f;       // the camera looks slightly down, so the plinth reads as a disc
        const float RestYaw = -14f;     // a three-quarter turn reads as a figure, not a mugshot

        RawImage _img;
        Transform _plinth;
        RenderTexture _rt;
        Camera _cam;
        GameObject _stage;
        Transform _pivot;
        OverworldAvatar3d _av;
        Animator _anim;
        bool _female;
        Focus _focus = Focus.Full;
        float _zoom = 1f;               // wheel zoom on top of the focus (1 = framed, 0.7 = closer)

        // turn
        float _yaw = RestYaw, _yawVel, _yawTarget = RestYaw;
        bool _dragging, _tweening;

        // framing: measured (low-passed) body landmarks in stage space, and the eased camera state
        bool _measured;
        float _footY, _hipsY, _chestY, _headY, _topY, _width = 0.6f;
        float _camY, _camD, _camYVel, _camDVel, _lookY, _lookYVel;
        bool _snap = true;

        public bool HasBody => _av != null;
        public Focus CurrentFocus => _focus;

        public static AvatarPreview3d Create(Transform parent)
        {
            var go = new GameObject("AvatarPreview3d", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<AvatarPreview3d>();
            p._img = go.GetComponent<RawImage>();
            p._img.color = Color.white;
            p._img.raycastTarget = true;
            p._img.enabled = false;
            return p;
        }

        /// <summary>Move the stage view into another screen's slot (the profile and the customizer share one body).</summary>
        public void AttachTo(RectTransform host)
        {
            if (host == null) return;
            transform.SetParent(host, false);
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            transform.SetAsFirstSibling();
            EnsureTexture();
        }

        /// <summary>Show a look. Respawns the body when the base changes (male / female), otherwise restyles in place.</summary>
        public bool Apply(AvatarAppearance look)
        {
            if (look == null) return _av != null;
            // the player is always the 3D body: a signature row's 2D sprite id or a silhouette never applies here
            if (!string.IsNullOrEmpty(look.characterId) || look.silhouette)
            {
                look = look.Clone();
                look.characterId = "";
                look.silhouette = false;
            }
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
                _anim = _av.GetComponent<Animator>();
                var blob = _av.transform.Find("ShadowBlob");   // the plinth is the ground here
                if (blob != null) blob.gameObject.SetActive(false);
                _measured = false;
                _snap = true;
            }
            else _av.Apply(look);
            _img.enabled = true;
            EnsureTexture();
            return true;
        }

        public void SetFocus(Focus f, bool instant = false)
        {
            if (f != _focus) _zoom = 1f;
            _focus = f;
            if (instant) _snap = true;
        }

        /// <summary>Ease the turntable by <paramref name="degrees"/> (the arrow buttons).</summary>
        public void Turn(float degrees)
        {
            _yawTarget = (_tweening ? _yawTarget : _yaw) + degrees;
            _tweening = true;
            _yawVel = 0f;
        }

        /// <summary>Back to the resting three-quarter view, full body.</summary>
        public void ResetView()
        {
            _yawTarget = RestYaw + Mathf.Round((_yaw - RestYaw) / 360f) * 360f;
            _tweening = true;
            _zoom = 1f;
        }

        // ------------------------------------------------------------------ input

        public void OnBeginDrag(PointerEventData e) { _dragging = true; _tweening = false; }

        public void OnDrag(PointerEventData e)
        {
            var d = -e.delta.x * 0.42f;
            _yaw += d;
            var dt = Mathf.Max(Time.unscaledDeltaTime, 1e-3f);
            _yawVel = Mathf.Lerp(_yawVel, d / dt, 0.5f);
        }

        public void OnEndDrag(PointerEventData e) { _dragging = false; }

        public void OnScroll(PointerEventData e)
        {
            _zoom = Mathf.Clamp(_zoom - e.scrollDelta.y * 0.06f, 0.62f, 1.12f);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.clickCount >= 2) ResetView();
        }

        // ------------------------------------------------------------------ stage

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
            _cam.orthographic = false;
            _cam.fieldOfView = Fov;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = 1 << Layer;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 60f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.depth = -50;
            camGo.transform.localPosition = new Vector3(0f, 1.0f, 7f);
            camGo.transform.localRotation = Quaternion.Euler(Pitch, 180f, 0f);

            BuildPlinth();
            EnsureTexture();
        }

        /// <summary>
        /// The plinth: a basalt disc ringed in Millennium gold with twelve notches and a lapis inner line (style guide §2
        /// colours), drawn flat under the feet by a SpriteRenderer (built-in sprite shader: nothing for a build to strip).
        /// </summary>
        void BuildPlinth()
        {
            const int N = 512;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "CzPlinth", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            var gold = CzKit.Gold; var goldHi = CzKit.GoldHi; var goldLo = CzKit.GoldLo;
            var basalt = new Color(0.20f, 0.19f, 0.23f); var basaltDk = new Color(0.085f, 0.075f, 0.10f);
            var lapis = new Color(0.165f, 0.31f, 0.66f);
            var ink = Color.black;
            for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                var x = (i + 0.5f) / N * 2f - 1f; var y = (j + 0.5f) / N * 2f - 1f;
                var r = Mathf.Sqrt(x * x + y * y);
                var a = Mathf.Clamp01((1f - r) * N * 0.5f);
                if (a <= 0f) { px[j * N + i] = Color.clear; continue; }
                Color c;
                var ang = Mathf.Atan2(y, x);
                // the key light comes from the upper left: the far rim (y > 0 in the texture) is lit
                var lit = Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(ang + 0.75f));
                if (r > 0.955f) c = r > 0.985f ? ink : Color.Lerp(goldLo, goldHi, lit > 0.62f ? 1f : lit > 0.3f ? 0.5f : 0f);
                else if (r > 0.94f) c = ink;
                else if (r > 0.80f)
                {
                    c = Color.Lerp(basaltDk, basalt, 0.55f);
                    var notch = Mathf.Abs(Mathf.Repeat(ang / (Mathf.PI * 2f) * 12f + 0.5f, 1f) - 0.5f);
                    if (notch < 0.035f && r > 0.83f && r < 0.91f) c = gold;
                }
                else if (r > 0.785f) c = ink;
                else if (r > 0.765f) c = lapis;
                else if (r > 0.75f) c = ink;
                else
                {
                    // the disc face: two flat cel bands (no gradient inside a band), the lit band upper-left
                    var face = Vector2.Dot(new Vector2(x, y), new Vector2(-0.6f, 0.8f));
                    c = face > 0.25f ? basalt : Color.Lerp(basaltDk, basalt, 0.45f);
                }
                c.a = a;
                px[j * N + i] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N / 1.25f);   // 1.25 m across
            sprite.hideFlags = HideFlags.DontSave;
            var go = new GameObject("Plinth", typeof(SpriteRenderer));
            _plinth = go.transform;
            go.transform.SetParent(_stage.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.layer = Layer;
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -20;   // under the body's own contact shadow
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }

        void EnsureTexture()
        {
            if (_cam == null || _img == null) return;
            var rect = ((RectTransform)transform).rect;
            var canvas = GetComponentInParent<Canvas>();
            var scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            var w = Mathf.Clamp(Mathf.RoundToInt(rect.width * scale), 64, 2048);
            var h = Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 64, 2048);
            if (rect.width < 2f || rect.height < 2f) return;
            if (_rt != null && _rt.width == w && _rt.height == h) return;
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "AvatarPreviewRT", antiAliasing = 4 };
            _cam.targetTexture = _rt;
            _cam.aspect = (float)w / h;
            _img.texture = _rt;
        }

        // ------------------------------------------------------------------ per frame: turn + frame

        void LateUpdate()
        {
            if (_stage == null) return;
            EnsureTexture();
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            // turntable
            if (_tweening && !_dragging)
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, _yawTarget, ref _yawVel, 0.32f, Mathf.Infinity, dt);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _yawTarget)) < 0.05f) { _yaw = _yawTarget; _yawVel = 0f; _tweening = false; }
            }
            else if (!_dragging && Mathf.Abs(_yawVel) > 0.5f)
            {
                _yaw += _yawVel * dt;
                _yawVel *= Mathf.Exp(-dt * 4.5f);
            }
            if (_pivot != null) _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            Measure(dt);
            Frame(dt);
        }

        Vector3 StageLocal(Vector3 world) => _stage.transform.InverseTransformPoint(world);

        float BoneY(HumanBodyBones b, float fallback)
        {
            if (_anim == null || !_anim.isHuman) return fallback;
            var t = _anim.GetBoneTransform(b);
            return t != null ? StageLocal(t.position).y : fallback;
        }

        /// <summary>Landmarks of the live body: feet, hips, chest, head bones and the renderers' top (hair, hats).</summary>
        void Measure(float dt)
        {
            if (_av == null) return;
            var h = _av.transform.lossyScale.y;
            var foot = Mathf.Min(BoneY(HumanBodyBones.LeftFoot, 0.08f), BoneY(HumanBodyBones.RightFoot, 0.08f));
            var toes = Mathf.Min(BoneY(HumanBodyBones.LeftToes, foot), BoneY(HumanBodyBones.RightToes, foot));
            var footY = Mathf.Min(foot, toes) - 0.07f * h;
            var hips = BoneY(HumanBodyBones.Hips, 0.95f * h);
            var chest = BoneY(HumanBodyBones.UpperChest, BoneY(HumanBodyBones.Chest, 1.3f * h));
            var head = BoneY(HumanBodyBones.Head, 1.55f * h);
            // the top of everything worn on the head (hair spikes, hats) from the renderers' bounds
            var top = head + 0.22f * h;
            var minX = float.MaxValue; var maxX = float.MinValue;
            foreach (var r in _av.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || r.gameObject.name == "ShadowBlob") continue;
                var b = r.bounds;
                var lo = StageLocal(b.min); var hi = StageLocal(b.max);
                top = Mathf.Max(top, Mathf.Min(hi.y, head + 0.65f * h));
                minX = Mathf.Min(minX, Mathf.Min(lo.x, hi.x)); maxX = Mathf.Max(maxX, Mathf.Max(lo.x, hi.x));
            }
            var width = minX < maxX ? Mathf.Clamp(maxX - minX, 0.3f, 2.2f) : 0.6f;
            // the plinth under the soles: at the stage floor, or under the feet if a pose ever lowers them
            if (_plinth != null) _plinth.localPosition = new Vector3(0f, Mathf.Min(0.002f, toes - 0.03f * h), 0f);
            if (!_measured || _snap)
            {
                _footY = footY; _hipsY = hips; _chestY = chest; _headY = head; _topY = top; _width = width;
                _measured = true;
                return;
            }
            // low-pass: the idle sways a few millimetres; the camera must not
            var a = 1f - Mathf.Exp(-dt / 0.35f);
            _footY = Mathf.Lerp(_footY, footY, a); _hipsY = Mathf.Lerp(_hipsY, hips, a); _chestY = Mathf.Lerp(_chestY, chest, a);
            _headY = Mathf.Lerp(_headY, head, a); _topY = Mathf.Lerp(_topY, top, a); _width = Mathf.Lerp(_width, width, a);
        }

        /// <summary>The span of the body each focus frames (stage-space y0..y1) and the width it must fit.</summary>
        void Span(out float y0, out float y1, out float w)
        {
            var body = Mathf.Max(0.4f, _headY - _footY);
            switch (_focus)
            {
                case Focus.Face:
                    y0 = _headY - 0.10f * body; y1 = Mathf.Max(_topY, _headY + 0.14f * body) + 0.03f * body; w = 0.34f * body; break;
                case Focus.Bust:
                    y0 = _chestY - 0.20f * body; y1 = _topY + 0.04f * body; w = 0.62f * body; break;
                case Focus.Legs:
                    y0 = _footY - 0.04f * body; y1 = _hipsY + 0.14f * body; w = 0.55f * body; break;
                case Focus.Feet:
                    y0 = _footY - 0.05f * body; y1 = _footY + 0.30f * body; w = 0.45f * body; break;
                default:
                    y0 = _footY - 0.05f * body; y1 = _topY + 0.05f * body; w = Mathf.Max(_width, 0.5f * body); break;
            }
        }

        void Frame(float dt)
        {
            if (_cam == null) return;
            if (!_measured)
            {
                _cam.transform.localPosition = new Vector3(0f, 1.0f, 7f);
                return;
            }
            Span(out var y0, out var y1, out var w);
            var span = (y1 - y0) * _zoom;
            var aspect = Mathf.Max(0.2f, _cam.aspect);
            var half = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            // fit the span vertically and the body's width horizontally (with a margin either side)
            var dV = span * 0.5f / half;
            var dH = w * 0.5f * 1.15f / (half * aspect);
            var d = Mathf.Max(dV, dH) + 0.25f;
            var lookY = (y0 + y1) * 0.5f;
            if (_focus == Focus.Full) lookY = y0 + (y1 - y0) * 0.5f;
            if (_snap)
            {
                _camD = d; _camY = lookY; _lookY = lookY; _camDVel = _camYVel = _lookYVel = 0f;
                _snap = false;
            }
            else
            {
                _camD = Mathf.SmoothDamp(_camD, d, ref _camDVel, 0.30f, Mathf.Infinity, dt);
                _lookY = Mathf.SmoothDamp(_lookY, lookY, ref _lookYVel, 0.30f, Mathf.Infinity, dt);
            }
            var pitch = Pitch * Mathf.Deg2Rad;
            var pos = new Vector3(0f, _lookY + Mathf.Sin(pitch) * _camD, Mathf.Cos(pitch) * _camD);
            _cam.transform.localPosition = pos;
            _cam.transform.localRotation = Quaternion.Euler(Pitch, 180f, 0f);
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
