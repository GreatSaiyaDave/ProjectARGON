using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "BattleDestroyedGyRecovery" (v162): the GY-entry stamp (<see cref="GyEntryStamps"/>) and the cards that
    /// read it — Crimson Sentry, Lady Panther, The Forgiving Maiden, Fairy Guardian, Return of the Doomed, Revival Jam,
    /// Helpoemer and Rope of Life. Runtime: <see cref="BattleDestroyedGyRecoveryRuntime"/>.
    /// </summary>
    public static class BattleDestroyedGyRecoveryRegressionTests
    {
        const int CrimsonSentry = 28358902;
        const int LadyPanther = 38480590;
        const int ForgivingMaiden = 84080938;
        const int ForgivingMaidenAlt = 84080939;
        const int FairyGuardian = 22419772;
        const int ReturnOfTheDoomed = 19827717;
        const int RevivalJam = 31709826;     // Aqua Lv4 1500/500
        const int Helpoemer = 76052811;      // Fiend Lv5 2000/1400
        const int RopeOfLife = 93382620;
        const int Michizure = 37580756;

        const int Celtic = 91152256;         // Warrior Lv4 1400/1200
        const int SummonedSkull = 70781052;  // Fiend Lv6 2500/1200
        const int GiantSoldier = 13039848;   // Rock Lv3 1300/2000
        const int Mst = 5318639;             // Quick-Play Spell
        const int PotOfGreed = 55144522;     // Normal Spell
        const int DarkHole = 53129443;       // Normal Spell

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("BattleDestroyedGyRecovery: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            StampUnits(db, pDeck, aDeck, Check);
            SentryFamilyDuels(db, pDeck, aDeck, Check);
            FairyGuardianDuels(db, pDeck, aDeck, Check);
            ReturnOfTheDoomedDuels(db, pDeck, aDeck, Check);
            RevivalJamDuels(db, pDeck, aDeck, Check);
            HelpoemerDuels(db, pDeck, aDeck, Check);
            RopeOfLifeDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}]";

        static CompiledCardProgram CompileText(int id, string type, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "BattleDestroyedGyRecovery probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = type.StartsWith("Trap") || type.StartsWith("Spell") ? "Normal" : "Warrior",
                attribute = type.StartsWith("Trap") || type.StartsWith("Spell") ? null : "EARTH",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static DuelEngine Clean(DuelEngine engine)
        {
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The opponent's Battle Phase with an empty board and empty hands.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(engine);
            return Clean(engine);
        }

        /// <summary>The Player's Battle Phase (turn 3+) with an empty board and empty hands.</summary>
        static DuelEngine PlayerBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
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

            Clean(engine);
            reached = reached && engine.TryEnterBattlePhase(engine.Player) && engine.Phase == DuelPhase.Battle;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine;
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            return m;
        }

        /// <summary>The Player's Celtic Guardian attacks the opponent's Summoned Skull and is destroyed by battle.</summary>
        static CardInstance LoseBattle(DuelEngine e, int zone)
        {
            var celtic = Attacker(e, e.Player, Celtic, zone);
            var skull = e.Opponent.MonstersOnField().FirstOrDefault(m => m.CardId == SummonedSkull) ??
                        PlaceMonster(e, e.Opponent, SummonedSkull, 4, BattlePosition.Attack, true);
            e.NotifyPublic();
            e.TryAttack(e.Player, celtic, skull);
            DrainCombat(e);
            return celtic;
        }

        // ───────────────────────── compile shape ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            CompiledCardProgram Prog(int id) => db.TryGet(id, out var d) ? CardTextEffectCompiler.Compile(d) : null;
            bool Implemented(int id) => db.TryGet(id, out var d) &&
                                        CardEffectStatus.Classify(d) == CardEffectStatusKind.Implemented;

            bool SelfTribute(int id, EffectActionKind action, EffectZoneFilter zone, bool byBattle)
            {
                var p = Prog(id);
                return p != null && p.FullyCompiled && Implemented(id) && p.ClauseList.Count == 1 &&
                       p.ClauseList[0].Timing == EffectTiming.Activate && p.ClauseList[0].Action == action &&
                       p.ClauseList[0].Zone == zone && p.ClauseList[0].RequiresTributeThis &&
                       p.ClauseList[0].RequiresTargetChoice && p.ClauseList[0].IsPsctTarget &&
                       p.ClauseList[0].RequiresTargetSentToGyByBattleThisTurn == byBattle &&
                       p.ClauseList[0].RequiresTargetSentToGyByOpponentThisTurn == !byBattle;
            }

            Check("Crimson Sentry: Tribute this card; target a battle-destroyed monster of this turn; bottom of the Deck",
                SelfTribute(CrimsonSentry, EffectActionKind.PlaceGyTargetOnBottomOfDeck,
                    EffectZoneFilter.ControllerGyMonsters, true), Describe(Prog(CrimsonSentry)));
            Check("Lady Panther: the same, to the top of the Deck",
                SelfTribute(LadyPanther, EffectActionKind.PlaceTargetOnTopOfDeck, EffectZoneFilter.ControllerGyMonsters,
                    true), Describe(Prog(LadyPanther)));
            Check("The Forgiving Maiden (both ids): the same, to the hand",
                SelfTribute(ForgivingMaiden, EffectActionKind.AddFromGyToHand, EffectZoneFilter.ControllerGyMonsters, true) &&
                (!db.TryGet(ForgivingMaidenAlt, out _) ||
                 SelfTribute(ForgivingMaidenAlt, EffectActionKind.AddFromGyToHand, EffectZoneFilter.ControllerGyMonsters,
                     true)), Describe(Prog(ForgivingMaiden)));
            Check("Fairy Guardian: a Spell the opponent sent to your GY this turn, to the bottom of the Deck",
                SelfTribute(FairyGuardian, EffectActionKind.PlaceGyTargetOnBottomOfDeck, EffectZoneFilter.ControllerGySpells,
                    false), Describe(Prog(FairyGuardian)));

            var rotd = Prog(ReturnOfTheDoomed);
            Check("Return of the Doomed: discard 1 monster (cost); this turn's End Phase return, no target",
                rotd != null && rotd.FullyCompiled && Implemented(ReturnOfTheDoomed) && rotd.ClauseList.Count == 1 &&
                rotd.ClauseList[0].Action == EffectActionKind.AddMonsterDestroyedByBattleThisTurnFromGyToHandAtEndPhase &&
                rotd.ClauseList[0].RequiresDiscardCost && rotd.ClauseList[0].DiscardCostAttribute == "" &&
                !rotd.ClauseList[0].RequiresTargetChoice, Describe(rotd));

            var jam = Prog(RevivalJam);
            Check("Revival Jam: optional battle-destroyed trigger (pay 1000 LP) + its Standby Summon from the GY",
                jam != null && jam.FullyCompiled && Implemented(RevivalJam) && jam.ClauseList.Count == 2 &&
                jam.ClauseList[0].Timing == EffectTiming.SentFromFieldToGy &&
                jam.ClauseList[0].Action == EffectActionKind.ArmThisSpecialSummonAtNextStandby &&
                jam.ClauseList[0].PayLpAmount == 1000 && jam.ClauseList[0].IsOptional &&
                jam.ClauseList[0].RequiresThisDestroyedByBattle &&
                jam.ClauseList[1].Timing == EffectTiming.StandbyPhase && jam.ClauseList[1].ResolvesFromGy &&
                jam.ClauseList[1].Action == EffectActionKind.SpecialSummonArmedThisFromGyInDefenseAtStandby &&
                !jam.ClauseList[1].MakesChainLink, Describe(jam));

            var help = Prog(Helpoemer);
            Check("Helpoemer: end of the opponent's Battle Phase, from the GY, battle-destroyed: 1 random discard",
                help != null && help.FullyCompiled && Implemented(Helpoemer) && help.ClauseList.Count == 1 &&
                help.ClauseList[0].Timing == EffectTiming.EndOfBattlePhase &&
                help.ClauseList[0].Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                help.ClauseList[0].Amount == 1 && help.ClauseList[0].ResolvesFromGy &&
                help.ClauseList[0].OpponentTurnOnly && help.ClauseList[0].RequiresThisDestroyedByBattle, Describe(help));

            var rope = Prog(RopeOfLife);
            Check("Rope of Life: battle-destroyed window; discard the entire hand; target 1 of those; +800 ATK",
                rope != null && rope.FullyCompiled && Implemented(RopeOfLife) && rope.ClauseList.Count == 1 &&
                rope.ClauseList[0].Action == EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk &&
                rope.ClauseList[0].Amount == 800 && rope.ClauseList[0].DiscardEntireHandCost &&
                rope.ClauseList[0].TargetsThoseMonstersDestroyedByBattle &&
                rope.ClauseList[0].Zone == EffectZoneFilter.ControllerGyMonsters &&
                rope.ClauseList[0].ActivationCondition ==
                BattleDestroyedGyRecoveryRuntime.MonsterDestroyedByBattleSentToYourGy, Describe(rope));
        }

        static void NearMisses(CheckFn Check)
        {
            bool Refused(CompiledCardProgram p, EffectActionKind action) =>
                p != null && !p.FullyCompiled && !p.ClauseList.Exists(c => c != null && c.Action == action);

            var longer = CompileText(990162, "Effect Monster",
                "Tribute this face-up card to return 1 of your monsters destroyed as a result of battle during this turn " +
                "to the bottom of your Deck. Then draw 1 card.");
            Check("near miss: a longer Crimson Sentry text stays parked",
                Refused(longer, EffectActionKind.PlaceGyTargetOnBottomOfDeck), Describe(longer));

            var thatTurn = CompileText(990163, "Effect Monster",
                "Tribute this face-up card to return 1 of your monsters destroyed as a result of battle during that turn " +
                "to the top of your Deck.");
            Check("near miss: \"during that turn\" is not this batch's sentence",
                thatTurn == null || !thatTurn.ClauseList.Exists(c => c != null && c.RequiresTargetSentToGyByBattleThisTurn),
                Describe(thatTurn));

            var trap = CompileText(990164, "Effect Monster",
                "Tribute this face-up card to return 1 Trap Card sent to your Graveyard by your opponent's card effect " +
                "during this turn to the bottom of your Deck.");
            Check("near miss: Fairy Guardian with a Trap Card does not compile to the Spell recovery",
                trap == null || !trap.ClauseList.Exists(c => c != null && c.RequiresTargetSentToGyByOpponentThisTurn),
                Describe(trap));

            var onceJam = CompileText(990165, "Effect Monster",
                "When this card is destroyed by battle and sent to the Graveyard: You can pay 1000 Life Points; Special " +
                "Summon it in face-up Defense Position during your next turn's Standby Phase. You can only use this " +
                "effect once per Duel.");
            Check("near miss: a Revival Jam text with an extra rider stays parked",
                Refused(onceJam, EffectActionKind.ArmThisSpecialSummonAtNextStandby), Describe(onceJam));

            var ownBp = CompileText(990166, "Effect Monster",
                "Cannot be Special Summoned from the GY. At the end of your Battle Phase, if this card is in the GY because " +
                "it was destroyed by battle: Make your opponent discard 1 random card. This card must be in the GY to " +
                "activate and to resolve this effect.");
            Check("near miss: Helpoemer at the end of YOUR Battle Phase is not compiled",
                ownBp == null || !ownBp.FullyCompiled ||
                !ownBp.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.EndOfBattlePhase && c.ResolvesFromGy),
                Describe(ownBp));

            var oppRope = CompileText(990167, "Trap Card",
                "When a monster(s) is destroyed by battle and sent to your opponent's GY: Discard your entire hand " +
                "(min.1), then target 1 of those monsters; Special Summon it, and if you do, it gains 800 ATK.");
            Check("near miss: Rope of Life for the opponent's GY is not compiled",
                Refused(oppRope, EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk), Describe(oppRope));
        }

        // ───────────────────────── GY-entry stamp ─────────────────────────

        static void StampUnits(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var e = PlayerBattle(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var celtic = LoseBattle(e, 0);
            var battle = GyEntryStamps.DestroyedByBattleThisTurn(e, p, celtic) && celtic.GyEntryTurn == e.TurnNumber &&
                         (celtic.GyEntryReason & GyEntryReason.Destroyed) != 0 && celtic.GyEntryReasonPlayer == null;

            // Out of the GY and back by a discard the same turn: the battle stamp is gone.
            p.Graveyard.Remove(celtic);
            p.Hand.Add(celtic);
            MonsterEffects.DiscardFromHandByInstance(p, celtic, DiscardCause.Cost);
            var restamped = p.Graveyard.Contains(celtic) && !GyEntryStamps.DestroyedByBattleThisTurn(e, p, celtic) &&
                            (celtic.GyEntryReason & (GyEntryReason.Discard | GyEntryReason.Cost)) ==
                            (GyEntryReason.Discard | GyEntryReason.Cost);

            // Destroyed by the opponent's card effect: reason player = the opponent.
            var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Attack, true);
            var hole = PlaceSetTrap(e, opp, DarkHole, 0);
            TextEffectRuntime.DestroyCard(e, soldier, hole, byPlayer: opp);
            var byOpp = GyEntryStamps.SentByOpponentThisTurn(e, p, soldier) &&
                        !GyEntryStamps.DestroyedByBattleThisTurn(e, p, soldier) &&
                        (soldier.GyEntryReason & GyEntryReason.Effect) != 0;

            // Special Summoned from the GY: the stamp ends.
            p.Graveyard.Remove(soldier);
            e.NextSpecialSummonFromGraveyard = true;
            var back = e.SpecialSummonToField(p, soldier, BattlePosition.Attack, true);
            var cleared = back && soldier.GyEntryTurn == 0 && soldier.GyEntryReasonPlayer == null;
            Check("GY-entry stamp: battle / discard cost / opponent's effect; restamped on re-entry, cleared on the field",
                reached && battle && restamped && byOpp && cleared,
                $"reached={reached} battle={battle} restamped={restamped} byOpp={byOpp} cleared={cleared} " +
                $"reason={celtic.GyEntryReason}/{soldier.GyEntryReason}");
        }

        // ───────────────────────── Crimson Sentry / Lady Panther / The Forgiving Maiden ─────────────────────────

        static void SentryFamilyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            foreach (var (id, label) in new[]
                     {
                         (CrimsonSentry, "Crimson Sentry"), (LadyPanther, "Lady Panther"), (ForgivingMaiden, "The Forgiving Maiden")
                     })
            {
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var sentry = PlaceMonster(e, p, id, 1, BattlePosition.Attack, true);
                // Not targets: a monster sent to the GY by a card effect this turn, and one destroyed by battle on an
                // earlier turn.
                var byEffect = PlaceMonster(e, p, GiantSoldier, 2, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(e, byEffect, PlaceSetTrap(e, opp, DarkHole, 0), byPlayer: opp);
                var old = e.CreateCardInstance(Celtic);
                p.Graveyard.Add(old);
                GyEntryStamps.Entered(e, old, GyEntryReason.Destroyed | GyEntryReason.Battle);
                old.GyEntryTurn = e.TurnNumber - 1;
                // In Main Phase 2 of another duel with only those two in the GY, the ignition cannot be activated.
                var e0 = PlayerBattle(db, pDeck, aDeck, out _);
                e0.TryEnterMainPhase2(e0.Player);
                PassAll(e0);
                var sentry0 = PlaceMonster(e0, e0.Player, id, 1, BattlePosition.Attack, true);
                var byEffect0 = PlaceMonster(e0, e0.Player, GiantSoldier, 2, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(e0, byEffect0, PlaceSetTrap(e0, e0.Opponent, DarkHole, 0),
                    byPlayer: e0.Opponent);
                e0.NotifyPublic();
                var earlyRefused = e0.Phase == DuelPhase.Main2 && e0.Player.Graveyard.Contains(byEffect0) &&
                                   !e0.TryActivateSpellTrap(e0.Player, sentry0, fromHand: false) &&
                                   e0.Player.TryFindMonster(sentry0, out _);
                var celtic = LoseBattle(e, 0);
                var deckBefore = p.Deck.Count;
                e.TryEnterMainPhase2(p);
                PassAll(e);
                var used = e.TryActivateSpellTrap(p, sentry, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets;
                var onlyCeltic = !e.IsAwaitingEffectTarget || (legal != null && legal.Count == 1 && legal[0] == celtic);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                PassAll(e);
                bool placed = id switch
                {
                    CrimsonSentry => p.Deck.Count == deckBefore + 1 && p.Deck[p.Deck.Count - 1] == Celtic,
                    LadyPanther => p.Deck.Count == deckBefore + 1 && p.Deck[0] == Celtic,
                    _ => p.Hand.Contains(celtic)
                };
                Check($"{label}: Tribute it; the monster destroyed by battle this turn is returned (not an effect- or older one)",
                    reached && earlyRefused && used && onlyCeltic && placed && p.Graveyard.Contains(sentry) &&
                    !p.Graveyard.Contains(celtic) && p.Graveyard.Contains(byEffect) && p.Graveyard.Contains(old),
                    $"reached={reached} early={earlyRefused} used={used} only={onlyCeltic} placed={placed} " +
                    $"legal={(legal == null ? "-" : string.Join(",", legal.Select(l => l.Name)))}");
            }
        }

        // ───────────────────────── Fairy Guardian ─────────────────────────

        static void FairyGuardianDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var e = PlayerBattle(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            e.TryEnterMainPhase2(p);
            PassAll(e);
            var guardian = PlaceMonster(e, p, FairyGuardian, 0, BattlePosition.Attack, true);
            // The opponent's MST destroys the Player's Set Pot of Greed; the opponent's effect discards Dark Hole at
            // random; the Player's own effect destroys another Set Spell (not a target).
            var pot = PlaceSetTrap(e, p, PotOfGreed, 1);
            var oppMst = PlaceSetTrap(e, opp, Mst, 0);
            TextEffectRuntime.DestroyCard(e, pot, oppMst, byPlayer: opp);
            var hole = PutInHand(e, p, DarkHole);
            TextEffectRuntime.DiscardRandomFromOpponentHand(e, p, oppMst, null);
            var ownMst = PlaceSetTrap(e, p, Mst, 2);
            var own = PlaceSetTrap(e, p, PotOfGreed, 3);
            TextEffectRuntime.DestroyCard(e, own, ownMst, byPlayer: p);
            e.NotifyPublic();
            var deckBefore = p.Deck.Count;
            var used = e.TryActivateSpellTrap(p, guardian, fromHand: false);
            var legal = e.PendingActivation?.LegalTargets?.ToList();
            var targets = legal != null && legal.Count == 2 && legal.Contains(pot) && legal.Contains(hole) &&
                          !legal.Contains(own);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(hole);
            PassAll(e);
            Check("Fairy Guardian: a Spell destroyed or discarded by the opponent's effect this turn goes to the bottom",
                reached && used && targets && p.Graveyard.Contains(guardian) && !p.Graveyard.Contains(hole) &&
                p.Deck.Count == deckBefore + 1 && p.Deck[p.Deck.Count - 1] == DarkHole && p.Graveyard.Contains(pot),
                $"reached={reached} used={used} targets={targets} " +
                $"legal={(legal == null ? "-" : string.Join(",", legal.Select(l => l.Name)))}");
        }

        // ───────────────────────── Return of the Doomed ─────────────────────────

        static void ReturnOfTheDoomedDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Activated in Main Phase 1 with nothing destroyed yet; Celtic Guardian is destroyed by battle afterwards
                // and returns to the hand at the end of the turn. The discarded monster does not.
                var e = Fresh(db, pDeck, aDeck);
                var reached = false;
                for (var t = 0; t < 12 && !reached; t++)
                {
                    if (e.IsAwaitingResponse) e.PassResponse();
                    e.RecoverStuckCombat();
                    reached = e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3;
                    if (!reached) e.TryEndTurnSafe(e.TurnPlayer);
                }

                Clean(e);
                var p = e.Player;
                var rotd = PutInHand(e, p, ReturnOfTheDoomed);
                var fodder = PutInHand(e, p, GiantSoldier);
                PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, rotd, fromHand: true);
                var costOffer = e.PendingActivation?.LegalTargets?.ToList();
                var monstersOnly = costOffer == null || (costOffer.Count == 1 && costOffer[0] == fodder);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(fodder);
                PassAll(e);
                var paid = p.Graveyard.Contains(fodder) && p.Graveyard.Contains(rotd);
                e.TryEnterBattlePhase(p);
                PassAll(e);
                var celtic = LoseBattle(e, 0);
                var celticGy = p.Graveyard.Contains(celtic);
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Return of the Doomed: discard a monster (cost); the battle-destroyed monster returns at the end of the turn",
                    reached && used && monstersOnly && paid && celticGy && p.Hand.Contains(celtic) &&
                    p.Graveyard.Contains(fodder) && e.TurnPlayer == e.Opponent,
                    $"reached={reached} used={used} monsters={monstersOnly} paid={paid} celticGy={celticGy} " +
                    $"hand={p.Hand.Contains(celtic)} turn={e.TurnPlayer?.Name}");
            }

            {
                // Two monsters destroyed by battle: the human picks 1 at the end of the turn; the End Phase then goes on.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = LoseBattle(e, 0);
                var celtic2 = LoseBattle(e, 1);
                e.TryEnterMainPhase2(p);
                PassAll(e);
                var rotd = PutInHand(e, p, ReturnOfTheDoomed);
                var fodder = PutInHand(e, p, GiantSoldier);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, rotd, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(fodder))
                    e.TrySelectEffectTarget(fodder);
                PassAll(e);
                e.TryEndTurnSafe(p);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 2 &&
                            e.PendingActivation.LegalTargets.Contains(celtic) &&
                            e.PendingActivation.LegalTargets.Contains(celtic2);
                var picked = asked && e.TrySelectEffectTarget(celtic2);
                PassAll(e);
                Check("Return of the Doomed: with 2 candidates the human chooses 1; then the turn ends",
                    reached && used && asked && picked && p.Hand.Contains(celtic2) && p.Graveyard.Contains(celtic) &&
                    e.TurnPlayer == e.Opponent,
                    $"reached={reached} used={used} asked={asked} picked={picked} turn={e.TurnPlayer?.Name}");
            }
        }

        // ───────────────────────── Revival Jam ─────────────────────────

        static void RevivalJamDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Summoned Skull destroys the Player's Revival Jam: PAY 1000 → Special Summoned in face-up
                // Defense Position in the Player's next Standby Phase.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jam = PlaceMonster(e, p, RevivalJam, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, jam);
                DrainCombat(e);
                var lpBefore = p.LifePoints;
                var asked = e.PendingActivation != null && e.PendingActivation.Card == jam &&
                            e.PendingActivation.AwaitingLpCost && e.PendingActivation.LpCostChoices.Contains(1000);
                var paid = asked && e.TrySelectLpCost(1000) && p.LifePoints == lpBefore - 1000;
                DrainCombat(e);
                var stillGy = p.Graveyard.Contains(jam);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                var summoned = p.TryFindMonster(jam, out _) && jam.FaceUp && jam.Position == BattlePosition.Defense &&
                               e.TurnPlayer == p;
                Check("Revival Jam: pay 1000 LP when destroyed by battle; Special Summoned in Defense in your next Standby",
                    reached && asked && paid && stillGy && summoned,
                    $"reached={reached} asked={asked} paid={paid} gy={stillGy} summoned={summoned} pos={jam.Position}");
            }

            {
                // Cancel: no LP paid, no Summon.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jam = PlaceMonster(e, p, RevivalJam, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, jam);
                DrainCombat(e);
                var lpBefore = p.LifePoints;
                var asked = e.PendingActivation?.Card == jam;
                var declined = asked && e.CancelEffectTargeting() && p.LifePoints == lpBefore;
                DrainCombat(e);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                Check("Revival Jam: Cancel pays nothing and it stays in the GY",
                    reached && asked && declined && p.Graveyard.Contains(jam) && !p.TryFindMonster(jam, out _),
                    $"reached={reached} asked={asked} declined={declined}");
            }

            {
                // The flag is lost when it leaves the GY: paid, then returned to the hand and discarded again.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jam = PlaceMonster(e, p, RevivalJam, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, jam);
                DrainCombat(e);
                var paid = e.PendingActivation?.Card == jam && e.TrySelectLpCost(1000);
                DrainCombat(e);
                p.Graveyard.Remove(jam);
                p.Hand.Add(jam);
                MonsterEffects.DiscardFromHandByInstance(p, jam, DiscardCause.Cost);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                Check("Revival Jam: a copy that left the GY loses its flag",
                    reached && paid && p.Graveyard.Contains(jam) && !p.TryFindMonster(jam, out _),
                    $"reached={reached} paid={paid} gy={p.Graveyard.Contains(jam)}");
            }

            {
                // The AI's Revival Jam: it pays and comes back in its own next Standby Phase.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jam = PlaceMonster(e, opp, RevivalJam, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, p, SummonedSkull, 0);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                e.TryAttack(p, skull, jam);
                DrainCombat(e);
                var paid = opp.LifePoints == lp - 1000 - 1000 && !e.IsAwaitingEffectTarget;
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Revival Jam (AI): pays 1000 LP and is Special Summoned in its controller's next Standby Phase",
                    reached && paid && opp.TryFindMonster(jam, out _) && jam.Position == BattlePosition.Defense,
                    $"reached={reached} paid={paid} lp={opp.LifePoints} field={opp.TryFindMonster(jam, out _)}");
            }
        }

        // ───────────────────────── Helpoemer ─────────────────────────

        static void HelpoemerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Destroyed by battle in the opponent's Battle Phase: the opponent discards 1 random card at its end, and
                // again at the end of their next Battle Phase; not at the end of the Player's own Battle Phase.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var help = PlaceMonster(e, p, Helpoemer, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                PutInHand(e, opp, PotOfGreed);
                PutInHand(e, opp, DarkHole);
                PutInHand(e, opp, Mst);
                e.NotifyPublic();
                e.TryAttack(opp, skull, help);
                DrainCombat(e);
                var inGy = p.Graveyard.Contains(help);
                var before = opp.Hand.Count;
                e.TryEnterMainPhase2(opp);
                PassAll(e);
                var first = opp.Hand.Count == before - 1;
                e.TryEndTurnSafe(opp);
                PassAll(e);
                // The Player's own Battle Phase: nothing.
                var oppHand = opp.Hand.Count;
                e.TryEnterBattlePhase(p);
                PassAll(e);
                e.TryEnterMainPhase2(p);
                PassAll(e);
                var ownBpQuiet = opp.Hand.Count == oppHand;
                e.TryEndTurnSafe(p);
                PassAll(e);
                var handNow = opp.Hand.Count;
                e.TryEnterBattlePhase(opp);
                PassAll(e);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                var second = opp.Hand.Count == handNow - 1;
                Check("Helpoemer: each opponent's Battle Phase ends with a random discard while it stays in the GY",
                    reached && inGy && first && ownBpQuiet && second,
                    $"reached={reached} inGy={inGy} first={first} ownBp={ownBpQuiet} second={second}");
            }

            {
                // Destroyed by a card effect: nothing.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var help = PlaceMonster(e, p, Helpoemer, 0, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(e, help, PlaceSetTrap(e, opp, DarkHole, 0), byPlayer: opp);
                PutInHand(e, opp, PotOfGreed);
                e.NotifyPublic();
                e.TryEnterMainPhase2(opp);
                PassAll(e);
                Check("Helpoemer: not when it is in the GY because of a card effect",
                    reached && p.Graveyard.Contains(help) && opp.Hand.Count == 1,
                    $"reached={reached} hand={opp.Hand.Count}");
            }
        }

        // ───────────────────────── Rope of Life ─────────────────────────

        static void RopeOfLifeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Summoned Skull destroys Celtic Guardian: Rope of Life at the end of the Damage Step —
                // the whole hand is discarded, Celtic Guardian comes back in Defense Position (the Player's pick) with
                // 800 more ATK.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var rope = PlaceSetTrap(e, p, RopeOfLife, 0);
                var h1 = PutInHand(e, p, PotOfGreed);
                var h2 = PutInHand(e, p, GiantSoldier);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                for (var i = 0; i < 4 && e.IsAwaitingResponse &&
                                !(e.PendingResponse?.LegalCards?.Contains(rope) ?? false); i++)
                    e.PassResponse();
                var offered = e.PendingResponse?.Timing == ResponseTiming.EndOfDamageStep &&
                              e.PendingResponse.LegalCards.Contains(rope);
                var act = offered && e.TryActivateSpellTrap(p, rope, fromHand: false);
                var discarded = p.Hand.Count == 0 && p.Graveyard.Contains(h1) && p.Graveyard.Contains(h2);
                var legal = e.PendingActivation?.LegalTargets;
                var onlyCeltic = legal != null && legal.Count == 1 && legal[0] == celtic;
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var picked = onlyCeltic && e.TrySelectEffectTarget(celtic);
                PassAll(e);
                DrainCombat(e);
                e.NotifyPublic();
                var back = p.TryFindMonster(celtic, out _) && celtic.FaceUp &&
                           celtic.Position == BattlePosition.Defense && celtic.CurrentAtk == 1400 + 800;
                Check("Rope of Life: discard the entire hand; the battle-destroyed monster returns (chosen position) +800 ATK",
                    reached && offered && act && discarded && picked && back && p.Graveyard.Contains(rope),
                    $"reached={reached} offered={offered} act={act} discarded={discarded} only={onlyCeltic} " +
                    $"picked={picked} back={back} pos={celtic.Position} atk={celtic.CurrentAtk}");
            }

            {
                // Cancel at the target pick: the activation is undone and the discarded hand comes back.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var rope = PlaceSetTrap(e, p, RopeOfLife, 0);
                var h1 = PutInHand(e, p, PotOfGreed);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                for (var i = 0; i < 4 && e.IsAwaitingResponse &&
                                !(e.PendingResponse?.LegalCards?.Contains(rope) ?? false); i++)
                    e.PassResponse();
                var act = (e.PendingResponse?.LegalCards?.Contains(rope) ?? false) &&
                          e.TryActivateSpellTrap(p, rope, fromHand: false);
                var paid = act && p.Graveyard.Contains(h1) && e.IsAwaitingEffectTarget;
                var cancelled = paid && e.CancelEffectTargeting();
                var refunded = p.Hand.Contains(h1) && !p.Graveyard.Contains(h1) && p.TryFindSpellTrap(rope, out _) &&
                               !rope.FaceUp && p.Graveyard.Contains(celtic);
                PassAll(e);
                DrainCombat(e);
                Check("Rope of Life: Cancel at the target pick refunds the discarded hand and leaves it Set",
                    reached && act && paid && cancelled && refunded,
                    $"reached={reached} act={act} paid={paid} cancelled={cancelled} refunded={refunded}");
            }

            {
                // An empty hand: not offered. A monster destroyed by a card effect: Michizure is offered, Rope of Life not.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var rope = PlaceSetTrap(e, p, RopeOfLife, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                var emptyOffered = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    emptyOffered |= e.PendingResponse?.LegalCards?.Contains(rope) ?? false;
                    e.PassResponse();
                }
                DrainCombat(e);

                var e2 = OpponentBattle(db, pDeck, aDeck, out var reached2);
                var p2 = e2.Player;
                var celtic2 = PlaceMonster(e2, p2, Celtic, 0, BattlePosition.Attack, true);
                var rope2 = PlaceSetTrap(e2, p2, RopeOfLife, 0);
                var michi = PlaceSetTrap(e2, p2, Michizure, 1);
                PutInHand(e2, p2, PotOfGreed);
                PlaceMonster(e2, e2.Opponent, SummonedSkull, 0, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(e2, celtic2, PlaceSetTrap(e2, e2.Opponent, DarkHole, 0),
                    byPlayer: e2.Opponent);
                e2.NotifyPublic();
                var michiOffered = e2.PendingResponse?.LegalCards?.Contains(michi) ?? false;
                var ropeOffered = e2.PendingResponse?.LegalCards?.Contains(rope2) ?? false;
                PassAll(e2);
                Check("Rope of Life: not with an empty hand, nor for a monster destroyed by a card effect",
                    reached && reached2 && !emptyOffered && p.Graveyard.Contains(celtic) && michiOffered && !ropeOffered,
                    $"empty={emptyOffered} michi={michiOffered} rope={ropeOffered}");
            }

            {
                // The AI's Rope of Life (1 card in hand): the Player's Summoned Skull destroys its Celtic Guardian.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var rope = PlaceSetTrap(e, opp, RopeOfLife, 0);
                var h = PutInHand(e, opp, PotOfGreed);
                var skull = Attacker(e, p, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(p, skull, celtic);
                PassAll(e);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Rope of Life (AI): answers its battle-destroyed monster and Summons it with 800 more ATK",
                    reached && opp.TryFindMonster(celtic, out _) && celtic.CurrentAtk == 2200 && opp.Graveyard.Contains(h) &&
                    opp.Graveyard.Contains(rope) && !e.IsAwaitingResponse && !e.HasDeclaredAttack,
                    $"reached={reached} field={opp.TryFindMonster(celtic, out _)} atk={celtic.CurrentAtk} " +
                    $"hand={opp.Hand.Count} ropeGy={opp.Graveyard.Contains(rope)}");
            }
        }
    }
}
