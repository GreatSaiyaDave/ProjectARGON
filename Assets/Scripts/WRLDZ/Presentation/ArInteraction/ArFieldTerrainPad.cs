using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Duelist Kingdom terrain under a monster while a Field Spell is live: a
    /// soft disc of the field's own ground (baked from its illustration by
    /// <see cref="ArFieldSpellFloor"/>) instead of a floor carpet across the street.
    /// Face-up monsters the field changed get an aura column — accent colour
    /// rising for a boon, dim and low for a bane — read from the engine's
    /// <see cref="CardInstance.FieldAtkDelta"/> / <see cref="CardInstance.FieldDefDelta"/>,
    /// never from a Type table here. The cyan/magenta ownership ring stays on top.
    /// </summary>
    [DefaultExecutionOrder(810)]
    public class ArFieldTerrainPad : MonoBehaviour
    {
        const float PadDiameterMul = 1.45f;
        const float PadAlpha = 0.62f;
        const float PadLift = 0.004f;
        const float AuraHeight = 0.55f;
        const float BaneHeight = 0.3f;
        const float RampSeconds = 0.35f;
        const int AuraSegs = 24;

        static Mesh _padMesh;
        static Mesh _auraMesh;
        static Material _padMat;
        static Material _auraMat;
        static int _padMatVersion = -1;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        ArArenaCardVisual _host;
        MeshRenderer _pad;
        MeshRenderer _aura;
        MaterialPropertyBlock _mpb;
        float _ramp;

        public static ArFieldTerrainPad Ensure(ArArenaCardVisual host)
        {
            if (host == null) return null;
            var existing = host.GetComponentInChildren<ArFieldTerrainPad>(true);
            if (existing != null)
            {
                existing._host = host;
                return existing;
            }

            var go = new GameObject("FieldTerrainPad");
            go.transform.SetParent(host.transform, false);
            go.layer = host.gameObject.layer;
            var p = go.AddComponent<ArFieldTerrainPad>();
            p._host = host;
            p.Build();
            return p;
        }

        void Build()
        {
            _mpb = new MaterialPropertyBlock();
            EnsureShared();

            var d = ArPlaymatLayout.SummonRingDiameter * PadDiameterMul;
            _pad = MakeChild("Terrain", _padMesh, _padMat, new Vector3(d, 1f, d));
            var r = ArPlaymatLayout.SummonRingDiameter * 0.5f;
            _aura = MakeChild("FieldAura", _auraMesh, _auraMat, new Vector3(r, AuraHeight, r));
            _pad.enabled = false;
            _aura.enabled = false;
        }

        MeshRenderer MakeChild(string name, Mesh mesh, Material mat, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = scale;
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            return mr;
        }

        void LateUpdate()
        {
            if (_host == null) return;
            var card = _host.Card;
            var want = ArFieldSpellFloor.IsLive && _host.IsMonster && card != null && !_host.IsSpawning;
            _ramp = Mathf.MoveTowards(_ramp, want ? 1f : 0f, Time.unscaledDeltaTime / RampSeconds);

            // Stay flat on the street whatever pose the card takes (Set = pitched,
            // Defense = rolled, attacks lunge): ground-locked under the monster.
            var plane = _host.transform.parent;
            if (plane != null)
            {
                var local = plane.InverseTransformPoint(_host.transform.position);
                local.y = ArPlaymatLayout.MonsterHoverY + PadLift;
                transform.SetPositionAndRotation(plane.TransformPoint(local), plane.rotation);
            }

            var vis = _ramp * ArFieldSpellFloor.PresenceAt(transform.position);
            if (vis <= 0.001f)
            {
                if (_pad.enabled) _pad.enabled = false;
                if (_aura.enabled) _aura.enabled = false;
                return;
            }

            SyncSharedTexture();
            var env = ArFieldSpellFloor.Environment;
            _pad.enabled = ArFieldSpellFloor.TerrainTexture != null;
            SetTint(_pad, new Color(1f, 1f, 1f, PadAlpha * vis));

            // Hidden information: a face-down monster shows terrain, never an aura.
            var delta = card.FieldAtkDelta != 0 ? card.FieldAtkDelta : card.FieldDefDelta;
            if (!_host.FaceUp || delta == 0)
            {
                if (_aura.enabled) _aura.enabled = false;
                return;
            }

            var t = Time.unscaledTime;
            var r = ArPlaymatLayout.SummonRingDiameter * 0.5f;
            Color c;
            float h;
            if (delta > 0)
            {
                c = env.Accent;
                c.a = 0.5f * vis;
                h = AuraHeight * (1f + 0.12f * Mathf.Sin(t * 2.4f));
            }
            else
            {
                // Bane: the field's colour drained and low to the ground.
                c = Color.Lerp(env.Accent, new Color(0.35f, 0.33f, 0.42f), 0.7f);
                c.a = 0.4f * vis;
                h = BaneHeight * (0.8f + 0.2f * Mathf.Sin(t * 1.1f));
            }

            _aura.transform.localScale = new Vector3(r, h, r);
            SetTint(_aura, c);
            _aura.enabled = true;
        }

        void SetTint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, c);
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>All pads share one terrain texture; swap it once per environment.</summary>
        static void SyncSharedTexture()
        {
            if (_padMatVersion == ArFieldSpellFloor.EnvironmentVersion || _padMat == null) return;
            _padMatVersion = ArFieldSpellFloor.EnvironmentVersion;
            var tex = ArFieldSpellFloor.TerrainTexture;
            if (tex == null) return;
            if (_padMat.HasProperty("_MainTex")) _padMat.SetTexture("_MainTex", tex);
            if (_padMat.HasProperty("_BaseMap")) _padMat.SetTexture("_BaseMap", tex);
        }

        static void EnsureShared()
        {
            if (_padMesh == null) _padMesh = FlatQuad();
            if (_auraMesh == null) _auraMesh = OpenTube();
            if (_padMat == null)
            {
                _padMat = VertexColorMaterial("FieldTerrainPadMat", 2460);
                _padMatVersion = -1;
            }

            if (_auraMat == null) _auraMat = VertexColorMaterial("FieldAuraMat", 3060);
        }

        /// <summary>Sprites/Default: texture × vertex colour × _Color, alpha blended.</summary>
        static Material VertexColorMaterial(string name, int queue)
        {
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");
            var m = new Material(sh) { name = name, renderQueue = queue };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>Unit quad in XZ, facing up, centred on the origin.</summary>
        static Mesh FlatQuad()
        {
            var m = new Mesh { name = "FieldTerrainQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f)
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.colors32 = new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255)
            };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit-radius open cylinder, height 1; opaque at the base, clear at the top.</summary>
        static Mesh OpenTube()
        {
            var n = AuraSegs + 1;
            var v = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            var col = new Color32[n * 2];
            var tri = new int[AuraSegs * 6];
            for (var i = 0; i < n; i++)
            {
                var a = i / (float)AuraSegs * Mathf.PI * 2f;
                var x = Mathf.Cos(a);
                var z = Mathf.Sin(a);
                v[i] = new Vector3(x, 0f, z);
                v[i + n] = new Vector3(x, 1f, z);
                uv[i] = new Vector2(i / (float)AuraSegs, 0f);
                uv[i + n] = new Vector2(i / (float)AuraSegs, 1f);
                col[i] = new Color32(255, 255, 255, 255);
                col[i + n] = new Color32(255, 255, 255, 0);
            }

            var t = 0;
            for (var i = 0; i < AuraSegs; i++)
            {
                tri[t++] = i;
                tri[t++] = i + n;
                tri[t++] = i + 1;
                tri[t++] = i + 1;
                tri[t++] = i + n;
                tri[t++] = i + n + 1;
            }

            var m = new Mesh { name = "FieldAuraTube" };
            m.vertices = v;
            m.uv = uv;
            m.colors32 = col;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
