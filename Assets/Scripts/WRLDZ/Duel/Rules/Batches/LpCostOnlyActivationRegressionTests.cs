using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "LpCostOnlyActivation": the generic PSCT Activate path read an LP amount out of any sentence
    /// ("Each player must pay 500 Life Points per card to …", "Either player can pay 800 Life Points to negate …")
    /// and, when the rest of the sentence compiled to no action, kept a Continuous card's bare payment as its whole
    /// activation. Chain Energy, Royal Oppression, Weapon Change and Inspection were Implemented as "pay LP, do
    /// nothing". An Activate sentence with no compiled action is now refused, so those cards fail closed and the deck
    /// gate keeps them out. Toon World ("Activate this card by paying 1000 LP.") is a real whole-activation cost and
    /// compiles exactly as before. Chain Energy was later compiled whole by the RuleLocks batch (a continuous LP toll on
    /// hand actions, <see cref="RuleLocksRegressionTests"/>); here it is only checked to carry no bare LP payment.
    /// Inspection was compiled whole by the StandbyBurnsAndDice batch (v162: its 500 LP is the cost of an optional trigger
    /// in the opponent's Standby Phase, <see cref="StandbyBurnsAndDiceRegressionTests"/>); here it is checked to carry no
    /// bare LP payment and to cost nothing on activation.
    /// </summary>
    public static class LpCostOnlyActivationRegressionTests
    {
        const int ToonWorld = 15259703;       // Continuous Spell: "Activate this card by paying 1000 LP."
        const int ChainEnergy = 79323590;     // Continuous Spell: per-card LP tax on plays from the hand
        const int RoyalOppression = 93016201; // Continuous Trap: either player pays 800 to negate a Special Summon
        const int WeaponChange = 10035717;    // Continuous Spell: Standby pay 700, swap a monster's ATK/DEF
        const int Inspection = 16227556;      // Continuous Spell: opp Standby pay 500, look at a random hand card (v162)

        // Stubs whose only clause was the same bare payment; they now carry no clause at all.
        const int CardShuffle = 12183332;
        const int SenriEye = 60391791;
        const int SoulDemolition = 76297408;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("LpCostOnlyActivation: " + name, ok, detail ?? "");

            CorpusShapes(db, Check);
            NewCardRule(Check);
            ActivationRefused(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static bool HasBareLpClause(CompiledCardProgram prog) =>
            prog != null && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.None && c.PayLpAmount > 0);

        static string Shape(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram CompileText(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Near-miss LP cost " + id, type = type, race = race, desc = text
            });

        // ───────────────────────────── checks ─────────────────────────────

        static void CorpusShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            var toon = db.Get(ToonWorld);
            var toonProg = toon != null ? CardTextEffectCompiler.Compile(toon) : null;
            var tc = toonProg != null && toonProg.ClauseList.Count == 1 ? toonProg.ClauseList[0] : null;
            Check("Toon World: FullyCompiled, one Activate clause paying 1000 LP that stays on the field",
                toonProg != null && toonProg.FullyCompiled && tc != null &&
                tc.Timing == EffectTiming.Activate && tc.Action == EffectActionKind.None &&
                tc.PayLpAmount == 1000 && tc.StaysOnField && tc.MakesChainLink,
                Shape(toonProg));
            Check("Toon World: still Implemented and deck-legal",
                toon != null && CardEffectStatus.Classify(toon) == CardEffectStatusKind.Implemented &&
                CardEffectStatus.MayIncludeInDeck(toon),
                toon == null ? "missing" : CardEffectStatus.Classify(toon).ToString());

            // StandbyBurnsAndDice batch (v162): Inspection is Implemented — its 500 LP is the cost of its optional trigger
            // in the opponent's Standby Phase (PayLpLookAtRandomOppHandCard), never a bare payment on activation.
            {
                var def = db.Get(Inspection);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Inspection: no bare LP-payment clause; the 500 LP is its Standby trigger's cost",
                    def != null && prog != null && prog.FullyCompiled && !HasBareLpClause(prog) &&
                    prog.ClauseList.TrueForAll(c => c.PayLpAmount == 0) &&
                    prog.ClauseList.Exists(c => c.Action == EffectActionKind.PayLpLookAtRandomOppHandCard &&
                                                c.Timing == EffectTiming.StandbyPhase && c.Amount == 500),
                    Shape(prog));
            }

            {
                // LpLocksDelayedBurnAndGyNegation batch: Weapon Change's 700 LP is the cost of its Standby trigger; the
                // activation itself only places the card (no bare LP-payment clause).
                var def = db.Get(WeaponChange);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Weapon Change: no bare LP-payment clause; the 700 LP is its Standby trigger's cost",
                    def != null && prog != null && prog.FullyCompiled && !HasBareLpClause(prog) &&
                    prog.ClausesFor(EffectTiming.Activate).Count == 0 &&
                    prog.ClauseList.Exists(c =>
                        c.Action == EffectActionKind.SwitchTargetCurrentAtkDefUntilOpponentsNextEndPhase &&
                        c.Timing == EffectTiming.StandbyPhase && c.PayLpAmount == 700),
                    Shape(prog));
            }

            foreach (var (id, label) in new[]
                     {
                         (RoyalOppression, "Royal Oppression")
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label}: not FullyCompiled and no bare LP-payment clause",
                    def != null && prog != null && !prog.FullyCompiled && !HasBareLpClause(prog),
                    Shape(prog));
                var status = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check($"{label}: fails closed (not Implemented) and the deck gate keeps it out",
                    def != null && status != CardEffectStatusKind.Implemented &&
                    status != CardEffectStatusKind.Structural && !CardEffectStatus.MayIncludeInDeck(def),
                    status.ToString());
            }

            {
                // RuleLocks batch: Chain Energy is its continuous hand toll, never a payment on activation.
                var def = db.Get(ChainEnergy);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Chain Energy: compiled to the continuous hand toll, with no bare LP-payment clause",
                    prog != null && prog.FullyCompiled && !HasBareLpClause(prog) &&
                    prog.ClauseList.Exists(c => c.Action == EffectActionKind.HandActionsCostLp &&
                                                c.Timing == EffectTiming.ContinuousWhileFaceUp && c.PayLpAmount == 0),
                    Shape(prog));
            }

            {
                // PgdSpellTrapBasics batch (v162): Card Shuffle compiles whole — the 300 LP is the cost of its face-up
                // ignition (pay, then shuffle a chosen Deck), never a bare payment on the card's activation.
                var def = db.Get(CardShuffle);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Card Shuffle: the LP cost alone is no longer kept as a clause — it is the cost of its face-up ignition",
                    def != null && prog != null && prog.FullyCompiled && !HasBareLpClause(prog) &&
                    !prog.HasTiming(EffectTiming.Activate) && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.FaceUpFieldIgnition &&
                    prog.ClauseList[0].Action == EffectActionKind.ShuffleYourOrOpponentsDeck &&
                    prog.ClauseList[0].PayLpAmount == 300,
                    Shape(prog));
            }

            {
                // PhaseTriggersAndIgnitionCosts batch: Senri Eye compiles whole — the 100 LP is the cost of its optional
                // Standby trigger (pay, then look at the opponent's top card), never a bare payment on the activation.
                var def = db.Get(SenriEye);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Senri Eye: the LP cost alone is no longer kept as a clause — it is the cost of its Standby trigger",
                    def != null && prog != null && prog.FullyCompiled && !HasBareLpClause(prog) &&
                    !prog.HasTiming(EffectTiming.Activate) && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.StandbyPhase &&
                    prog.ClauseList[0].Action == EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby &&
                    prog.ClauseList[0].Amount == 100 && prog.ClauseList[0].PayLpAmount == 0,
                    Shape(prog));
            }

            {
                // FaceUpQuickEffects batch (v160): Soul Demolition compiles whole — the 500 LP is the cost of its face-up
                // Quick Effect (the GY banish), never a bare payment on the card's activation.
                var def = db.Get(SoulDemolition);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Soul Demolition: the LP cost belongs to its face-up Quick Effect, with no bare LP-payment clause",
                    def != null && prog != null && prog.FullyCompiled && !HasBareLpClause(prog) &&
                    !prog.HasTiming(EffectTiming.Activate) && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.FaceUpQuickEffect &&
                    prog.ClauseList[0].Action == EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish &&
                    prog.ClauseList[0].PayLpAmount == 500,
                    Shape(prog));
            }
        }

        /// <summary>
        /// The rule for new cards: only a sentence that is nothing but the activation payment compiles to a bare
        /// payment. An LP amount inside an effect the compiler did not understand never becomes the whole program.
        /// </summary>
        static void NewCardRule(Action<string, bool, string> Check)
        {
            var whole = CompileText(990401, "Spell Card", "Continuous", "Activate this card by paying 2000 Life Points.");
            Check("whole-sentence \"Activate this card by paying 2000 Life Points.\" compiles to a bare 2000 LP activation",
                whole != null && whole.FullyCompiled && whole.ClauseList.Count == 1 &&
                whole.ClauseList[0].Timing == EffectTiming.Activate && whole.ClauseList[0].PayLpAmount == 2000 &&
                whole.ClauseList[0].StaysOnField,
                Shape(whole));

            var nearMisses = new[]
            {
                ("Spell Card", "Continuous",
                    "Each player must pay 300 LP per card to activate Spell Cards from their hand."),
                ("Trap Card", "Continuous",
                    "Either player can pay 800 Life Points to make their opponent skip their next Draw Phase."),
                ("Spell Card", "Field",
                    "Each player can pay 500 Life Points to look at the top card of their opponent's Deck."),
                ("Spell Card", "Equip",
                    "The controller of the equipped monster must pay 200 Life Points to declare an attack with it."),
            };
            var i = 0;
            foreach (var (type, race, text) in nearMisses)
            {
                var prog = CompileText(990410 + i++, type, race, text);
                Check($"near miss ({race} {type}) \"{text}\" is not compiled to a bare LP payment",
                    prog != null && !prog.FullyCompiled && !HasBareLpClause(prog),
                    Shape(prog));
            }
        }

        /// <summary>In a duel the refused cards cannot be activated, and trying charges no LP.</summary>
        static void ActivationRefused(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                // StandbyBurnsAndDice batch (v162): Inspection can be activated, and its activation charges no LP.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var lp = p.LifePoints;
                var card = PutInHand(engine, p, Inspection);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var tried = engine.TryActivateSpellTrap(p, card, fromHand: true);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Inspection: activation from the hand places it face-up and costs no LP",
                    can && tried && p.TryFindSpellTrap(card, out _) && card.FaceUp && p.LifePoints == lp,
                    $"can={can} tried={tried} lp={p.LifePoints} was {lp}");
            }

            {
                // LpLocksDelayedBurnAndGyNegation batch: Weapon Change's activation is legal and costs nothing (the 700
                // LP belongs to its Standby trigger).
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var lp = p.LifePoints;
                var card = PutInHand(engine, p, WeaponChange);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var tried = engine.TryActivateSpellTrap(p, card, fromHand: true);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Weapon Change: activation from the hand places it face-up and costs no LP",
                    can && tried && p.TryFindSpellTrap(card, out _) && card.FaceUp && p.LifePoints == lp,
                    $"can={can} tried={tried} lp={p.LifePoints} was {lp}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var lp = p.LifePoints;
                var set = PlaceSetTrap(engine, p, RoyalOppression, 0);
                var can = engine.CanActivateSpellTrap(p, set, fromHand: false);
                var tried = engine.TryActivateSpellTrap(p, set, fromHand: false);
                Check("Royal Oppression: a Set copy cannot be activated and costs no LP",
                    !can && !tried && !set.FaceUp && p.TryFindSpellTrap(set, out _) && p.LifePoints == lp,
                    $"can={can} tried={tried} faceUp={set.FaceUp} lp={p.LifePoints} was {lp}");
            }

            {
                // Positive control: the same helpers do activate Toon World and charge its 1000 LP.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var lp = p.LifePoints;
                var card = PutInHand(engine, p, ToonWorld);
                var ok = engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                         engine.TryActivateSpellTrap(p, card, fromHand: true);
                DrainCombat(engine);
                Check("Toon World: activation from the hand still works and costs 1000 LP",
                    ok && p.TryFindSpellTrap(card, out _) && card.FaceUp && p.LifePoints == lp - 1000,
                    $"ok={ok} onField={p.TryFindSpellTrap(card, out _)} lp={p.LifePoints} was {lp}");
            }
        }
    }
}
