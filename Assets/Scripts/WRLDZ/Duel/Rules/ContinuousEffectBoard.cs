using System.Collections.Generic;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Tracks continuous effects that apply while conditions hold.
    /// Only registered continuous scripts may add entries — no free-text inference.
    /// </summary>
    public sealed class ContinuousEffectBoard
    {
        public sealed class Entry
        {
            public int SourceInstanceId;
            public int SourceCardId;
            public DuelistState Controller;
            public string Key;
            public string OfficialTextSnapshot;
            /// <summary>Optional ATK modifier applied by this continuous effect.</summary>
            public int AtkModifier;
            public int DefModifier;
            public bool GrantsPiercing;
            public bool PreventsBattleDestruction;
            public bool PreventsBattleDamageToController;
            public bool PreventsBattleDamageToOpponent;
            public int TurnsRemaining; // -1 = permanent while face-up
        }

        readonly List<Entry> _entries = new();
        public IReadOnlyList<Entry> Entries => _entries;

        public void Clear() => _entries.Clear();

        public void Add(Entry e)
        {
            if (e == null) return;
            _entries.Add(e);
        }

        public void RemoveBySource(int instanceId) =>
            _entries.RemoveAll(e => e.SourceInstanceId == instanceId);

        public void TickStandby(DuelistState turnPlayer)
        {
            // Countdown turn-limited continuous (e.g. Swords of Revealing Light — registered only)
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.TurnsRemaining < 0) continue;
                if (e.Controller != turnPlayer) continue;
                e.TurnsRemaining--;
                if (e.TurnsRemaining <= 0)
                    _entries.RemoveAt(i);
            }
        }

        public int AtkModFor(CardInstance monster)
        {
            if (monster == null) return 0;
            var sum = 0;
            foreach (var e in _entries)
                sum += e.AtkModifier; // refined filters via registered scripts later
            return sum;
        }

        public bool AnyPreventsBattleDamage(DuelistState who)
        {
            foreach (var e in _entries)
                if (e.Controller == who && e.PreventsBattleDamageToController)
                    return true;
            return false;
        }

        public bool SourceGrantsPiercing(CardInstance attacker)
        {
            if (attacker == null) return false;
            foreach (var e in _entries)
                if (e.SourceInstanceId == attacker.InstanceId && e.GrantsPiercing)
                    return true;
            return attacker.HasPiercing;
        }
    }
}
