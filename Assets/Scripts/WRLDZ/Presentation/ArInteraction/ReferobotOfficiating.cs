using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>Editor entry point: officiating and wheel checks (<c>unity cmd wrldz_referobot_checks</c>).</summary>
    public static class ReferobotChecks
    {
        public static string Run() =>
            ReferobotOfficiating.RunSanityChecks() + "\n" + ReferobotWheelMotion.RunSanityChecks();
    }

    /// <summary>
    /// Where Mr. Referobot looks and what he signals, modelled on how real officials work:
    /// <list type="bullet">
    /// <item>Face the field with both duelists in view and never turn his back on play (IFAB positioning
    /// guidelines; Hockey Canada "turn and follow"). The body stays square to the field and the head does
    /// the watching.</item>
    /// <item>Watch the ball — here the duelist whose turn it is — and make short glances at the other
    /// duelist, who may respond. Football referees shift their gaze mostly with the head and keep those
    /// looks away from the ball short (roughly 0.6–1 s), timed ahead of the next pass (J. Sports Sci. 2024).</item>
    /// <item>Anticipate: on an attack he looks at the attacker, then at its target before the hit; in a
    /// response window at the duelist who may respond; a new hologram gets a look as it lands.</item>
    /// <item>Call the server: at each new turn he points to the duelist whose move it is (tennis chair umpire).</item>
    /// <item>Stay unobtrusive and step in only on a violation (chess arbiter, TCG floor judge): a raised
    /// stop hand toward the duelist on an illegal play.</item>
    /// <item>Decision: turn to the winner and raise the arm on their side.</item>
    /// </list>
    /// Head, neck and arm turns are layered on after the rig has been posed for the frame (the wheel, lean and
    /// waist from <see cref="ReferobotWheelMotion"/>, or a clip on a legged rig). The static OBJ stand-in has no
    /// bones and turns its body instead.
    /// </summary>
    sealed class ReferobotOfficiating
    {
        // ── Gaze ────────────────────────────────────────────────────────────
        const float MaxHeadYaw = 80f;
        const float MaxLookDown = 35f;
        const float MaxLookUp = 25f;
        /// <summary>Settles in ~0.3 s, so a glance out and back takes about as long as a referee's.</summary>
        const float GazeSmoothTime = 0.11f;
        const float SpineShare = 0.2f;
        const float NeckShare = 0.3f;
        const float HeadShare = 0.5f;

        // ── Glances at the duelist who is not on the ball ──────────────────
        const float GlanceMin = 0.60f;
        const float GlanceMax = 0.95f;
        const float GlanceGapMin = 2.6f;
        const float GlanceGapMax = 5.2f;
        /// <summary>Battle Phase and response windows: the other duelist may answer, so check more often.</summary>
        const float BusyGapMin = 1.4f;
        const float BusyGapMax = 2.6f;

        // ── Arm signals ─────────────────────────────────────────────────────
        const float SignalIn = 0.28f;
        const float SignalOut = 0.32f;
        const float PointElevation = 8f;
        const float StopElevation = 35f;
        const float WinnerElevation = 68f;

        // ── Where a duelist's eyes are ─────────────────────────────────────
        const float HeadAboveTorso = 0.42f;
        const float HeadAboveDisk = 0.55f;
        /// <summary>Phone AR and lenses: a camera this close to the player's body is their eyes.</summary>
        const float CameraIsEyesWithin = 1.2f;

        // Beat priorities: a call at least as important as what is running takes over.
        const int PriorityGlance = 0;
        const int PriorityCard = 1;
        const int PriorityPlay = 2;
        const int PriorityCall = 3;
        const int PriorityOpening = 4;
        const int PriorityVerdict = 5;

        enum Signal { None, Point, Stop, Winner }

        struct Target
        {
            public bool IsCard;
            public bool PlayerSide;
            public int InstanceId;

            public static Target Duelist(bool playerSide) => new() { PlayerSide = playerSide };

            public static Target Card(int instanceId, bool playerSide) =>
                new() { IsCard = true, InstanceId = instanceId, PlayerSide = playerSide };
        }

        struct Beat
        {
            public Target Look;
            public float Seconds;
            public int Priority;
            public Signal Signal;
        }

        /// <summary>
        /// One bone the layer turns. Clips only write the channels they animate, so an offset left on a bone
        /// no clip touched is undone before the next one is added.
        /// </summary>
        sealed class Bone
        {
            public readonly Transform T;
            Quaternion _base;
            Quaternion _written;
            bool _wrote;

            public Bone(Transform t) => T = t;

            public void Begin()
            {
                if (T == null) return;
                if (_wrote && T.localRotation == _written) T.localRotation = _base;
                _base = T.localRotation;
            }

            public void Turn(Quaternion worldTurn, float share)
            {
                if (T == null || share <= 0f) return;
                T.rotation = Quaternion.Slerp(Quaternion.identity, worldTurn, share) * T.rotation;
            }

            public void End()
            {
                if (T == null) return;
                _written = T.localRotation;
                _wrote = true;
            }
        }

        readonly Transform _root;
        readonly ArArenaHologramManager _arena;
        readonly Bone _spine;
        readonly Bone _neck;
        readonly Bone _head;
        readonly Bone _leftUpper;
        readonly Bone _leftFore;
        readonly Bone _rightUpper;
        readonly Bone _rightFore;
        readonly Transform _leftHand;
        readonly Transform _rightHand;
        readonly Bone[] _bones;

        DuelEngine _engine;
        readonly List<Beat> _queue = new();
        Beat _beat;
        bool _hasBeat;
        float _beatEnds;
        float _nextGlanceAt;

        Signal _signal;
        bool _signalPlayerSide;
        float _signalStart;
        float _signalEnd;

        float _yaw;
        float _pitch;
        float _yawVelocity;
        float _pitchVelocity;

        int _lastTurn;
        int _lastAttacker;
        bool _wasAwaiting;
        bool _verdictCalled;
        float _gameOverAt = -1f;
        readonly HashSet<int> _seenHolograms = new();
        bool _hologramsPrimed;

        public ReferobotOfficiating(Transform root, ArArenaHologramManager arena)
        {
            _root = root;
            _arena = arena;
            _spine = new Bone(FindDeep(root, "Spine02"));
            _neck = new Bone(FindDeep(root, "neck"));
            _head = new Bone(FindDeep(root, "Head"));
            _leftUpper = new Bone(FindDeep(root, "LeftArm"));
            _leftFore = new Bone(FindDeep(root, "LeftForeArm"));
            _rightUpper = new Bone(FindDeep(root, "RightArm"));
            _rightFore = new Bone(FindDeep(root, "RightForeArm"));
            _leftHand = FindDeep(root, "LeftHand");
            _rightHand = FindDeep(root, "RightHand");
            _bones = new[] { _spine, _neck, _head, _leftUpper, _leftFore, _rightUpper, _rightFore };
        }

        /// <summary>True when the rig has a head to turn (the OBJ stand-in turns its body instead).</summary>
        public bool HasHead => _head.T != null;

        /// <summary>
        /// Play is live: an attack is on, a response window is open, or a card is resolving. Officials
        /// reposition in dead time, so he holds his spot unless something forces him off it.
        /// </summary>
        public bool PlayIsLive =>
            _engine != null && !_engine.GameOver &&
            (_engine.HasDeclaredAttack || _engine.IsAwaitingPlayerResponse ||
             ActivationBeat.BlocksAdvance(out _, out _));

        public void Bind(DuelEngine engine)
        {
            if (ReferenceEquals(engine, _engine)) return;
            _engine = engine;
            _queue.Clear();
            _hasBeat = false;
            _signal = Signal.None;
            _lastTurn = engine != null ? engine.TurnNumber : 0;
            _lastAttacker = 0;
            _wasAwaiting = false;
            _verdictCalled = false;
            _gameOverAt = -1f;
            _seenHolograms.Clear();
            _hologramsPrimed = false;
            if (engine == null) return;

            // Pre-duel check: a look at each duelist, then the call for whoever opens.
            Raise(new Beat { Look = Target.Duelist(true), Seconds = 1.0f, Priority = PriorityOpening },
                new Beat { Look = Target.Duelist(false), Seconds = 1.0f, Priority = PriorityOpening });
            if (engine.TurnPlayer != null)
                Raise(TurnCall(engine.TurnPlayer.IsPlayer));
            _nextGlanceAt = Time.time + 4f;
        }

        /// <summary>
        /// An illegal play was called: raised stop hand toward whoever is acting — you on your turn or in
        /// your response window, otherwise the duelist whose turn it is.
        /// </summary>
        public void CallIllegal()
        {
            var e = _engine;
            var playerSide = e == null || e.TurnPlayer == null || e.TurnPlayer.IsPlayer ||
                             e.IsAwaitingPlayerResponse;
            Raise(new Beat
            {
                Look = Target.Duelist(playerSide), Seconds = 1.2f, Priority = PriorityCall, Signal = Signal.Stop
            });
        }

        /// <summary>
        /// Arena-local point the body squares up toward, and how far toward it (field centre otherwise).
        /// With a head only the ball pulls the body; glances stay in the head.
        /// </summary>
        public bool TryGetBodyFocus(out Vector3 arenaLocal, out float share)
        {
            arenaLocal = Vector3.zero;
            share = 0f;
            var parent = _root.parent;
            if (parent == null) return false;
            var look = HasHead ? BallTarget() : CurrentTarget();
            if (!TryResolve(look, out var world)) return false;
            arenaLocal = parent.InverseTransformPoint(world);
            share = HasHead ? 0.3f : 0.75f;
            return true;
        }

        /// <summary>Run after the body is placed and the clip has posed the rig this frame.</summary>
        public void Tick(float dt)
        {
            PollEngine();
            PollHolograms();
            AdvanceBeats();

            for (var i = 0; i < _bones.Length; i++) _bones[i].Begin();
            if (HasHead)
            {
                Gaze(dt);
                PoseSignal();
            }

            for (var i = 0; i < _bones.Length; i++) _bones[i].End();
        }

        // ── What is happening ───────────────────────────────────────────────

        void PollEngine()
        {
            var e = _engine;
            if (e == null) return;

            if (e.TurnNumber != _lastTurn && e.TurnPlayer != null)
            {
                _lastTurn = e.TurnNumber;
                Raise(TurnCall(e.TurnPlayer.IsPlayer));
                // Settle on the new turn player before the first look away.
                _nextGlanceAt = Time.time + 1.4f + Random.Range(GlanceGapMin, GlanceGapMax);
            }

            // Attack: the attacker, then (ahead of the hit) whatever it is swinging at.
            var attacker = e.DeclaredAttacker;
            var attackerId = attacker != null ? attacker.InstanceId : 0;
            if (attackerId != 0 && attackerId != _lastAttacker)
            {
                var attackerSide = e.DeclaredAttackingPlayer != null
                    ? e.DeclaredAttackingPlayer.IsPlayer
                    : e.TurnPlayer == null || e.TurnPlayer.IsPlayer;
                var target = e.DeclaredAttackTarget;
                Raise(new Beat
                    {
                        Look = Target.Card(attackerId, attackerSide), Seconds = 0.7f, Priority = PriorityPlay
                    },
                    new Beat
                    {
                        Look = target != null
                            ? Target.Card(target.InstanceId, !attackerSide)
                            : Target.Duelist(!attackerSide),
                        Seconds = 1.3f,
                        Priority = PriorityPlay
                    });
            }

            _lastAttacker = attackerId;

            // Response window: watch the duelist who may answer.
            var awaiting = e.IsAwaitingPlayerResponse;
            if (awaiting && !_wasAwaiting)
            {
                var responder = new Beat { Look = Target.Duelist(true), Seconds = 1.5f, Priority = PriorityPlay };
                // The window an attack opens comes after attacker → target, not instead of it.
                if (e.HasDeclaredAttack) _queue.Add(responder);
                else Raise(responder);
            }

            _wasAwaiting = awaiting;

            if (e.GameOver && !_verdictCalled)
            {
                // Same hold as the spoken verdict: the card that won it is shown first.
                if (_gameOverAt < 0f) _gameOverAt = Time.time;
                var held = ActivationBeat.BlocksAdvance(out _, out _) &&
                           Time.time - _gameOverAt < SpellActivationPresentation.BeatHardCapSeconds;
                if (!held)
                {
                    _verdictCalled = true;
                    if (e.Winner != null)
                        Raise(new Beat
                        {
                            Look = Target.Duelist(e.Winner.IsPlayer), Seconds = 3.5f,
                            Priority = PriorityVerdict, Signal = Signal.Winner
                        });
                    else
                        Raise(new Beat { Look = Target.Duelist(true), Seconds = 1.0f, Priority = PriorityVerdict },
                            new Beat { Look = Target.Duelist(false), Seconds = 1.0f, Priority = PriorityVerdict });
                }
            }
        }

        /// <summary>A hologram that was not on the field before gets a look as it lands.</summary>
        void PollHolograms()
        {
            if (_arena == null) return;
            var freshId = 0;
            var freshSide = true;
            Scan(_arena.MonsterPlane, ref freshId, ref freshSide);
            Scan(_arena.SpellTrapPlane, ref freshId, ref freshSide);
            if (!_hologramsPrimed)
            {
                _hologramsPrimed = true;
                return;
            }

            if (freshId != 0)
                Raise(new Beat
                {
                    Look = Target.Card(freshId, freshSide), Seconds = 1.3f, Priority = PriorityCard
                });
        }

        void Scan(Transform plane, ref int freshId, ref bool freshSide)
        {
            if (plane == null) return;
            for (var i = 0; i < plane.childCount; i++)
            {
                var child = plane.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                var vis = child.GetComponent<ArArenaCardVisual>();
                if (vis == null || vis.InstanceId == 0) continue;
                if (!_seenHolograms.Add(vis.InstanceId)) continue;
                freshId = vis.InstanceId;
                freshSide = vis.PlayerSide;
            }
        }

        static Beat TurnCall(bool playerSide) =>
            new()
            {
                Look = Target.Duelist(playerSide), Seconds = 1.4f, Priority = PriorityCall, Signal = Signal.Point
            };

        /// <summary>
        /// A call at least as important as everything running or waiting takes over now; a lesser call
        /// waits its turn; a lesser glance or card look is dropped.
        /// </summary>
        void Raise(Beat first, Beat? second = null)
        {
            var busiest = _hasBeat ? _beat.Priority : -1;
            for (var i = 0; i < _queue.Count; i++) busiest = Mathf.Max(busiest, _queue[i].Priority);
            if (first.Priority >= busiest)
            {
                _queue.Clear();
                _hasBeat = false;
                // An arm signal still up eases down with the beat it belonged to.
                if (_signal != Signal.None) _signalEnd = Mathf.Min(_signalEnd, Time.time + SignalOut);
            }
            else if (first.Priority < PriorityPlay)
            {
                return;
            }

            _queue.Add(first);
            if (second.HasValue) _queue.Add(second.Value);
        }

        void AdvanceBeats()
        {
            var now = Time.time;
            if (_hasBeat && now >= _beatEnds) _hasBeat = false;
            if (!_hasBeat && _queue.Count == 0) MaybeGlance(now);
            if (_hasBeat || _queue.Count == 0) return;

            _beat = _queue[0];
            _queue.RemoveAt(0);
            _hasBeat = true;
            _beatEnds = now + _beat.Seconds;
            if (_beat.Signal == Signal.None) return;
            _signal = _beat.Signal;
            _signalPlayerSide = _beat.Look.PlayerSide;
            _signalStart = now;
            _signalEnd = now + _beat.Seconds;
        }

        /// <summary>A short look at the duelist who is not on the ball, then back.</summary>
        void MaybeGlance(float now)
        {
            if (_engine == null || _engine.GameOver || _engine.TurnPlayer == null || now < _nextGlanceAt) return;
            var glance = Random.Range(GlanceMin, GlanceMax);
            Raise(new Beat
            {
                Look = Target.Duelist(!_engine.TurnPlayer.IsPlayer), Seconds = glance, Priority = PriorityGlance
            });
            var busy = _engine.Phase == DuelPhase.Battle || _engine.IsAwaitingPlayerResponse;
            _nextGlanceAt = now + glance + (busy
                ? Random.Range(BusyGapMin, BusyGapMax)
                : Random.Range(GlanceGapMin, GlanceGapMax));
        }

        /// <summary>The ball: the duelist whose turn it is, or the winner once the verdict is in.</summary>
        Target BallTarget()
        {
            var e = _engine;
            if (e == null) return Target.Duelist(true);
            if (e.GameOver && _verdictCalled && e.Winner != null) return Target.Duelist(e.Winner.IsPlayer);
            return Target.Duelist(e.TurnPlayer == null || e.TurnPlayer.IsPlayer);
        }

        Target CurrentTarget() => _hasBeat ? _beat.Look : BallTarget();

        // ── Where things are ────────────────────────────────────────────────

        bool TryResolve(Target t, out Vector3 world)
        {
            if (t.IsCard && TryFindCard(t.InstanceId, out var vis))
            {
                var lift = vis.IsMonster ? CardArtFocus.MonsterArtLift : CardArtFocus.SpellTrapArtLift;
                world = vis.transform.TransformPoint(new Vector3(0f, lift, 0f));
                return true;
            }

            return TryDuelistEyes(t.PlayerSide, out world);
        }

        bool TryFindCard(int instanceId, out ArArenaCardVisual vis)
        {
            vis = null;
            if (_arena == null || instanceId == 0) return false;
            return FindIn(_arena.MonsterPlane, instanceId, out vis) || FindIn(_arena.SpellTrapPlane, instanceId, out vis);
        }

        static bool FindIn(Transform plane, int instanceId, out ArArenaCardVisual vis)
        {
            vis = null;
            if (plane == null) return false;
            for (var i = 0; i < plane.childCount; i++)
            {
                var child = plane.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                var v = child.GetComponent<ArArenaCardVisual>();
                if (v == null || v.InstanceId != instanceId) continue;
                vis = v;
                return true;
            }

            return false;
        }

        bool TryDuelistEyes(bool playerSide, out Vector3 world)
        {
            var parent = _root.parent;
            var up = parent != null ? parent.up : Vector3.up;
            var unit = parent != null ? parent.lossyScale.y : 1f;
            var tracker = _arena != null ? _arena.Tracker : null;
            Pose torso = default;
            var haveTorso = tracker != null &&
                            (playerSide ? tracker.TryGetTorso(out torso) : tracker.TryGetOpponentTorso(out torso));
            var disk = _arena == null ? null : playerSide ? _arena.PlayerDisk : _arena.OppDisk;
            if (haveTorso)
            {
                world = torso.position + up * (HeadAboveTorso * unit);
            }
            else if (disk != null)
            {
                world = disk.transform.position + up * (HeadAboveDisk * unit);
            }
            else if (parent != null)
            {
                var end = ArPlaymatLayout.LiveSpellTrapRowFromMid + ArPlaymatLayout.LiveStandOff;
                world = parent.TransformPoint(new Vector3(
                    playerSide ? ArPlaymatLayout.PlayerHoloX : ArPlaymatLayout.OppHoloX,
                    1.45f,
                    playerSide ? -end : end));
            }
            else
            {
                world = default;
                return false;
            }

            if (playerSide)
            {
                var cam = ArStageView.FindCamera(_root);
                if (cam != null && (cam.transform.position - world).magnitude < CameraIsEyesWithin * unit)
                    world = cam.transform.position;
            }

            return true;
        }

        // ── Posing ──────────────────────────────────────────────────────────

        void Gaze(float dt)
        {
            float wantYaw = 0f, wantPitch = 0f;
            if (TryResolve(CurrentTarget(), out var target))
            {
                // Body frame: +Z is his face.
                var local = Quaternion.Inverse(_root.rotation) * (target - _head.T.position);
                var flat = new Vector2(local.x, local.z).magnitude;
                wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -MaxHeadYaw, MaxHeadYaw);
                wantPitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -MaxLookUp, MaxLookDown);
            }

            if (dt > 0f)
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, wantYaw, ref _yawVelocity, GazeSmoothTime, Mathf.Infinity, dt);
                _pitch = Mathf.SmoothDampAngle(_pitch, wantPitch, ref _pitchVelocity, GazeSmoothTime,
                    Mathf.Infinity, dt);
            }

            // Pitch about his own right, then yaw about up; spread down the chain like a real head turn.
            var turn = Quaternion.AngleAxis(_yaw, _root.up) * Quaternion.AngleAxis(_pitch, _root.right);
            _spine.Turn(turn, SpineShare);
            _neck.Turn(turn, NeckShare);
            _head.Turn(turn, HeadShare);
        }

        void PoseSignal()
        {
            if (_signal == Signal.None) return;
            var now = Time.time;
            if (now >= _signalEnd)
            {
                _signal = Signal.None;
                return;
            }

            var w = Mathf.Min(Mathf.Clamp01((now - _signalStart) / SignalIn),
                Mathf.Clamp01((_signalEnd - now) / SignalOut));
            w = w * w * (3f - 2f * w);
            if (w <= 0f || !TryDuelistEyes(_signalPlayerSide, out var who)) return;

            // The arm on that duelist's side, whatever the rig calls it.
            var right = _rightUpper.T != null && _rightFore.T != null && _rightHand != null;
            var left = _leftUpper.T != null && _leftFore.T != null && _leftHand != null;
            if (!right && !left) return;
            var useRight = right && (!left ||
                                     (_rightUpper.T.position - who).sqrMagnitude <=
                                     (_leftUpper.T.position - who).sqrMagnitude);
            var upper = useRight ? _rightUpper : _leftUpper;
            var fore = useRight ? _rightFore : _leftFore;
            var hand = useRight ? _rightHand : _leftHand;

            var toward = Vector3.ProjectOnPlane(who - upper.T.position, _root.up);
            if (toward.sqrMagnitude < 1e-6f) return;
            var elevation = _signal switch
            {
                Signal.Stop => StopElevation,
                Signal.Winner => WinnerElevation,
                _ => PointElevation
            };
            var aim = Vector3.RotateTowards(toward.normalized, _root.up, elevation * Mathf.Deg2Rad, 0f);

            // Straight arm along the aim: upper arm first, then the forearm onto the same line.
            upper.Turn(Quaternion.FromToRotation(fore.T.position - upper.T.position, aim), w);
            fore.Turn(Quaternion.FromToRotation(hand.position - fore.T.position, aim), w);
        }

        // ── Sanity checks (Editor command wrldz_referobot_checks) ───────────

        /// <summary>
        /// Loads the shipped rig and a lab duel and checks the officiating layer: the head settles on the
        /// duelist, arm signals line up, the head never wraps behind him, every new turn hands the ball over
        /// with a pointed call, glances at the other duelist are short, lesser beats never cut into a call, and
        /// an illegal play raises the stop hand toward whoever is acting. Builds and destroys a hidden rig.
        /// </summary>
        internal static string RunSanityChecks()
        {
            var sb = new System.Text.StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) pass++;
                else fail++;
                sb.AppendLine((ok ? "PASS  " : "FAIL  ") + "Referobot: " + name +
                              (ok || string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            }

            var root = new GameObject("ReferobotChecks") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var actor = ReferobotActor.TryCreate(root.transform, 0);
                Check("rig loads from StreamingAssets", actor != null);
                if (actor == null) return Finish();
                var body = actor.transform;
                body.localPosition = new Vector3(3.94f, 0f, 0f);
                body.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                var off = new ReferobotOfficiating(body, null);
                Check("rig has the bones the layer turns",
                    off.HasHead && off._neck.T != null && off._spine.T != null && off._leftUpper.T != null &&
                    off._leftFore.T != null && off._leftHand != null && off._rightUpper.T != null &&
                    off._rightFore.T != null && off._rightHand != null);
                if (!off.HasHead) return Finish();

                // Gaze: head lands on the player's eyes.
                var restHead = off._head.T.rotation;
                off.TryDuelistEyes(true, out var eyes);
                for (var i = 0; i < 60; i++) off.Tick(1f / 30f);
                var look = off._head.T.rotation * Quaternion.Inverse(restHead) * body.forward;
                var lookError = Vector3.Angle(look, eyes - off._head.T.position);
                Check("head settles on the duelist's eyes", lookError < 3f, $"error {lookError:0.0}°");

                // Arm signal: a raised stop hand points at the duelist.
                off._signal = Signal.Stop;
                off._signalPlayerSide = true;
                off._signalStart = Time.time - 1f;
                off._signalEnd = Time.time + 1f;
                off.Tick(1f / 30f);
                var upper = (off._rightUpper.T.position - eyes).sqrMagnitude <=
                            (off._leftUpper.T.position - eyes).sqrMagnitude
                    ? off._rightUpper.T
                    : off._leftUpper.T;
                var hand = upper == off._rightUpper.T ? off._rightHand : off._leftHand;
                var flat = Vector3.ProjectOnPlane(eyes - upper.position, body.up).normalized;
                var aim = Vector3.RotateTowards(flat, body.up, StopElevation * Mathf.Deg2Rad, 0f);
                var armError = Vector3.Angle(hand.position - upper.position, aim);
                Check("stop hand lines up on the duelist", armError < 3f, $"error {armError:0.0}°");
                Check("stop hand is raised above the shoulder", hand.position.y > upper.position.y + 0.1f,
                    $"hand {hand.position.y:0.00} m, shoulder {upper.position.y:0.00} m");
                off._signal = Signal.None;

                // A duelist behind him: the head stops at its limit instead of wrapping round.
                body.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                for (var i = 0; i < 60; i++) off.Tick(1f / 30f);
                Check("head yaw stops at its limit for a duelist behind him",
                    Mathf.Abs(Mathf.Abs(off._yaw) - MaxHeadYaw) < 0.5f, $"yaw {off._yaw:0.0}°");
                body.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);

                // Beat arbitration without an engine.
                off.Raise(new Beat { Look = Target.Duelist(false), Seconds = 1f, Priority = PriorityCall,
                    Signal = Signal.Point });
                off.AdvanceBeats();
                off.Raise(new Beat { Look = Target.Duelist(true), Seconds = 1f, Priority = PriorityGlance });
                Check("a glance does not cut into a call", off._hasBeat && off._beat.Priority == PriorityCall &&
                                                          off._queue.Count == 0);
                off.Raise(new Beat { Look = Target.Card(7, true), Seconds = 1f, Priority = PriorityCard });
                Check("a card look waiting behind a call is dropped", off._queue.Count == 0);
                var now = Time.time;
                off.Raise(new Beat { Look = Target.Duelist(true), Seconds = 3f, Priority = PriorityVerdict,
                    Signal = Signal.Winner });
                Check("a verdict takes over at once", !off._hasBeat && off._queue.Count == 1);
                Check("the interrupted arm signal eases down", off._signalEnd <= now + SignalOut + 0.001f,
                    $"ends in {off._signalEnd - now:0.00}s");
                off._queue.Clear();
                off._hasBeat = false;
                off._signal = Signal.None;

                // A lab duel: turns, glances, illegal plays.
                var db = WRLDZ.Data.CardDatabase.Load();
                Check("card database loads", db != null && db.Count > 0);
                if (db == null || db.Count == 0) return Finish();
                var engine = new DuelEngine();
                engine.StartDuel(db, WRLDZ.Data.CardDatabase.LoadDeck("lab_rules_player.json"),
                    WRLDZ.Data.CardDatabase.LoadDeck("lab_rules_ai.json"), cinematicOpening: false);
                off.Bind(engine);
                var opener = engine.TurnPlayer != null && engine.TurnPlayer.IsPlayer;
                var opening = Drain(off);
                var want = $"YOU → OPP → {(opener ? "YOU" : "OPP")}+Point";
                Check("pre-duel check looks at each duelist, then calls the opener", opening == want,
                    $"got \"{opening}\", want \"{want}\"");

                for (var turn = 0; turn < 3; turn++)
                {
                    off._nextGlanceAt = Time.time + 999f;
                    for (var i = 0; i < 60; i++) off.Tick(1f / 30f);
                    var youToPlay = engine.TurnPlayer.IsPlayer;
                    var ball = off.CurrentTarget();
                    off.TryDuelistEyes(youToPlay, out var at);
                    var local = Quaternion.Inverse(body.rotation) * (at - off._head.T.position);
                    var wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -MaxHeadYaw, MaxHeadYaw);
                    Check($"turn {engine.TurnNumber}: watches the duelist whose turn it is",
                        !ball.IsCard && ball.PlayerSide == youToPlay && Mathf.Abs(off._yaw - wantYaw) < 2f,
                        $"watching {(ball.PlayerSide ? "YOU" : "OPP")}, yaw {off._yaw:0.0}° vs {wantYaw:0.0}°");
                    var call = MrReferobot.TurnCall(engine);
                    Check($"turn {engine.TurnNumber}: spoken call names whose move it is",
                        youToPlay
                            ? call == $"Turn {engine.TurnNumber} — your move!"
                            : call != null && call.EndsWith(" to play!") && call.StartsWith($"Turn {engine.TurnNumber} — "),
                        call);
                    engine.EndTurn(engine.TurnPlayer);
                    var handover = Drain(off);
                    var next = engine.TurnPlayer.IsPlayer ? "YOU" : "OPP";
                    Check($"turn {engine.TurnNumber}: points to the new turn player", handover.StartsWith(next + "+Point"),
                        $"got \"{handover}\"");
                }

                off._nextGlanceAt = Time.time - 1f;
                off.Tick(1f / 30f);
                var other = !engine.TurnPlayer.IsPlayer;
                Check("glance goes to the duelist who is not on the ball",
                    off._hasBeat && off._beat.Priority == PriorityGlance && off._beat.Look.PlayerSide == other);
                Check("glance is short (0.6–0.95 s)",
                    off._hasBeat && off._beat.Seconds >= GlanceMin && off._beat.Seconds <= GlanceMax,
                    $"{off._beat.Seconds:0.00}s");

                var turnSide = engine.TurnPlayer.IsPlayer ? "YOU" : "OPP";
                off.CallIllegal();
                var illegal = Drain(off);
                Check("illegal play: stop hand toward whoever is acting",
                    illegal == turnSide + "+Stop" || (engine.IsAwaitingPlayerResponse && illegal == "YOU+Stop"),
                    $"got \"{illegal}\" on {turnSide}'s turn");
            }
            catch (System.Exception ex)
            {
                Check("checks ran without an exception", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return Finish();

            string Finish()
            {
                sb.AppendLine($"--- Referobot: {pass} passed, {fail} failed ---");
                return sb.ToString();
            }
        }

        /// <summary>Plays out queued beats in order ("YOU → OPP+Point"), ending each one early.</summary>
        static string Drain(ReferobotOfficiating off)
        {
            var seq = new List<string>();
            off.PollEngine();
            for (var i = 0; i < 8; i++)
            {
                off.AdvanceBeats();
                if (!off._hasBeat) break;
                var b = off._beat;
                seq.Add((b.Look.PlayerSide ? "YOU" : "OPP") + (b.Signal != Signal.None ? "+" + b.Signal : ""));
                off._beatEnds = Time.time - 1f;
            }

            off._hasBeat = false;
            return string.Join(" → ", seq);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }
    }
}
