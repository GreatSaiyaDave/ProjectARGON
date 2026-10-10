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

        // The two duelists as seated when the window opened; the pass flags follow them, not the
        // priority swap (the old swap re-marked the same side, so two passes never closed a window).
        DuelistState _openedFirst;
        DuelistState _openedSecond;

        public void Open(WindowKind kind, DuelistState priority, DuelistState other, ChainEvent cause)
        {
            Kind = kind;
            PriorityPlayer = priority;
            OtherPlayer = other;
            _openedFirst = priority;
            _openedSecond = other;
            Cause = cause;
            TurnPlayerPassed = false;
            OpponentPassed = false;
        }

        public void Close()
        {
            Kind = WindowKind.None;
            PriorityPlayer = null;
            OtherPlayer = null;
            _openedFirst = null;
            _openedSecond = null;
            Cause = ChainEvent.None;
            TurnPlayerPassed = false;
            OpponentPassed = false;
        }

        /// <summary>
        /// <paramref name="who"/> passes. <see cref="TurnPlayerPassed"/> is the duelist who had priority when
        /// the window opened, <see cref="OpponentPassed"/> the other one. After one pass priority moves to
        /// the other duelist; when both have passed (with no new activation in between, which reopens
        /// the window) the window closes and this returns true.
        /// </summary>
        public bool Pass(DuelistState who)
        {
            if (!IsOpen || who == null) return false;
            if (who == _openedFirst)
                TurnPlayerPassed = true;
            else if (who == _openedSecond)
                OpponentPassed = true;
            else
                return false;

            if (TurnPlayerPassed && OpponentPassed)
            {
                Close();
                return true; // fully closed
            }

            // Priority goes to whoever has not passed yet.
            var next = who == _openedFirst ? _openedSecond : _openedFirst;
            if (next != null && next != who)
            {
                PriorityPlayer = next;
                OtherPlayer = who;
            }

            return false;
        }

        public bool HasPriority(DuelistState who) =>
            IsOpen && who != null && who == PriorityPlayer;
    }
}
