using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Resolves 3D assets for cards on the hologram arena field.
    /// Lookup order:
    ///   StreamingAssets/Models/Cards/{id}.glb  — per-shell relief + {id}.shells.json rig (preferred)
    ///   StreamingAssets/Models/Cards/{id}.obj  — single static mesh
    ///   Fallback: card-art billboard plane (always works today)
    /// </summary>
    public static class CardModelCatalog
    {
        public const string CardsRelativeDir = "Models/Cards";

        static readonly Dictionary<int, Mesh> MeshCache = new();
        static Mesh _billboardQuad;

        /// <summary>
        /// Disk folder for Editor tooling (GlbShellSmoke). Runtime reads never use it: on Android it is
        /// a jar: URL. They go through <see cref="StreamingFiles"/> with <see cref="CardsRelativeDir"/>.
        /// </summary>
        public static string CardsDir =>
            Path.Combine(Application.streamingAssetsPath, CardsRelativeDir);

        static string Rel(int cardId, string ext) => CardsRelativeDir + "/" + cardId + ext;

        public static bool HasDedicatedModel(int cardId) =>
            HasShellModel(cardId) || StreamingFiles.Exists(Rel(cardId, ".obj"));

        public static bool HasShellModel(int cardId) =>
            StreamingFiles.Exists(Rel(cardId, ".glb"));

        /// <summary>
        /// Life-size hologram height in meters for a card. Reads <c>lifesize_m</c> from
        /// {id}.shells.json; the ygo-recon pipeline authors 1 unit = 1 meter, so a missing
        /// or non-positive value falls back to 1 m. No canon height is ever invented here.
        /// </summary>
        public static float GetLifesizeHeight(int cardId)
        {
            if (StreamingFiles.TryReadAllText(Rel(cardId, ".shells.json"), out var manJson))
            {
                try
                {
                    var manifest = JsonUtility.FromJson<ShellRig.Manifest>(manJson);
                    if (manifest != null && manifest.lifesize_m > 0.01f) return manifest.lifesize_m;
                }
                catch (System.Exception) { }
            }
            return 1f;
        }

        /// <summary>
        /// Instantiate the per-shell relief model for a card under <paramref name="parent"/>:
        /// GLB shells (sword / body / cape ...) as separate renderers, unlit + alpha-clipped
        /// via the anime presentation, normalised to <paramref name="targetHeight"/> with the
        /// feet on the parent's origin, plus the spring-bone / capsule rig from
        /// {id}.shells.json. Returns null when there is no GLB.
        /// </summary>
        public static ShellRig InstantiateShellModel(int cardId, Transform parent, Color tint, float targetHeight = 1f)
        {
            var entry = AcquireShellTemplate(cardId);
            if (entry == null) return null;
            var createdMats = new List<Material>();
            var model = GlbShellLoader.Instantiate(entry.Template, "MonsterShells_" + cardId, tex =>
            {
                var m = MakeShellMaterial(tex, tint);
                createdMats.Add(m);
                return m;
            });
            if (model == null) return null;
            entry.Live++;
            entry.LastUse = ++_shellUseClock;
            // Owner frees what this instance created (materials, spring-chain skinned mesh copies) and releases the
            // shared template when the relief is destroyed with its arena visual.
            model.Root.AddComponent<ShellModelOwner>().Init(cardId, entry, createdMats);

            var root = model.Root.transform;
            root.SetParent(parent, false);
            var b = model.LocalBounds;
            var h = b.size.y > 0.0001f ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            var s = h > 0.0001f ? targetHeight / h : 1f;
            root.localScale = Vector3.one * s;
            // feet on the origin, centred in X, relief centred on the card plane
            root.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            foreach (var part in model.Parts)
            {
                ArAnimePresentation.ConfigureHoloRenderer(part.Renderer);
                part.Go.layer = parent.gameObject.layer;
            }

            ShellRig.Manifest manifest = null;
            if (StreamingFiles.TryReadAllText(Rel(cardId, ".shells.json"), out var manJson))
            {
                try { manifest = JsonUtility.FromJson<ShellRig.Manifest>(manJson); }
                catch (System.Exception e) { Debug.LogWarning("[WRLDZ] shells.json unreadable for " + cardId + ": " + e.Message); }
            }
            var rig = ShellRig.Build(model, manifest);
            // Give every shell model its state machine. Idle is a breath -- a couple of percent of
            // travel -- so attaching it by default changes nothing visually until a caller asks
            // for Float / Fly / Defense / Activate / Hit via rig.GetComponent<ShellPoser>().Set().
            ShellPoser.Attach(rig, model);
            return rig;
        }

        // ── GLB shell cache ──────────────────────────────────────────────────────────────────────────
        // One parsed GLB (meshes + non-readable textures) per card id, shared by every summon of that card.
        // Before this every summon re-read + re-parsed the GLB on the main thread and leaked 4-50 MB of
        // meshes / textures / materials until the next scene load. Templates in use are never freed; idle
        // ones stay warm up to ShellIdleCapacity (a re-summon is free) and all idle ones are dropped at
        // duel end (ReleaseIdleShellTemplates, called from ArDuelSpace.OnDestroy).

        internal sealed class ShellCacheEntry
        {
            public int CardId;
            public GlbShellLoader.Template Template;
            public int Live;
            public int LastUse;
        }

        /// <summary>Idle (no live instance) shell templates kept for fast re-summon.</summary>
        public const int ShellIdleCapacity = 6;

        static readonly Dictionary<int, ShellCacheEntry> ShellCache = new();
        static int _shellUseClock;
        /// <summary>Set at duel end: instances destroyed afterwards free their template immediately.</summary>
        static bool _shellDrain;

        static ShellCacheEntry AcquireShellTemplate(int cardId)
        {
            _shellDrain = false;
            if (ShellCache.TryGetValue(cardId, out var e) && e.Template != null && !e.Template.Destroyed)
                return e;

            var glb = Rel(cardId, ".glb");
            if (!StreamingFiles.Exists(glb)) return null;
            var template = GlbShellLoader.LoadTemplateFromStreaming(glb);
            if (template == null) return null;
            e = new ShellCacheEntry { CardId = cardId, Template = template };
            ShellCache[cardId] = e;
            return e;
        }

        internal static void ReleaseShellInstance(ShellCacheEntry entry)
        {
            if (entry == null) return;
            entry.Live = Mathf.Max(0, entry.Live - 1);
            TrimShellCache(_shellDrain ? 0 : ShellIdleCapacity);
        }

        /// <summary>
        /// Duel end / scene change: free every shell template with no live instance now, and the rest as their
        /// (deferred-destroyed) instances go away, until the next summon warms the cache again.
        /// </summary>
        public static void ReleaseIdleShellTemplates()
        {
            _shellDrain = true;
            TrimShellCache(0);
        }

        static void TrimShellCache(int keepIdle)
        {
            var idle = new List<ShellCacheEntry>();
            foreach (var e in ShellCache.Values)
                if (e.Live <= 0) idle.Add(e);
            if (idle.Count <= keepIdle) return;
            idle.Sort((a, b) => a.LastUse.CompareTo(b.LastUse));
            for (var i = 0; i < idle.Count - keepIdle; i++)
            {
                var e = idle[i];
                e.Template?.DestroyAssets();
                ShellCache.Remove(e.CardId);
            }
        }

        /// <summary>Shell material: the anime unlit face material plus a hard alpha clip so
        /// the relief's transparent margin never draws (the Blender export is alphaMode MASK).</summary>
        public static Material MakeShellMaterial(Texture2D tex, Color tint)
        {
            var mat = ArAnimePresentation.MakeFaceMaterial(tex != null ? tex : Texture2D.whiteTexture, tint, "MonsterShellMat");
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f); // opaque + clip
            // A relief shell is a thin sheet: render both faces so winding never matters
            // (Blender previews are double-sided; Unity's default culls back faces).
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            return mat;
        }

        public static Mesh GetMesh(int cardId)
        {
            if (MeshCache.TryGetValue(cardId, out var cached) && cached != null)
                return cached;

            var objPath = Rel(cardId, ".obj");
            if (StreamingFiles.Exists(objPath))
            {
                var m = ObjMeshLoader.LoadFromStreaming(objPath, "Card_" + cardId);
                if (m != null)
                {
                    // Normalize to ~1 unit tall for arena slots
                    NormalizeMeshHeight(m, 1f);
                    MeshCache[cardId] = m;
                    return m;
                }
            }

            return GetBillboardQuad();
        }

        public static bool IsBillboardFallback(Mesh mesh) =>
            mesh != null && mesh == _billboardQuad;

        static Mesh GetBillboardQuad()
        {
            if (_billboardQuad != null) return _billboardQuad;
            _billboardQuad = new Mesh
            {
                name = "CardBillboard",
                vertices = new[]
                {
                    new Vector3(-0.35f, 0f, 0f),
                    new Vector3(0.35f, 0f, 0f),
                    new Vector3(0.35f, 1f, 0f),
                    new Vector3(-0.35f, 1f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(1, 1), new Vector2(0, 1)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            _billboardQuad.RecalculateNormals();
            _billboardQuad.RecalculateBounds();
            return _billboardQuad;
        }

        static void NormalizeMeshHeight(Mesh mesh, float targetHeight)
        {
            var b = mesh.bounds;
            var h = b.size.y;
            if (h < 0.0001f) h = Mathf.Max(b.size.x, b.size.z);
            if (h < 0.0001f) return;
            var s = targetHeight / h;
            var verts = mesh.vertices;
            var c = b.center;
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i] - c;
                v *= s;
                // sit on ground
                v.y += targetHeight * 0.5f;
                verts[i] = v;
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>
        /// Card / holo face material — anime unlit + AR exposure fit.
        /// Prefer Unlit over Lit: outdoor/variable AR has no reliable probes; Lit goes black.
        /// Exposure via <see cref="ArAnimePresentation"/> keeps holos readable noon→night.
        /// </summary>
        public static Material MakeArtMaterial(Sprite art, Color tint, bool emissive = true)
        {
            Texture tex = art != null && art.texture != null ? art.texture : Texture2D.whiteTexture;
            return ArAnimePresentation.MakeFaceMaterial(tex, tint, "CardModelMat");
        }
    }

    /// <summary>
    /// Lifetime owner on a shell relief root (<see cref="CardModelCatalog.InstantiateShellModel"/>). On destroy it
    /// frees what this instance created — its per-part materials and the skinned mesh copies ShellSpringChain made —
    /// and releases its reference on the shared, cached GLB template (whose meshes / textures it never destroys).
    /// </summary>
    public sealed class ShellModelOwner : MonoBehaviour
    {
        int _cardId;
        CardModelCatalog.ShellCacheEntry _entry;
        readonly List<Material> _materials = new();
        bool _released;

        public int CardId => _cardId;

        internal void Init(int cardId, CardModelCatalog.ShellCacheEntry entry, List<Material> materials)
        {
            _cardId = cardId;
            _entry = entry;
            if (materials != null) _materials.AddRange(materials);
        }

        void OnDestroy()
        {
            if (_released) return;
            _released = true;
            var template = _entry?.Template;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr != null ? smr.sharedMesh : null;
                if (m != null && (template == null || !template.Owns(m)))
                    ArInteraction.ArObjectUtil.Destroy(m);
            }

            foreach (var mat in _materials) ArInteraction.ArObjectUtil.Destroy(mat);
            _materials.Clear();
            CardModelCatalog.ReleaseShellInstance(_entry);
            _entry = null;
        }
    }
}
