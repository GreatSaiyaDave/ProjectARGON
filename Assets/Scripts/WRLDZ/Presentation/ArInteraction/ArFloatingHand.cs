using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The player's translucent RIGHT hand, floating where their real hand is, holding the hand cards as a fan.
    /// Replaces the free-floating card layout of <see cref="ArHandVolume"/> (owner rule: the avatar's body is not
    /// shown in duels — the player's physical body is there — but the hand that holds the cards is).
    /// <list type="bullet">
    /// <item><b>Mesh</b> — <c>Resources/WRLDZ/Avatar3D/argon_hand_right.fbx</c>, cut from the CC0 base by
    /// <c>~/argon-avatar/tools/make_hand.py</c>: blend shapes <c>Relaxed / FanHold / Pinch</c> and
    /// <c>Anchor_*</c> empties. All framing is read from the anchors, so FBX axis conventions never matter.</item>
    /// <item><b>Fan</b> — <see cref="SlotFor"/> gives each card a pose on an arc pivoting at the thumb web
    /// (Anchor_Grip); the rightmost card is nearest the eye so every card shows its top-left (name) corner. The
    /// stack sits between the thumb pad and the finger pads (Anchor_FanPads) and is compressed to fit, so no card
    /// is ever inside the fingers. The hand writes every idle card's transform itself each LateUpdate (rigid with
    /// the hand, <see cref="ArFloatingCard.HeldByHand"/>) and eases only slot changes: lifts, re-spread, depth.</item>
    /// <item><b>Draw</b> — <see cref="QueueDraw"/>: pinch, reach the disk deck well, peel the top card, return and
    /// slide it into its slot (≈ 0.9 s; chained draws 0.62 s each).</item>
    /// <item><b>Shuffle</b> — <see cref="PlayRiffle"/> (hooked to the existing hand SHUFFLE button): fan closes to a
    /// stack, three flick passes with z-order swaps, fan reopens in the new order.</item>
    /// <item><b>Select</b> — <see cref="ToggleSelected"/>: the tapped card lifts 4 cm and tilts its top toward the
    /// eye (pivoting at its bottom edge, so it never swings back through the thumb). <see cref="SetHover"/> gives
    /// the card under the pointer (or under the finger, before a drag) a smaller peek lift so the pick is readable
    /// before it happens. <see cref="PickCard"/> is the pick itself: this frame's card boxes, with hysteresis so a
    /// peeking card cannot move out from under the pointer and flicker.</item>
    /// <item><b>Carry / place</b> — <see cref="BeginCarry"/>: the hand takes the dragged card between its index
    /// and middle fingers (the <c>Present</c> shape, the anime grip) and follows it under the pointer.
    /// <see cref="PlayPlace"/> carries a card to a disk zone, slaps it down (the commit fires at contact), pushes it
    /// through the seat motion, then returns to the fan. A failed drop is carried back to the fan.</item>
    /// <item><b>Look</b> — <c>WRLDZ/SpiritHandHolo</c>: skin-tinted glass drawn as HD anime (one cel shadow band,
    /// an ink outline) with a Spirit-colour rim, faint energy flowing wrist → fingertips, and a forearm that
    /// dissolves into light below the wrist instead of ending in a cut. Sparkles trail off the fingers, light motes
    /// gather into the seam, and a drawn card leaves a comet streak.</item>
    /// </list>
    /// Never opaque, owns no collider (picks stay on the cards), fades out after 4 s with an empty hand.
    /// See FLOATING_HAND.md.
    /// </summary>
    public class ArFloatingHand : MonoBehaviour
    {
        /// <summary>A/B switch for the Editor: false = the old free-floating fan (<see cref="ArHandVolume.ComputeFanSlot"/>).</summary>
        public static bool Enabled = true;

        /// <summary>
        /// Editor inspection only: values above 1 slow the draw and carry / place gestures down (seat motions owned by
        /// the disk keep their own pace; the hand still rides them). Leave at 1.
        /// </summary>
        public static float SlowMotion = 1f;

        /// <summary>Gesture time step: unscaled, capped so a frame hitch cannot pop a gesture ahead of the cards.</summary>
        static float GestureDt => Mathf.Min(Time.unscaledDeltaTime, 1f / 20f) / Mathf.Max(0.05f, SlowMotion);

        public const string HandResource = "WRLDZ/Avatar3D/argon_hand_right";
        /// <summary>Wrist → middle fingertip of a real hand (m). The FBX is authored 1:1.</summary>
        public const float RealHandLength = 0.19f;
        public const float SelectLiftM = 0.04f;
        public const float SelectTiltDeg = 14f;
        public const float DrawSeconds = 0.9f;
        public const float DrawSecondsChained = 0.62f;
        public const float RiffleSeconds = 1.15f;
        public const float EmptyHideSeconds = 4f;
        /// <summary>Peek lift for the card under the pointer (m).</summary>
        public const float HoverLiftM = 0.014f;
        /// <summary>Where the present grip clamps the card: this fraction of its height up from the bottom edge.</summary>
        public const float GripInset = 0.28f;
        public const float PlaceGripSeconds = 0.18f;
        public const float PlaceCarrySeconds = 0.46f;
        public const float PlaceReleaseSeconds = 0.38f;
        /// <summary>Arc height of a carried card, metres.</summary>
        public const float PlaceRiseM = 0.07f;
        /// <summary>
        /// Hand length in card heights in EditorSim / phone RT (owner 2026-09-27: 50% bigger than the old 0.9).
        /// It grows around the thumb-web grip, so the fan stays where it was. Life-size AR stays 1:1 with a real hand.
        /// </summary>
        public const float SimHandCardHeights = 1.35f;
        /// <summary>A real hand is ~2.2 card heights (19 cm hand, 8.6 cm card): the hand's size next to a disk card.</summary>
        public const float RealHandCardHeights = RealHandLength / 0.086f;
        /// <summary>
        /// Fan cards are this fraction of the card body thickness, on the card's local z only (the anime disk body
        /// is ~5 mm real; ten of those never fit between the thumb and the fingers).
        /// </summary>
        public const float HeldCardThickness = 0.6f;
        /// <summary>Front card face behind the thumb pad, back card face in front of the finger pads (m).</summary>
        const float FanFrontGapM = 0.008f, FanBackGapM = 0.003f;
        /// <summary>Per-card easing (1/s): lifts / tilts, and the fan re-spread, depth and size.</summary>
        const float LiftRate = 16f, SlotRate = 11f;
        /// <summary>Render queues: after the aura (3010–3020), the deck well and field holos (3100–3150), so the
        /// hand's depth pass never punches a hand-shaped hole into effects drawn after it; FX after the hand.</summary>
        const int HandQueue = 3160, HandFxQueue = 3170;

        /// <summary>Hologram presence (owner: the hand is never opaque).</summary>
        public float Alpha = 0.5f;

        /// <summary>
        /// Real right hand (mixed reality): the fan and this hand follow it. Its mesh stays hidden unless this is
        /// on, because the player's own hand is right there in the passthrough; the spirit sparkles still show.
        /// </summary>
        public bool ShowGhostOverRealHand;
        /// <summary>
        /// Draw no hand mesh and no spirit sparkles: a body's own arm does the gesture (the AI opponent duelist,
        /// <see cref="ArOpponentDuelist"/>, IK-solves its right hand onto <see cref="WristPivot"/> every frame).
        /// Cards, timing and choreography are unchanged.
        /// </summary>
        public bool HideModel;
        /// <summary>The hand's wrist this frame: +Y fingers, +X thumb, palm −Z; scale = hand metres → world. Null before build.</summary>
        public Transform WristPivot => _modelPivot;
        /// <summary>Finger-shape weights this frame (0..1): fan hold, pinch (drawing), present (carrying a card).</summary>
        public float GripFan => _wFan;
        public float GripPinch => _wPinch;
        public float GripPresent => _wPresent;
        /// <summary>Drawing, riffling, carrying or placing: an actor must not take the hand away now.</summary>
        public bool IsBusy => IsDrawing || IsRiffling || _carry != null || _placeCo != null;
        /// <summary>
        /// Tracked grip (controller-grip convention: +Z out of the fist, +Y toward the thumb) → hand model frame
        /// (+Y fingers, +X thumb, palm −Z). The one rotation to tune on a new device.
        /// </summary>
        public static readonly Quaternion GripToModel = Quaternion.LookRotation(Vector3.right, Vector3.forward);
        bool _tracked;
        Vector3 _trackedPos;
        Quaternion _trackedRot = Quaternion.identity;
        /// <summary>The fan is held by the player's real, tracked right hand.</summary>
        public bool IsTrackedGrip => _tracked;
        /// <summary>Degrees between neighbouring cards and the cap for the whole fan.</summary>
        public float FanStepDeg = 17f;
        public float FanMaxSpreadDeg = 100f;

        public ArHandVolume Volume { get; private set; }
        public bool IsReady => _model != null;
        /// <summary>True when the hand owns the card layout (switch on and the FBX loaded).</summary>
        public bool IsLive => Enabled && IsReady;
        public bool IsRiffling => _riffleCo != null;
        public bool IsDrawing => _drawCo != null || _drawQueue.Count > 0;
        public ArFloatingCard Selected { get; private set; }
        /// <summary>Card under the pointer / finger (peek lift). Null when nothing is hovered.</summary>
        public ArFloatingCard Hover { get; private set; }
        /// <summary>The card the hand is holding (drag) or carrying to a zone.</summary>
        public ArFloatingCard Carried => _carry;
        public bool IsCarrying => _carry != null && _carryTarget > 0f;
        /// <summary>A place gesture is running: input to the hand and disk should wait.</summary>
        public bool IsPlacing => _placeCo != null;
        /// <summary>True when the FBX carries the Present shape and its grip anchors.</summary>
        public bool HasPresentGrip => _hasPresent;

        static bool _shufflePending;

        Transform _modelPivot;
        Transform _model;
        SkinnedMeshRenderer _smr;
        Renderer[] _renderers;
        Material _mat;
        int _bsRelaxed = -1, _bsFan = -1, _bsPinch = -1, _bsPresent = -1;
        float _wRelaxed, _wFan, _wPinch, _wPresent;
        float _drawPinch;       // 0..1 pinch override while drawing
        float _alphaNow;
        float _emptySince = -1f;

        // Anchors in canonical hand space, metres: origin wrist, +Y fingers, +Z away from the palm side
        // (palm faces -Z = the viewer), +X thumb side.
        Vector3 _cGrip, _cPinch, _cIndex, _cPinky, _cThumb, _cMiddle;
        Vector3 _cPresentGrip, _cPresentTip;
        bool _hasPresent;
        /// <summary>Finger pads behind the fan (FanHold): the back card sits just in front of this plane.</summary>
        Vector3 _cPads;
        bool _hasPads;
        float _fanLeanDeg = 18f;
        Quaternion _modelFrameInv = Quaternion.identity;
        Vector3 _modelWrist;
        float _modelUnit = 1f;

        // Hand root pose relative to the volume (written every Tick).
        bool _overridePose;
        Vector3 _ovPos;
        Quaternion _ovRot = Quaternion.identity;
        float _ovBlend;

        // Carry: the hand pose is derived from the held card every frame; the blend ramps the override in and out.
        ArFloatingCard _carry;
        float _carryTarget;        // 1 = holding, 0 = letting go
        float _carryBlend;
        float _carryFollowUntil = -1f;
        bool _carryOverride;
        Vector3 _carryPos;
        Quaternion _carryRot = Quaternion.identity;
        /// <summary>Hand size relative to its fan size while carrying: it shrinks with a card going onto the disk.</summary>
        float _carryScale = 1f;
        /// <summary>After contact the floating card is handed off to the disk's own visual: keep riding that one.</summary>
        ArDiskZone _followZone;
        Coroutine _placeCo;
        System.Action<bool> _placeDone;
        // a card taken while the hand was still holding / letting go of the last one: cross-fade the grip pose
        Vector3 _carryFromPos;
        Quaternion _carryFromRot = Quaternion.identity;
        float _carryFromScale = 1f;
        float _carryXfade = 1f;

        // Spirit FX: shader uniforms, fingertip sparkles, wrist-seam motes, draw streak.
        /// <summary>Seam centre in hand lengths from the wrist (negative = down the forearm) and its noisy width: the
        /// wrist narrowing and the heel of the palm, strong hand cues, stay drawn.</summary>
        const float DissolveAt = -0.18f;
        const float HandDissolve = 0.12f;
        const float SparkleRate = 9f;
        const float SeamMoteRate = 16f;
        ParticleSystem _sparkles;
        ParticleSystem _seamMotes;
        TrailRenderer _drawStreak;
        Material _sparkleMat, _sparkleTrailMat, _moteMat, _streakMat;
        float _sparkleAcc, _moteAcc;
        Color _accent = new(0.25f, 0.9f, 1f, 1f);
        Color _accentApplied = Color.clear;
        Vector3 _wristW, _axisW;
        float _lenW;

        readonly Queue<ArFloatingCard> _drawQueue = new();
        ArFloatingCard _drawing;
        Coroutine _drawCo;
        Coroutine _riffleCo;
        // riffle state read by SlotFor
        float _riffleClose;             // 0 = fan, 1 = closed stack
        readonly Dictionary<ArFloatingCard, Vector3> _riffleJitter = new();
        readonly Dictionary<ArFloatingCard, int> _riffleRank = new();
        /// <summary>Engine index of each card BEFORE the shuffle: the fan closes from the old angles.</summary>
        readonly Dictionary<ArFloatingCard, int> _riffleFromIndex = new();
        bool _riffleReopen;
        float _riffleRoll;

        /// <summary>
        /// Build the hand under a player hand volume. Returns the component even when the FBX is missing
        /// (<see cref="IsReady"/> false) so the volume falls back to the old layout.
        /// </summary>
        public static ArFloatingHand Attach(ArHandVolume volume)
        {
            if (volume == null) return null;
            var go = new GameObject("FloatingHand");
            go.transform.SetParent(volume.transform, false);
            go.layer = volume.Layer;
            var h = go.AddComponent<ArFloatingHand>();
            h.Volume = volume;
            h.Build();
            return h;
        }

        /// <summary>
        /// Called by the hand SHUFFLE command (DuelUI) right after the engine shuffles the hand. The next
        /// hand sync riffles even if the random order came out unchanged.
        /// </summary>
        public static void NotifyHandShuffled() => _shufflePending = true;

        /// <summary>Consumed by <see cref="ArHandVolume.SyncHand"/>.</summary>
        public static bool ConsumeShufflePending()
        {
            var p = _shufflePending;
            _shufflePending = false;
            return p;
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            GameObject prefab = null;
            try { prefab = Resources.Load<GameObject>(HandResource); }
            catch (System.Exception ex) { Debug.LogWarning("[WRLDZ AR] Floating hand load: " + ex.Message); }
            if (prefab == null)
            {
                Debug.LogWarning("[WRLDZ AR] " + HandResource + " missing — hand cards use the old floating fan.");
                return;
            }

            _modelPivot = new GameObject("HandModelPivot").transform;
            _modelPivot.SetParent(transform, false);
            var inst = Instantiate(prefab, _modelPivot, false);
            inst.name = "ArgonHandRight";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            _model = inst.transform;

            if (!ReadAnchors())
            {
                Debug.LogWarning("[WRLDZ AR] argon_hand_right has no Anchor_* empties — rebuild with make_hand.py.");
                ArObjectUtil.Destroy(_modelPivot.gameObject);
                _model = null;
                _modelPivot = null;
                return;
            }

            // Canonical placement: wrist on the pivot origin, fingers +Y, palm facing -Z, 1 unit = 1 m.
            _model.localRotation = _modelFrameInv;
            _model.localScale = Vector3.one * _modelUnit;
            _model.localPosition = -(_modelFrameInv * _modelWrist) * _modelUnit;

            PromoteBlendShapeMeshes(inst);
            _smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (_smr != null && _smr.sharedMesh != null)
            {
                _bsRelaxed = _smr.sharedMesh.GetBlendShapeIndex("Relaxed");
                _bsFan = _smr.sharedMesh.GetBlendShapeIndex("FanHold");
                _bsPinch = _smr.sharedMesh.GetBlendShapeIndex("Pinch");
                _bsPresent = _smr.sharedMesh.GetBlendShapeIndex("Present");
                _smr.updateWhenOffscreen = true;   // blend shapes move the fingers outside the import bounds
            }

            _mat = BuildMaterial(HandTint(_accent, Volume != null && Volume.IsOpponent), Alpha);
            _renderers = inst.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _renderers)
            {
                var n = r is SkinnedMeshRenderer s && s.sharedMesh != null ? s.sharedMesh.subMeshCount : 1;
                var mats = new Material[Mathf.Max(1, n)];
                for (var i = 0; i < mats.Length; i++) mats[i] = _mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                ArObjectUtil.Destroy(col);
            BuildSpiritFx();
            SetLayerRecursive(gameObject, Volume != null ? Volume.Layer : gameObject.layer);
            _alphaNow = 0f;
            ApplyAlpha(0f);
            Debug.Log($"[WRLDZ AR] Floating hand ready · lean {_fanLeanDeg:0}° · anchors ok · " +
                      $"present grip {(_hasPresent && _bsPresent >= 0 ? "ok" : "MISSING (pinch carry)")} · " +
                      $"fan pads {(_hasPads ? $"ok ({(_cPads.z - _cThumb.z) * 100f:0.0} cm gap)" : "none")}");
        }

        /// <summary>
        /// The FBX imports with Rig = None, which drops the skin, and Unity 6 then builds the hand as a plain
        /// MeshRenderer. A MeshRenderer cannot play blend shapes, so the fingers never left the basis pose (no fan
        /// grip, no pinch, no present). A bone-less SkinnedMeshRenderer on the same mesh plays them.
        /// </summary>
        static void PromoteBlendShapeMeshes(GameObject inst)
        {
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                var go = mf.gameObject;
                if (go.GetComponent<SkinnedMeshRenderer>() != null) continue;
                var mr = go.GetComponent<MeshRenderer>();
                var mats = mr != null ? mr.sharedMaterials : null;
                // one renderer per GameObject: the old pair must be gone before the skinned one is added
                if (mr != null) DestroyImmediate(mr);
                DestroyImmediate(mf);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                if (mats != null) smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;
            }
        }

        bool ReadAnchors()
        {
            Transform Find(string n)
            {
                foreach (var t in _model.GetComponentsInChildren<Transform>(true))
                    if (t.name == n) return t;
                return null;
            }

            var wrist = Find("Anchor_Wrist");
            var palm = Find("Anchor_Palm");
            var normal = Find("Anchor_PalmNormal");
            var middle = Find("Anchor_MiddleTip");
            var grip = Find("Anchor_Grip");
            var pinch = Find("Anchor_Pinch");
            var index = Find("Anchor_IndexTip");
            var pinky = Find("Anchor_PinkyTip");
            var thumb = Find("Anchor_ThumbTip");
            if (wrist == null || palm == null || normal == null || middle == null || grip == null ||
                pinch == null || index == null || pinky == null || thumb == null)
                return false;

            Vector3 M(Transform t) => _model.InverseTransformPoint(t.position);
            var pw = M(wrist);
            var up = M(palm) - pw;
            var nrm = M(normal) - M(palm);
            var len = (M(middle) - pw).magnitude;
            if (up.sqrMagnitude < 1e-10f || nrm.sqrMagnitude < 1e-10f || len < 1e-6f) return false;

            // canonical forward (+Z) points away from the palm side, so the palm faces the viewer at identity
            var frame = Quaternion.LookRotation(-nrm.normalized, up.normalized);
            _modelFrameInv = Quaternion.Inverse(frame);
            _modelWrist = pw;
            // measured against the authored wrist → middle-tip length (0.184 m in make_hand.py), so a 100x
            // import scale or a unit mix-up cannot change the hand's size
            var authored = 0.184f;
            _modelUnit = authored / len;
            Vector3 C(Transform t) => _modelFrameInv * (M(t) - pw) * _modelUnit;
            _cGrip = C(grip);
            _cPinch = C(pinch);
            _cIndex = C(index);
            _cPinky = C(pinky);
            _cThumb = C(thumb);
            _cMiddle = C(middle);
            // optional (older FBX): the present grip between index and middle
            var pGrip = Find("Anchor_PresentGrip");
            var pTip = Find("Anchor_PresentTip");
            _hasPresent = pGrip != null && pTip != null;
            if (_hasPresent)
            {
                _cPresentGrip = C(pGrip);
                _cPresentTip = C(pTip);
                if ((_cPresentTip - _cPresentGrip).sqrMagnitude < 1e-8f) _hasPresent = false;
            }

            // optional (2026-09-30 FBX): the finger pads behind the fan, the stack is fitted to them
            var pads = Find("Anchor_FanPads");
            _hasPads = pads != null;
            if (_hasPads) _cPads = C(pads);

            // fan centre direction: halfway between the finger direction and the grip → fingertip midpoint (the
            // rest-pose tips, Anchor_FanLean, when exported: stable however the FanHold fingers are posed)
            var lean = Find("Anchor_FanLean");
            var tips = (lean != null ? C(lean) : (_cIndex + _cPinky) * 0.5f) - _cGrip;
            var toward = Mathf.Atan2(-tips.x, Mathf.Max(0.001f, tips.y)) * Mathf.Rad2Deg;
            _fanLeanDeg = Mathf.Clamp(toward * 0.5f, 0f, 30f);
            return true;
        }

        static Color HandTint(Color accent, bool opponent = false)
        {
            // the opponent is not the player's avatar: a neutral skin under their Spirit colour
            var hex = opponent ? null : AppSession.Ensure()?.Account?.avatar?.skinHex;
            var skin = new Color(0.90f, 0.78f, 0.67f);
            if (!string.IsNullOrEmpty(hex))
            {
                if (hex[0] != '#') hex = "#" + hex;
                if (ColorUtility.TryParseHtmlString(hex, out var c)) skin = c;
            }

            // skin, pulled a third of the way toward the Spirit magic colour (lifted to a light tint) so it reads
            // as a projection of the same energy as the disk aura
            var spirit = Color.Lerp(accent, Color.white, 0.45f);
            return Color.Lerp(skin, spirit, 0.22f);
        }

        /// <summary>Ink outline: a deep shade of the Spirit colour (anime line art, never plain black on a light).</summary>
        static Color InkFor(Color accent)
        {
            var c = Color.Lerp(accent, new Color(0.04f, 0.03f, 0.10f), 0.72f);
            c.a = 0.85f;
            return c;
        }

        static Color RimFor(Color accent)
        {
            var c = Color.Lerp(accent, Color.white, 0.2f);
            c.a = 1f;
            return c;
        }

        static Material BuildMaterial(Color tint, float alpha)
        {
            // Spirit projection (rim + energy flow + wrist dissolve). Both shaders live where builds include them.
            var holo = Shader.Find("WRLDZ/SpiritHandHolo");
            if (holo != null && holo.isSupported)
            {
                var h = new Material(holo) { name = "FloatingHandSpirit" };
                tint.a = alpha;
                var accent0 = new Color(0.25f, 0.9f, 1f);
                h.SetColor("_Color", tint);
                h.SetColor("_RimColor", RimFor(accent0));
                h.SetColor("_OutlineColor", InkFor(accent0));
                // anime paint: bright flat fill, one cool shadow band, a thin lit-edge rim, a 1.6 px ink line
                h.SetFloat("_Fill", 0.85f);
                h.SetFloat("_ShadowAmt", 0.12f);
                h.SetFloat("_RimAmt", 0.6f);
                h.SetFloat("_OutlineWidth", 1.6f);
                // the spirit glow stays, but thinner: the form has to read first
                h.SetFloat("_RimPower", 3f);
                h.SetFloat("_RimStrength", 1f);
                h.SetFloat("_ScanAmt", 0.04f);
                h.SetFloat("_BandAmt", 0.3f);
                h.SetFloat("_Dissolve", HandDissolve);
                h.SetFloat("_DissolveAt", DissolveAt);
                h.SetFloat("_Fade", 0f);
                h.renderQueue = HandQueue;
                return h;
            }

            var ghost = Shader.Find("WRLDZ/SpiritGhostUnlit");
            Material m;
            if (ghost != null && ghost.isSupported)
            {
                m = new Material(ghost) { name = "FloatingHandGhost" };
                if (m.HasProperty("_Fill")) m.SetFloat("_Fill", 0.85f);
                if (m.HasProperty("_RimPower")) m.SetFloat("_RimPower", 2.2f);
                if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.95f);
            }
            else
            {
                // unlit transparent fallback (art guide: no PBR)
                var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                m = new Material(sh) { name = "FloatingHandUnlit" };
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
            }

            tint.a = alpha;
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            // after the ghost disk (3000) and the field holos so the hand is never erased by their alpha; cards
            // are opaque and write depth, so fingers behind the fan stay hidden and the thumb over the faces shows
            m.renderQueue = HandQueue;
            return m;
        }

        void ApplyAlpha(float a)
        {
            if (_mat == null) return;
            if (_mat.HasProperty("_Fade"))
            {
                // SpiritHandHolo: _Color.a is the glass alpha, _Fade takes the whole projection (rim, seam) down
                _mat.SetFloat("_Fade", Alpha > 0.0001f ? Mathf.Clamp01(a / Alpha) : 0f);
                SetRenderersOn(a > 0.004f);
                return;
            }

            var key = _mat.HasProperty("_Color") ? "_Color" : "_BaseColor";
            if (!_mat.HasProperty(key)) return;
            var c = _mat.GetColor(key);
            c.a = a;
            _mat.SetColor(key, c);
            SetRenderersOn(a > 0.004f);
        }

        void SetRenderersOn(bool on)
        {
            on = on && !HideModel && (!_tracked || ShowGhostOverRealHand);
            if (_renderers != null)
                foreach (var r in _renderers)
                    if (r != null && r.enabled != on)
                        r.enabled = on;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        // ------------------------------------------------------------------ sizes

        bool LifeSize => Volume != null && Volume.Tracker != null && !(Volume.Tracker is SimulatedArmTracker);

        float CardScaleFor(int count)
        {
            var baseScale = Volume != null ? Volume.CardScale : 0.22f;
            return baseScale * Mathf.Lerp(1f, 0.86f, Mathf.InverseLerp(5f, 10f, count));
        }

        /// <summary>Volume-local units per real metre for the hand (1 on lenses / ARCore; card-matched in EditorSim).</summary>
        public float MetreScale
        {
            get
            {
                if (LifeSize)
                {
                    var ls = Volume != null ? Volume.transform.lossyScale.y : 1f;
                    return 1f / Mathf.Max(0.01f, ls);
                }

                // EditorSim / phone RT: cards are oversized for picking; the hand is SimHandCardHeights card heights
                var cardH = ArFloatingCard.BodyLocalSize.y * CardScaleFor(5);
                return cardH * SimHandCardHeights / RealHandLength;
            }
        }

        // ------------------------------------------------------------------ poses

        Quaternion RestRotation()
        {
            // palm toward the eye, top tipped back a little, palm yawed toward the body midline,
            // fingers leaning left so the fan opens over the fingers (right hand, pivot at the thumb web)
            return Quaternion.Euler(6f, 10f, 12f);
        }

        /// <summary>The player's real right-hand grip (world) drives the rest pose this frame.</summary>
        public void SetTrackedGrip(Pose worldGrip)
        {
            var parent = transform.parent;
            if (parent == null) return;
            _trackedPos = parent.InverseTransformPoint(worldGrip.position);
            _trackedRot = Quaternion.Inverse(parent.rotation) * worldGrip.rotation * GripToModel;
            _tracked = true;
        }

        public void ClearTrackedGrip() => _tracked = false;

        /// <summary>
        /// A body's own right wrist (world, this class's canonical hand frame: +Y fingers, +X thumb, palm −Z) holds the
        /// fan this frame: the rest pose and the fan sit exactly on it (the AI duelist while it gestures).
        /// </summary>
        public void SetTrackedWrist(Vector3 worldWrist, Quaternion worldRot)
        {
            var parent = transform.parent;
            if (parent == null) return;
            _trackedRot = Quaternion.Inverse(parent.rotation) * worldRot;
            _trackedPos = parent.InverseTransformPoint(worldWrist) + _trackedRot * (_cGrip * MetreScale);
            _tracked = true;
        }

        void RestPose(out Vector3 pos, out Quaternion rot)
        {
            var m = MetreScale;
            if (_tracked)
            {
                // the model's thumb-web grip sits on the tracked palm
                rot = _trackedRot;
                pos = _trackedPos - rot * (_cGrip * m);
                return;
            }

            var cs = CsNow(FanCount());
            var h = ArFloatingCard.BodyLocalSize.y * cs;
            var w = ArFloatingCard.BodyLocalSize.x * cs;
            var lateral = LifeSize ? 0.20f * m : 0.95f * w;
            var t = Time.time;
            var grip = new Vector3(lateral, -0.52f * h, 0f) + Vector3.up * (Mathf.Sin(t * 1.7f) * 0.005f * m);
            rot = RestRotation() * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.1f) * 1.2f + _riffleRoll);
            pos = grip - rot * (_cGrip * m);
        }

        void CurrentHandPose(out Vector3 pos, out Quaternion rot)
        {
            RestPose(out pos, out rot);
            if (_overridePose && _ovBlend > 0f)
            {
                pos = Vector3.Lerp(pos, _ovPos, _ovBlend);
                rot = Quaternion.Slerp(rot, _ovRot, _ovBlend);
            }
        }

        /// <summary>One card's place in the held fan, in hand space around the thumb-web grip.</summary>
        struct FanSlot
        {
            public float Angle;     // roll about the grip, degrees (+ = toward the leftmost card)
            public float Depth;     // along the palm normal, grip-relative: the stack rank
            public float Lift;      // out of the fan along the card's up (hover peek / select)
            public float Tilt;      // top toward the eye, degrees (select)
            public float Scale;     // card body scale
            public Vector3 Jitter;  // riffle flick: x, y in card heights, z degrees
        }

        /// <summary>Per-card eased slot: the hand writes the fan rigidly every frame and eases only these.</summary>
        sealed class SlotState
        {
            public FanSlot S;
            public int Frame = -1;
        }

        readonly Dictionary<ArFloatingCard, SlotState> _slotStates = new();
        readonly List<ArFloatingCard> _deadStates = new();
        /// <summary>Card scale the fan and the rest pose use, eased: crossing 5 → 6 cards must not pop the hand.</summary>
        float _csNow = -1f;

        /// <summary>Cards the fan is laid out for: the engine hand minus the draws the hand has not started yet
        /// (the fan opens a gap as each card is actually drawn, not the moment the engine counts it).</summary>
        int FanCount()
        {
            var n = Volume != null ? Volume.SlotCount : 0;
            return Mathf.Max(1, n - _drawQueue.Count);
        }

        float CsNow(int count) => _csNow > 0f ? _csNow : CardScaleFor(count);

        /// <summary>
        /// Rest pose of hand card <paramref name="i"/> of <paramref name="count"/> in <see cref="ArHandVolume.VolumeRoot"/>
        /// space: an arc around the thumb-web grip, rightmost card nearest the eye. No lift (the unlifted slot).
        /// </summary>
        public void SlotFor(int i, int count, out Vector3 localPos, out Quaternion localRot, out Vector3 localScale)
        {
            var slot = SlotTarget(i, count, null);
            ComposeSlot(in slot, out localPos, out localRot, out localScale);
        }

        void SlotFor(int i, int count, ArFloatingCard card,
            out Vector3 localPos, out Quaternion localRot, out Vector3 localScale)
        {
            var slot = SlotTarget(i, count, card);
            ComposeSlot(in slot, out localPos, out localRot, out localScale);
        }

        FanSlot SlotTarget(int i, int count, ArFloatingCard card)
        {
            count = Mathf.Max(1, count);
            i = Mathf.Clamp(i, 0, count - 1);
            var m = MetreScale;
            var cs = CsNow(count);
            var th = ArFloatingCard.BodyLocalSize.z * cs * HeldCardThickness;

            var spread = count > 1 ? Mathf.Min(FanStepDeg * (count - 1), FanMaxSpreadDeg) : 0f;
            float AngleOf(int idx) =>
                count > 1 ? spread * 0.5f - Mathf.Clamp(idx, 0, count - 1) * spread / (count - 1) : 0f;
            var a = AngleOf(i);

            // the front card sits just behind the thumb pad; the rest stack away from the eye toward the finger
            // pads, and the step shrinks so the whole hand fits in the thumb → pad gap (no card inside the fingers)
            var frontZ = _cThumb.z * m + th * 0.5f + FanFrontGapM * m;
            var dz = th * 1.25f + 0.002f * m;
            if (count > 1 && _hasPads)
            {
                var backZ = _cPads.z * m - th * 0.5f - FanBackGapM * m;
                dz = Mathf.Min(dz, Mathf.Max(th * 1.02f, (backZ - frontZ) / (count - 1)));
            }

            var rank = count - 1 - i;
            var slot = new FanSlot { Scale = cs };
            if (_riffleClose > 0f && card != null)
            {
                // closing from the OLD order, reopening into the new one
                if (!_riffleReopen && _riffleFromIndex.TryGetValue(card, out var fi)) a = AngleOf(fi);
                a = Mathf.Lerp(a, 0f, _riffleClose);
                if (_riffleRank.TryGetValue(card, out var rr)) rank = Mathf.Clamp(rr, 0, count - 1);
                if (_riffleJitter.TryGetValue(card, out var j)) slot.Jitter = j;
            }
            else if (card != null && card == Selected)
            {
                slot.Lift = SelectLiftM * m;
                slot.Tilt = SelectTiltDeg;
            }
            else if (card != null && card == Hover)
            {
                slot.Lift = HoverLiftM * m;   // peek: readable, still in the fan
            }

            slot.Angle = a;
            // depth is measured in hand space; the arc is built around the grip, so make it grip-relative
            slot.Depth = frontZ + rank * dz - _cGrip.z * m;
            return slot;
        }

        void ComposeSlot(in FanSlot slot, out Vector3 localPos, out Quaternion localRot, out Vector3 localScale)
        {
            var m = MetreScale;
            var h = ArFloatingCard.BodyLocalSize.y * slot.Scale;
            var r = 0.46f * h;
            var roll = Quaternion.Euler(0f, 0f, slot.Angle + slot.Jitter.z);
            var offset = new Vector3(slot.Jitter.x * h, slot.Jitter.y * h, slot.Depth);
            var center = roll * new Vector3(0f, r + slot.Lift, 0f) + offset;
            var tiltQ = Quaternion.identity;
            if (Mathf.Abs(slot.Tilt) > 0.001f)
            {
                // the tilt pivots at the card's bottom edge (the grip line): only the top comes toward the eye, the
                // bottom never swings back through the thumb or the card in front
                tiltQ = Quaternion.Euler(-slot.Tilt, 0f, 0f);
                var pivot = roll * new Vector3(0f, r + slot.Lift - h * 0.5f, 0f) + offset;
                center = pivot + (roll * tiltQ * Quaternion.Inverse(roll)) * (center - pivot);
            }

            // The fan hangs at the hand's REST grip, not wherever the hand is right now: when the hand leaves to draw
            // or to play a card, the other cards stay put (Solid Vision) instead of flying off with it palm-down.
            RestPose(out var hp, out var hr);
            var fan = Quaternion.Euler(0f, 0f, _fanLeanDeg);
            var gripLocal = _cGrip * m;
            localPos = hp + hr * (gripLocal + fan * center);
            // art normal (+Z) turned to face the eye; a tilt brings the lifted card's top toward the viewer
            localRot = hr * fan * roll * tiltQ * Quaternion.Euler(0f, 180f, 0f);
            localScale = new Vector3(slot.Scale, slot.Scale, slot.Scale * HeldCardThickness);
        }

        /// <summary>
        /// The hand card under <paramref name="worldRay"/> for hover, tap and drag. Boxes are tested on this frame's
        /// transforms (physics auto-sync is off, so collider raycasts saw last step's poses). The selected card's
        /// lifted box wins first; the hovered card keeps the pick while the ray is on its lifted box OR its unlifted
        /// rest box unless a card in front of it is hit — so its own peek cannot move it out from under the pointer
        /// and flicker. Otherwise the nearest box along the ray. Null while riffling or with nothing hit.
        /// </summary>
        public ArFloatingCard PickCard(Ray worldRay)
        {
            if (!IsLive || Volume == null || IsRiffling) return null;
            var cards = Volume.Cards;
            var count = FanCount();
            var half = ArFloatingCard.BodyLocalSize * 0.5f;
            var root = Volume.VolumeRoot != null ? Volume.VolumeRoot : transform.parent;
            var rootM = root != null ? root.localToWorldMatrix : Matrix4x4.identity;

            bool Pickable(ArFloatingCard c) =>
                c != null && !c.IsLockedToZone && !c.IsDragging && !c.IsDrawArriving &&
                c.gameObject.activeInHierarchy && Volume.SlotIndexOf(c) >= 0;
            bool HitLive(ArFloatingCard c, out float t) =>
                RayHitsBox(worldRay, c.transform.localToWorldMatrix, half, out t);
            bool HitRest(ArFloatingCard c)
            {
                SlotFor(Volume.SlotIndexOf(c), count, out var p, out var r, out var sc);
                return RayHitsBox(worldRay, rootM * Matrix4x4.TRS(p, r, sc), half, out _);
            }

            var sel = Selected;
            if (Pickable(sel) && HitLive(sel, out _)) return sel;

            ArFloatingCard best = null;
            var bestT = float.MaxValue;
            for (var i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!Pickable(c) || !HitLive(c, out var t)) continue;
                if (t < bestT - 1e-5f ||
                    (best != null && Mathf.Abs(t - bestT) <= 1e-5f && Volume.SlotIndexOf(c) > Volume.SlotIndexOf(best)))
                {
                    best = c;
                    bestT = t;
                }
            }

            var hov = Hover;
            if (Pickable(hov) && hov != best)
            {
                var frontHit = best != null && Volume.SlotIndexOf(best) > Volume.SlotIndexOf(hov);
                if (!frontHit && (HitLive(hov, out _) || HitRest(hov))) return hov;
            }

            return best;
        }

        /// <summary>Ray vs an oriented box (slab test in the box's own space). <paramref name="t"/> is along the ray.</summary>
        static bool RayHitsBox(Ray ray, Matrix4x4 boxToWorld, Vector3 half, out float t)
        {
            t = 0f;
            var inv = boxToWorld.inverse;
            var o = inv.MultiplyPoint3x4(ray.origin);
            var d = inv.MultiplyVector(ray.direction);
            var tMin = 0f;
            var tMax = float.MaxValue;
            for (var axis = 0; axis < 3; axis++)
            {
                var oa = o[axis];
                var da = d[axis];
                var ha = half[axis];
                if (Mathf.Abs(da) < 1e-9f)
                {
                    if (oa < -ha || oa > ha) return false;
                    continue;
                }

                var t1 = (-ha - oa) / da;
                var t2 = (ha - oa) / da;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }

            t = tMin;
            return true;
        }

        // ------------------------------------------------------------------ per frame (driven by ArHandVolume)

        /// <summary>
        /// Called from <see cref="ArHandVolume"/> LateUpdate after the volume is placed: hand pose, blend
        /// shapes, fade, and the fan rest targets for every idle card.
        /// </summary>
        public void Tick(IReadOnlyList<ArFloatingCard> cards)
        {
            if (!IsReady) return;
            var live = IsLive;
            if (_modelPivot != null && _modelPivot.gameObject.activeSelf != live)
                _modelPivot.gameObject.SetActive(live);
            if (!live)
            {
                if (cards != null)
                    for (var c = 0; c < cards.Count; c++)
                        if (cards[c] != null) cards[c].HeldByHand = false;
                return;
            }

            var count = Volume != null ? Volume.SlotCount : 0;
            if (Selected != null && (Selected.IsLockedToZone || Selected.IsDragging || Volume == null ||
                                     Volume.SlotIndexOf(Selected) < 0))
                Selected = null;
            if (Hover != null && (Hover.IsLockedToZone || Hover.IsDragging || Hover.IsDrawArriving ||
                                  Volume == null || Volume.SlotIndexOf(Hover) < 0))
                Hover = null;

            TickCarry();
            var dt = Time.unscaledDeltaTime;
            var csTarget = CardScaleFor(FanCount());
            _csNow = _csNow > 0f ? Mathf.Lerp(_csNow, csTarget, 1f - Mathf.Exp(-8f * dt)) : csTarget;

            // pose + placement (a carried card going onto the disk shrinks the hand with it)
            var carryK = _carryBlend > 0.001f ? Mathf.Lerp(1f, _carryScale, Mathf.SmoothStep(0f, 1f, _carryBlend)) : 1f;
            _modelPivot.localScale = Vector3.one * (MetreScale * carryK);
            CurrentHandPose(out var hp, out var hr);
            transform.localPosition = hp;
            transform.localRotation = hr;

            // finger shapes: FanHold with cards, Relaxed when empty, Pinch while drawing, Present while carrying.
            // Pinch / Present follow their own eased drivers exactly; the fan grip opens (and the empty hand
            // relaxes) with an ease of its own, so the first draw into an empty hand does not snap the fingers.
            var settled = Mathf.Max(0, count - _drawQueue.Count - (_drawing != null && _drawing.IsDrawArriving ? 1 : 0));
            var hasCards = settled > 0;
            var k = 1f - Mathf.Exp(-14f * dt);
            _wHasCards = Mathf.Lerp(_wHasCards, hasCards ? 1f : 0f, k);
            var tPresent = _hasPresent && _bsPresent >= 0 ? _carryBlend : 0f;
            var tPinch = Mathf.Clamp01(_drawPinch + (_hasPresent && _bsPresent >= 0 ? 0f : _carryBlend));
            var gesture = _drawCo != null || _carryBlend > 0.001f;
            _wPinch = Mathf.Lerp(_wPinch, tPinch, gesture ? 1f : k);
            _wPresent = Mathf.Lerp(_wPresent, tPresent, gesture ? 1f : k);
            // weights always sum to 1 (the shapes are deltas from the flat basis; >1 would over-curl)
            var rest = Mathf.Clamp01(1f - _wPinch - _wPresent);
            _wFan = rest * _wHasCards;
            _wRelaxed = rest * (1f - _wHasCards);
            if (_smr != null)
            {
                if (_bsRelaxed >= 0) _smr.SetBlendShapeWeight(_bsRelaxed, _wRelaxed * 100f);
                if (_bsFan >= 0) _smr.SetBlendShapeWeight(_bsFan, _wFan * 100f);
                if (_bsPinch >= 0) _smr.SetBlendShapeWeight(_bsPinch, _wPinch * 100f);
                if (_bsPresent >= 0) _smr.SetBlendShapeWeight(_bsPresent, _wPresent * 100f);
            }

            // empty hand: fade after EmptyHideSeconds, return on the next draw
            var busy = IsDrawing || IsRiffling || _carry != null || _placeCo != null;
            if (hasCards || busy) _emptySince = -1f;
            else if (_emptySince < 0f) _emptySince = Time.unscaledTime;
            var hide = _emptySince >= 0f && Time.unscaledTime - _emptySince > EmptyHideSeconds;
            var targetA = hide ? 0f : Alpha;
            _alphaNow = Mathf.MoveTowards(_alphaNow, targetA, dt * (hide ? 0.8f : 1.6f));
            ApplyAlpha(_alphaNow);
            TickSpiritFx();

            if (cards == null) return;
            DriveFan(cards, dt);
        }

        float _wHasCards;

        /// <summary>
        /// Every idle card is placed by the hand this frame (rigid with it: no spring lag, no per-card bob). Slot
        /// changes are eased per card: lifts and tilts fast, the re-spread / depth / size a little slower. A card
        /// returning from a drag or a failed drop keeps snapping on its own while its rest is refreshed here, so it
        /// lands exactly in the slot; the riffle drives depth ranks itself (flicks are meant to be instant).
        /// </summary>
        void DriveFan(IReadOnlyList<ArFloatingCard> cards, float dt)
        {
            var fanCount = FanCount();
            var riffling = _riffleCo != null;
            var kLift = 1f - Mathf.Exp(-LiftRate * dt);
            var kSlot = 1f - Mathf.Exp(-SlotRate * dt);
            var frame = Time.frameCount;
            for (var c = 0; c < cards.Count; c++)
            {
                var fc = cards[c];
                if (fc == null || fc.IsLockedToZone || fc.IsDrawArriving) continue;
                var idx = Volume.SlotIndexOf(fc);
                if (idx < 0) continue;
                if (!_slotStates.TryGetValue(fc, out var st))
                {
                    st = new SlotState();
                    _slotStates[fc] = st;
                }

                if (fc.IsDragging)
                {
                    // out with the pointer or in the hand's grip: keep its unlifted slot fresh, so the return lands home
                    var home = SlotTarget(idx, fanCount, null);
                    st.S = home;
                    st.Frame = frame;
                    ComposeSlot(in home, out var hp, out var hr, out var hs);
                    fc.HeldByHand = true;
                    fc.SetHandRest(hp, hr, hs);
                    continue;
                }

                var target = SlotTarget(idx, fanCount, fc);
                if (st.Frame < frame - 1)
                {
                    st.S = target;   // first sight, or back from a drag: nothing stale to ease from
                }
                else
                {
                    st.S.Angle = Mathf.Lerp(st.S.Angle, target.Angle, kSlot);
                    st.S.Depth = riffling ? target.Depth : Mathf.Lerp(st.S.Depth, target.Depth, kSlot);
                    st.S.Scale = Mathf.Lerp(st.S.Scale, target.Scale, kSlot);
                    st.S.Lift = Mathf.Lerp(st.S.Lift, target.Lift, kLift);
                    st.S.Tilt = Mathf.Lerp(st.S.Tilt, target.Tilt, kLift);
                    st.S.Jitter = target.Jitter;
                }

                st.Frame = frame;
                ComposeSlot(in st.S, out var p, out var r, out var s);
                fc.HeldByHand = true;
                if (fc.IsSnapAnimating) fc.SetHandRest(p, r, s);   // a return in flight tracks this and lands on it
                else fc.SetHandRestImmediate(p, r, s);
            }

            if ((frame & 63) == 0 && _slotStates.Count > cards.Count)
            {
                _deadStates.Clear();
                foreach (var kv in _slotStates)
                    if (kv.Key == null || Volume.SlotIndexOf(kv.Key) < 0) _deadStates.Add(kv.Key);
                foreach (var dead in _deadStates) _slotStates.Remove(dead);
            }
        }

        // ------------------------------------------------------------------ select

        /// <summary>Tap on a hand card: lift it (or drop it back when it was already lifted).</summary>
        public void ToggleSelected(ArFloatingCard card)
        {
            if (card == null || IsRiffling) return;
            Selected = Selected == card ? null : card;
        }

        public void ClearSelected() => Selected = null;

        /// <summary>Peek: the card under the pointer (Editor / desktop hover, or the finger before a drag).</summary>
        public void SetHover(ArFloatingCard card)
        {
            if (card != null && (card.IsDragging || card.IsLockedToZone || card.IsDrawArriving || IsRiffling))
                card = null;
            Hover = card;
        }

        // ------------------------------------------------------------------ carry / place

        /// <summary>
        /// The hand takes hold of a card that the pointer is about to move (drag start). Returns false while the
        /// hand is busy drawing or placing; the drag then runs as before, without the hand.
        /// </summary>
        public bool BeginCarry(ArFloatingCard card)
        {
            if (!IsLive || card == null || IsDrawing || IsRiffling || _placeCo != null) return false;
            TakeCarry(card);
            return true;
        }

        /// <summary>Hold <paramref name="card"/>; if the hand was still holding / letting go of another, cross-fade.</summary>
        void TakeCarry(ArFloatingCard card)
        {
            if (Selected == card) Selected = null;
            if (Hover == card) Hover = null;
            if (_carryBlend > 0.001f)
            {
                _carryFromPos = _carryPos;
                _carryFromRot = _carryRot;
                _carryFromScale = _carryScale;
                _carryXfade = 0f;
            }
            else
            {
                _carryXfade = 1f;
            }

            _carry = card;
            _carryTarget = 1f;
            _carryFollowUntil = -1f;
            _followZone = null;
        }

        void ReleaseCarry()
        {
            _carry = null;
            _carryTarget = 0f;
            _carryFollowUntil = -1f;
            _followZone = null;
        }

        /// <summary>The hand's pose is (still) shaped by a held card or a place: a draw waits for it.</summary>
        bool CarryHoldsPose => _carryBlend > 0.001f || _placeCo != null;

        /// <summary>
        /// Let go. With <paramref name="followSeconds"/> the hand keeps tracking the card that long first (the
        /// card's own return / seat motion), then eases back to the fan.
        /// </summary>
        public void EndCarry(float followSeconds = 0f)
        {
            // Never gated on the card still existing: after a place the disk's own visual has already taken the
            // card over (the floating one is destroyed), and returning early here left the hand following the
            // seated card for good instead of going back to the fan.
            if (followSeconds > 0f && _carry != null)
            {
                _carryFollowUntil = Time.unscaledTime + followSeconds;
                return;
            }

            ReleaseCarry();
        }

        /// <summary>
        /// Carry <paramref name="card"/> to <paramref name="zone"/> and set it down. <paramref name="atContact"/>
        /// runs when the card touches the zone (the engine commit + <see cref="ArDiskZone.SnapLock"/>); return
        /// false there and the hand brings the card back to the fan. The hand then rides the seat motion and
        /// returns. False when the hand cannot do it now (busy, not live): the caller commits directly.
        /// </summary>
        public bool PlayPlace(ArFloatingCard card, ArDiskZone zone, ArZoneOrientation orient,
            System.Func<bool> atContact, System.Action<bool> done = null)
        {
            if (!IsLive || !isActiveAndEnabled || card == null || zone == null) return false;
            if (IsDrawing || IsRiffling || _placeCo != null) return false;
            if (_carry != null && _carry != card) return false;
            _placeDone = done;
            _placeCo = StartCoroutine(PlaceCo(card, zone, orient, atContact));
            return true;
        }

        void FinishPlace(bool ok)
        {
            _placeCo = null;
            var d = _placeDone;
            _placeDone = null;
            d?.Invoke(ok);
        }

        IEnumerator PlaceCo(ArFloatingCard card, ArDiskZone zone, ArZoneOrientation orient,
            System.Func<bool> atContact)
        {
            var root = Volume != null && Volume.VolumeRoot != null ? Volume.VolumeRoot : transform.parent;
            var m = MetreScale;
            var wasCarrying = _carry == card && _carryBlend > 0.5f;
            var idx = Volume != null ? Volume.SlotIndexOf(card) : -1;
            var count = FanCount();
            // a card the pointer dragged out without the hand (it was busy) is lifted where it is, not from the fan
            var fromFan = !card.IsDragging && idx >= 0;
            card.IsDragging = true;   // the hand drives it: idle spring and picks off
            TakeCarry(card);

            if (!wasCarrying)
            {
                // from the fan: the card lifts out toward the eye while the hand changes its grip
                var lp = card.transform.localPosition;
                var lr = card.transform.localRotation;
                var ls = card.transform.localScale;
                Vector3 upP;
                Quaternion upR;
                Vector3 upS;
                if (fromFan)
                {
                    var keep = Selected;
                    Selected = card;
                    SlotFor(idx, count, card, out upP, out upR, out upS);
                    Selected = keep;
                }
                else
                {
                    // already out of the engine hand (a generated opponent play): lift it where it is
                    upP = lp + lr * Vector3.up * (SelectLiftM * m);
                    upR = lr;
                    upS = ls;
                }
                var t0 = 0f;
                while (t0 < PlaceGripSeconds)
                {
                    if (card == null) break;
                    t0 += GestureDt;
                    var e = EaseOut(t0 / PlaceGripSeconds);
                    card.transform.localPosition = Vector3.Lerp(lp, upP, e);
                    card.transform.localRotation = Quaternion.Slerp(lr, upR, e);
                    card.transform.localScale = Vector3.Lerp(ls, upS, e);
                    yield return null;
                }
            }

            // target: exactly where the zone's own seat motion starts, so it continues without a pop
            zone.RefreshFlushLock(orient);
            Vector3 zp;
            Quaternion zr;
            Vector3 zs;
            if (ArZoneLayout.IsSlotKind(zone.Kind))
                ArZoneLayout.GetSlotInsertStart(zone.Kind, orient, out zp, out zr, out zs, zone.transform);
            else
            {
                zp = zone.LockLocalPosition + new Vector3(0f, 0.06f, 0f);
                zr = zone.LockLocalRotation;
                zs = zone.LockLocalScale * 1.08f;
            }

            var startP = card != null ? card.transform.localPosition : Vector3.zero;
            var startR = card != null ? card.transform.localRotation : Quaternion.identity;
            var startS = card != null ? card.transform.localScale : Vector3.one;
            var endL = startP;
            var endRL = startR;
            var endSL = startS;
            var t = 0f;
            while (t < PlaceCarrySeconds)
            {
                if (card == null || zone == null) break;
                t += GestureDt;
                var u = Mathf.Clamp01(t / PlaceCarrySeconds);
                var e = Mathf.SmoothStep(0f, 1f, u);
                // the arm moves: resample the zone every frame
                var zt = zone.transform;
                endL = root.InverseTransformPoint(zt.TransformPoint(zp));
                endRL = Quaternion.Inverse(root.rotation) * (zt.rotation * zr);
                endSL = zs * (zt.lossyScale.x / Mathf.Max(0.0001f, root.lossyScale.x));
                var rise = root.InverseTransformDirection(Vector3.up) * (PlaceRiseM * m);
                var mid = Vector3.Lerp(startP, endL, 0.5f) + rise;
                var o = 1f - e;
                card.transform.localPosition = o * o * startP + 2f * o * e * mid + e * e * endL;
                card.transform.localRotation = Quaternion.Slerp(startR, endRL, e);
                card.transform.localScale = Vector3.Lerp(startS, endSL, e);
                yield return null;
            }

            if (card != null)
            {
                card.transform.localPosition = endL;
                card.transform.localRotation = endRL;
                card.transform.localScale = endSL;
                card.IsDragging = false;
            }

            var ok = card != null && (atContact == null || atContact());
            if (!ok)
            {
                if (card != null) card.ReturnToHandRest();
                EndCarry(ArDiskMotion.PadSnap);
                FinishPlace(false);
                yield break;
            }

            // ride the seat motion (pad snap / toaster present), then let go and return to the fan
            _followZone = zone;
            var follow = zone != null && ArZoneLayout.IsSlotKind(zone.Kind)
                ? ArDiskMotion.SlotInsertPresent + 0.08f
                : ArDiskMotion.PadSnap;
            var tf = 0f;
            while (tf < follow)
            {
                tf += Time.unscaledDeltaTime;   // real time: this rides the disk's own seat motion
                yield return null;
            }

            EndCarry();
            FinishPlace(true);
        }

        // ── reveal: a one-shot Spell shown to the opponent instead of carried to a zone ─────────────

        /// <summary>Raise the card out of the fan into the reveal pose (s).</summary>
        public const float RevealRaiseSeconds = 0.42f;
        /// <summary>Hold it up, face to the opponent, before it activates (s).</summary>
        public const float RevealHoldSeconds = 0.6f;
        /// <summary>The card leaves the hand: it shrinks away as the hologram takes over (s).</summary>
        public const float RevealDissolveSeconds = 0.24f;
        /// <summary>Whole reveal from the fan to the activation (s); the opponent's beat waits this long.</summary>
        public const float RevealSeconds = PlaceGripSeconds + RevealRaiseSeconds + RevealHoldSeconds;
        /// <summary>How far the card rises out of the hand and is pushed toward the opponent (m).</summary>
        public const float RevealRiseM = 0.17f, RevealPushM = 0.10f;
        /// <summary>The opponent's body holds the card up to about head height (their fan sits at the chest).</summary>
        public const float RevealRiseOpponentM = 0.32f, RevealPushOpponentM = 0.14f;

        /// <summary>
        /// One-shot Spell (Normal / Quick-Play / Ritual) from the hand: it goes to the GY after resolving, so the hand
        /// does not set it on a zone (owner 2026-10-04). The hand lifts it out of the fan, raises it and turns its face
        /// to the opponent (the player's hand: away from your eye; the opponent's hand: to you), holds it up, then
        /// <paramref name="atReveal"/> runs (the engine activation; the disk card and the arena hologram take over) and
        /// the card leaves the hand. Return false there and the card goes back to the fan. False when the hand cannot
        /// do it now: the caller commits directly.
        /// </summary>
        public bool PlayReveal(ArFloatingCard card, System.Func<bool> atReveal, System.Action<bool> done = null)
        {
            if (!IsLive || !isActiveAndEnabled || card == null) return false;
            if (IsDrawing || IsRiffling || _placeCo != null) return false;
            if (_carry != null && _carry != card) return false;
            _placeDone = done;
            _placeCo = StartCoroutine(RevealCo(card, atReveal));
            return true;
        }

        IEnumerator RevealCo(ArFloatingCard card, System.Func<bool> atReveal)
        {
            var root = Volume != null && Volume.VolumeRoot != null ? Volume.VolumeRoot : transform.parent;
            var m = MetreScale;
            var wasCarrying = _carry == card && _carryBlend > 0.5f;
            var idx = Volume != null ? Volume.SlotIndexOf(card) : -1;
            var count = FanCount();
            var fromFan = !card.IsDragging && idx >= 0;
            card.IsDragging = true;   // the hand drives it: idle spring and picks off
            card.HeldByGesture = true;   // the activation empties the engine hand under it: this coroutine ends it
            TakeCarry(card);

            if (!wasCarrying && fromFan)
            {
                // out of the fan toward the eye while the hand changes its grip (as a place starts)
                var lp = card.transform.localPosition;
                var lr = card.transform.localRotation;
                var ls = card.transform.localScale;
                var keep = Selected;
                Selected = card;
                SlotFor(idx, count, card, out var upP, out var upR, out var upS);
                Selected = keep;
                var t0 = 0f;
                while (t0 < PlaceGripSeconds)
                {
                    if (card == null) break;
                    t0 += GestureDt;
                    var e = EaseOut(t0 / PlaceGripSeconds);
                    card.transform.localPosition = Vector3.Lerp(lp, upP, e);
                    card.transform.localRotation = Quaternion.Slerp(lr, upR, e);
                    card.transform.localScale = Vector3.Lerp(ls, upS, e);
                    yield return null;
                }
            }

            if (card == null)
            {
                ReleaseCarry();
                FinishPlace(false);
                yield break;
            }

            // the reveal pose: up out of the hand, pushed toward the opponent, the face square to them
            var opponentHand = Volume != null && Volume.IsOpponent;
            var cam = ArStageView.FindCamera(transform);
            var cw = card.transform.position;
            Vector3 artW;
            if (cam != null)
            {
                artW = opponentHand ? cam.transform.position - cw : cam.transform.forward;
                if (!opponentHand) artW.y = 0f;   // yours: level, away from your eye toward the opponent
            }
            else
                artW = opponentHand ? -root.forward : root.forward;
            if (artW.sqrMagnitude < 1e-6f) artW = root.forward;
            artW.Normalize();
            var flat = new Vector3(artW.x, 0f, artW.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : artW;
            if (opponentHand)
            {
                // the opponent's fan keeps its faces hidden from you; this one is shown
                card.KeepFaceHidden = false;
                card.RefreshArtFace(showFace: true);
            }

            var p0 = card.transform.localPosition;
            var r0 = card.transform.localRotation;
            var revealP = p0 + root.InverseTransformDirection(Vector3.up) * ((opponentHand ? RevealRiseOpponentM : RevealRiseM) * m) +
                          root.InverseTransformDirection(flat) * ((opponentHand ? RevealPushOpponentM : RevealPushM) * m);
            // the art is the card's +Z: square to the opponent, upright
            var revealR = Quaternion.Inverse(root.rotation) * Quaternion.LookRotation(artW, Vector3.up);
            var t = 0f;
            while (t < RevealRaiseSeconds)
            {
                if (card == null) break;
                t += GestureDt;
                var e = EaseOut(t / RevealRaiseSeconds);
                card.transform.localPosition = Vector3.Lerp(p0, revealP, e);
                card.transform.localRotation = Quaternion.Slerp(r0, revealR, Mathf.SmoothStep(0f, 1f, t / RevealRaiseSeconds));
                yield return null;
            }

            // hold it up: a short thrust toward the opponent as it lands, then still
            var push = root.InverseTransformDirection(flat) * (0.025f * m);
            var th = 0f;
            while (th < RevealHoldSeconds)
            {
                if (card == null) break;
                th += GestureDt;
                var k = Mathf.Clamp01(th / 0.22f);
                card.transform.localPosition = revealP + push * Mathf.Sin(k * Mathf.PI);
                card.transform.localRotation = revealR;
                yield return null;
            }

            var ok = card != null && (atReveal == null || atReveal());
            if (!ok)
            {
                if (card != null)
                {
                    card.HeldByGesture = false;
                    if (opponentHand) card.KeepFaceHidden = true;
                    card.ReturnToHandRest();
                }

                EndCarry(ArDiskMotion.PadSnap);
                FinishPlace(false);
                yield break;
            }

            // activated: the hand lets go and the card shrinks away — the disk card and the hologram take over
            ReleaseCarry();
            if (card != null)
            {
                Volume?.RemoveCard(card);
                var s0 = card.transform.localScale;
                var from = card.transform.localPosition;
                var td = 0f;
                while (td < RevealDissolveSeconds && card != null)
                {
                    td += GestureDt;
                    var e = Mathf.SmoothStep(0f, 1f, td / RevealDissolveSeconds);
                    card.transform.localPosition = from + push * (1.6f * e);
                    card.transform.localScale = s0 * (1f - e);
                    yield return null;
                }

                if (card != null) ArObjectUtil.Destroy(card.gameObject);
            }

            FinishPlace(true);
        }

        /// <summary>
        /// Hand pose (volume-local) that holds the card at <paramref name="ct"/> in the present grip, and the hand
        /// size for it: in EditorSim the hand is sized to the (oversized) fan cards, so a card shrinking onto the
        /// disk takes the hand down with it. Life-size AR stays 1:1.
        /// </summary>
        void CarryPoseFor(Transform ct, out Vector3 pos, out Quaternion rot, out float k)
        {
            var root = Volume != null && Volume.VolumeRoot != null ? Volume.VolumeRoot : transform.parent;
            var h = ArFloatingCard.BodyLocalSize.y;
            k = 1f;
            if (!LifeSize)
            {
                // the fan hand is SimHandCardHeights tall; next to a smaller card it tends to real proportions
                // (RealHandCardHeights) instead of shrinking all the way down with it
                var cardH = h * ct.lossyScale.y / Mathf.Max(0.0001f, root.lossyScale.y);
                var fanH = h * CsNow(FanCount());
                var ratio = cardH / Mathf.Max(0.0001f, fanH);
                k = Mathf.Clamp(ratio * RealHandCardHeights / SimHandCardHeights, 0.15f, 1f);
            }

            var m = MetreScale * k;
            var gripW = ct.TransformPoint(new Vector3(0f, -h * 0.5f + h * GripInset, 0f));
            var gripL = root.InverseTransformPoint(gripW);
            var upL = root.InverseTransformDirection(ct.up);
            var faceL = root.InverseTransformDirection(ct.forward);   // art normal (+Z)
            if (upL.sqrMagnitude < 1e-8f || faceL.sqrMagnitude < 1e-8f)
            {
                pos = transform.localPosition;
                rot = transform.localRotation;
                k = _carryScale;
                return;
            }

            // hand frame: +X (thumb side) shows the card face, grip -> tip runs up the card
            var fingerDir = _hasPresent ? (_cPresentTip - _cPresentGrip).normalized : Vector3.up;
            var qc = Quaternion.LookRotation(Vector3.Cross(Vector3.right, fingerDir), fingerDir);
            var qt = Quaternion.LookRotation(Vector3.Cross(faceL.normalized, upL.normalized), upL.normalized);
            rot = qt * Quaternion.Inverse(qc);
            var grip = _hasPresent ? _cPresentGrip : _cPinch;
            pos = gripL - rot * (grip * m);
        }

        void TickCarry()
        {
            var dt = Time.unscaledDeltaTime;
            if (_carryFollowUntil > 0f && Time.unscaledTime > _carryFollowUntil) ReleaseCarry();

            // the held card left the hand for good (a direct commit seated it, an effect took it): ride it a moment,
            // then return — the hand must never stay parked at the disk
            if (_carry != null && _placeCo == null && _carryFollowUntil < 0f &&
                (_carry.IsLockedToZone || (Volume != null && Volume.SlotIndexOf(_carry) < 0)))
                _carryFollowUntil = Time.unscaledTime + 0.3f;

            // a card returning to the fan is nearly home: let go before the fingers re-enter the other cards
            if (_carry != null && _carryFollowUntil > 0f && !_carry.IsDragging && !_carry.IsLockedToZone)
            {
                var h = ArFloatingCard.BodyLocalSize.y * Mathf.Abs(_carry.HandRestScale.y);
                if ((_carry.transform.localPosition - _carry.HandRestLocal).sqrMagnitude < h * h * 0.36f) ReleaseCarry();
            }

            // nothing is placing and the held card is gone (seated, destroyed by an effect): back to the fan
            if (_carryTarget > 0f && _placeCo == null && _carry == null) ReleaseCarry();

            if (_carryTarget > 0f)
            {
                // the held card, or after contact the disk visual that took it over
                Transform held = _carry != null ? _carry.transform : null;
                if (held == null && _followZone != null)
                {
                    var vis = _followZone.GetComponentInChildren<ArDiskCardVisual>();
                    if (vis != null) held = vis.transform;
                }

                if (held == null)
                {
                    ReleaseCarry();   // nothing left to hold: let go where we are
                }
                else
                {
                    // a dragged card straightens up to face the eye as it leaves the fan
                    if (_carry != null && _carry.IsDragging && _placeCo == null)
                    {
                        var cam = ArStageView.FindCamera(transform);
                        if (cam != null)
                        {
                            var face = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
                            var kk = 1f - Mathf.Exp(-9f * dt);
                            _carry.transform.rotation = Quaternion.Slerp(_carry.transform.rotation, face, kk);
                        }
                    }

                    CarryPoseFor(held, out var cp, out var cr, out var ck);
                    if (_carryXfade < 1f)
                    {
                        // taken while the hand was still on / letting go of the last card: cross-fade from where the
                        // hand is instead of jumping to the new grip
                        _carryXfade = Mathf.Min(1f, _carryXfade + GestureDt / PlaceGripSeconds);
                        var x = Mathf.SmoothStep(0f, 1f, _carryXfade);
                        cp = Vector3.Lerp(_carryFromPos, cp, x);
                        cr = Quaternion.Slerp(_carryFromRot, cr, x);
                        ck = Mathf.Lerp(_carryFromScale, ck, x);
                    }

                    _carryPos = cp;
                    _carryRot = cr;
                    _carryScale = ck;
                }
            }

            var rate = _carryTarget > 0f ? PlaceGripSeconds : PlaceReleaseSeconds;
            _carryBlend = Mathf.MoveTowards(_carryBlend, _carryTarget, GestureDt / Mathf.Max(0.01f, rate));
            if (_carryBlend <= 0.001f && _carryTarget <= 0f) _carryScale = 1f;

            // a draw that arrived while the hand held a card waits until the grip has let go
            if (_drawCo == null && _drawQueue.Count > 0 && !CarryHoldsPose && isActiveAndEnabled)
                _drawCo = StartCoroutine(DrawLoop());
            if (_drawCo != null)
            {
                _carryOverride = false;   // the draw owns the override pose
                return;
            }

            if (_carryBlend > 0.001f)
            {
                _overridePose = true;
                _ovPos = _carryPos;
                _ovRot = _carryRot;
                _ovBlend = Mathf.SmoothStep(0f, 1f, _carryBlend);
                _carryOverride = true;
            }
            else if (_carryOverride)
            {
                _carryOverride = false;
                _overridePose = false;
                _ovBlend = 0f;
            }
        }

        // ------------------------------------------------------------------ draw

        /// <summary>
        /// New card from the engine: it waits invisible in the deck well until the hand has pinched it out.
        /// Draws chain one after another (the hand cannot be in two places).
        /// </summary>
        public void QueueDraw(ArFloatingCard card)
        {
            if (card == null) return;
            if (!isActiveAndEnabled)
            {
                // hand hidden (volume off): no choreography, the card just takes its slot
                card.EndHandDraw();
                return;
            }

            card.BeginHandDraw();
            Volume?.SourceDeck?.ReserveDraw();   // the well keeps the card on top until the pinch takes it
            _drawQueue.Enqueue(card);
            _emptySince = -1f;
            // while the hand holds or places a card the draw waits (TickCarry starts it): two gestures fighting over
            // the one hand pose used to teleport it off the card and back
            if (_drawCo == null && isActiveAndEnabled && !CarryHoldsPose)
                _drawCo = StartCoroutine(DrawLoop());
        }

        IEnumerator DrawLoop()
        {
            // one frame first: lets a same-frame sync queue every new card (so the chain knows its length),
            // and keeps StartCoroutine from returning an already-finished routine into _drawCo
            yield return null;
            var first = true;
            while (_drawQueue.Count > 0)
            {
                var card = _drawQueue.Dequeue();
                if (card == null || !card.IsDrawArriving)
                {
                    // gone before its turn: its reserved card still leaves the well
                    if (Volume != null && Volume.SourceDeck != null) Volume.SourceDeck.TakeTopCardForDraw();
                    continue;
                }
                _drawing = card;
                var dur = first && _drawQueue.Count == 0 ? DrawSeconds : first ? 0.75f : DrawSecondsChained;
                first = false;
                yield return DrawOne(card, dur);
                _drawing = null;
            }

            _overridePose = false;
            _ovBlend = 0f;
            _drawPinch = 0f;
            _drawCo = null;
            if (_riffleQueued)
            {
                _riffleQueued = false;
                PlayRiffle();
            }
        }

        static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            var u = 1f - x;
            return 1f - u * u * u;
        }

        IEnumerator DrawOne(ArFloatingCard card, float dur)
        {
            var deck = Volume != null ? Volume.SourceDeck : null;
            var root = Volume != null && Volume.VolumeRoot != null ? Volume.VolumeRoot : transform.parent;
            var m = MetreScale;
            var cs = CsNow(FanCount());
            var cardH = ArFloatingCard.BodyLocalSize.y;
            var dispensed = false;
            var flipped = false;
            var topSign = 0f;
            var t = 0f;
            const float reachEnd = 0.40f, grabEnd = 0.52f;

            while (t < dur)
            {
                if (card == null)
                {
                    if (!dispensed) deck?.TakeTopCardForDraw();
                    yield break;
                }

                t += GestureDt;
                var u = Mathf.Clamp01(t / dur);

                // deck top in volume space (the arm moves; resample every frame)
                Vector3 deckP;
                Quaternion deckR;
                var startS = cs * 0.2f;
                if (deck != null && deck.TryGetDrawOriginWorldPose(out var wp, out var wr, out var wsz))
                {
                    deckP = root.InverseTransformPoint(wp);
                    deckR = Quaternion.Inverse(root.rotation) * wr;
                    // the well card's long side runs along the housing (deck X = wsz.x): size by the card's height
                    var rls = Mathf.Max(0.0001f, root.lossyScale.x);
                    startS = Mathf.Clamp(wsz.x / rls / ArFloatingCard.BodyLocalSize.y, 0.01f, cs);
                }
                else
                {
                    // no disk: pretend the well is low on the left, in front of the chest
                    deckP = new Vector3(-1.3f * cardH * cs, -1.8f * cardH * cs, 0.1f * m);
                    deckR = Quaternion.identity;
                }

                var deckUp = deckR * Vector3.up;

                // reach pose: palm down onto the magazine, fingers pointing from the rest wrist toward the well;
                // the pinch lands on the top card's near edge and peels it back toward the wrist
                RestPose(out var restP, out var restR);
                var fingers = deckP - restP;
                fingers -= deckUp * Vector3.Dot(fingers, deckUp);
                if (fingers.sqrMagnitude < 1e-8f) fingers = Vector3.forward;
                fingers.Normalize();
                var peelDir = -fingers;
                var pinchP = deckP + deckUp * (0.012f * m) + peelDir * (0.3f * cardH * startS);
                var reachR = Quaternion.LookRotation(deckUp, fingers);
                var reachP = pinchP - reachR * (_cPinch * m);
                // The well card lies back-up with its long side along the housing (deck X). Its top end is the one
                // the fingers point at, fixed once so the card cannot flip ends mid-draw.
                var deckX = deckR * Vector3.right;
                if (topSign == 0f) topSign = Vector3.Dot(fingers, deckX) >= 0f ? 1f : -1f;
                var deckPose = Quaternion.LookRotation(-deckUp, deckX * topSign);

                // hand path
                float handBlend, pinch;
                if (u < reachEnd)
                {
                    var e = EaseOut(u / reachEnd);
                    handBlend = e;
                    pinch = e;
                }
                else if (u < grabEnd)
                {
                    handBlend = 1f;
                    pinch = 1f;
                }
                else
                {
                    var e = EaseOut((u - grabEnd) / (1f - grabEnd));
                    handBlend = 1f - e;
                    pinch = 1f - e;
                }

                _overridePose = true;
                _ovPos = reachP;
                _ovRot = reachR;
                _ovBlend = handBlend;
                _drawPinch = pinch;

                // card path (volume-root local; the card is a child of VolumeRoot)
                CurrentHandPose(out var hp, out var hr);
                var pinchNow = hp + hr * (_cPinch * m);
                var idx = Volume != null ? Volume.SlotIndexOf(card) : 0;
                SlotFor(Mathf.Max(0, idx), FanCount(), card, out var slotP, out var slotR, out var slotS);

                Vector3 p;
                Quaternion r;
                float s;
                var sv = Vector3.zero;   // set in the return phase: the fan card is thinner than the well card
                if (u < reachEnd)
                {
                    // still "in the deck": the magazine's own top card stands in until the pinch closes
                    p = deckP;
                    r = deckPose;
                    s = 0.001f;
                }
                else if (u < grabEnd)
                {
                    if (!dispensed)
                    {
                        dispensed = true;
                        deck?.TakeTopCardForDraw();   // the well's top card is gone; this one, same pose/size, replaces it
                        WrldzAudio.PlayCardDraw();
                        BurstSparkles(root.TransformPoint(deckP), 16);
                        StartStreak(root.TransformPoint(deckP));
                    }

                    // peel: the thumb slides the top card back off the stack, then it lifts into the pinch
                    var e = Mathf.SmoothStep(0f, 1f, (u - reachEnd) / (grabEnd - reachEnd));
                    var peel = Mathf.Clamp01(e / 0.55f);
                    var lift = Mathf.Clamp01((e - 0.45f) / 0.55f);
                    var peeled = deckP + peelDir * (peel * 0.32f * cardH * startS) + deckUp * (peel * 0.004f * m);
                    var held = pinchNow + (hr * Vector3.up) * (cardH * startS * 0.45f);
                    p = Vector3.Lerp(peeled, held, lift);
                    r = Quaternion.Slerp(deckPose, hr * Quaternion.Euler(0f, 180f, 0f), lift);
                    s = startS;
                }
                else
                {
                    // held in the pinch while the hand swings back, then slides into its slot
                    var back = (u - grabEnd) / (1f - grabEnd);
                    var e = EaseOut(Mathf.InverseLerp(0.2f, 1f, back));
                    s = Mathf.Lerp(startS, slotS.x, EaseOut(back));
                    sv = Vector3.Lerp(Vector3.one * startS, slotS, EaseOut(back));
                    var held = pinchNow + (hr * Vector3.up) * (cardH * s * 0.45f);
                    // into the fan from above: an arc over the slot, then down into it
                    var over = slotP + slotR * Vector3.up * (0.1f * cardH * slotS.x);
                    var o = 1f - e;
                    p = o * o * held + 2f * o * e * over + e * e * slotP;
                    r = Quaternion.Slerp(hr * Quaternion.Euler(0f, 180f, 0f), slotR, e);
                    if (!flipped && back > 0.35f)
                    {
                        flipped = true;
                        card.RefreshArtFace(showFace: !card.KeepFaceHidden);
                    }
                }

                card.transform.localPosition = p;
                card.transform.localRotation = r;
                card.transform.localScale = u < grabEnd ? Vector3.one * s : sv;
                if (dispensed && _drawStreak != null) _drawStreak.transform.position = card.transform.position;
                yield return null;
            }

            StopStreak();
            if (card != null)
            {
                if (!dispensed) deck?.TakeTopCardForDraw();
                // rest = current slot, then settle
                var idx = Volume != null ? Volume.SlotIndexOf(card) : 0;
                SlotFor(Mathf.Max(0, idx), FanCount(), card, out var sp, out var sr, out var ss);
                card.SetHandRest(sp, sr, ss);
                card.EndHandDraw();
            }
        }

        // ------------------------------------------------------------------ shuffle (riffle)

        bool _riffleQueued;

        /// <summary>
        /// Riffle the held fan: close from the old order, three flick passes, reopen in the (new) engine order.
        /// <paramref name="fromIndex"/>: each card's engine index before the shuffle (the fan closes from those
        /// angles instead of jumping to the new ones first). Queued behind draws or a riffle already running.
        /// </summary>
        public void PlayRiffle(Dictionary<ArFloatingCard, int> fromIndex = null)
        {
            if (!IsLive || !isActiveAndEnabled) return;
            if (Volume == null || Volume.SlotCount < 2) return;
            if (_drawCo != null || _drawQueue.Count > 0 || _riffleCo != null)
            {
                _riffleQueued = true;
                return;
            }

            Selected = null;
            Hover = null;
            _riffleFromIndex.Clear();
            if (fromIndex != null)
                foreach (var kv in fromIndex)
                    if (kv.Key != null) _riffleFromIndex[kv.Key] = kv.Value;
            _riffleReopen = false;
            _riffleCo = StartCoroutine(RiffleCo());
        }

        IEnumerator RiffleCo()
        {
            var cards = new List<ArFloatingCard>();
            if (Volume != null)
                foreach (var c in Volume.Cards)
                    if (c != null && !c.IsLockedToZone && !c.IsDragging && !c.IsDrawArriving)
                        cards.Add(c);
            var n = cards.Count;
            _riffleJitter.Clear();
            _riffleRank.Clear();
            var count = Mathf.Max(1, FanCount());
            // depth ranks start as the fan's own order (rightmost card nearest the eye)
            cards.Sort((x, y) => Volume.SlotIndexOf(y).CompareTo(Volume.SlotIndexOf(x)));
            for (var i = 0; i < n; i++) _riffleRank[cards[i]] = Mathf.Clamp(count - 1 - Volume.SlotIndexOf(cards[i]), 0, count - 1);
            WrldzAudio.PlayCardSlide();

            const float closeEnd = 0.22f, passEnd = 0.80f;
            const int passes = 3;
            var lastPass = -1;
            var interleaved = -1;
            var t = 0f;
            while (t < RiffleSeconds)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
                var u = Mathf.Clamp01(t / RiffleSeconds);
                if (u < closeEnd) _riffleClose = Mathf.SmoothStep(0f, 1f, u / closeEnd);
                else if (u < passEnd) _riffleClose = 1f;
                else
                {
                    _riffleReopen = true;
                    _riffleClose = 1f - Mathf.SmoothStep(0f, 1f, (u - passEnd) / (1f - passEnd));
                }

                _riffleRoll = 0f;
                if (u >= closeEnd && u < passEnd)
                {
                    var pv = (u - closeEnd) / (passEnd - closeEnd) * passes;
                    var pass = Mathf.Min(passes - 1, Mathf.FloorToInt(pv));
                    var v = pv - pass;
                    if (pass != lastPass)
                    {
                        lastPass = pass;
                        WrldzAudio.PlayCardSlide();
                    }

                    // half the stack flicks out left, half right, and they interleave at the apex
                    var env = Mathf.Sin(v * Mathf.PI);
                    for (var i = 0; i < n; i++)
                    {
                        var side = ((i + pass) & 1) == 0 ? -1f : 1f;
                        var depthFrac = n > 1 ? i / (float)(n - 1) : 0f;
                        _riffleJitter[cards[i]] = new Vector3(
                            side * env * 0.30f,
                            env * 0.05f * depthFrac,
                            side * env * 7f);
                    }

                    if (v >= 0.5f && interleaved != pass)
                    {
                        interleaved = pass;
                        // the last flick lands the depth order on the engine order, so the reopen is already the
                        // final fan and the hand-off is continuous (the first two stay perfect riffles for the look)
                        if (pass == passes - 1) AssignFinalRanks(cards, count);
                        else Interleave(cards);
                    }
                    _riffleRoll = Mathf.Sin(v * Mathf.PI * 2f) * 4f;
                }
                else
                {
                    foreach (var c in cards)
                        if (c != null) _riffleJitter[c] = Vector3.zero;
                }

                yield return null;
            }

            _riffleClose = 0f;
            _riffleRoll = 0f;
            _riffleReopen = false;
            _riffleJitter.Clear();
            _riffleRank.Clear();
            _riffleFromIndex.Clear();
            _riffleCo = null;
            WrldzAudio.PlayConfirm();
            if (_riffleQueued)
            {
                _riffleQueued = false;
                PlayRiffle();
            }
        }

        void AssignFinalRanks(List<ArFloatingCard> cards, int count)
        {
            foreach (var c in cards)
                if (c != null)
                    _riffleRank[c] = Mathf.Clamp(count - 1 - Volume.SlotIndexOf(c), 0, count - 1);
        }

        /// <summary>
        /// One pass of a perfect riffle on the depth order: the front half and the back half interleave
        /// (position p of the stack → 2p, or 2(p - half) + 1). Always a permutation, so no two cards share a depth.
        /// </summary>
        void Interleave(List<ArFloatingCard> cards)
        {
            var n = cards.Count;
            if (n < 2) return;
            var byRank = new List<ArFloatingCard>(cards);
            byRank.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
            var half = (n + 1) / 2;
            for (var p = 0; p < n; p++)
            {
                var c = byRank[p];
                if (c == null) continue;
                _riffleRank[c] = p < half ? 2 * p : 2 * (p - half) + 1;
            }

            int Rank(ArFloatingCard c) => c != null && _riffleRank.TryGetValue(c, out var r) ? r : 0;
        }

        // ------------------------------------------------------------------ lifecycle

        void OnDisable()
        {
            // hidden mid-animation (retract / zone mode): settle every waiting card on its slot
            if (_drawCo != null) StopCoroutine(_drawCo);
            _drawCo = null;
            if (_drawing != null) _drawing.EndHandDraw();
            _drawing = null;
            while (_drawQueue.Count > 0)
            {
                var c = _drawQueue.Dequeue();
                if (c != null) c.EndHandDraw();
            }

            if (_riffleCo != null) StopCoroutine(_riffleCo);
            _riffleCo = null;
            _riffleQueued = false;
            _riffleClose = 0f;
            _riffleRoll = 0f;
            _riffleReopen = false;
            _riffleJitter.Clear();
            _riffleRank.Clear();
            _riffleFromIndex.Clear();
            _overridePose = false;
            _ovBlend = 0f;
            _drawPinch = 0f;
            StopStreak();
            if (_placeCo != null) StopCoroutine(_placeCo);
            // a place cut short still reports back, or the drag system waits for it forever (all AR input frozen)
            FinishPlace(false);
            if (_carry != null && _carry.IsDragging && !_carry.IsLockedToZone) _carry.ReturnToHandRest();
            _carry = null;
            _carryTarget = 0f;
            _carryBlend = 0f;
            _carryOverride = false;
            _carryScale = 1f;
            _carryXfade = 1f;
            _followZone = null;
            Hover = null;
            _slotStates.Clear();
            if (Volume != null)
                foreach (var c in Volume.Cards)
                    if (c != null) c.HeldByHand = false;
        }

        // ------------------------------------------------------------------ spirit FX

        void BuildSpiritFx()
        {
            _sparkleMat = FxMaterial("HandSparkle", SpiritFxTextures.Star);
            _sparkleTrailMat = FxMaterial("HandSparkleTrail", SpiritFxTextures.Trail);
            _moteMat = FxMaterial("HandSeamMote", SpiritFxTextures.Glow);
            _streakMat = FxMaterial("HandDrawStreak", SpiritFxTextures.Trail);

            _sparkles = FxSystem("HandSparkles", _sparkleMat, 48);
            var main = _sparkles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            var rol = _sparkles.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            FadeInOut(_sparkles, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f)));
            var trails = _sparkles.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.3f);
            trails.minVertexDistance = 0.003f;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.inheritParticleColor = true;
            trails.sizeAffectsWidth = true;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.3f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var trailFade = new Gradient();
            trailFade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailFade);
            _sparkles.GetComponent<ParticleSystemRenderer>().trailMaterial = _sparkleTrailMat;

            _seamMotes = FxSystem("HandSeamMotes", _moteMat, 40);
            var mm = _seamMotes.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            FadeInOut(_seamMotes, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.1f)));

            var streakGo = new GameObject("DrawStreak");
            streakGo.transform.SetParent(transform, false);
            _drawStreak = streakGo.AddComponent<TrailRenderer>();
            _drawStreak.sharedMaterial = _streakMat;
            // a comet: white-hot head at the card, tapering to nothing, short enough to read as motion
            _drawStreak.time = 0.14f;
            _drawStreak.minVertexDistance = 0.004f;
            _drawStreak.numCapVertices = 4;
            _drawStreak.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.25f, 0.6f), new Keyframe(1f, 0f));
            _drawStreak.textureMode = LineTextureMode.Stretch;
            _drawStreak.alignment = LineAlignment.View;
            _drawStreak.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _drawStreak.receiveShadows = false;
            _drawStreak.emitting = false;
        }

        static Material FxMaterial(string name, Texture tex)
        {
            // additive light (HoloUnlit honours the blend; the textures carry their shape in alpha)
            var mat = new Material(ArFieldMaterials.HoloShader) { name = name };
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = HandFxQueue;
            return mat;
        }

        ParticleSystem FxSystem(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // shed motes trail off a moving hand
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.rateOverTime = 0f;   // emitted from the hand anchors in TickSpiritFx
            var sh = ps.shape;
            sh.enabled = false;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.03f);
            noise.frequency = 1.6f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.8f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortMode = ParticleSystemSortMode.None;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            rend.maxParticleSize = 0.5f;
            rend.sharedMaterial = mat;
            if (Application.isPlaying) ps.Play(true);
            return ps;
        }

        static void FadeInOut(ParticleSystem ps, AnimationCurve size)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);
        }

        /// <summary>World pose of the hand axis (wrist → middle fingertip) for the shader and the emitters.</summary>
        void UpdateHandAxis()
        {
            if (_modelPivot == null) return;
            _wristW = _modelPivot.position;
            var tip = _modelPivot.TransformPoint(_cMiddle);
            var d = tip - _wristW;
            _lenW = Mathf.Max(0.0001f, d.magnitude);
            _axisW = d / _lenW;
        }

        void TickSpiritFx()
        {
            if (_mat == null || _modelPivot == null) return;
            if (Volume != null && Volume.SourceDeck != null) _accent = Volume.SourceDeck.AuraColorNow;
            if (_accent != _accentApplied)
            {
                _accentApplied = _accent;
                if (_mat.HasProperty("_RimColor"))
                {
                    var tint = HandTint(_accent, Volume != null && Volume.IsOpponent);
                    tint.a = Alpha;
                    _mat.SetColor("_Color", tint);
                    _mat.SetColor("_RimColor", RimFor(_accent));
                    if (_mat.HasProperty("_OutlineColor")) _mat.SetColor("_OutlineColor", InkFor(_accent));
                }

                var trail = new Gradient();
                trail.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(RimFor(_accent), 0.35f), new GradientColorKey(_accent, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.55f, 0.3f), new GradientAlphaKey(0f, 1f) });
                if (_drawStreak != null) _drawStreak.colorGradient = trail;
            }

            UpdateHandAxis();
            if (_mat.HasProperty("_WristPos"))
            {
                _mat.SetVector("_WristPos", _wristW);
                _mat.SetVector("_HandAxis", new Vector4(_axisW.x, _axisW.y, _axisW.z, _lenW));
            }

            if (!Application.isPlaying) return;
            var fade = Alpha > 0.0001f ? Mathf.Clamp01(_alphaNow / Alpha) : 0f;
            var dt = Time.unscaledDeltaTime;
            var wm = _lenW / 0.184f;   // world units per real metre
            if (_drawStreak != null) _drawStreak.widthMultiplier = 0.03f * wm;
            if (fade < 0.05f) return;

            _sparkleAcc += SparkleRate * fade * (IsDrawing ? 2.2f : 1f) * dt;
            _moteAcc += SeamMoteRate * fade * dt;
            var n = Mathf.Min((int)_sparkleAcc, 6);
            _sparkleAcc -= n;
            for (var i = 0; i < n; i++) EmitFingerSparkle(wm);
            n = Mathf.Min((int)_moteAcc, 6);
            _moteAcc -= n;
            for (var i = 0; i < n; i++) EmitSeamMote(wm);
        }

        Color SparkleColor(float roll)
        {
            if (roll < 0.45f) return Color.white;
            if (roll < 0.8f) return RimFor(_accent);
            return new Color(1f, 0.9f, 0.55f, 1f);
        }

        void EmitFingerSparkle(float wm)
        {
            // somewhere along a finger: wrist → one of the tips, weighted toward the tips
            Vector3 tip;
            switch (Random.Range(0, 4))
            {
                case 0: tip = _cIndex; break;
                case 1: tip = _cMiddle; break;
                case 2: tip = _cPinky; break;
                default: tip = _cThumb; break;
            }

            var along = Mathf.Lerp(0.45f, 1.02f, Mathf.Sqrt(Random.value));
            // on the skin (a finger is ~1 cm across), not inside the bone line
            var p = _modelPivot.TransformPoint(tip * along) + Random.onUnitSphere * (0.011f * wm);
            var side = Vector3.Cross(_axisW, Vector3.up);
            var ep = new ParticleSystem.EmitParams
            {
                position = p,
                velocity = (Vector3.up * Random.Range(0.04f, 0.1f) + side * Random.Range(-0.04f, 0.04f)
                            + _axisW * Random.Range(0f, 0.03f)) * wm,
                startSize = Random.Range(0.006f, 0.013f) * wm,
                startLifetime = Random.Range(0.6f, 1.1f),
                startColor = SparkleColor(Random.value),
                rotation = Random.Range(0f, 90f),
                applyShapeToPosition = false
            };
            _sparkles.Emit(ep, 1);
        }

        void EmitSeamMote(float wm)
        {
            // light gathering up the forearm into the dissolving wrist: the hand forms out of it
            var radial = Vector3.Cross(_axisW, Random.onUnitSphere);
            if (radial.sqrMagnitude < 1e-6f) radial = Vector3.Cross(_axisW, Vector3.right);
            radial.Normalize();
            var at = _lenW * Random.Range(DissolveAt - 0.12f, DissolveAt + HandDissolve * 0.9f);
            var ep = new ParticleSystem.EmitParams
            {
                position = _wristW + _axisW * at + radial * (Random.Range(0.018f, 0.04f) * wm),
                velocity = (_axisW * Random.Range(0.05f, 0.1f) - radial * 0.015f + Vector3.up * 0.015f) * wm,
                startSize = Random.Range(0.008f, 0.016f) * wm,
                startLifetime = Random.Range(0.5f, 0.9f),
                startColor = Color.Lerp(RimFor(_accent), Color.white, Random.value * 0.5f),
                applyShapeToPosition = false
            };
            _seamMotes.Emit(ep, 1);
        }

        void BurstSparkles(Vector3 at, int count)
        {
            if (_sparkles == null || !Application.isPlaying) return;
            var wm = _lenW > 0f ? _lenW / 0.184f : MetreScale;
            for (var i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                var ep = new ParticleSystem.EmitParams
                {
                    position = at + dir * (0.006f * wm),
                    velocity = (dir * Random.Range(0.12f, 0.3f) + Vector3.up * 0.06f) * wm,
                    startSize = Random.Range(0.008f, 0.016f) * wm,
                    startLifetime = Random.Range(0.35f, 0.7f),
                    startColor = SparkleColor(Random.value),
                    rotation = Random.Range(0f, 90f),
                    applyShapeToPosition = false
                };
                _sparkles.Emit(ep, 1);
            }
        }

        void StartStreak(Vector3 at)
        {
            if (_drawStreak == null) return;
            _drawStreak.transform.position = at;
            _drawStreak.Clear();
            _drawStreak.emitting = true;
        }

        void StopStreak()
        {
            if (_drawStreak != null) _drawStreak.emitting = false;
        }

        void OnDestroy()
        {
            if (_mat != null) ArObjectUtil.Destroy(_mat);
            ArObjectUtil.Destroy(_sparkleMat);
            ArObjectUtil.Destroy(_sparkleTrailMat);
            ArObjectUtil.Destroy(_moteMat);
            ArObjectUtil.Destroy(_streakMat);
        }
    }
}
