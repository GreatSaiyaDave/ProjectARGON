using System.Text;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Coin toss / die roll mechanic + the engine→arena presentation bus
    /// (<see cref="CoinDicePresentation"/>). Verifies deterministic results (queued),
    /// that every toss/roll is reported for the hologram, multi-coin counting, and
    /// die-face clamping. The AR hologram (<c>ArCoinDiceFx</c>) consumes this bus.
    /// </summary>
    public static class CoinDiceRegressionTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; sb.AppendLine("PASS  " + name); }
                else { fail++; sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
            }

            var rng = new DuelRng();

            // ── Deterministic coin (queued) + reported to the presentation bus ──
            CoinDicePresentation.Clear();
            rng.QueueCoin(true);
            rng.QueueCoin(false);
            var c1 = rng.TossCoin();
            var c2 = rng.TossCoin();
            Check("Coin: queued results are deterministic (heads, tails)", c1 && !c2);
            Check("Coin: both tosses reported to bus", CoinDicePresentation.TotalCoins == 2,
                $"coins={CoinDicePresentation.TotalCoins}");
            Check("Coin: last reported result is tails", CoinDicePresentation.HasLast &&
                !CoinDicePresentation.Last.IsDie && !CoinDicePresentation.Last.Heads);

            // Drain the queue in order (FIFO) — this is what the arena consumes.
            var ok1 = CoinDicePresentation.TryDequeue(out var e1);
            var ok2 = CoinDicePresentation.TryDequeue(out var e2);
            Check("Coin: bus dequeues in toss order",
                ok1 && ok2 && e1.Heads && !e2.Heads && !e1.IsDie && !e2.IsDie);
            Check("Coin: bus empty after draining", !CoinDicePresentation.HasQueued);

            // ── Multi-coin count (Barrel Dragon-style: 3 coins) ──
            CoinDicePresentation.Clear();
            rng.QueueCoin(true); rng.QueueCoin(true); rng.QueueCoin(false);
            var heads = rng.TossCoinsCountHeads(3);
            Check("Coin: 3-coin toss counts 2 heads", heads == 2, $"heads={heads}");
            Check("Coin: 3 coins each reported", CoinDicePresentation.TotalCoins == 3,
                $"coins={CoinDicePresentation.TotalCoins}");

            // ── Deterministic die (queued) + reporting + clamping ──
            CoinDicePresentation.Clear();
            rng.QueueDie(4);
            rng.QueueDie(9);   // out of range → clamped to 6
            rng.QueueDie(0);   // out of range → clamped to 1
            var d1 = rng.RollDie();
            var d2 = rng.RollDie();
            var d3 = rng.RollDie();
            Check("Die: queued face is exact (4)", d1 == 4, $"d1={d1}");
            Check("Die: face clamps high to 6", d2 == 6, $"d2={d2}");
            Check("Die: face clamps low to 1", d3 == 1, $"d3={d3}");
            Check("Die: all rolls reported to bus", CoinDicePresentation.TotalDice == 3,
                $"dice={CoinDicePresentation.TotalDice}");
            Check("Die: last reported face is 1", CoinDicePresentation.HasLast &&
                CoinDicePresentation.Last.IsDie && CoinDicePresentation.Last.DieFace == 1);

            // ── Live RNG still reports and stays in range ──
            CoinDicePresentation.Clear();
            var live = new DuelRng();
            var inRange = true;
            for (var i = 0; i < 30; i++)
            {
                var f = live.RollDie();
                if (f < 1 || f > 6) inRange = false;
            }
            Check("Die: live rolls stay in 1..6", inRange);
            Check("Die: live rolls all reported", CoinDicePresentation.TotalDice == 30,
                $"dice={CoinDicePresentation.TotalDice}");

            // ── Presentation context (side/label) rides along to the event ──
            CoinDicePresentation.Clear();
            rng.SetPresentationContext(playerSide: false, label: "Barrel Dragon");
            rng.QueueCoin(true);
            rng.TossCoin();
            CoinDicePresentation.TryDequeue(out var ctxEvt);
            Check("Bus: event carries side + label context",
                !ctxEvt.PlayerSide && ctxEvt.Label == "Barrel Dragon");

            // ── End-to-end via the engine: a queued die drives Zorc-style resolution ──
            var db = CardDatabase.Load();
            if (db != null && db.Count > 0)
            {
                CoinDicePresentation.Clear();
                var engine = new DuelEngine();
                engine.StartDuel(db, CardDatabase.LoadDeck("lab_rules_player.json"),
                    CardDatabase.LoadDeck("lab_rules_ai.json"), cinematicOpening: false);
                // StartDuel clears the bus; a queued roll after start should report cleanly.
                engine.Rng.QueueDie(3);
                var roll = engine.Rng.RollDie();
                Check("Engine: queued die rolls 3 and reports", roll == 3 &&
                    CoinDicePresentation.TotalDice == 1 && CoinDicePresentation.Last.DieFace == 3);
            }

            CoinDicePresentation.Clear();
            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }
    }
}
