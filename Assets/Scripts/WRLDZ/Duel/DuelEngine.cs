using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Yu-Gi-Oh! TCG rules engine (structural + registered official effects).
    /// Card text authority: <see cref="OfficialCardAuthority"/> (card DB = Konami/Yugipedia text).
    /// Unregistered effects are never invented — activation is refused.
    /// AR / UI only visualize state after engine commits.
    /// </summary>
    public class DuelEngine : IDuelEngine
    {
        public DuelistState Player { get; private set; }
        public DuelistState Opponent { get; private set; }
        public DuelistState TurnPlayer { get; private set; }
        public DuelPhase Phase { get; private set; } = DuelPhase.Draw;
        public int TurnNumber { get; private set; }
        public bool GameOver { get; private set; }
        public DuelistState Winner { get; private set; }

        /// <summary>How the duel ended (<see cref="DuelEndReason.None"/> while it is running).</summary>
        public DuelEndReason EndReason { get; private set; }

        /// <summary>Both players reached 0 LP at the same time: no winner (Rulebook Draw).</summary>
        public bool IsDraw => GameOver && EndReason == DuelEndReason.Draw;

        /// <summary>The duelist who conceded (<see cref="Concede"/>), or null.</summary>
        public DuelistState ConcededBy { get; private set; }

        /// <summary>
        /// Mirror every duel log line to the Unity console. On in the Editor and Development builds;
        /// off in release builds, where each Debug.Log also captures a stack trace (PERF-13).
        /// </summary>
        public static bool MirrorToUnityLog =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>Battle Phase step when <see cref="Phase"/> == Battle.</summary>
        public BattleStep BattleStep { get; private set; } = BattleStep.None;

        /// <summary>Damage Step sub-step when in Damage Step.</summary>
        public DamageSubStep DamageSubStep { get; private set; } = DamageSubStep.None;

        /// <summary>Official Chain stack.</summary>
        public ChainStack Chain { get; } = new();

        /// <summary>Fast Effect Timing / priority window.</summary>
        public FastEffectTiming FastEffects { get; } = new();

        /// <summary>Continuous effects board (registered scripts only).</summary>
        public ContinuousEffectBoard Continuous { get; } = new();

        /// <summary>
        /// The match's only randomness (R1): deck / hand shuffles, coin tosses, die rolls, random picks.
        /// Seeded from <see cref="Config"/> on each start. Tests queue faces for determinism.
        /// </summary>
        public DuelRng Rng { get; } = new();

        DuelMatchConfig _config = new();
        bool _configProvided;

        /// <summary>
        /// Seed, ERAZ band and rule flags of this match (never PlayerPrefs / AppSession inside rules).
        /// Set before <see cref="StartDuel(CardDatabase, DeckFile, DeckFile)"/>; an engine that was never given
        /// one runs on defaults (fresh seed, cards_db text, flags off) and leaves <see cref="DuelMatchContext"/> alone.
        /// </summary>
        public DuelMatchConfig Config
        {
            get => _config;
            set
            {
                _config = value ?? new DuelMatchConfig();
                _configProvided = value != null;
            }
        }

        /// <summary>Time source for presentation stamps, the review log and snapshots (R4). Unity default.</summary>
        public IClock Clock { get; set; } = UnityDuelClock.Instance;

        /// <summary>Diagnostics sink for this engine (R4): the <see cref="MirrorToUnityLog"/> mirror. Unity default.</summary>
        public IDuelLog Diagnostics { get; set; } = UnityDuelLog.Instance;

        readonly List<IDuelEventSink> _extraSinks = new();

        /// <summary>Extra structured event consumer, bound next to <see cref="ReviewLog"/> on every start.</summary>
        public void AddEventSink(IDuelEventSink sink)
        {
            if (sink != null && !_extraSinks.Contains(sink)) _extraSinks.Add(sink);
        }

        /// <summary>Make this match's config current on the calling thread (hosts that multiplex matches).</summary>
        public DuelMatchContext.Scope EnterMatchScope() =>
            DuelMatchContext.Enter(_configProvided ? _config : DuelMatchContext.Current);

        /// <summary>Optional table overlay (Duelist Kingdom: no directs, 2000 LP).</summary>
        public DuelRulesOverlay Overlay { get; set; }

        /// <summary>Last rules snapshot for multiplayer sync.</summary>
        public RulesGameStateSnapshot LastRulesSnapshot { get; private set; }

        /// <summary>
        /// Who takes turn 1. The player unless the pre-duel coin toss / dice roll gave it to the opponent
        /// (<see cref="SetFirstPlayer"/>, played in <c>PreDuelCinematic</c>; decided silently when the cinematic is skipped).
        /// </summary>
        public DuelistState FirstPlayer => _opponentFirst ? Opponent : Player;
        bool _opponentFirst;
        /// <summary>The opponent (Player 2 / NPC) takes turn 1.</summary>
        public bool OpponentGoesFirst => _opponentFirst;

        /// <summary>
        /// Decide turn 1 before it begins (TurnNumber 0: the opening sequence is still running, or the engine was
        /// started with <c>cinematicOpening</c>). False once a turn has started.
        /// </summary>
        public bool SetFirstPlayer(bool opponentFirst)
        {
            if (TurnNumber != 0 || GameOver || Player == null || Opponent == null) return false;
            _opponentFirst = opponentFirst;
            Log($"First turn: {(opponentFirst ? Opponent.Name : Player.Name)}.");
            return true;
        }

        /// <summary>
        /// Hotseat / nearby PvP — both sides are human. Disables AI auto-response and
        /// treats Opponent response windows as player-controlled.
        /// Set by DuelBootstrap from <c>ArDuelMatchConfig.IsHumanOpponent</c>.
        /// </summary>
        public bool HumanVsHuman { get; set; }

        /// <summary>
        /// Pre-duel cinematic: decks shuffled, hands empty, turn not started.
        /// UI/AR run deploy + shuffle + draw-from-deck gesture before play.
        /// </summary>
        public bool OpeningSequenceActive { get; private set; }

        /// <summary>True when player must move hand to deck zone to draw opening hand.</summary>
        public bool AwaitingOpeningDrawGesture { get; private set; }

        /// <summary>True when this duelist is controlled by a human (not local AI).</summary>
        public bool IsHumanControlled(DuelistState who) =>
            who != null && (who.IsPlayer || HumanVsHuman);

        /// <summary>
        /// Rulebook: first player cannot conduct Battle Phase on their first turn.
        /// </summary>
        public bool CanConductBattlePhase
        {
            get
            {
                if (GameOver || TurnPlayer == null) return false;
                if (TurnNumber == 1 && TurnPlayer == FirstPlayer) return false;
                // Soul Exchange oath: "You cannot conduct your Battle Phase the turn you activate this card."
                if (TextEffects.TributeSummonsRuntime.BattlePhaseForbidden(this, TurnPlayer)) return false;
                // Weather Report: "conduct your Battle Phase twice" — a second Battle Phase from Main Phase 2.
                if (Phase == DuelPhase.Main2)
                    return TextEffects.SpellTrapDestroyedResponseRuntime.SecondBattlePhaseAvailable(this, TurnPlayer);
                return Phase == DuelPhase.Main1; // enter Battle only from MP1
            }
        }

        public bool InMainPhase => Phase == DuelPhase.Main1 || Phase == DuelPhase.Main2;

        public event Action<string> OnLog;
        public event Action OnStateChanged;
        public event Action OnGameOver;

        /// <summary>
        /// Structured review log (visible in duel UI + JSONL export for AI learning).
        /// Bound by UI on StartDuel; may be null before Bind.
        /// </summary>
        public DuelReviewLog ReviewLog { get; private set; }

        int _nextInstanceId = 1;
        CardDatabase _db;
        DeckFile _playerDeckFile;
        DeckFile _aiDeckFile;

        /// <summary>UI-selected tributes for next NS/Set of a high-level monster.</summary>
        public List<CardInstance> PendingTributes { get; } = new();

        /// <summary>Spell/Trap waiting for a target choice (e.g. Monster Reborn).</summary>
        public PendingActivation PendingActivation { get; private set; }

        /// <summary>
        /// UI-chosen Monster Zone for the next Special Summon (−1 = first empty).
        /// Cleared after the summon commits or the target window cancels.
        /// </summary>
        int _pendingSpecialSummonZone = -1;

        /// <summary>
        /// UI-chosen battle position for the next Special Summon. Null keeps the
        /// caller’s position (Attack unless the card text locks Defense).
        /// </summary>
        BattlePosition? _pendingSpecialSummonPosition;

        /// <summary>
        /// End Phase trigger (Ectoplasmer tribute) paused the turn switch until the
        /// turn player picks. Null when End Phase is not waiting on a trigger pick.
        /// </summary>
        DuelistState _endTurnPausedFor;
        DuelistState _standbyPausedFor;
        bool _inherentProcedureSummon;

        /// <summary>
        /// SummonNegation batch (v141): a Summon waiting on its summon-negation window
        /// (<see cref="ResponseTiming.SummonNegation"/>). Its success steps run when the window closes
        /// unless Solemn Judgment / Horn of Heaven negated it.
        /// </summary>
        sealed class SummonInProgress
        {
            public DuelistState Summoner;
            public CardInstance Summoned;
            public SummonKind Kind;
            public List<CardInstance> Tributes;
            /// <summary>
            /// Who each Tribute pays out to (Zolga). Null: the summoner. Integration seam for the TributeSummons
            /// batch (Soul Exchange): its owner map reaches <see cref="CompleteNormalSummon"/> through here.
            /// </summary>
            public Dictionary<CardInstance, DuelistState> TributeOwners;
            /// <summary>The Tributes' held "sent from the field to the GY" triggers (Sangan, Witch).</summary>
            public List<(DuelistState owner, CardInstance card)> HeldGyTriggers;
            public bool FromGraveyard;
            public bool Negated;
        }

        SummonInProgress _summonInProgress;
        /// <summary>Set by <see cref="TrySpecialSummonProcedure"/>: the next Special Summon is by procedure (negatable).</summary>
        bool _procedureSummonNegatable;
        /// <summary>SummonNegation batch (v141, Magic Drain): the resolving link whose waiver a player is choosing.</summary>
        ChainLink _chainPausedForWaiver;
        /// <summary>
        /// TypeDeclaration batch (v142): the chain is paused after a resolving link whose effect waits on a
        /// player's choice (<see cref="PendingActivation.HoldsChainResolution"/>, Earthshaker's Attributes); the
        /// rest of the chain resolves in <see cref="ResumeChainAfterChoice"/>.
        /// </summary>
        bool _chainHeldForChoice;

        /// <summary>
        /// SummonNegation batch (v141): while a Normal / Tribute Summon sends its Tributes to the GY, their
        /// "sent from the field to the GY" triggers (Sangan, Witch of the Black Forest) are held. They activate
        /// once the Summon is negated or succeeds, as in the TCG, so a Tribute's search pick can no longer
        /// keep the summon-negation window from opening.
        /// </summary>
        bool _holdMaterialGyTriggers;
        readonly List<(DuelistState owner, CardInstance card)> _heldMaterialGyTriggers = new();

        /// <summary>
        /// Toons batch (v142): the held Tribute triggers of a card's own Special Summon procedure
        /// (<see cref="SpecialSummonByOwnHandProcedure"/>), handed to that Special Summon's negation window by
        /// <see cref="SpecialSummonToField(DuelistState, CardInstance, BattlePosition, bool, SummonKind, int)"/>.
        /// </summary>
        List<(DuelistState owner, CardInstance card)> _procedureHeldGyTriggers;

        /// <summary>The monster whose Summon is in its summon-negation window (null otherwise).</summary>
        public CardInstance SummonAwaitingSuccess => _summonInProgress?.Summoned;

        /// <summary>
        /// True for the monster whose Summon is still in its summon-negation window. It is in its zone, but its
        /// Summon has not succeeded: it is not yet a face-up card on the field, so continuous-effect scans
        /// (auras, and locks such as Jinzo's) must skip it as a source.
        /// </summary>
        public bool IsSummonPending(CardInstance card) =>
            card != null && _summonInProgress != null && ReferenceEquals(card, _summonInProgress.Summoned);

        /// <summary>Invasion of Flames: Traps cannot answer the Normal Summon still in this window.</summary>
        public bool SummonResponseTrapsSealed { get; private set; }
        int _positionNotifyDepth;

        /// <summary>Open response window (attack declared / summon) for Speed 2 activations.</summary>
        public PendingResponse PendingResponse { get; private set; }
        public bool DeferSpellTrapResolution { get; set; }
        public bool IsResolvingChain { get; private set; }

        /// <summary>Append-only history for when/if and lingering legality.</summary>
        public DuelGameEventLog GameEvents { get; } = new();

        /// <summary>
        /// Combat window a seated chain must resume after pass/pass resolve.
        /// Activation seats the link; resolution is not the same operation.
        /// </summary>
        public ResponseTiming ResumeAfterChain { get; set; }
        public bool ChainNegatedAttack { get; set; }
        public bool ChainEndedBattle { get; set; }

        /// <summary>Attack declared but not yet resolved (animation + reaction window).</summary>
        public CardInstance DeclaredAttacker { get; private set; }
        public CardInstance DeclaredAttackTarget { get; private set; }
        public DuelistState DeclaredAttackingPlayer { get; private set; }
        public bool HasDeclaredAttack => DeclaredAttacker != null;

        /// <summary>
        /// Live combat presentation (attack charge / summon appear). Does not pause the game —
        /// reaction windows share this animation's impact time.
        /// </summary>
        public ActiveCombatPresentation ActivePresentation { get; private set; }

        public bool IsAwaitingEffectTarget => PendingActivation != null;
        public bool IsAwaitingResponse => PendingResponse != null;

        DuelistState _queuedDamageTaken;
        readonly List<(DuelistState summoner, CardInstance summoned, SummonKind procedure)> _queuedSummons = new();
        DuelistState _queuedOwnSummonResponder;
        DuelistState _queuedOwnSummoner;
        CardInstance _queuedOwnSummoned;
        DuelistState _fieldSummonTriggerSummoner;
        CardInstance _fieldSummonTriggerSummoned;
        /// <summary>Human must choose Pass / Activate (P1, or either side in hotseat PvP).</summary>
        public bool IsAwaitingPlayerResponse =>
            PendingResponse != null && IsHumanControlled(PendingResponse.Responder);

        /// <summary>
        /// True while <see cref="DuelCommandService"/> is executing one intent.
        /// Prevents re-entrant / double-tap actions (Yugioh Shorts server action lock pattern).
        /// </summary>
        public bool IsProcessingAction { get; internal set; }

        /// <summary>
        /// Set while resuming an activation after the human confirmed an LP cost
        /// that would leave 0. Prevents a second are-you-sure prompt.
        /// </summary>
        public bool LpZeroCostConfirmed { get; set; }

        /// <summary>Any modal engine state that blocks free actions.</summary>
        public bool IsBusy =>
            IsProcessingAction ||
            IsAwaitingEffectTarget || IsAwaitingResponse ||
            _deferredBattle != null ||
            _deferredDamage != null;

        /// <summary>
        /// After damage calc, Flip effects may open a target window (Man-Eater Bug).
        /// Battle destruction is deferred until the Flip target resolves/cancels.
        /// </summary>
        class DeferredBattleDestruction
        {
            public DuelistState AttackingPlayer;
            public DuelistState DefendingPlayer;
            public CardInstance Attacker;
            public CardInstance Defender;
            public bool DestroyAttacker;
            public bool DestroyDefender;
            public bool PiercingApplied;
            public CardInstance FlippedByBattle;
        }

        DeferredBattleDestruction _deferredBattle;

        /// <summary>
        /// Paused at Damage Calculation for hand Quick Effects (Kuriboh).
        /// Finish via Pass or Kuriboh activation.
        /// </summary>
        class DeferredDamageCalculation
        {
            public DuelistState AttackingPlayer;
            public DuelistState DefendingPlayer;
            public CardInstance Attacker;
            public CardInstance TargetOrNull;
            public CardInstance FlippedByBattle;
            /// <summary>BattleBoosts batch (v161): the defender's window was offered (it follows the attacking player's).</summary>
            public bool DefenderWindowOffered;
        }

        DeferredDamageCalculation _deferredDamage;

        public void SetPendingActivation(PendingActivation pending)
        {
            // FlipMassEffects (v142): an open Cyber Jar / Morphing Jar #2 / Spear Cretin choice holds cards outside
            // every zone; replacing it first makes that choice automatically, so nothing is lost.
            if (PendingActivation != null && !ReferenceEquals(PendingActivation, pending))
                TextEffects.FlipMassEffectsRuntime.SettleDisplacedPick(this);
            PendingActivation = pending;
        }

        public void ClearPendingActivation()
        {
            if (PendingActivation != null)
                TextEffects.FlipMassEffectsRuntime.SettleDisplacedPick(this);
            PendingActivation = null;
        }
        public void SetPendingResponse(PendingResponse pending) => PendingResponse = pending;
        public void ClearPendingResponse() => PendingResponse = null;
        public void ClearPresentation() => ActivePresentation = null;

        // ── IDuelEngine AR validation façade (no mutation) ──
        public RulesValidator.ActionVerdict ValidatePlacement(
            DuelistState who, CardInstance card, RulesZoneKind zone, int zoneIndex, bool preferSet) =>
            RulesValidator.ValidateDiskPlacement(this, who, card, zone, zoneIndex, preferSet);

        public RulesValidator.ActionVerdict ValidateActivation(
            DuelistState who, CardInstance card, bool fromHand) =>
            RulesValidator.ValidateActivation(this, who, card, fromHand);

        public RulesValidator.ActionVerdict ValidateAttack(
            DuelistState who, CardInstance attacker, CardInstance targetOrNull) =>
            RulesValidator.ValidateAttack(this, who, attacker, targetOrNull);

        public IGameStateView AsView() => new GameStateView(this);

        public void StartDuel(CardDatabase db, DeckFile playerDeck, DeckFile aiDeck) =>
            StartDuel(db, playerDeck, aiDeck, cinematicOpening: false);

        /// <param name="cinematicOpening">
        /// When true: shuffle only — no opening draw / BeginTurn until
        /// <see cref="CompleteOpeningDrawFromDeck"/> (pre-duel disk cinematic).
        /// </param>
        public void StartDuel(CardDatabase db, DeckFile playerDeck, DeckFile aiDeck,
            bool cinematicOpening) =>
            StartDuel(db, playerDeck, aiDeck, cinematicOpening, opponentFirst: false);

        /// <param name="opponentFirst">
        /// Turn 1 goes to the opponent (the pre-duel coin toss / dice roll, already decided when the cinematic is
        /// skipped). With a cinematic opening the result is set later through <see cref="SetFirstPlayer"/>.
        /// </param>
        public void StartDuel(CardDatabase db, DeckFile playerDeck, DeckFile aiDeck,
            bool cinematicOpening, bool opponentFirst)
        {
            _db = db;
            _opponentFirst = opponentFirst;
            _playerDeckFile = playerDeck;
            _aiDeckFile = aiDeck;
            _nextInstanceId = 1;
            PendingTributes.Clear();
            PendingActivation = null;
            ClearPendingSpecialSummonPlacement();
            _endTurnPausedFor = null;
            PendingResponse = null;
            _deferredBattle = null;
            _deferredDamage = null;
            ResumeAfterChain = ResponseTiming.None;
            ChainNegatedAttack = false;
            ChainEndedBattle = false;
            _summonInProgress = null;
            _procedureSummonNegatable = false;
            _chainPausedForWaiver = null;
            _chainHeldForChoice = false;
            _holdMaterialGyTriggers = false;
            _heldMaterialGyTriggers.Clear();
            _procedureHeldGyTriggers = null;
            GameEvents.Clear();
            IsProcessingAction = false;
            OpeningSequenceActive = false;
            AwaitingOpeningDrawGesture = false;
            ClearDeclaredAttack();
            ClearPresentation();

            // This match's rules inputs become current on this thread (era for card-text compiles).
            if (_configProvided) DuelMatchContext.Set(_config);
            // R1: one seeded stream per match — a configured seed, an explicit Rng.Reseed made just
            // before this start (tests / trainers), or a fresh seed (every restart reshuffles).
            if (_config.Seed.HasValue) Rng.Reseed(_config.Seed.Value);
            else if (!Rng.ConsumeExplicitSeed()) Rng.Reseed(DuelRng.NewSeed());
            Rng.ConsumeExplicitSeed();

            // Fresh review session each duel (AI learning / visible log); never draws from Rng.
            ReviewLog?.Unbind();
            ReviewLog = new DuelReviewLog();
            ReviewLog.Bind(this);
            foreach (var sink in _extraSinks)
            {
                sink.Unbind();
                sink.Bind(this);
            }

            var p1Name = HumanVsHuman ? "Player 1" : (playerDeck?.name ?? "You");
            var p2Name = HumanVsHuman ? "Player 2" : (aiDeck?.name ?? "Opponent");
            Player = new DuelistState(p1Name, true)
            {
                LifePoints = TcgRules.StartingLifePoints,
                Engine = this // RedirectAndReplace batch (v142): Banisher of the Light on engine-less GY helpers
            };
            Opponent = new DuelistState(p2Name, false)
            {
                LifePoints = TcgRules.StartingLifePoints,
                Engine = this
            };

            Player.Deck = FilterPlayable(CardDatabase.BuildMainPile(playerDeck));
            Opponent.Deck = FilterPlayable(CardDatabase.BuildMainPile(aiDeck));
            Player.ExtraDeck = BuildExtraPile(playerDeck);
            Opponent.ExtraDeck = BuildExtraPile(aiDeck);
            Player.Banished = new List<CardInstance>();
            Opponent.Banished = new List<CardInstance>();
            Rng.Shuffle(Player.Deck);
            Rng.Shuffle(Opponent.Deck);

            TurnNumber = 0;
            GameOver = false;
            Winner = null;
            EndReason = DuelEndReason.None;
            ConcededBy = null;
            _lpLossPending = false;
            _queuedSummons.Clear();
            _queuedDamageTaken = null;
            _standbyPausedFor = null;
            Phase = DuelPhase.Draw;
            TurnPlayer = Player;

            Chain.Clear();
            FastEffects.Close();
            Continuous.Clear();
            BattleStep = BattleStep.None;
            DamageSubStep = DamageSubStep.None;

            Log(
                $"Duel start — LP {TcgRules.StartingLifePoints}, hand {TcgRules.StartingHandSize}, " +
                $"decks you {Player.DeckCount} / AI {Opponent.DeckCount} " +
                $"(Extra {Player.ExtraDeck?.Count ?? 0}/{Opponent.ExtraDeck?.Count ?? 0}).");
            if (Player.DeckCount < TcgRules.MainDeckMin || Player.DeckCount > TcgRules.MainDeckMax)
                Log($"Warning: your deck size {Player.DeckCount} is outside {TcgRules.MainDeckMin}–{TcgRules.MainDeckMax}.");

            DuelPresentationPacer.Clear();
            CardShatterPresentation.Clear();
            Presentation.ArInteraction.SpellActivationPresentation.Clear();
            CoinDicePresentation.Clear();

            if (cinematicOpening)
            {
                OpeningSequenceActive = true;
                AwaitingOpeningDrawGesture = false;
                Log("Pre-duel · decks shuffled — deploy disk, then draw from the DECK zone.");
                Notify();
                return;
            }

            Draw(Player, TcgRules.StartingHandSize, silent: false);
            Draw(Opponent, TcgRules.StartingHandSize, silent: true);
            BeginTurn(FirstPlayer);
            Notify();
        }

        /// <summary>Lab ocg path: occupancy lives in <see cref="Ocg.OcgBoardView"/>; this engine is a view only.</summary>
        public void AdoptExternalView(DuelistState player, DuelistState opponent)
        {
            Player = player;
            Opponent = opponent;
            TurnPlayer = player;
            Phase = DuelPhase.Draw;
            TurnNumber = 1;
            GameOver = false;
            Winner = null;
            EndReason = DuelEndReason.None;
            _drawByCardEffect = false;
            _forcedBattleReturnPhase = null;
            ConcededBy = null;
            OpeningSequenceActive = false;
            AwaitingOpeningDrawGesture = false;
        }

        public void SyncViewMeta(DuelPhase phase, int turn, DuelistState turnPlayer, bool gameOver, DuelistState winner)
        {
            Phase = phase;
            TurnNumber = turn;
            if (turnPlayer != null) TurnPlayer = turnPlayer;
            GameOver = gameOver;
            Winner = winner;
        }

        /// <summary>Call after disk shuffle VFX — enables deck-zone draw gesture.</summary>
        public void MarkAwaitingOpeningDraw()
        {
            if (!OpeningSequenceActive) return;
            AwaitingOpeningDrawGesture = true;
            Log("Move your hand to the DECK zone on your duel disk to draw.");
            Notify();
        }

        /// <summary>
        /// Opening hand after player reaches the deck zone (anime draw).
        /// Draws both sides, starts turn 1 Main Phase 1.
        /// </summary>
        public bool CompleteOpeningDrawFromDeck()
        {
            if (Player == null || Opponent == null) return false;

            // Already past opening — ignore
            if (!OpeningSequenceActive && Player.HandCount > 0 && TurnNumber > 0)
                return false;

            if (Player.HandCount > 0)
            {
                OpeningSequenceActive = false;
                AwaitingOpeningDrawGesture = false;
                if (TurnNumber == 0)
                {
                    BeginTurn(FirstPlayer);
                    Notify();
                }

                return false;
            }

            AwaitingOpeningDrawGesture = false;
            OpeningSequenceActive = false;
            Log("—— OPENING DRAW ——");
            Draw(Player, TcgRules.StartingHandSize, silent: false);
            Draw(Opponent, TcgRules.StartingHandSize, silent: true);
            if (!GameOver && TurnNumber == 0)
                BeginTurn(FirstPlayer);
            Notify();
            return true;
        }

        public void RestartDuel()
        {
            if (_db == null || _playerDeckFile == null || _aiDeckFile == null)
            {
                Log("Cannot restart — missing decks.");
                return;
            }

            Log("—— RESTART ——");
            // turn 1 is decided again by the restarted cinematic (SetFirstPlayer)
            StartDuel(_db, _playerDeckFile, _aiDeckFile, cinematicOpening: true, opponentFirst: false);
        }

        List<int> FilterPlayable(List<int> pile)
        {
            var result = new List<int>();
            foreach (var id in pile)
            {
                if (_db.TryGet(id, out var def) && def != null && !def.IsExtraDeck)
                    result.Add(id);
            }

            return result;
        }

        List<int> BuildExtraPile(DeckFile deck)
        {
            var list = new List<int>();
            if (deck?.extra == null) return list;
            foreach (var e in deck.extra)
            {
                if (e == null) continue;
                var qty = Mathf.Clamp(e.qty, 0, TcgRules.MaxCopiesPerCard);
                for (var i = 0; i < qty; i++)
                    list.Add(e.id);
            }

            if (list.Count > TcgRules.ExtraDeckMax)
                Log($"Warning: Extra Deck size {list.Count} exceeds {TcgRules.ExtraDeckMax}.");
            return list;
        }

        void BeginTurn(DuelistState who)
        {
            TurnPlayer = who;
            TurnNumber++;
            // Spirit return (SpiritsAndMisc batch): "flipped face-up this turn" counts from here.
            TextEffects.SpiritsAndMiscRuntime.MarkTurnStartFaceState(this);
            // Life Absorbing Machine (ContinuousTrapsAndSpiritSupport batch): the LP costs of this player's previous turn.
            TextEffects.ContinuousTrapsAndSpiritSupportRuntime.OnTurnBegin(this, who);
            // Last turn's World Suppression / Mystic Probe / Metal Detector negation ends with it.
            TextEffects.TurnNegationTrapsRuntime.OnTurnStart(this);
            who.NormalSummonUsed = false;
            who.WabokuActive = false;
            who.CannotDeclareAttackThisTurn = false;
            // Soul Exchange: "this turn" Tribute grants end with the turn.
            TextEffects.TributeSummonsRuntime.ClearTurnGrants(Player, Opponent);
            PendingTributes.Clear();
            foreach (var m in who.MonstersOnField())
            {
                m.SummonedThisTurn = false;
                m.ClearAttackFlags();
                m.SetThisTurn = false;
                m.ChangedPositionThisTurn = false;
                m.EffectUsedThisTurn = false;
                m.DirectAttackThisTurn = false;
                m.DestroyedByBattleThisTurn = false;
                m.OncePerTurnStandbyRevivalUsed = false;
            }
            ResetEquippedMonsterTurnFlags(who);

            // "Once per turn" / "this turn" is per game turn, not per controller's turn (DR-09):
            // the non-turn player's monsters start the new turn clean too, so a monster that used
            // its effect last turn can use it again now (Quick Effects, or after a change of control).
            var other = OpponentOf(who);
            if (other != null)
            {
                foreach (var m in other.MonstersOnField())
                {
                    m.EffectUsedThisTurn = false;
                    m.DirectAttackThisTurn = false;
                    m.DestroyedByBattleThisTurn = false;
                    m.OncePerTurnStandbyRevivalUsed = false;
                }
            }

            foreach (var g in who.Graveyard)
                if (g != null) g.OncePerTurnStandbyRevivalUsed = false;
            foreach (var g in other?.Graveyard ?? Enumerable.Empty<CardInstance>())
                if (g != null) g.OncePerTurnStandbyRevivalUsed = false;

            // HandDisruption batch (v142): last turn's "opponent drew" windows are gone; a turn skipped by
            // Gamble's wrong call begins and ends here with no phases, and the opponent takes the next turn.
            TextEffects.HandDisruptionRuntime.OnTurnStart(this);
            if (TextEffects.HandDisruptionRuntime.ConsumeSkippedTurn(this, who))
            {
                // DcrSpellTrapUtility batch: a skipped turn still ends (ygopro raises EVENT_TURN_END for it), so Final
                // Countdown counts it.
                TextEffects.DcrSpellTrapUtilityRuntime.OnTurnEnd(this);
                if (GameOver) return;
                BeginTurn(OpponentOf(who));
                return;
            }

            // Great Long Nose (SpiritMonsters batch): a pending Battle Phase skip applies to this turn.
            TextEffects.SpiritMonstersRuntime.OnTurnStart(this, who);

            // —— Draw Phase ——
            Phase = DuelPhase.Draw;
            var side = who.IsPlayer ? "YOUR" : "OPPONENT";
            Log($"════ {side} TURN (game turn #{TurnNumber}) — {who.Name} ════");
            var firstPlayerFirstTurn = TurnNumber == 1 && who == FirstPlayer;
            var drawSkippedByEffect = !firstPlayerFirstTurn &&
                                      (who.SkipNextDrawPhaseCount > 0 || who.SkipNextDrawPhase);
            // Hino-Kagu-Tsuchi (SpiritsAndMisc batch): a pending "discard all during the next Draw Phase, before
            // they draw" applies now, or lapses when a card effect skips this Draw Phase.
            TextEffects.SpiritsAndMiscRuntime.OnDrawPhaseStart(this, skippedByEffect: drawSkippedByEffect);
            if (GameOver) return;
            // Maharaghi (SpiritMonsters batch): look at the top card before the draw; a human's top-or-bottom pick
            // holds the Draw Phase, and SpiritMonstersRuntime calls ContinueDrawPhase once it is answered.
            if (TextEffects.SpiritMonstersRuntime.OnDrawPhaseStart(this, who, drawSkippedByEffect))
            {
                Notify();
                return;
            }

            ContinueDrawPhase(who);
        }

        /// <summary>
        /// The Draw Phase after its "before you draw" effects: the draw (or its skip), then the Standby Phase and Main
        /// Phase 1. Run by BeginTurn, or once a pre-draw choice was answered (Maharaghi, SpiritMonsters batch).
        /// </summary>
        public void ContinueDrawPhase(DuelistState who)
        {
            if (who == null || GameOver || TurnPlayer != who || Phase != DuelPhase.Draw) return;
            var firstPlayerFirstTurn = TurnNumber == 1 && who == FirstPlayer;
            // NegationMonsters batch (v161): Freed the Matchless General — "instead of conducting your normal draw" (ygopro
            // EVENT_PREDRAW). A human's Deck pick holds the Draw Phase; NegationMonstersRuntime calls ContinueDrawPhase
            // again once it is answered (offered once per Draw Phase).
            if (TextEffects.NegationMonstersRuntime.OfferSearchInsteadOfDraw(this, who))
            {
                Notify();
                return;
            }
            if (firstPlayerFirstTurn)
                Log($"[{who.Name}] Draw Phase skipped (going first).");
            else if (who.SkipNextDrawPhaseCount > 0)
            {
                who.SkipNextDrawPhaseCount--;
                // An opponent's pending skip (Yata-Garasu, Fenrir, Time Seal) applies to this same Draw Phase and
                // ends with it (its EFFECT_SKIP_DP resets at this Draw Phase or at this turn's End Phase): it does
                // not carry over to a later Draw Phase (SpiritsAndMisc batch).
                who.SkipNextDrawPhase = false;
                Log($"[{who.Name}] Draw Phase skipped (card effect; remaining={who.SkipNextDrawPhaseCount}).");
            }
            else if (who.SkipNextDrawPhase)
            {
                who.SkipNextDrawPhase = false;
                Log($"[{who.Name}] Draw Phase skipped (card effect).");
            }
            else if (TextEffects.NegationMonstersRuntime.ConsumeDrawReplaced(this, who))
                Log($"[{who.Name}] No normal draw this turn (a card was added from the Deck instead).");
            else
                Draw(who, 1, silent: false);

            if (GameOver) return;

            // —— Standby Phase ——
            Phase = DuelPhase.Standby;
            BattleStep = BattleStep.None;
            DamageSubStep = DamageSubStep.None;
            if (who.SkipNextStandbyPhase)
            {
                // Solomon's Lawbook: nothing that happens "during your Standby Phase" happens.
                who.SkipNextStandbyPhase = false;
                Log($"[{who.Name}] Standby Phase skipped (card effect).");
                EnterMain1(who, firstPlayerFirstTurn);
                return;
            }
            Continuous.TickStandby(who);
            TextEffects.TextEffectRuntime.FirePhaseTriggers(this, who, TextEffects.EffectTiming.StandbyPhase);
            if (GameOver) return;
            // TriggerChainResponses batch (v163): a Standby trigger seated on a Chain (Life Absorbing Machine) waits on a
            // Chain response; the rest of the Standby Phase goes on once that Chain has resolved.
            if (TextEffects.TriggerChainResponsesRuntime.ChainOpen(this))
            {
                TextEffects.TriggerChainResponsesRuntime.RunAfterChain(this,
                    () => ContinueStandbyAfterTriggers(who, firstPlayerFirstTurn));
                Notify();
                return;
            }

            ContinueStandbyAfterTriggers(who, firstPlayerFirstTurn);
        }

        /// <summary>
        /// The Standby Phase after its triggers: Dimensionhole returns, upkeep picks and pauses, then Main Phase 1. Run by
        /// <see cref="ContinueDrawPhase"/>, or once a Standby trigger's Chain has resolved (TriggerChainResponses, v163).
        /// </summary>
        void ContinueStandbyAfterTriggers(DuelistState who, bool firstPlayerFirstTurn)
        {
            if (who == null || GameOver || TurnPlayer != who || Phase != DuelPhase.Standby) return;
            // Dimensionhole (LocksAndZones batch): monsters banished "until your next Standby Phase" return now
            // (before any Standby pause below, so an upkeep pick or a standby-only ignition cannot skip it).
            TextEffects.LocksAndZonesRuntime.OnStandbyPhase(this, who);
            // TypeDeclaration batch (v142): an upkeep waits on the turn player's Tribute pick (The Regulation of
            // Tribe); the Standby Phase goes on in ResumeStandbyAfterUpkeep once it is made, which then asks the
            // queued "pay N LP or destroy" choices below (v142 cross-group audit: asked first, they were paid for
            // the player because the Tribute pick was already open).
            if (PendingActivation != null && PendingActivation.AwaitingUpkeepTribute)
            {
                _standbyPausedFor = who;
                Notify();
                return;
            }
            // ContinuousTrapsAndSpiritSupport batch: Ominous Fortunetelling's call holds the Standby Phase; its answer
            // goes on through ResumeStandbyAfterUpkeep.
            if (TextEffects.ContinuousTrapsAndSpiritSupportRuntime.HoldsStandby(this))
            {
                _standbyPausedFor = who;
                Notify();
                return;
            }
            // StandbyGrowthAndMaintenance batch (v142): a human's "pay N LP or destroy this card" choice holds the
            // Standby Phase until it is answered; ContinueStandbyAfterUpkeepChoices then goes on from here. (If
            // another prompt is already open, the queued upkeeps are paid instead.)
            if (TextEffects.StandbyGrowthAndMaintenanceRuntime.OfferUpkeepChoice(this))
            {
                _standbyPausedFor = who;
                Notify();
                return;
            }
            if (IsHumanControlled(who) && TextEffects.TextEffectRuntime.HasStandbyOnlyIgnition(this, who))
            {
                _standbyPausedFor = who;
                Log($"[{who.Name}] Standby Phase — an effect can be activated.");
                Notify();
                return;
            }

            EnterMain1(who, firstPlayerFirstTurn);
        }

        /// <summary>
        /// A face-up Swords of Revealing Light whose turn count still runs (ContinuousTurnsRemaining, seeded at activation).
        /// PhaseTimersAndDeckTop batch (v163): also the turn count Pyro Clock of Destiny moves forward.
        /// </summary>
        public static bool IsSwordsCountdown(CardInstance st) =>
            st != null && st.FaceUp && st.CardId == SpellTrapEffects.SwordsOfRevealingLight &&
            st.ContinuousTurnsRemaining > 0;

        /// <summary>
        /// One step of Swords of Revealing Light's turn count (Lua s.desop): the End Phase of its controller's opponent's
        /// turn, or Pyro Clock of Destiny. It is destroyed on the 3rd.
        /// </summary>
        public void AdvanceSwordsCountdown(DuelistState swordsOwner, CardInstance st)
        {
            if (swordsOwner == null || !IsSwordsCountdown(st)) return;
            st.ContinuousTurnsRemaining--;
            Log(
                $"Swords of Revealing Light: opponent turn count → {st.ContinuousTurnsRemaining} remaining " +
                $"(destroy at 0 on opponent's End Phase).");
            if (st.ContinuousTurnsRemaining <= 0)
            {
                Log("Swords of Revealing Light is destroyed (End Phase of opponent's 3rd turn).");
                SendCardToGrave(swordsOwner, st);
            }
        }

        void EnterMain1(DuelistState who, bool firstPlayerFirstTurn)
        {
            // BattleOutcomeTriggers batch (v162): Timeater — "Your opponent skips their next Main Phase 1" (EFFECT_SKIP_M1).
            if (TextEffects.BattleOutcomeTriggersRuntime.MainPhase1SkipPending(this, who))
            {
                SkipMainPhase1(who);
                return;
            }

            // —— Main Phase 1 ——
            Phase = DuelPhase.Main1;
            // Cold Wave (LocksAndZones batch): "at the start of Main Phase 1" = before anything happens in it.
            TextEffects.LocksAndZonesRuntime.OnMainPhase1Start(this);
            FastEffects.Open(FastEffectTiming.WindowKind.OpenGameState, who, OpponentOf(who),
                ChainEvent.PhaseStart);
            if (firstPlayerFirstTurn)
                Log($"[{who.Name}] Main Phase 1 (no Battle this turn — going first).");
            else
                Log($"[{who.Name}] Main Phase 1");
            OfferOpponentTurnTrapWindow(OpponentOf(who));
        }

        /// <summary>
        /// BattleOutcomeTriggers batch (v162): <paramref name="who"/> skips this Main Phase 1 (Timeater). Nothing of Main Phase
        /// 1 happens: the turn goes on to the Battle Phase (entered from where Main Phase 1 would be), or to the End Phase when
        /// no Battle Phase can be conducted. While a Standby Phase pick is still open the Standby Phase holds instead
        /// (<see cref="StandbyPausedFor"/>; leaving it comes back here).
        /// </summary>
        void SkipMainPhase1(DuelistState who)
        {
            TextEffects.BattleOutcomeTriggersRuntime.ConsumeMainPhase1Skip(this, who);
            Log($"[{who.Name}] Main Phase 1 skipped (card effect).");
            Phase = DuelPhase.Main1;
            if (TryEnterBattlePhase(who)) return;
            if (!IsAwaitingResponse && !IsAwaitingEffectTarget && !HasDeclaredAttack)
            {
                EndTurn(who);
                return;
            }

            Phase = DuelPhase.Standby;
            TextEffects.BattleOutcomeTriggersRuntime.ScheduleMainPhase1Skip(this, who, null);
            _standbyPausedFor = who;
            Notify();
        }

        /// <summary>
        /// The human turn player the Standby Phase is holding for a standby-only ignition (Blast Juggler),
        /// or null. Only <see cref="ContinueAfterStandby"/> (<see cref="DuelIntentKind.EnterMainPhase1"/>)
        /// moves that turn on to Main Phase 1.
        /// </summary>
        public DuelistState StandbyPausedFor => _standbyPausedFor;

        /// <summary>
        /// True when <paramref name="who"/> may leave the Standby pause for Main Phase 1 now: no target pick,
        /// response window or chain is still open (a Blast Juggler activation has fully resolved).
        /// </summary>
        public bool CanContinueAfterStandby(DuelistState who) =>
            who != null && _standbyPausedFor == who && TurnPlayer == who && Phase == DuelPhase.Standby &&
            !GameOver && !IsAwaitingEffectTarget && !IsAwaitingResponse && !Chain.HasLinks &&
            !Chain.IsResolving && !IsResolvingChain &&
            // SimultaneousTriggers batch: the FLIP effects a Curse of Fiend flipped resolve in this Standby Phase.
            !TextEffects.SimultaneousTriggersRuntime.Busy(this);

        /// <summary>
        /// TypeDeclaration batch (v142): The Regulation of Tribe's Standby upkeep picks are done — finish the Standby
        /// Phase as <see cref="BeginTurn"/> would (it still holds for a standby-only ignition). A pick opened by a
        /// trigger of the Tribute is answered afterwards, as BeginTurn leaves any Standby trigger's pick open.
        /// </summary>
        public void ResumeStandbyAfterUpkeep()
        {
            var who = _standbyPausedFor;
            if (who == null || Phase != DuelPhase.Standby || GameOver) return;
            if (PendingActivation != null && PendingActivation.AwaitingUpkeepTribute) return;
            // v162 cross-group audit: a Tribute upkeep queued behind another Standby pick (A Man with Wdjat's look holds
            // the phase before the upkeeps run; RunStandbyUpkeep stops while a prompt is open) is run now, not skipped.
            TextEffects.TypeDeclarationRuntime.RunStandbyUpkeep(this);
            if (GameOver) return;
            if (PendingActivation != null && PendingActivation.AwaitingUpkeepTribute)
            {
                Notify();
                return;
            }
            // The same Standby Phase's "pay N LP or destroy this card" choices (Mirror Wall) come next; the last
            // answer goes on through ContinueStandbyAfterUpkeepChoices (v142 cross-group audit).
            if (TextEffects.StandbyGrowthAndMaintenanceRuntime.OfferUpkeepChoice(this))
            {
                Notify();
                return;
            }
            if (IsHumanControlled(who) && TextEffects.TextEffectRuntime.HasStandbyOnlyIgnition(this, who))
            {
                Log($"[{who.Name}] Standby Phase — an effect can be activated.");
                Notify();
                return;
            }

            _standbyPausedFor = null;
            EnterMain1(who, TurnNumber == 1 && who == FirstPlayer);
            Notify();
        }

        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142): every "pay N LP or destroy this card" choice holding this
        /// Standby Phase is answered — go on as the Standby Phase would have: hold for a standby-only ignition, or
        /// enter Main Phase 1.
        /// </summary>
        public void ContinueStandbyAfterUpkeepChoices()
        {
            // StandbyBurnsAndDice batch (v162): a human's Inspection choice queued behind another Standby choice
            // (Aqua Spirit) is asked once that one is answered.
            if (!IsAwaitingEffectTarget && TextEffects.StandbyBurnsAndDiceRuntime.OfferQueuedInspection(this))
            {
                Notify();
                return;
            }

            var who = _standbyPausedFor;
            if (who == null || Phase != DuelPhase.Standby || GameOver || IsAwaitingEffectTarget) return;
            if (IsHumanControlled(who) && TextEffects.TextEffectRuntime.HasStandbyOnlyIgnition(this, who))
            {
                Log($"[{who.Name}] Standby Phase — an effect can be activated.");
                Notify();
                return;
            }

            ContinueAfterStandby();
        }

        /// <summary>Leave a Standby window that is holding a standby-only ignition. False if nothing moved.</summary>
        public bool ContinueAfterStandby()
        {
            var who = _standbyPausedFor;
            if (!CanContinueAfterStandby(who)) return false;
            _standbyPausedFor = null;
            EnterMain1(who, TurnNumber == 1 && who == FirstPlayer);
            Notify();
            return true;
        }

        public void Draw(DuelistState who, int n, bool silent = false)
        {
            // Soft residual: Protector of the Sanctuary ContinuousWhileFaceUp
            // OpponentCannotDrawExceptDuringDrawPhases (Soft ContinuousProtections + Soft DE
            // Draw gate — Mask Soft surgical). Opponent blocked outside Draw Phase;
            // controller unrestricted. Draw Phase draws still OK. Soft residual OK:
            // searches / AddToHand / AddFromDeckToHand ≠ draw (not gated here).
            if (who != null &&
                TextEffects.ContinuousProtections.BlocksDrawOutsideDrawPhase(this, who))
            {
                if (!silent)
                    Log($"{who.Name} cannot draw — Protector of the Sanctuary (outside Draw Phase).");
                return;
            }

            var drawn = 0;
            for (var i = 0; i < n; i++)
            {
                if (who.Deck.Count == 0)
                {
                    Log($"{who.Name} cannot draw — deck out! (Victory Condition)");
                    EndGame(OpponentOf(who), DuelEndReason.DeckOut);
                    return;
                }

                var id = who.Deck[0];
                // PerCopyDeck batch (v163): the copy's owner / face-up state / "when drawn" trigger (Parasite Paracide,
                // Pharaoh's Treasure); CreateInstance gives the instance the copy's owner.
                var copy = who.Deck.InfoAt(0);
                who.Deck.RemoveAt(0);
                var inst = CreateInstance(id);
                who.Hand.Add(inst);
                drawn++;
                TextEffects.PerCopyDeckRuntime.NoteDrawn(this, who, inst, copy);
                TextEffects.TextEffectRuntime.ApplyCrushCardOnDraw(this, who, inst);
                TextEffects.TextEffectRuntime.CheckHandWinConditions(this);
                if (GameOver) return;
                if (!silent)
                {
                    if (who.IsPlayer)
                        Log($"[{who.Name}] draws: {inst.Name}");
                    else
                        Log($"[{who.Name}] draws 1 card (hand {who.HandCount}, deck {who.DeckCount}).");
                }
            }

            // Solemn Wishes: one trigger per draw event, however many cards it drew.
            // Local lineage (GainLpWhenControllerDraws) and GitHub lineage (ControllerDraws timing)
            // each dispatch only their own clauses, so both hooks run.
            TextEffects.TextEffectRuntime.NotifyDrew(this, who, drawn);
            if (drawn > 0 && TurnNumber > 0)
                TextEffects.TextEffectRuntime.NotifyDraw(this, who);
            // HandDisruption batch (v142): Appropriate — a draw outside either Draw Phase (the opponent's face-up
            // copies draw 2; a Set copy gets its activation window once this draw's chain has resolved).
            if (drawn > 0 && TurnNumber > 0)
                TextEffects.HandDisruptionRuntime.OnDrew(this, who, drawn);
        }

        CardInstance CreateInstance(int cardId)
        {
            _db.TryGet(cardId, out var def);
            var inst = new CardInstance
            {
                InstanceId = _nextInstanceId++,
                CardId = cardId,
                Def = def
            };
            FieldSpellEffects.ApplyRuleConditions(inst);
            // PerCopyDeck batch (v163): a copy a Deck just gave up keeps its owner (a card in another player's Deck).
            TextEffects.PerCopyDeckRuntime.ClaimDeckCopy(this, inst);
            return inst;
        }

        public DuelistState OpponentOf(DuelistState who) => who == Player ? Opponent : Player;

        bool IsMainActionWindow(DuelistState who) =>
            !GameOver && TurnPlayer == who && InMainPhase;

        // ───────────────────── Tribute selection ─────────────────────

        public void ToggleTribute(DuelistState who, CardInstance monster)
        {
            if (!IsMainActionWindow(who) || monster == null) return;
            // Mask of Restrict Soft ContinuousProtections / Soft DE check residual.
            if (TextEffects.ContinuousProtections.NeitherPlayerCanTribute(this)) return;
            // Face-down Defense Sets are legal tributes (Rulebook — you control them). A Soul Exchange target
            // may be tapped too (which one goes first when there are more targets than Tributes needed).
            if (!TcgRules.CanBeTributedForSummon(who, monster) &&
                !TextEffects.TributeSummonsRuntime.ForcedTributesForSummon(who).Contains(monster))
                return;

            if (PendingTributes.Contains(monster))
            {
                PendingTributes.Remove(monster);
                Log($"Tribute unselected: {monster.Name}");
            }
            else
            {
                PendingTributes.Add(monster);
                var face = monster.FaceUp ? monster.Name : $"{monster.Name} (face-down)";
                Log($"Tribute selected: {face} ({PendingTributes.Count} selected)");
            }

            Notify();
        }

        public void ClearTributes()
        {
            if (PendingTributes.Count == 0) return;
            PendingTributes.Clear();
            Notify();
        }

        /// <summary>
        /// Face-up and face-down monsters you control are both legal tributes.
        /// Humans must pick when there is a real choice; if the remaining field
        /// monsters exactly fill the requirement, use them (no silent discard of extras).
        /// AI auto-fills lowest ATK.
        /// </summary>
        /// <summary>
        /// Pick the Tributes for a Tribute Summon of <paramref name="summoning"/>. A Tribute is
        /// worth 1, or 2 for the Double Coston / The Trojan Horse family when the summoned
        /// monster's Attribute matches (<see cref="TextEffects.TextEffectRuntime.TributeValueFor"/>).
        /// </summary>
        List<CardInstance> ResolveTributes(DuelistState who, int needed, CardInstance summoning = null)
        {
            if (needed <= 0) return new List<CardInstance>();
            if (who == null) return null;
            // Mask of Restrict Soft ContinuousProtections / Soft DE check residual.
            if (TextEffects.ContinuousProtections.NeitherPlayerCanTribute(this)) return null;

            int ValueOf(CardInstance t) => TextEffects.TextEffectRuntime.TributeValueFor(t, summoning);
            int ValueSum(IEnumerable<CardInstance> ts) => ts.Sum(ValueOf);
            List<CardInstance> TakeWorth(List<CardInstance> ts)
            {
                var picked = new List<CardInstance>();
                var worth = 0;
                foreach (var t in ts)
                {
                    if (worth >= needed) break;
                    picked.Add(t);
                    worth += ValueOf(t);
                }

                return picked;
            }

            var chosen = PendingTributes
                .Where(t => TcgRules.CanBeTributedForSummon(who, t))
                .Distinct()
                .ToList();
            // Soul Exchange: the opponent's targets are Tributed as if you controlled them, and first (a
            // tapped one first when there are more of them than Tributes needed).
            var forced = TextEffects.TributeSummonsRuntime.ForcedTributesForSummon(who)
                .OrderBy(f => PendingTributes.Contains(f) ? 0 : 1)
                .ToList();
            foreach (var f in forced)
                if (!chosen.Contains(f))
                    chosen.Add(f);
            TextEffects.TributeSummonsRuntime.MoveForcedFirst(who, chosen, forced);
            if (ValueSum(chosen) >= needed)
                return TakeWorth(chosen);

            var remaining = who.MonstersOnField()
                .Where(m => m != null && !chosen.Contains(m))
                .ToList();
            var stillNeed = needed - ValueSum(chosen);

            // Unambiguous: every remaining monster must be tributed (e.g. 2 Sets for Dark Magician,
            // or a lone Double Coston for a DARK Level 7).
            if (ValueSum(remaining) == stillNeed)
            {
                chosen.AddRange(remaining);
                return chosen;
            }

            var allowAuto = !IsHumanControlled(who);
            if (!allowAuto)
            {
                Log(
                    $"Choose {needed} Tribute(s) — tap your field monster(s), including face-down Sets " +
                    $"(selected {chosen.Count}/{needed}), then Summon / Set again.");
                return null;
            }

            remaining = remaining
                .OrderBy(m => m.CurrentAtk)
                .ThenBy(m => m.Level)
                .ToList();
            foreach (var m in remaining)
            {
                if (ValueSum(chosen) >= needed) break;
                chosen.Add(m);
            }

            return ValueSum(chosen) >= needed ? TakeWorth(chosen) : null;
        }

        /// <summary>Internal alias — always use public path so field→GY triggers fire.</summary>
        void SendToGrave(DuelistState owner, CardInstance card) => SendCardToGrave(owner, card);

        // ───────────────────── Normal Summon / Set / Tribute ─────────────────────

        public bool CanNormalSummonOrSet(DuelistState who, CardInstance card) =>
            SummonProcedures.CheckNormalOrTribute(this, who, card, asSet: false).Legal
            || SummonProcedures.CheckNormalOrTribute(this, who, card, asSet: true).Legal;

        public bool TryNormalSummon(DuelistState who, CardInstance card, bool asSet) =>
            TryNormalSummonToZone(who, card, asSet, preferredZone: -1);

        /// <summary>
        /// Normal Summon/Set into a preferred Monster Zone index (AR disk snap).
        /// preferredZone &lt; 0 picks first empty after tributes.
        /// </summary>
        public bool TryNormalSummonToZone(DuelistState who, CardInstance card, bool asSet, int preferredZone)
        {
            // Light of Intervention (LocksAndZones batch): no monster can be Set — a Set is made as a Normal
            // (Tribute) Summon in face-up Defense Position instead (ygopro EFFECT_LIGHT_OF_INTERVENTION).
            var faceUpDefenseSummon = asSet && TextEffects.LocksAndZonesRuntime.SetBecomesFaceUpDefenseSummon(this);
            if (faceUpDefenseSummon) asSet = false;
            var check = SummonProcedures.CheckNormalOrTribute(this, who, card, asSet);
            if (!check.Legal)
            {
                Log($"Illegal Summon/Set: {check.Reason}");
                return false;
            }

            var need = TcgRules.TributesRequired(card.Level);
            List<CardInstance> tributes = null;
            if (need > 0)
            {
                // Dropping onto a monster you control is an explicit tribute of that monster
                // (including face-down Sets). The summoned monster may sit in that zone.
                if (preferredZone >= 0 && preferredZone < who.MonsterZones.Length)
                {
                    var occupant = who.MonsterZones[preferredZone].Occupant;
                    if (TcgRules.CanBeTributedForSummon(who, occupant) &&
                        !PendingTributes.Contains(occupant))
                        PendingTributes.Insert(0, occupant);
                }

                // SummonTriggersAndTributes batch (v163): Moisture Creature's extra procedure of exactly 3 Tributes.
                need = TextEffects.SummonTriggersAndTributesRuntime.TributesToUse(this, who, card, asSet, need);
                tributes = ResolveTributes(who, need, card);
                if (tributes == null)
                {
                    Log(
                        $"Need {need} Tribute(s) for {card.Name} (Lv{card.Level}). " +
                        "Tap face-up or face-down monsters you control, then Summon / Set again.");
                    return false;
                }

                // Soul Exchange: Tributing only the opponent's monster frees none of your zones.
                if (FirstEmpty(who.MonsterZones) < 0 && !tributes.Any(t => who.TryFindMonster(t, out _)))
                {
                    Log("No free Monster Zone.");
                    return false;
                }
            }

            // Owner of each Tribute (Zolga pays its owner), read before it leaves the field.
            var tributeOwners = new Dictionary<CardInstance, DuelistState>();

            // A Summon's Tributes' field → GY triggers (Sangan, Witch) are held until the Summon is negated or
            // succeeds (SummonNegation batch, v141); a Set's resolve at once, as before.
            List<(DuelistState owner, CardInstance card)> heldTriggers = null;
            if (tributes != null)
            {
                _holdMaterialGyTriggers = !asSet;
                try
                {
                    // The Tributes are sent at the same time: a Tributed Banisher of the Light still banishes the
                    // others (RedirectAndReplace batch, v142).
                    using var together = TextEffects.RedirectAndReplaceRuntime.EnterEffectApplication(this);
                    foreach (var t in tributes)
                    {
                        Log($"Tributed: {t.Name}");
                        TextEffects.ReleaseEvents.Record(this, t, who); // EngineHygiene: EVENT_RELEASE
                        var controller = ControllerOf(t) ?? who;
                        tributeOwners[t] = t.ControlOwner ?? controller;
                        // Use public path so Sangan / field→GY triggers fire (a Soul Exchange target leaves
                        // its controller's field).
                        SendCardToGrave(controller, t);
                    }
                }
                finally
                {
                    _holdMaterialGyTriggers = false;
                    heldTriggers = TakeHeldMaterialGyTriggers();
                }
            }

            var idx = preferredZone;
            if (idx < 0 || idx >= who.MonsterZones.Length || !who.MonsterZones[idx].IsEmpty)
                idx = FirstEmpty(who.MonsterZones);
            if (idx < 0)
            {
                Log("No free Monster Zone.");
                FlushMaterialGyTriggers(heldTriggers);
                return false;
            }

            // RuleLocks batch: Chain Energy — the LP toll to Normal Summon / Set a monster from the hand
            // (legality: SummonProcedures.CheckNormalOrTribute).
            if (who.Hand.Contains(card))
                TextEffects.RuleLocksRuntime.PayHandActionCost(this, who, card, asSet ? "Set" : "Normal Summon");
            who.Hand.Remove(card);
            if (asSet)
            {
                // Official: Normal Set → face-down Defense Position only (never face-down ATK)
                card.FaceUp = false;
                card.Position = BattlePosition.Defense;
                card.SetThisTurn = true;
                card.SummonedThisTurn = false;
                Log(who.IsPlayer
                    ? (need > 0
                        ? $"Tribute Set {card.Name} face-down Defense (DEF {BattleMechanics.DefenseValue(card)})!"
                        : $"Set {card.Name} face-down Defense (DEF {BattleMechanics.DefenseValue(card)})!")
                    : "Opponent Sets a monster face-down Defense.");
            }
            else if (faceUpDefenseSummon)
            {
                // Light of Intervention: the Set is made as a Normal (Tribute) Summon in face-up Defense Position.
                card.FaceUp = true;
                card.Position = BattlePosition.Defense;
                card.SummonedThisTurn = true;
                Log(need > 0
                    ? $"{who.Name} cannot Set: Tribute Summon {card.Name} in face-up Defense Position (DEF {BattleMechanics.DefenseValue(card)})."
                    : $"{who.Name} cannot Set: Normal Summon {card.Name} in face-up Defense Position (DEF {BattleMechanics.DefenseValue(card)}).");
            }
            else
            {
                card.FaceUp = true;
                card.Position = BattlePosition.Attack;
                card.SummonedThisTurn = true;
                Log(who.IsPlayer
                    ? (need > 0
                        ? $"Tribute Summon {card.Name} in Attack Position (ATK {BattleMechanics.AttackValue(card)})!"
                        : $"Normal Summon {card.Name} in Attack Position (ATK {BattleMechanics.AttackValue(card)})!")
                    : $"Opponent Normal Summons {(card.FaceUp ? card.Name : "a monster")}.");
            }

            ResetOnFieldEntry(card);
            if (!asSet)
                card.NormalOrFlipSummonedTurn = TurnNumber;
            // TokensDeclarationsAndPicks batch: this turn's summon record (Stray Lambs / Scapegoat; a Set is not a Summon).
            if (!asSet)
                TextEffects.TokensDeclarationsAndPicksRuntime.NoteSummon(this, who);
            // SpellCounterSpenders batch: Armor Exe cannot attack the turn it is Normal Summoned.
            if (!asSet)
                TextEffects.SpellCounterSpendersRuntime.OnNormalOrFlipSummoned(this, card);
            who.MonsterZones[idx].Occupant = card;
            card.WasSpecialSummoned = false;
            card.WasTributeSummoned = need > 0 && !asSet;
            // DerivedAtkMonsters batch (v176): record this card's Tributes (Guardian Tryce) and set a Tribute-derived ATK
            // (Great Maju Garzett / Maju Garzett) before the summon windows read it.
            TextEffects.DerivedAtkMonstersRuntime.NoteTributeSummon(this, card, tributes, asSet);
            TextEffects.SummonTriggersAndTributesRuntime.NoteNormalSummon(this, card, asSet, need);
            who.NormalSummonUsed = true;
            PendingTributes.Clear();
            if (!who.IsPlayer)
                DuelPresentationPacer.HoldOpponentSummon(card.Name, asSet);
            Notify();

            // A Summon (not a Set) first waits on its summon-negation window (Solemn Judgment / Horn of
            // Heaven). Its success steps run when that window closes, unless it was negated; the Tributes' held
            // triggers activate then too.
            if (!asSet && TryOpenSummonNegationWindow(who, card,
                    need > 0 ? SummonKind.TributeSummon : SummonKind.NormalSummon, tributes, fromGraveyard: false,
                    heldTriggers: heldTriggers, tributeOwners: tributeOwners))
                return true;

            FlushMaterialGyTriggers(heldTriggers);
            if (GameOver) return true;
            CompleteNormalSummon(who, card, asSet, tributes, tributeOwners: tributeOwners);
            return true;
        }

        /// <summary>
        /// The Normal / Tribute Summon (or Set) succeeded: tribute riders, "when Summoned", summon window.
        /// <paramref name="tributeOwners"/>: who each Tribute pays out to (Zolga); null or missing = the summoner.
        /// </summary>
        void CompleteNormalSummon(DuelistState who, CardInstance card, bool asSet, List<CardInstance> tributes,
            Dictionary<CardInstance, DuelistState> tributeOwners)
        {
            if (tributes != null && tributes.Count > 0)
            {
                // Zolga pays its owner for any Tribute Summon or Tribute Set; Precious Cards
                // from Beyond needs a Summon worth 2+ Tributes (a Double Coston counts as 2).
                var tributeValue = 0;
                foreach (var t in tributes)
                {
                    tributeValue += TextEffects.TextEffectRuntime.TributeValueFor(t, card);
                    var payee = tributeOwners != null && tributeOwners.TryGetValue(t, out var owner) && owner != null
                        ? owner
                        : who;
                    TextEffects.TextEffectRuntime.NotifyTributedForSummon(this, payee, t);
                }
                // SpecialSummonEffects batch (v161): Skull Knight #2 — Tributed for a face-up Fiend's Tribute Summon.
                if (!asSet)
                    TextEffects.SpecialSummonEffectsRuntime.NotifyTributeSummonMaterials(this, who, card, tributes,
                        tributeOwners);
                if (GameOver) return;
                if (!asSet)
                    TextEffects.TextEffectRuntime.NotifyTributeSummoned(this, who, card, tributeValue);
                if (GameOver) return;
            }

            if (!asSet)
                TextEffects.TextEffectRuntime.TryResolveThisCardSummoned(this, who, card);

            // Mysterious Puppeteer: either-player Normal Summon (not Set, not Special).
            if (!asSet)
                TextEffects.TextEffectRuntime.NotifyEitherPlayerNormalOrFlipSummoned(this);

            // Face-up NS/FS/SS and Normal Set: Torrential-family can answer a Set.
            // The procedure rides along so the arena plays the right hologram (Normal / Tribute / Set).
            // A paid Tribute is exactly a non-empty Tribute list; it is what separates a Tribute Summon/Set
            // from a plain one.
            var paidTribute = tributes != null && tributes.Count > 0;
            OpenSummonResponseOrContinue(who, card, asSet
                ? (paidTribute ? SummonKind.TributeSet : SummonKind.NormalSet)
                : (paidTribute ? SummonKind.TributeSummon : SummonKind.NormalSummon));
        }

        // ───────────────────── Flip Summon ─────────────────────

        public bool CanFlipSummon(DuelistState who, CardInstance monster)
        {
            if (!IsMainActionWindow(who) || monster == null) return false;
            if (!who.TryFindMonster(monster, out _)) return false;
            // Face-down Defense → face-up Attack
            if (monster.FaceUp || monster.Position != BattlePosition.Defense) return false;
            if (monster.SetThisTurn) return false; // cannot Flip Summon same turn it was Set
            if (TextEffects.TextEffectRuntime.PositionChangeForbiddenByEffect(monster)) return false;
            // Soft residual: Swords of Concealing Light ContinuousWhileFaceUp
            // OpponentCannotChangeBattlePositions — Flip Summon IS a battle-position
            // change (Fanbot mid-QC). Same Soft ContinuousProtections helper as
            // CanChangePosition. Soft residual OK: card-effect BP Soft-OK
            // (ApplyEffectPositionChange / Enemy Controller Soft path).
            if (TextEffects.ContinuousProtections.OpponentCannotChangeBattlePositions(this, monster))
                return false;
            if (TextEffects.PositionLocksRuntime.ManualPositionChangeLocked(this, monster)) return false; // Aqua Spirit / Curse of Fiend
            // MonsterNegationAndControlLocks batch: Final Attack Orders (EFFECT_CANNOT_CHANGE_POSITION on every monster).
            if (TextEffects.MonsterNegationAndControlLocksRuntime.FlipSummonLocked(this, monster)) return false;
            // RuleLocks batch: Jam Breeding Machine — its controller cannot Flip Summon.
            if (TextEffects.RuleLocksRuntime.FlipSummonLocked(this, who, monster)) return false;
            // ArchfiendRiders batch: Terrorking Archfiend — no Flip Summon without a face-up "Archfiend".
            if (TextEffects.ArchfiendRidersRuntime.NormalOrFlipSummonForbidden(this, who, monster, out _)) return false;
            // SummonTriggersAndTributes batch (v163): "You can only control 1 face-up X".
            if (TextEffects.SummonTriggersAndTributesRuntime.UniqueBlocksFaceUp(this, who, monster, out _)) return false;
            // SummonLocksAndStatRules batch: Rivalry of Warlords — no Flip Summon of another Type.
            if (TextEffects.SummonLocksAndStatRulesRuntime.BlocksFaceUpSummon(this, who, monster, out _)) return false;
            return true;
        }

        public bool TryFlipSummon(DuelistState who, CardInstance monster)
        {
            if (!CanFlipSummon(who, monster)) return false;
            monster.FaceUp = true;
            monster.Position = BattlePosition.Attack;
            monster.SummonedThisTurn = true;
            monster.ChangedPositionThisTurn = true;
            monster.WasTributeSummoned = false;
            monster.NormalOrFlipSummonedTurn = TurnNumber;
            // TokensDeclarationsAndPicks batch: this turn's summon record (Stray Lambs / Scapegoat).
            TextEffects.TokensDeclarationsAndPicksRuntime.NoteSummon(this, who);
            // SpellCounterSpenders batch: Armor Exe cannot attack the turn it is Flip Summoned.
            TextEffects.SpellCounterSpendersRuntime.OnNormalOrFlipSummoned(this, monster);
            Log(who.IsPlayer
                ? $"Flip Summon {monster.Name}!"
                : $"Opponent Flip Summons {monster.Name}.");
            // The Flip Summon waits on its summon-negation window first; FLIP effects and the
            // position-change triggers run only once it succeeds.
            if (TryOpenSummonNegationWindow(who, monster, SummonKind.FlipSummon, null, fromGraveyard: false))
                return true;
            CompleteFlipSummon(who, monster);
            return true;
        }

        /// <summary>The Flip Summon succeeded: position triggers, FLIP effect, "when Summoned", summon window.</summary>
        void CompleteFlipSummon(DuelistState who, CardInstance monster)
        {
            // SummonTriggersAndTributes batch (v163): King Tiger Wanghu does not answer a Flip Summon.
            TextEffects.SummonTriggersAndTributesRuntime.NoteFlipSummon(this, monster);
            TextEffects.TextEffectRuntime.NotifyPositionChanged(
                this, who, monster, BattlePosition.Defense, BattlePosition.Attack,
                destinationFaceUp: true, wasFlipSummon: true);
            // Official Flip effects (Man-Eater Bug, Magician of Faith, Cyber Jar, …). TriggerChainResponses batch (v163):
            // when the opponent may respond, the FLIP effect is Chain Link 1 of its own Chain (the summon's response
            // window opens after it); otherwise it resolves here as before.
            if (PendingActivation == null &&
                !TextEffects.SimultaneousTriggersRuntime.TryChainFlipSummoned(this, who, monster))
                MonsterEffects.OnFlipSummoned(this, who, monster);
            TextEffects.TextEffectRuntime.TryResolveThisCardSummoned(this, who, monster, flipSummon: true);
            // Mysterious Puppeteer: either-player Flip Summon (not Flip-by-battle).
            TextEffects.TextEffectRuntime.NotifyEitherPlayerNormalOrFlipSummoned(this);
            // Face-down Defense → face-up Attack is a Defense → Attack change (Crass Clown).
            TextEffects.TextEffectRuntime.NotifyPositionChanged(this, monster, toDefense: false);
            // If Flip opened a target window, do not open summon-response yet
            if (PendingActivation == null)
            {
                Notify();
                OpenSummonResponseOrContinue(who, monster, SummonKind.FlipSummon);
            }
            else
            {
                // MonsterNegationAndControlLocks batch: the FLIP's pick holds the summon window — Stumbling answers now.
                TextEffects.MonsterNegationAndControlLocksRuntime.OnSummonSettled(this, who, monster);
                Notify();
            }
        }

        // ───────────────────── Change battle position ─────────────────────

        public bool CanChangePosition(DuelistState who, CardInstance monster)
        {
            if (!IsMainActionWindow(who) || monster == null) return false;
            if (!who.TryFindMonster(monster, out _)) return false;
            if (!monster.FaceUp) return false; // face-down uses Flip Summon, not manual change
            // Rulebook: cannot change battle position the turn it was Summoned, Set, or attacked
            if (monster.SummonedThisTurn) return false;
            if (monster.SetThisTurn) return false; // includes flipped face-up by effects same turn as Set
            if (monster.AttackedThisTurn) return false;
            if (monster.ChangedPositionThisTurn) return false;
            if (BoundTrapLocksBattlePosition(monster)) return false;
            if (FieldLocksBattlePosition(monster)) return false;
            if (monster.PositionLockedByEffect) return false; // Dragon Capture Jar
            if (TextEffects.BattlePhaseEndRuntime.ManualPositionChangeLocked(this, monster)) return false; // Goblin Attack Force
            if (TextEffects.PositionLocksRuntime.ManualPositionChangeLocked(this, monster)) return false; // Aqua Spirit / Curse of Fiend
            if (TextEffects.TextEffectRuntime.PositionChangeForbiddenByEffect(monster)) return false; // Spellbinding Circle
            // Soft residual: Swords of Concealing Light ContinuousWhileFaceUp
            // OpponentCannotChangeBattlePositions (Soft ContinuousProtections + Soft DE
            // CanChangePosition / CanFlipSummon — Mask Soft surgical). Opp monsters only
            // while face-up host. Flip Summon gated separately in CanFlipSummon.
            if (TextEffects.ContinuousProtections.OpponentCannotChangeBattlePositions(this, monster))
                return false;
            return true;
        }

        /// <summary>
        /// Spellbinding Circle family: the bound host cannot change battle position
        /// while the Continuous Trap stays face-up and linked.
        /// </summary>
        /// <summary>
        /// Every battle-position lock from either lineage: Dragon Capture Jar / Spellbinding Circle
        /// (GitHub: <see cref="CardInstance.PositionLockedByEffect"/>, equip lock) and the local
        /// bound-trap / field locks.
        /// </summary>
        public bool IsBattlePositionLocked(CardInstance monster) =>
            monster != null &&
            (monster.PositionLockedByEffect ||
             TextEffects.TextEffectRuntime.PositionChangeForbiddenByEffect(monster) ||
             BoundTrapLocksBattlePosition(monster) || FieldLocksBattlePosition(monster) ||
             TextEffects.BattlePhaseEndRuntime.ManualPositionChangeLocked(this, monster) || // Goblin Attack Force
             TextEffects.PositionLocksRuntime.ManualPositionChangeLocked(this, monster)); // Aqua Spirit / Curse of Fiend

        public bool BoundTrapLocksBattlePosition(CardInstance monster)
        {
            if (monster?.Equips == null) return false;
            foreach (var eq in monster.Equips)
            {
                if (eq == null || !eq.FaceUp || eq.IsNegated || eq.Def == null) continue;
                var prog = TextEffects.CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null) continue;
                foreach (var c in prog.ClauseList)
                    if (c != null && c.AlsoCannotChangeBattlePosition)
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Dragon Capture Jar family: a face-up S/T locks matching face-up monsters
        /// from changing battle position.
        /// </summary>
        public bool FieldLocksBattlePosition(CardInstance monster)
        {
            if (monster == null || !monster.FaceUp) return false;
            foreach (var who in new[] { Player, Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField())
                {
                    if (st == null || !st.FaceUp || st.IsNegated || st.Def == null) continue;
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null) continue;
                    foreach (var c in prog.ClauseList)
                    {
                        if (c == null || !c.AlsoCannotChangeBattlePosition) continue;
                        if (!string.IsNullOrEmpty(c.RaceFilter) &&
                            (monster.Race == null ||
                             monster.Race.IndexOf(c.RaceFilter,
                                 System.StringComparison.OrdinalIgnoreCase) < 0))
                            continue;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool TryChangePosition(DuelistState who, CardInstance monster)
        {
            if (!CanChangePosition(who, monster)) return false;
            var from = monster.Position;
            var to = from == BattlePosition.Attack
                ? BattlePosition.Defense
                : BattlePosition.Attack;
            monster.Position = to;
            monster.ChangedPositionThisTurn = true;
            Log(who.IsPlayer
                ? $"{monster.Name} → {monster.Position} Position"
                : $"Opponent changes {monster.Name} to {monster.Position}.");
            TextEffects.TextEffectRuntime.NotifyPositionChanged(
                this, who, monster, from, to, monster.FaceUp, wasFlipSummon: false);
            // Dream Clown / Crass Clown / Tainted Wisdom
            TextEffects.TextEffectRuntime.NotifyPositionChanged(this, monster,
                toDefense: monster.Position == BattlePosition.Defense);
            Notify();
            return true;
        }

        /// <summary>
        /// An effect changes battle position. Manual changes and Flip Summons use
        /// their own methods, which call the same position notify.
        /// </summary>
        public bool ApplyEffectPositionChange(CardInstance monster, BattlePosition to, bool faceUp)
        {
            if (monster == null) return false;
            var from = monster.Position;
            var wasFace = monster.FaceUp;
            if (from == to && wasFace == faceUp) return false;
            if (_positionNotifyDepth > 8) return false;
            _positionNotifyDepth++;
            try
            {
                monster.Position = to;
                monster.FaceUp = faceUp;
                var who = ControllerOf(monster);
                TextEffects.TextEffectRuntime.NotifyPositionChanged(
                    this, who, monster, from, to, faceUp, wasFlipSummon: false);
                // SpiritMonsters batch: flipped face-up by a card effect → its "flipped face-up" trigger, queued.
                if (!wasFace && faceUp)
                    TextEffects.SpiritMonstersRuntime.NoteFlippedFaceUpByEffect(this, monster);
            }
            finally
            {
                _positionNotifyDepth--;
            }

            return true;
        }

        // ───────────────────── Spell/Trap Set + Activate ─────────────────────

        public bool CanSetSpellTrap(DuelistState who, CardInstance card)
        {
            if (!IsMainActionWindow(who)) return false;
            if (card?.Def == null || !(card.Def.IsSpell || card.Def.IsTrap)) return false;
            // Official: Field Spells are played in the Field Zone, not a Spell & Trap Zone.
            if (card.Def.IsFieldSpell) return false;
            if (!who.Hand.Contains(card)) return false;
            // DestinyBoard batch: "This card can only be placed on the field by the effect of "X"." cannot be Set.
            if (TextEffects.DestinyBoardRuntime.PlacerOf(card.Def) != null) return false;
            // Cold Wave (LocksAndZones batch): neither player can Set Spell/Trap Cards until its lock ends.
            if (TextEffects.LocksAndZonesRuntime.SpellTrapSetLocked(this)) return false;
            // RuleLocks batch: Chain Energy — Setting from the hand needs the LP toll.
            if (TextEffects.RuleLocksRuntime.HandActionUnaffordable(this, who, card, out _)) return false;
            return FirstEmpty(who.SpellTrapZones) >= 0;
        }

        public bool TrySetSpellTrap(DuelistState who, CardInstance card) =>
            TrySetSpellTrapToZone(who, card, preferredZone: -1);

        /// <summary>Set Spell/Trap into a preferred zone index (AR disk snap).</summary>
        public bool TrySetSpellTrapToZone(DuelistState who, CardInstance card, int preferredZone)
        {
            if (!CanSetSpellTrap(who, card)) return false;
            var idx = preferredZone;
            if (idx < 0 || idx >= who.SpellTrapZones.Length || !who.SpellTrapZones[idx].IsEmpty)
                idx = FirstEmpty(who.SpellTrapZones);
            if (idx < 0)
            {
                Log("No free Spell & Trap Zone.");
                return false;
            }

            TextEffects.RuleLocksRuntime.PayHandActionCost(this, who, card, "Set"); // Chain Energy (RuleLocks batch)
            who.Hand.Remove(card);
            card.FaceUp = false;
            card.SetThisTurn = true;
            card.FieldEntryStamp++; // a new field presence (Card Shuffle's once per turn, PgdSpellTrapBasics)
            who.SpellTrapZones[idx].Occupant = card;
            Log(who.IsPlayer
                ? $"Set Spell/Trap: {card.Name}. (Traps/Quick-Play cannot activate the turn they are Set.)"
                : "Opponent Sets a Spell/Trap.");
            if (!who.IsPlayer)
                DuelPresentationPacer.HoldOpponentSetSpellTrap();
            Notify();
            return true;
        }

        /// <summary>Place/activate a Field Spell into the Field Spell Zone (replaces prior).</summary>
        public bool TryPlaceFieldSpell(DuelistState who, CardInstance card)
        {
            if (who == null || card?.Def == null) return false;
            if (!IsMainActionWindow(who)) return false;
            if (!card.Def.IsFieldSpell)
            {
                Log("Not a Field Spell.");
                return false;
            }

            if (!who.Hand.Contains(card)) return false;
            // Cold Wave (LocksAndZones batch): placing a Field Spell is its activation (the AR disk path comes
            // here without the activation gate).
            if (TextEffects.LocksAndZonesRuntime.SpellTrapActivationLocked(this, card, out var coldWave))
            {
                Log($"Activation illegal: {coldWave}");
                return false;
            }

            // SpellTrapAnswersAndActivationLocks batch: Cursed Seal of the Forbidden Spell's name lock applies to this
            // activation too.
            if (TextEffects.SpellTrapAnswersAndActivationLocksRuntime.ActivationLocked(this, who, card, out var nameLock))
            {
                Log($"Activation illegal: {nameLock}");
                return false;
            }

            // RuleLocks batch: Chain Energy — placing a Field Spell from the hand is its activation. The activation
            // path (TryActivateSpellTrap) has already paid; a direct placement pays here.
            if (TextEffects.RuleLocksRuntime.HandActionUnaffordable(this, who, card, out var fieldToll))
            {
                Log($"Activation illegal: {fieldToll}");
                return false;
            }
            if (!HandActionCostPaid.Contains(card))
                TextEffects.RuleLocksRuntime.PayHandActionCost(this, who, card, "activate");

            // Send previous field spell to GY
            if (who.FieldSpellZone?.Occupant != null)
            {
                Log($"{who.FieldSpellZone.Occupant.Name} leaves the Field Zone.");
                SendCardToGrave(who, who.FieldSpellZone.Occupant);
            }

            who.Hand.Remove(card);
            card.FaceUp = true;
            card.SetThisTurn = false;
            who.FieldSpellZone.Occupant = card;
            Log(who.IsPlayer
                ? $"Field Spell activated: {card.Name}!"
                : $"Opponent activates Field Spell {card.Name}.");
            // BattleRules batch: Array of Revealing Light — the Type declared as it is activated is recorded on the card.
            TextEffects.BattleRulesRuntime.OnFieldSpellPlaced(this, who, card);
            Notify();
            return true;
        }

        /// <summary>Place a Pendulum Monster as a scale (left=true → zone 0).</summary>
        public bool TryPlacePendulum(DuelistState who, CardInstance card, bool left)
        {
            if (who == null || card?.Def == null) return false;
            if (!IsMainActionWindow(who)) return false;
            if (!card.Def.IsMonster ||
                card.Def.type == null ||
                card.Def.type.IndexOf("Pendulum", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                Log("Not a Pendulum Monster.");
                return false;
            }

            if (!who.Hand.Contains(card)) return false;
            var idx = left ? 0 : 1;
            if (who.PendulumZones[idx].Occupant != null)
            {
                Log("That Pendulum Zone is occupied.");
                return false;
            }

            // RuleLocks batch: Chain Energy — placing a Pendulum Scale is activating the card from the hand.
            if (TextEffects.RuleLocksRuntime.HandActionUnaffordable(this, who, card, out var scaleToll))
            {
                Log($"Activation illegal: {scaleToll}");
                return false;
            }
            TextEffects.RuleLocksRuntime.PayHandActionCost(this, who, card, "activate");
            who.Hand.Remove(card);
            card.FaceUp = true;
            who.PendulumZones[idx].Occupant = card;
            Log(who.IsPlayer
                ? $"Pendulum Scale set: {card.Name} ({(left ? "Left" : "Right")})"
                : $"Opponent sets Pendulum Scale {card.Name}.");
            Notify();
            return true;
        }

        /// <summary>Activate from hand (Normal/Quick-Play Spells) or from field (Set card).</summary>
        public bool CanActivateSpellTrap(DuelistState who, CardInstance card, bool fromHand) =>
            OfficialEffectRegistry.CanActivateOfficial(this, who, card, fromHand, out _);

        /// <summary>
        /// Activate only if an official script is registered for this card ID.
        /// Will not invent effect resolution for unregistered cards.
        /// </summary>
        public bool TryActivateSpellTrap(DuelistState who, CardInstance card, bool fromHand)
        {
            // RuleLocks batch: Chain Energy — an activation from the hand pays its LP toll as it is made; refunded when
            // the activation is not made or its target choice is cancelled.
            var handToll = TextEffects.RuleLocksRuntime.BeginHandActivation(this, who, card, fromHand);
            var ok = false;
            try
            {
                ok = TryActivateSpellTrapCore(who, card, fromHand);
            }
            finally
            {
                TextEffects.RuleLocksRuntime.EndHandActivation(this, who, card, handToll, ok);
            }

            return ok;
        }

        bool TryActivateSpellTrapCore(DuelistState who, CardInstance card, bool fromHand)
        {
            if (!OfficialEffectRegistry.CanActivateOfficial(this, who, card, fromHand, out var reason))
            {
                Log($"Activation illegal: {reason}");
                return false;
            }

            if (PendingResponse != null && PendingResponse.Timing == ResponseTiming.ChainResponse) return SpellTrapEffects.BeginOrResolveManual(this, who, card, fromHand, who == null || !who.IsPlayer);
            // BattleRules batch: Array of Revealing Light — a human declares the Type before the Field Spell is
            // activated (the pick re-enters this activation); the AI declares as the card is placed.
            if (card.Def.IsFieldSpell && TextEffects.BattleRulesRuntime.BeginFieldSpellDeclaration(this, who, card, fromHand))
                return true;
            // Place as Chain Link 1 (or next) with correct Spell Speed
            var speed = OfficialEffectRegistry.SpeedOf(card.Def);
            var cls = OfficialEffectRegistry.ClassOfActivation(card.Def, isTriggerWindow: false);
            var from = card.Def.IsMonster
                ? CardLocation.MonsterZone
                : fromHand ? CardLocation.Hand : CardLocation.SpellTrapZone;
            Chain.BeginBuilding();
            var link = Chain.AddLink(who, card, speed, cls, from, fromHand,
                wasSet: !fromHand && !card.FaceUp, effectKey: "manual", target: null);
            if (link != null)
                Log($"Chain {Chain.Describe()} — {card.Name}");

            if (card.Def.IsFieldSpell)
            {
                // Placement is the Field Spell activation; do not bypass a complete
                // compiled program that has already rejected this card.
                var fieldProg = CompiledEffectCache.GetOrCompile(card.Def);
                if (fieldProg != null && fieldProg.FullyCompiled && !fieldProg.CanResolveAny)
                {
                    Chain.Clear();
                    Log("Activation illegal: [" + card.Name + "] learned text is not legal now.");
                    return false;
                }

                // The opponent may chain to the Field Spell's activation (World Suppression, a free-chain
                // Trap / Quick-Play): when they can, the activation stays Chain Link 1 and resolves with
                // the chain (IsFieldSpellActivationLink); an AI responder answers inside the call. While
                // that link is unresolved the card sits face-up in its Field Zone but its effects do not
                // apply yet (CardInstance.ActivationUnresolvedOnChain).
                var mayAnswer = link != null && Chain.Count == 1 && Chain.Last == link &&
                                !IsResolvingChain && !Chain.IsResolving;
                card.ActivationUnresolvedOnChain = mayAnswer;
                var placed = TryPlaceFieldSpell(who, card);
                var answerable = placed && mayAnswer && OpenChainResponseWindow(OpponentOf(who));
                if (!answerable && card.ActivationUnresolvedOnChain)
                {
                    // Nobody could answer: the activation resolves now and the Field Spell applies.
                    card.ActivationUnresolvedOnChain = false;
                    if (placed) Notify();
                }

                if (!placed)
                    Chain.Clear();
                else if (!answerable)
                {
                    TextEffects.TextEffectRuntime.NotifySpellResolved(this, who, card);
                    if (!Chain.IsResolving && Chain.Count <= 1)
                        Chain.Clear();
                }
                return placed;
            }

            var autoPick = who == null || !who.IsPlayer;
            DeferSpellTrapResolution = !IsResolvingChain;
            var ok = SpellTrapEffects.BeginOrResolveManual(this, who, card, fromHand, autoPick);
            DeferSpellTrapResolution = false;
            if (ok)
            {
                // Keep a seated link while a chain window or target pick is open.
                // Clearing Count<=1 here was bookkeeping theater and dropped CL1 before pass/pass.
                ClearIdleChain();
            }
            else
                Chain.Clear();

            return ok;
        }

        /// <summary>
        /// An activation of <paramref name="card"/> was undone before its chain resolved (the target or
        /// cost pick was cancelled): remove its link so it neither blocks attacks nor resolves later.
        /// </summary>
        public bool DropUnresolvedLink(CardInstance card)
        {
            if (card == null || IsResolvingChain || Chain.IsResolving) return false;
            var link = Chain.Last;
            if (link == null || link.Card != card || link.Resolved) return false;
            return Chain.RemoveUnresolved(link);
        }

        /// <summary>
        /// Drop a leftover CL1 after a fully resolved activation (no window, no target pick).
        /// Fenrir SS / Relinquished absorb used to leave Chain.HasLinks, which blocks CanAttack.
        /// </summary>
        public void ClearIdleChain()
        {
            if (PendingResponse == null && PendingActivation == null &&
                !Chain.IsResolving && Chain.Count <= 1 && ResumeAfterChain == ResponseTiming.None)
            {
                Chain.Clear();
                // Michizure (BattleWindows batch): an activation that resolved inline built no response window, so a
                // monster its cost sent can no longer be chained to; a monster its effect sent gets its window now
                // (Notify ran while the idle link still blocked it).
                TextEffects.BattleWindowsRuntime.DropCostSends(this);
                if (!Ocg.OcgLabDuelHost.IsActive)
                    OfferMonsterSentWindow();
                // EquipModel batch: an equip made by that inline resolution gets its window now too.
                if (!Ocg.OcgLabDuelHost.IsActive)
                    OfferMonsterEquippedWindow();
                // ForcedActivationAndSubstitution batch (v163): and a return to the hand (Major Riot).
                if (!Ocg.OcgLabDuelHost.IsActive)
                    OfferMonstersReturnedWindow();
                // HandDisruption batch (v142): a draw inside that inline resolution queued its "opponent drew"
                // window behind the idle link; it can open now (no-op unless one is queued).
                if (!Ocg.OcgLabDuelHost.IsActive)
                    TextEffects.HandDisruptionRuntime.FlushDrawWindow(this);
                // ChaosBanishSummons batch: a banish made by that inline resolution (Chaos Sorcerer's ignition, Gustaph)
                // triggers Soul Absorption now (Notify ran while the idle link still blocked it).
                if (!Ocg.OcgLabDuelHost.IsActive && !GameOver && !TextEffects.SimultaneousTriggersRuntime.Busy(this) &&
                    PlayIdleForDeferredEvents())
                    TextEffects.BanishEvents.FireQueuedTriggers(this);
            }
        }

        /// <summary>
        /// Resolve the current Chain in official Last-In-First-Out order (CLn → … → CL1).
        /// Links flagged <see cref="ChainLink.Negated"/> are skipped (their effect does not
        /// apply, but they still occupied a link). <paramref name="resolveLink"/> applies one
        /// link's effect and returns whether it resolved. Returns the number of links resolved.
        ///
        /// This is the multi-link resolution driver: it drives <see cref="ChainStack.StartResolution"/>
        /// and <see cref="ChainStack.PopNextToResolve"/> so a built chain resolves reverse-order,
        /// which is the foundation for correct Quick-Effect / Counter-Trap interaction.
        /// </summary>
        public int ResolveChainLifo(Func<ChainLink, bool> resolveLink)
        {
            if (resolveLink == null) throw new ArgumentNullException(nameof(resolveLink));
            if (!Chain.HasLinks) return 0;

            Chain.StartResolution();
            var resolved = 0;
            while (true)
            {
                var link = Chain.PopNextToResolve();
                if (link == null) break;
                if (link.Negated)
                {
                    Log($"CL{link.LinkNumber} {link.Card?.Name ?? "?"} was negated — skipped.");
                    link.Resolved = true;
                    continue;
                }

                if (resolveLink(link))
                    resolved++;
                link.Resolved = true;
            }

            return resolved;
        }

        /// <summary>While awaiting a target, select a legal card (GY monster, S/T, etc.).</summary>
        public bool TrySelectEffectTarget(CardInstance target)
        {
            // Resolve presentation/UI refs to the exact LegalTargets entry (InstanceId-safe).
            var legal = ResolveLegalEffectTarget(target);
            if (legal == null) return false;
            // A compiled activation still being made records this pick's effect for its Chain Link.
            var ok = TextEffects.CompiledActivationChainRuntime.Continue(this,
                () => SpellTrapEffects.TryResolveWithTarget(this, legal));
            if (ok)
            {
                TryCompleteDeferredBattleDestruction();
                ClearIdleChain();
            }
            return ok;
        }

        public bool CancelEffectTargeting()
        {
            ClearPendingSpecialSummonPlacement();
            var ok = TextEffects.CompiledActivationChainRuntime.Continue(this, () => SpellTrapEffects.CancelPending(this));
            // Flip target cancelled mid-Damage-Step — still finish battle destructions
            if (ok || PendingActivation == null)
                TryCompleteDeferredBattleDestruction();
            return ok;
        }

        /// <summary>
        /// Remember the column and ATK/DEF the player chose for a pending Special Summon.
        /// Tests and AI leave these unset and keep first-empty Attack.
        /// </summary>
        public void SetPendingSpecialSummonPlacement(int zoneIndex, BattlePosition? position)
        {
            _pendingSpecialSummonZone = zoneIndex;
            _pendingSpecialSummonPosition = position;
        }

        public void ClearPendingSpecialSummonPlacement()
        {
            _pendingSpecialSummonZone = -1;
            _pendingSpecialSummonPosition = null;
        }

        /// <summary>
        /// The column the player chose for the pending Special Summon (-1 = none). Read by a pick that
        /// records the choice for a later simultaneous Special Summon (FlipMassEffects batch).
        /// </summary>
        public int PendingSpecialSummonZone => _pendingSpecialSummonZone;

        /// <summary>The ATK/DEF the player chose for the pending Special Summon (null = none chosen).</summary>
        public BattlePosition? PendingSpecialSummonPosition => _pendingSpecialSummonPosition;

        public bool IsLegalEffectTarget(CardInstance card) =>
            ResolveLegalEffectTarget(card) != null;

        /// <summary>
        /// Match by reference first, then by <see cref="CardInstance.InstanceId"/>.
        /// AR pad visuals / UI rebuilds can hold the same engine card by id when
        /// list.Contains fails due to a stale presentation ref.
        /// </summary>
        public CardInstance ResolveLegalEffectTarget(CardInstance card)
        {
            if (PendingActivation?.LegalTargets == null || card == null)
                return null;
            var list = PendingActivation.LegalTargets;
            if (list.Contains(card)) return card;
            var id = card.InstanceId;
            if (id == 0) return null;
            for (var i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t != null && t.InstanceId == id)
                    return t;
            }

            return null;
        }

        // —— helpers used by SpellTrapEffects ——

        /// <summary>
        /// The Spell/Trap Zone a duelist's next card seated by <see cref="FirstEmptySpellTrap"/> should take, per side
        /// (0 = Player, 1 = Opponent; -1 = none). The AR UI sets it to the zone the player chose for a Continuous /
        /// Equip Spell activated from the hand, right before <see cref="TryActivateSpellTrap"/>: the activation seats
        /// the card through FirstEmptySpellTrap, possibly later (after a target or cost pick), so the preference
        /// stays until that zone is taken or <see cref="ClearPreferredSpellTrapZone"/> runs. Presentation only:
        /// nothing in the rules sets it, and unset it changes nothing.
        /// </summary>
        readonly int[] _preferredSpellTrapZone = { -1, -1 };

        public void SetPreferredSpellTrapZone(DuelistState who, int zone)
        {
            if (who == null) return;
            _preferredSpellTrapZone[who == Player ? 0 : 1] =
                who.SpellTrapZones != null && zone >= 0 && zone < who.SpellTrapZones.Length ? zone : -1;
        }

        public void ClearPreferredSpellTrapZone(DuelistState who)
        {
            if (who != null) _preferredSpellTrapZone[who == Player ? 0 : 1] = -1;
        }

        public int FirstEmptySpellTrap(DuelistState who)
        {
            if (who == null) return -1;
            var side = who == Player ? 0 : 1;
            var pref = _preferredSpellTrapZone[side];
            if (pref >= 0)
            {
                // IsEmpty, as FirstEmpty: a zone a card made unusable (UnusableFor) is not free either.
                if (pref < who.SpellTrapZones.Length && who.SpellTrapZones[pref] != null && who.SpellTrapZones[pref].IsEmpty)
                    return pref;
                _preferredSpellTrapZone[side] = -1;   // taken or unusable: spent
            }

            return FirstEmpty(who.SpellTrapZones);
        }

        /// <summary>
        /// First free zone — prefer <b>center</b>, then expand outward.
        /// 5 zones → order 2, 1, 3, 0, 4 (classic first placement when user does not specify).
        /// </summary>
        public int FirstEmpty(FieldZone[] zones)
        {
            if (zones == null || zones.Length == 0) return -1;
            var n = zones.Length;
            var center = n / 2; // 5 → 2
            for (var dist = 0; dist < n; dist++)
            {
                if (dist == 0)
                {
                    if (zones[center] != null && zones[center].IsEmpty)
                        return center;
                    continue;
                }

                var left = center - dist;
                var right = center + dist;
                if (left >= 0 && zones[left] != null && zones[left].IsEmpty)
                    return left;
                if (right < n && zones[right] != null && zones[right].IsEmpty)
                    return right;
            }

            return -1;
        }

        public void NotifyPublic() => Notify();

        /// <summary>
        /// BattleBoosts batch (v161): the battle whose damage calculation is pending (its response windows are open or its
        /// Chain is resolving): its attacker and attack target (null for a direct attack). False outside one.
        /// </summary>
        public bool TryGetPendingDamageCalculation(out CardInstance attacker, out CardInstance target)
        {
            attacker = _deferredDamage?.Attacker;
            target = _deferredDamage?.TargetOrNull;
            return _deferredDamage != null;
        }

        /// <summary>Attacker and attack target of the current battle (declaration or Damage Step).</summary>
        public bool TryGetBattlingPair(out CardInstance attacker, out CardInstance target)
        {
            attacker = _deferredDamage?.Attacker ?? PendingResponse?.Attacker ?? DeclaredAttacker;
            target = _deferredDamage?.TargetOrNull ?? PendingResponse?.AttackTarget ?? DeclaredAttackTarget;
            return attacker != null;
        }

        public bool TrySelectLpCost(int amount)
        {
            if (PendingActivation == null) return false;
            if (PendingActivation.AwaitingLpZeroConfirm &&
                PendingActivation.LpCostChoices != null &&
                PendingActivation.LpCostChoices.Contains(amount))
                return TryConfirmLpZeroPay();
            if (!PendingActivation.AwaitingLpCost)
                return false;
            if (TextEffects.TextEffectRuntime.TryFinishLpCost(this, amount))
                return true;
            return YgoProTriggerCatalog.FinishPayLp(this, amount);
        }

        /// <summary>
        /// Human confirmed paying an LP cost that leaves 0. Continues the activation.
        /// </summary>
        public bool TryConfirmLpZeroPay()
        {
            var p = PendingActivation;
            if (p == null || !p.AwaitingLpZeroConfirm) return false;
            var who = p.Controller;
            var card = p.Card;
            var fromHand = p.FromHand;
            ClearPendingActivation();
            var ok = SpellTrapEffects.ResumeAfterLpZeroConfirm(this, who, card, fromHand);
            if (!ok)
                Chain.Clear();
            return ok;
        }

        public void BanishCard(DuelistState owner, CardInstance card, bool destroyed = false, DuelistState by = null)
        {
            if (owner == null || card == null) return;
            if (owner.Graveyard != null && owner.Graveyard.Contains(card) &&
                TextEffects.ContinuousProtections.CannotBanishFromGraveyard(this, owner))
            {
                Log($"A card in the GY cannot be banished ({card.Name}).");
                return;
            }
            // RuleLocks batch: Kycoo the Ghost Destroyer — <paramref name="by"/> (the player whose effect or cost
            // banishes) cannot banish a card from either GY while their opponent controls it face-up.
            if (owner.Graveyard != null && owner.Graveyard.Contains(card) &&
                TextEffects.RuleLocksRuntime.BanishFromGyLocked(this, by))
            {
                Log($"{by.Name} cannot banish cards from the GY ({card.Name}).");
                return;
            }
            var fromFieldMonster = owner.TryFindMonster(card, out _);
            var fromFieldSt = owner.TryFindSpellTrap(card, out _);
            if (fromFieldMonster)
                UnlinkEquips(card, hostDestroyed: destroyed, fallbackOwner: owner, leavingToGy: false);
            UnlinkCardFromHostOnLeave(owner, card, fromFieldSt, destroyed);
            DetachFromField(owner, card);
            if (owner.Hand != null && owner.Hand.Remove(card)) card.ProcedureCompleted = false;
            owner.Graveyard?.Remove(card);
            card.FaceUp = true;
            var pile = PileOwner(owner, card);
            if (!pile.Banished.Contains(card))
                pile.Banished.Add(card);
            card.BanishedTurnNumber = TurnNumber;
            Log($"{card.Name} is banished.");
            // ChaosBanishSummons batch: the banish event (Lua EVENT_REMOVE; Soul Absorption).
            TextEffects.BanishEvents.Record(this, card);
        }

        public int BanishCopiesFromHandAndDeck(DuelistState who, int cardId)
        {
            if (who == null || cardId <= 0) return 0;
            // ChaosBanishSummons batch: the hand and Deck copies are banished at the same time (one EVENT_REMOVE).
            using var banishGroup = TextEffects.BanishEvents.Group(this);
            var n = 0;
            if (who.Hand != null)
            {
                for (var i = who.Hand.Count - 1; i >= 0; i--)
                {
                    var c = who.Hand[i];
                    if (c == null || c.CardId != cardId) continue;
                    BanishCard(who, c);
                    n++;
                }
            }

            if (who.Deck != null)
            {
                for (var i = who.Deck.Count - 1; i >= 0; i--)
                {
                    if (who.Deck[i] != cardId) continue;
                    who.Deck.RemoveAt(i);
                    var inst = CreateCardInstance(cardId);
                    inst.FaceUp = true;
                    inst.BanishedTurnNumber = TurnNumber;
                    // PerCopyDeck batch (v163): a copy another player owns is banished to its owner's pile.
                    var pile = PileOwner(who, inst);
                    if (!pile.Banished.Contains(inst))
                        pile.Banished.Add(inst);
                    TextEffects.BanishEvents.Record(this, inst);
                    n++;
                }
            }

            if (n > 0)
                Log($"Banished {n} copy(ies) of #{cardId} from {who.Name}'s hand/Deck.");
            return n;
        }

        public int DestroyCopiesFromHandAndDeck(DuelistState who, int cardId)
        {
            if (who == null || cardId <= 0) return 0;
            var n = 0;
            if (who.Hand != null)
            {
                for (var i = who.Hand.Count - 1; i >= 0; i--)
                {
                    var c = who.Hand[i];
                    if (c == null || c.CardId != cardId) continue;
                    who.Hand.RemoveAt(i);
                    // Banisher of the Light (RedirectAndReplace batch, v142): banished instead.
                    TextEffects.RedirectAndReplaceRuntime.PutInGraveyard(this, who, c);
                    n++;
                }
            }

            if (who.Deck != null)
            {
                for (var i = who.Deck.Count - 1; i >= 0; i--)
                {
                    if (who.Deck[i] != cardId) continue;
                    who.Deck.RemoveAt(i);
                    var inst = CreateCardInstance(cardId);
                    TextEffects.RedirectAndReplaceRuntime.PutInGraveyard(this, who, inst);
                    n++;
                }
            }

            if (n > 0)
                Log($"Destroyed {n} copy(ies) of #{cardId} from {who.Name}'s hand/Deck.");
            return n;
        }

        public bool TrySelectCoinCall(bool heads)
        {
            if (PendingActivation == null || !PendingActivation.AwaitingCoinCall)
                return false;
            return TextEffects.TextEffectRuntime.TryFinishCoinCall(this, heads);
        }

        /// <summary>Mushroom Man #2: pay the End Phase cost, or decline and keep control.</summary>
        public bool TryResolveOptionalPay(bool pay) =>
            TextEffects.TextEffectRuntime.TryResolveOptionalPay(this, pay);

        public bool TryTakeControl(DuelistState newController, CardInstance card)
        {
            if (newController == null || card == null) return false;
            var old = ControllerOf(card);
            if (old == null || old == newController) return false;
            var dest = FirstEmpty(newController.MonsterZones);
            if (dest < 0) return false;
            DetachFromField(old, card);
            newController.MonsterZones[dest].Occupant = card;
            RecordControlMove(card, old, newController);
            Log($"{newController.Name} takes control of {card.Name}.");
            TextEffects.TextEffectRuntime.NotifyControlChanged(this, newController, card);
            return true;
        }

        /// <summary>
        /// A card entering <paramref name="controller"/>'s field from <paramref name="pileOwner"/>'s GY (Monster
        /// Reborn on the opponent's GY) is owned by the pile's player: record it in
        /// <see cref="CardInstance.ControlOwner"/> when that is not the controller, so the card goes to its
        /// owner's GY / hand / Deck when it leaves the field (<see cref="PileOwner"/>, Tribute owners) and
        /// Remove Brainwashing / Change of Heart see its real owner. Call before the Special Summon.
        /// </summary>
        public static void RecordOwnerOnFieldEntry(CardInstance card, DuelistState pileOwner,
            DuelistState controller)
        {
            if (card == null) return;
            card.ControlOwner = pileOwner != null && pileOwner != controller ? pileOwner : null;
            card.ReturnControlAtEndOfTurn = -1;
        }

        /// <summary>Remember the owner while someone else controls the card; clear on return.</summary>
        static void RecordControlMove(CardInstance card, DuelistState old, DuelistState newController)
        {
            if (card.ControlOwner == null)
                card.ControlOwner = old;
            else if (card.ControlOwner == newController)
            {
                card.ControlOwner = null;
                card.ReturnControlAtEndOfTurn = -1;
            }
        }

        /// <summary>
        /// Switch control of two monsters on opposite sides of the field (Invader of the Throne,
        /// ygopro Duel.SwapControl): each moves into the Monster Zone the other one left, so no free
        /// zone is needed. Owner bookkeeping is the same as <see cref="TryTakeControl"/>.
        /// </summary>
        public bool TrySwapControl(CardInstance a, CardInstance b)
        {
            if (a == null || b == null || a == b) return false;
            var holderA = ControllerOf(a);
            var holderB = ControllerOf(b);
            if (holderA == null || holderB == null || holderA == holderB) return false;
            if (!holderA.TryFindMonster(a, out var zoneA) || !holderB.TryFindMonster(b, out var zoneB))
                return false;
            holderA.MonsterZones[zoneA].Occupant = b;
            holderB.MonsterZones[zoneB].Occupant = a;
            RecordControlMove(a, holderA, holderB);
            RecordControlMove(b, holderB, holderA);
            // A permanent switch outranks a Charmer's while-face-up control of either card.
            a.ControlBoundTo = null;
            b.ControlBoundTo = null;
            Log($"{holderB.Name} takes control of {a.Name}; {holderA.Name} takes control of {b.Name}.");
            TextEffects.TextEffectRuntime.NotifyControlChanged(this, holderB, a);
            TextEffects.TextEffectRuntime.NotifyControlChanged(this, holderA, b);
            return true;
        }

        /// <summary>
        /// The Set monster whose FLIP effect is resolving after damage calculation while it is about
        /// to be destroyed by that battle (ygopro STATUS_BATTLE_DESTROYED). Null otherwise. Charmers
        /// read it: a battle-destroyed Charmer's effect takes no target.
        /// </summary>
        public CardInstance FlipSourceBattleDestroyPending { get; private set; }

        (CardInstance attacker, CardInstance defender) _battleDestroyPending;

        /// <summary>
        /// A monster the battle being calculated destroys, while that battle's battle-damage triggers run (ygopro
        /// STATUS_BATTLE_DESTROYED): an effect cannot target it (Dark Scorpion - Gorg cannot return it to the Deck).
        /// </summary>
        public bool IsBattleDestroyPending(CardInstance card) =>
            card != null && (card == _battleDestroyPending.attacker || card == _battleDestroyPending.defender);

        /// <summary>
        /// A card leaving the field other than to the GY (hand / Deck / banish). Both lineages'
        /// leave-field rules live in <see cref="UnlinkCardFromHostOnLeave"/> and
        /// <see cref="UnlinkEquips"/>; this is the GitHub-lineage entry point (Monster Recovery).
        /// Call it after <see cref="DetachFromField"/>: a linked card was on the field.
        /// </summary>
        void UnlinkLeavingField(DuelistState controller, CardInstance card, bool destroyed)
        {
            if (card == null) return;
            UnlinkCardFromHostOnLeave(controller, card, fromFieldSt: card.EquippedTo != null, destroyed);
            UnlinkEquips(card, hostDestroyed: destroyed, fallbackOwner: controller, leavingToGy: false);
        }

        /// <summary>Fresh field presence: effects that lasted "while face-up on the field" end.</summary>
        static void ResetOnFieldEntry(CardInstance card)
        {
            if (card == null) return;
            // A new field presence: "this turn" grants on the old one end (Soul Exchange), and a
            // "cannot attack this turn" lock does not follow it back (Tribute Doll).
            card.FieldEntryStamp++;
            card.SpecialSummonedByOwnProcedure = false;
            card.CannotAttackDuringTurnNumber = 0;
            card.EffectUsedWhileFaceUp = false;
            card.AttackLockedBy = null;
            card.ControlBoundTo = null;
            card.NormalOrFlipSummonedTurn = -1;
            card.TempDestroyOnEndOfTurn = -1;
            // FlipCostsAndFusion batch: a GY Fusion Material stamp and a Fusion Summon's material list end here.
            card.SentAsFusionMaterialFor = null;
            card.FusionMaterialsUsed = null;
            card.UntilEndOfTurnAtk = 0;
            card.UntilEndOfTurnDef = 0;
            card.FaceUpStampAtTurnStart = card.FaceUpStamp;
            card.AbsorbedBy = null;
            // NegationMonsters batch (v161): a Ha Des / Twin-Headed Wolf negation ends when the card leaves the GY.
            card.EffectsNegatedAfterBattle = false;
            // BattleDestroyedGyRecovery (v162): its GY-entry stamp ends with that stay in the GY.
            TextEffects.GyEntryStamps.Left(card);
            // DerivedAtkMonsters batch (v176): a self-set ATK/DEF and the Tribute record belong to the old presence.
            card.SelfSetAtk = int.MinValue;
            card.SelfSetDef = int.MinValue;
            card.TributeMaterials = null;
        }

        /// <summary>
        /// A monster leaves its Monster Zone for a Spell &amp; Trap Zone and stays on the field (Kiseitai becoming an
        /// Equip Spell Card, SpiritsAndMisc batch). Its own Equip Cards lost their monster and go to the GY. Link cards
        /// that are not Equip Cards (Call of the Haunted, Spellbinding Circle) stay face-up and unlinked, as when a
        /// monster leaves the field other than to the GY.
        /// </summary>
        public void UnlinkEquipsForZoneMove(DuelistState controller, CardInstance card) =>
            UnlinkEquips(card, hostDestroyed: false, fallbackOwner: controller, leavingToGy: false,
                hostStaysOnField: true);

        /// <summary>
        /// Shuffle-into-Deck destination for a card on the field (Monster Recovery): its owner's
        /// Deck, or the Extra Deck for Fusion-type monsters. Tokens vanish. The caller shuffles, unless
        /// <paramref name="onTop"/> places a Main Deck card on top of its owner's Deck (Dark Scorpion - Gorg).
        /// PerCopyDeck batch (v163): <paramref name="intoDeckOf"/> puts a Main Deck card in that player's Deck instead
        /// (Parasite Paracide: it stays its owner's card), and <paramref name="copyInfo"/> gives the copy its metadata
        /// (face-up, its "when drawn" trigger; the owner is filled in here).
        /// </summary>
        public bool ReturnCardToDeck(CardInstance card, bool onTop = false, DuelistState intoDeckOf = null,
            DeckCopyInfo copyInfo = null)
        {
            var controller = ControllerOf(card);
            if (controller == null) return false;
            var wasFaceUp = card.FaceUp;
            DetachFromField(controller, card);
            PendingTributes.Remove(card);
            UnlinkLeavingField(controller, card, destroyed: false);
            var owner = PileOwner(controller, card);
            var deckHolder = intoDeckOf ?? owner;
            if (copyInfo == null && !ReferenceEquals(deckHolder, owner))
                copyInfo = new DeckCopyInfo();
            if (copyInfo != null)
                copyInfo.Owner ??= owner;
            var faceUpNote = copyInfo != null && copyInfo.FaceUp ? " face-up" : "";
            if (card.IsToken)
                Log($"{card.Name} leaves the field (Token).");
            else if (card.Def != null && card.Def.IsExtraDeck)
            {
                owner.ExtraDeck.Add(card.CardId);
                Log($"{card.Name} returns to the Extra Deck.");
            }
            else if (onTop)
            {
                deckHolder.Deck.Insert(0, card.CardId, copyInfo);
                Log($"{card.Name} is placed{faceUpNote} on top of {deckHolder.Name}'s Deck.");
            }
            else
            {
                deckHolder.Deck.Add(card.CardId, copyInfo);
                Log(ReferenceEquals(deckHolder, owner) && copyInfo == null
                    ? $"{card.Name} returns to {owner.Name}'s Deck."
                    : $"{card.Name} is put{faceUpNote} in {deckHolder.Name}'s Deck.");
            }

            // DeckReturnsAndMassEffects batch (v161): the one "returned from the field to the Deck" event (Serpentine
            // Princess's trigger is queued with the simultaneous triggers).
            TextEffects.DeckReturnsAndMassEffectsRuntime.NoteReturnedToDeck(this, owner, card, wasFaceUp);
            return true;
        }

        /// <summary>
        /// The pile a card leaving the field goes to: its owner's if control changed,
        /// else the current controller's. Clears the control bookkeeping.
        /// </summary>
        DuelistState PileOwner(DuelistState controller, CardInstance card)
        {
            if (card == null) return controller;
            var owner = card.ControlOwner;
            card.ControlOwner = null;
            card.ReturnControlAtEndOfTurn = -1;
            card.ControlBoundTo = null;
            return owner ?? controller;
        }

        /// <summary>
        /// Lose LP by a card effect (not damage: no damage windows / reflection), then
        /// check for a loss. The Immortal of Thunder / Jirai Gumo family.
        /// </summary>
        public void LoseLifePoints(DuelistState who, int amount, string sourceName = null)
        {
            if (who == null || amount <= 0) return;
            who.LifePoints = Mathf.Max(0, who.LifePoints - amount);
            var src = string.IsNullOrEmpty(sourceName) ? "effect" : sourceName;
            Log($"{who.Name} loses {amount} LP ({src}) → {who.LifePoints} LP.");
            if (!IsAwaitingResponse)
                CheckLpWin(Player, Opponent);
        }

        /// <summary>A card effect ends the Duel (Exodia). Null winner = draw.</summary>
        public void EndGameByCardEffect(DuelistState winner, string reason)
        {
            if (GameOver) return;
            if (!string.IsNullOrEmpty(reason))
                Log(winner == null ? $"★ {reason}" : $"★ {winner.Name} wins: {reason}.");
            // LastTurn batch (v163): the result is a card effect's (EndReason CardEffect); no winner is a DRAW.
            _drawByCardEffect = winner == null;
            EndGame(winner, DuelEndReason.CardEffect);
        }

        /// <summary>LastTurn batch (v163): the DRAW was declared by a card effect, not by both players reaching 0 LP.</summary>
        bool _drawByCardEffect;

        public CardInstance CreateToken(string name, string race, string attribute, int level,
            int atk, int def)
        {
            var defn = new WRLDZ.Data.CardDef
            {
                id = StableTokenId(name),
                name = string.IsNullOrEmpty(name) ? "Token" : name,
                type = "Token Monster",
                frameType = "token",
                race = race ?? "",
                attribute = attribute ?? "",
                level = Math.Max(1, level),
                atk = Math.Max(0, atk),
                def = Math.Max(0, def),
                desc = "This card cannot exist except as a Token."
            };
            var inst = CreateInstance(defn.id);
            inst.Def = defn;
            inst.CardId = defn.id;
            inst.IsToken = true;
            inst.FaceUp = true;
            inst.WasSpecialSummoned = true;
            return inst;
        }

        /// <summary>
        /// Token CardId from FNV-1a of the token name (R2): identical in every process and on every
        /// runtime (string.GetHashCode is randomized per process on CoreCLR). Always in [-(2^31-1), -1].
        /// </summary>
        public static int StableTokenId(string name)
        {
            var h = 2166136261u;
            foreach (var ch in string.IsNullOrEmpty(name) ? "Token" : name)
            {
                h ^= ch;
                h = unchecked(h * 16777619u);
            }

            return -(int)(h % int.MaxValue) - 1;
        }

        public void ClearUntilEndOfTurnStatMods()
        {
            foreach (var who in new[] { Player, Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (m == null) continue;
                    m.UntilEndOfTurnAtk = 0;
                    m.UntilEndOfTurnDef = 0;
                    m.SwapOriginalAtkDefThisTurn = false;
                    m.DamageStepAtkBonus = 0;
                }
            }

            // SummonLocksAndStatRules batch — Amazoness Spellcaster: switched original ATK returns.
            TextEffects.SummonLocksAndStatRulesRuntime.EndOfTurn(this);
        }

        static bool HasCompiledClause(CardInstance card, Func<EffectClause, bool> predicate)
        {
            if (card?.Def == null || predicate == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.ClauseList != null &&
                   prog.ClauseList.Exists(c => c != null && predicate(c));
        }

        /// <summary>
        /// Remove every equip link from a host leaving the field. True Equip Spells
        /// and ordinary equip links still go to the GY; bound traps such as
        /// Spellbinding Circle (<see cref="EffectClause.DestroyThisWhenBoundHostDestroyed"/>) only
        /// leave when the host was actually destroyed. When the host leaves other than to the GY
        /// (bounce / banish / Deck), link cards that are not Equip Cards (Call of the Haunted,
        /// Spellbinding Circle — <see cref="TextEffects.TextEffectRuntime.IsRealEquipCard"/>) stay face-up.
        /// </summary>
        void UnlinkEquips(CardInstance host, bool hostDestroyed, DuelistState fallbackOwner = null,
            bool leavingToGy = true, bool hostStaysOnField = false)
        {
            if (host?.Equips == null || host.Equips.Count == 0) return;
            var eqs = host.Equips.ToList();
            host.Equips.Clear();
            // Sent by rule after their monster left: a Banisher of the Light that left with it no longer applies
            // (RedirectAndReplace batch, v142).
            using var byRule = TextEffects.RedirectAndReplaceRuntime.EnterRuleSend();
            // GuardiansAndEquipEvents batch: Morale Boost — the Equip Spells this monster loses leave together (one event).
            using var lostTargets = TextEffects.GuardiansAndEquipEventsRuntime.EnterEquipLeaveEvent(this);
            foreach (var eq in eqs)
            {
                if (eq == null) continue;
                // REASON_LOST_TARGET: an Equip Spell Card that goes to the GY because its monster left (Morale Boost).
                var lostTargetController = ControllerOf(eq) ?? fallbackOwner;
                eq.EquippedTo = null;
                // A linked Continuous Spell/Trap whose effects are negated (Metal Detector this turn)
                // does not destroy itself when its monster leaves; it stays face-up with no link.
                if (TextEffects.TurnNegationTrapsRuntime.NegatedLinkStaysWhenHostLeaves(eq)) continue;
                if (hostDestroyed)
                {
                    // "When that monster is destroyed, destroy this card" (Call of the Haunted, Spellbinding
                    // Circle) is an effect of a link card that is not an Equip Card: while that card is
                    // negated (Royal Decree, or Jinzo as the destroyed monster itself) it stays face-up,
                    // unlinked (Lua: a FIELD continuous effect without EFFECT_FLAG_CANNOT_DISABLE).
                    if (eq.IsNegated && !TextEffects.TextEffectRuntime.IsRealEquipCard(eq)) continue;
                    // Banisher of the Light (v142): sent once the event that removed its monster is over.
                    TextEffects.GuardiansAndEquipEventsRuntime.NoteEquipLeftField(this, eq, lostTargetController);
                    if (TextEffects.RedirectAndReplaceRuntime.TryDeferRuleSend(this, eq, fallbackOwner)) continue;
                    SendCardToGrave(ControllerOf(eq) ?? fallbackOwner, eq);
                    continue;
                }

                // StandbyBurnsAndDice batch (v162): "When it leaves the field, destroy this card" (Nightmare Wheel) — any
                // way the bound monster leaves the field, not only its destruction; not while this card is negated.
                if (!hostStaysOnField && !eq.IsNegated && HasCompiledClause(eq, c => c.DestroyThisWhenBoundHostLeaves))
                {
                    TextEffects.GuardiansAndEquipEventsRuntime.NoteEquipLeftField(this, eq, lostTargetController);
                    if (TextEffects.RedirectAndReplaceRuntime.TryDeferRuleSend(this, eq, fallbackOwner)) continue;
                    SendCardToGrave(ControllerOf(eq) ?? fallbackOwner, eq);
                    continue;
                }

                if (HasCompiledClause(eq, c => c.DestroyThisWhenBoundHostDestroyed)) continue;
                if (!leavingToGy && !TextEffects.TextEffectRuntime.IsRealEquipCard(eq)) continue;
                TextEffects.GuardiansAndEquipEventsRuntime.NoteEquipLeftField(this, eq, lostTargetController);
                if (TextEffects.RedirectAndReplaceRuntime.TryDeferRuleSend(this, eq, fallbackOwner)) continue;
                SendCardToGrave(ControllerOf(eq) ?? fallbackOwner, eq);
            }
        }

        /// <summary>
        /// This card (an equip / link card) leaves the field: unlink it from its host. A Call of the
        /// Haunted-style link destroys its host ("destroy that monster" is a destruction); Premature
        /// Burial (<see cref="EffectClause.DestroyHostOnlyIfThisDestroyed"/>) only when this card was destroyed.
        /// </summary>
        void UnlinkCardFromHostOnLeave(DuelistState leavingOwner, CardInstance card, bool fromFieldSt,
            bool destroyed = true)
        {
            if (card?.EquippedTo == null) return;
            var host = card.EquippedTo;
            // GuardiansAndEquipEvents batch: Morale Boost — an Equip Spell Card leaves the field while equipped
            // (Lua GetEquipTarget()~=nil on EVENT_LEAVE_FIELD), to any destination.
            if (fromFieldSt)
                TextEffects.GuardiansAndEquipEventsRuntime.NoteEquipLeftField(this, card, leavingOwner);
            host.Equips.Remove(card);
            card.EquippedTo = null;
            RevertEquipTakeControl(leavingOwner, card, host);
            // A negated link card that is not an Equip Card (Call of the Haunted / Soul Resurrection under
            // Royal Decree) leaves without destroying its monster: their Lua checks IsDisabled() on
            // EVENT_LEAVE_FIELD_P. Equip Cards (Premature Burial, Battle-Scarred) have no such check: their
            // leave-field destruction applies once they are off the field, negated or not.
            var negatedLinkSparesHost = card.IsNegated && !TextEffects.TextEffectRuntime.IsRealEquipCard(card);
            if (fromFieldSt && !negatedLinkSparesHost && HasCompiledClause(card, c =>
                    c.DestroyHostWhenThisLeaves && (destroyed || !c.DestroyHostOnlyIfThisDestroyed)))
            {
                var hostOwner = ControllerOf(host);
                // EquipAndSpellProtection batch: a link card that is not an Equip Card (Call of the Haunted) destroys the
                // monster it targets (Lua rc:IsHasCardTarget(tc)) — Heart of Clear Water protects that monster.
                using var linkTarget = TextEffects.TextEffectRuntime.IsRealEquipCard(card)
                    ? null
                    : TextEffects.EquipAndSpellProtectionRuntime.EnterTargetedResolution(card, host);
                if (hostOwner != null)
                    DestroyMonster(hostOwner, host, card);
            }
            // EquipLocksAndLeaveField batch: Big Bang Shot — "When this card leaves the field, banish the equipped
            // monster" (Lua e5 EVENT_LEAVE_FIELD: any reason; an Equip Card's leave-field effect applies once it is off
            // the field, negated or not, as Premature Burial's above).
            else if (fromFieldSt && HasCompiledClause(card, c => c.BanishHostWhenThisLeaves))
            {
                var hostOwner = ControllerOf(host);
                if (hostOwner != null && hostOwner.TryFindMonster(host, out _))
                    BanishCard(hostOwner, host, destroyed: false, by: leavingOwner);
            }
        }

        public void SendCardToGrave(DuelistState owner, CardInstance card, CardInstance sentBy = null)
        {
            if (owner == null || card == null) return;
            // Banisher of the Light (RedirectAndReplace batch, v142): decided while the card is still where it is,
            // so a face-up Banisher sent to the GY is banished by its own effect.
            var banishInstead = TextEffects.RedirectAndReplaceRuntime.SendsToBanishedInstead(this, card);
            var fromFieldMonster = owner.TryFindMonster(card, out var mi);
            var fromFieldSt = owner.TryFindSpellTrap(card, out var si);
            // S/T leaving field:
            // · Activation resolve → fade hologram (SpellActivationPresentation)
            // · Destroyed by effect (MST / Heavy Storm) → shatter
            if (fromFieldSt)
            {
                if (Presentation.ArInteraction.SpellActivationPresentation.WantsFadeToGy(card.InstanceId))
                {
                    // Fade already queued — do not shatter
                }
                else
                    CardShatterPresentation.QueueEffect(card.InstanceId, card.Name);
            }
            if (fromFieldMonster)
                owner.MonsterZones[mi].Occupant = null;
            if (fromFieldSt)
                owner.SpellTrapZones[si].Occupant = null;
            if (owner.FieldSpellZone != null && owner.FieldSpellZone.Occupant == card)
                owner.FieldSpellZone.Occupant = null;
            if (owner.PendulumZones != null)
            {
                for (var i = 0; i < owner.PendulumZones.Length; i++)
                    if (owner.PendulumZones[i]?.Occupant == card)
                        owner.PendulumZones[i].Occupant = null;
            }

            // A Semi-Nomi that went through the hand must be properly summoned again (TributeSummons batch).
            if (owner.Hand.Remove(card)) card.ProcedureCompleted = false;
            var pile = PileOwner(owner, card);
            if (banishInstead)
            {
                // Banished instead: it was never sent to the GY, so no "sent to the GY" event or trigger. It still
                // left the field — its links and Equip Cards come off as for any banish (BanishCard).
                TextEffects.RedirectAndReplaceRuntime.BanishInsteadOfGy(this, pile, card);
                PendingTributes.Remove(card);
                UnlinkCardFromHostOnLeave(owner, card, fromFieldSt);
                if (fromFieldMonster)
                    UnlinkEquips(card, hostDestroyed: false, fallbackOwner: owner, leavingToGy: false);
                return;
            }

            // FlipCostsAndFusion batch: a new trip to the GY is not as Fusion Material (TryResolveFusion stamps after).
            card.SentAsFusionMaterialFor = null;
            if (!pile.Graveyard.Contains(card))
                pile.Graveyard.Add(card);
            // BattleDestroyedGyRecovery (v162): GY-entry stamp — sent by a card's effect, or for an unknown reason.
            TextEffects.GyEntryStamps.Entered(this, card,
                sentBy != null ? GyEntryReason.Effect : GyEntryReason.None,
                TextEffects.GyEntryStamps.HolderOf(this, sentBy));
            PendingTributes.Remove(card);
            UnlinkCardFromHostOnLeave(owner, card, fromFieldSt);
            if (fromFieldMonster)
                UnlinkEquips(card, hostDestroyed: false, fallbackOwner: owner);

            GameEvents.Add(DuelGameEventKind.SentToGy, TurnNumber, owner, card, sentBy);
            // Michizure (BattleWindows batch): a monster sent from the field to its owner's GY.
            if (fromFieldMonster)
            {
                TextEffects.BattleWindowsRuntime.NoteMonsterSentFromField(this, pile, card);
                // NamedSummonPairs batch: A Deal with Dark Ruler's record (previous controller, Level).
                TextEffects.NamedSummonPairsRuntime.NoteMonsterSentToGy(this, owner, card);
            }

            if (fromFieldMonster || fromFieldSt)
            {
                MarkSentFromFieldToGy(card, sentBy, destroyedByBattle: false, battleDestroyer: null);
                // BothSideAndMonsterEquip (v161): a monster Card that left a Spell & Trap Zone (an opponent's monster
                // Sword Hunter equipped) activates its "sent from the field to the GY" effect from the GY it landed in,
                // as that GY's owner (ygopro: a card in the GY is controlled by its owner; Sangan / Witch check only
                // IsPreviousLocation(LOCATION_ONFIELD)) — never for the player whose zone it left.
                var triggerPlayer = fromFieldSt && card.Def != null && card.Def.IsMonster ? pile : owner;
                // A Summon's Tributes: their triggers wait until that Summon is negated or succeeds.
                if (_holdMaterialGyTriggers)
                    _heldMaterialGyTriggers.Add((triggerPlayer, card));
                else
                    MonsterEffects.OnSentFromFieldToGy(this, triggerPlayer, card, destroyed: false,
                        destroyedByBattle: false, battleDestroyer: null);
            }
        }

        /// <summary>Take the Tributes' held field → GY triggers (see <see cref="_holdMaterialGyTriggers"/>).</summary>
        List<(DuelistState owner, CardInstance card)> TakeHeldMaterialGyTriggers()
        {
            if (_heldMaterialGyTriggers.Count == 0) return null;
            var held = new List<(DuelistState owner, CardInstance card)>(_heldMaterialGyTriggers);
            _heldMaterialGyTriggers.Clear();
            return held;
        }

        /// <summary>The Summon was negated or succeeded: its Tributes' held triggers activate now.</summary>
        void FlushMaterialGyTriggers(List<(DuelistState owner, CardInstance card)> held)
        {
            if (held == null) return;
            // FlipMassEffects (v142): the Tributes left together — a trigger that opens a choice (Spear Cretin)
            // keeps it open while the others wait.
            TextEffects.FlipMassEffectsRuntime.Simultaneously(this, () =>
            {
                foreach (var (owner, card) in held)
                {
                    if (GameOver) return;
                    MonsterEffects.OnSentFromFieldToGy(this, owner, card, destroyed: false,
                        destroyedByBattle: false, battleDestroyer: null);
                }
            });
        }

        void MarkSentFromFieldToGy(CardInstance card, CardInstance sentBy, bool destroyedByBattle,
            CardInstance battleDestroyer)
        {
            if (card == null) return;
            TextEffects.EquipFamilyRuntime.ForgetDestroyRecord(card); // Dark Necrofear: recorded by DestroyMonster
            card.SentFromFieldTurnNumber = TurnNumber;
            card.WasDestroyedByBattle = destroyedByBattle;
            card.BattleDestroyer = battleDestroyer;
            card.SentByContinuousSpellEffect =
                sentBy?.Def != null && sentBy.Def.IsSpell && sentBy.Def.IsContinuousSpellOrTrap;
            card.SentByOpponentEffect =
                sentBy != null && this.OpponentOf(this.ControllerOf(card) ?? card.ControlOwner) == this.ControllerOf(sentBy);
        }

        /// <summary>EquipFamily batch (Collected Power moves Snatch Steal): its former monster's control returns.</summary>
        public void RevertEquipTakeControlPublic(DuelistState equipOwner, CardInstance equip, CardInstance host) =>
            RevertEquipTakeControl(equipOwner, equip, host);

        void RevertEquipTakeControl(DuelistState equipOwner, CardInstance equip, CardInstance host)
        {
            if (host == null || !host.TakenByEquipControl || equip?.Def == null) return;
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(equip.Def);
            if (prog == null ||
                !prog.ClauseList.Exists(c => c != null && c.TakeControlOfTarget))
                return;
            host.TakenByEquipControl = false;
            var other = OpponentOf(equipOwner);
            if (other == null || ControllerOf(host) != equipOwner) return;
            if (!TryTakeControl(other, host))
                SendCardToGrave(equipOwner, host);
        }

        /// <summary>
        /// Return a card on the field to its current controller's hand
        /// (Extra Deck monsters go back to the Extra Deck). Does not trigger field→GY.
        /// </summary>
        /// <param name="by">ForcedActivationAndSubstitution batch (v163): the player whose card effect returns it (ygopro
        /// reason player), or null when unknown. A monster an opponent's effect returns to a hand opens Major Riot's
        /// window (<see cref="TextEffects.MajorRiotRuntime.NoteReturnedToHand"/>). A Token leaves the field (it never
        /// reaches the hand).</param>
        public bool ReturnCardToHand(CardInstance card, DuelistState by = null)
        {
            if (card == null) return false;
            var owner = ControllerOf(card);
            if (owner == null) return false;

            var fromFieldMonster = owner.TryFindMonster(card, out _);
            var fromFieldSt = owner.TryFindSpellTrap(card, out _);
            var wasFaceUp = card.FaceUp; // Spirit's Invitation (ContinuousTrapsAndSpiritSupport): IsPreviousPosition(FACEUP)
            if (fromFieldMonster)
                UnlinkEquips(card, hostDestroyed: false, fallbackOwner: owner, leavingToGy: false);
            UnlinkCardFromHostOnLeave(owner, card, fromFieldSt, destroyed: false);
            DetachFromField(owner, card);
            // Presentation only: an unresolved activation bounced outside any resolving effect is
            // abandoned (beat closed); a resolved one keeps its beat.
            Presentation.ArInteraction.SpellActivationPresentation.AbandonActivation(card.InstanceId);
            PendingTributes.Remove(card);
            card.FaceUp = true;
            // Back in the hand, a Semi-Nomi must be properly summoned again (TributeSummons batch).
            card.ProcedureCompleted = false;
            card.Position = BattlePosition.Attack;
            card.SummonedThisTurn = false;
            card.SetThisTurn = false;
            card.ClearAttackFlags();
            card.ChangedPositionThisTurn = false;
            card.AtkModifier = 0;
            card.DefModifier = 0;
            card.LevelModifier = 0;
            card.LingeringAtkModifier = 0;
            card.LingeringDefModifier = 0;
            card.WasDestroyedByBattle = false;
            card.BattleDestroyer = null;
            card.SentByContinuousSpellEffect = false;
            card.SentFromFieldTurnNumber = 0;

            owner = PileOwner(owner, card);
            if (card.IsToken)
                Log($"{card.Name} leaves the field (Token)."); // ForcedActivationAndSubstitution batch (v163)
            else if (card.Def != null && card.Def.IsExtraDeck)
            {
                owner.ExtraDeck.Add(card.CardId);
                Log($"{card.Name} returned to the Extra Deck.");
            }
            else
            {
                owner.Hand.Add(card);
                Log($"{card.Name} returned to {owner.Name}'s hand.");
                // ForcedActivationAndSubstitution batch (v163): Major Riot's event (a monster to a hand by an effect).
                if (fromFieldMonster)
                    TextEffects.MajorRiotRuntime.NoteReturnedToHand(this, owner, card, by);
                // Spring of Rebirth: monster(s) from field → owner's hand (not S/T bounce, not ED).
                if (fromFieldMonster)
                {
                    // Spirit's Invitation (ContinuousTrapsAndSpiritSupport batch): a face-up Spirit back in this hand.
                    TextEffects.ContinuousTrapsAndSpiritSupportRuntime.NoteMonsterReturnedToHand(this, owner, card,
                        wasFaceUp);
                    TextEffects.TextEffectRuntime.NotifyMonstersReturnedToOwnerHand(this, 1);
                }
            }

            return true;
        }

        public DuelistState ControllerOf(CardInstance card)
        {
            if (card == null) return null;
            if (Player != null && ControlsOnField(Player, card)) return Player;
            if (Opponent != null && ControlsOnField(Opponent, card)) return Opponent;
            return null;
        }

        static bool ControlsOnField(DuelistState who, CardInstance card)
        {
            if (who == null || card == null) return false;
            if (who.TryFindMonster(card, out _)) return true;
            if (who.TryFindSpellTrap(card, out _)) return true;
            if (who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card) return true;
            if (who.PendulumZones != null)
            {
                foreach (var z in who.PendulumZones)
                    if (z?.Occupant == card) return true;
            }

            return false;
        }

        static void DetachFromField(DuelistState owner, CardInstance card)
        {
            if (owner == null || card == null) return;
            if (owner.TryFindMonster(card, out var mi))
                owner.MonsterZones[mi].Occupant = null;
            if (owner.TryFindSpellTrap(card, out var si))
                owner.SpellTrapZones[si].Occupant = null;
            if (owner.FieldSpellZone != null && owner.FieldSpellZone.Occupant == card)
                owner.FieldSpellZone.Occupant = null;
            if (owner.PendulumZones == null) return;
            for (var i = 0; i < owner.PendulumZones.Length; i++)
                if (owner.PendulumZones[i]?.Occupant == card)
                    owner.PendulumZones[i].Occupant = null;
        }

        public void DestroyMonsterPublic(DuelistState owner, CardInstance card) =>
            DestroyMonster(owner, card, null);

        public void DestroyMonsterPublic(DuelistState owner, CardInstance card, CardInstance byEffect) =>
            DestroyMonster(owner, card, byEffect);

        public void DestroyMonsterPublic(DuelistState owner, CardInstance card, CardInstance byEffect,
            bool banishIfDestroyed) =>
            DestroyMonster(owner, card, byEffect, banishIfDestroyed);

        public void DestroyMonsterByBattle(DuelistState owner, CardInstance card, CardInstance attacker) =>
            DestroyMonster(owner, card, null, false, true, attacker);

        /// <summary>
        /// Generic Special Summon entry point. A printed hard-Nomi restriction
        /// remains closed here; callers must identify a registered summon
        /// procedure explicitly.
        /// </summary>
        public bool SpecialSummonToField(DuelistState who, CardInstance card, BattlePosition pos, bool faceUp) =>
            SpecialSummonToField(who, card, pos, faceUp, SummonKind.SpecialSummon);

        public bool CanSpecialSummonProcedure(DuelistState who, CardInstance card)
        {
            // RuleLocks batch: Jowgen the Spiritualist / Jam Breeding Machine; Chain Energy's toll (from the hand).
            if (TextEffects.RuleLocksRuntime.SpecialSummonLocked(this, who, card)) return false;
            if (TextEffects.RuleLocksRuntime.HandActionUnaffordable(this, who, card, out _)) return false;
            // Compiled "Must first be Special Summoned (from your hand) … while you control "X"." (Toons batch).
            // "You can Special Summon this card (from your hand)." (Gilasaurus, NegationsAndGySummons batch).
            if (TextEffects.NegationsAndGySummonsRuntime.HasOwnHandProcedure(card))
                return TextEffects.NegationsAndGySummonsRuntime.CanSummonByOwnHandProcedure(this, who, card, out _);
            if (TextEffects.ToonsRuntime.HasHandProcedure(card))
                return TextEffects.ToonsRuntime.CanSummonByHandProcedure(this, who, card, out _);
            if (SummonProcedures.CheckSpecial(this, who, card, OfficialEffectRegistry.CocoonMothProc).Legal)
                return true;
            return SummonProcedures.CheckSpecial(this, who, card, OfficialEffectRegistry.GateGuardianProc).Legal;
        }

        /// <summary>
        /// Toons batch (v142): perform a card's own "Must first be Special Summoned (from your hand) …" procedure,
        /// whose legality and Tributes <see cref="TextEffects.ToonsRuntime"/> already decided. The Tributes are the
        /// cost: they go to the GY first, and their "sent from the field to the GY" triggers wait for the Summon
        /// (as a Tribute Summon's do). The card is then Special Summoned from the hand in face-up Attack Position
        /// (or the UI's pending placement) with a summon-negation window. It counts as properly summoned
        /// (<see cref="CardInstance.ProcedureCompleted"/>) unless that Summon is negated.
        /// </summary>
        internal bool SpecialSummonByOwnHandProcedure(DuelistState who, CardInstance card,
            List<CardInstance> tributes)
        {
            if (who?.Hand == null || card == null || !who.Hand.Contains(card)) return false;
            List<(DuelistState owner, CardInstance card)> held = null;
            if (tributes != null && tributes.Count > 0)
            {
                _holdMaterialGyTriggers = true;
                try
                {
                    foreach (var t in tributes)
                    {
                        Log($"Tributed: {t.Name}");
                        TextEffects.ReleaseEvents.Record(this, t, who); // EngineHygiene: EVENT_RELEASE
                        // A Soul Exchange target leaves its controller's field.
                        SendCardToGrave(ControllerOf(t) ?? who, t);
                    }
                }
                finally
                {
                    _holdMaterialGyTriggers = false;
                    held = TakeHeldMaterialGyTriggers();
                }
            }

            PendingTributes.Clear();
            if (GameOver)
            {
                FlushMaterialGyTriggers(held);
                return false;
            }

            who.Hand.Remove(card);
            MarkSpecialSummonFromHand(card); // Chain Energy toll (RuleLocks batch)
            // Proper summon (ygopro STATUS_PROC_COMPLETE); NegateSummonInProgress clears it again.
            card.ProcedureCompleted = true;
            _inherentProcedureSummon = true;
            _procedureSummonNegatable = true;
            _procedureHeldGyTriggers = held;
            bool summoned;
            try
            {
                summoned = SpecialSummonToField(who, card, BattlePosition.Attack, true);
            }
            finally
            {
                _inherentProcedureSummon = false;
                _procedureSummonNegatable = false;
            }

            // Not handed to a negation window (the Summon failed before placement): activate them now.
            var leftover = _procedureHeldGyTriggers;
            _procedureHeldGyTriggers = null;
            if (!summoned)
            {
                card.ProcedureCompleted = false;
                if (!who.Hand.Contains(card)) who.Hand.Add(card);
            }

            FlushMaterialGyTriggers(leftover);
            return summoned;
        }

        public bool TrySpecialSummonProcedure(DuelistState who, CardInstance card)
        {
            // RuleLocks batch: refused before any Tribute is paid (Jowgen the Spiritualist / Jam Breeding Machine).
            if (TextEffects.RuleLocksRuntime.SpecialSummonLocked(this, who, card, out var procedureLock))
            {
                Log($"Illegal Special Summon: {procedureLock}");
                return false;
            }
            if (TextEffects.RuleLocksRuntime.HandActionUnaffordable(this, who, card, out var procedureToll))
            {
                Log($"Illegal Special Summon: {procedureToll}");
                return false;
            }
            if (TextEffects.NegationsAndGySummonsRuntime.HasOwnHandProcedure(card))
                return TextEffects.NegationsAndGySummonsRuntime.TrySummonByOwnHandProcedure(this, who, card);
            if (TextEffects.ToonsRuntime.HasHandProcedure(card))
                return TextEffects.ToonsRuntime.TrySummonByHandProcedure(this, who, card);
            if (card != null && OfficialEffectRegistry.CocoonTurnsNeeded(card.CardId) > 0)
            {
                if (!SummonProcedures.CheckSpecial(this, who, card, OfficialEffectRegistry.CocoonMothProc).Legal)
                    return false;
                var need = OfficialEffectRegistry.CocoonTurnsNeeded(card.CardId);
                var trib = OfficialEffectRegistry.FindCocoonMothTribute(who, need);
                if (trib == null) return false;
                TextEffects.ReleaseEvents.Record(this, trib, who); // EngineHygiene: EVENT_RELEASE
                SendCardToGrave(who, trib);
                who.Hand.Remove(card);
                MarkSpecialSummonFromHand(card); // Chain Energy toll (RuleLocks batch)
                // A Special Summon by procedure (not by an effect) has a summon-negation window.
                _procedureSummonNegatable = true;
                var moth = SpecialSummonToField(who, card, BattlePosition.Attack, true);
                if (moth)
                    Log($"{who.Name} Special Summons {card.Name} by Tributing Petit Moth (Cocoon turn {need}+).");
                return moth;
            }

            if (!SummonProcedures.CheckSpecial(this, who, card, OfficialEffectRegistry.GateGuardianProc).Legal)
                return false;
            var sanga = OfficialEffectRegistry.FindFaceUpById(who, OfficialEffectRegistry.SangaOfTheThunderId);
            var kazejin = OfficialEffectRegistry.FindFaceUpById(who, OfficialEffectRegistry.KazejinId);
            var suijin = OfficialEffectRegistry.FindFaceUpById(who, OfficialEffectRegistry.SuijinId);
            if (sanga == null || kazejin == null || suijin == null) return false;
            foreach (var gateTribute in new[] { sanga, kazejin, suijin })
                TextEffects.ReleaseEvents.Record(this, gateTribute, who); // EngineHygiene: EVENT_RELEASE
            SendCardToGrave(who, sanga);
            SendCardToGrave(who, kazejin);
            SendCardToGrave(who, suijin);
            who.Hand.Remove(card);
            MarkSpecialSummonFromHand(card); // Chain Energy toll (RuleLocks batch)
            _inherentProcedureSummon = true;
            _procedureSummonNegatable = true;
            bool summoned;
            try
            {
                summoned = SpecialSummonToField(who, card, BattlePosition.Attack, true);
            }
            finally
            {
                _inherentProcedureSummon = false;
                _procedureSummonNegatable = false;
            }

            if (summoned)
                Log($"{who.Name} Special Summons {card.Name} by Tributing Sanga of the Thunder, Kazejin, and Suijin.");
            return summoned;
        }

        /// <summary>
        /// Special Summon with an explicit procedure kind. A printed hard-Nomi gate
        /// can only be opened by a procedure registered for this card and kind;
        /// compiled effect text never self-authorizes that exception.
        /// </summary>
        public bool SpecialSummonToField(DuelistState who, CardInstance card, BattlePosition pos,
            bool faceUp, SummonKind summonKind) =>
            SpecialSummonToField(who, card, pos, faceUp, summonKind, preferredZone: -1);

        /// <summary>
        /// Special Summon into a preferred Monster Zone. <paramref name="preferredZone"/>
        /// &lt; 0 uses a UI-pending column, then first empty.
        /// </summary>
        /// <summary>
        /// Set by a Graveyard Special Summon right before <see cref="SpecialSummonToField(DuelistState, CardInstance, BattlePosition, bool, SummonKind, int)"/>
        /// so "Special Summoned from the Graveyard" triggers (Molten Zombie) can tell the origin.
        /// Consumed (cleared) by the next Special Summon attempt.
        /// </summary>
        public bool NextSpecialSummonFromGraveyard { get; set; }

        CardInstance _specialSummonFromHand;

        /// <summary>
        /// RuleLocks batch (Chain Energy): <paramref name="card"/>, just taken from the hand, is the subject of the next
        /// Special Summon attempt, which then charges the hand toll (ygopro EFFECT_SPSUMMON_COST, range hand). Consumed by
        /// that attempt; a different card's Special Summon ignores it.
        /// </summary>
        public void MarkSpecialSummonFromHand(CardInstance card) => _specialSummonFromHand = card;

        /// <summary>
        /// RuleLocks batch (Chain Energy): hand cards whose activation toll was taken as the activation began
        /// (TextEffects.RuleLocksRuntime.BeginHandActivation); their own legality re-checks do not ask for it again.
        /// </summary>
        internal readonly HashSet<CardInstance> HandActionCostPaid = new();

        /// <summary>
        /// RuleLocks batch: a cost taken straight from LifePoints (Chain Energy's activation toll) left
        /// <paramref name="who"/> at 0 LP — the loss settles like one from <see cref="PayLifePoints"/>.
        /// </summary>
        internal void SettleLpLossAfterCost(DuelistState who)
        {
            if (who == null || who.LifePoints > 0) return;
            _lpLossPending = true;
            SettlePendingLpLoss();
        }

        public bool SpecialSummonToField(DuelistState who, CardInstance card, BattlePosition pos,
            bool faceUp, SummonKind summonKind, int preferredZone)
        {
            var fromGraveyard = NextSpecialSummonFromGraveyard;
            NextSpecialSummonFromGraveyard = false;
            var byProcedure = _procedureSummonNegatable;
            _procedureSummonNegatable = false;
            var fromHand = card != null && ReferenceEquals(_specialSummonFromHand, card);
            _specialSummonFromHand = null;
            if (who == null || card?.Def == null) return false;
            var officialText = OfficialCardAuthority.OfficialText(card);
            var procedureAuthorized = OfficialEffectRegistry.HasSummonProcedure(card.CardId, summonKind);
            // NamedSummonPairs batch: "Must be Special Summoned with "X" and cannot be Special Summoned by other ways"
            // — only X's own named Special Summon (ygopro nocheck=true) gets through.
            if (PsctGrammar.BlocksSpecialSummon(officialText) && !procedureAuthorized &&
                !TextEffects.NamedSummonPairsRuntime.NamedSummonerIsSummoning(card))
                return false;
            if (OfficialEffectRegistry.HasInherentHandTribute(card.CardId) && !_inherentProcedureSummon)
                return false;
            var summonGate = PsctGrammar.SummonGateNamed(officialText);
            if (!string.IsNullOrEmpty(summonGate) &&
                !FieldSpellEffects.ControlsFaceUpNamed(who, summonGate))
                return false;
            // "Must first be Special Summoned with "X"" (Wall Shadow, Harpie Lady Sisters): X's effect, or a
            // copy that was properly summoned before (TributeSummons batch).
            if (!TextEffects.TributeSummonsRuntime.SemiNomiSummonAllowed(card, out var completesProcedure))
                return false;
            // "Must first be Special Summoned (from your hand) … while you control "X"" (Toons batch): its own
            // procedure, or a copy that was properly summoned before (Monster Reborn from the GY).
            if (!_inherentProcedureSummon && TextEffects.ToonsRuntime.BlocksSpecialSummon(card))
                return false;
            // "This monster can only be Special Summoned by selecting 1 face-up "X" …" (Gradius' Option,
            // SpecialSummonEffects batch, v161): its own hand procedure only.
            if (!_inherentProcedureSummon && TextEffects.SpecialSummonEffectsRuntime.OnlyByOwnProcedure(card))
                return false;
            // RuleLocks batch: Jowgen the Spiritualist (neither player) / Jam Breeding Machine (its controller, except
            // its Token) — no Special Summon of any kind. Every caller restores the card when this returns false.
            if (TextEffects.RuleLocksRuntime.SpecialSummonLocked(this, who, card, out var summonLock))
            {
                Log($"{card.Name} cannot be Special Summoned: {summonLock}");
                return false;
            }
            // RuleLocks batch: Chain Energy — a Special Summon of a card from the hand needs its LP toll.
            if (fromHand && TextEffects.RuleLocksRuntime.HandSpecialSummonUnaffordable(this, who))
            {
                Log($"{card.Name} cannot be Special Summoned from the hand: " +
                    $"{TextEffects.RuleLocksRuntime.HandActionLpCost(this, who)} LP must be paid.");
                return false;
            }

            var posToUse = _pendingSpecialSummonPosition ?? pos;
            // Light of Intervention (LocksAndZones batch): a face-down Special Summon is made in face-up Defense
            // Position instead (ygopro EFFECT_LIGHT_OF_INTERVENTION in special_summon).
            if (!faceUp && TextEffects.LocksAndZonesRuntime.FaceDownSpecialSummonBecomesFaceUp(this))
            {
                faceUp = true;
                posToUse = BattlePosition.Defense;
                Log($"{card.Name} cannot be Special Summoned face-down: it is Special Summoned in face-up Defense Position.");
            }
            // SummonTriggersAndTributes batch (v163): "You can only control 1 face-up X" — no face-up Special Summon.
            if (faceUp && TextEffects.SummonTriggersAndTributesRuntime.UniqueBlocksFaceUp(this, who, card, out var uniqueReason))
            {
                Log($"{card.Name} cannot be Special Summoned: {uniqueReason}");
                return false;
            }
            // SummonLocksAndStatRules batch: Rivalry of Warlords — another Type only face-down (FORCE_SPSUMMON_POSITION).
            if (faceUp && TextEffects.SummonLocksAndStatRulesRuntime.BlocksFaceUpSummon(this, who, card, out var rivalryReason))
            {
                Log($"{card.Name} cannot be Special Summoned face-up: {rivalryReason}");
                return false;
            }
            var idx = preferredZone >= 0 ? preferredZone : _pendingSpecialSummonZone;
            ClearPendingSpecialSummonPlacement();
            if (idx < 0 || idx >= who.MonsterZones.Length || !who.MonsterZones[idx].IsEmpty)
                idx = FirstEmpty(who.MonsterZones);
            if (idx < 0) return false;
            // EngineHygiene batch: a per-card summon cost (ygopro EFFECT_SPSUMMON_COST, e.g. Tyrant Dragon from the GY).
            var summonOrigin = fromGraveyard ? TextEffects.SummonOrigin.Graveyard
                : fromHand ? TextEffects.SummonOrigin.Hand
                : TextEffects.SummonOrigin.Unknown;
            if (!TextEffects.SpecialSummonLegality.CanPaySummonCosts(this, who, card, summonOrigin))
            {
                Log($"{card.Name} cannot be Special Summoned: its summon cost cannot be paid.");
                return false;
            }
            if (fromHand)
                TextEffects.RuleLocksRuntime.PayHandActionCost(this, who, card, "Special Summon");
            if (!TextEffects.SpecialSummonLegality.PaySummonCosts(this, who, card, summonOrigin))
                return false;
            // FlipMassEffects (v142): a Ritual / Fusion Summon is a proper Summon — that copy may later be Special
            // Summoned from the GY or banishment (ygopro STATUS_PROC_COMPLETE; Spear Cretin's GY targets).
            if (summonKind == SummonKind.RitualSummon || summonKind == SummonKind.FusionSummon)
                card.ProcedureCompleted = true;
            // FlipMassEffects (v142): Special Summoned face-down, like a Set monster, it cannot be Flip Summoned this turn.
            if (!faceUp) card.SetThisTurn = true;
            ResetOnFieldEntry(card);
            if (completesProcedure) card.ProcedureCompleted = true;
            // "If Summoned this way" (NegationsAndGySummons batch): a Special Summon by the card's own procedure.
            card.SpecialSummonedByOwnProcedure = _inherentProcedureSummon && byProcedure;
            card.FaceUp = faceUp;
            // Entering face-up from a face-down pile is not a flip (Spirit return, SpiritsAndMisc batch).
            card.FaceUpStampAtTurnStart = card.FaceUpStamp;
            card.Position = posToUse;
            card.SummonedThisTurn = true;
            card.WasSpecialSummoned = true;
            card.WasTributeSummoned = false;
            card.ClearAttackFlags();
            card.LingeringAtkModifier = 0;
            card.LingeringDefModifier = 0;
            card.WasDestroyedByBattle = false;
            card.BattleDestroyer = null;
            card.SentByContinuousSpellEffect = false;
            card.SentFromFieldTurnNumber = 0;
            who.MonsterZones[idx].Occupant = card;
            // TokensDeclarationsAndPicks batch: this turn's summon record (Stray Lambs / Scapegoat).
            TextEffects.TokensDeclarationsAndPicksRuntime.NoteSummon(this, who);
            // "Cannot attack the turn it is Special Summoned." (Toons batch).
            TextEffects.ToonsRuntime.OnSpecialSummoned(this, card, faceUp);
            // Gradius' Option: link it to the selected face-up "Gradius" (SpecialSummonEffects batch, v161).
            TextEffects.SpecialSummonEffectsRuntime.OnSpecialSummoned(this, who, card);
            // A card's own procedure (Toons batch) hands its held Tribute triggers to the window.
            var procedureHeld = _procedureHeldGyTriggers;
            _procedureHeldGyTriggers = null;
            // A Special Summon by procedure (Gate Guardian, the Cocoon moths) waits on its summon-negation
            // window; one performed by a resolving effect cannot be negated and succeeds at once.
            if (faceUp && byProcedure &&
                TryOpenSummonNegationWindow(who, card, SummonKind.SpecialSummon, null, fromGraveyard,
                    heldTriggers: procedureHeld))
                return true;
            if (procedureHeld != null)
            {
                FlushMaterialGyTriggers(procedureHeld);
                if (GameOver) return true;
            }

            CompleteSpecialSummon(who, card, faceUp, fromGraveyard);
            return true;
        }

        /// <summary>
        /// A card's own Special Summon procedure from the hand that does not use the chain (The Fiend Megacyber:
        /// "you can Special Summon this card (from your hand)"). The activation's bookkeeping link is dropped and
        /// the Special Summon gets its summon-negation window, like Gate Guardian's. Call right before
        /// <see cref="SpecialSummonToField(DuelistState, CardInstance, BattlePosition, bool)"/>; a Summon made
        /// while a chain resolves still cannot be negated.
        /// </summary>
        internal void BeginInherentSpecialSummonFromHand(CardInstance card)
        {
            if (card == null || IsResolvingChain || Chain.IsResolving) return;
            DropUnresolvedLink(card);
            _procedureSummonNegatable = true;
        }

        /// <summary>
        /// LocksAndZones batch (v142): a monster banished "until" a later phase (Dimensionhole) returns to
        /// <paramref name="controller"/>'s Monster Zone <paramref name="zoneIndex"/> in the position it left
        /// (ygopro Duel.ReturnToField). This is not a Summon: no summon window, "when Summoned" effect or Flip
        /// effect, and it may change its battle position and attack as usual this turn. It is a new card on the
        /// field (nothing that applied to it before it was banished carries over). <paramref name="owner"/>
        /// is the duelist whose banished pile held it; the caller has removed it from that pile.
        /// </summary>
        public void PlaceReturnedMonster(DuelistState controller, DuelistState owner, CardInstance card,
            int zoneIndex, BattlePosition pos, bool faceUp)
        {
            if (controller == null || card == null) return;
            if (zoneIndex < 0 || zoneIndex >= controller.MonsterZones.Length ||
                !controller.MonsterZones[zoneIndex].IsEmpty)
                zoneIndex = FirstEmpty(controller.MonsterZones);
            if (zoneIndex < 0) return;
            ResetOnFieldEntry(card);
            card.FaceUp = faceUp;
            card.Position = faceUp ? pos : BattlePosition.Defense;
            card.SummonedThisTurn = false;
            card.SetThisTurn = false;
            card.ChangedPositionThisTurn = false;
            card.WasSpecialSummoned = false;
            card.WasTributeSummoned = false;
            card.ClearAttackFlags();
            card.EffectUsedThisTurn = false;
            card.DirectAttackThisTurn = false;
            card.DestroyedByBattleThisTurn = false;
            card.LingeringAtkModifier = 0;
            card.LingeringDefModifier = 0;
            card.WasDestroyedByBattle = false;
            card.BattleDestroyer = null;
            card.SentByContinuousSpellEffect = false;
            card.SentFromFieldTurnNumber = 0;
            card.Counters = 0;
            card.ControlRevertsAtEndPhase = false;
            card.TakenByEquipControl = false;
            card.ReturnControlAtEndOfTurn = -1;
            card.CannotAttackThroughTurn = -1;
            card.CannotBeTributedForSummon = false;
            card.IsNegated = false;
            card.ControlOwner = owner != null && owner != controller ? owner : null;
            controller.MonsterZones[zoneIndex].Occupant = card;
            Log(faceUp
                ? $"{card.Name} returns to the field in {card.Position} Position."
                : $"{(controller.IsPlayer ? card.Name : "A monster")} returns to the field face-down.");
            Notify();
        }

        void CompleteSpecialSummon(DuelistState who, CardInstance card, bool faceUp, bool fromGraveyard)
        {
            // Card of Safe Return (NegationsAndGySummons batch): "When a monster is Special Summoned from your GY".
            // The origin is the GY of the card's owner (Monster Reborn on an opponent's monster records it as
            // ControlOwner); face-down Special Summons count too.
            if (fromGraveyard)
                TextEffects.NegationsAndGySummonsRuntime.OnSpecialSummoned(this, card.ControlOwner ?? who, card);
            if (faceUp)
                TextEffects.TextEffectRuntime.TryResolveThisCardSummoned(
                    this, who, card, specialSummon: true, fromGraveyard: fromGraveyard);
            if (faceUp)
                OpenSummonResponseOrContinue(who, card, SummonKind.SpecialSummon);
        }

        // ───────────────────── Summon negation (SummonNegation batch, v141) ─────────────────────

        /// <summary>
        /// Open the summon-negation window for a Summon that was just placed (Normal / Tribute / Flip Summon,
        /// or a Special Summon by procedure) to the summoner's opponent, who may answer with a "When a
        /// monster(s) would be Summoned" Counter Trap (Solemn Judgment / Horn of Heaven). Lua: only while no
        /// chain is open (<c>Duel.GetCurrentChain(true) == 0</c>). Returns false (nothing opened: the caller
        /// completes the Summon now) when the opponent has nothing legal. An AI responder answers inside
        /// this call; the Summon then completes (or not) before it returns.
        /// </summary>
        bool TryOpenSummonNegationWindow(DuelistState summoner, CardInstance summoned, SummonKind kind,
            List<CardInstance> tributes, bool fromGraveyard,
            List<(DuelistState owner, CardInstance card)> heldTriggers = null,
            Dictionary<CardInstance, DuelistState> tributeOwners = null)
        {
            if (GameOver || summoner == null || summoned == null) return false;
            if (_summonInProgress != null || IsResolvingChain || Chain.HasLinks || IsAwaitingResponse ||
                IsAwaitingEffectTarget)
                return false;
            var responder = OpponentOf(summoner);
            if (responder == null) return false;
            _summonInProgress = new SummonInProgress
            {
                Summoner = summoner,
                Summoned = summoned,
                Kind = kind,
                Tributes = tributes != null ? new List<CardInstance>(tributes) : null,
                TributeOwners = tributeOwners,
                HeldGyTriggers = heldTriggers,
                FromGraveyard = fromGraveyard
            };
            var profile = CombatAnimTimings.ForSummon(summoned, kind);
            if (!OpenResponseWindow(responder, ResponseTiming.SummonNegation, null, null, null, summoned, summoner,
                    profile))
            {
                _summonInProgress = null;
                return false;
            }

            ActivePresentation = new ActiveCombatPresentation
            {
                Kind = CombatActionKind.SummonAppear,
                SourceCard = summoned,
                Actor = summoner,
                Profile = profile,
                Procedure = kind,
                StartedUnscaledTime = Clock.UnscaledTime,
                Clock = Clock,
                ImpactResolved = false
            };
            if (!IsHumanControlled(responder))
                SpellTrapEffects.AiAutoRespond(this);
            Notify();
            return true;
        }

        /// <summary>
        /// The summon-negation window closed (pass, or its chain resolved). A negated Summon ends here: it never
        /// succeeded. Otherwise the Summon's success steps run now.
        /// </summary>
        void FinishSummonNegationWindow()
        {
            var s = _summonInProgress;
            _summonInProgress = null;
            if (s == null || GameOver)
            {
                ClearPresentation();
                Notify();
                return;
            }

            // The Summon was negated or is about to succeed: its Tributes' triggers (Sangan's search) activate now.
            FlushMaterialGyTriggers(s.HeldGyTriggers);
            if (GameOver || s.Negated || s.Summoner == null || !s.Summoner.TryFindMonster(s.Summoned, out _))
            {
                ClearPresentation();
                Notify();
                return;
            }

            // The Summon succeeds now, after its negation chain: that is the last thing to happen, so a Spell/Trap
            // destroyed in that chain (Seven Tools on Solemn Judgment) can no longer be answered by Gust / Driving
            // Snow (SpellTrapDestroyedResponse). A FLIP effect run below may record a new destruction.
            TextEffects.SpellTrapDestroyedResponseRuntime.DropPending(this);
            switch (s.Kind)
            {
                case SummonKind.FlipSummon:
                    CompleteFlipSummon(s.Summoner, s.Summoned);
                    break;
                case SummonKind.SpecialSummon:
                    CompleteSpecialSummon(s.Summoner, s.Summoned, s.Summoned.FaceUp, s.FromGraveyard);
                    break;
                default:
                    CompleteNormalSummon(s.Summoner, s.Summoned, asSet: false, s.Tributes, s.TributeOwners);
                    break;
            }
        }

        /// <summary>
        /// Negate the Summon waiting in its summon-negation window (Horn of Heaven / Solemn Judgment) and destroy
        /// the monster. It was never Summoned and never on the field: it goes straight to the GY with no
        /// field-to-GY trigger (Sangan's Summon negated: no search). The Normal Summon stays used.
        /// Every card in the pool that negates a Summon also destroys the monster (<paramref name="destroy"/>).
        /// Returns false when <paramref name="summoned"/> is not that Summon any more.
        /// </summary>
        public bool NegateSummonInProgress(CardInstance summoned, CardInstance byCard, bool destroy)
        {
            var s = _summonInProgress;
            if (s == null || s.Negated || summoned == null || s.Summoned != summoned || s.Summoner == null)
                return false;
            if (!s.Summoner.TryFindMonster(summoned, out var zi)) return false;
            s.Negated = true;
            // A negated Summon never succeeded: the monster was not properly summoned (Toons batch — a Toon whose
            // own procedure is negated cannot be revived later).
            summoned.ProcedureCompleted = false;
            Log($"{byCard?.Name ?? "An effect"} negates the Summon of {summoned.Name}.");
            if (!destroy) return true;
            CardShatterPresentation.QueueEffect(summoned.InstanceId, summoned.Name);
            s.Summoner.MonsterZones[zi].Occupant = null;
            UnlinkEquips(summoned, hostDestroyed: true, fallbackOwner: s.Summoner);
            PendingTributes.Remove(summoned);
            var pile = PileOwner(s.Summoner, summoned);
            // Banisher of the Light (RedirectAndReplace batch, v142): banished instead. A Banisher whose own Summon
            // is negated never applied (it has already left its zone here).
            if (TextEffects.RedirectAndReplaceRuntime.PutInGraveyard(this, pile, summoned))
            {
                TextEffects.BattleWindowsRuntime.NoteMonsterSentFromField(this, pile, summoned);
                TextEffects.NamedSummonPairsRuntime.NoteMonsterSentToGy(this, s.Summoner, summoned);
            }
            GameEvents.Add(DuelGameEventKind.Destroyed, TurnNumber, s.Summoner, summoned, byCard);
            Log($"Destroyed: {summoned.Name} (its Summon was negated).");
            DuelPresentationPacer.HoldCombatResult();
            return true;
        }

        public void ForceEndBattlePhase(DuelistState turnPlayer)
        {
            if (Phase != DuelPhase.Battle || TurnPlayer != turnPlayer) return;
            EndBattlePhase();
            Phase = DuelPhase.Main2;
            Log("[Main Phase 2] (Battle ended by card effect)");
        }

        /// <summary>
        /// End Step of the Battle Phase: "at the end of the Battle Phase" work (Magical Hats dummies)
        /// and the step markers. Every way out of the Battle Phase runs this once — Main Phase 2,
        /// a card that ends the Battle Phase, or ending the turn straight from Battle (DR-11).
        /// </summary>
        void EndBattlePhase()
        {
            if (Phase != DuelPhase.Battle) return;
            BattleStep = BattleStep.EndStep;
            TextEffects.TextEffectRuntime.DestroyMagicalHatDummies(this);
            // "At the end of the Battle Phase" monster effects (Goblin Attack Force, Giant Orc).
            TextEffects.BattlePhaseEndRuntime.FireEndOfBattlePhase(this);
            // Fairy Box (BattleWindows batch): "ATK becomes 0 until the end of the Battle Phase" ends.
            TextEffects.BattleWindowsRuntime.EndBattlePhase(this);
            // Spirit Ryu (BattleBoosts batch, v161): "until the end of the Battle Phase" ATK/DEF ends.
            TextEffects.BattleBoostsRuntime.EndBattlePhase(this);
            // After the Struggle (BattleRules batch): the monsters that battled this turn are destroyed.
            TextEffects.BattleRulesRuntime.EndBattlePhase(this);
            BattleStep = BattleStep.None;
            DamageSubStep = DamageSubStep.None;
        }

        // ───────────────────── Phase transitions ─────────────────────

        public bool TryEnterBattlePhase(DuelistState who)
        {
            if (GameOver || TurnPlayer != who) return false;
            // Weather Report (SpellTrapDestroyedResponse batch): a second Battle Phase follows Main Phase 2.
            var secondBattlePhase = Phase == DuelPhase.Main2 &&
                                    TextEffects.SpellTrapDestroyedResponseRuntime.SecondBattlePhaseAvailable(this, who);
            if (Phase != DuelPhase.Main1 && !secondBattlePhase)
            {
                Log("Battle Phase is entered from Main Phase 1 only.");
                return false;
            }

            if (TurnNumber == 1 && who == FirstPlayer)
            {
                Log("Rule: the player who goes first cannot conduct a Battle Phase on their first turn.");
                return false;
            }

            if (TextEffects.TributeSummonsRuntime.BattlePhaseForbidden(this, who))
            {
                Log($"{who.Name} cannot conduct their Battle Phase this turn.");
                return false;
            }

            if (secondBattlePhase)
                TextEffects.SpellTrapDestroyedResponseRuntime.BeginSecondBattlePhase(this, who);
            Phase = DuelPhase.Battle;
            BattleStep = BattleStep.StartStep;
            DamageSubStep = DamageSubStep.None;
            // Start Step: registered triggers only; then open Battle Step
            BattleStep = BattleStep.BattleStep;
            Log(secondBattlePhase ? $"[{who.Name}] Battle Phase (second this turn)" : $"[{who.Name}] Battle Phase");
            OfferOpponentTurnTrapWindow(OpponentOf(who));
            Notify();
            return true;
        }

        /// <summary>From Battle → Main Phase 2 (or skip Battle: Main1 → Main2).</summary>
        public bool TryEnterMainPhase2(DuelistState who)
        {
            if (GameOver || TurnPlayer != who) return false;
            // An open response (the Battle-Phase-start trap window, a chain) is answered before the
            // phase changes. CanAttack already refuses in this state.
            if (IsAwaitingResponse || IsAwaitingEffectTarget) return false;
            if (Phase != DuelPhase.Battle && Phase != DuelPhase.Main1)
            {
                Log("Main Phase 2 follows Main Phase 1 or Battle Phase.");
                return false;
            }

            var from = Phase;
            EndBattlePhase();

            Phase = DuelPhase.Main2;
            if (from == DuelPhase.Main1)
                Log($"[{who.Name}] Main Phase 2 (Battle skipped)");
            else
                Log($"[{who.Name}] Main Phase 2");
            PendingTributes.Clear();
            Notify();
            return true;
        }

        public bool CanAttack(DuelistState who, CardInstance attacker)
        {
            if (GameOver || Phase != DuelPhase.Battle || TurnPlayer != who) return false;
            if (BattleStep != BattleStep.BattleStep)
                return false;
            if (IsAwaitingResponse || IsAwaitingEffectTarget) return false;
            if (HasDeclaredAttack) return false;
            if (Chain.HasLinks || Chain.IsResolving) return false;
            if (attacker == null) return false;
            if (attacker.AttacksDeclaredThisTurn >= MaxAttacksThisTurn(attacker)) return false;
            if (!attacker.FaceUp || attacker.Position != BattlePosition.Attack) return false;
            if (!who.TryFindMonster(attacker, out _)) return false;
            if (who.CannotDeclareAttackThisTurn) return false;
            // Official continuous: Swords of Revealing Light — opponent cannot declare an attack
            if (OpponentHasSwordsOfRevealingLight(who) &&
                !TextEffects.MonsterNegationAndControlLocksRuntime.IgnoresSpellAttackLock(this, who, attacker))
                return false;
            if (ContinuousCannotAttackBlocks(who, attacker))
                return false;
            if (attacker.CannotAttackDuringTurnNumber > 0 &&
                TurnNumber == attacker.CannotAttackDuringTurnNumber)
                return false;
            if (TextEffects.TextEffectRuntime.AttackLpCostBlocks(who, attacker))
                return false;
            // Paralyzing Potion / Electric Lizard lock (GitHub lineage)
            if (TextEffects.TextEffectRuntime.AttackForbiddenByEffect(this, attacker))
                return false;
            // Dark Elf / Toll / Gravekeeper's Servant (GitHub lineage): the attack cost must be payable
            // (unless this declaration already paid it — a replay after a coin call).
            if (!attacker.AttackCostsPaidThisDeclaration)
            {
                TextEffects.TextEffectRuntime.AttackCosts(this, who, attacker, out var lpCost, out var millCost);
                if (lpCost > 0 && who.LifePoints < lpCost)
                    return false;
                if (millCost > 0 && who.DeckCount < millCost)
                    return false;
            }
            var magnetHosts = MagnetismHosts(who);
            if (magnetHosts.Count > 1) return false;
            if (magnetHosts.Count == 1 &&
                !CanBeAttacked(attacker, magnetHosts[0]))
                return false;
            return true;
        }

        /// <summary>
        /// Face-up equips that force the opponent to attack the equipped monster.
        /// </summary>
        public List<CardInstance> MagnetismHosts(DuelistState attacker)
        {
            var hosts = new List<CardInstance>();
            var defender = OpponentOf(attacker);
            if (defender == null) return hosts;
            foreach (var st in defender.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.IsNegated || st.EquippedTo == null) continue;
                if (!defender.TryFindMonster(st.EquippedTo, out _)) continue;
                var prog = TextEffects.CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null) continue;
                var locks = false;
                foreach (var c in prog.ClausesFor(TextEffects.EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c != null &&
                        c.Action == TextEffects.EffectActionKind.AttacksMustTargetEquippedHost)
                    {
                        locks = true;
                        break;
                    }
                }

                if (locks && !hosts.Contains(st.EquippedTo))
                    hosts.Add(st.EquippedTo);
            }

            return hosts;
        }

        bool CanBeAttacked(CardInstance attacker, CardInstance target)
        {
            if (attacker == null || target == null) return false;
            var defender = OpponentOf(ControllerOf(attacker));
            if (defender == null || !defender.TryFindMonster(target, out _)) return false;
            if (ContinuousProtections.CannotBeAttackTarget(this, target)) return false;
            // Toon rule (Toons batch): only a face-up Toon while the opponent controls one.
            if (TextEffects.ToonsRuntime.ForbidsAttackTarget(this, attacker, target)) return false;
            return true;
        }

        /// <summary>
        /// Gravity Bind / Insect Barrier / Messenger of Peace: a face-up Continuous
        /// program forbids this monster from declaring an attack.
        /// </summary>
        public bool ContinuousCannotAttackBlocks(DuelistState attackerController, CardInstance attacker)
        {
            if (attacker?.Def == null) return false;
            // GitHub-lineage locks (Paralyzing Potion equip, Electric Lizard, Invitation to a Dark Sleep)
            // answer the same question.
            if (TextEffects.TextEffectRuntime.AttackForbiddenByEffect(this, attacker)) return true;
            // BattleRules batch: Cave Dragon — "cannot declare an attack unless you control another Dragon-Type monster".
            if (TextEffects.BattleRulesRuntime.AttackForbidden(this, attackerController, attacker)) return true;
            // DamageStepRules batch: Ultimate Obedient Fiend — only while it is its controller's only card, with no hand.
            if (TextEffects.DamageStepRulesRuntime.AttackForbidden(this, attackerController, attacker)) return true;
            // ContinuousLocksAndIgnitions batch (v163): Gora Turtle — a face-up monster's ATK-threshold attack lock.
            if (TextEffects.ContinuousLocksAndIgnitionsRuntime.MonsterAttackLockBlocks(this, attackerController, attacker))
                return true;
            foreach (var side in new[] { Player, Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st == null || !st.FaceUp || st.IsNegated || st.Def == null) continue;
                    // MonsterNegationAndControlLocks batch: Non-Spellcasting Area — a Spell's attack lock does not apply.
                    if (TextEffects.MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(this, attacker, st)) continue;
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null) continue;
                    foreach (var c in prog.ClausesFor(TextEffects.EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null ||
                            c.Action != TextEffects.EffectActionKind.ContinuousCannotAttack)
                            continue;
                        if (st.EquippedTo != null)
                            continue;
                        if (c.Side == TextEffects.EffectSide.Opponent && side == attackerController)
                            continue;
                        if (c.Side == TextEffects.EffectSide.Controller && side != attackerController)
                            continue;
                        // Vengeful Bog Spirit: only monsters Summoned (incl. Flip) this turn.
                        if (c.RequiresSummonedOrFlippedThisTurn)
                        {
                            if (!attacker.SummonedThisTurn) continue;
                            // Array of Revealing Light (BattleRules batch): only monsters of the Type it declared.
                            if (c.RaceFilterIsDeclared)
                            {
                                var declared = TextEffects.TypeDeclarationRuntime.DeclaredTypeOf(st);
                                if (string.IsNullOrEmpty(declared) ||
                                    !TextEffects.ClassicEraTemplates.RaceMatches(attacker.Race, declared))
                                    continue;
                            }

                            return true;
                        }
                        // The Regulation of Tribe (TypeDeclaration batch, v142): the Type this card declared.
                        var raceFilter = c.RaceFilterIsDeclared
                            ? TextEffects.TypeDeclarationRuntime.DeclaredTypeOf(st)
                            : c.RaceFilter;
                        if (c.RaceFilterIsDeclared && string.IsNullOrEmpty(raceFilter))
                            continue;
                        if (!string.IsNullOrEmpty(raceFilter) &&
                            !TextEffects.ClassicEraTemplates.RaceMatches(attacker.Race, raceFilter))
                            continue;
                        if (c.AmountIsLevel)
                        {
                            if (attacker.Level < c.Amount) continue;
                            return true;
                        }

                        if (c.Amount > 0 && attacker.CurrentAtk < c.Amount) continue;
                        if (c.Amount > 0 || !string.IsNullOrEmpty(raceFilter))
                            return true;
                    }

                    if (st.EquippedTo != null &&
                        (st.EquippedTo == attacker ||
                         (attacker != null && st.EquippedTo.InstanceId == attacker.InstanceId)))
                    {
                        foreach (var c in prog.ClausesFor(TextEffects.EffectTiming.ContinuousWhileFaceUp))
                        {
                            if (c == null ||
                                c.Action != TextEffects.EffectActionKind.ContinuousCannotAttack)
                                continue;
                            if (!string.IsNullOrEmpty(c.ExcludeRaceFilter) &&
                                attacker.Race != null &&
                                attacker.Race.IndexOf(c.ExcludeRaceFilter,
                                    System.StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;
                            return true;
                        }
                    }
                }

                // Big-Tusked Mammoth: a face-up monster carrying the summoned-this-turn lock.
                // Only that shape is monster-borne here; other monster attack locks keep their paths.
                foreach (var mon in side.MonstersOnField())
                {
                    if (mon == null || !mon.FaceUp || mon.Def == null) continue;
                    var mprog = TextEffects.CompiledEffectCache.GetOrCompile(mon.Def);
                    if (mprog == null || !mprog.FullyCompiled) continue;
                    foreach (var c in mprog.ClausesFor(TextEffects.EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null ||
                            c.Action != TextEffects.EffectActionKind.ContinuousCannotAttack ||
                            !c.RequiresSummonedOrFlippedThisTurn)
                            continue;
                        if (c.Side == TextEffects.EffectSide.Opponent && side == attackerController)
                            continue;
                        if (c.Side == TextEffects.EffectSide.Controller && side != attackerController)
                            continue;
                        if (attacker.SummonedThisTurn) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Rulebook 1 attack, plus continuous extra attacks (Mermaid Knight while Umi, …).</summary>
        public int MaxAttacksThisTurn(CardInstance attacker)
        {
            var max = 1 + ExtraAttacksAllowed(attacker);
            // Asura Priest (SpiritMonsters batch): one more attack while an opponent's monster was not attacked yet.
            max = TextEffects.SpiritMonstersRuntime.AdjustMaxAttacks(this, attacker, max);
            // ChaosBanishSummons batch: Black Luster Soldier - Envoy of the Beginning's second attack in a row.
            max = TextEffects.ChaosBanishSummonsRuntime.AdjustMaxAttacks(this, attacker, max);
            // Soft residual: The Dark Door ContinuousWhileFaceUp BP attack-count gate
            // (Mask-style Soft surgical). Only 1 monster may attack per Battle Phase —
            // if another of the controller's monsters already declared, Soft-clamp this
            // monster's MaxAttacks to 0. Same-monster ExtraAttacks remain OK.
            if (attacker != null &&
                TextEffects.ContinuousProtections.OnlyOneMonsterMayAttackPerBattlePhase(this))
            {
                var ctrl = ControllerOf(attacker);
                if (ctrl != null &&
                    TextEffects.ContinuousProtections.AnotherMonsterAlreadyAttackedThisBattlePhase(
                        ctrl, attacker))
                    return 0;
            }
            return max;
        }

        /// <summary>YGOPro EFFECT_EXTRA_ATTACK value (1 = attack twice). Condition: named card / Umi.</summary>
        public int ExtraAttacksAllowed(CardInstance attacker)
        {
            if (attacker?.Def == null || !attacker.FaceUp) return 0;
            // BattleBoosts batch (v161): Gray Wing's "attack twice this turn" (EFFECT_FLAG_CANNOT_DISABLE: it stays if this
            // monster is negated afterwards).
            var turnExtra = TextEffects.BattleBoostsRuntime.TurnExtraAttacks(this, attacker);
            // DcrEquipSpells batch: Twin Swords of Flashing Light - Tryce's second attack is the Equip Card's effect
            // (EFFECT_TYPE_EQUIP + EFFECT_EXTRA_ATTACK): it stays while the monster is negated.
            turnExtra = Math.Max(turnExtra, TextEffects.DcrEquipSpellsRuntime.ExtraAttacks(this, attacker));
            if (attacker.IsNegated) return turnExtra;
            var extra = turnExtra;
            if (attacker.CardId == MonsterEffects.MermaidKnight &&
                FieldSpellEffects.UmiIsOnField(this))
                extra = Math.Max(extra, 1);

            extra = Math.Max(extra, YgoProContinuousCatalog.ExtraAttacks(this, attacker));

            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog != null)
            {
                foreach (var clause in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (clause == null || clause.Action != EffectActionKind.ExtraAttacks)
                        continue;
                    if (!string.IsNullOrEmpty(clause.RequiresFaceUpName) &&
                        !FieldSpellEffects.NamedCardIsFaceUpOnField(this, clause.RequiresFaceUpName))
                        continue;
                    // NegationMonsters batch (v161): Tyrant Dragon — only while the opponent controls a monster.
                    if (!TextEffects.NegationMonstersRuntime.ExtraAttackConditionMet(this, attacker, clause))
                        continue;
                    extra = Math.Max(extra, Math.Max(0, clause.Amount));
                }
            }

            return extra;
        }

        /// <summary>
        /// Rulebook: attack directly if the opponent controls no monsters.
        /// Exception: registered continuous "can attack directly" (MK-3 while Umi, etc.).
        /// </summary>
        public bool CanAttackDirectly(DuelistState who, CardInstance attacker)
        {
            if (Overlay != null && Overlay.ForbidDirectAttacks) return false;
            if (!CanAttack(who, attacker)) return false;
            var opp = OpponentOf(who);
            if (opp == null) return false;
            // Toon rule (Toons batch): no direct attack while the opponent controls a face-up Toon monster.
            if (TextEffects.ToonsRuntime.ForbidsDirectAttack(this, attacker)) return false;
            if (TextEffects.SelfStatMonstersRuntime.ForbidsDirectAttack(this, attacker)) return false;
            // Asura Priest (SpiritMonsters batch): its attacks past the ordinary ones are on monsters only.
            if (TextEffects.SpiritMonstersRuntime.ForbidsDirectAttack(this, attacker)) return false;
            if (!HasMonsters(opp)) return true;
            if (GrantsDirectAttack(attacker)) return true;
            return ContinuousProtections.AllOpponentMonstersAllowDirect(this, opp);
        }

        /// <summary>
        /// Continuous grant only (no phase check). YGOPro EFFECT_DIRECT_ATTACK.
        /// MK-3 hardcoded + compiled <see cref="EffectActionKind.CanAttackDirectly"/>.
        /// </summary>
        public bool GrantsDirectAttack(CardInstance attacker)
        {
            // PgdSpellTrapBasics batch (v162): Secret Pass to the Treasures' grant comes from the Spell — the monster's own
            // negation does not remove it.
            if (TextEffects.PgdSpellTrapBasicsRuntime.HasSpellDirectAttackGrant(this, attacker)) return true;
            // DcrEquipSpells batch: Shooting Star Bow - Ceal's grant is the Equip Card's effect (EFFECT_TYPE_EQUIP) — the
            // monster's own negation does not remove it.
            if (TextEffects.DcrEquipSpellsRuntime.GrantsDirectAttack(this, attacker)) return true;
            // SimpleTriggersAndSwarm batch: Dark Scorpion Combination's field grant (its flag on the monster, not the
            // monster's own effect — the monster's negation does not remove it).
            if (TextEffects.SimpleTriggersAndSwarmRuntime.GrantsDirectAttack(this, attacker)) return true;
            if (attacker?.Def == null || !attacker.FaceUp || attacker.IsNegated) return false;
            if (attacker.DirectAttackThisTurn) return true;
            var ctrl = ControllerOf(attacker);
            if (ctrl != null && ctrl.MustAttackDirectlyThisTurn)
                return true;

            if (attacker.CardId == MonsterEffects.AmphibiousBugrothMk3 &&
                FieldSpellEffects.UmiIsOnField(this))
                return true;
            if (YgoProContinuousCatalog.GrantsDirectAttack(this, attacker))
                return true;
            // EndOfDamageStepRiders batch: Drillago — while the opponent controls only face-up monsters with N or more ATK.
            if (TextEffects.EndOfDamageStepRidersRuntime.GrantsDirectAttack(this, attacker))
                return true;
            // Toon rule (Toons batch): direct attacks while the opponent controls no face-up Toon monster.
            if (TextEffects.ToonsRuntime.GrantsDirectAttack(this, attacker))
                return true;

            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null) return false;
            foreach (var clause in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (clause == null || clause.Action != EffectActionKind.CanAttackDirectly)
                    continue;
                // Shadowslayer / Black Tyranno / Rocket Jumper: all opp monsters Defense.
                // Fail-closed: do NOT treat as unconditional when this flag is set.
                if (clause.RequiresAllOpponentMonstersDefense)
                {
                    if (TextEffectRuntime.AllOpponentMonstersAreDefensePosition(this, attacker))
                        return true;
                    continue;
                }
                if (string.IsNullOrEmpty(clause.RequiresFaceUpName))
                    return true;
                if (FieldSpellEffects.NamedCardIsFaceUpOnField(this, clause.RequiresFaceUpName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True if the opponent of <paramref name="attacker"/> controls face-up Swords of Revealing Light.
        /// Official: "your opponent's monsters cannot declare an attack."
        /// </summary>
        public bool OpponentHasSwordsOfRevealingLight(DuelistState attacker)
        {
            var opp = OpponentOf(attacker);
            if (opp == null) return false;
            foreach (var st in opp.SpellTrapsOnField())
            {
                if (st != null && st.FaceUp && !st.IsNegated &&
                    st.CardId == SpellTrapEffects.SwordsOfRevealingLight)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Declare an attack and start the attack animation. The defender's reaction window
        /// lasts until IMPACT (profile from attacker card) — combat does not pause for a menu.
        /// Activate a trap mid-charge or miss the hit.
        /// </summary>
        public bool TryAttack(DuelistState who, CardInstance attacker, CardInstance targetOrNull)
        {
            if (!CanAttack(who, attacker)) return false;
            var magnetHosts = MagnetismHosts(who);
            if (magnetHosts.Count == 1 && targetOrNull != magnetHosts[0])
            {
                Log($"{attacker.Name} can only attack {magnetHosts[0].Name}.");
                return false;
            }

            var opp = OpponentOf(who);

            if (targetOrNull == null)
            {
                if (Overlay != null && Overlay.ForbidDirectAttacks)
                {
                    Log("Duelist Kingdom — no direct attacks.");
                    return false;
                }

                if (TextEffects.ToonsRuntime.ForbidsDirectAttack(this, attacker))
                {
                    Log($"{attacker.Name} cannot attack directly — the opponent controls a Toon monster.");
                    return false;
                }

                if (TextEffects.SelfStatMonstersRuntime.ForbidsDirectAttack(this, attacker))
                {
                    Log($"{attacker.Name} cannot attack your opponent directly.");
                    return false;
                }

                if (TextEffects.SpiritMonstersRuntime.ForbidsDirectAttack(this, attacker))
                {
                    Log($"{attacker.Name} can only attack your opponent's monsters it has not attacked this Battle Phase.");
                    return false;
                }

                if (HasMonsters(opp) && !GrantsDirectAttack(attacker) &&
                    !ContinuousProtections.AllOpponentMonstersAllowDirect(this, opp))
                {
                    Log("Cannot attack directly — opponent controls a monster.");
                    return false;
                }
            }
            else if (!opp.TryFindMonster(targetOrNull, out _))
            {
                Log("Illegal attack target.");
                return false;
            }
            else if (who.MustAttackDirectlyThisTurn)
            {
                Log("Attacks become direct attacks this turn — cannot attack a monster.");
                return false;
            }
            else if (ContinuousProtections.CannotBeAttackTarget(this, targetOrNull))
            {
                Log($"{targetOrNull.Name} cannot be targeted for attacks.");
                return false;
            }
            else if (TextEffects.ToonsRuntime.ForbidsAttackTarget(this, attacker, targetOrNull))
            {
                Log($"{attacker.Name} must target a face-up Toon monster for its attacks.");
                return false;
            }
            else if (TextEffects.SpiritMonstersRuntime.ForbidsAttackTarget(this, attacker, targetOrNull))
            {
                // Inaba White Rabbit attacks directly only; Asura Priest attacks each monster once per Battle Phase.
                Log($"{attacker.Name} cannot attack {(targetOrNull.FaceUp ? targetOrNull.Name : "that monster")}.");
                return false;
            }

            if (who.CannotDeclareAttackThisTurn)
            {
                Log("Cannot declare an attack this turn.");
                return false;
            }

            if (OpponentHasSwordsOfRevealingLight(who) &&
                !TextEffects.MonsterNegationAndControlLocksRuntime.IgnoresSpellAttackLock(this, who, attacker))
            {
                Log("Cannot declare an attack — opponent controls Swords of Revealing Light.");
                return false;
            }

            // Ring of Magnetism (GitHub lineage): only the equipped monster may be attacked.
            var forced = TextEffects.TextEffectRuntime.ForcedAttackTargets(opp);
            if (forced.Count > 0 && (targetOrNull == null || !forced.Contains(targetOrNull)))
            {
                Log($"You can only attack {forced[0].Name} (Ring of Magnetism).");
                return false;
            }

            // BattleRules batch: Patrician of Darkness — the defender chooses the target of an attack on a monster.
            if (TextEffects.BattleRulesRuntime.TryDefenderChoosesTarget(this, who, attacker, targetOrNull,
                    out var defenderPick, out var waitingForDefender))
            {
                if (waitingForDefender)
                {
                    Notify();
                    return true;
                }

                if (!ReferenceEquals(defenderPick, targetOrNull))
                    return TryAttack(who, attacker, defenderPick);
            }

            // Costs and declaration-time coin calls only after every legality check (DR-04):
            // an attack that is refused never costs LP (Dark Elf) or Deck cards (Gravekeeper's Servant).
            if (!TextEffects.TextEffectRuntime.PayAttackDeclarationCost(this, who, attacker))
                return false;
            if (GameOver) return false;
            // GitHub-lineage attack costs (AttackCostLp / AttackCostMillForOpponent). Each lineage's
            // payer reads only its own clause kinds, so a card never pays twice.
            if (!attacker.AttackCostsPaidThisDeclaration)
            {
                TextEffects.TextEffectRuntime.AttackCosts(this, who, attacker, out var attackCost, out var attackMill);
                if (attackCost > 0)
                    PayLifePointCost(who, attackCost, $"{attacker.Name} attack cost");
                if (attackMill > 0)
                    TextEffects.TextEffectRuntime.PayAttackMill(this, who, attackMill);
                attacker.AttackCostsPaidThisDeclaration = attackCost > 0 || attackMill > 0;
            }
            if (GameOver) return false;
            if (TextEffects.TextEffectRuntime.BeginOwnAttackCoin(this, who, attacker, targetOrNull))
                return true;
            return DeclareAttack(who, attacker, targetOrNull, "");
        }

        /// <summary>
        /// The attack is declared (every legality check, cost and declaration coin call is behind it): the declared-attack
        /// state, the presentation, the declaration triggers, then the response window or the Damage Step. Shared by
        /// <see cref="TryAttack"/> and <see cref="BeginForcedAttack"/>.
        /// </summary>
        bool DeclareAttack(DuelistState who, CardInstance attacker, CardInstance targetOrNull, string logSuffix)
        {
            TextEffects.TextEffectRuntime.NoteAttackerSeal(this, attacker, targetOrNull);
            // —— Attack declaration + combat animation starts immediately ——
            DeclaredAttacker = attacker;
            DeclaredAttackTarget = targetOrNull;
            DeclaredAttackingPlayer = who;
            // Asura Priest (SpiritMonsters batch): the monsters it attacked this Battle Phase.
            TextEffects.SpiritMonstersRuntime.NoteAttackDeclared(this, attacker, targetOrNull);
            // ChaosBanishSummons batch: another monster's attack ends a second attack "in a row".
            TextEffects.ChaosBanishSummonsRuntime.NoteAttackDeclared(this, attacker);
            GameEvents.Add(DuelGameEventKind.DeclaredAttack, TurnNumber, who, attacker, targetOrNull);
            var hadMonstersAtDeclaration = HasMonsters(OpponentOf(who));

            var direct = targetOrNull == null;
            var profile = CombatAnimTimings.ForAttack(attacker, direct);
            ActivePresentation = new ActiveCombatPresentation
            {
                Kind = CombatActionKind.Attack,
                SourceCard = attacker,
                TargetCard = targetOrNull,
                Actor = who,
                Profile = profile,
                StartedUnscaledTime = Clock.UnscaledTime,
                Clock = Clock,
                ImpactResolved = false,
                HadMonstersAtAttackDeclaration = hadMonstersAtDeclaration
            };

            var tName = direct
                ? "directly"
                : (targetOrNull.FaceUp ? targetOrNull.Name : "a face-down monster");
            Log($"{attacker.Name} attacks {tName}{logSuffix}.");
            // "When this card declares an attack:" (Jirai Gumo)
            TextEffects.TextEffectRuntime.FireAttackDeclared(this, who, attacker);
            if (GameOver) return true;
            // BattleWindows batch: the defender's face-up Fairy Box / Jam Defender trigger on the declaration. A human's
            // coin call or target pick pauses the attack; the runtime continues it (ContinueDeclaredAttack) once answered.
            if (TextEffects.BattleWindowsRuntime.FireOpponentAttackDeclaredTriggers(this, who, attacker))
            {
                Notify();
                return true;
            }

            return ContinueDeclaredAttack();
        }

        /// <summary>
        /// The declared attack goes on to its response window (the defender may fire Fast Effects / Traps while the
        /// attack animation plays), or straight to the Damage Step when nobody can respond. Runs right after the
        /// declaration, or once the declaration triggers that paused it were answered (BattleWindows batch).
        /// </summary>
        public bool ContinueDeclaredAttack()
        {
            if (!HasDeclaredAttack || DeclaredAttackingPlayer == null || DeclaredAttacker == null)
            {
                Notify();
                return false;
            }

            if (GameOver) return true;
            var who = DeclaredAttackingPlayer;
            var attacker = DeclaredAttacker;
            var targetOrNull = DeclaredAttackTarget;
            var opp = OpponentOf(who);
            var profile = ActivePresentation?.Profile ?? CombatAnimTimings.ForAttack(attacker, targetOrNull == null);
            var hadMonstersAtDeclaration = ActivePresentation?.HadMonstersAtAttackDeclaration ?? true;

            // Defender may fire Fast Effects / traps while the attack anim plays
            if (OpenResponseWindow(opp, ResponseTiming.AttackDeclared, attacker, targetOrNull, who, null, null,
                    profile))
            {
                // Presentation only: keep the attacker charged while an inline response's result waits.
                Presentation.ArInteraction.ActivationBeat.NoteAttackTrigger(attacker);
                if (!IsHumanControlled(opp))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return true;
            }

            // No legal responses — resolve damage immediately (official: empty chain)
            if (ActivePresentation != null)
                ActivePresentation.ImpactResolved = true;
            return ResolveDeclaredAttack(hadMonstersAtDeclaration);
        }

        // chip3 BeginDeferredChainActivation seats the activation card before pass/pass
        // resolves the chain; this fallback is the last zone-state repair, not effect logic.

        static bool IsOneShotChainSpellTrap(CardInstance card)
        {
            var def = card?.Def;
            if (def == null || def.IsFieldSpell || def.IsContinuousSpellOrTrap) return false;
            // A Normal Spell/Trap whose own text keeps it on the field after it resolves is not a
            // one-shot, even though its race says "Normal". Swords of Revealing Light is the
            // canonical case: sweeping it to the GY here silently cancels the whole card, because
            // both the attack lock and the End Phase turn counter look for it face-up on the field.
            if (SpellTrapEffects.StaysOnFieldAfterActivate(
                    card, TextEffects.CompiledEffectCache.GetOrCompile(def)))
                return false;
            return SpellTrapEffects.IsNormalSpell(def) ||
                   SpellTrapEffects.IsQuickPlay(def) ||
                   SpellTrapEffects.IsTrap(def);
        }

        void EnsureDeferredSpellTrapGrave(ChainLink link)
        {
            var card = link?.Card;
            if (!IsOneShotChainSpellTrap(card)) return;
            var owner = link.Controller ?? ControllerOf(card);
            if (owner == null) return;
            var stillActive = owner.TryFindSpellTrap(card, out _) || owner.Hand.Contains(card);
            if (!stillActive) return;
            Log($"[RULE] Chain resolve left {card.Name} active; sending one-shot Spell/Trap to the GY.");
            Presentation.ArInteraction.SpellActivationPresentation.QueueFadeToGy(card.InstanceId);
            Presentation.ArInteraction.SpellActivationPresentation.EnqueueGhostIfNeeded(card.InstanceId);
            SendCardToGrave(owner, card);
        }


        /// <summary>A resolving negation link (Magic Jammer, Solemn Judgment …) negates the link it answers.</summary>
        void ApplyActivationNegation(ChainLink link)
        {
            if (link.FlipSelfFaceUpDefense && link.Card != null)
            {
                link.Card.FaceUp = true;
                link.Card.Position = BattlePosition.Defense;
            }

            if (link.TargetLink != null)
            {
                link.TargetLink.Negated = true;
                // Presentation only: the negated link's banner / shatter follow this link's result.
                if (link.TargetLink.Card != null)
                    Presentation.ArInteraction.ActivationBeat.NoteNegated(link.TargetLink.Card.InstanceId);
                if (link.DestroyNegatedCard && link.TargetLink.Card != null)
                {
                    // "Negate the activation, and if you do, destroy it" (Magic Jammer, Seven Tools, Solemn
                    // Judgment): a Spell/Trap on the field destroyed by that effect may be answered by Gust /
                    // Driving Snow (SpellTrapDestroyedResponse batch).
                    var negatedCard = link.TargetLink.Card;
                    var negatedController = ControllerOf(negatedCard);
                    var wasFaceUpOnField = TextEffects.ToonsRuntime.IsFaceUpOnField(this, negatedCard);
                    SendCardToGrave(link.TargetLink.Controller, negatedCard);
                    // "If "X" on the field is destroyed, destroy this card." (Toons batch).
                    TextEffects.ToonsRuntime.NotifyFaceUpCardDestroyed(this, negatedCard, wasFaceUpOnField);
                    if (negatedController != null)
                        TextEffects.SpellTrapDestroyedResponseRuntime.NoteDestroyedByEffect(
                            this, negatedController, negatedCard, link.Controller);
                }
                EnsureDeferredSpellTrapGrave(link.TargetLink);
            }

            EnsureDeferredSpellTrapGrave(link);
        }

        /// <summary>
        /// SummonNegation batch (v141, Magic Drain): the answered player made their choice while the chain was
        /// paused on Magic Drain's link. <paramref name="waived"/> (they discarded a Spell): Magic Drain's effect
        /// is negated. Otherwise its negation applies. Then the rest of the chain resolves.
        /// </summary>
        public void ResumeChainAfterSpellDiscardWaiver(bool waived)
        {
            var link = _chainPausedForWaiver;
            _chainPausedForWaiver = null;
            if (link == null) return;
            if (waived)
                EnsureDeferredSpellTrapGrave(link);
            else
                ApplyActivationNegation(link);
            ResolveChainStack();
        }

        /// <summary>
        /// TypeDeclaration batch (v142): the choice a resolving link was waiting on is made
        /// (<see cref="PendingActivation.HoldsChainResolution"/>); resolve the rest of the chain. No-op unless the
        /// chain is held, or while another held choice is still open. A pick opened by a trigger of that effect
        /// (Sangan) stays open while the chain goes on, as for any destruction inside a resolving chain.
        /// </summary>
        public void ResumeChainAfterChoice()
        {
            if (!_chainHeldForChoice) return;
            if (PendingActivation != null && PendingActivation.HoldsChainResolution) return;
            _chainHeldForChoice = false;
            ResolveChainStack();
        }

        /// <summary>
        /// Chain Link 1 of a Field Spell activated from the hand (<see cref="TryActivateSpellTrap"/>
        /// keeps it on the chain only when the opponent could respond to it).
        /// </summary>
        static bool IsFieldSpellActivationLink(ChainLink link) =>
            link?.Card?.Def != null && link.Card.Def.IsFieldSpell && link.FromHand && link.EffectKey == "manual";

        public void ResolveChainStackForActivation() => ResolveChainStack();
        void ResolveChainStack()
        {
            IsResolvingChain = true;
            while (Chain.Count > 0)
            {
                // SummonPromptsAndCosts batch (v163): the link Special Summons a card whose summon cost needs a human's
                // Tribute pick (Tyrant Dragon) — it is picked as the link starts to resolve; the Chain waits for it.
                if (TextEffects.SummonPromptsAndCostsRuntime.HoldBeforeLinkResolves(this, Chain.Last))
                {
                    _chainHeldForChoice = true;
                    Notify();
                    return;
                }

                var link = Chain.PopNextToResolve();
                if (link == null) break;
                // SpellEventsAndHandSendTriggers batch: a Continuous / Field card whose own activation this is applies only
                // from now on — the Spells of this Chain were activated before it applied (aux.chainreg).
                TextEffects.SpellEventsAndHandSendTriggersRuntime.NoteLinkResolving(this, link);
                // EffectDamageEvents batch (v162): damage modifiers keyed to a Chain Link apply while it resolves.
                TextEffects.EffectDamageEventsRuntime.NoteResolving(this, link);
                // Skull Invitation: settle the cards the previous links sent to the GY before this one resolves; this
                // link's own one-shot card counts once the chain is over (StandbyGrowthAndMaintenance batch, v142).
                TextEffects.StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint(this, link.Card);
                // Presentation only: this link's results get their own beat, after the previous link's.
                if (link.Card != null)
                    Presentation.ArInteraction.ActivationBeat.BeginResolve(link.Card.InstanceId);
                // Its activation resolves now: a Field / Continuous Spell or Trap placed face-up for it
                // applies its effects from here on (unless negated).
                if (link.Card != null)
                    link.Card.ActivationUnresolvedOnChain = false;
                // ChainLinkAnswers batch: Chaos Command Magician (ygopro EVENT_CHAIN_SOLVING) — a monster effect whose only
                // target is a face-up Chaos Command Magician is negated as it starts to resolve.
                TextEffects.ChainLinkAnswersRuntime.NegateResolvingLinkTargetingOnlyThis(this, link);

                // TriggerChainResponses batch (v163): a trigger effect's link (FLIP, "sent to the GY", a phase trigger)
                // applies the resolution recorded when it was seated (negated: nothing; its card is not sent to the GY).
                // A pick its resolution opens holds the Chain; TriggerChainResponsesRuntime.Flush resumes it.
                if (TextEffects.TriggerChainResponsesRuntime.TryResolveLink(this, link))
                {
                    if (PendingActivation != null)
                    {
                        _chainHeldForChoice = true;
                        TextEffects.TriggerChainResponsesRuntime.NoteHeldForChoice(this);
                        Notify();
                        return;
                    }

                    continue;
                }

                // FaceUpQuickEffects batch (v160): a face-up card's Quick Effect link applies what it recorded at
                // activation (negated: nothing; its card is not sent to the GY). Skull Lair's selection may hold it.
                // BattleBoosts batch (v161): a face-up monster's battle Quick Effect link (Injection Fairy Lily, Spirit Ryu).
                if (TextEffects.BattleBoostsRuntime.TryResolveLink(this, link))
                    continue;
                // ChaosBanishSummons batch: Strike Ninja's Quick Effect link (banish itself until the End Phase).
                if (TextEffects.ChaosBanishSummonsRuntime.TryResolveNinjaLink(this, link))
                    continue;
                // TargetNegationsAndHandQuickEffects batch (v163): a hand Quick Effect link (Charm of Shabti); a mandatory
                // negation link that is no longer right after the activation it answers (Great Dezard, Fushioh Richie).
                if (TextEffects.TargetNegationsAndHandQuickEffectsRuntime.TryResolveLink(this, link))
                    continue;
                // ArchfiendRiders batch: Desrook Archfiend's hand Quick Effect link (Special Summon its target); the
                // human's face-up position pick holds the rest of the Chain.
                if (TextEffects.ArchfiendRidersRuntime.TryResolveLink(this, link))
                {
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }
                if (TextEffects.FaceUpQuickEffectsRuntime.TryResolveLink(this, link))
                {
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }

                // A negated link does nothing — checked before anything it would do, so a negated
                // negation (Seven Tools on Magic Jammer) no longer negates its own target (DR-02).
                if (link.Negated)
                {
                    if (link.Card != null)
                        Presentation.ArInteraction.ActivationBeat.NoteNegated(link.Card.InstanceId);
                    // The negator may already have destroyed it: only a card still in play goes to the GY.
                    if (link.Card != null &&
                        (link.Card.Def?.IsSpell == true || link.Card.Def?.IsTrap == true) &&
                        link.Controller != null &&
                        (link.Controller.TryFindSpellTrap(link.Card, out _) ||
                         link.Controller.Hand.Contains(link.Card) ||
                         (link.Controller.FieldSpellZone != null &&
                          link.Controller.FieldSpellZone.Occupant == link.Card)))
                        SendCardToGrave(link.Controller, link.Card);
                    continue;
                }

                // Gust / Driving Snow answer a destruction only when it was the last thing to happen: another link
                // resolving after it (a negated one does not) means that timing is missed (SpellTrapDestroyedResponse).
                TextEffects.SpellTrapDestroyedResponseRuntime.DropPending(this);
                // Michizure (BattleWindows batch): the same "last thing to happen" rule for monsters sent to the GY.
                TextEffects.BattleWindowsRuntime.DropSentRecords(this);

                // NegationMonsters batch (v161): Freed the Matchless General / Frontier Wiseman / Tyrant Dragon (ygopro
                // EVENT_CHAIN_SOLVING) — a Spell/Trap effect that targets a monster they protect is negated as it resolves,
                // and its card is destroyed.
                if (TextEffects.NegationMonstersRuntime.TryNegateTargetingLink(this, link))
                {
                    EnsureDeferredSpellTrapGrave(link);
                    continue;
                }

                // ChainLinkAnswers batch: Jar Robber (negate the answered effect, draw), My Body as a Shield against a
                // monster effect (negate, destroy that monster), Dramatic Rescue (return, then a Special Summon pick that
                // may hold the Chain), and the activation whose effect Jar Robber negated (it resolves with no effect).
                if (TextEffects.ChainLinkAnswersRuntime.TryResolveLink(this, link))
                {
                    EnsureDeferredSpellTrapGrave(link);
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }

                // EffectDamageEvents batch (v162): Barrel Behind the Door / Trap of Board Eraser register the reflect /
                // negate of the effect damage of the link they answered (Board Eraser's discard pick may hold the chain).
                if (TextEffects.EffectDamageEventsRuntime.TryResolveLink(this, link))
                {
                    EnsureDeferredSpellTrapGrave(link);
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }

                // Royal Decree / Imperial Order: this Counter Trap's effect is negated on the field, so
                // the link it answers is not negated (ContinuousNegation batch).
                if (link.NegatesActivation &&
                    TextEffects.ContinuousNegationRuntime.ChainLinkEffectNegated(this, link))
                {
                    EnsureDeferredSpellTrapGrave(link);
                    continue;
                }

                // SpellTrapAnswersAndActivationLocks batch: a negation with a rider (Cursed Seal's name lock, Spell Vanishing's
                // same-name send, Judgment of Anubis's destroy-and-burn, whose pick may hold the Chain).
                if (TextEffects.SpellTrapAnswersAndActivationLocksRuntime.TryResolveLink(this, link, ApplyActivationNegation))
                {
                    EnsureDeferredSpellTrapGrave(link);
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }

                if (link.NegatesActivation)
                {
                    // Magic Drain: the answered player may discard 1 Spell to negate this effect instead. A
                    // human's choice pauses the chain here; ResumeChainAfterSpellDiscardWaiver continues it.
                    if (link.NegationWaivableBySpellDiscard)
                    {
                        var waiver = TextEffects.SummonNegationRuntime.OfferSpellDiscardWaiver(this, link);
                        if (waiver == TextEffects.SummonNegationRuntime.WaiverOutcome.AwaitingChoice)
                        {
                            _chainPausedForWaiver = link;
                            return;
                        }

                        if (waiver == TextEffects.SummonNegationRuntime.WaiverOutcome.Waived)
                        {
                            EnsureDeferredSpellTrapGrave(link);
                            continue;
                        }
                    }

                    ApplyActivationNegation(link);
                    continue;
                }

                // PerCountBurns (v141): a face-up Spell's ignition link (Wave-Motion Cannon) deals the damage it
                // fixed at activation; its card already left the field as the cost.
                if (TextEffects.PerCountBurnsRuntime.TryResolveFaceUpFieldIgnitionLink(this, link))
                {
                    // PgdSpellTrapBasics batch (v162): Card Shuffle's link waits on the player's Deck choice; the rest of
                    // the chain resolves once it is made (ResumeChainAfterChoice).
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }

                // A Field Spell's activation that was left open to a chain response: the card is
                // already face-up in its Field Zone; from now on its effects apply while it stays there,
                // so resolving it only fires the Spell-resolved hooks (as the no-response path does).
                if (IsFieldSpellActivationLink(link))
                {
                    if (link.Controller?.FieldSpellZone != null &&
                        ReferenceEquals(link.Controller.FieldSpellZone.Occupant, link.Card))
                        TextEffects.TextEffectRuntime.NotifySpellResolved(this, link.Controller, link.Card);
                    continue;
                }

                // A Continuous Spell/Trap whose own activation is this link and that left the field before it
                // resolved (an MST chained to it): the activation resolves with no effect, and the card is not
                // put back on the field from the GY (ygopro c:IsRelateToEffect(e); StandbyGrowthAndMaintenance, v142).
                if (TextEffects.StandbyGrowthAndMaintenanceRuntime.ContinuousActivationLeftField(this, link))
                {
                    Log($"{link.Card.Name} is no longer on the field — its activation resolves with no effect.");
                    continue;
                }

                // A compiled activation recorded when it was activated: its costs are paid and its targets chosen,
                // so only its effect applies now (TextEffects.CompiledActivationChainRuntime).
                if (TextEffects.CompiledActivationChainRuntime.TryResolveLink(this, link))
                {
                    EnsureDeferredSpellTrapGrave(link);
                    // CoinDiceInterrupts batch: a coin toss / die roll in its effect waits on a redo choice; the rest
                    // of the chain resolves once it is made (ResumeChainAfterChoice).
                    if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                    {
                        _chainHeldForChoice = true;
                        Notify();
                        return;
                    }

                    continue;
                }

                // Prefer the target chosen during deferred activation (Monster Reborn GY pick).
                var chosen = link.Target;
                if (chosen == null && link.Targets != null && link.Targets.Count > 0)
                    chosen = link.Targets[0];

                var prog = TextEffects.CompiledEffectCache.GetOrCompile(link.Card?.Def);
                if (link.SeatedFrom != ResponseTiming.None && prog != null && prog.FullyCompiled)
                    TextEffects.TextEffectRuntime.TryResolveSeatedResponse(this, link, prog);
                else if (prog != null && prog.FullyCompiled)
                    TextEffects.TextEffectRuntime.TryResolveActivation(
                        this, link.Controller, link.Card, link.FromHand, prog, true, chosen,
                        lpCostPaidOnActivation: link.ActivationLpCostPaid);
                else
                    SpellTrapEffects.BeginOrResolveManual(
                        this, link.Controller, link.Card, false, true);
                EnsureDeferredSpellTrapGrave(link);
                // Burning Land destroyed before its "When this card is activated:" link resolved: the effect applied,
                // and the card stays in the GY (StandbyGrowthAndMaintenance batch, v142).
                TextEffects.StandbyGrowthAndMaintenanceRuntime.SettleOffFieldActivation(this, link);
                // TypeDeclaration batch (v142): this link's effect waits on a player's choice (Earthshaker's
                // Attributes). The rest of the chain resolves once it is made (ResumeChainAfterChoice).
                if (PendingActivation != null && PendingActivation.HoldsChainResolution)
                {
                    _chainHeldForChoice = true;
                    Notify();
                    return;
                }
            }

            IsResolvingChain = false;
            Chain.Clear();
            // EffectDamageEvents batch (v162): per-link damage modifiers end with the Chain (RESET_CHAIN).
            TextEffects.EffectDamageEventsRuntime.OnChainEnded(this);
            // DiscardTriggers batch (v142): discard / Deck-send triggers held while the chain resolved (Electric
            // Snake, Minar, Penguin Knight) activate now, before the game moves on.
            TextEffects.DiscardTriggersRuntime.Flush(this);
            // EngineHygiene batch: Super Rejuvenation resolved during the End Phase draws now.
            TextEffects.EngineHygieneRuntime.AfterChainResolved(this);
            if (ResumeAfterChain != ResponseTiming.None)
            {
                var neg = ChainNegatedAttack;
                var ended = ChainEndedBattle;
                ChainNegatedAttack = false;
                ChainEndedBattle = false;
                ContinueAfterResponseActivation(neg, ended);
            }
            SettlePendingLpLoss();
            // TriggerChainResponses batch (v163): the phase or Summon that seated a trigger Chain goes on (Freed's Draw
            // Phase, a Standby / End Phase, a Flip Summon's queued summon window).
            TextEffects.TriggerChainResponsesRuntime.AfterChainResolved(this);
            Notify();
        }

        /// <summary>
        /// <paramref name="responder"/> passes on the last Chain Link. Priority goes to the other duelist;
        /// two passes in a row resolve the chain. A duelist with nothing legal to chain passes at once.
        /// </summary>
        bool PassChainResponse(DuelistState responder)
        {
            ClearPendingResponse();
            var closed = !FastEffects.IsOpen || FastEffects.Pass(responder);
            // At most one hop: the other duelist either gets a window or passes automatically.
            for (var hop = 0; !closed && hop < 2; hop++)
            {
                var next = FastEffects.PriorityPlayer;
                if (next == null) break;
                // An AI answers inside OpenChainResponseWindow (activate or pass); either way that
                // call already moved the chain on.
                if (OpenChainResponseWindow(next, false)) return true;
                closed = FastEffects.Pass(next);
            }

            if (FastEffects.IsOpen) FastEffects.Close();
            Chain.StartResolution();
            ResolveChainStack();
            return true;
        }
        public bool PassResponse()
        {
            if (PendingResponse == null) return false;
            if (PendingResponse.Timing == ResponseTiming.ChainResponse) return PassChainResponse(PendingResponse.Responder);
            var timing = PendingResponse.Timing;
            var responder = PendingResponse.Responder;
            // Quiet pass when player/AI chooses Pass; only note auto-timeout
            var atImpact = ActivePresentation != null && ActivePresentation.PastImpact;
            if (atImpact)
            {
                var names = PendingResponse.LegalCards != null
                    ? string.Join(", ",
                        PendingResponse.LegalCards.FindAll(c => c != null).ConvertAll(c => c.Name))
                    : "";
                if (timing == ResponseTiming.AttackDeclared)
                    Log(string.IsNullOrEmpty(names)
                        ? "No response — attack resolves."
                        : $"No response — {names} timed out. Attack resolves.");
                else if (timing == ResponseTiming.MonsterSummoned || timing == ResponseTiming.SummonNegation)
                    Log(string.IsNullOrEmpty(names)
                        ? "No response — summon succeeds."
                        : $"No response — {names} timed out. Summon succeeds.");
                else if (timing == ResponseTiming.OpponentOpenState)
                    Log(string.IsNullOrEmpty(names)
                        ? "No response — opponent continues."
                        : $"No response — {names} timed out. Opponent continues.");
                else if (timing == ResponseTiming.YouTakeDamage)
                    Log(string.IsNullOrEmpty(names)
                        ? "No response — damage stands."
                        : $"No response — {names} timed out. Damage stands.");
                else if (timing == ResponseTiming.OpponentDrewOutsideDrawPhase)
                    Log(string.IsNullOrEmpty(names)
                        ? "No response — play continues."
                        : $"No response — {names} timed out. Play continues.");
            }

            ClearPendingResponse();

            if (timing == ResponseTiming.DamageCalculation)
            {
                ContinueDeferredDamageCalculation();
                return true;
            }

            // HandDisruption batch (v142): the "opponent drew outside either Draw Phase" window only offered a
            // card; nothing was waiting on it.
            if (timing == ResponseTiming.OpponentOpenState ||
                timing == ResponseTiming.OpponentDrewOutsideDrawPhase)
            {
                FlushQueuedDamageTakenWindow();
                Notify();
                return true;
            }

            if (timing == ResponseTiming.YouTakeDamage)
            {
                CheckLpWin(Player, Opponent);
                FlushQueuedDamageTakenWindow();
                Notify();
                return true;
            }

            if (timing == ResponseTiming.AttackDeclared)
            {
                var hadMonsters = ActivePresentation?.HadMonstersAtAttackDeclaration ?? true;
                if (ActivePresentation != null)
                    ActivePresentation.ImpactResolved = true;
                ResolveDeclaredAttack(hadMonsters);
                return true;
            }

            if (timing == ResponseTiming.MonsterSummoned)
            {
                FinishSummonResponseWindow();
                return true;
            }

            if (timing == ResponseTiming.SummonNegation)
            {
                FinishSummonNegationWindow();
                return true;
            }

            if (timing == ResponseTiming.SpellTrapDestroyedByOpponent)
            {
                FinishSpellTrapDestroyedWindow();
                return true;
            }

            // BattleWindows batch: the end-of-Damage-Step window passes to the other duelist, or the Damage Step ends.
            if (timing == ResponseTiming.EndOfDamageStep)
            {
                TextEffects.BattleWindowsRuntime.NoteEndOfDamageStepPass(this, responder);
                ContinueEndOfDamageStep();
                return true;
            }

            // BattleWindows batch: Michizure's window closed — the other duelist's window (if any) opens on Notify.
            if (timing == ResponseTiming.MonsterSentFromFieldToYourGy)
            {
                FinishMonsterSentWindow();
                return true;
            }

            // EquipModel batch: Armored Glass's window closed — the other duelist's window (if any) opens on Notify.
            if (timing == ResponseTiming.MonsterEquipped)
            {
                FinishMonsterEquippedWindow();
                return true;
            }

            // EngineHygiene batch: the Forced Requisition window closed.
            if (timing == ResponseTiming.YouDiscardedFromHand)
            {
                FinishDiscardedWindow();
                return true;
            }

            // ForcedActivationAndSubstitution batch (v163): the Major Riot window closed.
            if (timing == ResponseTiming.MonstersReturnedToHandByOpponent)
            {
                FinishMonstersReturnedWindow();
                return true;
            }

            // Summon appear finished
            ClearPresentation();
            Notify();
            return true;
        }

        /// <summary>Trap / hand QE activated mid-window — cancel or modify, then continue.</summary>
        public void ContinueAfterResponseActivation(bool attackNegated, bool battlePhaseEnded)
        {
            var timing = PendingResponse?.Timing ?? ResumeAfterChain;
            ResumeAfterChain = ResponseTiming.None;
            ClearPendingResponse();

            if (timing == ResponseTiming.MonsterSummoned)
            {
                FinishSummonResponseWindow();
                return;
            }

            if (timing == ResponseTiming.SummonNegation)
            {
                FinishSummonNegationWindow();
                return;
            }

            if (timing == ResponseTiming.YouTakeDamage)
            {
                CheckLpWin(Player, Opponent);
                FlushQueuedDamageTakenWindow();
                Notify();
                return;
            }

            // Gust / Driving Snow resolved (SpellTrapDestroyedResponse batch): play continues.
            if (timing == ResponseTiming.SpellTrapDestroyedByOpponent)
            {
                ClearIdleChain();
                FinishSpellTrapDestroyedWindow();
                return;
            }

            // BattleWindows batch: an answer at the end of the Damage Step resolved (Destruction Punch / Michizure).
            // Something new happened, so both duelists may answer again (only what that chain sent to the GY).
            if (timing == ResponseTiming.EndOfDamageStep)
            {
                ClearIdleChain();
                TextEffects.BattleWindowsRuntime.NoteEndOfDamageStepChainResolved(this);
                ContinueEndOfDamageStep();
                return;
            }

            // BattleWindows batch: Michizure resolved in its window; a monster it destroyed opens a new one.
            if (timing == ResponseTiming.MonsterSentFromFieldToYourGy)
            {
                ClearIdleChain();
                FinishMonsterSentWindow();
                return;
            }

            // EquipModel batch: Armored Glass resolved in its window; play continues.
            if (timing == ResponseTiming.MonsterEquipped)
            {
                ClearIdleChain();
                FinishMonsterEquippedWindow();
                return;
            }

            // EngineHygiene batch: Forced Requisition resolved in its window.
            if (timing == ResponseTiming.YouDiscardedFromHand)
            {
                ClearIdleChain();
                FinishDiscardedWindow();
                return;
            }

            // ForcedActivationAndSubstitution batch (v163): Major Riot resolved in its window.
            if (timing == ResponseTiming.MonstersReturnedToHandByOpponent)
            {
                ClearIdleChain();
                FinishMonstersReturnedWindow();
                return;
            }

            // HandDisruption batch (v142): Appropriate answered the "opponent drew" window — play just continues.
            if (timing == ResponseTiming.OpponentDrewOutsideDrawPhase)
            {
                FlushQueuedDamageTakenWindow();
                Notify();
                return;
            }

            // Damage Calculation: Kuriboh applied PreventBattleDamageThisBattle — finish calc
            if (timing == ResponseTiming.DamageCalculation || _deferredDamage != null)
            {
                // The inline answer has resolved: drop its idle link before damage can open another window
                // (a "you take damage" window would otherwise keep it as a stale Chain Link; v141 audit).
                ClearIdleChain();
                ContinueDeferredDamageCalculation(afterActivation: true);
                return;
            }

            if (attackNegated || battlePhaseEnded || !HasDeclaredAttack)
            {
                if (attackNegated)
                {
                    Log("🎬 Attack animation interrupted — negated!");
                    // Official: a negated attack still counts as declared. The monster cannot
                    // attack again this Battle Phase unless it has extra attacks remaining.
                    var atk = DeclaredAttacker;
                    var ctrl = DeclaredAttackingPlayer;
                    if (atk != null && ctrl != null && ctrl.TryFindMonster(atk, out _))
                        atk.NoteAttackResolved();
                }
                if (attackNegated || battlePhaseEnded)
                    ClearDeclaredAttack();
                ClearPresentation();
                // Dark Spirit of the Silent (BattleWindows batch): another monster battles the original attack target.
                if (attackNegated && !battlePhaseEnded &&
                    TextEffects.BattleWindowsRuntime.TryStartReplacementBattle(this))
                    return;
                TextEffects.BattleWindowsRuntime.ForgetReplacementBattle(this);
                Notify();
                return;
            }

            // Attacker destroyed (e.g. Mirror Force) → attack fizzles mid-charge
            if (DeclaredAttacker != null && DeclaredAttackingPlayer != null &&
                !DeclaredAttackingPlayer.TryFindMonster(DeclaredAttacker, out _))
            {
                Log("🎬 Attack shattered mid-animation.");
                ClearDeclaredAttack();
                ClearPresentation();
                Notify();
                return;
            }

            // Non-negating reaction (Waboku), or an attack-window Trap whose effect was negated (Mirror Force
            // under Royal Decree) — impact still comes, with protection. The inline answer has resolved: drop
            // its idle link first, or a Damage Calculation / "you take damage" window opened by the battle keeps
            // it as a stale Chain Link that blocks later attacks and swallows the next activation (v141 audit).
            ClearIdleChain();
            var hadMonsters = ActivePresentation?.HadMonstersAtAttackDeclaration ?? true;
            if (ActivePresentation != null)
                ActivePresentation.ImpactResolved = true;
            ResolveDeclaredAttack(hadMonsters);
        }

        public bool ResolveDeclaredAttack() =>
            ResolveDeclaredAttack(ActivePresentation?.HadMonstersAtAttackDeclaration ?? true);

        public bool ResolveDeclaredAttack(bool hadMonstersAtDeclaration)
        {
            if (!HasDeclaredAttack || DeclaredAttackingPlayer == null || DeclaredAttacker == null)
            {
                ClearDeclaredAttack();
                ClearPresentation();
                return false;
            }

            // BattleBoosts batch (v161): the attacking player's Battle Step window, only for a monster Quick Effect of the
            // attacker (Spirit Ryu); the attack goes on to the Damage Step once they pass.
            if (TextEffects.BattleBoostsRuntime.OfferBattleStepWindow(this))
            {
                Notify();
                return true;
            }

            var who = DeclaredAttackingPlayer;
            var attacker = DeclaredAttacker;
            var targetOrNull = DeclaredAttackTarget;
            var opp = OpponentOf(who);
            ClearDeclaredAttack();
            ClearPendingResponse();
            ClearPresentation();

            // Re-validate after responses
            if (Phase != DuelPhase.Battle || GameOver)
            {
                Notify();
                return true;
            }

            // Swords may have resolved mid-window — cannot complete declaration illegally
            if (OpponentHasSwordsOfRevealingLight(who) &&
                !TextEffects.MonsterNegationAndControlLocksRuntime.IgnoresSpellAttackLock(this, who, attacker))
            {
                Log("Attack cancelled — Swords of Revealing Light prevents attacks.");
                Notify();
                return true;
            }

            if (!who.TryFindMonster(attacker, out _) ||
                !attacker.FaceUp || attacker.Position != BattlePosition.Attack)
            {
                Log("Attack fizzled (attacker no longer legal).");
                Notify();
                return true;
            }

            if (targetOrNull != null && targetOrNull.MagicalHatDummy &&
                opp.TryFindMonster(targetOrNull, out _))
            {
                Log($"{attacker.Name} attacks a Magical Hat — the hat is destroyed.");
                SendCardToGrave(opp, targetOrNull);
                attacker.NoteAttackResolved();
                Notify();
                return true;
            }

            // —— Damage Step (official sub-steps) ——
            BattleStep = BattleStep.DamageStep;
            // A Spell/Trap destroyed during this Battle Step whose Gust / Driving Snow window could not open while
            // the attack was pending: the attack moved on, so that chance has passed (SpellTrapDestroyedResponse).
            TextEffects.SpellTrapDestroyedResponseRuntime.DropPending(this);
            // Michizure (BattleWindows batch): a monster sent to the GY during the Battle Step can no longer be answered.
            TextEffects.BattleWindowsRuntime.DropSentRecords(this);

            // Replay: target left, a monster appeared after an empty-field direct, or
            // the direct-attack grant (e.g. Umi for MK-3) was lost during the Battle Step.
            var lostDirectGrant = targetOrNull == null && HasMonsters(opp) &&
                                  !GrantsDirectAttack(attacker) &&
                                  !ContinuousProtections.AllOpponentMonstersAllowDirect(this, opp);
            if (BattleMechanics.NeedsReplay(attacker, targetOrNull, opp, hadMonstersAtDeclaration) ||
                lostDirectGrant)
            {
                if (targetOrNull != null && !opp.TryFindMonster(targetOrNull, out _))
                {
                    Log("Attack target left the field — REPLAY: choose a new attack target or cancel.");
                    BattleStep = BattleStep.BattleStep;
                    DamageSubStep = DamageSubStep.None;
                    Notify();
                    return true;
                }

                if (lostDirectGrant)
                {
                    Log(hadMonstersAtDeclaration
                        ? "Direct-attack condition ended (e.g. Umi left) — REPLAY: choose a new attack target or cancel."
                        : "A monster is now on the opponent's field — REPLAY: attack is no longer direct.");
                    BattleStep = BattleStep.BattleStep;
                    DamageSubStep = DamageSubStep.None;
                    Notify();
                    return true;
                }
            }

            attacker.NoteAttackResolved();

            DamageSubStep = DamageSubStep.Start;

            // Sasuke / Mystic Swordsman / Paladin destroy the battled Defense Position
            // monster here — before the flip below, so a face-down target never turns
            // face-up and its Flip effect never applies — and before damage calculation,
            // so no battle damage is dealt.
            if (targetOrNull != null && opp.TryFindMonster(targetOrNull, out _) &&
                TextEffects.TextEffectRuntime.TryStartOfDamageStepDestroy(
                    this, who, attacker, targetOrNull))
            {
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                Notify();
                return true;
            }

            // Start of Damage Step: flip face-down targets face-up Defense (Rulebook).
            // Flip effects activate after damage calculation (not here) — track who was flipped.
            CardInstance flippedByBattle = null;
            if (targetOrNull != null && !targetOrNull.FaceUp && opp.TryFindMonster(targetOrNull, out _))
            {
                targetOrNull.FaceUp = true;
                // Face-down monsters are ALWAYS Defense Position — never resolve as ATK
                targetOrNull.Position = BattlePosition.Defense;
                flippedByBattle = targetOrNull;
                // DerivedAtkMonsters batch (v176): its continuous effects apply from now on, this damage calculation
                // included (Gren Maju Da Eiza's "?" DEF; any self-stat monster flipped by the attack).
                if (!Ocg.OcgLabDuelHost.IsActive) FieldSpellEffects.RefreshBoard(this);
                Log(
                    $"[Damage Step · Start] Flipped {targetOrNull.Name} face-up in Defense Position " +
                    $"(DEF {BattleMechanics.DefenseValue(targetOrNull)}).");
            }

            DamageSubStep = DamageSubStep.BeforeDamageCalculation;
            // Dreamsprite (BattleWindows batch): attacked by an opponent's monster, it moves the battle to another monster
            // its controller controls. The runtime continues the Damage Step itself (ContinueBeforeDamageCalculation),
            // now or once the player picked the new monster.
            if (targetOrNull != null &&
                TextEffects.BattleWindowsRuntime.BeginBattleConfirmSwitch(this, who, opp, attacker, targetOrNull,
                    flippedByBattle))
                return true;
            return ContinueBeforeDamageCalculation(who, opp, attacker, targetOrNull, flippedByBattle);
        }

        /// <summary>
        /// Rest of the Damage Step from "before damage calculation": Kiseitai, the direct-attack and target checks, the
        /// Damage Calculation window and damage calculation. <paramref name="switchedBattle"/>: a battle started by an
        /// effect (BattleWindows batch — Dreamsprite's new target, Dark Spirit of the Silent's replacement attacker; ygopro
        /// Duel.CalculateDamage) — the new target was not attacked, and a direct battle needs no direct-attack right.
        /// </summary>
        internal bool ContinueBeforeDamageCalculation(DuelistState who, DuelistState opp, CardInstance attacker,
            CardInstance targetOrNull, CardInstance flippedByBattle, bool switchedBattle = false)
        {
            if (GameOver || who == null || opp == null || attacker == null)
            {
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                Notify();
                return true;
            }

            DamageSubStep = DamageSubStep.BeforeDamageCalculation;
            // DamageStepRules batch: the battle of this Damage Step (Cat's Ear Tribe's ATK, Thousand Needles).
            TextEffects.DamageStepRulesRuntime.BeginBattle(this, who, attacker, targetOrNull);
            // Kiseitai (SpiritsAndMisc batch): attacked in face-down Defense Position, it becomes an Equip Spell
            // equipped to the attacker before damage calculation, and no damage calculation is conducted.
            if (!switchedBattle && targetOrNull != null && flippedByBattle == targetOrNull &&
                TextEffects.SpiritsAndMiscRuntime.TryBecomeEquipOnFaceDownAttacker(this, who, attacker, targetOrNull))
            {
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                Notify();
                return true;
            }

            DamageSubStep = DamageSubStep.DamageCalculation;

            if (targetOrNull == null)
            {
                if (!switchedBattle && HasMonsters(opp) && !GrantsDirectAttack(attacker) &&
                    !ContinuousProtections.AllOpponentMonstersAllowDirect(this, opp))
                {
                    Log("Cannot attack directly — opponent controls a monster.");
                    attacker.UndoLastResolvedAttack();
                    BattleStep = BattleStep.BattleStep;
                    DamageSubStep = DamageSubStep.None;
                    Notify();
                    return false;
                }
            }
            else if (!opp.TryFindMonster(targetOrNull, out _))
            {
                Log("Attack target left the field during Damage Step — attack stops.");
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                Notify();
                return true;
            }

            // Final position sanity before calc
            if (targetOrNull != null && !targetOrNull.FaceUp)
                targetOrNull.Position = BattlePosition.Defense;

            // —— Damage Calculation window (official: Kuriboh, Honest, etc.) ——
            // Konami Kuriboh: "During damage calculation, if your opponent's monster attacks…"
            _deferredDamage = new DeferredDamageCalculation
            {
                AttackingPlayer = who,
                DefendingPlayer = opp,
                Attacker = attacker,
                TargetOrNull = targetOrNull,
                FlippedByBattle = flippedByBattle
            };

            // BattleBoosts batch (v161): a new battle's damage calculation (Injection Fairy Lily's once-per-battle mark), and
            // the attacking player's window first when a monster Quick Effect of theirs is legal in it.
            TextEffects.BattleBoostsRuntime.BeginDamageCalculation(this);
            if (TextEffects.BattleBoostsRuntime.OfferAttackerDamageCalculationWindow(this, who, attacker, targetOrNull))
            {
                Notify();
                return true;
            }

            _deferredDamage.DefenderWindowOffered = true;
            // Only defender may activate Kuriboh-style "opponent's monster attacks" effects
            if (OpenResponseWindow(opp, ResponseTiming.DamageCalculation, attacker, targetOrNull, who,
                    null, null, CombatAnimTimings.ForDamageCalculation()))
            {
                if (!IsHumanControlled(opp))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return true;
            }

            // No legal DC responses — resolve immediately
            return FinishDeferredDamageCalculation();
        }

        /// <summary>
        /// BattleBoosts batch (v161): a damage-calculation window closed (passed, or its answer resolved). After the attacking
        /// player's window the defender's opens (when they can respond); then damage calculation is performed. When an
        /// activation made in the defender's window has resolved (<paramref name="afterActivation"/>), the turn player gets
        /// priority again: the attacking player's window re-opens when a monster Quick Effect of theirs is legal in it (their
        /// Injection Fairy Lily answering the defender's), and the defender's follows it again.
        /// </summary>
        bool ContinueDeferredDamageCalculation(bool afterActivation = false)
        {
            var dd = _deferredDamage;
            if (dd != null && afterActivation && dd.DefenderWindowOffered && !GameOver)
            {
                // Cleared first: an AI answering inside the window comes back here (pass → the defender's window).
                dd.DefenderWindowOffered = false;
                if (TextEffects.BattleBoostsRuntime.OfferAttackerDamageCalculationWindow(this, dd.AttackingPlayer,
                        dd.Attacker, dd.TargetOrNull))
                {
                    Notify();
                    return true;
                }

                dd.DefenderWindowOffered = true;
            }

            if (dd != null && !dd.DefenderWindowOffered && !GameOver)
            {
                dd.DefenderWindowOffered = true;
                if (OpenResponseWindow(dd.DefendingPlayer, ResponseTiming.DamageCalculation, dd.Attacker, dd.TargetOrNull,
                        dd.AttackingPlayer, null, null, CombatAnimTimings.ForDamageCalculation()))
                {
                    if (!IsHumanControlled(dd.DefendingPlayer))
                        SpellTrapEffects.AiAutoRespond(this);
                    Notify();
                    return true;
                }
            }

            return FinishDeferredDamageCalculation();
        }

        /// <summary>
        /// Apply damage calculation after Damage Calculation response window (or if empty).
        /// Honors Waboku + <see cref="DuelistState.PreventBattleDamageThisBattle"/> (Kuriboh).
        /// </summary>
        public bool FinishDeferredDamageCalculation()
        {
            if (_deferredDamage == null)
                return false;

            var who = _deferredDamage.AttackingPlayer;
            var opp = _deferredDamage.DefendingPlayer;
            var attacker = _deferredDamage.Attacker;
            var targetOrNull = _deferredDamage.TargetOrNull;
            var flippedByBattle = _deferredDamage.FlippedByBattle;
            _deferredDamage = null;
            ClearPendingResponse();

            void ClearBattleDmgFlags()
            {
                if (who != null) who.PreventBattleDamageThisBattle = false;
                if (opp != null) opp.PreventBattleDamageThisBattle = false;
            }

            if (who == null || attacker == null || opp == null || GameOver)
            {
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                ClearBattleDmgFlags();
                Notify();
                return false;
            }

            // Re-validate attacker / target still present
            if (!who.TryFindMonster(attacker, out _))
            {
                Log("Attacker left the field before damage calculation finished.");
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                ClearBattleDmgFlags();
                Notify();
                return true;
            }

            if (targetOrNull != null && !opp.TryFindMonster(targetOrNull, out _))
            {
                Log("Attack target left the field before damage calculation finished.");
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                ClearBattleDmgFlags();
                Notify();
                return true;
            }

            DamageSubStep = DamageSubStep.DamageCalculation;
            TextEffects.DamageStepRulesRuntime.BeginBattle(this, who, attacker, targetOrNull);

            var piercing = OfficialEffectRegistry.HasPiercing(attacker) ||
                           Continuous.SourceGrantsPiercing(attacker) ||
                           TextEffects.TextEffectRuntime.GrantsPiercing(attacker) ||
                           // BattleBoosts batch (v161): Dragon's Rage grants piercing to its controller's Dragons.
                           TextEffects.BattleBoostsRuntime.FieldGrantsPiercing(this, attacker) ||
                           // DamageStepRules batch: Meteorain — this turn, its controller's monsters.
                           TextEffects.DamageStepRulesRuntime.TurnGrantsPiercing(this, attacker);
            var atkNoDes = OfficialEffectRegistry.CannotBeDestroyedByBattle(attacker) || who.WabokuActive ||
                           // EquipAndSpellProtection batch: Heart of Clear Water.
                           TextEffects.EquipAndSpellProtectionRuntime.EquipPreventsBattleDestruction(this, attacker) ||
                           // TargetNegationsAndHandQuickEffects batch (v163): Charm of Shabti.
                           TextEffects.TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(this, attacker);
            var defNoDes = targetOrNull != null &&
                           (OfficialEffectRegistry.CannotBeDestroyedByBattle(targetOrNull) || opp.WabokuActive ||
                            TextEffects.EquipAndSpellProtectionRuntime.EquipPreventsBattleDestruction(this, targetOrNull) ||
                            TextEffects.TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(this, targetOrNull));
            var noDmgAtk = who.WabokuActive || who.PreventBattleDamageThisBattle ||
                           Continuous.AnyPreventsBattleDamage(who) ||
                           FieldSpellEffects.ControllerAvoidsBattleDamage(this, who, attacker, targetOrNull);
            var noDmgDef = opp.WabokuActive || opp.PreventBattleDamageThisBattle ||
                           Continuous.AnyPreventsBattleDamage(opp) ||
                           FieldSpellEffects.ControllerAvoidsBattleDamage(this, opp, attacker, targetOrNull);

            if (opp.PreventBattleDamageThisBattle)
                Log($"[Damage Calculation] {opp.Name}: no battle damage from that battle (Kuriboh / effect).");

            // Insect Soldiers of the Sky (GitHub lineage): ATK gain during the Damage Step only.
            // Assign first; the local-lineage attribute bonus below accumulates onto it.
            attacker.DamageStepAtkBonus = TextEffects.TextEffectRuntime.DamageStepAtkBonus(attacker, targetOrNull);
            if (attacker.DamageStepAtkBonus > 0)
                Log($"{attacker.Name} gains {attacker.DamageStepAtkBonus} ATK during this Damage Step.");
            if (targetOrNull != null)
                TextEffects.TextEffectRuntime.NotifyAttackedBy(this, targetOrNull, attacker);
            TextEffects.TextEffectRuntime.ApplyDamageStepAttributeBonus(attacker, targetOrNull);
            // FlipDownAndPeek batch (v162): 8-Claws Scorpion attacking a monster that was face-down Defense when the Damage
            // Step started (its ATK becomes 2400 for this calculation); the battle is noted for Giant Axe Mummy.
            TextEffects.FlipDownAndPeekRuntime.ApplyDamageCalculationAtk(this, attacker, targetOrNull, flippedByBattle);
            // BattleBoosts batch (v161): The Hunter with 7 Weapons vs its declared Type; Injection Fairy Lily's resolved gain.
            TextEffects.BattleBoostsRuntime.ApplyDamageCalculationAtk(this, attacker, targetOrNull);
            // ContinuousLocksAndIgnitions batch (v163): Buster Rancher vs a 2500+ ATK (Attack) / DEF (Defense) battler.
            TextEffects.ContinuousLocksAndIgnitionsRuntime.ApplyDamageCalculationAtk(this, attacker, targetOrNull);
            // "Before damage calculation" effects of the attack target (Reflect Bounder), with this Damage Step's
            // ATK in place. Its "after damage calculation" destruction runs below.
            var destroyAfterCalc = TextEffects.BattlePhaseEndRuntime.FireBeforeDamageCalculation(
                this, who, opp, attacker, targetOrNull);
            if (GameOver)
            {
                TextEffects.TextEffectRuntime.ClearDamageStepAttributeBonus(attacker, targetOrNull);
                BattleStep = BattleStep.BattleStep;
                DamageSubStep = DamageSubStep.None;
                ClearBattleDmgFlags();
                Notify();
                return true;
            }
            // NamedSummonPairs batch: Mirage Knight — the battle target's original ATK, during damage calculation only
            // (applied after the "before damage calculation" effects, which do not see it).
            TextEffects.NamedSummonPairsRuntime.ApplyDamageCalculationAtk(this, attacker, targetOrNull);
            // EffectDamageEvents batch (v162): Gravekeeper's Vassal — the battle damage it inflicts is effect damage instead,
            // so no battle-damage change or prevention applies to it (ygopro calculate_battle_damage skips them).
            TextEffects.EffectDamageEventsRuntime.BattleDamageAsEffect(this, attacker, targetOrNull, piercing,
                ref noDmgAtk, ref noDmgDef, out var defDamageAsEffectBy, out var atkDamageAsEffectBy);
            // DamageStepRules batch: Kishido Spirit — not destroyed by battle with a monster of equal ATK (this damage
            // calculation's ATK).
            if (!atkNoDes)
                atkNoDes = TextEffects.DamageStepRulesRuntime.BattleIndestructible(this, who, attacker, targetOrNull);
            if (!defNoDes && targetOrNull != null)
                defNoDes = TextEffects.DamageStepRulesRuntime.BattleIndestructible(this, opp, targetOrNull, attacker);
            var calc = BattleMechanics.Calculate(attacker, targetOrNull, piercing, atkNoDes, defNoDes,
                noDmgAtk, noDmgDef);
            // Pass prevention flags so ATK < DEF still deals (DEF−ATK) to the attacker
            // unless Waboku / Kuriboh / continuous protection applies to that controller.
            // FlipDownAndPeek batch (v162): Sanitize re-reads the ATK, so this damage calculation's bonus is cleared only
            // after it (cleared before, an attacker's damage-step ATK against a Defense Position monster was undone:
            // 8-Claws Scorpion, Insect Soldiers of the Sky, The Hunter with 7 Weapons).
            BattleMechanics.Sanitize(ref calc, attacker, targetOrNull, noDmgAtk, noDmgDef);
            // The ATK this damage calculation used; the defense-hold guards below read it, not the ATK left once the
            // damage-calculation-only modifiers are gone.
            var calcAtk = BattleMechanics.AttackValue(attacker);
            TextEffects.TextEffectRuntime.ClearDamageStepAttributeBonus(attacker, targetOrNull);
            Log(calc.LogLine);
            if (attacker != null) attacker.AtkBecomesZeroThisCalculation = false;
            if (attacker != null) attacker.DamageStepAtkBonus = 0;
            if (targetOrNull != null) targetOrNull.AtkBecomesZeroThisCalculation = false;

            DamageSubStep = DamageSubStep.AfterDamageCalculation;
            // The monsters this battle destroys cannot be targeted by the battle-damage triggers below.
            _battleDestroyPending = (calc.DestroyAttacker ? attacker : null, calc.DestroyDefender ? targetOrNull : null);
            // Apply LP damage before destruction so UI/orbs update even if destroy is deferred
            // Susa Soldier (SpiritMonsters batch): the battle damage it inflicts to its controller's opponent is halved.
            // A halving to 0 is no battle damage: no LP change, no battle-damage event, and no forced defense-hold.
            var rawDamageToAttackingPlayer = calc.DamageToAttackingPlayer;
            if (calc.DamageToDefendingPlayer > 0)
                calc.DamageToDefendingPlayer = TextEffects.SpiritMonstersRuntime.HalveBattleDamage(
                    this, attacker, opp, calc.DamageToDefendingPlayer);
            if (calc.DamageToAttackingPlayer > 0 && targetOrNull != null)
                calc.DamageToAttackingPlayer = TextEffects.SpiritMonstersRuntime.HalveBattleDamage(
                    this, targetOrNull, who, calc.DamageToAttackingPlayer);
            // DamageStepRules batch: Amazoness Swords Woman — the battle damage its controller would take from its battles
            // is taken by the opponent instead (applied below, after the normal damage).
            TextEffects.DamageStepRulesRuntime.ReflectBattleDamage(this, who, opp, attacker, targetOrNull,
                atkDamageAsEffectBy == null, defDamageAsEffectBy == null, noDmgAtk, noDmgDef,
                ref calc.DamageToAttackingPlayer, ref calc.DamageToDefendingPlayer,
                out var reflectedToAttackingPlayer, out var reflectedToDefendingPlayer);
            // DcrEquipSpells batch: Rod of the Mind's Eye — the battle damage its monster inflicts to the opponent of the
            // Rod's controller becomes 1000 (EFFECT_CHANGE_BATTLE_DAMAGE). Not damage converted to effect damage.
            if (calc.DamageToDefendingPlayer > 0 && defDamageAsEffectBy == null)
                calc.DamageToDefendingPlayer = TextEffects.DcrEquipSpellsRuntime.ChangeBattleDamage(
                    this, attacker, opp, calc.DamageToDefendingPlayer);
            if (calc.DamageToAttackingPlayer > 0 && targetOrNull != null && atkDamageAsEffectBy == null)
                calc.DamageToAttackingPlayer = TextEffects.DcrEquipSpellsRuntime.ChangeBattleDamage(
                    this, targetOrNull, who, calc.DamageToAttackingPlayer);
            // SimpleTriggersAndSwarm batch: Dark Scorpion Combination — a flagged monster's direct attack inflicts 400.
            if (calc.DamageToDefendingPlayer > 0 && defDamageAsEffectBy == null)
                calc.DamageToDefendingPlayer = TextEffects.SimpleTriggersAndSwarmRuntime.ChangeBattleDamage(
                    this, attacker, targetOrNull, opp, calc.DamageToDefendingPlayer);
            // BattleDamageEvents batch: Piranha Army (its direct attack) / Stone Statue of the Aztecs (attacked) double the
            // battle damage their controller's opponent takes — after the set values above (ygopro doubles a changed value).
            if (calc.DamageToDefendingPlayer > 0 && defDamageAsEffectBy == null)
                calc.DamageToDefendingPlayer = TextEffects.BattleDamageEventsRuntime.DoubleBattleDamage(
                    this, attacker, targetOrNull, opp, calc.DamageToDefendingPlayer);
            if (calc.DamageToAttackingPlayer > 0 && targetOrNull != null && atkDamageAsEffectBy == null)
                calc.DamageToAttackingPlayer = TextEffects.BattleDamageEventsRuntime.DoubleBattleDamage(
                    this, attacker, targetOrNull, who, calc.DamageToAttackingPlayer);
            if (calc.DamageToDefendingPlayer > 0 && defDamageAsEffectBy != null)
                ApplyEffectDamage(opp, calc.DamageToDefendingPlayer, defDamageAsEffectBy.Name, defDamageAsEffectBy);
            else if (calc.DamageToDefendingPlayer > 0)
            {
                ApplyDamage(opp, calc.DamageToDefendingPlayer);
                TextEffects.TextEffectRuntime.NotifyInflictedBattleDamage(
                    this, who, opp, attacker, targetOrNull == null, calc.DamageToDefendingPlayer);
                TextEffects.TextEffectRuntime.NotifyBattleDamageInflicted(
                    this, who, attacker, opp, calc.DamageToDefendingPlayer);
            }
            if (calc.DamageToAttackingPlayer > 0 && atkDamageAsEffectBy != null)
                ApplyEffectDamage(who, calc.DamageToAttackingPlayer, atkDamageAsEffectBy.Name, atkDamageAsEffectBy);
            else if (calc.DamageToAttackingPlayer > 0)
            {
                ApplyDamage(who, calc.DamageToAttackingPlayer);
                if (targetOrNull != null)
                {
                    TextEffects.TextEffectRuntime.NotifyInflictedBattleDamage(
                        this, opp, who, targetOrNull, false, calc.DamageToAttackingPlayer);
                    TextEffects.TextEffectRuntime.NotifyBattleDamageInflicted(
                        this, opp, targetOrNull, who, calc.DamageToAttackingPlayer);
                }
            }
            else if (rawDamageToAttackingPlayer <= 0 && targetOrNull != null &&
                     BattleMechanics.UsesDefenseStat(targetOrNull) &&
                     calcAtk < BattleMechanics.DefenseValue(targetOrNull) &&
                     !noDmgAtk)
            {
                // Hard fail-closed: never silently skip defense-hold damage
                var gap = BattleMechanics.DefenseValue(targetOrNull) - calcAtk;
                if (gap > 0) gap = TextEffects.SpiritMonstersRuntime.HalveBattleDamage(this, targetOrNull, who, gap);
                if (gap > 0 && atkDamageAsEffectBy == null)
                    gap = TextEffects.DcrEquipSpellsRuntime.ChangeBattleDamage(this, targetOrNull, who, gap);
                if (gap > 0 && atkDamageAsEffectBy == null)
                    gap = TextEffects.BattleDamageEventsRuntime.DoubleBattleDamage(this, attacker, targetOrNull, who, gap);
                if (gap > 0)
                {
                    Log(
                        $"[RULE] Forced defense-hold damage {gap} → {who.Name} " +
                        $"(ATK {calcAtk} < DEF {BattleMechanics.DefenseValue(targetOrNull)}).");
                    calc.DamageToAttackingPlayer = gap;
                    if (atkDamageAsEffectBy != null)
                        ApplyEffectDamage(who, gap, atkDamageAsEffectBy.Name, atkDamageAsEffectBy);
                    else
                    {
                        ApplyDamage(who, gap);
                        TextEffects.TextEffectRuntime.NotifyInflictedBattleDamage(
                            this, opp, who, targetOrNull, false, gap);
                        TextEffects.TextEffectRuntime.NotifyBattleDamageInflicted(
                            this, opp, targetOrNull, who, gap);
                    }
                }
            }
            if (reflectedToDefendingPlayer > 0) ApplyDamage(opp, reflectedToDefendingPlayer);
            if (reflectedToAttackingPlayer > 0) ApplyDamage(who, reflectedToAttackingPlayer);
            _battleDestroyPending = default;

            // Hard rule gate: never destroy a Defense monster when ATK ≤ DEF
            if (calc.DestroyDefender && targetOrNull != null &&
                BattleMechanics.UsesDefenseStat(targetOrNull) &&
                calcAtk <= BattleMechanics.DefenseValue(targetOrNull))
            {
                Log(
                    $"[RULE] Blocked illegal destruction: {targetOrNull.Name} is in Defense " +
                    $"(DEF {BattleMechanics.DefenseValue(targetOrNull)} ≥ ATK {calcAtk}).");
                calc.DestroyDefender = false;
            }

            // Clear per-battle damage prevention after this battle's calc
            who.PreventBattleDamageThisBattle = false;
            opp.PreventBattleDamageThisBattle = false;

            // NegationMonsters batch (v161), ygopro EVENT_BATTLED: Lesser Fiend's banish redirect is decided, and Dark Ruler
            // Ha Des / Twin-Headed Wolf negate the monsters this battle destroys, before any of their effects can apply.
            TextEffects.NegationMonstersRuntime.OnBattled(this, who, opp, attacker, targetOrNull,
                calc.DestroyAttacker, calc.DestroyDefender);

            // —— After-damage-calc monster triggers (Wall of Illusion / D.D. Lady & Assailant) ——
            TextEffects.TextEffectRuntime.NotifyAfterDamageCalculation(
                this, who, opp, attacker, targetOrNull,
                calc.DestroyAttacker, calc.DestroyDefender);
            // After the Struggle (BattleRules batch): both battlers were involved in damage calculation (EVENT_BATTLED).
            TextEffects.BattleRulesRuntime.NoteBattled(this, attacker, targetOrNull);
            // NamedSummonPairs batch: Mirage Knight's "battled this turn" (GetBattledGroupCount).
            TextEffects.NamedSummonPairsRuntime.NoteBattled(this, attacker, targetOrNull);
            // Reflect Bounder: "If you do, after damage calculation: Destroy this card."
            TextEffects.BattlePhaseEndRuntime.DestroyAfterDamageCalculation(this, destroyAfterCalc);

            // —— Flip effects (after damage calculation, before battle destruction) ——
            if (flippedByBattle != null && opp.TryFindMonster(flippedByBattle, out _))
            {
                var hasFlip = MonsterEffects.IsFlipEffectMonster(flippedByBattle.CardId);
                if (!hasFlip)
                {
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(flippedByBattle);
                    hasFlip = prog != null && prog.HasTiming(TextEffects.EffectTiming.Flip);
                }

                // NegationMonsters batch (v161): negated by Ha Des / Twin-Headed Wolf, its FLIP effect does not activate.
                if (hasFlip && TextEffects.NegationMonstersRuntime.SuppressesBattleFlip(this, flippedByBattle))
                    hasFlip = false;

                if (hasFlip)
                {
                    FlipSourceBattleDestroyPending = calc.DestroyDefender ? flippedByBattle : null;
                    try
                    {
                        MonsterEffects.OnFlipSummoned(this, opp, flippedByBattle);
                    }
                    finally
                    {
                        FlipSourceBattleDestroyPending = null;
                    }
                }

                // SpiritMonsters batch: "When this card is Normal Summoned or flipped face-up" (Otohime, Maharaghi)
                // activates here too, after damage calculation like a FLIP effect (EVENT_FLIP in the Damage Step).
                if (PendingActivation == null && opp.TryFindMonster(flippedByBattle, out _) && !flippedByBattle.IsNegated)
                    TextEffects.SpiritMonstersRuntime.NotifyFlippedFaceUp(this, opp, flippedByBattle);
            }

            if (PendingActivation != null && PendingActivation.IsMonsterEffect)
            {
                _deferredBattle = new DeferredBattleDestruction
                {
                    AttackingPlayer = who,
                    DefendingPlayer = opp,
                    Attacker = attacker,
                    Defender = targetOrNull,
                    DestroyAttacker = calc.DestroyAttacker,
                    DestroyDefender = calc.DestroyDefender,
                    PiercingApplied = calc.PiercingApplied,
                    FlippedByBattle = flippedByBattle
                };
                DamageSubStep = DamageSubStep.AfterDamageCalculation;
                Log("Battle paused for Flip effect target — choose a target (or Cancel).");
                Notify();
                return true;
            }

            ApplyBattleDestructions(who, opp, attacker, targetOrNull, calc.DestroyAttacker,
                calc.DestroyDefender, calc.PiercingApplied, calc.DamageToDefendingPlayer,
                calc.DamageToAttackingPlayer);
            return true;
        }

        void ApplyBattleDestructions(
            DuelistState who,
            DuelistState opp,
            CardInstance attacker,
            CardInstance targetOrNull,
            bool destroyAttacker,
            bool destroyDefender,
            bool piercingApplied,
            int dmgDefAlreadyApplied = -1,
            int dmgAtkAlreadyApplied = -1)
        {
            DamageSubStep = DamageSubStep.End;
            // Banisher of the Light (RedirectAndReplace batch, v142): a monster this battle destroys has no continuous
            // effect while its destruction is applied.
            TextEffects.RedirectAndReplaceRuntime.NoteBattleDestroyed(this,
                destroyAttacker ? attacker : null, destroyDefender ? targetOrNull : null);

            var dmgDef = dmgDefAlreadyApplied >= 0 ? dmgDefAlreadyApplied : 0;
            var dmgAtk = dmgAtkAlreadyApplied >= 0 ? dmgAtkAlreadyApplied : 0;

            // FlipMassEffects (v142): when both battling monsters are destroyed, they are destroyed at the same time —
            // their "sent to the GY" triggers activate once both are in the GY (Spear Cretin can select the other).
            // EngineHygiene batch (v160): "If this card destroys an opponent's monster by battle" (EVENT_BATTLE_DESTROYING,
            // ygopro aux.bdcon: c:IsRelateToBattle()) — the attacker or the defender, once both destructions are done, and
            // only while it is still on the field (both destroyed: neither activates).
            CardInstance attackerDestroyed = null, defenderDestroyed = null;
            if (destroyAttacker && destroyDefender)
                TextEffects.FlipMassEffectsRuntime.Simultaneously(this, DestroyBattled);
            else
                DestroyBattled();
            if (attackerDestroyed != null && who.TryFindMonster(attacker, out _))
                TextEffects.TextEffectRuntime.NotifyDestroyedOpponentByBattle(this, who, opp, attacker, attackerDestroyed);
            if (defenderDestroyed != null && targetOrNull != null && opp.TryFindMonster(targetOrNull, out _))
                TextEffects.TextEffectRuntime.NotifyDestroyedOpponentByBattle(this, opp, who, targetOrNull,
                    defenderDestroyed);
            // BattleDamageEvents batch: Begone, Knave! / Des Counterblow (EVENT_BATTLE_DAMAGE) share the chain with the
            // destroys-by-battle triggers above, so they resolve after battle destruction (the destroyer's trigger still applies).
            TextEffects.BattleDamageEventsRuntime.ResolvePending(this, attacker, targetOrNull);

            void DestroyBattled()
            {
                // If attacker was destroyed by Flip (e.g. MEB) mid-step, skip battle destroy
                if (destroyDefender && targetOrNull != null && opp.TryFindMonster(targetOrNull, out _))
                {
                    if (TryDestroyEquipInsteadOfBattle(opp, targetOrNull))
                    {
                        Log($"{targetOrNull.Name} was not destroyed by battle (equipped monster destroyed instead).");
                    }
                    else
                    {
                        var overwhelm = CardShatterPresentation.ComputeBattleOverwhelm(
                            attacker, targetOrNull, true, false, dmgDef, dmgAtk);
                        CardShatterPresentation.QueueBattle(targetOrNull.InstanceId, overwhelm, targetOrNull.Name);
                        DestroyMonster(opp, targetOrNull, byEffect: null, banishIfDestroyed: false,
                            destroyedByBattle: true, battleDestroyer: attacker);
                        if (attacker != null)
                        {
                            attacker.DestroyedByBattleThisTurn = true;
                            if (!opp.TryFindMonster(targetOrNull, out _)) attackerDestroyed = targetOrNull;
                        }
                        Log($"{targetOrNull.Name} destroyed by battle.");
                    }
                }
                else if (destroyDefender && targetOrNull != null)
                {
                    Log($"{targetOrNull.Name} already left the field (Flip / effect) before battle destruction.");
                }

                if (destroyAttacker && who.TryFindMonster(attacker, out _))
                {
                    if (TryDestroyEquipInsteadOfBattle(who, attacker))
                    {
                        Log($"{attacker.Name} was not destroyed by battle (equipped monster destroyed instead).");
                    }
                    else
                    {
                        var overwhelm = CardShatterPresentation.ComputeBattleOverwhelm(
                            attacker, targetOrNull, false, true, dmgDef, dmgAtk);
                        CardShatterPresentation.QueueBattle(attacker.InstanceId, overwhelm, attacker.Name);
                        DestroyMonster(who, attacker, byEffect: null, banishIfDestroyed: false,
                            destroyedByBattle: true, battleDestroyer: targetOrNull);
                        if (targetOrNull != null && !who.TryFindMonster(attacker, out _)) defenderDestroyed = attacker;
                        Log($"{attacker.Name} destroyed by battle.");
                    }
                }
                else if (destroyAttacker)
                {
                    Log($"{attacker.Name} already left the field (Flip / effect) before battle destruction.");
                }
            }

            if (piercingApplied)
                Log("Piercing battle damage applied (official continuous property).");

            if (!destroyAttacker && !destroyDefender &&
                dmgDefAlreadyApplied == 0 && dmgAtkAlreadyApplied == 0)
                Log("Battle resolved — no damage, no destruction.");

            // End of Damage Step: Hyper Hammerhead bounce if battled monster survived.
            TextEffects.TextEffectRuntime.NotifyEndOfDamageStep(
                this, who, opp, attacker, targetOrNull);

            // BattleWindows batch: the end-of-Damage-Step window (Destruction Punch; Michizure for a monster this
            // Damage Step sent to its owner's GY). The Damage Step ends once both duelists passed.
            if (TextEffects.BattleWindowsRuntime.BeginEndOfDamageStep(this, who, opp, attacker, targetOrNull) &&
                OfferEndOfDamageStepWindow())
                return;
            if (TextEffects.BattleWindowsRuntime.EndOfDamageStepOpen(this))
            {
                FinishEndOfDamageStep();
                return;
            }

            FinishDamageStep(who, opp);
        }

        /// <summary>The Damage Step is over: back to the Battle Step.</summary>
        void FinishDamageStep(DuelistState who, DuelistState opp)
        {
            BattleStep = BattleStep.BattleStep;
            DamageSubStep = DamageSubStep.None;
            // DamageStepRules batch: Cat's Ear Tribe's Damage Step ATK ends.
            TextEffects.DamageStepRulesRuntime.EndBattle(this);
            if (!IsAwaitingResponse && who != null && opp != null)
                CheckLpWin(who, opp);
            Notify();
        }

        /// <summary>
        /// BattleWindows batch: offer the <see cref="ResponseTiming.EndOfDamageStep"/> window to each duelist who has
        /// not passed it yet, the turn player first, only when they have a legal answer. False when nobody does.
        /// </summary>
        bool OfferEndOfDamageStepWindow()
        {
            if (!TextEffects.BattleWindowsRuntime.EndOfDamageStepOpen(this)) return false;
            if (GameOver) return false;
            TextEffects.BattleWindowsRuntime.EndOfDamageStepBattle(this, out var atkPlayer, out var attacker,
                out var target);
            foreach (var p in new[] { TurnPlayer, OpponentOf(TurnPlayer) })
            {
                if (p == null || TextEffects.BattleWindowsRuntime.EndOfDamageStepPassed(this, p)) continue;
                if (OpenResponseWindow(p, ResponseTiming.EndOfDamageStep, attacker, target, atkPlayer, null, null,
                        CombatAnimTimings.ForOpenGameState()))
                {
                    if (!IsHumanControlled(p))
                        SpellTrapEffects.AiAutoRespond(this);
                    Notify();
                    return true;
                }

                TextEffects.BattleWindowsRuntime.NoteEndOfDamageStepPass(this, p);
            }

            return false;
        }

        /// <summary>BattleWindows batch: after a pass or an answer, re-offer the window, or end the Damage Step.</summary>
        void ContinueEndOfDamageStep()
        {
            if (!TextEffects.BattleWindowsRuntime.EndOfDamageStepOpen(this))
            {
                Notify();
                return;
            }

            if (!GameOver && OfferEndOfDamageStepWindow()) return;
            FinishEndOfDamageStep();
        }

        void FinishEndOfDamageStep()
        {
            TextEffects.BattleWindowsRuntime.EndOfDamageStepBattle(this, out var atkPlayer, out _, out _);
            TextEffects.BattleWindowsRuntime.FinishEndOfDamageStep(this);
            FinishDamageStep(atkPlayer, atkPlayer != null ? OpponentOf(atkPlayer) : null);
        }

        /// <summary>
        /// BattleWindows batch, Dark Spirit of the Silent: <paramref name="attacker"/> battles <paramref name="targetOrNull"/>
        /// (null = direct) outside an attack declaration (ygopro Duel.CalculateDamage): a Damage Step from the face-down
        /// flip on, with its Damage Calculation window, battle destruction and end-of-Damage-Step window. The monster does
        /// not declare an attack, so its attack count does not change.
        /// </summary>
        internal bool RunReplacementBattle(DuelistState who, CardInstance attacker, CardInstance targetOrNull)
        {
            var opp = OpponentOf(who);
            ClearIdleChain();
            if (GameOver || who == null || opp == null || attacker == null || Phase != DuelPhase.Battle)
            {
                Notify();
                return false;
            }

            BattleStep = BattleStep.DamageStep;
            TextEffects.SpellTrapDestroyedResponseRuntime.DropPending(this);
            TextEffects.BattleWindowsRuntime.DropSentRecords(this);
            DamageSubStep = DamageSubStep.Start;
            CardInstance flippedByBattle = null;
            if (targetOrNull != null && !targetOrNull.FaceUp && opp.TryFindMonster(targetOrNull, out _))
            {
                targetOrNull.FaceUp = true;
                targetOrNull.Position = BattlePosition.Defense;
                flippedByBattle = targetOrNull;
                if (!Ocg.OcgLabDuelHost.IsActive) FieldSpellEffects.RefreshBoard(this); // DerivedAtkMonsters (v176)
                Log($"[Damage Step · Start] Flipped {targetOrNull.Name} face-up in Defense Position " +
                    $"(DEF {BattleMechanics.DefenseValue(targetOrNull)}).");
            }

            Log(targetOrNull == null
                ? $"{attacker.Name} attacks directly instead."
                : $"{attacker.Name} battles {targetOrNull.Name} instead.");
            return ContinueBeforeDamageCalculation(who, opp, attacker, targetOrNull, flippedByBattle,
                switchedBattle: true);
        }

        /// <summary>
        /// ygopro Card.CanAttack for a monster that battles by an effect (Dark Spirit of the Silent): face-up in Attack
        /// Position on <paramref name="who"/>'s field and not under a "cannot attack" effect. Its attack count does not
        /// matter — no attack is declared.
        /// </summary>
        public bool CanBattleByEffect(DuelistState who, CardInstance m)
        {
            if (who == null || m == null || !m.FaceUp || m.Position != BattlePosition.Attack) return false;
            if (!who.TryFindMonster(m, out _)) return false;
            if (ContinuousCannotAttackBlocks(who, m)) return false;
            if (m.CannotAttackDuringTurnNumber > 0 && TurnNumber == m.CannotAttackDuringTurnNumber) return false;
            return !TextEffects.TextEffectRuntime.AttackForbiddenByEffect(this, m);
        }

        /// <summary>LastTurn batch (v163): the phase (and step) a forced battle interrupted; null when none is running or it
        /// began in the Battle Phase.</summary>
        DuelPhase? _forcedBattleReturnPhase;
        BattleStep _forcedBattleReturnStep;

        /// <summary>
        /// LastTurn batch (v163) — ygopro Duel.ForceAttack: <paramref name="attacker"/> (controlled by <paramref name="who"/>)
        /// attacks <paramref name="target"/> now. No player declares it, so no declaration cost, coin call or attack-target
        /// rule applies and the attacker's remaining attacks do not matter (it still counts as its attack this turn); the
        /// attack-declared triggers, response windows and the whole Damage Step run as usual. Outside the Battle Phase the
        /// duel steps into the Battle Step for this battle only (ocgcore process_forced_battle) and goes back to the phase it
        /// was in through <see cref="EndForcedBattle"/> once the battle is over (TextEffects.LastTurnRuntime.OnPlayIdle).
        /// False when the attack cannot happen (nothing changed then).
        /// </summary>
        public bool BeginForcedAttack(DuelistState who, CardInstance attacker, CardInstance target)
        {
            var opp = OpponentOf(who);
            if (GameOver || who == null || opp == null || attacker == null || target == null || HasDeclaredAttack)
                return false;
            if (!CanBattleByEffect(who, attacker) || !opp.TryFindMonster(target, out _)) return false;
            if (Phase != DuelPhase.Battle)
            {
                _forcedBattleReturnPhase = Phase;
                _forcedBattleReturnStep = BattleStep;
                Phase = DuelPhase.Battle;
            }

            BattleStep = BattleStep.BattleStep;
            DamageSubStep = DamageSubStep.None;
            return DeclareAttack(who, attacker, target, " (a forced attack)");
        }

        /// <summary>LastTurn batch (v163): a forced battle is over — back to the phase it interrupted (no-op otherwise).</summary>
        public void EndForcedBattle()
        {
            if (_forcedBattleReturnPhase == null) return;
            Phase = _forcedBattleReturnPhase.Value;
            BattleStep = _forcedBattleReturnStep;
            DamageSubStep = DamageSubStep.None;
            _forcedBattleReturnPhase = null;
            Log($"The forced battle is over — back to {Phase}.");
        }

        /// <summary>Finish Damage Step after Flip target is chosen/cancelled.</summary>
        public void TryCompleteDeferredBattleDestruction()
        {
            if (_deferredBattle == null) return;
            // Still waiting on Flip / other monster target
            if (PendingActivation != null && PendingActivation.IsMonsterEffect)
                return;

            var d = _deferredBattle;
            _deferredBattle = null;
            Log("[Damage Step · End] Completing battle after Flip resolution…");
            ApplyBattleDestructions(
                d.AttackingPlayer,
                d.DefendingPlayer,
                d.Attacker,
                d.Defender,
                d.DestroyAttacker,
                d.DestroyDefender,
                d.PiercingApplied);
        }

        void ClearDeclaredAttack()
        {
            // Presentation only: later activations are not answering this attack (attacker staging).
            Presentation.ArInteraction.ActivationBeat.NoteAttackEnded();
            DeclaredAttacker = null;
            DeclaredAttackTarget = null;
            DeclaredAttackingPlayer = null;
        }

        void OpenSummonResponseOrContinue(DuelistState summoner, CardInstance summoned,
            SummonKind procedure = SummonKind.None)
        {
            if (summoner == null || summoned == null || GameOver) return;
            if (IsAwaitingResponse || IsAwaitingEffectTarget)
            {
                _queuedSummons.Add((summoner, summoned, procedure));
                return;
            }

            var profile = CombatAnimTimings.ForSummon(summoned, procedure);
            ActivePresentation = new ActiveCombatPresentation
            {
                Kind = CombatActionKind.SummonAppear,
                SourceCard = summoned,
                Actor = summoner,
                Profile = profile,
                Procedure = procedure,
                StartedUnscaledTime = Clock.UnscaledTime,
                Clock = Clock,
                ImpactResolved = false
            };

            _fieldSummonTriggerSummoner = summoner;
            _fieldSummonTriggerSummoned = summoned;

            var defender = OpponentOf(summoner);
            if (OpenResponseWindow(defender, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                QueueOwnSummonWindowIfLegal(summoner, summoned);
                // Presentation only: if an inline AI response removes the monster in this call, the
                // arena still stages it (it never reached a Sync) before the trap's result.
                if (summoner.TryFindMonster(summoned, out var summonZi))
                    Presentation.ArInteraction.ActivationBeat.NoteSummonTrigger(summoned, summonZi,
                        summoner.IsPlayer);
                if (!IsHumanControlled(defender))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return;
            }

            if (OpenResponseWindow(summoner, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                if (!IsHumanControlled(summoner))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return;
            }

            SummonResponseTrapsSealed = false;
            TextEffects.TextEffectRuntime.TryTriggerFaceUpFieldSummon(this, summoner, summoned);
            ClearPresentation();
        }

        public void SealTrapsForSummonResponse() => SummonResponseTrapsSealed = true;

        void QueueOwnSummonWindowIfLegal(DuelistState summoner, CardInstance summoned)
        {
            if (summoner == null || summoned == null) return;
            var own = SpellTrapEffects.CollectLegalResponseCards(
                this, summoner, ResponseTiming.MonsterSummoned, summoned);
            if (own.Count == 0) return;
            _queuedOwnSummonResponder = summoner;
            _queuedOwnSummoner = summoner;
            _queuedOwnSummoned = summoned;
        }

        /// <summary>After a resolving SS (Reborn, etc.) the summon window may have been queued.</summary>
        public void FlushQueuedSummonResponses()
        {
            if (GameOver || IsAwaitingResponse || IsAwaitingEffectTarget) return;
            if (TryOpenQueuedOwnSummonWindow()) return;
            if (_queuedSummons.Count == 0) return;
            var next = _queuedSummons[0];
            _queuedSummons.RemoveAt(0);
            OpenSummonResponseOrContinue(next.summoner, next.summoned, next.procedure);
        }

        bool TryOpenQueuedOwnSummonWindow()
        {
            var who = _queuedOwnSummonResponder;
            var summoned = _queuedOwnSummoned;
            var summoner = _queuedOwnSummoner;
            _queuedOwnSummonResponder = null;
            _queuedOwnSummoned = null;
            _queuedOwnSummoner = null;
            if (who == null || summoned == null || GameOver) return false;
            var profile = CombatAnimTimings.ForSummon(summoned);
            if (!OpenResponseWindow(who, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
                return false;
            if (!IsHumanControlled(who))
                SpellTrapEffects.AiAutoRespond(this);
            Notify();
            return true;
        }

        void FinishSummonResponseWindow()
        {
            if (TryOpenQueuedOwnSummonWindow()) return;
            if (_queuedSummons.Count > 0)
            {
                // King Tiger Wanghu (SummonTriggersAndTributes, v163): answer the Summon whose window just closed
                // before the queued Summon's window overwrites _fieldSummonTriggerSummoned (else it is never seen).
                if (!IsAwaitingEffectTarget)
                    TextEffects.SummonTriggersAndTributesRuntime.OnSummonSettled(
                        this, _fieldSummonTriggerSummoner, _fieldSummonTriggerSummoned);
                // MonsterNegationAndControlLocks batch: Stumbling answers that Summon too.
                if (!IsAwaitingEffectTarget)
                    TextEffects.MonsterNegationAndControlLocksRuntime.OnSummonSettled(
                        this, _fieldSummonTriggerSummoner, _fieldSummonTriggerSummoned);
                FlushQueuedSummonResponses();
                return;
            }

            SummonResponseTrapsSealed = false;
            TextEffects.TextEffectRuntime.TryTriggerFaceUpFieldSummon(
                this, _fieldSummonTriggerSummoner, _fieldSummonTriggerSummoned);
            ClearPresentation();
            Notify();
        }

        /// <summary>
        /// Opens a combat-reaction window if the responder has legal cards.
        /// Duration is bound to <paramref name="profile"/> impact time (animation-driven).
        /// </summary>
        /// <summary>
        /// Pause for compiled "Activate only during your opponent's turn" traps
        /// (Absolute End and the same shape). No-op if none are legal.
        /// </summary>
        void OfferOpponentTurnTrapWindow(DuelistState responder)
        {
            if (responder == null || GameOver) return;
            if (IsAwaitingResponse || IsAwaitingEffectTarget) return;
            if (TurnPlayer == responder) return;
            var profile = CombatAnimTimings.ForOpenGameState();
            if (OpenResponseWindow(responder, ResponseTiming.OpponentOpenState,
                    null, null, null, null, null, profile))
            {
                if (!IsHumanControlled(responder))
                    SpellTrapEffects.AiAutoRespond(this);
            }
        }

        /// <summary>
        /// After LP damage: Numinous Healer / Attack and Receive and the same text shape.
        /// Queues if another window is already open.
        /// </summary>
        void OfferYouTakeDamageWindow(DuelistState who)
        {
            if (who == null || GameOver) return;
            if (IsAwaitingResponse || IsAwaitingEffectTarget)
            {
                _queuedDamageTaken = who;
                return;
            }

            var profile = CombatAnimTimings.ForDamageTaken();
            if (OpenResponseWindow(who, ResponseTiming.YouTakeDamage,
                    null, null, null, null, null, profile))
            {
                if (!IsHumanControlled(who))
                    SpellTrapEffects.AiAutoRespond(this);
            }
        }

        void FlushQueuedDamageTakenWindow()
        {
            var next = _queuedDamageTaken;
            _queuedDamageTaken = null;
            if (next != null && !GameOver && !IsAwaitingResponse)
                OfferYouTakeDamageWindow(next);
        }

        /// <summary>
        /// Play is idle: nothing resolving, no chain, response window, target pick, declared attack, paused battle /
        /// Damage Step, Summon in progress or paused phase. Events that wait for the current chain to finish
        /// (<see cref="FlushFlipEffectsAfterResolution"/>, <see cref="OfferSpellTrapDestroyedWindow"/>) run only then.
        /// </summary>
        bool PlayIdleForDeferredEvents() =>
            !(IsResolvingChain || Chain.IsResolving || Chain.HasLinks || PendingResponse != null ||
              PendingActivation != null || HasDeclaredAttack || _deferredBattle != null ||
              _deferredDamage != null || _summonInProgress != null || _chainPausedForWaiver != null ||
              _endTurnPausedFor != null || _standbyPausedFor != null ||
              ResumeAfterChain != ResponseTiming.None || BattleStep == BattleStep.DamageStep);

        /// <summary>
        /// SimultaneousTriggers batch: <see cref="PlayIdleForDeferredEvents"/>, except that the turn player's Standby
        /// pause (a standby-only activation such as Curse of Fiend) is open play too: the triggers a resolved Chain
        /// left behind go on their own Chain there, before the turn moves on.
        /// </summary>
        internal bool PlayIdleForTriggerChain() =>
            !(IsResolvingChain || Chain.IsResolving || Chain.HasLinks || PendingResponse != null ||
              PendingActivation != null || HasDeclaredAttack || _deferredBattle != null ||
              _deferredDamage != null || _summonInProgress != null || _chainPausedForWaiver != null ||
              _endTurnPausedFor != null ||
              ResumeAfterChain != ResponseTiming.None || BattleStep == BattleStep.DamageStep);

        /// <summary>
        /// TriggerChainResponses batch (v163): once the Chain now resolving is over, play will be open again (no attack,
        /// Damage Step, battle / Summon / phase pause or response window waits behind it), so the triggers it causes can go
        /// on a new Chain there (TextEffects.SimultaneousTriggersRuntime.TryDeferSentToGy).
        /// </summary>
        internal bool OpenPlayAfterChain() =>
            !(HasDeclaredAttack || _deferredBattle != null || _deferredDamage != null || _summonInProgress != null ||
              _chainPausedForWaiver != null || _endTurnPausedFor != null ||
              ResumeAfterChain != ResponseTiming.None || BattleStep == BattleStep.DamageStep ||
              (PendingResponse != null && PendingResponse.Timing != ResponseTiming.ChainResponse));

        /// <summary>
        /// <paramref name="monster"/> (controlled by <paramref name="controller"/>) was just flipped face-up by a
        /// resolving card effect that does not suppress FLIP effects (Swords of Revealing Light, Curse of Fiend; ygopro
        /// Duel.ChangePosition without NO_FLIP_EFFECT). Its FLIP effect activates after that effect — and the chain
        /// it is on — has finished resolving, together with every other trigger the same resolution caused, as one
        /// Chain (SEGOC, TextEffects.SimultaneousTriggersRuntime).
        /// </summary>
        public void QueueFlipEffectAfterResolution(DuelistState controller, CardInstance monster) =>
            TextEffects.SimultaneousTriggersRuntime.QueueFlip(this, controller, monster);

        /// <summary>
        /// From <see cref="Notify"/>: once play is idle, the queued simultaneous triggers go on one Chain and resolve
        /// (TextEffects.SimultaneousTriggersRuntime.Flush); a Chain waiting on a player's pick goes on after it.
        /// </summary>
        void FlushFlipEffectsAfterResolution() => TextEffects.SimultaneousTriggersRuntime.Flush(this);

        /// <summary>
        /// SpellTrapDestroyedResponse batch (v142): once play is idle — nothing resolving, no chain, response
        /// window, target pick, declared attack, paused battle / Damage Step or Summon in progress, and a Main or
        /// Battle Phase — each duelist whose Spell / Trap Cards were destroyed and sent to the GY by a card effect
        /// the opponent controls gets the <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/> window (turn
        /// player first) when they have a legal answer (Gust / Driving Snow). Runs from <see cref="Notify"/>;
        /// destructions nobody can answer are dropped (TextEffects.SpellTrapDestroyedResponseRuntime).
        /// </summary>
        void OfferSpellTrapDestroyedWindow()
        {
            if (!TextEffects.SpellTrapDestroyedResponseRuntime.HasPending(this)) return;
            if (GameOver)
            {
                TextEffects.SpellTrapDestroyedResponseRuntime.ClearAll(this);
                return;
            }

            // A queued FLIP effect still resolving (Weather Report flipped by Swords) finishes first: the whole
            // simultaneous-trigger Chain (SimultaneousTriggers batch).
            if (TextEffects.SimultaneousTriggersRuntime.Busy(this) || !PlayIdleForDeferredEvents() ||
                (Phase != DuelPhase.Main1 && Phase != DuelPhase.Battle && Phase != DuelPhase.Main2))
                return;

            foreach (var victim in new[] { TurnPlayer, OpponentOf(TurnPlayer) })
            {
                if (!TextEffects.SpellTrapDestroyedResponseRuntime.BeginWindow(this, victim)) continue;
                if (OpenResponseWindow(victim, ResponseTiming.SpellTrapDestroyedByOpponent,
                        null, null, null, null, null, CombatAnimTimings.ForOpenGameState()))
                {
                    if (!IsHumanControlled(victim))
                        SpellTrapEffects.AiAutoRespond(this);
                    return;
                }

                TextEffects.SpellTrapDestroyedResponseRuntime.EndWindows(this);
            }
        }

        /// <summary>
        /// BattleWindows batch: once play is idle in a Main or Battle Phase, each duelist (turn player first) who had a
        /// monster sent from the field to their GY gets the <see cref="ResponseTiming.MonsterSentFromFieldToYourGy"/>
        /// window when they have a legal answer (Michizure). Sends nobody can answer are dropped
        /// (TextEffects.BattleWindowsRuntime).
        /// </summary>
        void OfferMonsterSentWindow()
        {
            if (!TextEffects.BattleWindowsRuntime.HasSentRecords(this)) return;
            if (GameOver)
            {
                TextEffects.BattleWindowsRuntime.DropSentRecords(this);
                return;
            }

            if (TextEffects.SimultaneousTriggersRuntime.Busy(this) || !PlayIdleForDeferredEvents() ||
                (Phase != DuelPhase.Main1 && Phase != DuelPhase.Battle && Phase != DuelPhase.Main2))
                return;

            foreach (var victim in new[] { TurnPlayer, OpponentOf(TurnPlayer) })
            {
                if (!TextEffects.BattleWindowsRuntime.BeginSentWindow(this, victim)) continue;
                if (OpenResponseWindow(victim, ResponseTiming.MonsterSentFromFieldToYourGy,
                        null, null, null, null, null, CombatAnimTimings.ForOpenGameState()))
                {
                    if (!IsHumanControlled(victim))
                        SpellTrapEffects.AiAutoRespond(this);
                    return;
                }

                TextEffects.BattleWindowsRuntime.EndSentWindow(this);
            }
        }

        /// <summary>
        /// EquipModel batch: once play is idle in a Main or Battle Phase, each duelist (turn player first) gets the
        /// <see cref="ResponseTiming.MonsterEquipped"/> window once for the monsters just equipped with an Equip Card
        /// when they have a legal answer (Armored Glass). Equips nobody can answer are dropped
        /// (TextEffects.EquipModelRuntime).
        /// </summary>
        void OfferMonsterEquippedWindow()
        {
            if (!TextEffects.EquipModelRuntime.HasPending(this)) return;
            if (GameOver)
            {
                TextEffects.EquipModelRuntime.ClearAll(this);
                return;
            }

            if (TextEffects.SimultaneousTriggersRuntime.Busy(this) || !PlayIdleForDeferredEvents() ||
                (Phase != DuelPhase.Main1 && Phase != DuelPhase.Battle && Phase != DuelPhase.Main2))
                return;

            foreach (var victim in new[] { TurnPlayer, OpponentOf(TurnPlayer) })
            {
                if (!TextEffects.EquipModelRuntime.BeginWindow(this, victim)) continue;
                if (OpenResponseWindow(victim, ResponseTiming.MonsterEquipped,
                        null, null, null, null, null, CombatAnimTimings.ForOpenGameState()))
                {
                    if (!IsHumanControlled(victim))
                        SpellTrapEffects.AiAutoRespond(this);
                    return;
                }
            }

            TextEffects.EquipModelRuntime.ClearAll(this);
        }

        /// <summary>The Armored Glass window closed (passed or answered): the other duelist's window, then play.</summary>
        void FinishMonsterEquippedWindow()
        {
            FlushQueuedSummonResponses();
            if (!IsAwaitingResponse)
                FlushQueuedDamageTakenWindow();
            Notify();
        }

        /// <summary>
        /// EngineHygiene batch: once play is idle, each duelist (turn player first) who just discarded from their hand
        /// gets the <see cref="ResponseTiming.YouDiscardedFromHand"/> window when they have a legal answer (Forced
        /// Requisition). Discards nobody can answer are dropped (TextEffects.DiscardEvents).
        /// </summary>
        void OfferDiscardedWindow()
        {
            if (!TextEffects.DiscardEvents.HasWindowRecords(this)) return;
            if (GameOver)
            {
                TextEffects.DiscardEvents.DropWindowRecords(this);
                return;
            }

            if (TextEffects.SimultaneousTriggersRuntime.Busy(this) || !PlayIdleForDeferredEvents()) return;
            foreach (var victim in new[] { TurnPlayer, OpponentOf(TurnPlayer) })
            {
                if (!TextEffects.DiscardEvents.BeginDiscardWindow(this, victim)) continue;
                if (OpenResponseWindow(victim, ResponseTiming.YouDiscardedFromHand,
                        null, null, null, null, null, CombatAnimTimings.ForOpenGameState()))
                {
                    if (!IsHumanControlled(victim))
                        SpellTrapEffects.AiAutoRespond(this);
                    return;
                }

                TextEffects.DiscardEvents.EndDiscardWindow(this);
            }
        }

        /// <summary>The Forced Requisition window closed (passed or answered): play continues.</summary>
        void FinishDiscardedWindow()
        {
            TextEffects.DiscardEvents.EndDiscardWindow(this);
            Notify();
        }

        /// <summary>
        /// ForcedActivationAndSubstitution batch (v163): once play is idle, each duelist (turn player first) whose
        /// monster(s) an opponent's card effect just returned to their hand gets the
        /// <see cref="ResponseTiming.MonstersReturnedToHandByOpponent"/> window once, when they have a legal answer
        /// (Major Riot). Returns nobody can answer are dropped (TextEffects.MajorRiotRuntime).
        /// </summary>
        void OfferMonstersReturnedWindow()
        {
            if (!TextEffects.MajorRiotRuntime.HasPending(this)) return;
            if (GameOver)
            {
                TextEffects.MajorRiotRuntime.ClearAll(this);
                return;
            }

            if (TextEffects.SimultaneousTriggersRuntime.Busy(this) || !PlayIdleForDeferredEvents()) return;
            foreach (var victim in new[] { TurnPlayer, OpponentOf(TurnPlayer) })
            {
                if (!TextEffects.MajorRiotRuntime.BeginWindow(this, victim)) continue;
                if (OpenResponseWindow(victim, ResponseTiming.MonstersReturnedToHandByOpponent,
                        null, null, null, null, null, CombatAnimTimings.ForOpenGameState()))
                {
                    if (!IsHumanControlled(victim))
                        SpellTrapEffects.AiAutoRespond(this);
                    return;
                }
            }

            TextEffects.MajorRiotRuntime.ClearAll(this);
        }

        /// <summary>The Major Riot window closed (passed or answered): the other duelist's window, then play.</summary>
        void FinishMonstersReturnedWindow()
        {
            FlushQueuedSummonResponses();
            Notify();
        }

        /// <summary>The Michizure window closed (passed or answered): queued windows and play continue.</summary>
        void FinishMonsterSentWindow()
        {
            TextEffects.BattleWindowsRuntime.EndSentWindow(this);
            FlushQueuedSummonResponses();
            if (!IsAwaitingResponse)
                FlushQueuedDamageTakenWindow();
            Notify();
        }

        /// <summary>The Gust / Driving Snow window closed (passed or answered): queued windows and play continue.</summary>
        void FinishSpellTrapDestroyedWindow()
        {
            TextEffects.SpellTrapDestroyedResponseRuntime.EndWindows(this);
            // A Flip Summon's summon window (or a damage-taken window) queued behind this one opens now.
            FlushQueuedSummonResponses();
            if (!IsAwaitingResponse)
                FlushQueuedDamageTakenWindow();
            Notify();
        }

        /// <summary>
        /// Absolute End family: an in-flight attack targeting a monster becomes direct.
        /// </summary>
        public void ConvertDeclaredAttackToDirect()
        {
            if (DeclaredAttackTarget == null) return;
            Log($"{DeclaredAttacker?.Name ?? "The attack"} becomes a direct attack.");
            DeclaredAttackTarget = null;
            if (ActivePresentation != null)
                ActivePresentation.TargetCard = null;
            if (PendingResponse != null)
                PendingResponse.AttackTarget = null;
        }

        /// <summary>
        /// RedirectAndReplace batch (v142), Shift: the in-flight attack on a monster now targets
        /// <paramref name="newTarget"/> (ygopro Duel.ChangeAttackTarget — the attack goes on, no replay). The caller
        /// checks that the attacker could attack it.
        /// </summary>
        public bool ChangeDeclaredAttackTarget(CardInstance newTarget)
        {
            if (DeclaredAttacker == null || DeclaredAttackTarget == null || newTarget == null) return false;
            if (ReferenceEquals(DeclaredAttackTarget, newTarget)) return true;
            Log($"{DeclaredAttacker.Name}'s attack now targets {(newTarget.FaceUp ? newTarget.Name : "a face-down monster")}.");
            DeclaredAttackTarget = newTarget;
            if (ActivePresentation != null)
                ActivePresentation.TargetCard = newTarget;
            if (PendingResponse != null && PendingResponse.Timing == ResponseTiming.AttackDeclared)
                PendingResponse.AttackTarget = newTarget;
            return true;
        }

        /// <summary>
        /// Offer <paramref name="priority"/> a response to the last Chain Link (Counter Traps, monster
        /// Quick Effects, and any Spell Speed 2+ Trap / Quick-Play that can legally chain). Returns false
        /// when they have nothing legal. An AI responder answers (activate or pass) inside this call.
        /// </summary>
        public bool OpenChainResponseWindow(DuelistState priority, bool resetFast = true)
        {
            if (priority == null || Chain.Last == null) return false;
            // TargetNegationsAndHandQuickEffects batch (v163): Great Dezard / Fushioh Richie — a mandatory Quick Effect
            // (QUICK_F on EVENT_CHAINING) becomes the next Chain Link before anyone may respond to a new activation.
            if (resetFast &&
                TextEffects.TargetNegationsAndHandQuickEffectsRuntime.TrySeatForcedNegations(this, out var forcedWindow))
                return forcedWindow;
            var other = OpponentOf(priority);
            if (other == null) return false;
            if (resetFast)
                FastEffects.Open(FastEffectTiming.WindowKind.ChainResponse, priority, other, ChainEvent.Activation);
            var probe = new PendingResponse
            {
                Timing = ResponseTiming.ChainResponse,
                Responder = priority,
                ChainTarget = Chain.Last,
                // A player chaining onto their own last link is offered only chain-answer effects (see
                // field); the AI decides for itself (it only chains a free-chain card that is threatened).
                ChainAnswersOnly = Chain.Last.Controller == priority && IsHumanControlled(priority)
            };
            PendingResponse = probe;
            var legal = SpellTrapEffects.CollectLegalResponseCards(this, priority, ResponseTiming.ChainResponse, null);
            if (legal.Count == 0)
            {
                PendingResponse = null;
                // Michizure (BattleWindows batch): the opponent has nothing to chain to a new link whose activation sent
                // its controller's monster to the GY (a Tribute cost), but that controller may still chain to their own
                // activation: the opponent passes and the activator gets the window, offered only that answer.
                if (!resetFast || Chain.Last.Controller != other ||
                    !TextEffects.BattleWindowsRuntime.HasSentRecords(this))
                    return false;
                probe = new PendingResponse
                {
                    Timing = ResponseTiming.ChainResponse,
                    Responder = other,
                    ChainTarget = Chain.Last,
                    ChainAnswersOnly = true
                };
                PendingResponse = probe;
                legal = SpellTrapEffects.CollectLegalResponseCards(this, other, ResponseTiming.ChainResponse, null);
                legal.RemoveAll(c => c?.Def == null || !TextEffects.BattleWindowsRuntime.AnswersSendThisChainMade(this,
                    other, TextEffects.CompiledEffectCache.GetOrCompile(c.Def)));
                if (legal.Count == 0)
                {
                    PendingResponse = null;
                    return false;
                }

                if (FastEffects.IsOpen) FastEffects.Pass(priority);
                priority = other;
            }

            probe.LegalCards.AddRange(legal);
            probe.OpenedUnscaledTime = Clock.UnscaledTime;
            probe.ReactionSeconds = CombatAnimTimings.DefaultResponseSeconds;
            probe.Prompt = IsHumanControlled(priority)
                ? $"Chain Link {probe.ChainTarget.LinkNumber}: activate a response or pass."
                : "Opponent is considering a Chain Link response…";
            Log(probe.Prompt);
            if (!IsHumanControlled(priority))
                SpellTrapEffects.AiAutoRespond(this);
            else
                Notify(); // listeners (TimedResponseClock, UI) start the human's countdown
            return true;
        }

        public bool TryActivateChainResponse(DuelistState who, CardInstance card) =>
            TryActivateChainResponse(who, card, fromHand: false, autoPickTarget: who == null || !who.IsPlayer);

        /// <summary>
        /// Chain <paramref name="card"/> to the last Chain Link. Counter Traps / monster Quick Effects use
        /// their ChainLinkActivated clause (negate …); any other legal Spell Speed 2+ Trap or Quick-Play
        /// (MST, Book of Moon, Waboku …) chains its normal Activate effect and resolves in chain order.
        /// </summary>
        public bool TryActivateChainResponse(DuelistState who, CardInstance card, bool fromHand,
            bool autoPickTarget)
        {
            if (PendingResponse == null || PendingResponse.Timing != ResponseTiming.ChainResponse ||
                PendingResponse.Responder != who || card == null)
                return false;
            // Jinzo / Spell Canceller: that card kind cannot be activated (ContinuousNegation batch).
            if (TextEffects.ContinuousNegationRuntime.ActivationLocked(this, who, card, out var lockReason))
            {
                Log($"Activation illegal: {lockReason}");
                return false;
            }
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clauses = prog.ClausesFor(TextEffects.EffectTiming.ChainLinkActivated);
            if (clauses == null || clauses.Count == 0)
                return TextEffects.TextEffectRuntime.ActivateFreeChainOnChain(this, who, card, fromHand, prog,
                    autoPickTarget);
            var clause = clauses[0];
            // ChainLinkAnswers batch: Jar Robber / Spell Shield Type-8 (its bullet and send cost) / My Body as a Shield /
            // Dramatic Rescue — every check, the bullet choice and the costs; a Quick-Play may answer from the hand.
            if (TextEffects.ChainLinkAnswersRuntime.TryActivateAnswer(this, who, card, fromHand, autoPickTarget, clauses,
                    out var answerSeated))
                return answerSeated;
            // TargetNegationsAndHandQuickEffects batch (v163): a monster's Chain answer from the hand (Gravekeeper's Watcher).
            var handAnswer = fromHand && TextEffects.TargetNegationsAndHandQuickEffectsRuntime.IsHandChainAnswer(clause);
            if (fromHand && !handAnswer) return false;
            if (handAnswer &&
                (!TextEffects.TargetNegationsAndHandQuickEffectsRuntime.TryCanAnswerFromHand(this, who, card, clause,
                     PendingResponse.ChainTarget, out var handOk, out var handWhy) || !handOk))
            {
                Log($"Activation illegal: {handWhy}");
                return false;
            }
            // EffectDamageEvents batch (v162): Barrel Behind the Door / Trap of Board Eraser answer only a link that would
            // inflict effect damage to their controller.
            if (TextEffects.EffectDamageEventsRuntime.IsDamageAnswer(clause) &&
                !TextEffects.EffectDamageEventsRuntime.CanAnswerDamageLink(this, who, card, clause,
                    PendingResponse.ChainTarget, out var damageAnswerReason))
            {
                Log($"Activation illegal: {damageAnswerReason}");
                return false;
            }
            var speed = OfficialEffectRegistry.SpeedOf(card.Def);
            var cls = OfficialEffectRegistry.ClassOfActivation(card.Def, false);
            if (!Chain.CanAddLink(speed, cls)) return false;

            // Every cost is checked before any is paid (DR-08): a refused activation costs nothing.
            // Soft residual: Spell Economics Soft ContinuousProtections Spell activate PayLp skip
            // (Mask Soft surgical Soft DE) — Trap / non-Spell chain answers still pay.
            var skipSpellPayLp = TextEffects.ContinuousProtections.SkipsSpellActivatePayLp(this, who, card);
            if (clause.PayLpAmount > 0 && who.LifePoints < clause.PayLpAmount && !skipSpellPayLp)
                return false;
            // Maryokutai (NegationsAndGySummons batch): "You can Tribute this card" — the monster must still be
            // Tributable (Checked before any cost is paid).
            if (clause.RequiresTributeThis &&
                !TextEffects.NegationsAndGySummonsRuntime.CanTributeThisForChainAnswer(this, who, card, out _))
                return false;
            // SpellTrapAnswersAndActivationLocks batch: a discard of N cards or of a Spell (Spell Vanishing, Cursed Seal of the
            // Forbidden Spell), which the one-card discard below cannot pay.
            if (TextEffects.SpellTrapAnswersAndActivationLocksRuntime.TryPayAnswerDiscards(this, who, card, clause,
                    human: !autoPickTarget && IsHumanControlled(who),
                    seat: () => SeatChainAnswer(who, card, clause, speed, cls, skipSpellPayLp, null, handAnswer),
                    out var batchDiscardSeated))
                return batchDiscardSeated;
            CardInstance discard = null;
            if (clause.RequiresDiscardCost)
            {
                // EngineResiduals (v163): a human chooses the card to discard (the generic card pick; Cancel = the card is
                // not activated, nothing is paid, the window stays open); a lone card is discarded with no prompt. The AI
                // discards the lowest-value other card (AiHeuristicPolicy.DiscardPriority).
                if (!autoPickTarget && IsHumanControlled(who))
                {
                    var answering = PendingResponse;
                    return TextEffects.EngineResidualsRuntime.AskChainAnswerDiscard(this, who, card, clause,
                        chosen =>
                        {
                            var stillThere = card.Def != null && card.Def.IsMonster
                                ? who.TryFindMonster(card, out _)
                                : who.TryFindSpellTrap(card, out _) && !card.FaceUp;
                            if (!ReferenceEquals(PendingResponse, answering) || PendingResponse.Responder != who ||
                                !stillThere || !Chain.CanAddLink(speed, cls))
                            {
                                Log($"{card.Name}: the window it answered has passed — not activated.");
                                return false;
                            }

                            return SeatChainAnswer(who, card, clause, speed, cls, skipSpellPayLp, chosen, handAnswer);
                        });
                }

                discard = AiHeuristicPolicy.PickLowestValue(who.Hand, card);
                if (discard == null) return false;
            }

            // SpellCounterSpenders batch: Anti-Spell removes N Spell Counters from its controller's side as it is
            // activated. A human with a real choice taps the cards (Cancel: not activated, the counters go back, the
            // window stays open); otherwise they are removed at once.
            if (clause.RemoveSpellCountersOnYourSide > 0)
            {
                var answeringCounters = PendingResponse;
                var stillSet = card.Def != null && !card.Def.IsMonster;
                return TextEffects.SpellCounterSpendersRuntime.AskChainAnswerCost(this, who, card,
                    clause.RemoveSpellCountersOnYourSide, human: !autoPickTarget && IsHumanControlled(who),
                    stillLegal: () => ReferenceEquals(PendingResponse, answeringCounters) &&
                                      PendingResponse?.Responder == who &&
                                      (!stillSet || (who.TryFindSpellTrap(card, out _) && !card.FaceUp)) &&
                                      Chain.CanAddLink(speed, cls),
                    seat: () => SeatChainAnswer(who, card, clause, speed, cls, skipSpellPayLp, discard, handAnswer));
            }

            return SeatChainAnswer(who, card, clause, speed, cls, skipSpellPayLp, discard, handAnswer);
        }

        /// <summary>
        /// Every check of a Counter Trap / monster Quick Effect answering the last Chain Link passed and its discard (if
        /// any) is chosen: pay the costs, seat the link and let the other player respond (<see cref="TryActivateChainResponse"/>).
        /// </summary>
        internal bool SeatChainAnswer(DuelistState who, CardInstance card, TextEffects.EffectClause clause, SpellSpeed speed,
            EffectClass cls, bool skipSpellPayLp, CardInstance discard, bool handAnswer)
        {
            // ChainLinkAnswers batch: a Quick-Play Spell answering from the hand (Jar Robber, My Body as a Shield) is
            // placed face-up in a free Spell & Trap Zone as it activates.
            var handQuickPlay = !handAnswer && card?.Def != null && card.Def.IsSpell && who.Hand.Contains(card);
            var costSendMark = TextEffects.BattleWindowsRuntime.SentCount(this);
            if (clause.PayLpAmount > 0 && !skipSpellPayLp)
                PayLifePoints(who, clause.PayLpAmount, $"Cost: pay {clause.PayLpAmount} LP.");
            else if (clause.PayLpAmount > 0 && skipSpellPayLp)
                Log($"{who.Name} does not pay LP to activate Spell Cards (Spell Economics).");
            // Solemn Judgment: pay half your LP (always payable).
            if (clause.PayHalfLpCost)
                TextEffects.SummonNegationRuntime.PayHalfLp(this, who);
            if (discard != null)
            {
                SendCardToGrave(who, discard, sentBy: card);
                Log($"Cost: discard {discard.Name}.");
                TextEffects.DiscardEvents.Record(this, who, discard, TextEffects.DiscardCause.Cost);
            }

            if (clause.RequiresTributeThis)
                TextEffects.NegationsAndGySummonsRuntime.TributeThisForChainAnswer(this, who, card);
            if (handAnswer && !TextEffects.TargetNegationsAndHandQuickEffectsRuntime.PayHandAnswerCost(this, who, card, clause))
                return false;

            if (card.Def != null && (card.Def.IsTrap || card.Def.IsSpell))
                SpellTrapEffects.PlaceFaceUpForActivation(this, who, card, fromHand: handQuickPlay);

            var target = PendingResponse.ChainTarget;
            var from = handAnswer || handQuickPlay ? CardLocation.Hand
                : card.Def != null && card.Def.IsMonster
                ? CardLocation.MonsterZone
                : CardLocation.SpellTrapZone;
            var link = Chain.AddLink(who, card, speed, cls, from, handAnswer || handQuickPlay,
                wasSet: from == CardLocation.SpellTrapZone, "chain-response");
            if (link == null) return false;
            // v159 audit follow-up (EngineHygiene): the Tribute paid above is this link's cost send (Michizure).
            TextEffects.BattleWindowsRuntime.TieCostSendsToLink(this, costSendMark, link);
            link.TargetLink = target;
            link.NegatesActivation = clause.Action == TextEffects.EffectActionKind.NegateActivation;
            link.DestroyNegatedCard = clause.DestroyNegatedCard;
            link.FlipSelfFaceUpDefense = clause.FlipSelfFaceUpDefense;
            link.NegationWaivableBySpellDiscard = clause.OpponentMayDiscardSpellToNegateThis;
            GameEvents.Add(DuelGameEventKind.Activated, TurnNumber, who, card, target?.Card);
            ClearPendingResponse();
            if (!OpenChainResponseWindow(OpponentOf(who), true))
            {
                Chain.StartResolution();
                ResolveChainStack();
            }

            return true;
        }
        public bool OpenResponseWindow(
            DuelistState responder,
            ResponseTiming timing,
            CardInstance attacker,
            CardInstance attackTarget,
            DuelistState attackingPlayer,
            CardInstance summoned,
            DuelistState summoner,
            CombatAnimProfile? profile = null)
        {
            if (responder == null || timing == ResponseTiming.None) return false;

            // Assign PendingResponse shell first so legality helpers can read timing context
            // (hand QEs need AttackingPlayer/Attacker for Kuriboh PSCT).
            var pendingProbe = new PendingResponse
            {
                Timing = timing,
                Responder = responder,
                Attacker = attacker,
                AttackTarget = attackTarget,
                AttackingPlayer = attackingPlayer,
                Summoned = summoned,
                Summoner = summoner
            };
            PendingResponse = pendingProbe;

            var legal = SpellTrapEffects.CollectLegalResponseCards(
                this, responder, timing, summoned, attacker, attackingPlayer);
            if (legal.Count == 0)
            {
                PendingResponse = null;
                return false;
            }

            var p = profile ?? (timing switch
            {
                ResponseTiming.AttackDeclared => CombatAnimTimings.ForAttack(attacker, attackTarget == null),
                ResponseTiming.DamageCalculation => CombatAnimTimings.ForDamageCalculation(),
                ResponseTiming.OpponentOpenState => CombatAnimTimings.ForOpenGameState(),
                ResponseTiming.YouTakeDamage => CombatAnimTimings.ForDamageTaken(),
                ResponseTiming.SpellTrapDestroyedByOpponent => CombatAnimTimings.ForOpenGameState(),
                ResponseTiming.MonsterEquipped => CombatAnimTimings.ForOpenGameState(),
                ResponseTiming.MonstersReturnedToHandByOpponent => CombatAnimTimings.ForOpenGameState(),
                _ => CombatAnimTimings.ForSummon(summoned)
            });

            // Do not stretch hologram ImpactAtSeconds to the decision window.
            // Player-facing activate/response countdown is DefaultResponseSeconds (5s).
            var legalNames = string.Join(", ", legal.FindAll(c => c != null).ConvertAll(c => c.Name));
            if (legalNames.Length > 80)
                legalNames = legalNames.Substring(0, 77) + "…";

            pendingProbe.OpenedUnscaledTime = Clock.UnscaledTime;
            pendingProbe.ReactionSeconds = CombatAnimTimings.DefaultResponseSeconds;
            pendingProbe.MotionLine = p.MotionLine;
            pendingProbe.LegalCards.Clear();
            pendingProbe.LegalCards.AddRange(legal);
            PendingResponse = pendingProbe;

            // Human sees their own legal cards. AI considering a face-down trap must
            // not leak the name ("Ring of Destruction or Pass") on the player's turn.
            if (!IsHumanControlled(responder))
            {
                pendingProbe.Prompt = "Opponent is considering a response…";
                pendingProbe.MotionLine = pendingProbe.Prompt;
            }
            else
            {
                var win = CombatAnimTimings.DefaultResponseSeconds;
                pendingProbe.Prompt = timing switch
                {
                    ResponseTiming.AttackDeclared =>
                        $"Attack ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.DamageCalculation =>
                        $"Damage Step ({win:0.0}s) — activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.OpponentOpenState =>
                        $"Opponent's turn ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.YouTakeDamage =>
                        $"You took damage ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.SpellTrapDestroyedByOpponent =>
                        $"Your card was destroyed ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.OpponentDrewOutsideDrawPhase =>
                        $"Opponent drew ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.EndOfDamageStep =>
                        $"End of the Damage Step ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.MonsterSentFromFieldToYourGy =>
                        $"Your monster went to the GY ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.YouDiscardedFromHand =>
                        $"You discarded ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.MonstersReturnedToHandByOpponent =>
                        $"Your monster returned to your hand ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    ResponseTiming.MonsterEquipped =>
                        $"A monster was equipped ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal).",
                    _ =>
                        $"Summon ({win:0.0}s) — tap a blinking zone to activate {legalNames} ({legal.Count} legal)."
                };
            }

            Log(pendingProbe.Prompt);
            return true;
        }

        void ApplyDamage(DuelistState target, int dmg)
        {
            if (dmg <= 0 || target == null) return;
            if (target.NoDamageThroughTurnNumber > 0 && TurnNumber <= target.NoDamageThroughTurnNumber)
            {
                Log($"{target.Name} takes no damage (Crush Card Virus).");
                return;
            }
            target.LifePoints = Mathf.Max(0, target.LifePoints - dmg);
            var you = target.IsPlayer ? "You took" : $"{target.Name} took";
            Log($"{you} {dmg} damage ({target.LifePoints} LP remaining).");
            DuelPresentationPacer.HoldCombatResult();
            OfferYouTakeDamageWindow(target);
        }

        void CheckLpWin(DuelistState a, DuelistState b)
        {
            if (a == null || b == null || GameOver) return;
            var aOut = a.LifePoints <= 0;
            var bOut = b.LifePoints <= 0;
            // Rulebook: both players at 0 LP at the same time → the duel is a Draw (DR-06).
            if (aOut && bOut) EndGame(null, DuelEndReason.Draw);
            else if (aOut) EndGame(b, DuelEndReason.LifePoints);
            else if (bOut) EndGame(a, DuelEndReason.LifePoints);
        }

        /// <summary>
        /// A cost, payment or burn inside an open window dropped a player to 0 LP; the LP check runs once
        /// the window / chain closes (<see cref="SettlePendingLpLoss"/>).
        /// </summary>
        bool _lpLossPending;

        /// <summary>
        /// Pay / lose LP that is not damage (activation costs, "pay X LP", "lose half your LP").
        /// Clamps at 0 and logs <paramref name="logLine"/>. Reaching 0 LP loses the duel (DR-05): at once
        /// when nothing is open, otherwise as soon as the chain / response window in progress closes.
        /// </summary>
        /// <param name="isCost">False for an LP loss that is not a cost ("loses half their LP"): only costs are recorded
        /// for Life Absorbing Machine (ContinuousTrapsAndSpiritSupport batch, Lua EVENT_PAY_LPCOST).</param>
        public void PayLifePoints(DuelistState who, int amount, string logLine = null, bool isCost = true)
        {
            if (who == null || amount <= 0) return;
            if (isCost)
                TextEffects.ContinuousTrapsAndSpiritSupportRuntime.NoteLpCostPaid(this, who,
                    Math.Min(amount, Math.Max(0, who.LifePoints)));
            who.LifePoints = Math.Max(0, who.LifePoints - amount);
            if (!string.IsNullOrEmpty(logLine))
                Log(logLine);
            if (who.LifePoints > 0) return;
            _lpLossPending = true;
            SettlePendingLpLoss();
        }

        /// <summary>
        /// Ends the duel for a pending 0 LP from <see cref="PayLifePoints"/> once no chain is resolving and
        /// no response window or cost / target pick is open. Safe to call at any time.
        /// </summary>
        public void SettlePendingLpLoss()
        {
            if (!_lpLossPending) return;
            if (GameOver)
            {
                _lpLossPending = false;
                return;
            }

            if (IsResolvingChain || Chain.IsResolving || PendingResponse != null || PendingActivation != null)
                return;
            _lpLossPending = false;
            CheckLpWin(Player, Opponent);
        }

        public void EndTurn(DuelistState who)
        {
            if (GameOver || TurnPlayer != who) return;
            if (IsAwaitingResponse || IsAwaitingEffectTarget || HasDeclaredAttack)
            {
                Log("Cannot end turn during a response / targeting window.");
                return;
            }

            // May end from MP1, Battle, or MP2. Leaving Battle still runs its End Step.
            _standbyPausedFor = null;
            EndBattlePhase();
            // SummonPromptsAndCosts batch (v163): a human's optional end-of-Battle-Phase Special Summon (Vampire Baby) is
            // answered before the End Phase starts; the turn ends once it is (SummonPromptsAndCostsRuntime.ContinueEndTurn).
            if (TextEffects.SummonPromptsAndCostsRuntime.HoldsEndTurn(this, who))
            {
                Phase = DuelPhase.Main2;
                Notify();
                return;
            }
            Phase = DuelPhase.End;
            Log($"[{who.Name}] End Phase");
            if (who.CrushCardLingerEndsRemaining > 0)
                TextEffects.TextEffectRuntime.AdvanceVirusLinger(this, who);
            TextEffects.TextEffectRuntime.TickCocoonCounters(this, who);
            // LastTurn batch (v163): "at the End Phase of this turn" — a continuous effect, applied before the End Phase
            // triggers (Lua EVENT_PHASE + PHASE_END, EFFECT_TYPE_CONTINUOUS).
            TextEffects.LastTurnRuntime.OnEndPhase(this);
            if (GameOver) return;
            // DcrSpellTrapUtility batch: Interdimensional Matter Transporter — banished "until the End Phase" (Lua
            // EVENT_PHASE + PHASE_END, EFFECT_TYPE_CONTINUOUS): back at the start of the End Phase.
            TextEffects.DcrSpellTrapUtilityRuntime.OnEndPhase(this);
            // ChaosBanishSummons batch: Chaosrider Gustaph's "until the end of your opponent's next turn" ATK.
            TextEffects.ChaosBanishSummonsRuntime.OnEndPhase(this);
            if (GameOver) return;
            // TriggerChainResponses (v163 review): an End Phase trigger that goes on a Chain (Spirit's Invitation) is
            // seated once the End Phase effects below have resolved, never while they resolve.
            TextEffects.TriggerChainResponsesRuntime.BeginCollect(this);
            try
            {
                TextEffects.TextEffectRuntime.FirePhaseTriggers(this, who, TextEffects.EffectTiming.EndPhase);
                // EngineHygiene batch: Ekibyo Drakmord's countdown, Super Rejuvenation's draw.
                if (!GameOver) TextEffects.EngineHygieneRuntime.OnEndPhase(this);
                // BattleDestroyedGyRecovery batch (v162): Return of the Doomed's "at the end of this turn".
                if (!GameOver) TextEffects.BattleDestroyedGyRecoveryRuntime.OnEndPhase(this);
            }
            finally
            {
                TextEffects.TriggerChainResponsesRuntime.EndCollect(this);
            }

            if (GameOver) return;
            // PerCopyDeck batch (v163): Graverobber's Spell goes back to its owner's GY "at the end of the turn".
            TextEffects.PerCopyDeckRuntime.OnEndPhase(this);
            if (GameOver) return;
            if (IsAwaitingEffectTarget)
            {
                _endTurnPausedFor = who;
                Notify();
                return;
            }

            // TriggerChainResponses batch (v163): an End Phase trigger seated on a Chain (Spirit's Invitation after a
            // Spirit's return) waits on a Chain response; the End Phase goes on once that Chain has resolved.
            if (TextEffects.TriggerChainResponsesRuntime.ChainOpen(this))
            {
                _endTurnPausedFor = who;
                TextEffects.TriggerChainResponsesRuntime.RunAfterChain(this, ResumeEndTurnAfterTrigger);
                Notify();
                return;
            }

            FinishEndTurnAfterTriggers(who);
        }

        /// <summary>
        /// Continue End Phase after a mandatory trigger pick (turn-player tribute).
        /// No-op if End Phase is not paused.
        /// </summary>
        public void ResumeEndTurnAfterTrigger()
        {
            var who = _endTurnPausedFor;
            if (who == null) return;
            if (IsAwaitingEffectTarget) return;
            // DestinyBoard batch: a queued Destiny Board placement pick is asked before the End Phase goes on.
            if (TextEffects.DestinyBoardRuntime.OfferQueuedPick(this))
            {
                Notify();
                return;
            }
            if (IsAwaitingEffectTarget) return;
            if (GameOver)
            {
                _endTurnPausedFor = null;
                return;
            }

            // EquipFamily: a human's queued Dark Necrofear pick is mandatory. FirePhaseTriggers skips it when another
            // End Phase pick (Garuda / Aqua Spirit, a tribute) opened first, so every resume asks it before the End
            // Phase finishes; the phase stays paused until it is answered.
            if (TextEffects.EquipFamilyRuntime.OfferQueuedPick(this))
            {
                Notify();
                return;
            }

            // SpecialSummonEffects batch (v161): a human's queued Twin-Headed Behemoth Yes / Cancel.
            if (TextEffects.SpecialSummonEffectsRuntime.OfferQueuedPick(this))
            {
                Notify();
                return;
            }

            // BattleDestroyedGyRecovery batch (v162): a human's queued Return of the Doomed pick.
            if (TextEffects.BattleDestroyedGyRecoveryRuntime.OfferQueuedPick(this))
            {
                Notify();
                return;
            }

            _endTurnPausedFor = null;
            FinishEndTurnAfterTriggers(who);
        }

        void FinishEndTurnAfterTriggers(DuelistState who)
        {
            if (who == null) return;
            // LastTurn batch (v163): a check registered during this End Phase still applies in it.
            TextEffects.LastTurnRuntime.OnEndPhase(this);
            if (GameOver) return;
            // DcrSpellTrapUtility batch: a monster banished until the End Phase during this End Phase returns in it.
            TextEffects.DcrSpellTrapUtilityRuntime.OnEndPhase(this);
            // ChaosBanishSummons batch: Chaosrider Gustaph's "until the end of your opponent's next turn" ATK.
            TextEffects.ChaosBanishSummonsRuntime.OnEndPhase(this);
            if (GameOver) return;
            // Hand size limit. There is no discard-choice UI yet (DR-07), so the lowest-value card
            // (AiHeuristicPolicy.DiscardPriority) goes first — never simply the newest draw.
            // The discard is a real "sent to the GY" (event log, GY triggers).
            // Soft residual: Infinite Cards ContinuousWhileFaceUp NoHandSizeLimit (Mask/Dark Door
            // Soft surgical) — Soft DE HandSizeLimitEndPhase override skips EP discard while
            // a face-up non-negated NoHandSizeLimit host is on the field (either player).
            // Soft residual: Enervating Mist ContinuousWhileFaceUp OpponentHandSizeLimitBecomes
            // (Infinite Cards Soft surgical twin) — Soft DE HandSizeLimit override for opponent
            // only: while Mist face-up on the other side, `who`'s EP limit becomes Amount (5);
            // controller unaffected (default 6). Infinite Cards NoHandSizeLimit still wins /
            // bars Enervating Mist remapping when both are face-up.
            if (!TextEffects.ContinuousProtections.NoHandSizeLimit(this))
            {
                var handLimit = TextEffects.ContinuousProtections.HandSizeLimitEndPhaseFor(this, who);
                // EngineHygiene batch: the hand-size discards are one discard event.
                TextEffects.DiscardEvents.Begin(this);
                while (who.Hand.Count > handLimit)
                {
                    var discard = AiHeuristicPolicy.PickLowestValue(who.Hand, null) ?? who.Hand[who.Hand.Count - 1];
                    SendCardToGrave(who, discard);
                    if (who.Hand.Contains(discard))
                    {
                        // Defensive: never loop forever on a card that refused to leave the hand.
                        who.Hand.Remove(discard);
                        if (!who.Graveyard.Contains(discard)) who.Graveyard.Add(discard);
                    }
                    Log($"[{who.Name}] discards {discard.Name} (hand size limit {handLimit}).");
                    TextEffects.DiscardEvents.Record(this, who, discard, TextEffects.DiscardCause.HandSizeRule);
                }
                TextEffects.DiscardEvents.End(this);
            }

            // Official: Swords of Revealing Light is destroyed during the End Phase of
            // the opponent's 3rd turn (count opponent's End Phases after activation).
            var swordsOwner = OpponentOf(who);
            if (swordsOwner != null)
            {
                foreach (var st in swordsOwner.SpellTrapsOnField().ToList())
                    if (IsSwordsCountdown(st))
                        AdvanceSwordsCountdown(swordsOwner, st);
            }

            // Temporary control (Change of Heart) returns during the End Phase. With no
            // free Monster Zone on the owner's side, the monster is sent to the GY.
            foreach (var side in new[] { Player, Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m == null || m.ReturnControlAtEndOfTurn != TurnNumber || m.ControlOwner == null)
                        continue;
                    var back = m.ControlOwner;
                    if (!TryTakeControl(back, m))
                    {
                        Log($"{m.Name}: no Monster Zone to return to — sent to the GY.");
                        SendCardToGrave(side, m);
                    }
                }
            }

            // Temporary Special Summons (Archfiend's Roar) are destroyed during the End
            // Phase of the turn they were summoned.
            foreach (var side in new[] { Player, Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m == null || m.TempDestroyOnEndOfTurn != TurnNumber) continue;
                    Log($"{m.Name} is destroyed during the End Phase (temporary Special Summon).");
                    DestroyMonsterPublic(side, m);
                }
            }
            if (GameOver) return;
            // PhaseTriggersAndIgnitionCosts batch: Magical Scientist's Fusion Monster returns to the Extra Deck.
            TextEffects.PhaseTriggersAndIgnitionCostsRuntime.OnEndPhase(this);
            if (GameOver) return;

            // After End Phase, Set cards may be activated on following turns
            // (also clear leftover flags on both sides so a Set trap is legal next turn).
            foreach (var side in new[] { Player, Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                    st.SetThisTurn = false;
            }
            foreach (var m in who.MonstersOnField())
                m.SetThisTurn = false;

            // Clear once-per-turn attack flags for next turn of this player
            foreach (var m in who.MonstersOnField())
            {
                m.ClearAttackFlags();
                m.SummonedThisTurn = false;
                m.ChangedPositionThisTurn = false;
            }

            // Waboku / until-end-of-turn ATK: "this turn" ends at End Phase
            Player.WabokuActive = false;
            Opponent.WabokuActive = false;
            Player.CannotDeclareAttackThisTurn = false;
            Opponent.CannotDeclareAttackThisTurn = false;
            Player.MustAttackDirectlyThisTurn = false;
            Opponent.MustAttackDirectlyThisTurn = false;
            ClearUntilEndOfTurnStatMods();
            RevertUntilEndPhaseControl();

            PendingTributes.Clear();
            // Last thing in the End Phase: what the other player holds now is "at the end of the opponent's
            // last End Phase" on their next turn (The Agent of Wisdom - Mercury).
            TextEffects.EmptyHandStandbyDrawRuntime.RecordEndOfEndPhase(this, who);
            // DcrSpellTrapUtility batch: Final Countdown counts the turn as it ends (Lua EVENT_TURN_END) and may end the Duel.
            TextEffects.DcrSpellTrapUtilityRuntime.OnTurnEnd(this);
            if (GameOver) return;
            BeginTurn(OpponentOf(who));
            Notify();
        }

        /// <summary>
        /// Change of Heart family: return stolen monsters at this End Phase.
        /// No zone for the original controller → send the monster to the GY.
        /// </summary>
        void RevertUntilEndPhaseControl()
        {
            var pending = new List<CardInstance>();
            foreach (var who in new[] { Player, Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                    if (m != null && m.ControlRevertsAtEndPhase)
                        pending.Add(m);
            }

            foreach (var m in pending)
            {
                m.ControlRevertsAtEndPhase = false;
                var original = m.OriginalControllerIsPlayer ? Player : Opponent;
                var current = ControllerOf(m);
                if (original == null || current == null || original == current) continue;
                if (!TryTakeControl(original, m))
                {
                    SendCardToGrave(current, m);
                    Log($"{m.Name} is sent to the GY (no Monster Zone to return control).");
                }
            }
        }

        /// <summary>
        /// Relinquished family (Ignis c64631466 EFFECT_DESTROY_SUBSTITUTE, REASON_BATTLE):
        /// if this card would be destroyed by battle, destroy the absorbed Equip instead.
        /// Does not rewrite Damage Step windows or chains.
        /// </summary>
        bool TryDestroyEquipInsteadOfBattle(DuelistState owner, CardInstance card)
        {
            if (card?.IsNegated == true) return false;
            if (card?.Equips == null || card.Equips.Count == 0) return false;
            CardInstance unionEquip = null;
            foreach (var eq in card.Equips)
            {
                if (eq == null) continue;
                var eqDef = eq.Def;
                if (eqDef == null || !eqDef.IsMonster) continue;
                var type = eqDef.type ?? "";
                if (type.IndexOf("Union", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                // UnionRiders batch (v175): every Union in its Union state whose text prints the rule (not only those
                // with an EquippedMonsterDestroysByBattle clause); a Union another card took (Union Rider) never.
                if (TextEffects.UnionRidersRuntime.SubstitutesForBattleDestruction(this, eq))
                {
                    unionEquip = eq;
                    break;
                }
                if (TextEffects.UnionRidersRuntime.UnionTextOff(eq)) continue;
                var prog = CompiledEffectCache.GetOrCompile(eqDef);
                if (prog == null) continue;
                foreach (var c in prog.ClauseList)
                {
                    if (c != null && c.Timing == EffectTiming.EquippedMonsterDestroysByBattle)
                    {
                        unionEquip = eq;
                        break;
                    }
                }
                if (unionEquip != null) break;
            }
            if (unionEquip == null)
            {
                var absorb = false;
                var hostProg = CompiledEffectCache.GetOrCompile(card.Def);
                if (hostProg != null)
                {
                    foreach (var c in hostProg.ClauseList)
                    {
                        if (c != null && c.Action == EffectActionKind.EquipTargetToThis)
                        {
                            absorb = true;
                            break;
                        }
                    }
                }
                if (!absorb) return false;
                unionEquip = TextEffects.TextEffectRuntime.AbsorbedEquipOf(card);
                if (unionEquip == null) return false;
            }
            var eqOwner = ControllerOf(unionEquip) ?? OpponentOf(owner) ?? owner;
            Log($"{unionEquip.Name} is destroyed instead of {card.Name} (battle).");
            CardShatterPresentation.QueueEffect(unionEquip.InstanceId, unionEquip.Name);
            SendCardToGrave(eqOwner, unionEquip);
            return true;
        }

        void ResetEquippedMonsterTurnFlags(DuelistState who)
        {
            if (who?.MonsterZones == null) return;
            foreach (var z in who.MonsterZones)
            {
                if (z.Occupant?.Def == null || !z.Occupant.Def.IsMonster) continue;
                var type = z.Occupant.Def.type ?? "";
                if (type.IndexOf("Union", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                z.Occupant.EffectUsedThisTurn = false;
            }
            foreach (var z in who.SpellTrapZones)
            {
                var eq = z.Occupant;
                if (eq?.Def == null || !eq.Def.IsMonster) continue;
                var type = eq.Def.type ?? "";
                if (type.IndexOf("Union", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (eq.EquippedTo == null) continue;
                eq.EffectUsedThisTurn = false;
            }
        }

        void DestroyMonster(DuelistState owner, CardInstance card, CardInstance byEffect = null,
            bool banishIfDestroyed = false, bool destroyedByBattle = false,
            CardInstance battleDestroyer = null)
        {
            if (owner == null || card == null) return;
            if (destroyedByBattle && TryDestroyEquipInsteadOfBattle(owner, card))
                return;
            if (byEffect != null &&
                ContinuousProtections.IsUnaffectedBy(this, card, byEffect))
            {
                Log($"{card.Name} is unaffected by {byEffect.Name}.");
                return;
            }
            // EquipAndSpellProtection batch: Heart of Clear Water — not destroyed by an effect that targets it.
            if (!destroyedByBattle &&
                TextEffects.EquipAndSpellProtectionRuntime.PreventsTargetedMonsterDestruction(this, card, byEffect))
                return;
            // BattleDestroyedTriggers batch: Different Dimension Dragon — not destroyed by a Spell/Trap effect that does not
            // target it.
            if (!destroyedByBattle &&
                TextEffects.BattleDestroyedTriggersRuntime.PreventsUntargetedSpellTrapDestruction(this, card, byEffect))
                return;
            // NamedSummonPairs batch: Exodia Necross — not destroyed by a Spell/Trap effect.
            if (!destroyedByBattle &&
                TextEffects.NamedSummonPairsRuntime.PreventsSpellTrapEffectDestruction(this, card, byEffect))
                return;

            if (owner.TryFindMonster(card, out var i))
            {
                var wasFaceUp = card.FaceUp;
                // NegationMonsters batch (v161): Lesser Fiend — a monster it destroys by battle is banished instead
                // (ygopro EFFECT_BATTLE_DESTROY_REDIRECT, decided while both battlers were on the field).
                if (destroyedByBattle && !banishIfDestroyed && !card.IsToken &&
                    TextEffects.NegationMonstersRuntime.BanishesBattleDestroyed(this, card, battleDestroyer))
                    banishIfDestroyed = true;
                // Banisher of the Light (RedirectAndReplace batch, v142): destroyed and banished instead of sent to
                // the GY — read while the monster is still on the field (a destroyed Banisher banishes itself). A
                // Token is banished instead too: it leaves play without reaching the GY.
                if (!banishIfDestroyed && TextEffects.RedirectAndReplaceRuntime.SendsToBanishedInstead(this, card))
                {
                    if (card.IsToken)
                        TextEffects.RedirectAndReplaceRuntime.NoteTokenBanishedInstead(this, card);
                    else
                        banishIfDestroyed = true;
                }
                // Effect / non-battle destroy → default shatter (battle already queued)
                CardShatterPresentation.QueueEffect(card.InstanceId, card.Name);
                owner.MonsterZones[i].Occupant = null;
                // A destroyed monster's Equip cards (and destroy-linked Continuous
                // Traps like Call of the Haunted / Battle-Scarred) are sent to the GY.
                UnlinkEquips(card, hostDestroyed: true, fallbackOwner: owner);
                PendingTributes.Remove(card);
                if (card.IsToken)
                {
                    if (card.TokenDestroyedDamage > 0)
                        ApplyEffectDamage(owner, card.TokenDestroyedDamage, card.Name);
                    Log($"Token destroyed: {card.Name}");
                    // CallsDiceAndSetTriggers batch (v163): Cobra Jar's Token, destroyed by battle, burns the bound player.
                    TextEffects.CallsDiceAndSetTriggersRuntime.OnTokenDestroyed(this, card, destroyedByBattle);
                }
                else if (banishIfDestroyed)
                {
                    card.FaceUp = true;
                    var banPile = PileOwner(owner, card);
                    if (!banPile.Banished.Contains(card))
                        banPile.Banished.Add(card);
                    card.BanishedTurnNumber = TurnNumber;
                    Log($"Destroyed: {card.Name} (banished).");
                    // ChaosBanishSummons batch: the banish event (Soul Absorption).
                    TextEffects.BanishEvents.Record(this, card);
                    // Never sent to the GY, but still destroyed: its own "if this card is destroyed (by battle)"
                    // triggers that do not need the GY still activate (Slate Warrior; RedirectAndReplace, v142).
                    TextEffects.RedirectAndReplaceRuntime.ResolveDestroyedTriggersOutsideGy(this, owner, card,
                        destroyedByBattle, battleDestroyer);
                }
                else
                {
                    var gyPile = PileOwner(owner, card);
                    if (!gyPile.Graveyard.Contains(card))
                        gyPile.Graveyard.Add(card);
                    // BattleDestroyedGyRecovery (v162): GY-entry stamp — destroyed by battle (no reason player), or by
                    // a card's effect (that card's controller; TextEffectRuntime.DestroyCard names the resolving player).
                    TextEffects.GyEntryStamps.Entered(this, card,
                        GyEntryReason.Destroyed | (destroyedByBattle ? GyEntryReason.Battle
                            : byEffect != null ? GyEntryReason.Effect : GyEntryReason.None),
                        destroyedByBattle ? null : TextEffects.GyEntryStamps.HolderOf(this, byEffect));
                    Log($"Destroyed: {card.Name}");
                    MarkSentFromFieldToGy(card, byEffect, destroyedByBattle, battleDestroyer);
                    // Michizure (BattleWindows batch): a monster sent from the field to its owner's GY.
                    TextEffects.BattleWindowsRuntime.NoteMonsterSentFromField(this, gyPile, card);
                    // NamedSummonPairs batch: A Deal with Dark Ruler's record (previous controller, Level).
                    TextEffects.NamedSummonPairsRuntime.NoteMonsterSentToGy(this, owner, card);
                    // EquipFamily batch (Dark Necrofear): destroyed in whose Monster Zone, by whose card.
                    TextEffects.EquipFamilyRuntime.NoteDestroyedToGy(this, owner, gyPile, card, byEffect,
                        destroyedByBattle, battleDestroyer);
                    MonsterEffects.OnSentFromFieldToGy(this, owner, card, destroyed: true,
                        destroyedByBattle, battleDestroyer);
                }

                // "If "X" on the field is destroyed, destroy this card." (Toons batch).
                TextEffects.ToonsRuntime.NotifyFaceUpCardDestroyed(this, card, wasFaceUp);
                DuelPresentationPacer.HoldCombatResult();
            }
        }

        /// <summary>
        /// Safety: clear stuck attack/response states that block End Turn / AI.
        /// Call after every command (finally) and before phase advances / UI hang detection.
        /// Pattern: simulator try/finally always returns to a playable open state.
        /// </summary>
        public bool RecoverStuckCombat()
        {
            var fixedAny = false;

            // Empty response window should not hang the duel
            if (PendingResponse != null &&
                (PendingResponse.LegalCards == null || PendingResponse.LegalCards.Count == 0))
            {
                Log("[RULE] Empty response window — auto-pass.");
                PassResponse();
                fixedAny = true;
            }

            // Attack declared but no open window and not resolving — finish damage
            // (Do not auto-resolve while player still has a response or Flip target pending.)
            if (HasDeclaredAttack && !IsAwaitingResponse && !IsAwaitingEffectTarget)
            {
                Log("[RULE] Completing stuck attack declaration…");
                ResolveDeclaredAttack();
                fixedAny = true;
            }

            // Damage calculation was deferred (Kuriboh window) but response vanished — finish it
            // or ATK < DEF battle damage never applies.
            if (_deferredDamage != null && !IsAwaitingResponse && !IsAwaitingEffectTarget)
            {
                Log("[RULE] Completing stuck damage calculation…");
                FinishDeferredDamageCalculation();
                fixedAny = true;
            }

            // Flip target already cleared but battle GY still deferred
            if (_deferredBattle != null && !IsAwaitingEffectTarget)
            {
                TryCompleteDeferredBattleDestruction();
                fixedAny = true;
            }

            // BattleWindows batch: an end-of-Damage-Step window that closed without ending the Damage Step.
            if (TextEffects.BattleWindowsRuntime.EndOfDamageStepOpen(this) && !IsAwaitingResponse &&
                !IsAwaitingEffectTarget && !Chain.HasLinks && !IsResolvingChain)
            {
                Log("[RULE] Ending a Damage Step left at its end-of-Damage-Step window…");
                FinishEndOfDamageStep();
                fixedAny = true;
            }

            // A summon-negation window that closed without finishing its Summon (SummonNegation batch, v141).
            if (_summonInProgress != null && !IsAwaitingResponse && !IsAwaitingEffectTarget &&
                !Chain.HasLinks && !IsResolvingChain)
            {
                Log("[RULE] Completing a Summon left in its negation window…");
                FinishSummonNegationWindow();
                fixedAny = true;
            }

            // A cost paid down to 0 LP while a window was open ends the duel once it closed.
            SettlePendingLpLoss();
            return fixedAny;
        }

        /// <summary>End turn with stuck-state recovery so the AI always receives the turn.</summary>
        public bool TryEndTurnSafe(DuelistState who)
        {
            RecoverStuckCombat();
            if (GameOver || TurnPlayer != who) return false;
            if (IsAwaitingResponse || IsAwaitingEffectTarget || HasDeclaredAttack)
            {
                RecoverStuckCombat();
                if (IsAwaitingResponse || IsAwaitingEffectTarget || HasDeclaredAttack)
                    return false;
            }

            EndTurn(who);
            return true;
        }

        /// <summary>
        /// Effect damage (not battle) — Ring of Destruction, etc. <paramref name="source"/>: the card whose effect inflicts
        /// it, when the caller knows it. EffectDamageEvents batch (v162): the central effect-damage event — reflect / negate
        /// modifiers of the resolving Chain Link apply first, the after-damage event follows the LP change.
        /// </summary>
        public void ApplyEffectDamage(DuelistState target, int dmg, string sourceName = null, CardInstance source = null)
        {
            if (dmg <= 0 || target == null) return;
            TextEffects.EffectDamageEventsRuntime.ModifyEffectDamage(this, ref target, ref dmg, sourceName, source);
            if (dmg <= 0 || target == null) return;
            if (target.NoDamageThroughTurnNumber > 0 && TurnNumber <= target.NoDamageThroughTurnNumber)
            {
                Log($"{target.Name} takes no effect damage (Crush Card Virus).");
                return;
            }
            target.LifePoints = Mathf.Max(0, target.LifePoints - dmg);
            var src = string.IsNullOrEmpty(sourceName) ? "effect" : sourceName;
            var you = target.IsPlayer ? "You took" : $"{target.Name} took";
            Log($"💥 {you} {dmg} effect damage from {src} ({target.LifePoints} LP remaining).");
            DuelPresentationPacer.HoldCombatResult();
            TextEffects.EffectDamageEventsRuntime.AfterEffectDamage(this, target, dmg, sourceName, source);
            OfferYouTakeDamageWindow(target);
            if (!IsAwaitingResponse)
                CheckLpWin(Player, Opponent);
            else if (target.LifePoints <= 0)
                _lpLossPending = true; // burn inside an open window: checked when it closes
        }

        /// <summary>
        /// Effect damage to both players at the same time (Ring of Destruction). Both amounts land before
        /// the LP check, so two players reaching 0 together is a Draw, not a loss for whoever was
        /// damaged first (DR-06).
        /// </summary>
        public void ApplyEffectDamageBoth(DuelistState first, int firstDamage, DuelistState second,
            int secondDamage, string sourceName = null, CardInstance source = null)
        {
            var src = string.IsNullOrEmpty(sourceName) ? "effect" : sourceName;
            // EffectDamageEvents batch (v162): each damage is reflected / negated on its own (Barrel Behind the Door on
            // Ring of Destruction: the opponent takes both).
            TextEffects.EffectDamageEventsRuntime.ModifyEffectDamage(this, ref first, ref firstDamage, sourceName, source);
            TextEffects.EffectDamageEventsRuntime.ModifyEffectDamage(this, ref second, ref secondDamage, sourceName, source);
            var hitFirst = TakeSimultaneousEffectDamage(first, firstDamage, src);
            var hitSecond = TakeSimultaneousEffectDamage(second, secondDamage, src);
            if (!hitFirst && !hitSecond) return;
            DuelPresentationPacer.HoldCombatResult();
            if (hitFirst)
                TextEffects.EffectDamageEventsRuntime.AfterEffectDamage(this, first, firstDamage, sourceName, source);
            if (hitSecond)
                TextEffects.EffectDamageEventsRuntime.AfterEffectDamage(this, second, secondDamage, sourceName, source);
            if (hitFirst) OfferYouTakeDamageWindow(first);
            if (hitSecond && !(hitFirst && ReferenceEquals(first, second))) OfferYouTakeDamageWindow(second);
            if (!IsAwaitingResponse)
                CheckLpWin(Player, Opponent);
            else if ((first != null && first.LifePoints <= 0) || (second != null && second.LifePoints <= 0))
                _lpLossPending = true; // inside an open window: checked when it closes
        }

        bool TakeSimultaneousEffectDamage(DuelistState target, int dmg, string src)
        {
            if (dmg <= 0 || target == null) return false;
            if (target.NoDamageThroughTurnNumber > 0 && TurnNumber <= target.NoDamageThroughTurnNumber)
            {
                Log($"{target.Name} takes no effect damage (Crush Card Virus).");
                return false;
            }

            target.LifePoints = Mathf.Max(0, target.LifePoints - dmg);
            var you = target.IsPlayer ? "You took" : $"{target.Name} took";
            Log($"💥 {you} {dmg} effect damage from {src} ({target.LifePoints} LP remaining).");
            return true;
        }

        /// <summary>
        /// Deduct a mandatory Life Point cost (floored at 0) and check for a loss.
        /// Used for non-optional upkeep such as the Archfiend Standby maintenance cost;
        /// unlike <see cref="ApplyEffectDamage"/> this is a cost, not battle/effect damage.
        /// </summary>
        public void PayLifePointCost(DuelistState who, int amount, string reason = null)
        {
            if (who == null || amount <= 0) return;
            var paid = Mathf.Min(amount, who.LifePoints);
            TextEffects.ContinuousTrapsAndSpiritSupportRuntime.NoteLpCostPaid(this, who, paid);
            who.LifePoints -= paid;
            var tag = string.IsNullOrEmpty(reason) ? "cost" : reason;
            Log($"{who.Name} pays {paid} LP ({tag}) → {who.LifePoints} LP.");
            if (!IsAwaitingResponse)
                CheckLpWin(Player, Opponent);
        }

        /// <summary>Create a card instance from the DB (Extra Deck Fusion, etc.).</summary>
        public CardInstance CreateCardInstance(int cardId) => CreateInstance(cardId);

        public CardDatabase Database => _db;

        /// <summary>
        /// Lord of D. continuous: neither player can target Dragon monsters on the field with card effects.
        /// </summary>
        public bool IsDragonTargetProtected(CardInstance monster) =>
            ContinuousProtections.CannotBeTargetedByEffects(this, monster);

        static bool HasMonsters(DuelistState who) => who.MonsterCount > 0;

        /// <summary>
        /// Exodia / named hand-win / 0 LP from a cost: public so card code can declare a winner.
        /// A null winner is a Draw.
        /// </summary>
        public void EndGamePublic(DuelistState winner) =>
            EndGame(winner, winner == null ? DuelEndReason.Draw : DuelEndReason.CardEffect);

        /// <summary>
        /// Forfeit: <paramref name="who"/> loses now, through the same game-over path as a 0 LP loss, so
        /// <see cref="OnGameOver"/> fires once and rewards, penalties, streaks and stakes settle exactly once.
        /// Open response windows, target picks, the chain and any declared attack are abandoned.
        /// Returns false (and changes nothing) when the duel is already over or <paramref name="who"/>
        /// is not a seat in this duel.
        /// </summary>
        public bool Concede(DuelistState who, string reason)
        {
            if (GameOver || who == null || (who != Player && who != Opponent)) return false;
            var winner = OpponentOf(who);
            PendingActivation = null;
            PendingResponse = null;
            ClearPendingSpecialSummonPlacement();
            PendingTributes.Clear();
            _deferredBattle = null;
            _deferredDamage = null;
            _endTurnPausedFor = null;
            _standbyPausedFor = null;
            _queuedSummons.Clear();
            _queuedDamageTaken = null;
            _queuedOwnSummonResponder = null;
            _queuedOwnSummoner = null;
            _queuedOwnSummoned = null;
            _lpLossPending = false;
            _summonInProgress = null;
            _procedureSummonNegatable = false;
            _chainPausedForWaiver = null;
            _chainHeldForChoice = false;
            _holdMaterialGyTriggers = false;
            _heldMaterialGyTriggers.Clear();
            ResumeAfterChain = ResponseTiming.None;
            ChainNegatedAttack = false;
            ChainEndedBattle = false;
            IsResolvingChain = false;
            DeferSpellTrapResolution = false;
            LpZeroCostConfirmed = false;
            Chain.Clear();
            FastEffects.Close();
            if (HasDeclaredAttack) ClearDeclaredAttack();
            ClearPresentation();
            ConcededBy = who;
            var why = string.IsNullOrWhiteSpace(reason) ? "forfeit" : reason.Trim();
            Log($"{who.Name} concedes the duel ({why}).");
            EndGame(winner, DuelEndReason.Forfeit);
            return true;
        }

        void EndGame(DuelistState winner, DuelEndReason reason = DuelEndReason.LifePoints)
        {
            if (GameOver) return;
            GameOver = true;
            Winner = winner;
            EndReason = winner == null ? DuelEndReason.Draw : reason;
            _lpLossPending = false;
            Phase = DuelPhase.GameOver;
            Log(GameOverLine());
            // Programs learned during the duel are written once here, not on every cache miss (PERF-07).
            CompiledEffectCache.Flush();
            OnGameOver?.Invoke();
            Notify();
            // Outside a live duel, compiles go back to the host's ambient era.
            if (_configProvided && ReferenceEquals(DuelMatchContext.Current, _config))
                DuelMatchContext.Clear();
        }

        string GameOverLine()
        {
            if (EndReason == DuelEndReason.Draw)
                return _drawByCardEffect
                    ? "★ DRAW — a card effect ended the duel with no winner."
                    : "★ DRAW — both players met a win condition at the same time.";
            var youWin = Winner != null && Winner.IsPlayer;
            switch (EndReason)
            {
                case DuelEndReason.Forfeit:
                    return youWin
                        ? $"★ YOU WIN — {ConcededBy?.Name ?? "your opponent"} conceded."
                        : "★ YOU LOSE — you conceded the duel.";
                case DuelEndReason.DeckOut:
                    return youWin
                        ? "★ YOU WIN — your opponent could not draw."
                        : "★ YOU LOSE — you could not draw.";
                case DuelEndReason.CardEffect:
                    return youWin
                        ? "★ YOU WIN — a card effect decided the duel."
                        : "★ YOU LOSE — a card effect decided the duel.";
                default:
                    return youWin
                        ? "★ YOU WIN — opponent’s LP is 0 or they could not draw."
                        : "★ YOU LOSE — your LP is 0 or you could not draw.";
            }
        }

        public void Log(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            // Drop known fluff before UI / review see it
            if (DuelReviewLog.IsNoise(msg)) return;
            if (MirrorToUnityLog)
                Diagnostics.Info("[Duel] " + msg);
            // ReviewLog also listens to OnLog; fire event for UI + structured capture
            OnLog?.Invoke(msg);
        }

        /// <summary>Explicit structured review note (optional; most lines go through <see cref="Log"/>).</summary>
        public void LogReview(DuelLogKind kind, string actor, string message)
        {
            // Do not re-fire OnLog (ReviewLog already captures via Record).
            ReviewLog?.Record(kind, actor, message);
            if (MirrorToUnityLog)
                Diagnostics.Info("[Duel/Review] " + message);
        }

        void Notify()
        {
            SettlePendingLpLoss();
            if (!Ocg.OcgLabDuelHost.IsActive)
            {
                FieldSpellEffects.RefreshBoard(this);
                // FlipCostsAndFusion batch: a Fusion Material that left the GY is no longer "sent as Fusion Material".
                TextEffects.FlipCostsAndFusionRuntime.SweepFusionMaterialStamps(this);
                // DestinyBoard batch: its leave-field send, then its field win (Lua EVENT_ADJUST).
                if (!GameOver && TextEffects.DestinyBoardRuntime.OnBoardSettled(this))
                    return; // EndGame already notified.
                // StandbyBurnsAndDice batch (v162): Mask of Dispel's leave-field destruction; per-presence amounts.
                TextEffects.StandbyBurnsAndDiceRuntime.OnBoardSettled(this);
                // Exodia family: a hand win condition is checked whenever the state changes.
                if (!GameOver && TextEffects.TextEffectRuntime.CheckHandWinConditions(this))
                    return; // EndGame already notified.
                // TriggerChainResponses batch (v163): a trigger link's pick was answered — the rest of its Chain resolves.
                TextEffects.TriggerChainResponsesRuntime.Flush(this);
                // FLIP effects of monsters Swords of Revealing Light flipped face-up activate once it has resolved.
                FlushFlipEffectsAfterResolution();
                // Gust / Driving Snow: a Spell/Trap destroyed by an opponent's effect gets its window once idle.
                OfferSpellTrapDestroyedWindow();
                // Michizure (BattleWindows batch): a monster sent from the field to the GY gets its window once idle.
                OfferMonsterSentWindow();
                // Armored Glass (EquipModel batch): a monster equipped with an Equip Card gets its window once idle.
                OfferMonsterEquippedWindow();
                // EngineHygiene batch: Forced Requisition's queued discard triggers, then its activation window.
                if (!TextEffects.SimultaneousTriggersRuntime.Busy(this) && PlayIdleForDeferredEvents())
                    TextEffects.DiscardEvents.FireQueuedTriggers(this);
                // SimpleTriggersAndSwarm batch: "When this card is sent to the GY" (from anywhere) once play is idle.
                if (!GameOver && !TextEffects.SimultaneousTriggersRuntime.Busy(this) && PlayIdleForDeferredEvents())
                    TextEffects.SimpleTriggersAndSwarmRuntime.FireQueuedSentToGy(this);
                // ChaosBanishSummons batch: Chaosrider Gustaph's ATK ends when it leaves the field or is negated; Soul
                // Absorption's queued banish triggers activate once play is idle.
                TextEffects.ChaosBanishSummonsRuntime.OnBoardSettled(this);
                if (!GameOver && !TextEffects.SimultaneousTriggersRuntime.Busy(this) && PlayIdleForDeferredEvents())
                    TextEffects.BanishEvents.FireQueuedTriggers(this);
                OfferDiscardedWindow();
                // LastTurn batch (v163): a forced battle starts once the Chain and the triggers it left are over, and hands
                // the duel back to the phase it interrupted once it is over.
                if (!GameOver && !TextEffects.SimultaneousTriggersRuntime.Busy(this) && PlayIdleForTriggerChain())
                    TextEffects.LastTurnRuntime.OnPlayIdle(this);
                // SummonPromptsAndCosts batch (v163): a queued end-of-Battle-Phase Special Summon offer (Vampire Baby) opens
                // once play is idle; a turn held for it ends after it.
                TextEffects.SummonPromptsAndCostsRuntime.OnNotify(this);
                // Major Riot (ForcedActivationAndSubstitution, v163): monsters an opponent's effect returned to a hand.
                OfferMonstersReturnedWindow();
            }
            LastRulesSnapshot = GameStateSnapshotBuilder.Capture(this);
            OnStateChanged?.Invoke();
            // HandDisruption batch (v142): an Appropriate window queued by a draw opens once play is stable again.
            if (!Ocg.OcgLabDuelHost.IsActive)
                TextEffects.HandDisruptionRuntime.FlushDrawWindow(this);
        }

        /// <summary>Rules-legal multiplayer snapshot (identical on both peers).</summary>
        public RulesGameStateSnapshot CaptureRulesState() => GameStateSnapshotBuilder.Capture(this);

        public string StatusLine()
        {
            if (Player == null || Opponent == null)
                return "Loading duel…";

            if (GameOver)
                return Winner == null ? "DRAW" : Winner.IsPlayer ? "YOU WIN" : "YOU LOSE";

            var whose = TurnPlayer != null && TurnPlayer.IsPlayer ? "YOUR TURN" : "OPP TURN";
            var battleLock = TurnNumber == 1 && TurnPlayer == FirstPlayer ? " · NO BATTLE" : "";
            var step = Phase == DuelPhase.Battle && BattleStep != BattleStep.None
                ? $"/{BattleStep}"
                : "";
            var chain = Chain.HasLinks ? $" · Chain {Chain.Count}" : "";
            return $"{whose} · Game#{TurnNumber} · {Phase}{step}{battleLock}{chain} · You {Player.LifePoints} · AI {Opponent.LifePoints}";
        }

        public string HintLine()
        {
            if (Player == null || Opponent == null)
                return "Starting duel…";

            if (GameOver)
                return Winner != null && Winner.IsPlayer
                    ? "Victory! Restart Duel for a rematch."
                    : "Defeat. Restart Duel to try again.";

            // Flip / damage read holds take priority so the player knows why the duel is pausing
            if (DuelPresentationPacer.IsHolding && !string.IsNullOrEmpty(DuelPresentationPacer.Reason))
                return DuelPresentationPacer.Reason;

            if (PendingActivation != null)
                return PendingActivation.Prompt + "  (or Cancel Target)";

            if (PendingResponse != null)
            {
                if (PendingResponse.Responder != null && PendingResponse.Responder.IsPlayer)
                {
                    var left = ActivePresentation?.SecondsToImpact ?? PendingResponse.ReactionSeconds;
                    // The summon-negation window (Solemn Judgment / Horn of Heaven) is a Summon window too.
                    var summon = PendingResponse.Timing == ResponseTiming.MonsterSummoned ||
                                 PendingResponse.Timing == ResponseTiming.SummonNegation;
                    return summon
                        ? $"Summon {left:0.0}s — tap a blinking zone to activate"
                        : $"Attack {left:0.0}s — tap a blinking zone to activate";
                }

                return "Opponent may respond…";
            }

            if (TurnPlayer != Player)
                return "Opponent’s turn…";

            switch (Phase)
            {
                case DuelPhase.Main1:
                    return "Main 1: Summon/Set · Activate Spell · Set S/T · Battle / End";
                case DuelPhase.Battle:
                    return "Battle: declare attack (opponent may respond with Traps) · Main 2 / End";
                case DuelPhase.Main2:
                    return "Main 2: Summon/Activate if still available · End Turn";
                default:
                    return "";
            }
        }

        public string FieldSummary()
        {
            if (Player == null || Opponent == null)
                return "Duel not started yet.";

            var sb = new StringBuilder();
            sb.AppendLine($"AI  {Opponent.LifePoints} LP   Hand {Opponent.HandCount}   Deck {Opponent.DeckCount}");
            sb.Append("AI M: ");
            AppendMonsters(sb, Opponent);
            sb.AppendLine();
            sb.Append("AI S/T: ");
            AppendSpellTraps(sb, Opponent);
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("You M: ");
            AppendMonsters(sb, Player);
            sb.AppendLine();
            sb.Append("You S/T: ");
            AppendSpellTraps(sb, Player);
            sb.AppendLine();
            sb.Append($"You {Player.LifePoints} LP   Hand {Player.HandCount}   Deck {Player.DeckCount}");
            if (Player.WabokuActive) sb.Append("   [Waboku]");
            if (PendingTributes.Count > 0)
                sb.Append($"\nTributes selected: {PendingTributes.Count}");
            if (PendingActivation != null)
            {
                sb.Append($"\n★ TARGETING: {PendingActivation.Card?.Name} — {PendingActivation.LegalTargets.Count} legal");
                sb.Append("\nGY: ");
                AppendGy(sb, Player, "You");
                sb.Append(" | ");
                AppendGy(sb, Opponent, "AI");
            }

            if (TurnNumber == 1 && TurnPlayer == FirstPlayer && !GameOver)
                sb.Append("\n(First turn — no Battle Phase)");
            return sb.ToString();
        }

        static void AppendGy(StringBuilder sb, DuelistState who, string label)
        {
            sb.Append(label).Append('[');
            var any = false;
            foreach (var c in who.Graveyard)
            {
                if (!SpellTrapEffects.IsLegalGyMonster(c)) continue;
                if (any) sb.Append(", ");
                sb.Append(c.Name);
                any = true;
            }

            if (!any) sb.Append("—");
            sb.Append(']');
        }

        static void AppendMonsters(StringBuilder sb, DuelistState who)
        {
            var any = false;
            foreach (var z in who.MonsterZones)
            {
                if (z.Occupant == null) continue;
                any = true;
                var m = z.Occupant;
                var pos = m.Position == BattlePosition.Attack ? "ATK" : "DEF";
                var face = m.FaceUp ? m.Name : "Set";
                var stat = m.FaceUp
                    ? (m.Position == BattlePosition.Attack ? m.CurrentAtk.ToString() : m.CurrentDef.ToString())
                    : "?";
                sb.Append($"[{face} {pos} {stat}] ");
            }

            if (!any) sb.Append("(empty)");
        }

        static void AppendSpellTraps(StringBuilder sb, DuelistState who)
        {
            var any = false;
            foreach (var z in who.SpellTrapZones)
            {
                if (z.Occupant == null) continue;
                any = true;
                var c = z.Occupant;
                sb.Append(c.FaceUp ? $"[{c.Name}] " : "[Set S/T] ");
            }

            if (!any) sb.Append("(empty)");
        }
    }
}
