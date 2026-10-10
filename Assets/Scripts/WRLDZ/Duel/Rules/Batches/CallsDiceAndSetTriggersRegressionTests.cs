using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CallsDiceAndSetTriggers batch (v163): Statue of the Wicked, Dark Coffin, Cobra Jar, Dice Jar, Ordeal of a Traveler,
    /// Reversal Quiz and Reasoning. Compile shapes, mini-duels through the real engine entry points (TryActivateSpellTrap
    /// with MST / Heavy Storm, TryFlipSummon, TryAttack, TrySelectEffectTarget / CancelEffectTargeting on the declaration
    /// tokens) with the human as caller and as card owner, AI vs AI, Dice Re-Roll on Dice Jar, and near misses.
    /// Runtime: <see cref="CallsDiceAndSetTriggersRuntime"/>.
    /// </summary>
    public static class CallsDiceAndSetTriggersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Statue = 65810489;
        const int DarkCoffin = 1804528;
        const int CobraJar = 86801871;
        const int DiceJar = 3549275;
        const int Ordeal = 39537362;
        const int ReversalQuiz = 5990062;
        const int Reasoning = 58577036;

        const int Mst = 5318639;
        const int HeavyStorm = 19613556;
        const int DiceReRoll = 83241722;
        const int PotOfGreed = 55144522;
        const int DarkHole = 53129443;
        const int TrapHole = 4206964;
        const int Celtic = 91152256;        // Lv4 1400/1200
        const int DarkBlade = 11321183;     // Lv4 1800/1500
        const int SummonedSkull = 70781052; // Lv6 2500/1200
        const int Gaia = 6368038;           // Lv7 2300/2100
        const int GiantSoldier = 13039848;  // Lv3 1300/2000

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("CallsDiceAndSetTriggers: " + name, ok, detail ?? "");
            Shapes(db, Check);
            NearMisses(db, Check);
            StatueDuels(db, pDeck, aDeck, Check);
            DarkCoffinDuels(db, pDeck, aDeck, Check);
            CobraJarDuels(db, pDeck, aDeck, Check);
            DiceJarDuels(db, pDeck, aDeck, Check);
            OrdealDuels(db, pDeck, aDeck, Check);
            ReversalQuizDuels(db, pDeck, aDeck, Check);
            ReasoningDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            Clean(engine);
            return engine;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            Clean(engine);
            engine.ClearPendingResponse();
            return engine;
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(engine);
            Clean(engine);
            engine.ClearPendingResponse();
            return engine;
        }

        static void Clean(DuelEngine engine)
        {
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static CardInstance FaceUpTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            return m;
        }

        static bool OnField(DuelistState who, CardInstance c) =>
            who.TryFindMonster(c, out _) || who.TryFindSpellTrap(c, out _);

        static CardInstance TokenNamed(DuelistState who, string name) =>
            who.MonstersOnField().FirstOrDefault(m => m != null && m.IsToken && m.Name == name);

        /// <summary>The open prompt's declaration token named <paramref name="name"/> (prefix match), or null.</summary>
        static CardInstance Choice(DuelEngine e, string name) =>
            e.PendingActivation?.LegalTargets.FirstOrDefault(t =>
                t?.Def?.name != null && t.Def.name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

        static string Names(DuelEngine e) =>
            e.PendingActivation == null ? "none" : string.Join(",", e.PendingActivation.LegalTargets.Select(t => t?.Name));

        static bool Pick(DuelEngine e, DuelistState who, string name)
        {
            var t = Choice(e, name);
            return t != null && ReferenceEquals(e.PendingActivation?.Controller, who) && e.TrySelectEffectTarget(t);
        }

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.Card?.Name ?? "none"} to={e.PendingActivation?.Controller?.Name} " +
            $"choices=[{Names(e)}] response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} " +
            $"attack={e.HasDeclaredAttack}";

        static EffectClause Only(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
        }

        static CardDef Variant(CardDatabase db, int id, string desc)
        {
            var d = db.Get(id);
            return new CardDef
            {
                id = 990000000 + (id % 1000000),
                name = d.name + " (near miss)",
                type = d.type,
                frameType = d.frameType,
                race = d.race,
                attribute = d.attribute,
                level = d.level,
                atk = d.atk,
                def = d.def,
                archetype = d.archetype,
                desc = desc
            };
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            {
                var c = Only(db, Statue, out var prog);
                Check("Statue of the Wicked: one mandatory 'destroyed while face-down' GY trigger Summoning 1 Wicked " +
                      "Token (Fiend/DARK/4/1000/1000), no Activate clause, Implemented",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.SentFromFieldToGy &&
                    c.Action == EffectActionKind.SpecialSummonToken && c.RequiresDestroyedWhileFaceDown &&
                    !c.IsOptional && c.TokenName == "Wicked Token" && c.TokenRace == "Fiend" &&
                    c.TokenAttribute == "DARK" && c.TokenLevel == 4 && c.TokenAtk == 1000 && c.TokenDef == 1000 &&
                    c.TokenCount == 1 && !c.TokenToOpponent && !prog.HasTiming(EffectTiming.Activate) &&
                    CardEffectStatus.Classify(db.Get(Statue)) == CardEffectStatusKind.Implemented,
                    $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} action={c?.Action}");
            }
            {
                var c = Only(db, DarkCoffin, out var prog);
                Check("Dark Coffin: one mandatory 'destroyed while face-down' GY trigger (the opponent's option), " +
                      "no Activate clause, Implemented",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.SentFromFieldToGy &&
                    c.Action == EffectActionKind.OpponentDiscardsRandomOrDestroysOwnMonster &&
                    c.RequiresDestroyedWhileFaceDown && !c.IsOptional && !prog.HasTiming(EffectTiming.Activate) &&
                    CardEffectStatus.Classify(db.Get(DarkCoffin)) == CardEffectStatusKind.Implemented,
                    $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} action={c?.Action}");
            }
            {
                var c = Only(db, CobraJar, out var prog);
                Check("Cobra Jar: FLIP Token (Reptile/EARTH/3/1200/1200) whose battle destruction burns the opponent 500",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.Flip &&
                    c.Action == EffectActionKind.SpecialSummonToken && c.TokenName == "Poisonous Snake Token" &&
                    c.TokenRace == "Reptile" && c.TokenAttribute == "EARTH" && c.TokenLevel == 3 &&
                    c.TokenAtk == 1200 && c.TokenDef == 1200 && c.TokenBattleDestroyedDamageToOpponent == 500 &&
                    c.TokenDestroyedDamage == 0,
                    $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} burn={c?.TokenBattleDestroyedDamageToOpponent}");
            }
            {
                var c = Only(db, DiceJar, out var prog);
                Check("Dice Jar: FLIP both-players die roll, roll x 500 or 6000",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.Flip &&
                    c.Action == EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage && c.Amount == 500 &&
                    c.MaxRollDamage == 6000 &&
                    EffectVocabulary.ResolutionOf(c) == EffectResolutionKind.Randomize,
                    $"full={prog.FullyCompiled} amount={c?.Amount} max={c?.MaxRollDamage}");
            }
            {
                var c = Only(db, Ordeal, out var prog);
                Check("Ordeal of a Traveler: an optional face-up attack-declaration trigger (placement-only activation)",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker && c.IsOptional &&
                    c.StaysOnField &&
                    CardEffectStatus.Classify(db.Get(Ordeal)) == CardEffectStatusKind.Implemented,
                    $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count}");
            }
            {
                var c = Only(db, ReversalQuiz, out var prog);
                Check("Reversal Quiz: send-all-others cost, call the top card, exchange LP",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.CallTopDeckCardTypeRightExchangesLp &&
                    c.CostSendAllOtherHandAndFieldCardsToGy &&
                    c.ActivationCondition == CallsDiceAndSetTriggersRuntime.OtherCardInHandOrOnField &&
                    EffectVocabulary.CostOf(c) == EffectCostKind.SendToGy,
                    $"full={prog.FullyCompiled} cost={c?.CostSendAllOtherHandAndFieldCardsToGy}");
            }
            {
                var c = Only(db, Reasoning, out var prog);
                Check("Reasoning: the opponent's Level declaration, then the excavation",
                    prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster &&
                    c.ActivationCondition == CallsDiceAndSetTriggersRuntime.CanExcavateSummonableMonster &&
                    !c.RequiresTargetChoice,
                    $"full={prog.FullyCompiled} cond={c?.ActivationCondition}");
            }
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var mine = new[]
            {
                EffectActionKind.OpponentDiscardsRandomOrDestroysOwnMonster,
                EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage,
                EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker,
                EffectActionKind.CallTopDeckCardTypeRightExchangesLp,
                EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster
            };
            foreach (var (id, desc, label) in new[]
                     {
                         (Statue, "When this card is destroyed and sent to the Graveyard, Special Summon 1 \"Wicked Token\" " +
                                  "(Fiend-Type/DARK/Level 4/ATK 1000/DEF 1000) on your side of the field.",
                             "Statue without 'face-down'"),
                         (DarkCoffin, "When this face-down card is destroyed and sent to the Graveyard, your opponent " +
                                      "selects and executes 1 of the following effects: ● Discard 1 card in their hand. " +
                                      "● Select 1 monster on their side of the field and destroy it.",
                             "Dark Coffin with a chosen (not random) discard"),
                         (CobraJar, "FLIP: Special Summon 1 \"Poisonous Snake Token\" (Reptile-Type/EARTH/3 Stars/ATK " +
                                    "1200/DEF 1200). When the \"Cobra Token\" is destroyed as a result of battle, inflict " +
                                    "500 points of damage to your opponent's Life Points.",
                             "Cobra Jar naming another Token"),
                         (DiceJar, "FLIP: Both players roll a six-sided die. The player with the lower result takes damage " +
                                   "equal to their opponent's roll x 500. However, if the winner rolled a 6, the loser " +
                                   "takes 6000 damage.",
                             "Dice Jar without the re-roll on a tie"),
                         (Ordeal, "When your monster declares an attack: Your opponent chooses 1 random card in your hand " +
                                  "and calls the type of the card (Monster, Spell, or Trap), and if they call it wrong, " +
                                  "return the attacking monster to the hand.",
                             "Ordeal on your own attack"),
                         (ReversalQuiz, "Call the type of card (Spell, Trap, or Monster) on top of your Deck. If you call " +
                                        "it right, exchange your current Life Points with your opponent's current ones.",
                             "Reversal Quiz without its cost"),
                         (Reasoning, "You declare a monster Level from 1 to 12. Excavate cards from the top of your Deck " +
                                     "until you excavate a monster that can be Normal Summoned/Set, then, if that monster " +
                                     "is the same Level as the one declared by your opponent, send all excavated cards " +
                                     "to the GY. If not, Special Summon the excavated monster, also send the remaining " +
                                     "cards to the GY.",
                             "Reasoning declared by its controller"),
                     })
            {
                var prog = CardTextEffectCompiler.Compile(Variant(db, id, desc));
                var hit = prog != null && prog.ClauseList.Exists(c => c != null &&
                    (mine.Contains(c.Action) || c.RequiresDestroyedWhileFaceDown ||
                     c.TokenBattleDestroyedDamageToOpponent > 0 || c.CostSendAllOtherHandAndFieldCardsToGy));
                Check($"near miss: {label} does not compile to this batch's clause and is not FullyCompiled",
                    prog != null && !hit && !prog.FullyCompiled,
                    $"full={prog?.FullyCompiled} actions={(prog == null ? "" : string.Join(",", prog.ClauseList.Select(c => c?.Action)))}");
            }
        }

        // ───────────────────────────── Statue of the Wicked ─────────────────────────────

        static void StatueDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Set Statue, destroyed by the Player's MST: the opponent (its controller) gets the Token.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var statue = PlaceSetTrap(e, o, Statue, 2);
                var cantActivate = !e.CanActivateSpellTrap(o, statue, fromHand: false);
                var mst = PutInHand(e, p, Mst);
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(statue))
                    e.TrySelectEffectTarget(statue);
                Settle(e);
                var tok = TokenNamed(o, "Wicked Token");
                Check("Statue of the Wicked: cannot be activated; Set and destroyed by the opponent's MST, its controller " +
                      "Special Summons 1 Wicked Token (1000/1000, face-up Attack)",
                    reached && cantActivate && act && o.Graveyard.Contains(statue) && tok != null && tok.FaceUp &&
                    tok.CurrentAtk == 1000 && tok.CurrentDef == 1000 && tok.Level == 4 && TokenNamed(p, "Wicked Token") == null,
                    $"cant={cantActivate} act={act} gy={o.Graveyard.Contains(statue)} tok={tok?.Name} {State(e)}");
            }
            {
                // The Player's own Heavy Storm destroys the Player's Set Statue (and the opponent's): both trigger.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceSetTrap(e, p, Statue, 0);
                var theirs = PlaceSetTrap(e, o, Statue, 0);
                var ownCantActivate = !e.CanActivateSpellTrap(p, mine, fromHand: false) &&
                                      !e.CanActivateSpellTrap(p, PutInHand(e, p, Statue), fromHand: true);
                p.Hand.Clear();
                var storm = PutInHand(e, p, HeavyStorm);
                var act = e.TryActivateSpellTrap(p, storm, fromHand: true);
                Settle(e);
                Check("Statue of the Wicked: its own controller's Heavy Storm destroys it while Set — the Token still comes " +
                      "(each player gets one)",
                    ownCantActivate && act && p.Graveyard.Contains(mine) && o.Graveyard.Contains(theirs) &&
                    TokenNamed(p, "Wicked Token") != null && TokenNamed(o, "Wicked Token") != null,
                    $"act={act} pTok={TokenNamed(p, "Wicked Token") != null} oTok={TokenNamed(o, "Wicked Token") != null} {State(e)}");
            }
            {
                // Face-up (not Set) when destroyed, or sent to the GY without being destroyed: no Token.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var faceUp = FaceUpTrap(e, o, Statue, 0);
                var sent = PlaceSetTrap(e, o, Statue, 1);
                var mst = PutInHand(e, p, Mst);
                e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(faceUp))
                    e.TrySelectEffectTarget(faceUp);
                Settle(e);
                var afterFaceUp = TokenNamed(o, "Wicked Token");
                e.SendCardToGrave(o, sent);
                Check("Statue of the Wicked: destroyed face-up, or sent (not destroyed) while Set — no Token",
                    o.Graveyard.Contains(faceUp) && o.Graveyard.Contains(sent) && afterFaceUp == null &&
                    TokenNamed(o, "Wicked Token") == null,
                    $"faceUpGy={o.Graveyard.Contains(faceUp)} sentGy={o.Graveyard.Contains(sent)} {State(e)}");
            }
        }

        // ───────────────────────────── Dark Coffin ─────────────────────────────

        static void DarkCoffinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Set Dark Coffin destroyed by the Player's MST: the Player (human) chooses — destroy, then
                // picks which of their 2 monsters.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var coffin = PlaceSetTrap(e, o, DarkCoffin, 1);
                var cantActivate = !e.CanActivateSpellTrap(o, coffin, fromHand: false);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var blade = PlaceMonster(e, p, DarkBlade, 1, BattlePosition.Attack, true);
                var keep = PutInHand(e, p, PotOfGreed);
                var mst = PutInHand(e, p, Mst);
                e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(coffin))
                    e.TrySelectEffectTarget(coffin);
                Settle(e);
                var options = Names(e);
                var chose = Pick(e, p, "Destroy");
                var monsterPick = e.PendingActivation != null && e.PendingActivation.Controller == p &&
                                  e.PendingActivation.LegalTargets.Contains(celtic) &&
                                  e.PendingActivation.LegalTargets.Contains(blade);
                var picked = monsterPick && e.TrySelectEffectTarget(blade);
                Settle(e);
                Check("Dark Coffin: cannot be activated; destroyed while Set, the human opponent chooses 'destroy' and " +
                      "selects 1 of their monsters — it is destroyed, the hand is kept",
                    reached && cantActivate && o.Graveyard.Contains(coffin) && options.Contains("Discard") && chose &&
                    picked && p.Graveyard.Contains(blade) && OnField(p, celtic) && p.Hand.Contains(keep) &&
                    e.PendingActivation == null,
                    $"cant={cantActivate} options={options} chose={chose} pick={monsterPick} {State(e)}");
            }
            {
                // The human chooses the random discard.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var coffin = PlaceSetTrap(e, o, DarkCoffin, 1);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var a = PutInHand(e, p, PotOfGreed);
                var b = PutInHand(e, p, Gaia);
                var mst = PutInHand(e, p, Mst);
                e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(coffin))
                    e.TrySelectEffectTarget(coffin);
                Settle(e);
                e.Rng.QueuePick(1);
                var chose = Pick(e, p, "Discard");
                Settle(e);
                Check("Dark Coffin: the human chooses 'discard' — 1 random card of their hand is discarded",
                    chose && p.Graveyard.Contains(b) && p.Hand.Contains(a) && OnField(p, celtic) &&
                    e.PendingActivation == null,
                    $"chose={chose} gy={string.Join(",", p.Graveyard.Select(c => c.Name))} {State(e)}");
            }
            {
                // The Player's own Heavy Storm destroys the Player's Set Dark Coffin: the AI opponent chooses at once (its
                // only monster is a strong one → it discards at random).
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var coffin = PlaceSetTrap(e, p, DarkCoffin, 0);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var x = PutInHand(e, o, Celtic);
                var y = PutInHand(e, o, PotOfGreed);
                var storm = PutInHand(e, p, HeavyStorm);
                e.Rng.QueuePick(0);
                var act = e.TryActivateSpellTrap(p, storm, fromHand: true);
                Settle(e);
                Check("Dark Coffin: destroyed by its own controller's Heavy Storm it still triggers; the AI opponent " +
                      "keeps its Summoned Skull and discards 1 random card",
                    act && p.Graveyard.Contains(coffin) && OnField(o, skull) && o.Graveyard.Contains(x) &&
                    o.Hand.Contains(y) && e.PendingActivation == null,
                    $"act={act} oGy={string.Join(",", o.Graveyard.Select(c => c.Name))} {State(e)}");
            }
            {
                // Only a monster to lose (empty hand): no choice — the AI's weakest monster is destroyed.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var coffin = PlaceSetTrap(e, p, DarkCoffin, 0);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, o, GiantSoldier, 1, BattlePosition.Defense, false);
                var storm = PutInHand(e, p, HeavyStorm);
                e.TryActivateSpellTrap(p, storm, fromHand: true);
                Settle(e);
                Check("Dark Coffin: with an empty hand the AI must destroy a monster — its least valuable one (Set too)",
                    p.Graveyard.Contains(coffin) && OnField(o, skull) && o.Graveyard.Contains(soldier),
                    $"skull={OnField(o, skull)} soldier={o.Graveyard.Contains(soldier)} {State(e)}");
            }
        }

        // ───────────────────────────── Cobra Jar ─────────────────────────────

        static void CobraJarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The Player Flip Summons Cobra Jar; the Token lasts after Cobra Jar is gone; the opponent's Dark Blade
                // destroys it by battle → the opponent takes 500.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jar = PlaceMonster(e, p, CobraJar, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, jar);
                Settle(e);
                var snake = TokenNamed(p, "Poisonous Snake Token");
                var stats = snake != null && snake.CurrentAtk == 1200 && snake.CurrentDef == 1200 && snake.Level == 3 &&
                            snake.FaceUp;
                e.SendCardToGrave(p, jar);
                var reachedBattle = ReachOpponentBattle(e);
                var blade = Attacker(e, o, DarkBlade, 0);
                var oLp = o.LifePoints;
                var pLp = p.LifePoints;
                var declared = snake != null && e.TryAttack(o, blade, snake);
                DrainCombat(e);
                Check("Cobra Jar: FLIP Summons the Poisonous Snake Token (1200/1200)",
                    reached && flipped && stats, $"flipped={flipped} snake={snake?.Name} {State(e)}");
                Check("Cobra Jar: the Token destroyed by battle (Cobra Jar already gone) burns the opponent of Cobra Jar's " +
                      "controller 500; the Token's controller takes the battle damage",
                    reachedBattle && declared && !OnField(p, snake) && o.LifePoints == oLp - 500 &&
                    p.LifePoints == pLp - 600,
                    $"battle={reachedBattle} declared={declared} o={oLp}->{o.LifePoints} p={pLp}->{p.LifePoints} {State(e)}");
            }
            {
                // Destroyed by a card effect (Dark Hole), not by battle: no damage.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var jar = PlaceMonster(e, p, CobraJar, 0, BattlePosition.Defense, false);
                e.TryFlipSummon(p, jar);
                Settle(e);
                var snake = TokenNamed(p, "Poisonous Snake Token");
                var oLp = o.LifePoints;
                var hole = PutInHand(e, p, DarkHole);
                e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                Check("Cobra Jar: the Token destroyed by a card effect inflicts no damage",
                    snake != null && !OnField(p, snake) && o.LifePoints == oLp,
                    $"snake={snake?.Name} o={oLp}->{o.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Dice Jar ─────────────────────────────

        static void DiceJarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (dice, pLoss, oLoss, label) in new[]
                     {
                         (new[] { 3, 5 }, 2500, 0, "the controller rolls 3, the opponent 5: the controller takes 5 x 500"),
                         (new[] { 2, 2, 6, 1 }, 0, 6000, "2 / 2 are rolled again; 6 / 1: the opponent takes 6000"),
                         (new[] { 4, 1 }, 0, 2000, "4 / 1: the opponent takes 4 x 500"),
                     })
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jar = PlaceMonster(e, p, DiceJar, 0, BattlePosition.Defense, false);
                foreach (var d in dice) e.Rng.QueueDie(d);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var flipped = e.TryFlipSummon(p, jar);
                Settle(e);
                Check("Dice Jar: " + label,
                    reached && flipped && p.LifePoints == pLp - pLoss && o.LifePoints == oLp - oLoss &&
                    e.PendingActivation == null,
                    $"flipped={flipped} p={pLp}->{p.LifePoints} o={oLp}->{o.LifePoints} {State(e)}");
            }

            {
                // The AI opponent holds Dice Re-Roll: the Player's Dice Jar rolls 5 / 2 (good for the Player) — the AI
                // has both dice re-rolled (1 / 4): the Player takes 2000.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var drr = e.CreateCardInstance(DiceReRoll);
                CoinDiceInterruptsRuntime.GrantDiceReRoll(e, o, drr);
                var jar = PlaceMonster(e, p, DiceJar, 0, BattlePosition.Defense, false);
                foreach (var d in new[] { 5, 2, 1, 4 }) e.Rng.QueueDie(d);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                e.TryFlipSummon(p, jar);
                Settle(e);
                Check("Dice Jar + Dice Re-Roll (AI): the opponent re-rolls both dice of the event; the new 1 / 4 applies",
                    p.LifePoints == pLp - 2000 && o.LifePoints == oLp && !CoinDiceInterruptsRuntime.HasDiceReRoll(e, o),
                    $"p={pLp}->{p.LifePoints} o={oLp}->{o.LifePoints} {State(e)}");
            }
            {
                // The human holds Dice Re-Roll: 1 / 6 would cost them 6000 — they tap Dice Re-Roll; 6 / 2 → the opponent
                // takes 6000.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var drr = e.CreateCardInstance(DiceReRoll);
                CoinDiceInterruptsRuntime.GrantDiceReRoll(e, p, drr);
                var jar = PlaceMonster(e, p, DiceJar, 0, BattlePosition.Defense, false);
                foreach (var d in new[] { 1, 6, 6, 2 }) e.Rng.QueueDie(d);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                e.TryFlipSummon(p, jar);
                Settle(e);
                var offered = e.PendingActivation != null && ReferenceEquals(e.PendingActivation.Card, drr) &&
                              e.PendingActivation.Controller == p && p.LifePoints == pLp;
                var tapped = offered && e.TrySelectEffectTarget(drr);
                Settle(e);
                Check("Dice Jar + Dice Re-Roll (human): the 1 / 6 waits for the redo choice; the re-roll 6 / 2 applies",
                    offered && tapped && p.LifePoints == pLp && o.LifePoints == oLp - 6000 && e.PendingActivation == null,
                    $"offered={offered} tapped={tapped} p={pLp}->{p.LifePoints} o={oLp}->{o.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Ordeal of a Traveler ─────────────────────────────

        static void OrdealDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (handId, bounced) in new[] { (TrapHole, true), (Celtic, false) })
            {
                // The Player (human) controls a face-up Ordeal; the AI's Summoned Skull attacks. The Player activates;
                // the AI calls Monster (the Player's Deck is all monsters); the random card is the hand's only card.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                for (var i = 0; i < 10; i++) p.Deck.Add(Celtic);
                FaceUpTrap(e, p, Ordeal, 0);
                var card = PutInHand(e, p, handId);
                var soldier = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                var declared = e.TryAttack(o, skull, soldier);
                var asked = Choice(e, "Activate") != null && Choice(e, "Do not") != null &&
                            e.PendingActivation.Controller == p;
                var activated = asked && Pick(e, p, "Activate");
                DrainCombat(e);
                Check($"Ordeal of a Traveler: the human activates; the AI calls Monster on a " +
                      (bounced ? "Trap — wrong, the attacker returns to the hand" : "Monster — right, the attack goes on"),
                    reached && declared && asked && activated && p.Hand.Contains(card) &&
                    (bounced
                        ? o.Hand.Contains(skull) && !OnField(o, skull) && OnField(p, soldier)
                        : OnField(o, skull) && p.Graveyard.Contains(soldier)) &&
                    e.PendingActivation == null && !e.HasDeclaredAttack,
                    $"declared={declared} asked={asked} act={activated} skullHand={o.Hand.Contains(skull)} " +
                    $"soldier={OnField(p, soldier)} {State(e)}");
            }
            {
                // The activation only places the Continuous Trap face-up (its controller's open state).
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var set = PlaceSetTrap(e, p, Ordeal, 0);
                PutInHand(e, p, PotOfGreed);
                var can = e.CanActivateSpellTrap(p, set, fromHand: false);
                var act = can && e.TryActivateSpellTrap(p, set, fromHand: false);
                Settle(e);
                Check("Ordeal of a Traveler: activated from the Set, it is placed face-up and stays; nothing else happens",
                    can && act && set.FaceUp && OnField(p, set) && p.Hand.Count == 1 && e.PendingActivation == null,
                    $"can={can} act={act} faceUp={set.FaceUp} {State(e)}");
            }
            {
                // CANCEL = do not activate: the attack goes on.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, Ordeal, 0);
                PutInHand(e, p, TrapHole);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.TryAttack(o, skull, null);
                var asked = Choice(e, "Activate") != null;
                if (asked) e.CancelEffectTargeting();
                DrainCombat(e);
                Check("Ordeal of a Traveler: CANCEL declines the optional trigger — the direct attack goes on",
                    asked && OnField(o, skull) && p.LifePoints == pLp - 2500 && e.PendingActivation == null,
                    $"asked={asked} p={pLp}->{p.LifePoints} {State(e)}");
            }
            {
                // The human attacks into the AI's face-up Ordeal: the AI activates; the human calls (Spell) on the AI's
                // only hand card, a Spell — right: the attack goes on.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, o, Ordeal, 0);
                PutInHand(e, o, PotOfGreed);
                var blade = Attacker(e, p, DarkBlade, 0);
                var inBattle = e.TryEnterBattlePhase(p);
                Settle(e);
                var oLp = o.LifePoints;
                var declared = e.TryAttack(p, blade, null);
                var names = Names(e);
                var called = Pick(e, p, "Spell");
                DrainCombat(e);
                Check("Ordeal of a Traveler (AI controller): the human attacker calls Spell — right, the attack goes on",
                    reached && inBattle && declared && names == "Monster,Spell,Trap" && called && OnField(p, blade) &&
                    o.LifePoints == oLp - 1800,
                    $"battle={inBattle} declared={declared} names={names} called={called} o={oLp}->{o.LifePoints} {State(e)}");
            }
            {
                // The same, wrong call (Trap): Dark Blade returns to the Player's hand, no damage.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, o, Ordeal, 0);
                PutInHand(e, o, PotOfGreed);
                var blade = Attacker(e, p, DarkBlade, 0);
                e.TryEnterBattlePhase(p);
                Settle(e);
                var oLp = o.LifePoints;
                e.TryAttack(p, blade, null);
                var called = Pick(e, p, "Trap");
                DrainCombat(e);
                Check("Ordeal of a Traveler (AI controller): a wrong call returns the human's attacker to the hand",
                    called && p.Hand.Contains(blade) && o.LifePoints == oLp && !e.HasDeclaredAttack,
                    $"called={called} hand={p.Hand.Contains(blade)} o={oLp}->{o.LifePoints} {State(e)}");
            }
            {
                // No card in hand: no trigger. The Ordeal's controller's own attack: no trigger.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, Ordeal, 0);
                FaceUpTrap(e, o, Ordeal, 0);
                PutInHand(e, o, PotOfGreed);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.TryAttack(o, skull, null);
                var prompted = e.PendingActivation != null;
                DrainCombat(e);
                Check("Ordeal of a Traveler: no card in its controller's hand — no trigger; the attacker's own Ordeal " +
                      "does not trigger either",
                    !prompted && p.LifePoints == pLp - 2500 && OnField(o, skull),
                    $"prompted={prompted} p={pLp}->{p.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Reversal Quiz ─────────────────────────────

        static void ReversalQuizDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (call, right) in new[] { ("Monster", true), ("Trap", false) })
            {
                // The human: hand Gaia + Quiz, field Celtic + Set Trap Hole; the cost sends 3 cards; the top card is
                // Celtic Guardian.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var gaia = PutInHand(e, p, Gaia);
                var quiz = PutInHand(e, p, ReversalQuiz);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var hole = PlaceSetTrap(e, p, TrapHole, 0);
                p.Deck.Insert(0, Celtic);
                var deckCount = p.Deck.Count;
                p.LifePoints = 1000;
                o.LifePoints = 7000;
                var act = e.TryActivateSpellTrap(p, quiz, fromHand: true);
                var paid = p.Graveyard.Contains(gaia) && p.Graveyard.Contains(celtic) && p.Graveyard.Contains(hole) &&
                           p.Hand.Count == 0;
                Settle(e);
                var names = Names(e);
                var called = Pick(e, p, call);
                Settle(e);
                Check($"Reversal Quiz: the cost sends every other card in hand and field; the human calls {call} on " +
                      "a Monster — " + (right ? "right: the LP are exchanged (1000 ↔ 7000)" : "wrong: the LP stay") +
                      "; the Deck is shuffled",
                    reached && act && paid && names == "Monster,Spell,Trap" && called && p.Graveyard.Contains(quiz) &&
                    p.LifePoints == (right ? 7000 : 1000) && o.LifePoints == (right ? 1000 : 7000) &&
                    p.Deck.Count == deckCount && e.PendingActivation == null,
                    $"act={act} paid={paid} names={names} called={called} p={p.LifePoints} o={o.LifePoints} {State(e)}");
            }
            {
                // Nothing else in hand or on the field: cannot be activated.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var quiz = PutInHand(e, p, ReversalQuiz);
                Check("Reversal Quiz: with no other card in hand or on the field it cannot be activated",
                    !e.CanActivateSpellTrap(p, quiz, fromHand: true));
            }
            {
                // AI vs AI: both duelists AI-controlled; the AI's Quiz calls the type most common in its own Deck.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                p.IsPlayer = false;
                try
                {
                    var quiz = PutInHand(e, p, ReversalQuiz);
                    PutInHand(e, p, PotOfGreed);
                    p.Deck.Clear();
                    for (var i = 0; i < 6; i++) p.Deck.Add(Celtic);
                    p.LifePoints = 500;
                    o.LifePoints = 6000;
                    var wants = CallsDiceAndSetTriggersRuntime.AiWantsToActivateFromHand(e, p, quiz);
                    var act = e.TryActivateSpellTrap(p, quiz, fromHand: true);
                    Settle(e);
                    Check("Reversal Quiz (AI vs AI): the AI wants it while far behind, calls Monster on its all-monster " +
                          "Deck and exchanges the LP with no prompt",
                        wants && act && p.LifePoints == 6000 && o.LifePoints == 500 && e.PendingActivation == null,
                        $"wants={wants} act={act} p={p.LifePoints} o={o.LifePoints} {State(e)}");
                }
                finally
                {
                    p.IsPlayer = true;
                }
            }
        }

        // ───────────────────────────── Reasoning ─────────────────────────────

        static void ReasoningDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human activates; the AI opponent declares Level 4 (no public monster of the Player); Deck: Pot, Dark
                // Hole, Gaia (Lv7) … → Gaia is Special Summoned (the human picks Defense), the 2 Spells go to the GY.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var r = PutInHand(e, p, Reasoning);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { PotOfGreed, DarkHole, Gaia, Celtic, Celtic });
                var act = e.TryActivateSpellTrap(p, r, fromHand: true);
                Settle(e);
                var positions = Names(e);
                var picked = Pick(e, p, "Face-up Defense");
                Settle(e);
                var gaia = p.MonstersOnField().FirstOrDefault(m => m.CardId == Gaia);
                Check("Reasoning: the AI declares 4; Gaia (Level 7) is excavated and Special Summoned in the position " +
                      "the human picks; the Spells above it go to the GY; the rest of the Deck stays",
                    reached && act && picked && gaia != null && gaia.FaceUp && gaia.Position == BattlePosition.Defense &&
                    p.Graveyard.Count(c => c.CardId == PotOfGreed || c.CardId == DarkHole) == 2 &&
                    p.Graveyard.Contains(r) && p.Deck.Count == 2 && p.Deck.All(id => id == Celtic) &&
                    e.PendingActivation == null,
                    $"act={act} positions={positions} picked={picked} gaia={gaia?.Position} deck={p.Deck.Count} " +
                    $"gy={string.Join(",", p.Graveyard.Select(c => c.Name))} {State(e)}");
            }
            {
                // The AI activates; the human declares Level 7 — the excavated Gaia matches: every excavated card to the GY.
                var e = OpponentMain(db, pDeck, aDeck, out var reachedO);
                var p = e.Player;
                var o = e.Opponent;
                var r = PutInHand(e, o, Reasoning);
                o.Deck.Clear();
                o.Deck.AddRange(new[] { PotOfGreed, Gaia, Celtic });
                var act = e.TryActivateSpellTrap(o, r, fromHand: true);
                Settle(e);
                var names = Names(e);
                var declared = Pick(e, p, "Level 7");
                Settle(e);
                Check("Reasoning (AI's): the human declares Level 7 from 12 Level tokens; Gaia is Level 7 — Pot of Greed " +
                      "and Gaia go to the GY, nothing is Summoned",
                    reachedO && act && names.StartsWith("Level 1,Level 2") && names.EndsWith("Level 12") && declared &&
                    o.Graveyard.Any(c => c.CardId == Gaia) && o.Graveyard.Any(c => c.CardId == PotOfGreed) &&
                    !o.MonstersOnField().Any() && o.Deck.Count == 1 && e.PendingActivation == null,
                    $"act={act} names={names} declared={declared} deck={o.Deck.Count} " +
                    $"gy={string.Join(",", o.Graveyard.Select(c => c.Name))} {State(e)}");
            }
            {
                // AI vs AI: the AI activator, an AI declarer (Level 4 from the activator's public Level 4 monsters); the
                // excavated Celtic Guardian (Level 4) matches → both excavated cards go to the GY.
                var e = OpponentMain(db, pDeck, aDeck, out var reachedO);
                var p = e.Player;
                var o = e.Opponent;
                p.IsPlayer = false;
                try
                {
                    var r = PutInHand(e, o, Reasoning);
                    o.Graveyard.Add(e.CreateCardInstance(DarkBlade));
                    o.Deck.Clear();
                    o.Deck.AddRange(new[] { DarkHole, Celtic, Gaia });
                    var wants = CallsDiceAndSetTriggersRuntime.AiWantsToActivateFromHand(e, o, r);
                    var act = e.TryActivateSpellTrap(o, r, fromHand: true);
                    Settle(e);
                    Check("Reasoning (AI vs AI): Level 4 declared at once; the excavated Level 4 matches — 2 cards to the GY",
                        reachedO && wants && act && e.PendingActivation == null && !o.MonstersOnField().Any() &&
                        o.Graveyard.Any(c => c.CardId == Celtic) && o.Graveyard.Any(c => c.CardId == DarkHole) &&
                        o.Deck.Count == 1 && o.Deck[0] == Gaia,
                        $"wants={wants} act={act} deck={o.Deck.Count} {State(e)}");
                }
                finally
                {
                    p.IsPlayer = true;
                }
            }
            {
                // No monster that can be Normal Summoned/Set in the Deck, or no free Monster Zone: cannot be activated.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var r = PutInHand(e, p, Reasoning);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { PotOfGreed, DarkHole });
                var noMonster = !e.CanActivateSpellTrap(p, r, fromHand: true);
                p.Deck.Add(Celtic);
                var withMonster = e.CanActivateSpellTrap(p, r, fromHand: true);
                for (var i = 0; i < p.MonsterZones.Length; i++)
                    if (p.MonsterZones[i].Occupant == null && p.MonsterZones[i].IsEmpty)
                        PlaceMonster(e, p, GiantSoldier, i, BattlePosition.Defense, true);
                var fullField = !e.CanActivateSpellTrap(p, r, fromHand: true);
                Check("Reasoning: needs a monster that can be Normal Summoned/Set in the Deck and a free Monster Zone",
                    noMonster && withMonster && fullField,
                    $"noMonster={noMonster} withMonster={withMonster} fullField={fullField}");
            }
        }
    }
}
