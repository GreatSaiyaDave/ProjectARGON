using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// MfcSimpleSpells batch (v168) — honest pins (2026-10-07 audit).
    ///
    /// The v168 version of this file asserted six cards Implemented against placeholder ids that are
    /// not in cards_db at all, and its atoms were written against invented card texts, so every check
    /// failed at HEAD (the suite read 5839/6). This rewrite uses the real ids and printed texts:
    /// <list type="bullet">
    /// <item>Dark Magician Girl (38033121) — Implemented: "Gains 300 ATK for every "Dark Magician" or
    /// "Magician of Black Chaos" in the GY." (real-text atom in MfcLabCards3Atoms; ATK only, no DEF).</item>
    /// <item>Emblem of Dragon Destroyer (6390406), Hidden Spellbook (21840375) — Implemented by the
    /// MfcSimpleSpellsB batch (v174): real-text atoms plus mini-duels in
    /// MfcSimpleSpellsBRegressionTests. Their pins moved there.</item>
    /// <item>Mass Driver (34906152) is Implemented by the PhaseTriggersAndIgnitionCosts batch (its pins live in
    /// PhaseTriggersAndIgnitionCostsRegressionTests). Anti-Spell (53112492) is Implemented whole by the
    /// SpellCounterSpenders batch (its cost fragment still never compiles on its own).</item>
    /// <item>Thunder of Ruler (91781589) — stays fail-closed on purpose: its only timing is the
    /// opponent's Standby Phase and the engine has no activation path for a Set Normal Trap on that
    /// timing (MfcSimpleSpellsBAtoms documents why). Queued until a Standby priority window for the
    /// non-turn player exists.</item>
    /// </list>
    /// </summary>
    public static class MfcSimpleSpellsRegressionTests
    {
        const int DarkMagicianGirl = 38033121;
        const int ThunderOfRuler = 91781589;
        const int AntiSpell = 53112492;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("MfcSimpleSpells: " + n, ok, d ?? "");

            CompileShapes(db, Check);
        }

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            (CompiledCardProgram prog, CardEffectStatusKind st) Of(int id)
            {
                var def = db.Get(id);
                var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
                return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented);
            }

            void StaysFailClosed(string name, int id)
            {
                var (p, st) = Of(id);
                Check($"{name}: stays fail-closed (real text, no atom yet — not FullyCompiled, not Implemented)",
                    p != null && !p.FullyCompiled && st != CardEffectStatusKind.Implemented &&
                    st != CardEffectStatusKind.Stub,
                    $"status={st} {Describe(p)}");
            }

            {
                // The only one of the six with a real-text atom (MfcLabCards3Atoms): ATK only, both GYs.
                var (p, st) = Of(DarkMagicianGirl);
                var clauses = p?.ClausesFor(EffectTiming.ContinuousWhileFaceUp) ?? new System.Collections.Generic.List<EffectClause>();
                Check("Dark Magician Girl: Implemented — continuous +300 ATK per \"Dark Magician\" or \"Magician of Black Chaos\" in the GY (ATK only)",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    clauses.Exists(c => c.Action == EffectActionKind.GainAtkDefPerNamedInBothGyWhileFaceUp &&
                                        c.Amount == 300 && c.DefAmount == 0 &&
                                        c.NamedCard == "Dark Magician" &&
                                        c.AltNamedCard == "Magician of Black Chaos"),
                    $"status={st} {Describe(p)}");
            }

            // Mass Driver: Implemented by the PhaseTriggersAndIgnitionCosts batch (its pins moved there).
            StaysFailClosed("Thunder of Ruler (opp Standby activation; no Battle Phase this turn)", ThunderOfRuler);
            {
                // SpellCounterSpenders batch: Anti-Spell compiles whole (its own atom: the side-wide counter cost and the
                // Spell negation together); the mini-duels are in SpellCounterSpendersRegressionTests.
                var (p, st) = Of(AntiSpell);
                Check("Anti-Spell: Implemented whole by SpellCounterSpenders (2 counters from your side, negate a Spell activation)",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && p.ClauseList.Count == 1 &&
                    p.ClauseList[0].Action == EffectActionKind.NegateActivation &&
                    p.ClauseList[0].RemoveSpellCountersOnYourSide == 2,
                    $"status={st} {Describe(p)}");
            }
        }

        static string Describe(CompiledCardProgram p)
        {
            if (p == null) return "null";
            var unparsed = p.UnparsedFragments != null ? p.UnparsedFragments.Length : 0;
            return $"full={p.FullyCompiled} | clauses={p.ClauseList.Count} | unparsed={unparsed}";
        }
    }
}
