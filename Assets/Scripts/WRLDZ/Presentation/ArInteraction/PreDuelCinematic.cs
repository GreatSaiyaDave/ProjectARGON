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
    /// 0) First turn: the setup's coin toss (sides dealt and shown beforehand) or dice roll (ties rolled again), on the
    ///    engine's own random stream, shown as the arena's coin / die hologram; the result sets turn 1
    /// 1) Disk deploy cinematic: the AI duelist strikes its character's deploy pose (ArOpponentDuelist), then the
    ///    player's Spirit Dueler flies off the arm to hang in front of them, deploys, boots its LP, and returns to the arm
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
            Complete,
            FirstTurn
        }

        public Phase State { get; private set; } = Phase.Idle;
        public bool IsRunning => State is Phase.FirstTurn or Phase.Deploying or Phase.Shuffling or Phase.AwaitDrawGesture or Phase.Drawing;
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
        ArDuelMatchConfig _match;

        const float AutoDrawFailsafeSeconds = 12f;
        const float DeckHoldSeconds = 0.2f;
        /// <summary>The coin hologram spins ~1.5 s and settles; the die tumbles a little longer.</summary>
        const float CoinSettleSeconds = 2.7f, DieSettleSeconds = 2.8f;

        public static PreDuelCinematic Ensure(GameObject host)
        {
            if (host == null) return null;
            return ArObjectUtil.GetOrAdd<PreDuelCinematic>(host);
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
            Transform uiParentForFallback = null,
            ArDuelMatchConfig match = null)
        {
            _engine = engine;
            _ix = interaction;
            _match = match ?? AppSession.Instance?.PendingArMatch;
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
            // ── 0) First turn: the setup's method, decided for real on the engine's stream ──
            State = Phase.FirstTurn;
            yield return DecideFirstTurn();

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
                _ix.SetHandVolumeVisible(false);
                // the opponent first (a disk hanging in front of your face would hide its pose), then yours
                var duelist = _ix.OppDuelist;
                if (duelist != null && duelist.isActiveAndEnabled)
                {
                    Hint("DUEL!");
                    var len = duelist.PlayDeployCinematic();
                    _ix.OppDuelist2?.PlayDeployCinematic();
                    // ~2.5 s on the opponent + ~4 s on your Spirit Dueler's activation = the ~6 s deploy cinematic (art bar)
                    // the whole blade activation plays on screen, then the lock jolt and the zone light sweep (≈0.9 s)
                    // ...and the post-lock pose (the duelist keys its hold on its disk's mechanism: DeployShowSeconds)
                    var show = Mathf.Min(len, duelist.DeployShowSeconds + 0.3f);
                    // the opponent stands metres off down the street: its pose plays in a close-up cut-in
                    var cut = DeployCutIn.Show(duelist, _stageCam != null ? _stageCam : Camera.main, show + 4f,
                        _ix.OppDisk != null ? _ix.OppDisk.AuraColorNow : new Color(1f, 0.35f, 0.55f));
                    // follow the duelist's own clock (a hitch must not cut its pose short), with a wall-clock cap
                    var capAt = Time.unscaledTime + show + 2.5f;
                    while (duelist != null && duelist.IsDeploying && duelist.DeployElapsed < duelist.DeployShowSeconds + 0.3f && Time.unscaledTime < capAt)
                        yield return null;
                    if (cut != null) cut.Close();
                }
                else if (_ix.OppDisk != null)
                {
                    // No AI duelist body was raised (rig not humanoid, or a nav duelist): the "DUEL!" beat still plays —
                    // a cut-in over the opponent's own blade deploy instead of the body's pose.
                    Hint("DUEL!");
                    var diskName = _engine?.Opponent != null && !string.IsNullOrEmpty(_engine.Opponent.Name)
                        ? _engine.Opponent.Name : null;
                    DeployCutIn.Show(null, _stageCam != null ? _stageCam : Camera.main, 3.4f,
                        _ix.OppDisk.AuraColorNow, _ix.OppDisk.transform, diskName);
                    _ix.OppDisk.DeployForDuel(0.28f);
                    yield return new WaitForSecondsRealtime(3.2f);
                }
                else
                    _ix.OppDisk?.DeployForDuel(0.28f);

                yield return PlayerDiskShowcase();
            }
            else
            {
                Debug.LogWarning("[WRLDZ PreDuel] No interaction system — deploy/shuffle will be skipped");
                yield return new WaitForSecondsRealtime(1.35f);
            }

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
                // Energy pulse on the already-deployed plate (BladeDeploy here used to snap the plate shut and
                // re-open it at the start of every AR duel).
                _ix.PlayerDisk.PlayFx(DiskFxEvent.HoloProject, 0.35f);
                _ix.SyncNow();
                _ix.PlayerDisk.EnsureDeckStackVisual();
                _ix.OppDisk?.EnsureDeckStackVisual();
                // Both Main Decks were shuffled at duel start. Riffle them together (the AI duelist rubs its own deck).
                _ix.OppDuelist?.PlayOpeningShuffle();
                var you = _ix.PlayerDisk.PlayShuffleRoutine(1.75f);
                var opp = _ix.OppDisk != null ? _ix.OppDisk.PlayShuffleRoutine(1.75f) : null;
                try
                {
                    while (true)
                    {
                        var moreYou = you.MoveNext();
                        var moreOpp = opp != null && opp.MoveNext();
                        if (!moreYou && !moreOpp) break;
                        yield return null;
                    }
                }
                finally
                {
                    (you as System.IDisposable)?.Dispose();
                    (opp as System.IDisposable)?.Dispose();
                }
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
            // Mr. Referobot calls "Turn 1 — your move!"; the hint only adds where the cards are.
            Hint("Cards on your disk · holograms in the arena");
            try { _onComplete?.Invoke(); }
            catch (Exception ex) { Debug.LogError("[WRLDZ PreDuel] onComplete: " + ex); }
            _co = null;
        }

        /// <summary>
        /// Who takes turn 1, the way the setup asked. Coin: the sides were dealt at setup and are shown, then the engine's
        /// stream tosses (the arena raises the coin hologram through <see cref="CoinDicePresentation"/>) and the side
        /// that came up goes first. Dice: both roll, both results shown, the higher goes first, a tie is rolled
        /// again. The result goes into the engine (<see cref="DuelEngine.SetFirstPlayer"/>) and the match record.
        /// </summary>
        IEnumerator DecideFirstTurn()
        {
            if (_engine == null || _engine.TurnNumber != 0 || _engine.Player == null || _engine.Opponent == null)
                yield break;
            // Dim the standby disk glow so the coin/die hologram reads clean (restored below).
            _ix?.PlayerDisk?.Fx?.SuppressStandbyGlow(true);
            _ix?.OppDisk?.Fx?.SuppressStandbyGlow(true);
            var match = _match;
            var you = _engine.HumanVsHuman ? "PLAYER 1" : "YOU";
            var opp = _engine.HumanVsHuman ? "PLAYER 2" : (_engine.Opponent.Name ?? "OPPONENT").ToUpperInvariant();
            var method = match != null ? match.FirstPlayerMethod : FirstPlayerMethod.CoinToss;
            bool opponentFirst;
            if (method == FirstPlayerMethod.DiceRoll)
            {
                int a, b;
                var tries = 0;
                do
                {
                    if (tries > 0)
                    {
                        Hint("TIE  ·  ROLL AGAIN");
                        yield return new WaitForSecondsRealtime(1.0f);
                    }

                    Hint("FIRST TURN  ·  DICE ROLL");
                    _engine.Rng.SetPresentationContext(true, "First turn");
                    a = _engine.Rng.RollDie();
                    _engine.Rng.SetPresentationContext(false, "First turn");
                    b = _engine.Rng.RollDie();
                    _ix?.SyncNow();   // the arena dequeues the rolls into die holograms
                    yield return new WaitForSecondsRealtime(DieSettleSeconds);
                    Hint($"{you} {a}  ·  {opp} {b}");
                    yield return new WaitForSecondsRealtime(0.8f);
                    tries++;
                } while (a == b && tries < 50);

                opponentFirst = b > a;
                if (match != null)
                {
                    match.FirstPlayerPlayerRoll = a;
                    match.FirstPlayerOpponentRoll = b;
                }
            }
            else
            {
                var headsIsP1 = match == null || match.CoinHeadsIsPlayerOne;
                Hint($"FIRST TURN  ·  COIN TOSS  ·  {you}: {(headsIsP1 ? "HEADS" : "TAILS")}  ·  {opp}: {(headsIsP1 ? "TAILS" : "HEADS")}");
                yield return new WaitForSecondsRealtime(1.2f);
                _engine.Rng.SetPresentationContext(true, "First turn");
                var heads = _engine.Rng.TossCoin();
                _ix?.SyncNow();   // the arena dequeues the toss into the coin hologram
                yield return new WaitForSecondsRealtime(CoinSettleSeconds);
                opponentFirst = heads != headsIsP1;
                if (match != null) match.CoinLandedHeads = heads;
                Hint($"{(heads ? "HEADS" : "TAILS")}");
                yield return new WaitForSecondsRealtime(0.8f);
            }

            _engine.SetFirstPlayer(opponentFirst);
            if (match != null)
            {
                match.FirstPlayerDecided = true;
                match.OpponentGoesFirst = opponentFirst;
            }

            Hint(opponentFirst ? $"{opp} GOES FIRST" : _engine.HumanVsHuman ? $"{you} GOES FIRST" : "YOU GO FIRST");
            Debug.Log($"[WRLDZ PreDuel] First turn · {method} → {(opponentFirst ? "opponent" : "player")}");
            // Hold the turn-order callout long enough to read before the disk deploy overwrites it.
            yield return new WaitForSecondsRealtime(1.5f);
            _ix?.PlayerDisk?.Fx?.SuppressStandbyGlow(false);
            _ix?.OppDisk?.Fx?.SuppressStandbyGlow(false);
        }

        /// <summary>
        /// Battle City activation (SOLID_VISION_BEHAVIOR.md §3 beat 1, the anime "Duel Disk, activate!"): the Spirit Dueler
        /// rises from the arm into the duelist's reading pose (plate face to the eye, about 30° below the line of sight, hub
        /// at the wrist, blade running sideways as worn), holds still while the real mechanism runs — halves tuck, stick
        /// out, meet at the deck, slide to the lock, the LP window lights — then the arm lowers back for the draw. No spin; the
        /// aura is kept to a rim and its world-space trails are held so nothing streaks across the view. Falls back to a
        /// plain on-arm deploy when there is no disk or camera.
        /// </summary>
        IEnumerator PlayerDiskShowcase()
        {
            var disk = _ix != null ? _ix.PlayerDisk : null;
            var cam = _stageCam != null ? _stageCam : Camera.main;
            if (disk == null || cam == null || !disk.isActiveAndEnabled || disk.MonsterZones[0] == null || disk.MonsterZones[4] == null)
            {
                _ix?.DeployForDuelOnly(0.05f);
                // the whole mechanism (plus the rig's possible 0.28 s skin fade), or the safety snap after this
                // teleports the halves mid-sweep
                yield return new WaitForSecondsRealtime(DiskFxDriver.DeploySeconds + 0.4f);
                yield break;
            }

            var camT = cam.transform;
            var dt = disk.transform;
            var p0 = dt.position; var r0 = dt.rotation;
            // the disk as it sits on the arm now: where the plate faces and which way the blade runs
            var m0 = disk.MonsterZones[0].transform.position; var m4 = disk.MonsterZones[4].transform.position;
            // the open kit is 0.64 m end to end (small blade + field slot to the big blade tip): size and centre the shot on
            // the whole plate, not on the card zones (which span only the middle 0.28 m)
            var kitMin = ArPlaymatLayout.DiskMeshBoundsMin; var kitMax = ArPlaymatLayout.DiskMeshBoundsMax;
            var kitSpan = (kitMax.x - kitMin.x) * ArPlaymatLayout.DiskMeshVisualScale * dt.lossyScale.x;
            var span = Mathf.Max(kitSpan * 1.08f, 0.05f);
            var kitCentreWorld = disk.BladePivot.TransformPoint((kitMin + kitMax) * 0.5f * ArPlaymatLayout.DiskMeshVisualScale);
            var centreLocal = dt.InverseTransformPoint(kitCentreWorld);
            var bladeNow = (m4 - m0).normalized;
            var normalNow = disk.MonsterZones[0].transform.up;
            var now = Quaternion.LookRotation(bladeNow, normalNow);
            var hfov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect);
            // the open plate takes ~55 % of the view width: near enough to read the slots, far enough to see the whole arm's worth
            var dist = span / (0.55f * 2f * Mathf.Tan(hfov * 0.5f * Mathf.Deg2Rad));
            dist = Mathf.Max(dist, cam.nearClipPlane * 4f);
            // the blade keeps the side it already runs to on the arm (the disk's own left / right), never mirrored
            var side = Vector3.Dot(bladeNow, camT.right) >= 0f ? camT.right : -camT.right;

            Pose Reading(float t)
            {
                // ~12° below the line of sight (in frame on a phone's portrait view too), like glancing at a raised
                // wrist; a slow breath while held
                var breathe = Mathf.Sin(t * 1.8f) * 0.01f * dist;
                var centre = camT.position + camT.forward * (dist * 0.97f) - camT.up * (dist * 0.21f + breathe);
                var toEye = (camT.position - centre).normalized;
                var plateUp = (toEye + camT.up * 0.20f).normalized;              // face to the eye, tipped a touch up
                var blade = Vector3.ProjectOnPlane(side + camT.forward * 0.12f, plateUp).normalized;
                var want = Quaternion.LookRotation(blade, plateUp);
                var rot = want * Quaternion.Inverse(now) * r0;
                var pos = centre - rot * Vector3.Scale(centreLocal, dt.lossyScale);
                return new Pose(pos, rot);
            }

            // smootherstep: zero velocity AND acceleration at both ends, so the arm never pops
            static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }

            disk.ExternalPose = true;
            // the aura is sized for a disk seen from arm's length: up close it would wash the plate out
            var aura = disk.GetComponentInChildren<SpiritDuelerAura>(true);
            var emph0 = aura != null ? aura.Emphasis : 1f;
            void AuraAt(float k) { if (aura != null) aura.Emphasis = Mathf.Lerp(emph0, emph0 * 0.15f, k); }
            if (aura != null) aura.Travelling = true;
            var trails = disk.GetComponentsInChildren<TrailRenderer>(true);
            foreach (var tr in trails) { tr.emitting = false; tr.Clear(); }
            Hint("DUEL DISK — ACTIVATE");
            FreeUiKit.PlayConfirm();

            // Battle City timing: raise 0.7 s → the sections extend, sweep and meet, the plate slides to the lock
            // (DiskFxDriver.DeploySeconds) → LP lights → hold on the locked plate → lower 0.7 s
            const float raise = 0.7f, lower = 0.7f;
            // the rig's DeployRoutine may fade the skin in for 0.28 s before the mechanism starts, so the LP power-on
            // (and its HoloProject pulse) waits for the whole travel plus that margin — it used to land at 95 % and
            // the plate never reached the lock
            const float mech = DiskFxDriver.DeploySeconds + 0.35f, lpAt = mech + 0.1f, hold = mech + 0.7f;
            var t = 0f; var deployed = false; var lpOn = false;
            try
            {
                while (t < raise + hold)
                {
                    t += Time.unscaledDeltaTime;
                    var f = Reading(t);
                    if (t < raise)
                    {
                        // the arm comes up and in: a shallow arc, forward then up, no roll beyond the pose change itself
                        var e = Ease(t / raise);
                        var arc = camT.forward * (Mathf.Sin(e * Mathf.PI) * 0.06f * dist) - camT.up * (Mathf.Sin(e * Mathf.PI) * 0.04f * dist);
                        dt.SetPositionAndRotation(Vector3.Lerp(p0, f.position, e) + arc, Quaternion.Slerp(r0, f.rotation, e));
                        AuraAt(e);
                    }
                    else
                    {
                        dt.SetPositionAndRotation(f.position, f.rotation);
                        if (aura != null) aura.Travelling = false;   // still now: the rim may breathe again
                        AuraAt(1f);
                    }
                    // the hub's phase buttons belong to play, not to the activation beat
                    disk.PhaseButtons?.SetDeployed(false);

                    if (!deployed && t >= raise)
                    {
                        deployed = true;
                        disk.DeployForDuel(0f);            // tuck → stick out → meet at the deck → plate slides to the lock
                    }
                    if (!lpOn && t >= raise + lpAt)
                    {
                        lpOn = true;
                        var lp = _engine?.Player != null ? _engine.Player.LifePoints : TcgRules.StartingLifePoints;
                        disk.LpCounter?.PowerOn(lp);
                        disk.PlayFx(DiskFxEvent.HoloProject, 0.5f);
                        Hint($"LIFE POINTS  {lp}");
                    }
                    yield return null;
                }

                // lower: back onto the live arm pose (it moves while the disk comes down)
                if (aura != null) aura.Travelling = true;
                var q0 = dt.position; var qr = dt.rotation;
                var tb = 0f;
                while (tb < lower)
                {
                    tb += Time.unscaledDeltaTime;
                    var e = Ease(tb / lower);
                    var arm = disk.Tracker != null && disk.Tracker.TryGetLeftForearm(out var ap) ? ap : new Pose(p0, r0);
                    dt.SetPositionAndRotation(Vector3.Lerp(q0, arm.position, e), Quaternion.Slerp(qr, arm.rotation, e));
                    AuraAt(1f - e);
                    disk.PhaseButtons?.SetDeployed(false);
                    yield return null;
                }
            }
            finally
            {
                disk.ExternalPose = false;
                AuraAt(0f);
                if (aura != null) aura.Travelling = false;
                if (!deployed) disk.DeployForDuel(0f);
                disk.PhaseButtons?.SetDeployed(true);
                foreach (var tr in trails) if (tr != null) { tr.Clear(); tr.emitting = true; }
            }
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
