using System.Collections;
using UnityEngine;
using WRLDZ.Duel;

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
        /// Anime Ka projection at the arena landing: ground rings + light column
        /// as a hologram materializes. Never plays on the disk plate.
        /// Scales with <see cref="ArPlaymatLayout.LiveHoloScale"/>.
        /// </summary>
        public static void PlayHoloMaterialize(Vector3 worldPos, bool playerSide, bool isMonster,
            int layer = 28)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null, layer);
            fx.StartCoroutine(fx.HoloMaterializeRoutine(worldPos, playerSide, isMonster));
        }

        public static void PlayAttackBeam(Vector3 from, Vector3 to, bool playerSide, float charge01)
        {
            if (!Application.isPlaying) return;
            var fx = Ensure(null);
            fx.StartCoroutine(fx.BeamRoutine(from, to, playerSide, charge01));
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

        IEnumerator HoloMaterializeRoutine(Vector3 pos, bool playerSide, bool isMonster)
        {
            var s = Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            var accent = playerSide
                ? new Color(0.35f, 0.92f, 1f, 1f)
                : new Color(1f, 0.42f, 0.58f, 1f);
            var white = new Color(1f, 1f, 0.97f, 1f);
            var dur = isMonster ? 0.92f : 0.58f;
            var colH = (isMonster ? 1.92f : 0.62f) * s;
            var ringMax = (isMonster ? 1.85f : 0.72f) * s;

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

            var ps = MakeBurst(pos + Vector3.up * 0.35f * s, accent,
                isMonster ? 48 : 22, 0.7f, isMonster ? 0.14f * s : 0.07f * s);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 18f;
            sh.radius = 0.08f * s;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(1.6f * s);
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

                var flashPop = Mathf.Sin(Mathf.Clamp01(u * 1.35f) * Mathf.PI);
                flash.transform.localScale = Vector3.one * (Mathf.Lerp(0.12f, 0.85f, flashPop) * s * fade);

                yield return null;
            }

            ArObjectUtil.Destroy(core);
            ArObjectUtil.Destroy(column);
            ArObjectUtil.Destroy(ring);
            ArObjectUtil.Destroy(ring2);
            ArObjectUtil.Destroy(flash);
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

        IEnumerator BeamRoutine(Vector3 from, Vector3 to, bool playerSide, float charge01)
        {
            var accent = playerSide
                ? new Color(1f, 0.85f, 0.25f, 1f)
                : new Color(1f, 0.45f, 0.35f, 1f);
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
            ArFieldMaterials.Apply(beam.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.35f + charge01 * 0.55f));

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "ChargeOrb";
            ball.layer = _layer;
            ArObjectUtil.Destroy(ball.GetComponent<Collider>());
            ball.transform.position = from + Vector3.up * 0.25f;
            var bs = Mathf.Lerp(0.08f, 0.2f, charge01);
            ball.transform.localScale = Vector3.one * bs;
            ArFieldMaterials.Apply(ball.GetComponent<MeshRenderer>(),
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
            ArFieldMaterials.Apply(flash.GetComponent<MeshRenderer>(),
                new Color(1f, 1f, 0.9f, 0.95f));

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ImpactRing";
            ring.layer = _layer;
            ArObjectUtil.Destroy(ring.GetComponent<Collider>());
            ring.transform.position = pos + Vector3.up * 0.03f;
            ring.transform.localScale = new Vector3(0.2f, 0.01f, 0.2f);
            ArFieldMaterials.Apply(ring.GetComponent<MeshRenderer>(),
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
            ArFieldMaterials.Apply(shield.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.45f));

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "ShieldRim";
            rim.layer = _layer;
            ArObjectUtil.Destroy(rim.GetComponent<Collider>());
            rim.transform.position = pos + Vector3.up * 0.05f;
            rim.transform.localScale = new Vector3(0.35f, 0.01f, 0.35f);
            ArFieldMaterials.Apply(rim.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.65f));

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
            ArFieldMaterials.Apply(ring.GetComponent<MeshRenderer>(),
                new Color(accent.r, accent.g, accent.b, 0.75f));

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "SpellCastOrb";
            orb.layer = _layer;
            ArObjectUtil.Destroy(orb.GetComponent<Collider>());
            orb.transform.position = pos + Vector3.up * 0.08f;
            orb.transform.localScale = Vector3.one * 0.06f;
            ArFieldMaterials.Apply(orb.GetComponent<MeshRenderer>(),
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
            vel.y = new ParticleSystem.MinMaxCurve(-1.8f * s);
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
            return ps;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
