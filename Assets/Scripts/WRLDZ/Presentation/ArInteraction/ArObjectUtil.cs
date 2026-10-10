using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Edit-mode safe Object helpers for EditorSim / headless AR smoke
    /// (<see cref="Application.isPlaying"/> false still builds the full graph).
    /// </summary>
    public static class ArObjectUtil
    {
        /// <summary>
        /// <see cref="Object.Destroy"/> in play mode; <see cref="Object.DestroyImmediate"/> in edit mode.
        /// </summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        /// <summary>
        /// Existing component or a new one. Never write <c>GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()</c>:
        /// <c>??</c> skips Unity's overloaded null check, so in the Editor a "fake null" GetComponent result is kept,
        /// nothing is added, and the first native access throws MissingComponentException (UNT0007).
        /// </summary>
        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            if (go == null) return null;
            var c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }

        /// <summary>Destroy delayed only works in play mode; edit mode destroys immediately.</summary>
        public static void Destroy(Object obj, float t)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj, t);
            else
                Object.DestroyImmediate(obj);
        }

        /// <summary>
        /// Constant velocity on all three axes. Unity rejects a VelocityOverLifetime module whose
        /// x/y/z curves mix modes ("Particle Velocity curves must all be in the same mode") and
        /// logs it every simulated frame — always set the three axes together through these helpers.
        /// </summary>
        public static void SetVelocity(ParticleSystem.VelocityOverLifetimeModule vel, Vector3 v)
        {
            vel.x = new ParticleSystem.MinMaxCurve(v.x);
            vel.y = new ParticleSystem.MinMaxCurve(v.y);
            vel.z = new ParticleSystem.MinMaxCurve(v.z);
        }

        /// <summary>Random-between-two-constants velocity; all three axes in TwoConstants mode.</summary>
        public static void SetVelocityRange(ParticleSystem.VelocityOverLifetimeModule vel, Vector3 min, Vector3 max)
        {
            vel.x = new ParticleSystem.MinMaxCurve(min.x, max.x);
            vel.y = new ParticleSystem.MinMaxCurve(min.y, max.y);
            vel.z = new ParticleSystem.MinMaxCurve(min.z, max.z);
        }

        /// <summary>Constant orbital velocity on all three axes (same-mode rule as <see cref="SetVelocity"/>).</summary>
        public static void SetOrbital(ParticleSystem.VelocityOverLifetimeModule vel, Vector3 v)
        {
            vel.orbitalX = new ParticleSystem.MinMaxCurve(v.x);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(v.y);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(v.z);
        }

        /// <summary>Random-between-two-constants orbital velocity; all three axes in TwoConstants mode.</summary>
        public static void SetOrbitalRange(ParticleSystem.VelocityOverLifetimeModule vel, Vector3 min, Vector3 max)
        {
            vel.orbitalX = new ParticleSystem.MinMaxCurve(min.x, max.x);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(min.y, max.y);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(min.z, max.z);
        }
    }
}
