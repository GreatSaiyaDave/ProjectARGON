using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "CoinDiceInterrupts" (v161 LOD-b): the coin-toss / die-roll result event
    /// (<see cref="CoinDiceInterruptsRuntime"/>) and the two cards that redo a result before it applies.
    /// <list type="bullet">
    /// <item>Second Coin Toss c36562627 (Continuous Spell): its controller's own tosses (Lua ep==tp), once per turn,
    /// all coins of the toss; a human taps the card or CANCELs, the AI redoes a bad result.</item>
    /// <item>Dice Re-Roll c83241722 (Normal Trap): one re-roll this turn of either player's die roll; the flag is set
    /// when it is used (declining keeps it).</item>
    /// </list>
    /// Coins and dice are queued (<see cref="DuelRng.QueueCoin"/> / <see cref="DuelRng.QueueDie"/>).
    /// </summary>
    public static class CoinDiceInterruptsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail);

        const int SecondCoinToss = 36562627;
        const int DiceReRoll = 83241722;
        const int FiendComedian = 81172176;
        const int FairyBox = 21598948;
        const int GracefulDice = 74137509;
        const int Offerings = 19230407;
        const int Vilepawn = 73219648; // die-roll protection, negates on 3
        const int Celtic = 91152256;
        const int MysticalElf = 15025844;
        const int GiantSoldier = 13039848;
        const int Koumori = 67724379;
        const int BattleOx = 5053103;
        const int PotOfGreed = 55144522;
        const int MirrorForce = 44095762;
        const int SummonedSkull = 70781052;
        const int SkullServant = 32274490;
        const int Bewd = 89631139;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("CoinDiceInterrupts: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            SecondCoinTossOnACalledToss(db, pDeck, aDeck, Check);
            SecondCoinTossInBattleOncePerTurn(db, pDeck, aDeck, Check);
            SecondCoinTossRedoesEveryCoin(db, pDeck, aDeck, Check);
            SecondCoinTossAi(db, pDeck, aDeck, Check);
            DiceReRollOnGracefulDice(db, pDeck, aDeck, Check);
            DiceReRollOnAnArchfiendRoll(db, pDeck, aDeck, Check);
            DiceReRollAi(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>The open choice is the redo offer of <paramref name="redoCard"/> to <paramref name="who"/>.</summary>
        static bool Offered(DuelEngine e, DuelistState who, CardInstance redoCard)
        {
            var p = e.PendingActivation;
            return p != null && ReferenceEquals(p.Card, redoCard) && ReferenceEquals(p.Controller, who) &&
                   p.LegalTargets.Count == 1 && ReferenceEquals(p.LegalTargets[0], redoCard);
        }

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.Card?.Name ?? "none"} coinCall={e.PendingActivation?.AwaitingCoinCall} " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(e);
            ClearBoard(e);
            e.ClearPendingResponse();
            e.Player.Hand.Clear();
            e.Opponent.Hand.Clear();
            return e;
        }

        static DuelEngine PlayerBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = false;
            for (var t = 0; t < 12; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3)
                {
                    main = true;
                    break;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            ClearBoard(e);
            e.ClearPendingResponse();
            e.Player.Hand.Clear();
            e.Opponent.Hand.Clear();
            reached = main && e.TryEnterBattlePhase(e.Player) && e.Phase == DuelPhase.Battle;
            if (e.IsAwaitingResponse) e.PassResponse();
            return e;
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            return m;
        }

        // ───────────────────────────── compile ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var sctDef = db.Get(SecondCoinToss);
            var sct = sctDef != null ? CardTextEffectCompiler.Compile(sctDef) : null;
            Check("Second Coin Toss: one continuous redo marker, Implemented",
                sct != null && sct.FullyCompiled && sct.ClauseList.Count == 1 &&
                sct.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                sct.ClauseList[0].Action == EffectActionKind.RedoOwnCoinTossOncePerTurn &&
                CardEffectStatus.Classify(sctDef) == CardEffectStatusKind.Implemented,
                sct == null ? "null" : string.Join(";", sct.ClauseList.Select(c => $"{c.Timing}/{c.Action}")));

            var drrDef = db.Get(DiceReRoll);
            var drr = drrDef != null ? CardTextEffectCompiler.Compile(drrDef) : null;
            Check("Dice Re-Roll: one Activate clause granting the re-roll (a Chain Link, no target), Implemented",
                drr != null && drr.FullyCompiled && drr.ClauseList.Count == 1 &&
                drr.ClauseList[0].Timing == EffectTiming.Activate &&
                drr.ClauseList[0].Action == EffectActionKind.GrantDiceReRollThisTurn &&
                drr.ClauseList[0].MakesChainLink && !drr.ClauseList[0].RequiresTargetChoice &&
                CardEffectStatus.Classify(drrDef) == CardEffectStatusKind.Implemented,
                drr == null ? "null" : string.Join(";", drr.ClauseList.Select(c => $"{c.Timing}/{c.Action}")));

            Check("Vocabulary: both redo actions resolve as Randomize",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.RedoOwnCoinTossOncePerTurn }) ==
                EffectResolutionKind.Randomize &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.GrantDiceReRollThisTurn }) ==
                EffectResolutionKind.Randomize, "");
        }

        static CompiledCardProgram Synthetic(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990161, name = "CoinDice probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : "spell",
                race = race, desc = text
            });

        static void NearMisses(Chk Check)
        {
            const string sct = "When a coin toss is performed, you can choose to redo the coin toss. (If the toss involved " +
                               "multiple coin flips, redo all of them.) You can only use the effect of \"Second Coin " +
                               "Toss\" once per turn.";
            const string drr = "One time, during the rest of this turn, if either player(s) rolls a die (or dice), you " +
                               "can have the player(s) re-roll. (You can only gain this effect once per turn.)";
            (string text, string type, string race, EffectActionKind action, string why)[] misses =
            {
                (sct.Replace(" You can only use the effect of \"Second Coin Toss\" once per turn.", ""), "Spell Card",
                    "Continuous", EffectActionKind.RedoOwnCoinTossOncePerTurn, "Second Coin Toss without its once per turn"),
                (sct.Replace("When a coin toss is performed", "When your opponent performs a coin toss"), "Spell Card",
                    "Continuous", EffectActionKind.RedoOwnCoinTossOncePerTurn, "Second Coin Toss for the opponent's tosses"),
                (sct + " Then draw 1 card.", "Spell Card", "Continuous", EffectActionKind.RedoOwnCoinTossOncePerTurn,
                    "Second Coin Toss with an extra sentence"),
                (drr.Replace("either player(s) rolls a die (or dice), you can have the player(s) re-roll",
                        "your opponent rolls a die, you can have them re-roll"), "Trap Card", "Normal",
                    EffectActionKind.GrantDiceReRollThisTurn, "Dice Re-Roll for the opponent's rolls"),
                (drr.Replace(" (You can only gain this effect once per turn.)", ""), "Trap Card", "Normal",
                    EffectActionKind.GrantDiceReRollThisTurn, "Dice Re-Roll without its once per turn"),
                (drr.Replace("One time, during the rest of this turn", "Twice, during the rest of this turn"), "Trap Card",
                    "Normal", EffectActionKind.GrantDiceReRollThisTurn, "Dice Re-Roll twice"),
            };
            foreach (var (text, type, race, action, why) in misses)
            {
                var p = Synthetic(type, race, text);
                var used = p != null && p.ClauseList.Exists(c => c != null && c.Action == action);
                Check($"near miss stays closed: {why}", p != null && !p.FullyCompiled && !used,
                    p == null ? "null" : $"full={p.FullyCompiled} clauses={p.ClauseList.Count}");
            }
        }

        // ───────────────────────────── Second Coin Toss ─────────────────────────────

        /// <summary>The Player's Set Fiend Comedian (3 cards in the opponent's GY, 5 in the Player's Deck).</summary>
        static DuelEngine ComedianBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool sctFaceUp,
            out CardInstance fc, out CardInstance sct)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            p.Deck.Clear();
            p.Deck.AddRange(new[] { Celtic, MysticalElf, GiantSoldier, Koumori, BattleOx });
            foreach (var id in new[] { Celtic, PotOfGreed, MirrorForce })
                e.Opponent.Graveyard.Add(e.CreateCardInstance(id));
            fc = PlaceSetTrap(e, p, FiendComedian, 0);
            sct = PlaceSetTrap(e, p, SecondCoinToss, 1);
            sct.FaceUp = sctFaceUp;
            return e;
        }

        static void SecondCoinTossOnACalledToss(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Called Heads, the toss is Tails: before the call is judged, the Player may toss again; Heads → right.
                var e = ComedianBoard(db, pDeck, aDeck, true, out var fc, out var sct);
                var p = e.Player;
                var o = e.Opponent;
                e.Rng.QueueCoin(false);
                e.Rng.QueueCoin(true);
                var act = e.TryActivateSpellTrap(p, fc, fromHand: false);
                PassAll(e);
                var called = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall && e.TrySelectCoinCall(true);
                var offered = Offered(e, p, sct) && e.PendingActivation.HoldsChainResolution;
                var notYet = o.Graveyard.Count == 3 && p.Deck.Count == 5;
                var tapped = offered && e.TrySelectEffectTarget(sct);
                PassAll(e);
                Check("Second Coin Toss: a called toss shows Tails — the Player is offered the redo before it applies",
                    act && called && offered && notYet, $"act={act} called={called} offered={offered} notYet={notYet} {State(e)}");
                Check("Second Coin Toss: tapping it tosses again (Heads) — the call is right, the opponent's GY is banished",
                    tapped && o.Graveyard.Count == 0 && o.Banished.Count == 3 && p.Deck.Count == 5 &&
                    p.Graveyard.Contains(fc) && p.TryFindSpellTrap(sct, out _) && e.PendingActivation == null &&
                    !e.Chain.HasLinks,
                    $"tapped={tapped} oGy={o.Graveyard.Count} oBan={o.Banished.Count} deck={p.Deck.Count} {State(e)}");
            }

            {
                // CANCEL keeps Tails: the call is wrong (3 cards from the Deck to the GY).
                var e = ComedianBoard(db, pDeck, aDeck, true, out var fc, out var sct);
                var p = e.Player;
                e.Rng.QueueCoin(false);
                e.TryActivateSpellTrap(p, fc, fromHand: false);
                PassAll(e);
                if (e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall) e.TrySelectCoinCall(true);
                var offered = Offered(e, p, sct);
                e.CancelEffectTargeting();
                PassAll(e);
                Check("Second Coin Toss: CANCEL keeps the result (Tails) — the wrong call sends 3 cards from the Deck",
                    offered && p.Deck.Count == 2 && e.Opponent.Graveyard.Count == 3 && e.PendingActivation == null &&
                    !e.Chain.HasLinks, $"offered={offered} deck={p.Deck.Count} {State(e)}");
            }

            {
                // A Set (face-down) Second Coin Toss does nothing.
                var e = ComedianBoard(db, pDeck, aDeck, false, out var fc, out var sct);
                var p = e.Player;
                e.Rng.QueueCoin(false);
                e.TryActivateSpellTrap(p, fc, fromHand: false);
                PassAll(e);
                if (e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall) e.TrySelectCoinCall(true);
                Check("Second Coin Toss: a Set copy offers no redo — the wrong call applies at once",
                    e.PendingActivation == null && p.Deck.Count == 2 && !e.Chain.HasLinks, State(e));
            }
        }

        static void SecondCoinTossInBattleOncePerTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The Player's face-up Fairy Box and Second Coin Toss; the opponent's Summoned Skull, then Skull Servant,
            // attack Celtic Guardian. The first toss is redone (Tails → Heads: the Skull's ATK becomes 0 and the attack
            // goes on); the second toss of the turn is not offered (once per turn).
            var e = OpponentBattle(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            FaceUpSpellTrap(e, p, FairyBox, 0);
            var sct = FaceUpSpellTrap(e, p, SecondCoinToss, 1);
            var skull = Attacker(e, opp, SummonedSkull, 0);
            var servant = Attacker(e, opp, SkullServant, 1);
            e.NotifyPublic();
            var oppLp = opp.LifePoints;
            e.Rng.QueueCoin(false);
            e.Rng.QueueCoin(true);
            var declared = e.TryAttack(opp, skull, celtic);
            var called = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall && e.TrySelectCoinCall(true);
            var offered = Offered(e, p, sct);
            var waits = skull.CurrentAtk == 2500;
            var tapped = offered && e.TrySelectEffectTarget(sct);
            DrainCombat(e);
            Check("Second Coin Toss: in battle, the redo turns a wrong Fairy Box call right; the attack goes on after it",
                reached && declared && called && offered && waits && tapped && opp.Graveyard.Contains(skull) &&
                p.TryFindMonster(celtic, out _) && opp.LifePoints == oppLp - 1400,
                $"reached={reached} declared={declared} called={called} offered={offered} waits={waits} " +
                $"skullGy={opp.Graveyard.Contains(skull)} oppLp={oppLp}->{opp.LifePoints} {State(e)}");

            var lpBefore = opp.LifePoints;
            e.Rng.QueueCoin(false);
            e.TryAttack(opp, servant, celtic);
            var calledAgain = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall && e.TrySelectCoinCall(true);
            var offeredAgain = e.PendingActivation != null && ReferenceEquals(e.PendingActivation.Card, sct);
            DrainCombat(e);
            Check("Second Coin Toss: once per turn — the second toss this turn is not offered (the wrong call stands)",
                calledAgain && !offeredAgain && servant.CurrentAtk == 300 && opp.Graveyard.Contains(servant) &&
                opp.LifePoints == lpBefore - 1100,
                $"calledAgain={calledAgain} offeredAgain={offeredAgain} atk={servant.CurrentAtk} lp={lpBefore}->{opp.LifePoints}");
        }

        static void SecondCoinTossRedoesEveryCoin(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The Player's Barrel Dragon targets the opponent's Celtic Guardian and tosses 3 coins: Tails ×3. The redo
            // tosses all 3 again (Tails, Heads, Heads): only a redo of every coin reaches 2 Heads and destroys it.
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            const int barrelDragon = 81480460;
            var dragon = PlaceMonster(e, p, barrelDragon, 0, BattlePosition.Attack, true);
            var sct = FaceUpSpellTrap(e, p, SecondCoinToss, 0);
            var prey = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            foreach (var h in new[] { false, false, false, false, true, true })
                e.Rng.QueueCoin(h);
            var act = e.TryActivateSpellTrap(p, dragon, fromHand: false);
            if (e.PendingActivation != null && !ReferenceEquals(e.PendingActivation.Card, sct) &&
                e.PendingActivation.LegalTargets.Contains(prey))
                e.TrySelectEffectTarget(prey);
            PassAll(e);
            var offered = Offered(e, p, sct);
            var waits = o.TryFindMonster(prey, out _);
            var tapped = offered && e.TrySelectEffectTarget(sct);
            PassAll(e);
            Check("Second Coin Toss: a 3-coin toss is redone as a whole — 2 Heads, Barrel Dragon destroys its target",
                act && offered && waits && tapped && o.Graveyard.Contains(prey) && e.PendingActivation == null &&
                !e.Chain.HasLinks,
                $"act={act} offered={offered} waits={waits} tapped={tapped} preyGy={o.Graveyard.Contains(prey)} {State(e)}");
        }

        static void SecondCoinTossAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var first in new[] { false, true })
            {
                // The AI's Fairy Box and Second Coin Toss; the Player's Blue-Eyes attacks. The AI calls Heads; Tails is
                // bad for it, so it tosses again (Heads); Heads it keeps.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = Attacker(e, p, Bewd, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                FaceUpSpellTrap(e, opp, FairyBox, 0);
                FaceUpSpellTrap(e, opp, SecondCoinToss, 1);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.Rng.QueueCoin(first);
                if (!first) e.Rng.QueueCoin(true);
                e.Rng.QueueCoin(false); // must stay unused: a redo of Heads would draw it
                var declared = e.TryAttack(p, bewd, celtic);
                var noPrompt = !e.IsAwaitingEffectTarget;
                DrainCombat(e);
                Check($"Second Coin Toss: the AI {(first ? "keeps Heads" : "tosses Tails again (Heads)")} — Blue-Eyes at 0 ATK is destroyed",
                    reached && declared && noPrompt && p.Graveyard.Contains(bewd) && opp.TryFindMonster(celtic, out _) &&
                    p.LifePoints == pLp - 1400 && e.Rng.TossCoin() == false,
                    $"reached={reached} declared={declared} noPrompt={noPrompt} pLp={pLp}->{p.LifePoints} {State(e)}");
            }

            {
                // Only its controller's own tosses: the Player's Second Coin Toss does not redo the AI's Fairy Box toss.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = Attacker(e, p, Bewd, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                FaceUpSpellTrap(e, opp, FairyBox, 0);
                FaceUpSpellTrap(e, p, SecondCoinToss, 1);
                e.NotifyPublic();
                e.Rng.QueueCoin(false);
                var declared = e.TryAttack(p, bewd, celtic);
                var noPrompt = !e.IsAwaitingEffectTarget;
                DrainCombat(e);
                Check("Second Coin Toss: the opponent's toss is not redone — the AI's wrong call stands",
                    reached && declared && noPrompt && opp.Graveyard.Contains(celtic) && p.TryFindMonster(bewd, out _),
                    $"declared={declared} noPrompt={noPrompt} {State(e)}");
            }
        }

        // ───────────────────────────── Dice Re-Roll ─────────────────────────────

        /// <summary>The Player's Main Phase: Celtic Guardian face-up, a Set Dice Re-Roll activated and resolved.</summary>
        static DuelEngine DiceBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out CardInstance drr,
            out CardInstance celtic, out bool granted)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            drr = PlaceSetTrap(e, p, DiceReRoll, 0);
            var act = e.TryActivateSpellTrap(p, drr, fromHand: false);
            PassAll(e);
            granted = act && CoinDiceInterruptsRuntime.HasDiceReRoll(e, p) && p.Graveyard.Contains(drr) &&
                      !e.Chain.HasLinks && e.PendingActivation == null;
            return e;
        }

        static void DiceReRollOnGracefulDice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = DiceBoard(db, pDeck, aDeck, out var drr, out var celtic, out var granted);
                var p = e.Player;
                var gd = PutInHand(e, p, GracefulDice);
                var gd2 = PutInHand(e, p, GracefulDice);
                Check("Dice Re-Roll: activated and resolved, it gives its controller one re-roll this turn",
                    granted && !CoinDiceInterruptsRuntime.HasDiceReRoll(e, e.Opponent), State(e));

                e.Rng.QueueDie(1);
                e.Rng.QueueDie(6);
                var act = e.TryActivateSpellTrap(p, gd, fromHand: true);
                PassAll(e);
                var offered = Offered(e, p, drr) && e.PendingActivation.HoldsChainResolution;
                var waits = celtic.CurrentAtk == 1400;
                var tapped = offered && e.TrySelectEffectTarget(drr);
                PassAll(e);
                Check("Dice Re-Roll: Graceful Dice rolls a 1 — the Player may re-roll before it applies (the Chain waits)",
                    act && offered && waits, $"act={act} offered={offered} atk={celtic.CurrentAtk} {State(e)}");
                Check("Dice Re-Roll: the re-roll (6) replaces the 1 — Celtic Guardian gains 600; the Chain finishes",
                    tapped && celtic.CurrentAtk == 2000 && celtic.CurrentDef == 1800 && p.Graveyard.Contains(gd) &&
                    !e.Chain.HasLinks && e.PendingActivation == null && !CoinDiceInterruptsRuntime.HasDiceReRoll(e, p),
                    $"tapped={tapped} atk={celtic.CurrentAtk} def={celtic.CurrentDef} {State(e)}");

                e.Rng.QueueDie(2);
                e.TryActivateSpellTrap(p, gd2, fromHand: true);
                PassAll(e);
                Check("Dice Re-Roll: one time only — the next roll this turn applies at once (+200)",
                    e.PendingActivation == null && celtic.CurrentAtk == 2200 && !e.Chain.HasLinks,
                    $"atk={celtic.CurrentAtk} {State(e)}");
            }

            {
                // CANCEL keeps the 1; declining does not use the effect (the next roll is offered again).
                var e = DiceBoard(db, pDeck, aDeck, out var drr, out var celtic, out _);
                var p = e.Player;
                var gd = PutInHand(e, p, GracefulDice);
                var gd2 = PutInHand(e, p, GracefulDice);
                e.Rng.QueueDie(1);
                e.TryActivateSpellTrap(p, gd, fromHand: true);
                PassAll(e);
                var offered = Offered(e, p, drr);
                e.CancelEffectTargeting();
                PassAll(e);
                var kept = celtic.CurrentAtk == 1500 && !e.Chain.HasLinks && e.PendingActivation == null;
                e.Rng.QueueDie(3);
                e.TryActivateSpellTrap(p, gd2, fromHand: true);
                PassAll(e);
                var offeredAgain = Offered(e, p, drr);
                e.CancelEffectTargeting();
                PassAll(e);
                Check("Dice Re-Roll: CANCEL keeps the roll; declined, the effect is still there for the next roll",
                    offered && kept && offeredAgain && celtic.CurrentAtk == 1800 && CoinDiceInterruptsRuntime.HasDiceReRoll(e, p),
                    $"offered={offered} kept={kept} again={offeredAgain} atk={celtic.CurrentAtk} {State(e)}");
            }
        }

        static void DiceReRollOnAnArchfiendRoll(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var redo in new[] { true, false })
            {
                // The Player's Offerings to the Doomed targets the AI's Vilepawn Archfiend, which rolls a 3 (negate and
                // destroy Offerings). The Player's Dice Re-Roll: re-roll (4) → Offerings destroys Vilepawn; CANCEL → the
                // roll stands.
                var e = DiceBoard(db, pDeck, aDeck, out var drr, out var celtic, out var granted);
                var p = e.Player;
                var o = e.Opponent;
                p.MonsterZones[0].Occupant = null; // Vilepawn is the only face-up monster (a lone target)
                var vilepawn = PlaceMonster(e, o, Vilepawn, 0, BattlePosition.Attack, true);
                var off = PutInHand(e, p, Offerings);
                e.Rng.QueueDie(3);
                e.Rng.QueueDie(4);
                var act = e.TryActivateSpellTrap(p, off, fromHand: true);
                if (e.PendingActivation != null && !ReferenceEquals(e.PendingActivation.Card, drr) &&
                    e.PendingActivation.LegalTargets.Contains(vilepawn))
                    e.TrySelectEffectTarget(vilepawn);
                PassAll(e);
                var offered = Offered(e, p, drr);
                var waits = o.TryFindMonster(vilepawn, out _);
                if (redo) e.TrySelectEffectTarget(drr);
                else e.CancelEffectTargeting();
                PassAll(e);
                var ok = redo
                    ? o.Graveyard.Contains(vilepawn) && p.Graveyard.Contains(off)
                    : o.TryFindMonster(vilepawn, out _) && p.Graveyard.Contains(off);
                Check(redo
                        ? "Dice Re-Roll: an Archfiend's negating roll (3) is re-rolled (4) — the targeting Spell destroys it"
                        : "Dice Re-Roll: CANCEL keeps the Archfiend's 3 — the Spell is negated and destroyed",
                    granted && act && offered && waits && ok && !e.Chain.HasLinks && e.PendingActivation == null,
                    $"granted={granted} act={act} offered={offered} waits={waits} vile={(o.Graveyard.Contains(vilepawn) ? "gy" : "field")} " +
                    $"off={(p.Graveyard.Contains(off) ? "gy" : "?")} {State(e)}");
            }
        }

        static void DiceReRollAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (first, why) in new[] { (2, "rolls a 2 and re-rolls it (5)"), (5, "rolls a 5 and keeps it") })
            {
                // The AI's own turn: it activates its Set Dice Re-Roll, then Graceful Dice; it re-rolls only a bad roll.
                var e = Fresh(db, pDeck, aDeck);
                var main = ReachOpponentMain(e);
                ClearBoard(e);
                e.ClearPendingResponse();
                var o = e.Opponent;
                o.Hand.Clear();
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var drr = PlaceSetTrap(e, o, DiceReRoll, 0);
                var act = e.TryActivateSpellTrap(o, drr, fromHand: false);
                PassAll(e);
                var granted = CoinDiceInterruptsRuntime.HasDiceReRoll(e, o);
                var gd = PutInHand(e, o, GracefulDice);
                e.Rng.QueueDie(first);
                e.Rng.QueueDie(first == 2 ? 5 : 1);
                var rolled = e.TryActivateSpellTrap(o, gd, fromHand: true);
                PassAll(e);
                Check($"Dice Re-Roll (AI): on its own Graceful Dice the AI {why} — +500",
                    main && act && granted && rolled && e.PendingActivation == null && celtic.CurrentAtk == 1900 &&
                    !e.Chain.HasLinks && CoinDiceInterruptsRuntime.HasDiceReRoll(e, o) == (first != 2),
                    $"main={main} act={act} granted={granted} rolled={rolled} atk={celtic.CurrentAtk} {State(e)}");
            }

            foreach (var (first, expectAtk, why) in new[]
                     {
                         (6, 1500, "the Player rolls a 6 — the AI has it re-rolled (1)"),
                         (2, 1600, "the Player rolls a 2 — the AI keeps it")
                     })
            {
                // Either player's roll: the AI holds a Dice Re-Roll this turn (its effect, given directly) and uses it on
                // the Player's Graceful Dice only when the roll is good for the Player.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var drrCard = e.CreateCardInstance(DiceReRoll);
                o.Graveyard.Add(drrCard);
                CoinDiceInterruptsRuntime.GrantDiceReRoll(e, o, drrCard);
                var gd = PutInHand(e, p, GracefulDice);
                e.Rng.QueueDie(first);
                e.Rng.QueueDie(1);
                var act = e.TryActivateSpellTrap(p, gd, fromHand: true);
                PassAll(e);
                Check($"Dice Re-Roll (AI): {why}",
                    act && e.PendingActivation == null && celtic.CurrentAtk == expectAtk && !e.Chain.HasLinks &&
                    CoinDiceInterruptsRuntime.HasDiceReRoll(e, o) == (first != 6),
                    $"act={act} atk={celtic.CurrentAtk} {State(e)}");
            }
        }
    }
}
