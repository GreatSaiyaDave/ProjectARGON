using System;
using System.Collections;
using System.Collections.Generic;

namespace WRLDZ.Duel
{
    /// <summary>
    /// PerCopyDeck batch (v163): what one copy in a Deck carries besides its card id. A copy with no info (null) is an
    /// ordinary card: owned by the duelist whose Deck holds it, face-down. Parasite Paracide puts itself face-up in the
    /// opponent's Deck (its owner stays its owner); Pharaoh's Treasure shuffles itself face-up into its own Deck. Both
    /// register a "when this card is drawn" trigger on that copy (ygopro EVENT_DRAW, TRIGGER_F, registered by the
    /// operation that put it there), which <see cref="DrawTrigger"/> records.
    /// </summary>
    public sealed class DeckCopyInfo
    {
        /// <summary>The copy's owner. Null = the duelist whose Deck holds it.</summary>
        public DuelistState Owner;

        /// <summary>The copy is face-up in the Deck (ygopro Card.ReverseInDeck): public knowledge.</summary>
        public bool FaceUp;

        /// <summary>
        /// The effect that put this copy here registered its own "when this card is drawn" trigger on it (the clauses
        /// with <see cref="TextEffects.EffectTiming.DrawnFaceUpFromDeck"/>). Another copy of the same card, or this one
        /// after it left the Deck other than by a draw, has no such trigger.
        /// </summary>
        public bool DrawTrigger;
    }

    /// <summary>
    /// PerCopyDeck batch (v163): a duelist's Main Deck. It keeps the <c>List&lt;int&gt;</c> surface the engine was
    /// written against (same member names and semantics; the top of the Deck is index 0), so every call site compiles
    /// unchanged, and keeps per-copy metadata (<see cref="DeckCopyInfo"/>) aligned with the ids. Adding or inserting a
    /// plain id adds an ordinary copy (owned, face-down), as before.
    /// <para>Shuffles move the metadata with its id: <see cref="DuelRng.Shuffle(DeckPile)"/> makes the same RNG calls as
    /// the list shuffle, so seeded duels replay the same. The pile is deliberately not an <c>IList&lt;int&gt;</c>, so a
    /// generic index-swapping helper cannot separate an id from its metadata.</para>
    /// <para>The copy most recently removed by <see cref="RemoveAt"/> / <see cref="Remove"/> is remembered until the
    /// next change to the pile or the next card instance the engine creates: that instance, made right after the
    /// removal (<see cref="TextEffects.PerCopyDeckRuntime.ClaimDeckCopy"/>), takes the copy's owner, and putting the
    /// same id straight back keeps its metadata. A path that already holds the instance (a pick from a Deck list made
    /// before the removal) or moves several copies at once uses <see cref="TakeAt"/>, which hands the metadata over
    /// directly (<see cref="TextEffects.PerCopyDeckRuntime.TakeFromDeck"/>).</para>
    /// </summary>
    public sealed class DeckPile : IReadOnlyList<int>
    {
        readonly List<int> _ids;
        // Lazily allocated: null while every copy is ordinary (nearly always).
        List<DeckCopyInfo> _info;
        // The copy last removed by RemoveAt / Remove, until it is claimed or forgotten. The stamp orders removals across
        // both players' piles, so a claim takes the newest one.
        static long s_departureStamp;
        bool _hasDeparted;
        int _departedId;
        DeckCopyInfo _departedInfo;
        long _departedStamp;

        public DeckPile() => _ids = new List<int>();

        public DeckPile(IEnumerable<int> ids) => _ids = ids != null ? new List<int>(ids) : new List<int>();

        /// <summary>Source compatibility: <c>who.Deck = someList</c> builds a pile of ordinary copies.</summary>
        public static implicit operator DeckPile(List<int> ids) => ids == null ? null : new DeckPile(ids);

        public int Count => _ids.Count;

        /// <summary>The id at <paramref name="index"/>. Setting it puts a different, ordinary card there.</summary>
        public int this[int index]
        {
            get => _ids[index];
            set
            {
                _ids[index] = value;
                if (_info != null) _info[index] = null;
            }
        }

        public List<int>.Enumerator GetEnumerator() => _ids.GetEnumerator();
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => _ids.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _ids.GetEnumerator();

        // ───────────────────────── List<int> surface ─────────────────────────

        public void Add(int id) => Insert(_ids.Count, id);

        public void AddRange(IEnumerable<int> ids)
        {
            if (ids == null) return;
            foreach (var id in ids is DeckPile p ? new List<int>(p._ids) : ids)
                Add(id);
        }

        public void Insert(int index, int id)
        {
            DeckCopyInfo info = null;
            // The copy just taken out and put straight back (a reorder): the same card, so the same metadata.
            if (_hasDeparted && _departedId == id)
                info = _departedInfo;
            ForgetDeparted();
            InsertCore(index, id, info);
        }

        public void InsertRange(int index, IEnumerable<int> ids)
        {
            if (ids == null) return;
            foreach (var id in ids is DeckPile p ? new List<int>(p._ids) : new List<int>(ids))
                Insert(index++, id);
        }

        public void RemoveAt(int index)
        {
            var id = _ids[index];
            var info = _info?[index];
            _ids.RemoveAt(index);
            _info?.RemoveAt(index);
            _hasDeparted = true;
            _departedId = id;
            _departedInfo = info;
            _departedStamp = ++s_departureStamp;
        }

