using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Soft UI SFX + BGM from StreamingAssets/WRLDZ/Audio.
    /// Boot / title: <c>bgm_scarab_under_stone.mp3</c> ("Scarab Under Stone").
    /// Overworld: <c>bgm_alternate_under_stone.mp3</c> ("Alternate Under Stone").
    /// Duel: <c>bgm_aetherborn.mp3</c> ("Aetherborn").
    /// </summary>
    public class WrldzAudio : MonoBehaviour
    {
        public enum BgmTrack
        {
            None = 0,
            /// <summary>Splash / title / boot cascade — Scarab Under Stone.</summary>
            Title = 1,
            /// <summary>Overworld / hub map — Alternate Under Stone.</summary>
            Overworld = 2,
            /// <summary>Duel — Aetherborn.</summary>
            Ambient = 3
        }

        static WrldzAudio _instance;
        AudioSource _sfx;
        AudioSource _bgm;
        AudioSource _lpTickSrc;
        AudioClip _click, _select, _confirm, _millenniumEye;
        AudioClip _titleClip;
        AudioClip _overworldClip;
        AudioClip _ambientClip;
        AudioClip _lpTickLoop, _lpLock, _duelBegin;
        AudioClip _cardDraw, _cardPlay, _cardSet, _cardTap, _cardSlide, _holoProject;
        int _lpTickRef;
        bool _duelBeginPending;
        float _duelBeginStart = -1f;
        float _duelBeginDur;
        BgmTrack _currentTrack = BgmTrack.None;
        bool _loading;
        bool _bgmWanted = true;
        float _bgmVolume = 0.22f;

        public static WrldzAudio Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("WRLDZ_Audio");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WrldzAudio>();
            _instance.Bootstrap();
            return _instance;
        }

        void Bootstrap()
        {
            // Boot scene often has no camera listener — add one so title BGM is audible
            if (Object.FindAnyObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _sfx.volume = 0.45f;

            _bgm = gameObject.AddComponent<AudioSource>();
            _bgm.playOnAwake = false;
            _bgm.spatialBlend = 0f;
            _bgm.loop = true;
            _bgm.volume = _bgmVolume;

            _lpTickSrc = gameObject.AddComponent<AudioSource>();
            _lpTickSrc.playOnAwake = false;
            _lpTickSrc.spatialBlend = 0f;
            _lpTickSrc.loop = true;
            _lpTickSrc.volume = 0.55f;

            // Prefer Resources (already imported by Unity as AudioClip)
            try
            {
                _click = Resources.Load<AudioClip>("WRLDZ/Audio/click");
                _select = Resources.Load<AudioClip>("WRLDZ/Audio/select");
                _confirm = Resources.Load<AudioClip>("WRLDZ/Audio/confirm");
            }
            catch { /* optional */ }

            StartCoroutine(LoadStreaming());
        }

        IEnumerator LoadStreaming()
        {
            if (_loading) yield break;
            _loading = true;
            var root = Path.Combine(Application.streamingAssetsPath, "WRLDZ", "Audio");

            // Soft UI overrides if present
            yield return LoadClip(Path.Combine(root, "ui_click.ogg"), c => { if (c != null) _click = c; });
            yield return LoadClip(Path.Combine(root, "ui_select.ogg"), c => { if (c != null) _select = c; });
            yield return LoadClip(Path.Combine(root, "ui_confirm.ogg"), c => { if (c != null) _confirm = c; });
            // Overworld Millennium Eye menu orb (Pokéball-style open)
            yield return LoadClip(Path.Combine(root, "ui_millennium_eye.ogg"), c =>
            {
                if (c != null) _millenniumEye = c;
            });
            if (_millenniumEye == null)
                yield return LoadClip(Path.Combine(root, "ui_millennium_eye.wav"), c =>
                {
                    if (c != null) _millenniumEye = c;
                });

            // Boot / title — Scarab Under Stone (Pharaoh's Shadow as fallback)
            yield return LoadClip(Path.Combine(root, "bgm_scarab_under_stone.mp3"), c =>
            {
                _titleClip = c;
                if (c != null)
                    Debug.Log("[WRLDZ Audio] Title BGM loaded — Scarab Under Stone");
            });
            if (_titleClip == null)
                yield return LoadClip(Path.Combine(root, "bgm_pharaohs_shadow.mp3"), c =>
                {
                    _titleClip = c;
                    if (c != null)
                        Debug.Log("[WRLDZ Audio] Title BGM fallback — Pharaoh's Shadow");
                    else
                        Debug.LogWarning("[WRLDZ Audio] Title BGM missing: bgm_scarab_under_stone.mp3");
                });

            // Overworld — Alternate Under Stone
            yield return LoadClip(Path.Combine(root, "bgm_alternate_under_stone.mp3"), c =>
            {
                _overworldClip = c;
                if (c != null)
                    Debug.Log("[WRLDZ Audio] Overworld BGM loaded — Alternate Under Stone");
            });

            // Duel — Aetherborn (ambient loop as fallback)
            yield return LoadClip(Path.Combine(root, "bgm_aetherborn.mp3"), c =>
            {
                _ambientClip = c;
                if (c != null)
                    Debug.Log("[WRLDZ Audio] Duel BGM loaded — Aetherborn");
            });
            if (_ambientClip == null)
                yield return LoadClip(Path.Combine(root, "bgm_loop_ambient.ogg"), c =>
                {
                    _ambientClip = c;
                });

            yield return LoadClip(Path.Combine(root, "sfx_lp_tick_loop.ogg"), c =>
            {
                _lpTickLoop = c;
            });
            yield return LoadClip(Path.Combine(root, "sfx_lp_lock.ogg"), c =>
            {
                _lpLock = c;
            });
            yield return LoadClip(Path.Combine(root, "sfx_duel_begin.mp3"), c =>
            {
                _duelBegin = c;
                if (c != null)
                    Debug.Log("[WRLDZ Audio] Duel begin sting loaded");
            });
            yield return LoadClip(Path.Combine(root, "sfx_card_draw.ogg"), c => _cardDraw = c);
            yield return LoadClip(Path.Combine(root, "sfx_card_play.ogg"), c => _cardPlay = c);
            yield return LoadClip(Path.Combine(root, "sfx_card_set.ogg"), c => _cardSet = c);
            yield return LoadClip(Path.Combine(root, "sfx_card_tap.ogg"), c => _cardTap = c);
            yield return LoadClip(Path.Combine(root, "sfx_card_slide.ogg"), c => _cardSlide = c);
            yield return LoadClip(Path.Combine(root, "sfx_holo_project.ogg"), c =>
            {
                _holoProject = c;
                if (c != null)
                    Debug.Log("[WRLDZ Audio] Hologram projection sting loaded");
            });

            // If something already requested title BGM before load finished, start it now
            if (_bgmWanted && _currentTrack != BgmTrack.None && !_bgm.isPlaying)
                ApplyTrack(_currentTrack, forceRestart: true);
            else if (_bgmWanted && _currentTrack == BgmTrack.None && _titleClip != null)
            {
                // Default boot path: start title theme as soon as it is ready
                ApplyTrack(BgmTrack.Title, forceRestart: true);
            }

            if (_duelBeginPending && (_holoProject != null || _duelBegin != null))
            {
                _duelBeginPending = false;
                FireDuelBegin();
            }

            _loading = false;
        }

        IEnumerator LoadClip(string path, System.Action<AudioClip> onDone)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                onDone?.Invoke(null);
                yield break;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var type = ext switch
            {
                ".mp3" => AudioType.MPEG,
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                _ => AudioType.UNKNOWN
            };

            // Editor / desktop: file:// URL. Android StreamingAssets is a jar URL already.
            var url = path;
            if (!path.Contains("://"))
                url = "file://" + path;

            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                var handler = (DownloadHandlerAudioClip)req.downloadHandler;
                handler.streamAudio = false;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null)
                        clip.name = Path.GetFileNameWithoutExtension(path);
                    onDone?.Invoke(clip);
                    yield break;
                }

                Debug.LogWarning($"[WRLDZ Audio] Failed {Path.GetFileName(path)}: {req.error}");
            }

            onDone?.Invoke(null);
        }

        public static void PlayClick() => Ensure().PlayOne(_instance._click ?? _instance._select, 0.4f);
        public static void PlaySelect() => Ensure().PlayOne(_instance._select ?? _instance._click, 0.35f);
        public static void PlayConfirm() => Ensure().PlayOne(_instance._confirm ?? _instance._click, 0.45f);

        /// <summary>Millennium Eye overworld menu orb — mystical open chime.</summary>
        public static void PlayMillenniumEye()
        {
            var a = Ensure();
            a.PlayOne(a._millenniumEye ?? a._confirm ?? a._select, 0.55f);
        }

        /// <summary>Start the looping point-drop tick (ref-counted for dual LP rolls).</summary>
        public static void BeginLpTick()
        {
            var a = Ensure();
            a._lpTickRef++;
            if (a._lpTickRef != 1 || a._lpTickSrc == null) return;
            if (a._lpTickLoop == null) return;
            a._lpTickSrc.clip = a._lpTickLoop;
            a._lpTickSrc.loop = true;
            a._lpTickSrc.volume = 0.55f;
            a._lpTickSrc.Play();
        }

        /// <summary>Stop the tick loop when the last rolling counter lands; play the lock ding.</summary>
        public static void EndLpTick()
        {
            var a = Ensure();
            a._lpTickRef = Mathf.Max(0, a._lpTickRef - 1);
            if (a._lpTickRef > 0 || a._lpTickSrc == null) return;
            a._lpTickSrc.Stop();
            a.PlayOne(a._lpLock ?? a._confirm, 0.62f);
        }

        /// <summary>Paper-flutter draw from the deck well.</summary>
        public static void PlayCardDraw() => Ensure().PlayOne(
            _instance._cardDraw ?? _instance._cardSlide ?? _instance._select, 0.72f);

        /// <summary>Short snap when a player taps / selects a card.</summary>
        public static void PlayCardTap() => Ensure().PlayOne(
            _instance._cardTap ?? _instance._select ?? _instance._click, 0.58f);

        /// <summary>Slide / lift when dragging a card from the hand.</summary>
        public static void PlayCardSlide() => Ensure().PlayOne(
            _instance._cardSlide ?? _instance._cardTap ?? _instance._select, 0.55f);

        /// <summary>Face-up play / summon onto a zone.</summary>
        public static void PlayCardPlay() => Ensure().PlayOne(
            _instance._cardPlay ?? _instance._confirm, 0.70f);

        /// <summary>Face-down set onto a zone.</summary>
        public static void PlayCardSet() => Ensure().PlayOne(
            _instance._cardSet ?? _instance._cardPlay ?? _instance._confirm, 0.66f);

        /// <summary>
        /// Hologram-projection sting (end of the DM SFX pack), then the legacy
        /// disk-begin clip if the holo file is missing. Starts <b>before</b> LP ticks.
        /// </summary>
        public static void PlayDuelBegin()
        {
            var a = Ensure();
            if (a._holoProject != null || a._duelBegin != null)
            {
                a._duelBeginPending = false;
                a.FireDuelBegin();
                return;
            }

            a._duelBeginPending = true;
        }

        void FireDuelBegin()
        {
            var holo = _holoProject;
            if (holo != null)
            {
                PlayOne(holo, 0.86f);
                _duelBeginStart = Time.unscaledTime;
                _duelBeginDur = holo.length;
                return;
            }

            if (_duelBegin != null)
            {
                PlayOne(_duelBegin, 0.78f);
                _duelBeginStart = Time.unscaledTime;
                _duelBeginDur = _duelBegin.length;
            }
        }

        /// <summary>
        /// Seconds until the hologram sting has peaked so LP ticks may start.
        /// Peak of the packed clip sits ~81% in; LP rolls after that.
        /// </summary>
        public static float SecondsUntilLpMayTick()
        {
            if (_instance == null) return 0f;
            if (_instance._duelBeginPending && _instance._loading) return 0.4f;
            if (_instance._duelBeginDur <= 0.05f) return 0f;
            var peakAt = _instance._duelBeginStart + _instance._duelBeginDur * 0.82f;
            return Mathf.Max(0f, peakAt - Time.unscaledTime);
        }

        /// <summary>
        /// 0…1 progress through the duel-start hologram sting, plus RMS envelope
        /// for the arena border flash. False when no sting is playing.
        /// </summary>
        public static bool TryDuelBeginGlow(out float t01, out float envelope)
        {
            t01 = 0f;
            envelope = 0f;
            if (_instance == null || _instance._duelBeginDur <= 0.05f) return false;
            var e = Time.unscaledTime - _instance._duelBeginStart;
            if (e < -0.02f || e > _instance._duelBeginDur + 0.12f) return false;
            t01 = Mathf.Clamp01(e / _instance._duelBeginDur);
            envelope = SampleHoloEnvelope(t01);
            return e <= _instance._duelBeginDur;
        }

        /// <summary>Normalized RMS of sfx_holo_project.ogg (48 bins, peak at ~0.81).</summary>
        static readonly float[] HoloEnvelope =
        {
            0.243f, 0.567f, 0.750f, 0.782f, 0.718f, 0.692f, 0.607f, 0.689f,
            0.653f, 0.568f, 0.661f, 0.655f, 0.694f, 0.550f, 0.441f, 0.430f,
            0.386f, 0.426f, 0.450f, 0.415f, 0.359f, 0.376f, 0.436f, 0.496f,
            0.470f, 0.420f, 0.450f, 0.393f, 0.438f, 0.500f, 0.453f, 0.432f,
            0.467f, 0.548f, 0.570f, 0.571f, 0.486f, 0.876f, 0.966f, 1.000f,
            0.921f, 0.580f, 0.404f, 0.307f, 0.261f, 0.222f, 0.199f, 0.100f
        };

        static float SampleHoloEnvelope(float t01)
        {
            var n = HoloEnvelope.Length;
            var x = Mathf.Clamp01(t01) * (n - 1);
            var i = Mathf.FloorToInt(x);
            var j = Mathf.Min(n - 1, i + 1);
            return Mathf.Lerp(HoloEnvelope[i], HoloEnvelope[j], x - i);
        }

        /// <summary>Splash / title / boot cascade — Scarab Under Stone.</summary>
        public static void PlayTitleBgm() => Ensure().ApplyTrack(BgmTrack.Title, forceRestart: false);

        /// <summary>Battle City map / hub — Alternate Under Stone.</summary>
        public static void PlayOverworldBgm() => Ensure().ApplyTrack(BgmTrack.Overworld, forceRestart: false);

        /// <summary>Overworld bed (alias). Duel uses <see cref="PlayDuelBgm"/>.</summary>
        public static void PlayAmbientBgm() => PlayOverworldBgm();

        /// <summary>In-duel theme — Aetherborn.</summary>
        public static void PlayDuelBgm() => Ensure().ApplyTrack(BgmTrack.Ambient, forceRestart: false);

        public static void PlayBgm(BgmTrack track) => Ensure().ApplyTrack(track, forceRestart: false);

        public static void SetBgmEnabled(bool on)
        {
            var a = Ensure();
            a._bgmWanted = on;
            if (!on)
            {
                a._bgm.Stop();
                return;
            }

            if (a._currentTrack == BgmTrack.None)
                a.ApplyTrack(BgmTrack.Title, forceRestart: true);
            else if (!a._bgm.isPlaying)
                a.ApplyTrack(a._currentTrack, forceRestart: true);
        }

        public static void SetBgmVolume(float v)
        {
            var a = Ensure();
            a._bgmVolume = Mathf.Clamp01(v);
            if (a._bgm != null)
                a._bgm.volume = a._bgmVolume;
        }

        public static void StopBgm()
        {
            var a = Ensure();
            a._bgmWanted = false;
            a._bgm.Stop();
            a._currentTrack = BgmTrack.None;
        }

        void ApplyTrack(BgmTrack track, bool forceRestart)
        {
            _currentTrack = track;
            if (!_bgmWanted || _bgm == null)
                return;

            AudioClip clip = track switch
            {
                BgmTrack.Title => _titleClip ?? _overworldClip ?? _ambientClip,
                BgmTrack.Overworld => _overworldClip ?? _ambientClip ?? _titleClip,
                BgmTrack.Ambient => _ambientClip ?? _overworldClip ?? _titleClip,
                _ => null
            };

            if (clip == null)
            {
                // Clips still loading — will resume in LoadStreaming
                return;
            }

            if (!forceRestart && _bgm.isPlaying && _bgm.clip == clip)
                return;

            _bgm.clip = clip;
            _bgm.loop = true;
            _bgm.volume = _bgmVolume;
            _bgm.Play();
            Debug.Log($"[WRLDZ Audio] BGM → {track} ({clip.name})");
        }

        void PlayOne(AudioClip clip, float vol)
        {
            if (clip == null || _sfx == null) return;
            _sfx.PlayOneShot(clip, vol);
        }
    }
}
