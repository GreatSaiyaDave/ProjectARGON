using System.Collections.Generic;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// One player's state at the end of the opponent's End Phase (EmptyHandStandbyDraw):
    /// <see cref="DuelistState.EndOfOpponentsLastEndPhase"/>.
    /// </summary>
    public sealed class EndPhaseRecord
    {
        /// <summary><see cref="DuelEngine.TurnNumber"/> of the opponent's turn whose End Phase ended.</summary>
        public int Turn;

        /// <summary>The player had no cards in their hand at that moment.</summary>
        public bool HandEmpty;

        /// <summary>
        /// Every face-up card the player controlled on the field at that moment, as a field presence: the card
        /// with its <see cref="CardInstance.FieldEntryStamp"/> and <see cref="CardInstance.FaceDownStamp"/>, so a
        /// copy that left the field or was turned face-down since no longer counts.
        /// </summary>
        public readonly List<(CardInstance card, int entryStamp, int faceDownStamp)> FaceUp =
            new List<(CardInstance, int, int)>();
    }

    /// <summary>
    /// "During your Standby Phase, if you controlled this face-up card and had no cards in your hand at the end of
    /// the opponent's last End Phase: Draw 1 card." (The Agent of Wisdom - Mercury,
    /// <see cref="EmptyHandStandbyDrawAtoms"/>). <see cref="RecordEndOfEndPhase"/> runs as the last step of every
    /// End Phase (DuelEngine.FinishEndTurnAfterTriggers) and records the other player's hand and face-up cards;
    /// TextEffectRuntime.FirePhaseTriggersFor asks <see cref="ControlledFaceUpWithEmptyHand"/> before it applies a
    /// <see cref="EffectClause.RequiresEmptyHandAtOpponentsLastEndPhase"/> clause. Recording ignores negation
    /// (ygopro EFFECT_FLAG_CANNOT_DISABLE); a card negated at the Standby Phase still draws nothing, like every
    /// other negated phase trigger.
    /// </summary>
    public static class EmptyHandStandbyDrawRuntime
    {
        /// <summary>
        /// End of <paramref name="turnPlayer"/>'s End Phase: record, for the other player, whether their hand is
        /// empty and which cards they control face-up. Replaces that player's previous record.
        /// </summary>
        public static void RecordEndOfEndPhase(DuelEngine engine, DuelistState turnPlayer)
        {
            var other = engine?.OpponentOf(turnPlayer);
            if (other == null) return;
            var record = new EndPhaseRecord { Turn = engine.TurnNumber, HandEmpty = other.Hand.Count == 0 };
            foreach (var m in other.MonstersOnField())
                if (m != null && m.FaceUp)
                    record.FaceUp.Add((m, m.FieldEntryStamp, m.FaceDownStamp));
            foreach (var st in other.SpellTrapsOnField())
                if (st != null && st.FaceUp)
                    record.FaceUp.Add((st, st.FieldEntryStamp, st.FaceDownStamp));
            other.EndOfOpponentsLastEndPhase = record;
        }

        /// <summary>
        /// True when <paramref name="who"/> had no cards in hand at the end of the End Phase of the turn right
        /// before this one (the opponent's), controlled <paramref name="card"/> face-up then, and it has stayed on
        /// the field face-up since. Only that one following turn: the Lua trigger resets after the next Standby Phase.
        /// </summary>
        public static bool ControlledFaceUpWithEmptyHand(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var record = who?.EndOfOpponentsLastEndPhase;
            if (engine == null || record == null || card == null || !card.FaceUp) return false;
            if (!record.HandEmpty || record.Turn != engine.TurnNumber - 1) return false;
            foreach (var (c, entryStamp, faceDownStamp) in record.FaceUp)
                if (ReferenceEquals(c, card))
                    return entryStamp == card.FieldEntryStamp && faceDownStamp == card.FaceDownStamp;
            return false;
        }
    }
}
