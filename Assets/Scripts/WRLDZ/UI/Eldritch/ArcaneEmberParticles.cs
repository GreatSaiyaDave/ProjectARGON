using UnityEngine;
using UnityEngine.Rendering;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// World-space rune-ember ring for the AR disk hub. Real <see cref="ParticleSystem"/> —
    /// only valid outside overlay canvases (see <see cref="UiEmberField"/> for UI).
    /// </summary>
    public static class ArcaneEmberParticles
    {
        public static ParticleSystem Create(Transform parent, Color accent, float radius = 0.06f,
            int layer = 28, bool loop = true)
        {
            var go = new GameObject("ArcaneEmbers");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = layer;

            var ps = go.AddComponent<ParticleSystem>();
            // A fresh system may already be running (playOnAwake): duration/loop cannot be set live.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.duration = loop ? 2f : 1.6f;
            main.loop = loop;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var hot = new Color(accent.r * 1.35f, accent.g * 1.35f, accent.b * 1.35f, 1f); // HDR lift
            main.startColor = new ParticleSystem.MinMaxGradient(hot, accent);
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = -0.012f; // gentle rise on top of the velocity module

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 24f;

            // contract note: DiskRoot is thin along Y (hub normal = +Y), so the ring lies in XZ.
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = Mathf.Max(0.005f, radius);
            sh.radiusThickness = 0.18f;
            sh.arc = 360f;
            sh.arcMode = ParticleSystemShapeMultiModeValue.Random;
            sh.rotation = new Vector3(90f, 0f, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            // All three axes TwoConstants (mixed Constant/TwoConstants spams "Particle Velocity curves must all be in the same mode").
            ArObjectUtil.SetVelocityRange(vel,
                new Vector3(0f, 0.025f, 0f), new Vector3(0f, 0.07f, 0f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 0.6f),
                    new GradientColorKey(new Color(0.6f, 0.5f, 0.9f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0.7f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.15f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.018f);
            noise.frequency = 0.9f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.35f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.alignment = ParticleSystemRenderSpace.View;
            rend.sortMode = ParticleSystemSortMode.Distance;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = LightProbeUsage.Off;
            rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
            rend.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            rend.minParticleSize = 0f;
            rend.maxParticleSize = 0.05f;

            var mat = MakeMaterial(accent);
            rend.sharedMaterial = mat;
            var owner = go.AddComponent<ArcaneEmberMaterialOwner>();
            owner.Mat = mat;

            main.playOnAwake = true; // prefab instances start on their own
            if (Application.isPlaying) ps.Play(true);
            return ps;
        }

        /// <summary>Soft ember sprite on Sprites/Default (URP renders it as SRPDefaultUnlit); solid fallback.</summary>
        static Material MakeMaterial(Color accent)
        {
            var emberSpr = EldritchAssets.EmberSoft();
            var tex = emberSpr != null ? emberSpr.texture : null;
            var sprShader = Shader.Find("Sprites/Default");
            if (tex != null && sprShader != null)
            {
                var m = new Material(sprShader) { name = "ArcaneEmberSprite" };
                m.mainTexture = tex;
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                m.renderQueue = (int)RenderQueue.Transparent + 10;
                return m;
            }

            var solid = ArAnimePresentation.MakeSolid(accent, "ArcaneEmberSolid");
            solid.renderQueue = (int)RenderQueue.Transparent + 10;
            return solid;
        }
    }

    /// <summary>Destroys the per-instance ember material with its ParticleSystem (edit-mode safe).</summary>
    public sealed class ArcaneEmberMaterialOwner : MonoBehaviour
    {
        public Material Mat;

        void OnDestroy()
        {
            if (Mat != null)
            {
                ArObjectUtil.Destroy(Mat);
                Mat = null;
            }
        }
    }
}
