using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DcrSpellTrapUtility batch: Ray of Hope, Spell Reproduction, Frozen Soul, Arsenal Robber, Interdimensional Matter
    /// Transporter, Dragged Down into the Grave, Final Countdown, Different Dimension Gate. Compile shapes through the real
    /// compiler, near-miss sentences, and mini-duels through the engine entry points (TryActivateSpellTrap,
    /// TrySelectEffectTarget, TryEndTurnSafe, response windows). Turn 1 is the Player's, turn 2 the AI's.
    /// Runtime: <see cref="DcrSpellTrapUtilityRuntime"/>.
    /// </summary>
    public static class DcrSpellTrapUtilityRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int RayOfHope = 82529174;           // Normal Trap
        const int SpellReproduction = 29228529;   // Normal Spell
        const int FrozenSoul = 57069605;          // Normal Trap
        const int ArsenalRobber = 55348096;       // Normal Trap
        const int Transporter = 36261276;         // Interdimensional Matter Transporter, Normal Trap
        const int DraggedDown = 16435215;         // Dragged Down into the Grave, Normal Spell
        const int FinalCountdown = 95308449;      // Normal Spell
        const int DdGate = 56460688;              // Different Dimension Gate, Continuous Spell

        const int Bewd = 89631139;                // LIGHT Dragon 3000/2500
        const int MysticalElf = 15025844;         // LIGHT Spellcaster 800/2000
        const int StJoan = 21175632;              // LIGHT Fusion
        const int Kuriboh = 40640057;             // DARK Fiend 300/200
        const int Celtic = 91152256;              // EARTH Warrior 1400/1200
        const int PotOfGreed = 55144522;          // Normal Spell
        const int DarkHole = 53129443;            // Normal Spell
        const int Mst = 5318639;                  // Mystical Space Typhoon
        const int AxeOfDespair = 40619825;        // Equip Spell
        const int BlackPendant = 65169794;        // Equip Spell
        const int GiantTrunade = 42703248;        // Normal Spell: every Spell/Trap on the field to the hand
        const int PyroClock = 1082946;            // Pyro Clock of Destiny, Normal Trap

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DcrSpellTrapUtility: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            RayOfHopeDuels(db, pDeck, aDeck, Check);
            SpellReproductionDuels(db, pDeck, aDeck, Check);
            FrozenSoulDuels(db, pDeck, aDeck, Check);
            ArsenalRobberDuels(db, pDeck, aDeck, Check);
            TransporterDuels(db, pDeck, aDeck, Check);
            DraggedDownDuels(db, pDeck, aDeck, Check);
            FinalCountdownDuels(db, pDeck, aDeck, Check);
            GateDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }
        }

        static void EndTurn(DuelEngine e)
        {
            Drive(e, _ => false);
            e.TryEndTurnSafe(e.TurnPlayer);
            Drive(e, _ => false);
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.TryFindMonster(c, out var z)) return $"{tag}M{z}{(c.FaceUp ? "u" : "d")}{c.Position}";
                if (who.TryFindSpellTrap(c, out var s)) return $"{tag}S{s}";
                if (who.Hand.Contains(c)) return tag + "Hand";
                if (who.Graveyard.Contains(c)) return tag + "GY";
                if (who.Banished.Contains(c)) return tag + "Banished";
            }
            return "gone";
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990000001, name = "DcrSpellTrapUtility probe", type = type, frameType = "spell", race = race,
                desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static bool One(CardDatabase db, int id, Func<EffectClause, bool> shape, out string detail)
        {
            var p = Prog(db, id);
            detail = Describe(p);
            return p != null && p.FullyCompiled && Implemented(db, id) && p.ClauseList.Count == 1 &&
                   Has(p, c => c.Timing == EffectTiming.Activate && shape(c));
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            Check("Ray of Hope: exactly 2 LIGHT monsters in your GY are targeted and shuffled into the Deck",
                One(db, RayOfHope, c => c.Action == EffectActionKind.ReturnChosenToDeckShuffle &&
                                        c.Zone == EffectZoneFilter.ControllerGyCards && c.AttributeFilter == "LIGHT" &&
                                        c.IsPsctTarget && c.TargetCount == 2 && !c.TargetUpTo, out var d1), d1);
            Check("Spell Reproduction: cost send 2 Spells from the hand; target 1 Spell in your GY to the hand",
                One(db, SpellReproduction, c => c.Action == EffectActionKind.AddFromGyToHand &&
                                                c.Zone == EffectZoneFilter.ControllerGySpells && c.IsPsctTarget &&
                                                c.DiscardCostCount == 2 && c.CostSendSpellsFromHandCount == 2,
                    out var d2), d2);
            Check("Frozen Soul: LP gap condition, the opponent skips their next Battle Phase",
                One(db, FrozenSoul, c => c.Action == EffectActionKind.OpponentSkipsNextBattlePhase &&
                                         c.ActivationCondition == DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent &&
                                         c.ConditionAmount == 2000, out var d3), d3);
            Check("Arsenal Robber: the opponent sends 1 Equip Spell from their Deck (filter, not a target)",
                One(db, ArsenalRobber, c => c.Action == EffectActionKind.OpponentSendsSpellFromDeckToGy &&
                                            c.Zone == EffectZoneFilter.DeckEquipSpells && !c.IsPsctTarget &&
                                            c.ActivationCondition == DcrSpellTrapUtilityRuntime.OpponentDeckNotEmpty,
                    out var d4), d4);
            Check("Interdimensional Matter Transporter: 1 face-up monster you control, banished until the End Phase",
                One(db, Transporter, c => c.Action == EffectActionKind.BanishTargetUntilEndPhase &&
                                          c.Zone == EffectZoneFilter.ControllerAnyMonsters && c.RequiresTargetFaceUp &&
                                          c.IsPsctTarget, out var d5), d5);
            Check("Dragged Down into the Grave: both-hands condition, picks, discards, then both draw",
                One(db, DraggedDown, c => c.Action == EffectActionKind.BothPickFromOpponentHandDiscardThenBothDraw &&
                                          c.ActivationCondition == DcrSpellTrapUtilityRuntime.BothHandsHaveACard,
                    out var d6), d6);
            Check("Final Countdown: pay 2000 LP, win after 20 turns",
                One(db, FinalCountdown, c => c.Action == EffectActionKind.WinAfterTurnCountdown && c.PayLpAmount == 2000 &&
                                             c.Amount == 20, out var d7), d7);
            Check("Different Dimension Gate: 1 own + 1 opponent's monster banished, returned when destroyed to the GY",
                One(db, DdGate, c => c.Action == EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy &&
                                     c.Zone == EffectZoneFilter.ControllerAnyMonsters && c.RequiresSecondTarget &&
                                     c.SecondZone == EffectZoneFilter.OppMonsters &&
                                     c.SecondAction == EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy,
                    out var d8), d8);

            // Near misses: the atoms own the whole text.
            var rayPlus = CompileText("Trap Card", "Normal",
                "Add 2 LIGHT monsters from your Graveyard to your Deck and shuffle it. Draw 1 card.");
            Check("near miss: Ray of Hope's sentence with a rider is not the Ray atom",
                !Has(rayPlus, c => c.Action == EffectActionKind.ReturnChosenToDeckShuffle && c.AttributeFilter == "LIGHT") ||
                !rayPlus.FullyCompiled, Describe(rayPlus));
            var oppTransport = CompileText("Trap Card", "Normal",
                "Target 1 face-up monster your opponent controls; banish that target until the End Phase.");
            Check("near miss: banishing the opponent's monster until the End Phase is not the Transporter atom",
                !Has(oppTransport, c => c.Action == EffectActionKind.BanishTargetUntilEndPhase), Describe(oppTransport));
            var frozenNoCond = CompileText("Trap Card", "Normal",
                "You can only activate this card when your Life Points are at least 2000 higher than your opponent's. " +
                "Skip your opponent's next Battle Phase.");
            Check("near miss: a 'higher' LP condition is not Frozen Soul's condition",
                !Has(frozenNoCond, c => c.ActivationCondition == DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent),
                Describe(frozenNoCond));
            var robberMonster = CompileText("Trap Card", "Normal",
                "Your opponent selects 1 Equip Spell Card from his/her Deck and sends it to the Graveyard. Draw 1 card.");
            Check("near miss: Arsenal Robber's sentence with a rider is not the atom",
                !Has(robberMonster, c => c.Zone == EffectZoneFilter.DeckEquipSpells) || !robberMonster.FullyCompiled,
                Describe(robberMonster));
            var countdown10 = CompileText("Spell Card", "Normal",
                "Pay 2000 Life Points. After 10 turns have passed (counting the turn you activate this card as the 1st " +
                "turn), you win the Duel.");
            Check("Final Countdown's turn count is read from the text (10 → Amount 10)",
                Has(countdown10, c => c.Action == EffectActionKind.WinAfterTurnCountdown && c.Amount == 10),
                Describe(countdown10));
        }

        // ───────────────────────────── Ray of Hope ─────────────────────────────

        static void RayOfHopeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bewd = AddToGy(e, p, Bewd);
                var elf = AddToGy(e, p, MysticalElf);
                var joan = AddToGy(e, p, StJoan);
                var kuri = AddToGy(e, p, Kuriboh);
                AddToGy(e, p, PotOfGreed);
                var oppLight = AddToGy(e, e.Opponent, Bewd);
                var ray = PlaceSetTrap(e, p, RayOfHope, 0);
                var deckBefore = p.Deck.Count;
                var extraBefore = p.ExtraDeck.Count(id => id == StJoan);
                var act = e.TryActivateSpellTrap(p, ray, fromHand: false);
                var legal = Legal(e);
                Drive(e, c => c == bewd || c == joan);
                Check("Ray of Hope: only LIGHT monsters in your own GY are legal; both targets leave the GY " +
                      "(the LIGHT Fusion to the Extra Deck)",
                    act && legal.Contains(bewd) && legal.Contains(elf) && legal.Contains(joan) && !legal.Contains(kuri) &&
                    !legal.Contains(oppLight) && !legal.Exists(c => c.CardId == PotOfGreed) &&
                    !p.Graveyard.Contains(bewd) && !p.Graveyard.Contains(joan) && p.Graveyard.Contains(elf) &&
                    p.Graveyard.Contains(kuri) && p.Deck.Count == deckBefore + 1 && p.Deck.Contains(Bewd) &&
                    p.ExtraDeck.Count(id => id == StJoan) == extraBefore + 1 && p.Graveyard.Contains(ray),
                    $"act={act} legal=[{string.Join(",", legal.Select(c => c.Name))}] bewd={Where(e, bewd)} " +
                    $"joan={Where(e, joan)} deck={deckBefore}->{p.Deck.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                AddToGy(e, p, Bewd);
                AddToGy(e, p, Kuriboh);
                AddToGy(e, e.Opponent, MysticalElf);
                var ray = PlaceSetTrap(e, p, RayOfHope, 0);
                Check("Ray of Hope: 1 LIGHT monster in your GY (another in the opponent's) cannot activate it",
                    !e.CanActivateSpellTrap(p, ray, fromHand: false), "");
            }
        }

        // ───────────────────────────── Spell Reproduction ─────────────────────────────

        static void SpellReproductionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sr = PutInHand(e, p, SpellReproduction);
                var pot = PutInHand(e, p, PotOfGreed);
                var hole = PutInHand(e, p, DarkHole);
                var celtic = PutInHand(e, p, Celtic);
                var mst = AddToGy(e, p, Mst);
                AddToGy(e, p, Kuriboh);
                var act = e.TryActivateSpellTrap(p, sr, fromHand: true);
                var costLegal = Legal(e);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(pot);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(hole);
                var targetLegal = Legal(e);
                Drive(e, c => c == mst);
                Check("Spell Reproduction: only other Spells pay the cost (sent to the GY); then 1 Spell in your GY " +
                      "returns to the hand",
                    act && costLegal.Contains(pot) && costLegal.Contains(hole) && !costLegal.Contains(celtic) &&
                    !costLegal.Contains(sr) && p.Graveyard.Contains(pot) && p.Graveyard.Contains(hole) &&
                    targetLegal.Contains(mst) && !targetLegal.Exists(c => c.CardId == Kuriboh) &&
                    p.Hand.Contains(mst) && p.Hand.Contains(celtic) && p.Graveyard.Contains(sr),
                    $"act={act} cost=[{string.Join(",", costLegal.Select(c => c.Name))}] " +
                    $"targets=[{string.Join(",", targetLegal.Select(c => c.Name))}] mst={Where(e, mst)} " +
                    $"pot={Where(e, pot)} hole={Where(e, hole)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sr = PutInHand(e, p, SpellReproduction);
                PutInHand(e, p, PotOfGreed);
                PutInHand(e, p, Celtic);
                AddToGy(e, p, Mst);
                var oneSpell = e.CanActivateSpellTrap(p, sr, fromHand: true);
                var e2 = Setup(db, pDeck, aDeck);
                var sr2 = PutInHand(e2, e2.Player, SpellReproduction);
                PutInHand(e2, e2.Player, PotOfGreed);
                PutInHand(e2, e2.Player, DarkHole);
                AddToGy(e2, e2.Player, Kuriboh);
                AddToGy(e2, e2.Opponent, Mst);
                var noTarget = e2.CanActivateSpellTrap(e2.Player, sr2, fromHand: true);
                Check("Spell Reproduction: 1 other Spell in hand, or no Spell in your own GY, cannot activate it",
                    !oneSpell && !noTarget, $"oneSpell={oneSpell} noTarget={noTarget}");
            }
        }

        // ───────────────────────────── Frozen Soul ─────────────────────────────

        static void FrozenSoulDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 6000;
                o.LifePoints = 8000;
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var fs = PlaceSetTrap(e, p, FrozenSoul, 0);
                var act = e.TryActivateSpellTrap(p, fs, fromHand: false);
                Drive(e, _ => false);
                EndTurn(e);
                var oppTurn = e.TurnPlayer == o && e.TurnNumber == 2;
                var canBp = e.CanConductBattlePhase;
                var entered = e.TryEnterBattlePhase(o);
                EndTurn(e);
                EndTurn(e);
                var turn4 = e.TurnPlayer == o && e.TurnNumber == 4;
                var canBp4 = e.CanConductBattlePhase;
                Check("Frozen Soul: activated in your turn, the opponent's next Battle Phase is skipped (only that one)",
                    act && oppTurn && !canBp && !entered && turn4 && canBp4 && p.Graveyard.Contains(fs),
                    $"act={act} turn2={oppTurn} canBp={canBp} entered={entered} turn4={turn4} canBp4={canBp4}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 6001;
                o.LifePoints = 8000;
                var fs = PlaceSetTrap(e, p, FrozenSoul, 0);
                var gap1999 = e.CanActivateSpellTrap(p, fs, fromHand: false);
                p.LifePoints = 6000;
                var gap2000 = e.CanActivateSpellTrap(p, fs, fromHand: false);
                Check("Frozen Soul: only while your LP is at least 2000 lower (1999 no, 2000 yes)",
                    !gap1999 && gap2000, $"1999={gap1999} 2000={gap2000}");
            }

            {
                // The opponent's own turn before its Battle Phase (Main Phase 1): that turn's Battle Phase is skipped
                // (Lua EFFECT_SKIP_BP applies at once). The AI's Set Frozen Soul hits the Player in turn 3.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                EndTurn(e);
                EndTurn(e);
                var turn3 = e.TurnPlayer == p && e.TurnNumber == 3 && e.Phase == DuelPhase.Main1;
                o.LifePoints = 5000;
                p.LifePoints = 8000;
                var fs = PlaceSetTrap(e, o, FrozenSoul, 0);
                var act = e.TryActivateSpellTrap(o, fs, fromHand: false);
                Drive(e, _ => false);
                var canBp = e.CanConductBattlePhase;
                EndTurn(e);
                EndTurn(e);
                var canBp5 = e.TurnPlayer == p && e.TurnNumber == 5 && e.CanConductBattlePhase;
                Check("Frozen Soul: activated in the victim's Main Phase 1, that turn's Battle Phase is skipped " +
                      "(the next turn's is not)",
                    turn3 && act && !canBp && canBp5 && !p.SkipNextBattlePhase,
                    $"turn3={turn3} act={act} canBp={canBp} canBp5={canBp5} turn={e.TurnNumber}");
            }
        }

        // ───────────────────────────── Arsenal Robber ─────────────────────────────

        static void ArsenalRobberDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { Celtic, PotOfGreed, AxeOfDespair, Kuriboh });
                var ar = PlaceSetTrap(e, p, ArsenalRobber, 0);
                var act = e.TryActivateSpellTrap(p, ar, fromHand: false);
                Drive(e, _ => false);
                Check("Arsenal Robber: the AI opponent sends its Equip Spell from its Deck to its GY",
                    act && o.Graveyard.Exists(c => c.CardId == AxeOfDespair) && !o.Deck.Contains(AxeOfDespair) &&
                    o.Deck.Count == 3 && o.Deck.Contains(PotOfGreed) && p.Graveyard.Contains(ar),
                    $"act={act} oGy=[{string.Join(",", o.Graveyard.Select(c => c.Name))}] deck={o.Deck.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { Celtic, PotOfGreed });
                var ar = PlaceSetTrap(e, p, ArsenalRobber, 0);
                var act = e.TryActivateSpellTrap(p, ar, fromHand: false);
                Drive(e, _ => false);
                var noEquip = act && o.Deck.Count == 2 && o.Graveyard.Count == 0;
                o.Deck.Clear();
                var ar2 = PlaceSetTrap(e, p, ArsenalRobber, 1);
                var emptyDeck = e.CanActivateSpellTrap(p, ar2, fromHand: false);
                Check("Arsenal Robber: no Equip Spell in the Deck sends nothing; an empty Deck cannot be chosen",
                    noEquip && !emptyDeck, $"act={act} deck={o.Deck.Count} gy={o.Graveyard.Count} empty={emptyDeck}");
            }

            {
                // The AI activates it: the Player chooses (a prompt over the Equip Spells of the Player's Deck only).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachOpponentMain(e);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, AxeOfDespair, PotOfGreed, BlackPendant, AxeOfDespair });
                var ar = PlaceSetTrap(e, o, ArsenalRobber, 0);
                var act = reached && e.TryActivateSpellTrap(o, ar, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var legal = Legal(e);
                Drive(e, c => c.CardId == BlackPendant);
                Check("Arsenal Robber (AI's): the Player picks among the Equip Spells of their own Deck",
                    act && legal.Count == 2 && legal.Exists(c => c.CardId == AxeOfDespair) &&
                    legal.Exists(c => c.CardId == BlackPendant) &&
                    p.Graveyard.Exists(c => c.CardId == BlackPendant) && !p.Deck.Contains(BlackPendant) &&
                    p.Deck.Count(id => id == AxeOfDespair) == 2,
                    $"reached={reached} act={act} legal=[{string.Join(",", legal.Select(c => c.Name))}] " +
                    $"pGy=[{string.Join(",", p.Graveyard.Select(c => c.Name))}]");
            }
        }

        // ───────────────────────────── Interdimensional Matter Transporter ─────────────────────────────

        static void TransporterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bewd = PlaceMonster(e, p, Bewd, 2, BattlePosition.Attack, true);
                var setMon = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, false);
                var opp = PlaceMonster(e, e.Opponent, Kuriboh, 0, BattlePosition.Attack, true);
                var imt = PlaceSetTrap(e, p, Transporter, 0);
                var act = e.TryActivateSpellTrap(p, imt, fromHand: false);
                var legal = Legal(e);
                Drive(e, c => c == bewd);
                var banished = p.Banished.Contains(bewd);
                e.TryEndTurnSafe(p);
                Drive(e, _ => false);
                var back = p.TryFindMonster(bewd, out var zone);
                Check("Interdimensional Matter Transporter: your face-up monster is banished, then returns in the End " +
                      "Phase to its zone in the same position",
                    act && !legal.Contains(setMon) && !legal.Contains(opp) && banished && back && zone == 2 &&
                    bewd.FaceUp && bewd.Position == BattlePosition.Attack && !p.Banished.Contains(bewd) &&
                    p.Graveyard.Contains(imt),
                    $"act={act} legal=[{string.Join(",", legal.Select(c => c.Name))}] banished={banished} " +
                    $"bewd={Where(e, bewd)} turn={e.TurnNumber}");
            }

            {
                // The usual use: answering the opponent's attack. The attack target leaves, the attack is not resolved
                // against it, and it returns in the opponent's End Phase.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var imt = PlaceSetTrap(e, p, Transporter, 0);
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                bewd.ClearAttackFlags();
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var declared = e.TryAttack(o, bewd, celtic);
                var offered = e.PendingResponse != null && e.PendingResponse.Responder == p &&
                              e.PendingResponse.LegalCards.Contains(imt);
                var act = offered && e.TryActivateSpellTrap(p, imt, fromHand: false);
                Drive(e, c => c == celtic);
                DrainCombat(e);
                var banished = p.Banished.Contains(celtic);
                var lpKept = p.LifePoints == pLp;
                e.TryEndTurnSafe(o);
                Drive(e, _ => false);
                Check("Interdimensional Matter Transporter: answering an attack, the target is banished (no battle), " +
                      "then returns in that turn's End Phase",
                    reached && declared && offered && act && banished && lpKept && p.TryFindMonster(celtic, out var z) &&
                    z == 1 && celtic.FaceUp && celtic.Position == BattlePosition.Attack,
                    $"reached={reached} declared={declared} offered={offered} act={act} banished={banished} " +
                    $"lp={pLp}->{p.LifePoints} celtic={Where(e, celtic)} turn={e.TurnNumber}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, Kuriboh, 0, BattlePosition.Attack, true);
                var imt = PlaceSetTrap(e, p, Transporter, 0);
                Check("Interdimensional Matter Transporter: no face-up monster you control, no activation",
                    !e.CanActivateSpellTrap(p, imt, fromHand: false), "");
            }
        }

        // ───────────────────────────── Dragged Down into the Grave ─────────────────────────────

        static void DraggedDownDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var dd = PutInHand(e, p, DraggedDown);
                var celtic = PutInHand(e, p, Celtic);
                var pot = PutInHand(e, p, PotOfGreed);
                var bewd = PutInHand(e, o, Bewd);
                var kuri = PutInHand(e, o, Kuriboh);
                var pDeck0 = p.Deck.Count;
                var oDeck0 = o.Deck.Count;
                var act = e.TryActivateSpellTrap(p, dd, fromHand: true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var legal = Legal(e);
                Drive(e, c => c == bewd);
                var pLost = p.Graveyard.Contains(celtic) ^ p.Graveyard.Contains(pot);
                Check("Dragged Down into the Grave: you pick from the opponent's hand, the AI picks from yours, both are " +
                      "discarded, then each player draws 1",
                    act && legal.Contains(bewd) && legal.Contains(kuri) && !legal.Contains(celtic) &&
                    o.Graveyard.Contains(bewd) && o.Hand.Contains(kuri) && pLost &&
                    p.Hand.Count == 2 && o.Hand.Count == 2 && p.Deck.Count == pDeck0 - 1 && o.Deck.Count == oDeck0 - 1 &&
                    p.Graveyard.Contains(dd),
                    $"act={act} legal=[{string.Join(",", legal.Select(c => c.Name))}] bewd={Where(e, bewd)} " +
                    $"celtic={Where(e, celtic)} pot={Where(e, pot)} hands={p.Hand.Count}/{o.Hand.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var dd = PutInHand(e, e.Player, DraggedDown);
                PutInHand(e, e.Opponent, Bewd);
                var aloneInHand = e.CanActivateSpellTrap(e.Player, dd, fromHand: true);
                var e2 = Setup(db, pDeck, aDeck);
                var dd2 = PutInHand(e2, e2.Player, DraggedDown);
                PutInHand(e2, e2.Player, Celtic);
                var oppEmpty = e2.CanActivateSpellTrap(e2.Player, dd2, fromHand: true);
                Check("Dragged Down into the Grave: needs another card in your hand and a card in the opponent's",
                    !aloneInHand && !oppEmpty, $"alone={aloneInHand} oppEmpty={oppEmpty}");
            }
        }

        // ───────────────────────────── Final Countdown ─────────────────────────────

        static void FinalCountdownDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var fc = PutInHand(e, p, FinalCountdown);
                var act = e.TryActivateSpellTrap(p, fc, fromHand: true);
                Drive(e, _ => false);
                var lp = p.LifePoints;
                var overEarly = false;
                for (var t = 1; t <= 19 && !e.GameOver; t++)
                    EndTurn(e);
                overEarly = e.GameOver;
                var turn20 = e.TurnNumber == 20;
                EndTurn(e);
                Check("Final Countdown: pay 2000 LP; the activator wins at the end of the 20th turn (activation turn = 1st)",
                    act && lp == 6000 && p.Graveyard.Contains(fc) && !overEarly && turn20 && e.GameOver &&
                    e.Winner == p && e.EndReason == DuelEndReason.CardEffect,
                    $"act={act} lp={lp} early={overEarly} turn20={turn20} over={e.GameOver} " +
                    $"winner={e.Winner?.Name} reason={e.EndReason} turn={e.TurnNumber}");
            }

            {
                // Turn 1: the Player's countdown; turn 2: the AI's. Pyro Clock of Destiny (turn 3) moves the AI's forward
                // 1 turn, so both end at the end of turn 20: a DRAW (Lua Duel.Win PLAYER_NONE).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var fc = PutInHand(e, p, FinalCountdown);
                var act1 = e.TryActivateSpellTrap(p, fc, fromHand: true);
                Drive(e, _ => false);
                EndTurn(e);
                var fc2 = PutInHand(e, o, FinalCountdown);
                var act2 = e.TurnPlayer == o && e.TryActivateSpellTrap(o, fc2, fromHand: true);
                Drive(e, _ => false);
                EndTurn(e);
                var clock = PlaceSetTrap(e, p, PyroClock, 0);
                var act3 = e.TurnPlayer == p && e.TryActivateSpellTrap(p, clock, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var holders = Legal(e);
                Drive(e, c => c == fc2);
                for (var t = 3; t <= 19 && !e.GameOver; t++)
                    EndTurn(e);
                var overEarly = e.GameOver;
                EndTurn(e);
                Check("Final Countdown: both countdowns ending on the same turn make the Duel a DRAW",
                    act1 && act2 && act3 && holders.Contains(fc) && holders.Contains(fc2) && !overEarly && e.GameOver &&
                    e.IsDraw,
                    $"acts={act1}/{act2}/{act3} holders=[{string.Join(",", holders.Select(c => Where(e, c)))}] " +
                    $"early={overEarly} over={e.GameOver} draw={e.IsDraw} winner={e.Winner?.Name} turn={e.TurnNumber}");
            }
        }

        // ───────────────────────────── Different Dimension Gate ─────────────────────────────

        static void GateDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var theirs = PlaceMonster(e, o, Celtic, 3, BattlePosition.Defense, false);
                var gate = PutInHand(e, p, DdGate);
                var act = e.TryActivateSpellTrap(p, gate, fromHand: true);
                var first = Legal(e);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(mine);
                var second = Legal(e);
                Drive(e, c => c == theirs);
                var bothGone = p.Banished.Contains(mine) && o.Banished.Contains(theirs) &&
                               p.TryFindSpellTrap(gate, out _) && gate.FaceUp;
                var mst = PutInHand(e, o, Mst);
                var reached = ReachOpponentMain(e);
                var destroyed = reached && e.TryActivateSpellTrap(o, mst, fromHand: true);
                Drive(e, c => c == gate);
                var mineBack = p.TryFindMonster(mine, out var mz);
                var theirsBack = o.TryFindMonster(theirs, out var tz);
                Check("Different Dimension Gate: 1 monster on each side banished; destroyed and sent to the GY, both " +
                      "return to their zones in the same positions",
                    act && first.Contains(mine) && !first.Contains(theirs) && second.Contains(theirs) &&
                    !second.Contains(mine) && bothGone && destroyed && p.Graveyard.Contains(gate) && mineBack && mz == 1 &&
                    mine.FaceUp && mine.Position == BattlePosition.Attack && theirsBack && tz == 3 && !theirs.FaceUp &&
                    theirs.Position == BattlePosition.Defense,
                    $"act={act} first=[{string.Join(",", first.Select(c => c.Name))}] " +
                    $"second=[{string.Join(",", second.Select(c => c.Name))}] both={bothGone} reached={reached} " +
                    $"destroyed={destroyed} gate={Where(e, gate)} mine={Where(e, mine)} theirs={Where(e, theirs)}");
            }

            {
                // Returned to the hand (Giant Trunade), not destroyed: the monsters stay banished.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var theirs = PlaceMonster(e, o, Celtic, 3, BattlePosition.Attack, true);
                var gate = PutInHand(e, p, DdGate);
                var act = e.TryActivateSpellTrap(p, gate, fromHand: true);
                Drive(e, c => c == mine || c == theirs);
                var trunade = PutInHand(e, p, GiantTrunade);
                var act2 = e.TryActivateSpellTrap(p, trunade, fromHand: true);
                Drive(e, _ => false);
                Check("Different Dimension Gate: leaving the field without being destroyed leaves both monsters banished",
                    act && act2 && p.Hand.Contains(gate) && p.Banished.Contains(mine) && o.Banished.Contains(theirs),
                    $"act={act} act2={act2} gate={Where(e, gate)} mine={Where(e, mine)} theirs={Where(e, theirs)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Bewd, 1, BattlePosition.Attack, true);
                var gate = PutInHand(e, e.Player, DdGate);
                var noOpp = e.CanActivateSpellTrap(e.Player, gate, fromHand: true);
                var e2 = Setup(db, pDeck, aDeck);
                PlaceMonster(e2, e2.Opponent, Bewd, 1, BattlePosition.Attack, true);
                var gate2 = PutInHand(e2, e2.Player, DdGate);
                var noOwn = e2.CanActivateSpellTrap(e2.Player, gate2, fromHand: true);
                Check("Different Dimension Gate: needs a monster on each side of the field",
                    !noOpp && !noOwn, $"noOpp={noOpp} noOwn={noOwn}");
            }
        }
    }
}
