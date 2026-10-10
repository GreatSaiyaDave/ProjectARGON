using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The AI opponent as a person (PvE): a customizable 3D duelist (<see cref="OverworldAvatar3d"/> body, look from
    /// <see cref="AiDuelistLooks"/>) standing behind the opponent disk and physically working it.
    ///
    /// Unlike the player's side (the tracked disk + floating ghost hand), everything here is a body:
    /// <list type="bullet">
    /// <item>The body stands on the street facing you, placed from the opponent forearm pose the disk used to float at,
    /// at human scale (1 m = the stage's units per metre).</item>
    /// <item>Left arm: two-bone IK puts the wrist on that pose; the disk (<see cref="ArDuelDiskRig.ExternalPose"/>) then
    /// rides the left hand bone, so it stays strapped on through a fall or a victory pose.</item>
    /// <item>Right hand: the opponent <see cref="ArFloatingHand"/> still runs the card choreography (draw from the disk's
    /// deck, fan, carry, set down on a zone) with its mesh hidden; the body's right hand is IK-solved onto its wrist every
    /// frame and the fingers curl with its grip. During a full-arm gesture the roles swap: the fan follows the body.</item>
    /// <item>Clips (<c>ArgonClips_Duel</c> from Meshy + the UAL set) play duel beats read from the engine: duel start,
    /// turn start, spell / trap activation, attack command, damage taken, a good hit, victory and defeat.</item>
    /// </list>
    /// Runs after the hand volume and the disk (execution order) so it reads this frame's choreography.
    /// </summary>
    [DefaultExecutionOrder(650)]   // after ArHandVolume (600): the fan sits on this frame's hand and the IK reads this frame's wrist pivot
    public sealed class ArOpponentDuelist : MonoBehaviour
    {
        const string DuelClipsRes = "WRLDZ/Avatar3D/ArgonClips_Duel";
        const string UalClipsRes = "WRLDZ/Avatar3D/ArgonClips_UAL";

        public IArTrackingSource Tracker;
        public ArDuelDiskRig Disk;
        public ArHandVolume Hand;
        public ArArenaHologramManager Arena;
        /// <summary>Stage units per real metre (1 life-size, the simulated tracker's value in EditorSim / phone RT).</summary>
        public float UnitsPerMetre = 1f;
        public bool LifeSize;
        /// <summary>Debug / preview: skip parts of the solve (1 left arm, 2 right arm, 4 head, 8 fingers, 16 disk follow).</summary>
        public int DebugSkip;

        OverworldAvatar3d _body;
        Animator _anim;
        Transform _root;             // scaled to UnitsPerMetre; the body (with its own height scale) hangs under it
        Transform _fanAnchor;        // the hand volume's anchor: in front of the chest
        AvatarAppearance _look;
        DuelEngine _engine;

        // ---- bones + hand frames (canonical: +Y fingers, +X thumb for the RIGHT hand, back of hand +Z)
        Transform _lUpper, _lFore, _lHand, _rUpper, _rFore, _rHand, _head, _neck, _chest, _hips;
        Quaternion _lFrameInv = Quaternion.identity, _rFrameInv = Quaternion.identity;
        readonly List<(Transform bone, Vector3 axisLocal, float fan, float pinch, float present, float relaxed)> _rFingers = new();
        readonly List<(Transform bone, Vector3 axisLocal)> _lFingers = new();
        Vector3 _headFwdLocal = Vector3.forward, _headUpLocal = Vector3.up;

        // ---- animation
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _base, _gesture;
        readonly Dictionary<string, AnimationClip> _clips = new();
        AnimationClip _idleClip;
        double _baseTime, _gestureTime;
        float _gestureWeight, _gestureLen;
        Gesture _current;
        bool _holdLastFrame;
        readonly Queue<Gesture> _queue = new();

        // ---- disk reaches: the right hand works the duel disk itself (deck, zones, graveyard)
        /// <summary>
        /// One hand move on the disk: hover above the spot, touch it (tap / pinch / slap), come back. Targets are the live
        /// disk transforms, so the hand lands where the card really is even while the disk moves with the arm.
        /// </summary>
        sealed class Reach
        {
            public string Kind;            // draw | tap | place | shuffle | grave
            public Transform Target;
            public float Dur, T;
            public float QueuedAt;
            public Vector3 Start; public bool HasStart;
            public string Then;            // a beat to play when the reach lands (e.g. "reveal" after tapping a set card)
        }
        Reach _reach;
        readonly Queue<Reach> _reaches = new();
        float _reachW;                     // 0..1 blend of the reach over the normal right-hand target

        /// <summary>Queue a disk move: kind = draw | tap | place | shuffle | grave | activate_disk (press the disk's
        /// deploy button as its blade opens); zone index for tap / place (0..4 monster, 5..9 S/T, 10 field).</summary>
        public void ReachDisk(string kind, int zone = -1, string then = null)
        {
            if (Disk == null) return;
            var t = kind switch
            {
                "draw" or "shuffle" => Disk.MainDeckZone,
                "grave" => Disk.GraveyardZone,
                "activate_disk" => _npcDisk != null ? _npcDisk.ButtonAnchor : Disk.MainDeckZone,
                _ => ZoneTransform(zone)
            };
            if (t == null) return;
            if (_reaches.Count >= 4) return;
            _reaches.Enqueue(new Reach
            {
                Kind = kind, Target = t, Then = then, QueuedAt = Time.unscaledTime,
                Dur = kind switch { "activate_disk" => 1.1f, "shuffle" => 1.8f, "draw" => 1.05f, "place" => 1.0f, "grave" => 0.9f, _ => 0.85f }
            });
        }

        Transform ZoneTransform(int zone)
        {
            if (Disk == null) return null;
            if (zone >= 0 && zone < 5) return Disk.MonsterZones[zone] != null ? Disk.MonsterZones[zone].transform : null;
            if (zone >= 5 && zone < 10) return Disk.SpellTrapZones[zone - 5] != null ? Disk.SpellTrapZones[zone - 5].transform : null;
            if (zone == 10) return Disk.FieldSpellZone != null ? Disk.FieldSpellZone.transform : null;
            return null;
        }

        public bool IsReaching => _reach != null;
        public string CurrentReach => _reach != null ? _reach.Kind : "";
        public float CurrentReachLength => _reach != null ? _reach.Dur : 0f;

        // ---- IK state
        float _wLeft = 1f, _wRight = 1f;
        Quaternion _diskLocal = Quaternion.identity;   // hand canonical → disk rotation, captured while the arm is on the disk
        bool _haveDiskLocal;
        Vector3 _rootPos; Quaternion _rootRot = Quaternion.identity; bool _placed;
        float _standbyW;
        DeployStyle _style;   // this duelist's style (standby shift), resolved on first use
        Vector3 _reachPull;
        float _tipLift;   // extra wrist height so the fingertips rest on the spot instead of sinking into the plate
        bool _gloved;     // a gloves piece (Mai, Keith) sits ~1 cm past the bare fingertip the bones describe
        readonly List<(Transform tip, Transform parent)> _rTips = new();   // how far the disk arm brings the disk in so the working hand can reach the spot
        Vector3 _chestRefPos; Quaternion _chestRefRot = Quaternion.identity; bool _haveChestRef;   // chest in the root frame, idle pose
        float _visible = 1f;

        enum Arms { None, Right, Both }

        sealed class Gesture
        {
            public string Name;
            public string[] Clips;
            public Arms Arms;
            public bool Hold;          // stay on the last frame (defeat)
            public float Hold_Pacer;   // seconds the AI waits for this beat
            public float MaxWait = 2f; // seconds it may wait for the hand to finish a draw / place
            public float Twist = 1f;   // how much of the chest's twist about the vertical carries the disk arm round
            public float QueuedAt;
        }

        // ------------------------------------------------------------------ create

        public static ArOpponentDuelist Create(Transform stageRoot, int layer, IArTrackingSource tracker, ArDuelDiskRig disk,
            ArHandVolume hand, ArArenaHologramManager arena, bool lifeSize, float unitsPerMetre, string opponentName)
        {
            if (!AiDuelistLooks.IsHumanDuelist(opponentName) || stageRoot == null)
            {
                Debug.LogWarning($"[WRLDZ AR] Opponent skip · name='{opponentName ?? "(null)"}' human={AiDuelistLooks.IsHumanDuelist(opponentName)} stageRoot={stageRoot != null}");
                return null;
            }
            var go = new GameObject("OpponentDuelist");
            go.transform.SetParent(stageRoot, false);
            go.layer = layer;
            var d = go.AddComponent<ArOpponentDuelist>();
            d.Tracker = tracker; d.Disk = disk; d.Hand = hand; d.Arena = arena;
            d.LifeSize = lifeSize; d.UnitsPerMetre = Mathf.Max(0.05f, unitsPerMetre);
            if (!d.Build(AiDuelistLooks.For(opponentName), layer))
            {
                var animDbg = d._anim == null ? "null"
                    : (d._anim.isHuman ? d._anim.name + " human" : d._anim.name + " NOT-human");
                Debug.LogWarning($"[WRLDZ AR] Opponent build failed · {opponentName} · body={d._body != null} anim={animDbg}");
                Destroy(go); return null;
            }
            // its own Battle City disk: the opponent rig worn solid on the forearm, life size
            d._npcDisk = disk != null ? NpcDuelDisk.Wear(go.transform, disk, layer, d.UnitsPerMetre) : null;
            d._name = opponentName ?? "";
            Debug.Log($"[WRLDZ AR] Opponent duelist · {opponentName} · hair {d._look.hairStyleId} · {d._look.EffectiveHeightCm:0} cm · " +
                      $"{d.UnitsPerMetre:0.00} u/m · clips {d._clips.Count} · disk {(d._npcDisk != null ? d._npcDisk.Id : "rig")}");
            return d;
        }

        NpcDuelDisk _npcDisk;
        bool _wasDeployed;
        string _name = "";
        /// <summary>Sideways stand offset in metres (a tag-team partner beside the seat's duelist).</summary>
        public float StandOffsetM;
        /// <summary>The other duelist of a tag team: victory / defeat / start beats are shared.</summary>
        public ArOpponentDuelist Partner;
        /// <summary>The duelist's own worn disk (previews / tests).</summary>
        public NpcDuelDisk OwnDisk => _npcDisk;

        bool Build(AvatarAppearance look, int layer)
        {
            _look = look;
            _root = new GameObject("DuelistRoot").transform;
            _root.SetParent(transform, false);
            _root.localScale = Vector3.one * UnitsPerMetre;
            _body = OverworldAvatar3d.AttachWorld(_root, look, layer);
            if (_body == null)
            {
                Debug.LogWarning($"[WRLDZ AR] Opponent build · base body not attached · look={(look == null ? "null" : (look.IsFemale ? "base_female" : "base_male"))}");
                return false;
            }
            _body.DisableLocomotion();
            _body.IgnoreBodyHide = true;          // the PLAYER's body hides in duels; this one is the opponent
            _body.SetPresence(1f);
            _anim = _body.GetComponent<Animator>();
            if (_anim == null || !_anim.isHuman)
            {
                var av = _anim != null ? (_anim.avatar != null ? _anim.avatar.name : "no-avatar") : "null";
                Debug.LogWarning($"[WRLDZ AR] Opponent build · animator not humanoid: anim={(_anim != null ? _anim.name : "null")} isHuman={_anim != null && _anim.isHuman} avatar={av}");
                return false;
            }
            _anim.applyRootMotion = false;
            _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _fanAnchor = new GameObject("FanAnchor").transform;
            _fanAnchor.SetParent(transform, false);

            ReadBones();
            foreach (var r in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name.Contains("_hands_")) { _gloved = true; break; }
            LoadClips();
            BuildGraph();
            if (Hand != null)
            {
                Hand.AnchorOverride = _fanAnchor;
                if (Hand.FloatingHand != null) Hand.FloatingHand.HideModel = true;
            }
            if (Disk != null) Disk.ExternalPose = true;
            return true;
        }

        void ReadBones()
        {
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            _lUpper = B(HumanBodyBones.LeftUpperArm); _lFore = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
            _rUpper = B(HumanBodyBones.RightUpperArm); _rFore = B(HumanBodyBones.RightLowerArm); _rHand = B(HumanBodyBones.RightHand);
            _head = B(HumanBodyBones.Head); _neck = B(HumanBodyBones.Neck); _hips = B(HumanBodyBones.Hips);
            _chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            _lFrameInv = Quaternion.Inverse(HandFrameLocal(_lHand, B(HumanBodyBones.LeftMiddleProximal),
                B(HumanBodyBones.LeftIndexProximal), B(HumanBodyBones.LeftLittleProximal), left: true));
            _rFrameInv = Quaternion.Inverse(HandFrameLocal(_rHand, B(HumanBodyBones.RightMiddleProximal),
                B(HumanBodyBones.RightIndexProximal), B(HumanBodyBones.RightLittleProximal), left: false));

            // finger curl axes in bone space (toward the palm); weights per grip shape: fan hold, pinch, present, relaxed
            void Finger(List<(Transform, Vector3, float, float, float, float)> dst, HumanBodyBones b0, Transform hand, bool left,
                float fan, float pinch, float present, float relaxed)
            {
                for (var k = 0; k < 3; k++)
                {
                    var bone = B((HumanBodyBones)((int)b0 + k));
                    if (bone == null || hand == null) continue;
                    var frame = hand.rotation * Quaternion.Inverse(left ? _lFrameInv : _rFrameInv);   // canonical in world
                    var fingers = frame * Vector3.up; var palm = frame * Vector3.back;
                    var axisWorld = Vector3.Cross(fingers, palm).normalized;
                    var mul = k == 0 ? 0.9f : k == 1 ? 1.1f : 0.8f;
                    dst.Add((bone, Quaternion.Inverse(bone.rotation) * axisWorld, fan * mul, pinch * mul, present * mul, relaxed * mul));
                }
            }
            var rf = new List<(Transform, Vector3, float, float, float, float)>();
            Finger(rf, HumanBodyBones.RightIndexProximal, _rHand, false, 32f, 42f, 18f, 12f);
            Finger(rf, HumanBodyBones.RightMiddleProximal, _rHand, false, 40f, 55f, 22f, 16f);
            Finger(rf, HumanBodyBones.RightRingProximal, _rHand, false, 48f, 62f, 68f, 20f);
            Finger(rf, HumanBodyBones.RightLittleProximal, _rHand, false, 52f, 66f, 72f, 24f);
            Finger(rf, HumanBodyBones.RightThumbProximal, _rHand, false, 12f, 30f, 20f, 6f);
            foreach (var f in rf) _rFingers.Add(f);
            // every right finger joint (thumb .. little, proximal .. distal) for the disk keep-out
            for (var hb = HumanBodyBones.RightThumbProximal; hb <= HumanBodyBones.RightLittleDistal; hb++)
            {
                var j = B(hb);
                if (j != null) _rHandJoints.Add((j, ((int)hb - (int)HumanBodyBones.RightThumbProximal) % 3 == 2));
            }
            foreach (var d in new[] { HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal })
            {
                var tip = B(d); var parent = B(d - 1);
                if (tip != null && parent != null) _rTips.Add((tip, parent));
            }
            var lf = new List<(Transform, Vector3, float, float, float, float)>();
            foreach (var b0 in new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal })
                Finger(lf, b0, _lHand, true, 0f, 0f, 0f, 34f);
            foreach (var f in lf) _lFingers.Add((f.Item1, f.Item2 * 1f));

            if (_head != null)
            {
                _headFwdLocal = Quaternion.Inverse(_head.rotation) * _body.transform.forward;
                _headUpLocal = Quaternion.Inverse(_head.rotation) * _body.transform.up;
            }
        }

        /// <summary>The hand's canonical frame in its bone space: +Y along the fingers, +Z out of the back of the hand.</summary>
        static Quaternion HandFrameLocal(Transform hand, Transform middle, Transform index, Transform little, bool left)
        {
            if (hand == null || middle == null || index == null || little == null) return Quaternion.identity;
            var fingers = hand.InverseTransformDirection(middle.position - hand.position).normalized;
            var thumbSide = hand.InverseTransformDirection(index.position - little.position).normalized;
            var back = left ? Vector3.Cross(fingers, thumbSide) : Vector3.Cross(thumbSide, fingers);
            return Quaternion.LookRotation(back.normalized, fingers);
        }

        // ------------------------------------------------------------------ clips

        void LoadClips()
        {
            foreach (var res in new[] { UalClipsRes, DuelClipsRes })
                foreach (var c in Resources.LoadAll<AnimationClip>(res))
                {
                    var take = c.name.Substring(c.name.LastIndexOf('|') + 1);
                    _clips[take] = c;
                }
            _idleClip = Clip("Duel_Idle", "Idle_Loop");
        }

        AnimationClip Clip(params string[] names)
        {
            foreach (var n in names) if (n != null && _clips.TryGetValue(n, out var c)) return c;
            return null;
        }

        void BuildGraph()
        {
            _graph = PlayableGraph.Create("OpponentDuelist");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // evaluated in Update (works in edit-mode previews too)
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            if (_idleClip != null)
            {
                _base = AnimationClipPlayable.Create(_graph, _idleClip);
                _base.SetApplyFootIK(true);
                _graph.Connect(_base, 0, _mixer, 0);
            }
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            var output = AnimationPlayableOutput.Create(_graph, "duelist", _anim);
            output.SetSourcePlayable(_mixer);
        }

        void PlayGestureNow(Gesture g)
        {
            var clip = Clip(g.Clips);
            if (clip == null) return;
            if (_gesture.IsValid()) { _graph.Disconnect(_mixer, 1); _gesture.Destroy(); }
            _gesture = AnimationClipPlayable.Create(_graph, clip);
            _gesture.SetApplyFootIK(true);
            _graph.Connect(_gesture, 0, _mixer, 1);
            _gestureTime = 0; _gestureLen = clip.length; _current = g; _holdLastFrame = g.Hold;
            if (g.Hold_Pacer > 0f && Application.isPlaying) DuelPresentationPacer.Extend(g.Hold_Pacer, "opponent duelist: " + g.Name);
        }

        /// <summary>Queue a duel beat by name (see <see cref="Beat"/>). Public so previews and tests can drive it.</summary>
        public void Play(string beat)
        {
            var g = Beat(beat);
            if (g == null) return;
            // a beaten duelist's disk powers down: the blade folds (and stays out of the floor while the body lies there)
            if (beat == "defeat" && _npcDisk != null) _npcDisk.Override = false;
            else if (beat == "victory" && _npcDisk != null) _npcDisk.Override = null;
            if (Partner != null && (beat == "defeat" || beat == "victory" || beat == "start")) Partner.Play(beat);
            if (g.Hold) _queue.Clear();   // the end of the duel overrides everything
            g.QueuedAt = Time.unscaledTime;
            if (_queue.Count < 3) _queue.Enqueue(g);
        }

        static Gesture Beat(string name) => name switch
        {
            // the disk arm stays on its pose (the torso carries it): the clip's own left arm lowered the open blade through the thigh
            "start" => new Gesture { Name = name, Clips = new[] { "Duel_DuelStart", "Duel_BattlePose", "Spell_Simple_Enter" }, Arms = Arms.Right },
            "turn" => new Gesture { Name = name, Clips = new[] { "Duel_Taunt", "Duel_Scheme", "Idle_Talking_Loop" }, Arms = Arms.Right, Hold_Pacer = 1.2f },
            "activate" => new Gesture { Name = name, Clips = new[] { "Duel_Cast", "Spell_Simple_Shoot" }, Arms = Arms.Right, Hold_Pacer = 0.6f },
            "reveal" => new Gesture { Name = name, Clips = new[] { "Duel_Reveal", "Duel_Cast", "Spell_Simple_Shoot" }, Arms = Arms.Right, Hold_Pacer = 0.6f },
            // the disk arm holds still while the casting arm sweeps across the body: carried round with the twist, the hub
            // met the sweeping hand (Rex, Bonz)
            "summon_big" => new Gesture { Name = name, Clips = new[] { "Duel_CastBig", "Duel_Cast", "Spell_Simple_Shoot" }, Arms = Arms.Right, Hold_Pacer = 0.8f, Twist = 0.35f },
            "attack" => new Gesture { Name = name, Clips = new[] { "Duel_AttackCommand", "Punch_Jab" }, Arms = Arms.Right, MaxWait = 0.3f },
            "hit" => new Gesture { Name = name, Clips = new[] { "Duel_Hit", "Hit_Chest" }, Arms = Arms.None, MaxWait = 0.2f },
            "hit_big" => new Gesture { Name = name, Clips = new[] { "Duel_HitBig", "Hit_Head", "Duel_Hit" }, Arms = Arms.Right, MaxWait = 0.2f },
            "good" => new Gesture { Name = name, Clips = new[] { "Duel_FistPump", "Spell_Simple_Shoot" }, Arms = Arms.Right },
            "wait" => new Gesture { Name = name, Clips = new[] { "Duel_HandOnHip", "Duel_Scheme", "Idle_Talking_Loop" }, Arms = Arms.Right, MaxWait = 0.5f },
            // the disk arm stays on its pose: lifting the disk overhead drove the blade through tall anime hair
            "victory" => new Gesture { Name = name, Clips = new[] { "Duel_Victory", "Dance_Loop" }, Arms = Arms.Right, Hold = true },
            // the disk arm stays on IK (limp at the side, then on the street beside the fallen body; see Solve): released
            // to the clip, the open or folding blade swung through the head and hair
            "defeat" => new Gesture { Name = name, Clips = new[] { "Duel_Defeat", "Death" }, Arms = Arms.Right, Hold = true },
            _ => null
        };

        /// <summary>The playing beat's clip (previews and tests).</summary>
        public string CurrentGestureName => _current != null && _gesture.IsValid() ? _gesture.GetAnimationClip().name : "";
        public float CurrentGestureLength => _current != null ? _gestureLen : 0f;

        /// <summary>Back to idle, arms on the disk and cards (edit-mode previews after an end-of-duel beat).</summary>
        public void ResetForPreview()
        {
            if (_npcDisk != null) _npcDisk.Override = null;
            _queue.Clear();
            _current = null;
            _holdLastFrame = false;
            _gestureWeight = 0f;
            _wLeft = _wRight = 1f;
        }

        /// <summary>Debug: root / hips / chest / head orientation this frame (world pitch, yaw, roll).</summary>
        public string DebugPose()
        {
            string E(Transform t) => t == null ? "-" : t.name.Replace("mixamorig:", "") + " " + t.eulerAngles.ToString("0");
            var hips = _anim != null ? _anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            return $"root {_root.eulerAngles:0} | {E(hips)} | {E(_chest)} | {E(_head)} | body {_body.transform.eulerAngles:0} scale {_body.transform.lossyScale.y:0.000}";
        }

        bool HandBusy => Hand != null && Hand.FloatingHand != null && Hand.FloatingHand.IsBusy;

        // ------------------------------------------------------------------ deploy cinematic

        /// <summary>
        /// One duelist's disk deploy, after the anime: the disk arm winds up across the chest, snaps out into the
        /// character's pose as the blade opens, holds while the right arm plays the character's gesture, then settles
        /// back onto the disk pose. Wrist targets are metres from the left shoulder in the body frame
        /// (x = the duelist's right, y = up, z = toward the opponent); Fingers / Back give the hand's frame there.
        /// </summary>
        sealed class DeployStyle
        {
            public string Name;
            public Vector3 Thrust, Fingers, Back;
            // the wind-up: by default the standby pose itself (forearm forward at the waist, plate out): the folded halves lie
            // along the forearm past the elbow, so a wind-up across the belly put one of them into the chest
            public Vector3 Wind = StandbyPos, WindFingers = StandbyFingers, WindBack = StandbyBack;
            public string[] Clips;         // the character's right arm / body gesture, once the plate has locked
            // the free arm while the halves sweep: kept behind the plate's plane (fist low at the hip, Yugi's GIF frames 1-8)
            public string[] ActClips = { "Duel_Idle" };
            public bool ActFist = true;    // that free hand clenched
            // the free hand placed by IK during the activation (metres from the RIGHT shoulder, x = the duelist's right, y up,
            // z toward the opponent), with its fingers / back-of-hand directions; zero = the activation clip owns the arm
            public Vector3 CardHand, CardFingers, CardBack;
            // seconds the pose is held after the plate locks (the hold before it follows the disk's own mechanism)
            public float AfterLock = 0.8f;
            public float Bob;              // metres of breathing on the held arm (0 = rigid)
            public bool OpenHand;          // the disk hand opens into a claw instead of the strap grip (Yami Marik)
            // the folded-disk standby (before the duel; the wind-up starts here): the shared pose unless a body needs its own
            public Vector3 Standby = StandbyPos, StandbyFingersDir = StandbyFingers, StandbyBackDir = StandbyBack;
            // extra room for the disk while a duel beat plays (x = further out to the side, z = further forward), and a
            // shift of the defeat slump (metres): long hair that falls to the waist fills the space a lean swings into.
            // (Mai's defeat: every limp-arm slump crosses her hair as the blade folds; only an arm held up and forward
            // cleared it, which no beaten duelist does — left at the natural slump.)
            public Vector3 GestureRoom;
            /// <summary>Defeat: how far out from the feet the disk arm lands on the street (70°; nearer the hip the folded
            /// halves met a loose sleeve at the elbow; waist-length hair fans round the head and shoulders on the ground, so
            /// that arm lands at 55°).</summary>
            public float DefeatArmDeg = 70f;
            /// <summary>A standby of its own, which is also where the wind-up starts.</summary>
            public DeployStyle WithStandby(Vector3 pos, Vector3 fingers, Vector3 back)
            {
                Standby = Wind = pos; StandbyFingersDir = WindFingers = fingers; StandbyBackDir = WindBack = back;
                return this;
            }
            public bool RightFist;         // the free hand clenches into a fist while the clip plays
            public float OpenDelay = 0.14f; // seconds after the wind-up before the blade starts (0.34 = as the pose lands)
            // optional second wrist pose the hold eases into while the blade sweeps (zero Present = stay on Thrust)
            public Vector3 Present, PresentFingers, PresentBack;
        }

        // standby (disk folded): wrist forward of the hip and out from the side, fingers forward, the plate facing out, so
        // the folded halves lie above and below the forearm clear of the body (facing down, the lower half went into the thigh)
        static readonly Vector3 StandbyPos = new(-0.24f, -0.38f, 0.32f), StandbyFingers = new(-0.25f, -0.12f, 1f), StandbyBack = new(-1f, 0.15f, -0.2f);
        // a standby for a long coat or hair that falls to the waist (both fill the space behind the elbow, where the folded
        // halves of the shared standby reach back to): the forearm upright in front of the chest, plate facing out (Bonz's
        // Battle City frame), so the halves hang down past the elbow in front of the belly
        static readonly Vector3 UprightStandbyPos = new(0.06f, -0.20f, 0.34f), UprightStandbyFingers = new(0.1f, 1f, 0.25f), UprightStandbyBack = new(0f, 0.1f, 1f);
        // the Battle City stance: forearm out toward the opponent and a little down, plate tipped up toward them, so the
        // blade stands out level to the duelist's left (the disk's blade runs across the forearm, away from the body)
        static readonly Vector3 ShieldFingers = new(0.12f, -0.35f, 1f);
        // the activation pose where no reference shows the moment itself: forearm out toward the opponent at the chest, the
        // plate facing them (its plane ~40 cm in front of the body, clear of the halves' sweep); the referenced stance
        // follows the lock as Present
        static readonly Vector3 ForwardPos = new(0.02f, -0.17f, 0.40f), ForwardBack = new(0f, 0.35f, 1f);
        // the forearm level across the navel (Joey's FBK art, Weevil, Mai): elbow bent ~90° at the side, the wrist at elbow
        // height in front of the navel, zones up and tipped toward the opponent. Further out or lower, the arm ran out of
        // reach, straightened, and the plate slanted up toward the shoulder.
        static readonly Vector3 LevelPos = new(0.16f, -0.24f, 0.31f), LevelFingers = new(0.8f, 0f, 0.6f);

        /// <summary>
        /// Deploy poses by character, each from an anime / game frame (scratchpad refs of 2026-10-04, listed per style);
        /// characters with no frame of them wearing a Battle City disk get the default stance. The pose is struck as the
        /// disk opens and held rigid through the whole activation with the plate facing out ~35 cm in front of the body:
        /// the halves sweep a ~40 cm disc round the hub in the plate's plane, and a plate tipped up at the hips swept it
        /// through the legs, hips and long hair. Once the plate has locked the character's gesture plays and the arm eases
        /// into the referenced stance (<see cref="DeployStyle.Present"/>).
        /// </summary>
        static DeployStyle StyleFor(string opponentName)
        {
            var n = (opponentName ?? "").ToLowerInvariant();
            bool Has(string k) => n.Contains(k);
            // Kaiba (Battle City, the synchronized activation beside Yami Yugi; tenor P3cdDqH9Nhw): a card raised beside the
            // head in the right hand, the disk forearm angled down across the body to the right hip, plate facing out;
            // then squared up, forearm level across the waist (same clip, and the Battle City walk-in mMckTRjOTSI).
            if (Has("kaiba")) return new DeployStyle
            {
                // with this kit's blade both referenced disk stances (diagonal to the hip, level across the waist) put the
                // plate inside his open coat (the flared front and tails fill the space round the hips: 270-570 edges), so
                // the disk arm stays out front; the card raised beside the head is his
                // 12 cm further to his left than the default forward pose: his idle right hand sat under the plate's end
                Name = "kaiba", Thrust = ForwardPos + new Vector3(-0.12f, 0.02f, 0.02f), Fingers = ShieldFingers, Back = ForwardBack,
                // no second gesture after the lock: Duel_BattlePose / Duel_HandOnHip brought the raised hand down through the plate
                // the card held up beside the head, elbow bent (Duel_Reveal threw the arm overhead and brought it down
                // across his face at the lock)
                CardHand = new(-0.04f, 0.05f, 0.28f), CardFingers = new(-0.15f, 1f, 0.15f), CardBack = new(0.35f, 0f, 1f),
                ActClips = new[] { "Duel_Idle" }, ActFist = false, Clips = new string[0], AfterLock = 1.1f
            }.WithStandby(UprightStandbyPos, UprightStandbyFingers, UprightStandbyBack);
            // Joey (Falsebound Kingdom marshal art, the only frame of him holding the disk): forearm level across the navel,
            // fist to his right, plate up; the right fist pumped to head height (Battle City, tenor ZccTk-XPVuQ).
            if (Has("joey") || Has("wheeler")) return new DeployStyle
            {
                Name = "joey", Thrust = ForwardPos, Fingers = ShieldFingers, Back = ForwardBack,
                Present = LevelPos, PresentFingers = LevelFingers, PresentBack = new(0f, 0.9f, 0.42f),
                Clips = new[] { "Duel_FistPump", "Duel_BattlePose" }, RightFist = true, AfterLock = 1.2f
            };
            // Mai (Waking the Dragons disk only, tenor Md-xyY4SFwc): disk level across the hips, a card held up beside the head.
            if (Has("mai") || Has("valentine")) return new DeployStyle
            {
                // her hair falls to the waist all round and the bust sits forward: everything held ~10 cm further out
                Name = "mai", Thrust = ForwardPos + new Vector3(0f, 0f, 0.08f), Fingers = ShieldFingers, Back = ForwardBack,
                Present = LevelPos + new Vector3(0f, -0.02f, 0.02f), PresentFingers = LevelFingers, PresentBack = new(0f, 0.9f, 0.42f),
                Clips = new[] { "Duel_Reveal", "Duel_HandOnHip" }, AfterLock = 1.1f,
                GestureRoom = new(0.12f, 0f, 0.16f), DefeatArmDeg = 55f
            }.WithStandby(UprightStandbyPos + new Vector3(0f, 0f, 0.10f), UprightStandbyFingers, UprightStandbyBack);
            // Yami Marik (Battle City finals, tenor nwkhbaoCNBA): the disk arm thrust at the opponent at shoulder height, the
            // hand an open claw, palm down; the other arm hangs.
            if (Has("marik") || Has("malik")) return new DeployStyle
            {
                Name = "marik", Thrust = new(-0.02f, -0.04f, 0.52f), Fingers = new(0f, -0.1f, 1f), Back = new(0f, 1f, 0.1f),
                Clips = new[] { "Duel_Scheme", "Duel_Taunt" }, AfterLock = 1.0f, OpenHand = true
            };
            // Yami Bakura (Battle City finals, tenor 9oWS1VRp8y0): the disk arm low at the left hip with a card in the hand,
            // the right index finger pointed straight at the opponent.
            if (Has("bakura")) return new DeployStyle
            {
                Name = "bakura", Thrust = new(-0.06f, -0.44f, 0.36f), Fingers = new(0.15f, -0.45f, 1f), Back = new(-0.25f, 0.2f, 1f),
                Present = new(-0.06f, -0.50f, 0.30f), PresentFingers = new(0.15f, -0.45f, 1f), PresentBack = new(-0.55f, 0.6f, 0.45f),
                Clips = new[] { "Duel_AttackCommand", "Duel_Scheme" }, AfterLock = 1.3f
            };
            // Weevil (Battle City, fandom Weevil_Yu-Gi-Oh_064): forearm level across the belly, plate tilted up.
            if (Has("weevil")) return new DeployStyle
            {
                Name = "weevil", Thrust = ForwardPos, Fingers = ShieldFingers, Back = ForwardBack,
                Present = LevelPos, PresentFingers = LevelFingers, PresentBack = new(0f, 0.85f, 0.5f),
                Clips = new[] { "Duel_Scheme", "Duel_Taunt" }, AfterLock = 1.1f
            };
            // Rex (Waking the Dragons disk only, fandom ep. 160): forearm raised level across the chest, fist by the right
            // shoulder, plate facing out; the right fist clenched low.
            if (Has("rex") || Has("raptor")) return new DeployStyle
            {
                Name = "rex", Thrust = new(0.28f, -0.14f, 0.34f), Fingers = new(1f, 0.05f, 0.1f), Back = new(0f, 0.15f, 1f),
                Clips = new[] { "Duel_Idle", "Duel_BattlePose" }, RightFist = true
            }.WithStandby(UprightStandbyPos, UprightStandbyFingers, UprightStandbyBack);
            // Bonz (Battle City, fandom Bonz.png): the disk upright in front of the chest, plate facing out.
            if (Has("bonz")) return new DeployStyle
            {
                Name = "bonz", Thrust = new(0.10f, -0.02f, 0.30f), Fingers = new(0.1f, 1f, 0.1f), Back = new(0f, 0.1f, 1f),
                Clips = new[] { "Duel_Scheme", "Duel_Taunt" }
            };
            // Yami Yugi (the owner's 12-frame GIF + tenor P3cdDqH9Nhw): the forearm up, fist beside the head, plate facing
            // out, the right fist low, held rigid while the halves slide, meet and swing (frames 1-8); once the plate has
            // locked the arm comes down, the plate level across the chest under the chin (frames 9-12).
            if (Has("yugi") || Has("yami")) return new DeployStyle
            {
                Name = "yugi",
                // wrist beside the head bone (this rig's head sits barely above the shoulder) and forward of the hair: a
                // straight arm overhead put the plate through the spikes
                Thrust = new(0.00f, 0.12f, 0.24f), Fingers = new(-0.35f, 0.78f, 0.40f), Back = new(0.00f, 0.12f, 1f),
                Present = new(0.04f, -0.02f, 0.32f), PresentFingers = new(-0.12f, 0.95f, 0.22f), PresentBack = new(0f, 0.08f, 1f),
                Clips = new[] { "Duel_Idle", "Duel_BattlePose" }, AfterLock = 1.3f, OpenDelay = 0.34f, RightFist = true
            // from the upright standby the snap is a straight lift: from the hip, the folded halves swept the chest on the way up
            }.WithStandby(UprightStandbyPos, UprightStandbyFingers, UprightStandbyBack);
            // no frame of these duelists wearing a Battle City disk was found (Pegasus never wears one in Duel Monsters;
            // Keith, Mako, the Paradox Brothers and Tea only appear disk-less in Duelist Kingdom): the plain ready stance
            return new DeployStyle { Name = "default", Thrust = ForwardPos, Fingers = ShieldFingers, Back = ForwardBack, Clips = new[] { "Duel_BattlePose", "Duel_DuelStart" } };
        }

        // The disk's activation beats (fractions of DiskFxDriver.MechOpen, SpiritDuelerMechanics' timeline): the halves
        // meet beside the deck at MeetMech (a machined clack), the joined plate finishes its swing at SwingEndMech and
        // locks at LockMech (jolt + zone sweep). The body holds its pose through all of it and reacts on those beats.
        const float MeetMech = 0.60f, SwingEndMech = 0.93f, LockMech = 0.95f;
        const float SettleSeconds = 0.8f, PresentSeconds = 0.7f;

        sealed class DeployRun
        {
            public DeployStyle Style;
            public float T;
            public bool Opened, Gestured, LockGestured;
            public float Mech;                           // the disk's mechanism this frame (0 standby .. 1 locked)
            public float PresentW;                       // 0..1 eased into the post-lock stance
            public float MeetT = -1f, LockT = -1f;       // clock time the halves met / the plate locked
            public float SnapAt => 0.5f;                 // wind-up done, arm starts the snap
            public float OpenAt => SnapAt + Style.OpenDelay; // blade opens (Yugi: as the raised fist lands)
            /// <summary>Lock time on the nominal timeline (the real one comes from the disk: <see cref="LockT"/>).</summary>
            public float NominalLockAt => OpenAt + LockMech * DiskFxDriver.DeploySeconds;
            public float HoldUntil => (LockT >= 0f ? LockT : NominalLockAt) + Style.AfterLock;
            public float End => HoldUntil + SettleSeconds;
        }
        DeployRun _deployRun;

        /// <summary>This duelist's deploy style (previews / tests).</summary>
        public string DeployStyleName => StyleFor(_name).Name;
        public bool IsDeploying => _deployRun != null;
        /// <summary>Seconds into the running deploy cinematic on this duelist's own clock (-1 when none).</summary>
        public float DeployElapsed => _deployRun != null ? _deployRun.T : -1f;
        /// <summary>Seconds into the cinematic when the pose is struck (the blade is open and the hold begins).</summary>
        public float DeployPoseAt => 0.5f + 0.34f;
        /// <summary>
        /// Seconds of the cinematic worth showing in the cut-in: the whole activation, the lock, and the post-lock pose
        /// (Yugi's lowered present) — everything but the settle back onto the duel pose.
        /// </summary>
        public float DeployShowSeconds => _deployRun != null ? _deployRun.HoldUntil : StyleRunLength(StyleFor(_name)) - SettleSeconds;

        static float StyleRunLength(DeployStyle st) => new DeployRun { Style = st }.End;

        /// <summary>The disk's mechanism for the running deploy: the rig's own MechOpen (preview: the debug pose), else the clock.</summary>
        float DeployMech(DeployRun r)
        {
            if (!r.Opened) return 0f;
            if (SpiritDuelerMechanics.DebugPose >= 0f) return Mathf.Clamp01(SpiritDuelerMechanics.DebugPose);
            if (Disk != null && Disk.Fx != null && Application.isPlaying) return Disk.Fx.MechOpen;
            return Mathf.Clamp01((r.T - r.OpenAt) / DiskFxDriver.DeploySeconds);
        }

        /// <summary>
        /// Start the pre-duel deploy cinematic; returns its length in seconds. The opponent rig deploys on the snap
        /// (this duelist's own disk model opens its blade there).
        /// </summary>
        public float PlayDeployCinematic()
        {
            _deployRun = new DeployRun { Style = StyleFor(_name) };
            _wasDeployed = true;                 // the snap replaces the "press the button" reach
            if (_npcDisk != null) _npcDisk.Override = false;
            Debug.Log($"[WRLDZ AR] Opponent deploy cinematic · {_name} · style {_deployRun.Style.Name} · {_deployRun.End:0.0}s");
            return _deployRun.End;
        }

        /// <summary>
        /// A close-up for the deploy cut-in: an eye in front of the duelist at chest height, a little to its disk side,
        /// looking at the upper body. False until the body has been placed.
        /// </summary>
        public bool TryGetCutInShot(out Vector3 eye, out Vector3 look, out float heightWorld)
        {
            eye = look = default; heightWorld = 0f;
            if (!_placed || _body == null || _head == null) return false;
            var s = UnitsPerMetre * (_look != null ? _look.HeightScale : 1f);
            var facing = _rootRot * Vector3.forward; var right = _rootRot * Vector3.right;
            var chest = _chest != null ? _chest.position : _rootPos + Vector3.up * (1.2f * s);
            // frame head to waist with room above for a raised disk blade
            // Yugi's splash holds the disk beside the head: frame that, not the waist
            var splash = DeployStyleName == "yugi" && _deployRun != null;
            var headW = splash ? 0.78f : 0.45f;
            look = Vector3.Lerp(chest, _head.position, headW) - right * ((splash ? 0.04f : 0.16f) * s) + Vector3.up * ((splash ? 0.16f : 0.06f) * s);
            eye = look + facing * ((splash ? 1.85f : 2.5f) * s) - right * ((splash ? 0.22f : 0.40f) * s) + Vector3.up * ((splash ? 0.02f : 0.05f) * s);
            heightWorld = (splash ? 1.35f : 1.1f) * s;
            return true;
        }

        public string DisplayName => Partner != null && !string.IsNullOrEmpty(Partner._name) ? _name + " & " + Partner._name : _name;

        /// <summary>The opening riffle on this duelist's own deck (pre-duel shuffle phase).</summary>
        public void PlayOpeningShuffle() => ReachDisk("shuffle");

        /// <summary>
        /// The deploy cinematic's left wrist target this frame, and how much it overrides the disk pose (0..1).
        /// Also fires its events (gesture, blade open) and ends it.
        /// </summary>
        float DeployTarget(float dt, float scale, Vector3 facing, Vector3 right, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var r = _deployRun;
            if (r == null || _lUpper == null) return 0f;
            r.T += dt;
            var st = r.Style;
            if (!r.Gestured && r.T >= r.SnapAt - 0.05f)
            {
                r.Gestured = true;
                // while the halves sweep the free arm stays out of the plate's plane (the activation clip); the character's
                // own gesture plays once the plate has locked
                EnqueueGesture(new Gesture { Name = "deploy", Clips = st.ActClips, Arms = Arms.Right, MaxWait = 0.2f });
            }
            if (!r.Opened && r.T >= r.OpenAt)
            {
                r.Opened = true;
                if (_npcDisk != null) _npcDisk.Override = true;
                Disk?.DeployForDuel(0f);
            }
            r.Mech = DeployMech(r);
            if (r.MeetT < 0f && r.Mech >= MeetMech) r.MeetT = r.T;
            if (r.LockT < 0f && r.Mech >= LockMech) r.LockT = r.T;
            // a disk that never locks (no mechanism, a stalled rig) must not hold the duelist forever
            if (r.LockT < 0f && r.T > r.NominalLockAt + 1.5f) r.LockT = r.T;
            if (r.LockT >= 0f && !r.LockGestured)
            {
                r.LockGestured = true;
                if (st.Clips != null && st.Clips.Length > 0) EnqueueGesture(new Gesture { Name = "deploy_lock", Clips = st.Clips, Arms = Arms.Right, MaxWait = 0.2f });
            }
            if (r.T >= r.End)
            {
                _deployRun = null;
                if (_npcDisk != null) _npcDisk.Override = null;
                return 0f;
            }

            Vector3 W(Vector3 v) => right * v.x + Vector3.up * v.y + facing * v.z;
            var shoulder = _lUpper.position;
            Vector3 wind = shoulder + W(st.Wind) * scale, thrust = shoulder + W(st.Thrust) * scale;
            var windRot = Quaternion.LookRotation(W(st.WindBack).normalized, W(st.WindFingers).normalized);
            var thrustRot = Quaternion.LookRotation(W(st.Back).normalized, W(st.Fingers).normalized);
            if (r.T < r.SnapAt) { pos = wind; rot = windRot; }
            else
            {
                // the snap: ease-out-back (fast out, a small overshoot that settles) - anime smear timing without a pop
                var k = Mathf.Clamp01((r.T - r.SnapAt) / 0.34f);
                const float c1 = 1.2f, c3 = c1 + 1f;
                var e = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
                // the splash turns the plate as the fist arrives; other styles keep the slower twist
                var eRot = st.Present.sqrMagnitude > 1e-4f ? Mathf.Clamp01(e) : k * k * k * (k * (k * 6f - 15f) + 10f);
                // forward first, then across / up: a straight line swung the folded halves (they lie along the forearm past
                // the elbow) through the hips on the way
                var ctrl = (wind + thrust) * 0.5f + facing * (0.28f * scale);
                pos = Vector3.LerpUnclamped(Vector3.LerpUnclamped(wind, ctrl, e), Vector3.LerpUnclamped(ctrl, thrust, e), e);
                rot = Quaternion.Slerp(windRot, thrustRot, eRot);
                if (st.Bob > 0f) pos += Vector3.up * (Mathf.Sin((r.T - r.SnapAt) * 5.5f) * st.Bob * scale);
                // the anime holds the raised pose rigid while the halves slide round the hub, meet and the plate swings
                // (GIF frames 1-8); only once the plate has locked does the arm come down level (frames 9-12)
                if (st.Present.sqrMagnitude > 1e-4f && r.LockT >= 0f)
                {
                    var u = Mathf.Clamp01((r.T - r.LockT) / PresentSeconds);
                    u = u * u * u * (u * (u * 6f - 15f) + 10f);
                    var present = shoulder + W(st.Present) * scale;
                    var presentRot = Quaternion.LookRotation(W(st.PresentBack).normalized, W(st.PresentFingers).normalized);
                    r.PresentW = u;
                    pos = Vector3.Lerp(pos, present, u);
                    rot = Quaternion.Slerp(rot, presentRot, u);
                }
                // the disk's beats felt in the arm: a small recoil as the halves clack together beside the deck, and the
                // lock jolt (the kit's own LockJoltDeg twitch) pushing the forearm down a touch, both damped out in ~0.3 s
                static float Kick(float since, float len) => since < 0f || since > len ? 0f : Mathf.Sin(since / len * Mathf.PI * 2f) * (1f - since / len);
                var meet = r.MeetT >= 0f ? Kick(r.T - r.MeetT, 0.26f) : 0f;
                var jolt = r.LockT >= 0f ? Kick(r.T - r.LockT, 0.32f) : 0f;
                pos += (-W(st.Back).normalized * (0.010f * meet) - Vector3.up * (0.018f * jolt)) * scale;
                rot = Quaternion.AngleAxis(4f * jolt, right) * rot;
            }
            // blend in over the wind-up, out over the settle
            static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
            var wIn = Smoother(r.T / 0.4f);
            var wOut = r.T > r.HoldUntil ? 1f - Smoother((r.T - r.HoldUntil) / (r.End - r.HoldUntil)) : 1f;
            return wIn * wOut;
        }

        void EnqueueGesture(Gesture g)
        {
            g.QueuedAt = Time.unscaledTime;
            _queue.Clear();
            _queue.Enqueue(g);
        }

        // ------------------------------------------------------------------ engine beats

        int _lastTurn = -1, _oppLp, _youLp, _oppMonsters, _oppHand;
        /// <summary>Opponent field snapshot per zone (0..4 monsters, 5..9 spell / trap, 10 field): card, face-up, position.</summary>
        readonly (int id, bool up, BattlePosition pos)[] _zones = new (int, bool, BattlePosition)[11];
        bool _wasOppTurn, _endPlayed;
        float _lastAttackStarted = -1f, _nextWaitAt;
        readonly HashSet<int> _seenActivations = new();
        readonly List<SpellActivationPresentation.Event> _acts = new();

        public void Bind(DuelEngine engine)
        {
            if (ReferenceEquals(engine, _engine)) return;
            _engine = engine;
            if (_npcDisk != null) _npcDisk.Bind(engine);
            _queue.Clear();
            _seenActivations.Clear();
            _endPlayed = false;
            if (engine == null) return;
            _lastTurn = engine.TurnNumber;
            _oppLp = engine.Opponent != null ? engine.Opponent.LifePoints : 0;
            _youLp = engine.Player != null ? engine.Player.LifePoints : 0;
            _oppMonsters = CountMonsters(engine.Opponent);
            _wasOppTurn = engine.TurnPlayer != null && !engine.TurnPlayer.IsPlayer;
            _oppHand = engine.Opponent != null ? engine.Opponent.Hand.Count : 0;
            SnapshotZones(engine.Opponent, null);
            // the pre-duel cinematic (PreDuelCinematic) runs the deploy pose and the shuffle itself
            if (engine.TurnNumber <= 1 && !engine.OpeningSequenceActive) Play("start");
            _nextWaitAt = Time.unscaledTime + 10f;
        }

        static FieldZone ZoneOf(DuelistState d, int i) =>
            i < 5 ? d.MonsterZones[i] : i < 10 ? d.SpellTrapZones[i - 5] : d.FieldSpellZone;

        /// <summary>Record the field; with <paramref name="onChange"/> report each zone that changed (zone, was-there-before).</summary>
        void SnapshotZones(DuelistState d, System.Action<int, CardInstance, bool> onChange)
        {
            if (d?.MonsterZones == null || d.SpellTrapZones == null) return;
            for (var i = 0; i < _zones.Length; i++)
            {
                var z = ZoneOf(d, i);
                var c = z != null ? z.Occupant : null;
                var now = c != null ? (c.InstanceId, c.FaceUp, c.Position) : (0, false, BattlePosition.Attack);
                var was = _zones[i];
                if (onChange != null && c != null && (was.id != now.Item1 || was.up != now.Item2 || was.pos != now.Item3))
                    onChange(i, c, was.id == now.Item1);
                _zones[i] = now;
            }
        }

        /// <summary>A card changed on the opponent's field: the hand works the disk if no carried-card gesture is doing it.</summary>
        void OnZoneChanged(int zone, CardInstance card, bool sameCard)
        {
            var dz = zone < 5 ? Disk?.MonsterZones[zone] : zone < 10 ? Disk?.SpellTrapZones[zone - 5] : Disk?.FieldSpellZone;
            if (dz != null && dz.IncomingGesture) return;          // the card hand is already setting it down
            if (_reaches.Count > 0 || _reach != null) return;       // an activation already queued a move here
            // flip / position change: tap the card; a card that arrived without a carry: set it down on the zone
            ReachDisk(sameCard ? "tap" : "place", zone);
        }

        static int CountMonsters(DuelistState d)
        {
            if (d?.MonsterZones == null) return 0;
            var n = 0;
            foreach (var m in d.MonsterZones) if (m != null && !m.IsEmpty) n++;
            return n;
        }

        /// <summary>One-shot Spells the hand is revealing: the cast plays once the reveal is done (id, queued, seen revealing).</summary>
        readonly List<(int id, float at, bool seen)> _castAfterReveal = new();

        void TickCastAfterReveal()
        {
            for (var i = _castAfterReveal.Count - 1; i >= 0; i--)
            {
                var (id, at, seen) = _castAfterReveal[i];
                var revealing = Hand != null && Hand.IsRevealing(id);
                if (revealing && !seen) { _castAfterReveal[i] = (id, at, true); continue; }
                // done revealing, or the hand never took it up (half a second's grace for its sync)
                if (revealing || (!seen && Time.unscaledTime - at < 0.5f)) continue;
                _castAfterReveal.RemoveAt(i);
                Play("activate");
            }
        }

        void PollEngine()
        {
            var e = _engine;
            if (e == null || e.Opponent == null || e.Player == null) return;
            if (e.GameOver)
            {
                if (!_endPlayed) { _endPlayed = true; Play(e.Winner == e.Opponent ? "victory" : "defeat"); }
                return;
            }
            var oppTurn = e.TurnPlayer != null && !e.TurnPlayer.IsPlayer;
            if (oppTurn && (!_wasOppTurn || e.TurnNumber != _lastTurn)) Play("turn");
            _wasOppTurn = oppTurn;
            _lastTurn = e.TurnNumber;

            // spell / trap activations by the opponent (the activation bus the disk and arena use)
            _acts.Clear();
            SpellActivationPresentation.CollectActive(_acts);
            foreach (var a in _acts)
            {
                if (a.PlayerSide || !_seenActivations.Add(a.InstanceId)) continue;
                var zi = a.ZoneIndex >= 0 ? 5 + a.ZoneIndex : a.ZoneIndex == -1 ? 10 : -1;
                if (zi < 0) { Play("activate"); continue; }
                // a set card was already there (flip it with a tap); otherwise it comes from the hand (slap it down)
                var wasSet = _zones[zi].id == a.InstanceId;
                // a one-shot Spell from the hand is not set down: the hand shows it to you (ArFloatingHand.PlayReveal,
                // which this right arm follows), then the cast
                if (!wasSet && ArSnapRules.RevealsFromHand(e.Database != null ? e.Database.Get(a.CardId) : null))
                {
                    // the cast waits for the reveal: started now, the clip took the arm (and the fan with it) away
                    // before the card was shown
                    _castAfterReveal.Add((a.InstanceId, Time.unscaledTime, false));
                    continue;
                }
                ReachDisk(wasSet ? "tap" : "place", zi, wasSet ? "reveal" : "activate");
            }

            // the field and the hand: flips, position changes, cards arriving without a carry; a draw with no card hand
            SnapshotZones(e.Opponent, OnZoneChanged);
            var hand = e.Opponent.Hand.Count;
            if (hand > _oppHand && (Hand == null || Hand.FloatingHand == null || !Hand.FloatingHand.IsLive)) ReachDisk("draw");
            _oppHand = hand;

            // attack declared by the opponent
            var ap = e.ActivePresentation;
            if (ap != null && ap.Kind == CombatActionKind.Attack && ap.Actor == e.Opponent && ap.StartedUnscaledTime != _lastAttackStarted)
            {
                _lastAttackStarted = ap.StartedUnscaledTime;
                Play("attack");
            }

            // life points
            var oppLp = e.Opponent.LifePoints; var youLp = e.Player.LifePoints;
            if (oppLp < _oppLp) Play(_oppLp - oppLp >= 2000 ? "hit_big" : "hit");
            if (youLp < _youLp && oppTurn) Play("good");
            _oppLp = oppLp; _youLp = youLp;

            // a monster arrived that the hand did not carry (special summon / tribute beat)
            var mons = CountMonsters(e.Opponent);
            if (mons > _oppMonsters && !HandBusy && oppTurn) Play("summon_big");
            _oppMonsters = mons;

            // idle flavour while you think
            if (!oppTurn && _current == null && _queue.Count == 0 && Time.unscaledTime > _nextWaitAt)
            {
                Play("wait");
                _nextWaitAt = Time.unscaledTime + Random.Range(12f, 20f);
            }
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (!_graph.IsValid() || _anim == null) return;
            var dt = Application.isPlaying ? Time.deltaTime : 1f / 30f;
            PollEngine();
            TickCastAfterReveal();
            TickGestures(dt);
            _graph.Evaluate(dt);
        }

        /// <summary>Advance and evaluate the clip graph by <paramref name="dt"/> (edit-mode previews call this, then <see cref="Solve"/>).</summary>
        public void Step(float dt)
        {
            TickGestures(dt);
            _graph.Evaluate(dt);
        }

        void TickGestures(float dt)
        {
            // start the next beat when the hand is free (or it has waited long enough)
            if (_current == null && _queue.Count > 0)
            {
                var g = _queue.Peek();
                var mustWait = g.Arms != Arms.None && HandBusy && Time.unscaledTime - g.QueuedAt < g.MaxWait && Application.isPlaying;
                if (!mustWait) PlayGestureNow(_queue.Dequeue());
            }

            if (_base.IsValid() && _idleClip != null)
            {
                _baseTime = (_baseTime + dt) % Mathf.Max(0.01f, _idleClip.length);
                _base.SetTime(_baseTime);
            }
            var target = 0f;
            if (_current != null && _gesture.IsValid())
            {
                _gestureTime += dt;
                var end = _gestureLen;
                if (_holdLastFrame) _gestureTime = Mathf.Min((float)_gestureTime, end - 0.001f);
                _gesture.SetTime(_gestureTime);
                target = 1f;
                if (!_holdLastFrame && _gestureTime >= end - 0.25f) target = 0f;
                if (!_holdLastFrame && _gestureTime >= end) _current = null;
            }
            _gestureWeight = Mathf.MoveTowards(_gestureWeight, target, dt / 0.22f);
            _mixer.SetInputWeight(0, 1f - _gestureWeight);
            _mixer.SetInputWeight(1, _gestureWeight);

            // disk reaches: one at a time, only while the card hand is not mid-draw / place and no arm beat is playing
            if (_reach == null && _reaches.Count > 0)
            {
                var armBusy = _current != null && _current.Arms != Arms.None && _gestureWeight > 0.05f;
                var r = _reaches.Peek();
                if (!HandBusy && !armBusy) _reach = _reaches.Dequeue();
                else if (Time.unscaledTime - r.QueuedAt > 4f) _reaches.Dequeue();   // stale: the moment passed
            }
            if (_reach != null)
            {
                _reach.T += dt;
                if (_reach.T >= _reach.Dur)
                {
                    if (!string.IsNullOrEmpty(_reach.Then)) Play(_reach.Then);
                    _reach = null;
                }
            }
            _reachW = Mathf.MoveTowards(_reachW, _reach != null ? 1f : 0f, dt / 0.18f);
            if (_reachW <= 0.001f) { _reachPull = Vector3.zero; _tipLift = 0f; }

            // arm IK weights: an arm the gesture needs is released to the clip, the others stay on the disk / cards
            var arms = _current != null ? _current.Arms : Arms.None;
            var rightFree = arms != Arms.None && _gestureWeight > 0.01f;
            // (no beat releases the disk arm today: start, victory and defeat keep it on its pose, carried by the torso)
            var leftFree = arms == Arms.Both && _gestureWeight > 0.01f;
            _wRight = Mathf.MoveTowards(_wRight, rightFree ? 0f : 1f, dt / 0.25f);
            _wLeft = Mathf.MoveTowards(_wLeft, leftFree || (Disk == null && _deployRun == null) ? 0f : 1f, dt / 0.3f);
        }

        void LateUpdate() => Solve(Application.isPlaying ? Time.deltaTime : 1f / 30f);

        /// <summary>Place the body, IK both arms, fingers, head, and hang the disk + fan on it (after the clip pose).</summary>
        public void Solve(float dt)
        {
            if (_body == null || Tracker == null || !Tracker.TryGetOpponentLeftForearm(out var armBone)) return;
            // a tag-team partner stands beside the seat's arm (no disk of its own): the same pose, shifted sideways
            var arm = (pos: armBone.position, rot: armBone.rotation);
            if (Mathf.Abs(StandOffsetM) > 1e-4f)
            {
                var sideFacing = Vector3.back;
                if (Tracker.TryGetTorso(out var youT)) sideFacing = youT.position - armBone.position;
                sideFacing.y = 0f;
                if (sideFacing.sqrMagnitude > 1e-6f) arm.pos += Vector3.Cross(Vector3.up, sideFacing.normalized) * (StandOffsetM * UnitsPerMetre);
            }

            // hidden with the opponent's presence (Zone Mode turns the opponent disk off)
            var show = Disk == null || Disk.gameObject.activeInHierarchy;
            var vis = show ? 1f : 0f;
            if (!Mathf.Approximately(vis, _visible))
            {
                _visible = vis; _body.SetPresence(vis);
                if (_npcDisk != null) _npcDisk.gameObject.SetActive(show);
            }
            if (!show) return;

            var s = UnitsPerMetre;
            var hs = _look != null ? _look.HeightScale : 1f;

            // ---- where the body stands: behind its wrist, facing you, feet on the street
            var facing = Vector3.back;
            if (Tracker.TryGetTorso(out var you)) facing = you.position - arm.pos;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = -transform.parent.forward;
            facing.Normalize();
            var right = Vector3.Cross(Vector3.up, facing);
            var floorY = LifeSize ? arm.pos.y - 0.95f * s : Arena != null && Arena.ArenaRoot != null ? Arena.ArenaRoot.position.y : arm.pos.y - 1.22f * s;
            var pos = arm.pos - facing * (0.32f * s * hs) + right * (0.08f * s * hs);
            pos.y = floorY;
            var rot = Quaternion.LookRotation(facing, Vector3.up);
            var firstPlace = !_placed;
            if (!_placed) { _rootPos = pos; _rootRot = rot; _placed = true; }
            var k = 1f - Mathf.Exp(-dt * 6f);
            _rootPos = Vector3.Lerp(_rootPos, pos, k);
            _rootRot = Quaternion.Slerp(_rootRot, rot, k);
            _root.SetPositionAndRotation(_rootPos, _rootRot);

            // ---- fan anchor for the hand volume: in front of the chest, a little to the right
            var chestY = floorY + 1.18f * s * hs;
            _fanAnchor.SetPositionAndRotation(
                new Vector3(_rootPos.x, chestY, _rootPos.z) + facing * (0.34f * s * hs) - right * (0.10f * s * hs),
                Quaternion.LookRotation(facing, Vector3.up));

            // ---- a disk reach leans the chest toward the disk a little: before either arm is solved (applied after, it
            // carried the aimed hand and the disk arm away from their targets)
            if (_chest != null && _reachW > 0.001f)
                _chest.rotation = Quaternion.AngleAxis(9f * _reachW * ReachEnvelope(), right) * _chest.rotation;

            // ---- left arm onto the disk pose
            var elbowDir = PickAxis(arm.rot, (-right * 0.8f - facing * 0.25f - Vector3.up * 0.2f).normalized);
            var handFingers = -elbowDir;
            var handBack = PickAxis(arm.rot, Vector3.up, elbowDir);
            var leftTargetRot = Quaternion.LookRotation(handBack, handFingers);
            var leftHint = arm.pos + elbowDir * (0.30f * s * hs) - Vector3.up * (0.12f * s * hs) - facing * (0.06f * s * hs);
            // working the disk: bring it in toward the chest and up a little, tipped toward the eyes
            var lift = _reachW * ReachEnvelope();
            // (3 cm in toward the body: 7 cm put the hub into the chest / bust while the other hand worked it)
            var leftPos = arm.pos + (Vector3.up * 0.06f - facing * 0.03f + right * 0.03f) * (s * hs * lift) + _reachPull * _reachW;
            var leftRot = Quaternion.AngleAxis(-14f * lift, right) * leftTargetRot;
            // the clip moves the torso (a lean into a cast, a stagger, a twist); the disk arm moves with it or the open blade
            // cuts through the hips. Track the chest against its idle pose and carry the disk target along by the difference.
            var bodyTurn = Quaternion.identity;
            if (_chest != null)
            {
                var inv = Quaternion.Inverse(_root.rotation);
                var cPos = inv * (_chest.position - _root.position); var cRot = inv * _chest.rotation;
                if (_current == null || _gestureWeight < 0.01f) { _chestRefPos = cPos; _chestRefRot = cRot; _haveChestRef = true; }
                else if (_haveChestRef && _deployRun == null)
                {
                    var chestTurn = _root.rotation * (cRot * Quaternion.Inverse(_chestRefRot)) * inv;
                    var refW = _root.position + _root.rotation * _chestRefPos;
                    var w = _gestureWeight * (1f - lift);
                    // the chest's travel in full and its twist about the vertical in full for most beats (the attack command
                    // turns the duelist side-on: carried a third of the way, the hub stayed behind in the coat / belly; see
                    // Gesture.Twist), but only a third of its lean / tilt: a duelist keeps the disk arm up through a lean
                    // (swung rigidly, a forward lean dropped the open blade into the thigh)
                    var turnedFwd = Vector3.ProjectOnPlane(chestTurn * facing, Vector3.up);
                    var yaw = turnedFwd.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(facing, turnedFwd.normalized) : Quaternion.identity;
                    var swing = Quaternion.Slerp(Quaternion.identity, yaw, _current.Twist) * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(yaw) * chestTurn, 0.35f);
                    leftPos = Vector3.Lerp(leftPos, _chest.position + swing * (leftPos - refW), w);
                    leftRot = Quaternion.Slerp(leftRot, swing * leftRot, w);
                    leftHint = Vector3.Lerp(leftHint, _chest.position + swing * (leftHint - refW), w);
                    bodyTurn = Quaternion.Slerp(Quaternion.identity, chestTurn, _gestureWeight);
                    // and a little more room: the idle disk pose holds the hub a hand's width off the belly, which a lean
                    // into a cast, an attack or a stagger closed (the hub's underside met the stomach / coat front)
                    var room = (_style ??= StyleFor(_name)).GestureRoom;
                    leftPos += bodyTurn * (facing * (0.06f + room.z) - right * (0.02f + room.x)) * (s * hs * w);
                }
            }
            // standby: with the disk folded the halves lie along the forearm on both sides of the hub, pointing back past
            // the elbow, so a forearm held across the body puts one of them into the chest, and a hanging arm stands them up
            // beside the ribs. The forearm points forward at the waist, out from the hip, the plate facing outward: the halves
            // lie above and below the arm, outside the body. The duel pose comes back as the halves slide out.
            _standbyW = Mathf.MoveTowards(_standbyW, 1f - Mathf.SmoothStep(0f, 1f, DiskMechNow() / 0.3f), dt / 0.35f);
            if (_standbyW > 0.001f && _lUpper != null)
            {
                // in the torso's frame, so a hanging arm stays at the side through a stagger or a fall
                Vector3 Wb(Vector3 v) => bodyTurn * (right * v.x + Vector3.up * v.y + facing * v.z);
                var sh = _lUpper.position; var sc = s * hs;
                var sb = _style ??= StyleFor(_name);
                var hangPos = sh + Wb(sb.Standby) * sc;
                var hangRot = Quaternion.LookRotation(Wb(sb.StandbyBackDir).normalized, Wb(sb.StandbyFingersDir).normalized);
                var w = Mathf.SmoothStep(0f, 1f, _standbyW);
                leftPos = Vector3.Lerp(leftPos, hangPos, w);
                leftRot = Quaternion.Slerp(leftRot, hangRot, w);
                leftHint = Vector3.Lerp(leftHint, sh + Wb(new Vector3(-0.24f, -0.30f, -0.04f)) * sc, w);
            }
            // defeat: Duel_Defeat staggers the duelist and throws them onto their back (flat by ~1 s). The clip's own disk arm
            // folds the disk over the head, and a slump set in the standing frame pushed the arm through the street once the
            // body lay down. So the frame is the torso's own, from the bones: standing, the arm hangs limp out at the side,
            // plate facing out; lying, it rests on the street flung out ~70° from the feet, palm down, the powered-down disk
            // flat on top of the forearm and the elbow low. (The folded halves flank the forearm 7-21 cm each side, the big
            // blade 23 cm past the elbow: plate out stood them in the street, plate down put the big blade into the ribs,
            // a raised elbow stood the disk up like a fin, and the arm nearer the hip cut the torso as the body went down.)
            if (_current != null && _current.Name == "defeat" && _lUpper != null && _rUpper != null && _hips != null && _neck != null)
            {
                var spine = (_neck.position - _hips.position).normalized;
                var outL = Vector3.ProjectOnPlane(_lUpper.position - _rUpper.position, spine).normalized;
                var front = Vector3.Cross(spine, outL);
                Vector3 T(Vector3 v) => -outL * v.x + spine * v.y + front * v.z;   // (right, up, forward) of the torso
                var sh = _lUpper.position; var sc = s * hs;
                var w = Mathf.SmoothStep(0f, 1f, _gestureWeight);
                // standing: limp out at the side
                var hangPos = sh + T(new Vector3(-0.30f, -0.50f, 0.06f)) * sc;
                var hangRot = Quaternion.LookRotation(T(new Vector3(-1f, 0f, 0.1f)).normalized, T(new Vector3(-0.35f, -1f, 0.1f)).normalized);
                var hangHint = sh + T(new Vector3(-0.30f, -0.25f, -0.08f)) * sc;
                // going down and lying (the spine below ~45°): flung out from the torso's axis in its own frame
                // (DefeatArmDeg), so the disk stays clear of the ribs through the fall and lands flat on the street beside the body
                var down = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 0.25f, spine.y));
                if (down > 0f && _lFore != null && _lHand != null)
                {
                    var reach = ((_lFore.position - sh).magnitude + (_lHand.position - _lFore.position).magnitude) * 0.97f;
                    var armDeg = (_style ??= StyleFor(_name)).DefeatArmDeg * Mathf.Deg2Rad;
                    var along = (-spine * Mathf.Cos(armDeg) + outL * Mathf.Sin(armDeg)).normalized;
                    var lyPos = sh + along * reach;
                    // once the body is nearly flat the arm comes down onto the street (in the torso's plane it hung a
                    // shoulder's height above it); the wrist rests just off the street. Never steeper than 35° down: from
                    // a shoulder still high in the fall, a short steep reach brought the disk in against the ribs.
                    var low = floorY + 0.04f * s;
                    var settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.15f, spine.y));
                    if (lyPos.y < low || settle > 0f)
                    {
                        var dy = Mathf.Max(low - sh.y, -reach * Mathf.Sin(35f * Mathf.Deg2Rad));
                        var flat = Vector3.ProjectOnPlane(along, Vector3.up).normalized;
                        var street = sh + flat * Mathf.Sqrt(Mathf.Max(0f, reach * reach - dy * dy)) + Vector3.up * dy;
                        lyPos = lyPos.y < low ? street : Vector3.Lerp(lyPos, street, settle);
                    }
                    var lyAlong = (lyPos - sh).normalized;
                    hangPos = Vector3.Lerp(hangPos, lyPos, down);
                    hangRot = Quaternion.Slerp(hangRot, Quaternion.LookRotation(Vector3.ProjectOnPlane(front, lyAlong).normalized, lyAlong), down);
                    hangHint = Vector3.Lerp(hangHint, (sh + lyPos) * 0.5f + (outL * 0.04f - front * 0.06f) * sc, down);
                }
                leftPos = Vector3.Lerp(leftPos, hangPos, w);
                leftRot = Quaternion.Slerp(leftRot, hangRot, w);
                leftHint = Vector3.Lerp(leftHint, hangHint, w);
            }
            var dw = DeployTarget(dt, s * hs, facing, right, out var dPos, out var dRot);
            if (dw > 0f) { leftPos = Vector3.Lerp(leftPos, dPos, dw); leftRot = Quaternion.Slerp(leftRot, dRot, dw); }
            // raised-fist deploy, once the snap starts: elbow flares out beside the shoulder (the wind-up stays low)
            if (dw > 0.35f && _deployRun != null && _deployRun.T >= _deployRun.SnapAt && _lUpper != null)
            {
                var flare = _lUpper.position + (-right * 0.32f + facing * 0.10f + Vector3.up * 0.04f) * (s * hs);
                // a referenced stance held low (forearm across the navel / hips) after the lock: the elbow drops to the side,
                // so the forearm lies level (flared at shoulder height it slanted the plate down across the thigh)
                if (_deployRun.PresentW > 0f && _deployRun.Style.Present.y < -0.2f)
                {
                    var low = (_lUpper.position + dPos) * 0.5f + (-right * 0.18f - Vector3.up * 0.18f - facing * 0.04f) * (s * hs);
                    flare = Vector3.Lerp(flare, low, _deployRun.PresentW);
                }
                leftHint = Vector3.Lerp(leftHint, flare, dw);
            }
            if (_wLeft > 0.001f && (DebugSkip & 1) == 0) TwoBoneIk(_lUpper, _lFore, _lHand, leftPos, leftHint, leftRot * _lFrameInv, _wLeft);
            if (_wLeft > 0.999f && lift < 0.001f && dw <= 0f && _deployRun == null) { _diskLocal = Quaternion.Inverse(leftTargetRot) * arm.rot; _haveDiskLocal = true; }
            if ((DebugSkip & 8) == 0)
                CurlFingers(_lFingers, _deployRun == null || dw <= 0.45f ? 1f : _deployRun.Style.OpenHand ? -0.35f : 1.4f);
            // strap the worn disk to this frame's forearm now, before the other hand aims at its zones (strapped at the end,
            // the hand chased last frame's disk and went through the plate whenever the disk arm moved)
            if (_npcDisk != null && Disk != null) _npcDisk.Tick(_lFore, _lHand, _lHand.rotation * Quaternion.Inverse(_lFrameInv), dt);
            // ---- and never through the street: lying after a defeat, the disk rests on it
            KeepDiskAboveStreet(floorY, s, leftPos, leftHint, leftRot, dt);

            // ---- right hand onto the card choreography (or the fan onto the hand during a gesture)
            var fh = Hand != null ? Hand.FloatingHand : null;
            var ghost = fh != null && fh.IsLive && Hand.isActiveAndEnabled ? fh.WristPivot : null;
            if (_reach != null || _reachW > 0.001f)
            {
                SolveReach(s * hs, facing, right, ghost);
                if (ghost != null) fh.ClearTrackedGrip();
            }
            else if (ghost != null)
            {
                if (_wRight > 0.001f && (DebugSkip & 2) == 0)
                {
                    var rHint = ghost.position + right * (0.28f * s * hs) - Vector3.up * (0.22f * s * hs) - facing * (0.12f * s * hs);
                    TwoBoneIk(_rUpper, _rFore, _rHand, ghost.position, rHint, ghost.rotation * _rFrameInv, _wRight);
                }
                if (_wRight < 0.999f && _rHand != null)
                    fh.SetTrackedWrist(_rHand.position, _rHand.rotation * Quaternion.Inverse(_rFrameInv));
                else
                    fh.ClearTrackedGrip();
                CurlRight(fh.GripFan, fh.GripPinch, fh.GripPresent);
            }
            else if (_reach == null && _reachW <= 0.001f)
            {
                var run = _deployRun;
                // a referenced free-hand pose held through the activation (Kaiba's card beside the head), by IK over the clip;
                // it lowers again over the post-lock ease
                var card = 0f;
                if (run != null && run.Style.CardHand.sqrMagnitude > 1e-4f && _rUpper != null && (DebugSkip & 2) == 0)
                {
                    card = dw * (run.LockT < 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (run.T - run.LockT) / PresentSeconds));
                    if (card > 0.001f)
                    {
                        Vector3 Wr(Vector3 v) => right * v.x + Vector3.up * v.y + facing * v.z;
                        var sc = s * hs; var sh = _rUpper.position;
                        // elbow forward and down: lifted out to the side, the upper arm stretched a coat sleeve's
                        // armpit (weighted to the chest) into strands
                        TwoBoneIk(_rUpper, _rFore, _rHand, sh + Wr(run.Style.CardHand) * sc, sh + Wr(new Vector3(0.10f, -0.26f, 0.22f)) * sc,
                            Quaternion.LookRotation(Wr(run.Style.CardBack).normalized, Wr(run.Style.CardFingers).normalized) * _rFrameInv, card);
                    }
                }
                var fist = run != null && dw > 0.4f && (run.LockT < 0f ? run.Style.ActFist : run.Style.RightFist) ? 1f : 0f;
                CurlRight(fist * (1f - card), Mathf.Max(fist * (1f - card), card), 0f);   // a card is held in a pinch
            }

            // ---- the right hand never sinks into its own disk: a clip beat swinging across the body, the approach of a
            // touch, a finger curling over the hub all leave it pushed out to its side of the plate
            KeepRightHandOutOfDisk(s * hs);

            // ---- head: the hand while it works, the attack target while attacking, you otherwise
            var look = you.position + Vector3.up * (0.25f * s);
            if (_reach != null && _reach.Target != null) look = _reach.Target.position;
            else if (fh != null && fh.IsBusy && ghost != null) look = ghost.position;
            else if (_current != null && _current.Name == "attack" && _engine?.ActivePresentation != null &&
                     Arena != null && _engine.ActivePresentation.TargetCard != null &&
                     Arena.TryGetMonsterVisual(_engine.ActivePresentation.TargetCard, true, out var tv) && tv != null)
                look = tv.transform.position;
            if ((DebugSkip & 4) == 0) LookAt(look, _current != null && _current.Arms == Arms.Both ? 0.25f : 0.8f);

            // ---- the disk rides the left hand (exactly the tracker pose while the arm is on it); a worn disk
            // (NpcDuelDisk) is strapped to the forearm bones below instead
            if ((DebugSkip & 16) != 0 || _npcDisk != null) { }
            else if (Disk != null && Disk.ExternalPose && _lHand != null && _haveDiskLocal)
            {
                var handCanon = _lHand.rotation * Quaternion.Inverse(_lFrameInv);
                Disk.transform.SetPositionAndRotation(_lHand.position, handCanon * _diskLocal);
            }
            else if (Disk != null && !_haveDiskLocal) Disk.transform.SetPositionAndRotation(arm.pos, arm.rot);
            // the disk's swirl trails would streak from wherever the rig was built to the arm: start them fresh
            if (firstPlace && Disk != null)
                foreach (var tr in Disk.GetComponentsInChildren<TrailRenderer>(true)) tr.Clear();

            // ---- its own Battle City disk, strapped to the forearm
            if (_npcDisk != null && Disk != null)
            {
                if (Disk.IsDeployed && !_wasDeployed) ReachDisk("activate_disk");
                _wasDeployed = Disk.IsDeployed;
            }
        }

        /// <summary>The worn disk's mechanism now: 0 folded (standby) .. 1 locked open (previews: the debug pose).</summary>
        float DiskMechNow()
        {
            if (SpiritDuelerMechanics.DebugPose >= 0f) return Mathf.Clamp01(SpiritDuelerMechanics.DebugPose);
            if (Disk != null && Disk.Fx != null && Application.isPlaying) return Disk.Fx.MechOpen;
            return 1f;
        }

        // ── keep-out ────────────────────────────────────────────────────────────────────────────────
        // The kit's parts are meshes in the kit's own frame (local Y = the plate normal, also while the halves sweep
        // round the hub), so each part's mesh bounds is a slab: thin along Y, the footprint in X / Z.
        readonly List<(Transform t, Bounds b, Renderer r)> _diskParts = new();
        readonly List<(Transform joint, bool distal)> _rHandJoints = new();
        /// <summary>Clearance the hand keeps from the disk's surfaces (m).</summary>
        const float DiskKeepOutM = 0.008f;
        const float UpperArmSleeveM = 0.09f;   // a loose sleeve bunches out 8-9 cm round a bent elbow (Yugi's jacket)

        /// <summary>
        /// The right hand and its finger joints are kept out of every part of the worn disk: for each part whose
        /// footprint a joint is over and whose slab it is inside, the hand moves out through that part's face on the
        /// wrist's side (above the plate: up; under it: down) and the arm is re-solved to the moved wrist.
        /// </summary>
        void EnsureDiskParts()
        {
            if (_diskParts.Count > 0 || _npcDisk == null) return;
            foreach (var mf in _npcDisk.DiskTransform.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.gameObject.name.StartsWith("DiskPart:") &&
                    !mf.gameObject.name.Contains("arm mount"))
                    _diskParts.Add((mf.transform, mf.sharedMesh.bounds, mf.GetComponent<Renderer>()));
        }

        /// <summary>The lowest point of the worn disk's shown parts (each part's mesh box, in world).</summary>
        float DiskLowestY()
        {
            var low = float.PositiveInfinity;
            foreach (var (t, b, r) in _diskParts)
            {
                if (t == null || !t.gameObject.activeInHierarchy || (r != null && !r.enabled)) continue;
                var e = b.extents;
                var y = t.TransformPoint(b.center).y - Mathf.Abs(t.TransformVector(e.x, 0f, 0f).y)
                        - Mathf.Abs(t.TransformVector(0f, e.y, 0f).y) - Mathf.Abs(t.TransformVector(0f, 0f, e.z).y);
                low = Mathf.Min(low, y);
            }
            return low;
        }

        const float StreetClearM = 0.005f;

        /// <summary>Lifts the disk arm until the worn disk clears the street (a duelist lying on it after a defeat).</summary>
        void KeepDiskAboveStreet(float floorY, float s, Vector3 pos, Vector3 hint, Quaternion rot, float dt)
        {
            if (_npcDisk == null || Disk == null || _lUpper == null || _lFore == null || _lHand == null || _wLeft < 0.001f || (DebugSkip & 1) != 0) return;
            EnsureDiskParts();
            for (var pass = 0; pass < 3; pass++)
            {
                var need = floorY + StreetClearM * s - DiskLowestY();
                if (need <= 0f || float.IsInfinity(need)) return;
                pos += Vector3.up * need; hint += Vector3.up * need;
                TwoBoneIk(_lUpper, _lFore, _lHand, pos, hint, rot * _lFrameInv, _wLeft);
                _npcDisk.Tick(_lFore, _lHand, _lHand.rotation * Quaternion.Inverse(_lFrameInv), dt);
            }
        }

        void KeepRightHandOutOfDisk(float scale)
        {
            if (_rHand == null || _rUpper == null || _rFore == null || _npcDisk == null || (DebugSkip & 2) != 0) return;
            EnsureDiskParts();
            var reach = 0.8f * scale;
            if ((_rHand.position - _npcDisk.DiskTransform.position).sqrMagnitude > reach * reach) return;
            for (var pass = 0; pass < 2; pass++)
            {
                var push = Vector3.zero;
                foreach (var (t, b, r) in _diskParts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy || (r != null && !r.enabled)) continue;
                    var ls = Mathf.Max(1e-6f, Mathf.Abs(t.lossyScale.y));
                    // clearance in the part's units (a glove sits ~1 cm past the bare fingertip the bones describe)
                    var m = (_gloved ? DiskKeepOutM + 0.010f : DiskKeepOutM) * scale / ls;
                    var c = b.center; var e = b.extents;
                    var top = c.y + e.y + m; var bot = c.y - e.y - m;
                    var wrist = t.InverseTransformPoint(_rHand.position);
                    var up = wrist.y >= c.y;                     // the side of the part the hand is on
                    var need = 0f;
                    // the forearm too (a sleeve is ~4.5 cm round the bone): it reaches over the hub to the far zones
                    var mArm = (0.045f + DiskKeepOutM) * scale / ls;
                    for (var f = 1; f <= 3; f++)
                    {
                        var lp = t.InverseTransformPoint(Vector3.Lerp(_rFore.position, _rHand.position, f * 0.25f));
                        if (Mathf.Abs(lp.x - c.x) > e.x + mArm || Mathf.Abs(lp.z - c.z) > e.z + mArm) continue;
                        var topA = c.y + e.y + mArm; var botA = c.y - e.y - mArm;
                        if (lp.y >= topA || lp.y <= botA) continue;
                        // the wrist end moves the full amount, the elbow end not at all: scale the need up toward the elbow
                        var gain = 1f / Mathf.Max(0.25f, f * 0.25f);
                        need = up ? Mathf.Max(need, (topA - lp.y) * gain) : Mathf.Min(need, (botA - lp.y) * gain);
                    }

                    for (var i = -1; i < _rHandJoints.Count; i++)
                    {
                        var j = i < 0 ? _rHand : _rHandJoints[i].joint;
                        var distal = i >= 0 && _rHandJoints[i].distal;
                        if (j == null) continue;
                        // a joint and, for the last one of each finger, its fingertip
                        for (var k = 0; k < 2; k++)
                        {
                            Vector3 wp;
                            if (k == 0) wp = j.position;
                            else
                            {
                                if (!distal || j.parent == null) continue;   // fingertips past the distal joints
                                wp = j.position + (j.position - j.parent.position) * (_gloved ? 1.5f : 1.3f);
                            }
                            var lp = t.InverseTransformPoint(wp);
                            if (Mathf.Abs(lp.x - c.x) > e.x + m || Mathf.Abs(lp.z - c.z) > e.z + m) continue;
                            if (lp.y >= top || lp.y <= bot) continue;
                            need = up ? Mathf.Max(need, top - lp.y) : Mathf.Min(need, bot - lp.y);
                        }
                    }

                    if (Mathf.Abs(need) < 1e-6f) continue;
                    var w = t.TransformVector(new Vector3(0f, need, 0f));
                    if (w.sqrMagnitude > push.sqrMagnitude) push = w;
                }

                if (push.sqrMagnitude < 1e-10f) break;
                TwoBoneIk(_rUpper, _rFore, _rHand, _rHand.position + push, _rFore.position, _rHand.rotation, 1f);
            }

            // the elbow and upper arm too: reaching across to the far zones, a loose sleeve bunched round the bent elbow lay
            // over the hub. The elbow (the IK pole) lifts off the plate; the hand stays where it is.
            for (var pass = 0; pass < 4; pass++)
            {
                var lift = Vector3.zero;
                foreach (var (t, b, r) in _diskParts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy || (r != null && !r.enabled)) continue;
                    var ls = Mathf.Max(1e-6f, Mathf.Abs(t.lossyScale.y));
                    var m = (UpperArmSleeveM + DiskKeepOutM) * scale / ls;
                    var c = b.center; var e = b.extents;
                    var up = t.InverseTransformPoint(_rFore.position).y >= c.y;
                    var top = c.y + e.y + m; var bot = c.y - e.y - m;
                    var need = 0f;
                    for (var f = 0; f <= 2; f++)
                    {
                        var lp = t.InverseTransformPoint(Vector3.Lerp(_rFore.position, _rUpper.position, f * 0.25f));
                        if (Mathf.Abs(lp.x - c.x) > e.x + m || Mathf.Abs(lp.z - c.z) > e.z + m) continue;
                        if (lp.y >= top || lp.y <= bot) continue;
                        // the elbow moves the full amount, the shoulder end not at all
                        var gain = 1f / Mathf.Max(0.5f, 1f - f * 0.25f);
                        need = up ? Mathf.Max(need, (top - lp.y) * gain) : Mathf.Min(need, (bot - lp.y) * gain);
                    }
                    if (Mathf.Abs(need) < 1e-6f) continue;
                    var wv = t.TransformVector(new Vector3(0f, need, 0f));
                    if (wv.sqrMagnitude > lift.sqrMagnitude) lift = wv;
                }
                if (lift.sqrMagnitude < 1e-10f) break;
                TwoBoneIk(_rUpper, _rFore, _rHand, _rHand.position, _rFore.position + lift * 1.6f, _rHand.rotation, 1f);
            }
        }

        /// <summary>0 → 1 → 0 over the reach (smooth), used for the disk lift and torso lean.</summary>
        float ReachEnvelope()
        {
            if (_reach == null) return 1f;
            var p = Mathf.Clamp01(_reach.T / Mathf.Max(0.01f, _reach.Dur));
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(p / 0.3f, (1f - p) / 0.3f));
        }

        /// <summary>
        /// Right hand along the reach: from where it was → hover over the spot → contact (tap / pinch / slap; a small
        /// circle on the deck for a shuffle) → back. Palm faces the disk face; the chest leans in a touch.
        /// </summary>
        void SolveReach(float scale, Vector3 facing, Vector3 right, Transform ghost)
        {
            if (_rHand == null) return;
            var r = _reach;
            // where the hand rests between moves: the card hand's wrist when it exists, else the clip pose
            var rest = ghost != null ? ghost.position : _rHand.position;
            if (r != null && !r.HasStart) { r.Start = rest; r.HasStart = true; }
            if (r == null || r.Target == null)
            {
                if (ghost != null && (DebugSkip & 2) == 0)
                    TwoBoneIk(_rUpper, _rFore, _rHand, ghost.position, ghost.position + right * (0.28f * scale) - Vector3.up * (0.22f * scale),
                        ghost.rotation * _rFrameInv, _wRight);
                return;
            }
            var tgt = r.Target;
            var normal = tgt.up;
            // the blade's outer zones sit beyond the other arm: the disk arm brings the disk across toward the working hand
            // (the anime's "forearm across the waist, right hand on the blade") until the spot is within 88 % of its reach
            if (_rUpper != null && _rFore != null)
            {
                var armLen = (_rFore.position - _rUpper.position).magnitude + (_rHand.position - _rFore.position).magnitude;
                var toT = tgt.position + normal * (0.06f * scale) - _rUpper.position;
                var excess = toT.magnitude - 0.88f * armLen;
                var step = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f);
                if (!Application.isPlaying) step = 1f - Mathf.Exp(-(1f / 30f) * 9f);
                // sideways and up only: pulled straight at the shoulder, the hub went into the chest
                var pullDir = Vector3.ProjectOnPlane(-toT.normalized, facing);
                if (Vector3.Dot(-toT.normalized, facing) > 0f) pullDir = -toT.normalized;
                _reachPull = excess > 0f
                    ? Vector3.ClampMagnitude(_reachPull + pullDir * (excess * step), 0.32f * scale)
                    : Vector3.MoveTowards(_reachPull, Vector3.zero, -excess * step);   // in reach: ease back, never push out
            }
            // the disk face normal must point toward the duelist's eyes side, not into the forearm
            if (_head != null && Vector3.Dot(normal, _head.position - tgt.position) < 0f) normal = -normal;
            // the IK target is the wrist, so contact is measured from the hand: a fingertip touch (tap, the graveyard, the
            // latch button) comes in pitched down with the wrist high, only the fingertips meet the plate; a flat slap
            // (place, shuffle) or the pinch on the deck keeps the palm and the curled fingers above it.
            // (1.2 cm off the plate put the palm and fingers through the blade.)
            var fingertip = r.Kind is "tap" or "grave" or "activate_disk";
            // the graveyard slot and the deck sit up on the hub, above the plate the zone frame describes
            var clear = r.Kind == "grave" ? 0.11f : fingertip ? 0.085f : r.Kind == "draw" ? 0.08f : 0.05f;
            var pitch = fingertip ? 38f : r.Kind == "draw" ? 22f : 8f;
            var contact = tgt.position + normal * (clear * scale + _tipLift);
            var hover = tgt.position + normal * ((clear + 0.07f) * scale) + Vector3.up * (0.03f * scale);
            var p = Mathf.Clamp01(r.T / Mathf.Max(0.01f, r.Dur));
            Vector3 pos;
            float press;
            // in and out over the top of the plate: a straight line from a hand resting below the disk level went through it
            Vector3 Arc(Vector3 from, Vector3 to, float k)
            {
                var under = (0.16f * scale) - Vector3.Dot(from - tgt.position, normal);
                var ctrl = (from + to) * 0.5f + normal * (Mathf.Max(0f, under) + 0.04f * scale);
                var a = Vector3.Lerp(from, ctrl, k); var b = Vector3.Lerp(ctrl, to, k);
                return Vector3.Lerp(a, b, k);
            }
            if (p < 0.35f) { var k = Mathf.SmoothStep(0f, 1f, p / 0.35f); pos = Arc(r.Start, hover, k); press = 0f; }
            else if (p < 0.65f)
            {
                var k = Mathf.Sin((p - 0.35f) / 0.3f * Mathf.PI);   // down and back up
                pos = Vector3.Lerp(hover, contact, k);
                press = k;
                if (r.Kind == "shuffle")
                {
                    var a = (p - 0.35f) / 0.3f * Mathf.PI * 6f;
                    pos += (Mathf.Cos(a) * right + Mathf.Sin(a) * Vector3.Cross(normal, right).normalized) * (0.02f * scale);
                }
            }
            else { var k = Mathf.SmoothStep(0f, 1f, (p - 0.65f) / 0.35f); pos = Arc(rest, hover, 1f - k); press = 0f; }
            if (r.Kind == "shuffle") { /* long contact: stretch the middle phase */ }

            // palm to the disk: back of the hand along the face normal, fingers pointing out along the reach
            var fingers = Vector3.ProjectOnPlane(tgt.position - (_rUpper != null ? _rUpper.position : _rHand.position), normal);
            if (fingers.sqrMagnitude < 1e-8f) fingers = facing;
            var side = Vector3.Cross(normal, fingers.normalized);
            var fingersDown = Quaternion.AngleAxis(pitch, side) * fingers.normalized;   // tip the fingers toward the plate
            var handRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(normal, fingersDown).normalized, fingersDown);
            if (ghost != null) handRot = Quaternion.Slerp(ghost.rotation, handRot, _reachW);
            pos = Vector3.Lerp(rest, pos, _reachW);
            // elbow out to the side and well above the plate: the forearm comes down onto the spot over the top of the
            // hub (hinted below it, the forearm came up through the blade; 14 cm up, a wide sleeve or cuff still dragged
            // across the hub on the way to the far zones)
            var hint = pos + right * (0.30f * scale) + normal * (0.24f * scale) - facing * (0.10f * scale);
            if ((DebugSkip & 2) == 0) TwoBoneIk(_rUpper, _rFore, _rHand, pos, hint, handRot * _rFrameInv, 1f);

            // fingers: point to tap, pinch to draw, open to slap / shuffle
            var tap = r.Kind is "tap" or "grave";
            var pinch = r.Kind == "draw";
            CurlRight(0f, pinch ? press : 0f, tap ? 1f : 0f);

            // touch, don't sink: measure the lowest fingertip against the spot's surface and lift the wrist until it rests
            // ~6 mm above it (hand sizes, curls and the hub's height differ per duelist and per spot)
            if (p > 0.28f && p < 0.72f && _rTips.Count > 0)   // from the arrival over the spot (from 20 % of the press was too late)
            {
                var low = float.MaxValue;
                foreach (var (tipBone, parent) in _rTips)
                {
                    if (tipBone == null || parent == null) continue;
                    // the skinned fingertip reaches ~1.3 distal-bone lengths past the last joint (0.9 left it sinking a few mm)
                    var tip = tipBone.position + (tipBone.position - parent.position) * 1.3f;
                    low = Mathf.Min(low, Vector3.Dot(tip - tgt.position, normal));
                }
                if (low < float.MaxValue)
                    _tipLift = Mathf.Clamp(_tipLift + ((_gloved ? 0.016f : 0.006f) * scale - low) * 0.6f, 0f, 0.12f * scale);
            }

        }

        /// <summary>The axis of <paramref name="r"/> (±X, ±Y, ±Z) closest to <paramref name="want"/>, skipping ones parallel to <paramref name="notAlong"/>.</summary>
        static Vector3 PickAxis(Quaternion r, Vector3 want, Vector3 notAlong = default)
        {
            var best = Vector3.zero; var bestDot = -2f;
            foreach (var a in new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back })
            {
                var w = r * a;
                if (notAlong != default && Mathf.Abs(Vector3.Dot(w, notAlong)) > 0.7f) continue;
                var d = Vector3.Dot(w, want);
                if (d > bestDot) { bestDot = d; best = w; }
            }
            return best;
        }

        /// <summary>
        /// Analytic two-bone IK: the elbow goes into the plane of the hint, the upper arm and forearm swing onto it,
        /// the hand takes <paramref name="handRot"/>; blended with the clip pose by <paramref name="w"/>.
        /// </summary>
        static void TwoBoneIk(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 hint, Quaternion handRot, float w)
        {
            if (upper == null || fore == null || hand == null) return;
            var a = upper.position; var b = fore.position; var c = hand.position;
            var l1 = (b - a).magnitude; var l2 = (c - b).magnitude;
            var toT = target - a; var d = Mathf.Clamp(toT.magnitude, 0.02f * (l1 + l2), (l1 + l2) * 0.999f);
            var dir = toT.normalized;
            var along = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            var h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            var bend = Vector3.ProjectOnPlane(hint - a, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(b - a, dir);
            bend = bend.sqrMagnitude > 1e-8f ? bend.normalized : Vector3.Cross(dir, Vector3.up).normalized;
            var elbow = a + dir * along + bend * h;

            var u0 = upper.rotation; var f0 = fore.rotation; var h0 = hand.rotation;
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * u0;
            var c1 = hand.position; var b1 = fore.position;
            fore.rotation = Quaternion.FromToRotation(c1 - b1, a + dir * d - b1) * fore.rotation;
            hand.rotation = handRot;
            if (w < 0.999f)
            {
                upper.rotation = Quaternion.Slerp(u0, upper.rotation, w);
                fore.rotation = Quaternion.Slerp(f0, fore.rotation, w);
                hand.rotation = Quaternion.Slerp(h0, hand.rotation, w);
            }
        }

        void CurlRight(float fan, float pinch, float present)
        {
            var relaxed = Mathf.Clamp01(1f - fan - pinch - present);
            foreach (var (bone, axis, f, p, pr, r) in _rFingers)
                if (bone != null) bone.localRotation = bone.localRotation * Quaternion.AngleAxis(f * fan + p * pinch + pr * present + r * relaxed, axis);
        }

        void CurlFingers(List<(Transform bone, Vector3 axisLocal)> list, float amount)
        {
            // the left hand grips the disk strap: a fixed loose curl (angles baked into axis length by ReadBones: 34°)
            foreach (var (bone, axis) in list)
                if (bone != null) bone.localRotation = bone.localRotation * Quaternion.AngleAxis(28f * amount, axis);
        }

        void LookAt(Vector3 world, float weight)
        {
            if (_head == null) return;
            var fwd = _head.rotation * _headFwdLocal;
            var want = (world - _head.position).normalized;
            var turn = Quaternion.FromToRotation(fwd, want);
            turn.ToAngleAxis(out var ang, out var ax);
            if (ang > 180f) ang -= 360f;
            ang = Mathf.Clamp(ang, -60f, 60f) * weight;
            if (_neck != null) _neck.rotation = Quaternion.AngleAxis(ang * 0.35f, ax) * _neck.rotation;
            _head.rotation = Quaternion.AngleAxis(ang * 0.65f, ax) * _head.rotation;
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (Hand != null)
            {
                if (Hand.AnchorOverride == _fanAnchor) Hand.AnchorOverride = null;
                if (Hand.FloatingHand != null) { Hand.FloatingHand.HideModel = false; Hand.FloatingHand.ClearTrackedGrip(); }
            }
            if (Disk != null) Disk.ExternalPose = false;
            if (_npcDisk != null) Destroy(_npcDisk.gameObject);
        }
    }
}
