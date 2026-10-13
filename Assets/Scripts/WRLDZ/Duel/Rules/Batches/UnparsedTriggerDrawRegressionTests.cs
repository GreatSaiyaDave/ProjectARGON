using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "UnparsedTriggerDraw": the generic PSCT draw fallback (<c>draw (\d+) cards?</c> in
    /// CardTextEffectCompiler.CompilePsctSentence) kept only the draw of a sentence whose trigger sat in the blue text.
    /// Card of Safe Return ("When a monster is Special Summoned from your Graveyard, you can draw 1 card.") has no colon,
    /// so nothing split the trigger off. It compiled to one Activate Draw 1: activating the Continuous Spell drew at
    /// once, and a Special Summon from the GY never drew. A fallback draw whose resolution text opens with
    /// "When / If / Each time / Every time" is now refused, so Card of Safe Return and "7" fail closed and the deck gate
    /// keeps them out. Flat draws behind a colon (Airknight Parshath, Des Lacooda) compile exactly as before.
    /// Since the NegationsAndGySummons batch (v159) a whole-sentence atom compiles Card of Safe Return to its real
    /// trigger (DrawWhenMonsterSpecialSummonedFromYourGy): it activates, and its activation still draws nothing.
    /// </summary>
    public static class UnparsedTriggerDrawRegressionTests
    {
        const int CardOfSafeReturn = 57953380;  // Continuous Spell: draw 1 when a monster is Special Summoned from your GY
        const int Seven = 67048711;             // "7", Continuous Spell: draw 3 while 3 face-up "7", then destroy them
        const int AirknightParshath = 18036057; // "If this card inflicts battle damage to your opponent: Draw 1 card."
        const int DesLacooda = 2326738;         // "When this card is Flip Summoned: Draw 1 card."
        const int PotOfGreed = 55144522;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("UnparsedTriggerDraw: " + name, ok, detail ?? "");

            CorpusShapes(db, Check);
            NewCardRule(Check);
            ActivationRefused(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static bool HasDraw(CompiledCardProgram prog) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Draw);

        static string Shape(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram CompileText(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Near-miss trigger draw " + id, type = type, race = race, desc = text
            });

        static EffectClause OnlyDraw(CompiledCardProgram prog) =>
            prog?.ClauseList.Find(c => c != null && c.Action == EffectActionKind.Draw);

        // ───────────────────────────── checks ─────────────────────────────

        static void CorpusShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            // NegationsAndGySummons batch (v159): Card of Safe Return's sentence is compiled whole by a batch atom to
            // its draw trigger (DrawWhenMonsterSpecialSummonedFromYourGy); it still has no bare Draw clause.
            {
                var def = db.Get(CardOfSafeReturn);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Card of Safe Return: no bare Draw clause — a face-up draw trigger (NegationsAndGySummons)",
                    def != null && prog != null && prog.FullyCompiled && !HasDraw(prog) &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                                c.Action == EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy),
                    Shape(prog));
            }

            foreach (var (id, label) in new[] { (Seven, "\"7\"") })
            {
                // GyHandAndSummonEventTriggers batch (v178): "7"'s sentence 1-2 compiles whole by its batch atom to
                // DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed (draw only while N face-up copies, then destroy
                // them), so it is FullyCompiled, Implemented and deck-legal; it never falls back to a bare Draw.
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var drawClause = prog?.ClauseList.Find(c => c != null && c.Action ==
                    EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed);
                Check($"{label}: FullyCompiled by its batch atom, with no bare Draw clause",
                    def != null && prog != null && prog.FullyCompiled && !HasDraw(prog) && drawClause != null,
                    Shape(prog));
                var status = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check($"{label}: Implemented and the deck gate keeps it in",
                    def != null && status == CardEffectStatusKind.Implemented &&
                    CardEffectStatus.MayIncludeInDeck(def),
                    status.ToString());
            }

            // Positive controls: the same fallback still compiles a flat draw whose condition sits before a colon.
            {
                var def = db.Get(AirknightParshath);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var d = OnlyDraw(prog);
                Check("Airknight Parshath: still FullyCompiled with its ThisCardInflictsBattleDamage Draw 1",
                    prog != null && prog.FullyCompiled && d != null &&
                    d.Timing == EffectTiming.ThisCardInflictsBattleDamage && d.Amount == 1 &&
                    d.Side == EffectSide.Controller &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Shape(prog));
            }
            {
                var def = db.Get(DesLacooda);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var d = OnlyDraw(prog);
                Check("Des Lacooda: still FullyCompiled with its Flip-Summoned Draw 1",
                    prog != null && prog.FullyCompiled && d != null &&
                    d.Timing == EffectTiming.ThisCardSummoned && d.RequiresThisFlipSummoned && d.Amount == 1 &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Shape(prog));
            }
        }

        /// <summary>
        /// The rule for new cards: a fallback draw is kept only when no trigger or condition is left in its blue text.
        /// </summary>
        static void NewCardRule(Action<string, bool, string> Check)
        {
            var nearMisses = new[]
            {
                ("Spell Card", "Continuous", "When a monster is Normal Summoned, you can draw 1 card."),
                ("Spell Card", "Continuous", "Each time your opponent Special Summons a monster, draw 1 card."),
                ("Trap Card", "Continuous", "Every time a Spell Card is activated, draw 1 card."),
                ("Spell Card", "Normal", "If you control no monsters, draw 2 cards."),
            };
            var i = 0;
            foreach (var (type, race, text) in nearMisses)
            {
                var prog = CompileText(990501 + i++, type, race, text);
                Check($"near miss ({race} {type}) \"{text}\" is not compiled to a bare Draw",
                    prog != null && !prog.FullyCompiled && !HasDraw(prog),
                    Shape(prog));
            }

            var control = CompileText(990510, "Effect Monster", "Fairy",
                "If this card inflicts battle damage to your opponent: Draw 2 cards.");
            var cd = OnlyDraw(control);
            Check("control: \"If this card inflicts battle damage to your opponent: Draw 2 cards.\" still compiles",
                control != null && control.FullyCompiled && cd != null &&
                cd.Timing == EffectTiming.ThisCardInflictsBattleDamage && cd.Amount == 2,
                Shape(control));
        }

        /// <summary>In a duel Card of Safe Return's activation draws nothing (v159: it activates and stays face-up).</summary>
        static void ActivationRefused(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var deck = p.DeckCount;
                var card = PutInHand(engine, p, CardOfSafeReturn);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var tried = engine.TryActivateSpellTrap(p, card, fromHand: true);
                DrainCombat(engine);
                // NegationsAndGySummons batch (v159): it activates now, and its activation still draws nothing.
                Check("Card of Safe Return: activation from the hand draws nothing (it stays face-up)",
                    can && tried && p.Hand.Count == 0 && p.TryFindSpellTrap(card, out _) && card.FaceUp &&
                    p.DeckCount == deck,
                    $"can={can} tried={tried} hand={p.Hand.Count} deck={p.DeckCount} was {deck}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var deck = p.DeckCount;
                var set = PlaceSetTrap(engine, p, CardOfSafeReturn, 0);
                var can = engine.CanActivateSpellTrap(p, set, fromHand: false);
                var tried = engine.TryActivateSpellTrap(p, set, fromHand: false);
                DrainCombat(engine);
                Check("Card of Safe Return: a Set copy activates face-up and draws nothing",
                    can && tried && set.FaceUp && p.Hand.Count == 0 && p.DeckCount == deck,
                    $"can={can} tried={tried} faceUp={set.FaceUp} hand={p.Hand.Count} deck={p.DeckCount} was {deck}");
            }

            {
                // Positive control: the same helpers do activate a real draw Spell and draw.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var deck = p.DeckCount;
                var card = PutInHand(engine, p, PotOfGreed);
                var ok = engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                         engine.TryActivateSpellTrap(p, card, fromHand: true);
                DrainCombat(engine);
                Check("Pot of Greed: activation from the hand still draws 2",
                    ok && p.Hand.Count == 2 && p.DeckCount == deck - 2,
                    $"ok={ok} hand={p.Hand.Count} deck={p.DeckCount} was {deck}");
            }
        }
    }
}
