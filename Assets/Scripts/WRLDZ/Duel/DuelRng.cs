using System;
using System.Collections.Generic;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Coin toss / six-sided die for official effects. Tests queue results so resolution
    /// is deterministic; live play uses a seeded RNG.
    /// Heads = true, Tails = false. Die faces are 1–6.
    /// </summary>
    public sealed class DuelRng
    {
        readonly Queue<bool> _coins = new();
        readonly Queue<int> _dice = new();
        readonly Random _live = new();

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

        public void ClearQueues()
        {
            _coins.Clear();
            _dice.Clear();
        }

        public bool TossCoin()
        {
            var heads = _coins.Count > 0 ? _coins.Dequeue() : _live.Next(0, 2) == 1;
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
            var face = _dice.Count > 0 ? _dice.Dequeue() : _live.Next(1, 7);
            CoinDicePresentation.ReportDie(face, PresentationPlayerSide, PresentationLabel);
            return face;
        }
    }
}
