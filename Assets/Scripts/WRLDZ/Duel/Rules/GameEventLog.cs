using System.Collections.Generic;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Append-only duel history. Trigger "when" vs "if" and OPT legality read this,
    /// not live board crumbs. Do not use as a callback bus.
    /// </summary>
    public enum DuelGameEventKind
    {
        None = 0,
        Summoned,
        Destroyed,
        SentToGy,
        TookDamage,
        DeclaredAttack,
        Targeted,
        Activated
    }

    public struct DuelGameEvent
    {
        public DuelGameEventKind Kind;
        public int TurnNumber;
        public DuelistState Actor;
        public CardInstance Card;
        public CardInstance Related;
    }

    public sealed class DuelGameEventLog
    {
        readonly List<DuelGameEvent> _events = new();
        public IReadOnlyList<DuelGameEvent> Events => _events;

        public void Add(DuelGameEventKind kind, int turn, DuelistState actor,
            CardInstance card, CardInstance related = null)
        {
            if (kind == DuelGameEventKind.None) return;
            _events.Add(new DuelGameEvent
            {
                Kind = kind,
                TurnNumber = turn,
                Actor = actor,
                Card = card,
                Related = related
            });
        }

        public void Clear() => _events.Clear();

        public bool HappenedThisTurn(DuelGameEventKind kind, int turn, CardInstance card = null)
        {
            for (var i = _events.Count - 1; i >= 0; i--)
            {
                var e = _events[i];
                if (e.TurnNumber != turn) break;
                if (e.Kind != kind) continue;
                if (card == null || e.Card == card) return true;
            }

            return false;
        }

        /// <summary>
        /// "When" (activation-only) is legal only if this event is the current chain/window cause.
        /// </summary>
        public bool LatestThisTurnIs(DuelGameEventKind kind, int turn, CardInstance card = null)
        {
            for (var i = _events.Count - 1; i >= 0; i--)
            {
                var e = _events[i];
                if (e.TurnNumber != turn) return false;
                if (card != null && e.Card != card && e.Related != card) continue;
                return e.Kind == kind;
            }

            return false;
        }
    }
}
