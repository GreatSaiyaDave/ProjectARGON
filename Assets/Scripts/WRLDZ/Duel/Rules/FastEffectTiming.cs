using System.Collections.Generic;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Fast Effect Timing / priority (Yugipedia "Fast Effect Timing").
    /// Turn player has priority to activate Fast Effects first when a chain is not open
    /// after an action; then opponent; both pass → continue.
    /// </summary>
    public sealed class FastEffectTiming
    {
        public enum WindowKind
        {
            None = 0,
            /// <summary>Open response to an action (summon, attack declaration, activation).</summary>
            OpenResponse,
            /// <summary>After a chain link is activated — opponent may chain.</summary>
            ChainResponse,
            /// <summary>Open game state in Main Phase — turn player priority.</summary>
            OpenGameState
        }

        public WindowKind Kind { get; private set; }
        public DuelistState PriorityPlayer { get; private set; }
        public DuelistState OtherPlayer { get; private set; }
        public bool TurnPlayerPassed { get; private set; }
        public bool OpponentPassed { get; private set; }
        public ChainEvent Cause { get; private set; }

        public bool IsOpen => Kind != WindowKind.None;

        public void Open(WindowKind kind, DuelistState priority, DuelistState other, ChainEvent cause)
        {
            Kind = kind;
            PriorityPlayer = priority;
            OtherPlayer = other;
            Cause = cause;
            TurnPlayerPassed = false;
            OpponentPassed = false;
        }

        public void Close()
        {
            Kind = WindowKind.None;
            PriorityPlayer = null;
            OtherPlayer = null;
            Cause = ChainEvent.None;
            TurnPlayerPassed = false;
            OpponentPassed = false;
        }

        /// <summary>Player with priority passes.</summary>
        public bool Pass(DuelistState who)
        {
            if (!IsOpen || who == null) return false;
            if (who == PriorityPlayer)
                TurnPlayerPassed = true;
            else if (who == OtherPlayer)
                OpponentPassed = true;
            else
                return false;

            // If priority passed, other gets chance; if both passed, window closes
            if (TurnPlayerPassed && OpponentPassed)
            {
                Close();
                return true; // fully closed
            }

            // Swap priority to the other player after first pass
            if (TurnPlayerPassed && !OpponentPassed && PriorityPlayer != OtherPlayer)
            {
                var tmp = PriorityPlayer;
                PriorityPlayer = OtherPlayer;
                OtherPlayer = tmp;
            }

            return false;
        }

        public bool HasPriority(DuelistState who) =>
            IsOpen && who != null && who == PriorityPlayer;
    }
}
