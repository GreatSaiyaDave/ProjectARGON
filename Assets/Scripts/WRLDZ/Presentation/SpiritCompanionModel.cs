using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// A 3D spirit companion (Kuriboh family, <see cref="SpiritCompanions"/>): Resources/WRLDZ/Companions/&lt;id&gt;.fbx (1 unit tall,
    /// centred, facing +Z; winged ones carry Wing_L / Wing_R bones) + &lt;id&gt;.png, drawn unlit with the avatar recolor material
    /// (no paint: plain texture, fade through _BaseColor alpha). Owns its idle motion: hover bob, squash and stretch, a slow
    /// sway, wing flaps; <see cref="Bounce"/> for a reaction. Used on the map token and by <see cref="KuribohArBehaviour"/>.
    /// </summary>
    public sealed class SpiritCompanionModel : MonoBehaviour
    {
        public const string Res = "WRLDZ/Companions/";

        public string Id { get; private set; }
        /// <summary>Hover amplitude as a fraction of the height (0 = still).</summary>
        public float BobAmount = 0.05f;
        public float Alpha { get; private set; } = 1f;
        /// <summary>Owner's size multiplier (pop-in, pulses); the squash rides on top of it.</summary>
        public float ScaleMul = 1f;
        bool _alphaApplied;
        Vector3 _flapAxisL, _flapAxisR;

        Transform _model, _wingL, _wingR;
        Quaternion _wingL0, _wingR0;
        readonly List<Material> _mats = new();
        float _t, _bounce, _seed, _height = 1f;
        Vector3 _baseOffset;

        public static bool Exists(string id) => !string.IsNullOrEmpty(id) && Resources.Load<GameObject>(Res + id) != null;

        /// <summary>Spawn under <paramref name="parent"/> at <paramref name="height"/> (parent units), centred on the parent, facing its +Z.</summary>
        public static SpiritCompanionModel Spawn(string id, Transform parent, int layer, float height)
        {
            var prefab = string.IsNullOrEmpty(id) ? null : Resources.Load<GameObject>(Res + id);
            if (prefab == null || parent == null) return null;
            var root = new GameObject("Companion_" + id);
            root.transform.SetParent(parent, false);
            var c = root.AddComponent<SpiritCompanionModel>();
            c.Id = id;
            c._seed = Random.value * 10f;
            var inst = Instantiate(prefab, root.transform, false);
            inst.name = "Model";
            c._model = inst.transform;
            c._height = height;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                if (t.name == "Wing_L") c._wingL = t;
                else if (t.name == "Wing_R") c._wingR = t;
            }
            root.layer = layer;
            // wings hinge about the body's forward axis (+Z of the model), expressed in each wing's parent space
            if (c._wingL != null) { c._wingL0 = c._wingL.localRotation; c._flapAxisL = c._wingL.parent.InverseTransformDirection(c._model.forward).normalized; }
            if (c._wingR != null) { c._wingR0 = c._wingR.localRotation; c._flapAxisR = c._wingR.parent.InverseTransformDirection(c._model.forward).normalized; }
            c.BuildMaterials();
            c.Normalise();
            return c;
        }

        /// <summary>
        /// Size and centre from the rendered bounds, not the file scale: the static and the rigged FBX import at different
        /// unit scales (a bone-less mesh keeps Blender's 0.01, an armature compensates), so measure and fit to the height.
        /// </summary>
        void Normalise()
        {
            _model.localPosition = Vector3.zero;
            _model.localScale = Vector3.one;
            // A winged model (Darkuriboh) carries "BodyMin" / "BodyMax" empties at the corners of the furball's own box
            // (darkuriboh_build.py): size and centre on those, so the body matches the plain Kuriboh and the card's wings
            // stand above it instead of the whole model being shrunk to fit the renderer bounds.
            var lo = FindDeep(_model, "BodyMin");
            var hi = FindDeep(_model, "BodyMax");
            if (lo != null && hi != null)
            {
                var bodyH = Mathf.Abs(transform.InverseTransformPoint(hi.position).y -
                                      transform.InverseTransformPoint(lo.position).y);
                if (bodyH > 1e-6f)
                {
                    _model.localScale = Vector3.one * (_height / bodyH);
                    var mid = (transform.InverseTransformPoint(hi.position) + transform.InverseTransformPoint(lo.position)) * 0.5f;
                    _baseOffset = -mid;
                    _model.localPosition = _baseOffset;
                    return;
                }
            }
            if (!TryLocalBounds(out var b) || b.size.y < 1e-6f) { _model.localScale = Vector3.one * _height; return; }
            _model.localScale = Vector3.one * (_height / b.size.y);
            if (TryLocalBounds(out b)) _baseOffset = -b.center;
            _model.localPosition = _baseOffset;
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

        bool TryLocalBounds(out Bounds lb)
        {
            lb = default;
            var any = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var wb = r.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = transform.InverseTransformPoint(corner);
                    if (!any) { lb = new Bounds(lp, Vector3.zero); any = true; } else lb.Encapsulate(lp);
                }
            }
            return any;
        }

        void BuildMaterials()
        {
            var tex = Resources.Load<Texture2D>(Res + Id);
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var n = r is SkinnedMeshRenderer s && s.sharedMesh != null ? s.sharedMesh.subMeshCount : 1;
                var mats = new Material[Mathf.Max(1, n)];
                for (var i = 0; i < mats.Length; i++)
                {
                    var m = tpl != null ? new Material(tpl) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    m.name = "Companion_" + Id;
                    if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); }
                    mats[i] = m; _mats.Add(m);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
        }

        /// <summary>0 hidden .. 1 solid (spirits in AR sit a little translucent).</summary>
        public void SetAlpha(float a)
        {
            a = Mathf.Clamp01(a);
            if (_alphaApplied && Mathf.Approximately(a, Alpha)) return;
            _alphaApplied = true;
            Alpha = a;
            var translucent = a < 0.999f;
            foreach (var m in _mats)
            {
                if (m == null) continue;
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)(translucent ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(translucent ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", translucent ? 0f : 1f);
                var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                c.a = a;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                m.renderQueue = translucent ? 3010 : 2000;
            }
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = a > 0.004f;
        }

        /// <summary>A happy hop (draws, summons, wins).</summary>
        public void Bounce(float strength = 1f) => _bounce = Mathf.Max(_bounce, Mathf.Clamp01(strength));

        void Update() => Tick(Application.isPlaying ? Time.unscaledDeltaTime : 1f / 30f);

        public void Tick(float dt)
        {
            if (_model == null) return;
            _t += dt;
            _bounce = Mathf.MoveTowards(_bounce, 0f, dt * 1.6f);
            var h = _height;
            var bob = Mathf.Sin((_t + _seed) * 2.1f) * BobAmount;
            var hop = Mathf.Sin((1f - _bounce) * Mathf.PI) * 0.18f * _bounce;
            var squash = 1f + Mathf.Sin((_t + _seed) * 4.2f) * 0.025f - hop * 0.3f;
            _model.localPosition = _baseOffset + new Vector3(0f, (bob + hop) * h, 0f);
            _model.localRotation = Quaternion.Euler(Mathf.Sin((_t + _seed) * 0.9f) * 4f, Mathf.Sin((_t + _seed) * 0.6f) * 9f, 0f);
            var side = ScaleMul / Mathf.Sqrt(squash);
            transform.localScale = new Vector3(side, ScaleMul * squash, side);
            if (_wingL != null && _wingR != null)
            {
                var flap = Mathf.Sin((_t + _seed) * (9f + 6f * _bounce)) * (28f + 14f * _bounce);
                _wingL.localRotation = Quaternion.AngleAxis(flap, _flapAxisL) * _wingL0;
                _wingR.localRotation = Quaternion.AngleAxis(-flap, _flapAxisR) * _wingR0;
            }
        }
    }
}
