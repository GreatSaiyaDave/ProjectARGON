using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Click bridge on the AR <see cref="RawImage"/>: screen point → RT viewport → stage ray →
    /// first <see cref="ArDiskHubHit"/> in the collider's parents. Lives beside
    /// <see cref="ArViewportInput"/> (which owns down/drag/up) and never touches the drag system.
    /// </summary>
    public sealed class ArDiskHubTapRouter : MonoBehaviour, IPointerClickHandler
    {
        public Camera StageCamera;
        public RectTransform ViewportRect;
        /// <summary>Optional — clicks the drag system owns are ignored.</summary>
        public ArDuelInteractionSystem Interaction;
        public Action OnHubTapped;
        /// <summary>Zone Mode DUEL chip. Preferred over the broad hub hit when both are under the ray.</summary>
        public Action OnDuelMode;
        public float MaxRayDistance = 14f;
        /// <summary>Press→release travel (screen px) above which the click is treated as a drag.</summary>
        public float TapSlopPx = 28f;

        public ArDiskHubHit LastHit { get; private set; }

        public static ArDiskHubTapRouter Attach(RawImage raw, Camera stageCam, Action onHubTapped,
            ArDuelInteractionSystem interaction = null)
        {
            if (raw == null) return null;
            raw.raycastTarget = true;
            var router = raw.gameObject.GetComponent<ArDiskHubTapRouter>();
            if (router == null) router = raw.gameObject.AddComponent<ArDiskHubTapRouter>();
            router.StageCamera = stageCam;
            router.ViewportRect = raw.rectTransform;
            router.OnHubTapped = onHubTapped;
            router.Interaction = interaction;
            return router;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (eventData.dragging) return;
            if ((eventData.position - eventData.pressPosition).sqrMagnitude > TapSlopPx * TapSlopPx) return;

            var drag = Interaction != null ? Interaction.Drag : null;
            if (drag != null && drag.OwnsPointer) return;

            if (TryRaycastDuelButton(eventData.position, eventData.pressEventCamera))
            {
                OnDuelMode?.Invoke();
                return;
            }

            if (!TryRaycastHub(eventData.position, eventData.pressEventCamera, out var hit)) return;
            LastHit = hit;
            OnHubTapped?.Invoke();
        }

        public bool TryRaycastDuelButton(Vector2 screenPos, Camera uiCamera)
        {
            if (!TryScreenToRay(screenPos, uiCamera, out var ray)) return false;
            var hits = Physics.RaycastAll(ray, MaxRayDistance, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var duel = h.collider.GetComponentInParent<ArDiskDuelHit>();
                if (duel != null && duel.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
        }

        /// <summary>Public so the session can drive taps from tests / wearable controllers.</summary>
        public bool TryRaycastHub(Vector2 screenPos, Camera uiCamera, out ArDiskHubHit hit)
        {
            hit = null;
            if (!TryScreenToRay(screenPos, uiCamera, out var ray)) return false;

            var hits = Physics.RaycastAll(ray, MaxRayDistance, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var hub = h.collider.GetComponentInParent<ArDiskHubHit>();
                if (hub == null || !hub.gameObject.activeInHierarchy) continue;
                hit = hub;
                return true;
            }

            return false;
        }

        public bool TryScreenToRay(Vector2 screenPos, Camera uiCamera, out Ray ray)
        {
            ray = default;
            if (StageCamera == null) return false;

            if (ViewportRect == null)
            {
                ray = StageCamera.ScreenPointToRay(screenPos);
                return true;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    ViewportRect, screenPos, uiCamera, out var local))
                return false;

            var rect = ViewportRect.rect;
            if (rect.width < 1f || rect.height < 1f) return false;
            // local is pivot-relative; rect.min is the pivot-relative bottom-left
            var u = (local.x - rect.xMin) / rect.width;
            var v = (local.y - rect.yMin) / rect.height;
            if (u < -0.02f || u > 1.02f || v < -0.02f || v > 1.02f) return false;
            ray = StageCamera.ViewportPointToRay(new Vector3(Mathf.Clamp01(u), Mathf.Clamp01(v), 0f));
            return true;
        }
    }
}
