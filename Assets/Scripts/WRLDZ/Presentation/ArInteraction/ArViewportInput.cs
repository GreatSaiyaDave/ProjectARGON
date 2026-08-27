using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Bridges UI pointer events on the AR RawImage into <see cref="ArCardDragSystem"/>.
    /// Fixes Editor/S23 testing where Update-only mouse polling misses overlay UI focus.
    /// </summary>
    public class ArViewportInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler,
        IPointerExitHandler
    {
        public ArCardDragSystem Drag;
        public Camera StageCamera;
        public RectTransform ViewportRect;
        public ArFreeLookCamera FreeLook;

        public static ArViewportInput Attach(RawImage raw, ArCardDragSystem drag, Camera stageCam)
        {
            if (raw == null || drag == null) return null;
            raw.raycastTarget = true;
            var bridge = raw.gameObject.GetComponent<ArViewportInput>()
                         ?? raw.gameObject.AddComponent<ArViewportInput>();
            bridge.Drag = drag;
            bridge.StageCamera = stageCam;
            bridge.ViewportRect = raw.rectTransform;
            drag.ViewportRect = raw.rectTransform;
            drag.StageCamera = stageCam;
            bridge.FreeLook = stageCam != null
                ? stageCam.GetComponent<ArFreeLookCamera>()
                : null;
            return bridge;
        }

        public void OnPointerDown(PointerEventData e)
        {
            Drag?.BeginFromScreen(e.position);
            FreeLook?.OnPointerDown(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (Drag != null)
            {
                Drag.PointerMovedFromScreen(e.position);
                if (Drag.OwnsPointer)
                    return;
            }

            FreeLook?.OnPointerDrag(e.position);
        }

        public void OnPointerUp(PointerEventData e)
        {
            Drag?.EndFromScreen(e.position);
            FreeLook?.OnPointerUp();
        }

        public void OnPointerExit(PointerEventData e)
        {
            // Don't cancel mid-drag when slightly leaving — EndFromScreen handles release
        }
    }
}
