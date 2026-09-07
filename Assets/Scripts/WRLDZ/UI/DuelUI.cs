using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Duel presentation host:
    /// <list type="bullet">
    /// <item><b>AR / Zone Mode AR</b> — full-bleed Solid Vision stage; phone is
    /// <see cref="DuelFloatingHud"/> (LP / phase / status islands) plus
    /// <see cref="ArCompanionPhoneHud"/> (profile + systems). No 2D duel board.</item>
    /// <item><b>Digital</b> — same floating HUD over the stage; hand is the
    /// torso holos (2D tray is fallback only when holos are hidden).</item>
    /// </list>
    /// All moves go through <see cref="DuelCommandService"/> when announced.
    /// Hotseat PvP: both humans — no SimpleAi; <see cref="CommandWho"/> routes input.
    /// </summary>
    public class DuelUI : MonoBehaviour
    {
        DuelEngine _engine;
        CardDatabase _db;
        Action _onRestart;
        TimedResponseClock _responseClock;
        ArDuelSpace _arSpace;
        MrReferobot _referobot;
        CardInspectPopup _inspect;
        GraveyardBrowser _gyBrowser;
        BanishedBrowser _banishedBrowser; // Backup-bot stub — Dilbot wire OpenBanished
        CardInstance _inspectCard;
        bool _inspectFromHand;
        float _inspectSuppressedUntil;
        PreDuelCinematic _preDuel;
        bool _preDuelComplete;

        /// <summary>True when phone is companion-only (AR duel / Zone AR). False = digital 2D board.</summary>
        bool _arCompanionMode;
        ArCompanionPhoneHud.Host _companion;
        DuelFloatingHud _floatHud;

        Text _status;
        Text _hint;
        Text _log;
        Text _field; // compact meta line
        Text _phaseBanner; // large current phase (Konami turn structure)
        DuelReviewLogPanel _reviewPanel;
        DuelReviewLog _reviewLog;
        Text _youLpOrb;
        Text _oppLpOrb;
        /// <summary>Optional LP readout on the disk hub cuff (AR companion).</summary>
        Text _diskHubLp;
        Transform _handRow;
        Transform _playerMonsters;
        Transform _oppMonsters;
        Transform _playerSpells;
        Transform _oppSpells;
        Text _deckCountLabel;
        Text _gyCountLabel;
        Text _extraCountLabel;
        GameObject _overlay;
        Text _overlayTitle;
        Text _overlaySub;
        Image _phasePillMp1, _phasePillBattle, _phasePillMp2, _phasePillEnd;

        // Phase / global only — card actions live on the card itself
        Button _btnBattle;
        Button _btnMain2;
        Button _btnEnd;
        Button _btnShuffleHand;
        Button _btnRestartBar;
        Button _btnMenu;
        Button _btnClearTrib;
        Button _btnCancelTarget;

        Transform _targetRow;
        Text _targetPrompt;
        Text _targetHint;
        GameObject _targetPanel;
        GameObject _targetDim;
        Button _targetCancelBtn;
        readonly List<CardInstance> _attackTargets = new();
        CardInstance _attackPickerAttacker;

        /// <summary>
        /// Response tray: countdown + ACTIVATE for every LegalCards entry + PASS.
        /// Field Sets also blink on-zone; timer expiry still passes.
        /// </summary>
        GameObject _responseTray;
        Transform _responseBtnRow;
        Text _responsePrompt;

        // Anime announce (typed stand-in for voice STT)
        InputField _announceField;
        Text _announceFeedback;
        Text _timerLabel;
        Image _timerFill;

        readonly List<string> _logLines = new();
        const int MaxLog = 55;

        /// <summary>Cached legal plays for yellow/cyan glow (LegalIntentService).</summary>
        LegalIntentService.Snapshot _legalSnap;

        CardInstance _selectedHand;
        CardInstance _selectedField; // your monster for flip/pos/attack
        CardInstance _selectedSpellTrap; // set S/T or continuous
        CardInstance _selectedAttacker;
        /// <summary>True after Summon/Set — waiting for a highlighted zone tap.</summary>
        bool _awaitingZonePick;
        bool _zonePickAsSet;
        int _pendingZoneIndex = -1;
        RulesZoneKind _pendingZoneKind = RulesZoneKind.Monster;
        RectTransform _zonePicker;
        Text _zonePickerHint;
        static Font _font;

        bool _aiRunning;
        Coroutine _aiRoutine;
        /// <summary>Coalesce engine+UI refresh to one rebuild per frame (field lag fix).</summary>
        bool _fieldDirty;
        bool _refreshing;
        int _drawSfxHandCount = -1;

        /// <summary>False for NearbyPeer / LocalPass hotseat — both sides human.</summary>
        bool IsAiOpponent => _engine == null || !_engine.HumanVsHuman;

        /// <summary>
        /// Who the local UI currently commands.
        /// PvAI: always Player. Hotseat PvP: TurnPlayer (or response Responder).
        /// </summary>
        DuelistState CommandWho()
        {
            if (_engine == null) return null;
            if (IsAiOpponent) return _engine.Player;
            if (_engine.IsAwaitingPlayerResponse && _engine.PendingResponse?.Responder != null)
                return _engine.PendingResponse.Responder;
            if (_engine.IsAwaitingEffectTarget && _engine.PendingActivation != null)
            {
                // Flip / Sangan / targeting can fire on opponent's turn — use controller
                return _engine.PendingActivation.Controller
                       ?? _engine.TurnPlayer
                       ?? _engine.Player;
            }
            return _engine.TurnPlayer ?? _engine.Player;
        }

        DuelistState EnemyOf(DuelistState who) =>
            who == null || _engine == null ? null
            : who == _engine.Player ? _engine.Opponent : _engine.Player;

        bool IsMySide(DuelistState who) => who != null && who == CommandWho();

        /// <summary>InstanceId → was face-up last paint (for flip-reveal animation).</summary>
        readonly Dictionary<int, bool> _lastFaceUp = new();
        /// <summary>InstanceIds present on field last paint (new summons vs flips).</summary>
        readonly HashSet<int> _lastFieldIds = new();
        struct CardAction
        {
            public string Label;
            public Color Color;
            public Action Invoke;
        }

        public void Bind(DuelEngine engine, CardDatabase db, Action onRestart = null)
        {
            _engine = engine;
            _db = db;
            _onRestart = onRestart;
            engine.OnLog += AppendLog;
            engine.OnStateChanged += MarkFieldDirty;
            engine.OnGameOver += OnGameOverUi;
            EnsureEventSystem();
            BuildUi();

            // Structured review log (visible panel + JSONL for AI learning)
            EnsureReviewLog();

            _responseClock = gameObject.GetComponent<TimedResponseClock>()
                             ?? gameObject.AddComponent<TimedResponseClock>();
            // Duration comes from combat anim impact — not a fixed menu pause
            _responseClock.Bind(engine);
            _responseClock.OnTick += OnResponseTick;
            _responseClock.OnStarted += () =>
            {
                MarkFieldDirty();
                // Immediate tray so Waboku / Mirror Force are tappable before first full refresh
                RebuildResponseTray();
            };
            _responseClock.OnExpired += () =>
            {
                MarkFieldDirty();
                MaybeRunAi();
            };

            _referobot?.Bind(engine);
            _lastFaceUp.Clear();
            _lastFieldIds.Clear();
            DuelPresentationPacer.Clear();
            _preDuelComplete = !engine.OpeningSequenceActive;
            // Immediate first paint
            RefreshNow();
            // Anime pre-duel: disk deploy → shuffle → hand to deck zone → draw
            if (engine.OpeningSequenceActive)
                StartCoroutine(StartPreDuelWhenReady());
        }

        System.Collections.IEnumerator StartPreDuelWhenReady()
        {
            // Wait until AR interaction + player disk exist (can take several frames)
            ArDuelInteractionSystem ix = null;
            for (var i = 0; i < 120; i++)
            {
                ix = _arSpace != null ? _arSpace.Interaction : null;
                if (ix == null)
                    ix = FindAnyObjectByType<ArDuelInteractionSystem>();
                if (ix?.PlayerDisk != null)
                    break;
                yield return null;
            }

            if (ix?.PlayerDisk == null)
                Debug.LogWarning("[WRLDZ PreDuel] Timed out waiting for AR disk — cinematic will be limited");

            var cam = _arSpace != null
                ? _arSpace.GetComponentInChildren<Camera>(true)
                : Camera.main;
            RectTransform vp = null;
            if (_arSpace != null)
            {
                var raws = _arSpace.GetComponentsInChildren<UnityEngine.UI.RawImage>(true);
                if (raws != null && raws.Length > 0)
                    vp = raws[0].rectTransform;
            }

            // Ensure deck stack exists before deploy/shuffle
            ix?.PlayerDisk?.EnsureDeckStackVisual();

            _preDuel = PreDuelCinematic.Ensure(gameObject);
            Transform uiRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _preDuel.Begin(
                _engine,
                ix,
                cam,
                vp,
                msg =>
                {
                    if (_hint != null) _hint.text = msg;
                    if (_status != null && !string.IsNullOrEmpty(msg))
                        _status.text = msg;
                },
                () =>
                {
                    _preDuelComplete = true;
                    // Safety: ensure AR hand holos after cinematic
                    _arSpace?.Interaction?.SetHandVolumeVisible(true);
                    _arSpace?.Interaction?.SyncNow();
                    WireArFieldTaps();
                    RefreshNow();
                    FreeUiKit.PlayConfirm();
                },
                uiRoot);
        }

        public void Unbind()
        {
            if (_responseClock != null)
            {
                _responseClock.OnTick -= OnResponseTick;
                _responseClock.Unbind();
            }

            if (_reviewPanel != null)
            {
                _reviewPanel.Unbind();
                _reviewPanel = null;
            }

            _reviewLog = null;

            if (_engine == null) return;
            _engine.OnLog -= AppendLog;
            _engine.OnStateChanged -= MarkFieldDirty;
            _engine.OnGameOver -= OnGameOverUi;
            _referobot?.Bind(null);
        }

        void EnsureReviewLog()
        {
            if (_engine == null) return;

            // Engine owns the session (new log each StartDuel / Restart)
            var engLog = _engine.ReviewLog;
            if (engLog == null)
            {
                // UI bound before StartDuel — create temporary until StartDuel replaces it
                if (_reviewLog == null)
                {
                    _reviewLog = new DuelReviewLog();
                    _reviewLog.Bind(_engine);
                }
            }
            else if (!ReferenceEquals(_reviewLog, engLog))
            {
                _reviewLog = engLog;
            }

            if (_reviewPanel == null)
            {
                // BuildUi attaches full-screen UI under first child
                var attach = transform.childCount > 0 ? transform.GetChild(0) : transform;
                try
                {
                    _reviewPanel = DuelReviewLogPanel.Create(attach, _reviewLog);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] Review log panel failed: " + ex.Message);
                }
            }
            else
                _reviewPanel.Bind(_reviewLog);
        }

        /// <summary>Queue a single field rebuild for LateUpdate (handles Notify + UI double-fire).</summary>
        void MarkFieldDirty() => _fieldDirty = true;

        /// <summary>Public entry used by buttons — always coalesces.</summary>
        void Refresh() => MarkFieldDirty();

        void LateUpdate()
        {
            if (!_fieldDirty || _refreshing) return;
            _fieldDirty = false;
            RefreshNow();
        }

        void OnResponseTick(float remaining)
        {
            var toImpact = remaining;
            var progress = _responseClock != null ? _responseClock.FractionToImpact : 0f;
            var timing = _engine?.PendingResponse?.Timing ?? ResponseTiming.None;

            // Response countdown goes into the single status line (timer UI is hidden)
            if (_status != null && toImpact > 0.05f)
            {
                if (timing == ResponseTiming.DamageCalculation)
                    _status.text = progress < 0.85f
                        ? $"Damage Calc {toImpact:0.0}s — activate Kuriboh now"
                        : $"{toImpact:0.0}s — last chance!";
                else if (timing == ResponseTiming.MonsterSummoned)
                    _status.text = progress < 0.85f
                        ? $"Summon {toImpact:0.0}s — tap a blinking zone to activate"
                        : $"{toImpact:0.0}s — last chance!";
                else
                    _status.text = progress < 0.85f
                        ? $"Attack {toImpact:0.0}s — tap a blinking zone to activate"
                        : $"{toImpact:0.0}s — last chance!";
                _status.color = progress < 0.65f
                    ? new Color(1f, 0.85f, 0.4f, 1f)
                    : new Color(1f, 0.45f, 0.4f, 1f);
            }
            else if (_status != null)
                _status.color = new Color(0.82f, 0.86f, 0.90f, 0.95f);

            if (_timerFill != null && _timerFill.rectTransform != null)
            {
                _timerFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            }

            _arSpace?.SetAttackCharge(playerSide: false, progress);
            _arSpace?.SetAttackCharge(playerSide: true, Mathf.Max(0.15f, 1f - progress * 0.5f));
            // Pulse only on attack-declared windows (not summon / damage calc)
            if (progress > 0.85f && timing == ResponseTiming.AttackDeclared)
                _arSpace?.PulseAttack();
        }

        void UpdatePresentationHoldUi()
        {
            if (_engine == null || !DuelPresentationPacer.IsHolding) return;
            if (_hint != null && !string.IsNullOrEmpty(DuelPresentationPacer.Reason))
                _hint.text = DuelPresentationPacer.Reason;
            if (_timerLabel != null && !_engine.IsAwaitingPlayerResponse)
            {
                var left = DuelPresentationPacer.SecondsRemaining;
                if (left > 0.05f)
                    _timerLabel.text = $"Read {left:0.0}s";
            }
        }

        /// <summary>
        /// AR RawImage: floating hand holos open the play menu; disk pad cards
        /// open Attack / action menus. Arena artwork holograms are not tappable
        /// (presentation until 3D models). Safe to call repeatedly — rebinds when
        /// Interaction mounts late.
        /// </summary>
        void WireArFieldTaps()
        {
            var drag = _arSpace?.Interaction?.Drag;
            if (drag == null)
            {
                var ix = FindAnyObjectByType<ArDuelInteractionSystem>();
                drag = ix?.Drag;
            }

            if (drag == null) return;
            drag.OnFieldCardTapped -= OnArFieldCardTapped;
            drag.OnFieldCardTapped += OnArFieldCardTapped;
            drag.OnEmptyZoneTapped -= OnArEmptyZoneTapped;
            drag.OnEmptyZoneTapped += OnArEmptyZoneTapped;
            drag.OnHandCardTapped -= OnArHandCardTapped;
            drag.OnHandCardTapped += OnArHandCardTapped;
            drag.OnGraveyardTapped -= OnArGraveyardTapped;
            drag.OnGraveyardTapped += OnArGraveyardTapped;
            // Banished/RFG floater — Backup-bot stub; Dilbot owns full pick/target routing.
            drag.OnBanishedTapped -= OnArBanishedTapped;
            drag.OnBanishedTapped += OnArBanishedTapped;
            drag.OnPhaseButtonTapped -= OnArPhaseButtonTapped;
            drag.OnPhaseButtonTapped += OnArPhaseButtonTapped;
            drag.OnOppGlanceCardTapped -= OnArOppGlanceCardTapped;
            drag.OnOppGlanceCardTapped += OnArOppGlanceCardTapped;
        }

        void OnArOppGlanceCardTapped(CardInstance card, bool publicFace)
        {
            if (card == null || _engine == null) return;

            // The glance is the opponent-field view used during target windows too:
            // only a legal card is painted/pickable, but keep this guard for stale
            // colliders after a same-frame engine update.
            if (_engine.IsAwaitingEffectTarget)
            {
                if (_engine.TrySelectEffectTarget(card))
                {
                    ClearCardSelection();
                    Refresh();
                    return;
                }
                if (_status != null) _status.text = $"Not a legal target: {card.Name}";
                return;
            }

            if (_attackPickerAttacker != null && _engine.Phase == DuelPhase.Battle)
            {
                if (_attackTargets.Exists(c => c != null && c.InstanceId == card.InstanceId))
                {
                    DeclareAttackOn(_attackPickerAttacker, card);
                    return;
                }
                if (_status != null) _status.text = "That opponent monster is not an attack target.";
                return;
            }

            OpenInspect(card, showFace: publicFace, null);
        }

        void OnArPhaseButtonTapped(ArDiskPhaseKind kind)
        {
            switch (kind)
            {
                case ArDiskPhaseKind.Battle:
                    DoBattle();
                    break;
                case ArDiskPhaseKind.Main2:
                    DoMain2();
                    break;
                case ArDiskPhaseKind.End:
                    DoEndTurn();
                    break;
            }
        }

        void OnArGraveyardTapped(bool playerSide)
        {
            OpenGraveyard(playerSide);
        }

        /// <summary>
        /// Banished floater tap (Backup-bot presentation stub).
        /// Dilbot: extend for targeting / opp side / effect FD rules as needed.
        /// </summary>
        void OnArBanishedTapped(bool playerSide)
        {
            OpenBanished(playerSide);
        }

        void OpenBanished(bool playerSide)
        {
            if (_engine == null) return;
            var who = playerSide
                ? (CommandWho() ?? _engine.Player)
                : _engine.Opponent;
            if (who == null) return;
            var pile = who.Banished;
            if (pile == null || pile.Count == 0) return;
            if (_banishedBrowser == null)
            {
                var attach = transform.childCount > 0 ? transform.GetChild(0) : transform;
                try { _banishedBrowser = BanishedBrowser.Create(attach); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] BanishedBrowser: " + ex.Message);
                    return;
                }
            }

            DismissInspectQuiet();
            HideTargetPicker();
            WrldzAudio.PlayCardTap();
            // onPick: inspect for now — Dilbot routes effect targets like OnGraveyardCardPicked.
            _banishedBrowser.Show(pile, _db, playerSide, card =>
            {
                if (card == null) return;
                if (_engine.IsAwaitingEffectTarget)
                {
                    if (_engine.TrySelectEffectTarget(card))
                    {
                        _banishedBrowser?.Hide();
                        ClearCardSelection();
                        Refresh();
                        return;
                    }
                    if (_status != null)
                        _status.text = $"Not a legal target: {card.Name}";
                    return;
                }
                OpenInspect(card, showFace: card.FaceUp, null);
            });
        }

        /// <summary>Tap a floating hand holo — select it; drag-to-disk is the other play path.</summary>
        void OnArHandCardTapped(CardInstance card)
        {
            if (card == null || _engine == null) return;
            if (_engine.IsAwaitingEffectTarget) return;
            if (TryToggleInspectClosed(card))
            {
                Refresh();
                return;
            }

            OpenCardMenu(card);
        }

        void OnArFieldCardTapped(CardInstance card, bool playerSide, bool isMonster)
        {
            if (_engine == null || card == null) return;
            // Effect target window: pad taps on own or opp field (InstanceId-safe)
            if (_engine.IsAwaitingEffectTarget)
            {
                if (_engine.TrySelectEffectTarget(card))
                {
                    ClearCardSelection();
                    Refresh();
                    return;
                }

                if (_status != null)
                    _status.text = $"Not a legal target: {card.Name}";
                return;
            }

            if (TryActivateResponseCard(card))
                return;

            WrldzAudio.PlayCardTap();

            // Re-check ownership from engine (disk parent is not always the owner)
            var commander = CommandWho() ?? _engine.Player;
            var mine = commander != null &&
                       (commander.TryFindMonster(card, out _) ||
                        commander.TryFindSpellTrap(card, out _) ||
                        commander.FieldSpellZone?.Occupant == card);
            if (!mine && playerSide)
                playerSide = false; // log/UI said you, engine says opp
            if (mine) playerSide = true;

            if (playerSide)
            {
                if (isMonster)
                {
                    commander.TryFindMonster(card, out var zi);

                    // Summon/Set zone pick: tapping a monster (incl. face-down Set)
                    // tributes it and uses that pad.
                    if (_awaitingZonePick && zi >= 0)
                    {
                        OnArEmptyZoneTapped(RulesZoneKind.Monster, zi);
                        return;
                    }

                    // High-level hand card selected — tap field monsters to mark tributes.
                    if (_engine.InMainPhase && _selectedHand != null &&
                        _selectedHand.Def != null && _selectedHand.Def.IsMonster &&
                        TcgRules.TributesRequired(_selectedHand.Level) > 0)
                    {
                        _engine.ToggleTribute(commander, card);
                        var need = TcgRules.TributesRequired(_selectedHand.Level);
                        var have = _engine.PendingTributes.Count;
                        if (_status != null)
                            _status.text = have >= need
                                ? $"Tributes ready ({have}/{need}) — Summon or Set {_selectedHand.Name}"
                                : $"Tribute select {have}/{need} — tap face-up or face-down monsters";
                        Refresh();
                        return;
                    }

                    if (TryToggleInspectClosed(card))
                    {
                        Refresh();
                        return;
                    }

                    SelectFieldMonster(card);
                    OpenInspect(card, showFace: true, CollectFieldMonsterActions(card));
                }
                else
                {
                    if (TryToggleInspectClosed(card))
                    {
                        Refresh();
                        return;
                    }

                    SelectSpellTrap(card);
                    OpenInspect(card, showFace: true, CollectFieldSpellTrapActions(card));
                }
            }
            else
            {
                // Enemy: declare attack if attacker already chosen
                if (isMonster && _selectedAttacker != null && _engine.Phase == DuelPhase.Battle)
                {
                    DeclareAttackOn(_selectedAttacker, card);
                    return;
                }

                OpenInspect(card, showFace: card.FaceUp, null);
                if (_status != null)
                    _status.text = card.FaceUp
                        ? $"Enemy {card.Name} — select your attacker, then Attack or tap here"
                        : "Enemy Set — select your attacker, then Attack";
            }

            Refresh();
        }

        // Legacy disk-field click path (kept no-op for safety if old refs remain)
        void OnPlayerDiskCardClicked(CardInstance card, bool isMonster)
        {
            if (_engine == null || card == null) return;
            if (_engine.IsAwaitingEffectTarget)
            {
                if (_engine.TrySelectEffectTarget(card))
                {
                    ClearCardSelection();
                    Refresh();
                }
                return;
            }

            if (isMonster)
            {
                if (_engine.InMainPhase && _selectedHand != null &&
                    TcgRules.TributesRequired(_selectedHand.Level) > 0)
                {
                    _engine.ToggleTribute(CommandWho() ?? _engine.Player, card);
                    Refresh();
                    return;
                }

                // Text action menu — owner always sees face of own face-down cards
                SelectFieldMonster(card);
                OpenInspect(card, showFace: true, CollectFieldMonsterActions(card));
            }
            else
            {
                SelectSpellTrap(card);
                OpenInspect(card, showFace: true, CollectFieldSpellTrapActions(card));
            }

            Refresh();
        }

        void OnOppDiskCardClicked(CardInstance card, bool isMonster)
        {
            if (_engine == null || card == null) return;
            if (_engine.IsAwaitingEffectTarget)
            {
                if (_engine.TrySelectEffectTarget(card))
                {
                    ClearCardSelection();
                    Refresh();
                }
                return;
            }

            // Attack declaration onto enemy monster
            if (isMonster && _selectedAttacker != null && _engine.Phase == DuelPhase.Battle)
            {
                if (!_engine.TryAttack(CommandWho() ?? _engine.Player, _selectedAttacker, card))
                    _engine.Log("Attack failed.");
                ClearCardSelection();
                Refresh();
                MaybeRunAi();
                return;
            }

            _engine.Log(card.FaceUp
                ? $"Enemy: {card.Name}"
                : "Enemy Set card");
        }

        static void EnsureEventSystem() => UiFoundation.EnsureEventSystem();

        static Font UiFont()
        {
            if (_font != null) return _font;
            FreeUiKit.EnsureLoaded();
            // Body/UI faces for duel readability; display (Bangers) only on titles
            _font = FreeUiKit.BodyFont()
                    ?? FreeUiKit.UiFont()
                    ?? FreeUiKit.DisplayFont()
                    ?? UiFoundation.BuiltinFont();
            return _font;
        }

        /// <summary>
        /// AR companion when not PreferDigital. Lab test / Instant Digital keep the 2D board.
        /// </summary>
        static bool ResolveArCompanionMode()
        {
            var match = AppSession.Ensure().PendingArMatch;
            // Explicit digital path (Zone Mode DIGITAL, lab PreferDigital)
            if (match != null && match.PreferDigital) return false;
            // Test duel / desktop instant often PreferDigital — if no match, companion off
            // so editor lab still has a playable 2D board.
            if (match == null) return false;
            // Zone Mode AR · Create PvAI/PvP · Tear AR → phone is companion
            return true;
        }

        void BuildUi()
        {
            FreeUiKit.EnsureLoaded();
            WRLDZ.Core.WrldzLab.Apply();
            _arCompanionMode = ResolveArCompanionMode();

            var canvasGo = new GameObject("DuelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            UiTheme.ApplyPortraitPhone(canvasGo.GetComponent<CanvasScaler>());

            var root = CreatePanel(canvasGo.transform, "Root", new Color(0.05f, 0.07f, 0.10f, 0f));
            Stretch(root);

            if (_arCompanionMode)
            {
                BuildArCompanionUi(root);
                return;
            }

            // ── DIGITAL layout: 2D field + hand + dock ──
            BuildDigitalDuelUi(root);
        }

        /// <summary>
        /// AR / Zone Mode AR: full-bleed Solid Vision stage + phone companion
        /// (profile, decks, bag, tome, artifacts, settings). No 2D duel board.
        /// </summary>
        void BuildArCompanionUi(Transform root)
        {
            Debug.Log("[WRLDZ] DuelUI · AR companion phone (no 2D duel board)");

            var arRootImg = root.GetComponent<Image>();
            if (arRootImg != null)
            {
                arRootImg.color = Color.clear;
                arRootImg.raycastTarget = false;
            }

            // Full-bleed AR stage (passthrough / sim behind companion chrome)
            try
            {
                _arSpace = ArDuelSpace.CreateInUi(root, 0f, 0f, 1f, 1f, lifetimeParent: transform);
                if (_engine != null && _db != null)
                    _arSpace.BindEngine(_engine, _db);
                WireArFieldTaps();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] ArDuelSpace failed: " + ex.Message);
            }

            BindFloatingHud(root);

            if (ArPresentationTarget.IsSalvageOst)
            {
                // Combiner only sees the letterboxed optical window — hide phone chrome.
                if (_floatHud != null) _floatHud.gameObject.SetActive(false);
            }

            _companion = ArCompanionPhoneHud.Build(root, LeaveDuelToMap, () =>
            {
                if (_reviewPanel != null)
                    _reviewPanel.gameObject.SetActive(true);
            });

            _field = CreateText(root, "MetaHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _field.gameObject.SetActive(false);
            _log = CreateText(root, "LogHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _log.gameObject.SetActive(false);
            _referobot = null;

            BuildResponseTray(root);

            // Fallback 2D tray — hidden while torso holos are the live hand
            _handRow = CreateDiskZone(root, "ArHandTray", 0.08f, 0.016f, 0.82f, 0.118f,
                new Color(0.03f, 0.04f, 0.06f, 0.12f));
            _handRow.gameObject.SetActive(false);
            var handImgAr = _handRow.GetComponent<Image>();
            if (handImgAr != null) handImgAr.raycastTarget = true;
            BuildHandShuffleButton(root, 0.012f, 0.016f, 0.068f, 0.118f);
            BuildZonePicker(root, 0.22f, 0.210f, 0.78f, 0.252f);
            if (ArPresentationTarget.IsSalvageOst)
            {
                if (_companion?.Root != null) _companion.Root.gameObject.SetActive(false);
                if (_btnShuffleHand != null) _btnShuffleHand.gameObject.SetActive(false);
            }
            _handRow.SetAsLastSibling();
            if (_btnShuffleHand != null)
                _btnShuffleHand.transform.SetAsLastSibling();

            // Target picker still needed for Sangan / MEB / MST (large sheet over companion)
            BuildEffectTargetPicker(root);

            try { _inspect = CardInspectPopup.Create(root); }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ] CardInspectPopup: " + ex.Message); }
            try { _gyBrowser = GraveyardBrowser.Create(root); }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ] GraveyardBrowser: " + ex.Message); }

            BuildGameOverOverlay(root);
        }

        /// <summary>
        /// Glanceable glass islands: YOU/OPP LP, phase, status, phase CTAs, Cancel.
        /// Replaces the old full-width combat bar + disk phase hub.
        /// </summary>
        void BindFloatingHud(Transform root)
        {
            _floatHud = DuelFloatingHud.Create(root);
            _youLpOrb = _floatHud.YouLp;
            _oppLpOrb = _floatHud.OppLp;
            _phaseBanner = _floatHud.PhaseLabel;
            _status = _floatHud.StatusLine;
            _hint = _status;
            _deckCountLabel = _floatHud.DeckCount;
            _gyCountLabel = _floatHud.GyCount;
            _floatHud.OnGyClicked = () => OpenGraveyard(true);
            _extraCountLabel = _floatHud.ExtraCount;
            _phasePillMp1 = _floatHud.PillMp1;
            _phasePillBattle = _floatHud.PillBattle;
            _phasePillMp2 = _floatHud.PillMp2;
            _phasePillEnd = _floatHud.PillEnd;

            _btnBattle = CreateButton(_floatHud.ActionRow, "BATTLE", DoBattle, GbaTheme.CmdBattle);
            _btnMain2 = CreateButton(_floatHud.ActionRow, "MAIN 2", DoMain2, GbaTheme.CmdNeutral);
            _btnEnd = CreateButton(_floatHud.ActionRow, "END", DoEndTurn, GbaTheme.CmdDanger);
            FitPhaseLabels(_btnBattle, _btnMain2, _btnEnd);
            ShrinkPhaseChip(_btnBattle);
            ShrinkPhaseChip(_btnMain2);
            ShrinkPhaseChip(_btnEnd);

            _btnCancelTarget = CreateButton(_floatHud.ContextRow, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
            _btnClearTrib = CreateButton(_floatHud.ContextRow, "TRIBUTES", () =>
            {
                _engine.ClearTributes();
                Refresh();
            }, GbaTheme.CmdMuted);
            _floatHud.SetActionsVisible(false);
            _floatHud.SetContextVisible(false);

            _timerLabel = CreateText(root, "TimerHidden", 10, TextAnchor.MiddleCenter, FontStyle.Bold);
            _timerLabel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Phase advance controls used to live on a disk-cuff column. They now
        /// sit in <see cref="DuelFloatingHud"/> so the AR stage stays open.
        /// </summary>
        void BuildDiskPhaseHub(Transform root, bool arCompanion)
        {
            if (_floatHud == null)
                BindFloatingHud(root);
        }

        void BuildDigitalDuelUi(Transform root)
        {
            // Lab / PreferDigital: AR stage is the hero (full-bleed). Digital zones are a
            // compact translucent strip so disks + holos stay visible while developing.
            Debug.Log("[WRLDZ] DuelUI · Digital lab layout (AR full-bleed · compact zone strip)");

            // Soft root so AR RT reads cleanly
            var rootImg = root.GetComponent<Image>();
            if (rootImg != null) rootImg.color = new Color(0.02f, 0.03f, 0.04f, 0.10f);

            try
            {
                // Full-bleed Solid Vision stage (behind chrome)
                _arSpace = ArDuelSpace.CreateInUi(root, 0f, 0f, 1f, 1f,
                    lifetimeParent: transform);
                if (_engine != null && _db != null)
                    _arSpace.BindEngine(_engine, _db);
                WireArFieldTaps();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] ArDuelSpace failed: " + ex.Message);
            }

            BindFloatingHud(root);

            _field = CreateText(root, "MetaHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _field.gameObject.SetActive(false);
            _log = CreateText(root, "LogHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _log.gameObject.SetActive(false);

            _referobot = null;

            BuildResponseTray(root);
            BuildEffectTargetPicker(root);

            // Fallback 2D tray — hidden while torso holos are the live hand
            _handRow = CreateDiskZone(root, "Hand", 0.10f, 0.016f, 0.82f, 0.118f,
                new Color(0.03f, 0.035f, 0.045f, 0.12f));
            _handRow.gameObject.SetActive(false);
            var handImgDig = _handRow.GetComponent<Image>();
            if (handImgDig != null) handImgDig.raycastTarget = true;
            BuildHandShuffleButton(root, 0.012f, 0.016f, 0.068f, 0.118f);
            BuildZonePicker(root, 0.22f, 0.210f, 0.78f, 0.252f);

            BuildBottomDock(root);

            // Keep field zone strip above AR viewport so 2D Sangan/etc. stay tappable
            var fieldBoard = root.Find("FieldBoard");
            if (fieldBoard != null)
                fieldBoard.SetAsLastSibling();
            if (_handRow != null)
                _handRow.SetAsLastSibling();
            if (_btnShuffleHand != null)
                _btnShuffleHand.transform.SetAsLastSibling();

            try { _inspect = CardInspectPopup.Create(root); }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ] CardInspectPopup: " + ex.Message); }
            try { _gyBrowser = GraveyardBrowser.Create(root); }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ] GraveyardBrowser: " + ex.Message); }

            BuildGameOverOverlay(root);
            WireArFieldTaps();
        }

        void BuildGameOverOverlay(Transform root)
        {
            _overlay = CreatePanel(root, "GameOverOverlay", new Color(0.01f, 0.02f, 0.05f, 0.28f)).gameObject;
            Stretch(_overlay.GetComponent<RectTransform>());
            var overlayImg = _overlay.GetComponent<Image>();
            if (overlayImg != null) overlayImg.raycastTarget = true;
            _overlay.SetActive(false);

            var box = FloatingPanel.Create(_overlay.transform, "Box", goldEdge: true);
            FloatingPanel.Place(box, 0.12f, 0.34f, 0.88f, 0.68f);
            var boxImg = box.GetComponent<Image>();
            if (boxImg != null) boxImg.color = new Color(0.04f, 0.06f, 0.10f, 0.94f);
            _overlayTitle = CreateText(box, "Title", 36, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            _overlayTitle.color = DuelystUi.Gold;
            Place(_overlayTitle.rectTransform, 0.05f, 0.55f, 0.95f, 0.9f);
            _overlaySub = CreateText(box, "Sub", 16, TextAnchor.MiddleCenter, FontStyle.Normal);
            _overlaySub.color = DuelystUi.TextCream;
            Place(_overlaySub.rectTransform, 0.08f, 0.28f, 0.92f, 0.55f);
            var restartRow = CreatePanel(box, "RestartRow", new Color(0, 0, 0, 0));
            Place(restartRow, 0.18f, 0.08f, 0.82f, 0.26f);
            AddHLayout(restartRow);
            CreateButton(restartRow, "Restart Duel", DoRestart, GbaTheme.CmdSafe);

            HideContextualActions();
        }

        /// <summary>Single top chrome: YOU LP · phase · OPP LP.</summary>
        void BuildTopBar(Transform root)
        {
            var bar = CreateGlassBar(root, "TopBar", 0f, 0.94f, 1f, 1f);
            bar.GetComponent<Image>().color = new Color(0.03f, 0.035f, 0.045f, 0.55f);
            bar.GetComponent<Image>().raycastTarget = false;

            var youOrb = CreatePanel(bar, "YouLp", new Color(0.08f, 0.14f, 0.20f, 0.95f));
            Place(youOrb, 0.02f, 0.12f, 0.20f, 0.88f);
            _youLpOrb = CreateText(youOrb, "YouLP", 20, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            _youLpOrb.color = new Color(0.85f, 0.95f, 1f, 1f);
            Stretch(_youLpOrb.rectTransform);
            _youLpOrb.text = "8000";

            var phaseChip = CreatePanel(bar, "Phase", new Color(0.06f, 0.07f, 0.09f, 0.5f));
            Place(phaseChip, 0.22f, 0.10f, 0.78f, 0.90f);
            _phaseBanner = CreateText(phaseChip, "PhaseTxt", 15, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            _phaseBanner.color = DuelystUi.GoldHot;
            Place(_phaseBanner.rectTransform, 0.02f, 0.38f, 0.98f, 0.95f);
            _phaseBanner.text = "MAIN PHASE 1";

            var oppOrb = CreatePanel(bar, "OppLp", new Color(0.20f, 0.08f, 0.10f, 0.95f));
            Place(oppOrb, 0.80f, 0.12f, 0.98f, 0.88f);
            _oppLpOrb = CreateText(oppOrb, "OppLP", 20, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            _oppLpOrb.color = new Color(1f, 0.82f, 0.86f, 1f);
            Stretch(_oppLpOrb.rectTransform);
            _oppLpOrb.text = "8000";
        }

        /// <summary>
        /// Digital lab no longer paints a 2D zone strip. Empty pads were reading as
        /// "markers where cards will be." The sculpted Battle City disk is the field;
        /// cards appear on it when played. Phase / deck live on the cuff hub.
        /// </summary>
        void BuildBattleCityDiskControl(Transform root)
        {
            BuildDiskPhaseHub(root, arCompanion: false);
        }

        /// <summary>Low-opacity glass plate (no neon rim) for clean HUD chips.</summary>
        static RectTransform CreateGlassBar(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.03f, 0.06f, 0.11f, 0.22f);
            img.raycastTarget = false;
            return rt;
        }


        /// <summary>
        /// Tiny M1/BP/M2/END dots under the phase title inside the top bar (no AR overlay).
        /// </summary>
        void BuildPhasePills(Transform root)
        {
            // Nested under TopBar/Phase so they sit under the phase title
            var phase = root.Find("TopBar/Phase");
            var parent = phase != null ? phase : root;
            var bar = CreateGlassBar(parent, "PhasePills", 0.06f, 0.04f, 0.94f, 0.36f);
            bar.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            bar.GetComponent<Image>().raycastTarget = false;
            _phasePillMp1 = MakePhasePill(bar, "MP1", "MAIN 1", 0.02f, 0.08f, 0.24f, 0.92f);
            _phasePillBattle = MakePhasePill(bar, "BP", "BATTLE", 0.26f, 0.08f, 0.48f, 0.92f);
            _phasePillMp2 = MakePhasePill(bar, "MP2", "MAIN 2", 0.50f, 0.08f, 0.72f, 0.92f);
            _phasePillEnd = MakePhasePill(bar, "EP", "END", 0.74f, 0.08f, 0.98f, 0.92f);
            foreach (var pill in new[] { _phasePillMp1, _phasePillBattle, _phasePillMp2, _phasePillEnd })
            {
                if (pill == null) continue;
                var t = pill.GetComponentInChildren<Text>();
                if (t != null)
                {
                    WrldzType.StyleButtonLabel(t, 12, display: false);
                    t.resizeTextForBestFit = true;
                    t.resizeTextMinSize = 12;
                    t.resizeTextMaxSize = 18;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
                pill.color = new Color(0.14f, 0.16f, 0.19f, 0.95f);
            }
        }

        Image MakePhasePill(Transform parent, string name, string label, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);
            img.raycastTarget = false;
            var t = CreateText(go.transform, "L", 11, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            t.text = label;
            t.color = new Color(0.88f, 0.92f, 0.96f, 0.95f);
            Stretch(t.rectTransform);
            return img;
        }

        /// <summary>
        /// Digital-only utility chips: Map / Restart. Combat chrome lives on
        /// <see cref="DuelFloatingHud"/> so this dock no longer spans the screen.
        /// </summary>
        void BuildBottomDock(Transform root)
        {
            var dock = CreateGlassBar(root, "BottomDock", 0.88f, 0.016f, 0.988f, 0.108f);
            dock.GetComponent<Image>().color = new Color(0.03f, 0.035f, 0.045f, 0.22f);
            dock.GetComponent<Image>().raycastTarget = true;

            _btnMenu = CreateButton(dock, "Map", LeaveDuelToMap, GbaTheme.GoldDim);
            Place(_btnMenu.GetComponent<RectTransform>(), 0.04f, 0.08f, 0.56f, 0.92f);
            _btnRestartBar = CreateButton(dock, "↻", DoRestart, GbaTheme.CmdMuted);
            Place(_btnRestartBar.GetComponent<RectTransform>(), 0.60f, 0.08f, 0.96f, 0.92f);

            // Keep announce/timer objects for code paths, but off the busy dock
            _timerLabel = CreateText(dock, "Timer", 10, TextAnchor.MiddleCenter, FontStyle.Bold);
            _timerLabel.color = new Color(0.5f, 0.9f, 1f, 0.95f);
            Place(_timerLabel.rectTransform, 0.63f, 0.02f, 0.76f, 0.14f);
            _timerLabel.text = "";
            _timerLabel.gameObject.SetActive(false);

            var track = CreatePanel(dock, "TimerTrack", new Color(0.08f, 0.12f, 0.18f, 0.9f));
            Place(track, 0.50f, 0.02f, 0.61f, 0.12f);
            track.gameObject.SetActive(false);
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track, false);
            _timerFill = fillGo.GetComponent<Image>();
            _timerFill.sprite = UiFoundation.WhiteSprite();
            _timerFill.color = new Color(0.25f, 0.85f, 0.55f, 0.95f);
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var fieldGo = new GameObject("AnnounceField", typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(dock, false);
            Place(fieldGo.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.38f, 0.12f);
            fieldGo.SetActive(false);
            var fImg = fieldGo.GetComponent<Image>();
            fImg.sprite = UiFoundation.WhiteSprite();
            fImg.color = new Color(0.05f, 0.08f, 0.14f, 0.95f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(fieldGo.transform, false);
            var text = textGo.GetComponent<Text>();
            WrldzType.Style(text, 12, display: false);
            text.color = WrldzTheme.Cream;
            text.alignment = TextAnchor.MiddleLeft;
            Stretch(textGo.GetComponent<RectTransform>());

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(fieldGo.transform, false);
            var ph = phGo.GetComponent<Text>();
            WrldzType.Style(ph, 11, display: false);
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.55f, 0.6f, 0.68f, 0.55f);
            ph.text = "Announce…";
            Stretch(phGo.GetComponent<RectTransform>());

            _announceField = fieldGo.GetComponent<InputField>();
            _announceField.textComponent = text;
            _announceField.placeholder = ph;
            _announceField.lineType = InputField.LineType.SingleLine;
            _announceField.onEndEdit.AddListener(OnAnnounceEndEdit);

            _announceFeedback = CreateText(root, "AnnFb", 11, TextAnchor.MiddleCenter, FontStyle.Normal);
            _announceFeedback.color = WrldzTheme.Soft;
            Place(_announceFeedback.rectTransform, 0.08f, 0.088f, 0.92f, 0.105f);
            _announceFeedback.text = "";
        }

        /// <summary>Horizontal zone row — soft matte only (no double frame / neon).</summary>
        Transform CreateFieldRow(Transform parent, string name, float x0, float y0, float x1, float y1, Color bg)
        {
            return CreateDiskZone(parent, name, x0, y0, x1, y1,
                new Color(bg.r, bg.g, bg.b, Mathf.Clamp01(bg.a)));
        }

        void OnAnnounceEndEdit(string _)
        {
            // Submit on keyboard done / enter
            if (WRLDZ.Core.WrldzInput.KeyDown(KeyCode.Return) || WRLDZ.Core.WrldzInput.KeyDown(KeyCode.KeypadEnter))
                SubmitAnnounce();
        }

        void SubmitAnnounce()
        {
            if (_engine == null || _announceField == null) return;
            var line = _announceField.text;
            if (string.IsNullOrWhiteSpace(line)) return;

            FreeUiKit.PlayClick();
            var who = CommandWho() ?? _engine.Player;
            var result = DuelCommandService.Announce(_engine, who, line, source: "type");
            _announceField.text = "";
            if (_announceFeedback != null)
            {
                _announceFeedback.text = result.Message ?? "";
                _announceFeedback.color = result.Ok ? WrldzTheme.Ok : WrldzTheme.Danger;
            }

            ClearCardSelection();
            Refresh();
            if (result.Ok)
                MaybeRunAi();
        }

        /// <summary>Route a structured UI intent through the shared legal gateway.</summary>
        DuelCommandResult Cmd(DuelIntent intent)
        {
            if (_engine == null || intent == null)
                return DuelCommandResult.Fail(intent, "No engine.");
            intent.Source = "ui";
            var who = CommandWho() ?? _engine.Player;
            var result = DuelCommandService.Execute(_engine, who, intent);
            if (_announceFeedback != null && !string.IsNullOrEmpty(result.Message))
            {
                _announceFeedback.text = result.Message;
                _announceFeedback.color = result.Ok ? WrldzTheme.Soft : WrldzTheme.Danger;
            }

            return result;
        }

        static RectTransform CreateDiskFrame(Transform parent, string name,
            float x0, float y0, float x1, float y1, Color fill)
        {
            var frame = CreateFramedBar(parent, name, x0, y0, x1, y1, fill);
            return frame;
        }

        static Transform CreateDiskZone(Transform parent, string name, float x0, float y0, float x1, float y1,
            Color? fill = null)
        {
            // Dark matte zone tray — no blue plate, no heavy neon rim
            var panel = CreatePanel(parent, name, fill ?? new Color(0.07f, 0.08f, 0.10f, 0.6f));
            Place(panel, x0, y0, x1, y1);

            var h = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 2, 2);
            return panel;
        }

        static RectTransform CreateSolidBar(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            return CreateFramedBar(parent, name, x0, y0, x1, y1, GbaTheme.NavyDeep);
        }

        static RectTransform CreateFramedBar(Transform parent, string name,
            float x0, float y0, float x1, float y1, Color fill, bool creamFrame = false)
        {
            return CreateNeonBar(parent, name, x0, y0, x1, y1, fill, DuelystUi.NeonEdge);
        }

        /// <summary>Glass HUD bar with neon rim — Master Duel × cyber.</summary>
        static RectTransform CreateNeonBar(Transform parent, string name,
            float x0, float y0, float x1, float y1, Color fill, Color edge)
        {
            FreeUiKit.EnsureLoaded();
            // Outer neon shell
            var shell = CreatePanel(parent, name + "Shell", edge);
            Place(shell, x0, y0, x1, y1);
            shell.GetComponent<Image>().raycastTarget = false;

            var bar = CreatePanel(shell, name, fill);
            Stretch(bar);
            bar.offsetMin = new Vector2(2f, 2f);
            bar.offsetMax = new Vector2(-2f, -2f);
            var fillImg = bar.GetComponent<Image>();
            var plate = DuelystUi.Panel() ?? DuelystUi.Bar();
            if (plate != null)
            {
                fillImg.sprite = plate;
                fillImg.type = Image.Type.Sliced;
                // Tint plate slightly so fill color still reads
                fillImg.color = new Color(
                    Mathf.Clamp01(fill.r + 0.15f),
                    Mathf.Clamp01(fill.g + 0.15f),
                    Mathf.Clamp01(fill.b + 0.2f),
                    Mathf.Max(0.85f, fill.a));
            }
            else
                fillImg.color = fill;
            return bar;
        }

        static void AddNeonLine(Transform parent, string name, float x0, float y0, float x1, float y1, Color c)
        {
            var line = CreatePanel(parent, name, c);
            Place(line, x0, y0, x1, y1);
        }

        static RectTransform CreateKitBar(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var bar = CreateFramedBar(parent, name, x0, y0, x1, y1, new Color(0.04f, 0.08f, 0.16f, 0.95f));
            AddHLayout(bar);
            return bar;
        }

        static void AddHLayout(RectTransform bar)
        {
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }

        static void Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        Transform CreateRow(Transform parent, string name, float y0, float y1)
        {
            var panel = CreatePanel(parent, name, new Color(0.08f, 0.1f, 0.16f, 0.65f));
            Place(panel, 0.02f, y0, 0.98f, y1);
            var h = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;
            h.padding = new RectOffset(8, 8, 4, 4);
            return panel;
        }

        static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            // Solid sprite required for reliable uGUI drawing/raycasts (Unity 6).
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false; // panels don't steal clicks; buttons set true
            return go.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rt)
        {
            Stretch(rt, 0f, 0f);
        }

        static void Stretch(RectTransform rt, float padX, float padY)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        static Text CreateText(Transform parent, string name, int size, TextAnchor anchor, FontStyle style,
            bool title = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: title, heavyOutline: true);
            if (!title)
                t.font = UiFont();
            t.alignment = anchor;
            t.fontStyle = style;
            t.color = DuelystUi.TextCream;
            t.raycastTarget = false;
            Stretch(go.GetComponent<RectTransform>());
            return t;
        }

        static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick,
            Color? color = null, string icon = null)
        {
            FreeUiKit.EnsureLoaded();
            var c = color ?? new Color(0.12f, 0.22f, 0.45f, 1f);
            MenuCommandButton.Kind kind;
            if (c.r > 0.45f && c.g < 0.22f)
                kind = MenuCommandButton.Kind.Danger;
            else if (c.r > 0.45f && c.b < 0.40f)
                kind = MenuCommandButton.Kind.Gold;
            else if (c.g > 0.45f && c.g >= c.r)
                kind = MenuCommandButton.Kind.Primary;
            else
                kind = MenuCommandButton.Kind.Primary;

            var btn = MenuCommandButton.Create(parent, label ?? "", () => onClick(), kind, centerTitle: true);
            var face = btn.GetComponent<Image>();
            var chip = kind == MenuCommandButton.Kind.Gold ? ImagineAssets.HudChipGold()
                : kind == MenuCommandButton.Kind.Danger ? ImagineAssets.HudChipDanger()
                : ImagineAssets.HudChipCyan();
            if (face != null && chip != null)
            {
                face.sprite = chip;
                face.type = Image.Type.Simple;
                face.color = Color.white;
                var ol = btn.GetComponent<Outline>();
                if (ol != null) ol.enabled = false;
            }

            var le = btn.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = 72;
                le.preferredWidth = 120;
                le.preferredHeight = 44;
                le.minHeight = 44;
                le.flexibleWidth = 0;
            }
            return btn;
        }

        static void ShrinkPhaseChip(Button b)
        {
            if (b == null) return;
            var le = b.GetComponent<LayoutElement>();
            if (le == null) le = b.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 96;
            le.preferredWidth = 128;
            le.flexibleWidth = 0;
            le.minHeight = 40;
            le.preferredHeight = 40;
            le.flexibleHeight = 0;
        }

        static void FitPhaseLabels(params Button[] buttons)
        {
            foreach (var b in buttons)
            {
                if (b == null) continue;
                var t = b.GetComponentInChildren<Text>();
                if (t == null) continue;
                WrldzType.StyleButtonLabel(t, 13, display: true);
                WrldzType.ApplyOutline(t, heavy: true, buttonContrast: false);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 11;
                t.resizeTextMaxSize = 16;
                t.alignment = TextAnchor.MiddleCenter;
            }
        }

        void AppendLog(string msg)
        {
            _logLines.Add(msg);
            while (_logLines.Count > MaxLog) _logLines.RemoveAt(0);
            if (_log == null) return;
            // Mid strip: last 3 lines (full transcript lives in Review Log panel)
            var sb = new StringBuilder();
            var start = Mathf.Max(0, _logLines.Count - 3);
            for (var i = start; i < _logLines.Count; i++)
                sb.Append(i > start ? "  ·  " : "").Append(_logLines[i]);
            _log.text = sb.ToString();
        }

        void OnGameOverUi()
        {
            // Anime: fold disks when the duel ends
            _arSpace?.RetractDisks();
            if (_overlay == null) return;
            var win = _engine.Winner != null && _engine.Winner.IsPlayer;
            _overlay.SetActive(true);
            _overlayTitle.text = win ? "YOU WIN!" : "YOU LOSE…";
            _overlayTitle.color = win ? GbaTheme.GoldBright : new Color(1f, 0.45f, 0.45f);

            // Duelist XP (Pokémon GO pace) — skip pure practice
            var rewardLine = ApplyDuelProgressionRewards(win);

            var exportHint = "";
            if (_engine?.ReviewLog != null)
            {
                // Ensure export if game-over handler on log already ran
                var path = _engine.ReviewLog.LastExportPath;
                if (string.IsNullOrEmpty(path))
                    path = _engine.ReviewLog.ExportToDisk();
                if (!string.IsNullOrEmpty(path))
                    exportHint = "\n\nAI review log saved:\n" + path;
            }

            var acc = AppSession.Ensure()?.Account;
            var deactivated = acc != null && acc.deactivated;
            if (deactivated)
            {
                _overlayTitle.text = "SOUL TAKEN";
                _overlayTitle.color = new Color(1f, 0.35f, 0.45f);
                _overlaySub.text =
                    "Every soul fracture is spent. This Spirit Dueler is deactivated.\n" +
                    "Umbrax holds the last fragment.\n\n" +
                    rewardLine +
                    "\nOne soul restores every 24 hours — then you may log in again.\nLeave to return to the title." +
                    exportHint;
            }
            else
            {
                var outcome = win
                    ? "Victory! Opponent LP 0 or deck out."
                    : "Defeat… Your LP 0 or deck out.";
                _overlaySub.text = outcome + "\n" + rewardLine +
                                   "\nPress Restart Duel for a rematch." + exportHint;
            }
        }

        /// <summary>Award XP / coins after a real duel; returns a player-facing summary line.</summary>
        string ApplyDuelProgressionRewards(bool playerWon)
        {
            try
            {
                var session = AppSession.Ensure();
                var acc = session.Account;
                if (acc == null)
                    return "Sign in to earn duelist XP.";

                // Lab test account still progresses (smoke-test friendly)
                var match = session.PendingArMatch;
                var turns = _engine != null ? _engine.TurnNumber : 0;
                var reward = ProgressionService.AwardDuelRewards(acc, match, playerWon, turns);
                FreeUiKit.PlayConfirm();
                if (reward.LevelsGained > 0)
                    Debug.Log(
                        $"[WRLDZ] LEVEL UP x{reward.LevelsGained} → Lv{reward.LevelAfter} " +
                        $"({Data.DuelistXpCurve.BandName(reward.LevelAfter)})");
                return reward.SummaryLine ?? "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Progression award failed: " + ex.Message);
                return "Progression update skipped.";
            }
        }

        void RefreshNow()
        {
            if (_engine == null || _refreshing) return;
            _fieldDirty = false; // absorb any queued marks from Notify in this stack
            _refreshing = true;
            // Interaction can mount a few frames after AR stage — keep tap→Attack wired
            WireArFieldTaps();
            try
            {
                // Keep panel attached to latest engine ReviewLog (after RestartDuel)
                if (_engine.ReviewLog != null && !ReferenceEquals(_reviewLog, _engine.ReviewLog))
                    EnsureReviewLog();

                PruneStaleSelection();
                CloseInspectIfCardLeftHand();

                // One status sentence on the floating island (deck counts live on YOU).
                if (_status != null)
                {
                    string line = null;
                    if (!IsAiOpponent && !_engine.GameOver)
                    {
                        var who = CommandWho();
                        var label = who == _engine.Player ? "P1" : "P2";
                        if (_engine.IsAwaitingPlayerResponse)
                            line = $"PASS PHONE · {label} may respond";
                        else if (_engine.TurnPlayer == _engine.Opponent)
                            line = "PASS PHONE · P2 turn";
                        else
                            line = _engine.HintLine();
                    }
                    else
                        line = _engine.HintLine();

                    if (string.IsNullOrEmpty(line))
                        line = _engine.StatusLine();

                    if (_floatHud != null)
                        _floatHud.SetStatus(line);
                    else
                        _status.text = line ?? "";
                }
                if (_engine.Player != null && _engine.Opponent != null)
                {
                    if (_floatHud != null)
                    {
                        _floatHud.SetLifePoints(_engine.Player.LifePoints, _engine.Opponent.LifePoints);
                        _floatHud.SetNames(
                            string.IsNullOrEmpty(_engine.Player.Name) ? "YOU" : ShortName(_engine.Player.Name),
                            string.IsNullOrEmpty(_engine.Opponent.Name) ? "OPP" : ShortName(_engine.Opponent.Name));
                        _floatHud.SetTurn(_engine.TurnNumber);
                    }
                    else
                    {
                        if (_youLpOrb != null)
                            _youLpOrb.text = _engine.Player.LifePoints.ToString();
                        if (_oppLpOrb != null)
                            _oppLpOrb.text = _engine.Opponent.LifePoints.ToString();
                    }

                    if (_diskHubLp != null)
                        _diskHubLp.text = _engine.Player.LifePoints.ToString();
                }

                RefreshDiskBodyCounts();

                if (_engine.Player == null || _engine.Opponent == null)
                    return;

                // Legal plays for the human currently holding the device
                _legalSnap = WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive
                    ? WRLDZ.Duel.Ocg.OcgLegalActions.Build(WRLDZ.Duel.Ocg.OcgLabDuelHost.Current)
                    : LegalIntentService.Build(_engine, CommandWho() ?? _engine.Player);

                if (_engine.GameOver)
                {
                    if (_overlay != null && !_overlay.activeSelf) OnGameOverUi();
                }
                else if (_overlay != null && _overlay.activeSelf)
                    _overlay.SetActive(false);

                if (!_arCompanionMode)
                {
                    // Digital: 2D field + hand
                    RebuildHand();
                    RebuildMonsters(_playerMonsters, _engine.Player, true);
                    RebuildSpellTraps(_playerSpells, _engine.Player, true);
                    RebuildMonsters(_oppMonsters, _engine.Opponent, false);
                    RebuildSpellTraps(_oppSpells, _engine.Opponent, false);
                    RefreshDiskBodyCounts();
                    CommitFieldFaceSnapshot();
                    ClearFieldActionOverlay();
                }
                else
                {
                    // AR: torso holos are the hand (tap / drag). One inspect menu — no HUD clone.
                    RebuildHand();
                    ClearFieldActionOverlay();
                    _companion?.RefreshProfile?.Invoke();
                }

                // AR holograms always (digital uses stage strip; AR uses full-bleed)
                try
                {
                    // Set the filter before SyncFromEngine so the same paint pass
                    // hides non-legal opponent cards in the AR mini-playmat.
                    var glance = _arSpace?.Interaction?.Arena;
                    if (glance != null)
                    {
                        if (_engine.IsAwaitingEffectTarget)
                            glance.SetOpponentTargeting(_engine.PendingActivation?.LegalTargets);
                        else if (_attackPickerAttacker != null && _engine.Phase == DuelPhase.Battle)
                            glance.SetOpponentTargeting(_attackTargets);
                        else
                            glance.ClearOpponentTargeting();
                    }
                    _arSpace?.SyncFromEngine(_engine, _db);
                }
                catch (Exception syncEx)
                {
                    Debug.LogWarning("[WRLDZ] AR space sync: " + syncEx.Message);
                }

                if (_awaitingZonePick && _selectedHand != null)
                {
                    ShowLegalZoneHighlights(_selectedHand);
                    RefreshZonePicker();
                }
                else if (IsLocalResponseWindow())
                    ShowResponseZoneHighlights();
                else
                    ClearLegalZoneHighlights();

                RebuildEffectTargets();
                RebuildResponseTray();
                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] UI Refresh failed: " + ex);
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>
        /// Slim tray: ACTIVATE for every legal response card + Pass.
        /// Field Sets also blink on-zone; tray bg must not steal those taps.
        /// </summary>
        void BuildResponseTray(Transform root)
        {
            var panel = FloatingPanel.Create(root, "ResponseTray", goldEdge: true);
            FloatingPanel.Place(panel, 0.16f, 0.42f, 0.84f, 0.56f);
            var img = panel.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(0.04f, 0.06f, 0.10f, 0.36f);
                // Prompt/buttons catch clicks; empty tray chrome must not block field taps.
                img.raycastTarget = false;
            }
            _responseTray = panel.gameObject;

            _responsePrompt = CreateText(panel, "Prompt", 15, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            _responsePrompt.color = DuelystUi.GoldHot;
            _responsePrompt.raycastTarget = false;
            Place(_responsePrompt.rectTransform, 0.04f, 0.72f, 0.96f, 0.96f);
            _responsePrompt.text = "RESPONSE";

            var row = CreatePanel(panel, "Btns", new Color(0, 0, 0, 0));
            Place(row, 0.03f, 0.08f, 0.97f, 0.68f);
            row.GetComponent<Image>().raycastTarget = false;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.padding = new RectOffset(4, 4, 4, 4);
            _responseBtnRow = row;

            _responseTray.SetActive(false);
        }


        static void StyleResponseTrayButton(Button b, float minWidth, float flexibleWidth)
        {
            if (b == null) return;
            var brt = b.GetComponent<RectTransform>();
            if (brt != null)
                brt.sizeDelta = new Vector2(0f, 0f);
            var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.minWidth = minWidth;
            le.flexibleWidth = flexibleWidth;
            le.minHeight = 72f;
            le.preferredHeight = 72f;
            var t = b.GetComponentInChildren<Text>();
            if (t != null)
            {
                WrldzType.StyleButtonLabel(t, 15, display: true);
                t.alignment = TextAnchor.MiddleCenter;
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 14;
                t.resizeTextMaxSize = 24;
            }
        }

        void RebuildResponseTray()
        {
            if (_responseTray == null || _responseBtnRow == null || _engine == null)
                return;

            var open = _engine.IsAwaitingPlayerResponse &&
                       _engine.PendingResponse != null &&
                       _engine.PendingResponse.Responder != null &&
                       // Only the human at this device (PvAI: always Player; hotseat: responder)
                       (IsAiOpponent
                           ? _engine.PendingResponse.Responder == _engine.Player
                           : _engine.PendingResponse.Responder == CommandWho());

            if (!open)
            {
                _responseTray.SetActive(false);
                return;
            }

            var pr = _engine.PendingResponse;
            _responseTray.SetActive(true);
            _responseTray.transform.SetAsLastSibling();
            var secs = _responseClock != null
                ? _responseClock.SecondsRemaining
                : pr.ReactionSeconds;
            _responsePrompt.text =
                $"{pr.Prompt}\n" +
                $"⏱ {secs:0.0}s — ACTIVATE / PASS, or tap a blinking zone";

            // Clear old buttons
            for (var i = _responseBtnRow.childCount - 1; i >= 0; i--)
                Destroy(_responseBtnRow.GetChild(i).gameObject);

            // Every legal response card gets an ACTIVATE chip (hand QE + field Sets/monsters).
            // Zone blink remains a second path for AR / digital pads.
            if (pr.LegalCards != null)
            {
                var who = CommandWho() ?? _engine.Player;
                foreach (var c in pr.LegalCards)
                {
                    if (c == null || who == null) continue;
                    var card = c;
                    var onField = who.TryFindSpellTrap(card, out _) ||
                                  who.TryFindMonster(card, out _);
                    var label = "ACTIVATE\n" + ShortName(card.Name);
                    var b = CreateButton(_responseBtnRow, label, () =>
                    {
                        FreeUiKit.PlaySelect();
                        if (onField)
                        {
                            if (who.TryFindSpellTrap(card, out _))
                                _selectedSpellTrap = card;
                            else
                                _selectedField = card;
                        }
                        else
                            _selectedHand = card;
                        DoActivate();
                    }, GbaTheme.CmdSafe);
                    StyleResponseTrayButton(b, minWidth: 100f, flexibleWidth: 1f);
                }
            }

            // Explicit PASS (timer expiry still passes via TimedResponseClock).
            {
                var pass = CreateButton(_responseBtnRow, "PASS", DoPassResponse, GbaTheme.CmdDanger);
                StyleResponseTrayButton(pass, minWidth: 88f, flexibleWidth: 0.6f);
            }
        }

        /// <summary>Drop selection if that card left hand/field (prevents ghost actions).</summary>
        void PruneStaleSelection()
        {
            if (_engine?.Player == null)
            {
                ClearCardSelection();
                return;
            }

            var p = _engine.Player;
            if (_selectedHand != null && !p.Hand.Contains(_selectedHand))
                _selectedHand = null;
            if (_selectedField != null && !p.TryFindMonster(_selectedField, out _))
            {
                _selectedField = null;
                _selectedAttacker = null;
            }

            if (_selectedAttacker != null && !p.TryFindMonster(_selectedAttacker, out _))
                _selectedAttacker = null;
            if (_selectedSpellTrap != null && !p.TryFindSpellTrap(_selectedSpellTrap, out _) &&
                !p.Hand.Contains(_selectedSpellTrap))
                _selectedSpellTrap = null;
        }

        /// <summary>
        /// Inspect popup is the only action menu. A HUD chip strip used to clone
        /// Attack / Change Pos / Activate under it whenever a monster was selected.
        /// </summary>
        void ClearFieldActionOverlay()
        {
            if (_handRow == null || _handRow.parent == null) return;
            var existing = _handRow.parent.Find("FieldActionOverlay");
            if (existing != null)
                Destroy(existing.gameObject);
        }

        /// <summary>Second tap on the same floating/disk card closes its menu.</summary>
        bool TryToggleInspectClosed(CardInstance card)
        {
            if (card == null || _inspect == null || !_inspect.IsOpen) return false;
            if (_inspect.ShownCard != card) return false;
            DismissInspectQuiet();
            ClearCardSelection();
            return true;
        }

        /// <summary>
        /// Full-width target picker used for Monster Reborn, Man-Eater Bug, Sangan, MST, etc.
        /// Sized for phone portrait / Desktop Lab — works without AR holograms.
        /// </summary>
        void BuildEffectTargetPicker(Transform root)
        {
            // Dim whole board while choosing so the sheet is the focus
            var dim = CreatePanel(root, "TargetDim", new Color(0.01f, 0.02f, 0.06f, 0.28f));
            Stretch(dim);
            dim.GetComponent<Image>().raycastTarget = true;
            _targetDim = dim.gameObject;
            _targetDim.SetActive(false);

            // Large center sheet (~half the screen) — readable card faces + labels
            var targetBg = CreateGlassBar(root, "TargetPanel", 0.10f, 0.28f, 0.90f, 0.70f);
            targetBg.GetComponent<Image>().color = new Color(0.04f, 0.08f, 0.12f, 0.52f);
            targetBg.GetComponent<Image>().raycastTarget = true;
            _targetPanel = targetBg.gameObject;

            // Gold edge hairline
            var edge = CreatePanel(targetBg, "Edge", new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.75f));
            Place(edge, 0.02f, 0.97f, 0.98f, 0.995f);
            edge.GetComponent<Image>().raycastTarget = false;

            _targetPrompt = CreateText(targetBg, "TargetPrompt", 22, TextAnchor.MiddleCenter, FontStyle.Bold,
                title: true);
            Place(_targetPrompt.rectTransform, 0.04f, 0.86f, 0.78f, 0.97f);
            _targetPrompt.text = "Choose a target";
            _targetPrompt.color = DuelystUi.GoldHot;
            _targetPrompt.horizontalOverflow = HorizontalWrapMode.Wrap;
            _targetPrompt.verticalOverflow = VerticalWrapMode.Truncate;

            _targetHint = CreateText(targetBg, "TargetHint", 14, TextAnchor.MiddleCenter, FontStyle.Normal,
                title: false);
            Place(_targetHint.rectTransform, 0.04f, 0.78f, 0.78f, 0.86f);
            _targetHint.text = "Tap a card below · works on phone / desktop (no AR required)";
            _targetHint.color = new Color(0.85f, 0.92f, 1f, 0.95f);

            _targetCancelBtn = CreateButton(targetBg, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
            Place(_targetCancelBtn.GetComponent<RectTransform>(), 0.78f, 0.86f, 0.97f, 0.96f);

            // Scrollable row of large portrait cards
            var scrollGo = new GameObject("TargetScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(targetBg, false);
            Place(scrollGo.GetComponent<RectTransform>(), 0.03f, 0.04f, 0.97f, 0.76f);
            var scrollBg = scrollGo.GetComponent<Image>();
            scrollBg.sprite = UiFoundation.WhiteSprite();
            scrollBg.color = new Color(0.02f, 0.04f, 0.07f, 0.28f);
            scrollBg.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(6f, 6f);
            vrt.offsetMax = new Vector2(-6f, -6f);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.sprite = UiFoundation.WhiteSprite();
            vpImg.color = new Color(1, 1, 1, 0.02f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = new Vector2(0, 1);
            crt.pivot = new Vector2(0, 0.5f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var hlg = content.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.padding = new RectOffset(12, 12, 10, 10);
            var csf = content.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            _targetRow = content.transform;
            _targetPanel.SetActive(false);
        }

        static bool IsGraveyardTargetKind(EffectTargetKind kind)
        {
            return kind == EffectTargetKind.MonsterInEitherGy ||
                   kind == EffectTargetKind.SpellInYourGy ||
                   kind == EffectTargetKind.TrapInYourGy ||
                   kind == EffectTargetKind.BanishFromYourGy;
        }

        void RebuildEffectTargets()
        {
            if (_targetPanel == null || _targetRow == null) return;
            ClearChildren(_targetRow);

            if (_attackPickerAttacker != null && _attackTargets.Count > 0)
            {
                RebuildAttackTargetPicker();
                return;
            }

            var pending = _engine.PendingActivation;
            if (pending == null)
            {
                _targetPanel.SetActive(false);
                if (_targetDim != null) _targetDim.SetActive(false);
                return;
            }

            // One overlay: hide inspect / GY so they cannot sit on this picker.
            DismissInspectQuiet();
            _gyBrowser?.Hide();

            if (_targetDim != null) _targetDim.SetActive(true);
            _targetPanel.SetActive(true);
            // Bring above hand / field so taps always hit the picker
            _targetPanel.transform.SetAsLastSibling();
            if (_targetDim != null)
                _targetDim.transform.SetSiblingIndex(_targetPanel.transform.GetSiblingIndex() - 1);

            if (_targetPrompt != null)
            {
                var src = pending.Card != null ? pending.Card.Name : "Effect";
                var prompt = string.IsNullOrEmpty(pending.Prompt)
                    ? $"Choose a target for {src}"
                    : pending.Prompt;
                _targetPrompt.text = prompt;
            }

            if (pending.AwaitingCoinCall)
            {
                if (_targetHint != null)
                    _targetHint.text = "Call the coin toss";
                foreach (var heads in new[] { true, false })
                {
                    var call = heads;
                    var b = CreateButton(_targetRow, call ? "HEADS" : "TAILS", () =>
                    {
                        if (_engine.TrySelectCoinCall(call))
                            Refresh();
                    }, GbaTheme.CmdSafe);
                    var le = b.gameObject.AddComponent<LayoutElement>();
                    le.minWidth = 88f;
                    le.minHeight = 56f;
                    le.flexibleWidth = 1f;
                }

                return;
            }

            if (IsGraveyardTargetKind(pending.TargetKind) && _gyBrowser != null)
            {
                // The GY is public information, so use the existing detailed
                // browser and pass only the engine's legal targets. This avoids
                // the old flat name/card strip and keeps invalid cards out.
                _gyBrowser.Show(pending.LegalTargets, _db, true, OnGraveyardCardPicked, "GY TARGETS");
                return;
            }

            if (pending.AwaitingLpCost)
            {
                if (_targetHint != null)
                    _targetHint.text = "Pay LP as the cost — tap an amount";
                foreach (var n in pending.LpCostChoices)
                {
                    var pay = n;
                    var b = CreateButton(_targetRow, $"PAY {pay}", () =>
                    {
                        if (_engine.TrySelectLpCost(pay))
                            Refresh();
                    }, GbaTheme.CmdSafe);
                    var le = b.gameObject.AddComponent<LayoutElement>();
                    le.minWidth = 88f;
                    le.minHeight = 56f;
                    le.flexibleWidth = 1f;
                }

                return;
            }

            if (_targetHint != null)
            {
                var n = pending.LegalTargets?.Count ?? 0;
                _targetHint.text = n <= 0
                    ? "No legal targets — tap CANCEL"
                    : n == 1
                        ? "1 legal target — choose it in the menu, or CANCEL"
                        : $"{n} legal targets — choose one in the menu, or CANCEL";
            }

            if (pending.LegalTargets == null) return;

            var controller = pending.Controller ?? CommandWho() ?? _engine.Player;
            var oppOfController = controller != null ? EnemyOf(controller) : _engine.Opponent;

            foreach (var t in pending.LegalTargets)
            {
                var card = t;
                var tag = "";
                if (pending.TargetKind == EffectTargetKind.MonsterInEitherGy)
                {
                    if (controller != null && controller.Graveyard.Contains(card)) tag = "YOURS\n";
                    else if (oppOfController != null && oppOfController.Graveyard.Contains(card))
                        tag = "OPP\n";
                }
                else if (pending.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq ||
                         pending.TargetKind == EffectTargetKind.FieldSpellInYourDeck ||
                         pending.TargetKind == EffectTargetKind.MonsterInYourDeckFiltered ||
                         pending.TargetKind == EffectTargetKind.MonsterInYourDeckToSummon)
                    tag = "DECK\n";
                else if (pending.TargetKind == EffectTargetKind.SpellInYourGy ||
                         pending.TargetKind == EffectTargetKind.TrapInYourGy)
                    tag = "GY\n";
                else if (pending.TargetKind == EffectTargetKind.SpellTrapOnField)
                {
                    if (controller != null &&
                        (controller.TryFindSpellTrap(card, out _) ||
                         controller.FieldSpellZone?.Occupant == card))
                        tag = "YOURS\n";
                    else
                        tag = "OPP\n";
                }
                else if (pending.TargetKind == EffectTargetKind.DiscardMonsterInHand)
                    tag = "HAND\n";
                else if (pending.TargetKind == EffectTargetKind.BanishFromYourGy)
                    tag = "GY\n";
                else if (pending.TargetKind == EffectTargetKind.AnyCardOnField ||
                         pending.TargetKind == EffectTargetKind.SendFaceUpNamedToGy ||
                         pending.TargetKind == EffectTargetKind.TributeMonsterYouControl ||
                         pending.TargetKind == EffectTargetKind.EquipMonsterYouControl ||
                         pending.TargetKind == EffectTargetKind.SendMonsterYouControlToGy)
                {
                    if (controller != null &&
                        (controller.TryFindMonster(card, out _) ||
                         controller.TryFindSpellTrap(card, out _) ||
                         controller.FieldSpellZone?.Occupant == card))
                        tag = "YOURS\n";
                    else
                        tag = "OPP\n";
                }
                else if (pending.TargetKind == EffectTargetKind.AnyMonsterOnField ||
                         pending.TargetKind == EffectTargetKind.OppFaceUpMonster ||
                         pending.TargetKind == EffectTargetKind.OppFaceUpMonsterAtkLeqLp)
                {
                    // Own-field targets must read clearly (Man-Eater Bug, Raigeki Break, etc.)
                    if (controller != null && controller.TryFindMonster(card, out _))
                        tag = "YOURS\n";
                    else
                        tag = "OPP\n";
                }

                // Large portrait cards — readable on phone/desktop without AR
                var pick = card;
                var go = CreateCardButton(_targetRow, pick, showFace: true, tributeMark: false, () =>
                {
                    if (_engine.TrySelectEffectTarget(pick))
                    {
                        _selectedHand = null;
                        _selectedSpellTrap = null;
                        _selectedField = null;
                    }

                    Refresh();
                }, small: false, forceCardBack: false, handSize: false, wireButtonClick: true,
                    targetPickerSize: true);

                // Green selection rim (don't wash out full card art)
                var rim = new GameObject("LegalRim", typeof(RectTransform), typeof(Image));
                rim.transform.SetParent(go.transform, false);
                rim.transform.SetAsFirstSibling();
                var rimImg = rim.GetComponent<Image>();
                rimImg.sprite = UiFoundation.WhiteSprite();
                rimImg.color = new Color(0.15f, 0.95f, 0.45f, 0.95f);
                rimImg.raycastTarget = false;
                var rrt = rim.GetComponent<RectTransform>();
                rrt.anchorMin = Vector2.zero;
                rrt.anchorMax = Vector2.one;
                rrt.offsetMin = new Vector2(-5f, -5f);
                rrt.offsetMax = new Vector2(5f, 5f);

                var nameTxt = go.GetComponentInChildren<Text>();
                if (nameTxt != null)
                {
                    WrldzType.StyleButtonLabel(nameTxt, 15);
                    nameTxt.fontSize = WrldzType.Readable(15);
                    var stat = "";
                    if (card.Def != null && card.Def.IsMonster)
                        stat = card.FaceUp && card.Position == BattlePosition.Defense
                            ? $"\nDEF {card.CurrentDef}"
                            : $"\nATK {card.CurrentAtk}";
                    nameTxt.text = tag + ShortName(card.Name) + stat;
                }
            }

            var cancel = CreateButton(_targetRow, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
            var cle = cancel.gameObject.AddComponent<LayoutElement>();
            cle.minWidth = 92f;
            cle.minHeight = 56f;
            cle.preferredWidth = 100f;
        }

        void UpdateButtonStates()
        {
            if (_engine?.Player == null) return;

            UpdatePhaseBanner();

            // Pre-duel cinematic: lock phase / card actions
            if (_engine.OpeningSequenceActive || (_preDuel != null && _preDuel.BlocksGameplay && !_preDuelComplete))
            {
                SetPhaseButtons(false, false, false);
                HideContextualActions();
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                return;
            }

            var targeting = _engine.IsAwaitingEffectTarget;
            var responding = _engine.IsAwaitingPlayerResponse;
            var busyAttack = _engine.HasDeclaredAttack;

            // AI turn — unlock Pass when player may activate traps
            if (_aiRunning && IsAiOpponent && !responding && !targeting)
            {
                SetPhaseButtons(false, false, false);
                HideContextualActions();
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                return;
            }

            if (targeting)
            {
                SetPhaseButtons(false, false, false);
                ShowContextualAction(_btnCancelTarget);
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                return;
            }

            if (responding)
            {
                SetPhaseButtons(false, false, false);
                HideContextualActions();
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                _arSpace?.PlayFx(playerSide: true, DiskFxEvent.LegalZonePulse);
                if (_responsePrompt != null && _engine.PendingResponse != null &&
                    _responseClock != null)
                {
                    _responsePrompt.text =
                        $"{_engine.PendingResponse.Prompt}\n" +
                        $"⏱ {_responseClock.SecondsRemaining:0.0}s — ACTIVATE / PASS, or tap a blinking zone";
                }

                return;
            }

            // Hotseat: either human's turn enables phase advance
            var commander = CommandWho();
            var yourTurn = !_engine.GameOver && commander != null && _engine.TurnPlayer == commander;
            var main = yourTurn && _engine.InMainPhase;
            var canAdvance = yourTurn && !_engine.IsAwaitingResponse && !busyAttack;

            // Official advances (Rulebook):
            // Battle from Main1 only (not first turn); Main2 from Main1 or Battle; End from MP1/Battle/MP2
            bool canBattle;
            bool canMain2;
            bool canEnd;
            if (WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive)
            {
                var chips = WRLDZ.Duel.Ocg.OcgLegalActions.PhaseChips(WRLDZ.Duel.Ocg.OcgLabDuelHost.Current);
                canBattle = chips.Battle;
                canMain2 = chips.Main2;
                canEnd = chips.End;
            }
            else
            {
                canBattle = canAdvance && _engine.CanConductBattlePhase;
                canMain2 = canAdvance &&
                           (_engine.Phase == DuelPhase.Main1 || _engine.Phase == DuelPhase.Battle);
                canEnd = canAdvance &&
                         (_engine.Phase == DuelPhase.Main1 ||
                          _engine.Phase == DuelPhase.Battle ||
                          _engine.Phase == DuelPhase.Main2);
            }

            SetPhaseButtons(canBattle, canMain2, canEnd);
            if (main && _engine.PendingTributes.Count > 0)
                ShowContextualAction(_btnClearTrib);
            else
                HideContextualActions();
            SetBtn(_btnRestartBar, true);
            SetBtn(_btnMenu, true);
        }

        /// <summary>Cancel / Tributes share the thumb island — only one visible at a time.</summary>
        void ShowContextualAction(Button which)
        {
            HideContextualActions();
            if (which == null) return;
            which.gameObject.SetActive(true);
            SetBtn(which, true);
            _floatHud?.SetContextVisible(true);
        }

        void HideContextualActions()
        {
            if (_btnCancelTarget != null)
            {
                _btnCancelTarget.gameObject.SetActive(false);
                SetBtn(_btnCancelTarget, false);
            }

            if (_btnClearTrib != null)
            {
                _btnClearTrib.gameObject.SetActive(false);
                SetBtn(_btnClearTrib, false);
            }

            _floatHud?.SetContextVisible(false);
        }

        void SetPhaseButtons(bool battle, bool main2, bool end)
        {
            var rail = PlayerPhaseRail();
            if (rail != null)
            {
                rail.SetEnabled(battle, main2, end);
                _floatHud?.SetActionsVisible(false);
                SetBtn(_btnBattle, false);
                SetBtn(_btnMain2, false);
                SetBtn(_btnEnd, false);
                return;
            }

            SetBtn(_btnBattle, battle);
            SetBtn(_btnMain2, main2);
            SetBtn(_btnEnd, end);
            StylePhaseBtn(_btnBattle, battle, MenuCommandButton.FillGold);
            StylePhaseBtn(_btnMain2, main2, MenuCommandButton.FillPrimary);
            StylePhaseBtn(_btnEnd, end, MenuCommandButton.FillDanger);
            _floatHud?.SetActionsVisible(battle || main2 || end);
        }

        ArDiskPhaseButtons PlayerPhaseRail()
        {
            var ix = _arSpace != null ? _arSpace.Interaction : null;
            if (ix == null)
                ix = FindAnyObjectByType<ArDuelInteractionSystem>();
            var rail = ix?.PlayerDisk?.PhaseButtons;
            if (rail != null && ix.PlayerDisk.IsDeployed)
                rail.SetDeployed(true);
            return rail;
        }

        static void StylePhaseBtn(Button b, bool on, Color glass)
        {
            if (b == null) return;
            var label = b.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = on ? DuelystUi.TextCream : new Color(0.72f, 0.76f, 0.82f, 0.70f);
                WrldzType.ApplyOutline(label, heavy: true, buttonContrast: false);
            }
            var img = b.targetGraphic as Image ?? b.GetComponent<Image>();
            if (img == null) return;
            img.color = on ? Color.white : new Color(1f, 1f, 1f, 0.38f);
        }

        void UpdatePhaseBanner()
        {
            if (_engine == null) return;
            var phase = _engine.Phase;
            var name = phase switch
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
            if (_phaseBanner != null)
            {
                if (_engine.OpeningSequenceActive)
                {
                    _phaseBanner.text = _engine.AwaitingOpeningDrawGesture
                        ? "PRE-DUEL · DRAW FROM DECK"
                        : "PRE-DUEL · DISK READY";
                    _phaseBanner.color = WrldzTheme.Cyan;
                }
                else
                {
                    string who;
                    if (!IsAiOpponent)
                        who = _engine.TurnPlayer == _engine.Player ? "P1" : "P2";
                    else
                        who = _engine.TurnPlayer == _engine.Player ? "YOU" : "OPP";
                    _phaseBanner.text = $"{who} · {name}";
                    _phaseBanner.color = phase == DuelPhase.Battle ? WrldzTheme.Magenta : WrldzTheme.GoldHot;
                }
            }

            // Soft highlight on active phase pill
            SetPhasePill(_phasePillMp1, phase == DuelPhase.Main1, new Color(0.2f, 0.7f, 0.95f, 0.95f));
            SetPhasePill(_phasePillBattle, phase == DuelPhase.Battle, new Color(0.9f, 0.35f, 0.4f, 0.95f));
            SetPhasePill(_phasePillMp2, phase == DuelPhase.Main2, new Color(0.25f, 0.75f, 0.9f, 0.95f));
            SetPhasePill(_phasePillEnd, phase == DuelPhase.End, new Color(0.85f, 0.55f, 0.25f, 0.95f));
        }

        static void SetPhasePill(Image img, bool active, Color hotTint)
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

        static void SetBtn(Button b, bool on)
        {
            if (b != null) b.interactable = on;
        }

        void ClearCardSelection()
        {
            _selectedHand = null;
            _selectedField = null;
            _selectedSpellTrap = null;
            _selectedAttacker = null;
            _awaitingZonePick = false;
            _pendingZoneIndex = -1;
            ClearLegalZoneHighlights();
            RefreshZonePicker();
        }

        void SelectHand(CardInstance c)
        {
            if (_selectedHand == c)
            {
                ClearCardSelection();
                return;
            }

            HoldHand(c);
        }

        /// <summary>Select without toggle — used when committing a chosen zone.</summary>
        void HoldHand(CardInstance c)
        {
            _selectedHand = c;
            _selectedField = null;
            _selectedSpellTrap = null;
            _selectedAttacker = null;
            if (_awaitingZonePick)
            {
                ShowLegalZoneHighlights(c);
                RefreshZonePicker();
            }
            else
                ClearLegalZoneHighlights();
        }

        void SelectFieldMonster(CardInstance c)
        {
            _selectedField = c;
            _selectedHand = null;
            _selectedSpellTrap = null;
            var who = CommandWho() ?? _engine.Player;
            if (_engine.Phase == DuelPhase.Battle && _engine.CanAttack(who, c))
            {
                _selectedAttacker = c;
                var enemy = EnemyOf(who);
                var n = 0;
                if (enemy != null)
                    foreach (var _ in enemy.MonstersOnField()) n++;
                var canDirect = _engine.CanAttackDirectly(who, c);
                _engine.Log(canDirect && n == 0
                    ? $"{c.Name} ready — choose Direct Attack."
                    : canDirect
                        ? $"{c.Name} ready — choose Direct Attack or Attack."
                        : n == 1
                            ? $"{c.Name} ready — choose Attack."
                            : $"{c.Name} ready — choose Attack… and pick a target.");
            }
            else
                _selectedAttacker = null;
        }

        void SelectSpellTrap(CardInstance c)
        {
            _selectedSpellTrap = c;
            _selectedHand = null;
            _selectedField = null;
            _selectedAttacker = null;
        }

        void AttachDiskDropZone(Transform row, bool monsterZone)
        {
            if (row == null) return;
            var zone = row.gameObject.GetComponent<DiskDropZone>()
                       ?? row.gameObject.AddComponent<DiskDropZone>();
            zone.IsMonsterZone = monsterZone;
            zone.EnsureHighlight();
            // Row Image must receive raycasts for drop hit-tests
            var img = row.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;
        }

        /// <summary>Battle City disk body: Main Deck / GY / Extra counts.</summary>
        void RefreshDiskBodyCounts()
        {
            var who = CommandWho() ?? _engine?.Player;
            if (who == null) return;
            var gy = who.Graveyard?.Count ?? 0;
            var extra = who.ExtraDeck?.Count ?? 0;
            if (_floatHud != null)
            {
                _floatHud.SetCounts(who.DeckCount, gy, extra);
                if (_gyBrowser != null && _gyBrowser.IsOpen)
                {
                    var side = _gyBrowser.ShowingPlayerSide ? who : (_engine?.Opponent);
                    _gyBrowser.Refresh(side?.Graveyard);
                }

                return;
            }

            if (_deckCountLabel != null)
                _deckCountLabel.text = "D" + who.DeckCount;
            if (_gyCountLabel != null)
                _gyCountLabel.text = "GY " + gy;
            if (_extraCountLabel != null)
                _extraCountLabel.text = "EX " + extra;
        }

        /// <summary>
        /// Compact "Shuffle" control beside the hand tray (official: rearrange hand freely).
        /// </summary>
        void BuildHandShuffleButton(Transform root, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("ShuffleHand", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(root, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            var orb = ImagineAssets.HudFabOrb();
            img.sprite = orb != null ? orb : UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = orb != null ? Color.white : new Color(0.10f, 0.32f, 0.44f, 0.48f);
            img.raycastTarget = true;
            img.preserveAspect = true;
            if (orb == null)
            {
                var ol = go.AddComponent<Outline>();
                ol.effectColor = new Color(0.35f, 0.78f, 0.90f, 0.70f);
                ol.effectDistance = new Vector2(1.2f, -1.2f);
                ol.useGraphicAlpha = false;
            }
            var shufIcon = ImagineAssets.IconArenaShuffle();
            if (shufIcon != null)
            {
                var ico = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(go.transform, false);
                Place(ico.GetComponent<RectTransform>(), 0.18f, 0.18f, 0.82f, 0.82f);
                var ii = ico.GetComponent<Image>();
                ii.sprite = shufIcon;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                ii.color = Color.white;
            }
            _btnShuffleHand = go.GetComponent<Button>();
            _btnShuffleHand.targetGraphic = img;
            _btnShuffleHand.onClick.AddListener(DoShuffleHand);

            if (shufIcon == null)
            {
                var t = CreateText(go.transform, "L", 11, TextAnchor.MiddleCenter, FontStyle.Bold, title: false);
                t.text = "SHUF";
                t.color = DuelystUi.TextCream;
                Stretch(t.rectTransform);
                t.raycastTarget = false;
            }
        }

        void DoShuffleHand()
        {
            if (!CanRearrangeHand(out var who))
            {
                FreeUiKit.PlayClick();
                return;
            }

            if (who.Hand.Count < 2)
            {
                _engine.Log("Need at least 2 cards in hand to shuffle.");
                FreeUiKit.PlayClick();
                return;
            }

            FreeUiKit.PlaySelect();
            who.ShuffleHand();
            ClearCardSelection();
            _engine.Log($"[{who.Name}] shuffles hand. Drag cards left/right to rearrange.");
            FreeUiKit.PlayConfirm();
            RefreshHandOrder();
        }

        /// <summary>
        /// Manual hand rearrange: drag a card within the tray to a new slot.
        /// Official private knowledge — no priority pass / not a game action.
        /// </summary>
        void DoReorderHandCard(CardInstance card, int newIndex)
        {
            if (card == null || !CanRearrangeHand(out var who))
            {
                RefreshHandOrder();
                return;
            }

            if (who.Hand == null || who.Hand.Count < 2)
            {
                RefreshHandOrder();
                return;
            }

            if (!who.MoveHandCard(card, newIndex))
            {
                // Same slot — settle UI without log spam
                RefreshHandOrder();
                return;
            }

            FreeUiKit.PlaySelect();
            ClearCardSelection();
            // Quiet log — rearranging is free and frequent
            RefreshHandOrder();
        }

        bool CanRearrangeHand(out DuelistState who)
        {
            who = null;
            if (_engine == null || _engine.GameOver) return false;
            if (_engine.OpeningSequenceActive ||
                (_preDuel != null && _preDuel.BlocksGameplay && !_preDuelComplete))
                return false;
            // Allow any time during the duel — rearranging is private (not a game action)
            who = CommandWho() ?? _engine.Player;
            return who?.Hand != null && who.Hand.Count > 0;
        }

        void RefreshHandOrder()
        {
            Refresh();
            try { _arSpace?.Interaction?.SyncNow(); }
            catch { /* optional */ }
        }

        bool FloatingHandIsLive()
        {
            var hv = _arSpace?.Interaction?.HandVolume;
            return hv != null && hv.gameObject.activeInHierarchy;
        }

        void RebuildHand()
        {
            if (_handRow == null) return;
            var who = CommandWho() ?? _engine.Player;
            if (who == null) return;

            if (_btnShuffleHand != null)
                SetBtn(_btnShuffleHand, who.Hand != null && who.Hand.Count >= 2
                    && !_engine.GameOver
                    && !(_engine.OpeningSequenceActive
                         || (_preDuel != null && _preDuel.BlocksGameplay && !_preDuelComplete)));

            var arDrawing = FloatingHandIsLive();
            if (!arDrawing && _drawSfxHandCount >= 0 && who.IsPlayer &&
                who.Hand != null && who.Hand.Count > _drawSfxHandCount)
            {
                var n = who.Hand.Count - _drawSfxHandCount;
                for (var i = 0; i < n; i++)
                    WrldzAudio.PlayCardDraw();
            }

            _drawSfxHandCount = who.Hand?.Count ?? 0;

            // One hand: torso holos own play/inspect. 2D tray only when holos are hidden.
            if (arDrawing || ArPresentationTarget.IsSalvageOst)
            {
                if (_handRow.gameObject.activeSelf)
                {
                    ClearChildren(_handRow);
                    _handRow.gameObject.SetActive(false);
                }

                return;
            }

            if (!_handRow.gameObject.activeSelf)
                _handRow.gameObject.SetActive(true);

            ClearChildren(_handRow);

            foreach (var card in who.Hand)
            {
                var c = card;
                // Glance art always; click → popup menu; drag → disk zones
                var go = CreateCardButton(_handRow, c, showFace: true, tributeMark: false, () =>
                {
                    OpenCardMenu(c);
                }, small: false, forceCardBack: false, handSize: true, wireButtonClick: false);

                // Button steals pointer events from HandCardDrag — remove when drag owns input
                var btn = go.GetComponent<Button>();
                if (btn != null)
                    UnityEngine.Object.Destroy(btn);

                var drag = go.GetComponent<HandCardDrag>() ?? go.AddComponent<HandCardDrag>();
                drag.Card = c;
                drag.HandRow = _handRow as RectTransform;
                drag.OnClickPopup = OpenCardMenu;
                drag.OnDropOnDisk = OnHandDroppedOnDisk;
                drag.OnReorderInHand = DoReorderHandCard;
                // Ensure Graphic receives raycasts for EventSystem
                var handImg = go.GetComponent<Image>();
                if (handImg != null) handImg.raycastTarget = true;
                drag.OnDragBegin = () =>
                {
                    SelectHand(c);
                    // Highlights wait until Summon / Set is chosen (BeginZonePick)
                    _playerMonsters?.GetComponent<DiskDropZone>()?.SetHot(true);
                    _playerSpells?.GetComponent<DiskDropZone>()?.SetHot(true);
                };
                drag.OnDragEnd = () =>
                {
                    _playerMonsters?.GetComponent<DiskDropZone>()?.SetHot(false);
                    _playerSpells?.GetComponent<DiskDropZone>()?.SetHot(false);
                };

                // Legal play glow (cyan) — same list the server would send as "playable"
                if (_legalSnap != null && _legalSnap.CanGlow(c) && _selectedHand != c)
                    Tint(go, LegalIntentService.ColorFor(_legalSnap, c));
                else if (_selectedHand == c)
                    Tint(go, new Color(1f, 0.9f, 0.35f));
            }
        }

        void OpenGraveyard(bool playerSide)
        {
            if (_engine == null) return;
            var who = playerSide
                ? (CommandWho() ?? _engine.Player)
                : _engine.Opponent;
            if (who == null) return;
            if (_gyBrowser == null)
            {
                var attach = transform.childCount > 0 ? transform.GetChild(0) : transform;
                try { _gyBrowser = GraveyardBrowser.Create(attach); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] GraveyardBrowser: " + ex.Message);
                    return;
                }
            }

            DismissInspectQuiet();
            HideTargetPicker();
            WrldzAudio.PlayCardTap();
            _gyBrowser.Show(who.Graveyard, _db, playerSide, OnGraveyardCardPicked);
        }

        void OnGraveyardCardPicked(CardInstance card)
        {
            if (card == null || _engine == null) return;
            if (_engine.IsAwaitingEffectTarget)
            {
                // Target GY cards use the same full inspect component as field
                // cards, so art, name, ATK/DEF and official text are visible
                // before committing the selection.
                var chosen = card;
                OpenInspect(chosen, showFace: true, new List<CardAction>
                {
                    new()
                    {
                        Label = "SELECT TARGET",
                        Color = GbaTheme.CmdSafe,
                        Invoke = () =>
                        {
                            if (_engine.TrySelectEffectTarget(chosen))
                            {
                                ClearCardSelection();
                                Refresh();
                            }
                            else if (_status != null)
                                _status.text = $"Not a legal target: {chosen.Name}";
                        }
                    }
                }, closeLabel: "BACK TO GY", extraOnClose: () =>
                {
                    var pending = _engine.PendingActivation;
                    if (pending != null && IsGraveyardTargetKind(pending.TargetKind) && _gyBrowser != null)
                        _gyBrowser.Show(pending.LegalTargets, _db, true, OnGraveyardCardPicked, "GY TARGETS");
                }, force: true);
                return;
            }

            OpenInspect(card, showFace: true, null);
        }

        /// <summary>Tap card → full art + legal action menu popup.</summary>
        void OpenCardMenu(CardInstance c)
        {
            if (c == null || _engine == null) return;
            if (_engine.IsAwaitingEffectTarget) return;
            WrldzAudio.PlayCardTap();
            HoldHand(c);
            // Field cards may also open via other paths — hand always hand actions
            var commander = CommandWho() ?? _engine.Player;
            var inHand = commander != null && commander.Hand.Contains(c);
            List<CardAction> acts;
            if (inHand)
                acts = CollectHandActions(c);
            else if (c.Def != null && c.Def.IsMonster)
                acts = CollectFieldMonsterActions(c);
            else
                acts = CollectFieldSpellTrapActions(c);

            OpenInspect(c, showFace: true, acts);
        }

        /// <summary>
        /// Drag hand card onto monster row or S/T row of your duel disk.
        /// Monster → monster zones: chooser (Summon ATK / Set Face-Down).
        /// Monster → S/T strip: Set Face-Down.
        /// Spell/Trap → S/T strip: chooser (Set Face-Down / Activate when legal).
        /// </summary>
        void OnHandDroppedOnDisk(CardInstance card, bool monsterZone)
        {
            if (card == null || _engine == null || _engine.GameOver) return;
            var who = CommandWho() ?? _engine.Player;
            if (_engine.TurnPlayer != who || !_engine.InMainPhase)
            {
                _engine.Log("You can only play from hand during your Main Phase.");
                OpenCardMenu(card);
                return;
            }

            SelectHand(card);
            FreeUiKit.PlayConfirm();
            _arSpace?.PlayFx(true, DiskFxEvent.BladeDeploy, 0.45f);

            if (card.Def != null && card.Def.IsMonster)
            {
                if (!_engine.CanNormalSummonOrSet(who, card))
                {
                    // Tribute needed or other restriction — full menu
                    OpenCardMenu(card);
                    return;
                }

                if (monsterZone)
                {
                    // Offer both face-up summon and face-down set
                    OpenPlayChooser(card, CollectMonsterPlayActions(card));
                    return;
                }

                // Dropped on S/T strip → still a monster; pick a Monster Zone to Set
                BeginZonePick(card, asSet: true);
                return;
            }

            // Spell / Trap
            var stActs = CollectSpellTrapPlayActions(card);
            if (stActs.Count == 0)
            {
                OpenCardMenu(card);
                return;
            }

            if (stActs.Count == 1 && stActs[0].Label.Contains("Face-Down"))
            {
                // Only set legal — do it
                stActs[0].Invoke?.Invoke();
                return;
            }

            // Set Face-Down and/or Activate
            OpenPlayChooser(card, stActs);
        }

        /// <summary>Monster play options from hand (select + drag chooser).</summary>
        List<CardAction> CollectMonsterPlayActions(CardInstance card)
        {
            var list = new List<CardAction>();
            if (card == null || _engine == null) return list;
            var who = CommandWho() ?? _engine.Player;
            if (!_engine.CanNormalSummonOrSet(who, card)) return list;

            list.Add(new CardAction
            {
                Label = "Summon ATK",
                Color = GbaTheme.CmdSummon,
                Invoke = () =>
                {
                    SelectHand(card);
                    BeginZonePick(card, asSet: false);
                }
            });
            list.Add(new CardAction
            {
                Label = "Set Face-Down",
                Color = GbaTheme.CmdNeutral,
                Invoke = () =>
                {
                    SelectHand(card);
                    BeginZonePick(card, asSet: true);
                }
            });
            return list;
        }

        /// <summary>Spell/Trap play options from hand.</summary>
        List<CardAction> CollectSpellTrapPlayActions(CardInstance card)
        {
            var list = new List<CardAction>();
            if (card == null || _engine == null) return list;
            var who = CommandWho() ?? _engine.Player;

            if (_engine.CanSetSpellTrap(who, card))
            {
                list.Add(new CardAction
                {
                    Label = "Set Face-Down",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () =>
                    {
                        SelectHand(card);
                        BeginZonePick(card, asSet: true);
                    }
                });
            }

            if (_engine.CanActivateSpellTrap(who, card, fromHand: true))
            {
                list.Add(new CardAction
                {
                    Label = "Activate",
                    Color = GbaTheme.CmdSafe,
                    Invoke = () =>
                    {
                        SelectHand(card);
                        DoActivate();
                    }
                });
            }

            return list;
        }

        /// <summary>Open inspect popup as a play chooser (summon vs set face-down, etc.).</summary>
        void OpenPlayChooser(CardInstance card, List<CardAction> acts)
        {
            if (card == null) return;
            if (acts == null || acts.Count == 0)
            {
                OpenCardMenu(card);
                return;
            }

            SelectHand(card);
            OpenInspect(card, showFace: true, acts);
            Refresh();
        }

        void DismissInspectQuiet()
        {
            _inspectCard = null;
            _inspectFromHand = false;
            _inspect?.HideQuiet();
        }

        void HideTargetPicker()
        {
            _attackPickerAttacker = null;
            _attackTargets.Clear();
            if (_targetPanel != null) _targetPanel.SetActive(false);
            if (_targetDim != null) _targetDim.SetActive(false);
        }

        /// <summary>Open full legal TCG card popup with optional actions.</summary>
        void OpenInspect(CardInstance card, bool showFace, List<CardAction> actions,
            string closeLabel = "CLOSE", Action extraOnClose = null, bool force = false)
        {
            if (_inspect == null || card == null) return;
            if (!force && Time.unscaledTime < _inspectSuppressedUntil) return;
            if (force) _inspectSuppressedUntil = 0f;
            if (!force && _inspect != null && _inspect.IsOpen && _inspect.ShownCard == card)
                return;

            _gyBrowser?.Hide();
            if (_engine == null || !_engine.IsAwaitingEffectTarget)
                HideTargetPicker();

            var who = CommandWho() ?? _engine?.Player;
            _inspectCard = card;
            _inspectFromHand = who != null && who.Hand != null && who.Hand.Contains(card);

            var list = new List<(string, Color, Action)>();
            if (actions != null)
            {
                foreach (var a in actions)
                {
                    var act = a;
                    list.Add((act.Label, act.Color, () =>
                    {
                        // Play first; popup closes itself after the pointer ends
                        // (immediate Hide lets the same tap fall through and reopen).
                        act.Invoke?.Invoke();
                    }));
                }
            }

            _inspect.Show(card, _db, showFace, list, onClose: () =>
            {
                _inspectCard = null;
                _inspectFromHand = false;
                extraOnClose?.Invoke();
            }, closeLabel: closeLabel);
        }

        /// <summary>Close the card viewer after a play. Blocks reopen from the same tap.</summary>
        void CloseInspectAfterPlay()
        {
            _inspectSuppressedUntil = Time.unscaledTime + 0.45f;
            _inspectCard = null;
            _inspectFromHand = false;
            // Close now so menus are usable immediately (don't wait two frames).
            _inspect?.Hide();
            HideContextualActions();
            ClearFieldActionOverlay();
        }

        /// <summary>If the inspected hand card was played, dismiss the viewer.</summary>
        void CloseInspectIfCardLeftHand()
        {
            if (_inspect == null || !_inspect.IsOpen) return;
            if (!_inspectFromHand || _inspectCard == null) return;
            var who = CommandWho() ?? _engine?.Player;
            if (who?.Hand != null && who.Hand.Contains(_inspectCard)) return;
            CloseInspectAfterPlay();
        }

        void RebuildSpellTraps(Transform row, DuelistState who, bool playerSide)
        {
            if (row == null || who == null) return;
            ClearChildren(row);
            var mine = IsMySide(who);
            for (var i = 0; i < who.SpellTrapZones.Length; i++)
            {
                var st = who.SpellTrapZones[i].Occupant;
                if (st == null)
                {
                    if (mine && IsLegalPickSlot(RulesZoneKind.SpellTrap, i))
                        CreateEmptyZoneSlot(row, RulesZoneKind.SpellTrap, i);
                    continue;
                }

                var card = st;
                // Official: you may look at your own face-down cards; opp sets stay backs only
                var show = card.FaceUp || mine;
                var go = CreateCardButton(row, card, showFace: show, false, () =>
                {
                    if (_engine.IsAwaitingEffectTarget)
                    {
                        if (_engine.TrySelectEffectTarget(card))
                        {
                            ClearCardSelection();
                            Refresh();
                        }
                        return;
                    }

                    if (!mine)
                    {
                        // Opp face-up: inspect name; set: log only (no face leak)
                        if (card.FaceUp)
                            OpenInspect(card, showFace: true, null);
                        _engine.Log(card.FaceUp
                            ? $"Enemy S/T: {card.Name}"
                            : "Opponent Set Spell/Trap (face-down)");
                        return;
                    }

                    SelectSpellTrap(card);
                    OpenInspect(card, showFace: true, CollectFieldSpellTrapActions(card));
                    Refresh();
                }, small: false, forceCardBack: !card.FaceUp && !mine);
                ApplySpellTrapFieldSize(go, card);
                MaybeAnimateFieldCard(go, card);

                // Face-down sets: high-contrast back tint so they read on dark board
                if (!card.FaceUp)
                {
                    var img = go.GetComponent<Image>();
                    if (img != null)
                        img.color = Color.Lerp(img.color, new Color(0.55f, 0.62f, 0.95f, 1f), 0.25f);
                }

                // Legal / response glow from LegalIntentService
                var isResponse = mine && _legalSnap != null &&
                                 _legalSnap.HasKind(card, LegalIntentService.LegalKind.ResponseActivate);
                var canActivateField = mine && _legalSnap != null &&
                                       _legalSnap.HasKind(card, LegalIntentService.LegalKind.ActivateFromField);
                if (isResponse)
                {
                    AttachResponseBlink(go);
                    var btn = go.GetComponent<Button>();
                    var respondCard = card;
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() =>
                        {
                            FreeUiKit.PlaySelect();
                            TryActivateResponseCard(respondCard);
                        });
                    }
                }
                else if (canActivateField)
                {
                    Tint(go, LegalIntentService.ColorFor(_legalSnap, card));
                    var btn = go.GetComponent<Button>();
                    var respondCard = card;
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() =>
                        {
                            FreeUiKit.PlaySelect();
                            SelectSpellTrap(respondCard);
                            OpenInspect(respondCard, showFace: true, new List<CardAction>
                            {
                                new()
                                {
                                    Label = "Activate",
                                    Color = GbaTheme.CmdSafe,
                                    Invoke = () =>
                                    {
                                        _selectedSpellTrap = respondCard;
                                        DoActivate();
                                    }
                                }
                            });
                            Refresh();
                        });
                    }
                }
                else if (mine && _legalSnap != null && _legalSnap.CanGlow(card) &&
                         _selectedSpellTrap != card)
                    Tint(go, LegalIntentService.ColorFor(_legalSnap, card));
                else if (mine && _selectedSpellTrap == card)
                    Tint(go, new Color(0.4f, 1f, 0.7f));
            }
        }

        void RebuildMonsters(Transform row, DuelistState who, bool playerSide)
        {
            if (row == null || who == null) return;
            ClearChildren(row);
            var commander = CommandWho() ?? _engine.Player;
            // Hotseat: "my side" is whoever currently holds the device
            var mine = IsMySide(who);
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var m = who.MonsterZones[i].Occupant;
                if (m == null)
                {
                    if (mine && IsLegalPickSlot(RulesZoneKind.Monster, i))
                        CreateEmptyZoneSlot(row, RulesZoneKind.Monster, i);
                    continue;
                }

                var card = m;
                var isTribute = mine && _engine.PendingTributes.Contains(card);
                var needsTributePick = mine && _engine.InMainPhase && _selectedHand != null &&
                                       _selectedHand.Def != null && _selectedHand.Def.IsMonster &&
                                       TcgRules.TributesRequired(_selectedHand.Level) > 0;
                var go = CreateCardButton(row, card, card.FaceUp || mine, isTribute, () =>
                {
                    if (_engine.IsAwaitingEffectTarget)
                    {
                        if (_engine.TrySelectEffectTarget(card))
                        {
                            ClearCardSelection();
                            Refresh();
                        }
                        return;
                    }

                    if (mine)
                    {
                        // Tribute mode: tap monsters to mark which will be tributed
                        if (needsTributePick ||
                            (_engine.InMainPhase && _selectedHand != null &&
                             TcgRules.TributesRequired(_selectedHand.Level) > 0))
                        {
                            _engine.ToggleTribute(commander, card);
                            var need = TcgRules.TributesRequired(_selectedHand.Level);
                            var have = _engine.PendingTributes.Count;
                            if (_status != null)
                                _status.text = have >= need
                                    ? $"Tributes ready ({have}/{need}) — Summon or Set the hand card"
                                    : $"Tribute select {have}/{need} — tap field monsters";
                            Refresh();
                            return;
                        }

                        SelectFieldMonster(card);
                        // Owner always sees face of their own face-down sets
                        OpenInspect(card, showFace: true, CollectFieldMonsterActions(card));
                    }
                    else
                    {
                        if (_selectedAttacker != null && _engine.Phase == DuelPhase.Battle)
                        {
                            if (!_engine.TryAttack(commander, _selectedAttacker, card))
                                _engine.Log("Attack failed.");
                            ClearCardSelection();
                            Refresh();
                            MaybeRunAi();
                            return;
                        }

                        OpenInspect(card, showFace: card.FaceUp, null);
                        if (card.FaceUp)
                        {
                            var pos = card.Position == BattlePosition.Defense ? "DEF" : "ATK";
                            var stat = card.Position == BattlePosition.Defense
                                ? card.CurrentDef
                                : card.CurrentAtk;
                            _engine.Log(
                                $"Enemy: {card.Name} · {pos} {stat} · ATK {card.CurrentAtk}/DEF {card.CurrentDef}");
                        }
                        else
                            _engine.Log("Enemy face-down Defense monster — select attacker, then tap.");
                        return;
                    }

                    Refresh();
                }, small: false, forceCardBack: !card.FaceUp && !mine, handSize: false,
                    wireButtonClick: true);

                // Defense Position: horizontal card (classic YGO field language)
                ApplyFieldMonsterPose(go, card);
                AttachFieldStatBadge(go, card, playerSide || mine);
                MaybeAnimateFieldCard(go, card);

                if (isTribute)
                    Tint(go, new Color(1f, 0.55f, 0.2f, 1f)); // orange = marked for tribute
                else if (needsTributePick)
                    Tint(go, new Color(0.35f, 0.75f, 1f, 1f)); // cyan = can mark
                else if (mine && _legalSnap != null &&
                         _legalSnap.HasKind(card, LegalIntentService.LegalKind.ResponseActivate))
                {
                    AttachResponseBlink(go);
                    var btn = go.GetComponent<Button>();
                    var respondCard = card;
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() =>
                        {
                            FreeUiKit.PlaySelect();
                            TryActivateResponseCard(respondCard);
                        });
                    }
                }
                else if (mine && (_selectedField == card || _selectedAttacker == card))
                    Tint(go, GbaTheme.GoldBright);
                else if (mine && _legalSnap != null && _legalSnap.CanGlow(card))
                    Tint(go, LegalIntentService.ColorFor(_legalSnap, card));
                else if (!mine && _legalSnap != null && _legalSnap.CanGlow(card))
                    // Enemy monsters that are legal attack targets glow gold-soft
                    Tint(go, LegalIntentService.AttackGlowColor);
            }
        }

        /// <summary>Readable S/T pad size on the field board (not tiny 52×40 chips).</summary>
        static void ApplySpellTrapFieldSize(GameObject go, CardInstance card)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) return;
            // Portrait pad — large enough to see card backs on opp row
            le.preferredWidth = 78;
            le.preferredHeight = 108;
            le.minWidth = 64;
            le.minHeight = 90;
        }

        /// <summary>Defense = sideways; Attack = upright. Clear battle position at a glance.</summary>
        static void ApplyFieldMonsterPose(GameObject go, CardInstance card)
        {
            if (go == null || card == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            var inDef = !card.FaceUp || card.Position == BattlePosition.Defense;
            rt.localRotation = inDef
                ? Quaternion.Euler(0f, 0f, 90f)
                : Quaternion.identity;
            // Slightly larger field cards for readability
            var le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = inDef ? 72 : 88;
                le.preferredHeight = inDef ? 100 : 118;
                le.minWidth = le.preferredWidth - 8;
                le.minHeight = le.preferredHeight - 10;
            }
        }

        /// <summary>High-contrast ATK/DEF badge so position is never ambiguous.</summary>
        static void AttachFieldStatBadge(GameObject go, CardInstance card, bool playerSide)
        {
            if (go == null || card == null) return;
            var badge = new GameObject("StatBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(go.transform, false);
            var brt = badge.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0.28f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            // Counter-rotate so text stays upright when card is DEF-sideways
            var inDef = !card.FaceUp || card.Position == BattlePosition.Defense;
            badge.transform.localRotation = inDef
                ? Quaternion.Euler(0f, 0f, -90f)
                : Quaternion.identity;
            var bg = badge.GetComponent<Image>();
            var badgeArt = inDef ? DuelystUi.BadgeDef() : DuelystUi.IconAtk();
            if (badgeArt != null)
            {
                bg.sprite = badgeArt;
                bg.color = new Color(1f, 1f, 1f, 0.92f);
                bg.preserveAspect = true;
            }
            else
            {
                bg.sprite = UiFoundation.WhiteSprite();
                bg.color = inDef
                    ? new Color(0.08f, 0.18f, 0.35f, 0.92f)
                    : new Color(0.25f, 0.08f, 0.06f, 0.92f);
            }
            bg.raycastTarget = false;

            var tGo = new GameObject("T", typeof(RectTransform), typeof(Text));
            tGo.transform.SetParent(badge.transform, false);
            var t = tGo.GetComponent<Text>();
            WrldzType.Style(t, 12, display: false, heavyOutline: true);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            if (!card.FaceUp && !playerSide)
            {
                t.text = "SET DEF";
                t.color = DuelystUi.TextMuted;
            }
            else if (!card.FaceUp)
            {
                t.text = $"SET\nDEF {card.CurrentDef}";
                t.color = DuelystUi.Cyan;
            }
            else if (card.Position == BattlePosition.Defense)
            {
                t.text = $"DEF {card.CurrentDef}";
                t.color = DuelystUi.Cyan;
            }
            else
            {
                t.text = $"ATK {card.CurrentAtk}";
                t.color = DuelystUi.GoldHot;
            }

            Stretch(tGo.GetComponent<RectTransform>());
        }

        // ── Per-card action menus (attached to the card GO) ───────────────────

        bool PlayerMayActOnCards()
        {
            if (_engine == null || _engine.GameOver) return false;
            if (_engine.OpeningSequenceActive) return false;
            if (_engine.IsAwaitingEffectTarget) return false;
            // During AI turn, only allow actions when a player response window is open
            if (IsAiOpponent && _aiRunning && !_engine.IsAwaitingPlayerResponse) return false;
            return true;
        }

        List<CardAction> CollectHandActions(CardInstance card)
        {
            var list = new List<CardAction>();
            if (!PlayerMayActOnCards() || card == null) return list;
            var who = CommandWho() ?? _engine.Player;

            // Damage Calculation / response: hand Quick Effects (Kuriboh) if listed in LegalCards
            if (_engine.IsAwaitingPlayerResponse)
            {
                var pr = _engine.PendingResponse;
                if (pr != null && pr.Responder == who && pr.LegalCards != null &&
                    pr.LegalCards.Exists(c => c != null &&
                                              (c == card || c.InstanceId == card.InstanceId)))
                {
                    list.Add(new CardAction
                    {
                        Label = "Activate",
                        Color = GbaTheme.CmdSafe,
                        Invoke = DoActivate
                    });
                }

                return list;
            }

            // Open-turn hand plays only on your Main Phase
            if (_engine.GameOver || _engine.TurnPlayer != who)
                return list;

            var main = _engine.InMainPhase;
            if (!main) return list;

            // Monster: Normal Summon/Set — tributes must be chosen by the player (no auto-pick)
            if (card.Def != null && card.Def.IsMonster)
            {
                var needTrib = TcgRules.TributesRequired(card.Level);
                var haveTrib = _engine.PendingTributes.Count;
                var fieldMons = who.MonsterCount;
                if (_engine.CanNormalSummonOrSet(who, card))
                {
                    if (needTrib <= 0)
                    {
                        list.Add(new CardAction
                        {
                            Label = "Summon ATK",
                            Color = GbaTheme.CmdSummon,
                            Invoke = () => BeginZonePick(card, false)
                        });
                        list.Add(new CardAction
                        {
                            Label = "Set Face-Down",
                            Color = GbaTheme.CmdNeutral,
                            Invoke = () => BeginZonePick(card, true)
                        });
                    }
                    else if (haveTrib >= needTrib || fieldMons == needTrib)
                    {
                        list.Add(new CardAction
                        {
                            Label = $"Summon ATK ({Mathf.Max(haveTrib, needTrib)}/{needTrib})",
                            Color = GbaTheme.CmdSummon,
                            Invoke = () => BeginZonePick(card, false)
                        });
                        list.Add(new CardAction
                        {
                            Label = $"Set Face-Down ({Mathf.Max(haveTrib, needTrib)}/{needTrib})",
                            Color = GbaTheme.CmdNeutral,
                            Invoke = () => BeginZonePick(card, true)
                        });
                    }
                    else
                    {
                        list.Add(new CardAction
                        {
                            Label = $"Mark tributes {haveTrib}/{needTrib}",
                            Color = GbaTheme.CmdMuted,
                            Invoke = () => _engine.Log(
                                $"Lv{card.Level}: tap your field monsters to Tribute " +
                                $"(face-down Sets count). {haveTrib}/{needTrib} marked — then Summon/Set.")
                        });
                    }
                }
                else if (needTrib > 0)
                {
                    list.Add(new CardAction
                    {
                        Label = $"Need {needTrib} on field",
                        Color = GbaTheme.CmdMuted,
                        Invoke = () => _engine.Log(
                            $"Lv{card.Level}: need {needTrib} monster(s) on your field to Tribute Summon.")
                    });
                }
            }

            // Spell/Trap: set face-down and/or activate from hand when legal
            if (_engine.CanSetSpellTrap(who, card))
            {
                list.Add(new CardAction
                {
                    Label = "Set Face-Down",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () => BeginZonePick(card, true)
                });
            }

            if (_engine.CanActivateSpellTrap(who, card, fromHand: true))
            {
                list.Add(new CardAction
                {
                    Label = "Activate",
                    Color = GbaTheme.CmdSafe,
                    Invoke = DoActivate
                });
            }

            return list;
        }

        List<CardInstance> LegalAttackTargets(DuelistState who, CardInstance attacker,
            IEnumerable<CardInstance> candidates)
        {
            var legal = new List<CardInstance>();
            if (_engine == null || who == null || attacker == null ||
                !_engine.CanAttack(who, attacker) || who.MustAttackDirectlyThisTurn)
                return legal;

            var enemy = EnemyOf(who);
            if (enemy == null || candidates == null) return legal;
            foreach (var target in candidates)
            {
                if (target == null || !enemy.TryFindMonster(target, out _)) continue;
                if (WRLDZ.Duel.TextEffects.ContinuousProtections.CannotBeAttackTarget(
                        _engine, target))
                    continue;
                if (!legal.Exists(c => c.InstanceId == target.InstanceId))
                    legal.Add(target);
            }
            return legal;
        }

        List<CardAction> CollectFieldMonsterActions(CardInstance card)
        {
            var list = new List<CardAction>();
            if (!PlayerMayActOnCards() || card == null) return list;
            var who = CommandWho() ?? _engine.Player;
            if (_engine.IsAwaitingResponse) return list;
            if (_engine.GameOver || _engine.TurnPlayer != who)
                return list;

            if (_engine.InMainPhase)
            {
                if (_selectedHand != null && _selectedHand.Def != null &&
                    _selectedHand.Def.IsMonster &&
                    TcgRules.TributesRequired(_selectedHand.Level) > 0 &&
                    who.TryFindMonster(card, out _))
                {
                    var marked = _engine.PendingTributes.Contains(card);
                    list.Add(new CardAction
                    {
                        Label = marked ? "Unselect Tribute" : "Tribute",
                        Color = GbaTheme.CmdSummon,
                        Invoke = () =>
                        {
                            _engine.ToggleTribute(who, card);
                            Refresh();
                        }
                    });
                }

                if (_engine.CanFlipSummon(who, card))
                {
                    list.Add(new CardAction
                    {
                        Label = "Flip Summon",
                        Color = GbaTheme.CmdNeutral,
                        Invoke = DoFlip
                    });
                }

                if (_engine.CanChangePosition(who, card))
                {
                    list.Add(new CardAction
                    {
                        Label = "Change Pos",
                        Color = GbaTheme.CmdNeutral,
                        Invoke = DoChangePos
                    });
                }

                if (_engine.CanActivateSpellTrap(who, card, fromHand: false))
                {
                    list.Add(new CardAction
                    {
                        Label = "Activate",
                        Color = GbaTheme.CmdSafe,
                        Invoke = () =>
                        {
                            _selectedField = card;
                            DoActivate();
                        }
                    });
                }
            }

            // Battle Phase — explicit text actions (not only log "tap enemy")
            if (_engine.Phase == DuelPhase.Battle && _engine.CanAttack(who, card))
            {
                var enemy = EnemyOf(who);
                var targets = LegalAttackTargets(who, card,
                    enemy != null ? enemy.MonstersOnField() : null);

                if (_engine.CanAttackDirectly(who, card))
                {
                    list.Add(new CardAction
                    {
                        Label = "Direct Attack",
                        Color = GbaTheme.CmdBattle,
                        Invoke = () =>
                        {
                            _selectedAttacker = card;
                            _selectedField = card;
                            DoDirect();
                        }
                    });
                }

                if (targets.Count >= 1)
                {
                    list.Add(new CardAction
                    {
                        Label = "Attack…",
                        Color = GbaTheme.CmdBattle,
                        Invoke = () => OpenAttackTargetChooser(card, targets)
                    });
                }
            }

            return list;
        }

        /// <summary>Battle: pick an opponent monster from the same mini-playmat-style card view used by effects.</summary>
        void OpenAttackTargetChooser(CardInstance attacker, List<CardInstance> targets)
        {
            if (attacker == null || targets == null || targets.Count == 0) return;
            var who = CommandWho() ?? _engine?.Player;
            var legalTargets = LegalAttackTargets(who, attacker, targets);
            if (legalTargets.Count == 0) return;

            _selectedAttacker = attacker;
            _selectedField = attacker;
            _attackPickerAttacker = attacker;
            _attackTargets.Clear();
            _attackTargets.AddRange(legalTargets);

            // Keep the opponent's field floater visible in AR and replace the old
            // attacker inspect/text list with the legal target card tray.
            DismissInspectQuiet();
            Refresh();
        }

        void RebuildAttackTargetPicker()
        {
            if (_targetPanel == null || _targetRow == null || _attackPickerAttacker == null) return;
            _targetPanel.SetActive(true);
            if (_targetDim != null) _targetDim.SetActive(true);
            _targetPanel.transform.SetAsLastSibling();
            if (_targetDim != null) _targetDim.transform.SetSiblingIndex(_targetPanel.transform.GetSiblingIndex() - 1);
            if (_targetPrompt != null)
                _targetPrompt.text = $"Choose an attack target for {_attackPickerAttacker.Name}";
            if (_targetHint != null)
                _targetHint.text = _attackTargets.Count == 1
                    ? "OPP FIELD · 1 legal target"
                    : $"OPP FIELD · {_attackTargets.Count} legal targets";

            foreach (var target in _attackTargets)
            {
                var pick = target;
                var go = CreateCardButton(_targetRow, pick, showFace: true, tributeMark: false,
                    () => DeclareAttackOn(_attackPickerAttacker, pick), small: false, forceCardBack: false,
                    handSize: false, wireButtonClick: true, targetPickerSize: true);
                var rim = new GameObject("AttackLegalRim", typeof(RectTransform), typeof(Image));
                rim.transform.SetParent(go.transform, false);
                rim.transform.SetAsFirstSibling();
                var rimImg = rim.GetComponent<Image>();
                rimImg.sprite = UiFoundation.WhiteSprite();
                rimImg.color = new Color(1f, 0.65f, 0.16f, 0.95f);
                rimImg.raycastTarget = false;
                var rrt = rim.GetComponent<RectTransform>();
                rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
                rrt.offsetMin = new Vector2(-5f, -5f); rrt.offsetMax = new Vector2(5f, 5f);
                var label = go.GetComponentInChildren<Text>();
                if (label != null)
                {
                    var pos = pick.FaceUp ? (pick.Position == BattlePosition.Defense ? "DEF" : "ATK") : "SET DEF";
                    var stat = pick.FaceUp ? (pick.Position == BattlePosition.Defense ? pick.CurrentDef : pick.CurrentAtk).ToString() : "?";
                    label.text = $"OPP FIELD\n{ShortName(pick.Name)}\n{pos} {stat}";
                    WrldzType.StyleButtonLabel(label, 15);
                }
            }

            var cancel = CreateButton(_targetRow, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
            var le = cancel.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 92f; le.minHeight = 56f; le.preferredWidth = 100f;
        }

        void DeclareAttackOn(CardInstance attacker, CardInstance target)
        {
            if (_engine == null || attacker == null) return;
            _attackPickerAttacker = null;
            _attackTargets.Clear();
            var who = CommandWho() ?? _engine.Player;
            FreeUiKit.PlayConfirm();
            if (!_engine.TryAttack(who, attacker, target))
                _engine.Log("Attack failed.");
            ClearCardSelection();
            CloseInspectAfterPlay();
            Refresh();
            MaybeRunAi();
        }

        List<CardAction> CollectFieldSpellTrapActions(CardInstance card)
        {
            var list = new List<CardAction>();
            if (!PlayerMayActOnCards() || card == null) return list;
            var who = CommandWho() ?? _engine.Player;

            // Response window: LegalCards already filtered (Trap Hole / Mirror Force / …).
            // Do not re-gate with open-game-state CanActivate — that was blocking Trap Hole.
            if (_engine.IsAwaitingPlayerResponse)
            {
                var pr = _engine.PendingResponse;
                if (pr != null && pr.LegalCards != null &&
                    (pr.LegalCards.Contains(card) ||
                     pr.LegalCards.Exists(c => c != null && c.InstanceId == card.InstanceId)))
                {
                    list.Add(new CardAction
                    {
                        Label = "Activate",
                        Color = GbaTheme.CmdSafe,
                        Invoke = DoActivate
                    });
                }

                return list;
            }

            if (_engine.GameOver || _engine.TurnPlayer != who)
                return list;

            if (_engine.CanActivateSpellTrap(who, card, fromHand: false))
            {
                list.Add(new CardAction
                {
                    Label = "Activate",
                    Color = GbaTheme.CmdSafe,
                    Invoke = DoActivate
                });
            }

            return list;
        }

        /// <summary>
        /// Builds a vertical action strip parented to the card so buttons sit on/above it.
        /// </summary>
        void AttachCardActions(GameObject cardGo, List<CardAction> actions, bool above)
        {
            if (cardGo == null || actions == null || actions.Count == 0) return;

            // Bring selected card above siblings so the menu is not covered
            cardGo.transform.SetAsLastSibling();

            FreeUiKit.EnsureLoaded();
            var strip = new GameObject("CardActions", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            strip.transform.SetParent(cardGo.transform, false);

            var img = strip.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.04f, 0.06f, 0.14f, 0.96f);
            img.raycastTarget = true;

            var rt = strip.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, above ? 0f : 1f);
            rt.anchorMin = new Vector2(0.5f, above ? 1f : 0f);
            rt.anchorMax = new Vector2(0.5f, above ? 1f : 0f);
            rt.anchoredPosition = new Vector2(0f, above ? 6f : -6f);
            rt.sizeDelta = new Vector2(118f, 0f);

            var v = strip.GetComponent<VerticalLayoutGroup>();
            v.spacing = 3;
            v.padding = new RectOffset(4, 4, 4, 4);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var fit = strip.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var a in actions)
            {
                var act = a;
                CreateCardActionButton(strip.transform, act.Label, act.Color, () =>
                {
                    FreeUiKit.PlayClick();
                    act.Invoke?.Invoke();
                });
            }
        }

        static void CreateCardActionButton(Transform parent, string label, Color color, Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = new Color(color.r, color.g, color.b, 0.50f);
            img.raycastTarget = true;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(color.r + 0.25f, color.g + 0.25f, color.b + 0.25f, 0.70f);
            ol.effectDistance = new Vector2(1.2f, -1.2f);
            ol.useGraphicAlpha = false;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            btn.colors = GbaTheme.ButtonColors();

            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 30;
            le.preferredHeight = 32;
            le.minWidth = 100;
            le.preferredWidth = 110;

            var textGo = new GameObject("L", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var t = textGo.GetComponent<Text>();
            WrldzType.Style(t, 14, display: false);
            t.font = FreeUiKit.UiFont() ?? UiFont();
            t.text = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(textGo.GetComponent<RectTransform>());
        }

        /// <summary>
        /// Flip / pop field cards when they change face or newly land face-up so the player can read them.
        /// </summary>
        void MaybeAnimateFieldCard(GameObject go, CardInstance card)
        {
            if (go == null || card == null) return;
            var id = card.InstanceId;
            var nowUp = card.FaceUp;
            var had = _lastFaceUp.TryGetValue(id, out var wasUp);
            var known = _lastFieldIds.Contains(id);

            if (nowUp && had && !wasUp)
            {
                var anim = UiCardFlipAnimator.Ensure(go);
                // Flip Summon ends in Attack; battle-flip / S/T stay Defense
                if (card.Def != null && card.Def.IsMonster &&
                    card.Position == BattlePosition.Attack)
                {
                    anim?.PlayFlipSummon(card, _db, card.Name);
                    if (_hint != null)
                        _hint.text = $"{card.Name} Flip Summon — flipping, then Attack…";
                }
                else
                {
                    anim?.PlayFlipToFace(card, _db, card.Name);
                    if (_hint != null)
                        _hint.text = $"{card.Name} flipped face-up — read the card…";
                }
            }
            else if (nowUp && !known)
            {
                // Newly arrived face-up (Normal/Special Summon)
                var anim = UiCardFlipAnimator.Ensure(go);
                anim?.PlaySummonPop(card.Name);
            }
            else if (nowUp && known && wasUp)
            {
                // Already face-up: no anim
            }
        }

        void CommitFieldFaceSnapshot()
        {
            _lastFaceUp.Clear();
            _lastFieldIds.Clear();
            if (_engine?.Player == null || _engine.Opponent == null) return;
            void Pack(DuelistState who)
            {
                if (who == null) return;
                foreach (var m in who.MonstersOnField())
                {
                    if (m == null) continue;
                    _lastFaceUp[m.InstanceId] = m.FaceUp;
                    _lastFieldIds.Add(m.InstanceId);
                }

                foreach (var st in who.SpellTrapsOnField())
                {
                    if (st == null) continue;
                    _lastFaceUp[st.InstanceId] = st.FaceUp;
                    _lastFieldIds.Add(st.InstanceId);
                }
            }

            Pack(_engine.Player);
            Pack(_engine.Opponent);
        }

        GameObject CreateCardButton(Transform parent, CardInstance card, bool showFace, bool tributeMark, Action onClick,
            bool small = false, bool forceCardBack = false, bool handSize = false, bool wireButtonClick = true,
            bool targetPickerSize = false)
        {
            var go = new GameObject(card.Name ?? "card", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            // Hand: tall portrait with glance art. Field: compact pads. Target picker: large.
            var le = go.GetComponent<LayoutElement>();
            if (targetPickerSize)
            {
                // ~phone-readable portraits for Monster Reborn / Sangan / MEB pickers
                le.preferredWidth = 132;
                le.preferredHeight = 184;
                le.minWidth = 110;
                le.minHeight = 154;
            }
            else if (handSize)
            {
                le.preferredWidth = 102;
                le.preferredHeight = 140;
                le.minWidth = 84;
                le.minHeight = 118;
            }
            else
            {
                // Disk blade pads — compact so 5 columns fit Battle City strip
                le.preferredWidth = small ? 52 : 62;
                le.preferredHeight = small ? 40 : 54;
                le.minWidth = small ? 44 : 52;
                le.minHeight = small ? 34 : 44;
            }

            le.flexibleWidth = 0;
            le.flexibleHeight = 0;

            var img = go.GetComponent<Image>();
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
            img.preserveAspect = true;

            // ── Front = full TCG face (StreamingAssets/CardArt/{id}.jpg)
            // ── Back  = official-style card back (YgoFrames / WRLDZ CardBack)
            // CardArt files are complete faces (frame+name+art+stats). Do NOT nest them
            // inside a blank template art-window — that double-frames and looks broken.
            if (forceCardBack || !showFace)
            {
                var back = StreamingSprite.CardBack()
                           ?? YgoCardFrames.CardBack();
                img.sprite = back ?? UiFoundation.WhiteSprite();
                if (back == null)
                    img.color = new Color(0.12f, 0.16f, 0.38f, 1f);
            }
            else
            {
                var fullFace = _db != null ? _db.GetArt(card.CardId) : null;
                if (fullFace != null)
                {
                    // Preferred: scanned full card face
                    img.sprite = fullFace;
                    img.color = Color.white;
                }
                else
                {
                    // Fallback when art file missing: blank series frame + type tint
                    var frame = YgoCardFrames.FrameFor(card.Def);
                    img.sprite = frame ?? UiFoundation.WhiteSprite();
                    img.color = card.Def != null && card.Def.IsMonster
                        ? new Color(0.85f, 0.88f, 0.95f, 1f)
                        : card.Def != null && card.Def.IsTrap
                            ? new Color(0.95f, 0.8f, 0.9f, 1f)
                            : new Color(0.8f, 0.95f, 0.88f, 1f);
                }
            }

            if (tributeMark) img.color = Color.Lerp(img.color, new Color(1f, 0.55f, 0.30f, 1f), 0.45f);
            if (_selectedField == card || _selectedAttacker == card || _selectedHand == card ||
                _selectedSpellTrap == card)
                img.color = Color.Lerp(img.color, GbaTheme.GoldBright, 0.22f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (wireButtonClick)
                btn.onClick.AddListener(() => onClick());

            // Glance nameplate: skip on hand when full CardArt face is present (face already has name/ATK).
            // Target picker always shows a bold strip so zone tags (GY / DECK) stay readable.
            // Field pads stay small — keep ATK/DEF strip for battle readability.
            var hasFullFace = showFace && !forceCardBack && _db != null &&
                              _db.GetArt(card.CardId) != null;
            if (showFace && (targetPickerSize || !(handSize && hasFullFace)))
            {
                var bg = new GameObject("LabelBg", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(go.transform, false);
                var bgImg = bg.GetComponent<Image>();
                bgImg.sprite = UiFoundation.WhiteSprite();
                bgImg.type = Image.Type.Simple;
                bgImg.color = new Color(0f, 0f, 0f, 0.72f);
                bgImg.raycastTarget = false;
                var bgrt = bg.GetComponent<RectTransform>();
                if (hasFullFace)
                {
                    // Compact strip under tiny field pads (not the big template nameplate band)
                    bgrt.anchorMin = new Vector2(0, 0);
                    bgrt.anchorMax = new Vector2(1, small ? 0.42f : 0.34f);
                }
                else
                {
                    bgrt.anchorMin = YgoCardFrames.NameplateMin;
                    bgrt.anchorMax = YgoCardFrames.NameplateMax;
                }

                bgrt.offsetMin = Vector2.zero;
                bgrt.offsetMax = Vector2.zero;

                var label = new GameObject("Name", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(go.transform, false);
                var t = label.GetComponent<Text>();
                WrldzType.Style(t, small ? 9 : 10, display: false);
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                if (card.Def != null && card.Def.IsMonster)
                {
                    var atk = card.CurrentAtk;
                    var def = card.CurrentDef;
                    if (!card.FaceUp)
                        t.text = $"{ShortName(card.Name)}\nSET DEF {def}";
                    else if (card.Position == BattlePosition.Defense)
                        t.text = $"{ShortName(card.Name)}\nDEF {def}";
                    else
                        t.text = $"{ShortName(card.Name)}\nATK {atk}";
                }
                else
                    t.text = ShortName(card.Name ?? card.Def?.name);

                var lrt = label.GetComponent<RectTransform>();
                lrt.anchorMin = bgrt.anchorMin;
                lrt.anchorMax = bgrt.anchorMax;
                lrt.offsetMin = new Vector2(2, 1);
                lrt.offsetMax = new Vector2(-2, -1);
                label.transform.SetAsLastSibling();
            }
            else if (!showFace && !forceCardBack)
            {
                var bg = new GameObject("LabelBg", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(go.transform, false);
                var bgImg = bg.GetComponent<Image>();
                bgImg.sprite = UiFoundation.WhiteSprite();
                bgImg.color = new Color(0f, 0f, 0f, 0.75f);
                bgImg.raycastTarget = false;
                var bgrt = bg.GetComponent<RectTransform>();
                bgrt.anchorMin = new Vector2(0, 0);
                bgrt.anchorMax = new Vector2(1, 0.38f);
                bgrt.offsetMin = Vector2.zero;
                bgrt.offsetMax = Vector2.zero;
                var label = new GameObject("Name", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(go.transform, false);
                var t = label.GetComponent<Text>();
                WrldzType.Style(t, 12, display: false);
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.raycastTarget = false;
                t.text = "SET";
                var lrt = label.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0, 0);
                lrt.anchorMax = new Vector2(1, 0.38f);
                lrt.offsetMin = new Vector2(2, 1);
                lrt.offsetMax = new Vector2(-2, -1);
            }

            if (_selectedField == card || _selectedAttacker == card || _selectedHand == card ||
                _selectedSpellTrap == card)
            {
                var sel = new GameObject("Sel", typeof(RectTransform), typeof(Image));
                sel.transform.SetParent(go.transform, false);
                Stretch(sel.GetComponent<RectTransform>());
                var simg = sel.GetComponent<Image>();
                simg.sprite = UiFoundation.WhiteSprite();
                simg.color = new Color(1f, 0.85f, 0.2f, 0.22f);
                simg.raycastTarget = false;
                sel.transform.SetAsLastSibling();
            }

            return go;
        }

        /// <summary>
        /// Soft legal-play / selection tint. Must not replace Image.color with a pure accent —
        /// UI Image multiplies the sprite, so a cyan (0.25,0.95,1) would crush reds and make
        /// full CardArt faces look washed or near-black in low-contrast areas.
        /// </summary>
        static void Tint(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>();
            if (img == null) return;
            // Keep art readable: blend toward accent rather than full multiply replace
            img.color = Color.Lerp(Color.white, c, 0.38f);
        }

        static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Length <= 16 ? name : name.Substring(0, 14) + "…";
        }

        void CreateEmptySlot(Transform parent, bool small = false)
        {
            // Invisible layout spacer only — never paint a pad where a card might go.
            var go = new GameObject("Empty", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = small ? 56 : 64;
            le.preferredHeight = small ? 42 : 56;
            le.flexibleWidth = 0;
            le.flexibleHeight = 0;
        }

        /// <summary>
        /// Immediate destroy so HorizontalLayoutGroup does not keep ghost cards for a frame
        /// (root cause of field lagging behind summons/sets/attacks).
        /// </summary>
        static void ClearChildren(Transform t)
        {
            if (t == null) return;
            for (var i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                // Play mode + edit: Immediate keeps UI in lockstep with engine state
                DestroyImmediate(child);
            }
        }

        static List<LegalIntentService.LegalSlot> OcgZoneSlots(CardInstance card, bool asSet)
        {
            var list = new List<LegalIntentService.LegalSlot>();
            var host = WRLDZ.Duel.Ocg.OcgLabDuelHost.Current;
            if (host == null || card == null) return list;
            for (var i = 0; i < 5; i++)
            {
                var mon = WRLDZ.Duel.Ocg.OcgLegalActions.SlotLegal(
                    host, card, ArDuelZoneKind.Monster, i, asSet);
                var st = WRLDZ.Duel.Ocg.OcgLegalActions.SlotLegal(
                    host, card, ArDuelZoneKind.SpellTrap, i, asSet);
                if (!mon && !st) continue;
                list.Add(new LegalIntentService.LegalSlot
                {
                    Kind = mon ? RulesZoneKind.Monster : RulesZoneKind.SpellTrap,
                    Index = i,
                    CanSummonAtk = mon && !asSet,
                    CanSet = asSet,
                    CanActivate = st && !asSet
                });
            }
            return list;
        }

        bool IsLocalResponseWindow()
        {
            if (_engine == null || !_engine.IsAwaitingPlayerResponse) return false;
            var pr = _engine.PendingResponse;
            if (pr?.Responder == null) return false;
            return IsAiOpponent
                ? pr.Responder == _engine.Player
                : pr.Responder == CommandWho();
        }

        void ShowResponseZoneHighlights()
        {
            var ix = _arSpace?.Interaction;
            if (!IsLocalResponseWindow())
            {
                ix?.ClearLegalPlacements();
                return;
            }

            var who = CommandWho() ?? _engine.Player;
            var slots = LegalIntentService.ResponseActivationSlots(_engine, who);
            ix?.ShowResponseActivations(slots);
        }

        bool TryActivateResponseAtZone(RulesZoneKind kind, int index)
        {
            if (!IsLocalResponseWindow()) return false;
            var who = CommandWho() ?? _engine.Player;
            var slots = LegalIntentService.ResponseActivationSlots(_engine, who);
            for (var i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.Kind != kind || s.Index != index) continue;
                CardInstance occupant = null;
                if (kind == RulesZoneKind.SpellTrap && index >= 0 &&
                    index < who.SpellTrapZones.Length)
                    occupant = who.SpellTrapZones[index].Occupant;
                else if (kind == RulesZoneKind.Monster && index >= 0 &&
                         index < who.MonsterZones.Length)
                    occupant = who.MonsterZones[index].Occupant;
                return occupant != null && TryActivateResponseCard(occupant);
            }

            return false;
        }

        bool TryActivateResponseCard(CardInstance card)
        {
            if (!IsLocalResponseWindow() || card == null) return false;
            var pr = _engine.PendingResponse;
            if (pr.LegalCards == null ||
                !pr.LegalCards.Exists(c => c != null &&
                                           (c == card || c.InstanceId == card.InstanceId)))
                return false;

            var who = CommandWho() ?? _engine.Player;
            if (who != null && who.TryFindSpellTrap(card, out _))
                _selectedSpellTrap = card;
            else if (who != null && who.Hand != null && who.Hand.Contains(card))
                _selectedHand = card;
            else
                _selectedField = card;
            DoActivate();
            return true;
        }

        static void AttachResponseBlink(GameObject go)
        {
            if (go == null) return;
            var img = go.GetComponent<Image>();
            if (img == null) return;
            var blink = go.GetComponent<ResponseZoneBlink>() ?? go.AddComponent<ResponseZoneBlink>();
            blink.Bind(img, LegalIntentService.ResponseGlowColor);
        }

        void ShowLegalZoneHighlights(CardInstance card)
        {
            var ix = _arSpace?.Interaction;
            if (card == null || !_awaitingZonePick || _engine == null)
            {
                ix?.ClearLegalPlacements();
                return;
            }

            var who = CommandWho() ?? _engine.Player;
            var slots = WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive
                ? OcgZoneSlots(card, _zonePickAsSet)
                : LegalIntentService.LegalSlotsForAction(_engine, who, card, _zonePickAsSet);
            ix?.ShowLegalPlacements(slots);
            RefreshZonePicker();
        }

        void ClearLegalZoneHighlights()
        {
            _arSpace?.Interaction?.ClearLegalPlacements();
            RefreshZonePicker();
        }

        void BuildZonePicker(Transform root, float x0, float y0, float x1, float y1)
        {
            _zonePicker = CreateGlassBar(root, "ZonePicker", x0, y0, x1, y1);
            _zonePicker.GetComponent<Image>().color = new Color(0.03f, 0.05f, 0.07f, 0.28f);
            _zonePickerHint = CreateText(_zonePicker, "Hint", 14, TextAnchor.MiddleCenter, FontStyle.Bold);
            _zonePickerHint.color = DuelystUi.Cyan;
            Place(_zonePickerHint.rectTransform, 0.04f, 0.08f, 0.96f, 0.92f);
            _zonePickerHint.text = "Tap a highlighted zone";
            _zonePicker.gameObject.SetActive(false);
        }

        void RefreshZonePicker()
        {
            if (_zonePicker == null) return;
            var card = _selectedHand;
            if (!_awaitingZonePick || card == null || _engine == null)
            {
                _zonePicker.gameObject.SetActive(false);
                return;
            }

            _zonePicker.gameObject.SetActive(true);
            _zonePicker.SetAsLastSibling();
            var act = _zonePickAsSet ? "Set" : "Summon";
            if (_zonePickerHint != null)
                _zonePickerHint.text = $"{card.Name} — {act}: tap a highlighted zone";
        }

        bool IsLegalPickSlot(RulesZoneKind kind, int index)
        {
            if (!_awaitingZonePick || _selectedHand == null || _engine == null) return false;
            var who = CommandWho() ?? _engine.Player;
            var slots = LegalIntentService.LegalSlotsForAction(_engine, who, _selectedHand, _zonePickAsSet);
            for (var i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.Kind != kind) continue;
                if (kind == RulesZoneKind.FieldSpell ||
                    kind == RulesZoneKind.PendulumLeft ||
                    kind == RulesZoneKind.PendulumRight)
                    return true;
                if (s.Index == index) return true;
            }

            return false;
        }

        void CreateEmptyZoneSlot(Transform row, RulesZoneKind kind, int index)
        {
            var go = new GameObject("Empty_" + kind + index,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(row, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = kind == RulesZoneKind.Monster
                ? new Color(0.18f, 0.72f, 0.95f, 0.42f)
                : new Color(0.28f, 0.85f, 0.45f, 0.42f);
            img.raycastTarget = true;
            var le = go.GetComponent<LayoutElement>();
            if (kind == RulesZoneKind.Monster)
            {
                le.preferredWidth = 86;
                le.preferredHeight = 118;
                le.minWidth = 70;
                le.minHeight = 96;
            }
            else
            {
                le.preferredWidth = 78;
                le.preferredHeight = 108;
                le.minWidth = 64;
                le.minHeight = 90;
            }

            var label = SlotLabel(new LegalIntentService.LegalSlot { Kind = kind, Index = index });
            var t = CreateText(go.transform, "L", 13, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            t.text = label;
            t.color = Color.white;
            Stretch(t.rectTransform);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var k = kind;
            var z = index;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                OnArEmptyZoneTapped(k, z);
            });
        }

        static string SlotLabel(LegalIntentService.LegalSlot s) => s.Kind switch
        {
            RulesZoneKind.Monster => "M" + (s.Index + 1),
            RulesZoneKind.SpellTrap => "ST" + (s.Index + 1),
            RulesZoneKind.FieldSpell => "FIELD",
            RulesZoneKind.PendulumLeft => "P-L",
            RulesZoneKind.PendulumRight => "P-R",
            _ => "?"
        };

        void BeginZonePick(CardInstance card, bool asSet)
        {
            if (card == null || _engine == null) return;
            var who = CommandWho() ?? _engine.Player;
            var filtered = LegalIntentService.LegalSlotsForAction(_engine, who, card, asSet);
            var wantMon = card.Def != null && card.Def.IsMonster &&
                          !(card.Def.IsSpell || card.Def.IsTrap);

            if (filtered.Count == 0)
            {
                if (wantMon) DoSummon(asSet);
                else DoSetST();
                return;
            }

            _awaitingZonePick = true;
            _zonePickAsSet = asSet;
            HoldHand(card);
            CloseInspectAfterPlay();
            ShowLegalZoneHighlights(card);
            RefreshZonePicker();
            Refresh();
            var kindName = wantMon ? "Monster Zone" : "Spell/Trap Zone";
            var act = asSet ? "Set" : "Summon";
            if (_status != null)
                _status.text = $"{card.Name} — {act}: tap a highlighted {kindName} on your Duel Disk";
            _engine.Log($"{card.Name}: {act} — choose a highlighted {kindName} on your Duel Disk.");
        }

        void OnArEmptyZoneTapped(RulesZoneKind kind, int index)
        {
            if (_engine == null || _engine.GameOver) return;
            if (TryActivateResponseAtZone(kind, index))
                return;
            if (!_awaitingZonePick)
            {
                if (_status != null)
                    _status.text = "Choose Summon or Set on the card first.";
                return;
            }

            var card = _selectedHand ?? _inspectCard;
            if (card == null)
            {
                if (_status != null)
                    _status.text = "Select a card in hand first.";
                return;
            }

            var who = CommandWho() ?? _engine.Player;
            var verdict = _engine.ValidatePlacement(who, card, kind, index, _zonePickAsSet);
            if (!verdict.Legal)
            {
                _engine.Log(verdict.Reason ?? "Not a legal zone for this play.");
                return;
            }

            PlaceInSlot(card, new LegalIntentService.LegalSlot { Kind = kind, Index = index },
                _zonePickAsSet);
        }

        List<CardAction> CollectMonsterPlayActionsForZone(CardInstance card, int zone)
        {
            var list = new List<CardAction>();
            var who = CommandWho() ?? _engine.Player;
            if (_engine.ValidatePlacement(who, card, RulesZoneKind.Monster, zone, false).Legal)
                list.Add(new CardAction
                {
                    Label = "Summon ATK",
                    Color = GbaTheme.CmdSummon,
                    Invoke = () => PlaceInSlot(card,
                        new LegalIntentService.LegalSlot { Kind = RulesZoneKind.Monster, Index = zone },
                        false)
                });
            if (_engine.ValidatePlacement(who, card, RulesZoneKind.Monster, zone, true).Legal)
                list.Add(new CardAction
                {
                    Label = "Set Face-Down",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () => PlaceInSlot(card,
                        new LegalIntentService.LegalSlot { Kind = RulesZoneKind.Monster, Index = zone },
                        true)
                });
            return list;
        }

        List<CardAction> CollectSpellTrapPlayActionsForZone(CardInstance card, int zone)
        {
            var list = new List<CardAction>();
            var who = CommandWho() ?? _engine.Player;
            if (_engine.CanActivateSpellTrap(who, card, fromHand: true) &&
                _engine.ValidatePlacement(who, card, RulesZoneKind.SpellTrap, zone, false).Legal)
                list.Add(new CardAction
                {
                    Label = "Activate",
                    Color = GbaTheme.CmdSafe,
                    Invoke = () =>
                    {
                        SelectHand(card);
                        _pendingZoneIndex = zone;
                        DoActivate();
                    }
                });
            if (_engine.ValidatePlacement(who, card, RulesZoneKind.SpellTrap, zone, true).Legal)
                list.Add(new CardAction
                {
                    Label = "Set Face-Down",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () => PlaceInSlot(card,
                        new LegalIntentService.LegalSlot { Kind = RulesZoneKind.SpellTrap, Index = zone },
                        true)
                });
            return list;
        }

        void PlaceInSlot(CardInstance card, LegalIntentService.LegalSlot slot, bool asSet)
        {
            HoldHand(card);
            _pendingZoneIndex = slot.Index;
            _pendingZoneKind = slot.Kind;
            if (slot.Kind == RulesZoneKind.PendulumLeft || slot.Kind == RulesZoneKind.PendulumRight)
            {
                var who = CommandWho() ?? _engine.Player;
                if (_engine.TryPlacePendulum(who, card, slot.Kind == RulesZoneKind.PendulumLeft))
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                }

                RefreshNow();
            }
            else if (slot.Kind == RulesZoneKind.Monster)
                DoSummon(asSet);
            else if (slot.Kind == RulesZoneKind.FieldSpell)
            {
                var r = Cmd(DuelIntent.Of(DuelIntentKind.Activate, card));
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                }
                RefreshNow();
            }
            else
                DoSetST();
        }

        void DoSummon(bool asSet)
        {
            if (_engine.GameOver) return;
            if (_selectedHand == null)
            {
                _engine.Log("Tap a monster in hand, or announce \"I summon …\".");
                return;
            }

            var kind = asSet ? DuelIntentKind.SetMonsterDef : DuelIntentKind.NormalSummonAtk;
            var r = Cmd(DuelIntent.Of(kind, _selectedHand, zoneIndex: _pendingZoneIndex));
            if (r.Ok)
            {
                ClearCardSelection();
                CloseInspectAfterPlay();
            }

            // Engine.Notify already marks dirty; force same-frame paint for snappy feel
            RefreshNow();
        }

        void DoSetST()
        {
            if (_engine.GameOver || _selectedHand == null) return;
            var r = Cmd(DuelIntent.Of(DuelIntentKind.SetSpellTrap, _selectedHand,
                zoneIndex: _pendingZoneIndex));
            if (r.Ok)
            {
                ClearCardSelection();
                CloseInspectAfterPlay();
            }

            RefreshNow();
        }

        void DoActivate()
        {
            if (_engine.GameOver || _engine.IsAwaitingEffectTarget) return;

            // Face-up monster ignition (Abyss Soldier, …)
            if (_selectedField != null && _selectedField.Def != null && _selectedField.Def.IsMonster)
            {
                var r = Cmd(DuelIntent.Of(DuelIntentKind.Activate, _selectedField));
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                    RefreshNow();
                    MaybeRunAi();
                    return;
                }
            }

            // Field Set S/T response (Mirror Force, Trap Hole, …)
            if (_selectedSpellTrap != null)
            {
                var r = Cmd(DuelIntent.Of(DuelIntentKind.Activate, _selectedSpellTrap));
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                    RefreshNow();
                    MaybeRunAi();
                    return;
                }
            }

            // Hand: Normal Spells on your turn OR hand Quick Effects in response windows
            // (Kuriboh during Damage Calculation). Previously gated with !IsAwaitingResponse,
            // which blocked Kuriboh entirely while the UI still offered Activate.
            if (_selectedHand != null)
            {
                var r = Cmd(DuelIntent.Of(DuelIntentKind.Activate, _selectedHand));
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                    RefreshNow();
                    MaybeRunAi();
                    return;
                }
            }

            if (_announceFeedback != null)
            {
                _announceFeedback.text = "Cannot activate that card right now.";
                _announceFeedback.color = WrldzTheme.Danger;
            }

            RefreshNow();
        }

        void DoCancelTarget()
        {
            if (_engine == null) return;

            // Attack target picking is a UI-only chooser; it does not create a
            // PendingActivation, so the normal effect-target cancel guard must
            // not swallow this button.
            if (_attackPickerAttacker != null)
            {
                _attackPickerAttacker = null;
                _attackTargets.Clear();
                _selectedAttacker = null;
                _selectedField = null;
                DismissInspectQuiet();
                Refresh();
                return;
            }
            if (!_engine.IsAwaitingEffectTarget) return;

            Cmd(DuelIntent.Of(DuelIntentKind.CancelTarget));
            ClearCardSelection();
            Refresh();
        }

        void DoPassResponse()
        {
            if (_engine == null || !_engine.IsAwaitingPlayerResponse) return;
            FreeUiKit.PlayClick();
            Cmd(DuelIntent.Of(DuelIntentKind.PassResponse));
            ClearCardSelection();
            Refresh();
            MaybeRunAi();
        }

        void DoFlip()
        {
            if (_selectedField == null)
            {
                _engine.Log("Tap a face-down Defense monster, or announce \"Flip …\".");
                return;
            }

            var r = Cmd(DuelIntent.Of(DuelIntentKind.FlipSummon, _selectedField));
            if (r.Ok)
            {
                ClearCardSelection();
                CloseInspectAfterPlay();
            }
            RefreshNow(); // position flip must paint this frame
        }

        void DoChangePos()
        {
            if (_selectedField == null)
            {
                _engine.Log("Tap a face-up monster, or announce position change.");
                return;
            }

            var r = Cmd(DuelIntent.Of(DuelIntentKind.ChangePosition, _selectedField));
            if (r.Ok)
            {
                ClearCardSelection();
                CloseInspectAfterPlay();
            }
            RefreshNow(); // DEF/ATK pose + badge update immediately
        }

        void DoBattle()
        {
            if (_engine == null || _engine.GameOver) return;
            ClearCardSelection();
            var r = Cmd(DuelIntent.Of(DuelIntentKind.EnterBattlePhase));
            if (!r.Ok && _announceFeedback != null)
            {
                _announceFeedback.text = r.Message ?? "Cannot enter Battle Phase.";
                _announceFeedback.color = WrldzTheme.Danger;
            }
            else if (r.Ok)
                FreeUiKit.PlayConfirm();
            Refresh();
        }

        void DoMain2()
        {
            if (_engine == null || _engine.GameOver) return;
            ClearCardSelection();
            var r = Cmd(DuelIntent.Of(DuelIntentKind.EnterMainPhase2));
            if (!r.Ok && _announceFeedback != null)
            {
                _announceFeedback.text = r.Message ?? "Cannot enter Main Phase 2.";
                _announceFeedback.color = WrldzTheme.Danger;
            }
            else if (r.Ok)
                FreeUiKit.PlayConfirm();
            Refresh();
        }

        void DoDirect()
        {
            if (_selectedAttacker == null)
            {
                _engine.Log("Tap your attacker, then choose Direct on the card.");
                return;
            }

            // Official: cannot direct if opponent controls a monster — unless a continuous
            // grant (MK-3 while Umi) applies. Player always picks the target, even if only 1.
            var who = CommandWho() ?? _engine.Player;
            var enemy = EnemyOf(who);
            CardInstance target = null;
            if (enemy != null && enemy.MonsterCount > 0 &&
                !_engine.CanAttackDirectly(who, _selectedAttacker))
            {
                var targets = new List<CardInstance>();
                foreach (var m in enemy.MonstersOnField())
                    if (m != null) targets.Add(m);
                if (targets.Count > 0)
                {
                    OpenAttackTargetChooser(_selectedAttacker, targets);
                    return;
                }

                _engine.Log("Cannot attack directly — tap an enemy monster to attack.");
                return;
            }

            _engine.TryAttack(who, _selectedAttacker, target);
            ClearCardSelection();
            Refresh();
            MaybeRunAi();
        }

        void DoEndTurn()
        {
            if (_engine.GameOver || (_aiRunning && IsAiOpponent)) return;
            ClearCardSelection();
            var r = Cmd(DuelIntent.Of(DuelIntentKind.EndTurn));
            Refresh();
            if (r.Ok)
                StartOpponentTurnIfNeeded();
        }

        void DoRestart()
        {
            var acc = AppSession.Ensure()?.Account;
            if (acc != null && acc.deactivated)
            {
                LeaveDuelToMap();
                return;
            }

            if (_aiRoutine != null)
            {
                StopCoroutine(_aiRoutine);
                _aiRoutine = null;
            }

            _aiRunning = false;
            ClearCardSelection();
            _logLines.Clear();
            if (_log != null) _log.text = "";
            if (_overlay != null) _overlay.SetActive(false);
            if (_onRestart != null) _onRestart.Invoke();
            else _engine.RestartDuel();
        }

        /// <summary>Exit duel: Overworld (home), or Boot when lab Test Duel mode.</summary>
        void LeaveDuelToMap()
        {
            if (_aiRoutine != null)
            {
                StopCoroutine(_aiRoutine);
                _aiRoutine = null;
            }

            _aiRunning = false;
            // Anime: fold disks before leaving AR duel space
            _arSpace?.RetractDisks();
            var session = WRLDZ.Core.AppSession.Instance ?? WRLDZ.Core.AppSession.Ensure();
            Debug.Log(session.TestDuelMode
                ? "[WRLDZ] Leaving lab test duel → Boot"
                : "[WRLDZ] Leaving duel → Overworld (home)");
            session.LeaveDuel();
        }

        void MaybeRunAi()
        {
            if (_engine != null && _engine.OpeningSequenceActive) return;
            // Hotseat PvP: no AI — just refresh so P2 controls unlock
            if (!IsAiOpponent)
            {
                Refresh();
                return;
            }
            // After attacks that might have ended the player's turn incorrectly — only if it's opp turn
            StartOpponentTurnIfNeeded();
        }

        void StartOpponentTurnIfNeeded()
        {
            if (_engine == null || _engine.GameOver || _aiRunning) return;
            if (_engine.OpeningSequenceActive) return;
            if (_engine.TurnPlayer != _engine.Opponent) return;
            // NearbyPeer / LocalPass: human plays both sides
            if (!IsAiOpponent)
            {
                Refresh();
                return;
            }

            _aiRunning = true; // set before StartCoroutine to avoid double-start from Update()
            if (_aiRoutine != null)
                StopCoroutine(_aiRoutine);
            _aiRoutine = StartCoroutine(OpponentTurnCoroutine());
        }

        IEnumerator OpponentTurnCoroutine()
        {
            UpdateButtonStates(); // lock UI
            if (_hint != null)
                _hint.text = "OPPONENT TURN — AI is playing…";
            Refresh();

            // Stepped AI; waits on flip/read holds between actions so the board is readable
            yield return StartCoroutine(SimpleAi.TakeTurnRoutine(_engine, () =>
            {
                try { Refresh(); }
                catch (Exception ex) { Debug.LogError("[WRLDZ] AI step refresh: " + ex); }
            }));

            // Drain remaining presentation hold after last AI action
            while (DuelPresentationPacer.IsHolding)
            {
                UpdatePresentationHoldUi();
                yield return null;
            }

            _aiRunning = false;
            _aiRoutine = null;
            Refresh();
        }

        void Update()
        {
            if (_engine == null || _engine.GameOver) return;

            // Block AI / combat recovery during pre-duel cinematic
            if (_engine.OpeningSequenceActive)
            {
                UpdatePresentationHoldUi();
                return;
            }

            UpdatePresentationHoldUi();

            // Unstick hung attack / damage-calc so defense-hold LP damage always applies
            if (!_engine.IsAwaitingResponse && !_engine.IsAwaitingEffectTarget)
                _engine.RecoverStuckCombat();

            // Safety net: opponent turn but AI not running (PvAI only)
            if (IsAiOpponent && !_aiRunning && _engine.TurnPlayer == _engine.Opponent)
                StartOpponentTurnIfNeeded();
        }
    }

    /// <summary>Pulse a 2D field card so only the local responder sees a live Set.</summary>
    sealed class ResponseZoneBlink : MonoBehaviour
    {
        Image _img;
        Color _base;

        public void Bind(Image img, Color baseCol)
        {
            _img = img;
            _base = baseCol;
        }

        void LateUpdate()
        {
            if (_img == null) return;
            var pulse = 0.28f + 0.42f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.2f));
            _img.color = Color.Lerp(Color.white, _base, pulse);
        }
    }
}
