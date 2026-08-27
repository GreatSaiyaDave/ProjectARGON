using System.Collections;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Physical-feeling pacing for Battle City disk card motion.
    /// Nothing should pop instant — pad snaps, toaster slots, and flips all run solid beats.
    /// </summary>
    public static class ArDiskMotion
    {
        // ── Durations (seconds, unscaled) ───────────────────────────────────
        public const float PadSnap = 0.48f;
        /// <summary>Card presents above the plate, then drops to the slot mouth.</summary>
        public const float SlotInsertPresent = 0.34f;
        /// <summary>Aligns at the mouth before the pocket slide.</summary>
        public const float SlotInsertApproach = 0.30f;
        /// <summary>Visible push into the pocket (10% tip remains).</summary>
        public const float SlotInsertSlide = 0.58f;
        public const float SlotInsertTotal =
            SlotInsertPresent + SlotInsertApproach + SlotInsertSlide;
        /// <summary>LateUpdate hold so the zone does not stomp the insert.</summary>
        public const float SlotInsertHold = SlotInsertTotal + 0.18f;
        /// <summary>Magnet catch — last few centimeters snap harder.</summary>
        public const float MagneticCatch = 0.16f;
        public const float ToasterEject = 0.55f;
        public const float ToasterFlip = 0.70f;
        public const float ToasterReseat = 0.52f;
        public const float ToasterToGy = 0.85f;
        public const float FaceFlipHold = 0.12f;
        public const float MonsterLeave = 0.52f;
        public const float PositionTurn = 0.42f;
        /// <summary>Flip Summon: short lift off the pad before the reveal.</summary>
        public const float FlipSummonLift = 0.22f;
        /// <summary>Flip Summon: back → art (same beat as toaster flip).</summary>
        public const float FlipSummonReveal = ToasterFlip;
        /// <summary>Flip Summon: landscape DEF → portrait ATK (same beat as position change).</summary>
        public const float FlipSummonTurn = PositionTurn;
        /// <summary>Battle flip: same reveal beat as the toaster flip (no ATK turn).</summary>
        public const float BattleFlipReveal = ToasterFlip;
        /// <summary>Battle flip (arena only): set card stands up into ATK artwork.</summary>
        public const float BattleFlipRise = PositionTurn;
        public const float BattleFlipDiskTotal = BattleFlipReveal + FaceFlipHold;
        public const float BattleFlipArenaTotal =
            BattleFlipReveal + FaceFlipHold + BattleFlipRise;
        /// <summary>
        /// Lift + FlipY (includes mid-edge hold) + a beat on the revealed face + ATK turn.
        /// </summary>
        public const float FlipSummonTotal =
            FlipSummonLift + FlipSummonReveal + FaceFlipHold + FaceFlipHold + FlipSummonTurn;
        /// <summary>Thumb-peel off the magazine (readable, not a pop).</summary>
        public const float DrawLift = 0.42f;
        /// <summary>Arc from the live deck pose into the hand fan.</summary>
        public const float DrawTravel = 1.05f;
        /// <summary>Next card starts after the previous peel is clearly in the air.</summary>
        public const float DrawStagger = 0.52f;

        /// <summary>1/duration style speed for <see cref="ArFloatingCard"/> snaps.</summary>
        public const float FloatingSnapSpeed = 1.65f;

        public static IEnumerator LerpLocal(Transform t, Vector3 p0, Vector3 p1,
            Quaternion r0, Quaternion r1, Vector3 s0, Vector3 s1, float duration)
        {
            yield return MagneticLerp(t, p0, p1, r0, r1, s0, s1, duration);
        }

        /// <summary>
        /// Smoothstep travel — the whole path is readable (no cubic slam).
        /// Used for presenting a card at the slot mouth.
        /// </summary>
        public static IEnumerator VisibleLerp(Transform t, Vector3 p0, Vector3 p1,
            Quaternion r0, Quaternion r1, Vector3 s0, Vector3 s1, float duration)
        {
            if (t == null) yield break;
            var dur = Mathf.Max(0.08f, duration);
            var u = 0f;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / dur;
                var k = Mathf.Clamp01(u);
                var e = k * k * (3f - 2f * k);
                t.localPosition = Vector3.Lerp(p0, p1, e);
                t.localRotation = Quaternion.Slerp(r0, r1, e);
                t.localScale = Vector3.Lerp(s0, s1, e);
                yield return null;
            }

            if (t == null) yield break;
            t.localPosition = p1;
            t.localRotation = r1;
            t.localScale = s1;
        }

        /// <summary>
        /// Pocket slide: starts moving immediately so the card is seen going in,
        /// then eases into the 10% tip seat.
        /// </summary>
        public static IEnumerator SlotSlideLerp(Transform t, Vector3 p0, Vector3 p1,
            Quaternion r0, Quaternion r1, Vector3 s0, Vector3 s1, float duration)
        {
            if (t == null) yield break;
            var dur = Mathf.Max(0.08f, duration);
            var u = 0f;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / dur;
                var k = Mathf.Clamp01(u);
                // Ease-out cubic: push is visible from frame 1, settles at the peek
                var e = 1f - Mathf.Pow(1f - k, 3f);
                t.localPosition = Vector3.Lerp(p0, p1, e);
                t.localRotation = Quaternion.Slerp(r0, r1, e);
                t.localScale = Vector3.Lerp(s0, s1, e);
                yield return null;
            }

            if (t == null) yield break;
            t.localPosition = p1;
            t.localRotation = r1;
            t.localScale = s1;
        }

        /// <summary>
        /// Ease-in pull that accelerates into the pad/slot like a magnet, then seats hard.
        /// </summary>
        public static IEnumerator MagneticLerp(Transform t, Vector3 p0, Vector3 p1,
            Quaternion r0, Quaternion r1, Vector3 s0, Vector3 s1, float duration)
        {
            if (t == null) yield break;
            var dur = Mathf.Max(0.08f, duration);
            var u = 0f;
            var pull = p1 - p0;
            var overshoot = pull.sqrMagnitude > 1e-8f
                ? pull.normalized * Mathf.Min(0.010f, pull.magnitude * 0.10f)
                : Vector3.zero;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / dur;
                var k = Mathf.Clamp01(u);
                // Ease-in cubic: hover, then magnet-slam into the pad
                var e = 1f - Mathf.Pow(1f - k, 3f);
                var pos = k < 0.88f
                    ? Vector3.Lerp(p0, p1 + overshoot, e)
                    : Vector3.Lerp(p1 + overshoot, p1, (k - 0.88f) / 0.12f);

                t.localPosition = pos;
                t.localRotation = Quaternion.Slerp(r0, r1, e);
                t.localScale = Vector3.Lerp(s0, s1, e);
                yield return null;
            }

            if (t == null) yield break;
            t.localPosition = p1;
            t.localRotation = r1;
            t.localScale = s1;
        }

        public static IEnumerator LerpLocalPosRot(Transform t, Vector3 p0, Vector3 p1,
            Quaternion r0, Quaternion r1, float duration)
        {
            if (t == null) yield break;
            var s = t.localScale;
            yield return LerpLocal(t, p0, p1, r0, r1, s, s, duration);
        }

        /// <summary>Y-axis flip of a card root while swapping face at the midpoint.</summary>
        public static IEnumerator FlipY(Transform t, System.Action atMidpoint, float duration)
        {
            if (t == null) yield break;
            var half = Mathf.Max(0.1f, duration * 0.5f);
            var baseRot = t.localRotation;
            var baseScale = t.localScale;
            // Thin + yaw to edge-on
            var u = 0f;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / half;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                t.localRotation = baseRot * Quaternion.Euler(0f, Mathf.Lerp(0f, 90f, e), 0f);
                t.localScale = new Vector3(
                    Mathf.Lerp(baseScale.x, baseScale.x * 0.12f, e),
                    baseScale.y,
                    baseScale.z);
                yield return null;
            }

            atMidpoint?.Invoke();
            if (FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(FaceFlipHold);

            u = 0f;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / half;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                t.localRotation = baseRot * Quaternion.Euler(0f, Mathf.Lerp(90f, 180f, e), 0f);
                t.localScale = new Vector3(
                    Mathf.Lerp(baseScale.x * 0.12f, baseScale.x, e),
                    baseScale.y,
                    baseScale.z);
                yield return null;
            }

            if (t == null) yield break;
            t.localRotation = baseRot;
            t.localScale = baseScale;
        }

        /// <summary>
        /// Face flip that stays flush on the pad: squash local X, swap art at the
        /// edge, unsquash. Does not yaw or roll the card (no ATK/DEF turn).
        /// </summary>
        public static IEnumerator FlipInPlace(Transform t, System.Action atMidpoint, float duration)
        {
            if (t == null) yield break;
            var half = Mathf.Max(0.1f, duration * 0.5f);
            var basePos = t.localPosition;
            var baseRot = t.localRotation;
            var baseScale = t.localScale;
            var u = 0f;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / half;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                t.localPosition = basePos;
                t.localRotation = baseRot;
                t.localScale = new Vector3(
                    Mathf.Lerp(baseScale.x, baseScale.x * 0.08f, e),
                    baseScale.y,
                    baseScale.z);
                yield return null;
            }

            atMidpoint?.Invoke();
            if (FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(FaceFlipHold);

            u = 0f;
            while (u < 1f)
            {
                if (t == null) yield break;
                u += Time.unscaledDeltaTime / half;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                t.localPosition = basePos;
                t.localRotation = baseRot;
                t.localScale = new Vector3(
                    Mathf.Lerp(baseScale.x * 0.08f, baseScale.x, e),
                    baseScale.y,
                    baseScale.z);
                yield return null;
            }

            if (t == null) yield break;
            t.localPosition = basePos;
            t.localRotation = baseRot;
            t.localScale = baseScale;
        }
    }
}
