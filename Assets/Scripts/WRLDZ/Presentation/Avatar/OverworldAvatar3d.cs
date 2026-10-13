using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// The player's modular 3D body on the overworld token (OVERWORLD_3D.md phase 1, plus the
    /// walk the GDD implies: "avatar follows walking and cycling").
    ///
    /// Base meshes come from Resources/WRLDZ/Avatar3D/argon_base_{male,female}.fbx. Since "Meshy modular
    /// base v1" (2026-09-23) they are built by ~/argon-avatar/tools/build_meshy_avatar.sh: the owner's Meshy
    /// body + hair / coat / bottoms / boots, fitted to the MPFB mixamo rig, every part textured, NO
    /// blendshapes. <see cref="AvatarAppearance.morphs"/> still drives blendshapes by name when a base has
    /// them (the older MPFB build had 118; a left/right pair is ONE slider) and is silently ignored otherwise.
    ///
    /// Motion is a two-clip Playable blend (Idle_Loop / Walk_Loop from the CC0 Universal
    /// Animation Library, retargeted through Humanoid) weighted by <see cref="Speed"/>, which the
    /// map sets from its pan velocity. No AnimatorController asset is needed.
    /// </summary>
    public sealed class OverworldAvatar3d : MonoBehaviour
    {
        const string BaseRes = "WRLDZ/Avatar3D/argon_base_";
        const string ClipsRes = "WRLDZ/Avatar3D/ArgonClips_UAL";
        const float WalkSpeed = 1.2f;   // m/s at which the walk clip is fully weighted

        /// <summary>Metres per second the token is moving across the map; 0 = idle.</summary>
        public float Speed;
        public float Blend = 6f;        // 1/s response of the idle<->walk crossfade

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        float _walkWeight;
        SkinnedMeshRenderer _smr;
        readonly Dictionary<string, int> _shape = new();

        // ---- world mode (3D map) ----
        /// <summary>Degrees per second the body turns toward <see cref="FaceToward"/>'s direction.</summary>
        public float TurnDegPerSec = 540f;
        bool _hasFaceTarget;
        Quaternion _faceTarget = Quaternion.identity;
        Transform _shadowBlob;

        /// <summary>
        /// Spawn under a UI slot, the same contract as <see cref="OverworldAvatarToken.Attach"/>.
        /// Returns null when the base is not present so the caller can keep the old token.
        /// </summary>
        public static OverworldAvatar3d Attach(RectTransform slot, AvatarAppearance look, float uiScale = 90f)
        {
            if (slot == null) return null;
            var existing = slot.Find("Avatar3d");
            if (existing != null) Object.Destroy(existing.gameObject);

            var female = look != null && look.IsFemale;
            var prefab = Resources.Load<GameObject>(BaseRes + (female ? "female" : "male"));
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, slot, false);
            go.name = "Avatar3d";
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.15f, 0.02f); rt.anchorMax = new Vector2(0.85f, 0.98f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            // feet at the slot's bottom edge, facing the camera, at the same on-screen size the
            // OBJ token used; the UI scale factor turns 1.7 m into token pixels
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * uiScale;
            SetLayerRecursive(go, slot.gameObject.layer);

            var av = go.AddComponent<OverworldAvatar3d>();
            av._baseScale = uiScale;
            av.BuildMaterials(female ? "female" : "male");
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name.StartsWith("ArgonBody")) av._smr = r;
            if (av._smr == null) av._smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            av.IndexShapes();
            av.Apply(look);
            av.BuildGraph();
            return av;
        }

        /// <summary>
        /// Spawn as a WORLD-SPACE body on the 3D map (D1/D4: 1 unit = 1 m, the mesh is ~1.7 m tall
        /// so the scale stays 1). Same prefab, materials, sliders and Playable graph as
        /// <see cref="Attach"/>; the differences are: no RectTransform, the given render layer, and a
        /// flat ground shadow blob under the feet (style_spec §5: no real-time shadows, a dark quad
        /// reads as contact). Returns null when the base FBX is missing so the caller can fall back.
        /// </summary>
        public static OverworldAvatar3d AttachWorld(Transform parent, AvatarAppearance look, int layer)
        {
            if (parent == null) return null;
            if (look != null && !string.IsNullOrEmpty(look.characterId))
            {
                var full = AttachCharacterWorld(parent, look, layer);
                if (full != null) return full;
                Debug.LogWarning($"[WRLDZ] character model '{look.characterId}' missing under Resources/{CharacterRes}; using the base body");
            }
            var female = look != null && look.IsFemale;
            var prefab = Resources.Load<GameObject>(BaseRes + (female ? "female" : "male"));
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, parent, false);
            go.name = "Avatar3dWorld";
            go.transform.localPosition = Vector3.zero;
            // the FBX faces +Z (the HUD path yaws it 180 to face the canvas camera); on the map the
            // body faces along its walking direction, set by FaceToward, north (+Z) until then
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            SetLayerRecursive(go, layer);

            var av = go.AddComponent<OverworldAvatar3d>();
            av.BuildMaterials(female ? "female" : "male");
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name.StartsWith("ArgonBody")) av._smr = r;
            if (av._smr == null) av._smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            av.IndexShapes();
            av.Apply(look);
            av.BuildGraph();
            av.BuildShadowBlob(layer);
            return av;
        }

        public const string CharacterRes = "WRLDZ/Avatar3D/Characters/";
        bool _fullCharacter;
        /// <summary>True when this body is a complete sourced character model (no grafts / paints / decals apply).</summary>
        public bool IsFullCharacter => _fullCharacter;

        /// <summary>
        /// A complete character model (a Humanoid FBX under Resources/WRLDZ/Avatar3D/Characters/&lt;id&gt;) as the body:
        /// its own hair, clothes and face, every renderer swapped to the anime cel material keeping the model's textures,
        /// scaled so it stands the look's height. Animation goes through the Humanoid avatar like the base body.
        /// </summary>
        static OverworldAvatar3d AttachCharacterWorld(Transform parent, AvatarAppearance look, int layer)
        {
            var prefab = Resources.Load<GameObject>(CharacterRes + look.characterId);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = "Avatar3dWorld_" + look.characterId;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            SetLayerRecursive(go, layer);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            if (!anim.isHuman)
            {
                Debug.LogWarning($"[WRLDZ] character model '{look.characterId}' has no Humanoid avatar: the duel clips cannot retarget onto it");
            }

            var av = go.AddComponent<OverworldAvatar3d>();
            av._fullCharacter = true;
            av._sex = look.IsFemale ? "female" : "male";
            av._smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            // life size from the model's own standing height (rest pose bounds), then the look's height
            var bounds = new Bounds(go.transform.position, Vector3.zero); var any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    var tex = src != null ? (src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture) : null;
                    var m = av.Own(tpl != null ? new Material(tpl) : new Material(UnlitShader()));
                    m.name = "Char_" + look.characterId + "_" + (src != null ? src.name : i.ToString());
                    if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex; }
                    if (src != null && src.HasProperty("_Color") && tex == null && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_Color"));
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            var modelHeight = any ? Mathf.Max(0.2f, bounds.size.y) : 1.75f;
            av._characterHeightM = modelHeight;
            av.IndexShapes();
            av.Apply(look);
            av.BuildGraph();
            av.BuildShadowBlob(layer);
            Debug.Log($"[WRLDZ] character model '{look.characterId}' · {modelHeight:0.00} m tall as authored · humanoid {anim.isHuman}");
            return av;
        }
        float _characterHeightM = 1.75f;

        /// <summary>
        /// Turn the body to face a world-space direction (the walking vector). Smoothed in Update at
        /// <see cref="TurnDegPerSec"/> so a jittery GPS heading never snaps the figure around.
        /// A zero / vertical direction is ignored and the body keeps its last heading.
        /// </summary>
        public void FaceToward(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 1e-6f) return;
            _faceTarget = Quaternion.LookRotation(worldDir.normalized, Vector3.up);
            _hasFaceTarget = true;
        }

        /// <summary>
        /// Garment tint hook for NPC outfit palettes (style_spec §9: 4000 LP civilian grey, 8000 LP
        /// lapis coat + gold trim). Tints the Shirt / Bottoms parts' materials; the player's own look
        /// leaves garments untouched until the wardrobe pass, so this is opt-in.
        /// </summary>
        public void TintGarments(Color shirt, Color bottoms)
        {
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.name == "ArgonPart_Shirt") TintAll(r, shirt);
                else if (r.name == "ArgonPart_Bottoms") TintAll(r, bottoms);
            }
        }

        /// <summary>Flat dark disc under the feet: 0.9 m, 45% alpha, unlit, no depth write so it never
        /// z-fights the ground tile it sits 1 cm above.</summary>
        void BuildShadowBlob(int layer)
        {
            var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blob.name = "ShadowBlob";
            var col = blob.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            blob.transform.SetParent(transform, false);
            blob.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            blob.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            blob.layer = layer;
            var mr = blob.GetComponent<MeshRenderer>();
            var m = Own(new Material(UnlitShader()) { name = "AvatarShadowBlob" });
            var c = new Color(0.02f, 0.02f, 0.06f, 0.45f);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.renderQueue = 2990;   // after the opaque ground, before the transparent pins / holograms
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = !_renderersOff;   // built after OnEnable: respect a duel / Zone Mode hide
            _shadowBlob = blob.transform;
        }

        void IndexShapes()
        {
            _shape.Clear();
            if (_smr == null || _smr.sharedMesh == null) return;
            var m = _smr.sharedMesh;
            for (var i = 0; i < m.blendShapeCount; i++) _shape[m.GetBlendShapeName(i)] = i;
        }

        /// <summary>Push a look onto the body: sliders and skin tint. Safe to call every time the customizer changes.</summary>
        public void Apply(AvatarAppearance look)
        {
            if (_smr == null) return;
            _lastLook = look;
            if (look != null && _blinkTex != null && SkinHexFor(look) != _blinkSkinHex)
            {
                // the skin paint changed: the eyelid card is rebuilt in the look's new tone on the next blink
                if (_blinkQuad != null) Kill(_blinkQuad);
                _blinkQuad = null; _blinkRenderer = null;
            }
            // The Meshy base (v1) carries no blendshapes: _shape is empty and the slider loop is a no-op.
            foreach (var kv in _shape) _smr.SetBlendShapeWeight(kv.Value, 0f);
            if (look != null)
            {
                if (_shape.Count > 0 && look.morphs != null)
                foreach (var mw in look.morphs)
                {
                    if (mw == null || string.IsNullOrEmpty(mw.name)) continue;
                    var w = Mathf.Clamp01(mw.weight) * 100f;
                    // a slider named without a side prefix drives both l- and r- shapes
                    if (_shape.TryGetValue(mw.name, out var idx)) _smr.SetBlendShapeWeight(idx, w);
                    if (_shape.TryGetValue("l-" + mw.name, out var li)) _smr.SetBlendShapeWeight(li, w);
                    if (_shape.TryGetValue("r-" + mw.name, out var ri)) _smr.SetBlendShapeWeight(ri, w);
                }
                // Tints by part: skin on the body, hair colour on the hair part; garments keep their
                // own colour until the wardrobe pass. Material instances so tints never leak between duelists.
                // A TEXTURED body / hair (the Meshy base paints face, skin and hair colour into its albedo) is
                // never tinted: _BaseColor multiplies _BaseMap, so a skin tint would dye the painted eyes and
                // clothes. Tints apply only to untextured (flat) parts.
                // A region-painted body (the paint manifest's Body mask) takes its skin from the Body.0 paint, and
                // its undersuit region from the worn shirt's Suit; a flat _BaseColor tint would wash the whole part
                // - the undersuit included - in skin, which reads as a naked body under the clothes.
                var bodyMasked = _paintKey.TryGetValue(_smr, out var bodyKey) && bodyKey == "Body";
                if (!_fullCharacter && !_textured.Contains(_smr) && !bodyMasked && ColorUtility.TryParseHtmlString(look.skinHex, out var skin)) Tint(_smr, skin);
                if (ColorUtility.TryParseHtmlString(look.hairHex, out var hair))
                    foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (r.name == "ArgonPart_Hair" && !_textured.Contains(r)) Tint(r, hair);
            }
            ApplyCustomization(look);
        }

        // ---------------------------------------------------------------- customization v1
        // Height = uniform scale of the whole body (the skeleton, clothes and hair scale as one; the map token's
        // feet stay on the slot's bottom edge). Weight = the Weight_Thin / Weight_Heavy morphs that
        // tools/custom/custom_body.py bakes on the body AND every garment (clothes follow, nothing clips more than at
        // rest). Hairstyles = separate skinned FBX grafted onto this skeleton by bone name. Colour = WRLDZ/AvatarHair
        // zone colours and WRLDZ/AvatarRecolor region paints.

        public const string HairRes = "WRLDZ/Avatar3D/Hair/";
        float _baseScale = 1f;
        string _sex = "male";
        string _hairId;
        SkinnedMeshRenderer _hairSmr;

        /// <summary>The style a body wears when the look names none: its own Meshy hair, rebuilt as a recolourable style.</summary>
        public static string DefaultHairId(bool female) => female ? "crimson_flame" : "storm_crown";

        void ApplyCustomization(AvatarAppearance look)
        {
            if (look == null) return;
            if (_fullCharacter)
            {
                // a sourced model: stand it at the look's height (metres), nothing else is grafted or painted
                transform.localScale = Vector3.one * (_baseScale * (look.EffectiveHeightCm / 100f) / _characterHeightM);
                ApplySilhouette(look);
                if (_appliedAlpha < 0.999f) ApplyAlpha(_appliedAlpha);
                if (_renderersOff) foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                return;
            }
            transform.localScale = Vector3.one * (_baseScale * Mathf.Clamp(look.HeightScale, 0.75f, 1.25f));
            // the hair first: a freshly grafted style must take the weight morph below too
            ApplyHairStyle(string.IsNullOrEmpty(look.hairStyleId) ? DefaultHairId(_sex == "female") : look.hairStyleId);
            ApplyOutfit(look.outfitId ?? "");
            ApplyClothes(look);
            SyncHairUnderHat(look);

            // weight: 0.5 = as built; below blends in Weight_Thin, above Weight_Heavy (0..100 each)
            var w = Mathf.Clamp01(look.weight);
            var thin = w < 0.5f ? (0.5f - w) * 200f : 0f;
            var heavy = w > 0.5f ? (w - 0.5f) * 200f : 0f;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r.sharedMesh;
                if (m == null || m.blendShapeCount == 0) continue;
                var ti = m.GetBlendShapeIndex("Weight_Thin");
                var hi = m.GetBlendShapeIndex("Weight_Heavy");
                if (ti >= 0) r.SetBlendShapeWeight(ti, thin);
                if (hi >= 0) r.SetBlendShapeWeight(hi, heavy);
            }

            ApplyHairColour(look);
            ApplyPaints(look);
            ApplyFace(look.silhouette ? "" : look.faceId ?? "");
            ApplySilhouette(look);
            if (_appliedAlpha < 0.999f) ApplyAlpha(_appliedAlpha);
            if (_renderersOff) foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        // ---- silhouette: every renderer in one flat colour with the ink outline (a canon duelist before its real model
        // exists, or a mystery challenger). The hairstyle and outfit meshes keep shaping the outline.
        bool _silhouette;
        Material _silhouetteMat;

        void ApplySilhouette(AvatarAppearance look)
        {
            var on = look != null && look.silhouette;
            if (!on && !_silhouette) return;
            _silhouette = on;
            if (!on) return;   // a silhouette never turns back into a textured body in place (looks are rebuilt)
            if (_silhouetteMat == null)
            {
                var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
                _silhouetteMat = Own(tpl != null ? new Material(tpl) : new Material(UnlitShader()));
                _silhouetteMat.name = "Silhouette";
                if (_silhouetteMat.HasProperty("_BaseMap")) _silhouetteMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (_silhouetteMat.HasProperty("_MaskMap")) _silhouetteMat.SetTexture("_MaskMap", Texture2D.blackTexture);
                if (_silhouetteMat.HasProperty("_HideOn")) _silhouetteMat.SetFloat("_HideOn", 0f);
                if (_silhouetteMat.HasProperty("_ShadowAmt")) _silhouetteMat.SetFloat("_ShadowAmt", 0.04f);
                if (_silhouetteMat.HasProperty("_RimAmt")) _silhouetteMat.SetFloat("_RimAmt", 1f);
                if (_silhouetteMat.HasProperty("_OutlineWidth")) _silhouetteMat.SetFloat("_OutlineWidth", 1.8f);
                if (_silhouetteMat.HasProperty("_OutlineDark")) _silhouetteMat.SetFloat("_OutlineDark", 0.55f);
            }
            var col = ColorUtility.TryParseHtmlString(look.silhouetteHex, out var c) ? c : new Color(0.07f, 0.09f, 0.16f);
            if (_silhouetteMat.HasProperty("_BaseColor")) _silhouetteMat.SetColor("_BaseColor", col); else _silhouetteMat.color = col;
            // the anime mystery-duelist edge light: the body's own hue, bright, all round the figure
            Color.RGBToHSV(col, out var hh, out var ss, out _);
            var edge = Color.HSVToRGB(hh, Mathf.Clamp01(ss * 0.9f + 0.25f), 0.95f);
            if (_silhouetteMat.HasProperty("_RimColor")) _silhouetteMat.SetColor("_RimColor", new Color(edge.r, edge.g, edge.b, 0.85f));
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name.StartsWith("FaceDecal") || r.gameObject.name == "ShadowBlob") continue;
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++) mats[i] = _silhouetteMat;
                r.sharedMaterials = mats;
            }
            if (_faceDecal != null) _faceDecal.SetActive(false);
        }

        // ---- face decal: a canon duelist's eyes + brows (HD anime, drawn from the character art) on a quad in front of the
        // base face, parented to the head bone. Calibrated on the male base in Unity renders: the base face's eyes sit
        // ~3.6 cm above the head bone and the face surface ~12 cm ahead of it there; the female base is close enough.
        string _faceId;
        GameObject _faceDecal;
        const string FaceRes = "WRLDZ/Avatar3D/Faces/";

        void ApplyFace(string id)
        {
            if (id == _faceId) return;
            _faceId = id;
            if (_faceDecal != null) { foreach (var fr in _faceDecal.GetComponentsInChildren<Renderer>(true)) KillMaterials(fr); Kill(_faceDecal); _faceDecal = null; }
            if (string.IsNullOrEmpty(id)) return;
            var tex = Resources.Load<Texture2D>(FaceRes + id);
            var anim = GetComponent<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (tex == null || head == null) return;
            _faceDecal = new GameObject("FaceDecal_" + id);
            _faceDecal.transform.SetParent(head, false);
            // eyes + brows over the base eyes; an optional stern / character mouth 5 cm below (Faces/<id>_mouth.png)
            // the female face surface is 1.5 cm nearer the head bone (eye line 0.100 m ahead vs 0.115 m on the male base)
            var fwd = _sex == "female" ? 0.106f : 0.121f;
            FaceQuad(id, tex, head, 0.036f, fwd, new Vector2(0.132f, 0.070f));
            var mouth = Resources.Load<Texture2D>(FaceRes + id + "_mouth");
            if (mouth != null) FaceQuad(id + "_mouth", mouth, head, -0.014f, fwd - 0.003f, new Vector2(0.084f, 0.042f));
        }

        /// <summary>One decal quad on the head-bone frame; parented under <paramref name="parent"/> (default: the face decal holder).</summary>
        void FaceQuad(string id, Texture2D tex, Transform head, float up01, float fwd01, Vector2 size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Kill(go.GetComponent<Collider>());
            go.name = "FaceDecal_" + id;
            go.layer = gameObject.layer;
            var mr = go.GetComponent<MeshRenderer>();
            var mat = Own(new Material(Shader.Find("WRLDZ/AvatarRecolor") ?? UnlitShader()) { name = "FaceDecal_" + id });
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); else mat.mainTexture = tex;
            // cut-out: the decal's alpha is the feathered skin patch around the eyes
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0f);
            if (mat.HasProperty("_ShadowAmt")) mat.SetFloat("_ShadowAmt", 0f);
            if (mat.HasProperty("_RimAmt")) mat.SetFloat("_RimAmt", 0f);
            if (mat.HasProperty("_UseTexAlpha")) mat.SetFloat("_UseTexAlpha", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 2460;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // placed in the body's rest frame (head upright, facing the body's forward), then parented to the head bone
            var fwd = transform.forward; var up = transform.up;
            var s = transform.lossyScale.y;
            var centre = head.position + up * (up01 * s) + fwd * (fwd01 * s);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.LookRotation(-fwd, up);   // a Quad faces -Z: turn it to face forward
            go.transform.localScale = new Vector3(size.x * s, size.y * s, 1f);
            var holder = parent != null ? parent : _faceDecal != null ? _faceDecal.transform : head;
            go.transform.SetParent(holder, true);
        }

        void ApplyHairStyle(string id)
        {
            if (id == _hairId) return;
            _hairId = id;
            if (_hairSmr != null) { KillMaterials(_hairSmr); Kill(_hairSmr.gameObject); _hairSmr = null; }
            SkinnedMeshRenderer baseHair = null;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == "ArgonPart_Hair") baseHair = r;

            var prefab = Resources.Load<GameObject>(HairRes + "argon_hair_" + id + "_" + _sex);
            _hairSmr = prefab != null ? GraftHair(prefab, id) : null;
            if (baseHair != null) baseHair.gameObject.SetActive(_hairSmr == null && id != "none");   // the built-in hair only as fallback; "none" = bald
        }

        /// <summary>
        /// Instantiate a hair FBX (same ArgonAvatarRig rest pose as the base), move its skinned mesh under this body
        /// and point its bones at this body's bones by name. The bindposes came from the identical rest skeleton,
        /// so the hair deforms with the head / neck / upper back exactly like a part that shipped inside the base.
        /// </summary>
        SkinnedMeshRenderer GraftHair(GameObject prefab, string id)
        {
            var src = GraftSkinned(prefab, "ArgonHair_" + id);
            if (src == null) return null;
            var tex = Resources.Load<Texture2D>(HairRes + "Hair_" + id);
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarHair");
            var mat = Own(tpl != null ? new Material(tpl) : new Material(Shader.Find("WRLDZ/AvatarHair") ?? UnlitShader()));
            if (tex != null) { if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex); }
            var mats = new Material[Mathf.Max(1, src.sharedMesh != null ? src.sharedMesh.subMeshCount : 1)];
            for (var i = 0; i < mats.Length; i++) mats[i] = i == 0 ? mat : Own(new Material(mat));
            src.sharedMaterials = mats;
            _textured.Add(src);
            return src;
        }

        /// <summary>
        /// Instantiate a part FBX built on the same ArgonAvatarRig rest pose (hair, outfits), move its skinned mesh under
        /// this body and point its bones at this body's bones by name. The bindposes came from the identical rest skeleton,
        /// so it deforms exactly like a part that shipped inside the base.
        /// </summary>
        SkinnedMeshRenderer GraftSkinned(GameObject prefab, string goName)
        {
            var inst = Instantiate(prefab);
            inst.SetActive(false);   // never drawn at the origin for the frame before Destroy
            var src = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (src == null) { Kill(inst); return null; }
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            var mapped = new Transform[src.bones.Length];
            for (var i = 0; i < mapped.Length; i++)
            {
                var b = src.bones[i];
                if (b == null || !bones.TryGetValue(b.name, out var own)) { Kill(inst); return null; }
                mapped[i] = own;
            }
            // keep the renderer's pose relative to its model root, then hang it under ours
            var localPos = inst.transform.InverseTransformPoint(src.transform.position);
            var localRot = Quaternion.Inverse(inst.transform.rotation) * src.transform.rotation;
            var go = src.gameObject;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one;
            go.name = goName;
            go.layer = gameObject.layer;
            src.bones = mapped;
            if (src.rootBone != null && bones.TryGetValue(src.rootBone.name, out var rb)) src.rootBone = rb;
            else if (_smr != null) src.rootBone = _smr.rootBone;
            src.updateWhenOffscreen = true;   // bounds follow the grafted bones (the map token is small; cheap)
            Kill(inst);
            return src;
        }

        // ---- canon outfits (Resources/WRLDZ/Avatar3D/Outfits, tools/custom/outfit_build.py)
        public const string OutfitRes = "WRLDZ/Avatar3D/Outfits/";
        string _outfitId;
        SkinnedMeshRenderer _outfitSmr;
        AvatarCatalog.OutfitDef _outfit;

        void ApplyOutfit(string id)
        {
            if (id == _outfitId) return;
            _outfitId = id;
            if (_outfitSmr != null) { _paintKey.Remove(_outfitSmr); KillMaterials(_outfitSmr); Kill(_outfitSmr.gameObject); _outfitSmr = null; }
            _outfit = null;
            var def = AvatarCatalog.FindOutfit(id);
            var prefab = def != null ? Resources.Load<GameObject>(OutfitRes + "argon_outfit_" + id + "_" + _sex) : null;
            if (prefab != null) _outfitSmr = GraftSkinned(prefab, "ArgonOutfit_" + id);
            if (_outfitSmr != null)
            {
                _outfit = def;
                var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
                var mat = Own(tpl != null ? new Material(tpl) : new Material(UnlitShader()));
                var tex = Resources.Load<Texture2D>(OutfitRes + "Outfit_" + id);
                var mask = Resources.Load<Texture2D>(OutfitRes + "Outfit_" + id + "_mask");
                if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mask != null && mat.HasProperty("_MaskMap")) mat.SetTexture("_MaskMap", mask);
                var rl = def.refLum ?? new float[0];
                if (mat.HasProperty("_RefLum"))
                    mat.SetVector("_RefLum", new Vector4(rl.Length > 0 ? rl[0] : 0.2f, rl.Length > 1 ? rl[1] : 0.2f, rl.Length > 2 ? rl[2] : 0.2f, 0f));
                var mats = new Material[Mathf.Max(1, _outfitSmr.sharedMesh != null ? _outfitSmr.sharedMesh.subMeshCount : 1)];
                for (var i = 0; i < mats.Length; i++) mats[i] = i == 0 ? mat : Own(new Material(mat));
                _outfitSmr.sharedMaterials = mats;
                _textured.Add(_outfitSmr);
                _paintKey[_outfitSmr] = "Outfit";
            }
            // the base clothes step aside; the body under the outfit is not drawn
            var worn = _outfitSmr != null;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name is "ArgonPart_Shirt" or "ArgonPart_Bottoms" or "ArgonPart_Shoes") r.gameObject.SetActive(!worn);
            var hide = worn ? Resources.Load<Texture2D>(OutfitRes + "Outfit_" + id + "_" + _sex + "_hide") : null;
            SetBodyHide(hide);
        }

        // ---- clothing slots (Resources/WRLDZ/Avatar3D/Clothes, tools/custom/slot_clothes.py)
        public const string ClothRes = "WRLDZ/Avatar3D/Clothes/";
        string _clothesKey;
        bool _hatWorn;
        readonly List<SkinnedMeshRenderer> _clothSmr = new();
        readonly List<Material> _clothMats = new();
        Texture2D _hideComposite;

        void ApplyClothes(AvatarAppearance look)
        {
            if (_fullCharacter || look == null) return;
            if (_outfitSmr != null)
            {
                DestroySlotGrafts();
                _clothesKey = "outfit";
                _hatWorn = false;
                return;
            }
            var key = _sex + "|" + look.hatId + "|" + look.facialId + "|" + look.shirtId + "|"
                      + look.handsId + "|" + look.bottomsId + "|" + look.shoesId;
            if (key == _clothesKey) return;
            _clothesKey = key;
            DestroySlotGrafts();
            var hides = new List<Texture2D>(4);
            // a piece that does not fit this body (or is missing) falls back to the default shell of its slot; the Meshy
            // body's own garments (2-3 cm off the skin) only show if even that is missing
            var shirt = GraftCloth(look.shirtId, hides) || GraftCloth(ClothingCatalog.DefaultShirt, hides);
            var bottoms = GraftCloth(look.bottomsId, hides) || GraftCloth(ClothingCatalog.DefaultBottoms, hides);
            var shoes = GraftCloth(look.shoesId, hides) || GraftCloth(ClothingCatalog.DefaultShoes, hides);
            GraftCloth(look.handsId, hides);
            _hatWorn = GraftCloth(look.hatId, hides);     // the id alone is not enough: a hat that does not fit this body is not worn
            GraftCloth(look.facialId, hides);
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.name == "ArgonPart_Shirt") r.gameObject.SetActive(!shirt);
                else if (r.name == "ArgonPart_Bottoms") r.gameObject.SetActive(!bottoms);
                else if (r.name == "ArgonPart_Shoes") r.gameObject.SetActive(!shoes);
            }
            SetBodyHide(CombineHides(hides));
        }

        /// <summary>
        /// A crowned hat replaces the hairstyle. Runs after the hair, outfit and clothes steps on EVERY apply (not inside the
        /// keyed clothes block): changing the hairstyle under a cap grafts a fresh, active hair that must be hidden again,
        /// and taking the cap off must show the hair (grafted or the built-in fallback) without a slot change.
        /// </summary>
        void SyncHairUnderHat(AvatarAppearance look)
        {
            if (_fullCharacter || look == null) return;
            var clip = _outfitSmr == null && _hatWorn ? ClothingCatalog.HatHairClip(look.hatId) : ClothingCatalog.HairClip.None;
            // a grafted hairstyle stays on and is clipped inside the hat (AvatarHair _HatClip); the built-in fallback hair
            // has no clip support, so it is hidden under a hat as before
            if (_hairSmr != null) _hairSmr.gameObject.SetActive(true);
            SetupHatClip(clip);
            var hideBase = clip != ClothingCatalog.HairClip.None;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == "ArgonPart_Hair")
                    r.gameObject.SetActive(!hideBase && _hairSmr == null && (look.hairStyleId ?? "") != "none");
        }

        // ---- hair under a hat: a plane in head-bone space, pushed to the hair material every frame
        static readonly int HatClipId = Shader.PropertyToID("_HatClip");
        static readonly int HatClipOnId = Shader.PropertyToID("_HatClipOn");
        bool _hatClipOn;
        Vector3 _hatClipPointLocal, _hatClipNormalLocal;
        static readonly int HatTuckCId = Shader.PropertyToID("_HatTuckC");
        static readonly int HatTuckXId = Shader.PropertyToID("_HatTuckX");
        static readonly int HatTuckYId = Shader.PropertyToID("_HatTuckY");
        static readonly int HatTuckZId = Shader.PropertyToID("_HatTuckZ");
        static readonly int HatTuckOnId = Shader.PropertyToID("_HatTuckOn");
        bool _hatTuckOn;
        float _hatTuckBand;
        // the tuck ellipsoid in head-bone space: centre and its three semi-axes as vectors (length = radius)
        Vector3 _tuckCentreLocal, _tuckAxisXLocal, _tuckAxisYLocal, _tuckAxisZLocal;
        static readonly Dictionary<Mesh, (Vector3 c, Vector3 r)> s_craniumFit = new();

        /// <summary>
        /// The rim plane of the worn hat in head-bone space, from the same landmarks the hat shells are cut with
        /// (tools/custom/slot_clothes.py): caps / beanies 8.8 cm above the skull base at the forehead to 3.5 cm at the
        /// nape (1 cm lower, under the hat's lip), hoods / the veil level with their lower edge at the neck.
        /// </summary>
        static bool s_renderHooked;

        /// <summary>Push every worn hat's clip plane just before rendering, after all LateUpdates (ArOpponentDuelist turns
        /// the head at execution order 650, after this component's LateUpdate).</summary>
        static void HookRender()
        {
            if (s_renderHooked) return;
            s_renderHooked = true;
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += (ctx, cams) =>
            {
                for (var i = 0; i < s_live.Count; i++)
                    if (s_live[i] != null && s_live[i]._hatClipOn) s_live[i].PushHatClip();
            };
        }

        void SetupHatClip(ClothingCatalog.HairClip clip)
        {
            HookRender();
            _hatClipOn = false; _hatTuckOn = false;
            if (!_idleBonesIndexed) IndexIdleBones();               // the head bone may not be indexed yet on the first apply
            if (clip != ClothingCatalog.HairClip.None && _headBone != null)
            {
                // the landmarks are taken in the body's rest (bind) pose, in this avatar's own space, so the plane is the
                // same whatever pose the body is in when the hat goes on (the customizer applies mid-idle; a tilted head
                // used to bake its tilt into the cut)
                var restHead = RestBoneToRoot(_headBone);
                var headZ = _sex == "female" ? 1.486f : 1.582f;               // Head bone height on the base bodies (m)
                var h = (Vector3)restHead.GetColumn(3);
                var s = Mathf.Max(0.3f, h.y / headZ);
                var up = Vector3.up; var fwd = Vector3.forward; var right = Vector3.right;
                Vector3 p, n;
                if (clip == ClothingCatalog.HairClip.Rim)
                {
                    var f = h + up * (0.078f * s) + fwd * (0.08f * s);
                    var b = h + up * (0.025f * s) - fwd * (0.11f * s);
                    n = Vector3.Cross(right, f - b).normalized;
                    if (Vector3.Dot(n, up) < 0f) n = -n;
                    p = f;
                }
                else
                {
                    var drop = _sex == "female" ? 0.112f : 0.098f;                // the hood's lower edge: Neck - 4 cm
                    p = h - up * (drop * s); n = up;
                }
                var toHead = restHead.inverse;
                _hatClipPointLocal = toHead.MultiplyPoint3x4(p);
                _hatClipNormalLocal = toHead.MultiplyVector(n).normalized;
                _hatClipOn = true;
                // caps and beanies also tuck the hair under the rim (not hoods: long hair falls out from under those)
                Vector3 cc = default, cr = default;
                _hatTuckOn = clip == ClothingCatalog.HairClip.Rim && TryCranium(restHead, out cc, out cr);
                if (_hatTuckOn)
                {
                    const float rimOut = 0.018f;                                   // the hat shell sits ~2.2 cm off the scalp
                    _tuckCentreLocal = toHead.MultiplyPoint3x4(cc);
                    _tuckAxisXLocal = toHead.MultiplyVector(Vector3.right * (cr.x + rimOut * s));
                    _tuckAxisYLocal = toHead.MultiplyVector(Vector3.up * (cr.y + rimOut * s));
                    _tuckAxisZLocal = toHead.MultiplyVector(Vector3.forward * (cr.z + rimOut * s));
                    _hatTuckBand = 0.04f * s;
                }
            }
            PushHatClip();
        }

        /// <summary>A bone's rest (bind-pose) frame in this avatar's own space: bone-local to avatar-local. Falls back to
        /// the current pose for a bone the body mesh is not skinned to.</summary>
        Matrix4x4 RestBoneToRoot(Transform bone)
        {
            if (_smr != null && _smr.sharedMesh != null)
            {
                var i = System.Array.IndexOf(_smr.bones, bone);
                var bp = _smr.sharedMesh.bindposes;
                if (i >= 0 && i < bp.Length)
                    return transform.worldToLocalMatrix * _smr.transform.localToWorldMatrix * bp[i].inverse;
            }
            return transform.worldToLocalMatrix * bone.localToWorldMatrix;
        }

        /// <summary>The cranium as an axis-aligned ellipsoid in this avatar's own space, rest pose (cached per body mesh):
        /// body vertices skinned to the head above the eye line; the centre height is the widest band (about the
        /// temples), the radii reach the crown, the ears and the brow / occiput.</summary>
        bool TryCranium(Matrix4x4 restHead, out Vector3 centre, out Vector3 radii)
        {
            centre = radii = Vector3.zero;
            if (_smr == null || _smr.sharedMesh == null || _headBone == null) return false;
            var mesh = _smr.sharedMesh;
            var key = mesh;
            var toRoot = transform.worldToLocalMatrix * _smr.transform.localToWorldMatrix;
            if (!s_craniumFit.TryGetValue(key, out var fit))
            {
                var hi = System.Array.IndexOf(_smr.bones, _headBone);
                var bw = mesh.boneWeights; var vs = mesh.vertices;
                if (hi < 0 || bw.Length != vs.Length) return false;
                var h = (Vector3)restHead.GetColumn(3);
                var pts = new List<Vector3>();
                var s = Mathf.Max(0.3f, h.y / (_sex == "female" ? 1.486f : 1.582f));
                for (var i = 0; i < vs.Length; i++)
                {
                    if (bw[i].boneIndex0 != hi || bw[i].weight0 < 0.9f) continue;
                    var p = toRoot.MultiplyPoint3x4(vs[i]);
                    if (p.y > h.y + 0.031f * s) pts.Add(p);                 // above the eye line: cranium, no face / jaw
                }
                if (pts.Count < 50) return false;
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
                // the widest 1 cm band gives the centre height
                float bestW = 0f, bestY = minY;
                for (var y = minY; y < maxY; y += 0.005f * s)
                {
                    float lo = float.MaxValue, up = float.MinValue;
                    foreach (var p in pts) if (Mathf.Abs(p.y - y) < 0.005f * s) { lo = Mathf.Min(lo, p.x); up = Mathf.Max(up, p.x); }
                    if (up - lo > bestW) { bestW = up - lo; bestY = y; }
                }
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var p in pts)
                {
                    if (Mathf.Abs(p.y - bestY) > 0.015f * s) continue;
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
                }
                fit = (new Vector3((x0 + x1) * 0.5f, bestY, (z0 + z1) * 0.5f), new Vector3((x1 - x0) * 0.5f, maxY - bestY, (z1 - z0) * 0.5f));
                s_craniumFit[key] = fit;
            }
            centre = fit.c; radii = fit.r;
            return radii.x > 0.02f && radii.y > 0.02f && radii.z > 0.02f;
        }

        static Texture2D s_splitMask;

        /// <summary>A 2x1 linear mask: left texel region R (paint .0), right texel region G (paint .1).</summary>
        static Texture2D SplitMask()
        {
            if (s_splitMask != null) return s_splitMask;
            s_splitMask = new Texture2D(2, 1, TextureFormat.RGBA32, false, true)
            {
                name = "ClothSplitMask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            s_splitMask.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255) });
            s_splitMask.Apply(false, true);
            return s_splitMask;
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        void PushHatClip()
        {
            if (_hairSmr == null) return;
            var on = _hatClipOn && _headBone != null;
            var v = Vector4.zero;
            if (on)
            {
                var n = (_headBone.rotation * _hatClipNormalLocal).normalized;
                var p = _headBone.TransformPoint(_hatClipPointLocal);
                v = new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, p));
            }
            var tuck = on && _hatTuckOn;
            Vector4 tc = Vector4.zero, tx = Vector4.zero, ty = Vector4.zero, tz = Vector4.zero;
            if (tuck)
            {
                var c = _headBone.TransformPoint(_tuckCentreLocal);
                var s = Mathf.Max(1e-4f, transform.lossyScale.y);
                tc = new Vector4(c.x, c.y, c.z, _hatTuckBand * s);
                static Vector4 Inv(Vector3 a) { var l2 = Mathf.Max(a.sqrMagnitude, 1e-8f); return a / l2; }   // axis / radius
                tx = Inv(_headBone.TransformVector(_tuckAxisXLocal));
                ty = Inv(_headBone.TransformVector(_tuckAxisYLocal));
                tz = Inv(_headBone.TransformVector(_tuckAxisZLocal));
            }
            foreach (var m in _hairSmr.sharedMaterials)
            {
                if (m == null || !m.HasProperty(HatClipOnId)) continue;
                m.SetFloat(HatClipOnId, on ? 1f : 0f);
                if (on) m.SetVector(HatClipId, v);
                if (!m.HasProperty(HatTuckOnId)) continue;
                m.SetFloat(HatTuckOnId, tuck ? 1f : 0f);
                if (tuck) { m.SetVector(HatTuckCId, tc); m.SetVector(HatTuckXId, tx); m.SetVector(HatTuckYId, ty); m.SetVector(HatTuckZId, tz); }
            }
        }

        bool GraftCloth(string id, List<Texture2D> hides)
        {
            var item = ClothingCatalog.Get(id);
            if (item == null || item.EmptySlot || !item.FitsBody(_sex == "female")) return false;
            var prefab = Resources.Load<GameObject>(ClothRes + "argon_" + item.Id + "_" + _sex);
            if (prefab == null) return false;
            var smr = GraftSkinned(prefab, "ArgonCloth_" + item.Id);
            if (smr == null) return false;
            _clothSmr.Add(smr);
            var tpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            var mat = Own(tpl != null ? new Material(tpl) : new Material(UnlitShader()));
            var tex = Resources.Load<Texture2D>(ClothRes + item.AlbedoStem);
            var mask = string.IsNullOrEmpty(item.Mask) ? null : Resources.Load<Texture2D>(ClothRes + item.Mask);
            // the base wardrobe shirts / bottoms / shoes are flat-colour shells without an authored mask: the whole piece is
            // paint region A, so "Shirt.0" / "Bottoms.0" / "Shoes.0" recolour it (street NPC looks, the player's paint tab).
            // Signature pieces keep their own colours.
            var flatPaint = mask == null && item.BodyType < 0
                            && item.Slot is ClothingSlot.Shirt or ClothingSlot.Bottoms or ClothingSlot.Shoes;
            // two regions: the swatch's left half is the main colour (paint key .0), the right half the trim (.1: collar,
            // cuffs and hem bands, a waistband, a shoe's sole); the shell's UVs pick one half per face
            if (flatPaint) mask = SplitMask();
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mask != null && mat.HasProperty("_MaskMap")) mat.SetTexture("_MaskMap", mask);
            if (mat.HasProperty("_RefLum"))
            {
                if (flatPaint)
                {
                    // each half's own linear luminance: a paint lands exactly on its colour
                    var a = item.Fallback; var b = a * 0.78f;
                    if (tex != null && tex.isReadable && tex.width >= 2)
                    {
                        a = tex.GetPixel(tex.width / 4, tex.height / 2);
                        b = tex.GetPixel(tex.width * 3 / 4, tex.height / 2);
                    }
                    mat.SetVector("_RefLum", new Vector4(Lum(a.linear), Lum(b.linear), 0.2f, 0f));
                }
                else mat.SetVector("_RefLum", new Vector4(item.RefR, item.RefG, item.RefB, 0f));
            }
            // a clothing piece is an open one-sided shell: with back-face culling the gap between a hem and the skin showed
            // the background as dark teeth along every hem (Editor renders, 2026-10-04). Both sides render.
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            var mats = new Material[Mathf.Max(1, smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 1)];
            for (var i = 0; i < mats.Length; i++) mats[i] = i == 0 ? mat : Own(new Material(mat));
            smr.sharedMaterials = mats;
            _clothMats.AddRange(mats);
            _textured.Add(smr);
            if (mask != null) _paintKey[smr] = item.Slot switch
            {
                ClothingSlot.Bottoms => "Bottoms",
                ClothingSlot.Shoes => "Shoes",
                _ => "Shirt"
            };
            var hide = Resources.Load<Texture2D>(ClothRes + "hide_" + item.Id + "_" + _sex);
            if (hide != null) hides.Add(hide);
            return true;
        }

        void DestroySlotGrafts()
        {
            for (var i = 0; i < _clothSmr.Count; i++)
            {
                var r = _clothSmr[i];
                if (r == null) continue;
                _paintKey.Remove(r);
                _textured.Remove(r);
                Kill(r.gameObject);
            }
            _clothSmr.Clear();
            // the per-piece material instances go with the pieces (every wardrobe tap used to leak a handful)
            for (var i = 0; i < _clothMats.Count; i++) if (_clothMats[i] != null) { _ownedMats.Remove(_clothMats[i]); Kill(_clothMats[i]); }
            _clothMats.Clear();
            if (_hideComposite != null) Kill(_hideComposite);
            _hideComposite = null;
            // hair / outfit / face materials stay: those pieces outlive a clothes change (OnDestroy frees them)
        }

        void SetBodyHide(Texture2D hide)
        {
            if (_smr == null) return;
            foreach (var m in _smr.sharedMaterials)
            {
                if (m == null || !m.HasProperty("_HideOn")) continue;
                m.SetTexture("_HideMap", hide);
                m.SetFloat("_HideOn", hide != null ? 1f : 0f);
            }
        }

        Texture2D CombineHides(List<Texture2D> hides)
        {
            if (hides == null || hides.Count == 0) return null;
            if (hides.Count == 1) return hides[0];
            var w = 0; var h = 0;
            for (var i = 0; i < hides.Count; i++) if (hides[i] != null && hides[i].isReadable) { w = Mathf.Max(w, hides[i].width); h = Mathf.Max(h, hides[i].height); }
            if (w == 0) return hides[0];
            var acc = new byte[w * h];
            var wrote = false;
            for (var i = 0; i < hides.Count; i++)
            {
                var t = hides[i];
                if (t == null || !t.isReadable) continue;
                if (t.width == w && t.height == h && t.format == TextureFormat.R8)
                {
                    var px = t.GetPixelData<byte>(0);                      // zero-copy view of the R8 data
                    for (var p = 0; p < acc.Length; p++) if (px[p] > 127) { acc[p] = 255; wrote = true; }
                }
                else if (t.width == w && t.height == h)
                {
                    var px = t.GetPixels32();
                    for (var p = 0; p < px.Length; p++) if (px[p].r > 127) { acc[p] = 255; wrote = true; }
                }
                else
                {
                    // a map at another resolution is resampled, never dropped (dropping it left the skin visible under that piece)
                    for (var y = 0; y < h; y++)
                        for (var x = 0; x < w; x++)
                            if (t.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h).r > 0.5f) { acc[y * w + x] = 255; wrote = true; }
                }
            }
            if (!wrote) return hides[0];
            var tex = new Texture2D(w, h, TextureFormat.R8, false, true);
            tex.name = "ClothHide";
            tex.SetPixelData(acc, 0);
            tex.Apply(false, true);
            _hideComposite = tex;
            return tex;
        }

        static readonly int ScalpMapId = Shader.PropertyToID("_ScalpMap");
        static readonly int ScalpColorId = Shader.PropertyToID("_ScalpColor");

        /// <summary>The body's painted hairline takes the look's hair root colour (seen under hats and at partings).</summary>
        void ApplyScalpColour(AvatarAppearance look)
        {
            if (_smr == null || _fullCharacter) return;
            var map = Resources.Load<Texture2D>("WRLDZ/Avatar3D/Textures/scalp_" + _sex + "_mask");
            var c = Color.clear;
            var on = map != null && ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(look.hairHex) ? "#2A2A30" : look.hairHex, out c);
            foreach (var m in _smr.sharedMaterials)
            {
                if (m == null || !m.HasProperty(ScalpColorId)) continue;
                if (map != null) m.SetTexture(ScalpMapId, map);
                m.SetColor(ScalpColorId, on ? new Color(c.r, c.g, c.b, 1f) : Color.clear);
            }
        }

        void ApplyHairColour(AvatarAppearance look)
        {
            ApplyScalpColour(look);
            if (_hairSmr == null) return;
            var p = (HairPattern)look.hairPattern;
            var zones = p switch
            {
                HairPattern.Tips => new Vector4(1, 0, 0, 0),
                HairPattern.TriColor => new Vector4(1, 1, 0, 0),
                HairPattern.Streaks => new Vector4(0, 0, 1, 0),
                HairPattern.Split => new Vector4(0, 0, 0, 1),
                _ => Vector4.zero
            };
            foreach (var m in _hairSmr.sharedMaterials)
            {
                if (m == null) continue;
                m.SetFloat("_Original", p == HairPattern.Original ? 1f : 0f);
                m.SetVector("_Zones", zones);
                SetHex(m, "_HairRoot", look.hairHex);
                SetHex(m, "_HairTip", look.hairTipHex);
                SetHex(m, "_HairFringe", look.hairFringeHex);
                SetHex(m, "_HairStreak", look.hairStreakHex);
                SetHex(m, "_HairSide", look.hairSideHex);
            }
        }

        static void SetHex(Material m, string prop, string hex)
        {
            if (m.HasProperty(prop) && ColorUtility.TryParseHtmlString(hex, out var c)) m.SetColor(prop, c);
        }

        static readonly string[] RegionProps = { "_ColorA", "_ColorB", "_ColorC" };

        void ApplyPaints(AvatarAppearance look)
        {
            foreach (var kv in _paintKey)
            {
                var r = kv.Key;
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_ColorA")) continue;
                    for (var k = 0; k < RegionProps.Length; k++)
                    {
                        var hex = look.GetPaint(kv.Value + "." + k);
                        // The body's skin region is never left unpainted: a look with no Body.0 paint of its own
                        // still gets its skinHex (else the region-painted body would fall back to the white
                        // _BaseMap and read as a white body under the clothes).
                        if (string.IsNullOrEmpty(hex) && kv.Value == "Body" && k == 0)
                            hex = string.IsNullOrEmpty(look.skinHex) ? SkinHexFor(look) : look.skinHex;
                        if (string.IsNullOrEmpty(hex) && kv.Value == "Body" && k == 1)
                        {
                            var suit = _outfit != null ? _outfit.suit : ClothingCatalog.Get(look.shirtId)?.Suit;
                            if (!string.IsNullOrEmpty(suit))
                                hex = suit == "skin" ? SkinHexFor(look) : suit;
                        }
                        var on = !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out _);
                        var c = on && ColorUtility.TryParseHtmlString(hex, out var parsed) ? parsed : Color.white;
                        c.a = on ? 1f : 0f;
                        m.SetColor(RegionProps[k], c);
                    }
                }
            }
        }

        /// <summary>The skin the undersuit takes under an outfit with bare arms: the player's skin paint, else the painted skin's own mean.</summary>
        string SkinHexFor(AvatarAppearance look)
        {
            var p = look.GetPaint("Body.0");
            if (!string.IsNullOrEmpty(p)) return p;
            return _sex == "female" ? "#FBBF96" : "#F9C494";
        }

        /// <summary>Renderer -> paint key prefix ("Shirt", "Bottoms", "Shoes", "Body") for parts built with the recolor shader.</summary>
        readonly Dictionary<Renderer, string> _paintKey = new();

        /// <summary>One part's region mask, the paint-key prefix it drives (<c>Body</c> / <c>Shirt</c> ...) and the
        /// mask's per-channel mean luminance in the source albedo (see <see cref="MiniPaints"/>).</summary>
        class PaintPart { public string part; public string key; public string mask; public float[] refLum; }

        // ---------------------------------------------------------------- materials
        // The FBX is imported with materials OFF (import rule), so nothing PBR ever reaches the screen
        // (art guide: unlit / emissive output only). Each part's submeshes are rebuilt here from
        // argon_base_<sex>.parts.json + Textures/: every part with a manifest texture is textured unlit (the
        // Meshy base: body, hair, shirt, bottoms, shoes); a part without one stays flat unlit and takes the skin /
        // hair tint. Eye layers and mouth/nose decal cards (older MPFB builds only; optional, matched by name) are
        // textured unlit with alpha. The cel ramp + inverted-hull outline replace the
        // unlit shader in the shading milestone; the material plumbing stays the same.
        [System.Serializable] class PartMat { public string material; public string texture; }

        /// <summary>Renderers whose manifest texture loaded: skin / hair tints skip these (see <see cref="Apply"/>).</summary>
        readonly HashSet<Renderer> _textured = new();

        static Shader UnlitShader() =>
            Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");

        public void BuildMaterials(string sex)
        {
            _sex = sex;
            _paintKey.Clear();
            var paintInfo = new Dictionary<string, PaintPart>();
            var pj = LoadManifest(".paint", sex);
            if (pj != null) foreach (var pp in MiniPaints(pj.text)) paintInfo[pp.part] = pp;
            var recolorTpl = Resources.Load<Material>("WRLDZ/Avatar3D/Materials/AvatarRecolor");
            var json = LoadManifest(".parts", sex);
            var lookup = new Dictionary<string, List<PartMat>>();
            if (json != null)
            {
                // JsonUtility cannot read a dictionary; the manifest is {"PartName": {...}} so walk it by hand
                foreach (var part in MiniParts(json.text)) lookup[part.Key] = part.Value;
            }
            var sh = UnlitShader();
            _textured.Clear();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                lookup.TryGetValue(r.name, out var mats);
                var count = r.sharedMesh != null ? r.sharedMesh.subMeshCount : 1;
                var arr = new Material[count];
                paintInfo.TryGetValue(r.name, out var paint);
                var mask = paint != null && recolorTpl != null && !string.IsNullOrEmpty(paint.mask) ? Resources.Load<Texture2D>("WRLDZ/Avatar3D/Textures/" + paint.mask) : null;
                for (var i = 0; i < count; i++)
                {
                    var m = Own(mask != null ? new Material(recolorTpl) : new Material(sh));
                    if (mask != null)
                    {
                        m.SetTexture("_MaskMap", mask);
                        var rl = paint.refLum ?? new float[0];
                        m.SetVector("_RefLum", new Vector4(rl.Length > 0 ? rl[0] : 0.2f, rl.Length > 1 ? rl[1] : 0.2f, rl.Length > 2 ? rl[2] : 0.2f, 0f));
                        _paintKey[r] = paint.key;
                    }
                    var pm = mats != null && i < mats.Count ? mats[i] : null;
                    var isEye = r.name.StartsWith("ArgonPart_Eye") && !r.name.Contains("brow") && !r.name.Contains("lash");
                    var albedo = false;
                    if (pm != null && !string.IsNullOrEmpty(pm.texture))
                    {
                        var tex = Resources.Load<Texture2D>("WRLDZ/Avatar3D/Textures/" + pm.texture.Replace(".png", ""));
                        if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); _textured.Add(r); albedo = true; }
                    }
                    // No part albedo ships yet - Textures/ holds only the region masks - so a masked part keeps the
                    // white default _BaseMap. Paint keeps a region's shading through detail = (lum / refLum)^0.75, and
                    // a white base has lum = 1, so the manifest's source-albedo means would scale every paint up
                    // (a coat's primary 0.011 -> 8x, a suit 0.22 -> 2.8x). RefLum 1 lands each paint on its own
                    // colour; the manifest values stand in again as soon as an albedo texture exists.
                    if (mask != null && !albedo) m.SetVector("_RefLum", new Vector4(1f, 1f, 1f, 0f));
                    var isDecal = r.name == "ArgonPart_Mouth" || r.name == "ArgonPart_Nose";   // drawn mouth line / nose tick cards
                    if (isEye || isDecal || r.name.Contains("Eyelash") || r.name.Contains("Eyebrow"))
                    {
                        // alpha-blended layers, drawn after the skin so the iris sits on the face
                        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        m.renderQueue = 3000 + (isEye ? i : 5);
                    }
                    arr[i] = m;
                }
                r.sharedMaterials = arr;
            }
        }

        /// <summary>The Meshy avatar build renamed the part manifests from argon_base_* to argon_avatar_*; accept
        /// either name so a renamed (or older) manifest still binds. A miss here is not cosmetic: without the part
        /// masks every part renders flat and the whole body takes the skin tint, and without the recolor material
        /// the body has no _HideOn, so it is never discarded under the clothes it wears.</summary>
        static TextAsset LoadManifest(string stem, string sex) =>
            Resources.Load<TextAsset>("WRLDZ/Avatar3D/argon_avatar_" + sex + stem)
            ?? Resources.Load<TextAsset>("WRLDZ/Avatar3D/argon_base_" + sex + stem);

        /// <summary>Tiny reader for the parts manifest: {"Part": {"materials":[{"material":..,"texture":..},..]}}.</summary>
        static IEnumerable<KeyValuePair<string, List<PartMat>>> MiniParts(string text)
        {
            // Find each top-level key and its "materials" array; enough structure for our own file.
            var i = 0;
            while (true)
            {
                var k0 = text.IndexOf("\"Argon", i); if (k0 < 0) yield break;
                var k1 = text.IndexOf('\"', k0 + 1); var name = text.Substring(k0 + 1, k1 - k0 - 1);
                var m0 = text.IndexOf("\"materials\"", k1); var a0 = text.IndexOf('[', m0); var a1 = MatchBracket(text, a0);
                var list = new List<PartMat>();
                var body = text.Substring(a0 + 1, a1 - a0 - 1);
                foreach (var obj in SplitObjects(body))
                {
                    var pm = new PartMat { material = Field(obj, "material"), texture = Field(obj, "texture") };
                    list.Add(pm);
                }
                yield return new KeyValuePair<string, List<PartMat>>(name, list);
                i = a1 + 1;
            }
        }

        /// <summary>Tiny reader for the paint manifest:
        /// <c>{"parts":{"&lt;PartName&gt;":{"recolor":{"mask":"&lt;file&gt;.png","regions":[{"region":..,"mean_lum_linear":..},..]}}}}</c>.
        /// JsonUtility cannot read this file at all - the part name is a JSON key rather than a "part" field and the
        /// per-region mean luminances are not a "refLum" array - so every part came back null. The miss is not
        /// cosmetic: with no mask a part renders flat unlit, the body never gets its region paints and the whole
        /// body falls to the flat skin tint, which reads as a naked avatar under its clothes. The region order in
        /// the file is the mask's channel order (body R skin / G undersuit, garments R main / G accent / B trim),
        /// which is the order _ColorA/B/C wants.</summary>
        static IEnumerable<PaintPart> MiniPaints(string text)
        {
            var i = 0;
            while (true)
            {
                var k0 = text.IndexOf("\"Argon", i); if (k0 < 0) yield break;
                var k1 = text.IndexOf('"', k0 + 1); var name = text.Substring(k0 + 1, k1 - k0 - 1);
                var next = text.IndexOf("\"Argon", k1); var end = next < 0 ? text.Length : next;
                i = end;
                var key = name.Contains("Body") ? "Body"
                        : name.Contains("Shirt") ? "Shirt"
                        : name.Contains("Bottoms") ? "Bottoms"
                        : name.Contains("Shoes") ? "Shoes" : null;
                if (key == null) continue;
                var recolor = text.IndexOf("\"recolor\"", k1); if (recolor < 0 || recolor > end) continue;
                var block = text.Substring(k1, end - k1);
                var mask = Field(block, "mask"); if (mask != null) mask = mask.Replace(".png", "");
                var rl = new List<float>();
                var rq = block.IndexOf("\"regions\"");
                for (var s = rq < 0 ? -1 : rq; s >= 0;)
                {
                    var l0 = block.IndexOf("\"mean_lum_linear\"", s); if (l0 < 0) break;
                    var e = block.IndexOf(':', l0) + 1;
                    while (e < block.Length && char.IsWhiteSpace(block[e])) e++;
                    var f = e;
                    while (f < block.Length && (char.IsDigit(block[f]) || block[f] is '-' or '+' or '.' or 'e' or 'E')) f++;
                    if (float.TryParse(block.Substring(e, f - e), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) rl.Add(v);
                    s = f;
                }
                yield return new PaintPart { part = name, key = key, mask = mask, refLum = rl.ToArray() };
            }
        }
        static int MatchBracket(string t, int open) { var d = 0; for (var i = open; i < t.Length; i++) { if (t[i] == '[') d++; else if (t[i] == ']') { d--; if (d == 0) return i; } } return t.Length - 1; }
        static IEnumerable<string> SplitObjects(string arr) { var d = 0; var start = -1; for (var i = 0; i < arr.Length; i++) { if (arr[i] == '{') { if (d == 0) start = i; d++; } else if (arr[i] == '}') { d--; if (d == 0 && start >= 0) { yield return arr.Substring(start, i - start + 1); start = -1; } } } }
        static string Field(string obj, string key) { var k = obj.IndexOf("\"" + key + "\""); if (k < 0) return null; var c = obj.IndexOf(':', k); var q0 = obj.IndexOf('\"', c + 1); if (q0 < 0) return null; if (obj.Substring(c + 1, q0 - c - 1).Trim().StartsWith("null")) return null; var q1 = obj.IndexOf('\"', q0 + 1); return obj.Substring(q0 + 1, q1 - q0 - 1); }

        // ---------------------------------------------------------------- presence
        // Owner rules (Dave, 2026-09-22/23):
        //  * Zone Mode PROMPT (the "enter this zone?" sheet on the map): the body ghosts to 0.3 — the
        //    player's physical form is about to inhabit that space (OverworldUI calls SetPresence(0.3f)).
        //  * While a duel (DuelSlice / ArDuelSpace) or Zone Mode is ACTIVE: "the player's avatar should
        //    not be obstructive or really visible" — the player's real body is there. The body is
        //    HIDDEN: every Renderer disabled, the GameObject, Animator graph and look stay alive so the
        //    map token comes back instantly on exit. Driven by HideBody(reason) from the entry points
        //    (ArDuelSpace lifetime, ZoneModeSession lifetime, AppSession.StartArDuel scene hop).

        /// <summary>Reason key prefix: an <see cref="ArDuelSpace"/> (AR duel or Zone Mode stage) is alive.</summary>
        public const string HideForDuel = "duel";
        /// <summary>Reason key prefix: a Zone Mode session is alive.</summary>
        public const string HideForZone = "zone";
        /// <summary>Set by the duel launch; cleared on the next single-mode scene load.</summary>
        public const string HideForDuelLaunch = "duel-launch";

        static readonly HashSet<string> s_hideReasons = new();
        static readonly List<OverworldAvatar3d> s_live = new();

        /// <summary>
        /// Opt-out for a deliberate in-duel piece built from this body (e.g. the translucent floating
        /// right hand): global <see cref="HideBody"/> reasons are ignored, only <see cref="SetPresence"/> applies.
        /// </summary>
        public bool IgnoreBodyHide;

        float _presence = 1f;          // what the caller asked for (1 solid, 0.3 spirit, 0 hidden)
        float _appliedAlpha = 1f;      // alpha last pushed into the materials
        bool _renderersOff;

        /// <summary>True while any duel / Zone Mode reason hides the player's body.</summary>
        public static bool BodyHidden => s_hideReasons.Count > 0;

        /// <summary>
        /// Hide (or release) the player's body for <paramref name="reason"/>. Reasons stack: the body
        /// shows again only when every reason is released. Use a per-instance key
        /// (e.g. <c>HideForDuel + ":" + GetEntityId()</c>) when several owners can overlap.
        /// </summary>
        public static void HideBody(string reason, bool hide)
        {
            if (string.IsNullOrEmpty(reason)) return;
            var changed = hide ? s_hideReasons.Add(reason) : s_hideReasons.Remove(reason);
            if (!changed) return;
            for (var i = s_live.Count - 1; i >= 0; i--)
            {
                if (s_live[i] == null) { s_live.RemoveAt(i); continue; }
                s_live[i].ApplyPresence();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // survives "Enter Play Mode without domain reload"
            s_hideReasons.Clear();
            s_live.Clear();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // The launch reason only bridges the scene hop; ArDuelSpace owns "duel" once the stage exists,
            // and a refused / aborted launch must not leave the map token invisible.
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single) HideBody(HideForDuelLaunch, false);
        }

        void OnEnable()
        {
            if (!s_live.Contains(this)) s_live.Add(this);
            ApplyPresence();
        }

        void OnDisable() => s_live.Remove(this);

        /// <summary>
        /// 1 = solid (map), 0.3 = spirit (Zone Mode prompt), 0 = hidden (all Renderers disabled, object
        /// kept alive). A global <see cref="HideBody"/> reason overrides any requested presence.
        /// </summary>
        public void SetPresence(float alpha)
        {
            _presence = Mathf.Clamp01(alpha);
            ApplyPresence();
        }

        void ApplyPresence()
        {
            var effective = BodyHidden && !IgnoreBodyHide ? 0f : _presence;
            if (effective <= 0.001f)
            {
                SetRenderersEnabled(false);
                return;
            }

            SetRenderersEnabled(true);
            if (!Mathf.Approximately(effective, _appliedAlpha)) ApplyAlpha(effective);
        }

        void SetRenderersEnabled(bool on)
        {
            if (_renderersOff == !on) return;
            _renderersOff = !on;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        void ApplyAlpha(float alpha)
        {
            _appliedAlpha = alpha;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (_shadowBlob != null && r.transform == _shadowBlob) continue;   // handled below, stays blended
                // the avatar's own material instances are edited in place (r.materials cloned them, orphaning the tracked
                // originals: a leak per presence change); anything else keeps the per-renderer clone
                foreach (var m in AllOwned(r) ? r.sharedMaterials : r.materials)
                {
                    if (m == null) continue;
                    var translucent = alpha < 0.999f;
                    if (m.HasProperty("_Surface")) m.SetFloat("_Surface", translucent ? 1f : 0f);
                    if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", translucent ? 0f : 1f);
                    if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)(translucent ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
                    if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(translucent ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
                    if (translucent) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (m.HasProperty("_BaseColor")) { var c = m.GetColor("_BaseColor"); c.a = alpha; m.SetColor("_BaseColor", c); }
                    if (m.HasProperty("_Color")) { var c = m.GetColor("_Color"); c.a = alpha; m.SetColor("_Color", c); }
                    if (translucent) m.renderQueue = Mathf.Max(m.renderQueue, 3000);
                }
            }
            // the ground blob is always translucent; it only follows the presence proportionally
            if (_shadowBlob != null)
            {
                var br = _shadowBlob.GetComponent<Renderer>();
                if (br != null && br.material != null)
                {
                    var bm = br.material;
                    var c = bm.HasProperty("_BaseColor") ? bm.GetColor("_BaseColor") : bm.color;
                    c.a = 0.45f * alpha;
                    if (bm.HasProperty("_BaseColor")) bm.SetColor("_BaseColor", c);
                    if (bm.HasProperty("_Color")) bm.SetColor("_Color", c);
                }
            }
        }

        static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            var mat = r.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        /// <summary>Tint every submesh material of a part (garments carry one material per submesh).</summary>
        static void TintAll(Renderer r, Color c)
        {
            if (r == null) return;
            foreach (var mat in r.materials)
            {
                if (mat == null) continue;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }
        }

        /// <summary>
        /// Stop the idle / walk graph so another controller can own the Animator (the AI opponent duelist runs its own
        /// clip graph and IK on this body). The look, materials and presence rules stay.
        /// </summary>
        public void DisableLocomotion()
        {
            if (_graph.IsValid()) _graph.Destroy();
            ProceduralBreathing = false;   // the caller poses the body itself (AI duelist clips + IK); blinking stays
        }

        public SkinnedMeshRenderer BodyRenderer => _smr;

        /// <summary>
        /// Edit-mode previews (Assets/Editor/WRLDZ/AvatarAssetPreview.cs): advance the idle / walk blend by
        /// <paramref name="dt"/> yourself, because the Player loop that normally drives the graph is not running.
        /// </summary>
        public void EvaluatePreview(float dt)
        {
            if (_graph.IsValid())
            {
                var target = Mathf.Clamp01(Speed / WalkSpeed);
                _walkWeight = Mathf.MoveTowards(_walkWeight, target, Blend * dt);
                _mixer.SetInputWeight(0, 1f - _walkWeight);
                _mixer.SetInputWeight(1, _walkWeight);
                _graph.Evaluate(dt);
            }
            ProceduralTick(dt);
        }

        // ---------------------------------------------------------------- procedural idle (2026-10-04): breathing, blinking, walk lean
        // The bases have no facial blendshapes and the UAL idle is a plain loop, so the life signs are layered on top of
        // the evaluated pose every frame: a chest / shoulder breath (world-axis additive, so it is right for any bone
        // orientation and for the grafted hair / clothes, which share the bones), a small head drift, a lean into the walk,
        // and a blink drawn as a skin-coloured eyelid card over the painted eyes for ~0.1 s. Every instance has its own
        // seed, period and blink schedule, so a crowd never breathes or blinks in step.
        /// <summary>Breath and head drift on top of the clips. Off for bodies another system poses (AI duelists).</summary>
        public bool ProceduralBreathing = true;
        /// <summary>Blink card over the eyes (base faces and face decals; never for silhouettes or sourced characters).</summary>
        public bool ProceduralBlink = true;
        float _idleT, _idleSeed, _breathPeriod = 4.2f;
        float _nextBlink = 1.5f, _blinkT = -1f;
        bool _doubleBlink;
        System.Random _idleRng;
        bool _idleBonesIndexed;
        Transform _chestBone, _lShoulderBone, _rShoulderBone, _headBone;
        GameObject _blinkQuad;
        Renderer _blinkRenderer;
        Texture2D _blinkTex;
        string _blinkSkinHex;
        AvatarAppearance _lastLook;
        const float BlinkClose = 0.05f, BlinkHold = 0.05f, BlinkOpen = 0.07f;

        void IndexIdleBones()
        {
            _idleBonesIndexed = true;
            var anim = GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return;
            _chestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            _lShoulderBone = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _rShoulderBone = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
            _headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            if (_idleRng == null)
            {
                // a stable per-instance seed: instance id (differs per spawn), so two duelists never sync up
                _idleRng = new System.Random(GetEntityId().GetHashCode());
                _idleSeed = (float)_idleRng.NextDouble() * 10f;
                _breathPeriod = 3.6f + (float)_idleRng.NextDouble() * 1.2f;        // 3.6 .. 4.8 s per breath
                _nextBlink = 0.8f + (float)_idleRng.NextDouble() * 3f;
            }
        }

        void LateUpdate() => ProceduralTick(Time.deltaTime);

        void ProceduralTick(float dt)
        {
            if (dt <= 0f) return;
            ProceduralTickBody(dt);
            if (_hatClipOn) PushHatClip();
        }

        void ProceduralTickBody(float dt)
        {
            if (!_idleBonesIndexed) IndexIdleBones();
            _idleT += dt;
            if (ProceduralBreathing && !_renderersOff)
            {
                var ph = (_idleT + _idleSeed) / _breathPeriod * (2f * Mathf.PI);
                var b = Mathf.Sin(ph);                                 // -1 exhale .. +1 inhale, eased by nature
                var right = transform.right; var up = transform.up;
                var walk = _graph.IsValid() ? _walkWeight : 0f;
                // Rotations only: the Humanoid Animator rewrites every bone's rotation each frame, so an additive
                // rotation never accumulates, while a bone translation would (it writes positions for the hips alone).
                if (_chestBone != null)
                {
                    // inhale: the chest lifts and opens (pitch back ~1.4 deg); walking adds a lean into the step
                    var pitch = -1.4f * b + 3.0f * walk;
                    _chestBone.rotation = Quaternion.AngleAxis(pitch, right) * _chestBone.rotation;
                }
                // shoulders roll up a little on the inhale (about the body's forward axis, mirrored per side)
                var fwdAxis = transform.forward;
                var roll = 1.1f * (b + 1f) * 0.5f;
                if (_lShoulderBone != null) _lShoulderBone.rotation = Quaternion.AngleAxis(-roll, fwdAxis) * _lShoulderBone.rotation;
                if (_rShoulderBone != null) _rShoulderBone.rotation = Quaternion.AngleAxis(roll, fwdAxis) * _rShoulderBone.rotation;
                if (_headBone != null)
                {
                    // a slow, unsynchronised head drift (nod and look-about), smaller while walking
                    var k = 1f - 0.6f * walk;
                    var nod = 0.7f * Mathf.Sin(ph * 0.5f + 1.3f) * k;
                    var turn = 1.1f * Mathf.Sin((_idleT + _idleSeed) * 0.37f + 2.1f) * k;
                    _headBone.rotation = Quaternion.AngleAxis(nod, right) * Quaternion.AngleAxis(turn, up) * _headBone.rotation;
                }
            }
            TickBlink(dt);
        }

        void TickBlink(float dt)
        {
            var allowed = ProceduralBlink && !_fullCharacter && !_silhouette && _lastLook != null && _headBone != null && !_renderersOff;
            if (!allowed)
            {
                if (_blinkRenderer != null) _blinkRenderer.enabled = false;
                return;
            }
            var closed = false;
            if (_blinkT < 0f)
            {
                _nextBlink -= dt;
                if (_nextBlink <= 0f) _blinkT = 0f;
            }
            else
            {
                _blinkT += dt;
                var total = BlinkClose + BlinkHold + BlinkOpen;
                closed = _blinkT >= BlinkClose * 0.5f && _blinkT <= total - BlinkOpen * 0.5f;
                if (_blinkT > total)
                {
                    _blinkT = -1f;
                    if (!_doubleBlink && _idleRng != null && _idleRng.NextDouble() < 0.18)
                    {
                        _doubleBlink = true; _nextBlink = 0.16f;                       // the occasional double blink
                    }
                    else
                    {
                        _doubleBlink = false;
                        _nextBlink = 2.2f + (float)(_idleRng?.NextDouble() ?? 0.5) * 4.3f;   // 2.2 .. 6.5 s apart
                    }
                }
            }
            if (closed && _blinkQuad == null) BuildBlinkQuad();
            if (_blinkRenderer != null) _blinkRenderer.enabled = closed;
        }

        /// <summary>
        /// The eyelid card: a feathered patch in the look's skin tone with two closed-lid lash lines, on the same head-bone
        /// frame as the face decals (eyes ~3.2 cm above the head bone, face surface ~12 cm ahead), drawn just in front of
        /// a face decal so a canon face blinks too.
        /// </summary>
        void BuildBlinkQuad()
        {
            var skin = SkinHexFor(_lastLook);
            if (_blinkTex == null || _blinkSkinHex != skin)
            {
                if (_blinkTex != null) Kill(_blinkTex);
                _blinkTex = MakeEyelidTexture(skin);
                _blinkSkinHex = skin;
            }
            var holder = new GameObject("BlinkDecal");
            holder.transform.SetParent(_headBone, false);
            _blinkQuad = holder;
            FaceQuad("blink", _blinkTex, _headBone, 0.031f, _sex == "female" ? 0.1085f : 0.1235f, new Vector2(0.136f, 0.046f), holder.transform);
            var q = holder.transform.Find("FaceDecal_blink");
            if (q == null) { Kill(holder); _blinkQuad = null; return; }
            q.name = "BlinkQuad";
            _blinkRenderer = q.GetComponent<Renderer>();
            if (_blinkRenderer != null)
            {
                _blinkRenderer.sharedMaterial.renderQueue = 2461;   // over a canon face decal (2460)
                _blinkRenderer.enabled = false;
            }
            SetLayerRecursive(holder, gameObject.layer);
        }

        /// <summary>Destroy that also works in edit-mode previews (plain Destroy is deferred there and leaves strays).</summary>
        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>Every material instance this avatar creates; only these are ever destroyed (never a project template).</summary>
        readonly HashSet<Material> _ownedMats = new();

        Material Own(Material m) { if (m != null) _ownedMats.Add(m); return m; }

        /// <summary>Destroy a renderer's material instances created by this avatar.</summary>
        void KillMaterials(Renderer r)
        {
            if (r == null) return;
            foreach (var m in r.sharedMaterials)
                if (m != null && _ownedMats.Remove(m)) Kill(m);
        }

        bool AllOwned(Renderer r)
        {
            foreach (var m in r.sharedMaterials) if (m != null && !_ownedMats.Contains(m)) return false;
            return true;
        }

        static Texture2D MakeEyelidTexture(string skinHex)
        {
            const int W = 256, H = 96;
            if (!ColorUtility.TryParseHtmlString(skinHex, out var skin)) skin = new Color(0.98f, 0.78f, 0.58f);
            var shade = skin * 0.82f; shade.a = 1f;
            var lash = new Color(0.13f, 0.08f, 0.09f, 1f);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "BlinkEyelid", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var u = (x + 0.5f) / W; var v = (y + 0.5f) / H;
                // feathered oval patch
                var dx = (u - 0.5f) / 0.5f; var dy = (v - 0.5f) / 0.5f;
                var rr = dx * dx + dy * dy;
                var a = Mathf.Clamp01((1f - rr) / 0.35f);
                var c = skin;
                // two closed lids: a shallow downward lash arc per eye, with a soft shadow just under it
                for (var e = 0; e < 2; e++)
                {
                    var x0 = e == 0 ? 0.09f : 0.55f; var x1 = e == 0 ? 0.45f : 0.91f;
                    if (u < x0 || u > x1) continue;
                    var t = (u - x0) / (x1 - x0);
                    var yc = 0.52f - 0.07f * Mathf.Sin(Mathf.PI * t);                 // the lid line sags a little
                    var d = (v - yc) * H;                                                   // px from the line
                    var halfW = 3.2f * (0.55f + 0.45f * Mathf.Sin(Mathf.PI * t));       // thicker in the middle
                    var lashA = Mathf.Clamp01(halfW + 0.8f - Mathf.Abs(d));
                    if (d < 0f && d > -9f) c = Color.Lerp(c, shade, 0.35f * (1f + d / 9f));   // shadow under the lid
                    c = Color.Lerp(c, lash, lashA);
                }
                c.a = a;
                px[y * W + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        void BuildGraph()
        {
            var animator = GetComponent<Animator>() ?? gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            // Always animate: the procedural idle (ProceduralTick) adds rotations on top of the pose every frame and relies
            // on the Animator rewriting every bone first. With CullUpdateTransforms a body no camera sees (edit-mode
            // previews, a token scrolled off) stops being rewritten and the additions would pile up. 52 bones: cheap.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clips = Resources.LoadAll<AnimationClip>(ClipsRes);
            AnimationClip idle = null, walk = null;
            foreach (var c in clips)
            {
                // FBX takes arrive as "Rig|Idle_Loop" or "Rig|Rig|Idle_Loop" depending on the importer. Match the
                // take name exactly: the UAL also holds Swim_/Pistol_/Spell_Simple_/Crouch_/Sitting_Idle_Loop, and a
                // suffix match picked whichever came last (the token idled with its arms raised).
                var take = c.name.Substring(c.name.LastIndexOf('|') + 1);
                if (take == "Idle_Loop") idle = c;
                else if (take == "Walk_Loop") walk = c;
            }
            if (idle == null && walk == null) return;   // stays in bind pose; still shows the look

            _graph = PlayableGraph.Create("OverworldAvatar3d");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            if (idle != null) { var p = AnimationClipPlayable.Create(_graph, idle); _graph.Connect(p, 0, _mixer, 0); }
            if (walk != null) { var p = AnimationClipPlayable.Create(_graph, walk); _graph.Connect(p, 0, _mixer, 1); }
            _mixer.SetInputWeight(0, 1f); _mixer.SetInputWeight(1, 0f);
            var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        void Update()
        {
            if (_hasFaceTarget)
            {
                // world mode only: turn toward the walking vector. The blob is a child so it follows;
                // it is round, so the rotation is invisible on it.
                transform.localRotation = Quaternion.RotateTowards(
                    transform.localRotation, _faceTarget, TurnDegPerSec * Time.deltaTime);
            }
            if (!_graph.IsValid()) return;
            var target = Mathf.Clamp01(Speed / WalkSpeed);
            _walkWeight = Mathf.MoveTowards(_walkWeight, target, Blend * Time.deltaTime);
            _mixer.SetInputWeight(0, 1f - _walkWeight);
            _mixer.SetInputWeight(1, _walkWeight);
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_blinkTex != null) Kill(_blinkTex);
            for (var i = 0; i < _clothMats.Count; i++) if (_clothMats[i] != null) Kill(_clothMats[i]);
            _clothMats.Clear();
            if (_hideComposite != null) Kill(_hideComposite);
            _hideComposite = null;
            // every other instance this avatar made (hair, outfit, face decal, silhouette, shadow blob, full-character skins)
            foreach (var m in _ownedMats) if (m != null) Kill(m);
            _ownedMats.Clear();
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }
}
