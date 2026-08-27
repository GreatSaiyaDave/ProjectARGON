using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Full rules-legal game state for multiplayer sync.
    /// Both peers must apply the same snapshot; holograms only read this after commit.
    /// </summary>
    [Serializable]
    public class RulesGameStateSnapshot
    {
        public int SchemaVersion = 1;
        public int TurnNumber;
        public string Phase;
        public string BattleStep;
        public string DamageSubStep;
        public int PlayerLp;
        public int OppLp;
        public bool PlayerNormalSummonUsed;
        public bool OppNormalSummonUsed;
        public bool GameOver;
        public string WinnerSide; // "player" | "opponent" | ""
        public string ChainDescribe;
        public long TimestampMs;

        public List<ZoneCardState> PlayerMonsters = new();
        public List<ZoneCardState> OppMonsters = new();
        public List<ZoneCardState> PlayerSpells = new();
        public List<ZoneCardState> OppSpells = new();
        public ZoneCardState PlayerField;
        public ZoneCardState OppField;
        public List<int> PlayerHandIds = new(); // private: only owner should display names
        public List<int> OppHandCountOnly = new(); // size marker; hide card ids from opponent view
        public int PlayerDeckCount;
        public int OppDeckCount;
        public int PlayerGyCount;
        public int OppGyCount;
        public int PlayerExtraCount;
        public int OppExtraCount;
    }

    [Serializable]
    public class ZoneCardState
    {
        public int Zone;
        public int InstanceId;
        public int CardId;
        public bool FaceUp;
        public bool Defense;
        public bool SummonedThisTurn;
        public bool AttackedThisTurn;
        public int Counters;
        public int OverlayCount;
        public bool HasPiercing;
        public bool CannotBeDestroyedByBattle;
        public int AtkMod;
        public int DefMod;
    }

    public static class GameStateSnapshotBuilder
    {
        public static RulesGameStateSnapshot Capture(DuelEngine engine)
        {
            var s = new RulesGameStateSnapshot
            {
                TurnNumber = engine.TurnNumber,
                Phase = engine.Phase.ToString(),
                BattleStep = engine.BattleStep.ToString(),
                DamageSubStep = engine.DamageSubStep.ToString(),
                PlayerLp = engine.Player?.LifePoints ?? 0,
                OppLp = engine.Opponent?.LifePoints ?? 0,
                PlayerNormalSummonUsed = engine.Player?.NormalSummonUsed ?? false,
                OppNormalSummonUsed = engine.Opponent?.NormalSummonUsed ?? false,
                GameOver = engine.GameOver,
                WinnerSide = engine.Winner == null ? "" :
                    engine.Winner.IsPlayer ? "player" : "opponent",
                ChainDescribe = engine.Chain?.Describe() ?? "",
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PlayerDeckCount = engine.Player?.DeckCount ?? 0,
                OppDeckCount = engine.Opponent?.DeckCount ?? 0,
                PlayerGyCount = engine.Player?.Graveyard?.Count ?? 0,
                OppGyCount = engine.Opponent?.Graveyard?.Count ?? 0,
                PlayerExtraCount = engine.Player?.ExtraDeck?.Count ?? 0,
                OppExtraCount = engine.Opponent?.ExtraDeck?.Count ?? 0
            };

            PackZones(engine.Player?.MonsterZones, s.PlayerMonsters);
            PackZones(engine.Opponent?.MonsterZones, s.OppMonsters);
            PackZones(engine.Player?.SpellTrapZones, s.PlayerSpells);
            PackZones(engine.Opponent?.SpellTrapZones, s.OppSpells);
            s.PlayerField = PackOne(0, engine.Player?.FieldSpellZone?.Occupant);
            s.OppField = PackOne(0, engine.Opponent?.FieldSpellZone?.Occupant);

            if (engine.Player?.Hand != null)
                foreach (var c in engine.Player.Hand)
                    s.PlayerHandIds.Add(c.InstanceId);

            // Opponent hand: public knowledge is only count
            var oppHand = engine.Opponent?.HandCount ?? 0;
            for (var i = 0; i < oppHand; i++)
                s.OppHandCountOnly.Add(-1);

            return s;
        }

        static void PackZones(FieldZone[] zones, List<ZoneCardState> list)
        {
            list.Clear();
            if (zones == null) return;
            for (var i = 0; i < zones.Length; i++)
            {
                var c = zones[i]?.Occupant;
                if (c == null) continue;
                list.Add(PackOne(i, c));
            }
        }

        static ZoneCardState PackOne(int zone, CardInstance c)
        {
            if (c == null) return null;
            return new ZoneCardState
            {
                Zone = zone,
                InstanceId = c.InstanceId,
                CardId = c.CardId,
                FaceUp = c.FaceUp,
                Defense = c.Position == BattlePosition.Defense,
                SummonedThisTurn = c.SummonedThisTurn,
                AttackedThisTurn = c.AttackedThisTurn,
                Counters = c.Counters,
                OverlayCount = c.OverlayMaterials?.Count ?? 0,
                HasPiercing = c.HasPiercing,
                CannotBeDestroyedByBattle = c.CannotBeDestroyedByBattle,
                AtkMod = c.AtkModifier,
                DefMod = c.DefModifier
            };
        }
    }
}
