using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// LastTurn batch (v163): Last Turn. Compile shape, the activation condition (opponent's turn, your LP 1000 or less, a
    /// monster to select), mini-duels through the real entry points (TryActivateSpellTrap, the response windows, the human
    /// picks through TrySelectEffectTarget, the forced battle, EndTurn into the End Phase check) for each result — the
    /// activator wins, the opponent wins, a DRAW — the AI's use of it, and near misses.
    /// Runtime: <see cref="LastTurnRuntime"/>, <see cref="DuelEngine.BeginForcedAttack"/>.
    /// </summary>
    public static class LastTurnRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int LastTurn = 28566710;    // Normal Trap
        const int Celtic = 91152256;      // Lv4 1400/1200
        const int BlueEyes = 89631139;    // Lv8 3000/2500
        const int MysticalElf = 15025844; // Lv4 800/2000
        const int PotOfGreed = 55144522;
        const int Dustshoot = 64697231;   // a Set Trap as filler
        const int BlackLuster = 5405694;  // Ritual Monster: cannot be Special Summoned from the Deck

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("LastTurn: " + name, ok, detail ?? "");

            CompileShape(db, Check);
            ActivationCondition(db, pDeck, aDeck, Check);
            ActivatorWins(db, pDeck, aDeck, Check);
            OpponentWins(db, pDeck, aDeck, Check);
            DrawResult(db, pDeck, aDeck, Check);
            NoMonsterInDeck(db, pDeck, aDeck, Check);
            AnswersAnAttack(db, pDeck, aDeck, Check);
            AiUse(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static List<string> Capture(DuelEngine engine)
        {
            var lines = new List<string>();
            engine.OnLog += s => lines.Add(s);
            return lines;
        }

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", cards.Where(c => c != null).Select(c => c.Name).OrderBy(n => n));

        /// <summary>The opponent's Main Phase 1 (turn 2+), boards and hands empty.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine) && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards and hands empty.</summary>
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
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            who.Deck.AddRange(ids);
        }

        static void EndTurn(DuelEngine engine)
        {
            Settle(engine);
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
        }

        // ───────────────────────────── compile ─────────────────────────────

        static void CompileShape(CardDatabase db, Chk Check)
        {
            var prog = CardTextEffectCompiler.Compile(db.Get(LastTurn));
            var c = prog?.ClauseList.FirstOrDefault();
            Check("compiles to one Activate clause: opponent's turn, your LP 1000 or less, the showdown",
                prog != null && prog.FullyCompiled && Implemented(db, LastTurn) && prog.ClauseList.Count == 1 &&
                c != null && c.Timing == EffectTiming.Activate &&
                c.Action == EffectActionKind.SelectedMonsterShowdownLoneMonsterWins &&
                c.ActivationCondition == LastTurnRuntime.OpponentTurnYourLpAtMost && c.ConditionAmount == 1000 &&
                c.OpponentTurnOnly && !c.RequiresTargetChoice && !c.IsPsctTarget &&
                EffectVocabulary.ResolutionOf(c) == EffectResolutionKind.SpecialSummon,
                prog == null ? "null" : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} act={c?.Action} cond={c?.ActivationCondition}");
        }

        // ───────────────────────────── activation condition ─────────────────────────────

        static void ActivationCondition(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var trap = PlaceSetTrap(engine, p, LastTurn, 0);
                p.LifePoints = 1000;
                var noMonster = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                p.LifePoints = 1001;
                var lp1001 = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                p.LifePoints = 1000;
                var lp1000 = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                Check("opponent's turn: needs 1000 or less LP and a monster you control",
                    reached && !noMonster && !lp1001 && lp1000,
                    $"reached={reached} noMonster={noMonster} lp1001={lp1001} lp1000={lp1000}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var trap = PlaceSetTrap(engine, p, LastTurn, 0);
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, engine.Opponent, Celtic, 0, BattlePosition.Attack, true);
                p.LifePoints = 500;
                var own = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                Check("never during your own turn", reached && !own, $"reached={reached} own={own}");
            }
        }

        // ───────────────────────────── the three results ─────────────────────────────

        static void ActivatorWins(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The human keeps Blue-Eyes among 2 monsters; every other card on the field and in both hands goes to the GY; the
            // AI summons its highest-ATK Deck monster (Celtic, not the Ritual Black Luster Soldier), which must attack Blue-Eyes
            // and is destroyed with no battle damage; the turn goes on in Main Phase 1 and the player wins at the End Phase.
            var engine = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            p.LifePoints = 1000;
            var bewd = PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
            var trap = PlaceSetTrap(engine, p, LastTurn, 0);
            var pSet = PlaceSetTrap(engine, p, Dustshoot, 1);
            var pHand = PutInHand(engine, p, PotOfGreed);
            var oElf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Attack, true);
            var oSet = PlaceSetTrap(engine, o, Dustshoot, 0);
            var oHand = new[] { PutInHand(engine, o, PotOfGreed), PutInHand(engine, o, BlueEyes) };
            SetDeck(o, MysticalElf, BlackLuster, Celtic, PotOfGreed);
            var oLp = o.LifePoints;
            var log = Capture(engine);

            var act = engine.TryActivateSpellTrap(p, trap, fromHand: false);
            Settle(engine);
            var pend = engine.PendingActivation;
            var legal = pend == null ? "" : Names(pend.LegalTargets);
            var picked = engine.TrySelectEffectTarget(bewd);
            Settle(engine);
            var oMonsters = o.MonstersOnField().ToList();
            var sentOk = p.Graveyard.Contains(celtic) && p.Graveyard.Contains(pSet) && p.Graveyard.Contains(pHand) &&
                         p.Graveyard.Contains(trap) && o.Graveyard.Contains(oElf) && o.Graveyard.Contains(oSet) &&
                         oHand.All(o.Graveyard.Contains) && p.Hand.Count == 0 && o.Hand.Count == 0;
            var battleOk = oMonsters.Count == 0 && o.Graveyard.Any(g => g.CardId == Celtic) &&
                           p.MonstersOnField().SequenceEqual(new[] { bewd }) && o.LifePoints == oLp &&
                           p.LifePoints == 1000 && log.Exists(l => l.Contains("attacks Blue-Eyes White Dragon"));
            var phaseOk = engine.Phase == DuelPhase.Main1 && !engine.HasDeclaredAttack && !engine.GameOver &&
                          !engine.IsAwaitingEffectTarget && !engine.IsAwaitingResponse;
            Check("activator keeps Blue-Eyes; all else to the GY; the summoned Celtic attacks it, no damage; back to Main 1",
                reached && act && pend != null && pend.TargetKind == EffectTargetKind.YourMonster &&
                legal == "Blue-Eyes White Dragon,Celtic Guardian" && picked && sentOk && battleOk && phaseOk,
                $"act={act} legal={legal} picked={picked} sent={sentOk} battle={battleOk} phase={engine.Phase} " +
                $"oField={Names(oMonsters)} lp={p.LifePoints}/{o.LifePoints} oDeck={string.Join(",", o.Deck)}");

            EndTurn(engine);
            Check("End Phase: only the activator's monster remains → the activator wins the Duel",
                engine.GameOver && engine.Winner == p && engine.EndReason == DuelEndReason.CardEffect,
                $"over={engine.GameOver} winner={engine.Winner?.Name} reason={engine.EndReason}");
        }

        static void OpponentWins(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A lone monster is kept with no prompt; the AI's Blue-Eyes destroys it (1600 battle damage treated as 0, so the
            // activator's 1000 LP stay) and the opponent wins at the End Phase.
            var engine = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            p.LifePoints = 1000;
            var celtic = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
            var trap = PlaceSetTrap(engine, p, LastTurn, 0);
            SetDeck(o, Celtic, BlueEyes);
            var act = engine.TryActivateSpellTrap(p, trap, fromHand: false);
            Settle(engine);
            var noPrompt = !engine.IsAwaitingEffectTarget;
            var oField = o.MonstersOnField().ToList();
            Check("the lone monster is kept with no prompt; the AI's Blue-Eyes destroys it, no battle damage",
                reached && act && noPrompt && p.Graveyard.Contains(celtic) && p.MonsterCount == 0 &&
                oField.Count == 1 && oField[0].CardId == BlueEyes && oField[0].FaceUp &&
                oField[0].Position == BattlePosition.Attack && p.LifePoints == 1000 && !engine.GameOver &&
                engine.Phase == DuelPhase.Main1,
                $"act={act} noPrompt={noPrompt} oField={Names(oField)} lp={p.LifePoints} over={engine.GameOver} phase={engine.Phase}");

            EndTurn(engine);
            Check("End Phase: only the opponent's monster remains → the opponent wins the Duel",
                engine.GameOver && engine.Winner == o && engine.EndReason == DuelEndReason.CardEffect,
                $"over={engine.GameOver} winner={engine.Winner?.Name} reason={engine.EndReason}");
        }

        static void DrawResult(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The kept monster is a face-down Mystical Elf: the forced attack flips it (DEF 2000) and nothing is destroyed;
            // the attacker's 600 battle damage is treated as 0. Both sides keep a monster → DRAW at the End Phase.
            var engine = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            p.LifePoints = 800;
            var elf = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Defense, false);
            var trap = PlaceSetTrap(engine, p, LastTurn, 0);
            SetDeck(o, Celtic, PotOfGreed);
            var oLp = o.LifePoints;
            var act = engine.TryActivateSpellTrap(p, trap, fromHand: false);
            Settle(engine);
            var oField = o.MonstersOnField().ToList();
            Check("the summoned Celtic attacks the face-down Elf: it is flipped, nothing is destroyed, no battle damage",
                reached && act && p.MonstersOnField().SequenceEqual(new[] { elf }) && elf.FaceUp &&
                oField.Count == 1 && oField[0].CardId == Celtic && o.LifePoints == oLp && p.LifePoints == 800 &&
                !engine.GameOver,
                $"act={act} elfUp={elf.FaceUp} oField={Names(oField)} lp={p.LifePoints}/{o.LifePoints}");

            EndTurn(engine);
            Check("End Phase: monsters on both sides → the Duel is a DRAW",
                engine.GameOver && engine.Winner == null && engine.IsDraw,
                $"over={engine.GameOver} winner={engine.Winner?.Name} reason={engine.EndReason}");
        }

        static void NoMonsterInDeck(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Nothing in the opponent's Deck can be Special Summoned (a Spell, a Ritual Monster): no Summon, no attack; the
            // check still applies at the End Phase.
            var engine = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            p.LifePoints = 1000;
            var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
            var oElf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Attack, true);
            var trap = PlaceSetTrap(engine, p, LastTurn, 0);
            SetDeck(o, PotOfGreed, BlackLuster);
            var act = engine.TryActivateSpellTrap(p, trap, fromHand: false);
            Settle(engine);
            var settled = act && o.MonsterCount == 0 && o.Graveyard.Contains(oElf) && o.Deck.Count == 2 &&
                          p.MonstersOnField().SequenceEqual(new[] { celtic }) && !engine.GameOver;
            EndTurn(engine);
            Check("no monster in the opponent's Deck can be Special Summoned → no attack; the activator wins at the End Phase",
                reached && settled && engine.GameOver && engine.Winner == p,
                $"act={act} settled={settled} oDeck={o.Deck.Count} winner={engine.Winner?.Name}");
        }

        static void AnswersAnAttack(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // In the attack-declared window of a lethal attack (Blue-Eyes on Celtic, 1600 against 1000 LP): Last Turn sends
            // the attacker to the GY, so that attack ends; then the summoned Elf must attack Celtic and is destroyed with no
            // damage; the Battle Phase goes on and the activator wins at the End Phase.
            var engine = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentBattle(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            var p = engine.Player;
            var o = engine.Opponent;
            p.LifePoints = 1000;
            var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
            var trap = PlaceSetTrap(engine, p, LastTurn, 0);
            var bewd = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
            SetDeck(o, MysticalElf);
            var oLp = o.LifePoints;
            engine.NotifyPublic();
            var declared = engine.TryAttack(o, bewd, celtic);
            var window = engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                         engine.PendingResponse.Responder == p && engine.PendingResponse.LegalCards.Contains(trap);
            var act = window && engine.TryActivateSpellTrap(p, trap, fromHand: false);
            Settle(engine);
            DrainCombat(engine);
            var settled = o.Graveyard.Contains(bewd) && o.Graveyard.Any(g => g.CardId == MysticalElf) &&
                          o.MonsterCount == 0 && p.MonstersOnField().SequenceEqual(new[] { celtic }) &&
                          p.LifePoints == 1000 && o.LifePoints == oLp && !engine.HasDeclaredAttack &&
                          engine.Phase == DuelPhase.Battle && !engine.GameOver;
            Check("answers a lethal attack: the attacker is sent away; the summoned Elf must attack Celtic, no damage",
                reached && declared && window && act && settled,
                $"declared={declared} window={window} act={act} settled={settled} lp={p.LifePoints}/{o.LifePoints} " +
                $"oField={Names(o.MonstersOnField())} phase={engine.Phase}");
            EndTurn(engine);
            Check("answers a lethal attack: the activator wins at the End Phase",
                engine.GameOver && engine.Winner == p, $"winner={engine.Winner?.Name}");
        }

        // ───────────────────────────── AI ─────────────────────────────

        static void AiUse(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI, at 1000 LP, faces the player's 3000-ATK Blue-Eyes as the Battle Phase starts: it activates Last
                // Turn. It keeps its Celtic; the human picks a monster from their own Deck (the Deck pick); the Elf must
                // attack Celtic and is destroyed with no damage; the AI wins at the End Phase.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                o.LifePoints = 1000;
                var oCeltic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var trap = PlaceSetTrap(engine, o, LastTurn, 0);
                var bewd = PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
                SetDeck(p, BlueEyes, MysticalElf, BlackLuster);
                var pLp = p.LifePoints;
                var bp = engine.TryEnterBattlePhase(p);
                Settle(engine);
                var pend = engine.PendingActivation;
                var legal = pend == null ? "" : Names(pend.LegalTargets);
                var elf = pend?.LegalTargets.FirstOrDefault(c => c.CardId == MysticalElf);
                var picked = elf != null && engine.TrySelectEffectTarget(elf);
                Settle(engine);
                Check("AI: Last Turn against a lethal attacker; the human picks the Deck monster; it must attack the kept one",
                    reached && bp && o.Graveyard.Contains(trap) && p.Graveyard.Contains(bewd) &&
                    pend != null && pend.Controller == p && pend.TargetKind == EffectTargetKind.MonsterInYourDeckToSummon &&
                    legal == "Blue-Eyes White Dragon,Mystical Elf" && picked && p.MonsterCount == 0 &&
                    p.Graveyard.Any(g => g.CardId == MysticalElf) && o.MonstersOnField().SequenceEqual(new[] { oCeltic }) &&
                    p.LifePoints == pLp && o.LifePoints == 1000 && engine.Phase == DuelPhase.Battle &&
                    !engine.IsAwaitingEffectTarget,
                    $"bp={bp} legal={legal} picked={picked} pField={Names(p.MonstersOnField())} " +
                    $"oField={Names(o.MonstersOnField())} lp={p.LifePoints}/{o.LifePoints} phase={engine.Phase}");

                EndTurn(engine);
                Check("AI: only its monster remains at the End Phase → the AI wins",
                    engine.GameOver && engine.Winner == o, $"winner={engine.Winner?.Name}");
            }

            {
                // Not legal at 1001 LP: the AI never activates it.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                o.LifePoints = 1001;
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var trap = PlaceSetTrap(engine, o, LastTurn, 0);
                PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
                var bp = engine.TryEnterBattlePhase(p);
                Settle(engine);
                Check("AI: above 1000 LP it never activates Last Turn",
                    reached && bp && !trap.FaceUp && o.SpellTrapZones[0].Occupant == trap && p.MonsterCount == 1,
                    $"bp={bp} faceUp={trap.FaceUp}");
            }

            {
                // Legal, but no attacker of the player can take the AI's last LP: the AI keeps it Set.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                o.LifePoints = 1000;
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var trap = PlaceSetTrap(engine, o, LastTurn, 0);
                PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var bp = engine.TryEnterBattlePhase(p);
                Settle(engine);
                Check("AI: no lethal threat → it keeps Last Turn Set",
                    reached && bp && !trap.FaceUp && o.SpellTrapZones[0].Occupant == trap,
                    $"bp={bp} faceUp={trap.FaceUp}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            const string Full =
                "This card can only be activated during your opponent's turn when your Life Points are 1000 or less. " +
                "Select 1 monster on your side of the field and send all other cards on the field and in their respective " +
                "owners' hands to their respective Graveyards. After that, your opponent selects and Special Summons 1 " +
                "monster from their Deck in face-up Attack Position and attacks your selected monster. (Any Battle Damage " +
                "from this battle is treated as 0.) The player whose monster remains alone on the field at the End Phase " +
                "of this turn wins the Duel. Any other case results in a DRAW.";
            var cases = new[]
            {
                // Battle damage not treated as 0.
                Full.Replace(" (Any Battle Damage from this battle is treated as 0.)", ""),
                // No DRAW sentence.
                Full.Replace(" Any other case results in a DRAW.", ""),
                // During your own turn.
                Full.Replace("during your opponent's turn", "during your turn"),
                // The opponent's monster is the one selected.
                Full.Replace("Select 1 monster on your side of the field", "Select 1 monster on your opponent's side of the field"),
                // The summoned monster is Set, or comes from the hand.
                Full.Replace("in face-up Attack Position", "in face-down Defense Position"),
                Full.Replace("from their Deck", "from their hand"),
            };
            var like = db.Get(LastTurn);
            var i = 0;
            foreach (var text in cases)
            {
                i++;
                var def = new CardDef
                {
                    id = 0,
                    name = "LastTurn near miss",
                    type = like.type,
                    frameType = like.frameType,
                    race = like.race,
                    desc = text
                };
                var prog = CardTextEffectCompiler.Compile(def);
                var bad = prog != null && prog.FullyCompiled &&
                          prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SelectedMonsterShowdownLoneMonsterWins);
                Check($"near miss #{i} stays uncompiled", !bad,
                    prog == null ? "null" : $"full={prog.FullyCompiled} n={prog.ClauseList.Count}");
            }
        }
    }
}
