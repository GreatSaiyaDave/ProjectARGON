using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Spell Counter group (v164). These Effect Monsters place a Spell Counter whenever a Spell resolves
    /// and spend the counters on a second effect.
    ///
    /// This locks the whole-sentence fix in <c>AdvancedEffectTemplates</c>. The Spell Counter anchors used
    /// to match only the middle of their sentence, so the leading "You can" / "This card" and the trailing
    /// period survived as residue: the card compiled its action, kept a "You can        ." fragment, and
    /// stayed a Stub. Anchors that do not span the whole sentence are exactly what the fail-closed rule
    /// exists to prevent, so every card here is checked for residue as well as for its clause shape.
    /// </summary>
    public static class SpellCounterRegressionTests
    {
        const int RoyalMagicalLibrary = 70791313;
        const int SkilledDarkMagician = 73752131;
        const int SkilledWhiteMagician = 46363422;
        const int MagicalMarionette = 8034697;
        const int AntiSpell = 53112492;

        public static void Run(CardDatabase db, Action<string, bool, string> check)
        {
            if (db == null) return;
            WholeCompile(db, check, RoyalMagicalLibrary, "Royal Magical Library");
            WholeCompile(db, check, SkilledDarkMagician, "Skilled Dark Magician");
            WholeCompile(db, check, SkilledWhiteMagician, "Skilled White Magician");
            WholeCompile(db, check, MagicalMarionette, "Magical Marionette");

            // Royal Magical Library: 1 counter per Spell resolve, max 3; remove 3 to draw 1.
            var rml = Comp(db, RoyalMagicalLibrary);
            var rmlPlace = Clause(rml, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.PlaceSpellCounters);
            check("RML: places 1 Spell Counter when a Spell resolves",
                rmlPlace != null && rmlPlace.PlaceCounterOnSpellActivate && rmlPlace.Amount == 1, Describe(rmlPlace));
            check("RML: max 3 counters", rmlPlace != null && rmlPlace.CounterMax == 3, Describe(rmlPlace));
            var rmlDraw = Clause(rml, EffectTiming.Activate, EffectActionKind.Draw);
            check("RML: remove 3 counters then draw 1",
                rmlDraw != null && rmlDraw.Amount == 1 && rmlDraw.RequiresRemoveSpellCounters == 3, Describe(rmlDraw));

            // The Specialists share one sentence; only the summoned monster differs. The zone is a printed
            // list ("from your hand, Deck, or GY" / "... or Graveyard") so the anchor must capture the
            // first zone of the list.
            var sdm = Comp(db, SkilledDarkMagician);
            var sdmSs = Clause(sdm, EffectTiming.Activate, EffectActionKind.SpecialSummonNamed);
            check("Skilled Dark Magician: tribute this card with 3 counters, SS Dark Magician from hand",
                sdmSs != null && sdmSs.RequiresTributeThis && sdmSs.RequiresSpellCounters == 3 &&
                sdmSs.NamedCard == "Dark Magician" && sdmSs.FromHand, Describe(sdmSs));

            var swm = Comp(db, SkilledWhiteMagician);
            var swmSs = Clause(swm, EffectTiming.Activate, EffectActionKind.SpecialSummonNamed);
            check("Skilled White Magician: SS Buster Blader (long 'Graveyard' spelling)",
                swmSs != null && swmSs.RequiresTributeThis && swmSs.RequiresSpellCounters == 3 &&
                swmSs.NamedCard == "Buster Blader", Describe(swmSs));

            // Magical Marionette prints no "(max. N)" on its counter sentence, gains 200 ATK per counter,
            // and removes 2 to destroy a MONSTER. The S/T anchor must not have swallowed it.
            var mm = Comp(db, MagicalMarionette);
            var mmPlace = Clause(mm, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.PlaceSpellCounters);
            check("Magical Marionette: places a counter with no printed max",
                mmPlace != null && mmPlace.PlaceCounterOnSpellActivate, Describe(mmPlace));
            // Marionette prints no "(max. N)". CounterMax 0 means uncapped at runtime; a positive value
            // here would clamp its counters and silently freeze the 200-per-counter ATK growth. This is
            // the regression guard for making the group optional without inventing a cap the card lacks.
            check("Magical Marionette: no invented counter cap (uncapped at runtime)",
                mmPlace != null && mmPlace.CounterMax == 0,
                mmPlace == null ? "no clause" : $"CounterMax={mmPlace.CounterMax}");
            var mmGain = Clause(mm, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.GainAtkPerSpellCounter);
            check("Magical Marionette: gains 200 ATK per Spell Counter",
                mmGain != null && mmGain.Amount == 200, Describe(mmGain));
            var mmDestroy = Clause(mm, EffectTiming.Activate, EffectActionKind.Destroy);
            check("Magical Marionette: remove 2 counters then destroy a target monster",
                mmDestroy != null && mmDestroy.RequiresTargetChoice &&
                mmDestroy.Zone == EffectZoneFilter.FieldAnyMonster &&
                mmDestroy.RequiresRemoveSpellCounters == 2, Describe(mmDestroy));

            // Fail-closed: a counter spend is never free. If a cost sentence is not recognised the card
            // must stay uncompiled rather than resolving for 0 counters.
            check("counter spends never resolve without a printed cost",
                rmlDraw == null || rmlDraw.RequiresRemoveSpellCounters == 3, Describe(rmlDraw));

            // Anti-Spell shares the cost shape but its sentence carries a negate rider the anchors do
            // not cover, so it must STAY fail-closed: the whole-sentence anchors must not rescue a
            // partially understood card by matching its cost fragment. WholeResolutionRegressionTests
            // already asserts the same for Anti-Spell, Miracle Restoring and Blast Magician; this
            // asserts it from the Spell Counter side so widening an anchor cannot silently flip them.
            var anti = Comp(db, AntiSpell);
            check("Anti-Spell stays fail-closed (cost fragment must not compile on its own)",
                anti != null && !anti.FullyCompiled && anti.ClauseList.Count == 0,
                anti == null ? "null" : $"full={anti.FullyCompiled} clauses={anti.ClauseList.Count}");
        }

        static CompiledCardProgram Comp(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static EffectClause Clause(CompiledCardProgram prog, EffectTiming timing, EffectActionKind action)
        {
            if (prog == null) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Timing == timing && c.Action == action) return c;
            return null;
        }

        static void WholeCompile(CardDatabase db, Action<string, bool, string> check, int id, string name)
        {
            var prog = Comp(db, id);
            if (prog == null)
            {
                check($"{name}: compiles", false, "no program");
                return;
            }
            var unparsed = prog.UnparsedFragments ?? Array.Empty<string>();
            check($"{name}: no residue fragments",
                unparsed.Length == 0,
                unparsed.Length == 0 ? "clean" : string.Join(" | ", unparsed));
            check($"{name}: FullyCompiled", prog.FullyCompiled,
                prog.FullyCompiled ? "full" : string.Join(" | ", prog.ClauseList.Count + " clauses"));
        }

        static string Describe(EffectClause c) => c == null ? "no clause" : EffectVocabulary.Describe(c);
    }
}