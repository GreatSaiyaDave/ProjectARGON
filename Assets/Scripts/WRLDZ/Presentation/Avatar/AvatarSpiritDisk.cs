using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// The Spirit Dueler's Duel Disk worn on an avatar's left forearm (2026-10-19, owner: "include spirit dueler selection
    /// and color palette switching. There should be 2 duel disk assets on my system for that menu"). Both disks are the
    /// owner's full-size 3D-print models of the Battle City disk (<see cref="AvatarCatalog.DiskModelIds"/>):
    /// <list type="bullet">
    /// <item><b>Battle City V2</b> ("") - the kit the AR duel is built on: a real <see cref="ArDuelDiskRig"/> with its own
    /// mechanism (the blade halves fold into the hub and shoot out, the owner-approved deploy), strapped like the AI
    /// duelists' disks (<see cref="NpcDuelDisk.Strap"/>).</item>
    /// <item><b>Battle City Classic</b> ("classic") - the nine-section assembly kit, built by Tools/avatar/disk_kit_build.py
    /// in the canonical forearm frame (Resources/WRLDZ/Avatar3D/Disks/disk_classic.bytes). Its blade B rides a pivot pin, but
    /// no reference shows how far it folds, so it arrives deployed with the spirit flare (nothing invented).</item>
    /// </list>
    /// Both are solid, cel-shaded in the print's colours; the disk palette (<see cref="AvatarAppearance.diskHex"/>) paints
    /// the white plate parts, the aura palette (<see cref="AvatarAppearance.auraHex"/>) the Spirit Dueler aura that hugs the
    /// disk's silhouette (<see cref="SpiritDuelerAura"/>). Runs after the pose writers and the arm IK.
    /// </summary>
    [DefaultExecutionOrder(890)]
    public sealed class AvatarSpiritDisk : MonoBehaviour
    {
        public const string Classic = "classic";
        const string ClassicRes = "WRLDZ/Avatar3D/Disks/disk_classic";

        // the V2 kit's parts (BattleCityDuelDisk.obj groups): the Battle City anime colours; plate = takes the disk palette
        static readonly (string key, string hex, bool plate)[] V2Paint =
        {
            ("BIG BLADE CONNECTOR", "#B7BAC3", true), ("SMALL BLADE CONNECTOR", "#B7BAC3", true),
            ("BIG BLADE", "#E9EBEF", true), ("SMALL BLADE", "#E9EBEF", true), ("Card retainter", "#C4C7CF", true),
            ("FIELD SPELL SLOT", "#C4C7CF", true), ("MAIN BODY", "#DADCE2", true), ("BOTTOM COVER", "#4A4D56", false),
            ("COUNTER TOP", "#2C2F37", false), ("counter top key plate", "#C8302A", false), ("electronics cover", "#4A4D56", false),
            ("DECK FLAP", "#B7BAC3", true), ("DECK SPRING HOLDER", "#8A8D96", false), ("GY COVER", "#B7BAC3", true),
            ("LATCH BUTTON STEM", "#8A8D96", false), ("LATCH BUTTON", "#C8302A", false), ("LATCH", "#8A8D96", false),
            ("arm mount", "#3A3D45", false), ("Body", "#8FD9FF", false),
        };

        // the classic kit's print colours (file-name colours, sampled from the kit's own cover render, Manual.pdf p.1)
        static readonly Dictionary<string, (string hex, bool plate)> ClassicPaint = new()
        {
            ["kit_White"] = ("#E6E8EE", true), ["kit_Black"] = ("#2C2C2C", false), ["kit_LightBlue"] = ("#4774AF", false),
            ["kit_Red"] = ("#C23A44", false), ["kit_LightRed"] = ("#F4AECA", false), ["kit_LightBrown"] = ("#78553C", false),
            ["kit_Brown"] = ("#4D312D", false), ["kit_Translucent"] = ("#D6D8DE", false),
        };

        Animator _anim;
        Transform _fore, _hand, _mid, _idx, _lit;
        int _layer;
        string _model;
        Transform _holder;
        ArDuelDiskRig _rig;
        SpiritDuelerSkin _skin;
        SpiritDuelerAura _aura;                 // the classic disk's own (the V2 rig's skin owns its aura)
        Transform _bladeB;
        Quaternion _bladeBRest;
        readonly List<(Material m, Color baseCol, bool plate)> _paint = new();
        readonly List<Renderer> _renderers = new();
        readonly List<Mesh> _meshes = new();
        Color _diskCol = SpiritDuelerPalette.DefaultSpirit, _auraCol = SpiritDuelerPalette.DefaultSpirit;
        bool _shown;
        float _presence;                        // classic: 0..1 fade
        float _jolt = -1f;                      // classic: seconds since the lock
        Quaternion _handFrameLocal = Quaternion.identity;

        /// <summary>The disk is on the arm (or arriving).</summary>
        public bool Shown => _shown;
        public string Model => _model;
        /// <summary>The worn disk's root (framing, probes).</summary>
        public Transform Holder => _holder;

        public static AvatarSpiritDisk Ensure(GameObject body, int layer)
        {
            if (body == null) return null;
            if (!body.TryGetComponent<AvatarSpiritDisk>(out var d)) d = body.AddComponent<AvatarSpiritDisk>();
            d._layer = layer;
            return d;
        }

        /// <summary>Wear <paramref name="look"/>'s disk model in its palettes (rebuilds only when the model changes).</summary>
        public void Wear(AvatarAppearance look)
        {
            SpiritDuelerPalette.Resolve(look, out var disk, out var aura);
            Wear(look?.diskModel ?? "", disk, aura);
        }

        public void Wear(string model, Color disk, Color aura)
        {
            model ??= "";
            if (!IndexArm()) return;
            if (model != _model || _holder == null)
            {
                Build(model);
                if (_shown) Arrive();
            }
            _diskCol = disk; _auraCol = aura;
            Repaint();
        }

        /// <summary>Put the disk on (it deploys) or take it off (it folds and fades).</summary>
        public void Show(bool on)
        {
            if (on == _shown) return;
            _shown = on;
            if (on) Arrive();
            else Leave();
        }

        /// <summary>Play the arrival again (a new model, the tab reopened): fold and deploy, or the spirit flare.</summary>
        public void Replay()
        {
            if (!_shown) return;
            if (_rig != null) { _rig.Retract(0f); _rig.DeployForDuel(0.35f); }
            else Arrive();
        }

        bool IndexArm()
        {
            if (_anim != null) return _fore != null && _hand != null;
            if (!TryGetComponent(out _anim) || !_anim.isHuman) return false;
            _fore = _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _hand = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            _mid = _anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _idx = _anim.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _lit = _anim.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            if (_hand != null && _mid != null && _idx != null && _lit != null)
            {
                // the hand's canonical frame in its bone space (+Y along the fingers, +Z out of the back of the left hand),
                // as ArOpponentDuelist.HandFrameLocal; measured once, in whatever pose the body stands in now
                var fingers = _hand.InverseTransformDirection(_mid.position - _hand.position).normalized;
                var thumbSide = _hand.InverseTransformDirection(_idx.position - _lit.position).normalized;
                _handFrameLocal = Quaternion.LookRotation(Vector3.Cross(fingers, thumbSide).normalized, fingers);
            }
            return _fore != null && _hand != null;
        }

        // ------------------------------------------------------------------ build

        void Build(string model)
        {
            Clear();
            _model = model;
            _holder = new GameObject("SpiritDisk_" + (model.Length == 0 ? "v2" : model)).transform;
            _holder.SetParent(transform, false);
            _holder.gameObject.layer = _layer;
            if (model == Classic && BuildClassic()) return;
            if (model == Classic) _model = "";   // the asset is missing: the V2 kit stands in
            BuildV2();
        }

        void BuildV2()
        {
            _rig = ArDuelDiskRig.Create(_holder, false, _layer, _diskCol);
            _rig.ExternalPose = true;
            _rig.transform.localScale = Vector3.one;   // the kit is full size; the holder rides the body's metres
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            foreach (var mr in _rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                var n = mr.gameObject.name;
                if (n == "DiskMesh" || n.Contains("Underglow") || n.Contains("EnergyRing")) { mr.enabled = false; continue; }
                if (!n.StartsWith("DiskPart:")) continue;
                var part = n.Substring("DiskPart:".Length);
                var hex = "#DADCE2"; var plate = true;
                foreach (var (key, h, p) in V2Paint) if (part.StartsWith(key)) { hex = h; plate = p; break; }
                mr.sharedMaterial = Paint(tpl, "SpiritDisk_" + part, hex, plate);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _renderers.Add(mr);
            }
            _skin = _rig.GetComponent<SpiritDuelerSkin>();
            // the player's aura at full strength (the rig was made opponent-side: no phase buttons, no palette hotkeys)
            var aura = _rig.GetComponentInChildren<SpiritDuelerAura>(true);
            if (aura != null) aura.Emphasis = 1f;
            SetLayer(_holder.gameObject);
        }

        bool BuildClassic()
        {
            var asset = Resources.Load<TextAsset>(ClassicRes);
            if (asset == null) { Debug.LogWarning("[AvatarSpiritDisk] " + ClassicRes + " is missing; wearing the V2 kit"); return false; }
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            MeshRenderer body = null, blade = null;
            using (var r = new BinaryReader(new MemoryStream(asset.bytes)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WKIT") return false;
                r.ReadUInt32();
                var objects = (int)r.ReadUInt32();
                for (var o = 0; o < objects; o++)
                {
                    var name = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
                    var pivot = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    var n = (int)r.ReadUInt32();
                    var pos = new Vector3[n]; var nrm = new Vector3[n];
                    for (var i = 0; i < n; i++)
                    {
                        pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    }
                    var subs = (int)r.ReadUInt32();
                    var mesh = new Mesh { name = name, indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    mesh.vertices = pos; mesh.normals = nrm;
                    mesh.subMeshCount = subs;
                    var mats = new Material[subs];
                    for (var sIdx = 0; sIdx < subs; sIdx++)
                    {
                        var mat = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
                        var count = (int)r.ReadUInt32();
                        var idx = new int[count];
                        for (var i = 0; i < count; i++) idx[i] = (int)r.ReadUInt32();
                        mesh.SetTriangles(idx, sIdx, false);
                        var (hex, plate) = ClassicPaint.TryGetValue(mat, out var p) ? p : ("#E6E8EE", true);
                        mats[sIdx] = Paint(tpl, "SpiritDisk_" + mat, hex, plate);
                    }
                    mesh.RecalculateBounds();
                    _meshes.Add(mesh);
                    var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(_holder, false);
                    go.transform.localPosition = pivot;
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    _renderers.Add(mr);
                    if (name == "KitBladeB") { blade = mr; _bladeB = go.transform; _bladeBRest = go.transform.localRotation; }
                    else body = mr;
                }
            }
            if (body == null) return false;
            // the Spirit Dueler aura round the disk's silhouette (both pieces), centred on the hub (blade B's pin)
            var auraRoot = new GameObject("SpiritDiskAura").transform;
            auraRoot.SetParent(_holder, false);
            _aura = SpiritDuelerAura.Create(auraRoot, _layer, _auraCol, body);
            var parts = new List<MeshRenderer> { body };
            if (blade != null) parts.Add(blade);
            _aura.SetBodyParts(parts, _bladeB != null ? _bladeB.localPosition : Vector3.zero);
            SetLayer(_holder.gameObject);
            SetPresence(0f);
            return true;
        }

        Material Paint(Material tpl, string name, string hex, bool plate)
        {
            var m = tpl != null ? new Material(tpl) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.name = name;
            var col = ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 1.1f);
            if (m.HasProperty("_ShadowAmt")) m.SetFloat("_ShadowAmt", 0.10f);
            _paint.Add((m, col, plate));
            return m;
        }

        /// <summary>The plate parts take the disk palette (keeping each part's shade), everything else its print colour.</summary>
        void Repaint()
        {
            var lum = _diskCol.r * 0.2126f + _diskCol.g * 0.7152f + _diskCol.b * 0.0722f;
            foreach (var (m, baseCol, plate) in _paint)
            {
                if (m == null || !m.HasProperty("_BaseColor")) continue;
                var a = m.GetColor("_BaseColor").a;
                var c = baseCol;
                if (plate)
                {
                    // the part's own shade (white plate, grey connectors) carried onto the palette colour; a near-white
                    // palette keeps the anime white
                    var shade = (baseCol.r + baseCol.g + baseCol.b) / 3f / 0.91f;
                    c = Color.Lerp(baseCol, _diskCol * shade * Mathf.Lerp(1.15f, 1f, lum), 0.85f);
                }
                c.a = a;
                m.SetColor("_BaseColor", c);
            }
            _skin?.SetColors(_diskCol, _auraCol, 0.25f);
            _aura?.SetAccent(_auraCol);
        }

        void SetLayer(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _layer;
        }

        // ------------------------------------------------------------------ arrive / leave

        void Arrive()
        {
            if (_holder == null) return;
            _holder.gameObject.SetActive(true);
            if (_rig != null)
            {
                _rig.Retract(0f);
                _rig.DeployForDuel(0.25f);
                return;
            }
            // the classic kit: it fades in on the arm with the spirit flare, the lock jolt as it settles
            _jolt = 0f;
            _aura?.PulseDeploy();
        }

        /// <summary>A burst of the aura (the aura palette changed): the flare without the arrival.</summary>
        public void Flare()
        {
            if (!_shown || _holder == null) return;
            var aura = _aura != null ? _aura : _holder.GetComponentInChildren<SpiritDuelerAura>(true);
            aura?.PulseDeploy();
        }

        void Leave()
        {
            if (_rig != null) _rig.RetractThenFadeOut(0f);
        }

        void SetPresence(float p)
        {
            _presence = Mathf.Clamp01(p);
            var on = _presence > 0.01f;
            foreach (var r in _renderers) if (r != null) r.enabled = on;
            var translucent = _presence < 0.999f;
            foreach (var (m, _, _) in _paint)
            {
                if (m == null) continue;
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", translucent ? 1f : 0f);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", translucent ? 0f : 1f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)(translucent ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(translucent ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
                if (translucent) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = translucent ? 3000 : -1;
                if (m.HasProperty("_BaseColor")) { var c = m.GetColor("_BaseColor"); c.a = _presence; m.SetColor("_BaseColor", c); }
            }
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (_holder == null || _fore == null || _hand == null) return;
            var handCanon = _hand.rotation * _handFrameLocal;
            if (_rig != null)
            {
                NpcDuelDisk.Strap(_rig.transform, _fore, _hand, handCanon);
                if (!_shown && _skin != null && _skin.Presence <= 0.02f && _holder.gameObject.activeSelf) _holder.gameObject.SetActive(false);
                return;
            }
            if (!NpcDuelDisk.ForearmFrame(_fore, _hand, handCanon, out var canon, out _)) return;
            // the kit's frame: origin at the wrist on the forearm's axis (the hand bone's origin), +Y toward the hand
            _holder.SetPositionAndRotation(_hand.position, canon);
            var dt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
            var target = _shown ? 1f : 0f;
            if (!Mathf.Approximately(_presence, target)) SetPresence(Mathf.MoveTowards(_presence, target, dt / 0.35f));
            if (_bladeB != null)
            {
                // the lock jolt: blade B knocks against its stop on the pin and settles (a 4° kick, damped in 0.4 s)
                var k = 0f;
                if (_jolt >= 0f)
                {
                    _jolt += dt;
                    var u = _jolt / 0.4f;
                    if (u >= 1f) _jolt = -1f; else k = Mathf.Sin(u * Mathf.PI * 2f) * (1f - u) * 4f;
                }
                _bladeB.localRotation = _bladeBRest * Quaternion.AngleAxis(k, Vector3.forward);
            }
            if (_aura != null)
            {
                var cam = ArStageView.FindCamera(transform);
                _aura.Tick(_presence, 1f, _jolt >= 0f ? 1f - _jolt / 0.4f : 0f, cam);
            }
        }

        void Clear()
        {
            if (_holder != null) Destroy(_holder.gameObject);
            foreach (var (m, _, _) in _paint) if (m != null) Destroy(m);
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _paint.Clear(); _renderers.Clear(); _meshes.Clear();
            _holder = null; _rig = null; _skin = null; _aura = null; _bladeB = null; _presence = 0f;
        }

        void OnDestroy() => Clear();
    }
}
