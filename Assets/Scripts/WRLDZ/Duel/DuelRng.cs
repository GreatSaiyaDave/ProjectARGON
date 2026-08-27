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
            if (_coins.Count > 0) return _coins.Dequeue();
            return _live.Next(0, 2) == 1;
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
            if (_dice.Count > 0) return _dice.Dequeue();
            return _live.Next(1, 7);
        }
    }
}
