using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Classic 2D card flip on a UI card (scale-X squash): shows back → face mid-spin.
    /// Attach after building a face-up field button that just flipped from set.
    /// </summary>
    public class UiCardFlipAnimator : MonoBehaviour
    {
        RectTransform _rt;
        Image _rootImg;
        Coroutine _co;
        Vector3 _baseScale = Vector3.one;

        public static UiCardFlipAnimator Ensure(GameObject go)
        {
            if (go == null) return null;
            var a = go.GetComponent<UiCardFlipAnimator>();
            if (a == null) a = go.AddComponent<UiCardFlipAnimator>();
            a.Cache();
            return a;
        }

        void Cache()
        {
            _rt = transform as RectTransform;
            _rootImg = GetComponent<Image>();
            if (_rt != null)
                _baseScale = _rt.localScale.sqrMagnitude > 0.0001f
                    ? _rt.localScale
                    : Vector3.one;
        }

        /// <summary>
        /// Animate from card-back to current face content already built on this GO.
        /// Hides face children until midpoint, then reveals them.
        /// </summary>
        public void PlayFlipToFace(CardInstance card, CardDatabase db, string cardNameForHold = null)
        {
            if (_co != null) StopCoroutine(_co);
            Cache();
            DuelPresentationPacer.HoldCardFlip(cardNameForHold ?? card?.Name);
            _co = StartCoroutine(FlipCo());
        }

        /// <summary>
        /// Flip Summon: squash-flip to the face (still Defense / landscape), then
        /// rotate into Attack. Same beats as the disk toaster flip + position turn.
        /// </summary>
        public void PlayFlipSummon(CardInstance card, CardDatabase db, string cardNameForHold = null)
        {
            if (_co != null) StopCoroutine(_co);
            Cache();
            DuelPresentationPacer.HoldFlipSummon(cardNameForHold ?? card?.Name);
            _co = StartCoroutine(FlipSummonCo());
        }

        /// <summary>Short scale pop for a new face-up summon (not a true flip).</summary>
        public void PlaySummonPop(string cardName = null)
        {
            if (_co != null) StopCoroutine(_co);
            Cache();
            DuelPresentationPacer.HoldSummonOrActivate(cardName);
            _co = StartCoroutine(PopCo());
        }

        IEnumerator FlipCo()
        {
            if (_rt == null) yield break;
            var dur = Mathf.Max(0.25f, DuelPresentationPacer.FlipDuration);
            var half = dur * 0.5f;

            // Hide art / labels during first half (looks like a solid back)
            SetFaceChildrenVisible(false);
            var back = YgoCardFrames.CardBack()
                       ?? StreamingSprite.CardBack()
                       ?? ImagineAssets.CardBackWrldz();
            var savedSprite = _rootImg != null ? _rootImg.sprite : null;
            var savedColor = _rootImg != null ? _rootImg.color : Color.white;
            if (_rootImg != null)
            {
                _rootImg.sprite = back ?? UiFoundation.WhiteSprite();
                _rootImg.color = Color.white;
                _rootImg.preserveAspect = true;
            }

            // Shrink X to edge
            var t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / half);
                var e = 1f - (1f - u) * (1f - u);
                var sx = Mathf.Lerp(_baseScale.x, 0.02f, e);
                _rt.localScale = new Vector3(sx, _baseScale.y, _baseScale.z);
                yield return null;
            }

            // Midpoint: show face
            if (_rootImg != null)
            {
                _rootImg.sprite = savedSprite;
                _rootImg.color = savedColor;
            }

            SetFaceChildrenVisible(true);

            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / half);
                var e = u * u;
                var sx = Mathf.Lerp(0.02f, _baseScale.x, e);
                _rt.localScale = new Vector3(sx, _baseScale.y, _baseScale.z);
                yield return null;
            }

            _rt.localScale = _baseScale;
            _co = null;
        }

        IEnumerator FlipSummonCo()
        {
            if (_rt == null) yield break;

            // Start landscape so the ATK turn is a visible 90°
            var defR = Quaternion.Euler(0f, 0f, 90f);
            var atkR = Quaternion.identity;
            _rt.localRotation = defR;

            var dur = Mathf.Max(0.25f, ArDiskMotion.FlipSummonReveal);
            var half = dur * 0.5f;

            SetFaceChildrenVisible(false);
            var back = YgoCardFrames.CardBack()
                       ?? StreamingSprite.CardBack()
                       ?? ImagineAssets.CardBackWrldz();
            var savedSprite = _rootImg != null ? _rootImg.sprite : null;
            var savedColor = _rootImg != null ? _rootImg.color : Color.white;
            if (_rootImg != null)
            {
                _rootImg.sprite = back ?? UiFoundation.WhiteSprite();
                _rootImg.color = Color.white;
                _rootImg.preserveAspect = true;
            }

            var t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / half);
                var e = 1f - (1f - u) * (1f - u);
                _rt.localScale = new Vector3(Mathf.Lerp(_baseScale.x, 0.02f, e), _baseScale.y,
                    _baseScale.z);
                yield return null;
            }

            if (_rootImg != null)
            {
                _rootImg.sprite = savedSprite;
                _rootImg.color = savedColor;
            }

            SetFaceChildrenVisible(true);

            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / half);
                var e = u * u;
                _rt.localScale = new Vector3(Mathf.Lerp(0.02f, _baseScale.x, e), _baseScale.y,
                    _baseScale.z);
                yield return null;
            }

            _rt.localScale = _baseScale;

            if (ArDiskMotion.FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(ArDiskMotion.FaceFlipHold);

            // Rotate landscape DEF → portrait ATK
            t = 0f;
            var turn = Mathf.Max(0.08f, ArDiskMotion.FlipSummonTurn);
            while (t < turn)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / turn);
                var e = k * k * (3f - 2f * k);
                _rt.localRotation = Quaternion.Slerp(defR, atkR, e);
                yield return null;
            }

            _rt.localRotation = atkR;
            _co = null;
        }

        IEnumerator PopCo()
        {
            if (_rt == null) yield break;
            _rt.localScale = _baseScale * 0.15f;
            var t = 0f;
            const float dur = 0.4f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(t / dur);
                var e = 1f - Mathf.Pow(1f - u, 3f);
                _rt.localScale = Vector3.Lerp(_baseScale * 0.15f, _baseScale, e);
                yield return null;
            }

            _rt.localScale = _baseScale;
            _co = null;
        }

        void SetFaceChildrenVisible(bool visible)
        {
            for (var i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c == null) continue;
                // Only hide face content (Art, labels, badges) — keep structure intact
                var n = c.name;
                if (n == "Art" || n == "LabelBg" || n == "Name" || n == "StatBadge" ||
                    n.StartsWith("Badge") || n.StartsWith("Art"))
                    c.gameObject.SetActive(visible);
            }
        }
    }
}
