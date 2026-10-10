using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The AI duelist's own Battle City Duel Disk (Version 2): the same full-size kit the player's Spirit Dueler is built
    /// from (<see cref="ArDuelDiskRig"/> + <see cref="SpiritDuelerMechanics"/>: hub with deck holder, graveyard tray, LP
    /// counter and latch; both blade halves retracted into the hub in standby and shot out into play position on
    /// activation; field slot at the small blade's tip), worn as a real, solid object instead of a ghost:
    /// <list type="bullet">
    /// <item><b>Strapped to the forearm.</b> Every frame the opponent rig is placed so the kit's arm-mount cradle sits on the
    /// duelist's left forearm bone (axis elbow → hand, plate up out of the back of the forearm, wearer side toward the
    /// body), with the hub over the wrist. Measured in the rig's own root frame: the forearm runs along −X toward the hand,
    /// +Y is the plate normal, +Z the wearer's side; the cradle axis is ~6.6 cm up the forearm from the wrist.</item>
    /// <item><b>Solid skin.</b> The ghost-glass skin, underglow and aura are replaced by opaque cel-shaded parts in the
    /// anime's Battle City colours (white plate, dark hub top, red latch button and counter plate, dark strap).</item>
    /// <item><b>Life size.</b> The rig is scaled to exactly the stage's units per metre; the kit itself is full size.</item>
    /// <item><b>Its own mechanism.</b> Fold / deploy go through the rig (<see cref="ArDuelDiskRig.Retract(float)"/> /
    /// <see cref="ArDuelDiskRig.DeployForDuel"/>), so the AI's blades shoot out exactly like the player's.</item>
    /// </list>
    /// Zones, cards, deck and graveyard piles, LP counter and labels are the rig's own — nothing is pinned or hidden.
    /// </summary>
    public sealed class NpcDuelDisk : MonoBehaviour
    {
        // rig-root frame (measured with SnapVisibleDeployed on the kit): the arm-mount cradle centre, and its axis (+X)
        static readonly Vector3 CradleCentre = new(0.093f, -0.004f, -0.007f);
        /// <summary>Rig-local axes → the canonical forearm frame (+Y toward the hand, +Z plate up, +X away from the body).</summary>
        static readonly Quaternion RigToCanon = Quaternion.LookRotation(Vector3.left, Vector3.forward);

        // Battle City V2 (anime) palette per kit part
        static readonly (string key, Color col)[] Palette =
        {
            ("BIG BLADE", Hex("#E9EBEF")), ("SMALL BLADE", Hex("#E9EBEF")), ("Card retainter", Hex("#C4C7CF")),
            ("BIG BLADE CONNECTOR", Hex("#B7BAC3")), ("SMALL BLADE CONNECTOR", Hex("#B7BAC3")),
            ("FIELD SPELL SLOT", Hex("#C4C7CF")), ("MAIN BODY", Hex("#DADCE2")), ("BOTTOM COVER", Hex("#4A4D56")),
            ("COUNTER TOP", Hex("#2C2F37")), ("counter top key plate", Hex("#C8302A")), ("electronics cover", Hex("#4A4D56")),
            ("DECK FLAP", Hex("#B7BAC3")), ("DECK SPRING HOLDER", Hex("#8A8D96")), ("GY COVER", Hex("#B7BAC3")),
            ("LATCH BUTTON", Hex("#C8302A")), ("LATCH BUTTON STEM", Hex("#8A8D96")), ("LATCH", Hex("#8A8D96")),
            ("arm mount", Hex("#3A3D45")), ("Body", Hex("#8FD9FF")),   // Body37..41: the clear projector parts
        };

        public string Id => "battle_city_v2";
        /// <summary>The worn rig's transform (previews).</summary>
        public Transform DiskTransform => _rig != null ? _rig.transform : transform;
        ArDuelDiskRig _rig;
        DuelEngine _engine;
        float _units = 1f;
        bool? _override;
        Transform _button;
        readonly List<Material> _mats = new();
        Renderer _ghostMesh;
        SpiritDuelerAura _aura;

        /// <summary>Where the duelist presses to deploy: the kit's latch button (else the deck).</summary>
        public Transform ButtonAnchor => _button != null ? _button : _rig != null ? _rig.MainDeckZone : transform;

        /// <summary>
        /// The pre-duel deploy cinematic drives the blades: false folds them (standby), true shoots them out, null leaves the
        /// rig to its own deploy state.
        /// </summary>
        public bool? Override
        {
            get => _override;
            set
            {
                if (_override == value) return;
                _override = value;
                if (_rig == null || !value.HasValue) return;
                if (value.Value) _rig.DeployForDuel(0f);
                else _rig.Retract(0f);
            }
        }

        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

        /// <summary>Wear <paramref name="rig"/> (the opponent's disk) on the duelist whose transform is <paramref name="parent"/>.</summary>
        public static NpcDuelDisk Wear(Transform parent, ArDuelDiskRig rig, int layer, float unitsPerMetre)
        {
            if (rig == null) return null;
            var go = new GameObject("NpcDuelDisk");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var d = go.AddComponent<NpcDuelDisk>();
            d._rig = rig; d._units = unitsPerMetre;
            rig.ExternalPose = true;
            rig.transform.localScale = Vector3.one * unitsPerMetre;   // the kit is full size: life size at the stage's units
            d.Reskin();
            return d;
        }

        public void Bind(DuelEngine engine) => _engine = engine;

        /// <summary>Opaque cel-shaded Battle City parts in place of the ghost glass; the aura and underglow go.</summary>
        void Reskin()
        {
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            foreach (var mr in _rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                var n = mr.gameObject.name;
                if (n == "DiskMesh") { _ghostMesh = mr; mr.enabled = false; continue; }
                if (!n.StartsWith("DiskPart:")) continue;
                var part = n.Substring("DiskPart:".Length);
                var col = Hex("#DADCE2");
                foreach (var (key, c) in Palette) if (part.StartsWith(key)) { col = c; break; }
                var m = tpl != null ? new Material(tpl) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.name = "NpcDisk_" + part;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 1.1f);
                if (m.HasProperty("_ShadowAmt")) m.SetFloat("_ShadowAmt", 0.10f);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _mats.Add(m);
                if (part.StartsWith("LATCH BUTTON") && _button == null) _button = mr.transform;
            }
            _aura = _rig.GetComponentInChildren<SpiritDuelerAura>(true);
            if (_aura != null) { _aura.Emphasis = 0f; _aura.gameObject.SetActive(false); }
            foreach (var mr in _rig.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.gameObject.name.Contains("Underglow") || mr.gameObject.name.Contains("EnergyRing")) mr.enabled = false;
            _rig.PhaseButtons?.SetDeployed(false);
        }

        /// <summary>
        /// Strap the rig to the forearm: cradle axis on the bone (elbow → hand), plate up out of the back of the forearm, hub
        /// over the wrist. Called by <see cref="ArOpponentDuelist"/> after it has solved the arms this frame.
        /// </summary>
        public void Tick(Transform lowerArm, Transform hand, Quaternion handCanon, float dt)
        {
            if (_rig == null || lowerArm == null || hand == null) return;
            var along = hand.position - lowerArm.position;
            var len = along.magnitude;
            if (len < 1e-5f) return;
            var fwd = along / len;                                                   // elbow → hand
            var back = Vector3.ProjectOnPlane(handCanon * Vector3.forward, fwd);     // out of the back of the hand / forearm
            if (back.sqrMagnitude < 1e-6f) back = Vector3.up;
            back.Normalize();
            var canon = Quaternion.LookRotation(back, fwd);                          // +Z plate up, +Y toward the hand
            var rot = canon * RigToCanon;
            // the kit's cradle centre sits 6.6 cm up the forearm from the wrist, on the bone axis
            var s = _rig.transform.lossyScale.x;
            var cradleWorld = hand.position - fwd * (0.066f * s);
            var pos = cradleWorld - rot * (CradleCentre * s);
            _rig.transform.SetPositionAndRotation(pos, rot);

            // a solid disk has no ghost, no aura; the phase buttons are the player's
            if (_ghostMesh != null && _ghostMesh.enabled) _ghostMesh.enabled = false;
            if (_aura != null && _aura.gameObject.activeSelf) _aura.gameObject.SetActive(false);
            if (_rig.PhaseButtons != null && _rig.PhaseButtons.IsShowing) _rig.PhaseButtons.SetDeployed(false);
        }

        void OnDestroy()
        {
            if (_rig != null) _rig.ExternalPose = false;
            foreach (var m in _mats) if (m != null) Destroy(m);
        }
    }
}
