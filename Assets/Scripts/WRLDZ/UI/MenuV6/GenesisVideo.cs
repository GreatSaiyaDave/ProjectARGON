using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The approved Genesis cut (BUILD_PLAN D9): the pre-rendered VP8 clip of title.html (raw 0 → 21.6 s, 30 fps, 649
    /// frames; frame 648 IS raw 21.6 = the live title's first frame) played by a VideoPlayer into a RenderTexture shown on
    /// a RawImage over the title stage. Local-only (D7: it holds ripped pixels), so a clone has no clip and the title
    /// starts live. Play mode only (no RenderTexture in edit mode, R20). Any error, a missing first frame after 6 s, a clip
    /// that stops delivering frames for 3 s, or the clip's end hands over to the live title (<see cref="Failed"/> /
    /// <see cref="Ended"/>): never a blank or stuck screen (R7).
    /// </summary>
    public sealed class GenesisVideo : MonoBehaviour
    {
        public const double Fps = 30, LastFrame = 648;

        VideoPlayer _vp;
        RenderTexture _rt;
        RawImage _img;
        float _t0, _stall;
        bool _shown;
        long _lastFrame = -1;

        public bool Failed { get; private set; }
        public bool Ended { get; private set; }

        /// <summary>The clip's clock in the page's raw seconds (0 until its first frame is up).</summary>
        public double Raw
        {
            get
            {
                if (_vp == null || !_shown) return 0;
                var f = _vp.frame;
                var t = _vp.time;
                // frame-exact when the decoder reports a frame (the page's frames are n / 30)
                return f >= 0 ? System.Math.Max(t, f / Fps) : t;
            }
        }

        public static GenesisVideo Create(RectTransform parent, VideoClip clip, Vector2 stageSize)
        {
            if (parent == null || clip == null || !Application.isPlaying) return null;
            var go = new GameObject("GenesisVideo", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = stageSize;
            var g = go.AddComponent<GenesisVideo>();
            g._img = go.GetComponent<RawImage>();
            g._img.raycastTarget = false;
            g._img.color = new Color(1f, 1f, 1f, 0f);   // shown on its first decoded frame
            var w = (int)clip.width > 0 ? (int)clip.width : Mathf.RoundToInt(stageSize.x);
            var h = (int)clip.height > 0 ? (int)clip.height : Mathf.RoundToInt(stageSize.y);
            g._rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "GenesisVideoRT",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false
            };
            g._rt.Create();
            g._img.texture = g._rt;
            var vp = go.AddComponent<VideoPlayer>();
            g._vp = vp;
            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.waitForFirstFrame = true;
            vp.skipOnDrop = true;
            vp.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            vp.audioOutputMode = VideoAudioOutputMode.None;   // the boot's own bed plays (survey_boot §5.7)
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = g._rt;
            vp.source = VideoSource.VideoClip;
            vp.clip = clip;
            vp.errorReceived += (_, msg) =>
            {
                Debug.LogWarning("[WRLDZ MenuV6] Genesis video: " + msg + " — the title starts live.");
                g.Failed = true;
            };
            vp.loopPointReached += _ => g.Ended = true;
            vp.prepareCompleted += p => p.Play();
            g._t0 = Time.unscaledTime;
            vp.Prepare();
            return g;
        }

        public void Fit(float k) => transform.localScale = new Vector3(k, k, 1f);

        float _fadeT0 = float.NaN, _fadeDur;

        /// <summary>P3: keep the last decoded frame on screen and fade it out over <paramref name="dur"/> s of real time, then
        /// destroy this object (TitleStage's video → live handover: the decoder's shadow loss eases out instead of
        /// stepping). The player is left as it ended (no Stop / Pause: either can blank or rewind the target texture).
        /// False when nothing has been shown yet (the caller then destroys it at once).</summary>
        public bool HoldAndFade(float dur)
        {
            if (!_shown || _img == null || dur <= 0f) return false;
            _fadeT0 = Time.unscaledTime;
            _fadeDur = dur;
            return true;
        }

        void Update()
        {
            if (_vp == null) return;
            if (!float.IsNaN(_fadeT0))
            {
                var k = (float)MenuV6Math.InOut((Time.unscaledTime - _fadeT0) / _fadeDur);
                if (_img != null) _img.color = new Color(1f, 1f, 1f, 1f - k);
                if (k >= 1f) Destroy(gameObject);
                return;
            }

            if (!_shown)
            {
                if (_vp.isPlaying && _vp.frame >= 0)
                {
                    _shown = true;
                    _img.color = Color.white;
                }
                else if (Time.unscaledTime - _t0 > 6f)
                {
                    Debug.LogWarning("[WRLDZ MenuV6] Genesis video: no first frame after 6 s — the title starts live.");
                    Failed = true;
                }

                return;
            }

            if (_vp.frame >= (long)LastFrame || (!_vp.isPlaying && _vp.isPrepared && _vp.frame > 0)) Ended = true;
            // a decoder that stops delivering frames mid-clip (no error, still "playing") hands over too: 3 s without a new
            // frame, counted in capped steps so an app pause (focus loss) is not a stall
            if (_vp.frame != _lastFrame)
            {
                _lastFrame = _vp.frame;
                _stall = 0f;
            }
            else if ((_stall += Mathf.Min(Time.unscaledDeltaTime, .1f)) > 3f)
            {
                Debug.LogWarning("[WRLDZ MenuV6] Genesis video: no new frame for 3 s — the title starts live.");
                Ended = true;
            }
        }

        public void Stop()
        {
            if (_vp != null) _vp.Stop();
        }

        void OnDestroy()
        {
            if (_vp != null) _vp.targetTexture = null;
            if (_img != null) _img.texture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }
    }
}
