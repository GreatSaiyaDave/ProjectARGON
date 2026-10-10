using System.Collections;
using UnityEngine;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Live anime simulator: summon pillars, attack beams, impacts, defend shields.
    /// Driven by the rules engine only — never invents resolutions.
    /// </summary>
    public class ArAnimeSequenceDirector : MonoBehaviour
    {
        public Camera StageCamera;
        public ArArenaHologramManager Arena;
        public ArDuelDiskRig PlayerDisk;
        public ArDuelDiskRig OppDisk;
        public Transform ReferobotAnchor;

        /// <summary>Attack aim: a standing monster's torso, in hologram heights (art spans ~0.1–1.1).</summary>
        const float AimHeight = 0.55f;
        /// <summary>A face-down target lies as its card back just above the street.</summary>
        const float FaceDownAimHeight = 0.14f;
        const float ChargeTellInterval = 0.3f;
        /// <summary>A destroy / damage line this soon after a release is that release's hit, already shown on the target.</summary>
        const float ReleaseImpactWindow = 0.8f;
        /// <summary>
        /// No attack lands in the viewer's face: a direct attack on the local duelist aims at their disk, which
        /// rides beside the camera, so its landing stops this far out along its path (comfort on lenses; the
        /// flinch is §4.5's camera kick / flash, not light spheres around the lens).
        /// </summary>
        const float MinLandingFromEye = 1.1f;

        DuelEngine _engine;
        ArFreeLookCamera _freeLook;
        Vector3 _camBasePos;
        Quaternion _camBaseRot;
        bool _camCaptured;
        Coroutine _seq;
        int _lastAttackerId;
        bool _impactPlayedThisAttack;
        MrReferobot _referobot;

        /// <summary>One attack's look, fixed at its declaration (SOLID_VISION_BEHAVIOR.md §4.2).</summary>
        struct AttackShot
        {
            public AttackArchetype Kind;
            public Color Tint;
            public float Weight;
            public bool AttackerIsPlayer;
            public bool HasEndpoints;
            public Vector3 From;
            public Vector3 To;
            public float GroundY;
        }

        ActiveCombatPresentation _seqAttack;
        /// <summary>The Summon appear whose procedure hologram has already played, so it plays once.</summary>
        ActiveCombatPresentation _seqSummon;
        AttackShot _shot;
        Coroutine _seqCo;
        float _lastReleaseT = -10f;
        /// <summary>Until when a resolved attack may still be waiting on a Spell/Trap result gate before it flies
        /// (a time, not a flag, so a coroutine killed with its GameObject cannot leave it stuck).</summary>
        float _releasePendingUntil = -1f;
        /// <summary>Face-down Defense monster (card back only) that is the current attack target.</summary>
        ArArenaCardVisual _faceDownTargetVis;
        int _faceDownTargetId;

        public void Bind(DuelEngine engine, Camera cam)
        {
            Unbind();
            _engine = engine;
            StageCamera = cam;
            _freeLook = cam != null ? cam.GetComponent<ArFreeLookCamera>() : null;
            if (_engine != null)
            {
                _engine.OnStateChanged += OnState;
                _engine.OnLog += OnLog;
            }

            ArArenaCombatFx.Ensure(transform, 28);
        }

        public void Unbind()
        {
            if (_engine != null)
            {
                _engine.OnStateChanged -= OnState;
                _engine.OnLog -= OnLog;
            }

            ReleaseFaceDownTarget(immediate: true);
            _engine = null;
            _camCaptured = false;
            HoldCinematicCamera(false);
            if (_seq != null)
            {
                StopCoroutine(_seq);
                _seq = null;
            }

            _seqAttack = null;
            _releasePendingUntil = -1f;
            if (_seqCo != null)
            {
                StopCoroutine(_seqCo);
                _seqCo = null;
            }
        }

        void OnDestroy() => Unbind();

        void OnState()
        {
            if (_engine == null) return;
            TrackFaceDownAttackTarget();
            WatchAttackSequence(cancelLine: false);
            WatchSummonAppear();

            if (_engine.HasDeclaredAttack && _engine.ActivePresentation != null)
            {
                var ap = _engine.ActivePresentation;
                if (ap.Kind == CombatActionKind.Attack)
                    PulseAttackCharge(ap);
            }
            else if (_lastAttackerId != 0 || _impactPlayedThisAttack)
            {
                Arena?.ClearAllAttackCharges();
                _impactPlayedThisAttack = false;
                _lastAttackerId = 0;
            }
        }

        void OnLog(string line)
        {
            // The attack's own "X attacks Y." line is the first event after its presentation opens. After it
            // closes, the first line tells whether the attack went through: ResolveDeclaredAttack re-validates
            // before any damage-step line, so a cancel line is always first — and a damage-step line may
            // already follow the attacker's own battle destruction, so "attacker still there" is no test.
            var lower = string.IsNullOrEmpty(line) ? "" : line.ToLowerInvariant();
            WatchAttackSequence(IsAttackCancelLine(lower));
            if (lower.Length == 0) return;
            if (lower.Contains("normal summon") || lower.Contains("tribute summon") ||
                lower.Contains("special summon") || lower.Contains("flip summon") ||
                lower.Contains("summons "))
            {
                PlaySummonSequence();
            }
            else if (lower.Contains("activates") || lower.Contains("activate "))
            {
                PlayActivateSequence();
            }
            else if (lower.Contains("destroyed by battle") || lower.Contains("destroy"))
            {
                PlayImpactOnResultBeat(heavy: true);
            }
            else if (lower.Contains("damage") && lower.Contains("→"))
            {
                PlayImpactOnResultBeat(heavy: false);
            }
            else if (lower.Contains("defense") && lower.Contains("position"))
            {
                // Position change to DEF — shield on defender if we can find them
                PulseDefendFromLog();
            }
        }

        /// <summary>
        /// Opens the per-attack sequence when the engine opens an Attack presentation and closes it — releasing
        /// the attack only if the engine resolved it — the moment that presentation is gone.
        /// </summary>
        void WatchAttackSequence(bool cancelLine)
        {
            if (_engine == null) return;
            var ap = _engine.ActivePresentation;
            if (_seqAttack != null && ap != _seqAttack)
                EndAttackSequence(cancelLine);
            if (ap != null && ap.Kind == CombatActionKind.Attack && ap.SourceCard != null && ap != _seqAttack)
                BeginAttackSequence(ap);
        }

        /// <summary>
        /// Plays the Summon procedure's hologram when the engine opens a Summon appear. The engine tags the
        /// presentation with the procedure it came from, so a Fusion / Ritual / Synchro / Xyz / Pendulum / Link
        /// Summon gets its own lead-in instead of the generic materialize. The log-line match in
        /// <see cref="OnLog"/> cannot do this: it only recognizes "normal/tribute/special/flip summon", so
        /// "Fusion Summon" and the other Extra Deck procedures got no summon FX at all.
        /// The arrival is left to the arena card visual (arrive: false) so a summon gets one hologram, not two.
        /// </summary>
        void WatchSummonAppear()
        {
            if (_engine == null) return;
            var ap = _engine.ActivePresentation;
            if (ap == null || ap.Kind != CombatActionKind.SummonAppear || ap.SourceCard == null)
            {
                _seqSummon = null;
                return;
            }
            if (ap == _seqSummon) return;
            _seqSummon = ap;

            var isPlayer = ap.Actor != null && ap.Actor.IsPlayer;
            var vis = (ArArenaCardVisual)null;
            if (Arena != null)
                Arena.TryGetMonsterVisual(ap.SourceCard, isPlayer, out vis);
            var pos = vis != null
                ? vis.transform.position
                : (Arena != null && Arena.ArenaRoot != null
                    ? Arena.ArenaRoot.position + Vector3.up * 0.35f
                    : Vector3.zero);
            // A Set stays face-down: never hand its Def over, or its Attribute would leak into the light.
            var isSet = ap.Procedure == SummonKind.NormalSet || ap.Procedure == SummonKind.TributeSet;
            var def = isSet || !ap.SourceCard.FaceUp ? null : ap.SourceCard.Def;
            ArArenaCombatFx.PlaySummonProcedure(pos, isPlayer, ap.Procedure, 28, def, arrive: false);
        }

        /// <summary>DuelEngine.ResolveDeclaredAttack's lines for an attack that never reaches damage.</summary>
        static bool IsAttackCancelLine(string lower) =>
            lower.Length > 0 &&
            (lower.Contains("attack cancelled") || lower.Contains("attack fizzled") || lower.Contains("replay") ||
             lower.Contains("attack stops") || lower.Contains("cannot attack directly"));

        /// <summary>
        /// Beat 1 (SOLID_VISION_BEHAVIOR.md §4.2): the attack is declared. Mr. Referobot calls its anime name (the
        /// voice of the Spell/Trap name call-outs; only sourced names — most monsters have none), and the look
        /// of the whole attack is fixed from the attacker: archetype by race / table, colour by Attribute / table.
        /// </summary>
        void BeginAttackSequence(ActiveCombatPresentation ap)
        {
            _seqAttack = ap;
            var def = ap.SourceCard.Def;
            _shot = new AttackShot
            {
                Kind = SolidVisionCinematics.ArchetypeFor(def),
                Tint = SolidVisionCinematics.TintFor(def),
                Weight = SolidVisionCinematics.AttackWeight(ap.SourceCard),
                AttackerIsPlayer = ap.Actor != null && ap.Actor.IsPlayer
            };
            _shot.HasEndpoints = TryAttackEndpoints(ap, out _shot.From, out _shot.To, out _shot.GroundY,
                out _, out _);

            var call = SolidVisionCinematics.AttackCall(ap.SourceCard);
            var announcer = Announcer;
            if (call != null && announcer != null)
                announcer.SayNow(call);

            if (Application.isPlaying && isActiveAndEnabled)
                _seqCo = StartCoroutine(AttackChargeCo(ap));
        }

        /// <summary>
        /// Beat 2: while the defender may still respond, the attacker charges in its archetype's language — its
        /// aura ramps every frame and a charge tell pulses. (Engine state changes are too sparse to drive this.)
        /// </summary>
        IEnumerator AttackChargeCo(ActiveCombatPresentation ap)
        {
            var nextTell = 0f;
            var shieldPlayed = false;
            while (_engine != null && _engine.ActivePresentation == ap && _seqAttack == ap)
            {
                var charge = ChargeFraction(ap);
                if (TryAttackEndpoints(ap, out var from, out var to, out var groundY, out var atkVis, out var defVis))
                {
                    _shot.From = from;
                    _shot.To = to;
                    _shot.GroundY = groundY;
                    _shot.HasEndpoints = true;
                }

                if (atkVis != null)
                    atkVis.SetAttackCharge(charge);

                // The defender braces once as the charge starts to build (face-up DEF, or a revealed Set).
                if (!shieldPlayed && charge > 0.15f && defVis != null && ap.TargetCard != null &&
                    (ap.TargetCard.Position == BattlePosition.Defense || !ap.TargetCard.FaceUp))
                {
                    shieldPlayed = true;
                    ArArenaCombatFx.PlayDefendShield(defVis.transform.position, defVis.PlayerSide);
                }

                if (_shot.HasEndpoints && charge > 0.25f && Time.unscaledTime >= nextTell)
                {
                    nextTell = Time.unscaledTime + ChargeTellInterval;
                    ArArenaCombatFx.PlayChargeTell(_shot.Kind, _shot.From, _shot.To, _shot.GroundY,
                        _shot.AttackerIsPlayer, _shot.Tint, charge);
                }

                yield return null;
            }

            _seqCo = null;
            if (_seqAttack == ap)
                EndAttackSequence(cancelLine: false);
        }

        /// <summary>
        /// Beat 3: the presentation closed. Resolved (a pass, no legal response, a non-negating reaction) → the
        /// attack flies now, behind any Spell/Trap result still on screen, and lands as the battle result plays.
        /// Negated, cut by a trap, cancelled / fizzled / replayed, or the Battle Phase over → nothing flies; the
        /// trap's own beat tells that story.
        /// </summary>
        void EndAttackSequence(bool cancelLine)
        {
            var ap = _seqAttack;
            _seqAttack = null;
            if (_seqCo != null)
            {
                StopCoroutine(_seqCo);
                _seqCo = null;
            }

            Arena?.ClearAllAttackCharges();
            if (ap == null || cancelLine || !ap.ImpactResolved || !_shot.HasEndpoints) return;
            if (_engine == null || _engine.Phase != DuelPhase.Battle) return;
            var shot = _shot;
            if (Application.isPlaying && isActiveAndEnabled && ActivationBeat.AnyResultPending)
            {
                _releasePendingUntil = Time.unscaledTime + SpellActivationPresentation.BeatHardCapSeconds;
                StartCoroutine(ReleaseAfterResultGate(shot));
            }
            else
                ReleaseAttack(shot);
        }

        IEnumerator ReleaseAfterResultGate(AttackShot shot)
        {
            var cap = Time.unscaledTime + SpellActivationPresentation.BeatHardCapSeconds;
            while (ActivationBeat.AnyResultPending && Time.unscaledTime < cap)
                yield return null;
            ReleaseAttack(shot);
        }

        void ReleaseAttack(AttackShot shot)
        {
            _releasePendingUntil = -1f;
            _lastReleaseT = Time.unscaledTime;
            _impactPlayedThisAttack = true;
            ArArenaCombatFx.PlayAttackRelease(shot.Kind, shot.From, shot.To, shot.GroundY, shot.Tint, shot.Weight);
            (shot.AttackerIsPlayer ? OppDisk : PlayerDisk)?.PlayFx(DiskFxEvent.ImpactHit);
            (shot.AttackerIsPlayer ? PlayerDisk : OppDisk)?.PlayFx(DiskFxEvent.DamageShake);
        }

        /// <summary>0→1 over the defender's decision window (the hologram impact time when that is shorter).</summary>
        static float ChargeFraction(ActiveCombatPresentation ap)
        {
            var window = Mathf.Min(ap.Profile.ImpactAtSeconds, CombatAnimTimings.DefaultResponseSeconds);
            return window <= 0.01f ? 1f : Mathf.Clamp01(ap.Elapsed / window);
        }

        /// <summary>
        /// Where this attack flies from and to: monster torso to monster torso (a revealed Set is aimed low), a
        /// direct attack at the defending duelist's disk. <paramref name="groundY"/> is the street height.
        /// </summary>
        bool TryAttackEndpoints(ActiveCombatPresentation ap, out Vector3 from, out Vector3 to, out float groundY,
            out ArArenaCardVisual atkVis, out ArArenaCardVisual defVis)
        {
            from = to = default;
            groundY = 0f;
            atkVis = defVis = null;
            if (ap?.SourceCard == null) return false;

            var attackerIsPlayer = ap.Actor != null && ap.Actor.IsPlayer;
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            if (Arena != null)
            {
                Arena.TryGetMonsterVisual(ap.SourceCard, attackerIsPlayer, out atkVis);
                if (ap.TargetCard != null)
                    Arena.TryGetMonsterVisual(ap.TargetCard, !attackerIsPlayer, out defVis);
            }

            var atkDisk = attackerIsPlayer ? PlayerDisk : OppDisk;
            var defDisk = attackerIsPlayer ? OppDisk : PlayerDisk;
            var root = Arena != null ? Arena.ArenaRoot : null;

            if (atkVis != null)
                from = atkVis.transform.position + Vector3.up * (AimHeight * s);
            else if (atkDisk != null)
                from = atkDisk.transform.position + Vector3.up * 0.25f;
            else
                return false;

            if (defVis != null)
                to = defVis.transform.position +
                     Vector3.up * ((ap.TargetCard.FaceUp ? AimHeight : FaceDownAimHeight) * s);
            else if (ap.TargetCard == null && defDisk != null)
                to = defDisk.transform.position + Vector3.up * 0.2f;
            else if (ap.TargetCard == null && root != null)
                to = root.position + (attackerIsPlayer ? root.forward : -root.forward) * 0.6f + Vector3.up * 0.3f;
            else if (root != null)
                to = root.position + Vector3.up * 0.35f;
            else
                return false;

            if (StageCamera != null)
                to = KeepOffEye(from, to, StageCamera.transform.position, Mathf.Max(0.6f, MinLandingFromEye * s));

            groundY = root != null ? root.position.y
                : atkVis != null ? atkVis.transform.position.y
                : Mathf.Min(from.y, to.y) - AimHeight * s;
            return true;
        }

        /// <summary>Walks a landing back along its path toward <paramref name="from"/> until it is
        /// <paramref name="minDist"/> from the eye (unchanged when it already is).</summary>
        static Vector3 KeepOffEye(Vector3 from, Vector3 to, Vector3 eye, float minDist)
        {
            var min2 = minDist * minDist;
            if ((to - eye).sqrMagnitude >= min2) return to;
            for (var i = 1; i <= 10; i++)
            {
                var p = Vector3.Lerp(to, from, i / 10f);
                if ((p - eye).sqrMagnitude >= min2) return p;
            }

            return from;
        }

        /// <summary>DuelUI's Mr. Referobot chip — found even while hidden (optical Salvage), whose SayNow still
        /// reaches the street caption.</summary>
        MrReferobot Announcer
        {
            get
            {
                if (_referobot == null)
                    _referobot = FindAnyObjectByType<MrReferobot>(FindObjectsInactive.Include);
                return _referobot;
            }
        }

        /// <summary>
        /// Event-driven part of an attack (engine state changes only): attacker bookkeeping, camera push, disk
        /// charge. The per-frame charge, the tells and the release belong to the attack sequence above.
        /// </summary>
        void PulseAttackCharge(ActiveCombatPresentation ap)
        {
            if (ap?.SourceCard == null) return;
            CaptureCam();

            var attackerIsPlayer = ap.Actor != null && ap.Actor.IsPlayer;
            var atkId = ap.SourceCard.InstanceId;
            if (atkId != _lastAttackerId)
            {
                _lastAttackerId = atkId;
                _impactPlayedThisAttack = false;
                Arena?.ClearAllAttackCharges();
            }

            var t = ap.ImpactFraction;
            TryAttackEndpoints(ap, out var from, out var to, out _, out _, out _);

            // Camera push toward impact point — never steal a user look/walk
            if (StageCamera != null && to != default && !UserOwnsCamera())
            {
                HoldCinematicCamera(true);
                var look = Vector3.Lerp(from != default ? from : to, to, 0.55f);
                StageCamera.transform.position = Vector3.Lerp(
                    _camBasePos,
                    Vector3.Lerp(_camBasePos, look + Vector3.up * 0.4f - StageCamera.transform.forward * 0.3f, 0.35f),
                    t * 0.4f);
                StageCamera.transform.rotation = Quaternion.Slerp(
                    _camBaseRot,
                    Quaternion.LookRotation(look - StageCamera.transform.position, Vector3.up),
                    t * 0.45f);
            }

            (attackerIsPlayer ? PlayerDisk : OppDisk)?.PlayFx(DiskFxEvent.AttackCharge);
        }

        /// <summary>
        /// A face-down Defense monster declared as an attack target keeps its sideways CARD BACK up (no face
        /// info) so the charge beams / defend shield / hit flash have a visible target. It flips at the damage
        /// step (engine face-up → battle flip). If the attack ends without that (attacker destroyed by a
        /// response, battle skipped / cancelled) it rests on its zone as its card back again.
        /// </summary>
        void TrackFaceDownAttackTarget()
        {
            CardInstance target = null;
            if (_engine.TryGetBattlingPair(out _, out var t))
                target = t;
            var wantId = target != null && !target.FaceUp ? target.InstanceId : 0;
            if (_faceDownTargetId != 0 && _faceDownTargetId != wantId)
                ReleaseFaceDownTarget();
            if (wantId == 0 || Arena == null) return;
            if (!Arena.TryGetMonsterVisual(target, true, out var vis) || vis == null) return;
            vis.RevealAsAttackTarget();
            _faceDownTargetVis = vis;
            _faceDownTargetId = wantId;
        }

        /// <summary>
        /// The attack on the revealed face-down target ended. If a Spell/Trap result is still gated (Mirror
        /// Force / Negate Attack answered inline — the engine cleared the attack before the trap is even
        /// readable), keep the card back up until that result beat, like the attacker's staged charge.
        /// </summary>
        void ReleaseFaceDownTarget(bool immediate = false)
        {
            var vis = _faceDownTargetVis;
            var id = _faceDownTargetId;
            _faceDownTargetVis = null;
            _faceDownTargetId = 0;
            if (vis == null) return;
            if (!immediate && Application.isPlaying && isActiveAndEnabled && ActivationBeat.AnyResultPending)
            {
                StartCoroutine(EndTargetRevealAfterResultGate(vis, id));
                return;
            }

            vis.EndAttackTargetReveal();
        }

        IEnumerator EndTargetRevealAfterResultGate(ArArenaCardVisual vis, int id)
        {
            var cap = Time.unscaledTime + SpellActivationPresentation.BeatHardCapSeconds;
            while (ActivationBeat.AnyResultPending && Time.unscaledTime < cap)
                yield return null;
            // Gone, flipped (battle flip owns it now), or declared as the target again: leave it.
            if (vis == null || vis.FaceUp || vis.FlipAnimating) yield break;
            if (_faceDownTargetId != 0 && _faceDownTargetId == id) yield break;
            vis.EndAttackTargetReveal();
        }

        void PulseDefendFromLog()
        {
            if (_engine == null || Arena == null) return;
            // Best-effort: pulse shield on any face-up DEF monster that just changed
            foreach (var who in new[] { _engine.Player, _engine.Opponent })
            {
                if (who == null) continue;
                for (var i = 0; i < who.MonsterZones.Length; i++)
                {
                    var m = who.MonsterZones[i].Occupant;
                    if (m == null || !m.FaceUp || m.Position != BattlePosition.Defense) continue;
                    if (Arena.TryGetMonsterVisual(m, who.IsPlayer, out var vis) && vis != null)
                        ArArenaCombatFx.PlayDefendShield(vis.transform.position, vis.PlayerSide);
                }
            }
        }

        public void PlaySummonSequence()
        {
            if (_seq != null) StopCoroutine(_seq);
            _seq = StartCoroutine(SummonCo());
        }

        IEnumerator SummonCo()
        {
            CaptureCam();
            if (StageCamera != null && Arena?.ArenaRoot != null && !UserOwnsCamera())
            {
                HoldCinematicCamera(true);
                var look = Arena.ArenaRoot.TransformPoint(
                    ArPlaymatLayout.MonsterArenaLocal(2, true)) + Vector3.up * 0.95f;
                var t = 0f;
                while (t < 1f && !UserOwnsCamera())
                {
                    t += Time.deltaTime / 0.5f;
                    var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                    StageCamera.transform.position = Vector3.Lerp(_camBasePos,
                        _camBasePos + StageCamera.transform.right * 0.06f + Vector3.up * 0.08f, u * 0.55f);
                    StageCamera.transform.rotation = Quaternion.Slerp(_camBaseRot,
                        Quaternion.LookRotation(look - StageCamera.transform.position), u * 0.65f);
                    yield return null;
                }

                if (!UserOwnsCamera())
                {
                    yield return new WaitForSeconds(0.2f);
                    t = 0f;
                    var p0 = StageCamera.transform.position;
                    var r0 = StageCamera.transform.rotation;
                    while (t < 1f && !UserOwnsCamera())
                    {
                        t += Time.deltaTime / 0.35f;
                        var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                        StageCamera.transform.position = Vector3.Lerp(p0, _camBasePos, u);
                        StageCamera.transform.rotation = Quaternion.Slerp(r0, _camBaseRot, u);
                        yield return null;
                    }
                }
            }

            HoldCinematicCamera(false);
            _seq = null;
        }

        public void PlayActivateSequence()
        {
            if (Arena?.ArenaRoot == null) return;
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            StartCoroutine(ActivateRingAfterFrame());
        }

        static readonly System.Collections.Generic.List<ActivationBeat.Beat> LiveBeats = new();

        /// <summary>
        /// Generic arena ring for an "activates" log line, one frame late so a Spell/Trap beat opened in the same
        /// engine call is visible: a Spell/Trap activation plays its own Solid Vision once, at its art reveal
        /// (ArArenaCardVisual), so the ring here — fired in the engine frame, before a face-down Trap has even
        /// risen — would both double the FX and hint at the hidden card. Other activations (monster effects)
        /// keep the ring.
        /// </summary>
        IEnumerator ActivateRingAfterFrame()
        {
            yield return null;
            ActivationBeat.CollectLive(LiveBeats);
            var spellBeatLive = LiveBeats.Count > 0;
            LiveBeats.Clear();
            if (spellBeatLive || Arena?.ArenaRoot == null) yield break;
            ArArenaCombatFx.PlaySpellCast(Arena.ArenaRoot.position + Vector3.up * 0.35f, true);
        }

        /// <summary>
        /// Destroy / damage log lines arrive in the engine frame. While a Spell/Trap's result is still
        /// hidden (ActivationBeat), the impact waits for the same result beat as the shatter / LP drop.
        /// </summary>
        void PlayImpactOnResultBeat(bool heavy)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !ActivationBeat.AnyResultPending)
            {
                PlayImpact(heavy);
                return;
            }

            StartCoroutine(ImpactAfterResultGate(heavy));
        }

        IEnumerator ImpactAfterResultGate(bool heavy)
        {
            var cap = Time.unscaledTime + SpellActivationPresentation.BeatHardCapSeconds;
            while (ActivationBeat.AnyResultPending && Time.unscaledTime < cap)
                yield return null;
            PlayImpact(heavy);
        }

        public void PlayImpact(bool heavy = true)
        {
            OppDisk?.PlayFx(DiskFxEvent.ImpactHit);
            PlayerDisk?.PlayFx(DiskFxEvent.DamageShake);
            // An attack's release lands (or is about to land) its own hit on the real target; no second burst
            // mid-field.
            if (Time.unscaledTime < _releasePendingUntil || Time.unscaledTime - _lastReleaseT < ReleaseImpactWindow)
                return;
            if (Arena?.ArenaRoot != null)
                ArArenaCombatFx.PlayImpact(Arena.ArenaRoot.position + Vector3.up * 0.35f, heavy);
        }

        void CaptureCam()
        {
            if (StageCamera == null) return;
            if (UserOwnsCamera()) return;
            if (!_camCaptured || !_engine.HasDeclaredAttack)
            {
                _camBasePos = StageCamera.transform.position;
                _camBaseRot = StageCamera.transform.rotation;
                _camCaptured = true;
            }
        }

        bool UserOwnsCamera()
        {
            if (_freeLook == null && StageCamera != null)
                _freeLook = StageCamera.GetComponent<ArFreeLookCamera>();
            return _freeLook != null && _freeLook.UserMoved;
        }

        void HoldCinematicCamera(bool on)
        {
            if (_freeLook == null) return;
            _freeLook.SuppressApply = on && !_freeLook.UserMoved;
        }

        void LateUpdate()
        {
            if (_engine == null || StageCamera == null) return;
            if (UserOwnsCamera())
            {
                _camCaptured = false;
                HoldCinematicCamera(false);
                return;
            }

            if (_engine.HasDeclaredAttack) return;
            if (_seq != null) return;
            if (!_camCaptured)
            {
                HoldCinematicCamera(false);
                return;
            }

            HoldCinematicCamera(true);
            StageCamera.transform.position = Vector3.Lerp(
                StageCamera.transform.position, _camBasePos, Time.deltaTime * 2.5f);
            StageCamera.transform.rotation = Quaternion.Slerp(
                StageCamera.transform.rotation, _camBaseRot, Time.deltaTime * 2.5f);
            if (Vector3.Distance(StageCamera.transform.position, _camBasePos) < 0.004f &&
                Quaternion.Angle(StageCamera.transform.rotation, _camBaseRot) < 0.4f)
            {
                StageCamera.transform.position = _camBasePos;
                StageCamera.transform.rotation = _camBaseRot;
                _camCaptured = false;
                HoldCinematicCamera(false);
            }
        }
    }
}
