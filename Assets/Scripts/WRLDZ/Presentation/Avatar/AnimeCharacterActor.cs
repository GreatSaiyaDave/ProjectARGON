using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation.Avatar
{
    /// <summary>
    /// A 2D anime character displayed in 3D space on a camera-facing billboard plane.
    /// The sprite is cel-shaded (AnimeCelShader) so it reads as anime in the world —
    /// the player experiences the character as if watching the show. The actor handles
    /// billboard orientation, camera-angle sprite switching (front/3-4/side/back),
    /// idle breathing animation, expression swapping, and gesture overlays.
    ///
    /// Sprites are loaded from Resources/WRLDZ/AnimeCharacters/{id}/{angle}.png.
    /// A per-set depth+normal map ({id}_normal.png: RGB normal, A depth) makes the flat
    /// art EXIST IN 3D — the cel terminator follows the body (not the whole quad) and the
    /// sprite parallaxes with the camera. The AnimeCelShader applies stepped lighting +
    /// rim light + transparent cutout; no ink outline.
    /// </summary>
    public sealed class AnimeCharacterActor : MonoBehaviour
    {
        const string CelMatRes = "WRLDZ/Avatar3D/Materials/AnimeCel";
        const string QuadPref = "WRLDZ/AnimeCharacters/";

        /// <summary>Per-set form map: RGB = normal, A = depth (build_normal_depth.py).</summary>
        const string NormalSuffix = "_normal";

        /// <summary>World-space height of the character in metres (Yami Yugi 1.58, Kaiba 1.86, etc.).</summary>
        public float HeightM = 1.75f;

        /// <summary>Current camera angle category.</summary>
        public AnimeCharacterCatalog.CameraAngle CurrentAngle { get; private set; } = AnimeCharacterCatalog.CameraAngle.Front;

        /// <summary>Current expression variant.</summary>
        public string CurrentExpression { get; private set; } = "neutral";

        /// <summary>True while a gesture overlay is playing.</summary>
        public bool IsGesturing { get; private set; }

        AnimeCharacterSet _set;
        GameObject _quad;
        MeshRenderer _renderer;
        Material _material;
        Texture2D _currentSprite;
        Texture2D _normalMap;
        Camera _camera;
        float _baseY;
        float _idleTime;
        float _angleSwitchCooldown;
        Vector3 _lastCameraPos;
        bool _visible = true;

        /// <summary>Per-instance idle seed so nearby characters don't breathe in sync.</summary>
        float _idleSeed;

        public static AnimeCharacterActor Attach(Transform parent, string characterId, int layer)
        {
            var set = AnimeCharacterCatalog.Find(characterId);
            if (set == null || parent == null) return null;

            var go = new GameObject("AnimeCharacter_" + characterId);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            SetLayerRecursive(go, layer);

            var actor = go.AddComponent<AnimeCharacterActor>();
            actor._set = set;
            actor.HeightM = set.heightM;
            actor.Build();
            return actor;
        }

        void Build()
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "SpriteQuad";
            _quad.transform.SetParent(transform, false);
            _quad.transform.localPosition = new Vector3(0f, HeightM * 0.5f, 0f);
            // Unity's Quad faces -Z: at identity the billboard shows the camera its BACK, which
            // mirror-flips the art (ArPhysicalCardBuilder.cs hit the same thing) and points the
            // mesh normal AWAY from the viewer — so every form-map pixel lit backwards: replayed
            // against the shader maths, 53% of the figure sat in the shadow band with 83% of the
            // right third dark. Turned 180° so the art reads correctly and normalWS faces the
            // camera, which is the frame build_formmap_v2.py authors the maps in.
            _quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _quad.transform.localScale = new Vector3(HeightM * 0.6f, HeightM, 1f);
            Destroy(_quad.GetComponent<Collider>());

            _renderer = _quad.GetComponent<MeshRenderer>();
            var tpl = Resources.Load<Material>(CelMatRes);
            _material = tpl != null ? new Material(tpl) : new Material(Shader.Find("WRLDZ/AnimeCel"));
            _material.name = "AnimeCel_" + _set.id;
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            _quad.layer = gameObject.layer;
            _baseY = _quad.transform.localPosition.y;
            _idleSeed = Random.value * 100f;
            _lastCameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            LoadAngleSprite(AnimeCharacterCatalog.CameraAngle.Front);
            ApplyFormMap();
            BuildShadow();
        }

        /// <summary>Load this set's depth+normal map so the 2D art is lit as 3D form. When the map
        /// is missing the sprite stays flat (no form light, no parallax) rather than break.</summary>
        void ApplyFormMap()
        {
            if (_material == null || _set == null) return;
            var tex = Resources.Load<Texture2D>(QuadPref + _set.id + "/" + _set.id + NormalSuffix);
            if (tex == null)
            {
                if (_material.HasProperty("_NormalStrength")) _material.SetFloat("_NormalStrength", 0f);
                if (_material.HasProperty("_Parallax")) _material.SetFloat("_Parallax", 0f);
                if (_material.HasProperty("_KeyBias")) _material.SetFloat("_KeyBias", 1f);
                return;
            }
            _normalMap = tex;
            if (_material.HasProperty("_NormalMap")) _material.SetTexture("_NormalMap", tex);

            // Duelist Kingdom renders: form maps authored by build_formmap_v2.py, which models the
            // body as local cylinders instead of a silhouette pillow, so they are meant to be lit at
            // full strength. They also need the lateral portrait key — a camera-facing plane under a
            // frontal scene key measures 0% of the body in shadow (totally flat), and 78% under a
            // backlight; at 0.15 bias the split stays in a healthy 15-37% in every scene while still
            // following the world light. Every other set keeps the material defaults unchanged.
            if (_set.id.EndsWith("_da"))
            {
                SetFormLighting(1f, 0.03f);
                if (_material.HasProperty("_KeyBias")) _material.SetFloat("_KeyBias", 0.15f);
            }
        }

        /// <summary>Tune the 3D form: how strongly the normal map lights the body (0 = flat
        /// billboard, 1 = full form) and how far the depth map parallaxes with the camera.</summary>
        public void SetFormLighting(float normalStrength, float parallax)
        {
            if (_material == null || _normalMap == null) return;
            if (_material.HasProperty("_NormalStrength")) _material.SetFloat("_NormalStrength", Mathf.Clamp01(normalStrength));
            if (_material.HasProperty("_Parallax")) _material.SetFloat("_Parallax", Mathf.Clamp(parallax, 0f, 0.05f));
        }

        void BuildShadow()
        {
            var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blob.name = "ShadowBlob";
            blob.transform.SetParent(transform, false);
            blob.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float d = _set != null ? _set.shadowDiameter : 0.9f;
            blob.transform.localScale = new Vector3(d, d, 1f);
            blob.layer = gameObject.layer;
            Destroy(blob.GetComponent<Collider>());

            var mr = blob.GetComponent<MeshRenderer>();
            var m = new Material(Shader.Find("Unlit/Transparent") != null ? Shader.Find("Unlit/Transparent") : Shader.Find("Sprites/Default"));
            m.name = "AnimeShadow_" + _set.id;
            var c = new Color(0.02f, 0.02f, 0.06f, 0.45f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Load and apply a camera-angle sprite. Falls back to front when the angle texture is missing.</summary>
        public void LoadAngleSprite(AnimeCharacterCatalog.CameraAngle angle)
        {
            if (_material == null || _set == null) return;
            var name = AnimeCharacterCatalog.AngleSprite(_set, angle);
            if (string.IsNullOrEmpty(name)) name = _set.angles.front;
            var path = QuadPref + _set.id + "/" + name;
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null && angle != AnimeCharacterCatalog.CameraAngle.Front)
            {
                name = _set.angles.front;
                tex = Resources.Load<Texture2D>(QuadPref + _set.id + "/" + name);
            }
            if (tex == null) return;
            _currentSprite = tex;
            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", tex);
            else _material.mainTexture = tex;
            CurrentAngle = angle;
        }

        /// <summary>Swap the face expression variant.</summary>
        public void SetExpression(string expression)
        {
            if (_material == null || _set == null) return;
            CurrentExpression = expression;
            var name = _set.expressions.neutral;
            if (expression == "serious" && !string.IsNullOrEmpty(_set.expressions.serious)) name = _set.expressions.serious;
            else if (expression == "smiling" && !string.IsNullOrEmpty(_set.expressions.smiling)) name = _set.expressions.smiling;
            if (string.IsNullOrEmpty(name)) return;
            var path = QuadPref + _set.id + "/expr_" + name;
            var tex = Resources.Load<Texture2D>(path);
            if (tex != null)
            {
                if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", tex);
                else _material.mainTexture = tex;
            }
        }

        /// <summary>Play a gesture overlay (draw, summon, attack, defeat) for a duration, then return to the angle sprite.</summary>
        public void PlayGesture(string gestureName, float duration = 1.5f)
        {
            if (_material == null || _set == null || IsGesturing) return;
            var name = "";
            switch (gestureName)
            {
                case "draw": name = _set.gestures.draw; break;
                case "summon": name = _set.gestures.summon; break;
                case "attack": name = _set.gestures.attack; break;
                case "defeat": name = _set.gestures.defeat; break;
                case "idle": name = _set.gestures.idle; break;
            }
            if (string.IsNullOrEmpty(name)) return;
            var path = QuadPref + _set.id + "/gesture_" + name;
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return;
            StartCoroutine(PlayGestureRoutine(tex, duration));
        }

        System.Collections.IEnumerator PlayGestureRoutine(Texture2D gestureTex, float duration)
        {
            IsGesturing = true;
            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", gestureTex);
            else _material.mainTexture = gestureTex;
            yield return new WaitForSeconds(duration);
            LoadAngleSprite(CurrentAngle);
            IsGesturing = false;
        }

        /// <summary>Toggle visibility (duel mode, zone mode, etc.).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_quad != null) _quad.SetActive(visible);
        }

        /// <summary>Set the cel-shading shadow band depth and rim light intensity.</summary>
        public void SetShading(float shadowAmt, float rimAmt)
        {
            if (_material == null) return;
            if (_material.HasProperty("_ShadowAmt")) _material.SetFloat("_ShadowAmt", shadowAmt);
            if (_material.HasProperty("_RimAmt")) _material.SetFloat("_RimAmt", rimAmt);
        }

        void Update()
        {
            if (!_visible || _quad == null) return;
            _camera = Camera.main;
            if (_camera == null) return;

            Billboard();
            IdleBreath();
            SwitchAngle();
        }

        void Billboard()
        {
            var camPos = _camera.transform.position;
            var dir = camPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        void IdleBreath()
        {
            _idleTime += Time.deltaTime;
            var t = _idleTime + _idleSeed;
            var breath = Mathf.Sin(t * 1.8f) * 0.008f;
            var sway = Mathf.Sin(t * 0.9f) * 0.003f;
            _quad.transform.localPosition = new Vector3(sway, _baseY + breath, 0f);
        }

        void SwitchAngle()
        {
            _angleSwitchCooldown -= Time.deltaTime;
            if (_angleSwitchCooldown > 0f) return;
            _angleSwitchCooldown = 0.15f;

            var camPos = _camera.transform.position;
            if (Vector3.SqrMagnitude(camPos - _lastCameraPos) < 0.001f) return;
            _lastCameraPos = camPos;

            var camDir = camPos - transform.position;
            var camAzimuth = Mathf.Atan2(camDir.x, camDir.z);
            var myAzimuth = Mathf.Atan2(transform.forward.x, transform.forward.z);
            var relativeAzimuth = camAzimuth - myAzimuth;

            var angle = AnimeCharacterCatalog.AngleFor(relativeAzimuth);
            if (angle != CurrentAngle) LoadAngleSprite(angle);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_currentSprite != null) Resources.UnloadAsset(_currentSprite);
            if (_normalMap != null && _normalMap != _currentSprite) Resources.UnloadAsset(_normalMap);
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
