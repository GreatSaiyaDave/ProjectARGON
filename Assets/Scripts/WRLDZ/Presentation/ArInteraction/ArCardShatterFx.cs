using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Procedural card shatter: clones the card face into flying shards.
    /// Piece count from <see cref="CardShatterPresentation"/> (battle overwhelm / default effect).
    /// </summary>
    public class ArCardShatterFx : MonoBehaviour
    {
        static ArCardShatterFx _instance;
        int _layer = 28;

        public static ArCardShatterFx Ensure(Transform host = null, int layer = 28)
        {
            if (_instance != null)
            {
                _instance._layer = layer;
                return _instance;
            }

            var go = new GameObject("CardShatterFxHost");
            if (host != null) go.transform.SetParent(host, false);
            // DontDestroyOnLoad is play-mode only (EditorSim smoke runs in edit mode)
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<ArCardShatterFx>();
            _instance._layer = layer;
            return _instance;
        }

        /// <summary>
        /// Shatter a card-sized plane at world pose. Detaches visual immediately (caller destroys source).
        /// Edit mode / headless smoke: no-op (no coroutines).
        /// </summary>
        public static void Play(
            Vector3 worldPos,
            Quaternion worldRot,
            Vector3 worldScale,
            Texture faceTex,
            Color tint,
            in ShatterSpec spec,
            int layer = 28)
        {
            if (!Application.isPlaying)
            {
                Debug.Log(
                    $"[WRLDZ FX] Shatter skipped (edit mode) «{spec.CardName ?? "card"}»");
                return;
            }

            var fx = Ensure(null, layer);
            var pieces = CardShatterPresentation.PieceCount(spec);
            CardShatterPresentation.HoldForShatter(pieces);
            fx.StartCoroutine(fx.ShatterRoutine(
                worldPos, worldRot, worldScale, faceTex, tint, pieces, spec.IsBattle, spec.OverwhelmDiff));
            Debug.Log(
                $"[WRLDZ FX] Shatter «{spec.CardName ?? "card"}» pieces={pieces} " +
                $"battle={spec.IsBattle} overwhelm={spec.OverwhelmDiff:0}");
        }

        /// <summary>Shatter from an arena/disk visual, then destroy the GO.</summary>
        public static void PlayAndDestroyVisual(GameObject visual, in ShatterSpec spec, int layer = 28)
        {
            if (visual == null) return;
            if (Application.isPlaying)
            {
                Texture tex = null;
                Color tint = Color.white;
                TrySampleFace(visual, out tex, out tint);
                var t = visual.transform;
                Play(t.position, t.rotation, t.lossyScale, tex, tint, spec, layer);
            }

            ArObjectUtil.Destroy(visual);
        }

        /// <summary>
        /// Shatter only if rules queued a destroy for <paramref name="instanceId"/>;
        /// otherwise quiet-destroy (field refresh must not fake battle FX).
        /// Edit mode: drain queue + immediate destroy (no FX host).
        /// </summary>
        public static void ConsumeQueuedOrQuietDestroy(GameObject visual, int instanceId, int layer = 28)
        {
            if (visual == null) return;
            if (CardShatterPresentation.TryTake(instanceId, out var spec))
                PlayAndDestroyVisual(visual, spec, layer);
            else
                ArObjectUtil.Destroy(visual);
        }

        static void TrySampleFace(GameObject visual, out Texture tex, out Color tint)
        {
            tex = null;
            tint = Color.white;
            if (visual == null) return;
            var mrs = visual.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in mrs)
            {
                if (mr == null || mr.sharedMaterial == null) continue;
                var mat = mr.sharedMaterial;
                if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                {
                    tex = mat.GetTexture("_BaseMap");
                    if (mat.HasProperty("_BaseColor")) tint = mat.GetColor("_BaseColor");
                    return;
                }

                if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
                {
                    tex = mat.mainTexture;
                    if (mat.HasProperty("_Color")) tint = mat.color;
                    return;
                }
            }
        }

        IEnumerator ShatterRoutine(
            Vector3 origin,
            Quaternion orient,
            Vector3 scale,
            Texture faceTex,
            Color tint,
            int pieces,
            bool battle,
            float overwhelm)
        {
            pieces = Mathf.Clamp(pieces, 4, 64);
            var mat = ArFieldMaterials.CreateUnlitTextureDoubleSided(
                faceTex != null ? faceTex : Texture2D.whiteTexture);
            if (faceTex == null)
            {
                // Solid fallback — deep slate / gold edge feel
                mat = ArFieldMaterials.Get(new Color(0.15f, 0.12f, 0.2f, 1f));
            }
            else if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            }

            // Overwhelm flash (white → gold → red)
            var flashCol = battle
                ? Color.Lerp(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.2f, 0.1f, 1f),
                    Mathf.Clamp01(overwhelm / CardShatterPresentation.MaxOverwhelmDiff))
                : new Color(0.5f, 0.85f, 1f, 1f);

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "ShatterFlash";
            flash.layer = _layer;
            ArObjectUtil.Destroy(flash.GetComponent<Collider>());
            flash.transform.position = origin;
            flash.transform.localScale = Vector3.one * 0.08f;
            ArFieldMaterials.Apply(flash.GetComponent<MeshRenderer>(),
                new Color(flashCol.r, flashCol.g, flashCol.b, 0.95f));

            // Shard grid: roughly sqrt pieces on each axis
            var grid = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(pieces)));
            var shards = new List<Transform>(pieces);
            var vels = new List<Vector3>(pieces);
            var spins = new List<Vector3>(pieces);

            var baseSize = Mathf.Max(0.04f, (scale.x + scale.y) * 0.5f * 0.55f);
            var shardScale = baseSize / grid * 1.15f;
            var explode = Mathf.Lerp(1.1f, 3.2f, Mathf.Clamp01(overwhelm / CardShatterPresentation.MaxOverwhelmDiff));
            if (!battle) explode = 1.35f;

            var made = 0;
            for (var gy = 0; gy < grid && made < pieces; gy++)
            {
                for (var gx = 0; gx < grid && made < pieces; gx++)
                {
                    var shard = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    shard.name = "Shard" + made;
                    shard.layer = _layer;
                    ArObjectUtil.Destroy(shard.GetComponent<Collider>());
                    var tr = shard.transform;
                    // Local grid offset in card plane
                    var u = (gx + 0.5f) / grid - 0.5f;
                    var v = (gy + 0.5f) / grid - 0.5f;
                    var local = new Vector3(u * baseSize, v * baseSize * 1.35f, 0f);
                    tr.position = origin + orient * local;
                    tr.rotation = orient * Quaternion.Euler(
                        Random.Range(-12f, 12f), Random.Range(-12f, 12f), Random.Range(-25f, 25f));
                    tr.localScale = new Vector3(shardScale, shardScale * 1.35f, 1f);
                    var mr = shard.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    // Outward + upward burst (more violent with overwhelm)
                    var dir = (orient * local).normalized;
                    if (dir.sqrMagnitude < 1e-4f)
                        dir = Random.onUnitSphere;
                    dir = (dir + Vector3.up * 0.55f + Random.insideUnitSphere * 0.35f).normalized;
                    var speed = Random.Range(0.9f, 1.8f) * explode;
                    vels.Add(dir * speed);
                    spins.Add(Random.insideUnitSphere * Random.Range(180f, 520f) * (battle ? 1.4f : 1f));
                    shards.Add(tr);
                    made++;
                }
            }

            // Dust burst
            var dustCount = Mathf.Clamp(pieces / 2, 10, 40);
            var ps = MakeDust(origin, flashCol, dustCount, explode);
            ps.Play(true);

            var life = Mathf.Lerp(0.55f, 0.95f, Mathf.Clamp01(pieces / (float)CardShatterPresentation.MaxBattlePieces));
            var t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / life);
                var dt = Time.unscaledDeltaTime;
                var grav = Vector3.down * (2.8f + explode * 0.6f);

                for (var i = 0; i < shards.Count; i++)
                {
                    var s = shards[i];
                    if (s == null) continue;
                    vels[i] += grav * dt;
                    s.position += vels[i] * dt;
                    s.Rotate(spins[i] * dt, Space.World);
                    var fade = 1f - u * u;
                    s.localScale = new Vector3(shardScale, shardScale * 1.35f, 1f) * fade;
                }

                if (flash != null)
                {
                    var fu = Mathf.Clamp01(t / 0.18f);
                    flash.transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.45f * explode * 0.5f, fu) *
                                                 (1f - Mathf.Clamp01((t - 0.1f) / 0.25f));
                }

                yield return null;
            }

            for (var i = 0; i < shards.Count; i++)
                if (shards[i] != null) ArObjectUtil.Destroy(shards[i].gameObject);
            if (flash != null) ArObjectUtil.Destroy(flash);
            if (ps != null) ArObjectUtil.Destroy(ps.gameObject, 1.5f);
            // mat may be shared for texture case — only destroy if unique instance
            if (mat != null && faceTex != null && mat.name.Contains("ArFieldTex"))
                ArObjectUtil.Destroy(mat, 2f);
        }

        ParticleSystem MakeDust(Vector3 pos, Color c, int count, float explode)
        {
            var go = new GameObject("ShatterDust");
            go.layer = _layer;
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.5f;
            main.startSize = 0.04f * explode;
            main.startColor = c;
            main.maxParticles = count;
            main.loop = false;
            main.playOnAwake = false;
            main.gravityModifier = 0.6f;
            main.startSpeed = 1.2f * explode;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.08f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            return ps;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
