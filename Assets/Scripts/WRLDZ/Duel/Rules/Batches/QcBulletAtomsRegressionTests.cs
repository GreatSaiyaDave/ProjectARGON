using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// v208 QC (bullet atoms): compile-shape pins for the cards that the unanchored Fuh-Rin-Ka-Zan bullet atoms
    /// (SimpleSpellTrapsAtoms: "Draw N cards.", "Discard N random cards from your opponent's hand", "Destroy all
    /// monsters / Spell and Trap Cards your opponent controls.") and the unfenced B.E.S. counter atom had over-matched.
    /// Matching is case-insensitive, so the tail of another card's sentence ("… to draw 3 cards.") added a bogus extra
    /// Activate clause; Wattberyx and X-Saber Airbellum (Implemented) gained a fake ignition discard. The atoms now
    /// match only a whole standalone bullet text / the B.E.S. shape.
    /// <list type="bullet">
    /// <item>Each pinned card has exactly its own clause list (no bare Activate Draw / DiscardRandom).</item>
    /// <item>Fuh-Rin-Ka-Zan ("Apply 1 of these effects. ● … ● … ● … ● …") is a choice: not FullyCompiled.</item>
    /// <item>Miracle Jurassic Egg is not compiled from a lone counter placement (it was FullyCompiled).</item>
    /// </list>
    /// </summary>
    public static class QcBulletAtomsRegressionTests
    {
        const int ThreeHumpLacooda = 86988864;
        const int BeeListSoldier = 1474910;
        const int Wattberyx = 5554990;
        const int XSaberAirbellum = 90508760;
        const int FuhRinKaZan = 1781310;
        const int MiracleJurassicEgg = 63259351;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("QcBulletAtoms: " + n, ok, d ?? "");

            (CompiledCardProgram prog, CardEffectStatusKind st) Of(int id)
            {
                var def = db.Get(id);
                var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
                return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented);
            }

            void Exactly(string name, int id, int amount, params (EffectTiming timing, EffectActionKind action)[] want)
            {
                var (p, st) = Of(id);
                var got = p?.ClauseList?.Where(c => c != null).ToList() ?? new List<EffectClause>();
                var ok = p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                         got.Count == want.Length;
                for (var i = 0; ok && i < want.Length; i++)
                    ok = got[i].Timing == want[i].timing && got[i].Action == want[i].action && got[i].Amount == amount;
                Check($"{name}: exactly [{string.Join(", ", want.Select(w => w.timing + ":" + w.action))}] (no extra bogus clause)",
                    ok, $"status={st} {Describe(p)}");
            }

            Exactly("3-Hump Lacooda", ThreeHumpLacooda, 3, (EffectTiming.Activate, EffectActionKind.Draw));
            Exactly("Bee List Soldier", BeeListSoldier, 2, (EffectTiming.Activate, EffectActionKind.Draw));
            Exactly("Wattberyx", Wattberyx, 1,
                (EffectTiming.ThisCardInflictsBattleDamage, EffectActionKind.DiscardRandomFromOpponentHand));
            Exactly("X-Saber Airbellum", XSaberAirbellum, 1,
                (EffectTiming.ThisCardInflictsBattleDamage, EffectActionKind.DiscardRandomFromOpponentHand));

            {
                var (p, st) = Of(FuhRinKaZan);
                Check("Fuh-Rin-Ka-Zan: a choice (\"Apply 1 of these effects\") — not FullyCompiled, not Implemented",
                    p != null && !p.FullyCompiled && st != CardEffectStatusKind.Implemented &&
                    !p.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.DiscardRandomFromOpponentHand),
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(MiracleJurassicEgg);
                Check("Miracle Jurassic Egg: no lone counter-placement clause, not FullyCompiled",
                    p != null && !p.FullyCompiled && st != CardEffectStatusKind.Implemented &&
                    !p.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.PlaceCounterOnThisCard),
                    $"status={st} {Describe(p)}");
            }
        }

        static string Describe(CompiledCardProgram p)
        {
            if (p == null) return "null";
            var unparsed = p.UnparsedFragments != null ? p.UnparsedFragments.Length : 0;
            var clauses = string.Join(", ", p.ClauseList.Where(c => c != null).Select(c => c.Timing + ":" + c.Action));
            return $"full={p.FullyCompiled} | clauses=[{clauses}] | unparsed={unparsed}";
        }
    }
}
