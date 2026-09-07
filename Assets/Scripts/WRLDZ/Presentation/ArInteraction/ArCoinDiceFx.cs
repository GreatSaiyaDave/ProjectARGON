using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Procedural coin / die holograms for the duel arena. Consumes
    /// <see cref="CoinDicePresentation"/> events (Time Wizard, Barrel Dragon,
    /// "Zorc" d6, etc.): raises a Solid-Vision coin or die above the field, spins /
    /// tumbles it, and lands on the actual result the rules engine produced.
    ///
    /// Assets are generated at runtime (disc + cube + pip spheres) — same procedural
    /// approach as <see cref="ArCardShatterFx"/>, so no imported models are required.
    /// </summary>
    public class ArCoinDiceFx : MonoBehaviour
    {
        static ArCoinDiceFx _instance;
        int _layer = 28;

        // Anime gold for Heads / signature coin; cool steel for Tails; ivory die.
        static readonly Color CoinHeads = new Color(1f, 0.82f, 0.28f, 1f);
        static readonly Color CoinTails = new Color(0.62f, 0.68f, 0.78f, 1f);
        static readonly Color CoinEdge = new Color(0.85f, 0.7f, 0.3f, 1f);
        static readonly Color DieBody = new Color(0.95f, 0.94f, 0.9f, 1f);
        static readonly Color DiePip = new Color(0.12f, 0.1f, 0.14f, 1f);
        static readonly Color Glow = new Color(0.6f, 0.85f, 1f, 0.9f);

        public static ArCoinDiceFx Ensure(Transform host = null, int layer = 28)
        {
            if (_instance != null)
            {
                _instance._layer = layer;
                return _instance;
            }

            var go = new GameObject("CoinDiceFxHost");
            if (host != null) go.transform.SetParent(host, false);
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<ArCoinDiceFx>();
            _instance._layer = layer;
            return _instance;
        }

        /// <summary>Spawn and animate the hologram for one coin/die result.</summary>
        public static void Play(in CoinDiceEvent e, Vector3 worldPos, int layer = 28)
        {
            if (!Application.isPlaying)
            {
                Debug.Log($"[WRLDZ FX] Coin/Dice skipped (edit mode): {Describe(e)}");
                return;
            }

            var fx = Ensure(null, layer);
            if (e.IsDie)
                fx.StartCoroutine(fx.DieRoutine(Mathf.Clamp(e.DieFace, 1, 6), worldPos, e.Label));
            else
                fx.StartCoroutine(fx.CoinRoutine(e.Heads, worldPos, e.Label));
            Debug.Log($"[WRLDZ FX] Coin/Dice hologram: {Describe(e)}");
        }

        static string Describe(in CoinDiceEvent e) =>
            (e.IsDie ? $"die={e.DieFace}" : $"coin={(e.Heads ? "Heads" : "Tails")}") +
            (string.IsNullOrEmpty(e.Label) ? "" : $" «{e.Label}»");

        // ── Coin ─────────────────────────────────────────────────────────────
        IEnumerator CoinRoutine(bool heads, Vector3 pos, string label)
        {
            var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coin.name = "CoinHolo";
            coin.layer = _layer;
            ArObjectUtil.Destroy(coin.GetComponent<Collider>());
            // Unity cylinder is 2 units tall along Y → thin disc, coin-sized.
            coin.transform.position = pos;
            coin.transform.localScale = new Vector3(0.14f, 0.008f, 0.14f);
            var mr = coin.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = ArFieldMaterials.Get(heads ? CoinHeads : CoinTails);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            SetChildLayer(coin.transform);

            var glow = MakeGlow(pos, 0.24f);

            // Heads = face up (disc normal +Y, i.e. flat); Tails = flipped 180° about X.
            // Spin about X so the coin visibly flips edge-over-edge, decelerating to rest.
            var spinDur = 1.15f;
            var t = 0f;
            var startY = pos.y;
            var rise = 0.18f;
            var extraSpins = heads ? 5 : 5; // whole flips
            var endFlip = heads ? 0f : 180f;
            var totalDeg = 360f * extraSpins + endFlip + 90f; // +90 so a face (not edge) rests up
            while (t < spinDur)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / spinDur);
                var e = 1f - Mathf.Pow(1f - u, 3f); // ease-out
                var ang = totalDeg * e;
                coin.transform.rotation = Quaternion.Euler(ang, 0f, 0f);
                var hop = Mathf.Sin(u * Mathf.PI) * rise;
                coin.transform.position = new Vector3(pos.x, startY + hop, pos.z);
                if (glow != null)
                    glow.transform.position = coin.transform.position;
                yield return null;
            }

            coin.transform.rotation = Quaternion.Euler(90f + endFlip, 0f, 0f);
            coin.transform.position = new Vector3(pos.x, startY, pos.z);

            yield return HoldThenFade(coin.transform, glow, 0.85f, 0.4f);
            ArObjectUtil.Destroy(coin);
            if (glow != null) ArObjectUtil.Destroy(glow);
        }

        // ── Die ──────────────────────────────────────────────────────────────
        IEnumerator DieRoutine(int face, Vector3 pos, string label)
        {
            var die = GameObject.CreatePrimitive(PrimitiveType.Cube);
            die.name = "DieHolo";
            die.layer = _layer;
            ArObjectUtil.Destroy(die.GetComponent<Collider>());
            const float size = 0.12f;
            die.transform.position = pos;
            die.transform.localScale = Vector3.one * size;
            var mr = die.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = ArFieldMaterials.Get(DieBody);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Pips for the result on the top (+Y) face, in the die's local space.
            var pips = new List<GameObject>();
            foreach (var p in PipOffsets(face))
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = "Pip";
                pip.layer = _layer;
                ArObjectUtil.Destroy(pip.GetComponent<Collider>());
                pip.transform.SetParent(die.transform, false);
                pip.transform.localScale = Vector3.one * 0.22f;
                // Local cube spans ±0.5; sit pips just above the +Y face.
                pip.transform.localPosition = new Vector3(p.x, 0.52f, p.y);
                var pmr = pip.GetComponent<MeshRenderer>();
                if (pmr != null)
                {
                    pmr.sharedMaterial = ArFieldMaterials.Get(DiePip);
                    pmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                pips.Add(pip);
            }

            var glow = MakeGlow(pos, 0.22f);

            // Tumble about a fixed skew axis, decelerating; land axis-aligned so the
            // pip face rests upward.
            var tumbleDur = 1.15f;
            var t = 0f;
            var axis = new Vector3(1f, 0.6f, 0.35f).normalized;
            var totalDeg = 360f * 3f + 720f;
            var startY = pos.y;
            var rise = 0.16f;
            while (t < tumbleDur)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / tumbleDur);
                var e = 1f - Mathf.Pow(1f - u, 3f);
                die.transform.rotation = Quaternion.AngleAxis(totalDeg * e, axis) *
                                         Quaternion.identity;
                var hop = Mathf.Sin(u * Mathf.PI) * rise;
                die.transform.position = new Vector3(pos.x, startY + hop, pos.z);
                if (glow != null) glow.transform.position = die.transform.position;
                yield return null;
            }

            // Settle upright (pip face up).
            die.transform.rotation = Quaternion.identity;
            die.transform.position = new Vector3(pos.x, startY, pos.z);

            yield return HoldThenFade(die.transform, glow, 0.9f, 0.4f);
            for (var i = 0; i < pips.Count; i++)
                if (pips[i] != null) ArObjectUtil.Destroy(pips[i]);
            ArObjectUtil.Destroy(die);
            if (glow != null) ArObjectUtil.Destroy(glow);
        }

        /// <summary>Standard pip layout (unit offsets in the top-face plane, ±0.28).</summary>
        static IEnumerable<Vector2> PipOffsets(int face)
        {
            const float o = 0.28f;
            switch (face)
            {
                case 1:
                    return new[] { new Vector2(0, 0) };
                case 2:
                    return new[] { new Vector2(-o, -o), new Vector2(o, o) };
                case 3:
                    return new[] { new Vector2(-o, -o), new Vector2(0, 0), new Vector2(o, o) };
                case 4:
                    return new[] { new Vector2(-o, -o), new Vector2(o, -o), new Vector2(-o, o), new Vector2(o, o) };
                case 5:
                    return new[] { new Vector2(-o, -o), new Vector2(o, -o), new Vector2(0, 0), new Vector2(-o, o), new Vector2(o, o) };
                default:
                    return new[]
                    {
                        new Vector2(-o, -o), new Vector2(-o, 0), new Vector2(-o, o),
                        new Vector2(o, -o), new Vector2(o, 0), new Vector2(o, o),
                    };
            }
        }

        GameObject MakeGlow(Vector3 pos, float size)
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "CoinDiceGlow";
            glow.layer = _layer;
            ArObjectUtil.Destroy(glow.GetComponent<Collider>());
            glow.transform.position = pos;
            glow.transform.localScale = Vector3.one * size;
            var mr = glow.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = ArFieldMaterials.GetAdditive(Glow);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return glow;
        }

        IEnumerator HoldThenFade(Transform body, GameObject glow, float hold, float fade)
        {
            var t = 0f;
            while (t < hold)
            {
                t += Time.unscaledDeltaTime;
                if (body == null) yield break;
                var bob = Mathf.Sin(Time.unscaledTime * 3f) * 0.006f;
                body.position += new Vector3(0f, bob, 0f);
                yield return null;
            }

            var baseScale = body != null ? body.localScale : Vector3.one;
            var glowScale = glow != null ? glow.transform.localScale : Vector3.one;
            t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / fade);
                var k = 1f - u;
                if (body != null) body.localScale = baseScale * k;
                if (glow != null) glow.transform.localScale = glowScale * k;
                yield return null;
            }
        }

        void SetChildLayer(Transform root)
        {
            root.gameObject.layer = _layer;
            for (var i = 0; i < root.childCount; i++)
                SetChildLayer(root.GetChild(i));
        }
    }
}
