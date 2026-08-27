using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Opponent's public field as a floating mini-playmat to the left of
    /// the player's M1 hologram. Face-up shows legal art; face-down stays a
    /// card back. Tap a face-up slot for full public card text.
    /// Valid for human and AI opponents.
    /// </summary>
    [DefaultExecutionOrder(810)]
    public class ArOppFieldGlance : MonoBehaviour
    {
        public const int SlotCountExpected = 11;

        const int CanvasW = 720;
        const int CanvasH = 400;
        public const float WorldWidth = 0.72f;

        public Transform Root { get; private set; }
        public int SlotCount => _slots != null ? _slots.Length : 0;

        public ArDuelDiskRig PlayerDisk;
        public Transform ArenaRoot;
        int _layer;
        Canvas _canvas;
        CanvasGroup _cg;
        Image[] _slots;
        CardInstance[] _cards;
        BoxCollider _hit;

        public static ArOppFieldGlance Create(Transform parent, int layer)
        {
            var go = new GameObject("OppFieldGlance");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var g = go.AddComponent<ArOppFieldGlance>();
            g._layer = layer;
            g.Build();
            return g;
        }

        void Build()
        {
            FreeUiKit.EnsureLoaded();
            Root = transform;

            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup));
            plate.transform.SetParent(transform, false);
            plate.layer = _layer;
            var rt = plate.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            var canvas = plate.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 88;
            canvas.worldCamera = ArStageView.FindCamera(transform);
            _canvas = canvas;
            _cg = plate.GetComponent<CanvasGroup>();
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 1f;

            _hit = plate.AddComponent<BoxCollider>();
            _hit.size = new Vector3(CanvasW, CanvasH, 24f);
            _hit.center = Vector3.zero;
            var marker = plate.AddComponent<ArOppGlanceHit>();
            marker.Glance = this;

            var matBg = plate.gameObject.AddComponent<Image>();
            matBg.sprite = UiFoundation.WhiteSprite();
            matBg.color = new Color(0.03f, 0.06f, 0.10f, 0.32f);
            matBg.raycastTarget = false;
            var glass = ImagineAssets.HudIslandGlass();
            if (glass != null)
            {
                var frame = new GameObject("MatFrame", typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(plate.transform, false);
                frame.layer = _layer;
                var frt = frame.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero;
                frt.offsetMax = Vector2.zero;
                var fi = frame.GetComponent<Image>();
                fi.sprite = glass;
                fi.color = Color.white;
                fi.raycastTarget = false;
            }

            _slots = new Image[SlotCountExpected];
            _cards = new CardInstance[SlotCountExpected];
            // Mini playmat: opponent ST away (top), monsters toward you (bottom).
            for (var i = 0; i < 5; i++)
                MakeSlot(plate.transform, 5 + i, 0.16f + i * 0.16f, 0.58f, 0.30f + i * 0.16f, 0.94f);
            for (var i = 0; i < 5; i++)
                MakeSlot(plate.transform, i, 0.16f + i * 0.16f, 0.10f, 0.30f + i * 0.16f, 0.52f);
            MakeSlot(plate.transform, 10, 0.02f, 0.62f, 0.14f, 0.92f);

            transform.localScale = Vector3.one * (WorldWidth / CanvasW);
        }

        void MakeSlot(Transform parent, int index, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("Slot" + index, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            var srt = go.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(x0, y0);
            srt.anchorMax = new Vector2(x1, y1);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = new Color(0.12f, 0.18f, 0.24f, 0.35f);
            img.raycastTarget = false;
            go.SetActive(true);
            _slots[index] = img;
        }

        public void Sync(DuelistState opp, CardDatabase db)
        {
            if (_slots == null) return;
            var shown = 0;
            if (opp != null)
            {
                for (var i = 0; i < 5; i++)
                {
                    var c = opp.MonsterZones != null && i < opp.MonsterZones.Length
                        ? opp.MonsterZones[i].Occupant : null;
                    if (Paint(i, c, db)) shown++;
                }

                for (var i = 0; i < 5; i++)
                {
                    var c = opp.SpellTrapZones != null && i < opp.SpellTrapZones.Length
                        ? opp.SpellTrapZones[i].Occupant : null;
                    if (Paint(5 + i, c, db)) shown++;
                }

                if (Paint(10, opp.FieldSpellZone?.Occupant, db)) shown++;
            }
            else
            {
                for (var i = 0; i < _slots.Length; i++)
                    Paint(i, null, db);
            }

            if (_cg != null)
                _cg.alpha = 1f;
        }

        bool Paint(int index, CardInstance card, CardDatabase db)
        {
            if (index < 0 || index >= _slots.Length) return false;
            var img = _slots[index];
            if (img == null) return false;
            if (_cards != null && index < _cards.Length)
                _cards[index] = card;
            if (card == null)
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0.12f, 0.18f, 0.24f, 0.35f);
                return false;
            }
            if (card.FaceUp && db != null)
            {
                var art = db.GetArt(card.CardId);
                img.sprite = art != null ? art : (YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite());
            }
            else
            {
                img.sprite = StreamingSprite.CardBack()
                             ?? YgoCardFrames.CardBack()
                             ?? UiFoundation.WhiteSprite();
            }

            img.color = Color.white;
            return true;
        }

        /// <summary>
        /// Public field pick from the AR stage ray. Face-up cards return the
        /// instance (full legal text). Face-down returns the set card without
        /// revealing its face — caller must OpenInspect(showFace: false).
        /// </summary>
        public bool TryPick(Ray ray, out CardInstance card, out bool publicFace)
        {
            card = null;
            publicFace = false;
            if (_hit == null || _slots == null) return false;
            if (!_hit.Raycast(ray, out var hit, 24f)) return false;
            var best = float.MaxValue;
            var bestI = -1;
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;
                var lp = slot.rectTransform.InverseTransformPoint(hit.point);
                if (!slot.rectTransform.rect.Contains(new Vector2(lp.x, lp.y))) continue;
                var d = (hit.point - slot.transform.position).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                bestI = i;
            }

            if (bestI < 0 || _cards == null || _cards[bestI] == null) return false;
            card = _cards[bestI];
            publicFace = card.FaceUp;
            return true;
        }

        void LateUpdate()
        {
            if (_canvas != null)
            {
                var cam = ArStageView.FindCamera(transform);
                if (cam != null) _canvas.worldCamera = cam;
            }

            PlaceLeftOfM1();
        }

        void PlaceLeftOfM1()
        {
            Vector3 pos;
            if (ArenaRoot != null)
            {
                var m1 = ArenaRoot.TransformPoint(ArPlaymatLayout.MonsterArenaLocal(0, true));
                var left = -ArenaRoot.right;
                pos = m1 + left * (WorldWidth * 0.5f + 0.32f) + Vector3.up * 0.46f;
            }
            else if (PlayerDisk != null && PlayerDisk.DiskRoot != null)
            {
                pos = PlayerDisk.DiskRoot.TransformPoint(new Vector3(-0.42f, 0.22f, 0.12f));
            }
            else
                return;

            transform.position = pos;
            var cam = ArStageView.FindCamera(transform);
            transform.rotation = ArStageView.UiFacing(pos, cam);
        }
    }
}
