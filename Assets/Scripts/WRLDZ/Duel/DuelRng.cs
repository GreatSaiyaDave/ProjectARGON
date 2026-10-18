using System;
using System.Collections.Generic;

namespace WRLDZ.Duel
{
    /// <summary>
    /// The engine's only source of randomness (R1): deck and hand shuffles, coin tosses, die rolls and
    /// random picks all draw from this one stream, owned per <see cref="DuelEngine"/> — never from
    /// <c>UnityEngine.Random</c> or a shared <see cref="System.Random"/>. Same seed + same decisions =
    /// same duel, on any runtime (Mono, IL2CPP, CoreCLR): the generator is plain integer arithmetic
    /// (SplitMix64-seeded xoshiro128**), so no platform RNG decides a stream.
    /// Tests queue results so resolution is deterministic; queued coin / die faces come first.
    /// Heads = true, Tails = false. Die faces are 1–6.
    /// </summary>
    public sealed class DuelRng
    {
        readonly Queue<bool> _coins = new();
        readonly Queue<int> _dice = new();
        readonly Queue<int> _picks = new();
        uint _s0, _s1, _s2, _s3;
        bool _seededExplicitly;

        /// <summary>Unseeded: a fresh entropy seed (see <see cref="NewSeed"/>), recorded in <see cref="Seed"/>.</summary>
        public DuelRng() : this(NewSeed()) => _seededExplicitly = false;

        public DuelRng(int seed) => Reseed(seed);

        /// <summary>Seed of the current stream (log it with the intent log to replay a duel).</summary>
        public int Seed { get; private set; }

        /// <summary>
        /// Values drawn since the last (re)seed. Replays compare this with the recorded count to catch a
        /// stray draw early.
        /// </summary>
        public long Draws { get; private set; }

