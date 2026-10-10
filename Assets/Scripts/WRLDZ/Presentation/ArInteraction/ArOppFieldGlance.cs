using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Opponent's public field as a small Solid Vision panel in the player's own space, not the arena:
    /// ~0.62 m from the eye, low on the right of a landscape screen just outside the card hand, and
    /// near the top on a portrait phone. It tags along as you walk and stays
    /// square to your view (a panel turned to face the eye from that far off-centre looked skewed).
    /// It carries only the opponent's side (name, magenta LP, HAND / DECK / GY — tap GY for their
    /// graveyard) plus the shared turn and phase. Your own LP, deck and graveyard are on and around
    /// your disk, so they are not repeated here.
    /// The mini-field is laid out the way the opponent's holograms stand from the player's seat:
    /// S/T row far (top), monsters near (bottom), columns mirrored to match the arena, Field Spell
    /// beyond M1. Face-up shows legal art; face-down stays a card back; DEF monsters lie sideways.
    /// </summary>
    [DefaultExecutionOrder(810)]
    public class ArOppFieldGlance : MonoBehaviour
    {
        public const int SlotCountExpected = 11;

        const int CanvasW = 800;
        const int CanvasH = 500;

        /// <summary>Near-field size: about the same on screen as the 0.42 m board it replaced at 1.26 m.</summary>
        public const float WorldWidth = 0.19f;

        /// <summary>
        /// Eye-relative placement (metres, arena axes) on a landscape view: ~0.62 m out, down and to the
        /// right beside the card hand, so it reads as the player's own panel rather than part of the arena.
        /// </summary>
        static readonly Vector3 WideOffset = new(0.30f, -0.25f, 0.49f);

        /// <summary>Portrait view: 0.65 m out, near the top of the narrow view, clear of the hand.</summary>
        static readonly Vector3 TallOffset = new(0f, 0.09f, 0.64f);

        const float FollowRate = 5f;

        public Transform Root { get; private set; }
        public int SlotCount => _slots != null ? _slots.Length : 0;
        public bool IsTargeting => _targeting;

        public ArDuelDiskRig PlayerDisk;
        public Transform ArenaRoot;
        int _layer;
        Canvas _canvas;
        CanvasGroup _cg;
        RectTransform _field;
        RectTransform[] _slots;
        Image[] _art;
        Image[] _edge;
        Image[] _fill;
        CardInstance[] _cards;
        Text _oppName;
        Text _oppLp;
        Text _handCount;
        Text _deckCount;
        Text _gyCount;
        Text _phaseLabel;
        Text _turnChip;
        RectTransform _gyHit;
        Image _pillMp1;
        Image _pillBattle;
        Image _pillMp2;
        Image _pillEnd;
        Color _oppLpBase = DuelystUi.Magenta;
        int _lastOppLp = int.MinValue;
        float _oppPulse;
        float _columnSign;
        bool _placed;
        BoxCollider _hit;
        readonly HashSet<int> _legalTargetIds = new();
        bool _targeting;

        static readonly Color SlotEmptyFill = new(0.10f, 0.05f, 0.14f, 0.20f);
        static readonly Color SlotEmptyEdge = new(1f, 0.28f, 0.72f, 0.26f);
        static readonly Color PipIdleFill = new(0.04f, 0.05f, 0.09f, 0.85f);

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

            // One quiet slate under everything so the board reads as a single object.
            ArHoloSprites.Apply(plate.AddComponent<Image>(), ArHoloSprites.RoundRect(),
                new Color(0.02f, 0.025f, 0.06f, 0.55f), 30f);

            BuildClusterChrome(plate.transform);
            BuildField(plate.transform);

            transform.localScale = Vector3.one * (WorldWidth / CanvasW);
        }

        void BuildClusterChrome(Transform plate)
        {
            // Two gapped islands. Do not wrap the whole plate in HudIslandGlass — that melted into one bar.
            var opp = Island(plate, "OppScore", 0.015f, 0.715f, 0.545f, 0.975f, DuelystUi.Magenta);
            _oppName = Label(opp, "Who", "OPPONENT", 28, TextAnchor.MiddleLeft,
                new Color(1f, 0.62f, 0.85f, 1f), fitMin: 18);
            Place(_oppName.rectTransform, 0.05f, 0.70f, 0.95f, 0.96f);
            _oppLp = Label(opp, "LP", "8000", 62, TextAnchor.MiddleLeft, DuelystUi.Magenta, display: true);
            Place(_oppLp.rectTransform, 0.05f, 0.28f, 0.95f, 0.72f);
            _handCount = Label(opp, "Hand", "HAND 0", 22, TextAnchor.MiddleLeft, DuelystUi.TextCream);
            Place(_handCount.rectTransform, 0.05f, 0.04f, 0.37f, 0.28f);
            _deckCount = Label(opp, "Deck", "DECK 0", 22, TextAnchor.MiddleCenter, DuelystUi.TextCream);
            Place(_deckCount.rectTransform, 0.37f, 0.04f, 0.70f, 0.28f);
            _gyCount = Label(opp, "GY", "GY 0", 22, TextAnchor.MiddleRight, new Color(0.85f, 0.72f, 0.95f, 1f));
            Place(_gyCount.rectTransform, 0.70f, 0.04f, 0.95f, 0.28f);
            var gyHit = new GameObject("GyHit", typeof(RectTransform));
            gyHit.transform.SetParent(opp, false);
            gyHit.layer = _layer;
            _gyHit = gyHit.GetComponent<RectTransform>();
            Place(_gyHit, 0.66f, 0.0f, 1.0f, 0.34f);

            var phase = Island(plate, "Phase", 0.56f, 0.715f, 0.985f, 0.975f, DuelystUi.Gold);
            _turnChip = Label(phase, "Turn", "YOUR TURN", 24, TextAnchor.MiddleCenter, DuelystUi.TextMuted);
            Place(_turnChip.rectTransform, 0.04f, 0.70f, 0.96f, 0.96f);
            _phaseLabel = Label(phase, "PhaseTxt", "MAIN PHASE 1", 38, TextAnchor.MiddleCenter,
                DuelystUi.GoldHot, display: true, fitMin: 24);
            Place(_phaseLabel.rectTransform, 0.04f, 0.36f, 0.96f, 0.74f);
            var pips = new GameObject("Pips", typeof(RectTransform));
            pips.transform.SetParent(phase, false);
            pips.layer = _layer;
            Place(pips.GetComponent<RectTransform>(), 0.06f, 0.06f, 0.94f, 0.34f);
            _pillMp1 = Pip(pips.transform, "MP1", "M1", 0.00f, 0.235f);
            _pillBattle = Pip(pips.transform, "BP", "BP", 0.255f, 0.49f);
            _pillMp2 = Pip(pips.transform, "MP2", "M2", 0.51f, 0.745f);
            _pillEnd = Pip(pips.transform, "EP", "EP", 0.765f, 1.00f);
        }

        void BuildField(Transform plate)
        {
            var go = new GameObject("Field", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(plate, false);
            go.layer = _layer;
            _field = go.GetComponent<RectTransform>();
            Place(_field, 0.015f, 0.025f, 0.985f, 0.695f);
            ArHoloSprites.Apply(go.GetComponent<Image>(), ArHoloSprites.RoundRect(),
                new Color(0.12f, 0.03f, 0.10f, 0.22f), 22f);
            ArHoloSprites.Layer(go.transform, "Rim", ArHoloSprites.RoundRectRim(),
                new Color(1f, 0.28f, 0.72f, 0.40f), 22f);
            // A hairline between the rows: the opponent's S/T line (far) vs monsters (near).
            var mid = new GameObject("Midline", typeof(RectTransform), typeof(Image));
            mid.transform.SetParent(go.transform, false);
            mid.layer = _layer;
            Place(mid.GetComponent<RectTransform>(), 0.04f, 0.497f, 0.96f, 0.503f);
            ArHoloSprites.Apply(mid.GetComponent<Image>(), ArHoloSprites.Pill(),
                new Color(1f, 0.28f, 0.72f, 0.18f), 1f);

            _slots = new RectTransform[SlotCountExpected];
            _art = new Image[SlotCountExpected];
            _edge = new Image[SlotCountExpected];
            _fill = new Image[SlotCountExpected];
            _cards = new CardInstance[SlotCountExpected];
            for (var i = 0; i < SlotCountExpected; i++)
                MakeSlot(go.transform, i);
            _columnSign = 1f;
            LayoutSlots(1f);
        }

        /// <summary>
        /// Columns from the player's seat. <paramref name="sign"/> +1 puts the opponent's M1 on the
        /// left (their column sign mapped into this board's screen-right).
        /// </summary>
        void LayoutSlots(float sign)
        {
            const float cardW = 0.128f;
            const float pitch = 0.150f;
            const float rowH = 0.43f;
            var span = 5f * pitch + cardW;
            var x0 = (1f - span) * 0.5f;
            for (var col = 0; col < 6; col++)
            {
                // col 0 = Field Spell (outside M1), cols 1..5 = zones 0..4, flipped when M1 is on the right.
                var screenCol = sign >= 0f ? col : 5 - col;
                var xa = x0 + screenCol * pitch;
                if (col == 0)
                {
                    Place(_slots[10], xa, 0.535f, xa + cardW, 0.535f + rowH);
                    continue;
                }

                var zone = col - 1;
                Place(_slots[5 + zone], xa, 0.535f, xa + cardW, 0.535f + rowH);
                Place(_slots[zone], xa, 0.035f, xa + cardW, 0.035f + rowH);
            }
        }

        void MakeSlot(Transform parent, int index)
        {
            var go = new GameObject("Slot" + index, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            var srt = go.GetComponent<RectTransform>();
            _fill[index] = ArHoloSprites.Layer(go.transform, "Fill", ArHoloSprites.RoundRect(), SlotEmptyFill, 8f);

            var art = new GameObject("Art", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(go.transform, false);
            art.layer = _layer;
            var art_rt = art.GetComponent<RectTransform>();
            art_rt.anchorMin = art_rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = art.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = false;
            _art[index] = img;

            _edge[index] = ArHoloSprites.Layer(go.transform, "Edge", ArHoloSprites.RoundRectRim(), SlotEmptyEdge, 8f, 2f);
            _slots[index] = srt;
        }

        RectTransform Island(Transform parent, string name, float x0, float y0, float x1, float y1, Color accent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            var rt = go.GetComponent<RectTransform>();
            Place(rt, x0, y0, x1, y1);
            ArHoloSprites.Apply(go.GetComponent<Image>(), ArHoloSprites.RoundRect(),
                new Color(accent.r * 0.18f, accent.g * 0.18f, accent.b * 0.18f, 0.40f), 16f);
            ArHoloSprites.Layer(go.transform, "Rim", ArHoloSprites.RoundRectRim(),
                new Color(accent.r, accent.g, accent.b, 0.70f), 16f);
            // Accent underline: the island's colour reads at a glance even at the board's size.
            var bar = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(go.transform, false);
            bar.layer = _layer;
            Place(bar.GetComponent<RectTransform>(), 0.30f, 0.0f, 0.70f, 0.035f);
            ArHoloSprites.Apply(bar.GetComponent<Image>(), ArHoloSprites.Pill(), accent, 2f);
            return rt;
        }

        Image Pip(Transform parent, string name, string label, float x0, float x1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            Place(go.GetComponent<RectTransform>(), x0, 0.05f, x1, 0.95f);
            var img = ArHoloSprites.Apply(go.GetComponent<Image>(), ArHoloSprites.Pill(), PipIdleFill, 16f);
            var rim = ArHoloSprites.Layer(go.transform, "Rim", ArHoloSprites.PillRim(),
                new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.45f), 16f);
            rim.name = "Rim";
            var t = Label(go.transform, "L", label, 22, TextAnchor.MiddleCenter, DuelystUi.TextMuted);
            Place(t.rectTransform, 0f, 0f, 1f, 1f);
            return img;
        }

        Text Label(Transform parent, string name, string text, int size, TextAnchor align, Color color,
            bool display = false, int fitMin = 0)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = _layer;
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, 18, display: display, heavyOutline: true);
            // Explicit world-canvas sizes: Readable() would lift these past their rects and
            // Truncate would then blank them (the old YOU LP / counts / pips never showed).
            t.fontSize = size;
            t.text = text ?? "";
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            if (fitMin > 0)
            {
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = fitMin;
                t.resizeTextMaxSize = size;
            }
            else
            {
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.resizeTextForBestFit = false;
            }

            var o = t.GetComponent<Outline>();
            if (o != null)
                o.effectDistance = new Vector2(1.6f, -1.6f);
            return t;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Opponent name / LP and the shared turn + phase. The player's own values (youName, youLp,
        /// deck, gy, extra) are accepted for the arena's call but not shown: they live on the disk.
        /// </summary>
        public void SyncChrome(string youName, int youLp, string oppName, int oppLp,
            DuelPhase phase, bool youToMove, int turn, int deck, int gy, int extra)
        {
            if (_oppName != null)
                _oppName.text = string.IsNullOrEmpty(oppName) ? "OPPONENT" : oppName;
            oppLp = Mathf.Max(0, oppLp);
            if (_oppLp != null)
                _oppLp.text = oppLp.ToString();
            if (_lastOppLp != int.MinValue && oppLp != _lastOppLp)
                _oppPulse = 1f;
            _lastOppLp = oppLp;
            _oppLpBase = oppLp < 2000 ? DuelystUi.Danger : DuelystUi.Magenta;

            if (_phaseLabel != null)
            {
                _phaseLabel.text = FormatPhase(phase);
                _phaseLabel.color = phase == DuelPhase.Battle ? DuelystUi.BattleOrange : DuelystUi.GoldHot;
            }
            if (_turnChip != null)
            {
                var who = youToMove ? "YOUR TURN" : "OPPONENT'S TURN";
                _turnChip.text = turn > 0 ? who + "  ·  T" + turn : who;
                _turnChip.color = youToMove
                    ? new Color(0.62f, 0.95f, 1f, 1f)
                    : new Color(1f, 0.62f, 0.85f, 1f);
            }
            PaintPip(_pillMp1, phase == DuelPhase.Main1, DuelystUi.Gold);
            PaintPip(_pillBattle, phase == DuelPhase.Battle, DuelystUi.BattleOrange);
            PaintPip(_pillMp2, phase == DuelPhase.Main2, DuelystUi.Gold);
            PaintPip(_pillEnd, phase == DuelPhase.End, DuelystUi.Gold);
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

        static void PaintPip(Image img, bool active, Color hot)
        {
            if (img == null) return;
            img.color = active ? hot : PipIdleFill;
            var rim = img.transform.Find("Rim")?.GetComponent<Image>();
            if (rim != null)
                rim.color = active
                    ? new Color(1f, 1f, 1f, 0.85f)
                    : new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.45f);
            var label = img.transform.Find("L")?.GetComponent<Text>();
            if (label != null)
                label.color = active ? new Color(0.06f, 0.05f, 0.03f, 1f) : DuelystUi.TextMuted;
            var o = label != null ? label.GetComponent<Outline>() : null;
            if (o != null) o.enabled = !active;
        }

        /// <summary>
        /// Restrict the mini-field to the cards that can currently be chosen.
        /// Empty / non-legal slots stay as a dim silhouette, while legal cards keep their art,
        /// get a gold edge and become the only pickable targets.
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
            if (opp != null)
            {
                for (var i = 0; i < 5; i++)
                {
                    var c = opp.MonsterZones != null && i < opp.MonsterZones.Length
                        ? opp.MonsterZones[i].Occupant : null;
                    Paint(i, c, db, monster: true);
                }

                for (var i = 0; i < 5; i++)
                {
                    var c = opp.SpellTrapZones != null && i < opp.SpellTrapZones.Length
                        ? opp.SpellTrapZones[i].Occupant : null;
                    Paint(5 + i, c, db, monster: false);
                }

                Paint(10, opp.FieldSpellZone?.Occupant, db, monster: false);

                if (_handCount != null) _handCount.text = "HAND " + (opp.Hand != null ? opp.Hand.Count : 0);
                if (_deckCount != null) _deckCount.text = "DECK " + opp.DeckCount;
                if (_gyCount != null) _gyCount.text = "GY " + (opp.Graveyard != null ? opp.Graveyard.Count : 0);
            }
            else
            {
                for (var i = 0; i < _slots.Length; i++)
                    Paint(i, null, db, monster: false);
            }

            if (_cg != null)
                _cg.alpha = 1f;
        }

        bool Paint(int index, CardInstance card, CardDatabase db, bool monster)
        {
            if (index < 0 || index >= _slots.Length) return false;
            var img = _art[index];
            if (img == null) return false;
            if (_cards != null && index < _cards.Length)
                _cards[index] = card;

            var legal = card != null && (!_targeting || _legalTargetIds.Contains(card.InstanceId));
            if (card == null || !legal)
            {
                img.enabled = false;
                _fill[index].color = _targeting && card != null
                    ? new Color(0.04f, 0.05f, 0.08f, 0.35f)
                    : SlotEmptyFill;
                _edge[index].color = SlotEmptyEdge;
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
            img.enabled = true;
            FitArt(index, monster && card.Position == BattlePosition.Defense);
            _fill[index].color = new Color(0f, 0f, 0f, 0.35f);
            _edge[index].color = _targeting
                ? DuelystUi.GoldHot
                : new Color(1f, 0.28f, 0.72f, card.FaceUp ? 0.85f : 0.55f);
            return true;
        }

        /// <summary>Card art fills the slot; a DEF monster lies sideways inside it.</summary>
        void FitArt(int index, bool sideways)
        {
            var slot = _slots[index];
            var art = _art[index].rectTransform;
            var w = slot.rect.width;
            var h = slot.rect.height;
            if (w <= 1f || h <= 1f)
            {
                // Before the first layout pass: slot size from its anchors.
                w = (slot.anchorMax.x - slot.anchorMin.x) * _field.rect.width;
                h = (slot.anchorMax.y - slot.anchorMin.y) * _field.rect.height;
            }

            art.sizeDelta = sideways ? new Vector2(h, w) * 0.9f : new Vector2(w, h) * 0.92f;
            art.localRotation = Quaternion.Euler(0f, 0f, sideways ? 90f : 0f);
            art.anchoredPosition = Vector2.zero;
        }

        /// <summary>The opponent island's GY count — opens the opponent's graveyard.</summary>
        public bool TryPickGy(Ray ray)
        {
            if (_hit == null || _gyHit == null) return false;
            if (!_hit.Raycast(ray, out var hit, 24f)) return false;
            var lp = _gyHit.InverseTransformPoint(hit.point);
            return _gyHit.rect.Contains(new Vector2(lp.x, lp.y));
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
                var lp = slot.InverseTransformPoint(hit.point);
                if (!slot.rect.Contains(new Vector2(lp.x, lp.y))) continue;
                var d = (hit.point - slot.position).sqrMagnitude;
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
            var dt = Time.unscaledDeltaTime;
            _oppPulse = Mathf.MoveTowards(_oppPulse, 0f, dt * 5.5f);
            ApplyLpPulse(_oppLp, _oppPulse, _oppLpBase);

            var cam = ArStageView.FindCamera(transform);
            if (_canvas != null && cam != null) _canvas.worldCamera = cam;

            if (_targeting && _edge != null)
            {
                var glow = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
                for (var i = 0; i < _edge.Length; i++)
                    if (_art[i] != null && _art[i].enabled)
                        _edge[i].color = Color.Lerp(DuelystUi.Gold, Color.white, glow * 0.6f);
            }

            SyncColumns();
            PlaceBesidePlayer(cam, dt);
        }

        /// <summary>Mirror the columns whenever the opponent's disk (or this board) flips sides.</summary>
        void SyncColumns()
        {
            var sign = ArPlaymatLayout.OppColumnSign;
            if (ArenaRoot != null && Vector3.Dot(ArenaRoot.right, transform.right) < 0f)
                sign = -sign;
            if (Mathf.Approximately(sign, _columnSign)) return;
            _columnSign = sign;
            LayoutSlots(sign);
        }

        static void ApplyLpPulse(Text t, float pulse, Color baseCol)
        {
            if (t == null) return;
            t.color = Color.Lerp(baseCol, Color.white, pulse * 0.55f);
        }

        /// <summary>
        /// Tag-along: a fixed offset from the eye along the arena's axes (not the head's), so it
        /// stays put while you look around but follows when you walk. Square to the view.
        /// </summary>
        void PlaceBesidePlayer(Camera cam, float dt)
        {
            if (cam == null) return;
            var fwd = ArenaRoot != null ? ArenaRoot.forward : cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var o = cam.aspect >= 1f ? WideOffset : TallOffset;
            var eye = cam.transform.position;
            var target = eye + right * o.x + Vector3.up * o.y + fwd * o.z;

            if (!_placed || (transform.position - target).sqrMagnitude > 2.25f)
                transform.position = target;
            else
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-dt * FollowRate));
            _placed = true;
            transform.rotation = cam.transform.rotation;
        }
    }
}
