using System;
using System.Collections.Generic;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Single source of truth for TCG legality and resolution.
    /// AR duel-disk / hologram layers query and command only through this surface —
    /// they never mutate zones directly.
    /// </summary>
    public interface IDuelEngine
    {
        DuelistState Player { get; }
        DuelistState Opponent { get; }
        DuelistState TurnPlayer { get; }
        DuelPhase Phase { get; }
        BattleStep BattleStep { get; }
        int TurnNumber { get; }
        bool GameOver { get; }
        DuelistState Winner { get; }
        bool HasDeclaredAttack { get; }
        bool IsBusy { get; }
        ChainStack Chain { get; }

        event Action<string> OnLog;
        event Action OnStateChanged;
        event Action OnGameOver;

        void StartDuel(CardDatabase db, DeckFile playerDeck, DeckFile aiDeck);
        RulesGameStateSnapshot CaptureRulesState();

        // —— Queries (public knowledge aware where noted) ——
        bool CanNormalSummonOrSet(DuelistState who, CardInstance card);
        bool CanFlipSummon(DuelistState who, CardInstance monster);
        bool CanChangePosition(DuelistState who, CardInstance monster);
        bool CanSetSpellTrap(DuelistState who, CardInstance card);
        bool CanActivateSpellTrap(DuelistState who, CardInstance card, bool fromHand);
        bool CanAttack(DuelistState who, CardInstance attacker);
        bool CanAttackDirectly(DuelistState who, CardInstance attacker);
        bool CanConductBattlePhase { get; }
        bool InMainPhase { get; }

        /// <summary>AR pre-snap / pre-activate validation (no mutation).</summary>
        RulesValidator.ActionVerdict ValidatePlacement(
            DuelistState who, CardInstance card, RulesZoneKind zone, int zoneIndex, bool preferSet);

        RulesValidator.ActionVerdict ValidateActivation(
            DuelistState who, CardInstance card, bool fromHand);

        RulesValidator.ActionVerdict ValidateAttack(
            DuelistState who, CardInstance attacker, CardInstance targetOrNull);

        // —— Commands (mutate only if legal) ——
        bool TryNormalSummonToZone(DuelistState who, CardInstance card, bool asSet, int preferredZone);
        bool TryFlipSummon(DuelistState who, CardInstance monster);
        bool TryChangePosition(DuelistState who, CardInstance monster);
        bool TrySetSpellTrapToZone(DuelistState who, CardInstance card, int preferredZone);
        bool TryActivateSpellTrap(DuelistState who, CardInstance card, bool fromHand);
        bool TryAttack(DuelistState who, CardInstance attacker, CardInstance targetOrNull);
        bool TryEnterBattlePhase(DuelistState who);
        bool TryEnterMainPhase2(DuelistState who);
        void EndTurn(DuelistState who);
        bool PassResponse();

        DuelistState OpponentOf(DuelistState who);
        void Log(string msg);
    }

    /// <summary>
    /// Read-only view for holograms / netcode / UI binding.
    /// </summary>
    public interface IGameStateView
    {
        int TurnNumber { get; }
        DuelPhase Phase { get; }
        BattleStep BattleStep { get; }
        int PlayerLp { get; }
        int OppLp { get; }
        IReadOnlyList<CardInstance> PlayerHand { get; }
        /// <summary>Opponent hand size only (private knowledge).</summary>
        int OppHandCount { get; }
        FieldZone[] PlayerMonsters { get; }
        FieldZone[] OppMonsters { get; }
        FieldZone[] PlayerSpellTraps { get; }
        FieldZone[] OppSpellTraps { get; }
        bool GameOver { get; }
        RulesGameStateSnapshot Snapshot { get; }
    }

    /// <summary>Adapter wrapping <see cref="DuelEngine"/> as <see cref="IGameStateView"/>.</summary>
    public sealed class GameStateView : IGameStateView
    {
        readonly DuelEngine _e;
        public GameStateView(DuelEngine e) => _e = e;

        public int TurnNumber => _e.TurnNumber;
        public DuelPhase Phase => _e.Phase;
        public BattleStep BattleStep => _e.BattleStep;
        public int PlayerLp => _e.Player?.LifePoints ?? 0;
        public int OppLp => _e.Opponent?.LifePoints ?? 0;
        public IReadOnlyList<CardInstance> PlayerHand => _e.Player?.Hand;
        public int OppHandCount => _e.Opponent?.HandCount ?? 0;
        public FieldZone[] PlayerMonsters => _e.Player?.MonsterZones;
        public FieldZone[] OppMonsters => _e.Opponent?.MonsterZones;
        public FieldZone[] PlayerSpellTraps => _e.Player?.SpellTrapZones;
        public FieldZone[] OppSpellTraps => _e.Opponent?.SpellTrapZones;
        public bool GameOver => _e.GameOver;
        public RulesGameStateSnapshot Snapshot => _e.LastRulesSnapshot ?? _e.CaptureRulesState();
    }
}
