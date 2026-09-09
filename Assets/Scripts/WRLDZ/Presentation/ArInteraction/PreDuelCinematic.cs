using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Pre-duel anime sequence:
    /// 1) Disk blade deploy
    /// 2) Deck shuffle VFX
    /// 3) Player reaches DECK zone (or taps DRAW HAND / Space) → opening draw
    /// 4) Cards fly deck → hand, engine completes draw, duel begins
    /// </summary>
    public class PreDuelCinematic : MonoBehaviour
    {
        public enum Phase
        {
            Idle = 0,
            Deploying,
            Shuffling,
            AwaitDrawGesture,
            Drawing,
            Complete
        }

        public Phase State { get; private set; } = Phase.Idle;
        public bool IsRunning => State is Phase.Deploying or Phase.Shuffling or Phase.AwaitDrawGesture or Phase.Drawing;
        public bool BlocksGameplay => IsRunning;

        DuelEngine _engine;
        ArDuelInteractionSystem _ix;
        Camera _stageCam;
        RectTransform _viewport;
        Action<string> _onHint;
        Action _onComplete;
        Coroutine _co;
        Transform _handProxy;
        bool _drawRequested;
        float _nearDeckSeconds;
        float _awaitStartedAt;
        GameObject _drawCanvasGo;
        Button _drawFallbackBtn;

        const float AutoDrawFailsafeSeconds = 12f;
        const float DeckHoldSeconds = 0.2f;

        public static PreDuelCinematic Ensure(GameObject host)
        {
            if (host == null) return null;
            return host.GetComponent<PreDuelCinematic>() ?? host.AddComponent<PreDuelCinematic>();
        }

        /// <summary>UI / keyboard / auto: request opening draw while awaiting gesture.</summary>
        public void RequestDraw()
        {
            if (State == Phase.AwaitDrawGesture)
            {
                _drawRequested = true;
                Debug.Log("[WRLDZ PreDuel] Draw requested");
            }
        }

        public void Begin(
            DuelEngine engine,
            ArDuelInteractionSystem interaction,
            Camera stageCam,
            RectTransform arViewport,
            Action<string> onHint,
            Action onComplete,
            Transform uiParentForFallback = null)
        {
            _engine = engine;
            _ix = interaction;
            _stageCam = stageCam;
            _viewport = arViewport;
            _onHint = onHint;
            _onComplete = onComplete;
            _drawRequested = false;
            _nearDeckSeconds = 0f;

            if (_co != null) StopCoroutine(_co);
            if (engine == null || !engine.OpeningSequenceActive)
            {
                State = Phase.Complete;
                onComplete?.Invoke();
                return;
            }

            // Prefer interaction stage camera if available
            if (_ix != null && _stageCam == null)
            {
                // leave null — we'll still allow button/space
            }

            BuildHandProxy();
            BuildFallbackDrawButton();
            _co = StartCoroutine(RunSequence());
        }

        IEnumerator RunSequence()
        {
            // ── 1) Disk deploy ──
            State = Phase.Deploying;
            Hint("DUEL DISK — DEPLOY");
            Debug.Log(
                $"[WRLDZ PreDuel] Start · ix={_ix != null} disk={_ix?.PlayerDisk != null} " +
                $"deck={_ix?.PlayerDisk?.MainDeckZone != null}");
            _ix?.SetHandVolumeVisible(false);
            _ix?.PlayerDisk?.SetDeckHighlight(false);
            _ix?.PlayerDisk?.EnsureDeckStackVisual();

            if (_ix != null)
            {
                _ix.DeployForDuelOnly(0.05f);
                _ix.OppDisk?.DeployForDuel(0.28f);
            }
            else
                Debug.LogWarning("[WRLDZ PreDuel] No interaction system — deploy/shuffle will be skipped");

            yield return new WaitForSecondsRealtime(1.05f);

            // Safety: Editor sparse ticks only. Do not skip the anime swing in play.
            if (_ix?.PlayerDisk != null && _ix.PlayerDisk.Fx.BladeOpen < 0.85f)
            {
                _ix.PlayerDisk.Fx.SnapDeployed();
                _ix.PlayerDisk.EnsureDeckStackVisual();
            }

            if (_ix?.OppDisk != null && _ix.OppDisk.Fx.BladeOpen < 0.85f)
                _ix.OppDisk.Fx.SnapDeployed();

            yield return new WaitForSecondsRealtime(0.25f);

            // ── 2) Shuffle (must be obvious) ──
            State = Phase.Shuffling;
            Hint("SHUFFLING DECK…");
            Debug.Log("[WRLDZ PreDuel] Shuffle phase");
            if (_ix?.PlayerDisk != null)
            {
                _ix.PlayerDisk.PlayFx(DiskFxEvent.BladeDeploy, 0.35f);
                _ix.PlayerDisk.EnsureDeckStackVisual();
                // Run on the disk host so a destroyed PreDuel UI never cuts the riffle short
                yield return _ix.PlayerDisk.PlayShuffleRoutine(1.75f);
            }
            else
            {
                Debug.LogWarning("[WRLDZ PreDuel] Player disk missing — timed shuffle placeholder");
                yield return new WaitForSecondsRealtime(0.8f);
            }

            if (_engine != null && _engine.OpeningSequenceActive)
                _engine.MarkAwaitingOpeningDraw();

            // ── 3) Await draw gesture ──
            State = Phase.AwaitDrawGesture;
            _awaitStartedAt = Time.unscaledTime;
            _drawRequested = false;
            _nearDeckSeconds = 0f;
            Hint("TAP DRAW HAND  ·  Space  ·  or touch DECK");
            _ix?.PlayerDisk?.SetDeckHighlight(true);
            if (_handProxy != null) _handProxy.gameObject.SetActive(true);
            if (_drawCanvasGo != null) _drawCanvasGo.SetActive(true);
            if (_drawFallbackBtn != null)
            {
                _drawFallbackBtn.gameObject.SetActive(true);
                _drawFallbackBtn.interactable = true;
            }

            // Editor: short pause so you can see DRAW HAND; device: longer failsafe
            // (Always long enough that shuffle is finished before auto-draw starts.)
            var autoDrawAt = _awaitStartedAt + (Application.isEditor ? 2.2f : AutoDrawFailsafeSeconds);

            while (State == Phase.AwaitDrawGesture &&
                   _engine != null &&
                   _engine.OpeningSequenceActive)
            {
                UpdateHandProxy();

                if (_drawRequested || CheckDeckReach() || WrldzInput.KeyDown(KeyCode.Space) ||
                    WrldzInput.KeyDown(KeyCode.Return))
                {
                    Debug.Log("[WRLDZ PreDuel] Draw trigger accepted");
                    break;
                }

                if (Time.unscaledTime >= autoDrawAt)
                {
                    Debug.Log("[WRLDZ PreDuel] Auto-draw (Editor quick / failsafe)");
                    break;
                }

                yield return null;
            }

            if (_drawCanvasGo != null) _drawCanvasGo.SetActive(false);
            _ix?.PlayerDisk?.SetDeckHighlight(false);

            // ── 4) Engine draw, then cards leave the disk well into the hand ──
            State = Phase.Drawing;
            Hint("DRAW!");
            Debug.Log("[WRLDZ PreDuel] Draw from disk deck well");

            if (_engine != null)
            {
                var ok = _engine.CompleteOpeningDrawFromDeck();
                Debug.Log(
                    $"[WRLDZ PreDuel] CompleteOpeningDraw → {ok} " +
                    $"hand={_engine.Player?.HandCount} turn={_engine.TurnNumber}");
            }

            if (_ix?.PlayerDisk != null && _ix.PlayerDisk.BladeOpen01 < 0.9f)
                _ix.PlayerDisk.Fx.SnapDeployed();
            if (_ix?.OppDisk != null && _ix.OppDisk.BladeOpen01 < 0.9f)
                _ix.OppDisk.Fx.SnapDeployed();

            _ix?.SetHandVolumeVisible(true);
            if (_ix?.HandVolume != null)
                _ix.HandVolume.SourceDeck = _ix.PlayerDisk;
            if (_ix?.OppHandVolume != null)
                _ix.OppHandVolume.SourceDeck = _ix.OppDisk;
            _ix?.SyncNow();

            var n = Mathf.Max(1, _engine?.Player?.HandCount ?? TcgRules.StartingHandSize);
            var wait = ArDiskMotion.DrawLift + ArDiskMotion.DrawTravel
                       + (n - 1) * ArDiskMotion.DrawStagger + 0.12f;
            yield return new WaitForSecondsRealtime(wait);

            if (_handProxy != null) _handProxy.gameObject.SetActive(false);

            State = Phase.Complete;
            Hint("YOUR TURN — MAIN PHASE 1 · cards on disk + midfield arena");
            try { _onComplete?.Invoke(); }
            catch (Exception ex) { Debug.LogError("[WRLDZ PreDuel] onComplete: " + ex); }
            _co = null;
        }

        void BuildHandProxy()
        {
            if (_handProxy != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PreDuelHandProxy";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.07f;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                   ?? Shader.Find("Unlit/Color")
                                   ?? Shader.Find("Sprites/Default"));
            var c = new Color(0.4f, 0.95f, 1f, 0.75f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            mr.sharedMaterial = mat;
            go.SetActive(false);
            _handProxy = go.transform;
        }

        void BuildFallbackDrawButton()
        {
            if (_drawFallbackBtn != null) return;
            UiFoundation.EnsureEventSystem();

            // Own high-order canvas so dock / AR never block the button
            _drawCanvasGo = new GameObject("PreDuelDrawCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = _drawCanvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _drawCanvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);

            var go = new GameObject("DrawHandBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_drawCanvasGo.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.18f, 0.22f);
            rt.anchorMax = new Vector2(0.82f, 0.30f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = DuelystUi.BtnPrimary() ?? DuelystUi.BtnGold() ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;

            var label = new GameObject("L", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var trt = label.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var t = label.GetComponent<Text>();
            WrldzType.Style(t, 18, display: true, heavyOutline: true);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = DuelystUi.TextCream;
            t.text = "DRAW HAND";
            t.raycastTarget = false;

            _drawFallbackBtn = go.GetComponent<Button>();
            _drawFallbackBtn.targetGraphic = img;
            _drawFallbackBtn.onClick.AddListener(() =>
            {
                Debug.Log("[WRLDZ PreDuel] DRAW HAND button");
                FreeUiKit.PlayConfirm();
                RequestDraw();
            });
            _drawCanvasGo.SetActive(false);
        }

        void UpdateHandProxy()
        {
            if (_handProxy == null || !_handProxy.gameObject.activeSelf) return;
            if (TryGetPointerWorld(out var world))
                _handProxy.position = world;
            else if (_ix?.Tracker != null && _ix.Tracker.TryGetTorso(out var torso))
                _handProxy.position = torso.position + torso.forward * 0.28f + torso.up * -0.05f;
        }

        bool TryGetPointerScreen(out Vector2 screen)
        {
            screen = default;
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                screen = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null)
            {
                screen = Mouse.current.position.ReadValue();
                return true;
            }
#else
            try
            {
                if (Input.touchCount > 0)
                {
                    screen = Input.GetTouch(0).position;
                    return true;
                }

                screen = Input.mousePosition;
                return true;
            }
            catch { return false; }
#endif
            return false;
        }

        bool PointerPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                return true;
            if (Touchscreen.current != null &&
                Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                return true;
            return false;
#else
            try { return Input.GetMouseButtonDown(0); }
            catch { return false; }
#endif
        }

        bool TryGetPointerWorld(out Vector3 world)
        {
            world = default;
            if (_stageCam == null) return false;
            if (!TryGetPointerScreen(out var screen)) return false;

            Ray ray;
            // RT cameras: map screen → viewport of AR RawImage, then camera viewport ray
            if (_viewport != null)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _viewport, screen, null, out var local))
                    return false;
                var r = _viewport.rect;
                if (r.width < 1f || r.height < 1f) return false;
                var nx = (local.x - r.xMin) / r.width;
                var ny = (local.y - r.yMin) / r.height;
                // Allow slight outside for easier targeting
                if (nx < -0.05f || nx > 1.05f || ny < -0.05f || ny > 1.05f)
                    return false;
                ray = _stageCam.ViewportPointToRay(new Vector3(Mathf.Clamp01(nx), Mathf.Clamp01(ny), 0f));
            }
            else
                ray = _stageCam.ScreenPointToRay(screen);

            var disk = _ix?.PlayerDisk;
            if (disk?.MainDeckCollider != null &&
                disk.MainDeckCollider.Raycast(ray, out var hit, 20f))
            {
                world = hit.point;
                return true;
            }

            if (disk != null)
            {
                // Plane through deck, facing camera
                var n = -_stageCam.transform.forward;
                var plane = new Plane(n, disk.MainDeckWorldCenter);
                if (plane.Raycast(ray, out var enter))
                {
                    world = ray.GetPoint(enter);
                    return true;
                }
            }

            return false;
        }

        bool CheckDeckReach()
        {
            if (_drawRequested) return true;
            var disk = _ix?.PlayerDisk;
            if (disk == null) return false;

            var deck = disk.MainDeckWorldCenter;
            // Generous reach — phone RT units vary
            var radius = Mathf.Max(disk.MainDeckReachRadius, 0.35f);

            if (_handProxy != null && _handProxy.gameObject.activeSelf)
            {
                var d = Vector3.Distance(_handProxy.position, deck);
                if (d < radius)
                {
                    _nearDeckSeconds += Time.unscaledDeltaTime;
                    if (_nearDeckSeconds >= DeckHoldSeconds)
                        return true;
                }
                else
                    _nearDeckSeconds = Mathf.Max(0f, _nearDeckSeconds - Time.unscaledDeltaTime);
            }

            // Click / tap deck this frame
            if (PointerPressedThisFrame() && _stageCam != null && TryGetPointerScreen(out var screen))
            {
                Ray ray;
                if (_viewport != null &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _viewport, screen, null, out var local))
                {
                    var r = _viewport.rect;
                    var nx = (local.x - r.xMin) / Mathf.Max(1f, r.width);
                    var ny = (local.y - r.yMin) / Mathf.Max(1f, r.height);
                    ray = _stageCam.ViewportPointToRay(new Vector3(Mathf.Clamp01(nx), Mathf.Clamp01(ny), 0f));
                }
                else
                    ray = _stageCam.ScreenPointToRay(screen);

                if (disk.MainDeckCollider != null &&
                    disk.MainDeckCollider.Raycast(ray, out _, 20f))
                    return true;

                // Also accept click near deck in projected plane
                if (TryGetPointerWorld(out var w) && Vector3.Distance(w, deck) < radius * 1.5f)
                    return true;
            }

            // Lenses: controller tip near deck
            if (_ix?.Tracker != null && _ix.Tracker.TryGetLeftForearm(out var arm))
            {
                var handPt = arm.position + arm.rotation * new Vector3(0f, 0.02f, 0.14f);
                if (Vector3.Distance(handPt, deck) < radius * 1.2f)
                {
                    _nearDeckSeconds += Time.unscaledDeltaTime;
                    if (_nearDeckSeconds >= DeckHoldSeconds)
                        return true;
                }
            }

            return false;
        }

        void Hint(string msg) => _onHint?.Invoke(msg);

        void OnDestroy()
        {
            if (_handProxy != null) Destroy(_handProxy.gameObject);
            if (_drawCanvasGo != null) Destroy(_drawCanvasGo);
        }
    }
}
