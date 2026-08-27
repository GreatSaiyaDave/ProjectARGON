using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Duel;

namespace WRLDZ.UI
{
    /// <summary>
    /// Free-roam hand card drag.
    /// · Short press → action popup
    /// · Drag onto M / S·T zone → play
    /// · Drag left/right within hand tray → manual rearrange (insert gap preview)
    /// · Miss → soft ease home
    /// </summary>
    public class HandCardDrag : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public CardInstance Card;
        public Action<CardInstance> OnClickPopup;
        public Action<CardInstance, bool /*monsterZone*/> OnDropOnDisk;
        /// <summary>Called when the player drops this card at a new hand index (0…count-1 among remaining, final list index after move).</summary>
        public Action<CardInstance, int /*newIndex*/> OnReorderInHand;
        public Action OnDragBegin;
        public Action OnDragEnd;

        /// <summary>Hand tray root (HorizontalLayoutGroup of hand cards).</summary>
        public RectTransform HandRow;

        RectTransform _rt;
        Canvas _rootCanvas;
        LayoutElement _layout;
        CanvasGroup _group;

        Vector2 _startScreen;
        Vector2 _homeAnchored;
        Vector3 _homeScale;
        int _homeSibling;
        /// <summary>Logical hand index among HandCardDrag peers before lift (not layout sibling after SetAsLastSibling).</summary>
        int _homeHandIndex = -1;
        Vector2 _grabOffsetLocal;
        bool _dragging;
        bool _dragArmed;
        bool _returning;
        int _previewInsert = -1;
        Coroutine _returnCo;

        // Peers we temporarily pulled out of layout for gap preview
        readonly List<LayoutElement> _previewPeers = new List<LayoutElement>(16);
        readonly List<Vector2> _previewHomes = new List<Vector2>(16);

        const float DragThresholdPx = 10f;
        const float DragScale = 1.12f;
        const float ReturnSpeed = 9f;
        const float GapShiftFrac = 0.42f;

