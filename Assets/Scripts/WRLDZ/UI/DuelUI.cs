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
using WRLDZ.Duel.TextEffects;
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
        // Leave / restart confirm (owner rule 2026-09-23 #3: leaving an unfinished duel is a forfeit loss)
        GameObject _leaveConfirm;
        Text _leaveConfirmTitle, _leaveConfirmBody;
        Action _leaveConfirmAction;
        // UI-side claim-once token: bumps per duel (Bind / restart). ProgressionService also claims
        // once per ArDuelMatchConfig.DuelResultId; this keeps the overlay from asking twice.
        int _duelSerial;
        int _rewardsClaimedSerial = -1;
        string _lastRewardLine = "";
        // Countdown text caches (format only when the shown tenth changes)
        int _respTextKey = int.MinValue;
        int _holdTextTenths = int.MinValue;
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
        /// <summary>The match this duel plays (set by DuelBootstrap before Bind): the pre-duel setup's choices.</summary>
        public WRLDZ.Core.ArDuelMatchConfig Match { get; set; }
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
        GameObject _vsSting;
        Text _vsYou;
        Text _vsOpp;
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
        /// <summary>Standby Phase held for Blast Juggler → Main Phase 1 (thumb island, like CANCEL / TRIBUTES).</summary>
        Button _btnMain1;

        Transform _targetRow;
        Text _targetPrompt;
        Text _targetHint;
        GameObject _targetPanel;
        GameObject _targetDim;
        Button _targetCancelBtn;
        readonly List<CardInstance> _attackTargets = new();
        CardInstance _attackPickerAttacker;

        /// <summary>
        /// Local 5s response warning. ACTIVATE/PASS chips are gone — tap a blinking
        /// legal zone, or let the clock pass. Opponent never sees this.
        /// </summary>
        GameObject _responseTray;
        Transform _responseBtnRow;
        Text _responsePrompt;
        Image _responseWarn;
        Text _responseCount;

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
        /// <summary>Hand inspect "Mark tributes" — field taps tribute. Default field tap is inspect.</summary>
        bool _markingTributes;
        /// <summary>
        /// The marked Tributes pay the selected hand monster's own Special Summon procedure (the Toons),
        /// counted by <see cref="ProcedureTributesRequired"/> instead of the Tribute Summon Level rule.
        /// </summary>
        bool _markingForProcedure;
        bool _zonePickAsSet;
        int _pendingZoneIndex = -1;
        RulesZoneKind _pendingZoneKind = RulesZoneKind.Monster;
        /// <summary>Monster Reborn-family: waiting for a Monster Zone, then ATK/DEF if unlocked.</summary>
        bool _awaitingSsZonePick;
        bool _ssPositionLocked;
        int _pendingSsZone = -1;
        CardInstance _pendingSsTarget;
        BattlePosition _pendingSsPos;
        /// <summary>
        /// The pending Special Summon is the hand card's own summon procedure (Gate Guardian, Great Moth, the
        /// Toons), sent as <see cref="DuelIntentKind.SpecialSummon"/>, not an effect's target.
        /// </summary>
        bool _ssByProcedure;
        static Font _font;

        bool _aiRunning;
        Coroutine _aiRoutine;
        /// <summary>Coalesce engine+UI refresh to one rebuild per frame (field lag fix).</summary>
        bool _fieldDirty;

        // ── Spell/Trap beat (ActivationBeat): result gate, queued phase advance, name banner ──
        /// <summary>A refresh skipped result visuals (LP / 2D field) — refresh again when the gate opens.</summary>
        bool _refreshOnResultGate;
        /// <summary>Game over arrived during a Spell/Trap beat: overlay + disk retract wait for the beat.</summary>
        bool _gameOverPending;
        /// <summary>2D hand cards shown last rebuild (result gate hides new arrivals — Pot of Greed draws).</summary>
        readonly HashSet<int> _handShownIds = new();
        DuelistState _handShownOwner;
        /// <summary>Phase / turn advance the player asked for while a Spell/Trap beat was playing.</summary>
        DuelIntentKind _queuedAdvance = DuelIntentKind.None;
        int _queuedAdvanceTurn;
        Transform _uiRoot;
        CanvasGroup _beatBannerGroup;
        Image _beatBannerBg;
        Text _beatBannerKind;
        Text _beatBannerName;
        int _beatBannerSerial = -1;
        int _beatBannerDepth;
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

        /// <summary>
        /// Zone tag (M1–M5, ST1–ST5, FIELD) of a face-down card on a side the local UI does not
        /// command — official play shows it only as a card back in that zone. Null when the viewer
        /// may read the card: face-up, their own Set, or not on the field. Matches by InstanceId
        /// too, so a stale presentation ref still hides.
        /// </summary>
        string HiddenSetZone(CardInstance card)
        {
            if (card == null || _engine == null) return null;
            bool Same(CardInstance o) =>
                o != null && (o == card || (card.InstanceId != 0 && o.InstanceId == card.InstanceId));
            foreach (var side in new[] { _engine.Player, _engine.Opponent })
            {
                if (side == null || IsMySide(side)) continue;
                for (var i = 0; i < side.MonsterZones.Length; i++)
                    if (Same(side.MonsterZones[i].Occupant))
                        return side.MonsterZones[i].Occupant.FaceUp ? null : "M" + (i + 1);
                for (var i = 0; i < side.SpellTrapZones.Length; i++)
                    if (Same(side.SpellTrapZones[i].Occupant))
                        return side.SpellTrapZones[i].Occupant.FaceUp ? null : "ST" + (i + 1);
                var field = side.FieldSpellZone?.Occupant;
                if (Same(field)) return field.FaceUp ? null : "FIELD";
            }

            return null;
        }

        /// <summary>Card name for status lines; an opponent's Set reads "OPP · SET M2".</summary>
        string ViewerSafeName(CardInstance card)
        {
            var zone = HiddenSetZone(card);
            return zone != null ? $"OPP · SET {zone}" : card?.Name;
        }

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
            _duelSerial++;
            engine.OnLog += AppendLog;
            engine.OnStateChanged += MarkFieldDirty;
            engine.OnGameOver += OnGameOverEvent;
            EnsureEventSystem();
            BuildUi();
            ShowVsSting();

            // Structured review log (visible panel + JSONL for AI learning)
            EnsureReviewLog();

            _responseClock = UiFoundation.GetOrAdd<TimedResponseClock>(gameObject);
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
                uiRoot,
                Match);
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
            _engine.OnGameOver -= OnGameOverEvent;
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

        /// <summary>
        /// Rejection toast (illegal zone, illegal target). Uses the floating HUD clock so it
        /// fades after a few seconds instead of sitting on the status line.
        /// </summary>
        void ShowFlashStatus(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (_floatHud != null)
                _floatHud.SetStatus(msg);
            else if (_status != null)
                _status.text = msg;
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

            // Response countdown goes into the single status line (timer UI is hidden).
            // Re-format only when the shown tenth / wording changes (not every frame).
            if (_status != null && toImpact > 0.05f)
            {
                var tenths = Mathf.RoundToInt(toImpact * 10f);
                var late = progress >= 0.85f;
                var key = (tenths * 2 + (late ? 1 : 0)) * 8 + (int)timing % 8;
                if (key != _respTextKey)
                {
                    _respTextKey = key;
                    var secs = (tenths / 10f).ToString("0.0");
                    if (late)
                        _status.text = secs + "s — last chance!";
                    else if (timing == ResponseTiming.DamageCalculation)
                        _status.text = "Damage Calc " + secs + "s — activate Kuriboh now";
                    else if (timing == ResponseTiming.MonsterSummoned)
                        _status.text = "Summon " + secs + "s — tap a blinking zone to activate";
                    else
                        _status.text = "Attack " + secs + "s — tap a blinking zone to activate";
                }

                _status.color = progress < 0.65f
                    ? new Color(1f, 0.85f, 0.4f, 1f)
                    : new Color(1f, 0.45f, 0.4f, 1f);
            }
            else if (_status != null)
            {
                _respTextKey = int.MinValue;
                _status.color = new Color(0.82f, 0.86f, 0.90f, 0.95f);
            }

            if (_timerFill != null && _timerFill.rectTransform != null)
            {
                _timerFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            }

            _arSpace?.SetAttackCharge(playerSide: false, progress);
            _arSpace?.SetAttackCharge(playerSide: true, Mathf.Max(0.15f, 1f - progress * 0.5f));
            // Pulse only on attack-declared windows (not summon / damage calc)
            if (progress > 0.85f && timing == ResponseTiming.AttackDeclared)
                _arSpace?.PulseAttack();
            RebuildResponseTray();
        }

        /// <summary>
        /// Pop-up lines Mr. Referobot already says out loud (card activations, the opponent's Summons / Sets /
        /// Flip Summons, whose turn it is, the duel result). The status toast stays quiet for these; lines that
        /// carry something he does not say (why a move is illegal, response countdowns, control hints) still show.
        /// </summary>
        /// <summary>Last presentation-hold reason written to the hint line (cleared when Referobot takes over).</summary>
        string _holdHintShown;

        static bool ReferobotNarrates(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            return line.Contains(" activates") || line.Contains("hologram rising") ||
                   line.Contains("watch it resolve") || line.Contains("watch the field") ||
                   line.StartsWith("Opponent Summons") || line.StartsWith("Opponent Sets") ||
                   line.StartsWith("Flip Summon") || line.StartsWith("Opponent\u2019s turn") ||
                   line.StartsWith("Opponent's turn") || line.StartsWith("Victory!") || line.StartsWith("Defeat.");
        }

        void UpdatePresentationHoldUi()
        {
            if (_engine == null || !DuelPresentationPacer.IsHolding) return;
            var reason = DuelPresentationPacer.Reason;
            if (_hint != null && !string.IsNullOrEmpty(reason))
            {
                if (!ReferobotNarrates(reason))
                {
                    _hint.text = reason;
                    _holdHintShown = reason;
                }
                else if (_hint.text == _holdHintShown)
                    _hint.text = ""; // an earlier hold's line must not linger under Referobot's call
            }
            if (_timerLabel != null && !_engine.IsAwaitingPlayerResponse)
            {
                var left = DuelPresentationPacer.SecondsRemaining;
                if (left > 0.05f)
                {
                    var tenths = Mathf.RoundToInt(left * 10f);
                    if (tenths != _holdTextTenths)
                    {
                        _holdTextTenths = tenths;
                        _timerLabel.text = "Read " + (tenths / 10f).ToString("0.0") + "s";
                    }
                }
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
            drag.OnOppGlanceGyTapped -= OnArOppGlanceGyTapped;
            drag.OnOppGlanceGyTapped += OnArOppGlanceGyTapped;
        }

        /// <summary>
        /// Side the AR opponent glance paints: the enemy of the seat that is choosing (the activating
        /// controller in a target window, else the seat the UI commands, i.e. the attacker in the
        /// attack picker). Vs AI it is always the Opponent.
        /// </summary>
        DuelistState ArGlanceSide()
        {
            if (_engine == null) return null;
            if (IsAiOpponent) return _engine.Opponent;
            var chooser = (_engine.IsAwaitingEffectTarget ? _engine.PendingActivation?.Controller : null)
                          ?? CommandWho() ?? _engine.Player;
            return EnemyOf(chooser) ?? _engine.Opponent;
        }

        /// <summary>The glance's GY count opens the GY of the side the glance shows.</summary>
        void OnArOppGlanceGyTapped()
        {
            if (_engine == null) return;
            OpenGraveyard(ArGlanceSide(), playerSide: false);
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
                ShowFlashStatus($"Not a legal target: {ViewerSafeName(card)}");
                return;
            }

            if (_attackPickerAttacker != null && _engine.Phase == DuelPhase.Battle)
            {
                if (_attackTargets.Exists(c => c != null && c.InstanceId == card.InstanceId))
                {
                    DeclareAttackOn(_attackPickerAttacker, card);
                    return;
                }
                ShowFlashStatus("That opponent monster is not an attack target.");
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
                case ArDiskPhaseKind.Main1:
                    DoMain1();
                    break;
                case ArDiskPhaseKind.Main2:
                    DoMain2();
                    break;
                case ArDiskPhaseKind.End:
                    DoEndTurn();
                    break;
                case ArDiskPhaseKind.Shuffle:
                    DoShuffleHand();
                    break;
                case ArDiskPhaseKind.Restart:
                    DoRestart();
                    break;
                case ArDiskPhaseKind.Map:
                    RequestLeaveDuel();
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
            // onPick: effect-target picks route to the engine (like OnGraveyardCardPicked); otherwise inspect.
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
                    ShowFlashStatus($"Not a legal target: {card.Name}");
                    return;
                }
                OpenInspect(card, showFace: card.FaceUp, null);
            });
        }

        /// <summary>Tap a floating hand holo — select it; drag-to-disk is the other play path.</summary>
        /// <summary>The AR hand is carrying a card to a zone: taps wait for it.</summary>
        bool ArGestureBusy => _arSpace?.Interaction?.Drag != null && _arSpace.Interaction.Drag.GestureBusy;

        /// <summary>
        /// Zone-pick play through the AR hand: the hand carries the card to the disk and <paramref name="commit"/>
        /// runs at contact. False = no AR hand for this card; commit directly.
        /// </summary>
        bool TryArGesture(CardInstance card, RulesZoneKind kind, int index, bool faceDown, Func<bool> commit)
        {
            var ix = _arSpace?.Interaction;
            if (ix == null || card == null) return false;
            if (!ix.TryPlaceFromHandWithGesture(card, kind, index, faceDown, commit,
                    ok => { if (!ok) RefreshNow(); }))
                return false;
            // the sheet closes now so the hand is in view; the selection clears when the commit lands
            CloseInspectAfterPlay();
            return true;
        }

        void OnArHandCardTapped(CardInstance card)
        {
            if (card == null || _engine == null) return;
            if (ArGestureBusy) return;
            if (TrySelectPendingTarget(card))
                return;
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

                ShowFlashStatus($"Not a legal target: {ViewerSafeName(card)}");
                return;
            }

            if (TryActivateUniqueSummonResponse(card))
                return;
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

                    // After Summon/Set on inspect: tapping an occupied pad plays into
                    // that slot (tribute the occupant when the engine allows).
                    if (zi >= 0 && _selectedHand != null &&
                        commander.Hand != null && commander.Hand.Contains(_selectedHand) &&
                        !_markingTributes &&
                        (_awaitingSsZonePick || _awaitingZonePick) &&
                        IsLegalPickSlot(RulesZoneKind.Monster, zi))
                    {
                        OnArEmptyZoneTapped(RulesZoneKind.Monster, zi);
                        return;
                    }

                    // Explicit Mark tributes — tap field monsters to mark, then a disk slot.
                    if (FieldTapMarksTribute())
                    {
                        MarkTributeTapped(commander, card);
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
                if (FieldTapMarksTribute())
                {
                    ToggleMarkedTribute(CommandWho() ?? _engine.Player, card);
                    RefreshInspectForSelectedHand();
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
                if (BeatBlocksDeclaration("attack")) return;
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
            _uiRoot = root;

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
            if (_arSpace != null && _floatHud != null)
                _floatHud.SetScreenScoreVisible(false);

            if (ArPresentationTarget.IsSalvageOst)
            {
                // Combiner only sees the letterboxed optical window — hide phone chrome.
                if (_floatHud != null) _floatHud.gameObject.SetActive(false);
            }

            _companion = ArCompanionPhoneHud.Build(root, RequestLeaveDuel, () =>
            {
                if (_reviewPanel != null)
                    _reviewPanel.gameObject.SetActive(true);
            });

            _field = CreateText(root, "MetaHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _field.gameObject.SetActive(false);
            _log = CreateText(root, "LogHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _log.gameObject.SetActive(false);
            _referobot = MrReferobot.CreateCallout(root);
            HideReferobotChipOnStage();
            if (ArPresentationTarget.IsSalvageOst)
                _referobot.gameObject.SetActive(false);

            BuildResponseTray(root);

            // Fallback 2D tray — hidden while torso holos are the live hand
            _handRow = CreateDiskZone(root, "ArHandTray", 0.08f, 0.016f, 0.82f, 0.118f,
                new Color(0.03f, 0.04f, 0.06f, 0.12f));
            _handRow.gameObject.SetActive(false);
            var handImgAr = _handRow.GetComponent<Image>();
            if (handImgAr != null) handImgAr.raycastTarget = true;
            BuildHandShuffleButton(root, 0.012f, 0.016f, 0.068f, 0.118f);
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
            _btnMain1 = CreateButton(_floatHud.ContextRow, "MAIN 1", DoMain1, GbaTheme.CmdSafe);
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
            if (_arSpace != null && _floatHud != null)
                _floatHud.SetScreenScoreVisible(false);

            _field = CreateText(root, "MetaHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _field.gameObject.SetActive(false);
            _log = CreateText(root, "LogHidden", 1, TextAnchor.MiddleLeft, FontStyle.Normal);
            _log.gameObject.SetActive(false);

            _referobot = MrReferobot.CreateCallout(root);
            HideReferobotChipOnStage();

            BuildResponseTray(root);
            BuildEffectTargetPicker(root);

            // Fallback 2D tray — hidden while torso holos are the live hand
            _handRow = CreateDiskZone(root, "Hand", 0.10f, 0.016f, 0.82f, 0.118f,
                new Color(0.03f, 0.035f, 0.045f, 0.12f));
            _handRow.gameObject.SetActive(false);
            var handImgDig = _handRow.GetComponent<Image>();
            if (handImgDig != null) handImgDig.raycastTarget = true;
            BuildHandShuffleButton(root, 0.012f, 0.016f, 0.068f, 0.118f);

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
            Place(restartRow, 0.10f, 0.08f, 0.90f, 0.26f);
            AddHLayout(restartRow);
            // A finished duel has paid; Map just leaves. Restart only in practice / lab duels.
            CreateButton(restartRow, "Map", LeaveDuelToMap, GbaTheme.GoldDim);
            if (RestartAllowed)
                CreateButton(restartRow, "Restart Duel", DoRestart, GbaTheme.CmdSafe);

            HideContextualActions();
            BuildVsSting(root);
        }

        void BuildVsSting(Transform root)
        {
            _vsSting = CreatePanel(root, "VsSting", new Color(0.01f, 0.02f, 0.06f, 0.42f)).gameObject;
            Stretch(_vsSting.GetComponent<RectTransform>());
            var dim = _vsSting.GetComponent<Image>();
            if (dim != null) dim.raycastTarget = false;
            _vsSting.SetActive(false);

            var box = HubChrome.Sheet(_vsSting.transform, "Box", 0.10f, 0.38f, 0.90f, 0.64f, gold: true);
            var you = CreateText(box, "You", 22, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            you.color = DuelystUi.Cyan;
            Place(you.rectTransform, 0.04f, 0.52f, 0.42f, 0.90f);
            _vsYou = you;
            var vs = CreateText(box, "Vs", 28, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            vs.color = DuelystUi.GoldHot;
            vs.text = "VS";
            Place(vs.rectTransform, 0.38f, 0.18f, 0.62f, 0.82f);
            var opp = CreateText(box, "Opp", 22, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            opp.color = DuelystUi.Magenta;
            Place(opp.rectTransform, 0.58f, 0.52f, 0.96f, 0.90f);
            _vsOpp = opp;
            var sub = CreateText(box, "Sub", 14, TextAnchor.MiddleCenter, FontStyle.Normal);
            sub.color = DuelystUi.TextCream;
            sub.text = "IT'S TIME TO DUEL";
            Place(sub.rectTransform, 0.08f, 0.06f, 0.92f, 0.36f);
        }

        void ShowVsSting()
        {
            if (_vsSting == null || _engine == null || _engine.GameOver) return;
            if (_vsYou != null) _vsYou.text = string.IsNullOrEmpty(_engine.Player?.Name) ? "YOU" : _engine.Player.Name;
            if (_vsOpp != null)
                _vsOpp.text = string.IsNullOrEmpty(_engine.Opponent?.Name) ? "OPPONENT" : _engine.Opponent.Name;
            _vsSting.SetActive(true);
            _vsSting.transform.SetAsLastSibling();
            var box = _vsSting.transform.Find("Box");
            if (box != null) box.gameObject.SetActive(true);
            if (Application.isPlaying && isActiveAndEnabled)
                StartCoroutine(VsStingCo());
            else if (!Application.isPlaying)
                _vsSting.SetActive(false);
        }

        IEnumerator VsStingCo()
        {
            var box = _vsSting != null ? _vsSting.transform.Find("Box") : null;
            if (box != null)
                yield return MenuMotion.PopIn(box.gameObject, 0.22f);
            yield return new WaitForSecondsRealtime(0.90f);
            if (_vsSting != null)
                yield return MenuMotion.SheetOut(_vsSting, 0.18f);
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

            _btnMenu = CreateButton(dock, "Map", RequestLeaveDuel, GbaTheme.GoldDim);
            // Restart exists only in practice / lab duels (owner rule 2026-09-23 #3).
            if (RestartAllowed)
            {
                Place(_btnMenu.GetComponent<RectTransform>(), 0.04f, 0.08f, 0.56f, 0.92f);
                _btnRestartBar = CreateButton(dock, "↻", DoRestart, GbaTheme.CmdMuted);
                Place(_btnRestartBar.GetComponent<RectTransform>(), 0.60f, 0.08f, 0.96f, 0.92f);
            }
            else
                Place(_btnMenu.GetComponent<RectTransform>(), 0.04f, 0.08f, 0.96f, 0.92f);

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

        /// <summary>
        /// engine.OnGameOver fires inside the engine frame. A lethal Spell/Trap (Magic Cylinder, burn)
        /// must still be revealed, called out and resolved on screen before the duel-over overlay and
        /// the disk retract (Battle City order) — wait for the live beat, capped.
        /// </summary>
        void OnGameOverEvent()
        {
            if (_gameOverPending) return;
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                OnGameOverUi();
                return;
            }

            _gameOverPending = true;
            StartCoroutine(GameOverAfterBeatCo(_engine));
        }

        IEnumerator GameOverAfterBeatCo(DuelEngine engine)
        {
            var cap = Time.unscaledTime + SpellActivationPresentation.BeatHardCapSeconds;
            while (ActivationBeat.BlocksAdvance(out _, out _) && Time.unscaledTime < cap)
                yield return null;
            _gameOverPending = false;
            // Restarted / rebound while waiting: that duel's overlay is no longer wanted.
            if (_engine != engine || _engine == null || !_engine.GameOver) yield break;
            if (_overlay != null && _overlay.activeSelf) yield break;
            OnGameOverUi();
        }

        void OnGameOverUi()
        {
            // Anime: fold disks when the duel ends
            _arSpace?.RetractDisks();
            if (_vsSting != null) _vsSting.SetActive(false);
            if (_overlay == null) return;
            var win = _engine.Winner != null && _engine.Winner.IsPlayer;
            var draw = _engine.Winner == null;
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            var box = _overlay.transform.Find("Box");
            if (box != null && Application.isPlaying && isActiveAndEnabled)
                MenuMotion.Play(this, MenuMotion.PopIn(box.gameObject, 0.24f));
            _overlayTitle.text = draw ? "DRAW" : win ? "YOU WIN!" : "YOU LOSE…";
            _overlayTitle.color = win ? GbaTheme.GoldBright
                : draw ? Color.white
                : new Color(1f, 0.45f, 0.45f);

            // Duelist XP (Pokémon GO pace) — skip pure practice. Claimed once per duel.
            var rewardLine = ClaimDuelRewardsOnce(win);

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
                                   (RestartAllowed ? "\nPress Restart Duel for a rematch." : "\nTap Map to return.") +
                                   exportHint;
            }
        }

        /// <summary>
        /// Rewards for the duel that just ended, at most once per duel (a second game-over callback or
        /// an overlay re-show reuses the first line). ProgressionService additionally refuses a second
        /// payout for the same ArDuelMatchConfig.DuelResultId.
        /// </summary>
        string ClaimDuelRewardsOnce(bool playerWon)
        {
            if (_rewardsClaimedSerial == _duelSerial) return _lastRewardLine;
            _rewardsClaimedSerial = _duelSerial;
            _lastRewardLine = ApplyDuelProgressionRewards(playerWon) ?? "";
            return _lastRewardLine;
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

                    if (ReferobotNarrates(line))
                        line = ""; // he says it; no second copy on screen (and no status-line fallback)
                    else if (string.IsNullOrEmpty(line))
                        line = _engine.StatusLine();

                    if (_floatHud != null)
                        _floatHud.SetStatus(line);
                    else
                        _status.text = line ?? "";
                }
                // Spell/Trap result gate: LP and the 2D field show a result only after the activating
                // card has been readable (ActivationBeat) — same beat as the arena shatter / LP counter.
                var holdResults = Application.isPlaying && ActivationBeat.AnyResultPending;
                if (holdResults) _refreshOnResultGate = true;
                _handHoldArrivals = holdResults;
                if (_engine.Player != null && _engine.Opponent != null && !holdResults)
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

                // Deck / GY counts are results too (Pot of Greed draws, Raigeki's GY): same gate.
                if (!holdResults)
                    RefreshDiskBodyCounts();

                if (_engine.Player == null || _engine.Opponent == null)
                    return;

                // Legal plays for the human currently holding the device
                _legalSnap = WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive
                    ? WRLDZ.Duel.Ocg.OcgLegalActions.Build(WRLDZ.Duel.Ocg.OcgLabDuelHost.Current)
                    : LegalIntentService.Build(_engine, CommandWho() ?? _engine.Player);

                if (_engine.GameOver)
                {
                    // Via the beat wait: a lethal Spell/Trap is read before the overlay covers it.
                    if (_overlay != null && !_overlay.activeSelf && !_gameOverPending) OnGameOverEvent();
                }
                else if (_overlay != null && _overlay.activeSelf)
                    _overlay.SetActive(false);

                if (!_arCompanionMode && holdResults)
                {
                    // Digital: hand follows the engine now (the activated card left it); the 2D field
                    // keeps its pre-result state until the result beat (re-refresh in Update).
                    RebuildHand();
                    ClearFieldActionOverlay();
                }
                else if (!_arCompanionMode)
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
                        glance.GlanceSide = ArGlanceSide();
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

                if (_awaitingSsZonePick && _pendingSsTarget != null)
                    ShowLegalZoneHighlights(_pendingSsTarget);
                else if (_selectedHand != null && !_engine.IsAwaitingEffectTarget &&
                         !_engine.IsAwaitingPlayerResponse)
                    ShowLegalZoneHighlights(_selectedHand);
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
        /// Compact player-only warning + countdown. Does not cover the board.
        /// Tap blinking legal cards/zones to activate; expiry still PassResponse.
        /// </summary>
        void BuildResponseTray(Transform root)
        {
            var panel = new GameObject("ResponseWarn", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root, false);
            FloatingPanel.Place(panel.GetComponent<RectTransform>(), 0.42f, 0.78f, 0.58f, 0.90f);
            var img = panel.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            _responseTray = panel;

            var warnGo = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            warnGo.transform.SetParent(panel.transform, false);
            Place(warnGo.GetComponent<RectTransform>(), 0.18f, 0.28f, 0.82f, 1f);
            _responseWarn = warnGo.GetComponent<Image>();
            _responseWarn.sprite = ImagineAssets.FxWarningRespond() ?? UiFoundation.WhiteSprite();
            _responseWarn.preserveAspect = true;
            _responseWarn.raycastTarget = false;
            _responseWarn.color = Color.white;

            _responseCount = CreateText(panel.transform, "Count", 22, TextAnchor.MiddleCenter,
                FontStyle.Bold, title: true);
            _responseCount.color = new Color(1f, 0.92f, 0.55f, 1f);
            _responseCount.raycastTarget = false;
            Place(_responseCount.rectTransform, 0.10f, 0.00f, 0.90f, 0.32f);
            _responseCount.text = "5";

            _responsePrompt = _responseCount;
            _responseBtnRow = panel.transform;
            _responseTray.SetActive(false);
        }


        static void StyleResponseTrayButton(Button b, float minWidth, float flexibleWidth)
        {
            if (b == null) return;
            var brt = b.GetComponent<RectTransform>();
            if (brt != null)
                brt.sizeDelta = new Vector2(0f, 0f);
            var le = UiFoundation.GetOrAdd<LayoutElement>(b.gameObject);
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
            var shown = Mathf.CeilToInt(Mathf.Max(0f, secs));
            if (_responseCount != null)
                _responseCount.text = shown.ToString();
            if (_responseWarn != null)
            {
                var pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
                var c = _responseWarn.color;
                c.a = pulse;
                _responseWarn.color = c;
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
            // Honour the LayoutElement sizes CreateCardButton sets (132x184 portraits).
            // childForceExpandHeight forces flexible height onto every child, which
            // overrode flexibleHeight = 0 and stretched each card into a tall ribbon.
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = false;
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
                   kind == EffectTargetKind.CardInEitherGy ||
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

            // A Special Summon placement may already be in flight: the Attack/Defense menu
            // is open, or a Monster Zone tap is armed. The pending activation stays
            // non-null until the placement commits, so rebuilding here would
            // DismissInspectQuiet() that menu and re-raise this picker over the disk —
            // the player taps a GY monster and nothing happens (Monster Reborn).
            if (_pendingSsTarget != null)
            {
                if (_awaitingSsZonePick || (_inspect != null && _inspect.IsOpen))
                {
                    // Keep out of the way; the placement owns the screen until it
                    // commits or cancels, both of which null _pendingSsTarget.
                    _targetPanel.SetActive(false);
                    if (_targetDim != null) _targetDim.SetActive(false);
                    return;
                }

                // Orphaned placement — the menu went away without its close callback.
                // Drop it and rebuild the picker rather than hiding it forever.
                CancelSsPlacement();
            }

            // One overlay: hide inspect / GY so they cannot sit on this picker.
            DismissInspectQuiet();
            _gyBrowser?.Hide();

            if (_targetDim != null)
            {
                _targetDim.SetActive(true);
                var dimImg = _targetDim.GetComponent<Image>();
                // Never steal AR viewport / hologram taps. Dim is visual only.
                if (dimImg != null) dimImg.raycastTarget = false;
            }
            _targetPanel.SetActive(true);
            // Bring above hand / field so taps on the sheet hit the picker
            _targetPanel.transform.SetAsLastSibling();
            if (_targetDim != null)
                _targetDim.transform.SetSiblingIndex(Mathf.Max(0, _targetPanel.transform.GetSiblingIndex() - 1));

            if (_targetPrompt != null)
            {
                var src = pending.Card != null ? pending.Card.Name : "Effect";
                var prompt = string.IsNullOrEmpty(pending.Prompt)
                    ? $"Choose a target for {src}"
                    : pending.Prompt;
                _targetPrompt.text = prompt;
            }

            if (pending.AwaitingLpZeroConfirm)
            {
                if (_targetHint != null)
                    _targetHint.text = "Paying this LP cost leaves you at 0";
                var yes = CreateButton(_targetRow, "YES", () =>
                {
                    if (_engine.TryConfirmLpZeroPay())
                        Refresh();
                }, GbaTheme.CmdSafe);
                var yesLe = yes.gameObject.AddComponent<LayoutElement>();
                yesLe.minWidth = 88f;
                yesLe.minHeight = 56f;
                yesLe.flexibleWidth = 1f;
                var no = CreateButton(_targetRow, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
                var noLe = no.gameObject.AddComponent<LayoutElement>();
                noLe.minWidth = 92f;
                noLe.minHeight = 56f;
                return;
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

            // GY targets fall through to the large portrait picker below — the same
            // visual cards every other target kind uses. The browser's one-card
            // PREV/NEXT/CLOSE flow is for reading the pile, not for choosing a target.

            if (pending.TargetKind == EffectTargetKind.SpellTrapOnField ||
                pending.TargetKind == EffectTargetKind.OppFaceUpMonster ||
                pending.TargetKind == EffectTargetKind.OppFaceUpMonsterAtkLeqLp ||
                // TargetedClassics Soft: Nobleman of Crossout / Acid Trap Hole — field-glance like MST.
                pending.TargetKind == EffectTargetKind.FaceDownMonsterOnField ||
                pending.TargetKind == EffectTargetKind.AnyMonsterOnField ||
                pending.TargetKind == EffectTargetKind.FaceUpMonsterOnField ||
                pending.TargetKind == EffectTargetKind.OppMonster ||
                pending.TargetKind == EffectTargetKind.YourMonster)
            {
                // AR: the target is the hologram / glance / disk card. A covering
                // sheet + raycast dim ate those taps ("I click the target, nothing").
                if (_arCompanionMode)
                {
                    _targetPanel.SetActive(false);
                    if (_status != null)
                    {
                        _status.text = string.IsNullOrEmpty(pending.Prompt)
                            ? "Tap the target on the field"
                            : pending.Prompt;
                        _status.color = DuelystUi.GoldHot;
                    }
                    return;
                }

                // The picker's side, not _engine.Opponent: in hotseat the Opponent's seat
                // picks too, and its enemy is the Player.
                var picker = pending.Controller ?? CommandWho() ?? _engine.Player;
                var bothSides = FillFieldGlanceTargets(pending, picker);
                if (_targetHint != null)
                    _targetHint.text = bothSides
                        ? "OPP + YOUR FIELD · legal targets only — tap a card"
                        : "OPP FIELD · legal targets only — tap a card";
                var cancelSt = CreateButton(_targetRow, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
                var cleSt = cancelSt.gameObject.AddComponent<LayoutElement>();
                cleSt.minWidth = 92f;
                cleSt.minHeight = 56f;
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

            if (_targetHint != null && (pending.AwaitingRitualMonster || pending.AwaitingRitualTributes))
                _targetHint.text = pending.AwaitingRitualMonster
                    ? "Tap the Ritual Monster to Ritual Summon, or CANCEL"
                    : pending.RitualPicks.Count == 0
                        ? "Tap a card to Tribute it (hand or field), or CANCEL"
                        : "Tap the next Tribute · CANCEL starts the Tributes over";
            else if (_targetHint != null)
            {
                var n = pending.LegalTargets?.Count ?? 0;
                var where = IsGraveyardTargetKind(pending.TargetKind) ? "GY · " : "";
                _targetHint.text = n <= 0
                    ? $"{where}No legal targets — tap CANCEL"
                    : n == 1
                        ? $"{where}1 legal target — tap the card, or CANCEL"
                        : $"{where}{n} legal targets — tap a card, or CANCEL";
            }

            if (pending.LegalTargets == null) return;

            var controller = pending.Controller ?? CommandWho() ?? _engine.Player;
            var oppOfController = controller != null ? EnemyOf(controller) : _engine.Opponent;

            foreach (var t in pending.LegalTargets)
            {
                var card = t;
                var tag = "";
                if (pending.TargetKind == EffectTargetKind.MonsterInEitherGy ||
                    pending.TargetKind == EffectTargetKind.CardInEitherGy)
                {
                    if (controller != null && controller.Graveyard.Contains(card)) tag = "YOURS\n";
                    else if (oppOfController != null && oppOfController.Graveyard.Contains(card))
                        tag = "OPP\n";
                }
                else if (pending.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq ||
                         pending.TargetKind == EffectTargetKind.FieldSpellInYourDeck ||
                         pending.TargetKind == EffectTargetKind.MonsterInYourDeckFiltered ||
                         pending.TargetKind == EffectTargetKind.RitualCardInYourDeck ||
                         pending.TargetKind == EffectTargetKind.MonsterInYourDeckToSummon ||
                         pending.TargetKind == EffectTargetKind.CardInYourDeck)
                    tag = "DECK\n";
                else if (pending.TargetKind == EffectTargetKind.CardInOppHand)
                    tag = "OPP HAND\n";
                else if (pending.TargetKind == EffectTargetKind.MonsterInHandOrDeck)
                    tag = controller != null && controller.Hand.Contains(card) ? "HAND\n" : "DECK\n";
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
                else if (pending.TargetKind == EffectTargetKind.DiscardMonsterInHand ||
                         pending.TargetKind == EffectTargetKind.DiscardFromHand)
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
                         pending.TargetKind == EffectTargetKind.FaceDownMonsterOnField ||
                         pending.TargetKind == EffectTargetKind.OppFaceUpMonster ||
                         pending.TargetKind == EffectTargetKind.OppFaceUpMonsterAtkLeqLp ||
                         pending.TargetKind == EffectTargetKind.OppMonster ||
                         pending.TargetKind == EffectTargetKind.YourMonster ||
                         pending.TargetKind == EffectTargetKind.EquipAnyMonster ||
                         pending.TargetKind == EffectTargetKind.FaceUpMonsterOnField)
                {
                    // Own-field targets must read clearly (Man-Eater Bug, Raigeki Break, etc.)
                    if (controller != null && controller.TryFindMonster(card, out _))
                        tag = "YOURS\n";
                    else
                        tag = "OPP\n";
                }
                else if (pending.TargetKind == EffectTargetKind.RitualMonsterInHand)
                    tag = $"HAND · Lv{card.Level}\n";
                else if (pending.TargetKind == EffectTargetKind.RitualTribute)
                    tag = controller != null && controller.Hand.Contains(card)
                        ? $"HAND · Lv{card.Level}\n"
                        : $"FIELD · Lv{card.Level}\n";

                // An opponent's Set is only a card back in its zone (official play): no art,
                // name or ATK/DEF before the pick, just its zone so two Sets read apart.
                var hiddenZone = HiddenSetZone(card);

                // Large portrait cards — readable on phone/desktop without AR
                var pick = card;
                var go = CreateCardButton(_targetRow, pick, showFace: hiddenZone == null, tributeMark: false, () =>
                {
                    if (TryBeginSsPlacement(pick))
                    {
                        Refresh();
                        return;
                    }

                    if (_engine.TrySelectEffectTarget(pick))
                    {
                        _selectedHand = null;
                        _selectedSpellTrap = null;
                        _selectedField = null;
                    }

                    Refresh();
                }, small: false, forceCardBack: false, handSize: false, wireButtonClick: true,
                    targetPickerSize: true);

                // Green legal-target rim. This was a full-rect child Image, but uGUI draws
                // children OVER their parent — and the card face lives on the parent's own
                // Image — so it painted solid green across every portrait and only the
                // nameplate showed through. Outline is a mesh effect on the card's own
                // graphic, so it frames the art instead of covering it.
                var rimImg = go.GetComponent<Image>();
                if (rimImg != null)
                {
                    var rim = UiFoundation.GetOrAdd<Outline>(go);
                    rim.effectColor = new Color(0.15f, 0.95f, 0.45f, 0.95f);
                    rim.effectDistance = new Vector2(3f, -3f);
                    rim.useGraphicAlpha = false;
                }

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
                    if (hiddenZone != null)
                    {
                        // Shrink to fit the back's strip: Truncate would drop the zone line.
                        nameTxt.resizeTextForBestFit = true;
                        nameTxt.resizeTextMinSize = WrldzType.MinBody;
                        nameTxt.resizeTextMaxSize = nameTxt.fontSize;
                        nameTxt.text = $"OPP · SET\n{hiddenZone}";
                    }
                    else
                        nameTxt.text = tag + ShortName(card.Name) + stat;
                }
            }

            // No CANCEL tile in the card row — the panel's own CANCEL (top-right) already
            // covers it, and a text button wedged between portraits is what made this read
            // as a text menu. The cost / coin / opp-field branches above still add their own.
        }

        void UpdateButtonStates()
        {
            if (_engine?.Player == null) return;

            SyncDiskControls();

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
            // Standby Phase held for this human (Blast Juggler): the disk's left key stays MAIN 1, dim, through
            // the target pick and any response window, instead of flicking back to BATTLE.
            var standbyHeld = !WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive && _engine.StandbyPausedFor != null &&
                              _engine.StandbyPausedFor == CommandWho();

            // AI turn — unlock Pass when player may activate traps
            if (_aiRunning && IsAiOpponent && !responding && !targeting)
            {
                SetPhaseButtons(false, false, false, standbyHeld);
                HideContextualActions();
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                return;
            }

            if (targeting)
            {
                SetPhaseButtons(false, false, false, standbyHeld);
                ShowContextualAction(_btnCancelTarget);
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                return;
            }

            if (responding)
            {
                SetPhaseButtons(false, false, false, standbyHeld);
                HideContextualActions();
                SetBtn(_btnRestartBar, true);
                SetBtn(_btnMenu, true);
                _arSpace?.PlayFx(playerSide: true, DiskFxEvent.LegalZonePulse);
                RebuildResponseTray();

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

            // Standby Phase held for Blast Juggler: the only way on is MAIN 1 (or activating Juggler first).
            var standbyPause = !WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive && yourTurn &&
                               _engine.StandbyPausedFor == commander;
            var canMain1 = standbyPause && !busyAttack && _engine.CanContinueAfterStandby(commander);
            var railMain1 = SetPhaseButtons(canBattle, canMain2, canEnd, standbyPause, canMain1);
            if (standbyPause && !railMain1)
            {
                // No disk panel up: the thumb-island chip is the MAIN 1 (the disk's left key is, when it is up).
                ShowContextualAction(_btnMain1);
                SetBtn(_btnMain1, canMain1);
            }
            else if (standbyPause)
                HideContextualActions();
            else if (main && _engine.PendingTributes.Count > 0)
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

            if (_btnMain1 != null)
            {
                _btnMain1.gameObject.SetActive(false);
                SetBtn(_btnMain1, false);
            }

            _floatHud?.SetContextVisible(false);
        }

        /// <summary>Returns true when the disk panel is up and carries the phase keys (and MAIN 1 in a Standby pause).</summary>
        bool SetPhaseButtons(bool battle, bool main2, bool end, bool standbyPause = false, bool main1 = false)
        {
            var rail = PlayerPhaseRail();
            if (rail != null)
            {
                rail.SetEnabled(battle, main2, end, standbyPause, main1);
                _floatHud?.SetActionsVisible(false);
                SetBtn(_btnBattle, false);
                SetBtn(_btnMain2, false);
                SetBtn(_btnEnd, false);
                return rail.IsShowing;
            }

            SetBtn(_btnBattle, battle);
            SetBtn(_btnMain2, main2);
            SetBtn(_btnEnd, end);
            StylePhaseBtn(_btnBattle, battle, MenuCommandButton.FillGold);
            StylePhaseBtn(_btnMain2, main2, MenuCommandButton.FillPrimary);
            StylePhaseBtn(_btnEnd, end, MenuCommandButton.FillDanger);
            _floatHud?.SetActionsVisible(battle || main2 || end);
            return false;
        }

        /// <summary>
        /// Shuffle / Restart / Map live on the disk control panel while it is up; the screen versions
        /// come back whenever it is not (disk folded, no AR stage) so there is always a way out.
        /// </summary>
        void SyncDiskControls()
        {
            var rail = PlayerPhaseRail();
            var onDisk = rail != null && rail.IsShowing;
            if (rail != null)
                rail.SetUtility(CanRearrangeHand(out var who) && who.Hand.Count >= 2, RestartAllowed);
            if (_btnShuffleHand != null)
                _btnShuffleHand.gameObject.SetActive(!onDisk && !ArPresentationTarget.IsSalvageOst);
            var dock = _btnMenu != null ? _btnMenu.transform.parent : null;
            if (dock != null && dock.gameObject.activeSelf == onDisk)
                dock.gameObject.SetActive(!onDisk);
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
            // the AR fan: the tapped card lifted with the menu, it drops back with it
            _arSpace?.Interaction?.Drag?.ClearHandLift();
            _selectedField = null;
            _selectedSpellTrap = null;
            _selectedAttacker = null;
            _awaitingZonePick = false;
            _markingTributes = false;
            _markingForProcedure = false;
            _pendingZoneIndex = -1;
            ClearSsPlacement();
            ClearLegalZoneHighlights();
        }

        /// <summary>
        /// Explicit Mark tributes — field taps mark bodies. Default occupied-pad
        /// tap with a hand monster selected plays into that slot instead.
        /// </summary>
        bool FieldTapMarksTribute()
        {
            if (_engine == null || !_engine.InMainPhase) return false;
            var hand = _selectedHand;
            if (hand == null || hand.Def == null || !hand.Def.IsMonster) return false;
            if (MarkingTributesNeeded(hand) <= 0) return false;
            return _markingTributes;
        }

        /// <summary>
        /// Tributes the current marking is for: the selected monster's Special Summon procedure while marking for
        /// it, otherwise its Tribute Summon (Level 5–6: 1, Level 7+: 2).
        /// </summary>
        int MarkingTributesNeeded(CardInstance hand)
        {
            if (hand?.Def == null || !hand.Def.IsMonster) return 0;
            return _markingForProcedure ? ProcedureTributesRequired(hand) : TcgRules.TributesRequired(hand.Level);
        }

        /// <summary>Hand "Mark tributes": field taps now mark Tributes for <paramref name="card"/>.</summary>
        void BeginMarkingTributes(CardInstance card, bool forProcedure)
        {
            if (card == null || _engine == null) return;
            HoldHand(card);
            _markingTributes = true;
            _markingForProcedure = forProcedure;
            var need = MarkingTributesNeeded(card);
            var have = MarkedTributeCount(CommandWho() ?? _engine.Player, forProcedure);
            if (forProcedure)
            {
                _engine.Log(
                    $"{card.Name}: tap your field monsters to Tribute (face-down Sets count). " +
                    $"{have}/{need} marked — then Special Summon.");
                if (_status != null)
                    _status.text = $"Tribute select {have}/{need} — tap field monsters, then Special Summon";
            }
            else
            {
                _engine.Log(
                    $"Lv{card.Level}: tap your field monsters to Tribute " +
                    $"(face-down Sets count). {have}/{need} marked — then Summon/Set.");
                if (_status != null)
                    _status.text =
                        $"Tribute select {have}/{need} — tap field monsters, then Summon or Set";
            }

            RefreshInspectForSelectedHand();
            Refresh();
        }

        /// <summary>A field monster tapped while marking Tributes: toggle it, then show the count.</summary>
        void MarkTributeTapped(DuelistState who, CardInstance card)
        {
            ToggleMarkedTribute(who, card);
            var need = MarkingTributesNeeded(_selectedHand);
            var have = MarkedTributeCount(who, _markingForProcedure);
            if (_status != null)
            {
                if (_markingForProcedure)
                    _status.text = have >= need
                        ? $"Tributes ready ({have}/{need}) — Special Summon {_selectedHand?.Name}"
                        : $"Tribute select {have}/{need} — tap field monsters, then Special Summon";
                else
                    _status.text = have >= need
                        ? $"Tributes ready ({have}/{need}) — tap a Monster Zone on your disk"
                        : $"Tribute select {have}/{need} — tap field monsters, then a disk slot";
            }

            RefreshInspectForSelectedHand();
            Refresh();
        }

        /// <summary>
        /// Marked Tributes that count: for a summon procedure every one it may Tribute; for a Tribute Summon only
        /// those <see cref="DuelEngine"/> keeps (not a Token marked for a Toon, not a monster that left the field).
        /// Soul Exchange targets count for both.
        /// </summary>
        int MarkedTributeCount(DuelistState who, bool forProcedure)
        {
            if (_engine == null || who == null) return 0;
            var forced = WRLDZ.Duel.TextEffects.TributeSummonsRuntime.ForcedTributesForSummon(who);
            var n = 0;
            foreach (var t in _engine.PendingTributes)
            {
                if (forced.Contains(t) ||
                    (forProcedure ? CanMarkProcedureTribute(who, t) : TcgRules.CanBeTributedForSummon(who, t)))
                    n++;
            }

            return n;
        }

        void ToggleMarkedTribute(DuelistState who, CardInstance card)
        {
            if (_markingForProcedure)
                ToggleProcedureTribute(who, card);
            else
                _engine.ToggleTribute(who, card);
        }

        /// <summary>
        /// Mark or unmark a Tribute for a Special Summon procedure. <see cref="DuelEngine.ToggleTribute"/> refuses
        /// monsters that "cannot be Tributed for a Tribute Summon" (Ojama / Sheep / Kuriboh Tokens, Fox Fire), but
        /// the procedure is not a Tribute Summon (ygopro SelectReleaseGroup), so they may pay it. The engine
        /// re-checks every marked Tribute when the Summon is sent, and a Tribute Summon still skips them.
        /// </summary>
        void ToggleProcedureTribute(DuelistState who, CardInstance monster)
        {
            if (_engine == null || who == null || monster == null) return;
            var pending = _engine.PendingTributes;
            if (pending.Contains(monster))
            {
                pending.Remove(monster);
                _engine.Log($"Tribute unselected: {monster.Name}");
                return;
            }

            if (!CanMarkProcedureTribute(who, monster))
            {
                ShowFlashStatus($"{monster.Name} cannot be Tributed.");
                return;
            }

            pending.Add(monster);
            var face = monster.FaceUp ? monster.Name : $"{monster.Name} (face-down)";
            _engine.Log($"Tribute selected: {face} ({pending.Count} selected)");
        }

        /// <summary>
        /// A monster <paramref name="who"/> controls, face-up or face-down, while Tributing is allowed, unless it
        /// "cannot be Tributed" at all (<see cref="CardInstance.CannotBeTributed"/>, Archfiend's Roar).
        /// </summary>
        bool CanMarkProcedureTribute(DuelistState who, CardInstance monster) =>
            _engine != null && !_engine.GameOver && _engine.TurnPlayer == who && _engine.InMainPhase &&
            who.TryFindMonster(monster, out _) && !(monster.CannotBeTributed && monster.FaceUp) &&
            !WRLDZ.Duel.TextEffects.ContinuousProtections.NeitherPlayerCanTribute(_engine);

        /// <summary>
        /// The player still has to choose the procedure's Tributes: fewer are marked than it takes, and there are
        /// more candidates than that, so the engine waits for <see cref="DuelEngine.PendingTributes"/> instead of
        /// picking for a human.
        /// </summary>
        bool ProcedureNeedsTributeChoice(DuelistState who, CardInstance card)
        {
            var need = ProcedureTributesRequired(card);
            if (need <= 0 || who == null) return false;
            // As the engine picks them: Soul Exchange targets first, then the marked monsters.
            var forced = WRLDZ.Duel.TextEffects.TributeSummonsRuntime.ForcedTributesForSummon(who);
            var candidates = forced.Count;
            var chosen = forced.Count;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null || forced.Contains(m) || !CanMarkProcedureTribute(who, m)) continue;
                candidates++;
                if (_engine.PendingTributes.Contains(m)) chosen++;
            }

            return chosen < need && candidates > need;
        }

        /// <summary>
        /// Tributes a hand monster's own Special Summon procedure takes: the compiled "Must first be Special Summoned
        /// (from your hand) by Tributing N monsters" (the Toons). 0 when it takes none (Toon Mermaid) or names them and
        /// the engine picks them itself (Gate Guardian, Great Moth).
        /// </summary>
        static int ProcedureTributesRequired(CardInstance card) =>
            System.Math.Max(0, WRLDZ.Duel.TextEffects.ToonsRuntime.HandProcedureOf(card)?.Amount ?? 0);

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
            if (_ssByProcedure && _pendingSsTarget != c)
                ClearSsPlacement();
            // Tributes marked for another card's summon procedure: field taps stop marking for it.
            if (_markingForProcedure && _selectedHand != c)
            {
                _markingTributes = false;
                _markingForProcedure = false;
            }

            _selectedHand = c;
            _selectedField = null;
            _selectedSpellTrap = null;
            _selectedAttacker = null;
            ShowLegalZoneHighlights(c);
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
            var zone = UiFoundation.GetOrAdd<DiskDropZone>(row.gameObject);
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
            who.ShuffleHand(_engine.Rng);
            // AR: the floating hand riffles its fan on the next sync (even if the order came out unchanged)
            if (who == _engine.Player) ArFloatingHand.NotifyHandShuffled();
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

        /// <summary>Set by RefreshNow: a Spell/Trap result is pending, so newly arrived hand cards wait.</summary>
        bool _handHoldArrivals;
        readonly List<CardInstance> _handScratch = new();

        void RebuildHand()
        {
            if (_handRow == null) return;
            var who = CommandWho() ?? _engine.Player;
            if (who == null) return;

            // Spell/Trap result gate (same rule as ArHandVolume.SyncHand): cards that ARRIVE while a
            // result is pending (Pot of Greed's draws) appear — and play the draw SFX — at the result
            // beat (the _refreshOnResultGate re-refresh). Cards leaving the hand still leave now.
            var holdArrivals = Application.isPlaying && _handHoldArrivals &&
                               ReferenceEquals(_handShownOwner, who) && ActivationBeat.AnyResultPending;
            _handScratch.Clear();
            if (who.Hand != null)
                foreach (var hc in who.Hand)
                {
                    if (hc == null) continue;
                    if (holdArrivals && !_handShownIds.Contains(hc.InstanceId)) continue;
                    _handScratch.Add(hc);
                }

            _handShownIds.Clear();
            foreach (var hc in _handScratch)
                _handShownIds.Add(hc.InstanceId);
            _handShownOwner = who;

            if (_btnShuffleHand != null)
                SetBtn(_btnShuffleHand, who.Hand != null && who.Hand.Count >= 2
                    && !_engine.GameOver
                    && !(_engine.OpeningSequenceActive
                         || (_preDuel != null && _preDuel.BlocksGameplay && !_preDuelComplete)));

            var arDrawing = FloatingHandIsLive();
            if (!arDrawing && _drawSfxHandCount >= 0 && who.IsPlayer &&
                _handScratch.Count > _drawSfxHandCount)
            {
                var n = _handScratch.Count - _drawSfxHandCount;
                for (var i = 0; i < n; i++)
                    WrldzAudio.PlayCardDraw();
            }

            _drawSfxHandCount = _handScratch.Count;

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

            foreach (var card in _handScratch)
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

                var drag = UiFoundation.GetOrAdd<HandCardDrag>(go);
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
                    HoldHand(c);
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
            OpenGraveyard(playerSide ? (CommandWho() ?? _engine.Player) : _engine.Opponent, playerSide);
        }

        void OpenGraveyard(DuelistState who, bool playerSide)
        {
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
                // Monster Reborn-family: pick ATK/DEF and a Monster Zone before resolve.
                if (TryBeginSsPlacement(card))
                {
                    _gyBrowser?.Hide();
                    HideTargetPicker();
                    Refresh();
                    return;
                }

                // Target menu: tap art to commit. Inspect-then-SELECT left Reborn
                // pending after the player thought they had chosen.
                if (_engine.TrySelectEffectTarget(card))
                {
                    _gyBrowser?.Hide();
                    HideTargetPicker();
                    CloseInspectAfterPlay();
                    ClearCardSelection();
                    Refresh();
                    MaybeRunAi();
                }
                else
                    ShowFlashStatus($"Not a legal target: {card.Name}");
                return;
            }

            OpenInspect(card, showFace: true, null);
        }

        /// <summary>Tap card → full art + legal action menu popup.</summary>
        void OpenCardMenu(CardInstance c)
        {
            if (c == null || _engine == null) return;
            if (TrySelectPendingTarget(c))
                return;
            WrldzAudio.PlayCardTap();
            HoldHand(c);
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
            Refresh();
        }

        /// <summary>
        /// Drag hand card onto the monster or S/T row of your disk. Highlights
        /// legal slots — the player then taps the pad (ATK/DEF or Set after).
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

            FreeUiKit.PlayConfirm();
            _arSpace?.PlayFx(true, DiskFxEvent.BladeDeploy, 0.45f);
            OpenCardMenu(card);
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

            HoldHand(card);
            OpenInspect(card, showFace: true, acts, closeLabel: "CANCEL", force: true);
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
                var needsTributePick = mine && FieldTapMarksTribute();
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

                    if (TryActivateUniqueSummonResponse(card))
                        return;

                    if (mine)
                    {
                        if (FieldTapMarksTribute())
                        {
                            MarkTributeTapped(commander, card);
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
                            if (BeatBlocksDeclaration("attack")) return;
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

            // Standby Phase held for a standby-only activation: only what the engine allows there.
            if (_engine.Phase == DuelPhase.Standby && _engine.StandbyPausedFor == who)
            {
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

            var main = _engine.InMainPhase;
            if (!main) return list;

            // Monster: inspect play options, then tap a disk slot (no zone-button menu).
            if (card.Def != null && card.Def.IsMonster)
            {
                var needTrib = TcgRules.TributesRequired(card.Level);
                var haveTrib = MarkedTributeCount(who, forProcedure: false);
                var fieldMons = who.MonsterCount;
                if (_engine.CanNormalSummonOrSet(who, card))
                {
                    var tribTag = needTrib > 0 ? $" ({haveTrib}/{needTrib})" : "";
                    list.Add(new CardAction
                    {
                        Label = "Summon ATK" + tribTag,
                        Color = GbaTheme.CmdSummon,
                        Invoke = () => BeginDiskPlacement(card, asSet: false)
                    });
                    list.Add(new CardAction
                    {
                        Label = "Set Face-Down" + tribTag,
                        Color = GbaTheme.CmdNeutral,
                        Invoke = () => BeginDiskPlacement(card, asSet: true)
                    });
                    if (needTrib > 0 && haveTrib < needTrib && fieldMons != needTrib)
                    {
                        list.Add(new CardAction
                        {
                            Label = $"Mark tributes {haveTrib}/{needTrib}",
                            Color = GbaTheme.CmdMuted,
                            Invoke = () => BeginMarkingTributes(card, forProcedure: false)
                        });
                    }
                }
                else if (needTrib > 0 &&
                         !WRLDZ.Duel.TextEffects.PsctGrammar.BlocksNormalSummonOrSet(card.OfficialText))
                {
                    list.Add(new CardAction
                    {
                        Label = $"Need {needTrib} on field",
                        Color = GbaTheme.CmdMuted,
                        Invoke = () => _engine.Log(
                            $"Lv{card.Level}: need {needTrib} monster(s) on your field to Tribute Summon.")
                    });
                }

                // The card's own summon procedure from the hand (Gate Guardian, Great Moth, the Toons).
                if (_engine.CanSpecialSummonProcedure(who, card))
                {
                    var needProc = ProcedureTributesRequired(card);
                    var haveProc = MarkedTributeCount(who, forProcedure: true);
                    list.Add(new CardAction
                    {
                        Label = needProc > 0 ? $"Special Summon ({haveProc}/{needProc})" : "Special Summon",
                        Color = GbaTheme.CmdSummon,
                        Invoke = () => BeginProcedureSummon(card)
                    });
                    if (ProcedureNeedsTributeChoice(who, card))
                    {
                        list.Add(new CardAction
                        {
                            Label = $"Mark tributes {haveProc}/{needProc}",
                            Color = GbaTheme.CmdMuted,
                            Invoke = () => BeginMarkingTributes(card, forProcedure: true)
                        });
                    }
                }
            }

            if (_engine.CanSetSpellTrap(who, card))
            {
                list.Add(new CardAction
                {
                    Label = "Set Face-Down",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () => BeginDiskPlacement(card, asSet: true)
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
                // A Toon must target a Toon monster while the opponent controls one.
                if (WRLDZ.Duel.TextEffects.ToonsRuntime.ForbidsAttackTarget(_engine, attacker, target))
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
                    MarkingTributesNeeded(_selectedHand) > 0 &&
                    who.TryFindMonster(card, out _))
                {
                    var marked = _engine.PendingTributes.Contains(card);
                    list.Add(new CardAction
                    {
                        Label = marked ? "Unselect Tribute" : "Tribute",
                        Color = GbaTheme.CmdSummon,
                        Invoke = () =>
                        {
                            ToggleMarkedTribute(who, card);
                            RefreshInspectForSelectedHand();
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
                        Label = card.Position == BattlePosition.Attack
                            ? "To Defense"
                            : "To Attack",
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

            // Standby Phase held for a standby-only ignition (Blast Juggler): its Activate lives here.
            if (_engine.Phase == DuelPhase.Standby && _engine.StandbyPausedFor == who &&
                _engine.CanActivateSpellTrap(who, card, fromHand: false))
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

            // The attacker's enemy (hotseat: the Opponent's seat attacks the Player).
            var opp = EnemyOf(CommandWho() ?? _engine.Player);
            if (opp?.MonsterZones != null)
            {
                for (var i = 0; i < 5; i++)
                {
                    var occ = i < opp.MonsterZones.Length ? opp.MonsterZones[i].Occupant : null;
                    var legal = occ != null && _attackTargets.Exists(t =>
                        t != null && t.InstanceId == occ.InstanceId);
                    PaintGlanceSlot(_targetRow, occ, legal, legal
                        ? () => DeclareAttackOn(_attackPickerAttacker, occ)
                        : (System.Action)null);
                }
            }

            var cancel = CreateButton(_targetRow, "CANCEL", DoCancelTarget, GbaTheme.CmdDanger);
            var le = cancel.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 92f; le.minHeight = 56f; le.preferredWidth = 100f;
        }

        /// <summary>
        /// Mini-playmat glance of a field pick by <paramref name="picker"/>: their enemy's zones, then — for
        /// SpellTrapOnField, when a legal target is there — their own S/T + Field zones (MST can target your
        /// own Set card). Any legal target not in those zones gets a trailing slot so none is dropped.
        /// True when both sides were painted.
        /// </summary>
        bool FillFieldGlanceTargets(PendingActivation pending, DuelistState picker)
        {
            var enemy = EnemyOf(picker);
            if (enemy == null || pending?.LegalTargets == null) return false;
            bool Same(CardInstance a, CardInstance b) =>
                a != null && b != null && (a == b || (a.InstanceId != 0 && a.InstanceId == b.InstanceId));
            bool Legal(CardInstance c) => c != null && pending.LegalTargets.Exists(t => Same(t, c));
            var painted = new List<CardInstance>();
            void Paint(CardInstance occ)
            {
                var ok = Legal(occ);
                PaintGlanceSlot(_targetRow, occ, ok, ok
                    ? () => { _engine.TrySelectEffectTarget(occ); Refresh(); }
                    : null);
                if (occ != null) painted.Add(occ);
            }

            void PaintSpellTraps(DuelistState side)
            {
                if (side.SpellTrapZones != null)
                    for (var i = 0; i < 5; i++)
                        Paint(i < side.SpellTrapZones.Length ? side.SpellTrapZones[i].Occupant : null);
                Paint(side.FieldSpellZone?.Occupant);
            }

            var bothSides = false;
            void PaintMonsters(DuelistState side)
            {
                if (side?.MonsterZones == null) return;
                for (var i = 0; i < 5; i++)
                    Paint(i < side.MonsterZones.Length ? side.MonsterZones[i].Occupant : null);
            }

            if (pending.TargetKind == EffectTargetKind.SpellTrapOnField)
            {
                if (picker != null)
                    foreach (var own in picker.SpellTrapsOnField())
                        bothSides |= Legal(own);
                if (bothSides) PaintGlanceSideLabel(_targetRow, "OPP", DuelystUi.Magenta);
                PaintSpellTraps(enemy);
                if (bothSides)
                {
                    PaintGlanceSideLabel(_targetRow, "YOURS", DuelystUi.Cyan);
                    PaintSpellTraps(picker);
                }
            }
            else if (pending.TargetKind == EffectTargetKind.FaceDownMonsterOnField ||
                     pending.TargetKind == EffectTargetKind.AnyMonsterOnField ||
                     pending.TargetKind == EffectTargetKind.FaceUpMonsterOnField)
            {
                // Nobleman Soft: face-down (or any/face-up) monsters on either field — show both sides when
                // the controller also has a legal target (own Set under Nobleman).
                if (picker != null)
                    foreach (var own in picker.MonstersOnField())
                        bothSides |= Legal(own);
                if (bothSides) PaintGlanceSideLabel(_targetRow, "OPP", DuelystUi.Magenta);
                PaintMonsters(enemy);
                if (bothSides)
                {
                    PaintGlanceSideLabel(_targetRow, "YOURS", DuelystUi.Cyan);
                    PaintMonsters(picker);
                }
            }
            else if (enemy.MonsterZones != null)
                for (var i = 0; i < 5; i++)
                    Paint(i < enemy.MonsterZones.Length ? enemy.MonsterZones[i].Occupant : null);

            foreach (var t in pending.LegalTargets)
                if (t != null && !painted.Exists(p => Same(p, t)))
                    Paint(t);
            return bothSides;
        }

        /// <summary>Side tag ahead of a glance group when the sheet shows both fields.</summary>
        void PaintGlanceSideLabel(Transform row, string side, Color color)
        {
            var go = new GameObject("Side " + side, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(row, false);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 80f;
            le.minHeight = 120f;
            le.preferredWidth = 96f;
            le.preferredHeight = 140f;
            var t = CreateText(go.transform, "Label", 15, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            Stretch(t.rectTransform);
            t.fontSize = WrldzType.Readable(15);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = WrldzType.MinBody;
            t.resizeTextMaxSize = t.fontSize;
            t.color = color;
            t.raycastTarget = false;
            t.text = side;
        }

        void PaintGlanceSlot(Transform row, CardInstance card, bool legal, System.Action onPick)
        {
            var go = new GameObject(ViewerSafeName(card) ?? "empty", typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(row, false);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 88f;
            le.minHeight = 120f;
            le.preferredWidth = 100f;
            le.preferredHeight = 140f;
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = legal && onPick != null;
            if (card == null)
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0.12f, 0.18f, 0.24f, 0.35f);
            }
            else if (!legal)
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0.06f, 0.08f, 0.12f, 0.22f);
            }
            else
            {
                // Back for a Set on a side the viewer does not command; the viewer's own Set shows its
                // face with the field's face-down tint (RebuildSpellTraps), so MST can pick among them.
                var art = HiddenSetZone(card) == null && _db != null ? _db.GetArt(card.CardId) : null;
                img.sprite = art ?? YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
                img.color = card.FaceUp
                    ? Color.white
                    : Color.Lerp(Color.white, new Color(0.55f, 0.62f, 0.95f, 1f), 0.25f);
                var rim = go.AddComponent<Outline>();
                rim.effectColor = new Color(1f, 0.65f, 0.16f, 0.95f);
                rim.effectDistance = new Vector2(3f, -3f);
                rim.useGraphicAlpha = false;
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (legal && onPick != null)
                btn.onClick.AddListener(() => onPick());
        }

        void DeclareAttackOn(CardInstance attacker, CardInstance target)
        {
            if (_engine == null || attacker == null) return;
            if (BeatBlocksDeclaration("attack")) return;
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

        /// <summary>
        /// Summon window: tapping the summoned monster activates the unique legal Set trap
        /// (Trap Hole on Lord of D.). Two legal traps still require tapping the trap.
        /// </summary>
        bool TryActivateUniqueSummonResponse(CardInstance summoned)
        {
            if (!IsLocalResponseWindow() || summoned == null) return false;
            var pr = _engine.PendingResponse;
            if (pr.Timing != ResponseTiming.MonsterSummoned || pr.LegalCards == null)
                return false;
            if (pr.Summoned == null ||
                (pr.Summoned != summoned && pr.Summoned.InstanceId != summoned.InstanceId))
                return false;

            var who = CommandWho() ?? _engine.Player;
            CardInstance pick = null;
            for (var i = 0; i < pr.LegalCards.Count; i++)
            {
                var c = pr.LegalCards[i];
                if (c == null || who == null || !who.TryFindSpellTrap(c, out _))
                    continue;
                if (pick != null)
                    return false;
                pick = c;
            }

            return pick != null && TryActivateResponseCard(pick);
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
            var blink = UiFoundation.GetOrAdd<ResponseZoneBlink>(go);
            blink.Bind(img, LegalIntentService.ResponseGlowColor);
        }

        void ShowLegalZoneHighlights(CardInstance card)
        {
            var ix = _arSpace?.Interaction;
            if (card == null || _engine == null)
            {
                ix?.ClearLegalPlacements();
                return;
            }

            var who = CommandWho() ?? _engine.Player;
            List<LegalIntentService.LegalSlot> slots;
            if (_awaitingSsZonePick)
                slots = EmptyMonsterSlots(who);
            else if (WRLDZ.Duel.Ocg.OcgLabDuelHost.IsActive)
                slots = OcgZoneSlots(card, _zonePickAsSet);
            else if (_awaitingZonePick)
                slots = LegalIntentService.LegalSlotsForAction(_engine, who, card, _zonePickAsSet);
            else
                slots = LegalIntentService.LegalSlots(_engine, who, card);

            ix?.ShowLegalPlacements(slots);
        }

        static List<LegalIntentService.LegalSlot> EmptyMonsterSlots(DuelistState who)
        {
            var slots = new List<LegalIntentService.LegalSlot>();
            if (who?.MonsterZones == null) return slots;
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                if (who.MonsterZones[i] == null || !who.MonsterZones[i].IsEmpty) continue;
                slots.Add(new LegalIntentService.LegalSlot
                {
                    Kind = RulesZoneKind.Monster,
                    Index = i,
                    CanSummonAtk = true,
                    CanSet = true
                });
            }

            return slots;
        }

        void ClearLegalZoneHighlights()
        {
            _arSpace?.Interaction?.ClearLegalPlacements();
        }

        bool IsLegalPickSlot(RulesZoneKind kind, int index)
        {
            if (_engine == null) return false;
            var who = CommandWho() ?? _engine.Player;
            if (_awaitingSsZonePick)
                return kind == RulesZoneKind.Monster &&
                       who?.MonsterZones != null &&
                       index >= 0 && index < who.MonsterZones.Length &&
                       who.MonsterZones[index] != null &&
                       who.MonsterZones[index].IsEmpty;

            if (_selectedHand == null) return false;
            var slots = _awaitingZonePick
                ? LegalIntentService.LegalSlotsForAction(_engine, who, _selectedHand, _zonePickAsSet)
                : LegalIntentService.LegalSlots(_engine, who, _selectedHand);
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

        /// <summary>
        /// Play option chosen on inspect: hide the sheet and wait for a disk pad.
        /// No M1–M5 / ST1–ST5 button list.
        /// </summary>
        void BeginDiskPlacement(CardInstance card, bool asSet)
        {
            if (card == null || _engine == null) return;
            if (_ssByProcedure) ClearSsPlacement();
            _awaitingZonePick = true;
            _zonePickAsSet = asSet;
            HoldHand(card);
            RevealDiskForPlacement();
            ShowLegalZoneHighlights(card);
            var act = asSet ? "Set" : "Summon ATK";
            var kindName = card.Def != null && card.Def.IsMonster &&
                           !(card.Def.IsSpell || card.Def.IsTrap)
                ? "Monster Zone"
                : card.Def != null && card.Def.IsFieldSpell
                    ? "Field Spell slot"
                    : "Spell/Trap slot";
            if (_status != null)
                _status.text = $"{card.Name} — {act}: tap a highlighted {kindName} on your disk";
            _engine.Log($"{card.Name}: {act} — tap a {kindName} on your disk.");
            Refresh();
        }

        void RevealDiskForPlacement()
        {
            _inspectCard = null;
            _inspectFromHand = false;
            _inspect?.HideQuiet();
        }

        void CancelZonePick()
        {
            _awaitingZonePick = false;
            if (!_awaitingSsZonePick)
                ClearLegalZoneHighlights();
        }

        void RefreshInspectForSelectedHand()
        {
            var card = _selectedHand;
            if (card == null) return;
            OpenInspect(card, showFace: true, CollectHandActions(card), force: true);
        }

        void OnArEmptyZoneTapped(RulesZoneKind kind, int index)
        {
            if (_engine == null || _engine.GameOver) return;
            if (ArGestureBusy) return;
            if (TryActivateResponseAtZone(kind, index))
                return;
            if (_awaitingSsZonePick)
            {
                CommitSsPlacement(index, _pendingSsPos);
                return;
            }

            var card = _selectedHand ?? _inspectCard;
            if (card == null)
            {
                ShowFlashStatus("Select a card in hand first.");
                return;
            }

            if (_awaitingZonePick)
            {
                var who = CommandWho() ?? _engine.Player;
                var verdict = _engine.ValidatePlacement(who, card, kind, index, _zonePickAsSet);
                if (!verdict.Legal)
                {
                    var why = verdict.Reason ?? "Not a legal zone for this play.";
                    _engine.Log(why);
                    ShowFlashStatus(why);
                    return;
                }

                PlaceInSlot(card, new LegalIntentService.LegalSlot { Kind = kind, Index = index },
                    _zonePickAsSet);
                return;
            }

            OfferPositionForZone(card, kind, index);
        }

        /// <summary>
        /// Disk slot chosen: ATK / Set (or Activate) for that pad, then place.
        /// One legal play commits immediately.
        /// </summary>
        void OfferPositionForZone(CardInstance card, RulesZoneKind kind, int index)
        {
            if (card == null || _engine == null) return;
            HoldHand(card);
            List<CardAction> acts;
            if (kind == RulesZoneKind.Monster)
                acts = CollectMonsterPlayActionsForZone(card, index);
            else if (kind == RulesZoneKind.SpellTrap)
                acts = CollectSpellTrapPlayActionsForZone(card, index);
            else
            {
                PlaceInSlot(card, new LegalIntentService.LegalSlot { Kind = kind, Index = index },
                    asSet: false);
                return;
            }

            if (acts.Count == 0)
            {
                _engine.Log("Not a legal zone for this card.");
                ShowFlashStatus("That zone is not legal for this card.");
                return;
            }

            if (acts.Count == 1)
            {
                acts[0].Invoke?.Invoke();
                return;
            }

            if (_status != null)
                _status.text = kind == RulesZoneKind.Monster
                    ? $"{card.Name} — choose Attack or Set"
                    : $"{card.Name} — Activate or Set";
            OpenPlayChooser(card, acts);
        }

        /// <summary>
        /// Hand / deck / GY tap during an effect-target window. SS-from-GY/hand/deck
        /// opens zone (and ATK/DEF when the printed text does not lock position).
        /// </summary>
        bool TrySelectPendingTarget(CardInstance card)
        {
            if (card == null || _engine == null || !_engine.IsAwaitingEffectTarget)
                return false;
            if (TryBeginSsPlacement(card))
            {
                Refresh();
                return true;
            }

            if (!_engine.TrySelectEffectTarget(card))
            {
                ShowFlashStatus($"Not a legal target: {card.Name}");
                return true;
            }

            ClearCardSelection();
            CloseInspectAfterPlay();
            Refresh();
            MaybeRunAi();
            return true;
        }

        bool TryBeginSsPlacement(CardInstance target)
        {
            if (_engine == null || target == null) return false;
            var pending = _engine.PendingActivation;
            if (pending == null) return false;
            // Only the Special Summon shape gets the placement flow. Monster
            // Reincarnation shares MonsterInEitherGy with Monster Reborn, and the
            // "Summon ATK/DEF" menu left its add-to-hand target unresolved.
            if (!SpellTrapEffects.PendingTargetIsSpecialSummon(pending)) return false;
            if (_engine.ResolveLegalEffectTarget(target) == null) return false;
            var who = pending.Controller ?? CommandWho() ?? _engine.Player;
            var empties = EmptyMonsterSlots(who);
            if (empties.Count == 0) return false;

            _pendingSsTarget = target;
            _pendingSsZone = -1;
            _awaitingSsZonePick = false;
            _ssByProcedure = false;
            if (TryLockSsPosition(pending, out var locked))
            {
                BeginSsZonePick(target, locked);
                return true;
            }

            OpenInspect(target, showFace: true, new List<CardAction>
            {
                new()
                {
                    Label = "Summon ATK",
                    Color = GbaTheme.CmdSummon,
                    Invoke = () => BeginSsZonePick(target, BattlePosition.Attack)
                },
                new()
                {
                    Label = "Summon DEF",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () => BeginSsZonePick(target, BattlePosition.Defense)
                }
            }, closeLabel: "CANCEL", extraOnClose: CancelSsPlacement, force: true);
            if (_status != null)
                _status.text = $"{target.Name} — choose Attack or Defense, then a Monster Zone";
            _engine.Log($"{target.Name}: Special Summon — choose Attack or Defense, then tap a Monster Zone.");
            Refresh();
            return true;
        }

        static bool TryLockSsPosition(PendingActivation pending, out BattlePosition locked)
        {
            locked = BattlePosition.Attack;
            if (pending?.Card?.Def == null) return false;
            var prog = WRLDZ.Duel.TextEffects.CompiledEffectCache.GetOrCompile(pending.Card.Def);
            if (prog?.ClauseList != null)
            {
                foreach (var c in prog.ClauseList)
                {
                    if (c == null) continue;
                    var ss = c.Action == EffectActionKind.SpecialSummonFromGy ||
                             c.Action == EffectActionKind.SpecialSummonFromHand ||
                             c.Action == EffectActionKind.SpecialSummonFromDeck ||
                             c.Action == EffectActionKind.SpecialSummonNamed ||
                             c.Action == EffectActionKind.SpecialSummonThisFromHand;
                    if (!ss) continue;
                    if (c.SummonInDefense)
                    {
                        locked = BattlePosition.Defense;
                        return true;
                    }

                    if (c.SummonInAttack)
                    {
                        locked = BattlePosition.Attack;
                        return true;
                    }
                }
            }

            var text = pending.Card.OfficialText ?? pending.Card.Def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"Special Summon.{0,140}in Defense Position",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                locked = BattlePosition.Defense;
                return true;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"Special Summon.{0,140}in Attack Position",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                locked = BattlePosition.Attack;
                return true;
            }

            return false;
        }

        void BeginSsZonePick(CardInstance target, BattlePosition pos)
        {
            _pendingSsTarget = target;
            _pendingSsPos = pos;
            _awaitingSsZonePick = true;
            _ssPositionLocked = true;
            RevealDiskForPlacement();
            ShowLegalZoneHighlights(target);
            var posName = pos == BattlePosition.Defense ? "Defense" : "Attack";
            if (_status != null)
                _status.text = $"{target.Name} — Special Summon {posName}: tap a Monster Zone on your disk";
            _engine.Log($"{target.Name}: Special Summon {posName} — tap a Monster Zone on your disk.");
            Refresh();
        }

        void CommitSsPlacement(int zoneIndex, BattlePosition pos)
        {
            var target = _pendingSsTarget;
            if (target == null || _engine == null) return;
            if (_ssByProcedure)
            {
                ClearSsPlacement();
                CommitProcedureSummon(target, zoneIndex, pos);
                return;
            }

            _pendingSsPos = pos;
            _engine.SetPendingSpecialSummonPlacement(zoneIndex, pos);
            var ok = _engine.TrySelectEffectTarget(target);
            ClearSsPlacement();
            _gyBrowser?.Hide();
            HideTargetPicker();
            CloseInspectAfterPlay();
            if (ok)
            {
                ClearCardSelection();
                MaybeRunAi();
            }
            else
                _engine.Log("Special Summon failed.");
            Refresh();
        }

        void CancelSsPlacement()
        {
            _awaitingSsZonePick = false;
            _ssPositionLocked = false;
            _ssByProcedure = false;
            _pendingSsZone = -1;
            _pendingSsTarget = null;
            _engine?.ClearPendingSpecialSummonPlacement();
            ClearLegalZoneHighlights();
            if (_engine != null && _engine.IsAwaitingEffectTarget)
                Refresh();
        }

        void ClearSsPlacement()
        {
            _awaitingSsZonePick = false;
            _ssPositionLocked = false;
            _ssByProcedure = false;
            _pendingSsZone = -1;
            _pendingSsTarget = null;
            _engine?.ClearPendingSpecialSummonPlacement();
        }

        /// <summary>
        /// Hand "Special Summon": the card's own summon procedure. Tributes first when the player has a choice
        /// of them, then face-up Attack or Defense, then an empty Monster Zone (the AR hand carries an Attack Summon
        /// there). With no empty zone the engine uses the first one its Tributes free.
        /// </summary>
        void BeginProcedureSummon(CardInstance card)
        {
            if (card == null || _engine == null) return;
            var who = CommandWho() ?? _engine.Player;
            if (ProcedureNeedsTributeChoice(who, card))
            {
                BeginMarkingTributes(card, forProcedure: true);
                return;
            }

            _awaitingZonePick = false;
            _markingTributes = false;
            _markingForProcedure = false;
            HoldHand(card);
            _pendingSsTarget = card;
            _pendingSsZone = -1;
            _awaitingSsZonePick = false;
            _ssByProcedure = true;
            OpenInspect(card, showFace: true, new List<CardAction>
            {
                new()
                {
                    Label = "Summon ATK",
                    Color = GbaTheme.CmdSummon,
                    Invoke = () => BeginProcedureZonePick(card, BattlePosition.Attack)
                },
                new()
                {
                    Label = "Summon DEF",
                    Color = GbaTheme.CmdNeutral,
                    Invoke = () => BeginProcedureZonePick(card, BattlePosition.Defense)
                }
            }, closeLabel: "CANCEL", extraOnClose: CancelSsPlacement, force: true);
            if (_status != null)
                _status.text = $"{card.Name} — Special Summon: choose Attack or Defense, then a Monster Zone";
            _engine.Log($"{card.Name}: Special Summon — choose Attack or Defense, then tap a Monster Zone.");
            Refresh();
        }

        void BeginProcedureZonePick(CardInstance card, BattlePosition pos)
        {
            if (card == null || _engine == null) return;
            var who = CommandWho() ?? _engine.Player;
            // Every zone is taken until the Tributes leave: the engine uses the first one they free.
            if (EmptyMonsterSlots(who).Count == 0)
            {
                ClearSsPlacement();
                CommitProcedureSummon(card, -1, pos);
                return;
            }

            BeginSsZonePick(card, pos);
        }

        /// <summary>
        /// Send <see cref="DuelIntentKind.SpecialSummon"/> with the chosen zone and face-up position
        /// (<see cref="DuelEngine.SetPendingSpecialSummonPlacement"/>; −1 keeps the engine's first free zone).
        /// </summary>
        void CommitProcedureSummon(CardInstance card, int zoneIndex, BattlePosition pos)
        {
            if (card == null || _engine == null || _engine.GameOver) return;
            bool Commit()
            {
                _engine.SetPendingSpecialSummonPlacement(zoneIndex, pos);
                var r = Cmd(DuelIntent.Of(DuelIntentKind.SpecialSummon, card));
                // A refused procedure never reaches the placement: don't leave it for the next Special Summon.
                _engine.ClearPendingSpecialSummonPlacement();
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                }
                else if (ProcedureTributesRequired(card) > 0 &&
                         _engine.CanSpecialSummonProcedure(CommandWho() ?? _engine.Player, card))
                {
                    // The engine is waiting for the player to choose the Tributes.
                    BeginMarkingTributes(card, forProcedure: true);
                }

                RefreshNow();
                return r.Ok;
            }

            // The AR hand carries an Attack Summon to the pad; a Defense one lands from the engine sync.
            if (zoneIndex >= 0 && pos == BattlePosition.Attack &&
                TryArGesture(card, RulesZoneKind.Monster, zoneIndex, faceDown: false, Commit))
                return;
            Commit();
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
            if (_engine.InMainPhase && _engine.TurnPlayer == who && _engine.CanSpecialSummonProcedure(who, card))
            {
                if (ProcedureNeedsTributeChoice(who, card))
                {
                    list.Add(new CardAction
                    {
                        Label = "Special Summon",
                        Color = GbaTheme.CmdSummon,
                        Invoke = () => BeginProcedureSummon(card)
                    });
                }
                else
                {
                    list.Add(new CardAction
                    {
                        Label = "Special Summon ATK",
                        Color = GbaTheme.CmdSummon,
                        Invoke = () => CommitProcedureSummon(card, zone, BattlePosition.Attack)
                    });
                    list.Add(new CardAction
                    {
                        Label = "Special Summon DEF",
                        Color = GbaTheme.CmdNeutral,
                        Invoke = () => CommitProcedureSummon(card, zone, BattlePosition.Defense)
                    });
                }
            }

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
                        HoldHand(card);
                        _pendingZoneIndex = zone;
                        _activateInZone = zone;   // a Spell that stays on the field is activated in this zone
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
                bool Commit()
                {
                    var r = Cmd(DuelIntent.Of(DuelIntentKind.Activate, card));
                    if (r.Ok)
                    {
                        ClearCardSelection();
                        CloseInspectAfterPlay();
                    }
                    RefreshNow();
                    return r.Ok;
                }

                if (!TryArGesture(card, RulesZoneKind.FieldSpell, 0, faceDown: false, Commit))
                    Commit();
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
            var card = _selectedHand;
            var zoneIndex = _pendingZoneIndex;
            bool Commit()
            {
                var r = Cmd(DuelIntent.Of(kind, card, zoneIndex: zoneIndex));
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                }

                // Engine.Notify already marks dirty; force same-frame paint for snappy feel
                RefreshNow();
                return r.Ok;
            }

            // the AR hand carries the card to the pad and commits on contact; otherwise commit now
            if (zoneIndex >= 0 && TryArGesture(card, RulesZoneKind.Monster, zoneIndex, asSet, Commit)) return;
            Commit();
        }

        void DoSetST()
        {
            if (_engine.GameOver || _selectedHand == null) return;
            var card = _selectedHand;
            var zoneIndex = _pendingZoneIndex;
            bool Commit()
            {
                var r = Cmd(DuelIntent.Of(DuelIntentKind.SetSpellTrap, card, zoneIndex: zoneIndex));
                if (r.Ok)
                {
                    ClearCardSelection();
                    CloseInspectAfterPlay();
                }

                RefreshNow();
                return r.Ok;
            }

            if (zoneIndex >= 0 && TryArGesture(card, RulesZoneKind.SpellTrap, zoneIndex, faceDown: true, Commit))
                return;
            Commit();
        }

        /// <summary>The Spell/Trap Zone a zone-pad Activate chose (-1 = none); read once by <see cref="DoActivate"/>.</summary>
        int _activateInZone = -1;

        void DoActivate()
        {
            var zoneChoice = _activateInZone;
            _activateInZone = -1;
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
                // A one-shot Spell (Normal / Quick-Play / Ritual) is not set on a zone by the hand: the hand shows it
                // to the opponent and it activates at the end of the reveal; the engine picks the free S/T Zone.
                var handCard = _selectedHand;
                if (WRLDZ.Presentation.ArInteraction.ArSnapRules.RevealsFromHand(handCard) &&
                    _engine.CanActivateSpellTrap(_engine.Player, handCard, fromHand: true))
                {
                    bool CommitReveal()
                    {
                        var rr = Cmd(DuelIntent.Of(DuelIntentKind.Activate, handCard));
                        if (rr.Ok)
                        {
                            ClearCardSelection();
                            CloseInspectAfterPlay();
                        }

                        RefreshNow();
                        if (rr.Ok) MaybeRunAi();
                        return rr.Ok;
                    }

                    var ix = _arSpace?.Interaction;
                    if (ix != null && ix.TryRevealFromHandWithGesture(handCard, CommitReveal, ok => { if (!ok) RefreshNow(); }))
                    {
                        // the sheet closes now so the reveal is in view; the selection clears when it activates
                        CloseInspectAfterPlay();
                        return;
                    }
                }

                // A Spell that stays on the field (Continuous / Equip) activated from a zone pad goes to that zone: the
                // engine seats it there (preferred zone) and the hand carries it there, as a Set would be.
                if (zoneChoice >= 0 && handCard.Def != null && handCard.Def.IsSpell && !handCard.Def.IsFieldSpell &&
                    !WRLDZ.Presentation.ArInteraction.ArSnapRules.RevealsFromHand(handCard))
                {
                    var who = CommandWho() ?? _engine.Player;
                    bool CommitInZone()
                    {
                        _engine.SetPreferredSpellTrapZone(who, zoneChoice);
                        var rz = Cmd(DuelIntent.Of(DuelIntentKind.Activate, handCard));
                        if (rz.Ok)
                        {
                            ClearCardSelection();
                            CloseInspectAfterPlay();
                        }
                        else
                            _engine.ClearPreferredSpellTrapZone(who);

                        RefreshNow();
                        if (rz.Ok) MaybeRunAi();
                        return rz.Ok;
                    }

                    if (TryArGesture(handCard, RulesZoneKind.SpellTrap, zoneChoice, faceDown: false, CommitInZone)) return;
                    if (CommitInZone()) return;
                }

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
            if (QueueAdvanceWhileBeat(DuelIntentKind.EnterBattlePhase, "Battle Phase")) return;
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

        void DoMain1()
        {
            if (_engine == null || _engine.GameOver) return;
            ClearCardSelection();
            if (QueueAdvanceWhileBeat(DuelIntentKind.EnterMainPhase1, "Main Phase 1")) return;
            var r = Cmd(DuelIntent.Of(DuelIntentKind.EnterMainPhase1));
            if (!r.Ok && _announceFeedback != null)
            {
                _announceFeedback.text = r.Message ?? "Cannot enter Main Phase 1.";
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
            if (QueueAdvanceWhileBeat(DuelIntentKind.EnterMainPhase2, "Main Phase 2")) return;
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

            if (BeatBlocksDeclaration("attack")) return;
            _engine.TryAttack(who, _selectedAttacker, target);
            ClearCardSelection();
            Refresh();
            MaybeRunAi();
        }

        // ── Spell/Trap beat: the player's flow waits for the whole beat ─────────────────────

        /// <summary>
        /// Phase / turn advance during a Spell/Trap beat is queued and runs when the beat ends
        /// (card revealed → result → read → GY). Response windows are never blocked.
        /// </summary>
        bool QueueAdvanceWhileBeat(DuelIntentKind kind, string label)
        {
            if (_engine == null || _engine.IsAwaitingPlayerResponse) return false;
            if (!ActivationBeat.BlocksAdvance(out var name, out var left)) return false;
            _queuedAdvance = kind;
            _queuedAdvanceTurn = _engine.TurnNumber;
            if (_announceFeedback != null)
            {
                _announceFeedback.text = string.IsNullOrEmpty(name)
                    ? $"{label} after the card resolves ({left:0.0}s)…"
                    : $"{label} after {name} resolves ({left:0.0}s)…";
                _announceFeedback.color = WrldzTheme.Soft;
            }

            FreeUiKit.PlayClick();
            return true;
        }

        void TryRunQueuedAdvance()
        {
            if (_queuedAdvance == DuelIntentKind.None || _engine == null) return;
            if (_engine.GameOver || _engine.TurnNumber != _queuedAdvanceTurn ||
                _engine.TurnPlayer != (CommandWho() ?? _engine.Player))
            {
                _queuedAdvance = DuelIntentKind.None;
                return;
            }

            if (ActivationBeat.BlocksAdvance(out _, out _)) return;
            if (_engine.IsAwaitingResponse || _engine.IsAwaitingEffectTarget || _engine.IsProcessingAction)
                return;
            var kind = _queuedAdvance;
            _queuedAdvance = DuelIntentKind.None;
            switch (kind)
            {
                case DuelIntentKind.EnterBattlePhase: DoBattle(); break;
                case DuelIntentKind.EnterMainPhase1: DoMain1(); break;
                case DuelIntentKind.EnterMainPhase2: DoMain2(); break;
                case DuelIntentKind.EndTurn: DoEndTurn(); break;
            }
        }

        /// <summary>New attack declarations wait for the Spell/Trap beat on screen.</summary>
        bool BeatBlocksDeclaration(string what)
        {
            if (!ActivationBeat.BlocksAdvance(out var name, out var left)) return false;
            if (_announceFeedback != null)
            {
                _announceFeedback.text = string.IsNullOrEmpty(name)
                    ? $"Wait — a card is resolving ({left:0.0}s) before you {what}."
                    : $"Wait — {name} is resolving ({left:0.0}s) before you {what}.";
                _announceFeedback.color = WrldzTheme.Danger;
            }

            return true;
        }

        /// <summary>
        /// Name call-out for every Spell/Trap activation: SPELL / TRAP + card name + controller, up for
        /// the card's art-readable window (activation → fade). Screen-space, so it reads in AR and
        /// in the digital layout alike (the arena glass callout, ArEffectCallout, stays retired).
        /// </summary>
        void UpdateActivationBanner()
        {
            var on = ActivationBeat.TryGetBanner(out var name, out var isTrap, out var playerSide,
                out var depth, out var serial);
            // On the AR stage Mr. Referobot names every activation ("Trap card, Tornado Wall!") as its art turns
            // readable, so the banner only repeated him. It stays for a live chain, where it adds the link number.
            if (on && _arSpace != null && _referobot != null && depth < 1)
                on = false;
            if (!on)
            {
                if (_beatBannerGroup != null)
                {
                    _beatBannerGroup.alpha = Mathf.MoveTowards(_beatBannerGroup.alpha, 0f,
                        Time.unscaledDeltaTime * 4f);
                    if (_beatBannerGroup.alpha <= 0.001f)
                        _beatBannerGroup.gameObject.SetActive(false);
                }

                _beatBannerSerial = -1;
                return;
            }

            EnsureActivationBanner();
            if (_beatBannerGroup == null) return;
            if (!_beatBannerGroup.gameObject.activeSelf)
                _beatBannerGroup.gameObject.SetActive(true);
            _beatBannerGroup.transform.SetAsLastSibling();
            if (serial != _beatBannerSerial || depth != _beatBannerDepth)
            {
                var sameCard = serial == _beatBannerSerial;
                _beatBannerSerial = serial;
                _beatBannerDepth = depth;
                var kind = isTrap ? "TRAP CARD" : "SPELL CARD";
                var who = playerSide ? "YOU" : "OPPONENT";
                // depth = engine chain link number, only while a multi-link chain is live.
                var chain = depth >= 1 ? $"  ·  CHAIN LINK {depth}" : "";
                _beatBannerKind.text = $"{who} ACTIVATE{(playerSide ? "" : "S")}  ·  {kind}{chain}";
                _beatBannerName.text = string.IsNullOrEmpty(name) ? kind : name;
                _beatBannerBg.color = isTrap
                    ? new Color(0.46f, 0.10f, 0.34f, 0.86f)
                    : new Color(0.05f, 0.36f, 0.30f, 0.86f);
                // Only a new card re-pops the banner; a chain-link label added to the same card does not.
                if (!sameCard) _beatBannerGroup.alpha = 0f;
            }

            _beatBannerGroup.alpha = Mathf.MoveTowards(_beatBannerGroup.alpha, 1f, Time.unscaledDeltaTime * 6f);
        }

        void EnsureActivationBanner()
        {
            if (_beatBannerGroup != null) return;
            var parent = _uiRoot != null ? _uiRoot : (transform.childCount > 0 ? transform.GetChild(0) : transform);
            if (parent == null) return;
            var bg = CreatePanel(parent, "ActivationBanner", new Color(0.05f, 0.36f, 0.30f, 0.86f));
            Place(bg, 0.10f, 0.66f, 0.90f, 0.75f);
            _beatBannerBg = bg.GetComponent<Image>();
            _beatBannerGroup = bg.gameObject.AddComponent<CanvasGroup>();
            _beatBannerGroup.interactable = false;
            _beatBannerGroup.blocksRaycasts = false;
            _beatBannerGroup.alpha = 0f;

            _beatBannerKind = CreateText(bg, "Kind", 15, TextAnchor.UpperCenter, FontStyle.Bold);
            Place(_beatBannerKind.rectTransform, 0.02f, 0.60f, 0.98f, 0.96f);
            _beatBannerKind.color = new Color(1f, 0.92f, 0.62f, 1f);
            _beatBannerName = CreateText(bg, "Name", 30, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            Place(_beatBannerName.rectTransform, 0.02f, 0.04f, 0.98f, 0.64f);
            _beatBannerName.color = Color.white;
            _beatBannerName.resizeTextForBestFit = true;
            _beatBannerName.resizeTextMinSize = 14;
            _beatBannerName.resizeTextMaxSize = 30;
        }

        void DoEndTurn()
        {
            if (_engine.GameOver || (_aiRunning && IsAiOpponent)) return;
            ClearCardSelection();
            if (QueueAdvanceWhileBeat(DuelIntentKind.EndTurn, "End Turn")) return;
            var r = Cmd(DuelIntent.Of(DuelIntentKind.EndTurn));
            Refresh();
            if (r.Ok)
                StartOpponentTurnIfNeeded();
        }

        /// <summary>
        /// Restart exists only in practice / lab duels (owner rule 2026-09-23 #3): practice/lab launch
        /// kinds, the Boot lab TEST DUEL, and match-less Editor / Development lab duels.
        /// </summary>
        bool RestartAllowed
        {
            get
            {
                var session = AppSession.Ensure();
                if (session.TestDuelMode) return true;
                var match = session.PendingArMatch;
                return match != null ? match.AllowsRestart : WrldzLab.LabDoorsEnabled;
            }
        }

        bool DuelUnfinished => _engine != null && !_engine.GameOver;

        /// <summary>
        /// On the AR stage Mr. Referobot speaks through the caption over his head (<see cref="ReferobotWorldBody"/>),
        /// so the phone chip would show every line a second time. It stays (it drives those lines) but is invisible.
        /// </summary>
        void HideReferobotChipOnStage()
        {
            if (_referobot == null || _arSpace == null) return;
            // not `??`: a missing component is Unity's fake null, which C# null-coalescing does not see
            var cg = _referobot.GetComponent<CanvasGroup>();
            if (cg == null) cg = _referobot.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        /// <summary>Map / Leave: an unfinished duel asks first, because leaving is a forfeit loss.</summary>
        void RequestLeaveDuel()
        {
            if (!DuelUnfinished)
            {
                LeaveDuelToMap();
                return;
            }

            ShowLeaveConfirm(restart: false);
        }

        void DoRestart()
        {
            var acc = AppSession.Ensure()?.Account;
            if (acc != null && acc.deactivated)
            {
                LeaveDuelToMap();
                return;
            }

            if (!RestartAllowed)
            {
                // Not a practice / lab duel: the only way out is Map (forfeit when unfinished).
                RequestLeaveDuel();
                return;
            }

            if (DuelUnfinished)
            {
                ShowLeaveConfirm(restart: true);
                return;
            }

            RestartNow();
        }

        void RestartNow()
        {
            // A practice / lab restart is a new duel: new claim token (UI) and new result id (config).
            _duelSerial++;
            var match = AppSession.Ensure().PendingArMatch;
            if (match != null && match.AllowsRestart)
                match.BeginNewDuel();

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

        /// <summary>
        /// Confirmed Map / Restart on an unfinished duel: record the forfeit loss first (streaks,
        /// stakes and Shadow Game rules apply through ProgressionService), then concede the engine
        /// duel, then leave or restart. Practice / lab / same-device duels have nothing at stake.
        /// </summary>
        void ForfeitUnfinishedDuel()
        {
            if (!DuelUnfinished) return;
            var turns = _engine.TurnNumber;
            var session = AppSession.Ensure();
            var acc = session.Account;
            var match = session.PendingArMatch;
            if (_rewardsClaimedSerial != _duelSerial && acc != null && match != null && !session.TestDuelMode &&
                !match.IsPracticeOrLab && !match.IsSameDeviceHotseat)
            {
                _rewardsClaimedSerial = _duelSerial;
                try
                {
                    var r = ProgressionService.ForfeitDuel(acc, match, turns);
                    _lastRewardLine = r.SummaryLine ?? "";
                    Debug.Log("[WRLDZ] Left an unfinished duel → forfeit: " + _lastRewardLine);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] Forfeit record failed: " + ex.Message);
                }
            }

            _engine.Concede(_engine.Player, "left duel");
        }

        void ShowLeaveConfirm(bool restart)
        {
            EnsureLeaveConfirm();
            if (_leaveConfirm == null)
            {
                // No UI root to host the dialog: never leave silently without the forfeit.
                ForfeitUnfinishedDuel();
                if (restart) RestartNow(); else LeaveDuelToMap();
                return;
            }

            var session = AppSession.Ensure();
            var match = session.PendingArMatch;
            var stakes = match != null && !session.TestDuelMode && !match.IsPracticeOrLab &&
                         !match.IsSameDeviceHotseat;
            _leaveConfirmTitle.text = restart ? "RESTART THIS DUEL?" : "LEAVE THIS DUEL?";
            if (stakes)
            {
                var shadow = SoulFractureService.IsShadowGame(match);
                _leaveConfirmBody.text =
                    "Leaving now is a FORFEIT. It counts as a loss:\nyour streak resets" +
                    (shadow ? " and the Shadow Game takes its stake." : ".") +
                    "\n\nKeep dueling to finish the match.";
            }
            else
            {
                _leaveConfirmBody.text = restart
                    ? "This duel ends as a loss and a new one starts.\nNothing is at stake in this duel."
                    : "This duel ends as a loss.\nNothing is at stake in this duel.";
            }

            _leaveConfirmAction = () =>
            {
                ForfeitUnfinishedDuel();
                if (restart) RestartNow();
                else LeaveDuelToMap();
            };
            _leaveConfirm.SetActive(true);
            _leaveConfirm.transform.SetAsLastSibling();
        }

        void HideLeaveConfirm()
        {
            _leaveConfirmAction = null;
            if (_leaveConfirm != null) _leaveConfirm.SetActive(false);
        }

        void EnsureLeaveConfirm()
        {
            if (_leaveConfirm != null) return;
            var parent = _uiRoot;
            if (parent == null) return;

            var dim = CreatePanel(parent, "LeaveConfirm", new Color(0.01f, 0.02f, 0.05f, 0.62f));
            Stretch(dim);
            var dimImg = dim.GetComponent<Image>();
            if (dimImg != null) dimImg.raycastTarget = true; // modal: blocks the board underneath

            var box = FloatingPanel.Create(dim, "Box", goldEdge: true);
            FloatingPanel.Place(box, 0.08f, 0.34f, 0.92f, 0.66f);
            var boxImg = box.GetComponent<Image>();
            if (boxImg != null) boxImg.color = new Color(0.04f, 0.06f, 0.10f, 0.96f);

            _leaveConfirmTitle = CreateText(box, "Title", 30, TextAnchor.MiddleCenter, FontStyle.Bold, title: true);
            _leaveConfirmTitle.color = DuelystUi.Gold;
            Place(_leaveConfirmTitle.rectTransform, 0.05f, 0.74f, 0.95f, 0.94f);
            _leaveConfirmBody = CreateText(box, "Body", 18, TextAnchor.MiddleCenter, FontStyle.Normal);
            _leaveConfirmBody.color = DuelystUi.TextCream;
            Place(_leaveConfirmBody.rectTransform, 0.06f, 0.30f, 0.94f, 0.74f);

            var row = CreatePanel(box, "Buttons", new Color(0, 0, 0, 0));
            Place(row, 0.06f, 0.05f, 0.94f, 0.26f);
            AddHLayout(row);
            CreateButton(row, "KEEP DUELING", () =>
            {
                FreeUiKit.PlayClick();
                HideLeaveConfirm();
            }, GbaTheme.CmdSafe);
            CreateButton(row, "FORFEIT", () =>
            {
                var act = _leaveConfirmAction;
                HideLeaveConfirm();
                act?.Invoke();
            }, GbaTheme.CmdDanger);

            _leaveConfirm = dim.gameObject;
            _leaveConfirm.SetActive(false);
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
            UpdateButtonStates(); // lock UI (Mr. Referobot calls the opponent's turn; no pop-up)
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
            if (_engine == null) return;
            if (_engine.GameOver)
            {
                // The final beat (a lethal Spell/Trap) still plays: name banner, then the held LP /
                // 2D field at its result gate. The overlay waits for it (GameOverAfterBeatCo).
                UpdateActivationBanner();
                if (_refreshOnResultGate && !ActivationBeat.AnyResultPending)
                {
                    _refreshOnResultGate = false;
                    Refresh();
                }

                return;
            }

            // Block AI / combat recovery during pre-duel cinematic
            if (_engine.OpeningSequenceActive)
            {
                UpdatePresentationHoldUi();
                return;
            }

            UpdatePresentationHoldUi();
            UpdateActivationBanner();

            if (_refreshOnResultGate && !ActivationBeat.AnyResultPending)
            {
                _refreshOnResultGate = false;
                Refresh();
            }

            TryRunQueuedAdvance();

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
