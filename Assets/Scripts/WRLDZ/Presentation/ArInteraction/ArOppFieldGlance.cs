using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Opponent's public field as a floating mini-playmat to the left of
    /// the player's M1 hologram. Header lip recreates the overlay score
    /// islands (YOU cyan LP + D/GY/EX, gold phase + pips, OPP magenta LP).
    /// Face-up shows legal art; face-down stays a card back.
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
        public bool IsTargeting => _targeting;

        public ArDuelDiskRig PlayerDisk;
        public Transform ArenaRoot;
        int _layer;
        Canvas _canvas;
        CanvasGroup _cg;
        Image[] _slots;
        CardInstance[] _cards;
        Text _oppName;
        Text _oppLp;
        Text _youName;
        Text _youLp;
        Text _deckCount;
        Text _gyCount;
        Text _extraCount;
        Text _phaseLabel;
        Text _turnChip;
        Image _pillMp1;
        Image _pillBattle;
        Image _pillMp2;
        Image _pillEnd;
        Color _youLpBase = DuelystUi.Cyan;
        Color _oppLpBase = DuelystUi.Magenta;
        int _lastYouLp = int.MinValue;
        int _lastOppLp = int.MinValue;
        float _youPulse;
        float _oppPulse;
        BoxCollider _hit;
        readonly HashSet<int> _legalTargetIds = new();
        bool _targeting;

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
            BuildClusterChrome(plate.transform);
            // Mini playmat: ST away (top), monsters toward you. Score islands own the header lip.
            for (var i = 0; i < 5; i++)
                MakeSlot(plate.transform, 5 + i, 0.16f + i * 0.16f, 0.46f, 0.30f + i * 0.16f, 0.785f);
            for (var i = 0; i < 5; i++)
                MakeSlot(plate.transform, i, 0.16f + i * 0.16f, 0.04f, 0.30f + i * 0.16f, 0.42f);
            MakeSlot(plate.transform, 10, 0.02f, 0.50f, 0.14f, 0.76f);

            transform.localScale = Vector3.one * (WorldWidth / CanvasW);
        }

        void BuildClusterChrome(Transform plate)
        {
            // Same TCG grammar as the overlay islands: YOU cyan · gold phase · OPP magenta.
            var youIsland = LcdIsland(plate, "YouScore", 0.012f, 0.808f, 0.278f, 0.992f,
                new Color(DuelystUi.Cyan.r, DuelystUi.Cyan.g, DuelystUi.Cyan.b, 0.50f));
            _youName = MakeLabel(youIsland.transform, "Who", "YOU", 11, TextAnchor.MiddleLeft,
                DuelystUi.TextMuted, bestFit: true);
            Place(_youName.rectTransform, 0.07f, 0.58f, 0.42f, 0.94f);
            _youLp = MakeLabel(youIsland.transform, "LP", "8000", 20, TextAnchor.MiddleRight,
                DuelystUi.Cyan, display: true);
            Place(_youLp.rectTransform, 0.38f, 0.42f, 0.95f, 0.96f);
            _deckCount = MakeLabel(youIsland.transform, "Deck", "D—", 11, TextAnchor.MiddleLeft,
                DuelystUi.TextCream);
            Place(_deckCount.rectTransform, 0.07f, 0.06f, 0.36f, 0.44f);
            _gyCount = MakeLabel(youIsland.transform, "GY", "GY 0", 11, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.72f, 0.95f, 1f));
            Place(_gyCount.rectTransform, 0.36f, 0.06f, 0.66f, 0.44f);
            _extraCount = MakeLabel(youIsland.transform, "EX", "EX 0", 11, TextAnchor.MiddleRight,
                DuelystUi.Gold);
            Place(_extraCount.rectTransform, 0.66f, 0.06f, 0.95f, 0.44f);

            var phase = LcdIsland(plate, "Phase", 0.292f, 0.808f, 0.708f, 0.992f,
                new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.48f));
            _phaseLabel = MakeLabel(phase.transform, "PhaseTxt", "YOU · MAIN PHASE 1", 14,
                TextAnchor.MiddleCenter, DuelystUi.GoldHot, display: true, bestFit: true);
            Place(_phaseLabel.rectTransform, 0.04f, 0.46f, 0.96f, 0.94f);
            _turnChip = MakeLabel(phase.transform, "Turn", "T1", 11, TextAnchor.MiddleLeft,
                DuelystUi.TextMuted);
            Place(_turnChip.rectTransform, 0.06f, 0.08f, 0.22f, 0.44f);
            var pips = new GameObject("Pips", typeof(RectTransform));
            pips.transform.SetParent(phase.transform, false);
            pips.layer = _layer;
            Place(pips.GetComponent<RectTransform>(), 0.24f, 0.06f, 0.94f, 0.40f);
            _pillMp1 = Pip(pips.transform, "MP1", "M1", 0.00f, 0.24f);
            _pillBattle = Pip(pips.transform, "BP", "BP", 0.26f, 0.50f);
            _pillMp2 = Pip(pips.transform, "MP2", "M2", 0.52f, 0.76f);
            _pillEnd = Pip(pips.transform, "EP", "EP", 0.78f, 1.00f);

            var oppIsland = LcdIsland(plate, "OppScore", 0.722f, 0.808f, 0.988f, 0.992f,
                new Color(DuelystUi.Magenta.r, DuelystUi.Magenta.g, DuelystUi.Magenta.b, 0.50f));
            _oppName = MakeLabel(oppIsland.transform, "Who", "OPP", 11, TextAnchor.MiddleRight,
                DuelystUi.TextMuted, bestFit: true);
            Place(_oppName.rectTransform, 0.48f, 0.58f, 0.94f, 0.94f);
            _oppLp = MakeLabel(oppIsland.transform, "LP", "8000", 20, TextAnchor.MiddleLeft,
                DuelystUi.Magenta, display: true);
            Place(_oppLp.rectTransform, 0.06f, 0.12f, 0.70f, 0.78f);
        }

        public void SyncChrome(string youName, int youLp, string oppName, int oppLp,
            DuelPhase phase, bool youToMove, int turn, int deck, int gy, int extra)
        {
            if (_oppName != null)
                _oppName.text = string.IsNullOrEmpty(oppName) ? "OPP" : oppName;
            youLp = Mathf.Max(0, youLp);
            oppLp = Mathf.Max(0, oppLp);
            if (_oppLp != null)
                _oppLp.text = oppLp.ToString();
            if (_lastOppLp != int.MinValue && oppLp != _lastOppLp)
                _oppPulse = 1f;
            _lastOppLp = oppLp;
            _oppLpBase = oppLp <= 0 ? DuelystUi.Danger
                : oppLp < 2000 ? new Color(1f, 0.55f, 0.35f, 1f)
                : DuelystUi.Magenta;

            if (_youName != null)
                _youName.text = string.IsNullOrEmpty(youName) ? "YOU" : youName;
            if (_youLp != null)
                _youLp.text = youLp.ToString();
            if (_lastYouLp != int.MinValue && youLp != _lastYouLp)
                _youPulse = 1f;
            _lastYouLp = youLp;
            _youLpBase = youLp <= 0 ? DuelystUi.Danger
                : youLp < 2000 ? DuelystUi.Magenta
                : DuelystUi.Cyan;

            if (_deckCount != null) _deckCount.text = "D" + deck;
            if (_gyCount != null) _gyCount.text = "GY " + gy;
            if (_extraCount != null) _extraCount.text = "EX " + extra;

            var phaseName = FormatPhase(phase);
            var who = youToMove ? "YOU" : "OPP";
            if (_phaseLabel != null)
            {
                _phaseLabel.text = who + " · " + phaseName;
                _phaseLabel.color = phase == DuelPhase.Battle ? DuelystUi.Magenta : DuelystUi.GoldHot;
            }
            if (_turnChip != null)
                _turnChip.text = turn > 0 ? "T" + turn : "";
            PaintPip(_pillMp1, phase == DuelPhase.Main1, new Color(0.2f, 0.7f, 0.95f, 0.95f));
            PaintPip(_pillBattle, phase == DuelPhase.Battle, new Color(0.9f, 0.35f, 0.4f, 0.95f));
            PaintPip(_pillMp2, phase == DuelPhase.Main2, new Color(0.25f, 0.75f, 0.9f, 0.95f));
            PaintPip(_pillEnd, phase == DuelPhase.End, new Color(0.85f, 0.55f, 0.25f, 0.95f));
        }

        static string FormatPhase(DuelPhase phase)
        {
            return phase switch
            {
                DuelPhase.Draw => "DRAW PHASE",
                DuelPhase.Standby => "STANDBY PHASE",
                DuelPhase.Main1 => "MAIN PHASE 1",
                DuelPhase.Battle => "BATTLE PHASE",
                DuelPhase.Main2 => "MAIN PHASE 2",
                DuelPhase.End => "END PHASE",
                DuelPhase.GameOver => "DUEL OVER",
                _ => phase.ToString().ToUpperInvariant()
            };
        }

        static void PaintPip(Image img, bool active, Color hotTint)
        {
            if (img == null) return;
            var pip = active ? ImagineAssets.HudPipActive() : ImagineAssets.HudPipIdle();
            if (pip != null)
            {
                img.sprite = pip;
                img.color = Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = active ? hotTint : new Color(0.12f, 0.15f, 0.20f, 0.75f);
            }
            var label = img.GetComponentInChildren<Text>();
            if (label != null)
                label.color = active
                    ? Color.white
                    : new Color(0.65f, 0.7f, 0.78f, 0.9f);
        }

        GameObject LcdIsland(Transform parent, string name, float x0, float y0, float x1, float y1,
            Color edge)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var lcd = ImagineAssets.HudIslandGlass();
            if (lcd != null)
            {
                img.sprite = lcd;
                img.type = Image.Type.Simple;
                img.color = Color.white;
                HubChrome.HideFilament(img.transform);
            }
            else
                HubChrome.QuietFill(img, new Color(0.03f, 0.04f, 0.09f, 0.88f), edge);
            return go;
        }

        Image Pip(Transform parent, string name, string label, float x0, float x1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            Place(go.GetComponent<RectTransform>(), x0, 0.08f, x1, 0.92f);
            var img = go.GetComponent<Image>();
            var idle = ImagineAssets.HudPipIdle();
            if (idle != null)
            {
                img.sprite = idle;
                img.color = Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0.06f, 0.07f, 0.12f, 0.92f);
                HubChrome.FilamentRim(go.transform, DuelystUi.Gold, px: 1f);
            }
            img.raycastTarget = false;
            var t = MakeLabel(go.transform, "L", label, 9, TextAnchor.MiddleCenter, DuelystUi.TextCream);
            Place(t.rectTransform, 0f, 0f, 1f, 1f);
            return img;
        }

        static Text MakeLabel(Transform parent, string name, string text, int size, TextAnchor align,
            Color color, bool display = false, bool bestFit = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: display, heavyOutline: true);
            t.text = text ?? "";
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = bestFit ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (bestFit)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = Mathf.Max(8, size - 5);
                t.resizeTextMaxSize = size;
            }
            return t;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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

        /// <summary>
        /// Restrict the mini-playmat to the cards that can currently be chosen.
        /// Empty / non-legal slots remain visible as a dim field silhouette, while
        /// legal cards retain their art and become the only pickable targets.
        /// </summary>
        public void SetTargeting(IReadOnlyCollection<CardInstance> legalTargets)
        {
            _legalTargetIds.Clear();
            if (legalTargets != null)
                foreach (var card in legalTargets)
                    if (card != null) _legalTargetIds.Add(card.InstanceId);
            _targeting = legalTargets != null;
        }

        public void ClearTargeting()
        {
            _legalTargetIds.Clear();
            _targeting = false;
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

            // During a target window, keep the playmat silhouette but hide cards
            // that are not legal so the available targets read at a glance.
            if (_targeting && !_legalTargetIds.Contains(card.InstanceId))
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0.06f, 0.08f, 0.12f, 0.22f);
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
            if (_targeting && !_legalTargetIds.Contains(card.InstanceId))
            {
                card = null;
                return false;
            }
            publicFace = card.FaceUp;
            return true;
        }

        void LateUpdate()
        {
            _youPulse = Mathf.MoveTowards(_youPulse, 0f, Time.unscaledDeltaTime * 5.5f);
            _oppPulse = Mathf.MoveTowards(_oppPulse, 0f, Time.unscaledDeltaTime * 5.5f);
            ApplyLpPulse(_youLp, _youPulse, _youLpBase);
            ApplyLpPulse(_oppLp, _oppPulse, _oppLpBase);

            if (_canvas != null)
            {
                var cam = ArStageView.FindCamera(transform);
                if (cam != null) _canvas.worldCamera = cam;
            }

            PlaceLeftOfM1();
        }

        static void ApplyLpPulse(Text t, float pulse, Color baseCol)
        {
            if (t == null) return;
            t.color = Color.Lerp(baseCol, Color.white, pulse * 0.55f);
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
