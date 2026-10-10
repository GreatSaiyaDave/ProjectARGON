using System.Collections;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Solid Vision arena VFX (Yugipedia: holographic projections from the Duel Disk).
    /// Card-named looks first (Trap Hole pitfall, Raigeki bolt, …); generic ring is last resort.
    /// </summary>
    public class ArArenaCombatFx : MonoBehaviour
    {
        static ArArenaCombatFx _instance;
        int _layer = 28;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock _mpb;
        static Mesh _ringMesh;

        static float HoloScale => Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);

        public static ArArenaCombatFx Ensure(Transform host, int layer = 28)
        {
            if (_instance != null) return _instance;
            var go = new GameObject("ArenaCombatFxHost");
            if (host != null) go.transform.SetParent(host, false);
            _instance = go.AddComponent<ArArenaCombatFx>();
            _instance._layer = layer;
            return _instance;
        }

        public static void PlaySummon(Vector3 worldPos, bool playerSide, int layer = 28)
        {
            PlayHoloMaterialize(worldPos, playerSide, isMonster: true, layer);
        }

        /// <summary>
        /// A Summon procedure's arena hologram. The engine tags every appear with the procedure it came from
        /// (<see cref="ActiveCombatPresentation.Procedure"/>), so a Fusion card lands as a Fusion instead of the
        /// generic materialize. Each procedure plays its own lead-in silhouette — the materials converge for
        /// Fusion, the circle closes for Ritual, the tuners align for Synchro, the layers drop for Xyz, the
        /// scales swing for Pendulum, the links join for Link — then hands off to the shared arrival. A
        /// procedure with no distinct silhouette (Normal) is the arrival on its own.
        /// </summary>
        /// <param name="arrive">
        /// When false the lead-in stops before the arrival, leaving the materialize to whoever owns the landing
        /// (the arena card visual plays it as the card reaches its zone). Use false whenever a card visual is
        /// about to land, so the summon gets one hologram rather than two.
        /// </param>
        public static void PlaySummonProcedure(Vector3 worldPos, bool playerSide, SummonKind procedure,
            int layer = 28, CardDef def = null, bool arrive = true)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null, layer);
            switch (procedure)
            {
                case SummonKind.FusionSummon:
                    fx.StartCoroutine(fx.FusionFuseRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.RitualSummon:
                    fx.StartCoroutine(fx.RitualCircleRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.SynchroSummon:
                    fx.StartCoroutine(fx.OrbitAlignRoutine(worldPos, playerSide, def, 2, true, arrive));
                    return;
                case SummonKind.XyzSummon:
                    fx.StartCoroutine(fx.OrbitAlignRoutine(worldPos, playerSide, def, 3, false, arrive));
                    return;
                case SummonKind.PendulumSummon:
                    fx.StartCoroutine(fx.PendulumSwingRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.LinkSummon:
                    fx.StartCoroutine(fx.LinkJoinRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.FlipSummon:
                    fx.StartCoroutine(fx.FlipPlateRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.TributeSummon:
                    fx.StartCoroutine(fx.TributeAshRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.SpecialSummon:
                    fx.StartCoroutine(fx.SummonPortalRoutine(worldPos, playerSide, def, arrive));
                    return;
                case SummonKind.NormalSet:
                case SummonKind.TributeSet:
                    // A Set is deliberately quiet and low: face-down, no column, no reveal flash.
                    fx.StartCoroutine(fx.SetPinRoutine(worldPos, playerSide, arrive));
                    return;
                default:
                    PlayHoloMaterialize(worldPos, playerSide, isMonster: true, layer, def);
                    return;
            }
        }

        static Color SideTint(bool playerSide) => playerSide
            ? new Color(0.35f, 0.92f, 1f, 1f)
            : new Color(1f, 0.42f, 0.58f, 1f);

        /// <summary>The Ka light for a procedure lead-in: the monster's own Attribute when face-up, else the side.</summary>
        static Color ProcedureTint(CardDef def, Color side)
        {
            return def != null && def.IsMonster ? SolidVisionCinematics.TintFor(def) : side;
        }

        /// <summary>
        /// Fusion: the Fusion Materials pull into one knot of light, then release into the monster. The knot
        /// brightens as they close, so the payoff reads as a merge rather than an arrival from nowhere.
        /// </summary>
        IEnumerator FusionFuseRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.78f;
            var knot = Prim(PrimitiveType.Sphere, "FusionKnot", pos + Vector3.up * 0.5f * s);
            ArFieldMaterials.ApplyAdditive(knot.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.9f));

            var mats = new GameObject[3];
            for (var i = 0; i < mats.Length; i++)
            {
                var a = i / (float)mats.Length * Mathf.PI * 2f;
                mats[i] = Prim(PrimitiveType.Sphere, "FusionMaterial" + i,
                    pos + new Vector3(Mathf.Cos(a), 1.5f, Mathf.Sin(a)) * s);
                ArFieldMaterials.ApplyAdditive(mats[i].GetComponent<MeshRenderer>(),
                    new Color(accent.r, accent.g, accent.b, 0.8f));
            }

            var startPos = new Vector3[mats.Length];
            for (var i = 0; i < mats.Length; i++) startPos[i] = mats[i].transform.position;

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var pull = 1f - Mathf.Pow(1f - u, 2.2f);
                for (var i = 0; i < mats.Length; i++)
                {
                    mats[i].transform.position = Vector3.Lerp(startPos[i], pos + Vector3.up * 0.5f * s, pull);
                    mats[i].transform.localScale = Vector3.one * (0.14f * s * (1f - 0.6f * pull));
                }
                var pop = Mathf.Lerp(0.1f, 0.42f, pull);
                knot.transform.localScale = Vector3.one * (pop * s);
                yield return null;
            }

            ArObjectUtil.Destroy(knot);
            foreach (var m in mats) ArObjectUtil.Destroy(m);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>
        /// Ritual: the circle is drawn, then closes and stands the monster up through it. A Set Ritual reads as
        /// the same circle standing still — only the closing beat is missing.
        /// </summary>
        IEnumerator RitualCircleRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.82f;
            var circle = RingPrim("RitualCircle", pos + Vector3.up * 0.03f);
            ArFieldMaterials.ApplyAdditive(circle.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.9f));
            var pillar = Prim(PrimitiveType.Cylinder, "RitualPillar", pos + Vector3.up * 0.04f);
            ArFieldMaterials.ApplyAdditive(pillar.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 0.97f, 0.6f));

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var draw = 1f - Mathf.Pow(1f - u, 3f);
                var r = Mathf.Lerp(0.12f, 1.5f, draw);
                circle.transform.localScale = new Vector3(r, 1f, r);
                circle.transform.Rotate(Vector3.up, 150f * Time.deltaTime, Space.World);
                var h = Mathf.Lerp(0.05f, 1.5f, draw);
                pillar.transform.position = pos + Vector3.up * (h * 0.5f);
                pillar.transform.localScale = new Vector3(0.2f * s, h * 0.5f, 0.2f * s);
                yield return null;
            }

            ArObjectUtil.Destroy(circle);
            ArObjectUtil.Destroy(pillar);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>
        /// Synchro / Xyz: the tuner monsters orbit and either counter-spin into alignment (Synchro) or drop as
        /// layers (Xyz). Two orbits counter-rotate and pull together; three nest downward and settle.
        /// </summary>
        IEnumerator OrbitAlignRoutine(Vector3 pos, bool playerSide, CardDef def, int rings, bool counterSpin, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.86f;
            var orbit = new GameObject[rings];
            for (var i = 0; i < rings; i++)
            {
                orbit[i] = RingPrim((counterSpin ? "SynchroOrbit" : "XyzLayer") + i, pos + Vector3.up * 0.03f);
                ArFieldMaterials.ApplyAdditive(orbit[i].GetComponent<MeshRenderer>(),
                    new Color(accent.r, accent.g, accent.b, 0.85f - 0.12f * i));
            }

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var settle = 1f - Mathf.Pow(1f - u, 2.6f);
                for (var i = 0; i < rings; i++)
                {
                    var stagger = counterSpin ? (i % 2 == 0 ? 1f : -1f) : -1f;
                    orbit[i].transform.Rotate(Vector3.up,
                        (counterSpin ? 220f : -160f) * stagger * Time.deltaTime, Space.World);
                    var r = Mathf.Lerp(1.15f - 0.16f * i, 0.55f + 0.1f * i, settle) * s;
                    var y = counterSpin ? 0.03f : (0.03f + 0.07f * (rings - i));
                    orbit[i].transform.position = pos + Vector3.up * y;
                    orbit[i].transform.localScale = new Vector3(r, 1f, r);
                }
                yield return null;
            }

            foreach (var o in orbit) ArObjectUtil.Destroy(o);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>Pendulum: two scales swing in over the arena and level out as the monster rises between them.</summary>
        IEnumerator PendulumSwingRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.9f;
            var arcs = new GameObject[2];
            for (var i = 0; i < arcs.Length; i++)
            {
                arcs[i] = RingPrim("PendulumArc" + i, pos + Vector3.up * 0.03f);
                ArFieldMaterials.ApplyAdditive(arcs[i].GetComponent<MeshRenderer>(),
                    new Color(accent.r, accent.g, accent.b, 0.85f));
            }

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var level = 1f - Mathf.Pow(1f - u, 2f);
                for (var i = 0; i < arcs.Length; i++)
                {
                    var side2 = i == 0 ? -1f : 1f;
                    var tilt = side2 * Mathf.Lerp(34f, 0f, level);
                    arcs[i].transform.rotation = Quaternion.Euler(tilt, 0f, 0f);
                    var r = Mathf.Lerp(0.9f, 1.25f, level) * s;
                    arcs[i].transform.localScale = new Vector3(r, 1f, r);
                }
                yield return null;
            }

            foreach (var a in arcs) ArObjectUtil.Destroy(a);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>
        /// Link: the Link materials sit as separate points and are drawn together by the links between them, so
        /// the monster reads as assembled rather than dropped in.
        /// </summary>
        IEnumerator LinkJoinRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.8f;
            const int points = 3;
            var nodes = new GameObject[points];
            for (var i = 0; i < points; i++)
            {
                var a = i / (float)points * Mathf.PI * 2f;
                nodes[i] = Prim(PrimitiveType.Sphere, "LinkNode" + i,
                    pos + new Vector3(Mathf.Cos(a), 0.45f, Mathf.Sin(a)) * 0.9f * s);
                ArFieldMaterials.ApplyAdditive(nodes[i].GetComponent<MeshRenderer>(),
                    new Color(accent.r, accent.g, accent.b, 0.85f));
            }

            var hub = Prim(PrimitiveType.Sphere, "LinkHub", pos + Vector3.up * 0.45f * s);
            ArFieldMaterials.ApplyAdditive(hub.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 0.97f, 0.9f));

            var startPos = new Vector3[nodes.Length];
            for (var i = 0; i < nodes.Length; i++) startPos[i] = nodes[i].transform.position;

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var draw = 1f - Mathf.Pow(1f - u, 2f);
                hub.transform.localScale = Vector3.one * (0.3f * s * draw);
                for (var i = 0; i < points; i++)
                {
                    nodes[i].transform.position = Vector3.Lerp(startPos[i], pos + Vector3.up * 0.45f * s, draw);
                    nodes[i].transform.localScale = Vector3.one * (0.12f * s * (1f - 0.5f * draw));
                }
                yield return null;
            }

            foreach (var n in nodes) ArObjectUtil.Destroy(n);
            ArObjectUtil.Destroy(hub);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>Flip Summon: the face-down card turns over with a light wipe before it materializes.</summary>
        IEnumerator FlipPlateRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.5f;
            var plate = Prim(PrimitiveType.Cube, "FlipPlate", pos + Vector3.up * 0.05f);
            ArFieldMaterials.ApplyAdditive(plate.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.8f));
            plate.transform.localScale = new Vector3(0.62f * s, 0.02f * s, 0.88f * s);

            var wipe = RingPrim("FlipWipe", pos + Vector3.up * 0.03f);
            ArFieldMaterials.ApplyAdditive(wipe.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 0.97f, 0.7f));

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                plate.transform.Rotate(Vector3.up, 180f * Time.deltaTime, Space.World);
                var r = Mathf.Lerp(0.2f, 1.05f, u);
                wipe.transform.localScale = new Vector3(r, 1f, r);
                yield return null;
            }

            ArObjectUtil.Destroy(plate);
            ArObjectUtil.Destroy(wipe);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>Tribute Summon: the spent Tributes burn out below as the new monster arrives over them.</summary>
        IEnumerator TributeAshRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.6f;
            var spent = new GameObject[2];
            for (var i = 0; i < spent.Length; i++)
            {
                spent[i] = RingPrim("TributeAsh" + i, pos + new Vector3(-0.32f + 0.64f * i, 0.02f, 0.2f) * s);
                ArFieldMaterials.ApplyAdditive(spent[i].GetComponent<MeshRenderer>(),
                    new Color(1f, 0.62f, 0.28f, 0.8f));
            }

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var fade = 1f - u;
                for (var i = 0; i < spent.Length; i++)
                {
                    var r = Mathf.Lerp(0.18f, 0.6f, u);
                    spent[i].transform.localScale = new Vector3(r, 1f, r);
                    Fade(spent[i], new Color(1f, 0.62f, 0.28f, 0.8f), fade);
                }
                yield return null;
            }

            foreach (var sp in spent) ArObjectUtil.Destroy(sp);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>Special Summon by procedure: a ring opens in front of the monster, then the monster steps through.</summary>
        IEnumerator SummonPortalRoutine(Vector3 pos, bool playerSide, CardDef def, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var accent = ProcedureTint(def, side);
            var dur = 0.6f;
            var portal = RingPrim("SummonPortal", pos + Vector3.up * 0.5f * s);
            ArFieldMaterials.ApplyAdditive(portal.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.9f));
            portal.transform.localScale = new Vector3(0.9f, 1f, 0.9f);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                portal.transform.Rotate(Vector3.up, 260f * Time.deltaTime, Space.World);
                var open = Mathf.Sin(Mathf.Clamp01(u * 1.2f) * Mathf.PI);
                portal.transform.localScale = new Vector3(0.9f + 0.35f * open, 1f, 0.5f + 0.5f * open);
                Fade(portal, new Color(accent.r, accent.g, accent.b, 0.9f), 1f - u * 0.4f);
                yield return null;
            }

            ArObjectUtil.Destroy(portal);
            if (arrive) PlayHoloMaterialize(pos, playerSide, isMonster: true, _layer, def);
        }

        /// <summary>A Set is deliberately understated: a low pin of light, no reveal, no column.</summary>
        IEnumerator SetPinRoutine(Vector3 pos, bool playerSide, bool arrive)
        {
            var s = HoloScale;
            var side = SideTint(playerSide);
            var dur = 0.42f;
            var pin = Prim(PrimitiveType.Cylinder, "SetPin", pos + Vector3.up * 0.02f);
            ArFieldMaterials.ApplyAdditive(pin.GetComponent<MeshRenderer>(),
                new Color(side.r, side.g, side.b, 0.55f));
            var ring = RingPrim("SetPinRing", pos + Vector3.up * 0.02f);
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(),
                new Color(side.r, side.g, side.b, 0.6f));

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var grow = Mathf.Sin(Mathf.Clamp01(u * 1.15f) * Mathf.PI);
                pin.transform.localScale = new Vector3(0.1f * s * grow, 0.03f * s, 0.1f * s * grow);
                var r = Mathf.Lerp(0.12f, 0.5f, u);
                ring.transform.localScale = new Vector3(r, 1f, r);
                yield return null;
            }

            ArObjectUtil.Destroy(pin);
            ArObjectUtil.Destroy(ring);
        }

        /// <summary>
        /// Anime Ka projection at the arena landing: ground rings + light column
        /// as a hologram materializes. Never plays on the disk plate.
        /// Scales with <see cref="ArPlaymatLayout.LiveHoloScale"/>.
        /// </summary>
        public static void PlayHoloMaterialize(Vector3 worldPos, bool playerSide, bool isMonster,
            int layer = 28)
        {
            PlayHoloMaterialize(worldPos, playerSide, isMonster, layer, null);
        }

        /// <summary>
        /// Materialize in the monster's own light (SOLID_VISION_BEHAVIOR.md §4.2): a face-up monster's column and
        /// ground light take its Attribute (or its cinematics-table tint) and grow with its Level; the owner's
        /// side colour stays as a thin outer ring. <paramref name="def"/> null keeps the side-coloured look —
        /// never pass a face-down card's def, its Attribute would leak.
        /// </summary>
        public static void PlayHoloMaterialize(Vector3 worldPos, bool playerSide, bool isMonster, int layer,
            CardDef def)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null, layer);
            fx.StartCoroutine(fx.HoloMaterializeRoutine(worldPos, playerSide, isMonster, def));
        }

        public static void PlayAttackBeam(Vector3 from, Vector3 to, bool playerSide, float charge01)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null);
            fx.StartCoroutine(fx.BeamRoutine(from, to, playerSide, charge01));
        }

        /// <summary>
        /// One pulse of an attacker's charge while the defender may still respond (§4.2 beat 2), in its
        /// archetype's language. The director calls it sparsely; <paramref name="charge01"/> ramps over the window.
        /// </summary>
        public static void PlayChargeTell(AttackArchetype kind, Vector3 from, Vector3 to, float groundY,
            bool playerSide, Color tint, float charge01)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null);
            charge01 = Mathf.Clamp01(charge01);
            switch (kind)
            {
                case AttackArchetype.Beam:
                    fx.StartCoroutine(fx.BeamRoutine(from, to, playerSide, charge01, tint));
                    return;
                case AttackArchetype.Blast:
                    fx.StartCoroutine(fx.ChargeOrbRoutine(from, to, tint, charge01));
                    return;
                case AttackArchetype.Projectile:
                    fx.StartCoroutine(fx.GlintRoutine(from, to, tint, charge01));
                    return;
                case AttackArchetype.Swarm:
                    fx.StartCoroutine(fx.GatherRoutine(from, tint, charge01));
                    return;
                default:
                    // Lunge: light drawn in under the attacker's feet. Surge: wide, slow ripples outward.
                    fx.StartCoroutine(fx.GroundPulseRoutine(Ground(from, groundY), tint, charge01,
                        kind == AttackArchetype.Surge));
                    return;
            }
        }

        /// <summary>
        /// The attack itself (§4.2 beat 3). The director fires it when the engine RESOLVES the attack — never on a
        /// clock, since a response can still cancel it — so it is short: the battle result (shatter, LP) follows
        /// at once. <paramref name="groundY"/> is the street height under the duel.
        /// </summary>
        public static void PlayAttackRelease(AttackArchetype kind, Vector3 from, Vector3 to, float groundY,
            Color tint, float weight01)
        {
            if (!Application.isPlaying) return;
            if ((to - from).sqrMagnitude < 0.0025f) return;
            var fx = Ensure(null);
            weight01 = Mathf.Clamp01(weight01);
            switch (kind)
            {
                case AttackArchetype.Blast:
                    fx.StartCoroutine(fx.BlastRoutine(from, to, groundY, tint, weight01));
                    return;
                case AttackArchetype.Lunge:
                    fx.StartCoroutine(fx.LungeRoutine(from, to, groundY, tint, weight01));
                    return;
                case AttackArchetype.Projectile:
                    fx.StartCoroutine(fx.VolleyRoutine(from, to, groundY, tint, weight01));
                    return;
                case AttackArchetype.Swarm:
                    fx.StartCoroutine(fx.SwarmRoutine(from, to, groundY, tint, weight01));
                    return;
                case AttackArchetype.Surge:
                    fx.StartCoroutine(fx.SurgeRoutine(from, to, groundY, tint, weight01));
                    return;
                default:
                    fx.StartCoroutine(fx.BeamReleaseRoutine(from, to, groundY, tint, weight01));
                    return;
            }
        }

        public static void PlayImpact(Vector3 worldPos, bool heavy)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null);
            fx.StartCoroutine(fx.ImpactRoutine(worldPos, heavy));
        }

        public static void PlayDefendShield(Vector3 worldPos, bool playerSide)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null);
            fx.StartCoroutine(fx.DefendRoutine(worldPos, playerSide));
        }

        /// <summary>
        /// Spell/Trap Solid Vision. Pass <paramref name="cardId"/> so Trap Hole is a pit,
        /// Raigeki is lightning, etc. — not a generic ring.
        /// </summary>
        public static void PlaySpellCast(Vector3 worldPos, bool playerSide, int layer = 28,
            int cardId = 0)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null, layer);
            switch (cardId)
            {
                case SpellTrapEffects.TrapHole:
                case 29401950: // Bottomless Trap Hole — same pit, darker void
                    fx.StartCoroutine(fx.PitfallRoutine(worldPos, cardId == 29401950));
                    return;
                case SpellTrapEffects.Raigeki:
                    fx.StartCoroutine(fx.RaigekiRoutine(worldPos));
                    return;
                case SpellTrapEffects.DarkHole:
                    fx.StartCoroutine(fx.DarkHoleRoutine(worldPos));
                    return;
                case SpellTrapEffects.MirrorForce:
                    fx.StartCoroutine(fx.MirrorForceRoutine(worldPos, playerSide));
                    return;
                case SpellTrapEffects.SwordsOfRevealingLight:
                    fx.StartCoroutine(fx.SwordsRoutine(worldPos));
                    return;
                case SpellTrapEffects.MonsterReborn:
                    fx.StartCoroutine(fx.RebornRoutine(worldPos, playerSide));
                    return;
                default:
                    fx.StartCoroutine(fx.SpellCastRoutine(worldPos, playerSide));
                    return;
            }
        }

        IEnumerator HoloMaterializeRoutine(Vector3 pos, bool playerSide, bool isMonster, CardDef def = null)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var side = playerSide
                ? new Color(0.35f, 0.92f, 1f, 1f)
                : new Color(1f, 0.42f, 0.58f, 1f);
            // A face-up monster's Ka light is its own: Attribute / table tint, bigger and slower with its Level.
            var own = isMonster && def != null && def.IsMonster;
            var accent = own ? SolidVisionCinematics.TintFor(def) : side;
            var w = own ? SolidVisionCinematics.Weight(def) : 0f;
            var white = new Color(1f, 1f, 0.97f, 1f);
            var dur = (isMonster ? 0.92f : 0.58f) * (own ? 1f + 0.6f * w : 1f);
            var colH = (isMonster ? 1.92f : 0.62f) * s * (own ? 0.85f + 0.45f * w : 1f);
            var ringMax = (isMonster ? 1.85f : 0.72f) * s * (own ? 0.85f + 0.5f * w : 1f);

            var core = Prim(PrimitiveType.Cylinder, "KaLightCore", pos + Vector3.up * 0.04f);
            ArFieldMaterials.ApplyAdditive(core.GetComponent<MeshRenderer>(),
                new Color(white.r, white.g, white.b, 0.85f));

            var column = Prim(PrimitiveType.Cylinder, "KaLightColumn", pos + Vector3.up * 0.04f);
            ArFieldMaterials.ApplyAdditive(column.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.55f));

            var ring = Prim(PrimitiveType.Cylinder, "KaGroundRing", pos + Vector3.up * 0.02f);
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.8f));

            var ring2 = Prim(PrimitiveType.Cylinder, "KaGroundRing2", pos + Vector3.up * 0.025f);
            ArFieldMaterials.ApplyAdditive(ring2.GetComponent<MeshRenderer>(),
                new Color(white.r, white.g, white.b, 0.55f));

            var flash = Prim(PrimitiveType.Sphere, "KaFlash", pos + Vector3.up * (isMonster ? 0.7f * s : 0.22f * s));
            ArFieldMaterials.ApplyAdditive(flash.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 1f, 0.95f));

            // The owner stays readable when the light is the monster's: a thin side-coloured ring at the rim.
            GameObject ownerRing = null;
            if (own)
            {
                ownerRing = RingPrim("KaOwnerRing", pos + Vector3.up * 0.03f);
                ArFieldMaterials.ApplyAdditive(ownerRing.GetComponent<MeshRenderer>(),
                    new Color(side.r, side.g, side.b, 0.75f));
            }

            var motes = isMonster ? Mathf.RoundToInt(48 * (own ? 0.8f + 0.6f * w : 1f)) : 22;
            var ps = MakeBurst(pos + Vector3.up * 0.35f * s, accent,
                motes, 0.7f, isMonster ? 0.14f * s : 0.07f * s);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 18f;
            sh.radius = 0.08f * s;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            ArObjectUtil.SetVelocity(vel, new Vector3(0f, 1.6f * s, 0f));
            ps.Play(true);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var rise = 1f - Mathf.Pow(1f - u, 2.4f);
                var fade = u < 0.55f ? 1f : 1f - (u - 0.55f) / 0.45f;
                fade = Mathf.Clamp01(fade);

                var h = Mathf.Lerp(0.08f, colH, rise);
                core.transform.position = pos + Vector3.up * (h * 0.5f);
                core.transform.localScale = new Vector3(0.07f * s * fade, h * 0.5f, 0.07f * s * fade);
                column.transform.position = pos + Vector3.up * (h * 0.5f);
                column.transform.localScale = new Vector3(
                    Mathf.Lerp(0.16f, 0.42f, rise) * s * fade,
                    h * 0.5f,
                    Mathf.Lerp(0.16f, 0.42f, rise) * s * fade);

                var r1 = Mathf.Lerp(0.18f * s, ringMax, rise);
                ring.transform.localScale = new Vector3(r1, 0.012f * s, r1);
                var r2 = Mathf.Lerp(0.10f * s, ringMax * 0.62f, rise);
                ring2.transform.localScale = new Vector3(r2, 0.008f * s, r2);

                if (ownerRing != null)
                {
                    var r3 = Mathf.Lerp(0.2f * s, ringMax * 1.1f, rise);
                    ownerRing.transform.localScale = new Vector3(r3, 1f, r3);
                    Fade(ownerRing, new Color(side.r, side.g, side.b, 0.75f), fade);
                }

                var flashPop = Mathf.Sin(Mathf.Clamp01(u * 1.35f) * Mathf.PI);
                flash.transform.localScale = Vector3.one * (Mathf.Lerp(0.12f, 0.85f, flashPop) * s * fade);

                yield return null;
            }

            ArObjectUtil.Destroy(core);
            ArObjectUtil.Destroy(column);
            ArObjectUtil.Destroy(ring);
            ArObjectUtil.Destroy(ring2);
            ArObjectUtil.Destroy(flash);
            ArObjectUtil.Destroy(ownerRing);
            if (ps != null) ArObjectUtil.Destroy(ps.gameObject, 1.4f);
        }

        GameObject Prim(PrimitiveType type, string name, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = _layer;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.transform.position = pos;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            return go;
        }

        IEnumerator BeamRoutine(Vector3 from, Vector3 to, bool playerSide, float charge01, Color? tint = null)
        {
            var accent = tint ?? (playerSide
                ? new Color(1f, 0.85f, 0.25f, 1f)
                : new Color(1f, 0.45f, 0.35f, 1f));
            charge01 = Mathf.Clamp01(charge01);

            var mid = Vector3.Lerp(from, to, 0.5f) + Vector3.up * 0.15f;
            var dir = to - from;
            var len = dir.magnitude;
            if (len < 0.05f) yield break;

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "AttackBeam";
            beam.layer = _layer;
            ArObjectUtil.Destroy(beam.GetComponent<Collider>());
            beam.transform.position = mid;
            beam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var thickness = Mathf.Lerp(0.02f, 0.08f, charge01);
            beam.transform.localScale = new Vector3(thickness, thickness, len);
            // Energy FX are light, not solids: additive so they never hide the monster or the field (the opaque
            // ArFieldMaterials.Apply ignores alpha entirely).
            ArFieldMaterials.ApplyAdditive(beam.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.35f + charge01 * 0.55f));

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "ChargeOrb";
            ball.layer = _layer;
            ArObjectUtil.Destroy(ball.GetComponent<Collider>());
            ball.transform.position = from + Vector3.up * 0.25f;
            var bs = Mathf.Lerp(0.08f, 0.2f, charge01);
            ball.transform.localScale = Vector3.one * bs;
            ArFieldMaterials.ApplyAdditive(ball.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.85f));

            var hold = Mathf.Lerp(0.06f, 0.14f, charge01);
            yield return new WaitForSecondsRealtime(hold);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.16f;
                var a = 1f - Mathf.Clamp01(t);
                beam.transform.localScale = new Vector3(thickness * a, thickness * a, len);
                ball.transform.localScale = Vector3.one * bs * a;
                yield return null;
            }

            ArObjectUtil.Destroy(beam);
            ArObjectUtil.Destroy(ball);
        }

        IEnumerator ImpactRoutine(Vector3 pos, bool heavy)
        {
            var accent = heavy
                ? new Color(1f, 0.35f, 0.15f, 1f)
                : new Color(1f, 0.7f, 0.3f, 1f);

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "ImpactFlash";
            flash.layer = _layer;
            ArObjectUtil.Destroy(flash.GetComponent<Collider>());
            flash.transform.position = pos + Vector3.up * 0.25f;
            flash.transform.localScale = Vector3.one * 0.1f;
            ArFieldMaterials.ApplyAdditive(flash.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 0.9f, 0.95f));

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ImpactRing";
            ring.layer = _layer;
            ArObjectUtil.Destroy(ring.GetComponent<Collider>());
            ring.transform.position = pos + Vector3.up * 0.03f;
            ring.transform.localScale = new Vector3(0.2f, 0.01f, 0.2f);
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.8f));

            var count = heavy ? 32 : 20;
            var ps = MakeBurst(pos + Vector3.up * 0.3f, accent, count, 0.45f, heavy ? 0.22f : 0.14f);
            ps.Play(true);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.28f;
                var u = Mathf.Clamp01(t);
                var fade = 1f - u;
                flash.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, heavy ? 0.5f : 0.32f, u) * fade;
                var rr = Mathf.Lerp(0.2f, heavy ? 1.0f : 0.7f, u);
                ring.transform.localScale = new Vector3(rr, 0.01f, rr) * fade;
                yield return null;
            }

            ArObjectUtil.Destroy(flash);
            ArObjectUtil.Destroy(ring);
            if (ps != null) ArObjectUtil.Destroy(ps.gameObject, 1.2f);
        }

        IEnumerator DefendRoutine(Vector3 pos, bool playerSide)
        {
            var accent = playerSide
                ? new Color(0.35f, 0.75f, 1f, 1f)
                : new Color(1f, 0.55f, 0.7f, 1f);

            // Hex-ish shield = flattened sphere shell
            var shield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shield.name = "DefendShield";
            shield.layer = _layer;
            ArObjectUtil.Destroy(shield.GetComponent<Collider>());
            shield.transform.position = pos + Vector3.up * 0.28f;
            shield.transform.localScale = new Vector3(0.15f, 0.2f, 0.08f);
            // Shield + rim are translucent glass over the defender (the card must stay readable through them).
            shield.GetComponent<MeshRenderer>().sharedMaterial =
                ArFieldMaterials.GetTransparent(new Color(accent.r, accent.g, accent.b, 0.45f));

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "ShieldRim";
            rim.layer = _layer;
            ArObjectUtil.Destroy(rim.GetComponent<Collider>());
            rim.transform.position = pos + Vector3.up * 0.05f;
            rim.transform.localScale = new Vector3(0.35f, 0.01f, 0.35f);
            rim.GetComponent<MeshRenderer>().sharedMaterial =
                ArFieldMaterials.GetTransparent(new Color(accent.r, accent.g, accent.b, 0.65f));

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.45f;
                var u = Mathf.Clamp01(t);
                var pop = 1f + 0.25f * Mathf.Sin(u * Mathf.PI);
                var a = u < 0.5f ? u * 2f : (1f - u) * 2f;
                shield.transform.localScale = new Vector3(0.15f, 0.2f, 0.08f) * pop * a;
                var rr = Mathf.Lerp(0.25f, 0.5f, u);
                rim.transform.localScale = new Vector3(rr, 0.01f, rr) * a;
                yield return null;
            }

            ArObjectUtil.Destroy(shield);
            ArObjectUtil.Destroy(rim);
        }

        IEnumerator SpellCastRoutine(Vector3 pos, bool playerSide)
        {
            var accent = playerSide
                ? new Color(0.45f, 0.85f, 1f, 1f)
                : new Color(1f, 0.75f, 0.35f, 1f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SpellCastRing";
            ring.layer = _layer;
            ArObjectUtil.Destroy(ring.GetComponent<Collider>());
            ring.transform.position = pos + Vector3.up * 0.02f;
            ring.transform.localScale = new Vector3(0.12f, 0.008f, 0.12f);
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.75f));

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "SpellCastOrb";
            orb.layer = _layer;
            ArObjectUtil.Destroy(orb.GetComponent<Collider>());
            orb.transform.position = pos + Vector3.up * 0.08f;
            orb.transform.localScale = Vector3.one * 0.06f;
            ArFieldMaterials.ApplyAdditive(orb.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.85f));

            var ps = MakeBurst(pos + Vector3.up * 0.2f, accent, 18, 0.5f, 0.1f);
            ps.Play(true);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.7f;
                var u = Mathf.Clamp01(t);
                var fade = 1f - u * 0.85f;
                var rr = Mathf.Lerp(0.12f, 0.55f, u);
                ring.transform.localScale = new Vector3(rr, 0.008f, rr);
                orb.transform.position = pos + Vector3.up * Mathf.Lerp(0.08f, 0.35f, u);
                orb.transform.localScale = Vector3.one * (0.06f * fade);
                yield return null;
            }

            ArObjectUtil.Destroy(ring);
            ArObjectUtil.Destroy(orb);
            if (ps != null) ArObjectUtil.Destroy(ps.gameObject, 1f);
        }

        /// <summary>
        /// Trap Hole / 落とし穴 (Otoshiana, pitfall). Ground splits, dark well, monster drops in.
        /// Bottomless: same pit with a violet void.
        /// </summary>
        IEnumerator PitfallRoutine(Vector3 pos, bool bottomless)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var rimCol = bottomless
                ? new Color(0.45f, 0.12f, 0.72f, 1f)
                : new Color(0.55f, 0.32f, 0.12f, 1f);
            var voidCol = bottomless
                ? new Color(0.08f, 0.02f, 0.14f, 1f)
                : new Color(0.04f, 0.03f, 0.02f, 1f);

            var well = Prim(PrimitiveType.Cylinder, "PitWell", pos + Vector3.up * 0.01f);
            ArFieldMaterials.Apply(well.GetComponent<MeshRenderer>(), voidCol);
            well.transform.localScale = new Vector3(0.08f * s, 0.02f * s, 0.08f * s);

            var rim = Prim(PrimitiveType.Cylinder, "PitRim", pos + Vector3.up * 0.03f);
            ArFieldMaterials.ApplyAdditive(rim.GetComponent<MeshRenderer>(),
                new Color(rimCol.r, rimCol.g, rimCol.b, 0.9f));

            var dust = MakeBurst(pos + Vector3.up * 0.12f * s, rimCol, 36, 0.7f, 0.08f * s);
            var vel = dust.velocityOverLifetime;
            vel.enabled = true;
            ArObjectUtil.SetVelocity(vel, new Vector3(0f, -1.8f * s, 0f));
            dust.Play(true);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.85f;
                var u = Mathf.Clamp01(t);
                var open = 1f - Mathf.Pow(1f - u, 2.1f);
                var r = Mathf.Lerp(0.12f, 1.15f, open) * s;
                well.transform.localScale = new Vector3(r * 0.92f, Mathf.Lerp(0.02f, 0.55f, open) * s, r * 0.92f);
                well.transform.position = pos + Vector3.down * (0.18f * open * s);
                rim.transform.localScale = new Vector3(r, 0.018f * s, r);
                yield return null;
            }

            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.35f;
                var fade = 1f - Mathf.Clamp01(t);
                rim.transform.localScale *= fade;
                yield return null;
            }

            ArObjectUtil.Destroy(well);
            ArObjectUtil.Destroy(rim);
            if (dust != null) ArObjectUtil.Destroy(dust.gameObject, 1.2f);
        }

        /// <summary>Raigeki — bolt from the sky into the field, then a white flash.</summary>
        IEnumerator RaigekiRoutine(Vector3 pos)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var bolt = Prim(PrimitiveType.Cylinder, "RaigekiBolt", pos + Vector3.up * (2.2f * s));
            ArFieldMaterials.ApplyAdditive(bolt.GetComponent<MeshRenderer>(),
                new Color(0.75f, 0.9f, 1f, 0.95f));
            bolt.transform.localScale = new Vector3(0.08f * s, 2.2f * s, 0.08f * s);

            var flash = Prim(PrimitiveType.Sphere, "RaigekiFlash", pos + Vector3.up * 0.4f * s);
            ArFieldMaterials.ApplyAdditive(flash.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 0.95f, 1f));

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.55f;
                var u = Mathf.Clamp01(t);
                var a = u < 0.25f ? u * 4f : 1f - (u - 0.25f) / 0.75f;
                a = Mathf.Clamp01(a);
                bolt.transform.localScale = new Vector3(0.08f * s * a, 2.2f * s, 0.08f * s * a);
                flash.transform.localScale = Vector3.one * (Mathf.Lerp(0.2f, 1.6f, u) * s * a);
                yield return null;
            }

            ArObjectUtil.Destroy(bolt);
            ArObjectUtil.Destroy(flash);
        }

        /// <summary>Dark Hole — collapsing black vortex on the street.</summary>
        IEnumerator DarkHoleRoutine(Vector3 pos)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var core = Prim(PrimitiveType.Sphere, "DarkHoleCore", pos + Vector3.up * 0.35f * s);
            ArFieldMaterials.Apply(core.GetComponent<MeshRenderer>(),
                new Color(0.04f, 0.02f, 0.08f, 1f));
            var ring = Prim(PrimitiveType.Cylinder, "DarkHoleRing", pos + Vector3.up * 0.04f);
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(),
                new Color(0.55f, 0.2f, 0.85f, 0.85f));

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.8f;
                var u = Mathf.Clamp01(t);
                var spin = u * 540f;
                core.transform.localScale = Vector3.one * (Mathf.Lerp(0.15f, 1.4f, u) * s * (1f - u * 0.35f));
                core.transform.rotation = Quaternion.Euler(90f, spin, 0f);
                var rr = Mathf.Lerp(0.2f, 1.8f, u) * s;
                ring.transform.localScale = new Vector3(rr, 0.02f * s, rr);
                yield return null;
            }

            ArObjectUtil.Destroy(core);
            ArObjectUtil.Destroy(ring);
        }

        /// <summary>Mirror Force — a wall of light-mirrors facing the attacker.</summary>
        IEnumerator MirrorForceRoutine(Vector3 pos, bool playerSide)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var yaw = playerSide ? 0f : 180f;
            var panes = new GameObject[5];
            for (var i = 0; i < panes.Length; i++)
            {
                var u = (i - 2) / 2f;
                var p = pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(u * 0.55f * s, 0.55f * s, 0.12f * s);
                panes[i] = Prim(PrimitiveType.Quad, "MirrorPane", p);
                panes[i].transform.rotation = Quaternion.Euler(0f, yaw, u * -12f);
                panes[i].transform.localScale = new Vector3(0.42f * s, 0.95f * s, 1f);
                ArFieldMaterials.ApplyAdditive(panes[i].GetComponent<MeshRenderer>(),
                    new Color(0.85f, 0.95f, 1f, 0.55f));
            }

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.7f;
                var a = Mathf.Clamp01(t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f);
                for (var i = 0; i < panes.Length; i++)
                {
                    if (panes[i] == null) continue;
                    panes[i].transform.localScale = new Vector3(0.42f * s, 0.95f * s * a, 1f);
                }

                yield return null;
            }

            for (var i = 0; i < panes.Length; i++)
                ArObjectUtil.Destroy(panes[i]);
        }

        /// <summary>Swords of Revealing Light — blades of light pinning the field.</summary>
        IEnumerator SwordsRoutine(Vector3 pos)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var swords = new GameObject[4];
            for (var i = 0; i < swords.Length; i++)
            {
                var ang = i * 90f + 20f;
                var off = Quaternion.Euler(0f, ang, 0f) * (Vector3.forward * 0.45f * s);
                swords[i] = Prim(PrimitiveType.Cube, "LightSword", pos + off + Vector3.up * 0.7f * s);
                swords[i].transform.rotation = Quaternion.Euler(18f, ang, 0f);
                swords[i].transform.localScale = new Vector3(0.06f * s, 1.4f * s, 0.06f * s);
                ArFieldMaterials.ApplyAdditive(swords[i].GetComponent<MeshRenderer>(),
                    new Color(1f, 0.95f, 0.55f, 0.8f));
            }

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.9f;
                var u = Mathf.Clamp01(t);
                var a = u < 0.2f ? u / 0.2f : 1f - (u - 0.2f) / 0.8f * 0.4f;
                for (var i = 0; i < swords.Length; i++)
                {
                    if (swords[i] == null) continue;
                    swords[i].transform.localScale = new Vector3(0.06f * s, 1.4f * s * Mathf.Clamp01(a), 0.06f * s);
                }

                yield return null;
            }

            for (var i = 0; i < swords.Length; i++)
                ArObjectUtil.Destroy(swords[i]);
        }

        /// <summary>Monster Reborn — grave-light column lifting a hologram.</summary>
        IEnumerator RebornRoutine(Vector3 pos, bool playerSide)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var accent = playerSide
                ? new Color(0.45f, 1f, 0.62f, 1f)
                : new Color(0.7f, 1f, 0.45f, 1f);
            var col = Prim(PrimitiveType.Cylinder, "RebornColumn", pos + Vector3.up * 0.05f);
            ArFieldMaterials.ApplyAdditive(col.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.7f));
            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.85f;
                var u = Mathf.Clamp01(t);
                var h = Mathf.Lerp(0.2f, 1.8f, u) * s;
                col.transform.position = pos + Vector3.up * (h * 0.5f);
                col.transform.localScale = new Vector3(0.22f * s * (1f - u * 0.4f), h * 0.5f, 0.22f * s * (1f - u * 0.4f));
                yield return null;
            }

            ArObjectUtil.Destroy(col);
        }

        // —— Attack archetypes (SOLID_VISION_BEHAVIOR.md §4.2): charge tells ——

        /// <summary>Blast: a spell orb gathering in front of the attacker, bigger with every pulse.</summary>
        IEnumerator ChargeOrbRoutine(Vector3 from, Vector3 to, Color tint, float charge01)
        {
            var s = HoloScale;
            var pos = from + Flat(to - from) * (0.14f * s);
            var orb = Prim(PrimitiveType.Sphere, "ChargeOrb", pos);
            ArFieldMaterials.ApplyAdditive(orb.GetComponent<MeshRenderer>(), new Color(tint.r, tint.g, tint.b, 0.8f));
            var core = Prim(PrimitiveType.Sphere, "ChargeOrbCore", pos);
            ArFieldMaterials.ApplyAdditive(core.GetComponent<MeshRenderer>(), new Color(1f, 1f, 0.97f, 0.9f));
            var size = Mathf.Lerp(0.07f, 0.22f, charge01) * s;

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.32f;
                var pulse = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                orb.transform.localScale = Vector3.one * (size * (0.7f + 0.5f * pulse));
                core.transform.localScale = Vector3.one * (size * 0.45f * pulse);
                yield return null;
            }

            ArObjectUtil.Destroy(orb);
            ArObjectUtil.Destroy(core);
        }

        /// <summary>Projectile: muzzle glints flickering at the attacker's front.</summary>
        IEnumerator GlintRoutine(Vector3 from, Vector3 to, Color tint, float charge01)
        {
            var s = HoloScale;
            var fwd = Flat(to - from);
            var right = Vector3.Cross(Vector3.up, fwd);
            var glints = new GameObject[2];
            for (var i = 0; i < glints.Length; i++)
            {
                var p = from + fwd * (0.16f * s) + right * (UnityEngine.Random.Range(-0.14f, 0.14f) * s) +
                        Vector3.up * (UnityEngine.Random.Range(-0.08f, 0.1f) * s);
                glints[i] = Prim(PrimitiveType.Sphere, "ChargeGlint", p);
                ArFieldMaterials.ApplyAdditive(glints[i].GetComponent<MeshRenderer>(),
                    new Color(tint.r, tint.g, tint.b, 0.9f));
            }

            var size = Mathf.Lerp(0.04f, 0.1f, charge01) * s;
            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.18f;
                var a = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * size;
                for (var i = 0; i < glints.Length; i++)
                    if (glints[i] != null)
                        glints[i].transform.localScale = new Vector3(a, a * 0.35f, a);
                yield return null;
            }

            for (var i = 0; i < glints.Length; i++)
                ArObjectUtil.Destroy(glints[i]);
        }

        /// <summary>Swarm: motes drawn in from all around the attacker.</summary>
        IEnumerator GatherRoutine(Vector3 from, Color tint, float charge01)
        {
            var s = HoloScale;
            var ps = MakeBurst(from, tint, Mathf.RoundToInt(Mathf.Lerp(6f, 16f, charge01)), 0.3f, 0.05f * s);
            var main = ps.main;
            main.gravityModifier = 0f;
            main.startSpeed = -1.2f * s; // emitted on the shell, flying inward
            var sh = ps.shape;
            sh.radius = 0.36f * s;
            sh.radiusThickness = 0f;
            ps.Play(true);
            ArObjectUtil.Destroy(ps.gameObject, 0.8f);
            yield break;
        }

        /// <summary>Lunge: a ring of light tightening under the attacker (the crouch before the spring).
        /// Surge (<paramref name="wide"/>): slow ripples running outward instead.</summary>
        IEnumerator GroundPulseRoutine(Vector3 ground, Color tint, float charge01, bool wide)
        {
            var s = HoloScale;
            var color = new Color(tint.r, tint.g, tint.b, 0.4f + 0.45f * charge01);
            var ring = RingPrim(wide ? "SurgeRipple" : "LungeCrouch", ground);
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(), color);
            var inner = (wide ? 0.3f : 0.25f) * s;
            var outer = (wide ? Mathf.Lerp(0.9f, 1.5f, charge01) : Mathf.Lerp(0.55f, 0.85f, charge01)) * s;
            var dur = wide ? 0.55f : 0.32f;

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                var u = Mathf.Clamp01(t);
                var e = 1f - (1f - u) * (1f - u);
                var r = wide ? Mathf.Lerp(inner, outer, e) : Mathf.Lerp(outer, inner, e);
                ring.transform.localScale = new Vector3(r, 1f, r);
                Fade(ring, color, wide ? 1f - u : Mathf.Sin(u * Mathf.PI));
                yield return null;
            }

            ArObjectUtil.Destroy(ring);
        }

        // —— Attack archetypes: releases ——

        /// <summary>Beam: a held stream, white-hot core in the monster's colour (Blue-Eyes' White Lightning).</summary>
        IEnumerator BeamReleaseRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var dir = to - from;
            var len = dir.magnitude;
            // Cylinder primitive: axis Y, height 2 at scale 1.
            var axis = Quaternion.LookRotation(dir / len, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            var width = Mathf.Lerp(0.06f, 0.16f, w) * s;
            var outerC = new Color(tint.r, tint.g, tint.b, 0.75f);
            var coreC = new Color(1f, 1f, 0.97f, 0.95f);

            var outer = Prim(PrimitiveType.Cylinder, "AttackBeamOuter", from);
            ArFieldMaterials.ApplyAdditive(outer.GetComponent<MeshRenderer>(), outerC);
            outer.transform.rotation = axis;
            var core = Prim(PrimitiveType.Cylinder, "AttackBeamCore", from);
            ArFieldMaterials.ApplyAdditive(core.GetComponent<MeshRenderer>(), coreC);
            core.transform.rotation = axis;
            var muzzle = Prim(PrimitiveType.Sphere, "AttackBeamMuzzle", from);
            ArFieldMaterials.ApplyAdditive(muzzle.GetComponent<MeshRenderer>(), outerC);

            const float extend = 0.1f;
            const float fade = 0.18f;
            var hold = 0.2f + 0.16f * w;
            var arrived = false;
            var t = 0f;
            while (t < extend + hold + fade)
            {
                t += Time.deltaTime;
                var reach = Mathf.Clamp01(t / extend);
                var head = Vector3.Lerp(from, to, reach);
                var thin = t < extend + hold ? 1f : 1f - Mathf.Clamp01((t - extend - hold) / fade);
                var flicker = 1f + 0.12f * Mathf.Sin(t * 60f);
                var half = Mathf.Max(0.001f, reach * len * 0.5f);
                var mid = (from + head) * 0.5f;
                outer.transform.position = mid;
                outer.transform.localScale = new Vector3(width * thin * flicker, half, width * thin * flicker);
                core.transform.position = mid;
                core.transform.localScale = new Vector3(width * 0.38f * thin, half, width * 0.38f * thin);
                muzzle.transform.localScale = Vector3.one * (width * 2.2f * thin);
                if (!arrived && reach >= 1f)
                {
                    arrived = true;
                    StartCoroutine(ArrivalRoutine(to, groundY, tint, w));
                }

                yield return null;
            }

            ArObjectUtil.Destroy(outer);
            ArObjectUtil.Destroy(core);
            ArObjectUtil.Destroy(muzzle);
        }

        /// <summary>Blast: the orb snaps full, arcs across trailing light, and bursts on the target.</summary>
        IEnumerator BlastRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var size = Mathf.Lerp(0.12f, 0.26f, w) * s;
            var orb = Prim(PrimitiveType.Sphere, "BlastOrb", from);
            ArFieldMaterials.ApplyAdditive(orb.GetComponent<MeshRenderer>(), new Color(tint.r, tint.g, tint.b, 0.85f));
            var core = Prim(PrimitiveType.Sphere, "BlastCore", from);
            ArFieldMaterials.ApplyAdditive(core.GetComponent<MeshRenderer>(), new Color(1f, 1f, 0.97f, 0.95f));
            var trail = MakeTrail(from, tint, 0.28f, size * 0.45f);
            var apex = Vector3.Lerp(from, to, 0.5f) + Vector3.up * ((0.18f + 0.06f * (to - from).magnitude) * s);

            const float gather = 0.07f;
            const float flight = 0.2f;
            var t = 0f;
            while (t < gather + flight)
            {
                t += Time.deltaTime;
                var p = from;
                var grow = 1f;
                if (t < gather)
                    grow = t / gather;
                else
                {
                    var u = Mathf.Clamp01((t - gather) / flight);
                    p = Vector3.Lerp(Vector3.Lerp(from, apex, u), Vector3.Lerp(apex, to, u), u);
                }

                orb.transform.position = p;
                core.transform.position = p;
                trail.transform.position = p;
                orb.transform.localScale = Vector3.one * (size * grow * (1f + 0.15f * Mathf.Sin(t * 50f)));
                core.transform.localScale = Vector3.one * (size * 0.5f * grow);
                yield return null;
            }

            ArObjectUtil.Destroy(orb);
            ArObjectUtil.Destroy(core);
            EndTrail(trail, 0.28f);
            yield return ArrivalRoutine(to, groundY, tint, w);
        }

        /// <summary>Lunge: the attacker's light streaks across and cuts an X through the target.</summary>
        IEnumerator LungeRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var fwd = (to - from).normalized;
            var look = Quaternion.LookRotation(fwd, Vector3.up);
            var thick = Mathf.Lerp(0.05f, 0.11f, w) * s;
            var streak = Prim(PrimitiveType.Cube, "LungeStreak", from);
            ArFieldMaterials.ApplyAdditive(streak.GetComponent<MeshRenderer>(), new Color(tint.r, tint.g, tint.b, 0.8f));
            streak.transform.rotation = look;

            const float dash = 0.13f;
            const float lag = 0.45f; // the tail leaves this far (fraction of the dash) behind the head
            var t = 0f;
            while (t < dash * (1f + lag))
            {
                t += Time.deltaTime;
                var hu = Mathf.Clamp01(t / dash);
                var tu = Mathf.Clamp01((t - dash * lag) / dash);
                var head = Vector3.Lerp(from, to, hu * hu);
                var tail = Vector3.Lerp(from, to, tu * tu);
                streak.transform.position = (head + tail) * 0.5f;
                streak.transform.localScale = new Vector3(thick, thick, Mathf.Max(0.01f, (head - tail).magnitude));
                yield return null;
            }

            ArObjectUtil.Destroy(streak);
            StartCoroutine(ArrivalRoutine(to, groundY, tint, w * 0.8f));

            // Two cuts across the target, in the plane facing the attacker; the second a beat after the first.
            var cutC = Color.Lerp(tint, Color.white, 0.5f);
            cutC.a = 0.95f;
            var cutLen = Mathf.Lerp(0.5f, 0.95f, w) * s;
            var cuts = new GameObject[2];
            for (var i = 0; i < cuts.Length; i++)
            {
                cuts[i] = Prim(PrimitiveType.Cube, "LungeCut", to - fwd * (0.05f * s));
                cuts[i].transform.rotation = look * Quaternion.Euler(0f, 0f, i == 0 ? 50f : -50f);
                cuts[i].transform.localScale = Vector3.zero;
                ArFieldMaterials.ApplyAdditive(cuts[i].GetComponent<MeshRenderer>(), cutC);
            }

            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.3f;
                var u = Mathf.Clamp01(t);
                for (var i = 0; i < cuts.Length; i++)
                {
                    if (cuts[i] == null) continue;
                    var ci = Mathf.Clamp01((u - i * 0.2f) / 0.8f);
                    var grow = Mathf.Clamp01(ci / 0.3f);
                    var fade = ci < 0.3f ? 1f : 1f - (ci - 0.3f) / 0.7f;
                    cuts[i].transform.localScale = new Vector3(0.028f * s * fade, cutLen * grow, 0.02f * s);
                }

                yield return null;
            }

            for (var i = 0; i < cuts.Length; i++)
                ArObjectUtil.Destroy(cuts[i]);
        }

        /// <summary>Projectile: a volley of bolts, more for a heavier attacker; the last one lands the hit.</summary>
        IEnumerator VolleyRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var n = 3 + Mathf.RoundToInt(2f * w);
            var right = Vector3.Cross(Vector3.up, Flat(to - from));
            for (var i = 0; i < n; i++)
            {
                var spread = right * (UnityEngine.Random.Range(-0.14f, 0.14f) * s) +
                             Vector3.up * (UnityEngine.Random.Range(-0.06f, 0.1f) * s);
                StartCoroutine(BoltRoutine(from + spread * 0.4f, to + spread, groundY, tint, w, i == n - 1));
                yield return new WaitForSeconds(0.055f);
            }
        }

        IEnumerator BoltRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w, bool last)
        {
            var s = HoloScale;
            var bolt = Prim(PrimitiveType.Cube, "VolleyBolt", from);
            ArFieldMaterials.ApplyAdditive(bolt.GetComponent<MeshRenderer>(), new Color(tint.r, tint.g, tint.b, 0.9f));
            bolt.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
            var bt = Mathf.Lerp(0.025f, 0.045f, w) * s;
            bolt.transform.localScale = new Vector3(bt, bt, 0.2f * s);

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.15f;
                bolt.transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }

            ArObjectUtil.Destroy(bolt);
            if (last)
                yield return ArrivalRoutine(to, groundY, tint, w);
            else
                yield return PopRoutine(to, tint, 0.16f * s);
        }

        /// <summary>Swarm: a streaming cloud of motes converging on the target.</summary>
        IEnumerator SwarmRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var dir = to - from;
            var len = dir.magnitude;
            const float flight = 0.3f;
            var count = Mathf.RoundToInt(Mathf.Lerp(36f, 80f, w));
            var ps = MakeBurst(from, tint, count, flight, Mathf.Lerp(0.035f, 0.055f, w) * s);
            ps.transform.rotation = Quaternion.LookRotation(dir / len, Vector3.up); // the cone emits along +Z
            var main = ps.main;
            main.gravityModifier = 0f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(len / flight * 0.9f, len / flight * 1.1f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 9f;
            sh.radius = 0.1f * s;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f * s;
            noise.frequency = 2.2f;
            noise.scrollSpeed = 1.5f;
            // Spread over 0.12 s so the cloud streams instead of popping.
            var first = count / 2;
            var second = count / 3;
            ps.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)first),
                new ParticleSystem.Burst(0.06f, (short)second),
                new ParticleSystem.Burst(0.12f, (short)(count - first - second))
            });
            ps.Play(true);
            ArObjectUtil.Destroy(ps.gameObject, flight + 0.6f);
            yield return new WaitForSeconds(flight);
            yield return ArrivalRoutine(to, groundY, tint, w);
        }

        /// <summary>Surge: a crest rolls along the street into the target and breaks in spray.</summary>
        IEnumerator SurgeRoutine(Vector3 from, Vector3 to, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var a = Ground(from, groundY);
            var b = Ground(to, groundY);
            var fwd = Flat(b - a);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            var width = Mathf.Lerp(0.55f, 0.95f, w) * s;
            var crest = Mathf.Lerp(0.28f, 0.5f, w) * s;
            var wave = Prim(PrimitiveType.Sphere, "SurgeWave", a);
            ArFieldMaterials.ApplyAdditive(wave.GetComponent<MeshRenderer>(), new Color(tint.r, tint.g, tint.b, 0.6f));
            wave.transform.rotation = rot;
            var foamC = Color.Lerp(tint, Color.white, 0.55f);
            foamC.a = 0.7f;
            var foam = Prim(PrimitiveType.Sphere, "SurgeFoam", a);
            ArFieldMaterials.ApplyAdditive(foam.GetComponent<MeshRenderer>(), foamC);
            foam.transform.rotation = rot;

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.24f;
                var u = Mathf.Clamp01(t);
                var p = Vector3.Lerp(a, b, u);
                var h = crest * Mathf.Lerp(0.3f, 1f, Mathf.Sin(Mathf.Min(1f, u * 1.3f) * Mathf.PI * 0.5f));
                wave.transform.position = p + Vector3.up * (h * 0.35f);
                wave.transform.localScale = new Vector3(width, h, 0.22f * s);
                foam.transform.position = p + Vector3.up * (h * 0.7f);
                foam.transform.localScale = new Vector3(width * 0.8f, h * 0.22f, 0.1f * s);
                yield return null;
            }

            ArObjectUtil.Destroy(wave);
            ArObjectUtil.Destroy(foam);

            var spray = MakeBurst(b, foamC, Mathf.RoundToInt(Mathf.Lerp(24f, 48f, w)), 0.55f, 0.06f * s);
            spray.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone up
            var sh = spray.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 28f;
            sh.radius = 0.15f * s;
            var main = spray.main;
            main.startSpeed = 2.2f * s;
            main.gravityModifier = 0.9f;
            spray.Play(true);
            ArObjectUtil.Destroy(spray.gameObject, 1.2f);
            yield return ArrivalRoutine(to, groundY, tint, w);
        }

        /// <summary>Every attack lands here: white flash, a burst of the monster's colour, a ring across the street.</summary>
        IEnumerator ArrivalRoutine(Vector3 pos, float groundY, Color tint, float w)
        {
            var s = HoloScale;
            var flash = Prim(PrimitiveType.Sphere, "ArrivalFlash", pos);
            ArFieldMaterials.ApplyAdditive(flash.GetComponent<MeshRenderer>(), new Color(1f, 1f, 0.95f, 0.95f));
            var glowC = new Color(tint.r, tint.g, tint.b, 0.7f);
            var glow = Prim(PrimitiveType.Sphere, "ArrivalGlow", pos);
            ArFieldMaterials.ApplyAdditive(glow.GetComponent<MeshRenderer>(), glowC);
            var ringC = new Color(tint.r, tint.g, tint.b, 0.85f);
            var ring = RingPrim("ArrivalRing", Ground(pos, groundY));
            ArFieldMaterials.ApplyAdditive(ring.GetComponent<MeshRenderer>(), ringC);
            var ps = MakeBurst(pos, tint, Mathf.RoundToInt(Mathf.Lerp(16f, 40f, w)), 0.5f, Mathf.Lerp(0.07f, 0.13f, w) * s);
            ps.Play(true);

            var peak = Mathf.Lerp(0.3f, 0.62f, w) * s;
            var ringMax = Mathf.Lerp(0.8f, 1.6f, w) * s;
            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.32f;
                var u = Mathf.Clamp01(t);
                var pop = 1f - (1f - u) * (1f - u);
                var flashA = u < 0.25f ? u / 0.25f : 1f - (u - 0.25f) / 0.75f;
                flash.transform.localScale = Vector3.one * (peak * 0.6f * flashA);
                glow.transform.localScale = Vector3.one * (peak * pop);
                Fade(glow, glowC, 1f - u);
                var rr = Mathf.Lerp(0.15f * s, ringMax, pop);
                ring.transform.localScale = new Vector3(rr, 1f, rr);
                Fade(ring, ringC, 1f - u);
                yield return null;
            }

            ArObjectUtil.Destroy(flash);
            ArObjectUtil.Destroy(glow);
            ArObjectUtil.Destroy(ring);
            if (ps != null) ArObjectUtil.Destroy(ps.gameObject, 1f);
        }

        /// <summary>Small flash where a non-final bolt lands.</summary>
        IEnumerator PopRoutine(Vector3 pos, Color tint, float size)
        {
            var pop = Prim(PrimitiveType.Sphere, "BoltPop", pos);
            ArFieldMaterials.ApplyAdditive(pop.GetComponent<MeshRenderer>(), new Color(tint.r, tint.g, tint.b, 0.9f));
            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.12f;
                pop.transform.localScale = Vector3.one * (size * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
                yield return null;
            }

            ArObjectUtil.Destroy(pop);
        }

        /// <summary>World-space mote trail left behind a moving emitter (move its transform each frame).</summary>
        ParticleSystem MakeTrail(Vector3 pos, Color c, float life, float size)
        {
            var ps = MakeBurst(pos, c, 64, life, size);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startSpeed = 0.15f;
            var em = ps.emission;
            em.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
            em.rateOverDistance = 90f;
            ps.Play(true);
            return ps;
        }

        static void EndTrail(ParticleSystem ps, float life)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ArObjectUtil.Destroy(ps.gameObject, life + 0.1f);
        }

        /// <summary>Flat annulus with the cylinder primitive's convention (x/z scale = diameter).</summary>
        GameObject RingPrim(string name, Vector3 pos)
        {
            var go = new GameObject(name) { layer = _layer };
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = RingMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>Unit ring, outer diameter 1, band 6% of the radius. One-sided; the FX materials cull nothing.</summary>
        static Mesh RingMesh()
        {
            if (_ringMesh != null) return _ringMesh;
            const int seg = 64;
            const float outer = 0.5f;
            const float inner = 0.47f;
            var v = new Vector3[seg * 2];
            var n = new Vector3[seg * 2];
            var uv = new Vector2[seg * 2];
            var col = new Color32[seg * 2];
            var tri = new int[seg * 6];
            for (var i = 0; i < seg; i++)
            {
                var a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d * inner;
                v[i * 2 + 1] = d * outer;
                n[i * 2] = n[i * 2 + 1] = Vector3.up;
                uv[i * 2] = new Vector2(i / (float)seg, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)seg, 1f);
                col[i * 2] = col[i * 2 + 1] = new Color32(255, 255, 255, 255);
                var j = (i + 1) % seg;
                tri[i * 6] = i * 2;
                tri[i * 6 + 1] = j * 2;
                tri[i * 6 + 2] = i * 2 + 1;
                tri[i * 6 + 3] = i * 2 + 1;
                tri[i * 6 + 4] = j * 2;
                tri[i * 6 + 5] = j * 2 + 1;
            }

            _ringMesh = new Mesh { name = "ArKaRing" };
            _ringMesh.vertices = v;
            _ringMesh.normals = n;
            _ringMesh.uv = uv;
            _ringMesh.colors32 = col;
            _ringMesh.triangles = tri;
            _ringMesh.RecalculateBounds();
            return _ringMesh;
        }

        /// <summary>
        /// Per-renderer fade on a shared additive material: WRLDZ/HoloUnlit reads _Color per renderer, and with
        /// SrcAlpha/One blending alpha is brightness. (Shared materials alone can only pop out.)
        /// </summary>
        static void Fade(GameObject go, Color c, float a01)
        {
            if (go == null) return;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            _mpb ??= new MaterialPropertyBlock();
            mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(a01)));
            mr.SetPropertyBlock(_mpb);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        static Vector3 Ground(Vector3 p, float groundY) => new(p.x, groundY + 0.02f, p.z);

        ParticleSystem MakeBurst(Vector3 pos, Color c, int count, float life, float size)
        {
            var go = new GameObject("ArenaBurst");
            go.layer = _layer;
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = life;
            main.startSize = size;
            main.startColor = c;
            main.maxParticles = count;
            main.loop = false;
            main.playOnAwake = false;
            main.gravityModifier = 0.35f;
            main.startSpeed = 1.4f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.12f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            // AddComponent<ParticleSystem> leaves the renderer without an explicit material (magenta /
            // invisible in a player build) and with maxParticleSize 0.5 — one mote near the lens can fill
            // half the screen. Unlit additive (tint comes from startColor) and a screen-size cap.
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = ArFieldMaterials.ParticleSoftAdditive();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.maxParticleSize = 0.06f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            return ps;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