        /// <summary>
        /// Seed live coin / die / random-pick results and shuffles (stress runs, AI training, replays) so a
        /// seeded duel reproduces exactly; queued test faces still come first (DR-16). Called before
        /// <see cref="DuelEngine.StartDuel(WRLDZ.Data.CardDatabase, WRLDZ.Data.DeckFile, WRLDZ.Data.DeckFile)"/>
        /// it also decides the opening deck shuffle (the engine keeps an explicit seed for that one start).
        /// </summary>
        public void Reseed(int seed)
        {
            Seed = seed;
            Draws = 0;
            _seededExplicitly = true;
            // SplitMix64 expands the 32-bit seed into the 128-bit xoshiro state (never all zero).
            var x = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
            ulong Next64()
            {
                x = unchecked(x + 0x9E3779B97F4A7C15UL);
                var z = x;
                z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
                z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
                return z ^ (z >> 31);
            }

            var a = Next64();
            var b = Next64();
            _s0 = (uint)a;
            _s1 = (uint)(a >> 32);
            _s2 = (uint)b;
            _s3 = (uint)(b >> 32);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        /// <summary>
        /// True once per explicit <see cref="Reseed"/>: <see cref="DuelEngine.StartDuel(WRLDZ.Data.CardDatabase, WRLDZ.Data.DeckFile, WRLDZ.Data.DeckFile)"/>
        /// keeps that seed for its shuffle instead of drawing a fresh one.
        /// </summary>
        internal bool ConsumeExplicitSeed()
        {
            var was = _seededExplicitly;
            _seededExplicitly = false;
            return was;
        }

        /// <summary>
        /// A fresh seed from process entropy for a duel that was not given one (Unity: every new duel and
        /// every restart shuffles differently, as before R1). Never used by rules code mid-duel.
        /// </summary>
        public static int NewSeed() => SeedSource != null ? SeedSource() : Guid.NewGuid().GetHashCode();

        /// <summary>
        /// Test harnesses only: when set, every unseeded <see cref="DuelRng"/> takes its seed from this sequence
        /// instead of process entropy, so a whole suite run is reproducible (headless.sh --seed N). With entropy
        /// seeds, a check whose outcome depends on a shuffle or a random pick passed or failed at random (Timeater,
        /// Curse of Fiend, 2026-10-18). The game never sets it.
        /// </summary>
        public static Func<int> SeedSource;

        /// <summary>
        /// One-shot presentation context for the next toss/roll (which side, source name).
        /// Set by the effect resolver just before tossing so the arena hologram appears
        /// on the correct side; reported results always flow to <see cref="CoinDicePresentation"/>.
        /// </summary>
        public bool PresentationPlayerSide = true;
        public string PresentationLabel;

        public void SetPresentationContext(bool playerSide, string label)
        {
            PresentationPlayerSide = playerSide;
            PresentationLabel = label;
        }

        public void QueueCoin(bool heads) => _coins.Enqueue(heads);

        public void QueueDie(int face)
        {
            var n = face < 1 ? 1 : face > 6 ? 6 : face;
            _dice.Enqueue(n);
        }

        /// <summary>Queue the index the next <see cref="PickIndex"/> returns (tests).</summary>
        public void QueuePick(int index) => _picks.Enqueue(index < 0 ? 0 : index);

        public void ClearQueues()
        {
            _coins.Clear();
            _dice.Clear();
            _picks.Clear();
        }

        /// <summary>
        /// Uniform random index in [0, count) — random discards (White Magical Hat /
        /// Robbin' Goblin). Queued picks are clamped into range. Returns -1 if count ≤ 0.
        /// </summary>
        public int PickIndex(int count)
        {
            if (count <= 0) return -1;
            if (_picks.Count > 0)
            {
                var q = _picks.Dequeue();
                return q >= count ? count - 1 : q;
            }

            return Next(count);
        }

        public bool TossCoin()
        {
            var heads = _coins.Count > 0 ? _coins.Dequeue() : NextBelow(2) == 1;
            CoinDicePresentation.ReportCoin(heads, PresentationPlayerSide, PresentationLabel);
            return heads;
        }

        public int TossCoinsCountHeads(int n)
        {
            var heads = 0;
            for (var i = 0; i < n; i++)
                if (TossCoin()) heads++;
            return heads;
        }

        public int RollDie()
        {
            var face = _dice.Count > 0 ? _dice.Dequeue() : 1 + (int)NextBelow(6);
            CoinDicePresentation.ReportDie(face, PresentationPlayerSide, PresentationLabel);
            return face;
        }

        /// <summary>Uniform index in [0, exclusiveMax). Returns 0 if exclusiveMax ≤ 0.</summary>
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) return 0;
            return (int)NextBelow((uint)exclusiveMax);
        }

        /// <summary>Fisher–Yates shuffle in place (deck shuffles, hand shuffles). No-op below 2 items.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// The list shuffle for a <see cref="List{T}"/> argument (PerCopyDeck batch, v163: a List&lt;int&gt; also converts
        /// to <see cref="DeckPile"/>, so without this overload <c>Shuffle(listOfInts)</c> would be ambiguous).
        /// </summary>
        public void Shuffle<T>(List<T> list) => Shuffle((IList<T>)list);

        /// <summary>
        /// PerCopyDeck batch (v163): Fisher–Yates shuffle of a Deck. The same RNG calls as <see cref="Shuffle{T}"/>
        /// (seeded duels replay the same), and each copy's metadata (owner, face-up) moves with its id.
        /// </summary>
        public void Shuffle(DeckPile pile)
        {
            if (pile == null) return;
            for (var i = pile.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                pile.Swap(i, j);
            }
        }

        /// <summary>Unbiased value in [0, bound) (bitmask rejection; bound ≥ 1).</summary>
        uint NextBelow(uint bound)
        {
            if (bound <= 1) return 0;
            var mask = bound - 1;
            mask |= mask >> 1;
            mask |= mask >> 2;
            mask |= mask >> 4;
            mask |= mask >> 8;
            mask |= mask >> 16;
            uint v;
            do v = NextUInt() & mask;
            while (v >= bound);
            return v;
        }

        /// <summary>xoshiro128** step.</summary>
        uint NextUInt()
        {
            Draws++;
            var result = RotL(_s1 * 5, 7) * 9;
            var t = _s1 << 9;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotL(_s3, 11);
            return result;
        }

        static uint RotL(uint x, int k) => (x << k) | (x >> (32 - k));
    }
}
