using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "NegationsAndGySummons" (v159): Maryokutai and Riryoku Field (Spell negations), Gilasaurus (hand
    /// procedure; the opponent may Special Summon from their GY), Card of Safe Return (draw when a monster is Special
    /// Summoned from your GY) and Chosen One (two hand picks, one random pick). Type Zero Magic Crusher and Skull Lair
    /// stay unimplemented (see the batch notes); their guards are below.
    /// </summary>
    public static class NegationsAndGySummonsRegressionTests
    {
        const int Maryokutai = 71466592;
        const int RiryokuField = 70344351;
        const int TypeZeroMagicCrusher = 21237481;
        const int SkullLair = 6733059;
        const int Gilasaurus = 45894482;
        const int CardOfSafeReturn = 57953380;
        const int ChosenOne = 21888494;

        const int PotOfGreed = 55144522;
        const int DianKeto = 84257640;
        const int MonsterReborn = 83764719;
        const int OfferingsToTheDoomed = 19230407; // Quick-Play: target 1 face-up monster on the field; destroy it
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int JarOfGreed = 83968380;           // Normal Trap: draw 1
        const int CallOfTheHaunted = 97077563;     // Continuous Trap: Special Summon 1 monster from your GY
        const int Megamorph = 22046459;           // Equip Spell: ATK doubled / halved, no leave-field effect
        const int Celtic = 91152256;               // Warrior Lv4 1400/1200
        const int Bewd = 89631139;                 // Dragon Lv8 3000/2500
        const int MysticalElf = 15025844;          // Spellcaster Lv4 800/2000

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("NegationsAndGySummons: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            MaryokutaiDuels(db, pDeck, aDeck, Check);
            RiryokuFieldDuels(db, pDeck, aDeck, Check);
            GilasaurusDuels(db, pDeck, aDeck, Check);
            CardOfSafeReturnDuels(db, pDeck, aDeck, Check);
            ChosenOneDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var mary = Prog(db, Maryokutai);
            var m = mary?.ClauseList.Count == 1 ? mary.ClauseList[0] : null;
            Check("Maryokutai compiles to a Tribute-this Spell negation during the opponent's turn",
                mary != null && mary.FullyCompiled && Implemented(db, Maryokutai) && m != null &&
                m.Timing == EffectTiming.ChainLinkActivated && m.Action == EffectActionKind.NegateActivation &&
                m.ChainResponseOnly && m.RequiresTributeThis && m.OpponentTurnOnly && m.NegateOpponentActivationOnly &&
                m.NegateCardActivationOnly && m.NegateRespondsToSpell && !m.NegateRespondsToTrap &&
                m.DestroyNegatedCard && !m.NegateRequiresSingleMonsterTarget && !m.RequiresDiscardCost &&
                m.PayLpAmount == 0);

            var rir = Prog(db, RiryokuField);
            var r = rir?.ClauseList.Count == 1 ? rir.ClauseList[0] : null;
            Check("Riryoku Field compiles to a single-monster-target Spell negation (either player)",
                rir != null && rir.FullyCompiled && Implemented(db, RiryokuField) && r != null &&
                r.Timing == EffectTiming.ChainLinkActivated && r.Action == EffectActionKind.NegateActivation &&
                r.ChainResponseOnly && r.NegateRequiresSingleMonsterTarget && r.NegateCardActivationOnly &&
                r.NegateRespondsToSpell && !r.NegateRespondsToTrap && !r.NegateOpponentActivationOnly &&
                !r.OpponentTurnOnly && r.DestroyNegatedCard && !r.RequiresTributeThis && !r.RequiresDiscardCost);

            var gila = Prog(db, Gilasaurus);
            var proc = gila?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.SpecialSummonThisFromHandByOwnProcedure);
            var gift = gila?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.OpponentMaySpecialSummonOneFromOwnGy);
            Check("Gilasaurus compiles to its hand procedure and an 'If Summoned this way' gift to the opponent",
                gila != null && gila.FullyCompiled && Implemented(db, Gilasaurus) && gila.ClauseList.Count == 2 &&
                proc != null && proc.Timing == EffectTiming.ContinuousWhileFaceUp && proc.Amount == 0 &&
                string.IsNullOrEmpty(proc.RequiresFaceUpName) &&
                gift != null && gift.Timing == EffectTiming.ThisCardSummoned && gift.Side == EffectSide.Opponent &&
                gift.RequiresThisSpecialSummoned && gift.RequiresThisSummonedByOwnProcedure && gift.Amount == 1 &&
                !gift.RequiresTargetChoice && gift.Zone == EffectZoneFilter.None);

            var cosr = Prog(db, CardOfSafeReturn);
            var d = cosr?.ClauseList.Count == 1 ? cosr.ClauseList[0] : null;
            Check("Card of Safe Return compiles to a face-up draw trigger, not a draw on activation",
                cosr != null && cosr.FullyCompiled && Implemented(db, CardOfSafeReturn) && d != null &&
                d.Timing == EffectTiming.ContinuousWhileFaceUp &&
                d.Action == EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy && d.Amount == 1 &&
                d.Side == EffectSide.Controller && d.StaysOnField &&
                !cosr.ClauseList.Exists(c => c.Action == EffectActionKind.Draw));

            var co = Prog(db, ChosenOne);
            var c1 = co?.ClauseList.Count == 1 ? co.ClauseList[0] : null;
            Check("Chosen One compiles to one Activate selection with its hand activation condition",
                co != null && co.FullyCompiled && Implemented(db, ChosenOne) && c1 != null &&
                c1.Timing == EffectTiming.Activate &&
                c1.Action == EffectActionKind.SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom &&
                c1.ActivationCondition == NegationsAndGySummonsRuntime.ChosenOneHand && !c1.RequiresTargetChoice);

            // Deferred here for the face-up Quick Effect subsystem; the FaceUpQuickEffects batch (v160) compiles them
            // whole as that one clause (FaceUpQuickEffectsRegressionTests drives them).
            foreach (var id in new[] { TypeZeroMagicCrusher, SkullLair })
            {
                var prog = Prog(db, id);
                Check($"{db.Get(id)?.name} compiles only to its face-up Quick Effect (FaceUpQuickEffects v160)",
                    Implemented(db, id) && prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.FaceUpQuickEffect &&
                    !prog.HasTiming(EffectTiming.Activate));
            }
        }

        // ───────────────────────── near misses ─────────────────────────

        static CompiledCardProgram CompileText(string text, string type, string race, string frame) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990159, name = "Near-miss Card", type = type, frameType = frame, race = race, desc = text,
                atk = 900, def = 900, level = 3
            });

        static bool CompilesTo(string text, EffectActionKind action, string type, string race, string frame)
        {
            var prog = CompileText(text, type, race, frame);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static bool HasClause(string text, Func<EffectClause, bool> pred, string type, string race, string frame)
        {
            var prog = CompileText(text, type, race, frame);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && pred(c));
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            var maryText = db.Get(Maryokutai)?.desc ?? "";
            Check("Near miss: Maryokutai's text compiles (control)",
                HasClause(maryText, c => c.RequiresTributeThis && c.OpponentTurnOnly, "Effect Monster", "Aqua", "effect"));
            Check("Near miss: Maryokutai without 'During your opponent's turn' does not compile to the turn-gated answer",
                !HasClause(maryText.Replace("During your opponent's turn, when", "When"),
                    c => c.RequiresTributeThis && c.Action == EffectActionKind.NegateActivation,
                    "Effect Monster", "Aqua", "effect"));
            Check("Near miss: an extra sentence after Maryokutai's text is not compiled",
                !CompilesTo(maryText + " Then draw 1 card.", EffectActionKind.NegateActivation,
                    "Effect Monster", "Aqua", "effect"));

            var rirText = db.Get(RiryokuField)?.desc ?? "";
            Check("Near miss: Riryoku Field's text compiles (control)",
                HasClause(rirText, c => c.NegateRequiresSingleMonsterTarget, "Trap Card", "Counter", "trap"));
            Check("Near miss: 'targets exactly 1 card' (no monster) is not Riryoku Field's template",
                !HasClause(rirText.Replace("exactly 1 monster on the field", "exactly 1 card on the field"),
                    c => c.NegateRequiresSingleMonsterTarget, "Trap Card", "Counter", "trap"));
            var shield = Prog(db, 38275183); // Spell Shield Type-8 shares the phrase in a longer, two-option text
            Check("Near miss: Spell Shield Type-8 is not Riryoku Field's template",
                shield == null || !shield.ClauseList.Exists(c => c != null && c.NegateRequiresSingleMonsterTarget));

            var gilaText = db.Get(Gilasaurus)?.desc ?? "";
            Check("Near miss: Gilasaurus's text compiles (control)",
                CompilesTo(gilaText, EffectActionKind.OpponentMaySpecialSummonOneFromOwnGy, "Effect Monster",
                    "Dinosaur", "effect"));
            Check("Near miss: the procedure sentence alone is not compiled",
                !CompilesTo("You can Special Summon this card (from your hand).",
                    EffectActionKind.SpecialSummonThisFromHandByOwnProcedure, "Effect Monster", "Dinosaur", "effect"));
            Check("Near miss: 'from your GY' (your own GY) is not Gilasaurus's template",
                !CompilesTo(gilaText.Replace("your opponent can Special Summon 1 monster from their GY",
                        "you can Special Summon 1 monster from your GY"),
                    EffectActionKind.OpponentMaySpecialSummonOneFromOwnGy, "Effect Monster", "Dinosaur", "effect"));

            var cosrText = db.Get(CardOfSafeReturn)?.desc ?? "";
            Check("Near miss: Card of Safe Return's text compiles (control)",
                CompilesTo(cosrText, EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy, "Spell Card",
                    "Continuous", "spell"));
            Check("Near miss: 'from your opponent's Graveyard' is not Card of Safe Return's template",
                !CompilesTo(cosrText.Replace("from your Graveyard", "from your opponent's Graveyard"),
                    EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy, "Spell Card", "Continuous", "spell"));
            Check("Near miss: 'draw 2 cards' is not Card of Safe Return's template",
                !CompilesTo(cosrText.Replace("draw 1 card", "draw 2 cards"),
                    EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy, "Spell Card", "Continuous", "spell"));

            var coText = db.Get(ChosenOne)?.desc ?? "";
            Check("Near miss: Chosen One's text compiles (control)",
                CompilesTo(coText, EffectActionKind.SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom,
                    "Spell Card", "Normal", "spell"));
            Check("Near miss: Chosen One without its 'If not' sentence is not compiled",
                !CompilesTo(coText.Replace(" If not, send all the cards to the Graveyard.", ""),
                    EffectActionKind.SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom, "Spell Card", "Normal",
                    "spell"));
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool ChainWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static void Empty(DuelEngine engine)
        {
            ClearBoard(engine);
            engine.ClearPendingResponse();
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }
        }

        /// <summary>A fresh duel in the opponent's Main Phase with an empty board, hands and GYs.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            Empty(engine);
            return engine;
        }

        /// <summary>A fresh duel in the Player's Main Phase 1 (turn 3+) with an empty board, hands and GYs.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            Empty(engine);
            return engine;
        }

        static CardInstance PlaceFaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static CardInstance InGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        // ───────────────────────── Maryokutai ─────────────────────────

        static void MaryokutaiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Pot of Greed on their turn: the Player Tributes Maryokutai, Pot is negated and destroyed.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var mary = PlaceMonster(engine, p, Maryokutai, 0, BattlePosition.Attack, true);
                var pot = PutInHand(engine, opp, PotOfGreed);
                engine.NotifyPublic();
                var deck = opp.Deck.Count;
                var act = engine.TryActivateSpellTrap(opp, pot, fromHand: true);
                var offered = ChainWindowOffers(engine, p, mary);
                Check("Maryokutai: offered against the opponent's Spell Card activation during their turn",
                    reached && act && offered, $"act={act} {Window(engine)}");
                var answered = offered && engine.TryActivateChainResponse(p, mary);
                PassAll(engine);
                Check("Maryokutai: Tributed as the cost; the Spell is negated and destroyed (no draw)",
                    answered && p.Graveyard.Contains(mary) && !p.TryFindMonster(mary, out _) &&
                    opp.Graveyard.Contains(pot) && opp.Deck.Count == deck && engine.Chain.Count == 0 &&
                    !engine.IsAwaitingResponse,
                    $"answered={answered} maryGy={p.Graveyard.Contains(mary)} potGy={opp.Graveyard.Contains(pot)} " +
                    $"deck {deck}->{opp.Deck.Count}");
            }

            {
                // A face-down Maryokutai cannot use its Quick Effect; neither can a Trap activation be answered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var setMary = PlaceMonster(engine, p, Maryokutai, 0, BattlePosition.Defense, false);
                var mary = PlaceMonster(engine, p, Maryokutai, 1, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(engine, opp, JarOfGreed, 0);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, jar, fromHand: false);
                var trapOffered = ChainWindowOffers(engine, p, mary) || ChainWindowOffers(engine, p, setMary);
                PassAll(engine);
                Check("Maryokutai: not offered against a Trap Card activation",
                    reached && act && !trapOffered, $"act={act} offered={trapOffered}");
                var pot = PutInHand(engine, opp, PotOfGreed);
                engine.NotifyPublic();
                var act2 = engine.TryActivateSpellTrap(opp, pot, fromHand: true);
                Check("Maryokutai: a face-down copy is not offered (the face-up one is)",
                    act2 && ChainWindowOffers(engine, p, mary) && !ChainWindowOffers(engine, p, setMary), Window(engine));
                PassAll(engine);
            }

            {
                // During its controller's own turn it cannot answer (Duel.GetTurnPlayer()~=tp), and never its own Spell.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var mary = PlaceMonster(engine, p, Maryokutai, 0, BattlePosition.Attack, true);
                var oppMary = PlaceMonster(engine, opp, Maryokutai, 0, BattlePosition.Attack, true);
                var pot = PutInHand(engine, p, PotOfGreed);
                engine.NotifyPublic();
                var clause = Prog(db, Maryokutai).ClauseList[0];
                var link = new ChainLink { Card = pot, Controller = opp, FromHand = true };
                var ownTurn = NegationsAndGySummonsRuntime.ChainAnswerAllowed(engine, p, mary, clause, link, out var why);
                Check("Maryokutai: not usable during its controller's own turn",
                    reached && !ownTurn && why.Contains("opponent's turn"), why);

                // The AI's Maryokutai answers the Player's Pot of Greed (a draw is worth a monster).
                var deck = p.Deck.Count;
                var act = engine.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(engine);
                Check("Maryokutai (AI): Tributes itself to negate the Player's Pot of Greed",
                    act && opp.Graveyard.Contains(oppMary) && p.Graveyard.Contains(pot) && p.Deck.Count == deck &&
                    p.TryFindMonster(mary, out _),
                    $"act={act} maryGy={opp.Graveyard.Contains(oppMary)} deck {deck}->{p.Deck.Count}");
            }

            {
                // The AI keeps Maryokutai against an LP gain (Dian Keto).
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var oppMary = PlaceMonster(engine, opp, Maryokutai, 0, BattlePosition.Attack, true);
                var keto = PutInHand(engine, p, DianKeto);
                engine.NotifyPublic();
                var lp = p.LifePoints;
                var act = engine.TryActivateSpellTrap(p, keto, fromHand: true);
                PassAll(engine);
                Check("Maryokutai (AI): not spent on an LP gain (Dian Keto resolves)",
                    reached && act && opp.TryFindMonster(oppMary, out _) && p.LifePoints == lp + 1000,
                    $"act={act} lp {lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────── Riryoku Field ─────────────────────────

        static void RiryokuFieldDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Offerings to the Doomed targets the Player's Blue-Eyes: negated and destroyed.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var rir = PlaceSetTrap(engine, p, RiryokuField, 0);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var targeted = engine.Chain.Last != null && ReferenceEquals(engine.Chain.Last.Target, bewd);
                var offered = ChainWindowOffers(engine, p, rir);
                Check("Riryoku Field: offered against a Spell targeting exactly 1 monster",
                    reached && act && targeted && offered, $"act={act} targeted={targeted} {Window(engine)}");
                var answered = offered && engine.TryActivateChainResponse(p, rir);
                PassAll(engine);
                Check("Riryoku Field: the Spell is negated and destroyed — Blue-Eyes stays",
                    answered && p.TryFindMonster(bewd, out _) && opp.Graveyard.Contains(offerings) &&
                    p.Graveyard.Contains(rir) && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"answered={answered} bewd={p.TryFindMonster(bewd, out _)} offGy={opp.Graveyard.Contains(offerings)}");
            }

            {
                // An Equip Spell's activation targets the monster it equips (the opponent's own Celtic here).
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var rir = PlaceSetTrap(engine, p, RiryokuField, 0);
                // Megamorph has no "sent to the GY" effect (Axe of Despair's would put itself back on the Deck).
                var equip = PutInHand(engine, opp, Megamorph);
                engine.NotifyPublic();
                var atk = celtic.CurrentAtk;
                var act = engine.TryActivateSpellTrap(opp, equip, fromHand: true);
                var offered = ChainWindowOffers(engine, p, rir);
                var answered = offered && engine.TryActivateChainResponse(p, rir);
                PassAll(engine);
                Check("Riryoku Field: negates an Equip Spell equipping 1 monster (it is destroyed, no ATK change)",
                    reached && act && answered && opp.Graveyard.Contains(equip) && celtic.CurrentAtk == atk &&
                    celtic.Equips.Count == 0 && p.Graveyard.Contains(rir),
                    $"act={act} offered={offered} answered={answered} equipGy={opp.Graveyard.Contains(equip)} " +
                    $"atk {atk}->{celtic.CurrentAtk} {Window(engine)}");
            }

            {
                // Dark Hole targets nothing: not offered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var rir = PlaceSetTrap(engine, p, RiryokuField, 0);
                var hole = PutInHand(engine, opp, DarkHole);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, hole, fromHand: true);
                Check("Riryoku Field: not offered against a Spell that targets nothing (Dark Hole)",
                    reached && act && !ChainWindowOffers(engine, p, rir), Window(engine));
                PassAll(engine);
            }

            {
                // Mystical Space Typhoon targets a Spell/Trap Card, not a monster: not offered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var rir = PlaceSetTrap(engine, p, RiryokuField, 0);
                var mst = PutInHand(engine, opp, Mst);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, mst, fromHand: true);
                Check("Riryoku Field: not offered against a Spell targeting a Spell/Trap Card (MST)",
                    reached && act && !ChainWindowOffers(engine, p, rir), Window(engine));
                PassAll(engine);
            }

            {
                // The AI's Set Riryoku Field answers the Player's Offerings to the Doomed on the AI's monster.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var rir = PlaceSetTrap(engine, opp, RiryokuField, 0);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                PassAll(engine);
                Check("Riryoku Field (AI): negates the Player's Offerings to the Doomed — its monster stays",
                    reached && act && opp.TryFindMonster(celtic, out _) && opp.Graveyard.Contains(rir) &&
                    p.Graveyard.Contains(offerings),
                    $"act={act} celtic={opp.TryFindMonster(celtic, out _)} rirGy={opp.Graveyard.Contains(rir)}");
            }
        }

        // ───────────────────────── Gilasaurus ─────────────────────────

        static void GilasaurusDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The Player's procedure: no Normal Summon used; the AI opponent Special Summons its best GY monster
                // face-up, and its face-up Card of Safe Return draws 1 for it.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var gila = PutInHand(engine, p, Gilasaurus);
                var elf = InGy(engine, opp, MysticalElf);
                var bewd = InGy(engine, opp, Bewd);
                var cosr = PlaceFaceUpSpell(engine, opp, CardOfSafeReturn, 0);
                engine.NotifyPublic();
                var can = engine.CanSpecialSummonProcedure(p, gila);
                var oppHand = opp.Hand.Count;
                var oppDeck = opp.Deck.Count;
                var ok = engine.TrySpecialSummonProcedure(p, gila);
                PassAll(engine);
                Check("Gilasaurus: Special Summoned from the hand by its procedure (Normal Summon not used)",
                    reached && can && ok && p.TryFindMonster(gila, out _) && gila.FaceUp && !p.NormalSummonUsed,
                    $"can={can} ok={ok} field={p.TryFindMonster(gila, out _)}");
                Check("Gilasaurus: the AI opponent Special Summons its best GY monster face-up",
                    opp.TryFindMonster(bewd, out _) && bewd.FaceUp && opp.Graveyard.Contains(elf),
                    $"bewdField={opp.TryFindMonster(bewd, out _)} faceUp={bewd.FaceUp}");
                Check("Gilasaurus + Card of Safe Return: the opponent's Summon from their GY draws them 1 card",
                    opp.Hand.Count == oppHand + 1 && opp.Deck.Count == oppDeck - 1 && cosr.FaceUp,
                    $"hand {oppHand}->{opp.Hand.Count} deck {oppDeck}->{opp.Deck.Count}");
            }

            {
                // The AI's procedure: the Player chooses (face-up Defense here) or declines.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var gila = PutInHand(engine, opp, Gilasaurus);
                var celtic = InGy(engine, p, Celtic);
                engine.NotifyPublic();
                var ok = engine.TrySpecialSummonProcedure(opp, gila);
                var pending = engine.PendingActivation;
                var asked = ok && pending != null && pending.Controller == p &&
                            FlipMassEffectsRuntime.IsMassSummonPick(pending) &&
                            SpellTrapEffects.PendingTargetIsSpecialSummon(pending) &&
                            pending.LegalTargets.Contains(celtic);
                Check("Gilasaurus (AI): the Player is offered a monster in their own GY to Special Summon",
                    reached && asked, pending == null ? "no pick" : $"legal=[{string.Join(",", pending.LegalTargets.Select(c => c.Name))}]");
                if (asked)
                {
                    engine.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    engine.TrySelectEffectTarget(celtic);
                }

                PassAll(engine);
                Check("Gilasaurus (AI): 'Summon DEF' Special Summons it in face-up Defense Position",
                    p.TryFindMonster(celtic, out _) && celtic.FaceUp && celtic.Position == BattlePosition.Defense,
                    $"field={p.TryFindMonster(celtic, out _)} faceUp={celtic.FaceUp} pos={celtic.Position}");
            }

            {
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var gila = PutInHand(engine, opp, Gilasaurus);
                var celtic = InGy(engine, p, Celtic);
                engine.NotifyPublic();
                var ok = engine.TrySpecialSummonProcedure(opp, gila);
                var asked = engine.PendingActivation != null && FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
                if (asked) engine.CancelEffectTargeting();
                PassAll(engine);
                Check("Gilasaurus (AI): CANCEL declines — nothing is Special Summoned",
                    reached && ok && asked && p.Graveyard.Contains(celtic) && engine.PendingActivation == null &&
                    p.MonstersOnField().Count() == 0, $"ok={ok} asked={asked}");
            }

            {
                // Monster Reborn on Gilasaurus is not "Summoned this way": the opponent gets nothing.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var gila = InGy(engine, p, Gilasaurus);
                var celtic = InGy(engine, opp, Celtic);
                var reborn = PutInHand(engine, p, MonsterReborn);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, reborn, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(gila);
                PassAll(engine);
                Check("Gilasaurus: Special Summoned by Monster Reborn, the opponent does not Special Summon",
                    reached && act && p.TryFindMonster(gila, out _) && opp.Graveyard.Contains(celtic) &&
                    !opp.TryFindMonster(celtic, out _) && engine.PendingActivation == null,
                    $"act={act} gila={p.TryFindMonster(gila, out _)} celticGy={opp.Graveyard.Contains(celtic)}");
            }

            {
                // The procedure needs a free Monster Zone and the controller's Main Phase.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var gila = PutInHand(engine, p, Gilasaurus);
                for (var i = 0; i < p.MonsterZones.Length; i++)
                    PlaceMonster(engine, p, Celtic, i, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var fullField = engine.CanSpecialSummonProcedure(p, gila);
                var oppTurn = OpponentMain(db, pDeck, aDeck, out var reached2);
                var gila2 = PutInHand(oppTurn, oppTurn.Player, Gilasaurus);
                Check("Gilasaurus: no procedure with a full field or during the opponent's turn",
                    reached && reached2 && !fullField && !oppTurn.CanSpecialSummonProcedure(oppTurn.Player, gila2));
            }
        }

        // ───────────────────────── Card of Safe Return ─────────────────────────

        static void CardOfSafeReturnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Activating it draws nothing; it stays face-up.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var cosr = PutInHand(engine, p, CardOfSafeReturn);
                engine.NotifyPublic();
                var deck = p.Deck.Count;
                var act = engine.TryActivateSpellTrap(p, cosr, fromHand: true);
                PassAll(engine);
                Check("Card of Safe Return: its activation draws nothing and it stays face-up",
                    reached && act && p.TryFindSpellTrap(cosr, out _) && cosr.FaceUp && p.Deck.Count == deck,
                    $"act={act} deck {deck}->{p.Deck.Count}");

                // Monster Reborn on a monster in your own GY: draw 1.
                var celtic = InGy(engine, p, Celtic);
                var reborn = PutInHand(engine, p, MonsterReborn);
                engine.NotifyPublic();
                var hand = p.Hand.Count;
                deck = p.Deck.Count;
                var act2 = engine.TryActivateSpellTrap(p, reborn, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                PassAll(engine);
                Check("Card of Safe Return: a monster Special Summoned from your GY draws you 1 card",
                    act2 && p.TryFindMonster(celtic, out _) && p.Deck.Count == deck - 1 && p.Hand.Count == hand,
                    $"act={act2} deck {deck}->{p.Deck.Count} hand {hand}->{p.Hand.Count}");
            }

            {
                // Monster Reborn on the opponent's monster (from their GY): no draw for your copy, one for theirs.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceFaceUpSpell(engine, p, CardOfSafeReturn, 0);
                PlaceFaceUpSpell(engine, opp, CardOfSafeReturn, 0);
                var bewd = InGy(engine, opp, Bewd);
                var reborn = PutInHand(engine, p, MonsterReborn);
                engine.NotifyPublic();
                var pDeckCount = p.Deck.Count;
                var oDeckCount = opp.Deck.Count;
                var act = engine.TryActivateSpellTrap(p, reborn, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(bewd);
                PassAll(engine);
                Check("Card of Safe Return: a monster from the opponent's GY draws for its owner's copy, not yours",
                    reached && act && p.TryFindMonster(bewd, out _) && p.Deck.Count == pDeckCount &&
                    opp.Deck.Count == oDeckCount - 1,
                    $"act={act} pDeck {pDeckCount}->{p.Deck.Count} oDeck {oDeckCount}->{opp.Deck.Count}");
            }

            {
                // Missed timing: the Player chains Call of the Haunted (Chain Link 2) to the opponent's Monster Reborn;
                // Call's Summon from the Player's GY is not the last thing to happen (Monster Reborn still resolves).
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceFaceUpSpell(engine, p, CardOfSafeReturn, 0);
                var call = PlaceSetTrap(engine, p, CallOfTheHaunted, 1);
                var celtic = InGy(engine, p, Celtic);
                var bewd = InGy(engine, opp, Bewd);
                var reborn = PutInHand(engine, opp, MonsterReborn);
                engine.NotifyPublic();
                var deck = p.Deck.Count;
                var act = engine.TryActivateSpellTrap(opp, reborn, fromHand: true);
                var rebornTarget = engine.Chain.Last?.Target;
                var offered = ChainWindowOffers(engine, p, call);
                var chained = offered && engine.TryActivateSpellTrap(p, call, fromHand: false);
                if (chained && engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                PassAll(engine);
                Check("Card of Safe Return: a Summon from your GY by a Chain Link that is not the last to resolve " +
                      "misses the timing (no draw)",
                    reached && act && chained && p.TryFindMonster(celtic, out _) && p.Deck.Count == deck &&
                    rebornTarget != null && engine.ControllerOf(rebornTarget) == opp,
                    $"act={act} offered={offered} chained={chained} celtic={p.TryFindMonster(celtic, out _)} " +
                    $"deck {deck}->{p.Deck.Count} rebornTarget={rebornTarget?.Name} {Window(engine)}");
            }

            {
                // A Special Summon from the hand (Gilasaurus's procedure) is not from the GY; a negated copy does nothing.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceFaceUpSpell(engine, p, CardOfSafeReturn, 0);
                var negated = PlaceFaceUpSpell(engine, engine.Opponent, CardOfSafeReturn, 0);
                negated.IsNegated = true;
                var celtic = InGy(engine, engine.Opponent, Celtic);
                var gila = PutInHand(engine, p, Gilasaurus);
                engine.NotifyPublic();
                var deck = p.Deck.Count;
                var oDeck = engine.Opponent.Deck.Count;
                var ok = engine.TrySpecialSummonProcedure(p, gila);
                PassAll(engine);
                Check("Card of Safe Return: no draw for a Special Summon from the hand, nor for a negated copy",
                    reached && ok && p.TryFindMonster(gila, out _) && p.Deck.Count == deck &&
                    engine.Opponent.TryFindMonster(celtic, out _) && engine.Opponent.Deck.Count == oDeck,
                    $"ok={ok} deck {deck}->{p.Deck.Count} oDeck {oDeck}->{engine.Opponent.Deck.Count}");
            }
        }

        // ───────────────────────── Chosen One ─────────────────────────

        static void ChosenOneDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var co = PutInHand(engine, p, ChosenOne);
                PutInHand(engine, p, Celtic);
                PutInHand(engine, p, Mst);
                var one = engine.CanActivateSpellTrap(p, co, fromHand: true);
                PutInHand(engine, p, DarkHole);
                var two = engine.CanActivateSpellTrap(p, co, fromHand: true);
                p.Hand.RemoveAll(c => c.CardId == Celtic);
                var noMonster = engine.CanActivateSpellTrap(p, co, fromHand: true);
                Check("Chosen One: needs a monster and 2 other Spell/Trap Cards in the hand",
                    reached && !one && two && !noMonster, $"one={one} two={two} noMonster={noMonster}");
            }

            foreach (var monsterChosen in new[] { true, false })
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var co = PutInHand(engine, p, ChosenOne);
                var elf = PutInHand(engine, p, MysticalElf);
                var mst = PutInHand(engine, p, Mst);
                var hole = PutInHand(engine, p, DarkHole);
                var pot = PutInHand(engine, p, PotOfGreed);
                engine.NotifyPublic();
                engine.Rng.QueuePick(monsterChosen ? 0 : 2);
                var act = engine.TryActivateSpellTrap(p, co, fromHand: true);
                PassAll(engine);
                var first = engine.PendingActivation;
                var askedMonster = first != null && NegationsAndGySummonsRuntime.IsChosenOnePick(first) &&
                                   first.LegalTargets.Count == 1 && first.LegalTargets.Contains(elf) &&
                                   SpellTrapEffects.PendingTargetIsSpecialSummon(first);
                if (askedMonster)
                {
                    engine.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    engine.TrySelectEffectTarget(elf);
                }

                var second = engine.PendingActivation;
                var askedSpells = second != null && NegationsAndGySummonsRuntime.IsChosenOnePick(second) &&
                                  second.LegalTargets.Count == 3 && !second.LegalTargets.Contains(elf) &&
                                  !SpellTrapEffects.PendingTargetIsSpecialSummon(second);
                if (askedSpells)
                {
                    engine.TrySelectEffectTarget(mst);
                    if (engine.PendingActivation != null) engine.TrySelectEffectTarget(hole);
                }

                PassAll(engine);
                if (monsterChosen)
                    Check("Chosen One: the human selects the monster, then 2 Spell/Traps; the monster chosen at random " +
                          "is Special Summoned face-up (Defense) and the other 2 go to the GY",
                        reached && act && askedMonster && askedSpells && p.TryFindMonster(elf, out _) && elf.FaceUp &&
                        elf.Position == BattlePosition.Defense && p.Graveyard.Contains(mst) && p.Graveyard.Contains(hole) &&
                        p.Hand.Contains(pot) && p.Graveyard.Contains(co) && engine.PendingActivation == null,
                        $"act={act} askedMonster={askedMonster} askedSpells={askedSpells} elfField={p.TryFindMonster(elf, out _)}");
                else
                    Check("Chosen One: a Spell/Trap chosen at random sends all 3 cards to the GY",
                        reached && act && askedMonster && askedSpells && p.Graveyard.Contains(elf) &&
                        p.Graveyard.Contains(mst) && p.Graveyard.Contains(hole) && p.Hand.Contains(pot) &&
                        p.MonstersOnField().Count() == 0,
                        $"act={act} elfGy={p.Graveyard.Contains(elf)} mstGy={p.Graveyard.Contains(mst)}");
            }

            {
                // CANCEL cannot skip the selection: it is completed automatically.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var co = PutInHand(engine, p, ChosenOne);
                var celtic = PutInHand(engine, p, Celtic);
                var mst = PutInHand(engine, p, Mst);
                var hole = PutInHand(engine, p, DarkHole);
                engine.NotifyPublic();
                engine.Rng.QueuePick(0);
                var act = engine.TryActivateSpellTrap(p, co, fromHand: true);
                PassAll(engine);
                var asked = engine.PendingActivation != null &&
                            NegationsAndGySummonsRuntime.IsChosenOnePick(engine.PendingActivation);
                if (asked) engine.CancelEffectTargeting();
                PassAll(engine);
                Check("Chosen One: CANCEL completes the selection automatically",
                    reached && act && asked && engine.PendingActivation == null && p.TryFindMonster(celtic, out _) &&
                    p.Graveyard.Contains(mst) && p.Graveyard.Contains(hole),
                    $"act={act} asked={asked} celtic={p.TryFindMonster(celtic, out _)}");
            }
        }
    }
}