        /// <summary>
        /// Remove the copy at <paramref name="index"/> and return its metadata (null = ordinary) to the caller, which
        /// carries it (<see cref="TextEffects.PerCopyDeckRuntime.TakeFromDeck"/>, looking at the top cards and putting
        /// them back). Nothing is remembered for a later instance.
        /// </summary>
        public DeckCopyInfo TakeAt(int index)
        {
            var info = _info?[index];
            _ids.RemoveAt(index);
            _info?.RemoveAt(index);
            ForgetDeparted();
            return info;
        }

        public bool Remove(int id)
        {
            var i = _ids.IndexOf(id);
            if (i < 0) return false;
            RemoveAt(i);
            return true;
        }

        public int RemoveAll(Predicate<int> match)
        {
            if (match == null) return 0;
            var n = 0;
            for (var i = _ids.Count - 1; i >= 0; i--)
            {
                if (!match(_ids[i])) continue;
                _ids.RemoveAt(i);
                _info?.RemoveAt(i);
                n++;
            }

            ForgetDeparted();
            return n;
        }

        public void RemoveRange(int index, int count)
        {
            _ids.RemoveRange(index, count);
            _info?.RemoveRange(index, count);
            ForgetDeparted();
        }

        public void Clear()
        {
            _ids.Clear();
            _info = null;
            ForgetDeparted();
        }

        public bool Contains(int id) => _ids.Contains(id);
        public int IndexOf(int id) => _ids.IndexOf(id);
        public int IndexOf(int id, int index) => _ids.IndexOf(id, index);
        public int LastIndexOf(int id) => _ids.LastIndexOf(id);
        public int FindIndex(Predicate<int> match) => _ids.FindIndex(match);
        public int FindLastIndex(Predicate<int> match) => _ids.FindLastIndex(match);
        public int Find(Predicate<int> match) => _ids.Find(match);
        public bool Exists(Predicate<int> match) => _ids.Exists(match);
        public bool TrueForAll(Predicate<int> match) => _ids.TrueForAll(match);
        public List<int> GetRange(int index, int count) => _ids.GetRange(index, count);
        public int[] ToArray() => _ids.ToArray();
        public void CopyTo(int[] array, int arrayIndex) => _ids.CopyTo(array, arrayIndex);

        // ───────────────────────── per-copy metadata ─────────────────────────

        /// <summary>Insert <paramref name="id"/> at <paramref name="index"/> with <paramref name="info"/> (null = ordinary).</summary>
        public void Insert(int index, int id, DeckCopyInfo info)
        {
            ForgetDeparted();
            InsertCore(index, id, info);
        }

        /// <summary>Add <paramref name="id"/> at the bottom with <paramref name="info"/> (null = ordinary).</summary>
        public void Add(int id, DeckCopyInfo info) => Insert(_ids.Count, id, info);

        /// <summary>The metadata of the copy at <paramref name="index"/>, or null for an ordinary copy.</summary>
        public DeckCopyInfo InfoAt(int index) =>
            _info != null && index >= 0 && index < _info.Count ? _info[index] : null;

        /// <summary>True when some copy carries metadata (a face-up or foreign card).</summary>
        public bool HasSpecialCopies
        {
            get
            {
                if (_info == null) return false;
                foreach (var i in _info)
                    if (i != null) return true;
                return false;
            }
        }

        /// <summary>Swap two copies with their metadata (<see cref="DuelRng.Shuffle(DeckPile)"/>).</summary>
        public void Swap(int a, int b)
        {
            if (a == b) return;
            (_ids[a], _ids[b]) = (_ids[b], _ids[a]);
            if (_info != null) (_info[a], _info[b]) = (_info[b], _info[a]);
        }

        /// <summary>
        /// When this pile last gave up a copy of <paramref name="id"/> that is still unclaimed (a stamp increasing across
        /// both players' piles), or 0.
        /// </summary>
        public long DepartedStamp(int id) => _hasDeparted && _departedId == id ? _departedStamp : 0;

        /// <summary>
        /// The metadata of the copy of <paramref name="id"/> this pile last gave up, if nothing else changed the pile
        /// since (null for an ordinary copy); it is handed over once.
        /// </summary>
        public DeckCopyInfo TakeDeparted(int id)
        {
            if (!_hasDeparted || _departedId != id) return null;
            var info = _departedInfo;
            ForgetDeparted();
            return info;
        }

        /// <summary>
        /// Forget the copy this pile last gave up (<see cref="TextEffects.PerCopyDeckRuntime.ClaimDeckCopy"/>: a removal
        /// no instance claimed right away must never reach a later, unrelated one).
        /// </summary>
        public void ForgetDepartedCopy() => ForgetDeparted();

        void ForgetDeparted()
        {
            _hasDeparted = false;
            _departedId = 0;
            _departedInfo = null;
            _departedStamp = 0;
        }

        void InsertCore(int index, int id, DeckCopyInfo info)
        {
            if (info != null && _info == null)
            {
                _info = new List<DeckCopyInfo>(_ids.Count + 1);
                for (var i = 0; i < _ids.Count; i++) _info.Add(null);
            }

            _ids.Insert(index, id);
            _info?.Insert(index, info);
        }
    }
}
