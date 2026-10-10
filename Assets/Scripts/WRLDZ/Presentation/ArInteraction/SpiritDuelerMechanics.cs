using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The Spirit Dueler as its mechanical counterpart, the Battle City Duel Disk (V2): the disk is assembled from
    /// the print-kit parts (the <c>g</c> groups of BattleCityDuelDisk.obj, Cults3D "Working Full Size Battle City
    /// Duel Disk", Fusion v37) and the activation is the anime's: the Battle City "Duel Disk, activate!" cut
    /// (~/Pictures/yugioh-yugi-ready-for-duel-*.gif, 12 frames, owner 2026-10-04). Seen the way that cut is drawn —
    /// looking down on the hub's top face with the hand up (mesh −X up, −Z to the image's left) — the frames show:
    /// (1, frame 1) standby — both sections lie alongside the round body on opposite sides, parallel to the forearm
    /// and pointing toward the elbow, each pulled in along its connector so its hub end is tucked into the body: the
    /// three-stage section (BIG BLADE, M3–M5 / ST3–ST5 in this mesh's numbering) on the Graveyard side of the hub
    /// (−Z) — exactly where the plate will lock, so it ends where it started — the two-stage section (SMALL BLADE,
    /// M1–M2 / ST1–ST2 + the field slot) mirrored on the counter-top side (+Z); (2, frame 2) the latch releases and
    /// both sections slide straight out of the body along their connectors, still parallel to the forearm;
    /// (3, frames 2–5) the two halves slide toward each other: each connector sweeps along the hub ring toward the
    /// deck with its section trailing tangentially behind it like a car on a curved rail (the hinge never opens),
    /// so the halves splay from parallel to a straight plate as their roots meet end to end beside the deck holder,
    /// across the hand side of the hub; (4, frames 5–8) the central piece carries the joined plate on round the ring,
    /// back down the Graveyard side, into the kit's authored locked pose along the forearm; (5) lock jolt, the zones
    /// light M1 → M5. The hub never moves. Every pose is a function of the mechanism's progress, so a restart, a
    /// scene reload or an interrupted cinematic resumes or resets without a jump; the opponent's worn disk runs the
    /// same mechanism in its own (mirrored) rig frame.
    /// <list type="bullet">
    /// <item><b>Hub</b> (main body, covers, LP counter top, deck holder, arm mount) stays strapped to the arm.</item>
    /// <item><b>Latch</b>: the hub button clicks in and the latch swings to release the halves; it re-locks when
    /// they are locked. The same button presses when a Set card is activated.</item>
    /// <item><b>Sections</b> (one per connector): lying alongside the hub on opposite sides, they slide out, their
    /// connectors sweep along the ring toward each other (a yaw about the central axis) with the sections trailing
    /// tangentially, they meet end to end beside the deck, then the central piece carries the joined plate round to
    /// the locked pose. A streak of light rides each travelling tip; the disk locks with a jolt and the zones light
    /// up.</item>
    /// <item><b>Deck flap</b> lifts on every draw, the <b>GY cover</b> rides the graveyard tray and pops when a card
    /// lands, the <b>field spell slot</b> rides the tip drawer, and a zone lights when a card seats on it.</item>
    /// </list>
    /// Everything is driven by the disk (<see cref="DiskFxDriver.MechOpen"/> and the Note* hooks on
    /// <see cref="ArDuelDiskRig"/>), never by who performed the action, so the simulated hand, touch, the AI
    /// opponent and tracked hands all get the same mechanics. Timings and angles are named constants.
    /// </summary>
    public sealed class SpiritDuelerMechanics : MonoBehaviour
    {
        /// <summary>Editor inspection only: 0..1 freezes every disk at that point of its deploy. Negative = live.</summary>
        public static float DebugPose = -1f;

        // ── Kit geometry, mesh-local (BladePivot) metres: cm → m, Y up, X mirrored (ArPlaymatLayout) ─────────
        /// <summary>Central piece axis (plate normal) through the hub, between the two connector bosses.</summary>
        public static readonly Vector3 CarrierPivot = new(-0.005f, 0f, 0.215f);
        /// <summary>Hub end of the BIG BLADE (seam side); the blade extends along +X from here.</summary>
        public static readonly Vector3 BigBladeRoot = new(-0.076f, 0f, 0.04f);
        /// <summary>Hub end of the SMALL BLADE (seam with the big blade).</summary>
        public static readonly Vector3 SmallBladeRoot = new(-0.061f, 0f, 0.038f);
        /// <summary>Yaw that points local +X down the small blade toward the field-spell tip (~20° kink).</summary>
        public const float SmallBladeYaw = -159.6f;
        public const float BigBladeLength = 0.39f;
        public const float SmallBladeLength = 0.29f;
        /// <summary>Latch pin (plate normal axis).</summary>
        public static readonly Vector3 LatchPivot = new(-0.013f, 0f, 0.1925f);
        /// <summary>Deck flap hinge line (along X, on the wearer-side edge of the flap).</summary>
        public static readonly Vector3 DeckFlapHinge = new(-0.078f, 0.0205f, 0.232f);
        /// <summary>Hub radius: in standby only this much of the outline sheds aura streaks.</summary>
        public const float HubRadius = 0.085f;

        // ── Motion ───────────────────────────────────────────────────────────────────────────────────────────
        // Radial angles are positions on the hub circle in the kit's XZ plane (atan2(z, x), degrees): 0° = +X = the
        // elbow side of the forearm, 90° = +Z = the wearer's side, 180° = the hand side, 270° = −Z = straight out
        // from the arm. Directions use the same angles. A Unity Y rotation of +a turns XZ angle θ into θ − a.
        /// <summary>Radial angle of the big section's root in the authored (play) pose: the seam of the plate.</summary>
        public static float PlaySeamDeg => RadialDeg(BigBladeRoot);
        static float RadialDeg(Vector3 p) =>
            Mathf.Atan2(p.z - CarrierPivot.z, p.x - CarrierPivot.x) * Mathf.Rad2Deg;
        /// <summary>The small section's play direction as an XZ angle.</summary>
        static float SmallPlayDirDeg => -SmallBladeYaw;
        /// <summary>Where the sections meet: the seam beside the deck holder (the deck well's own radial).</summary>
        public static float MeetSeamDeg => RadialDeg(ArDeckWellCards.WellOrigin);
        /// <summary>
        /// Standby radials of the two roots on the hub ring (the GIF's frames 1–2, read with the hand up): the big
        /// section waits on the Graveyard side at the very radial its seam locks to (<see cref="PlaySeamDeg"/>,
        /// ≈248° — it ends where it started, now joined), the small section mirrored across the forearm on the
        /// counter-top side (≈112°). Each then slides 76° / 64° along the ring to the deck. (The earlier 135° / 270°
        /// put the three-stage tray on the wrong side of the hub; a radial "clock hand" sweep was not in the frames.)
        /// </summary>
        public static float BigStandbyDeg => PlaySeamDeg;
        public static float SmallStandbyDeg => -PlaySeamDeg;
        /// <summary>
        /// Standby directions: both sections lie parallel to the forearm, pointing toward the elbow (+X), alongside
        /// the body. For the big section that is exactly its locked attitude relative to its connector (its hinge
        /// stays shut through the whole activation); the small section's hinge eases its 19° kink shut as it slides.
        /// </summary>
        public const float BigStandbyDirDeg = 0f, SmallStandbyDirDeg = 0f;
        /// <summary>
        /// Standby root radii (metres from the central axis; the authored roots are at 0.189 / 0.186 m): how far each
        /// section is pulled in along its connector. The big section's inner rail then sits 2.5 cm inside the body's
        /// Graveyard-side rim (under the GY cover — "retracted into the round portion"); the small section's inner
        /// rail touches the counter-top rim, where the kit's five rim studs (Body37–41) stop it going further.
        /// </summary>
        public const float BigStandbyRootM = 0.150f, SmallStandbyRootM = 0.167f;
        /// <summary>
        /// From the end of the extension until the lock the roots ride this far beyond the authored radius (the GIF:
        /// in frame 7 the joined plate still stands a clear gap off the hub with the connector bridging it, and only
        /// in frame 8 does it slide in to the rim — the owner's "then slides into its activated state"). 3.5 cm keeps
        /// the kit's connector ends touching the trays' inner rails.
        /// </summary>
        public const float SwingOutsetM = 0.035f;
        /// <summary>The joined plate slides the outset back in over this last part of the swing.</summary>
        public const float SlideIn0 = 0.55f;
        /// <summary>
        /// How each half turns from its standby direction (parallel to the forearm) to its joined direction while
        /// its connector sweeps to the deck, as a ramp over the eased sweep. Frames 3–5: the three-stage half leans
        /// out progressively; the two-stage half hangs near-vertical beside the wrist until late (frame 4) and
        /// swings the last ~70° between frames 4 and 5. A ramp that starts at 0 is exactly "trailing tangent".
        /// </summary>
        public const float BigTurn0 = 0.10f, SmallTurn0 = 0.45f;
        /// <summary>Central piece yaw (Y) that puts the seam at <see cref="MeetSeamDeg"/>; 0 = the play position.</summary>
        public static float CarrierStandbyYaw => Mathf.DeltaAngle(0f, PlaySeamDeg - MeetSeamDeg);
        /// <summary>Standby sweep (Y rotation about the hub) of the big section's connector, relative to the central piece (the sweep is played back to 0 as it arrives beside the deck).</summary>
        public static float BigStandbySweep => Mathf.DeltaAngle(0f, PlaySeamDeg - CarrierStandbyYaw - BigStandbyDeg);
        /// <summary>Standby hinge (Y rotation at the root) of the big section: 0 — parallel to the forearm is its locked attitude on its connector.</summary>
        public static float BigStandbyHinge =>
            Mathf.DeltaAngle(0f, -CarrierStandbyYaw - BigStandbySweep - BigStandbyDirDeg);
        public static float SmallStandbySweep =>
            Mathf.DeltaAngle(0f, RadialDeg(SmallBladeRoot) - CarrierStandbyYaw - SmallStandbyDeg);
        public static float SmallStandbyHinge =>
            Mathf.DeltaAngle(0f, SmallPlayDirDeg - CarrierStandbyYaw - SmallStandbySweep - SmallStandbyDirDeg);

        public const float LatchPressM = 0.006f;
        public const float LatchSwingDeg = 22f;
        public const float DeckFlapLiftDeg = 38f;
        public const float GyPopM = 0.008f;
        public const float LockJoltDeg = 1.6f;
        public const float ZoneSweepStep = 0.055f;
        public const float ZoneFlashSeconds = 0.38f;

        // Deploy timeline, as fractions of MechOpen (DiskFxDriver.DeploySeconds; retract plays it backwards), in the
        // GIF's proportions (extend 1 : sweep 3 : swing 3) with its pauses: latch → both sections slide out of the
        // body → pause → they sweep along the ring, fold and meet beside the deck (a machined clack) → pause → the
        // joined plate rounds the ring to the locked pose → lock.
        const float LatchIn1 = 0.05f, LatchOut1 = 0.10f;
        const float Unlock0 = 0.02f, Unlock1 = 0.08f, Relock0 = 0.92f, Relock1 = 0.98f;
        const float Out0 = 0.06f, Out1 = 0.22f;
        const float Meet0 = 0.27f, Meet1 = 0.60f;
        const float Disk0 = 0.66f, Disk1 = 0.93f;
        const float Lock = 0.95f;

        enum Slot { Hub, Carrier, BigBlade, SmallBlade, FieldSlot, Latch, Button, GyCover, DeckFlap }

        ArDuelDiskRig _rig;
        Transform _root, _hub, _carrier, _bigArm, _smallArm, _big, _small, _field, _latch, _button, _gy, _flap;
        Quaternion _smallRestRot;
        Vector3 _fieldRest, _buttonRest, _gyRest;
        readonly List<MeshRenderer> _parts = new();
        readonly List<MeshRenderer> _bigParts = new();
        readonly List<MeshRenderer> _smallParts = new();
        MeshRenderer _bigTip, _smallTip;
        Material _tipMat, _flashMat;
        MaterialPropertyBlock _mpb;
        readonly List<(ArDiskZone zone, MeshRenderer quad)> _flashes = new();
        readonly Dictionary<ArDiskZone, float> _flashStart = new();
        float _lastMech = -1f;
        float _flapT = -1f, _gyT = -1f, _buttonT = -1f, _joltT = -1f;
        Color _accent = new(0.25f, 0.9f, 1f, 1f);

        /// <summary>Every visible kit part (the skin material, presence and the aura silhouette use these).</summary>
        public IReadOnlyList<MeshRenderer> PartRenderers => _parts;
        /// <summary>0 = the big section waiting on the Graveyard side of the hub (standby) .. 1 = joined at the seam.</summary>
        public float BigExtension { get; private set; }
        /// <summary>0 = the small section waiting on the counter-top side (standby) .. 1 = joined at the seam.</summary>
        public float SmallExtension { get; private set; }
        /// <summary>0 = both sections tucked into the round body .. 1 = slid out to the full radius of their connectors.</summary>
        public float Extension { get; private set; }
        /// <summary>0 = the joined plate still beside the deck .. 1 = swung round to the authored pose.</summary>
        public float Slide { get; private set; }
        /// <summary>Central piece yaw: <see cref="CarrierStandbyYaw"/> until the sections have met, 0 in the play pose.</summary>
        public float CarrierYaw { get; private set; }
        /// <summary>Where the mechanism is right now, 0 standby .. 1 locked open (honours <see cref="DebugPose"/>).</summary>
        public float Pose { get; private set; } = 1f;

        /// <summary>
        /// Build the part rig under <paramref name="bladePivot"/>. Null when the groups are missing (older OBJ):
        /// the rig keeps its single mesh. <paramref name="rig"/> may be null (tests).
        /// </summary>
        public static SpiritDuelerMechanics Build(ArDuelDiskRig rig, Transform bladePivot,
            IReadOnlyDictionary<string, Mesh> groups, Material material, int layer)
        {
            if (bladePivot == null || groups == null || !groups.ContainsKey("BIG BLADE") ||
                !groups.ContainsKey("SMALL BLADE") || !groups.ContainsKey("MAIN BODY"))
                return null;
            var go = new GameObject("DiskParts");
            go.layer = layer;
            go.transform.SetParent(bladePivot, false);
            go.transform.localScale = Vector3.one * ArPlaymatLayout.DiskMeshVisualScale;
            var m = go.AddComponent<SpiritDuelerMechanics>();
            m._rig = rig;
            if (rig != null) m._accent = rig.AuraColor;
            m.BuildParts(groups, material, layer);
            m.ApplyPose(0f);
            return m;
        }

        static Slot SlotFor(string group) => group switch
        {
            "BIG BLADE" or "Card retainter" => Slot.BigBlade,
            "BIG BLADE CONNECTOR" or "SMALL BLADE CONNECTOR" => Slot.Carrier,
            "SMALL BLADE" => Slot.SmallBlade,
            "FIELD SPELL SLOT" => Slot.FieldSlot,
            "LATCH" => Slot.Latch,
            "LATCH BUTTON" or "LATCH BUTTON STEM" => Slot.Button,
            "GY COVER" => Slot.GyCover,
            "DECK FLAP" => Slot.DeckFlap,
            _ => Slot.Hub
        };

        void BuildParts(IReadOnlyDictionary<string, Mesh> groups, Material material, int layer)
        {
            _mpb = new MaterialPropertyBlock();
            _root = transform;
            _hub = Node("Hub", _root, Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.identity, layer);
            _carrier = Node("CentralPiece", _root, CarrierPivot, Quaternion.identity, Vector3.zero,
                Quaternion.identity, layer);
            // each section hangs off its own connector: the connector sweeps round the central axis, the section
            // hinges at its root on the connector's end and slides along its own radial to tuck into the hub
            _bigArm = Node("BigConnector", _carrier, CarrierPivot, Quaternion.identity, CarrierPivot, Quaternion.identity, layer);
            _smallArm = Node("SmallConnector", _carrier, CarrierPivot, Quaternion.identity, CarrierPivot, Quaternion.identity, layer);
            _big = Node("BigBlade", _bigArm, BigBladeRoot, Quaternion.identity, CarrierPivot, Quaternion.identity,
                layer);
            var smallRot = Quaternion.Euler(0f, SmallBladeYaw, 0f);
            _smallRestRot = smallRot;
            _small = Node("SmallBlade", _smallArm, SmallBladeRoot, smallRot, CarrierPivot, Quaternion.identity, layer);
            // the field spell slot hangs off the small blade (it extends with it) but its node sits at the mesh
            // origin, unrotated, so the drawer offset and its geometry stay in plain mesh space
            _field = Node("FieldSpellSlot", _small, Vector3.zero, Quaternion.identity, SmallBladeRoot, smallRot, layer);
            _fieldRest = _field.localPosition;
            _latch = Node("Latch", _root, LatchPivot, Quaternion.identity, Vector3.zero, Quaternion.identity, layer);
            _button = Node("LatchButton", _root, Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.identity,
                layer);
            _buttonRest = _button.localPosition;
            _gy = Node("GyCover", _root, Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.identity, layer);
            _gyRest = _gy.localPosition;
            _flap = Node("DeckFlap", _root, DeckFlapHinge, Quaternion.identity, Vector3.zero, Quaternion.identity,
                layer);

            foreach (var kv in groups)
            {
                var slot = SlotFor(kv.Key);
                Transform parent;
                Vector3 pivot;
                Quaternion rot;
                switch (slot)
                {
                    case Slot.Carrier:
                        parent = kv.Key.StartsWith("SMALL") ? _smallArm : _bigArm;
                        pivot = CarrierPivot; rot = Quaternion.identity; break;
                    case Slot.BigBlade: parent = _big; pivot = BigBladeRoot; rot = Quaternion.identity; break;
                    case Slot.SmallBlade: parent = _small; pivot = SmallBladeRoot; rot = smallRot; break;
                    case Slot.FieldSlot: parent = _field; pivot = Vector3.zero; rot = Quaternion.identity; break;
                    case Slot.Latch: parent = _latch; pivot = LatchPivot; rot = Quaternion.identity; break;
                    case Slot.Button: parent = _button; pivot = Vector3.zero; rot = Quaternion.identity; break;
                    case Slot.GyCover: parent = _gy; pivot = Vector3.zero; rot = Quaternion.identity; break;
                    case Slot.DeckFlap: parent = _flap; pivot = DeckFlapHinge; rot = Quaternion.identity; break;
                    default: parent = _hub; pivot = Vector3.zero; rot = Quaternion.identity; break;
                }

                var part = new GameObject("DiskPart:" + kv.Key, typeof(MeshFilter), typeof(MeshRenderer));
                part.layer = layer;
                part.transform.SetParent(parent, false);
                var inv = Quaternion.Inverse(rot);
                part.transform.localRotation = inv;
                part.transform.localPosition = inv * -pivot;

                part.GetComponent<MeshFilter>().sharedMesh = kv.Value;
                var mr = part.GetComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                ArAnimePresentation.ConfigureHoloRenderer(mr);
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                _parts.Add(mr);
                if (slot == Slot.BigBlade) _bigParts.Add(mr);
                if (slot == Slot.SmallBlade || slot == Slot.FieldSlot) _smallParts.Add(mr);
            }

            _tipMat = FxMaterial("DiskBladeTipFlash", SpiritFxTextures.Glow);
            _flashMat = FxMaterial("DiskZoneLightUp", SpiritFxTextures.SoftRect);
            _bigTip = FxQuad("BigBladeTipFlash", _root, _tipMat, layer);
            _smallTip = FxQuad("SmallBladeTipFlash", _root, _tipMat, layer);
        }

        /// <summary>
        /// A node whose mesh-space pose is (<paramref name="pivot"/>, <paramref name="rot"/>), under a parent whose
        /// mesh-space pose is (<paramref name="parentPivot"/>, <paramref name="parentRot"/>).
        /// </summary>
        static Transform Node(string name, Transform parent, Vector3 pivot, Quaternion rot, Vector3 parentPivot,
            Quaternion parentRot, int layer)
        {
            var t = new GameObject(name).transform;
            t.gameObject.layer = layer;
            t.SetParent(parent, false);
            var inv = Quaternion.Inverse(parentRot);
            t.localPosition = inv * (pivot - parentPivot);
            t.localRotation = inv * rot;
            t.localScale = Vector3.one;
            return t;
        }

        static Material FxMaterial(string name, Texture tex)
        {
            var mat = new Material(ArFieldMaterials.HoloShader) { name = name };
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)RenderQueue.Transparent + 12;
            return mat;
        }

        static MeshRenderer FxQuad(string name, Transform parent, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<Collider>();
            if (col != null) ArObjectUtil.Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            mr.enabled = false;
            return mr;
        }

        // ------------------------------------------------------------------ hooks (any actor)

        public void SetAccent(Color accent)
        {
            if (accent.a < 0.01f) accent.a = 1f;
            _accent = accent;
        }

        /// <summary>A card is being drawn from the magazine: the deck flap lifts and drops back.</summary>
        public void PulseDeckFlap() => _flapT = 0f;

        /// <summary>A card landed in the graveyard: the cover pops.</summary>
        public void PulseGraveyard() => _gyT = 0f;

        /// <summary>The hub button: a Set card is activated.</summary>
        public void PressButton() => _buttonT = 0f;

        /// <summary>The slot under <paramref name="zone"/> lights (a card seated or activated there).</summary>
        public void FlashZone(ArDiskZone zone, float delay = 0f)
        {
            if (zone == null) return;
            EnsureFlashes();
            _flashStart[zone] = Time.unscaledTime + delay;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Called by <see cref="ArDuelDiskRig"/> after the FX driver ticks.</summary>
        public void Tick(float dt)
        {
            var mech = DebugPose >= 0f ? Mathf.Clamp01(DebugPose) : _rig != null ? _rig.Fx.MechOpen : 1f;
            var rising = _lastMech >= 0f && mech > _lastMech;
            var falling = _lastMech >= 0f && mech < _lastMech;
            if (_rig != null && _rig.IsPlayerSide && Application.isPlaying && _lastMech >= 0f)
            {
                if (rising && Crossed(_lastMech, mech, Unlock0)) WrldzAudio.PlayCardTap();
                if (rising && Crossed(_lastMech, mech, Out0)) WrldzAudio.PlayCardSlide();
                if (rising && Crossed(_lastMech, mech, Meet0)) WrldzAudio.PlayCardSlide();  // the halves start to slide together
                if (rising && Crossed(_lastMech, mech, Meet1)) WrldzAudio.PlayCardTap();   // the halves meet
                if (rising && Crossed(_lastMech, mech, Disk0)) WrldzAudio.PlayCardSlide();
                if (falling && Crossed(mech, _lastMech, Disk1)) WrldzAudio.PlayCardSlide(); // retract: the plate swings back
                if (falling && Crossed(mech, _lastMech, Meet1)) WrldzAudio.PlayCardSlide(); // retract: the halves part
                if (falling && Crossed(mech, _lastMech, Out1)) WrldzAudio.PlayCardSlide();
                if (falling && Crossed(mech, _lastMech, Unlock0)) WrldzAudio.PlayCardTap();
            }

            if (rising && Crossed(_lastMech, mech, Lock) && Application.isPlaying) OnLock();
            _lastMech = mech;
            Pose = mech;

            if (dt > 0f)
            {
                if (_flapT >= 0f) _flapT += dt;
                if (_gyT >= 0f) _gyT += dt;
                if (_buttonT >= 0f) _buttonT += dt;
                if (_joltT >= 0f) _joltT += dt;
            }

            ApplyPose(mech);
            TickTips(mech);
            TickFlashes();
        }

        static bool Crossed(float from, float to, float mark) => from < mark && to >= mark;

        void OnLock()
        {
            _joltT = 0f;
            if (_rig != null && _rig.IsPlayerSide) WrldzAudio.PlayCardSet();
            EnsureFlashes();
            var now = Time.unscaledTime;
            var i = 0;
            // M1 → M5, then S/T 1 → 5, then the field slot: a light sweep down the plate
            foreach (var (zone, _) in _flashes)
            {
                if (zone == null) continue;
                _flashStart[zone] = now + i * ZoneSweepStep;
                i++;
            }
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>Ease out with a small overshoot (the anime snap, but machined): 0 → ~1.03 → 1.</summary>
        static float Snap(float x)
        {
            x = Mathf.Clamp01(x);
            const float c = 0.9f;
            var u = x - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }

        /// <summary>
        /// A section's root on its own radial: at its standby radius, sliding out along its connector to the authored
        /// radius plus <see cref="SwingOutsetM"/> as the section extends; it rides there through the sweep and the
        /// swing, and the joined plate slides the outset back in at the lock.
        /// </summary>
        static Vector3 RootAt(Vector3 playFromHub, float standbyRoot, float extend, float slideIn)
        {
            var r = playFromHub.magnitude;
            if (r < 1e-6f) return playFromHub;
            var outset = SwingOutsetM * (1f - slideIn);
            return playFromHub / r * Mathf.Lerp(standbyRoot, r + outset, extend);
        }

        /// <summary>
        /// Hinge (Y rotation at the root, relative to the connector) that turns a section from its standby direction
        /// to its joined direction by <paramref name="turn"/> (0..1) while its connector has swept by
        /// <paramref name="sweep"/> (0..1). turn == sweep is the section trailing tangent to its circle; a late ramp
        /// holds it parallel to the forearm longer. At (1, 1) the hinge is 0: the authored pose.
        /// </summary>
        static float HingeAt(float standbySweep, float standbyHinge, float sweep, float turn) =>
            standbySweep * sweep + standbyHinge - (standbySweep + standbyHinge) * turn;

        /// <summary>The whole mechanism at deploy progress <paramref name="mech"/> (0 standby, 1 play).</summary>
        public void ApplyPose(float mech)
        {
            // latch: the button clicks in, the latch swings open, then re-locks behind the halves
            var press = mech < LatchIn1 ? Smooth(mech / LatchIn1)
                : mech < LatchOut1 ? 1f - Smooth((mech - LatchIn1) / (LatchOut1 - LatchIn1)) : 0f;
            press = Mathf.Max(press, Envelope(_buttonT, 0.07f, 0.03f, 0.14f));
            if (_button != null) _button.localPosition = _buttonRest + new Vector3(-LatchPressM * press, 0f, 0f);
            var unlock = Smooth(Mathf.InverseLerp(Unlock0, Unlock1, mech)) - Smooth(Mathf.InverseLerp(Relock0, Relock1, mech));
            if (_latch != null) _latch.localRotation = Quaternion.Euler(0f, LatchSwingDeg * unlock, 0f);

            // (1) standby: both sections lie alongside the hub on opposite sides, parallel to the forearm and pointing
            // toward the elbow, pulled in along their connectors so their hub ends sit in the body — the big one on
            // the Graveyard side, the small one on the counter-top side. (2) they slide out along their connectors to
            // the full radius and a little beyond. (3) the connectors sweep along the ring toward each other, each
            // section turning from parallel to its joined direction on its own ramp (the big one leaning out as it
            // goes, the small one hanging by the wrist until late), and they meet end to end beside the deck holder:
            // a shallow inverted V, as the kit's seam makes it. (4) the central piece carries the joined plate round
            // the ring, the plate slides in to the rim, and it settles with a small overshoot at the lock. The hub
            // never moves.
            var extend = Smooth(Mathf.InverseLerp(Out0, Out1, mech));
            var sweep = Smooth(Mathf.InverseLerp(Meet0, Meet1, mech));
            var swingRaw = Mathf.InverseLerp(Disk0, Disk1, mech);
            var swing = Snap(swingRaw);
            var slideIn = Smooth(Mathf.InverseLerp(SlideIn0, 1f, swingRaw));
            var bigTurn = Smooth(Mathf.InverseLerp(BigTurn0, 1f, sweep));
            var smallTurn = Smooth(Mathf.InverseLerp(SmallTurn0, 1f, sweep));
            Extension = extend;
            BigExtension = sweep;
            SmallExtension = sweep;
            Slide = swing;
            CarrierYaw = CarrierStandbyYaw * (1f - swing);
            if (_bigArm != null) _bigArm.localRotation = Quaternion.Euler(0f, BigStandbySweep * (1f - sweep), 0f);
            if (_smallArm != null) _smallArm.localRotation = Quaternion.Euler(0f, SmallStandbySweep * (1f - sweep), 0f);
            if (_big != null)
            {
                _big.localRotation = Quaternion.Euler(0f, HingeAt(BigStandbySweep, BigStandbyHinge, sweep, bigTurn), 0f);
                _big.localPosition = RootAt(BigBladeRoot - CarrierPivot, BigStandbyRootM, extend, slideIn);
            }
            if (_small != null)
            {
                _small.localRotation =
                    Quaternion.Euler(0f, HingeAt(SmallStandbySweep, SmallStandbyHinge, sweep, smallTurn), 0f) * _smallRestRot;
                _small.localPosition = RootAt(SmallBladeRoot - CarrierPivot, SmallStandbyRootM, extend, slideIn);
            }
            if (_carrier != null)
            {
                _carrier.localRotation = Quaternion.Euler(0f, CarrierYaw, 0f);
                _carrier.localPosition = CarrierPivot;
            }

            // the field spell slot rides the tip drawer; the GY cover rides the tray (locked over the deck in standby).
            // The kit's authored slot is the OPEN drawer (the card seats at DiskFieldSpellSurface); closed slides it
            // the same 6.2 cm back inside the small blade. Measuring from the closed point pushed the part 6.2 cm
            // past the card in both states, so it always stuck out of the tip.
            if (_field != null && _rig != null && _rig.FieldSpellZone != null)
            {
                var off = _rig.FieldSpellZone.transform.localPosition - ArPlaymatLayout.DiskFieldSpellSurface;
                _field.localPosition = _fieldRest + Quaternion.Inverse(Quaternion.Euler(0f, SmallBladeYaw, 0f)) * off;
            }

            if (_gy != null)
            {
                var tray = _rig != null && _rig.GraveyardZone != null
                    ? _rig.GraveyardZone.localPosition - _rig.GraveyardOpenLocal
                    : Vector3.zero;
                var pop = Envelope(_gyT, 0.09f, 0.04f, 0.28f);
                _gy.localPosition = _gyRest + tray + new Vector3(0f, GyPopM * pop, 0f);
            }

            if (_flap != null)
                _flap.localRotation = Quaternion.Euler(DeckFlapLiftDeg * Envelope(_flapT, 0.12f, 0.16f, 0.2f), 0f, 0f);

            // lock jolt: a short mechanical shudder through the whole disk
            if (_root != null)
            {
                var j = 0f;
                if (_joltT >= 0f && _joltT < 0.28f)
                    j = LockJoltDeg * Mathf.Sin(_joltT * 70f) * (1f - _joltT / 0.28f);
                else if (_joltT >= 0.28f) _joltT = -1f;
                _root.localRotation = Quaternion.Euler(j, 0f, j * 0.4f);
            }
        }

        /// <summary>0 → 1 over <paramref name="up"/>, hold, → 0 over <paramref name="down"/>; 0 when idle (t &lt; 0).</summary>
        static float Envelope(float t, float up, float hold, float down)
        {
            if (t < 0f) return 0f;
            if (t < up) return Smooth(t / up);
            if (t < up + hold) return 1f;
            if (t < up + hold + down) return 1f - Smooth((t - up - hold) / down);
            return 0f;
        }

        void TickTips(float mech)
        {
            // The travelling tip lights are off: no tiny orbs while the halves travel. The zone-pad sweep at
            // the lock (rectangles on the card slots) stays.
            if (_bigTip != null) _bigTip.enabled = false;
            if (_smallTip != null) _smallTip.enabled = false;
            _ = mech;
        }

        /// <summary>The larger of two travel envelopes (0 at rest, 1 mid-move), as a 0..1 phase for the flash.</summary>
        static float Travel(float a, float b)
        {
            float Env(float u) => u > 0f && u < 1f ? Mathf.Sin(u * Mathf.PI) : 0f;
            var e = Mathf.Max(Env(a), Env(b));
            return Mathf.Asin(Mathf.Clamp01(e)) / Mathf.PI;   // back to a phase whose sin is the envelope
        }

        void TipFlash(MeshRenderer quad, Transform blade, float length, float ext, float u, Camera cam)
        {
            if (quad == null || blade == null) return;
            // bright while the tip is travelling, and a last burst as the disk locks
            var travel = u > 0f && u < 1f ? Mathf.Sin(u * Mathf.PI) : 0f;
            var lockBurst = _joltT >= 0f ? 1f - Mathf.Clamp01(_joltT / 0.25f) : 0f;
            var a = Mathf.Max(travel, lockBurst * 0.8f);
            if (a < 0.02f || cam == null)
            {
                quad.enabled = false;
                return;
            }

            quad.enabled = true;
            var tip = blade.TransformPoint(new Vector3(length, 0.004f, 0f));
            var t = quad.transform;
            t.position = tip;
            t.rotation = Quaternion.LookRotation(tip - cam.transform.position, cam.transform.up);
            var s = 0.09f * (0.6f + 0.6f * a) * Mathf.Max(0.0001f, blade.lossyScale.y) /
                    Mathf.Max(0.0001f, _root.lossyScale.x);
            t.localScale = Vector3.one * s;
            quad.GetPropertyBlock(_mpb);
            var c = Color.Lerp(_accent, Color.white, 0.55f);
            c.a = a;
            _mpb.SetColor("_Color", c);
            quad.SetPropertyBlock(_mpb);
            _ = ext;
        }

        void EnsureFlashes()
        {
            if (_flashes.Count > 0 || _rig == null) return;
            var ordered = new List<ArDiskZone>();
            ordered.AddRange(_rig.MonsterZones);
            ordered.AddRange(_rig.SpellTrapZones);
            ordered.Add(_rig.FieldSpellZone);
            foreach (var z in ordered)
            {
                if (z == null) continue;
                var q = FxQuad("ZoneLightUp", z.transform, _flashMat, z.gameObject.layer);
                _flashes.Add((z, q));
            }
        }

        void TickFlashes()
        {
            if (_flashes.Count == 0) return;
            var now = Time.unscaledTime;
            foreach (var (zone, quad) in _flashes)
            {
                if (zone == null || quad == null) continue;
                if (!_flashStart.TryGetValue(zone, out var start))
                {
                    if (quad.enabled) quad.enabled = false;
                    continue;
                }

                var t = now - start;
                if (t < 0f)
                {
                    quad.enabled = false;
                    continue;
                }

                if (t > ZoneFlashSeconds)
                {
                    quad.enabled = false;
                    _flashStart.Remove(zone);
                    continue;
                }

                var u = t / ZoneFlashSeconds;
                var a = u < 0.2f ? u / 0.2f : 1f - Smooth((u - 0.2f) / 0.8f);
                quad.enabled = true;
                PlaceFlash(zone, quad.transform, 1f + 0.06f * u);
                quad.GetPropertyBlock(_mpb);
                var c = Color.Lerp(_accent, Color.white, 0.3f);
                c.a = a * 0.95f;
                _mpb.SetColor("_Color", c);
                quad.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>The card footprint on the zone: flat on the pad, or the slit strip for an S/T slot.</summary>
        static void PlaceFlash(ArDiskZone zone, Transform t, float grow)
        {
            var body = ArFloatingCard.BodyLocalSize;
            var s = zone.LockLocalScale.x;
            if (zone.Kind == ArDuelZoneKind.SpellTrap)
            {
                t.localRotation = Quaternion.Euler(90f, 0f, 0f);
                t.localPosition = new Vector3(0f, 0.0025f, 0f);
                t.localScale = new Vector3(body.x * s * grow, body.y * s * 0.34f * grow, 1f);
                return;
            }

            t.localRotation = zone.LockLocalRotation;
            t.localPosition = zone.LockLocalPosition + new Vector3(0f, 0.0018f, 0f);
            t.localScale = new Vector3(body.x * s * 1.06f * grow, body.y * s * 1.06f * grow, 1f);
        }

        void OnDestroy()
        {
            ArObjectUtil.Destroy(_tipMat);
            ArObjectUtil.Destroy(_flashMat);
        }
    }
}
