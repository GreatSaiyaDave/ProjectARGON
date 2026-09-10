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

        /// <summary>Coin toss / die rolls. Tests queue faces for determinism.</summary>
        public DuelRng Rng { get; } = new();

        /// <summary>Last rules snapshot for multiplayer sync.</summary>
        public RulesGameStateSnapshot LastRulesSnapshot { get; private set; }

        /// <summary>Player always goes first in this slice (dice-roll later).</summary>
        public DuelistState FirstPlayer => Player;

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
        /// End Phase trigger (Ectoplasmer tribute) paused the turn switch until the
        /// turn player picks. Null when End Phase is not waiting on a trigger pick.
        /// </summary>
        DuelistState _endTurnPausedFor;

        /// <summary>Open response window (attack declared / summon) for Speed 2 activations.</summary>
        public PendingResponse PendingResponse { get; private set; }

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
        readonly List<(DuelistState summoner, CardInstance summoned)> _queuedSummons = new();
        DuelistState _queuedOwnSummonResponder;
        DuelistState _queuedOwnSummoner;
        CardInstance _queuedOwnSummoned;
        /// <summary>Human must choose Pass / Activate (P1, or either side in hotseat PvP).</summary>
        public bool IsAwaitingPlayerResponse =>
            PendingResponse != null && IsHumanControlled(PendingResponse.Responder);

        /// <summary>
        /// True while <see cref="DuelCommandService"/> is executing one intent.
        /// Prevents re-entrant / double-tap actions (Yugioh Shorts server action lock pattern).
        /// </summary>
        public bool IsProcessingAction { get; internal set; }

        /// <summary>Any modal engine state that blocks free actions.</summary>
        public bool IsBusy =>
            IsProcessingAction ||
            IsAwaitingEffectTarget || IsAwaitingResponse ||
            (HasDeclaredAttack && IsAwaitingResponse) ||
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
        }

        DeferredDamageCalculation _deferredDamage;

        public void SetPendingActivation(PendingActivation pending) => PendingActivation = pending;
        public void ClearPendingActivation() => PendingActivation = null;
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
            bool cinematicOpening)
        {
            _db = db;
            _playerDeckFile = playerDeck;
            _aiDeckFile = aiDeck;
            _nextInstanceId = 1;
            PendingTributes.Clear();
            PendingActivation = null;
            _endTurnPausedFor = null;
            PendingResponse = null;
            _deferredBattle = null;
            _deferredDamage = null;
            IsProcessingAction = false;
            OpeningSequenceActive = false;
            AwaitingOpeningDrawGesture = false;
            ClearDeclaredAttack();
            ClearPresentation();

            // Fresh review session each duel (AI learning / visible log)
            ReviewLog?.Unbind();
            ReviewLog = new DuelReviewLog();
            ReviewLog.Bind(this);

            var p1Name = HumanVsHuman ? "Player 1" : (playerDeck?.name ?? "You");
            var p2Name = HumanVsHuman ? "Player 2" : (aiDeck?.name ?? "Opponent");
            Player = new DuelistState(p1Name, true)
            {
                LifePoints = TcgRules.StartingLifePoints
            };
            Opponent = new DuelistState(p2Name, false)
            {
                LifePoints = TcgRules.StartingLifePoints
            };

            Player.Deck = FilterPlayable(CardDatabase.BuildMainPile(playerDeck));
            Opponent.Deck = FilterPlayable(CardDatabase.BuildMainPile(aiDeck));
            Player.ExtraDeck = BuildExtraPile(playerDeck);
            Opponent.ExtraDeck = BuildExtraPile(aiDeck);
            Player.Banished = new List<CardInstance>();
            Opponent.Banished = new List<CardInstance>();
            CardDatabase.Shuffle(Player.Deck);
            CardDatabase.Shuffle(Opponent.Deck);

            TurnNumber = 0;
            GameOver = false;
            Winner = null;
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
            BeginTurn(Player);
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
                    BeginTurn(Player);
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
                BeginTurn(Player);
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
            StartDuel(_db, _playerDeckFile, _aiDeckFile, cinematicOpening: true);
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
            who.NormalSummonUsed = false;
            who.WabokuActive = false;
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
            }

            // —— Draw Phase ——
            Phase = DuelPhase.Draw;
            var side = who.IsPlayer ? "YOUR" : "OPPONENT";
            Log($"════ {side} TURN (game turn #{TurnNumber}) — {who.Name} ════");
            var firstPlayerFirstTurn = TurnNumber == 1 && who == FirstPlayer;
            if (firstPlayerFirstTurn)
                Log($"[{who.Name}] Draw Phase skipped (going first).");
            else if (who.SkipNextDrawPhase)
            {
                who.SkipNextDrawPhase = false;
                Log($"[{who.Name}] Draw Phase skipped (card effect).");
            }
            else
                Draw(who, 1, silent: false);

            if (GameOver) return;

            // —— Standby Phase ——
            Phase = DuelPhase.Standby;
            BattleStep = BattleStep.None;
            DamageSubStep = DamageSubStep.None;
            Continuous.TickStandby(who);
            TextEffects.TextEffectRuntime.FirePhaseTriggers(this, who, TextEffects.EffectTiming.StandbyPhase);
            if (GameOver) return;

            // —— Main Phase 1 ——
            Phase = DuelPhase.Main1;
            FastEffects.Open(FastEffectTiming.WindowKind.OpenGameState, who, OpponentOf(who),
                ChainEvent.PhaseStart);
            if (firstPlayerFirstTurn)
                Log($"[{who.Name}] Main Phase 1 (no Battle this turn — going first).");
            else
                Log($"[{who.Name}] Main Phase 1");
            OfferOpponentTurnTrapWindow(OpponentOf(who));
        }

        public void Draw(DuelistState who, int n, bool silent = false)
        {
            for (var i = 0; i < n; i++)
            {
                if (who.Deck.Count == 0)
                {
                    Log($"{who.Name} cannot draw — deck out! (Victory Condition)");
                    EndGame(OpponentOf(who));
                    return;
                }

                var id = who.Deck[0];
                who.Deck.RemoveAt(0);
                var inst = CreateInstance(id);
                who.Hand.Add(inst);
                if (!silent)
                {
                    if (who.IsPlayer)
                        Log($"[{who.Name}] draws: {inst.Name}");
                    else
                        Log($"[{who.Name}] draws 1 card (hand {who.HandCount}, deck {who.DeckCount}).");
                }
            }
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
            return inst;
        }

        public DuelistState OpponentOf(DuelistState who) => who == Player ? Opponent : Player;

        bool IsMainActionWindow(DuelistState who) =>
            !GameOver && TurnPlayer == who && InMainPhase;

        // ───────────────────── Tribute selection ─────────────────────

        public void ToggleTribute(DuelistState who, CardInstance monster)
        {
            if (!IsMainActionWindow(who) || monster == null) return;
            // Face-down Defense Sets are legal tributes (Rulebook — you control them).
            if (!TcgRules.CanBeTributedForSummon(who, monster)) return;

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
        List<CardInstance> ResolveTributes(DuelistState who, int needed)
        {
            if (needed <= 0) return new List<CardInstance>();
            if (who == null) return null;

            var chosen = PendingTributes
                .Where(t => TcgRules.CanBeTributedForSummon(who, t))
                .Distinct()
                .ToList();
            if (chosen.Count >= needed)
                return chosen.Take(needed).ToList();

            var remaining = who.MonstersOnField()
                .Where(m => m != null && !chosen.Contains(m))
                .ToList();
            var stillNeed = needed - chosen.Count;

            // Unambiguous: every remaining monster must be tributed (e.g. 2 Sets for Dark Magician).
            if (remaining.Count == stillNeed)
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
                if (chosen.Count >= needed) break;
                chosen.Add(m);
            }

            return chosen.Count >= needed ? chosen.Take(needed).ToList() : null;
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

                tributes = ResolveTributes(who, need);
                if (tributes == null)
                {
                    Log(
                        $"Need {need} Tribute(s) for {card.Name} (Lv{card.Level}). " +
                        "Tap face-up or face-down monsters you control, then Summon / Set again.");
                    return false;
                }
            }

            if (tributes != null)
            {
                foreach (var t in tributes)
                {
                    Log($"Tributed: {t.Name}");
                    // Use public path so Sangan / field→GY triggers fire
                    SendCardToGrave(who, t);
                }
            }

            var idx = preferredZone;
            if (idx < 0 || idx >= who.MonsterZones.Length || !who.MonsterZones[idx].IsEmpty)
                idx = FirstEmpty(who.MonsterZones);
            if (idx < 0)
            {
                Log("No free Monster Zone.");
                return false;
            }

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

            who.MonsterZones[idx].Occupant = card;
            card.WasSpecialSummoned = false;
            card.WasTributeSummoned = need > 0 && !asSet;
            who.NormalSummonUsed = true;
            PendingTributes.Clear();
            if (!who.IsPlayer)
                DuelPresentationPacer.HoldOpponentSummon(card.Name, asSet);
            Notify();

            if (!asSet)
                TextEffects.TextEffectRuntime.TryResolveThisCardSummoned(this, who, card);

            // Face-up NS/FS/SS and Normal Set: Torrential-family can answer a Set.
            OpenSummonResponseOrContinue(who, card);

            return true;
        }

        // ───────────────────── Flip Summon ─────────────────────

        public bool CanFlipSummon(DuelistState who, CardInstance monster)
        {
            if (!IsMainActionWindow(who) || monster == null) return false;
            if (!who.TryFindMonster(monster, out _)) return false;
            // Face-down Defense → face-up Attack
            if (monster.FaceUp || monster.Position != BattlePosition.Defense) return false;
            if (monster.SetThisTurn) return false; // cannot Flip Summon same turn it was Set
            return true;
        }

        public bool TryFlipSummon(DuelistState who, CardInstance monster)
        {
            if (!CanFlipSummon(who, monster)) return false;
            monster.FaceUp = true;
            monster.Position = BattlePosition.Attack;
            monster.SummonedThisTurn = true;
            monster.ChangedPositionThisTurn = true;
            Log(who.IsPlayer
                ? $"Flip Summon {monster.Name}!"
                : $"Opponent Flip Summons {monster.Name}.");
            // Official Flip effects (Man-Eater Bug, Magician of Faith, Cyber Jar, …)
            MonsterEffects.OnFlipSummoned(this, who, monster);
            TextEffects.TextEffectRuntime.TryResolveThisCardSummoned(this, who, monster, flipSummon: true);
            // If Flip opened a target window, do not open summon-response yet
            if (PendingActivation == null)
            {
                Notify();
                OpenSummonResponseOrContinue(who, monster);
            }
            else
                Notify();
            return true;
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
            return true;
        }

        public bool TryChangePosition(DuelistState who, CardInstance monster)
        {
            if (!CanChangePosition(who, monster)) return false;
            monster.Position = monster.Position == BattlePosition.Attack
                ? BattlePosition.Defense
                : BattlePosition.Attack;
            monster.ChangedPositionThisTurn = true;
            Log(who.IsPlayer
                ? $"{monster.Name} → {monster.Position} Position"
                : $"Opponent changes {monster.Name} to {monster.Position}.");
            Notify();
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

            who.Hand.Remove(card);
            card.FaceUp = false;
            card.SetThisTurn = true;
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
            if (!OfficialEffectRegistry.CanActivateOfficial(this, who, card, fromHand, out var reason))
            {
                Log($"Activation illegal: {reason}");
                return false;
            }

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
                var placed = TryPlaceFieldSpell(who, card);
                if (!placed)
                    Chain.Clear();
                else
                {
                    TextEffects.TextEffectRuntime.NotifySpellResolved(this, who);
                    if (!Chain.IsResolving && Chain.Count <= 1)
                        Chain.Clear();
                }
                return placed;
            }

            var autoPick = who == null || !who.IsPlayer;
            var ok = SpellTrapEffects.BeginOrResolveManual(this, who, card, fromHand, autoPick);
            if (ok)
            {
                // After Speed 1 Normal Spell resolves immediately if no chain response in slice:
                // full chain multi-pass is progressive; for CL1 Speed1 with no chain, clear chain
                if (!Chain.IsResolving && Chain.Count <= 1)
                    Chain.Clear();
            }
            else
                Chain.Clear();

            return ok;
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
            var ok = SpellTrapEffects.TryResolveWithTarget(this, legal);
            if (ok)
                TryCompleteDeferredBattleDestruction();
            return ok;
        }

        public bool CancelEffectTargeting()
        {
            var ok = SpellTrapEffects.CancelPending(this);
            // Flip target cancelled mid-Damage-Step — still finish battle destructions
            if (ok || PendingActivation == null)
                TryCompleteDeferredBattleDestruction();
            return ok;
        }

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

        public int FirstEmptySpellTrap(DuelistState who) => FirstEmpty(who.SpellTrapZones);

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

        /// <summary>Attacker and attack target of the current battle (declaration or Damage Step).</summary>
        public bool TryGetBattlingPair(out CardInstance attacker, out CardInstance target)
        {
            attacker = _deferredDamage?.Attacker ?? PendingResponse?.Attacker ?? DeclaredAttacker;
            target = _deferredDamage?.TargetOrNull ?? PendingResponse?.AttackTarget ?? DeclaredAttackTarget;
            return attacker != null;
        }

        public bool TrySelectLpCost(int amount)
        {
            if (PendingActivation == null || !PendingActivation.AwaitingLpCost)
                return false;
            if (TextEffects.TextEffectRuntime.TryFinishLpCost(this, amount))
                return true;
            return YgoProTriggerCatalog.FinishPayLp(this, amount);
        }

        public void BanishCard(DuelistState owner, CardInstance card)
        {
            if (owner == null || card == null) return;
            DetachFromField(owner, card);
            owner.Hand?.Remove(card);
            owner.Graveyard?.Remove(card);
            card.FaceUp = true;
            if (!owner.Banished.Contains(card))
                owner.Banished.Add(card);
            Log($"{card.Name} is banished.");
        }

        public int BanishCopiesFromHandAndDeck(DuelistState who, int cardId)
        {
            if (who == null || cardId <= 0) return 0;
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
                    if (!who.Banished.Contains(inst))
                        who.Banished.Add(inst);
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
                    if (!who.Graveyard.Contains(c))
                        who.Graveyard.Add(c);
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
                    if (!who.Graveyard.Contains(inst))
                        who.Graveyard.Add(inst);
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

        public bool TryTakeControl(DuelistState newController, CardInstance card)
        {
            if (newController == null || card == null) return false;
            var old = ControllerOf(card);
            if (old == null || old == newController) return false;
            var dest = FirstEmpty(newController.MonsterZones);
            if (dest < 0) return false;
            DetachFromField(old, card);
            newController.MonsterZones[dest].Occupant = card;
            Log($"{newController.Name} takes control of {card.Name}.");
            return true;
        }

        public CardInstance CreateToken(string name, string race, string attribute, int level,
            int atk, int def)
        {
            var defn = new WRLDZ.Data.CardDef
            {
                id = -Math.Abs((name ?? "Token").GetHashCode()),
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
                }
            }
        }

        public void SendCardToGrave(DuelistState owner, CardInstance card, CardInstance sentBy = null)
        {
            if (owner == null || card == null) return;
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

            owner.Hand.Remove(card);
            if (!owner.Graveyard.Contains(card))
                owner.Graveyard.Add(card);
            PendingTributes.Remove(card);

            if (card.EquippedTo != null)
            {
                var host = card.EquippedTo;
                host.Equips.Remove(card);
                card.EquippedTo = null;
                RevertEquipTakeControl(owner, card, host);
                if (fromFieldSt)
                {
                    var linkProg = TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
                    if (linkProg != null &&
                        linkProg.ClauseList.Exists(c => c != null && c.DestroyHostWhenThisLeaves))
                    {
                        var hostOwner = ControllerOf(host) ?? owner;
                        SendCardToGrave(hostOwner, host);
                    }
                }
            }

            if (fromFieldMonster && card.Equips.Count > 0)
            {
                var eqs = card.Equips.ToList();
                card.Equips.Clear();
                foreach (var eq in eqs)
                {
                    if (eq == null) continue;
                    eq.EquippedTo = null;
                    var eqOwner = ControllerOf(eq) ?? owner;
                    SendCardToGrave(eqOwner, eq);
                }
            }

            if (fromFieldMonster || fromFieldSt)
            {
                MarkSentFromFieldToGy(card, sentBy, destroyedByBattle: false, battleDestroyer: null);
                MonsterEffects.OnSentFromFieldToGy(this, owner, card, destroyed: false,
                    destroyedByBattle: false, battleDestroyer: null);
            }
        }

        void MarkSentFromFieldToGy(CardInstance card, CardInstance sentBy, bool destroyedByBattle,
            CardInstance battleDestroyer)
        {
            if (card == null) return;
            card.SentFromFieldTurnNumber = TurnNumber;
            card.WasDestroyedByBattle = destroyedByBattle;
            card.BattleDestroyer = battleDestroyer;
            card.SentByContinuousSpellEffect =
                sentBy?.Def != null && sentBy.Def.IsSpell && sentBy.Def.IsContinuousSpellOrTrap;
        }

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
        public bool ReturnCardToHand(CardInstance card)
        {
            if (card == null) return false;
            var owner = ControllerOf(card);
            if (owner == null) return false;

            DetachFromField(owner, card);
            PendingTributes.Remove(card);
            card.FaceUp = true;
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

            if (card.Def != null && card.Def.IsExtraDeck)
            {
                owner.ExtraDeck.Add(card.CardId);
                Log($"{card.Name} returned to the Extra Deck.");
            }
            else
            {
                owner.Hand.Add(card);
                Log($"{card.Name} returned to {owner.Name}'s hand.");
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

        public bool SpecialSummonToField(DuelistState who, CardInstance card, BattlePosition pos, bool faceUp)
        {
            var idx = FirstEmpty(who.MonsterZones);
            if (idx < 0) return false;
            card.FaceUp = faceUp;
            card.Position = pos;
            card.SummonedThisTurn = true;
            card.WasSpecialSummoned = true;
            card.ClearAttackFlags();
            card.LingeringAtkModifier = 0;
            card.LingeringDefModifier = 0;
            card.WasDestroyedByBattle = false;
            card.BattleDestroyer = null;
            card.SentByContinuousSpellEffect = false;
            card.SentFromFieldTurnNumber = 0;
            who.MonsterZones[idx].Occupant = card;
            if (faceUp)
                TextEffects.TextEffectRuntime.TryResolveThisCardSummoned(
                    this, who, card, specialSummon: true);
            if (faceUp)
                OpenSummonResponseOrContinue(who, card);
            return true;
        }

        public void ForceEndBattlePhase(DuelistState turnPlayer)
        {
            if (Phase != DuelPhase.Battle || TurnPlayer != turnPlayer) return;
            Phase = DuelPhase.Main2;
            Log("[Main Phase 2] (Battle ended by card effect)");
        }

        // ───────────────────── Phase transitions ─────────────────────

        public bool TryEnterBattlePhase(DuelistState who)
        {
            if (GameOver || TurnPlayer != who) return false;
            if (Phase != DuelPhase.Main1)
            {
                Log("Battle Phase is entered from Main Phase 1 only.");
                return false;
            }

            if (TurnNumber == 1 && who == FirstPlayer)
            {
                Log("Rule: the player who goes first cannot conduct a Battle Phase on their first turn.");
                return false;
            }

            Phase = DuelPhase.Battle;
            BattleStep = BattleStep.StartStep;
            DamageSubStep = DamageSubStep.None;
            // Start Step: registered triggers only; then open Battle Step
            BattleStep = BattleStep.BattleStep;
            Log($"[{who.Name}] Battle Phase");
            OfferOpponentTurnTrapWindow(OpponentOf(who));
            Notify();
            return true;
        }

        /// <summary>From Battle → Main Phase 2 (or skip Battle: Main1 → Main2).</summary>
        public bool TryEnterMainPhase2(DuelistState who)
        {
            if (GameOver || TurnPlayer != who) return false;
            if (Phase != DuelPhase.Battle && Phase != DuelPhase.Main1)
            {
                Log("Main Phase 2 follows Main Phase 1 or Battle Phase.");
                return false;
            }

            var from = Phase;
            if (Phase == DuelPhase.Battle)
            {
                BattleStep = BattleStep.EndStep;
                BattleStep = BattleStep.None;
                DamageSubStep = DamageSubStep.None;
            }

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
            // Official continuous: Swords of Revealing Light — opponent cannot declare an attack
            if (OpponentHasSwordsOfRevealingLight(who))
                return false;
            if (ContinuousCannotAttackBlocks(who, attacker))
                return false;
            return true;
        }

        /// <summary>
        /// Gravity Bind / Insect Barrier / Messenger of Peace: a face-up Continuous
        /// program forbids this monster from declaring an attack.
        /// </summary>
        public bool ContinuousCannotAttackBlocks(DuelistState attackerController, CardInstance attacker)
        {
            if (attacker?.Def == null) return false;
            foreach (var side in new[] { Player, Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st == null || !st.FaceUp || st.Def == null) continue;
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null) continue;
                    foreach (var c in prog.ClausesFor(TextEffects.EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null ||
                            c.Action != TextEffects.EffectActionKind.ContinuousCannotAttack)
                            continue;
                        if (c.Side == TextEffects.EffectSide.Opponent && side == attackerController)
                            continue;
                        if (c.Side == TextEffects.EffectSide.Controller && side != attackerController)
                            continue;
                        if (!string.IsNullOrEmpty(c.RaceFilter) &&
                            (attacker.Def.race == null ||
                             attacker.Def.race.IndexOf(c.RaceFilter,
                                 System.StringComparison.OrdinalIgnoreCase) < 0))
                            continue;
                        if (c.AmountIsLevel)
                        {
                            if (attacker.Level < c.Amount) continue;
                            return true;
                        }

                        if (c.Amount > 0 && attacker.CurrentAtk < c.Amount) continue;
                        if (c.Amount > 0 || !string.IsNullOrEmpty(c.RaceFilter))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Rulebook 1 attack, plus continuous extra attacks (Mermaid Knight while Umi, …).</summary>
        public int MaxAttacksThisTurn(CardInstance attacker) =>
            1 + ExtraAttacksAllowed(attacker);

        /// <summary>YGOPro EFFECT_EXTRA_ATTACK value (1 = attack twice). Condition: named card / Umi.</summary>
        public int ExtraAttacksAllowed(CardInstance attacker)
        {
            if (attacker?.Def == null || !attacker.FaceUp || attacker.IsNegated) return 0;
            var extra = 0;
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
            if (!CanAttack(who, attacker)) return false;
            var opp = OpponentOf(who);
            if (opp == null) return false;
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

            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null) return false;
            foreach (var clause in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (clause == null || clause.Action != EffectActionKind.CanAttackDirectly)
                    continue;
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
                if (st != null && st.FaceUp && st.CardId == SpellTrapEffects.SwordsOfRevealingLight)
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
            var opp = OpponentOf(who);

            if (targetOrNull == null)
            {
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

            if (OpponentHasSwordsOfRevealingLight(who))
            {
                Log("Cannot declare an attack — opponent controls Swords of Revealing Light.");
                return false;
            }

            // —— Attack declaration + combat animation starts immediately ——
            DeclaredAttacker = attacker;
            DeclaredAttackTarget = targetOrNull;
            DeclaredAttackingPlayer = who;
            var hadMonstersAtDeclaration = HasMonsters(opp);

            var direct = targetOrNull == null;
            var profile = CombatAnimTimings.ForAttack(attacker, direct);
            ActivePresentation = new ActiveCombatPresentation
            {
                Kind = CombatActionKind.Attack,
                SourceCard = attacker,
                TargetCard = targetOrNull,
                Actor = who,
                Profile = profile,
                StartedUnscaledTime = Time.unscaledTime,
                ImpactResolved = false,
                HadMonstersAtAttackDeclaration = hadMonstersAtDeclaration
            };

            var tName = direct
                ? "directly"
                : (targetOrNull.FaceUp ? targetOrNull.Name : "a face-down monster");
            Log($"{attacker.Name} attacks {tName}.");

            // Defender may fire Fast Effects / traps while the attack anim plays
            if (OpenResponseWindow(opp, ResponseTiming.AttackDeclared, attacker, targetOrNull, who, null, null,
                    profile))
            {
                if (!IsHumanControlled(opp))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return true;
            }

            // No legal responses — resolve damage immediately (official: empty chain)
            ActivePresentation.ImpactResolved = true;
            return ResolveDeclaredAttack(hadMonstersAtDeclaration);
        }

        /// <summary>
        /// Impact / player chose not to trap — resolve the declared attack damage.
        /// Called when reaction window expires (animation hit) or Pass.
        /// </summary>
        public bool PassResponse()
        {
            if (PendingResponse == null) return false;
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
                else if (timing == ResponseTiming.MonsterSummoned)
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
            }

            ClearPendingResponse();

            if (timing == ResponseTiming.DamageCalculation)
            {
                FinishDeferredDamageCalculation();
                return true;
            }

            if (timing == ResponseTiming.OpponentOpenState)
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

            // Summon appear finished
            ClearPresentation();
            Notify();
            return true;
        }

        /// <summary>Trap / hand QE activated mid-window — cancel or modify, then continue.</summary>
        public void ContinueAfterResponseActivation(bool attackNegated, bool battlePhaseEnded)
        {
            var timing = PendingResponse?.Timing ?? ResponseTiming.None;
            ClearPendingResponse();

            if (timing == ResponseTiming.MonsterSummoned)
            {
                FinishSummonResponseWindow();
                return;
            }

            if (timing == ResponseTiming.YouTakeDamage)
            {
                CheckLpWin(Player, Opponent);
                FlushQueuedDamageTakenWindow();
                Notify();
                return;
            }

            // Damage Calculation: Kuriboh applied PreventBattleDamageThisBattle — finish calc
            if (timing == ResponseTiming.DamageCalculation || _deferredDamage != null)
            {
                FinishDeferredDamageCalculation();
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

            // Non-negating reaction (Waboku) — impact still comes, with protection
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
            if (OpponentHasSwordsOfRevealingLight(who))
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

            // —— Damage Step (official sub-steps) ——
            BattleStep = BattleStep.DamageStep;

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
            // Start of Damage Step: flip face-down targets face-up Defense (Rulebook).
            // Flip effects activate after damage calculation (not here) — track who was flipped.
            CardInstance flippedByBattle = null;
            if (targetOrNull != null && !targetOrNull.FaceUp && opp.TryFindMonster(targetOrNull, out _))
            {
                targetOrNull.FaceUp = true;
                // Face-down monsters are ALWAYS Defense Position — never resolve as ATK
                targetOrNull.Position = BattlePosition.Defense;
                flippedByBattle = targetOrNull;
                Log(
                    $"[Damage Step · Start] Flipped {targetOrNull.Name} face-up in Defense Position " +
                    $"(DEF {BattleMechanics.DefenseValue(targetOrNull)}).");
            }

            DamageSubStep = DamageSubStep.BeforeDamageCalculation;
            DamageSubStep = DamageSubStep.DamageCalculation;

            if (targetOrNull == null)
            {
                if (HasMonsters(opp) && !GrantsDirectAttack(attacker) &&
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

            var piercing = OfficialEffectRegistry.HasPiercing(attacker) ||
                           Continuous.SourceGrantsPiercing(attacker);
            var atkNoDes = OfficialEffectRegistry.CannotBeDestroyedByBattle(attacker) || who.WabokuActive;
            var defNoDes = targetOrNull != null &&
                           (OfficialEffectRegistry.CannotBeDestroyedByBattle(targetOrNull) || opp.WabokuActive);
            var noDmgAtk = who.WabokuActive || who.PreventBattleDamageThisBattle ||
                           Continuous.AnyPreventsBattleDamage(who) ||
                           FieldSpellEffects.ControllerAvoidsBattleDamage(this, who);
            var noDmgDef = opp.WabokuActive || opp.PreventBattleDamageThisBattle ||
                           Continuous.AnyPreventsBattleDamage(opp) ||
                           FieldSpellEffects.ControllerAvoidsBattleDamage(this, opp);

            if (opp.PreventBattleDamageThisBattle)
                Log($"[Damage Calculation] {opp.Name}: no battle damage from that battle (Kuriboh / effect).");

            var calc = BattleMechanics.Calculate(attacker, targetOrNull, piercing, atkNoDes, defNoDes,
                noDmgAtk, noDmgDef);
            // Pass prevention flags so ATK < DEF still deals (DEF−ATK) to the attacker
            // unless Waboku / Kuriboh / continuous protection applies to that controller.
            BattleMechanics.Sanitize(ref calc, attacker, targetOrNull, noDmgAtk, noDmgDef);
            Log(calc.LogLine);
            if (attacker != null) attacker.AtkBecomesZeroThisCalculation = false;
            if (targetOrNull != null) targetOrNull.AtkBecomesZeroThisCalculation = false;

            DamageSubStep = DamageSubStep.AfterDamageCalculation;
            // Apply LP damage before destruction so UI/orbs update even if destroy is deferred
            if (calc.DamageToDefendingPlayer > 0)
                ApplyDamage(opp, calc.DamageToDefendingPlayer);
            if (calc.DamageToAttackingPlayer > 0)
                ApplyDamage(who, calc.DamageToAttackingPlayer);
            else if (targetOrNull != null &&
                     BattleMechanics.UsesDefenseStat(targetOrNull) &&
                     BattleMechanics.AttackValue(attacker) < BattleMechanics.DefenseValue(targetOrNull) &&
                     !noDmgAtk)
            {
                // Hard fail-closed: never silently skip defense-hold damage
                var gap = BattleMechanics.DefenseValue(targetOrNull) -
                          BattleMechanics.AttackValue(attacker);
                if (gap > 0)
                {
                    Log(
                        $"[RULE] Forced defense-hold damage {gap} → {who.Name} " +
                        $"(ATK {BattleMechanics.AttackValue(attacker)} < DEF {BattleMechanics.DefenseValue(targetOrNull)}).");
                    calc.DamageToAttackingPlayer = gap;
                    ApplyDamage(who, gap);
                }
            }

            // Hard rule gate: never destroy a Defense monster when ATK ≤ DEF
            if (calc.DestroyDefender && targetOrNull != null &&
                BattleMechanics.UsesDefenseStat(targetOrNull) &&
                BattleMechanics.AttackValue(attacker) <= BattleMechanics.DefenseValue(targetOrNull))
            {
                Log(
                    $"[RULE] Blocked illegal destruction: {targetOrNull.Name} is in Defense " +
                    $"(DEF {BattleMechanics.DefenseValue(targetOrNull)} ≥ ATK {BattleMechanics.AttackValue(attacker)}).");
                calc.DestroyDefender = false;
            }

            // Clear per-battle damage prevention after this battle's calc
            who.PreventBattleDamageThisBattle = false;
            opp.PreventBattleDamageThisBattle = false;

            // —— Flip effects (after damage calculation, before battle destruction) ——
            if (flippedByBattle != null && opp.TryFindMonster(flippedByBattle, out _))
            {
                var hasFlip = MonsterEffects.IsFlipEffectMonster(flippedByBattle.CardId);
                if (!hasFlip)
                {
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(flippedByBattle);
                    hasFlip = prog != null && prog.HasTiming(TextEffects.EffectTiming.Flip);
                }

                if (hasFlip)
                    MonsterEffects.OnFlipSummoned(this, opp, flippedByBattle);
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

            var dmgDef = dmgDefAlreadyApplied >= 0 ? dmgDefAlreadyApplied : 0;
            var dmgAtk = dmgAtkAlreadyApplied >= 0 ? dmgAtkAlreadyApplied : 0;

            // If attacker was destroyed by Flip (e.g. MEB) mid-step, skip battle destroy
            if (destroyDefender && targetOrNull != null && opp.TryFindMonster(targetOrNull, out _))
            {
                var overwhelm = CardShatterPresentation.ComputeBattleOverwhelm(
                    attacker, targetOrNull, true, false, dmgDef, dmgAtk);
                CardShatterPresentation.QueueBattle(targetOrNull.InstanceId, overwhelm, targetOrNull.Name);
                DestroyMonster(opp, targetOrNull, byEffect: null, banishIfDestroyed: false,
                    destroyedByBattle: true, battleDestroyer: attacker);
                if (attacker != null)
                {
                    attacker.DestroyedByBattleThisTurn = true;
                    TextEffects.TextEffectRuntime.NotifyDestroyedOpponentByBattle(this, who, opp, attacker);
                }
                Log($"{targetOrNull.Name} destroyed by battle.");
            }
            else if (destroyDefender && targetOrNull != null)
            {
                Log($"{targetOrNull.Name} already left the field (Flip / effect) before battle destruction.");
            }

            if (destroyAttacker && who.TryFindMonster(attacker, out _))
            {
                var overwhelm = CardShatterPresentation.ComputeBattleOverwhelm(
                    attacker, targetOrNull, false, true, dmgDef, dmgAtk);
                CardShatterPresentation.QueueBattle(attacker.InstanceId, overwhelm, attacker.Name);
                DestroyMonster(who, attacker, byEffect: null, banishIfDestroyed: false,
                    destroyedByBattle: true, battleDestroyer: targetOrNull);
                Log($"{attacker.Name} destroyed by battle.");
            }
            else if (destroyAttacker)
            {
                Log($"{attacker.Name} already left the field (Flip / effect) before battle destruction.");
            }

            if (piercingApplied)
                Log("Piercing battle damage applied (official continuous property).");

            if (!destroyAttacker && !destroyDefender &&
                dmgDefAlreadyApplied == 0 && dmgAtkAlreadyApplied == 0)
                Log("Battle resolved — no damage, no destruction.");

            BattleStep = BattleStep.BattleStep;
            DamageSubStep = DamageSubStep.None;
            if (!IsAwaitingResponse)
                CheckLpWin(who, opp);
            Notify();
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
            DeclaredAttacker = null;
            DeclaredAttackTarget = null;
            DeclaredAttackingPlayer = null;
        }

        void OpenSummonResponseOrContinue(DuelistState summoner, CardInstance summoned)
        {
            if (summoner == null || summoned == null || GameOver) return;
            if (IsAwaitingResponse || IsAwaitingEffectTarget)
            {
                _queuedSummons.Add((summoner, summoned));
                return;
            }

            var profile = CombatAnimTimings.ForSummon(summoned);
            ActivePresentation = new ActiveCombatPresentation
            {
                Kind = CombatActionKind.SummonAppear,
                SourceCard = summoned,
                Actor = summoner,
                Profile = profile,
                StartedUnscaledTime = Time.unscaledTime,
                ImpactResolved = false
            };

            var defender = OpponentOf(summoner);
            if (OpenResponseWindow(defender, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                QueueOwnSummonWindowIfLegal(summoner, summoned);
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

            ClearPresentation();
        }

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
            OpenSummonResponseOrContinue(next.summoner, next.summoned);
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
                FlushQueuedSummonResponses();
                return;
            }

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
                _ => CombatAnimTimings.ForSummon(summoned)
            });

            if (IsHumanControlled(responder) && legal.Count > 0)
            {
                p.ImpactAtSeconds = Mathf.Max(p.ImpactAtSeconds, 8f);
                p.TotalSeconds = Mathf.Max(p.TotalSeconds, p.ImpactAtSeconds + 0.85f);
                if (ActivePresentation != null)
                {
                    var ap = ActivePresentation.Profile;
                    ap.ImpactAtSeconds = p.ImpactAtSeconds;
                    ap.TotalSeconds = p.TotalSeconds;
                    ActivePresentation.Profile = ap;
                }
            }

            var legalNames = string.Join(", ", legal.FindAll(c => c != null).ConvertAll(c => c.Name));
            if (legalNames.Length > 80)
                legalNames = legalNames.Substring(0, 77) + "…";

            pendingProbe.OpenedUnscaledTime = Time.unscaledTime;
            pendingProbe.ReactionSeconds = p.ImpactAtSeconds;
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
                pendingProbe.Prompt = timing switch
                {
                    ResponseTiming.AttackDeclared =>
                        $"Response ({p.ImpactAtSeconds:0.0}s) — {legalNames} or Pass ({legal.Count} legal).",
                    ResponseTiming.DamageCalculation =>
                        $"Damage Step ({p.ImpactAtSeconds:0.0}s) — {legalNames} / Pass ({legal.Count} legal).",
                    ResponseTiming.OpponentOpenState =>
                        $"Opponent's turn ({p.ImpactAtSeconds:0.0}s) — {legalNames} or Pass ({legal.Count} legal).",
                    ResponseTiming.YouTakeDamage =>
                        $"You took damage ({p.ImpactAtSeconds:0.0}s) — {legalNames} or Pass ({legal.Count} legal).",
                    _ =>
                        $"Summon response ({p.ImpactAtSeconds:0.0}s) — {legalNames} or Pass ({legal.Count} legal)."
                };
            }

            Log(pendingProbe.Prompt);
            return true;
        }

        void ApplyDamage(DuelistState target, int dmg)
        {
            if (dmg <= 0 || target == null) return;
            target.LifePoints = Mathf.Max(0, target.LifePoints - dmg);
            var you = target.IsPlayer ? "You took" : $"{target.Name} took";
            Log($"{you} {dmg} damage ({target.LifePoints} LP remaining).");
            DuelPresentationPacer.HoldCombatResult();
            OfferYouTakeDamageWindow(target);
        }

        void CheckLpWin(DuelistState a, DuelistState b)
        {
            if (a.LifePoints <= 0) EndGame(b);
            else if (b.LifePoints <= 0) EndGame(a);
        }

        public void EndTurn(DuelistState who)
        {
            if (GameOver || TurnPlayer != who) return;
            if (IsAwaitingResponse || IsAwaitingEffectTarget || HasDeclaredAttack)
            {
                Log("Cannot end turn during a response / targeting window.");
                return;
            }

            // May end from MP1, Battle, or MP2
            Phase = DuelPhase.End;
            Log($"[{who.Name}] End Phase");
            TextEffects.TextEffectRuntime.FirePhaseTriggers(this, who, TextEffects.EffectTiming.EndPhase);
            if (GameOver) return;
            if (IsAwaitingEffectTarget)
            {
                _endTurnPausedFor = who;
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
            _endTurnPausedFor = null;
            if (GameOver) return;
            FinishEndTurnAfterTriggers(who);
        }

        void FinishEndTurnAfterTriggers(DuelistState who)
        {
            if (who == null) return;
            while (who.Hand.Count > TcgRules.HandSizeLimitEndPhase)
            {
                var discard = who.Hand[who.Hand.Count - 1];
                who.Hand.RemoveAt(who.Hand.Count - 1);
                who.Graveyard.Add(discard);
                Log($"[{who.Name}] discards {discard.Name} (hand size limit {TcgRules.HandSizeLimitEndPhase}).");
            }

            // Official: Swords of Revealing Light is destroyed during the End Phase of
            // the opponent's 3rd turn (count opponent's End Phases after activation).
            var swordsOwner = OpponentOf(who);
            if (swordsOwner != null)
            {
                foreach (var st in swordsOwner.SpellTrapsOnField().ToList())
                {
                    if (st == null || !st.FaceUp) continue;
                    if (st.CardId != SpellTrapEffects.SwordsOfRevealingLight) continue;
                    if (st.ContinuousTurnsRemaining <= 0) continue;
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

            // Temporary take-control (Change of Heart / Brain Control) returns
            // during the End Phase of the turn the effect resolved.
            var returning = new System.Collections.Generic.List<CardInstance>();
            foreach (var side in new[] { Player, Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m != null && m.TempControlUntilEndTurn == TurnNumber)
                        returning.Add(m);
                }
            }

            foreach (var m in returning)
            {
                var holder = ControllerOf(m);
                var original = holder != null ? OpponentOf(holder) : null;
                m.TempControlUntilEndTurn = -1;
                if (original == null || holder == original) continue;
                if (!TryTakeControl(original, m))
                {
                    Log($"{m.Name}: control cannot return — sent to the GY.");
                    DestroyMonsterPublic(holder, m);
                }
            }

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
            Player.MustAttackDirectlyThisTurn = false;
            Opponent.MustAttackDirectlyThisTurn = false;
            ClearUntilEndOfTurnStatMods();

            PendingTributes.Clear();
            BeginTurn(OpponentOf(who));
            Notify();
        }

        void DestroyMonster(DuelistState owner, CardInstance card, CardInstance byEffect = null,
            bool banishIfDestroyed = false, bool destroyedByBattle = false,
            CardInstance battleDestroyer = null)
        {
            if (owner == null || card == null) return;
            if (byEffect != null &&
                ContinuousProtections.IsUnaffectedBy(this, card, byEffect))
            {
                Log($"{card.Name} is unaffected by {byEffect.Name}.");
                return;
            }

            if (owner.TryFindMonster(card, out var i))
            {
                // Effect / non-battle destroy → default shatter (battle already queued)
                CardShatterPresentation.QueueEffect(card.InstanceId, card.Name);
                owner.MonsterZones[i].Occupant = null;
                PendingTributes.Remove(card);

                // A destroyed monster's Equip cards (and destroy-linked Continuous
                // Traps like Call of the Haunted / Battle-Scarred) are sent to the GY.
                if (card.Equips.Count > 0)
                {
                    var eqs = card.Equips.ToList();
                    card.Equips.Clear();
                    foreach (var eq in eqs)
                    {
                        if (eq == null) continue;
                        eq.EquippedTo = null;
                        var eqOwner = ControllerOf(eq) ?? owner;
                        SendCardToGrave(eqOwner, eq);
                    }
                }
                if (card.IsToken)
                {
                    if (card.TokenDestroyedDamage > 0)
                        ApplyEffectDamage(owner, card.TokenDestroyedDamage, card.Name);
                    Log($"Token destroyed: {card.Name}");
                }
                else if (banishIfDestroyed)
                {
                    card.FaceUp = true;
                    if (!owner.Banished.Contains(card))
                        owner.Banished.Add(card);
                    Log($"Destroyed: {card.Name} (banished).");
                }
                else
                {
                    if (!owner.Graveyard.Contains(card))
                        owner.Graveyard.Add(card);
                    Log($"Destroyed: {card.Name}");
                    MarkSentFromFieldToGy(card, byEffect, destroyedByBattle, battleDestroyer);
                    MonsterEffects.OnSentFromFieldToGy(this, owner, card, destroyed: true,
                        destroyedByBattle, battleDestroyer);
                }

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

        /// <summary>Effect damage (not battle) — Ring of Destruction, etc.</summary>
        public void ApplyEffectDamage(DuelistState target, int dmg, string sourceName = null)
        {
            if (dmg <= 0 || target == null) return;
            target.LifePoints = Mathf.Max(0, target.LifePoints - dmg);
            var src = string.IsNullOrEmpty(sourceName) ? "effect" : sourceName;
            var you = target.IsPlayer ? "You took" : $"{target.Name} took";
            Log($"💥 {you} {dmg} effect damage from {src} ({target.LifePoints} LP remaining).");
            DuelPresentationPacer.HoldCombatResult();
            OfferYouTakeDamageWindow(target);
            if (!IsAwaitingResponse)
                CheckLpWin(Player, Opponent);
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

        void EndGame(DuelistState winner)
        {
            if (GameOver) return;
            GameOver = true;
            Winner = winner;
            Phase = DuelPhase.GameOver;
            var youWin = winner != null && winner.IsPlayer;
            Log(youWin
                ? "★ YOU WIN — opponent’s LP is 0 or they could not draw."
                : "★ YOU LOSE — your LP is 0 or you could not draw.");
            OnGameOver?.Invoke();
            Notify();
        }

        public void Log(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            // Drop known fluff before UI / review see it
            if (DuelReviewLog.IsNoise(msg)) return;
            Debug.Log("[Duel] " + msg);
            // ReviewLog also listens to OnLog; fire event for UI + structured capture
            OnLog?.Invoke(msg);
        }

        /// <summary>Explicit structured review note (optional; most lines go through <see cref="Log"/>).</summary>
        public void LogReview(DuelLogKind kind, string actor, string message)
        {
            // Do not re-fire OnLog (ReviewLog already captures via Record).
            ReviewLog?.Record(kind, actor, message);
            Debug.Log("[Duel/Review] " + message);
        }

        void Notify()
        {
            if (!Ocg.OcgLabDuelHost.IsActive)
                FieldSpellEffects.RefreshBoard(this);
            LastRulesSnapshot = GameStateSnapshotBuilder.Capture(this);
            OnStateChanged?.Invoke();
        }

        /// <summary>Rules-legal multiplayer snapshot (identical on both peers).</summary>
        public RulesGameStateSnapshot CaptureRulesState() => GameStateSnapshotBuilder.Capture(this);

        public string StatusLine()
        {
            if (Player == null || Opponent == null)
                return "Loading duel…";

            if (GameOver)
                return Winner != null && Winner.IsPlayer ? "YOU WIN" : "YOU LOSE";

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
                    var summon = PendingResponse.Timing == ResponseTiming.MonsterSummoned;
                    return summon
                        ? $"Summon response {left:0.0}s — tap a blinking zone to activate, or Pass"
                        : $"Attack response {left:0.0}s — tap a blinking zone to activate, or Pass";
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
