using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Hub sandbox: seat any card on the Spirit Dueler (real zone poses) and
    /// preview arena models. No rules, no AI, no opponent.
    /// </summary>
    public class FreeViewScreen : MonoBehaviour
    {
        enum CatalogFilter { All = 0, Monsters, Spells, Traps, Models }

        const int NextIdStart = 900000;

        Action _onClose;
        CardDatabase _db;
        DuelEngine _engine;
        ArDuelSpace _space;
        Text _status;
        Text _selectedLabel;
        Transform _catalogHost;
        CatalogFilter _filter = CatalogFilter.Monsters;
        string _search = "";
        CardDef _selected;
        int _nextId = NextIdStart;
        int _pendingZone = -1;
        RulesZoneKind _pendingKind = RulesZoneKind.Monster;

        float _orbitYaw = 18f;
        float _orbitPitch = 28f;
        float _orbitDist = 1.35f;
        bool _orbitReady;
        bool _draggingView;
        Vector3 _focus;

        public static RectTransform Build(Transform modalHost, Action onClose)
        {
            FreeUiKit.EnsureLoaded();
            var go = new GameObject("FreeView", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(modalHost, false);
            var rt = go.GetComponent<RectTransform>();
            FloatingPanel.Stretch(rt);
            var bg = go.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = HubChrome.Dusk;
            bg.raycastTarget = true;

            var view = go.AddComponent<FreeViewScreen>();
            view._onClose = onClose;
            view.BuildChrome(rt);
            return rt;
        }

        void BuildChrome(RectTransform root)
        {
            HubChrome.HeaderBar(root, "FREE VIEW",
                "Seat cards on your disk · models project in the arena",
                () => _onClose?.Invoke(), out _, out _);

            var clear = HubChrome.Capsule(root, "CLEAR", ClearField, MenuCommandButton.Kind.Danger,
                centerTitle: true, titleSize: 16);
            FloatingPanel.Place(clear.GetComponent<RectTransform>(), 0.72f, 0.790f, 0.96f, 0.848f);

            // AR viewport — disk + arena
            var stageHost = new GameObject("StageHost", typeof(RectTransform)).GetComponent<RectTransform>();
            stageHost.SetParent(root, false);
            FloatingPanel.Place(stageHost, 0.02f, 0.42f, 0.98f, 0.78f);

            try
            {
                _space = ArDuelSpace.CreateInUi(stageHost, 0f, 0f, 1f, 1f, lifetimeParent: transform);
                var raw = stageHost.GetComponentInChildren<RawImage>(true);
                if (raw != null)
                {
                    raw.raycastTarget = true;
                    var hook = raw.gameObject.AddComponent<OrbitHook>();
                    hook.Owner = this;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Free View stage failed: " + ex.Message);
            }

            // Zone strip
            var zoneRow = new GameObject("Zones", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            zoneRow.transform.SetParent(root, false);
            FloatingPanel.Place(zoneRow.GetComponent<RectTransform>(), 0.02f, 0.355f, 0.98f, 0.415f);
            var zh = zoneRow.GetComponent<HorizontalLayoutGroup>();
            zh.spacing = 4;
            zh.childForceExpandWidth = true;
            zh.childForceExpandHeight = true;
            zh.padding = new RectOffset(2, 2, 2, 2);
            for (var i = 0; i < 5; i++)
                ZoneChip(zoneRow.transform, "M" + (i + 1), RulesZoneKind.Monster, i);
            for (var i = 0; i < 5; i++)
                ZoneChip(zoneRow.transform, "ST" + (i + 1), RulesZoneKind.SpellTrap, i);
            ZoneChip(zoneRow.transform, "FLD", RulesZoneKind.FieldSpell, 0);

            _selectedLabel = FloatingPanel.Body(root, "Pick a card, then a zone.", 13);
            FloatingPanel.Place(_selectedLabel.rectTransform, 0.03f, 0.318f, 0.70f, 0.352f);

            _status = FloatingPanel.Body(root, "", 12);
            FloatingPanel.Place(_status.rectTransform, 0.70f, 0.318f, 0.98f, 0.352f);
            _status.alignment = TextAnchor.MiddleRight;
            _status.color = DuelystUi.TextMuted;

            // Filter chips
            var filterRow = new GameObject("Filters", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            filterRow.transform.SetParent(root, false);
            FloatingPanel.Place(filterRow.GetComponent<RectTransform>(), 0.02f, 0.268f, 0.98f, 0.314f);
            var fh = filterRow.GetComponent<HorizontalLayoutGroup>();
            fh.spacing = 4;
            fh.childForceExpandWidth = true;
            fh.childForceExpandHeight = true;
            FilterChip(filterRow.transform, "ALL", CatalogFilter.All);
            FilterChip(filterRow.transform, "MON", CatalogFilter.Monsters);
            FilterChip(filterRow.transform, "SPELL", CatalogFilter.Spells);
            FilterChip(filterRow.transform, "TRAP", CatalogFilter.Traps);
            FilterChip(filterRow.transform, "MODELS", CatalogFilter.Models);

            var catalog = new GameObject("Catalog", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            catalog.transform.SetParent(root, false);
            FloatingPanel.Place(catalog.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.98f, 0.262f);
            var cimg = catalog.GetComponent<Image>();
            cimg.sprite = UiFoundation.WhiteSprite();
            cimg.color = HubChrome.WellFill;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(catalog.transform, false);
            FloatingPanel.Stretch(viewport.GetComponent<RectTransform>(), 4f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(52f, 72f);
            grid.spacing = new Vector2(4f, 4f);
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            var fit = content.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _catalogHost = content.transform;

            var scroll = catalog.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            BootSandbox();
            RebuildCatalog();
        }

        void ZoneChip(Transform parent, string label, RulesZoneKind kind, int index)
        {
            var b = HubChrome.Capsule(parent, label, () => PlaceOn(kind, index),
                MenuCommandButton.Kind.Primary, centerTitle: true, titleSize: 14);
            var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 40;
        }

        void FilterChip(Transform parent, string label, CatalogFilter filter)
        {
            HubChrome.Capsule(parent, label, () =>
            {
                _filter = filter;
                RebuildCatalog();
            }, MenuCommandButton.Kind.Primary, centerTitle: true, titleSize: 14);
        }

        void BootSandbox()
        {
            _db = CardDatabase.Load();
            if (_db == null || _db.Count == 0)
            {
                SetStatus("No card database.");
                return;
            }

            var playerDeck = CardDatabase.LoadDeck("player_starter.json")
                             ?? CardDatabase.LoadDeck("lab_rules_player.json");
            var aiDeck = CardDatabase.LoadDeck("ai_kaiba.json")
                         ?? CardDatabase.LoadDeck("lab_rules_ai.json");
            _engine = new DuelEngine();
            try
            {
                _engine.HumanVsHuman = true;
                if (playerDeck != null && aiDeck != null)
                    _engine.StartDuel(_db, playerDeck, aiDeck, cinematicOpening: true);
                else
                {
                    _engine.StartDuel(_db, playerDeck ?? new DeckFile { name = "Free" },
                        aiDeck ?? new DeckFile { name = "—" }, cinematicOpening: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Free View engine: " + ex.Message);
            }

            if (_engine.Player == null)
            {
                SetStatus("Could not start sandbox.");
                return;
            }

            ClearOccupants(_engine.Player);
            ClearOccupants(_engine.Opponent);
            _engine.Player.Hand.Clear();
            _engine.Opponent.Hand.Clear();

            var sys = _space != null ? _space.Interaction : null;
            if (sys != null)
            {
                if (sys.Arena != null)
                    sys.Arena.PreferDynamicModels = true;
                sys.BindEngine(_engine, _db);
                sys.SetHandVolumeVisible(false);
                if (sys.OppDisk != null)
                    sys.OppDisk.gameObject.SetActive(false);
                if (sys.Tracker is SimulatedArmTracker sim)
                    sim.EnableIdleMotion = false;
                sys.DeployForDuelOnly(0f);
            }

            _space?.SyncFromEngine(_engine, _db);
            FrameDisk();
            SetStatus("Tap a card, then M / ST / FLD.");
        }

        static void ClearOccupants(DuelistState who)
        {
            if (who == null) return;
            if (who.MonsterZones != null)
                foreach (var z in who.MonsterZones)
                    if (z != null) z.Occupant = null;
            if (who.SpellTrapZones != null)
                foreach (var z in who.SpellTrapZones)
                    if (z != null) z.Occupant = null;
            if (who.FieldSpellZone != null)
                who.FieldSpellZone.Occupant = null;
        }

        void RebuildCatalog()
        {
            if (_catalogHost == null || _db == null) return;
            for (var i = _catalogHost.childCount - 1; i >= 0; i--)
                Destroy(_catalogHost.GetChild(i).gameObject);

            var list = _db.GetAllCards();
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            var shown = 0;
            foreach (var def in list)
            {
                if (def == null || !PassFilter(def)) continue;
                MakeChip(def);
                shown++;
                if (shown >= 180) break;
            }

            SetStatus(shown + " cards");
        }

        bool PassFilter(CardDef def)
        {
            if (!string.IsNullOrEmpty(_search) &&
                (def.name == null || def.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            switch (_filter)
            {
                case CatalogFilter.Monsters: return def.IsMonster;
                case CatalogFilter.Spells: return def.IsSpell && !IsField(def);
                case CatalogFilter.Traps: return def.IsTrap;
                case CatalogFilter.Models: return CardModelCatalog.HasDedicatedModel(def.id);
                default: return true;
            }
        }

        static bool IsField(CardDef def) => def != null && def.IsFieldSpell;

        void MakeChip(CardDef def)
        {
            var go = new GameObject("C_" + def.id, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(_catalogHost, false);
            var img = go.GetComponent<Image>();
            var art = _db.GetArt(def.id);
            img.sprite = art ?? UiFoundation.WhiteSprite();
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var captured = def;
            btn.onClick.AddListener(() => SelectCard(captured));

            var cap = new GameObject("N", typeof(RectTransform), typeof(Text));
            cap.transform.SetParent(go.transform, false);
            var t = cap.GetComponent<Text>();
            WrldzType.Style(t, 9, display: false);
            t.text = ShortName(def.name);
            t.alignment = TextAnchor.LowerCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var trt = cap.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 0.28f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
        }

        static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Length <= 11 ? name : name.Substring(0, 10) + "…";
        }

        void SelectCard(CardDef def)
        {
            _selected = def;
            FreeUiKit.PlayClick();
            if (_selectedLabel != null)
            {
                var model = CardModelCatalog.HasDedicatedModel(def.id) ? " · model" : " · art";
                _selectedLabel.text = def.name + model + " — tap a zone";
            }

            HighlightLegalKinds(def);
        }

        void HighlightLegalKinds(CardDef def)
        {
            // Visual hint only — placement still checks type
            _pendingKind = def.IsMonster ? RulesZoneKind.Monster
                : IsField(def) ? RulesZoneKind.FieldSpell
                : RulesZoneKind.SpellTrap;
        }

        void PlaceOn(RulesZoneKind kind, int index)
        {
            if (_selected == null)
            {
                SetStatus("Pick a card first.");
                return;
            }

            if (_engine?.Player == null)
            {
                SetStatus("Sandbox not ready.");
                return;
            }

            if (_selected.IsMonster && kind != RulesZoneKind.Monster)
            {
                SetStatus("Monsters go on M1–M5.");
                return;
            }

            if (!_selected.IsMonster && kind == RulesZoneKind.Monster)
            {
                SetStatus("Spells/Traps go on ST or FIELD.");
                return;
            }

            if (IsField(_selected) && kind != RulesZoneKind.FieldSpell)
            {
                SetStatus("Field Spells go on FLD.");
                return;
            }

            if (!IsField(_selected) && kind == RulesZoneKind.FieldSpell)
            {
                SetStatus("Only Field Spells on FLD.");
                return;
            }

            var inst = new CardInstance
            {
                InstanceId = _nextId++,
                CardId = _selected.id,
                Def = _selected,
                FaceUp = _selected.IsMonster || IsField(_selected),
                Position = _selected.IsMonster ? BattlePosition.Attack : BattlePosition.Defense
            };

            switch (kind)
            {
                case RulesZoneKind.Monster:
                    _engine.Player.MonsterZones[Mathf.Clamp(index, 0, 4)].Occupant = inst;
                    break;
                case RulesZoneKind.SpellTrap:
                    _engine.Player.SpellTrapZones[Mathf.Clamp(index, 0, 4)].Occupant = inst;
                    break;
                case RulesZoneKind.FieldSpell:
                    _engine.Player.FieldSpellZone.Occupant = inst;
                    break;
            }

            _pendingKind = kind;
            _pendingZone = index;
            _space?.SyncFromEngine(_engine, _db);
            FreeUiKit.PlayConfirm();
            var where = kind == RulesZoneKind.Monster ? "M" + (index + 1)
                : kind == RulesZoneKind.SpellTrap ? "ST" + (index + 1)
                : "FIELD";
            SetStatus("Seated " + inst.Name + " on " + where);
            if (_selectedLabel != null)
                _selectedLabel.text = inst.Name + " on " + where +
                                      (CardModelCatalog.HasDedicatedModel(inst.CardId)
                                          ? " · model up"
                                          : " · art holo");
        }

        void ClearField()
        {
            if (_engine?.Player == null) return;
            ClearOccupants(_engine.Player);
            _space?.SyncFromEngine(_engine, _db);
            SetStatus("Field cleared.");
        }

        void SetStatus(string msg)
        {
            if (_status != null) _status.text = msg ?? "";
        }

        void FrameDisk()
        {
            var cam = _space != null ? _space.StageCamera : null;
            var disk = _space != null ? _space.Interaction?.PlayerDisk : null;
            if (cam == null) return;
            _focus = disk != null
                ? disk.transform.position + Vector3.up * 0.12f
                : new Vector3(0f, 0.55f, -1.8f);
            _orbitYaw = 12f;
            _orbitPitch = 32f;
            _orbitDist = 1.25f;
            _orbitReady = true;
            ApplyOrbit();
        }

        void ApplyOrbit()
        {
            var cam = _space != null ? _space.StageCamera : null;
            if (cam == null || !_orbitReady) return;
            _orbitPitch = Mathf.Clamp(_orbitPitch, 8f, 72f);
            _orbitDist = Mathf.Clamp(_orbitDist, 0.7f, 2.6f);
            var radYaw = _orbitYaw * Mathf.Deg2Rad;
            var radPitch = _orbitPitch * Mathf.Deg2Rad;
            var offset = new Vector3(
                Mathf.Sin(radYaw) * Mathf.Cos(radPitch),
                Mathf.Sin(radPitch),
                -Mathf.Cos(radYaw) * Mathf.Cos(radPitch));
            cam.transform.position = _focus + offset * _orbitDist;
            cam.transform.LookAt(_focus);
        }

        public void BeginOrbit() => _draggingView = true;

        public void OrbitBy(Vector2 delta)
        {
            if (!_orbitReady) return;
            _orbitYaw += delta.x * 0.35f;
            _orbitPitch -= delta.y * 0.28f;
            ApplyOrbit();
        }

        public void EndOrbit() => _draggingView = false;

        sealed class OrbitHook : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public FreeViewScreen Owner;

            public void OnBeginDrag(PointerEventData eventData) => Owner?.BeginOrbit();

            public void OnDrag(PointerEventData eventData)
            {
                if (eventData != null)
                    Owner?.OrbitBy(eventData.delta);
            }

            public void OnEndDrag(PointerEventData eventData) => Owner?.EndOrbit();
        }

        void LateUpdate()
        {
            if (!_orbitReady) return;
            var disk = _space != null ? _space.Interaction?.PlayerDisk : null;
            if (disk != null)
                _focus = disk.transform.position + Vector3.up * 0.12f;
            if (!_draggingView)
                ApplyOrbit();
        }
    }
}