        void Awake()
        {
            _rt = transform as RectTransform;
            _rootCanvas = GetComponentInParent<Canvas>();
            _layout = GetComponent<LayoutElement>();
            if (_layout == null) _layout = gameObject.AddComponent<LayoutElement>();
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            if (HandRow == null && _rt != null)
                HandRow = _rt.parent as RectTransform;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (_dragging || _dragArmed || _returning) return;
            OnClickPopup?.Invoke(Card);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Card == null || _rt == null || _returning) return;
            _startScreen = e.position;
            _dragArmed = true;
            _dragging = false;
            CaptureHome();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragArmed || Card == null || _rt == null) return;
            var dist = Vector2.Distance(_startScreen, e.position);
            if (!_dragging && dist < DragThresholdPx) return;

            if (!_dragging)
                BeginFreeRoam(e);

            if (!TryScreenToParentLocal(e.position, e.pressEventCamera, out var local))
                return;
            _rt.anchoredPosition = local + _grabOffsetLocal;

            // Live insert gap among remaining hand cards
            if (IsOverHandTray(e) || IsNearHandTray(e))
            {
                var insert = ComputeInsertIndex(e.position, e.pressEventCamera);
                if (insert != _previewInsert)
                {
                    _previewInsert = insert;
                    UpdateSiblingGapPreview(_previewInsert);
                }
            }
            else if (_previewInsert >= 0)
            {
                ClearSiblingGapPreview();
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragArmed) return;
            _dragArmed = false;

            if (!_dragging)
                return;

            _dragging = false;
            OnDragEnd?.Invoke();

            var previewAtDrop = _previewInsert;
            ClearSiblingGapPreview();

            // 1) Play on disk zone?
            var results = new List<RaycastResult>();
            if (EventSystem.current != null)
                EventSystem.current.RaycastAll(e, results);
            DiskDropZone zone = null;
            foreach (var r in results)
            {
                zone = r.gameObject.GetComponentInParent<DiskDropZone>();
                if (zone != null) break;
            }

            if (zone != null && Card != null)
            {
                RestoreLayoutFlags(keepLift: false);
                OnDropOnDisk?.Invoke(Card, zone.IsMonsterZone);
                return;
            }

            // 2) Reorder within / near hand tray
            // insert = index among *other* cards left of pointer = final list index after removal
            var insert = ComputeInsertIndex(e.position, e.pressEventCamera);
            if (insert < 0 && previewAtDrop >= 0)
                insert = previewAtDrop;

            if (insert >= 0 && OnReorderInHand != null && Card != null &&
                (IsOverHandTray(e) || IsNearHandTray(e)))
            {
                // Compare against logical home index (captured before SetAsLastSibling)
                var from = _homeHandIndex >= 0 ? _homeHandIndex : GetCurrentHandSiblingOrder();
                if (from >= 0 && insert != from)
                {
                    RestoreLayoutFlags(keepLift: false);
                    OnReorderInHand.Invoke(Card, insert);
                    return;
                }
            }

            // 3) Soft home
            if (_returnCo != null) StopCoroutine(_returnCo);
            _returnCo = StartCoroutine(EaseHomeThen(optionalPopup: false));
        }

        void BeginFreeRoam(PointerEventData e)
        {
            _dragging = true;
            WRLDZ.Presentation.WrldzAudio.PlayCardSlide();
            // Capture logical order BEFORE lifting to last sibling
            CaptureHome();
            _homeHandIndex = GetCurrentHandSiblingOrder();

            if (_layout != null)
            {
                _layout.ignoreLayout = true;
                if (_rt != null)
                {
                    _layout.preferredWidth = Mathf.Max(8f, _rt.rect.width);
                    _layout.preferredHeight = Mathf.Max(8f, _rt.rect.height);
                }
            }

            if (_group != null)
                _group.blocksRaycasts = false;

            transform.SetAsLastSibling();
            _rt.localScale = _homeScale * DragScale;

            if (TryScreenToParentLocal(e.position, e.pressEventCamera, out var fingerLocal))
                _grabOffsetLocal = _rt.anchoredPosition - fingerLocal;
            else
                _grabOffsetLocal = Vector2.zero;

            OnDragBegin?.Invoke();
        }

        void CaptureHome()
        {
            if (_rt == null) return;
            _homeAnchored = _rt.anchoredPosition;
            _homeScale = _rt.localScale.sqrMagnitude > 0.0001f ? _rt.localScale : Vector3.one;
            _homeSibling = transform.GetSiblingIndex();
        }

        /// <summary>
        /// Index among hand cards where the pointer would insert (0 = far left).
        /// Counts other cards whose center is left of the pointer — equals final
        /// list index after this card is removed from the hand.
        /// </summary>
        int ComputeInsertIndex(Vector2 screen, Camera eventCam)
        {
            var row = HandRow != null ? HandRow : _rt?.parent as RectTransform;
            if (row == null) return -1;

            var cam = _rootCanvas != null && _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : eventCam;

            var insert = 0;
            for (var i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i) as RectTransform;
                if (child == null || child == _rt) continue;
                var other = child.GetComponent<HandCardDrag>();
                if (other == null || other.Card == null) continue;

                var world = child.TransformPoint(child.rect.center);
                Vector2 otherScreen = RectTransformUtility.WorldToScreenPoint(cam, world);
                if (screen.x >= otherScreen.x)
                    insert++;
            }

            return insert;
        }

        int GetCurrentHandSiblingOrder()
        {
            var row = HandRow != null ? HandRow : _rt?.parent as RectTransform;
            if (row == null || Card == null) return -1;
            var idx = 0;
            for (var i = 0; i < row.childCount; i++)
            {
                var d = row.GetChild(i).GetComponent<HandCardDrag>();
                if (d == null || d.Card == null) continue;
                if (d == this) return idx;
                idx++;
            }

            return -1;
        }

        bool IsOverHandTray(PointerEventData e)
        {
            var row = HandRow != null ? HandRow : _rt?.parent as RectTransform;
            if (row == null) return false;

            if (EventSystem.current != null)
            {
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(e, results);
                foreach (var r in results)
                {
                    if (r.gameObject == null) continue;
                    if (r.gameObject.transform == row || r.gameObject.transform.IsChildOf(row))
                        return true;
                }
            }

            return PointInExpandedHandRect(e.position, e.pressEventCamera, expandY: 0.55f, padX: 24f);
        }

        /// <summary>Slightly larger band so short vertical flicks still count as rearrange.</summary>
        bool IsNearHandTray(PointerEventData e)
        {
            return PointInExpandedHandRect(e.position, e.pressEventCamera, expandY: 1.1f, padX: 48f);
        }

        bool PointInExpandedHandRect(Vector2 screen, Camera eventCam, float expandY, float padX)
        {
            var row = HandRow != null ? HandRow : _rt?.parent as RectTransform;
            if (row == null) return false;
            var cam = _rootCanvas != null && _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : eventCam;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    row, screen, cam, out var local))
                return false;
            var rect = row.rect;
            var h = Mathf.Max(8f, rect.height);
            rect.yMin -= h * expandY * 0.45f;
            rect.yMax += h * expandY * 0.55f;
            rect.xMin -= padX;
            rect.xMax += padX;
            return rect.Contains(local);
        }

        /// <summary>Nudge other cards sideways to show the insertion gap while dragging.</summary>
        void UpdateSiblingGapPreview(int insert)
        {
            var row = HandRow != null ? HandRow : _rt?.parent as RectTransform;
            if (row == null || insert < 0)
            {
                ClearSiblingGapPreview();
                return;
            }

            EnsurePreviewPeersPulled(row);

            var slot = _layout != null && _layout.preferredWidth > 1f
                ? _layout.preferredWidth + 6f
                : EstimateSlotWidth(row);
            var shift = slot * GapShiftFrac;

            var handIdx = 0;
            var peerI = 0;
            for (var i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i) as RectTransform;
                if (child == null || child == _rt) continue;
                var other = child.GetComponent<HandCardDrag>();
                if (other == null || other.Card == null) continue;
                if (peerI >= _previewPeers.Count || peerI >= _previewHomes.Count) break;

                var home = _previewHomes[peerI];
                // Open a gap at insert: cards at/after insert slide right
                var x = home.x + (handIdx >= insert ? shift : 0f);
                child.anchoredPosition = new Vector2(x, home.y);
                handIdx++;
                peerI++;
            }
        }

        void EnsurePreviewPeersPulled(RectTransform row)
        {
            if (_previewPeers.Count > 0) return;

            for (var i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i) as RectTransform;
                if (child == null || child == _rt) continue;
                var other = child.GetComponent<HandCardDrag>();
                if (other == null || other.Card == null) continue;

                var le = child.GetComponent<LayoutElement>();
                if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
                // Freeze preferred size so ignoreLayout doesn't collapse the card
                if (child.rect.width > 1f)
                {
                    le.preferredWidth = child.rect.width;
                    le.preferredHeight = child.rect.height;
                }

                le.ignoreLayout = true;
                _previewPeers.Add(le);
                _previewHomes.Add(child.anchoredPosition);
            }
        }

        float EstimateSlotWidth(RectTransform row)
        {
            var n = 0;
            float w = 0f;
            for (var i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i) as RectTransform;
                if (child == null || child == _rt) continue;
                if (child.GetComponent<HandCardDrag>() == null) continue;
                w += Mathf.Max(40f, child.rect.width);
                n++;
            }

            if (n <= 0) return 110f;
            return w / n + 6f;
        }

        void ClearSiblingGapPreview()
        {
            _previewInsert = -1;
            for (var i = 0; i < _previewPeers.Count; i++)
            {
                var le = _previewPeers[i];
                if (le == null) continue;
                le.ignoreLayout = false;
                // Home positions restored by layout rebuild; leave layout in control
            }

            _previewPeers.Clear();
            _previewHomes.Clear();

            // Force layout to settle peers back into tray slots
            var row = HandRow != null ? HandRow : _rt?.parent as RectTransform;
            if (row != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(row);
        }

        IEnumerator EaseHomeThen(bool optionalPopup)
        {
            _returning = true;
            if (_rt == null)
            {
                RestoreLayoutFlags(keepLift: false);
                _returning = false;
                yield break;
            }

            var startPos = _rt.anchoredPosition;
            var startScale = _rt.localScale;
            var t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * ReturnSpeed;
                var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                _rt.anchoredPosition = Vector2.Lerp(startPos, _homeAnchored, u);
                _rt.localScale = Vector3.Lerp(startScale, _homeScale, u);
                yield return null;
            }

            _rt.anchoredPosition = _homeAnchored;
            _rt.localScale = _homeScale;
            transform.SetSiblingIndex(Mathf.Clamp(_homeSibling, 0,
                transform.parent != null ? transform.parent.childCount - 1 : 0));
            RestoreLayoutFlags(keepLift: false);
            _returning = false;
            _returnCo = null;

            if (optionalPopup && Card != null)
                OnClickPopup?.Invoke(Card);
        }

        void RestoreLayoutFlags(bool keepLift)
        {
            if (_layout != null)
                _layout.ignoreLayout = false;
            if (_group != null)
                _group.blocksRaycasts = true;
            if (!keepLift && _rt != null)
                _rt.localScale = _homeScale.sqrMagnitude > 0.0001f ? _homeScale : Vector3.one;
        }

        bool TryScreenToParentLocal(Vector2 screen, Camera eventCam, out Vector2 local)
        {
            local = default;
            if (_rt == null || _rt.parent == null) return false;
            var parent = _rt.parent as RectTransform;
            if (parent == null) return false;
            var cam = _rootCanvas != null && _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : eventCam;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screen, cam, out local);
        }

        void OnDisable()
        {
            if (_returnCo != null)
            {
                StopCoroutine(_returnCo);
                _returnCo = null;
            }

            _dragging = false;
            _dragArmed = false;
            _returning = false;
            ClearSiblingGapPreview();
            RestoreLayoutFlags(keepLift: false);
            _homeHandIndex = -1;
        }

        void OnDestroy()
        {
            if (_returnCo != null) StopCoroutine(_returnCo);
            ClearSiblingGapPreview();
        }
    }

    /// <summary>Drop target on your duel-disk zone rows (monsters or S/T).</summary>
    public class DiskDropZone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public bool IsMonsterZone;
        Image _highlight;

        public void EnsureHighlight()
        {
            if (_highlight != null) return;
            var go = new GameObject("DropHighlight", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _highlight = go.GetComponent<Image>();
            _highlight.sprite = UiFoundation.WhiteSprite();
            _highlight.color = new Color(0.2f, 0.9f, 1f, 0f);
            _highlight.raycastTarget = false;
            go.transform.SetAsFirstSibling();
        }

        public void SetHot(bool on)
        {
            EnsureHighlight();
            if (_highlight != null)
                _highlight.color = on
                    ? new Color(0.25f, 0.95f, 1f, 0.28f)
                    : new Color(0.2f, 0.9f, 1f, 0f);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (e.pointerDrag != null && e.pointerDrag.GetComponent<HandCardDrag>() != null)
                SetHot(true);
        }

        public void OnPointerExit(PointerEventData e) => SetHot(false);
    }
}
