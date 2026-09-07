using System;
using System.Collections.Generic;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Engine → presentation bus for coin tosses and die rolls (Time Wizard, Barrel
    /// Dragon, "Zorc"-style d6 effects, etc.). Every result produced by
    /// <see cref="DuelRng"/> is reported here so the AR arena can raise a coin/dice
    /// hologram, tumble it, and land on the actual result. Mirrors the pattern of
    /// <see cref="CardShatterPresentation"/>: the rules engine pushes; presentation
    /// consumes (via <see cref="OnResult"/> or <see cref="TryDequeue"/>).
    ///
    /// Reporting here never affects rules resolution — the outcome is already decided
    /// by <see cref="DuelRng"/>; this only drives visuals and gives tests a hook.
    /// </summary>
    public struct CoinDiceEvent
    {
        /// <summary>True = six-sided die (<see cref="DieFace"/>); false = coin (<see cref="Heads"/>).</summary>
        public bool IsDie;
        public bool Heads;    // coin result
        public int DieFace;   // 1–6
        /// <summary>Best-effort: the side that tossed/rolled (for placement).</summary>
        public bool PlayerSide;
        /// <summary>Source card / effect name for the log line.</summary>
        public string Label;
        public float QueuedUnscaledTime;
        public int Serial;

        public string ResultText => IsDie ? DieFace.ToString() : (Heads ? "Heads" : "Tails");
    }

    public static class CoinDicePresentation
    {
        static readonly List<CoinDiceEvent> _queue = new();
        static int _serial;

        /// <summary>Immediate hook so AR can spawn a hologram the moment a result is produced.</summary>
        public static event Action<CoinDiceEvent> OnResult;

        // Lightweight introspection for tests / HUD (no queue drain).
        public static int TotalCoins { get; private set; }
        public static int TotalDice { get; private set; }
        public static bool HasLast { get; private set; }
        public static CoinDiceEvent Last { get; private set; }

        public static void ReportCoin(bool heads, bool playerSide = true, string label = null)
        {
            var e = new CoinDiceEvent
            {
                IsDie = false,
                Heads = heads,
                PlayerSide = playerSide,
                Label = label,
                QueuedUnscaledTime = NowUnscaled(),
                Serial = ++_serial,
            };
            TotalCoins++;
            Enqueue(e);
        }

        public static void ReportDie(int face, bool playerSide = true, string label = null)
        {
            var f = face < 1 ? 1 : face > 6 ? 6 : face;
            var e = new CoinDiceEvent
            {
                IsDie = true,
                DieFace = f,
                PlayerSide = playerSide,
                Label = label,
                QueuedUnscaledTime = NowUnscaled(),
                Serial = ++_serial,
            };
            TotalDice++;
            Enqueue(e);
        }

        static void Enqueue(CoinDiceEvent e)
        {
            Last = e;
            HasLast = true;
            _queue.Add(e);
            // Cap so a long headless run cannot grow unbounded if nothing drains.
            if (_queue.Count > 64) _queue.RemoveAt(0);
            OnResult?.Invoke(e);
        }

        public static bool HasQueued => _queue.Count > 0;

        public static bool TryDequeue(out CoinDiceEvent e)
        {
            if (_queue.Count == 0) { e = default; return false; }
            e = _queue[0];
            _queue.RemoveAt(0);
            return true;
        }

        public static void Clear()
        {
            _queue.Clear();
            TotalCoins = 0;
            TotalDice = 0;
            HasLast = false;
            Last = default;
        }

        static float NowUnscaled() => UnityEngine.Time.unscaledTime;
    }
}
