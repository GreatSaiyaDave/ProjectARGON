using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// NamedSummonPairs batch: A Deal with Dark Ruler + Berserk Dragon, Contract with Exodia + Exodia Necross, Dark Flare
    /// Knight → Mirage Knight, Dedication through Light and Darkness. Compile shapes through the real compiler, near-miss
    /// sentences, and mini-duels through the engine entry points (TryActivateSpellTrap, TryAttack, TryEndTurnSafe,
    /// SpecialSummonToField). Turn 1 is the Player's, turn 2 the AI's. Runtime: <see cref="NamedSummonPairsRuntime"/>.
    /// </summary>
    public static class NamedSummonPairsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int DealWithDarkRuler = 6850209;   // Quick-Play Spell
        const int BerserkDragon = 85605684;      // Zombie Lv8 3500/0, Nomi
        const int ContractWithExodia = 33244944; // Normal Spell
        const int ExodiaNecross = 12600382;      // Spellcaster Lv4 1800/0, Nomi
        const int MirageKnight = 49217579;       // Warrior Lv8 2800/2000, Nomi
        const int DarkFlareKnight = 13722870;    // Fusion Lv6 2200/800
        const int Dedication = 69542930;         // Quick-Play Spell
        const int DarkMagicianOfChaos = 40737112;
        const int DarkMagician = 46986414;
        const int DarkMagicianAlt = 36996508;    // the same name, another passcode
        const int DarkMagicianGirl = 38033121;

        const int ExodiaHead = 33396948;
        const int LeftArm = 7902349;
        const int RightArm = 70903634;
        const int LeftLeg = 44519536;
        const int RightLeg = 8124921;

        const int BlueEyes = 89631139;           // Dragon Lv8 3000/2500
        const int Celtic = 91152256;             // Warrior Lv4 1400/1200
        const int DarkHole = 53129443;           // Normal Spell: destroy all monsters on the field
        const int MonsterReborn = 83764718;
        const int RingOfDestruction = 83555666;  // Trap: destroy, and if you do, both take its original ATK

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("NamedSummonPairs: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            DealDuels(db, pDeck, aDeck, Check);
            ContractDuels(db, pDeck, aDeck, Check);
            NecrossTurnDuels(db, pDeck, aDeck, Check);
            NecrossRingDuels(db, pDeck, aDeck, Check);
            MirageDuels(db, pDeck, aDeck, Check);
            DedicationDuels(db, pDeck, aDeck, Check);
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

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards, hands and GYs.</summary>
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
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static void EndTurn(DuelEngine e)
        {
            if (e.IsAwaitingResponse) e.PassResponse();
            e.RecoverStuckCombat();
            e.TryEndTurnSafe(e.TurnPlayer);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
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

        static bool Activate(DuelEngine e, DuelistState who, CardInstance card, Func<CardInstance, bool> want = null)
        {
            var ok = e.TryActivateSpellTrap(who, card, fromHand: true);
            Drive(e, want ?? (_ => true));
            return ok;
        }

        static bool AttackAndDrain(DuelEngine e, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (e.IsAwaitingResponse) e.PassResponse();
            if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(who);
            if (e.IsAwaitingResponse) e.PassResponse();
            var ok = e.TryAttack(who, attacker, target);
            DrainCombat(e);
            return ok;
        }

        static void AddPieces(DuelEngine e, DuelistState who, params int[] ids)
        {
            foreach (var id in ids) who.Graveyard.Add(e.CreateCardInstance(id));
        }

        static CardInstance OnField(DuelistState who, string name) =>
            who.MonstersOnField().FirstOrDefault(m => m != null && m.Name == name);

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(int id, bool spell, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "NamedSummonPairs probe", type = spell ? "Spell Card" : "Effect Monster",
                frameType = spell ? "spell" : "effect", race = spell ? "Quick-Play" : "Warrior",
                attribute = spell ? null : "DARK", level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Gy(DuelistState who) => string.Join(",", who.Graveyard.Select(c => c.Name));

        static string Field(DuelistState who) =>
            string.Join(",", who.MonstersOnField().Where(m => m != null).Select(m => $"{m.Name}({m.CurrentAtk})"));

        const string Pieces = "Exodia the Forbidden One|Right Arm of the Forbidden One|Left Arm of the Forbidden One|" +
                              "Right Leg of the Forbidden One|Left Leg of the Forbidden One";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var deal = Prog(db, DealWithDarkRuler);
            Check("A Deal with Dark Ruler: Archfiend name rule + Level 8 turn record condition + Berserk Dragon from hand or Deck",
                deal != null && deal.FullyCompiled && Implemented(db, DealWithDarkRuler) &&
                Has(deal, c => c.Action == EffectActionKind.AlwaysTreatedAsName && c.TreatedAsName == "Archfiend") &&
                Has(deal, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.SpecialSummonNamed &&
                               c.NamedCard == "Berserk Dragon" && c.FromHand && c.FromDeck && !c.FromGrave &&
                               c.ActivationCondition == NamedSummonPairsRuntime.YourLevelOrHigherMonsterSentToGyThisTurn &&
                               c.ConditionAmount == 8 && !c.RequiresTargetChoice),
                Describe(deal));

            var berserk = Prog(db, BerserkDragon);
            Check("Berserk Dragon: attack-all + its controller's End Phase -500 ATK (lasting)",
                berserk != null && berserk.FullyCompiled && Implemented(db, BerserkDragon) &&
                berserk.ClauseList.Count == 2 &&
                Has(berserk, c => c.Action == EffectActionKind.AttackAllOpponentMonstersOnceEach) &&
                Has(berserk, c => c.Timing == EffectTiming.EndPhase &&
                                  c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && c.Amount == -500 &&
                                  !c.OpponentTurnOnly),
                Describe(berserk));

            var contract = Prog(db, ContractWithExodia);
            Check("Contract with Exodia: 5 named pieces in your GY; Exodia Necross from your hand",
                contract != null && contract.FullyCompiled && Implemented(db, ContractWithExodia) &&
                contract.ClauseList.Count == 1 &&
                Has(contract, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.SpecialSummonNamed &&
                                   c.NamedCard == "Exodia Necross" && c.FromHand && !c.FromDeck && !c.FromGrave &&
                                   c.ActivationCondition == NamedSummonPairsRuntime.AllNamedInYourGy &&
                                   c.ConditionName == Pieces),
                Describe(contract));

            var necross = Prog(db, ExodiaNecross);
            Check("Exodia Necross: battle + Spell/Trap protection, Standby +500, self-destruction unless the 5 pieces",
                necross != null && necross.FullyCompiled && Implemented(db, ExodiaNecross) &&
                necross.ClauseList.Count == 4 &&
                Has(necross, c => c.Action == EffectActionKind.CannotBeDestroyedByBattle) &&
                Has(necross, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.CannotBeDestroyedBySpellTrapEffects) &&
                Has(necross, c => c.Timing == EffectTiming.StandbyPhase &&
                                  c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && c.Amount == 500 &&
                                  !c.OpponentTurnOnly) &&
                Has(necross, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.SelfDestroyUnlessAllNamedInYourGy && c.NamedCard == Pieces),
                Describe(necross));

            var mirage = Prog(db, MirageKnight);
            Check("Mirage Knight: damage-calculation original-ATK gain + End Phase banish if it battled",
                mirage != null && mirage.FullyCompiled && Implemented(db, MirageKnight) && mirage.ClauseList.Count == 2 &&
                Has(mirage, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                 c.Action == EffectActionKind.GainBattleTargetOriginalAtkDuringDamageCalculation) &&
                Has(mirage, c => c.Timing == EffectTiming.EndPhase &&
                                 c.Action == EffectActionKind.BanishThisAtEndPhaseIfBattledThisTurn),
                Describe(mirage));

            var ded = Prog(db, Dedication);
            Check("Dedication through Light and Darkness: Tribute 1 \"Dark Magician\"; DMoC from hand, Deck or GY",
                ded != null && ded.FullyCompiled && Implemented(db, Dedication) && ded.ClauseList.Count == 1 &&
                Has(ded, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.SpecialSummonNamed &&
                              c.NamedCard == "Dark Magician of Chaos" && c.FromHand && c.FromDeck && c.FromGrave &&
                              c.RequiresTributeCount == 1 && c.TributeNamedCard == "Dark Magician"),
                Describe(ded));

            // LocksRedirectsAndActivity batch: Dark Magician of Chaos is compiled now (End Phase GY Spell, battle banish,
            // leave-field redirect); this guard pins that compiled shape.
            var dmoc = Prog(db, DarkMagicianOfChaos);
            Check("Dark Magician of Chaos (LocksRedirectsAndActivity batch): End Phase GY Spell, battle banish, leave-field " +
                  "redirect",
                dmoc != null && dmoc.FullyCompiled && Implemented(db, DarkMagicianOfChaos) && dmoc.ClauseList.Count == 3 &&
                Has(dmoc, c => c.Action == EffectActionKind.AddGySpellToHandAtEndPhaseIfSummonedThisTurn) &&
                Has(dmoc, c => c.Timing == EffectTiming.AfterDamageCalculation && c.Action == EffectActionKind.Banish &&
                               c.RequiresBattledMonsterDestroyed) &&
                Has(dmoc, c => c.Action == EffectActionKind.BanishThisInsteadOfLeavingField),
                Describe(dmoc));
        }

        static void NearMisses(Chk Check)
        {
            var rider = CompileText(990001, true,
                "If a Level 8 or higher monster under your control was sent to the Graveyard this turn: Special Summon 1 " +
                "\"Berserk Dragon\" from your hand or Deck, and if you do, draw 1 card.");
            Check("Near miss: a Deal sentence with an extra rider is not compiled by the batch atom",
                !Has(rider, c => c.ActivationCondition == NamedSummonPairsRuntime.YourLevelOrHigherMonsterSentToGyThisTurn) &&
                (rider == null || !rider.FullyCompiled), Describe(rider));

            var four = CompileText(990002, true,
                "If you have all 4 of \"Exodia the Forbidden One\", \"Right Arm of the Forbidden One\", \"Left Arm of the " +
                "Forbidden One\", and \"Right Leg of the Forbidden One\" in your Graveyard: Special Summon 1 \"Exodia " +
                "Necross\" from your hand.");
            Check("Near miss: a 4-name Contract sentence is not compiled as the 5-piece condition",
                !Has(four, c => c.ActivationCondition == NamedSummonPairsRuntime.AllNamedInYourGy), Describe(four));

            var cardEffects = CompileText(990003, false, "Cannot be destroyed by battle or card effects.");
            Check("Near miss: \"battle or card effects\" is not the Spell/Trap-only protection",
                !Has(cardEffects, c => c.Action == EffectActionKind.CannotBeDestroyedBySpellTrapEffects),
                Describe(cardEffects));

            var noGy = CompileText(990004, true,
                "Tribute 1 \"Dark Magician\"; Special Summon 1 \"Dark Magician of Chaos\" from your hand or Deck.");
            // RevivalEquipsAndGearfried batch: this is now Release Restraint's sentence (hand or Deck only) — never the
            // Dedication atom's GY origin.
            Check("Near miss: a named Tribute summon without the GY origin is not the Dedication atom",
                !Has(noGy, c => c.FromGrave) &&
                Has(noGy, c => c.TributeNamedCard == "Dark Magician" && c.FromHand && c.FromDeck), Describe(noGy));

            var atkDef = CompileText(990005, false, "During each of your End Phases, this card loses 500 ATK and DEF.");
            Check("Near miss: an End Phase ATK and DEF loss is not the Berserk Dragon atom",
                !Has(atkDef, c => c.Timing == EffectTiming.EndPhase &&
                                  c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp), Describe(atkDef));

            var attackedOnly = CompileText(990006, false,
                "During the End Phase of a turn this card attacked: Banish this card.");
            Check("Near miss: \"a turn this card attacked\" (no \"or was attacked\") is not the Mirage Knight atom",
                !Has(attackedOnly, c => c.Action == EffectActionKind.BanishThisAtEndPhaseIfBattledThisTurn),
                Describe(attackedOnly));
        }

        // ───────────────────────────── A Deal with Dark Ruler / Berserk Dragon ─────────────────────────────

        static void DealDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Insert(0, BerserkDragon);
            var deal = PutInHand(e, p, DealWithDarkRuler);
            var noRecord = !e.CanActivateSpellTrap(p, deal, fromHand: true);

            // The opponent's Level 8 monster destroyed by the Player's Dark Hole: recorded for the opponent only.
            PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
            PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var hole = PutInHand(e, p, DarkHole);
            var holeOk = Activate(e, p, hole);
            var oppOnly = !e.CanActivateSpellTrap(p, deal, fromHand: true);
            Check("A Deal with Dark Ruler: no activation without a Level 8+ monster of yours sent to the GY this turn " +
                  "(the opponent's does not count)",
                noRecord && holeOk && oppOnly && o.Graveyard.Exists(c => c.CardId == BlueEyes),
                $"noRecord={noRecord} hole={holeOk} oppOnly={oppOnly} oGy=[{Gy(o)}]");

            // A copy of Berserk Dragon cannot be Special Summoned by any other way.
            var stray = PutInHand(e, p, BerserkDragon);
            p.Hand.Remove(stray);
            var strayRefused = !e.SpecialSummonToField(p, stray, BattlePosition.Attack, true);

            var mine = PlaceMonster(e, p, BlueEyes, 1, BattlePosition.Attack, true);
            e.SendCardToGrave(p, mine);
            var can = e.CanActivateSpellTrap(p, deal, fromHand: true);
            var deckBefore = p.Deck.Count(id => id == BerserkDragon);
            var activated = Activate(e, p, deal);
            var berserk = OnField(p, "Berserk Dragon");
            var deckAfter = p.Deck.Count(id => id == BerserkDragon);
            Check("A Deal with Dark Ruler: your Level 8 sent to the GY this turn → Berserk Dragon from the Deck, face-up",
                strayRefused && can && activated && berserk != null && berserk.FaceUp &&
                berserk.Position == BattlePosition.Attack && berserk.CurrentAtk == 3500 && deckAfter == deckBefore - 1,
                $"strayRefused={strayRefused} can={can} activated={activated} field=[{Field(p)}] deck {deckBefore}->{deckAfter}");

            if (berserk == null) return;
            EndTurn(e); // the Player's End Phase (turn 1)
            var afterOwn = berserk.CurrentAtk;
            EndTurn(e); // the opponent's End Phase (turn 2)
            var afterOpp = berserk.CurrentAtk;

            // The record is per turn: in the Player's next turn, without a new send, A Deal cannot be activated; a new
            // Level 8 send of the Player's own makes it legal again.
            if (e.IsAwaitingResponse) e.PassResponse();
            p.Deck.Insert(0, BerserkDragon); // a second copy to summon, so only the record decides
            var deal2 = PutInHand(e, p, DealWithDarkRuler);
            var resetRefused = e.TurnPlayer == p && !e.CanActivateSpellTrap(p, deal2, fromHand: true);
            var again = PlaceMonster(e, p, BlueEyes, 1, BattlePosition.Attack, true);
            e.SendCardToGrave(p, again);
            var legalAgain = e.CanActivateSpellTrap(p, deal2, fromHand: true);
            Check("A Deal with Dark Ruler: the Level 8 record resets each turn (a new send of your own counts)",
                resetRefused && legalAgain, $"turn={e.TurnNumber} reset={resetRefused} again={legalAgain}");
            p.Hand.Remove(deal2);

            EndTurn(e); // the Player's End Phase (turn 3)
            var afterOwn2 = berserk.CurrentAtk;
            Check("Berserk Dragon: loses 500 ATK during each of its controller's End Phases only (3500 → 3000 → 3000 → 2500)",
                afterOwn == 3000 && afterOpp == 3000 && afterOwn2 == 2500 && p.TryFindMonster(berserk, out _),
                $"own={afterOwn} opp={afterOpp} own2={afterOwn2} turn={e.TurnNumber}");

            // Monster Reborn cannot revive it (cannot be Special Summoned by other ways).
            var e2 = Setup(db, pDeck, aDeck);
            e2.Player.Graveyard.Add(e2.CreateCardInstance(BerserkDragon));
            var reborn = PutInHand(e2, e2.Player, MonsterReborn);
            Check("Berserk Dragon: Monster Reborn cannot target it in the GY",
                !e2.CanActivateSpellTrap(e2.Player, reborn, fromHand: true), "");
        }

        // ───────────────────────────── Contract with Exodia / Exodia Necross ─────────────────────────────

        static void ContractDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var necross = PutInHand(e, p, ExodiaNecross);
            var contract = PutInHand(e, p, ContractWithExodia);
            AddPieces(e, p, ExodiaHead, LeftArm, RightArm, RightLeg);
            AddPieces(e, o, LeftLeg); // in the opponent's GY: does not count
            var fourOnly = !e.CanActivateSpellTrap(p, contract, fromHand: true);
            var stray = e.CreateCardInstance(ExodiaNecross);
            var strayRefused = !e.SpecialSummonToField(p, stray, BattlePosition.Attack, true);
            AddPieces(e, p, LeftLeg);
            // From the hand only: a Necross in the Deck does not make it legal.
            p.Hand.Remove(necross);
            p.Deck.Insert(0, ExodiaNecross);
            var handOnly = !e.CanActivateSpellTrap(p, contract, fromHand: true);
            p.Deck.RemoveAt(0);
            p.Hand.Add(necross);
            var activated = Activate(e, p, contract);
            Check("Contract with Exodia: needs all 5 pieces in your GY; Special Summons Exodia Necross from your hand " +
                  "(no other Special Summon of Necross)",
                fourOnly && strayRefused && handOnly && activated && p.TryFindMonster(necross, out _) && necross.FaceUp &&
                !p.Hand.Contains(necross),
                $"fourOnly={fourOnly} strayRefused={strayRefused} handOnly={handOnly} activated={activated} " +
                $"field=[{Field(p)}]");

            // Dark Hole: Necross is not destroyed by a Spell effect; the other monsters are.
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true); // Necross holds zone 2
            var oppCeltic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var hole = PutInHand(e, p, DarkHole);
            var holeOk = Activate(e, p, hole);
            Check("Exodia Necross: not destroyed by Dark Hole (a Spell effect); the other monsters are",
                holeOk && p.TryFindMonster(necross, out _) && p.Graveyard.Contains(celtic) && o.Graveyard.Contains(oppCeltic),
                $"hole={holeOk} field=[{Field(p)}] pGy=[{Gy(p)}]");

            // A piece leaves the GY: Necross is destroyed by its own effect.
            var leg = p.Graveyard.First(c => c.CardId == LeftLeg);
            p.Graveyard.Remove(leg);
            p.Banished.Add(leg);
            FieldSpellEffects.RefreshBoard(e);
            Check("Exodia Necross: destroyed once a piece is missing from your GY",
                !p.TryFindMonster(necross, out _) && p.Graveyard.Contains(necross),
                $"field=[{Field(p)}] pGy=[{Gy(p)}]");
        }

        static void NecrossTurnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            AddPieces(e, p, ExodiaHead, LeftArm, RightArm, RightLeg, LeftLeg);
            var necross = PlaceMonster(e, p, ExodiaNecross, 0, BattlePosition.Attack, true);
            var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
            var lp = p.LifePoints;
            var attacked = AttackAndDrain(e, p, necross, be);
            Check("Exodia Necross: not destroyed by battle (1800 into 3000: the Player takes 1200)",
                reached && attacked && p.TryFindMonster(necross, out _) && o.TryFindMonster(be, out _) &&
                p.LifePoints == lp - 1200,
                $"reached={reached} attacked={attacked} field=[{Field(p)}] lp {lp}->{p.LifePoints}");

            o.MonsterZones[0].Occupant = null; // the opponent's turn stays quiet
            EndTurn(e); // turn 3 → turn 4 (the opponent's Standby Phase: no gain)
            var oppTurn = necross.CurrentAtk;
            EndTurn(e); // turn 4 → turn 5 (the Player's Standby Phase: +500)
            var ownTurn = necross.CurrentAtk;
            Check("Exodia Necross: +500 ATK during its controller's Standby Phase only (1800 → 1800 → 2300)",
                oppTurn == 1800 && ownTurn == 2300 && e.TurnPlayer == p && p.TryFindMonster(necross, out _),
                $"opp={oppTurn} own={ownTurn} turn={e.TurnNumber} tp={e.TurnPlayer?.Name}");
        }

        /// <summary>
        /// Ring of Destruction: "destroy that face-up monster, and if you do, take damage ..." (ygopro: Duel.Destroy(tc)~=0
        /// gates the damage). Exodia Necross is not destroyed by a Trap effect, so nobody takes damage.
        /// </summary>
        static void NecrossRingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The Player's Ring on the AI's turn, targeting the AI's Necross.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                foreach (var who in new[] { e.Player, e.Opponent })
                {
                    who.Hand.Clear();
                    who.Graveyard.Clear();
                    who.Banished.Clear();
                }
                var p = e.Player;
                var o = e.Opponent;
                AddPieces(e, o, ExodiaHead, LeftArm, RightArm, RightLeg, LeftLeg);
                var necross = PlaceMonster(e, o, ExodiaNecross, 0, BattlePosition.Attack, true);
                var ring = PlaceSetTrap(e, p, RingOfDestruction, 0);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(p, ring, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(necross);
                Drive(e, c => c == necross);
                Check("Ring of Destruction (the Player's) on the AI's Exodia Necross: not destroyed, so no damage to anyone",
                    act && o.TryFindMonster(necross, out _) && p.LifePoints == pLp && o.LifePoints == oLp &&
                    p.Graveyard.Contains(ring),
                    $"reached={reached} act={act} oField=[{Field(o)}] lp p {pLp}->{p.LifePoints} o {oLp}->{o.LifePoints}");
            }

            // The AI's Ring on the Player's turn, targeting the Player's Necross.
            {
                var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                AddPieces(e, p, ExodiaHead, LeftArm, RightArm, RightLeg, LeftLeg);
                var necross = PlaceMonster(e, p, ExodiaNecross, 0, BattlePosition.Attack, true);
                var ring = PlaceSetTrap(e, o, RingOfDestruction, 0);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, ring, fromHand: false);
                Drive(e, c => c == necross);
                Check("Ring of Destruction (the AI's) on the Player's Exodia Necross: not destroyed, so no damage to anyone",
                    act && p.TryFindMonster(necross, out _) && p.LifePoints == pLp && o.LifePoints == oLp &&
                    o.Graveyard.Contains(ring),
                    $"reached={reached} act={act} pField=[{Field(p)}] lp p {pLp}->{p.LifePoints} o {oLp}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── Mirage Knight / Dark Flare Knight ─────────────────────────────

        static void MirageDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Attacks: +3000 (Blue-Eyes' original ATK) during damage calculation only, then banished at the End Phase.
            {
                var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mk = PlaceMonster(e, p, MirageKnight, 0, BattlePosition.Attack, true);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                var attacked = AttackAndDrain(e, p, mk, be);
                var atkAfter = mk.CurrentAtk;
                var stayed = p.TryFindMonster(mk, out _);
                Check("Mirage Knight: gains the battle target's original ATK during damage calculation only",
                    reached && attacked && o.Graveyard.Contains(be) && stayed && o.LifePoints == oppLp - 2800 &&
                    atkAfter == 2800,
                    $"reached={reached} attacked={attacked} oGy=[{Gy(o)}] oppLp {oppLp}->{o.LifePoints} atkAfter={atkAfter}");
                EndTurn(e);
                Check("Mirage Knight: banished during the End Phase of a turn it battled",
                    !p.TryFindMonster(mk, out _) && p.Banished.Contains(mk), $"banished={p.Banished.Contains(mk)}");
            }

            // No battle: it stays.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var mk = PlaceMonster(e, p, MirageKnight, 0, BattlePosition.Attack, true);
                EndTurn(e);
                Check("Mirage Knight: stays at the End Phase of a turn it did not battle",
                    p.TryFindMonster(mk, out _) && !p.Banished.Contains(mk), $"turn={e.TurnNumber}");
            }

            // Attacked during the opponent's turn: it gains too, and is banished at the opponent's End Phase.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mk = PlaceMonster(e, p, MirageKnight, 0, BattlePosition.Attack, true);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                o.Hand.Clear();
                var oppLp = o.LifePoints;
                var attacked = e.TryAttack(o, be, mk);
                DrainCombat(e);
                var survived = p.TryFindMonster(mk, out _);
                var beGone = o.Graveyard.Contains(be);
                EndTurn(e);
                Check("Mirage Knight: attacked by Blue-Eyes it battles at 5800, and is banished at the opponent's End Phase",
                    reached && attacked && survived && beGone && o.LifePoints == oppLp - 2800 &&
                    p.Banished.Contains(mk) && !p.TryFindMonster(mk, out _),
                    $"reached={reached} attacked={attacked} survived={survived} beGone={beGone} " +
                    $"oppLp {oppLp}->{o.LifePoints} banished={p.Banished.Contains(mk)} turn={e.TurnNumber}");
            }

            // Dark Flare Knight destroyed by battle: Mirage Knight from the Deck (its one legal Special Summon).
            {
                var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Insert(0, MirageKnight);
                var stray = e.CreateCardInstance(MirageKnight);
                var strayRefused = !e.SpecialSummonToField(p, stray, BattlePosition.Attack, true);
                var dfk = PlaceMonster(e, p, DarkFlareKnight, 0, BattlePosition.Attack, true);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var attacked = AttackAndDrain(e, p, dfk, be);
                Drive(e, _ => true);
                var mk = OnField(p, "Mirage Knight");
                Check("Dark Flare Knight: destroyed by battle, it Special Summons Mirage Knight from the Deck " +
                      "(no other way can)",
                    reached && strayRefused && attacked && p.Graveyard.Contains(dfk) && mk != null && mk.FaceUp &&
                    !p.Deck.Contains(MirageKnight),
                    $"reached={reached} strayRefused={strayRefused} attacked={attacked} field=[{Field(p)}] lp {lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Dedication through Light and Darkness ─────────────────────────────

        static void DedicationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Insert(0, DarkMagicianOfChaos);
                var girl = PlaceMonster(e, p, DarkMagicianGirl, 0, BattlePosition.Attack, true);
                var ded = PutInHand(e, p, Dedication);
                var girlOnly = !e.CanActivateSpellTrap(p, ded, fromHand: true);
                var dm = PlaceMonster(e, p, DarkMagicianAlt, 1, BattlePosition.Attack, true);
                var activated = Activate(e, p, ded, c => c == dm);
                var dmoc = OnField(p, "Dark Magician of Chaos");
                Check("Dedication: Dark Magician Girl is no Tribute; Tributing \"Dark Magician\" (another passcode) Special " +
                      "Summons Dark Magician of Chaos from the Deck",
                    girlOnly && activated && p.Graveyard.Contains(dm) && p.TryFindMonster(girl, out _) && dmoc != null &&
                    dmoc.FaceUp && !p.Deck.Contains(DarkMagicianOfChaos),
                    $"girlOnly={girlOnly} activated={activated} field=[{Field(p)}] pGy=[{Gy(p)}]");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                while (p.Deck.Contains(DarkMagicianOfChaos)) p.Deck.Remove(DarkMagicianOfChaos);
                var gyDmoc = e.CreateCardInstance(DarkMagicianOfChaos);
                p.Graveyard.Add(gyDmoc);
                var dm = PlaceMonster(e, p, DarkMagician, 0, BattlePosition.Defense, false);
                var ded = PutInHand(e, p, Dedication);
                var activated = Activate(e, p, ded, c => c == dm);
                Check("Dedication: a face-down Dark Magician you control can be Tributed; DMoC comes from the GY",
                    activated && p.Graveyard.Contains(dm) && p.TryFindMonster(gyDmoc, out _) && gyDmoc.FaceUp,
                    $"activated={activated} field=[{Field(p)}] pGy=[{Gy(p)}]");
            }
        }
    }
}
