using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CrvHandProceduresAndNamedSummons batch (CRV / EEN): Cyber Dragon (inherent Special Summon from the hand), Elemental
    /// HERO Bubbleman (its procedure and the draw-2 Summon trigger), King's Knight (Normal Summon with "Queen's Knight":
    /// "Jack's Knight" from the Deck), Des Frog (Tribute Summon: "Des Frog" from the hand / Deck up to the T.A.D.P.O.L.E.
    /// count), Water Dragon ("ATK becomes 0" for FIRE / Pyro, the destroyed-and-sent GY targets) and Winged Kuriboh LV10
    /// (Quick Effect in the opponent's Battle Phase: Tribute it, destroy Attack Position monsters, burn their printed ATK).
    /// Compile shapes, mini-duels through real engine entry points (TryActivateSpellTrap, TrySpecialSummonProcedure with the UI's placement,
    /// TryNormalSummon, TryFlipSummon, TryAttack, response windows, the pick flow) and fail-closed near misses.
    /// Runtime: <see cref="CrvHandProceduresAndNamedSummonsRuntime"/>.
    /// </summary>
    public static class CrvHandProceduresAndNamedSummonsRegressionTests
    {
        const int CyberDragon = 70095154;
        const int Bubbleman = 79979666;
        const int KingsKnight = 64788463;
        const int QueensKnight = 25652259;
        const int JacksKnight = 90876561;
        const int DesFrog = 84451804;
        const int Tadpole = 10456559;
        const int WaterDragon = 85066822;
        const int Hydrogeddon = 22587018;
        const int Oxygeddon = 58071123;
        const int Lv10 = 98585345;
        const int Tefnuit = 77901552;

        const int Celtic = 91152256;          // EARTH Warrior 1400/1200
        const int BlueEyes = 89631139;        // LIGHT Dragon 3000/2500
        const int MysticalElf = 15025844;     // LIGHT Spellcaster 800/2000
        const int FlameManipulator = 34460851; // FIRE Spellcaster 900/1000
        const int BlazingInpachi = 5464695;   // FIRE Pyro 1850/0
        const int Solemn = 41420027;
        const int DarkHole = 53129443;
        const int MirrorForce = 44095762;
        const int MonsterReborn = 83764718;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            CyberDragonDuels(db, pDeck, aDeck, Check);
            BubblemanDuels(db, pDeck, aDeck, Check);
            KingsKnightDuels(db, pDeck, aDeck, Check);
            DesFrogDuels(db, pDeck, aDeck, Check);
            WaterDragonDuels(db, pDeck, aDeck, Check);
            Lv10Duels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : $"full={p.FullyCompiled} " + string.Join("; ",
                p.ClauseList.Select(c => $"{c.Timing}/{c.Action}/{c.ActivationCondition}"));

        static CompiledCardProgram Prog(CardDatabase db, int id) =>
            db.Get(id) is { } d ? CardTextEffectCompiler.Compile(d) : null;

        static bool Implemented(CardDatabase db, int id) =>
            db.Get(id) is { } d && CardEffectStatus.Classify(d) == CardEffectStatusKind.Implemented;

        static CompiledCardProgram Probe(int id, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = "Crv Probe " + id,
                type = "Effect Monster",
                race = "Warrior",
                attribute = "DARK",
                level = 4,
                atk = 1000,
                def = 1000,
                desc = text
            });

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            Clean(e);
            return e;
        }

        static void Clean(DuelEngine e)
        {
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 12 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static List<CardInstance> OnField(DuelistState who, int id) =>
            who.MonstersOnField().Where(m => m != null && m.CardId == id).ToList();

        static int InDeck(DuelistState who, int id) => who.Deck.Count(x => x == id);

        static CardInstance Gy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static string State(DuelEngine e) =>
            $"turn={e.TurnNumber} tp={(e.TurnPlayer == e.Player ? "P" : "O")} phase={e.Phase} " +
            $"resp={e.PendingResponse?.Timing} pend={(e.PendingActivation != null)} chain={e.Chain.Count} " +
            $"P.LP={e.Player.LifePoints} O.LP={e.Opponent.LifePoints}";

        /// <summary>The Player's turn 3, Main Phase 1, board cleared (a turn with a Battle Phase).</summary>
        static DuelEngine PlayerTurn3(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Clean(e);
            return e;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            var cyber = Prog(db, CyberDragon);
            Check("Cyber Dragon: one hand procedure clause (no Chain Link), only while only the opponent controls a monster",
                cyber != null && cyber.FullyCompiled && Implemented(db, CyberDragon) && cyber.ClauseList.Count == 1 &&
                cyber.ClauseList[0].Action == EffectActionKind.SpecialSummonThisFromHandByOwnProcedure &&
                cyber.ClauseList[0].FromHand && !cyber.ClauseList[0].MakesChainLink &&
                cyber.ClauseList[0].ActivationCondition == CrvHandProceduresAndNamedSummonsRuntime.OnlyOpponentControlsMonsters,
                Describe(cyber));

            var tefnuit = Prog(db, Tefnuit);
            Check("near miss: Hieratic Dragon of Tefnuit shares the procedure sentence but stays not FullyCompiled",
                tefnuit != null && !tefnuit.FullyCompiled && !Implemented(db, Tefnuit), Describe(tefnuit));

            var bub = Prog(db, Bubbleman);
            Check("Elemental HERO Bubbleman: its own hand procedure + an optional Summon trigger draw 2 with its condition",
                bub != null && bub.FullyCompiled && Implemented(db, Bubbleman) && bub.ClauseList.Count == 2 &&
                bub.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonThisFromHandByOwnProcedure &&
                                           c.RequiresOnlyCardInHand) &&
                bub.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardSummoned && c.Action == EffectActionKind.Draw &&
                                           c.Amount == 2 && c.IsOptional && !c.RequiresThisNormalSummoned &&
                                           !c.RequiresThisSpecialSummoned && !c.RequiresThisFlipSummoned &&
                                           c.ActivationCondition ==
                                           CrvHandProceduresAndNamedSummonsRuntime.NoOtherCardsOnFieldOrInHand),
                Describe(bub));

            var bare = Probe(900212001, "When this card is Summoned: You can draw 2 cards.");
            Check("near miss: a draw-2 Summon trigger without Bubbleman's condition sentence gets no condition",
                bare != null && !bare.ClauseList.Exists(c =>
                    c.ActivationCondition == CrvHandProceduresAndNamedSummonsRuntime.NoOtherCardsOnFieldOrInHand),
                Describe(bare));

            var king = Prog(db, KingsKnight);
            Check("King's Knight: Normal Summon trigger, face-up \"Queen's Knight\" you control, \"Jack's Knight\" from the Deck",
                king != null && king.FullyCompiled && Implemented(db, KingsKnight) && king.ClauseList.Count == 1 &&
                king.ClauseList[0].Timing == EffectTiming.ThisCardSummoned &&
                king.ClauseList[0].Action == EffectActionKind.SpecialSummonNamed &&
                king.ClauseList[0].NamedCard == "Jack's Knight" && king.ClauseList[0].FromDeck &&
                !king.ClauseList[0].FromHand && !king.ClauseList[0].FromGrave &&
                king.ClauseList[0].RequiresThisNormalSummoned &&
                king.ClauseList[0].ActivationCondition == TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed &&
                king.ClauseList[0].ConditionName == "Queen's Knight",
                Describe(king));

            var frog = Prog(db, DesFrog);
            Check("Des Frog: Tribute Summon trigger, own name from the hand or Deck up to the T.A.D.P.O.L.E. count",
                frog != null && frog.FullyCompiled && Implemented(db, DesFrog) && frog.ClauseList.Count == 1 &&
                frog.ClauseList[0].Timing == EffectTiming.ThisCardSummoned &&
                frog.ClauseList[0].Action == EffectActionKind.SpecialSummonOwnNameFromHandOrDeckUpToNamedInGy &&
                frog.ClauseList[0].NamedCard == "Des Frog" && frog.ClauseList[0].ConditionName == "T.A.D.P.O.L.E." &&
                frog.ClauseList[0].RequiresThisTributeSummoned && frog.ClauseList[0].RequiresThisNormalSummoned,
                Describe(frog));

            var water = Prog(db, WaterDragon);
            Check("Water Dragon: FIRE / Pyro ATK 0 aura + optional destroyed-and-sent trigger (2 Hydrogeddon + 1 Oxygeddon)",
                water != null && water.FullyCompiled && Implemented(db, WaterDragon) && water.ClauseList.Count == 2 &&
                water.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                             c.Action == EffectActionKind.SetAtkZeroOfAttributeOrRaceOnField &&
                                             c.AttributeFilter == "FIRE" && c.RaceFilter == "Pyro") &&
                water.ClauseList.Exists(c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                             c.Action == EffectActionKind.SpecialSummonTargetedNamedGroupsFromYourGy &&
                                             c.RequiresDestroyed && c.IsOptional && c.IsPsctTarget &&
                                             c.CheckedAt == ConditionCheckedAt.Activation &&
                                             c.NamedCard == "Hydrogeddon" && c.Amount == 2 &&
                                             c.AltNamedCard == "Oxygeddon" && c.ConditionAmount == 1),
                Describe(water));

            var fireOnly = Probe(900212002, "The ATK of FIRE monsters on the field becomes 0.");
            Check("near miss: \"The ATK of FIRE monsters on the field becomes 0\" alone is not Water Dragon's aura",
                fireOnly != null &&
                !fireOnly.ClauseList.Exists(c => c.Action == EffectActionKind.SetAtkZeroOfAttributeOrRaceOnField),
                Describe(fireOnly));

            var lv10 = Prog(db, Lv10);
            Check("Winged Kuriboh LV10: a monster Quick Effect — Tribute itself; destroy Attack Position monsters, then burn",
                lv10 != null && lv10.FullyCompiled && Implemented(db, Lv10) && lv10.ClauseList.Count == 1 &&
                lv10.ClauseList[0].Timing == EffectTiming.MonsterQuickEffect &&
                lv10.ClauseList[0].Action == EffectActionKind.DestroyOppAttackPositionThenBurnPrintedAtk &&
                lv10.ClauseList[0].RequiresTributeThis && lv10.ClauseList[0].IsQuickEffect,
                Describe(lv10));

            var ownBp = Probe(900212003,
                "During your Battle Phase (Quick Effect): You can Tribute this face-up card; destroy as many Attack " +
                "Position monsters your opponent controls as possible, and if you do, inflict damage to your opponent " +
                "equal to the combined original ATK of those destroyed monster(s).");
            Check("near miss: the same Quick Effect during your own Battle Phase does not compile to it",
                ownBp != null &&
                !ownBp.ClauseList.Exists(c => c.Action == EffectActionKind.DestroyOppAttackPositionThenBurnPrintedAtk),
                Describe(ownBp));
        }

        // ───────────────────────────── Cyber Dragon ─────────────────────────────

        static void CyberDragonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var cyber = PutInHand(e, p, CyberDragon);
                var early = e.CanSpecialSummonProcedure(p, cyber);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var can = e.CanSpecialSummonProcedure(p, cyber);
                var aiWants = NegationsAndGySummonsRuntime.AiWantsOwnHandProcedure(e, p, cyber);
                var act = can && e.TrySpecialSummonProcedure(p, cyber);
                PassAll(e);
                Check("Cyber Dragon: Special Summoned from the hand while only the opponent controls a monster (no Chain Link, " +
                      "Normal Summon unused, face-up Attack Position by default; the AI takes it)",
                    !early && can && aiWants && act && p.TryFindMonster(cyber, out _) && cyber.FaceUp &&
                    cyber.Position == BattlePosition.Attack && !p.NormalSummonUsed &&
                    !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    $"early={early} can={can} ai={aiWants} act={act} {State(e)}");
            }

            {
                // Lua EFFECT_SPSUMMON_PROC (POS_FACEUP): the player chooses face-up Attack or Defense Position and the zone
                // (the UI's procedure flow: "Summon ATK" / "Summon DEF", then a Monster Zone).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var cyber = PutInHand(e, p, CyberDragon);
                var can = e.CanSpecialSummonProcedure(p, cyber);
                e.SetPendingSpecialSummonPlacement(3, BattlePosition.Defense);
                var ss = can && e.TrySpecialSummonProcedure(p, cyber);
                e.ClearPendingSpecialSummonPlacement();
                PassAll(e);
                Check("Cyber Dragon: the procedure Special Summons it in face-up Defense Position in the chosen zone",
                    can && ss && p.MonsterZones[3].Occupant == cyber && cyber.FaceUp &&
                    cyber.Position == BattlePosition.Defense && !p.NormalSummonUsed && !e.Chain.HasLinks,
                    $"can={can} ss={ss} pos={cyber.Position} faceUp={cyber.FaceUp} {State(e)}");
            }

            {
                // You control a monster (a face-down one counts): the procedure is not available.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, false);
                var cyber = PutInHand(e, p, CyberDragon);
                var can = e.CanSpecialSummonProcedure(p, cyber);
                var act = e.TrySpecialSummonProcedure(p, cyber);
                Check("near miss: Cyber Dragon cannot be Special Summoned this way while you control a monster",
                    !can && !act && p.Hand.Contains(cyber), $"can={can} act={act} {State(e)}");
            }

            {
                // A Summon, not an effect: the AI's Solemn Judgment negates it.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var cyber = PutInHand(e, p, CyberDragon);
                var act = e.TrySpecialSummonProcedure(p, cyber);
                PassAll(e);
                Check("Solemn Judgment (AI): negates Cyber Dragon's Special Summon from the hand",
                    act && p.Graveyard.Contains(cyber) && !p.TryFindMonster(cyber, out _) && o.Graveyard.Contains(solemn) &&
                    !e.Chain.HasLinks, $"act={act} {State(e)}");
            }

            {
                // Inherent Special Summon: the card stays Normal Summonable.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var cyber = PutInHand(e, p, CyberDragon);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                e.PendingTributes.Add(p.MonsterZones[0].Occupant);
                var ns = e.TryNormalSummon(p, cyber, false);
                PassAll(e);
                Check("Cyber Dragon stays Normal Summonable (Tribute Summon with 1 Tribute)",
                    ns && p.TryFindMonster(cyber, out _), $"ns={ns} {State(e)}");
            }
        }

        // ───────────────────────────── Elemental HERO Bubbleman ─────────────────────────────

        static void BubblemanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                // The only card in the hand: Special Summoned by its procedure, then it draws 2.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bub = PutInHand(e, p, Bubbleman);
                var deck = p.Deck.Count;
                var can = e.CanSpecialSummonProcedure(p, bub);
                var ss = can && e.TrySpecialSummonProcedure(p, bub);
                PassAll(e);
                Check("Bubbleman: the only card in the hand — Special Summoned by its procedure, then it draws 2",
                    can && ss && p.TryFindMonster(bub, out _) && p.Hand.Count == 2 && p.Deck.Count == deck - 2 &&
                    !p.NormalSummonUsed,
                    $"can={can} ss={ss} hand={p.Hand.Count} deck={deck}->{p.Deck.Count} {State(e)}");
            }

            {
                // Another card in the hand: no procedure; its Normal Summon leaves a card in the hand, so no draw.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bub = PutInHand(e, p, Bubbleman);
                PutInHand(e, p, Celtic);
                var can = e.CanSpecialSummonProcedure(p, bub);
                var deck = p.Deck.Count;
                var ns = e.TryNormalSummon(p, bub, false);
                PassAll(e);
                Check("near miss: Bubbleman with another card in the hand — no procedure, and its Normal Summon draws nothing",
                    !can && ns && p.TryFindMonster(bub, out _) && p.Hand.Count == 1 && p.Deck.Count == deck,
                    $"can={can} ns={ns} hand={p.Hand.Count} {State(e)}");
            }

            {
                // Normal Summoned as the last card in the hand, a Set card on the field: no draw.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceSetTrap(e, p, MirrorForce, 0);
                var bub = PutInHand(e, p, Bubbleman);
                var deck = p.Deck.Count;
                var ns = e.TryNormalSummon(p, bub, false);
                PassAll(e);
                Check("near miss: Bubbleman while you control another card — the draw cannot be activated",
                    ns && p.Hand.Count == 0 && p.Deck.Count == deck, $"ns={ns} hand={p.Hand.Count} {State(e)}");
            }

            {
                // Normal Summoned as the last card in the hand with an empty field: draw 2.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bub = PutInHand(e, p, Bubbleman);
                var deck = p.Deck.Count;
                var ns = e.TryNormalSummon(p, bub, false);
                PassAll(e);
                Check("Bubbleman: Normal Summoned with no other card in hand or on the field — draws 2",
                    ns && p.Hand.Count == 2 && p.Deck.Count == deck - 2, $"ns={ns} hand={p.Hand.Count} {State(e)}");
            }

            {
                // Flip Summon (EVENT_FLIP_SUMMON_SUCCESS) with an empty hand and field: draw 2.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bub = PlaceMonster(e, p, Bubbleman, 0, BattlePosition.Defense, false);
                var deck = p.Deck.Count;
                var flip = e.TryFlipSummon(p, bub);
                PassAll(e);
                Check("Bubbleman: Flip Summoned with no other card in hand or on the field — draws 2",
                    flip && bub.FaceUp && p.Hand.Count == 2 && p.Deck.Count == deck - 2,
                    $"flip={flip} hand={p.Hand.Count} {State(e)}");
            }

            {
                // Special Summoned from the GY by Monster Reborn, the last card in the hand: the resolved Monster Reborn is
                // about to leave the field (STATUS_LEAVE_CONFIRMED), so it does not count — draw 2.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bub = Gy(e, p, Bubbleman);
                var reborn = PutInHand(e, p, MonsterReborn);
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, reborn, true);
                if (e.IsAwaitingEffectTarget && Legal(e).Contains(bub)) e.TrySelectEffectTarget(bub);
                PassAll(e);
                Check("Bubbleman: Special Summoned by Monster Reborn (the last card in the hand) — draws 2",
                    act && p.TryFindMonster(bub, out _) && p.Graveyard.Contains(reborn) && p.Hand.Count == 2 &&
                    p.Deck.Count == deck - 2, $"act={act} hand={p.Hand.Count} {State(e)}");
            }

            {
                // IsPlayerCanDraw(tp,2): a 1-card Deck — the effect cannot be activated (no deck-out draw).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var bub = PutInHand(e, p, Bubbleman);
                while (p.Deck.Count > 1) p.Deck.RemoveAt(0);
                var ns = e.TryNormalSummon(p, bub, false);
                PassAll(e);
                Check("near miss: Bubbleman with 1 card in the Deck — the draw 2 is not activated",
                    ns && p.Hand.Count == 0 && p.Deck.Count == 1 && !e.GameOver,
                    $"ns={ns} hand={p.Hand.Count} deck={p.Deck.Count} {State(e)}");
            }
        }

        // ───────────────────────────── King's Knight ─────────────────────────────

        static void KingsKnightDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, QueensKnight, 0, BattlePosition.Attack, true);
                p.Deck.Insert(0, JacksKnight);
                var jacks = InDeck(p, JacksKnight);
                var king = PutInHand(e, p, KingsKnight);
                var ns = e.TryNormalSummon(p, king, false);
                PassAll(e);
                Check("King's Knight: Normal Summoned while you control \"Queen's Knight\" — \"Jack's Knight\" from the Deck",
                    ns && OnField(p, JacksKnight).Count == 1 && OnField(p, JacksKnight)[0].FaceUp &&
                    InDeck(p, JacksKnight) == jacks - 1,
                    $"ns={ns} jack={OnField(p, JacksKnight).Count} deck={jacks}->{InDeck(p, JacksKnight)} {State(e)}");
            }

            {
                // A face-down "Queen's Knight" does not count.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, QueensKnight, 0, BattlePosition.Defense, false);
                p.Deck.Insert(0, JacksKnight);
                var jacks = InDeck(p, JacksKnight);
                var king = PutInHand(e, p, KingsKnight);
                var ns = e.TryNormalSummon(p, king, false);
                PassAll(e);
                Check("near miss: King's Knight with only a face-down \"Queen's Knight\" — nothing is Special Summoned",
                    ns && OnField(p, JacksKnight).Count == 0 && InDeck(p, JacksKnight) == jacks,
                    $"ns={ns} {State(e)}");
            }

            {
                // The opponent's "Queen's Knight" does not count ("while you control").
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, e.Opponent, QueensKnight, 0, BattlePosition.Attack, true);
                p.Deck.Insert(0, JacksKnight);
                var king = PutInHand(e, p, KingsKnight);
                var ns = e.TryNormalSummon(p, king, false);
                PassAll(e);
                Check("near miss: King's Knight while only the opponent controls \"Queen's Knight\" — nothing happens",
                    ns && OnField(p, JacksKnight).Count == 0, $"ns={ns} {State(e)}");
            }

            {
                // Flip Summon is not a Normal Summon.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, QueensKnight, 0, BattlePosition.Attack, true);
                p.Deck.Insert(0, JacksKnight);
                var king = PlaceMonster(e, p, KingsKnight, 1, BattlePosition.Defense, false);
                var flip = e.TryFlipSummon(p, king);
                PassAll(e);
                Check("near miss: King's Knight Flip Summoned — its Normal Summon trigger does not apply",
                    flip && OnField(p, JacksKnight).Count == 0, $"flip={flip} {State(e)}");
            }
        }

        // ───────────────────────────── Des Frog ─────────────────────────────

        static (DuelEngine e, CardInstance frog, bool ns) FrogTributeSummon(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            int tadpoles, int handFrogs, int deckFrogs, bool asSet = false)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            p.Deck.RemoveAll(x => x == DesFrog);
            for (var i = 0; i < deckFrogs; i++) p.Deck.Insert(0, DesFrog);
            for (var i = 0; i < tadpoles; i++) Gy(e, p, Tadpole);
            for (var i = 0; i < handFrogs; i++) PutInHand(e, p, DesFrog);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var frog = PutInHand(e, p, DesFrog);
            e.PendingTributes.Add(celtic);
            var ns = e.TryNormalSummon(p, frog, asSet);
            return (e, frog, ns);
        }

        static void DesFrogDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                // 2 T.A.D.P.O.L.E.: the human takes the hand copy (Defense) and a Deck copy; then the count is reached.
                var (e, frog, ns) = FrogTributeSummon(db, pDeck, aDeck, tadpoles: 2, handFrogs: 1, deckFrogs: 2);
                var p = e.Player;
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Card == frog && Legal(e).Count == 2;
                var handCopy = Legal(e).FirstOrDefault(c => p.Hand.Contains(c));
                e.SetPendingSpecialSummonPlacement(1, BattlePosition.Defense);
                var first = handCopy != null && e.TrySelectEffectTarget(handCopy);
                var askedAgain = e.IsAwaitingEffectTarget && Legal(e).Count == 1 && !p.Hand.Contains(Legal(e)[0]);
                var second = askedAgain && e.TrySelectEffectTarget(Legal(e)[0]);
                PassAll(e);
                var frogs = OnField(p, DesFrog);
                Check("Des Frog: Tribute Summoned with 2 T.A.D.P.O.L.E. — 2 more \"Des Frog\" from the hand and Deck",
                    ns && asked && first && askedAgain && second && frogs.Count == 3 && frogs.All(m => m.FaceUp) &&
                    frogs.Count(m => m.Position == BattlePosition.Defense) == 1 && InDeck(p, DesFrog) == 1 &&
                    !p.Hand.Exists(h => h.CardId == DesFrog) && !e.IsAwaitingEffectTarget,
                    $"ns={ns} asked={asked} first={first} again={askedAgain} second={second} field={frogs.Count} " +
                    $"deck={InDeck(p, DesFrog)} {State(e)}");
            }

            {
                // CANCEL at the first pick: the optional trigger Special Summons none.
                var (e, frog, ns) = FrogTributeSummon(db, pDeck, aDeck, tadpoles: 2, handFrogs: 0, deckFrogs: 2);
                var p = e.Player;
                var declined = e.IsAwaitingEffectTarget && e.PendingActivation.Card == frog && e.CancelEffectTargeting();
                PassAll(e);
                Check("Des Frog: CANCEL at the first pick Special Summons none",
                    ns && declined && OnField(p, DesFrog).Count == 1 && InDeck(p, DesFrog) == 2,
                    $"ns={ns} declined={declined} {State(e)}");
            }

            {
                // 1 T.A.D.P.O.L.E.: at most 1 more, even with 2 copies in the Deck.
                var (e, frog, ns) = FrogTributeSummon(db, pDeck, aDeck, tadpoles: 1, handFrogs: 0, deckFrogs: 2);
                var p = e.Player;
                var first = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(Legal(e)[0]);
                PassAll(e);
                Check("Des Frog: up to the number of T.A.D.P.O.L.E. — 1 in the GY, 1 more \"Des Frog\"",
                    ns && first && OnField(p, DesFrog).Count == 2 && InDeck(p, DesFrog) == 1 && !e.IsAwaitingEffectTarget,
                    $"ns={ns} first={first} field={OnField(p, DesFrog).Count} {State(e)}");
            }

            {
                var (e, _, ns) = FrogTributeSummon(db, pDeck, aDeck, tadpoles: 0, handFrogs: 1, deckFrogs: 2);
                var p = e.Player;
                Check("near miss: Des Frog with no T.A.D.P.O.L.E. in the GY — no prompt, nothing is Special Summoned",
                    ns && !e.IsAwaitingEffectTarget && OnField(p, DesFrog).Count == 1 && InDeck(p, DesFrog) == 2,
                    $"ns={ns} {State(e)}");
            }

            {
                var (e, _, ns) = FrogTributeSummon(db, pDeck, aDeck, tadpoles: 2, handFrogs: 0, deckFrogs: 2, asSet: true);
                var p = e.Player;
                Check("near miss: Des Frog Tribute Set — not a Tribute Summon, no trigger",
                    ns && !e.IsAwaitingEffectTarget && OnField(p, DesFrog).Count == 1 && InDeck(p, DesFrog) == 2,
                    $"ns={ns} {State(e)}");
            }

            {
                // The AI: Tribute Summon on its turn — it takes as many as the T.A.D.P.O.L.E. count allows, no prompt.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var o = e.Opponent;
                o.Deck.RemoveAll(x => x == DesFrog);
                o.Deck.Insert(0, DesFrog);
                o.Deck.Insert(0, DesFrog);
                o.Deck.Insert(0, DesFrog);
                Gy(e, o, Tadpole);
                Gy(e, o, Tadpole);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var frog = PutInHand(e, o, DesFrog);
                e.PendingTributes.Add(celtic);
                var ns = e.TryNormalSummon(o, frog, false);
                PassAll(e);
                Check("Des Frog (AI): Tribute Summoned with 2 T.A.D.P.O.L.E. — 2 more from the Deck, no prompt left",
                    reached && ns && OnField(o, DesFrog).Count == 3 && InDeck(o, DesFrog) == 1 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} ns={ns} field={OnField(o, DesFrog).Count} {State(e)}");
            }
        }

        // ───────────────────────────── Water Dragon ─────────────────────────────

        static void WaterDragonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var water = PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Attack, true);
                var flame = PlaceMonster(e, o, FlameManipulator, 0, BattlePosition.Attack, true);
                var inpachi = PlaceMonster(e, o, BlazingInpachi, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var zeroFlame = flame.CurrentAtk;
                var zeroInpachi = inpachi.CurrentAtk;
                var celticAtk = celtic.CurrentAtk;
                var waterAtk = water.CurrentAtk;
                // A later ATK gain still adds to the 0 base.
                flame.UntilEndOfTurnAtk = 500;
                FieldSpellEffects.RefreshBoard(e);
                var boosted = flame.CurrentAtk;
                flame.UntilEndOfTurnAtk = 0;
                // Water Dragon leaves the field: the ATK comes back.
                p.MonsterZones[0].Occupant = null;
                p.Graveyard.Add(water);
                FieldSpellEffects.RefreshBoard(e);
                Check("Water Dragon: FIRE and Pyro monsters on the field have 0 ATK while it is face-up (others unchanged)",
                    zeroFlame == 0 && zeroInpachi == 0 && celticAtk == 1400 && waterAtk == 2800 && boosted == 500 &&
                    flame.CurrentAtk == 900 && inpachi.CurrentAtk == 1850,
                    $"flame={zeroFlame} inpachi={zeroInpachi} celtic={celticAtk} water={waterAtk} boosted={boosted} " +
                    $"after={flame.CurrentAtk}/{inpachi.CurrentAtk}");
            }

            {
                // Face-down Water Dragon: no aura; its own controller's FIRE monster is affected too when it is face-up.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var water = PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Defense, false);
                var mine = PlaceMonster(e, p, FlameManipulator, 1, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var faceDown = mine.CurrentAtk;
                water.FaceUp = true;
                FieldSpellEffects.RefreshBoard(e);
                Check("Water Dragon: no aura while face-down; face-up it reaches its own controller's FIRE monster too",
                    faceDown == 900 && mine.CurrentAtk == 0, $"faceDown={faceDown} faceUp={mine.CurrentAtk}");
            }

            {
                // Destroyed by Dark Hole: the human targets 2 Hydrogeddon + 1 Oxygeddon and Special Summons them.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var water = PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Attack, true);
                var h1 = Gy(e, p, Hydrogeddon);
                var h2 = Gy(e, p, Hydrogeddon);
                var h3 = Gy(e, p, Hydrogeddon);
                var ox = Gy(e, p, Oxygeddon);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, true);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Card == water && Legal(e).Count == 3 &&
                            Legal(e).All(c => c.CardId == Hydrogeddon);
                e.SetPendingSpecialSummonPlacement(2, BattlePosition.Defense);
                var t1 = asked && e.TrySelectEffectTarget(h1);
                var t2 = e.IsAwaitingEffectTarget && Legal(e).Count == 2 && e.TrySelectEffectTarget(h2);
                var askedOx = e.IsAwaitingEffectTarget && Legal(e).Count == 1 && Legal(e)[0] == ox;
                var t3 = askedOx && e.TrySelectEffectTarget(ox);
                PassAll(e);
                Check("Water Dragon: destroyed and sent to the GY — 2 Hydrogeddon + 1 Oxygeddon targeted and Special Summoned",
                    act && p.Graveyard.Contains(water) && asked && t1 && t2 && askedOx && t3 &&
                    p.TryFindMonster(h1, out _) && p.TryFindMonster(h2, out _) && p.TryFindMonster(ox, out _) &&
                    p.Graveyard.Contains(h3) && h1.Position == BattlePosition.Defense && !e.IsAwaitingEffectTarget,
                    $"act={act} asked={asked} t1={t1} t2={t2} ox={askedOx} t3={t3} {State(e)}");
            }

            {
                // Destroyed by battle (EFFECT_FLAG_DAMAGE_STEP): the trigger still offers its targets.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var water = PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Attack, true);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Oxygeddon);
                var bewd = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var attacked = e.TryAttack(o, bewd, water);
                var picks = 0;
                for (var i = 0; i < 30; i++)
                {
                    if (e.IsAwaitingEffectTarget && Legal(e).Count > 0)
                    {
                        if (e.TrySelectEffectTarget(Legal(e)[0])) picks++;
                        continue;
                    }

                    if (e.IsAwaitingResponse)
                    {
                        e.PassResponse();
                        continue;
                    }

                    if (e.HasDeclaredAttack)
                    {
                        e.ResolveDeclaredAttack();
                        continue;
                    }

                    break;
                }

                Check("Water Dragon: destroyed by battle — the trigger targets and Special Summons 2 Hydrogeddon + 1 Oxygeddon",
                    reached && attacked && p.Graveyard.Contains(water) && picks == 3 &&
                    OnField(p, Hydrogeddon).Count == 2 && OnField(p, Oxygeddon).Count == 1 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} attacked={attacked} picks={picks} {State(e)}");
            }

            {
                // CANCEL at the first target: not activated.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Attack, true);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Oxygeddon);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, true);
                PassAll(e);
                var declined = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                PassAll(e);
                Check("Water Dragon: CANCEL at the first target — nothing is Special Summoned",
                    act && declined && p.MonsterCount == 0 && p.Graveyard.Count(g => g.CardId == Hydrogeddon) == 2,
                    $"act={act} declined={declined} {State(e)}");
            }

            {
                // Only 1 Hydrogeddon: the effect cannot be activated (no prompt).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Attack, true);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Oxygeddon);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, true);
                PassAll(e);
                Check("near miss: Water Dragon with only 1 Hydrogeddon in the GY — no target prompt, nothing summoned",
                    act && !e.IsAwaitingEffectTarget && p.MonsterCount == 0, $"act={act} {State(e)}");
            }

            {
                // Tributed (not destroyed): no trigger.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var water = PlaceMonster(e, p, WaterDragon, 0, BattlePosition.Attack, true);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Hydrogeddon);
                Gy(e, p, Oxygeddon);
                var jack = PutInHand(e, p, JacksKnight);
                e.PendingTributes.Add(water);
                var ns = e.TryNormalSummon(p, jack, false);
                PassAll(e);
                Check("near miss: Water Dragon Tributed (not destroyed) — its GY trigger does not activate",
                    ns && p.Graveyard.Contains(water) && !e.IsAwaitingEffectTarget && p.MonsterCount == 1,
                    $"ns={ns} {State(e)}");
            }
        }

        // ───────────────────────────── Winged Kuriboh LV10 ─────────────────────────────

        static void Lv10Duels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                // The opponent attacks: the player Tributes LV10 — Blue-Eyes and Celtic (Attack Position) are destroyed,
                // the face-up Defense Position Mystical Elf stays; 3000 + 1400 damage.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var lv10 = PlaceMonster(e, p, Lv10, 0, BattlePosition.Defense, true);
                var bewd = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 2, BattlePosition.Defense, true);
                var lpBefore = o.LifePoints;
                e.NotifyPublic();
                var attacked = e.TryAttack(o, bewd, lv10);
                var window = e.IsAwaitingResponse && e.PendingResponse.Responder == p &&
                             e.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                             (e.PendingResponse.LegalCards?.Contains(lv10) ?? false);
                var act = window && e.TryActivateSpellTrap(p, lv10, false);
                DrainCombat(e);
                Check("Winged Kuriboh LV10: Tributed at the opponent's attack — their Attack Position monsters are destroyed " +
                      "and their printed ATK (3000 + 1400) is inflicted",
                    reached && attacked && window && act && p.Graveyard.Contains(lv10) && o.Graveyard.Contains(bewd) &&
                    o.Graveyard.Contains(celtic) && o.TryFindMonster(elf, out _) && o.LifePoints == lpBefore - 4400 &&
                    !e.Chain.HasLinks,
                    $"reached={reached} attacked={attacked} window={window} act={act} {State(e)}");
            }

            {
                // Not during the opponent's Main Phase, nor in your own turn.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var lv10 = PlaceMonster(e, p, Lv10, 0, BattlePosition.Defense, true);
                PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var oppMain = CrvHandProceduresAndNamedSummonsRuntime.CanActivateLv10(e, p, lv10, null, out var why);
                var e2 = Setup(db, pDeck, aDeck);
                var mine = PlaceMonster(e2, e2.Player, Lv10, 0, BattlePosition.Defense, true);
                PlaceMonster(e2, e2.Opponent, BlueEyes, 0, BattlePosition.Attack, true);
                var ownTurn = e2.CanActivateSpellTrap(e2.Player, mine, false);
                Check("near miss: Winged Kuriboh LV10 cannot be activated in the opponent's Main Phase or in your own turn",
                    reached && !oppMain && !ownTurn, $"reached={reached} oppMain={oppMain} ({why}) ownTurn={ownTurn}");
            }

            {
                // The AI Tributes its LV10 at the player's attack declaration: the player's attacker is destroyed, 3000 damage.
                var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv10 = PlaceMonster(e, o, Lv10, 0, BattlePosition.Defense, true);
                var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                var lpBefore = p.LifePoints;
                PassAll(e);
                e.TryEnterBattlePhase(p);
                PassAll(e);
                var attacked = e.CanAttack(p, bewd) && e.TryAttack(p, bewd, lv10);
                DrainCombat(e);
                Check("Winged Kuriboh LV10 (AI): Tributed at the player's attack — the attacker is destroyed, 3000 damage",
                    reached && attacked && o.Graveyard.Contains(lv10) && p.Graveyard.Contains(bewd) &&
                    p.LifePoints == lpBefore - 3000 && !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    $"reached={reached} attacked={attacked} {State(e)}");
            }
        }
    }
}
