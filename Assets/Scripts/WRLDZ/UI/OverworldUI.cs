using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>Battle City overworld — fixed avatar, pan map, compass nearby popout.</summary>
    public class OverworldUI : MonoBehaviour
    {
        RectTransform _mapContent, _avatar, _weatherFxRoot;
        Text _status, _rankLabel, _xpLabel, _envLabel, _currencyLine, _curDigi, _curCoins, _curEnergy;
        Image _xpFill, _nearbyPlateImg;
        WrldzTheme.MapAtmosphereRefs _atmos;
        MapEnvironment _env;
        AvatarPortraitView _mapPortrait, _hudPortrait;
        SoulBadgeView _soulBadge;
        /// <summary>Screen-fixed avatar — aligned to map UV center used by distance math.</summary>
        static readonly Vector2 AvatarScreen = new(0.5f, 0.48f);
        const float MetersPerMapWidth = 280f;
        const int NearbySlotCount = 6;
        int _spiritRank = 1;
        float _fxTimer, _pulseT, _statusHideAt;
        Vector2 _panCurrent, _panTarget;
        readonly List<RectTransform> _weatherDrops = new();
        readonly List<PinView> _pinViews = new();
        readonly List<NearbySlot> _nearbySlots = new();
        readonly List<MapPin> _nearbySort = new(16);
        GameObject _statusToast, _systemsSheet, _menuExpand, _compassPop, _deckSwitchPop, _deckSwitchCatch;
        Text _deckSwitchLabel;
        RectTransform _deckSwitchRt, _eyeRt, _menuBoard, _profileRt, _eyeBurstRt, _eyeFlashRt;
        readonly List<FanCard> _fanCards = new();

        struct FanCard
        {
            public RectTransform rt;
            public CanvasGroup cg;
            public Vector2 restPos;
            public Vector2 restSize;
            public float restRot;
            public MenuHoloPulse pulse;
        }
        Image _mainMenuImg, _eyeWhiteGlow, _eyeBurst, _eyeFlash, _menuGlass;
        Sprite _eyeIdleSpr;
        Material _eyeLightMat;
        MenuShell _menuShell;
        SetEnergyField _seField;
        OverworldNpcField _npcField;
        bool _menuOpen;
        bool _compassOpen;
        GameObject _menuVeil;
        CanvasGroup _menuVeilCg, _menuExpandCg, _compassCg, _currencyCg;
        Coroutine _menuMotion, _eyeFlashCo;

        const float MenuGlassX0 = 0.022f;
        const float MenuGlassX1 = 0.978f;
        const float MenuGlassY0 = 0.014f;
        const float MenuGlassClosedY1 = 0.118f;
        const float MenuGlassOpenY1 = 0.78f;
        float _menuGlassClosedY1 = MenuGlassClosedY1;

        struct MapPin
        {
            public string id, title, kind;
            public float x, y, distanceM;
        }

        struct PinView
        {
            public MapPin pin;
            public Image pinImage, disc;
            public MapZoneKind kind;
        }

        struct NearbySlot
        {
            public GameObject root;
            public Image icon, slotBg;
            public Text title, dist;
            public MapPin pin;
            public bool filled;
        }

        /// <summary>
        /// Live map pins only: Tear, Raid, Bazaar, Training, Story, PvP.
        /// Display names are the zone kind only — no place nicknames.
        /// Street NPCs spawn around Tears, not as pins. Tournaments are not pins.
        /// UV center (0.5,0.5) = player. ~1 UV = MetersPerMapWidth m.
        /// </summary>
        static readonly MapPin[] Pins =
        {
            new() { id = "tear_a", title = "TEAR ZONE", kind = "tear", x = 0.50f, y = 0.56f },
            new() { id = "bazaar_1", title = "BAZAAR ZONE", kind = "bazaar", x = 0.42f, y = 0.48f },
            new() { id = "train_1", title = "TRAINING ZONE", kind = "training", x = 0.60f, y = 0.48f },
            new() { id = "raid_1", title = "RAID ZONE", kind = "raid", x = 0.50f, y = 0.66f },
            new() { id = "pvp_1", title = "PvP ZONE", kind = "pvp", x = 0.38f, y = 0.58f },
            new() { id = "story_1", title = "STORY ZONE", kind = "story", x = 0.34f, y = 0.44f },
            new() { id = "tear_b", title = "TEAR ZONE", kind = "tear", x = 0.62f, y = 0.32f },
            new() { id = "tear_c", title = "TEAR ZONE", kind = "tear", x = 0.72f, y = 0.36f },
            new() { id = "bazaar_2", title = "BAZAAR ZONE", kind = "bazaar", x = 0.30f, y = 0.60f },
        };

        public void Build()
        {
            WrldzTheme.ApplyPortrait();
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            GoTheme.EnsureAssets();

            AppSession.Ensure();
            AppSession.Ensure().RefreshFromStore();
            var account = AppSession.Ensure().Account;
            if (account == null)
            {
                // One more hard load from DB before giving up
                PlayerAccountDatabase.Initialize();
                var dbAcc = PlayerAccountDatabase.GetSessionAccount();
                if (dbAcc != null)
                {
                    account = LocalAccountStore.Account.FromDb(dbAcc);
                    AppSession.Ensure().SetAccount(account);
                }
            }

            if (account == null)
            {
                Debug.LogWarning("[WRLDZ] Overworld: no session — Boot. DB=" + PlayerAccountDatabase.DatabasePath);
                AppSession.Ensure().GoBoot();
                return;
            }

            account.EnsureProgress();
            SoulFractureService.TickAndPersist(account);
            if (SoulFractureService.SyncDeactivation(account))
            {
                LocalAccountStore.UpdateAccount(account);
                AppSession.Ensure().Logout();
                AppSession.Ensure().GoBoot();
                return;
            }

            _spiritRank = Mathf.Max(1, account.progress.level);

            try
            {
                _env = MapEnvironment.Ensure();
                _env.OnEnvironmentChanged -= ApplyEnvironmentVisuals;
                _env.OnEnvironmentChanged += ApplyEnvironmentVisuals;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[WRLDZ] MapEnvironment failed: " + ex);
            }

            // ── MAP CANVAS — Battle City × Ingress night surface ──
            var mapCanvas = WrldzTheme.Canvas("OverworldMapCanvas", 10);
            // Night underlay so Imagine map / SE field read cleanly (not GO grass)
            var night = new Color(0.04f, 0.06f, 0.12f, 1f);
            var mapRoot = WrldzTheme.StretchFill(mapCanvas, "MapRoot", night);
            var mapRootImg = mapRoot.GetComponent<Image>();
            mapRootImg.raycastTarget = true;
            mapRootImg.color = night;

            // Soft sky/fog wash over night city
            _atmos = BuildGoStyleAtmosphere(mapRoot,
                _env != null ? _env.Daylight01 : 0.35f,
                _env != null ? _env.Weather : WeatherKind.Clear);
            DisableRaycastsUnder(mapRoot);

            // Map content is larger than screen so it can pan under fixed avatar
            _mapContent = new GameObject("MapContent", typeof(RectTransform)).GetComponent<RectTransform>();
            _mapContent.SetParent(mapRoot, false);
            WrldzTheme.Stretch(_mapContent);
            _mapContent.anchorMin = new Vector2(-1f, -1f);
            _mapContent.anchorMax = new Vector2(2f, 2f);
            _mapContent.offsetMin = Vector2.zero;
            _mapContent.offsetMax = Vector2.zero;

            OverworldMapWorld.Build(_mapContent);

            var landmarks = new System.Collections.Generic.List<(float x, float y, MapZoneKind kind, string id)>();
            foreach (var pin in Pins)
                landmarks.Add((pin.x, pin.y, MapZoneCatalog.Parse(pin.kind), pin.id));
            OverworldMapWorld.StampLandmarks(_mapContent, landmarks);

            // Pins live ON the map content so they move with GPS pan
            _pinViews.Clear();
            foreach (var pin in Pins)
                MakePin(pin);

            var tears = new List<(string id, float x, float y)>();
            foreach (var pin in Pins)
            {
                if (MapZoneCatalog.Parse(pin.kind) != MapZoneKind.Tear) continue;
                tears.Add((pin.id, pin.x, pin.y));
            }

            _npcField = OverworldNpcField.Attach(
                _mapContent, _env, MetersPerMapWidth, tears,
                (x, y) => _env != null ? _env.DistanceMetersToMapPin(x, y, MetersPerMapWidth) : 999f,
                OnStreetNpc);

            // Weather FX (screen space, not panned)
            _weatherFxRoot = new GameObject("WeatherFx", typeof(RectTransform)).GetComponent<RectTransform>();
            _weatherFxRoot.SetParent(mapRoot, false);
            WrldzTheme.Stretch(_weatherFxRoot);
            BuildWeatherDrops();

            // ── HUD — avatar fixed, menu chrome, no walk-stick ──
            var hudCanvas = WrldzTheme.Canvas("OverworldHudCanvas", 50);
            var hudRoot = new GameObject("HudRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            hudRoot.SetParent(hudCanvas, false);
            WrldzTheme.Stretch(hudRoot);

            // Fixed avatar on HUD (you don't drag around the map — you walk outside)
            _avatar = MakeAvatar(account);
            _avatar.SetParent(hudRoot, false);
            PlaceAvatarFixed();

            BuildGoTopHud(hudRoot, account);
            BuildGoBottomChrome(hudRoot, account);
            BuildGoStatusToast(hudRoot);

            // Set Energy motes (Ingress XM-style) — walk to attract & harvest
            _seField = SetEnergyField.Attach(
                _mapContent,
                hudRoot,
                _env,
                onGathered: (amt, toast) =>
                {
                    if (!string.IsNullOrEmpty(toast)) SetStatus(toast);
                    RefreshCurrencyStrip();
                },
                onCurrencyChanged: RefreshCurrencyStrip);

            if (_env != null)
            {
                _env.OnLocationMoved -= OnGpsMoved;
                _env.OnLocationMoved += OnGpsMoved;
            }

            ApplyEnvironmentVisuals();
            ApplyGpsMapPan();
            RefreshPinDistances();
            // Quiet boot — bottom badge already shows level / XP
            SetStatus("");
            RefreshXpBar(account);
            Debug.Log("[WRLDZ] Overworld ready · " + account.displayName);
        }

        /// <summary>Thin XP track. Compact = fill only (level lives on the badge pip).</summary>
        void BuildXpBar(Transform root, LocalAccountStore.Account account, bool compact = false)
        {
            var host = new GameObject("XpBar", typeof(RectTransform), typeof(Image));
            host.transform.SetParent(root, false);
            GoTheme.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var bg = host.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = new Color(0.04f, 0.05f, 0.08f, 0.88f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(host.transform, false);
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0.01f, 1f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            _xpFill = fillGo.GetComponent<Image>();
            _xpFill.sprite = UiFoundation.WhiteSprite();
            _xpFill.color = GoTheme.LevelGold;
            _xpFill.raycastTarget = false;

            if (!compact)
            {
                _xpLabel = GoTheme.Label(host.transform, "XpLbl", "", 10, Color.white, TextAnchor.MiddleCenter, bold: false);
                GoTheme.Place(_xpLabel.rectTransform, 0.02f, 0f, 0.98f, 1f);
                _xpLabel.color = new Color(1f, 1f, 1f, 0.9f);
            }
            else
                _xpLabel = null;

            RefreshXpBar(account);
        }

        void RefreshXpBar(LocalAccountStore.Account account)
        {
            if (account?.progress == null) return;
            account.progress.EnsureValid();
            SoulFractureService.TickAndPersist(account);
            var p = account.progress;
            var t = p.XpProgress01();
            if (_xpFill != null)
            {
                var frt = _xpFill.rectTransform;
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = new Vector2(Mathf.Clamp(t, 0.02f, 1f), 1f);
                frt.offsetMin = new Vector2(2f, 2f);
                frt.offsetMax = new Vector2(-2f, -2f);
            }

            if (_xpLabel != null)
            {
                var need = p.XpToNextLevel();
                if (need <= 0)
                    _xpLabel.text = $"Lv{p.level} MAX";
                else
                    _xpLabel.text = $"Lv{p.level}  {p.xp}/{need}";
            }

            if (_rankLabel != null)
                _rankLabel.text = Mathf.Max(1, p.level).ToString();
            _soulBadge?.Bind(p);
        }

        static void DisableRaycastsUnder(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                // keep only explicit interaction layers
                if (img.gameObject.name is "DragLayer" or "MapRoot") continue;
                if (img.GetComponent<Button>() != null) continue;
                img.raycastTarget = false;
            }
        }

        void OnDestroy()
        {
            if (_env != null)
            {
                _env.OnEnvironmentChanged -= ApplyEnvironmentVisuals;
                _env.OnLocationMoved -= OnGpsMoved;
            }

            if (_eyeLightMat != null)
            {
                Destroy(_eyeLightMat);
                _eyeLightMat = null;
            }
        }

        void OnGpsMoved()
        {
            ApplyGpsMapPan(); // pan + pin distances + proximity toast
            if (_seField != null && _env != null)
                _seField.NotifyMoved(_env.MetersEast, _env.MetersNorth);
        }

        /// <summary>
        /// Pokémon GO: avatar stays put on screen; map content shifts opposite real walk.
        /// East meters → map slides left; North meters → map slides down (north-up).
        /// </summary>
        void ApplyGpsMapPan()
        {
            if (_mapContent == null || _env == null) return;
            var east = _env.MetersEast / MetersPerMapWidth;
            var north = _env.MetersNorth / MetersPerMapWidth;
            // Target pan — smoothed in Update so walk feels GO-like
            _panTarget = new Vector2(-east, -north);
            RefreshPinDistances();
            CheckProximityGps();
        }

        /// <summary>Interact radius; slightly softer when not on real GPS (desk / editor lab).</summary>
        float InteractRadius(MapZoneKind k)
        {
            var r = MapZoneCatalog.InteractRadiusM(k);
            if (_env == null || !_env.UsingRealGps)
                r = Mathf.Max(r, 70f); // lab: easier to hit zones while walking pad / WASD
            return r;
        }

        void ApplyMapPanSmooth()
        {
            if (_mapContent == null) return;
            _panCurrent = Vector2.Lerp(_panCurrent, _panTarget, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            var ox = _panCurrent.x;
            var oy = _panCurrent.y;
            _mapContent.anchorMin = new Vector2(-1f + ox, -1f + oy);
            _mapContent.anchorMax = new Vector2(2f + ox, 2f + oy);
            _mapContent.offsetMin = Vector2.zero;
            _mapContent.offsetMax = Vector2.zero;
        }

        void RefreshPinDistances()
        {
            if (_env == null) return;
            for (var i = 0; i < _pinViews.Count; i++)
            {
                var pv = _pinViews[i];
                var dist = _env.DistanceMetersToMapPin(pv.pin.x, pv.pin.y, MetersPerMapWidth);
                pv.pin.distanceM = dist;
                _pinViews[i] = pv;
                var zk = pv.kind;
                var inRange = dist <= InteractRadius(zk);

                // Unique pin art keeps its own colors; only the shadow blob pulses.
                if (pv.pinImage != null)
                    pv.pinImage.color = inRange
                        ? new Color(1.08f, 1.08f, 1.08f, 1f)
                        : Color.white;
                if (pv.disc != null)
                {
                    pv.disc.color = new Color(0f, 0f, 0f, inRange ? 0.48f : 0.32f);
                }
            }

            RefreshNearbyLegend();
        }

        void RefreshNearbyLegend()
        {
            if (_nearbySlots.Count == 0) return;
            _nearbySort.Clear();
            for (var i = 0; i < _pinViews.Count; i++)
                _nearbySort.Add(_pinViews[i].pin);
            _nearbySort.Sort((a, b) =>
            {
                var c = a.distanceM.CompareTo(b.distanceM);
                if (c != 0) return c;
                return MapZoneCatalog.RadarPriority(MapZoneCatalog.Parse(a.kind))
                    .CompareTo(MapZoneCatalog.RadarPriority(MapZoneCatalog.Parse(b.kind)));
            });

            for (var i = 0; i < _nearbySlots.Count; i++)
            {
                var slot = _nearbySlots[i];
                if (i >= _nearbySort.Count)
                {
                    if (slot.root != null) slot.root.SetActive(false);
                    slot.filled = false;
                    _nearbySlots[i] = slot;
                    continue;
                }

                var pin = _nearbySort[i];
                var zk = MapZoneCatalog.Parse(pin.kind);
                var inRange = pin.distanceM <= InteractRadius(zk);
                if (slot.root != null) slot.root.SetActive(true);
                slot.pin = pin;
                slot.filled = true;

                if (slot.slotBg != null)
                {
                    slot.slotBg.sprite = UiFoundation.WhiteSprite();
                    slot.slotBg.type = Image.Type.Simple;
                    slot.slotBg.color = inRange
                        ? new Color(0.12f, 0.42f, 0.55f, 1f)
                        : new Color(0.10f, 0.14f, 0.22f, 1f);
                }

                if (slot.icon != null)
                {
                    slot.icon.sprite = SpriteForKind(pin.kind) ?? UiFoundation.WhiteSprite();
                    slot.icon.color = Color.white;
                    slot.icon.preserveAspect = true;
                }

                if (slot.title != null)
                {
                    slot.title.text = MapZoneCatalog.Label(zk);
                    slot.title.color = Color.white;
                }

                if (slot.dist != null)
                {
                    slot.dist.text = pin.distanceM < 1000f
                        ? $"{pin.distanceM:0}m"
                        : $"{pin.distanceM / 1000f:0.0}k";
                    slot.dist.color = inRange ? Color.white : DuelystUi.Cyan;
                }

                _nearbySlots[i] = slot;
            }
        }

        void Update()
        {
            // PC / editor: WASD or arrow keys. Outdoor: real GPS also updates env.
            if (_env != null)
            {
                var e = 0f;
                var n = 0f;
                // Faster in editor / sim so desk play can reach pins
                var speed = ((_env != null && _env.UsingRealGps) ? 8f : 22f) * Time.unscaledDeltaTime;
                if (WrldzInput.KeyHeld(KeyCode.W) || WrldzInput.KeyHeld(KeyCode.UpArrow)) n += speed;
                if (WrldzInput.KeyHeld(KeyCode.S) || WrldzInput.KeyHeld(KeyCode.DownArrow)) n -= speed;
                if (WrldzInput.KeyHeld(KeyCode.A) || WrldzInput.KeyHeld(KeyCode.LeftArrow)) e -= speed;
                if (WrldzInput.KeyHeld(KeyCode.D) || WrldzInput.KeyHeld(KeyCode.RightArrow)) e += speed;

                if (e * e + n * n > 0.0001f)
                    _env.SimulateWalkMeters(e, n);
            }

            _npcField?.Tick();
            ApplyMapPanSmooth();
            AnimateWeatherFx();
            AnimatePinsAndAvatar();
            AnimateEyeGlow();
            TickStatusToast();
        }

        void AnimatePinsAndAvatar()
        {
            _pulseT += Time.unscaledDeltaTime;
            var pulse = 0.5f + 0.5f * Mathf.Sin(_pulseT * 2.4f);
            for (var i = 0; i < _pinViews.Count; i++)
            {
                var pv = _pinViews[i];
                if (pv.disc == null) continue;
                var inRange = pv.pin.distanceM <= InteractRadius(pv.kind);
                if (!inRange) continue;
                var bob = 1f + 0.04f * pulse;
                pv.disc.rectTransform.localScale = new Vector3(bob, bob, 1f);
            }
        }

        void ApplyEnvironmentVisuals()
        {
            if (_env == null) return;
            // Day/night + weather only via soft sky/fog overlays — never recolor map tiles
            if (_atmos != null)
                ApplyGoAtmosphere(_atmos, _env.Daylight01, _env.Weather);

            if (_envLabel != null)
            {
                _envLabel.text = string.IsNullOrEmpty(_env.SummaryLine)
                    ? _env.Weather.ToString()
                    : _env.SummaryLine;
            }

            RefreshWeatherDropVisibility();
            RefreshPinDistances();
        }

        /// <summary>
        /// Minimal GO atmosphere: soft sky wash + fog only. No city neon / battle-city stacks.
        /// </summary>
        static WrldzTheme.MapAtmosphereRefs BuildGoStyleAtmosphere(
            Transform parent, float daylight01, WeatherKind weather)
        {
            daylight01 = Mathf.Clamp01(daylight01);
            var refs = new WrldzTheme.MapAtmosphereRefs();
            // Very light top sky gradient feel
            refs.Void = WrldzTheme.StretchFill(parent, "SkyWash",
                Color.Lerp(new Color(0.25f, 0.32f, 0.45f, 0.22f), new Color(0.55f, 0.75f, 0.95f, 0.12f), daylight01));
            WrldzTheme.Place(refs.Void, 0f, 0.55f, 1f, 1f);
            refs.Void.GetComponent<Image>().raycastTarget = false;

            refs.Fog = WrldzTheme.StretchFill(parent, "SoftFog", FogGo(weather, daylight01));
            refs.Fog.GetComponent<Image>().raycastTarget = false;

            refs.WeatherOverlay = WrldzTheme.StretchFill(parent, "WeatherOverlay",
                weather is WeatherKind.Rain or WeatherKind.Storm
                    ? new Color(0.5f, 0.6f, 0.7f, 0.08f)
                    : new Color(1f, 1f, 1f, 0f));
            refs.WeatherOverlay.GetComponent<Image>().raycastTarget = false;

            refs.Vignette = null;
            refs.Field = null;
            refs.City = null;
            refs.Glow = null;
            refs.Wash = null;
            return refs;
        }

        static void ApplyGoAtmosphere(WrldzTheme.MapAtmosphereRefs refs, float daylight01, WeatherKind weather)
        {
            if (refs == null) return;
            daylight01 = Mathf.Clamp01(daylight01);
            if (refs.Void != null)
            {
                var img = refs.Void.GetComponent<Image>();
                if (img != null)
                    img.color = Color.Lerp(
                        new Color(0.22f, 0.28f, 0.42f, 0.28f),
                        new Color(0.55f, 0.75f, 0.95f, 0.10f),
                        daylight01);
            }

            if (refs.Fog != null)
            {
                var img = refs.Fog.GetComponent<Image>();
                if (img != null) img.color = FogGo(weather, daylight01);
            }

            if (refs.WeatherOverlay != null)
            {
                var img = refs.WeatherOverlay.GetComponent<Image>();
                if (img != null)
                    img.color = weather switch
                    {
                        WeatherKind.Storm => new Color(0.4f, 0.45f, 0.55f, 0.14f),
                        WeatherKind.Rain => new Color(0.5f, 0.58f, 0.65f, 0.08f),
                        WeatherKind.Fog => new Color(0.85f, 0.88f, 0.9f, 0.12f),
                        WeatherKind.Snow => new Color(0.92f, 0.94f, 0.98f, 0.08f),
                        _ => new Color(1f, 1f, 1f, 0f)
                    };
            }
        }

        static Color FogGo(WeatherKind weather, float day) => weather switch
        {
            WeatherKind.Fog => new Color(0.88f, 0.90f, 0.92f, 0.22f),
            WeatherKind.Storm => new Color(0.45f, 0.5f, 0.55f, 0.12f),
            WeatherKind.Rain => new Color(0.7f, 0.75f, 0.8f, 0.06f),
            _ => new Color(1f, 1f, 1f, 0f)
        };

        void BuildWeatherDrops()
        {
            _weatherDrops.Clear();
            for (var i = 0; i < 28; i++)
            {
                var drop = WrldzTheme.StretchFill(_weatherFxRoot, "Drop" + i, Color.white);
                var w = 0.008f + (i % 3) * 0.004f;
                var h = 0.02f + (i % 4) * 0.01f;
                var x = (i * 0.037f) % 1f;
                var y = (i * 0.11f) % 1f;
                WrldzTheme.Place(drop, x, y, x + w, y + h);
                drop.GetComponent<Image>().raycastTarget = false;
                _weatherDrops.Add(drop);
            }

            RefreshWeatherDropVisibility();
        }

        void RefreshWeatherDropVisibility()
        {
            if (_env == null) return;
            var show = _env.Weather is WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow;
            var col = _env.Weather switch
            {
                WeatherKind.Snow => new Color(0.95f, 0.97f, 1f, 0.65f),
                WeatherKind.Storm => new Color(0.55f, 0.7f, 1f, 0.55f),
                _ => new Color(0.6f, 0.8f, 1f, 0.45f)
            };
            foreach (var d in _weatherDrops)
            {
                if (d == null) continue;
                d.gameObject.SetActive(show);
                var img = d.GetComponent<Image>();
                if (img != null) img.color = col;
            }
        }

        void AnimateWeatherFx()
        {
            if (_env == null || _weatherDrops.Count == 0) return;
            if (_env.Weather is not (WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow))
                return;

            var speed = _env.Weather == WeatherKind.Storm ? 0.55f :
                _env.Weather == WeatherKind.Snow ? 0.12f : 0.35f;
            var dt = Time.unscaledDeltaTime * speed;
            foreach (var d in _weatherDrops)
            {
                if (d == null || !d.gameObject.activeSelf) continue;
                var min = d.anchorMin;
                var max = d.anchorMax;
                var h = max.y - min.y;
                min.y -= dt;
                max.y -= dt;
                if (max.y < 0f)
                {
                    min.y += 1.1f;
                    max.y = min.y + h;
                    var x = Random.value * 0.95f;
                    var w = max.x - min.x;
                    min.x = x;
                    max.x = x + w;
                }

                d.anchorMin = min;
                d.anchorMax = max;
            }

            if (_env.Weather == WeatherKind.Storm && _atmos?.WeatherOverlay != null)
            {
                _fxTimer += Time.unscaledDeltaTime;
                if (_fxTimer > 2.5f && Random.value < 0.02f)
                {
                    _fxTimer = 0f;
                    var img = _atmos.WeatherOverlay.GetComponent<Image>();
                    if (img != null)
                        StartCoroutine(StormFlash(img));
                }
            }
        }

        System.Collections.IEnumerator StormFlash(Image img)
        {
            var baseC = img.color;
            img.color = new Color(0.85f, 0.9f, 1f, 0.35f);
            yield return new WaitForSecondsRealtime(0.07f);
            if (img != null) img.color = baseC;
        }

        /// <summary>Top chrome: compass nearby radar only.</summary>
        void BuildGoTopHud(Transform root, LocalAccountStore.Account account)
        {
            GoTheme.CircleButton(root, "Compass",
                ImagineAssets.IconCompass() ?? GoTheme.Compass() ?? UiFoundation.WhiteSprite(), Color.white,
                0.88f, 0.90f, 0.98f, 0.98f, ToggleCompassNearby);
            BuildCompassNearby(root);
        }

        /// <summary>
        /// Wallet as three stacked full-width rows (icon + amount). Three columns
        /// clip 7–8 digit comma values on a phone-width Eye sheet.
        /// </summary>
        void BuildCurrencyStrip(Transform root, LocalAccountStore.Account account)
        {
            var strip = new GameObject("Currency", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(CanvasGroup), typeof(Button));
            strip.transform.SetParent(root, false);
            var rt = strip.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.08f, 1f);
            rt.anchorMax = new Vector2(0.92f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 156f);
            rt.anchoredPosition = new Vector2(0f, -10f);

            var plate = strip.GetComponent<Image>();
            plate.sprite = UiTheme.RoundedRectSprite() ?? UiFoundation.WhiteSprite();
            plate.type = plate.sprite != null && plate.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced : Image.Type.Simple;
            plate.color = new Color(0.04f, 0.07f, 0.12f, 0.72f);
            plate.raycastTarget = true;

            var v = strip.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.padding = new RectOffset(8, 8, 8, 8);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            _currencyCg = MenuMotion.EnsureGroup(strip);
            _currencyCg.alpha = 0f;
            var btn = strip.GetComponent<Button>();
            btn.targetGraphic = plate;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                OpenArtifactsOverlay(null);
            });

            var p = account?.progress;
            _curDigi = CurrencyChip(strip.transform, "Digi", ImagineAssets.IconDigizeni(),
                DuelystUi.Cyan, p?.digizeni ?? 0, () => OpenArtifactsOverlay(ArtifactService.Digizeni));
            _curCoins = CurrencyChip(strip.transform, "Coin", ImagineAssets.IconDuelCoin(),
                DuelystUi.GoldHot, p?.duelCoin ?? 0, () => OpenArtifactsOverlay(ArtifactService.DuelCoin));
            _curEnergy = CurrencyChip(strip.transform, "SE", ImagineAssets.IconSetEnergy(),
                DuelystUi.Green, p?.setEnergy ?? 0, () =>
                {
                    var inv = AppSession.Ensure().Account?.inventory;
                    OpenArtifactsOverlay(ArtifactService.FirstSetEnergyId(inv) ?? ArtifactService.SetEnergyFocus);
                });
            _currencyLine = _curDigi;
        }

        static Text CurrencyChip(Transform parent, string name, Sprite icon, Color accent, int value,
            System.Action onClick)
        {
            var go = new GameObject("Chip_" + name, typeof(RectTransform), typeof(Image), typeof(LayoutElement),
                typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiTheme.RoundedRectSprite() ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced : Image.Type.Simple;
            img.color = new Color(0.08f, 0.12f, 0.20f, 0.94f);
            img.raycastTarget = true;
            var chipBtn = go.GetComponent<Button>();
            chipBtn.targetGraphic = img;
            chipBtn.transition = Selectable.Transition.None;
            if (onClick != null)
                chipBtn.onClick.AddListener(() => onClick());
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(accent.r, accent.g, accent.b, 0.75f);
            ol.effectDistance = new Vector2(1.6f, -1.6f);
            ol.useGraphicAlpha = false;
            var le = go.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
            le.minHeight = 42f;
            le.preferredHeight = 44f;

            if (icon != null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(go.transform, false);
                var ir = ico.GetComponent<RectTransform>();
                ir.anchorMin = new Vector2(0.02f, 0.10f);
                ir.anchorMax = new Vector2(0.16f, 0.90f);
                ir.offsetMin = Vector2.zero;
                ir.offsetMax = Vector2.zero;
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
            }

            var t = GoTheme.Label(go.transform, "V", FormatWallet(value), 16, Color.white,
                TextAnchor.MiddleLeft, bold: true);
            WrldzType.StyleButtonLabel(t, 16, display: false);
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            GoTheme.Place(t.rectTransform, 0.18f, 0.04f, 0.98f, 0.96f);
            return t;
        }

        static string FormatWallet(int n) => n.ToString("N0");

        void RefreshCurrencyStrip()
        {
            var acc = AppSession.Ensure()?.Account;
            acc?.EnsureProgress();
            var p = acc?.progress;
            if (_curDigi != null) _curDigi.text = FormatWallet(p?.digizeni ?? 0);
            if (_curCoins != null) _curCoins.text = FormatWallet(p?.duelCoin ?? 0);
            if (_curEnergy != null) _curEnergy.text = FormatWallet(p?.setEnergy ?? 0);
        }

        /// <summary>
        /// Compass popout: nearest landmarks, sorted by distance. Hidden until tapped.
        /// Solid navy plate + stacked header, matching the deck-switch list.
        /// </summary>
        void BuildCompassNearby(Transform root)
        {
            _nearbySlots.Clear();
            var plate = new GameObject("CompassNearby", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(root, false);
            // Sit under the compass orb (0.90–0.98 y) so the header never collides with it.
            GoTheme.Place(plate.GetComponent<RectTransform>(), 0.42f, 0.34f, 0.985f, 0.885f);
            _nearbyPlateImg = plate.GetComponent<Image>();
            _nearbyPlateImg.sprite = UiFoundation.WhiteSprite();
            _nearbyPlateImg.type = Image.Type.Simple;
            _nearbyPlateImg.color = new Color(0.05f, 0.08f, 0.14f, 0.96f);
            _nearbyPlateImg.raycastTarget = true;
            plate.SetActive(false);
            _compassCg = MenuMotion.EnsureGroup(plate);

            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(plate.transform, false);
            GoTheme.Place(header.GetComponent<RectTransform>(), 0f, 0.80f, 1f, 1f);
            var headerImg = header.GetComponent<Image>();
            headerImg.sprite = UiFoundation.WhiteSprite();
            headerImg.color = new Color(0.08f, 0.12f, 0.20f, 1f);
            headerImg.raycastTarget = false;

            var cap = GoTheme.Label(header.transform, "Cap", "NEARBY", 16, Color.white,
                TextAnchor.MiddleLeft, bold: true);
            cap.horizontalOverflow = HorizontalWrapMode.Overflow;
            cap.resizeTextForBestFit = false;
            GoTheme.Place(cap.rectTransform, 0.05f, 0.46f, 0.62f, 0.92f);

            _envLabel = GoTheme.Label(header.transform, "W", "—", 13, DuelystUi.Cyan,
                TextAnchor.MiddleLeft, bold: false);
            _envLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _envLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _envLabel.resizeTextForBestFit = false;
            GoTheme.Place(_envLabel.rectTransform, 0.05f, 0.08f, 0.62f, 0.46f);

            NearbyCircleAction(header.transform, "Recenter",
                ImagineAssets.PinAnchor() ?? GoTheme.PinAnchor() ?? ImagineAssets.IconCompass(),
                0.66f, 0.10f, 0.80f, 0.90f, () =>
                {
                    _env?.RecenterOriginHere();
                    ApplyGpsMapPan();
                    SetStatus("Recentered");
                });

            NearbyIconButton(header.transform, "Close",
                ImagineAssets.BtnClose() ?? DuelystUi.BtnClose() ?? UiFoundation.WhiteSprite(),
                0.82f, 0.10f, 0.96f, 0.90f, () => SetCompassOpen(false));

            var list = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(plate.transform, false);
            GoTheme.Place(list.GetComponent<RectTransform>(), 0.04f, 0.03f, 0.96f, 0.78f);
            var vlg = list.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            const float rowH = 48f;
            for (var i = 0; i < NearbySlotCount; i++)
            {
                var slotGo = new GameObject("Near_" + i, typeof(RectTransform), typeof(Image),
                    typeof(Button), typeof(LayoutElement));
                slotGo.transform.SetParent(list.transform, false);
                var le = slotGo.GetComponent<LayoutElement>();
                le.minHeight = rowH;
                le.preferredHeight = rowH;
                le.flexibleHeight = 0f;
                var bg = slotGo.GetComponent<Image>();
                bg.sprite = UiFoundation.WhiteSprite();
                bg.type = Image.Type.Simple;
                bg.color = new Color(0.10f, 0.14f, 0.22f, 1f);
                bg.raycastTarget = true;

                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(slotGo.transform, false);
                GoTheme.Place(iconGo.GetComponent<RectTransform>(), 0.03f, 0.14f, 0.16f, 0.86f);
                var icon = iconGo.GetComponent<Image>();
                icon.sprite = UiFoundation.WhiteSprite();
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                var title = GoTheme.Label(slotGo.transform, "T", "", 16, Color.white,
                    TextAnchor.MiddleLeft, bold: true);
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                title.verticalOverflow = VerticalWrapMode.Truncate;
                title.resizeTextForBestFit = false;
                GoTheme.Place(title.rectTransform, 0.18f, 0.08f, 0.70f, 0.92f);

                var dist = GoTheme.Label(slotGo.transform, "D", "—", 16, DuelystUi.Cyan,
                    TextAnchor.MiddleRight, bold: true);
                dist.horizontalOverflow = HorizontalWrapMode.Overflow;
                dist.resizeTextForBestFit = false;
                GoTheme.Place(dist.rectTransform, 0.70f, 0.08f, 0.96f, 0.92f);

                var idx = i;
                var btn = slotGo.GetComponent<Button>();
                btn.targetGraphic = bg;
                btn.transition = Selectable.Transition.ColorTint;
                var cols = btn.colors;
                cols.normalColor = Color.white;
                cols.highlightedColor = new Color(0.75f, 0.92f, 1f, 1f);
                cols.pressedColor = new Color(0.55f, 0.78f, 0.95f, 1f);
                cols.selectedColor = Color.white;
                cols.fadeDuration = 0.04f;
                btn.colors = cols;
                btn.onClick.AddListener(() => OnNearbySlot(idx));

                _nearbySlots.Add(new NearbySlot
                {
                    root = slotGo, icon = icon, slotBg = bg,
                    title = title, dist = dist, filled = false
                });
                slotGo.SetActive(false);
            }

            plate.SetActive(false);
            _compassPop = plate;
            _compassOpen = false;
        }

        static void NearbyIconButton(Transform parent, string name, Sprite spr,
            float x0, float y0, float x1, float y1, System.Action onClick)
        {
            var hold = new GameObject(name + "Hit", typeof(RectTransform));
            hold.transform.SetParent(parent, false);
            GoTheme.Place(hold.GetComponent<RectTransform>(), x0, y0, x1, y1);

            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(AspectRatioFitter));
            go.transform.SetParent(hold.transform, false);
            GoTheme.Stretch(go.GetComponent<RectTransform>());
            var fit = go.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;

            var img = go.GetComponent<Image>();
            img.sprite = spr ?? UiFoundation.WhiteSprite();
            img.preserveAspect = true;
            img.color = Color.white;
            img.raycastTarget = true;
            img.type = Image.Type.Simple;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = new Color(0.75f, 0.92f, 1f, 1f);
            cols.pressedColor = new Color(0.55f, 0.78f, 0.95f, 1f);
            cols.fadeDuration = 0.04f;
            btn.colors = cols;
            if (onClick != null)
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    onClick();
                });
        }

        static void NearbyCircleAction(Transform parent, string name, Sprite icon,
            float x0, float y0, float x1, float y1, System.Action onClick)
        {
            var hold = new GameObject(name + "Hit", typeof(RectTransform), typeof(Button));
            hold.transform.SetParent(parent, false);
            GoTheme.Place(hold.GetComponent<RectTransform>(), x0, y0, x1, y1);

            var discGo = new GameObject(name + "Disc", typeof(RectTransform), typeof(AspectRatioFitter));
            discGo.transform.SetParent(hold.transform, false);
            GoTheme.Stretch(discGo.GetComponent<RectTransform>());
            var fit = discGo.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;

            var disc = DuelystUi.BtnCircle() ?? UiFoundation.WhiteSprite();
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(discGo.transform, false);
            GoTheme.Stretch(ring.GetComponent<RectTransform>());
            var ringImg = ring.GetComponent<Image>();
            ringImg.sprite = disc;
            ringImg.preserveAspect = true;
            ringImg.color = DuelystUi.Cyan;
            ringImg.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(discGo.transform, false);
            GoTheme.Place(fill.GetComponent<RectTransform>(), 0.12f, 0.12f, 0.88f, 0.88f);
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = disc;
            fillImg.preserveAspect = true;
            fillImg.color = new Color(0.08f, 0.14f, 0.24f, 1f);
            fillImg.raycastTarget = false;

            if (icon != null)
            {
                var ico = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(discGo.transform, false);
                GoTheme.Place(ico.GetComponent<RectTransform>(), 0.22f, 0.22f, 0.78f, 0.78f);
                var icoImg = ico.GetComponent<Image>();
                icoImg.sprite = icon;
                icoImg.preserveAspect = true;
                icoImg.color = Color.white;
                icoImg.raycastTarget = false;
            }

            var hit = new GameObject("Hit", typeof(RectTransform), typeof(Image));
            hit.transform.SetParent(hold.transform, false);
            GoTheme.Stretch(hit.GetComponent<RectTransform>());
            var hitImg = hit.GetComponent<Image>();
            hitImg.sprite = UiFoundation.WhiteSprite();
            hitImg.color = new Color(1f, 1f, 1f, 0.01f);
            hitImg.raycastTarget = true;

            var btn = hold.GetComponent<Button>();
            btn.targetGraphic = hitImg;
            btn.transition = Selectable.Transition.ColorTint;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = new Color(0.75f, 0.92f, 1f, 1f);
            cols.pressedColor = new Color(0.55f, 0.78f, 0.95f, 1f);
            cols.fadeDuration = 0.04f;
            btn.colors = cols;
            if (onClick != null)
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    onClick();
                });
        }

        void ToggleCompassNearby()
        {
            FreeUiKit.PlaySelect();
            SetCompassOpen(!_compassOpen);
        }

        void SetCompassOpen(bool open)
        {
            if (open == _compassOpen && (_compassPop == null || _compassPop.activeSelf == open))
                return;
            _compassOpen = open;
            if (open) SetMenuOpen(false);
            if (_compassPop == null) return;
            if (open)
            {
                _compassPop.transform.SetAsLastSibling();
                RefreshNearbyLegend();
                MenuMotion.Play(this, MenuMotion.SheetIn(_compassPop, MenuMotion.Sheet));
            }
            else
                MenuMotion.Play(this, MenuMotion.SheetOut(_compassPop, MenuMotion.Snap));
        }

        void OnNearbySlot(int index)
        {
            if (index < 0 || index >= _nearbySlots.Count) return;
            var slot = _nearbySlots[index];
            if (!slot.filled) return;
            FreeUiKit.PlaySelect();
            SetCompassOpen(false);
            InteractWithPin(slot.pin);
        }

        /// <summary>Distinct art per kind — never collapse all pins to one glyph.</summary>
        static Sprite SpriteForKind(string kind)
        {
            var k = MapZoneCatalog.Parse(kind);
            return k switch
            {
                MapZoneKind.Tear => GoTheme.PinTear(),
                MapZoneKind.Portal => GoTheme.PinPortal(),
                MapZoneKind.Raid => GoTheme.PinRaid(),
                MapZoneKind.Bazaar => GoTheme.PinBazaar(),
                MapZoneKind.Training => GoTheme.PinTraining(),
                MapZoneKind.Tournament => GoTheme.PinTournament(),
                MapZoneKind.Event => GoTheme.PinEvent(),
                MapZoneKind.Npc => GoTheme.PinNpc(),
                MapZoneKind.Treasure => GoTheme.PinTreasure(),
                MapZoneKind.Anchor => GoTheme.PinAnchor(),
                MapZoneKind.Story => GoTheme.PinStory(),
                MapZoneKind.Pvp => GoTheme.PinPvp(),
                _ => GoTheme.PinTear()
            } ?? FreeUiKit.Star();
        }

        /// <summary>
        /// Map chrome: profile badge · deck switcher · Millennium Eye.
        /// Compass lives on the top HUD. Everything else opens from the Eye.
        /// </summary>
        void BuildGoBottomChrome(Transform root, LocalAccountStore.Account account)
        {
            var veil = new GameObject("MenuVeil", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Button));
            veil.transform.SetParent(root, false);
            GoTheme.Stretch(veil.GetComponent<RectTransform>());
            var vImg = veil.GetComponent<Image>();
            vImg.sprite = UiFoundation.WhiteSprite();
            vImg.color = new Color(0.02f, 0.05f, 0.12f, 1f);
            vImg.raycastTarget = true;
            _menuVeil = veil;
            _menuVeilCg = veil.GetComponent<CanvasGroup>();
            _menuVeilCg.alpha = 0f;
            _menuVeilCg.blocksRaycasts = false;
            _menuVeilCg.interactable = false;
            var vBtn = veil.GetComponent<Button>();
            vBtn.targetGraphic = vImg;
            vBtn.transition = Selectable.Transition.None;
            vBtn.onClick.AddListener(() => SetMenuOpen(false));
            veil.SetActive(false);

            StreamingSprite.ClearCache("WRLDZ/Imagine/ui/panel_menu_glass.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/ui/bar_bottom.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/ui/hud_chip_plate.png");
            var glassGo = new GameObject("MenuGlass", typeof(RectTransform), typeof(Image));
            glassGo.transform.SetParent(root, false);
            _menuGlass = glassGo.GetComponent<Image>();
            _menuGlass.raycastTarget = false;
            PlaceMenuGlass(false);

            BuildBottomProfile(root, account);
            BuildDeckSwitcher(root);

            StreamingSprite.ClearCache("WRLDZ/Imagine/icons/icon_millennium_eye_menu.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/icons/icon_millennium_eye_open.png");
            foreach (var icon in new[]
                     {
                         "icon_deck", "icon_bag", "icon_story", "icon_settings", "icon_bazaar",
                         "icon_tome", "icon_duel", "icon_vs_pvp", "icon_practice", "icon_compass",
                         "icon_menu"
                     })
                StreamingSprite.ClearCache("WRLDZ/Imagine/icons/" + icon + ".png");
            foreach (var pin in new[]
                     {
                         "pin_tear", "pin_portal", "pin_raid", "pin_arena", "pin_bazaar",
                         "pin_training", "pin_tournament", "pin_event", "pin_npc",
                         "pin_treasure", "pin_anchor", "pin_story", "pin_pvp"
                     })
                StreamingSprite.ClearCache("WRLDZ/Imagine/pins/" + pin + ".png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/fx/fx_eye_pupil_red.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/fx/fx_eye_red_halo.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/fx/fx_eye_pupil_core.png");
            _eyeIdleSpr = ImagineAssets.IconMillenniumEyeMenu();
            _eyeLightMat = MakeEyeLightMaterial();

            var flashGo = new GameObject("EyeOpenFlash", typeof(RectTransform), typeof(Image));
            flashGo.transform.SetParent(root, false);
            if (_menuVeil != null)
                flashGo.transform.SetSiblingIndex(_menuVeil.transform.GetSiblingIndex() + 1);
            _eyeFlashRt = flashGo.GetComponent<RectTransform>();
            _eyeFlashRt.anchorMin = new Vector2(0.5f, 0.5f);
            _eyeFlashRt.anchorMax = new Vector2(0.5f, 0.5f);
            _eyeFlashRt.pivot = new Vector2(0.5f, 0.5f);
            _eyeFlashRt.sizeDelta = new Vector2(160f, 160f);
            _eyeFlashRt.anchoredPosition = Vector2.zero;
            _eyeFlash = flashGo.GetComponent<Image>();
            _eyeFlash.sprite = ImagineAssets.FxEyeWhiteGlow()
                               ?? ImagineAssets.FxEyeBurstRing()
                               ?? GoTheme.LevelRing()
                               ?? UiFoundation.WhiteSprite();
            _eyeFlash.preserveAspect = true;
            _eyeFlash.raycastTarget = false;
            _eyeFlash.color = new Color(1f, 0.97f, 0.90f, 0f);
            if (_eyeLightMat != null)
                _eyeFlash.material = new Material(_eyeLightMat);
            flashGo.SetActive(false);

            var menuGo = new GameObject("MainMenuEye", typeof(RectTransform));
            menuGo.transform.SetParent(root, false);
            _eyeRt = menuGo.GetComponent<RectTransform>();
            _eyeRt.anchorMin = Vector2.zero;
            _eyeRt.anchorMax = Vector2.zero;
            _eyeRt.pivot = new Vector2(0.5f, 0.5f);
            _eyeRt.sizeDelta = new Vector2(154f, 154f);
            _eyeRt.anchoredPosition = Vector2.zero;

            // Square disc so circular glows never stretch into the slot's rectangle.
            var discGo = new GameObject("EyeDisc", typeof(RectTransform), typeof(AspectRatioFitter));
            discGo.transform.SetParent(menuGo.transform, false);
            var discRt = discGo.GetComponent<RectTransform>();
            discRt.anchorMin = Vector2.zero;
            discRt.anchorMax = Vector2.one;
            discRt.offsetMin = Vector2.zero;
            discRt.offsetMax = Vector2.zero;
            discRt.pivot = new Vector2(0.5f, 0.5f);
            var discFit = discGo.GetComponent<AspectRatioFitter>();
            discFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            discFit.aspectRatio = 1f;

            Image GlowLayer(string name, Transform parent, Sprite spr, float discScale,
                Vector2 sizeDelta, Color tint, float span = 0f)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0.5f, 0.5f);
                if (span > 0.01f)
                {
                    var pad = (1f - span) * 0.5f;
                    rt.anchorMin = new Vector2(pad, pad);
                    rt.anchorMax = new Vector2(1f - pad, 1f - pad);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
                else if (sizeDelta.sqrMagnitude > 0.01f)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = sizeDelta;
                }
                else
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    go.transform.localScale = new Vector3(discScale, discScale, 1f);
                }

                var img = go.GetComponent<Image>();
                img.sprite = spr ?? UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = tint;
                img.raycastTarget = false;
                if (_eyeLightMat != null)
                    img.material = new Material(_eyeLightMat);
                return img;
            }

            _eyeWhiteGlow = GlowLayer("EyeWhiteGlow", discGo.transform,
                ImagineAssets.FxEyeWhiteGlow() ?? WrldzPresentation.MenuGlow() ?? GoTheme.LevelRing(),
                1.08f, Vector2.zero, new Color(1f, 1f, 1f, 0.42f));

            var burstGo = new GameObject("EyeBurst", typeof(RectTransform), typeof(Image));
            burstGo.transform.SetParent(discGo.transform, false);
            burstGo.transform.SetAsFirstSibling();
            _eyeBurstRt = burstGo.GetComponent<RectTransform>();
            _eyeBurstRt.anchorMin = Vector2.zero;
            _eyeBurstRt.anchorMax = Vector2.one;
            _eyeBurstRt.offsetMin = Vector2.zero;
            _eyeBurstRt.offsetMax = Vector2.zero;
            _eyeBurstRt.localScale = Vector3.one;
            _eyeBurst = burstGo.GetComponent<Image>();
            _eyeBurst.sprite = ImagineAssets.FxEyeBurstRing() ?? GoTheme.LevelRing() ?? UiFoundation.WhiteSprite();
            _eyeBurst.preserveAspect = true;
            _eyeBurst.raycastTarget = false;
            _eyeBurst.color = new Color(0.45f, 0.95f, 1f, 0f);
            if (_eyeLightMat != null)
                _eyeBurst.material = new Material(_eyeLightMat);

            var shadow = new GameObject("EyeShadow", typeof(RectTransform), typeof(Image));
            shadow.transform.SetParent(discGo.transform, false);
            var srt = shadow.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.12f, -0.08f);
            srt.anchorMax = new Vector2(0.88f, 0.22f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var sImg = shadow.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0f, 0f, 0f, 0.35f);
            sImg.raycastTarget = false;

            var artGo = new GameObject("EyeArt", typeof(RectTransform), typeof(Image), typeof(Button));
            artGo.transform.SetParent(discGo.transform, false);
            GoTheme.Stretch(artGo.GetComponent<RectTransform>());
            _mainMenuImg = artGo.GetComponent<Image>();
            var eyeSpr = _eyeIdleSpr
                ?? DuelystUi.BtnGold()
                ?? DuelystUi.BtnCircle()
                ?? UiFoundation.WhiteSprite();
            _mainMenuImg.sprite = eyeSpr;
            _mainMenuImg.type = Image.Type.Simple;
            _mainMenuImg.preserveAspect = true;
            _mainMenuImg.color = Color.white;
            _mainMenuImg.raycastTarget = true;

            var menuBtn = artGo.GetComponent<Button>();
            menuBtn.targetGraphic = _mainMenuImg;
            menuBtn.transition = Selectable.Transition.None;
            menuBtn.onClick.AddListener(() => ToggleMainMenu(root));

            menuGo.transform.SetAsLastSibling();

            BuildEyeMenu(root, account);
            RelayoutDeckChipAndEye();
            StartCoroutine(RelayoutDeckChipAndEyeNextFrame());
        }

        System.Collections.IEnumerator RelayoutDeckChipAndEyeNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            RelayoutDeckChipAndEye();
        }

        void PlaceMenuGlass(bool menuOpen)
        {
            if (_menuGlass == null) return;
            var rt = _menuGlass.rectTransform;
            float y1 = menuOpen ? MenuGlassOpenY1 : _menuGlassClosedY1;
            GoTheme.Place(rt, MenuGlassX0, MenuGlassY0, MenuGlassX1, y1);
            ApplyMenuGlassSprite(menuOpen);
            _menuGlass.raycastTarget = menuOpen;
        }

        /// <summary>
        /// Closed HUD uses <c>bar_bottom</c> (12px vertical borders). Tall Eye sheet
        /// uses <c>panel_menu_glass</c>. Never 9-slice the tall plate onto the short bar.
        /// </summary>
        void ApplyMenuGlassSprite(bool eyeOpen)
        {
            if (_menuGlass == null) return;
            var ol = _menuGlass.GetComponent<Outline>();
            if (eyeOpen)
            {
                var sheet = ImagineAssets.PanelMenuGlass() ?? ImagineAssets.PanelHolo();
                if (sheet != null && sheet.border.sqrMagnitude > 0)
                {
                    _menuGlass.sprite = sheet;
                    _menuGlass.type = Image.Type.Sliced;
                    _menuGlass.color = Color.white;
                    if (ol != null) ol.enabled = false;
                    return;
                }
            }

            var bar = ImagineAssets.BarBottom() ?? ImagineAssets.BarTopHud() ?? ImagineAssets.HudChip();
            if (bar != null && bar.border.sqrMagnitude > 0)
            {
                _menuGlass.sprite = bar;
                _menuGlass.type = Image.Type.Sliced;
                // Translucent — orbs float over the map instead of sitting in a black well.
                _menuGlass.color = new Color(1f, 1f, 1f, 0.55f);
                if (ol != null) ol.enabled = false;
                return;
            }

            _menuGlass.sprite = UiFoundation.WhiteSprite();
            _menuGlass.type = Image.Type.Simple;
            _menuGlass.color = new Color(0.05f, 0.08f, 0.14f, 0.50f);
            ol = ol ?? _menuGlass.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.40f, 0.85f, 1f, 0.70f);
            ol.effectDistance = new Vector2(2f, -2f);
            ol.useGraphicAlpha = false;
            ol.enabled = true;
        }

        static Material MakeEyeLightMaterial()
        {
            // UI/Default keeps circular PNG alpha. Particle additive stretched the slot rect.
            var sh = Shader.Find("UI/Default");
            if (sh == null) return null;
            var mat = new Material(sh) { name = "EyeLightUi" };
            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }

        /// <summary>
        /// Trainer badge on the bottom-left of the menu bar — same family as the
        /// Millennium Eye: glass circle, gold pip, XP chord. Tap opens avatar.
        /// </summary>
        void BuildBottomProfile(Transform root, LocalAccountStore.Account account)
        {
            var go = new GameObject("GoProfile", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            _profileRt = rt;
            rt.anchorMin = new Vector2(0.038f, 0f);
            rt.anchorMax = new Vector2(0.038f, 0f);
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(154f, 154f);

            var frame = go.GetComponent<Image>();
            frame.sprite = DuelystUi.BtnCircle() ?? GoTheme.OrbProfile() ?? UiFoundation.WhiteSprite();
            frame.preserveAspect = true;
            frame.color = Color.white;
            frame.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = frame;
            btn.transition = Selectable.Transition.ColorTint;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = new Color(1.05f, 1.02f, 0.9f, 1f);
            cols.pressedColor = new Color(0.88f, 0.82f, 0.65f, 1f);
            cols.selectedColor = Color.white;
            cols.colorMultiplier = 1f;
            cols.fadeDuration = 0.08f;
            btn.colors = cols;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                SetMenuOpen(false);
                OpenAvatarMenus();
            });

            var shadow = new GameObject("BadgeShadow", typeof(RectTransform), typeof(Image));
            shadow.transform.SetParent(go.transform, false);
            shadow.transform.SetAsFirstSibling();
            var srt = shadow.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.12f, -0.08f);
            srt.anchorMax = new Vector2(0.88f, 0.20f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var sImg = shadow.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0f, 0f, 0f, 0.38f);
            sImg.raycastTarget = false;

            var clip = new GameObject("FaceClip", typeof(RectTransform), typeof(Image), typeof(Mask));
            clip.transform.SetParent(go.transform, false);
            GoTheme.Place(clip.GetComponent<RectTransform>(), 0.16f, 0.16f, 0.84f, 0.84f);
            var clipImg = clip.GetComponent<Image>();
            clipImg.sprite = DuelystUi.BtnCircle() ?? UiFoundation.WhiteSprite();
            clipImg.preserveAspect = true;
            clipImg.color = Color.white;
            clipImg.raycastTarget = false;
            clip.GetComponent<Mask>().showMaskGraphic = false;

            _hudPortrait = AvatarPortraitView.CreateFullBodyFill(clip.transform, hideBackground: true, badgeCrop: true);
            _hudPortrait.Apply(account != null ? account.GetAvatarOrDefault() : AvatarAppearance.Default());

            var pip = new GameObject("LevelPip", typeof(RectTransform));
            pip.transform.SetParent(go.transform, false);
            GoTheme.Place(pip.GetComponent<RectTransform>(), 0.62f, 0.02f, 0.98f, 0.38f);
            var disc = DuelystUi.BtnCircle() ?? UiFoundation.WhiteSprite();
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(pip.transform, false);
            GoTheme.Stretch(ring.GetComponent<RectTransform>());
            var ringImg = ring.GetComponent<Image>();
            ringImg.sprite = disc;
            ringImg.preserveAspect = true;
            ringImg.color = DuelystUi.Cyan;
            ringImg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(pip.transform, false);
            GoTheme.Place(fill.GetComponent<RectTransform>(), 0.12f, 0.12f, 0.88f, 0.88f);
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = disc;
            fillImg.preserveAspect = true;
            fillImg.color = new Color(0.08f, 0.14f, 0.24f, 1f);
            fillImg.raycastTarget = false;

            var lv = account?.progress != null ? Mathf.Max(1, account.progress.level) : _spiritRank;
            _rankLabel = GoTheme.Label(pip.transform, "Lv", lv.ToString(), 15, Color.white,
                TextAnchor.MiddleCenter, bold: true);
            _rankLabel.resizeTextForBestFit = true;
            _rankLabel.resizeTextMinSize = 10;
            _rankLabel.resizeTextMaxSize = 16;
            GoTheme.Place(_rankLabel.rectTransform, 0.12f, 0.12f, 0.88f, 0.88f);

            _soulBadge = SoulBadgeView.Create(go.transform);
            GoTheme.Place(_soulBadge.GetComponent<RectTransform>(), -0.04f, 0.62f, 0.36f, 1.02f);
            _soulBadge.Bind(account?.progress);

            var xpSlot = new GameObject("XpSlot", typeof(RectTransform));
            xpSlot.transform.SetParent(go.transform, false);
            GoTheme.Place(xpSlot.GetComponent<RectTransform>(), 0.16f, 0.00f, 0.84f, 0.10f);
            BuildXpBar(xpSlot.transform, account, compact: true);

            go.transform.SetAsLastSibling();
        }

        /// <summary>Deck orb — same circular family as the Eye, no glow or flash. Tap lists on-hand decks.</summary>
        void BuildDeckSwitcher(Transform overlayRoot)
        {
            const float eyePx = 154f;
            var orbPx = eyePx * 0.8f;

            var go = new GameObject("Nav_MainDeck", typeof(RectTransform));
            var host = _menuGlass != null ? _menuGlass.transform : overlayRoot;
            go.transform.SetParent(host, false);
            _deckSwitchRt = go.GetComponent<RectTransform>();
            _deckSwitchRt.anchorMin = new Vector2(0.5f, 0f);
            _deckSwitchRt.anchorMax = new Vector2(0.5f, 0f);
            _deckSwitchRt.pivot = new Vector2(0.5f, 0.5f);
            _deckSwitchRt.sizeDelta = new Vector2(orbPx, orbPx);
            _deckSwitchRt.anchoredPosition = Vector2.zero;

            var discGo = new GameObject("DeckDisc", typeof(RectTransform), typeof(AspectRatioFitter));
            discGo.transform.SetParent(go.transform, false);
            var discRt = discGo.GetComponent<RectTransform>();
            discRt.anchorMin = Vector2.zero;
            discRt.anchorMax = Vector2.one;
            discRt.offsetMin = Vector2.zero;
            discRt.offsetMax = Vector2.zero;
            discRt.pivot = new Vector2(0.5f, 0.5f);
            var discFit = discGo.GetComponent<AspectRatioFitter>();
            discFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            discFit.aspectRatio = 1f;

            var shadow = new GameObject("DeckShadow", typeof(RectTransform), typeof(Image));
            shadow.transform.SetParent(discGo.transform, false);
            var srt = shadow.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.12f, -0.08f);
            srt.anchorMax = new Vector2(0.88f, 0.22f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var sImg = shadow.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0f, 0f, 0f, 0.35f);
            sImg.raycastTarget = false;

            var artGo = new GameObject("DeckArt", typeof(RectTransform), typeof(Image), typeof(Button));
            artGo.transform.SetParent(discGo.transform, false);
            GoTheme.Stretch(artGo.GetComponent<RectTransform>());
            var art = artGo.GetComponent<Image>();
            art.sprite = ImagineAssets.IconDeck() ?? DuelystUi.IconDeck() ?? DuelystUi.BtnCircle()
                         ?? UiFoundation.WhiteSprite();
            art.type = Image.Type.Simple;
            art.preserveAspect = true;
            art.color = Color.white;
            art.raycastTarget = true;

            var btn = artGo.GetComponent<Button>();
            btn.targetGraphic = art;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                SetMenuOpen(false);
                ToggleDeckSwitchMenu(overlayRoot);
            });

            _deckSwitchLabel = null;
            RefreshDeckSwitchLabel();
        }

        void RefreshDeckSwitchLabel()
        {
            RelayoutDeckChipAndEye();
        }

        void RelayoutDeckChipAndEye()
        {
            if (_deckSwitchRt == null) return;
            var glass = _deckSwitchRt.parent as RectTransform;
            if (glass == null) return;
            var hud = glass.parent as RectTransform;
            var hudW = hud != null ? hud.rect.width : glass.rect.width;
            var glassW = glass.rect.width;
            if (hudW < 32f || glassW < 32f) return;

            var hudH = hud != null ? hud.rect.height : 1920f;

            float insetX = 22f, insetY = 14f;
            if (_menuGlass != null && _menuGlass.sprite != null)
            {
                var b = _menuGlass.sprite.border;
                insetX = Mathf.Max(18f, b.x * 0.55f);
                insetY = Mathf.Max(12f, Mathf.Min(b.y, b.w) * 0.55f);
            }

            const float orbPx = 154f;
            var deckPx = orbPx * 0.8f;
            if (!_menuOpen && _deckSwitchPop == null)
            {
                var closedPx = orbPx + insetY * 2f + 8f;
                _menuGlassClosedY1 = MenuGlassY0 + closedPx / Mathf.Max(1f, hudH);
                PlaceMenuGlass(false);
                Canvas.ForceUpdateCanvases();
            }

            // Orb / profile / Eye stay on the closed band even when the glass grows
            // for the on-hand list.
            var barH = (_menuGlassClosedY1 - MenuGlassY0) * hudH;
            glassW = glass.rect.width > 16f ? glass.rect.width : (MenuGlassX1 - MenuGlassX0) * hudW;
            var midY = barH * 0.5f;
            var orbY = MenuGlassY0 * hudH + midY - orbPx * 0.5f;

            _deckSwitchRt.anchorMin = new Vector2(0.5f, 0f);
            _deckSwitchRt.anchorMax = new Vector2(0.5f, 0f);
            _deckSwitchRt.pivot = new Vector2(0.5f, 0.5f);
            _deckSwitchRt.sizeDelta = new Vector2(deckPx, deckPx);
            _deckSwitchRt.anchoredPosition = new Vector2(0f, midY);

            if (_profileRt != null)
            {
                _profileRt.anchorMin = Vector2.zero;
                _profileRt.anchorMax = Vector2.zero;
                _profileRt.pivot = Vector2.zero;
                _profileRt.anchoredPosition = new Vector2(
                    MenuGlassX0 * hudW + insetX, orbY);
                _profileRt.sizeDelta = new Vector2(orbPx, orbPx);
            }

            if (_eyeRt == null) return;
            _eyeRt.anchorMin = Vector2.zero;
            _eyeRt.anchorMax = Vector2.zero;
            _eyeRt.pivot = new Vector2(0.5f, 0.5f);
            _eyeRt.sizeDelta = new Vector2(orbPx, orbPx);
            _eyeRt.anchoredPosition = new Vector2(
                MenuGlassX0 * hudW + glassW - insetX - orbPx * 0.5f,
                MenuGlassY0 * hudH + midY);
        }

        void ToggleDeckSwitchMenu(Transform overlayRoot)
        {
            if (_deckSwitchPop != null)
            {
                CloseDeckSwitchMenu();
                return;
            }

            var acc = AppSession.Ensure()?.Account;
            acc?.EnsureInventory();
            var inv = acc?.inventory;
            inv?.EnsureDeckBoxSlots();
            var hands = inv != null ? inv.OnHandDeckIndices() : new List<int>();

            var hud = overlayRoot as RectTransform;
            if (hud == null) return;
            Canvas.ForceUpdateCanvases();
            _deckSwitchPop = OnHandDeckSwitchMenu.Build(hud, inv, SelectMainDeck);
            PlaceDeckSwitchPop(hud, inv, hands.Count);
            BuildDeckSwitchCatch(overlayRoot);
            PlaceMenuGlass(_menuOpen);
            RelayoutDeckChipAndEye();
            if (_deckSwitchPop != null)
            {
                var glassIx = _menuGlass != null ? _menuGlass.transform.GetSiblingIndex() : 0;
                _deckSwitchPop.transform.SetSiblingIndex(glassIx + 1);
                MenuMotion.Play(this, MenuMotion.PopIn(_deckSwitchPop, MenuMotion.Pop));
            }
        }

        void PlaceDeckSwitchPop(RectTransform hud, PlayerInventory inv, int handCount)
        {
            if (_deckSwitchPop == null || hud == null) return;
            var hudW = hud.rect.width > 16f ? hud.rect.width : 1080f;
            var hudH = hud.rect.height > 16f ? hud.rect.height : 1920f;
            var rt = _deckSwitchPop.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

            var n = Mathf.Max(1, handCount);
            var w = rt.rect.width;
            var h = rt.rect.height;
            if (w < 8f) w = OnHandDeckSwitchMenu.PreferredWidth(hudW, inv);
            if (h < 8f) h = OnHandDeckSwitchMenu.ContentHeight(n);
            w = Mathf.Min(w, hudW * 0.96f);

            var glass = _menuGlass != null ? _menuGlass.rectTransform : null;
            var glassW = glass != null && glass.rect.width > 16f
                ? glass.rect.width
                : (MenuGlassX1 - MenuGlassX0) * hudW;
            var cx = MenuGlassX0 * hudW + glassW * 0.5f;
            var half = w * 0.5f;
            if (cx + half > hudW - 12f) cx = hudW - 12f - half;
            if (cx - half < 12f) cx = 12f + half;

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(cx, _menuGlassClosedY1 * hudH + 8f);
        }

        void BuildDeckSwitchCatch(Transform overlayRoot)
        {
            if (_deckSwitchCatch != null)
            {
                Destroy(_deckSwitchCatch);
                _deckSwitchCatch = null;
            }

            var go = new GameObject("DeckSwitchCatch", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(overlayRoot, false);
            GoTheme.Stretch(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0f, 0f, 0f, 0.01f);
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(CloseDeckSwitchMenu);
            _deckSwitchCatch = go;
            if (_menuGlass != null)
                go.transform.SetSiblingIndex(_menuGlass.transform.GetSiblingIndex());
        }

        void SelectMainDeck(int boxIndex)
        {
            var acc = AppSession.Ensure()?.Account;
            string err = null;
            if (acc == null || !InventoryService.TrySetActivePlayDeck(acc, boxIndex, out err))
            {
                SetStatus(string.IsNullOrEmpty(err) ? "Could not set main deck." : err);
                CloseDeckSwitchMenu();
                return;
            }

            FreeUiKit.PlayConfirm();
            RefreshDeckSwitchLabel();
            CloseDeckSwitchMenu();
            var nm = acc.inventory.DeckBoxDisplayName(boxIndex);
            SetStatus("Main deck: " + nm);
        }

        void CloseDeckSwitchMenu()
        {
            if (_deckSwitchCatch != null)
            {
                Destroy(_deckSwitchCatch);
                _deckSwitchCatch = null;
            }

            if (_deckSwitchPop != null)
            {
                Destroy(_deckSwitchPop);
                _deckSwitchPop = null;
            }

            PlaceMenuGlass(_menuOpen);
            RelayoutDeckChipAndEye();
        }

        /// <summary>Holo command cards that unfold from the Millennium Eye.</summary>
        void BuildEyeMenu(Transform root, LocalAccountStore.Account account)
        {
            _fanCards.Clear();
            var panel = new GameObject("EyeMenu", typeof(RectTransform), typeof(CanvasGroup));
            panel.transform.SetParent(root, false);
            GoTheme.Stretch(panel.GetComponent<RectTransform>());
            _menuExpandCg = panel.GetComponent<CanvasGroup>();
            _menuExpandCg.alpha = 0f;
            _menuExpandCg.blocksRaycasts = false;
            _menuExpandCg.interactable = false;

            BuildCurrencyStrip(panel.transform, account);

            var boardGo = new GameObject("MenuBoard", typeof(RectTransform));
            boardGo.transform.SetParent(panel.transform, false);
            _menuBoard = boardGo.GetComponent<RectTransform>();
            GoTheme.Stretch(_menuBoard);
            _menuBoard.pivot = new Vector2(0.5f, 0.5f);

            void Card(string name, string caption, Sprite icon, System.Action action, bool gold, int index)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
                go.transform.SetParent(_menuBoard, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(220f, 300f);
                rt.localScale = Vector3.one;

                var face = go.GetComponent<Image>();
                var spr = gold
                    ? (ImagineAssets.MenuHoloCardGold() ?? ImagineAssets.MenuHoloCard())
                    : (ImagineAssets.MenuHoloCard() ?? ImagineAssets.TileHub());
                face.sprite = spr ?? UiFoundation.WhiteSprite();
                face.color = Color.white;
                face.raycastTarget = true;
                face.preserveAspect = false;
                if (face.sprite != null && face.sprite.border.sqrMagnitude > 0)
                    face.type = Image.Type.Sliced;

                if (icon != null)
                {
                    var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                    ico.transform.SetParent(go.transform, false);
                    GoTheme.Place(ico.GetComponent<RectTransform>(), 0.10f, 0.28f, 0.90f, 0.90f);
                    var iimg = ico.GetComponent<Image>();
                    iimg.sprite = icon;
                    iimg.preserveAspect = true;
                    iimg.raycastTarget = false;
                    iimg.color = Color.white;
                }

                var t = GoTheme.Label(go.transform, "Title", caption, 20,
                    gold ? DuelystUi.GoldHot : Color.white,
                    TextAnchor.MiddleCenter, bold: true);
                WrldzType.StyleButtonLabel(t, 18, display: false);
                t.color = gold ? DuelystUi.GoldHot : Color.white;
                t.alignment = TextAnchor.MiddleCenter;
                t.resizeTextForBestFit = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                GoTheme.Place(t.rectTransform, 0.04f, 0.02f, 0.96f, 0.28f);

                var cg = go.GetComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;

                var btn = go.GetComponent<Button>();
                btn.targetGraphic = face;
                var block = ColorBlock.defaultColorBlock;
                block.highlightedColor = new Color(1.14f, 1.12f, 0.94f, 1f);
                block.pressedColor = new Color(0.70f, 0.74f, 0.80f, 1f);
                block.fadeDuration = 0.08f;
                btn.colors = block;
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    SetMenuOpen(false);
                    action?.Invoke();
                });

                var pulse = MenuHoloPulse.Attach(go, scan: true, breathe: true, phase: index * 0.33f);
                pulse.enabled = false;

                _fanCards.Add(new FanCard
                {
                    rt = rt,
                    cg = cg,
                    restPos = Vector2.zero,
                    restSize = rt.sizeDelta,
                    restRot = 0f,
                    pulse = pulse
                });
            }

            var i = 0;
            Card("Nav_VsAi", NavCopy.VsAi, ImagineAssets.IconDuel(), OpenCreateArDuelMenu, true, i++);
            Card("Nav_VsPvp", NavCopy.VsPvp, ImagineAssets.IconVsPvp(), OpenPlayerVsPlayerMenu, false, i++);
            Card("Nav_Tourney", NavCopy.TitleFor(MenuId.Tournament),
                ImagineAssets.IconTournament() ?? ImagineAssets.PinTournament() ?? ImagineAssets.IconDuel(),
                () => OpenSystemsMenu(MenuId.Tournament), false, i++);
            Card("Nav_Practice", "PRACTICE", ImagineAssets.IconPractice(), () =>
            {
                var c = ArDuelMatchConfig.Practice();
                c.Opponent = ArDuelOpponentKind.AiLocal;
                c.FormatTitle = "Practice";
                AppSession.Ensure().StartArDuel(c);
            }, false, i++);
            Card("Nav_Deck", NavCopy.DeckShort, ImagineAssets.IconDeck() ?? DuelystUi.IconDeck(),
                () => OpenSystemsMenu(MenuId.DeckCollection), false, i++);
            Card("Nav_Bag", NavCopy.BagShort, ImagineAssets.IconBag() ?? DuelystUi.IconBag(),
                () => OpenSystemsMenu(MenuId.Inventory), false, i++);
            Card("Nav_Story", NavCopy.TitleFor(MenuId.StorySeason), ImagineAssets.IconStory() ?? DuelystUi.IconStory(),
                () => OpenSystemsMenu(MenuId.StorySeason), false, i++);
            Card("Nav_Bazaar", NavCopy.TitleFor(MenuId.Bazaar), ImagineAssets.IconBazaar(),
                () => OpenSystemsMenu(MenuId.Bazaar), false, i++);
            Card("Nav_Tome", NavCopy.TitleFor(MenuId.TomeRaid), ImagineAssets.IconTome(),
                () => OpenSystemsMenu(MenuId.TomeRaid), false, i++);
            Card("Nav_Set", "SET", ImagineAssets.IconSettings() ?? DuelystUi.IconSettings(),
                OpenSettingsOverlay, false, i++);

            panel.SetActive(false);
            _menuExpand = panel;
            _menuOpen = false;
        }

        System.Collections.IEnumerator RelayoutMenuBoardDelayed()
        {
            yield return null;
            RelayoutMenuBoard();
            yield return new WaitForEndOfFrame();
            RelayoutMenuBoard();
        }

        void RelayoutMenuBoard()
        {
            if (_menuBoard == null || _fanCards.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            var r = _menuBoard.rect;
            if (r.width < 32f || r.height < 32f) return;

            const int cols = 3;
            var rows = Mathf.Max(3, Mathf.CeilToInt(_fanCards.Count / (float)cols));
            const float gap = 18f;
            const float aspect = 0.78f;
            // Sit below the stacked wallet (~156px from the top of the sheet).
            var areaTop = r.height * 0.08f;
            var areaBot = r.height * -0.40f;
            var areaH = areaTop - areaBot;
            var areaW = r.width * 0.94f;

            var cellW = (areaW - gap * (cols - 1)) / cols;
            var cellH = (areaH - gap * (rows - 1)) / rows;
            if (cellW / cellH > aspect)
                cellW = cellH * aspect;
            else
                cellH = cellW / aspect;
            cellW = Mathf.Max(cellW, Mathf.Min(168f, areaW / cols));
            cellH = Mathf.Max(cellH, Mathf.Min(220f, areaH / rows));

            var totalW = cols * cellW + (cols - 1) * gap;
            var totalH = rows * cellH + (rows - 1) * gap;
            var originX = -totalW * 0.5f + cellW * 0.5f;
            var originY = areaTop - cellH * 0.5f;

            for (var i = 0; i < _fanCards.Count; i++)
            {
                var col = i % cols;
                var row = i / cols;
                var card = _fanCards[i];
                card.restSize = new Vector2(cellW, cellH);
                card.restPos = new Vector2(
                    originX + col * (cellW + gap),
                    originY - row * (cellH + gap));
                card.restRot = (col - 1) * 3.5f;
                _fanCards[i] = card;
                if (card.rt == null) continue;
                card.rt.anchorMin = new Vector2(0.5f, 0.5f);
                card.rt.anchorMax = new Vector2(0.5f, 0.5f);
                card.rt.pivot = new Vector2(0.5f, 0.5f);
                if (_menuOpen)
                {
                    card.rt.sizeDelta = card.restSize;
                    card.rt.anchoredPosition = card.restPos;
                    card.rt.localRotation = Quaternion.Euler(0f, 0f, card.restRot);
                    card.rt.localScale = Vector3.one;
                }
            }
        }

        void ToggleMainMenu(Transform root)
        {
            FreeUiKit.PlayMillenniumEye();
            SetMenuOpen(!_menuOpen);
        }

        void SetMenuOpen(bool open)
        {
            if (open == _menuOpen && (open || _menuExpand == null || !_menuExpand.activeSelf))
            {
                if (!open) PlaceMenuGlass(false);
                return;
            }

            if (_menuMotion != null)
            {
                StopCoroutine(_menuMotion);
                _menuMotion = null;
            }

            if (_eyeFlashCo != null)
            {
                StopCoroutine(_eyeFlashCo);
                _eyeFlashCo = null;
            }

            if (!open)
                HideEyeFlash();

            _menuOpen = open;
            if (open)
            {
                CloseDeckSwitchMenu();
                if (_compassOpen) SetCompassOpen(false);
            }

            _menuMotion = StartCoroutine(open ? OpenEyeMenuCo() : CloseEyeMenuCo());
        }

        IEnumerator OpenEyeMenuCo()
        {
            ApplyMenuGlassSprite(true);
            if (_menuVeil != null)
                _menuVeil.SetActive(true);

            if (_menuExpand != null)
            {
                _menuExpand.SetActive(true);
                _menuExpand.transform.SetAsLastSibling();
                if (_menuExpandCg == null)
                    _menuExpandCg = MenuMotion.EnsureGroup(_menuExpand);
                _menuExpandCg.alpha = 1f;
                _menuExpandCg.blocksRaycasts = true;
                _menuExpandCg.interactable = true;
            }

            SetEyeOpened(true);
            if (_eyeRt != null)
            {
                _eyeRt.SetAsLastSibling();
                StartCoroutine(MenuMotion.PunchScale(_eyeRt, 1.16f, 0.22f));
            }

            if (_profileRt != null)
                _profileRt.SetAsLastSibling();

            yield return null;
            Canvas.ForceUpdateCanvases();
            RelayoutMenuBoard();

            if (_menuVeilCg != null)
                StartCoroutine(MenuMotion.Fade(_menuVeilCg, _menuVeilCg.alpha, 0.46f, MenuMotion.Snap));
            if (_currencyCg != null)
                StartCoroutine(MenuMotion.Fade(_currencyCg, 0f, 1f, MenuMotion.Sheet));

            var glassFrom = _menuGlassClosedY1;
            if (_menuGlass != null)
                StartCoroutine(MenuMotion.LerpAnchorY1(_menuGlass.rectTransform,
                    MenuGlassX0, MenuGlassY0, MenuGlassX1, glassFrom, MenuGlassOpenY1, MenuMotion.Glass));

            _eyeFlashCo = StartCoroutine(EyeOpenFlashCo());
            yield return FanCardsCo(opening: true);

            for (var i = 0; i < _fanCards.Count; i++)
            {
                var p = _fanCards[i].pulse;
                if (p == null) continue;
                p.CaptureBaseScale();
                p.enabled = true;
            }

            _menuMotion = null;
        }

        IEnumerator CloseEyeMenuCo()
        {
            for (var i = 0; i < _fanCards.Count; i++)
            {
                var p = _fanCards[i].pulse;
                if (p != null) p.enabled = false;
            }

            if (_currencyCg != null)
                StartCoroutine(MenuMotion.Fade(_currencyCg, _currencyCg.alpha, 0f, MenuMotion.Snap));

            yield return FanCardsCo(opening: false);

            if (_menuGlass != null)
                StartCoroutine(MenuMotion.LerpAnchorY1(_menuGlass.rectTransform,
                    MenuGlassX0, MenuGlassY0, MenuGlassX1, MenuGlassOpenY1, _menuGlassClosedY1, MenuMotion.Snap));

            SetEyeOpened(false);

            if (_menuExpandCg != null)
            {
                _menuExpandCg.alpha = 0f;
                _menuExpandCg.blocksRaycasts = false;
            }

            if (_menuExpand != null)
                _menuExpand.SetActive(false);

            if (_menuVeilCg != null && _menuVeil != null && _menuVeil.activeSelf)
                yield return MenuMotion.Fade(_menuVeilCg, _menuVeilCg.alpha, 0f, MenuMotion.Snap);
            if (_menuVeil != null)
                _menuVeil.SetActive(false);

            PlaceMenuGlass(false);

            if (_eyeRt != null)
                _eyeRt.SetAsLastSibling();
            if (_profileRt != null)
                _profileRt.SetAsLastSibling();

            _menuMotion = null;
        }

        void SetEyeOpened(bool open)
        {
            if (_mainMenuImg == null) return;
            // Keep the gold idle Eye — the open sprite has a baked red pupil glow.
            _mainMenuImg.sprite = _eyeIdleSpr ?? _mainMenuImg.sprite;
            _mainMenuImg.color = Color.white;
        }

        IEnumerator EyeOpenFlashCo()
        {
            if (_eyeFlash == null || _eyeFlashRt == null) yield break;
            PlaceEyeFlashAtEye();
            _eyeFlash.gameObject.SetActive(true);

            var hud = _eyeFlashRt.parent as RectTransform;
            var cover = 160f;
            if (hud != null)
            {
                var r = hud.rect;
                cover = Mathf.Max(r.width, r.height) * 2.4f;
            }

            // Brightness ramps up almost immediately, then the wash clears so the menu is readable.
            const float up = 0.11f;
            const float down = 0.22f;
            var t = 0f;
            var peak = new Color(1f, 0.97f, 0.88f, 0.92f);
            while (t < up + down)
            {
                t += Time.unscaledDeltaTime;
                PlaceEyeFlashAtEye();
                var u = Mathf.Clamp01(t / up);
                var grow = MenuMotion.OutCubic(u);
                var size = Mathf.Lerp(96f, cover, grow);
                _eyeFlashRt.sizeDelta = new Vector2(size, size);
                float a;
                if (t <= up)
                    a = Mathf.Lerp(0.08f, peak.a, MenuMotion.OutCubic(t / up));
                else
                    a = Mathf.Lerp(peak.a, 0f, MenuMotion.InCubic((t - up) / down));
                _eyeFlash.color = new Color(peak.r, peak.g, peak.b, a);
                yield return null;
            }

            HideEyeFlash();
            _eyeFlashCo = null;
        }

        void PlaceEyeFlashAtEye()
        {
            if (_eyeFlashRt == null || _eyeRt == null) return;
            var parent = _eyeFlashRt.parent as RectTransform;
            if (parent == null) return;
            var corners = new Vector3[4];
            _eyeRt.GetWorldCorners(corners);
            var world = (corners[0] + corners[2]) * 0.5f;
            _eyeFlashRt.anchoredPosition = parent.InverseTransformPoint(world);
        }

        void HideEyeFlash()
        {
            if (_eyeFlash != null)
                _eyeFlash.color = new Color(1f, 0.97f, 0.88f, 0f);
            if (_eyeFlashRt != null)
                _eyeFlashRt.sizeDelta = new Vector2(160f, 160f);
            if (_eyeFlash != null)
                _eyeFlash.gameObject.SetActive(false);
        }

        IEnumerator FanCardsCo(bool opening)
        {
            var n = _fanCards.Count;
            if (n == 0) yield break;
            RelayoutMenuBoard();
            var origin = EyeFanOriginPixels();
            const float dur = 0.32f;
            const float delay = 0.032f;
            var total = dur + delay * (n - 1);
            var t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                for (var i = 0; i < n; i++)
                {
                    var card = _fanCards[i];
                    if (card.rt == null) continue;
                    var slot = opening ? i : n - 1 - i;
                    var u = Mathf.Clamp01((t - slot * delay) / dur);
                    var k = opening ? MenuMotion.OutCubic(u) : 1f - MenuMotion.InCubic(u);
                    card.rt.anchoredPosition = Vector2.LerpUnclamped(origin, card.restPos, k);
                    card.rt.sizeDelta = Vector2.Lerp(new Vector2(64f, 64f), card.restSize, k);
                    var spin = Mathf.Lerp((i - 4) * 16f, card.restRot, k);
                    card.rt.localRotation = Quaternion.Euler(0f, 0f, spin);
                    var s = Mathf.Lerp(0.28f, 1f, MenuMotion.OutBack(opening ? u : 1f - u));
                    card.rt.localScale = new Vector3(s, s, 1f);
                    if (card.cg != null)
                    {
                        card.cg.alpha = opening ? MenuMotion.OutCubic(u) : 1f - MenuMotion.OutCubic(u);
                        card.cg.blocksRaycasts = opening && u > 0.55f;
                        card.cg.interactable = card.cg.blocksRaycasts;
                    }
                }

                yield return null;
            }

            for (var i = 0; i < n; i++)
            {
                var card = _fanCards[i];
                if (card.rt == null) continue;
                if (opening)
                {
                    card.rt.anchoredPosition = card.restPos;
                    card.rt.sizeDelta = card.restSize;
                    card.rt.localRotation = Quaternion.Euler(0f, 0f, card.restRot);
                    card.rt.localScale = Vector3.one;
                    if (card.cg != null)
                    {
                        card.cg.alpha = 1f;
                        card.cg.blocksRaycasts = true;
                        card.cg.interactable = true;
                    }
                }
                else
                {
                    card.rt.anchoredPosition = origin;
                    card.rt.localScale = new Vector3(0.28f, 0.28f, 1f);
                    if (card.cg != null)
                    {
                        card.cg.alpha = 0f;
                        card.cg.blocksRaycasts = false;
                        card.cg.interactable = false;
                    }
                }
            }
        }

        Vector2 EyeFanOriginPixels()
        {
            if (_eyeRt == null || _menuBoard == null) return Vector2.zero;
            var corners = new Vector3[4];
            _eyeRt.GetWorldCorners(corners);
            var world = (corners[0] + corners[2]) * 0.5f;
            return _menuBoard.InverseTransformPoint(world);
        }

        void AnimateEyeGlow()
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(_pulseT * 2.1f);
            if (_eyeWhiteGlow != null)
            {
                var a = _menuOpen ? 0.12f + 0.06f * pulse : 0.34f + 0.22f * pulse;
                _eyeWhiteGlow.color = new Color(1f, 1f, 1f, a);
                var s = 1.08f * (_menuOpen ? 1.02f + 0.03f * pulse : 1.02f + 0.04f * pulse);
                _eyeWhiteGlow.rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }

        static void StyleBarButton(GameObject go, string caption, Sprite spr, UnityEngine.Events.UnityAction onClick)
        {
            var img = go.GetComponent<Image>();
            img.sprite = spr ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
            var label = GoTheme.Label(go.transform, "Cap", caption, 13, DuelystUi.TextCream);
            GoTheme.Stretch(label.rectTransform);
            go.GetComponent<Button>().targetGraphic = img;
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        void BuildSystemsSheet(Transform root)
        {
            if (_systemsSheet != null) return;
            var sheet = new GameObject("SystemsSheet", typeof(RectTransform), typeof(Image));
            sheet.transform.SetParent(root, false);
            // Glass compact panel — map stays visible (MenuChromePrefs)
            GoTheme.Place(sheet.GetComponent<RectTransform>(), 0.10f, 0.20f, 0.90f, 0.78f);
            var bg = sheet.GetComponent<Image>();
            var sysSpr = ImagineAssets.MenuHoloSheet() ?? ImagineAssets.PanelMenuGlass() ?? ImagineAssets.PanelHolo();
            bg.sprite = sysSpr ?? UiFoundation.WhiteSprite();
            bg.type = sysSpr != null && sysSpr.border.sqrMagnitude > 0
                ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = Color.white;
            bg.raycastTarget = true;

            var title = GoTheme.Label(sheet.transform, "T", "MENU", 16, DuelystUi.GoldHot);
            GoTheme.Place(title.rectTransform, 0.05f, 0.91f, 0.70f, 0.98f);

            var sub = GoTheme.Label(sheet.transform, "S",
                "Map stays open · pick a row · CLOSE returns here",
                11, DuelystUi.Cyan, TextAnchor.MiddleLeft, bold: false);
            GoTheme.Place(sub.rectTransform, 0.05f, 0.84f, 0.95f, 0.90f);

            float y = 0.76f;
            void Row(MenuId id, bool gold = false)
            {
                var go = new GameObject("Row_" + id, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(sheet.transform, false);
                GoTheme.Place(go.GetComponent<RectTransform>(), 0.06f, y - 0.075f, 0.94f, y);
                var img = go.GetComponent<Image>();
                img.sprite = UiFoundation.WhiteSprite();
                img.color = gold ? MenuChromePrefs.RowGoldColor : MenuChromePrefs.RowColor;
                img.raycastTarget = true;
                var label = NavCopy.TitleFor(id) + "  ·  " + NavCopy.BlurbFor(id) + "  ›";
                var t = GoTheme.Label(go.transform, "L", label, 12, DuelystUi.TextCream,
                    TextAnchor.MiddleLeft);
                GoTheme.Place(t.rectTransform, 0.04f, 0.1f, 0.96f, 0.9f);
                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    HideSystemsSheet();
                    OpenSystemsMenu(id);
                });
                y -= 0.082f;
            }

            Row(MenuId.DeckCollection, gold: true);
            Row(MenuId.Inventory);
            Row(MenuId.TomeRaid);
            Row(MenuId.Bazaar);
            Row(MenuId.StorySeason);
            Row(MenuId.AvatarProfile);
            Row(MenuId.Settings);

            // Full hub scene (optional deep hub)
            var hub = new GameObject("Hub", typeof(RectTransform), typeof(Image), typeof(Button));
            hub.transform.SetParent(sheet.transform, false);
            GoTheme.Place(hub.GetComponent<RectTransform>(), 0.10f, 0.04f, 0.48f, 0.11f);
            StyleBarButton(hub, "FULL HUB", DuelystUi.BtnSecondary(), () =>
            {
                FreeUiKit.PlayConfirm();
                HideSystemsSheet();
                AppSession.Ensure().GoMainMenu();
            });

            var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(sheet.transform, false);
            GoTheme.Place(close.GetComponent<RectTransform>(), 0.52f, 0.04f, 0.90f, 0.11f);
            StyleBarButton(close, "× CLOSE", DuelystUi.BtnPrimary(), () =>
            {
                FreeUiKit.PlayClick();
                HideSystemsSheet();
            });

            sheet.SetActive(false);
            _systemsSheet = sheet;
        }

        void ToggleSystemsSheet(Transform root)
        {
            if (_systemsSheet == null) BuildSystemsSheet(root);
            if (_systemsSheet == null) return;
            var on = !_systemsSheet.activeSelf;
            if (on)
            {
                _systemsSheet.transform.SetAsLastSibling();
                SetStatus("Menu");
                MenuMotion.Play(this, MenuMotion.SheetIn(_systemsSheet));
            }
            else
                MenuMotion.Play(this, MenuMotion.SheetOut(_systemsSheet));
        }

        void HideSystemsSheet()
        {
            if (_systemsSheet != null && _systemsSheet.activeSelf)
                MenuMotion.Play(this, MenuMotion.SheetOut(_systemsSheet));
            SetMenuOpen(false);
        }

        void OpenArtifactsOverlay(string focusDefId)
        {
            ScreenRouter.Ensure().PendingArtifactFocus = focusDefId;
            OpenSystemsMenu(MenuId.Artifacts);
        }

        /// <summary>Open collection / deck / economy screens via MenuShell overlay (stay on overworld).</summary>
        void OpenSystemsMenu(MenuId id)
        {
            CloseDeckSwitchMenu();
            SetMenuOpen(false);
            DualMenuPresenter.HideTransientMenus();
            DestroyNamed("PlayerVsAiCreateCanvas");
            DestroyNamed("PvpCreateCanvas");
            DestroyNamed("ZoneModePromptCanvas");
            if (id == MenuId.AvatarProfile)
            {
                OpenAvatarMenus();
                return;
            }

            if (id == MenuId.Settings)
            {
                OpenSettingsOverlay();
                return;
            }

            ScreenRouter.Ensure().Presentation = UiPresentation.NonArPortrait;
            var shell = EnsureMenuShell();
            ScreenRouter.Ensure(shell);
            shell.ShowOverlay(id);
            SetStatus(NavCopy.ToastFor(id));
        }

        MenuShell EnsureMenuShell()
        {
            if (_menuShell != null) return _menuShell;
            // Prefer an overlay-only shell (no second bottom nav on the map)
            var existing = FindObjectsByType<MenuShell>(FindObjectsSortMode.None);
            foreach (var s in existing)
            {
                if (s != null && s.OverlayOnly)
                {
                    _menuShell = s;
                    return _menuShell;
                }
            }

            _menuShell = MenuShell.CreateOverlay(85);
            return _menuShell;
        }

        void OpenSettingsOverlay()
        {
            FreeUiKit.PlaySelect();
            var shell = EnsureMenuShell();
            shell.ShowOverlay(MenuId.Settings);
            SetStatus(NavCopy.ToastFor(MenuId.Settings));
        }

        void BuildGoStatusToast(Transform root)
        {
            // Slim toast above bottom dock (MENU)
            var toast = GoTheme.WhiteChip(root, "Toast", 0.18f, 0.50f, 0.82f, 0.545f);
            toast.GetComponent<Image>().sprite = GoTheme.ToastPlate() ?? GoTheme.NearbyPlate();
            toast.GetComponent<Image>().type = Image.Type.Sliced;
            toast.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.88f);
            _status = GoTheme.Label(toast, "S", "", 13, GoTheme.Ink,
                TextAnchor.MiddleCenter, bold: false);
            GoTheme.Place(_status.rectTransform, 0.04f, 0.1f, 0.96f, 0.9f);
            toast.gameObject.SetActive(false);
            _statusToast = toast.gameObject;
        }

        void MakePin(MapPin pin)
        {
            var zk = MapZoneCatalog.Parse(pin.kind);
            // Compact icon pin — no floating labels (tap identifies)
            var half = MapZoneCatalog.PinHalfHeight(zk) * 1.05f;

            var go = new GameObject("Pin_" + pin.id, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_mapContent, false);
            var rt = go.GetComponent<RectTransform>();
            var w = half * 0.85f;
            rt.anchorMin = new Vector2(pin.x - w, pin.y - half * 0.15f);
            rt.anchorMax = new Vector2(pin.x + w, pin.y + half * 1.05f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var hit = go.GetComponent<Image>();
            hit.sprite = UiFoundation.WhiteSprite();
            hit.color = new Color(1f, 1f, 1f, 0.01f);
            hit.raycastTarget = true;

            // Soft ground blob only — unique silhouettes must not sit in a circle.
            var discGo = new GameObject("Disc", typeof(RectTransform), typeof(Image));
            discGo.transform.SetParent(go.transform, false);
            GoTheme.Place(discGo.GetComponent<RectTransform>(), 0.18f, -0.06f, 0.82f, 0.18f);
            var disc = discGo.GetComponent<Image>();
            disc.sprite = UiFoundation.WhiteSprite();
            disc.color = new Color(0f, 0f, 0f, 0.38f);
            disc.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            GoTheme.Place(iconGo.GetComponent<RectTransform>(), 0.00f, 0.08f, 1f, 1f);
            var img = iconGo.GetComponent<Image>();
            img.sprite = SpriteForKind(pin.kind) ?? FreeUiKit.Star() ?? UiFoundation.WhiteSprite();
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;

            var captured = pin;
            go.GetComponent<Button>().onClick.AddListener(() => OnPin(captured));
            _pinViews.Add(new PinView
            {
                pin = pin,
                pinImage = img,
                disc = disc,
                kind = zk
            });
        }

        RectTransform MakeAvatar(LocalAccountStore.Account account)
        {
            var go = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = Color.clear;
            img.raycastTarget = false;

            var shadow = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            shadow.transform.SetParent(go.transform, false);
            GoTheme.Place(shadow.GetComponent<RectTransform>(), 0.18f, -0.04f, 0.82f, 0.10f);
            var sImg = shadow.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0f, 0f, 0f, 0.32f);
            sImg.raycastTarget = false;

            _mapPortrait = AvatarPortraitView.CreateFullBodyFill(go.transform, hideBackground: true, badgeCrop: false);
            _mapPortrait.Apply(account != null ? account.GetAvatarOrDefault() : AvatarAppearance.Default());

            return go.GetComponent<RectTransform>();
        }

        void PlaceAvatarFixed()
        {
            if (_avatar == null) return;
            const float halfW = 0.055f;
            _avatar.anchorMin = new Vector2(AvatarScreen.x - halfW, AvatarScreen.y - 0.02f);
            _avatar.anchorMax = new Vector2(AvatarScreen.x + halfW, AvatarScreen.y + 0.26f);
            _avatar.offsetMin = Vector2.zero;
            _avatar.offsetMax = Vector2.zero;
            _avatar.SetAsLastSibling();
        }

        void OpenAvatarMenus()
        {
            var ui = AvatarCustomizerUI.Ensure(transform);
            ui.OpenProfile(OnAvatarSaved);
        }

        void OnAvatarSaved()
        {
            AppSession.Ensure().RefreshFromStore();
            var acc = AppSession.Ensure().Account;
            if (acc == null) return;
            var look = acc.GetAvatarOrDefault();
            _mapPortrait?.Apply(look);
            _hudPortrait?.Apply(look);
            SetStatus(look.title);
        }

        /// <summary>Pin id last toasted as "in range" — avoid per-meter spam.</summary>
        string _lastProximityId;

        void CheckProximityGps()
        {
            // No auto text spam — player learns pin names by selecting them.
            if (_env == null) return;
            string nearestId = null;
            var nearestDist = float.MaxValue;
            foreach (var pv in _pinViews)
            {
                var zk = pv.kind;
                var distM = _env.DistanceMetersToMapPin(pv.pin.x, pv.pin.y, MetersPerMapWidth);
                if (distM > InteractRadius(zk)) continue;
                if (distM >= nearestDist) continue;
                nearestDist = distM;
                nearestId = pv.pin.id;
            }

            if (nearestId == null)
                _lastProximityId = null;
            else
                _lastProximityId = nearestId;
        }

        void OnPin(MapPin pin)
        {
            FreeUiKit.PlaySelect();
            InteractWithPin(pin);
        }

        /// <summary>Identify pin on select; act only when in range.</summary>
        void InteractWithPin(MapPin pin)
        {
            var dist = _env != null
                ? _env.DistanceMetersToMapPin(pin.x, pin.y, MetersPerMapWidth)
                : pin.distanceM;
            pin.distanceM = dist;
            var zk = MapZoneCatalog.Parse(pin.kind);
            // Always name the icon on select (replaces permanent map labels)
            DescribePin(pin, zk, dist);

            if (dist > InteractRadius(zk))
                return;

            switch (MapZoneCatalog.Action(zk))
            {
                case MapZoneAction.ZoneModeDuel:
                    ShowArZonePrompt(pin, zk);
                    break;
                case MapZoneAction.TrainingDuel:
                    ShowArZonePrompt(pin, zk);
                    break;
                case MapZoneAction.OpenBazaar:
                    OpenSystemsMenu(MenuId.Bazaar);
                    break;
                case MapZoneAction.OpenRaid:
                    ShowArZonePrompt(pin, zk);
                    break;
                case MapZoneAction.OpenStory:
                    ShowArZonePrompt(pin, zk);
                    break;
                case MapZoneAction.OpenPvp:
                    ShowArZonePrompt(pin, zk);
                    break;
                case MapZoneAction.NpcPossessDuel:
                    ShowArZonePrompt(pin, zk);
                    break;
                case MapZoneAction.LootGrant:
                    if (MapZoneService.TryLootGrant(pin.id, zk, out _, out var toast))
                        RefreshCurrencyStrip();
                    if (!string.IsNullOrEmpty(toast))
                        SetStatus($"{PinIdentity(pin, zk)} · {toast}");
                    break;
                case MapZoneAction.Toast:
                    SetStatus(MapZoneCatalog.PurposeTitle(zk));
                    break;
                default:
                    break;
            }
        }

        static string PinIdentity(MapPin pin, MapZoneKind zk) =>
            MapZoneCatalog.Label(zk);

        void DescribePin(MapPin pin, MapZoneKind zk, float distM)
        {
            var id = PinIdentity(pin, zk);
            if (distM < 1000f)
                SetStatus($"{id} · {distM:0}m");
            else
                SetStatus($"{id} · {distM / 1000f:0.0}km");
        }

        /// <summary>Zone Mode gate — never auto-force AR (PRODUCT_VISION).</summary>
        void ShowArZonePrompt(MapPin pin, MapZoneKind kind = MapZoneKind.Tear)
        {
            var acc = AppSession.Ensure()?.Account;
            acc?.EnsureProgress();
            var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var p = acc?.progress;
            var streak = p != null && p.tearStreakZoneId == pin.id ? p.tearStreak8000 : 0;
            var bossReady = p != null && p.TearBossReady(pin.id);
            ZoneModePrompt.Open(
                transform,
                pin.id,
                MapZoneCatalog.Label(kind),
                MapZoneCatalog.PurposeTitle(kind),
                () => SetStatus(""),
                kind,
                startingLp: kind == MapZoneKind.Raid ? MapZoneCatalog.RaidBossLp : 0,
                street8000Locked: false,
                bossReady: kind == MapZoneKind.Tear && bossReady,
                bossLp: kind == MapZoneKind.Tear ? MapZoneCatalog.TearBossLpFor(pin.id) : 0,
                streak: streak);
        }

        void OnStreetNpc(OverworldNpcField.Agent npc)
        {
            FreeUiKit.PlaySelect();
            var dist = npc.distanceM;
            if (_env != null)
                dist = _env.DistanceMetersToMapPin(npc.x, npc.y, MetersPerMapWidth);
            var lp = npc.band == StreetLpBand.Street8000
                ? MapZoneCatalog.Street8000Lp
                : MapZoneCatalog.Street4000Lp;
            SetStatus($"{npc.displayName} · {lp} LP · {dist:0}m");
            if (dist > MapZoneCatalog.StreetInteractM)
                return;

            var acc = AppSession.Ensure()?.Account;
            acc?.EnsureProgress();
            var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var locked = npc.band == StreetLpBand.Street8000
                         && (acc?.progress == null || !acc.progress.HasStreet8000Access(now));
            var flavor = locked
                ? "Win one 4000 LP street duel in the last 24 hours to challenge 8000 LP wanderers."
                : "They look human until a Tear opens behind them and a spirit takes the body. No disk until then.";
            ZoneModePrompt.Open(
                transform,
                npc.tearId,
                npc.displayName,
                flavor,
                () => SetStatus(""),
                MapZoneKind.Npc,
                startingLp: lp,
                street8000Locked: locked);
        }

        /// <summary>Primary overworld entry: Player vs AI → distance → AR duel.</summary>
        static void DestroyNamed(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) DestroyImmediate(go);
        }

        void OpenCreateArDuelMenu() => OpenSystemsMenu(MenuId.ArDuelCreate);

        /// <summary>Player vs Player: auto-scan distance between duelists → AR arena.</summary>
        void OpenPlayerVsPlayerMenu() => OpenSystemsMenu(MenuId.ArDuelPvpCreate);

        void SetStatus(string msg)
        {
            if (_status != null) _status.text = msg ?? "";
            if (_statusToast != null)
            {
                var show = !string.IsNullOrEmpty(msg);
                _statusToast.SetActive(show);
                _statusHideAt = show ? Time.unscaledTime + 2.4f : 0f;
            }

            if (!string.IsNullOrEmpty(msg))
                Debug.Log("[WRLDZ] " + msg);
        }

        void TickStatusToast()
        {
            if (_statusToast == null || !_statusToast.activeSelf) return;
            if (_statusHideAt > 0f && Time.unscaledTime >= _statusHideAt)
            {
                _statusToast.SetActive(false);
                _statusHideAt = 0f;
            }
        }
    }
}

