using System.Collections;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// World-space Y-axis flip for disk / arena card meshes when a set card is revealed.
    /// </summary>
    public class CardFlip3dAnimator : MonoBehaviour
    {
        Coroutine _co;
        Quaternion _restLocalRot = Quaternion.identity;
        Transform _body;

        public static void PlayIfNeeded(Transform root, bool faceChangedToUp, CardDatabase db = null)
        {
            if (root == null || !faceChangedToUp) return;
            var a = root.GetComponent<CardFlip3dAnimator>() ?? root.gameObject.AddComponent<CardFlip3dAnimator>();
            a.Play();
        }

        public void Play()
        {
            if (_co != null) StopCoroutine(_co);
            _restLocalRot = transform.localRotation;
            // Prefer flipping the visual body (card surface) if present
            _body = transform.Find("CardSurface")
                    ?? transform.Find("ArtworkBillboard")
                    ?? transform.Find("LargeCardFace")
                    ?? transform.Find("CardBackHolo")
                    ?? transform;
            DuelPresentationPacer.HoldCardFlip(gameObject.name);
            _co = StartCoroutine(FlipCo());
        }

        IEnumerator FlipCo()
        {
            var target = _body != null ? _body : transform;
            var baseScale = target.localScale;
            var dur = Mathf.Max(0.2f, DuelPresentationPacer.FlipDuration);
            var half = dur * 0.5f;
            var t = 0f;

            // Spin Y 0 → 90 while thinning
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / half);
                var e = 1f - (1f - u) * (1f - u);
                target.localRotation = Quaternion.Euler(0f, Mathf.Lerp(0f, 90f, e), 0f);
                target.localScale = new Vector3(
                    Mathf.Lerp(baseScale.x, baseScale.x * 0.15f, e),
                    baseScale.y,
                    baseScale.z);
                yield return null;
            }

            // Second half 90 → 180 (reads as flip complete); restore scale
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / half);
                var e = u * u;
                target.localRotation = Quaternion.Euler(0f, Mathf.Lerp(90f, 180f, e), 0f);
                target.localScale = new Vector3(
                    Mathf.Lerp(baseScale.x * 0.15f, baseScale.x, e),
                    baseScale.y,
                    baseScale.z);
                yield return null;
            }

            target.localRotation = Quaternion.identity;
            target.localScale = baseScale;
            transform.localRotation = _restLocalRot;
            _co = null;
        }
    }
}
