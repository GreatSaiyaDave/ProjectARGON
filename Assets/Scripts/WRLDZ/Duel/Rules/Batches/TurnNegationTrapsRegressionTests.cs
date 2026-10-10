using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "TurnNegationTraps" (v141): World Suppression, Mystic Probe, Metal Detector — Normal
    /// Traps chained to the activation of a Field Spell / Continuous Spell / Continuous Trap that negate
    /// every card of that kind on the field for the rest of the turn. Armored Glass is the EquipModel batch's
    /// (no post-equip activation window).
    /// </summary>
    public static class TurnNegationTrapsRegressionTests
    {
        const int WorldSuppression = 12253117;
        const int MysticProbe = 49251811;
        const int MetalDetector = 75646520;
        const int ArmoredGlass = 36868108;

        const int YellowLusterShield = 4542651; // Continuous Spell: your monsters +300 DEF
        const int MessengerOfPeace = 44656491;  // Continuous Spell: 1500+ ATK cannot attack
        const int MassDriver = 34906152;        // Continuous Spell (no compiled face-up ignition: not re-activatable)
        const int GravityBind = 85742772;       // Continuous Trap: Level 4+ cannot attack
        const int CallOfTheHaunted = 97077563;  // Continuous Trap: SS from GY, linked
        const int SpiritBarrier = 53239672;     // Continuous Trap
        const int InfiniteDismissal = 54109233; // Continuous Trap: End Phase destroy Lv3- summoned
        const int TornadoWall = 18605135;       // Continuous Trap: needs "Umi"; destroyed when Umi leaves
        const int Umi = 22702055;
        const int Yami = 59197169;              // Field: Fiend / Spellcaster +200
        const int Sogen = 86318356;             // Field: Warrior / Beast-Warrior +200
        const int Mountain = 50913601;          // Field: Dragon / Winged Beast / Thunder +200
        const int PotOfGreed = 55144522;
        const int SnatchSteal = 45986603;
        const int RemoveBrainwashing = 94739788; // Continuous Trap: control returns to the owners
        const int ToonWorld = 15259703;          // Continuous Spell: activate by paying 1000 LP
        const int SwordsOfConcealingLight = 12923641;

        const int SpiritsInvitation = 92394653;  // Continuous Trap: pay 500 LP or destroy (Standby upkeep)
        const int DarkbishopArchfiend = 35798491; // Archfiend: pays 500 LP each Standby (maintenance)
        const int BurningLand = 24294108;         // Continuous Spell: turn player takes 500 each Standby
        const int DragonCaptureJar = 50045299;    // Continuous Trap: all Dragons to Defense (untargeted)
        const int BattleScarred = 94463200;       // Continuous Trap linked to an Archfiend you control
        const int MagicJammer = 77414722;         // Counter Trap: discard 1, negate a Spell's activation
        const int LevelLimitAreaB = 3136426;      // Continuous Spell: Level 4+ monsters to Defense Position
        const int ExileOfTheWicked = 26725158;    // Normal Spell: destroy all Fiend monsters

        const int Celtic = 91152256;       // Warrior Lv4 1400/1200
        const int Bewd = 89631139;         // Dragon Lv8 3000/2500
        const int SummonedSkull = 70781052; // Fiend Lv6 2500/1200
        const int SkullServant = 32274490; // Zombie Lv1 300/200

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("TurnNegationTraps: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            MetalDetectorDuels(db, pDeck, aDeck, Check);
            MysticProbeDuels(db, pDeck, aDeck, Check);
            WorldSuppressionDuels(db, pDeck, aDeck, Check);
            AiAnswers(db, pDeck, aDeck, Check);
            ReviewFixes(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static EffectClause OnlyClause(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
        }

        static bool FamilyClause(EffectClause c, string family) =>
            c != null && c.Timing == EffectTiming.Activate &&
            c.Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn &&
            c.NegatedCardFamily == family &&
            c.ActivationCondition == TurnNegationTrapsRuntime.ChainLinkActivatesCardFamily &&
            c.ConditionName == family && c.CheckedAt == ConditionCheckedAt.Activation &&
            !c.RequiresTargetChoice && c.MakesChainLink && !c.StaysOnField;

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            foreach (var (id, family) in new[]
                     {
                         (WorldSuppression, TurnNegationTrapsRuntime.FieldSpell),
                         (MysticProbe, TurnNegationTrapsRuntime.ContinuousSpell),
                         (MetalDetector, TurnNegationTrapsRuntime.ContinuousTrap)
                     })
            {
                var def = db.Get(id);
                var c = OnlyClause(db, id, out var prog);
                Check($"{def?.name}: FullyCompiled, one untargeted Activate NegateCardFamilyOnFieldThisTurn ({family})",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && FamilyClause(c, family),
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check($"{def?.name}: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            // Armored Glass (EquipModel batch): the same family negation, activated in the window right after a monster
            // is equipped with an Equip Card (not as a chain answer). Its duels are in EquipModelRegressionTests.
            var armoredClause = OnlyClause(db, ArmoredGlass, out var armored);
            Check("Armored Glass: FullyCompiled, the Equip Card family, answering the equip window (not a chain link)",
                armored != null && armored.FullyCompiled && armoredClause != null &&
                armoredClause.Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn &&
                armoredClause.NegatedCardFamily == TurnNegationTrapsRuntime.EquipCard &&
                armoredClause.ActivationCondition == EquipModelRuntime.MonsterEquippedWithEquipCard &&
                !FamilyClause(armoredClause, TurnNegationTrapsRuntime.EquipCard),
                armoredClause == null ? $"clauses={armored?.ClauseList.Count}" : EffectVocabulary.Describe(armoredClause));
            Check("NegateCardFamilyOnFieldThisTurn maps to a shared resolution",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.NegateCardFamilyOnFieldThisTurn }) == EffectResolutionKind.Protection);
        }

        static CompiledCardProgram CompileText(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990001, name = "Near-miss Trap", type = "Trap Card", frameType = "trap", race = "Normal",
                desc = text
            });

        static bool CompilesToFamilyNegation(string text)
        {
            var prog = CompileText(text);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn);
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            Check("Near miss: an extra sentence after Mystic Probe's text is not compiled",
                !CompilesToFamilyNegation(
                    "You can only activate this card when a Continuous Spell Card is activated. Negate all " +
                    "Continuous Spell Cards during the turn this card is activated. Then draw 1 card."));
            Check("Near miss: another card family (Equip Spell) is not compiled",
                !CompilesToFamilyNegation(
                    "You can only activate this card when an Equip Spell Card is activated. Negate all " +
                    "Equip Spell Cards during the turn this card is activated."));
            Check("Near miss: negating the activation instead is not this template",
                !CompilesToFamilyNegation(
                    "You can only activate this card when a Continuous Trap Card is activated. Negate the " +
                    "activation of that Continuous Trap Card."));
            Check("Near miss: a lasting (not this-turn) negation is not this template",
                !CompilesToFamilyNegation(
                    "You can only activate this card when a Field Spell Card is activated. Negate the Field " +
                    "Spell Card."));
            Check("Near miss: Royal Decree's continuous negation is not this template",
                !CardTextEffectCompiler.Compile(db.Get(51452091))?.ClauseList
                    .Exists(c => c != null && c.Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn) ?? true);

            // Only a card activation opens the window, never an effect of a card already face-up.
            var face = new CardInstance { CardId = YellowLusterShield, Def = db.Get(YellowLusterShield), FaceUp = true };
            Check("Window: an effect of a face-up Continuous Spell is not a card activation",
                !TurnNegationTrapsRuntime.IsCardActivationOfFamily(
                    new ChainLink { Card = face, FromHand = false, WasSetOnField = false },
                    TurnNegationTrapsRuntime.ContinuousSpell) &&
                TurnNegationTrapsRuntime.IsCardActivationOfFamily(
                    new ChainLink { Card = face, FromHand = true },
                    TurnNegationTrapsRuntime.ContinuousSpell));

            // IsNegated keeps a turn-scoped family negation when another effect writes the stored flag.
            var both = new CardInstance { CardId = GravityBind, Def = db.Get(GravityBind), FaceUp = true };
            both.NegatedByTurnFamily = true;
            both.IsNegated = false;
            Check("CardInstance.IsNegated: writing the stored flag never drops a turn-family negation",
                both.IsNegated);
        }

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>Advance to <paramref name="who"/>'s Main Phase 1 on game turn ≥ <paramref name="minTurn"/>.</summary>
        static bool ReachMain(DuelEngine engine, DuelistState who, int minTurn)
        {
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn)
                    return true;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceFaceUpField(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static bool ChainWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        /// <summary>The human (Player) answers the open chain window with <paramref name="trap"/>; the chain resolves.</summary>
        static bool Answer(DuelEngine engine, DuelistState p, CardInstance trap)
        {
            var ok = engine.TryActivateSpellTrap(p, trap, fromHand: false);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            return ok;
        }

        // ───────────────────────── Metal Detector ─────────────────────────

        static void MetalDetectorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            CheckFn Check)
        {
            {
                // Not legal at open game state (no Chain Link to answer).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var md = PlaceSetTrap(engine, engine.Player, MetalDetector, 0);
                Check("Metal Detector: cannot be activated at open game state",
                    !engine.CanActivateSpellTrap(engine.Player, md, fromHand: false));
            }

            {
                // The opponent flips Gravity Bind during your turn; Metal Detector answers it. Gravity Bind
                // resolves without effect and stays; your Level 8 can attack this turn; next turn it cannot.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var gb = PlaceSetTrap(engine, opp, GravityBind, 0);
                var md = PlaceSetTrap(engine, p, MetalDetector, 0);
                var pog = PlaceSetTrap(engine, p, PotOfGreed, 1); // a Normal Spell Set: never offered
                var activated = engine.TryActivateSpellTrap(opp, gb, fromHand: false);
                Check("Metal Detector: offered in the chain window of a Continuous Trap's activation",
                    reached && activated && ChainWindowOffers(engine, p, md) &&
                    !engine.PendingResponse.LegalCards.Contains(pog),
                    Window(engine));
                var answered = Answer(engine, p, md);
                Check("Metal Detector: resolves — Gravity Bind stays face-up but negated, Metal Detector to GY",
                    answered && gb.FaceUp && opp.TryFindSpellTrap(gb, out _) && gb.IsNegated &&
                    p.Graveyard.Contains(md) && !engine.IsAwaitingResponse && engine.Chain.Count == 0,
                    $"answered={answered} gbUp={gb.FaceUp} gbNeg={gb.IsNegated} mdGy={p.Graveyard.Contains(md)} " +
                    $"chain={engine.Chain.Count} {Window(engine)}");
                engine.TryEnterBattlePhase(p);
                Check("Metal Detector: negated Gravity Bind lets a Level 8 attack this turn",
                    !engine.ContinuousCannotAttackBlocks(p, bewd) && engine.CanAttack(p, bewd),
                    $"phase={engine.Phase}");
                EndTurn(engine);
                var oppTurn = engine.TurnPlayer == opp;
                Check("Metal Detector: the negation ends with the turn",
                    oppTurn && !gb.IsNegated &&
                    !TurnNegationTrapsRuntime.FamilyNegatedThisTurn(engine, TurnNegationTrapsRuntime.ContinuousTrap),
                    $"turn={engine.TurnNumber} gbNeg={gb.IsNegated}");
                Check("Metal Detector: next turn Gravity Bind stops the Level 8 again",
                    engine.ContinuousCannotAttackBlocks(p, bewd));
            }

            {
                // Control: Call of the Haunted with no answer Special Summons its target.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var opp = engine.Opponent;
                engine.Player.Hand.Clear();
                opp.Hand.Clear();
                var skull = engine.CreateCardInstance(SummonedSkull);
                opp.Graveyard.Add(skull);
                var coth = PlaceSetTrap(engine, opp, CallOfTheHaunted, 0);
                var activated = engine.TryActivateSpellTrap(opp, coth, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Call of the Haunted (control): with no answer it Special Summons its target",
                    activated && opp.TryFindMonster(skull, out _) && coth.EquippedTo == skull,
                    $"act={activated} skullField={opp.TryFindMonster(skull, out _)}");
            }

            {
                // Call of the Haunted chained by Metal Detector: nothing is Special Summoned, the Trap stays.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = engine.CreateCardInstance(SummonedSkull);
                opp.Graveyard.Add(skull);
                var coth = PlaceSetTrap(engine, opp, CallOfTheHaunted, 0);
                var md = PlaceSetTrap(engine, p, MetalDetector, 0);
                var activated = engine.TryActivateSpellTrap(opp, coth, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                var offered = ChainWindowOffers(engine, p, md);
                var answered = offered && Answer(engine, p, md);
                Check("Metal Detector vs Call of the Haunted: no Special Summon, the Trap stays face-up",
                    activated && answered && opp.Graveyard.Contains(skull) && opp.MonsterCount == 0 &&
                    coth.FaceUp && opp.TryFindSpellTrap(coth, out _) && coth.EquippedTo == null,
                    $"act={activated} offered={offered} answered={answered} skullGy={opp.Graveyard.Contains(skull)} " +
                    $"mons={opp.MonsterCount} cothField={opp.TryFindSpellTrap(coth, out _)} {Window(engine)}");

                // Another Continuous Trap activated later this turn is negated as well.
                var second = PlaceSetTrap(engine, opp, CallOfTheHaunted, 1);
                var laterOk = engine.TryActivateSpellTrap(opp, second, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Metal Detector: a Continuous Trap activated later the same turn resolves without effect",
                    laterOk && opp.Graveyard.Contains(skull) && opp.MonsterCount == 0 &&
                    second.FaceUp && opp.TryFindSpellTrap(second, out _),
                    $"later={laterOk} skullGy={opp.Graveyard.Contains(skull)} mons={opp.MonsterCount}");
            }

            {
                // A face-up Call of the Haunted already linked to its monster: while negated, destroying the
                // monster does not destroy the Trap, and sending the Trap away does not destroy the monster.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var coth = PlaceFaceUpSpellTrap(engine, opp, CallOfTheHaunted, 0);
                coth.EquippedTo = skull;
                skull.Equips.Add(coth);
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                var coth2 = PlaceFaceUpSpellTrap(engine, opp, CallOfTheHaunted, 1);
                coth2.EquippedTo = celtic;
                celtic.Equips.Add(coth2);
                var gb = PlaceSetTrap(engine, opp, GravityBind, 2);
                var md = PlaceSetTrap(engine, p, MetalDetector, 0);
                engine.TryActivateSpellTrap(opp, gb, fromHand: false);
                var answered = ChainWindowOffers(engine, p, md) && Answer(engine, p, md);
                Check("Metal Detector: an already face-up Continuous Trap is negated too",
                    answered && coth.IsNegated && coth2.IsNegated, Window(engine));
                // Control: a Call of the Haunted that is not negated goes with its monster.
                var skull3 = PlaceMonster(engine, p, SummonedSkull, 2, BattlePosition.Attack, true);
                var coth3 = PlaceFaceUpSpellTrap(engine, p, CallOfTheHaunted, 3);
                coth3.EquippedTo = skull3;
                skull3.Equips.Add(coth3);
                engine.DestroyMonsterPublic(p, skull3);
                Check("Call of the Haunted (control, not negated): destroyed with its monster",
                    p.Graveyard.Contains(skull3) && p.Graveyard.Contains(coth3));
                engine.DestroyMonsterPublic(opp, skull);
                Check("Metal Detector: negated Call of the Haunted stays when its monster is destroyed",
                    opp.Graveyard.Contains(skull) && opp.TryFindSpellTrap(coth, out _),
                    $"skullGy={opp.Graveyard.Contains(skull)} cothField={opp.TryFindSpellTrap(coth, out _)}");
                engine.SendCardToGrave(opp, coth2);
                Check("Metal Detector: negated Call of the Haunted leaving does not destroy its monster",
                    opp.Graveyard.Contains(coth2) && opp.TryFindMonster(celtic, out _),
                    $"coth2Gy={opp.Graveyard.Contains(coth2)} celticField={opp.TryFindMonster(celtic, out _)}");
            }

            {
                // Remove Brainwashing negated this turn: Snatch Steal keeps control this turn; when the
                // negation ends (next turn) control snaps back to the owner.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var rb = PlaceFaceUpSpellTrap(engine, opp, RemoveBrainwashing, 0);
                TurnNegationTrapsRuntime.Register(engine, p, null, TurnNegationTrapsRuntime.ContinuousTrap);
                var snatch = PutInHand(engine, p, SnatchSteal);
                var ok = engine.TryActivateSpellTrap(p, snatch, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Metal Detector: negated Remove Brainwashing does not undo Snatch Steal this turn",
                    ok && rb.IsNegated && p.TryFindMonster(skull, out _),
                    $"ok={ok} rbNeg={rb.IsNegated} skullP={p.TryFindMonster(skull, out _)}");
                EndTurn(engine);
                Check("Metal Detector: next turn Remove Brainwashing applies again — control returns to the owner",
                    !rb.IsNegated && opp.TryFindMonster(skull, out _),
                    $"rbNeg={rb.IsNegated} skullOpp={opp.TryFindMonster(skull, out _)} turn={engine.TurnNumber}");
            }

            {
                // Spirit Barrier: battle damage goes through while it is negated (control: it stops it).
                foreach (var negate in new[] { false, true })
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ReachMain(engine, engine.Player, 3);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                    var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                    PlaceFaceUpSpellTrap(engine, opp, SpiritBarrier, 0);
                    if (negate)
                        TurnNegationTrapsRuntime.Register(engine, p, null, TurnNegationTrapsRuntime.ContinuousTrap);
                    var lp = opp.LifePoints;
                    engine.TryEnterBattlePhase(p);
                    var attacked = engine.TryAttack(p, bewd, celtic);
                    DrainCombat(engine);
                    Check(negate
                            ? "Metal Detector: negated Spirit Barrier lets 1600 battle damage through"
                            : "Spirit Barrier (control): no battle damage while it is face-up",
                        attacked && opp.LifePoints == (negate ? lp - 1600 : lp),
                        $"attacked={attacked} lp={lp}->{opp.LifePoints}");
                }
            }

            {
                // Tornado Wall: "Destroy this card when Umi leaves the field" is its own effect — not while
                // negated; once the negation ends it is destroyed.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var umi = PlaceFaceUpField(engine, opp, Umi);
                var wall = PlaceFaceUpSpellTrap(engine, opp, TornadoWall, 0);
                TurnNegationTrapsRuntime.Register(engine, p, null, TurnNegationTrapsRuntime.ContinuousTrap);
                engine.SendCardToGrave(opp, umi);
                engine.NotifyPublic();
                var stayed = opp.TryFindSpellTrap(wall, out _);
                EndTurn(engine);
                Check("Metal Detector: negated Tornado Wall is not destroyed when Umi leaves; it is once the negation ends",
                    stayed && !opp.TryFindSpellTrap(wall, out _) && opp.Graveyard.Contains(wall),
                    $"stayed={stayed} gy={opp.Graveyard.Contains(wall)}");
            }

            {
                // End Phase: a negated Infinite Dismissal does not destroy a Level 1 Normal Summoned this turn.
                foreach (var negate in new[] { false, true })
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ReachMain(engine, engine.Player, 3);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    PlaceFaceUpSpellTrap(engine, opp, InfiniteDismissal, 0);
                    var servant = PutInHand(engine, p, SkullServant);
                    var summoned = engine.TryNormalSummon(p, servant, asSet: false);
                    for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                    if (negate)
                    {
                        var sb = PlaceSetTrap(engine, opp, SpiritBarrier, 1);
                        var md = PlaceSetTrap(engine, p, MetalDetector, 0);
                        engine.TryActivateSpellTrap(opp, sb, fromHand: false);
                        if (ChainWindowOffers(engine, p, md)) Answer(engine, p, md);
                    }

                    EndTurn(engine);
                    var survived = p.TryFindMonster(servant, out _);
                    Check(negate
                            ? "Metal Detector: negated Infinite Dismissal destroys nothing in the End Phase"
                            : "Infinite Dismissal (control): destroys the Level 1 in the End Phase",
                        summoned && survived == negate,
                        $"summoned={summoned} survived={survived} turn={engine.TurnNumber}");
                }
            }

            {
                // Metal Detector does not answer a Continuous Spell's activation.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var md = PlaceSetTrap(engine, p, MetalDetector, 0);
                var yls = PutInHand(engine, opp, YellowLusterShield);
                engine.TryActivateSpellTrap(opp, yls, fromHand: true);
                Check("Metal Detector: not offered against a Continuous Spell's activation",
                    engine.PendingResponse == null || !engine.PendingResponse.LegalCards.Contains(md),
                    Window(engine));
            }
        }

        // ───────────────────────── Mystic Probe ─────────────────────────

        static void MysticProbeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            CheckFn Check)
        {
            {
                // The opponent activates Yellow Luster Shield; Mystic Probe negates it and the face-up
                // Messenger of Peace for the rest of the turn.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 1, BattlePosition.Attack, true);
                var messenger = PlaceFaceUpSpellTrap(engine, opp, MessengerOfPeace, 1);
                var yls = PutInHand(engine, opp, YellowLusterShield);
                var mp = PlaceSetTrap(engine, p, MysticProbe, 0);
                var md = PlaceSetTrap(engine, p, MetalDetector, 1);
                engine.NotifyPublic();
                var messengerBefore = engine.ContinuousCannotAttackBlocks(opp, skull);
                var activated = engine.TryActivateSpellTrap(opp, yls, fromHand: true);
                Check("Mystic Probe: offered against a Continuous Spell's activation (Metal Detector is not)",
                    reached && messengerBefore && activated && ChainWindowOffers(engine, p, mp) &&
                    !engine.PendingResponse.LegalCards.Contains(md),
                    $"reached={reached} msgBefore={messengerBefore} act={activated} {Window(engine)}");
                var answered = Answer(engine, p, mp);
                engine.NotifyPublic();
                Check("Mystic Probe: Yellow Luster Shield resolves negated — no +300 DEF this turn",
                    answered && yls.FaceUp && opp.TryFindSpellTrap(yls, out _) && yls.IsNegated &&
                    celtic.CurrentDef == 1200 && p.Graveyard.Contains(mp),
                    $"answered={answered} def={celtic.CurrentDef} ylsNeg={yls.IsNegated}");
                Check("Mystic Probe: the face-up Messenger of Peace is negated too (2500 ATK may attack)",
                    messenger.IsNegated && !engine.ContinuousCannotAttackBlocks(opp, skull));
                EndTurn(engine);
                Check("Mystic Probe: next turn both apply again (+300 DEF, Messenger locks attacks)",
                    engine.TurnPlayer == p && !yls.IsNegated && celtic.CurrentDef == 1500 &&
                    engine.ContinuousCannotAttackBlocks(opp, skull),
                    $"turn={engine.TurnNumber} def={celtic.CurrentDef}");
            }

            {
                // Costs are not negated: Toon World chained by Mystic Probe still costs 1000 LP.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var lp = opp.LifePoints;
                var toon = PutInHand(engine, opp, ToonWorld);
                var mp = PlaceSetTrap(engine, p, MysticProbe, 0);
                var activated = engine.TryActivateSpellTrap(opp, toon, fromHand: true);
                var answered = ChainWindowOffers(engine, p, mp) && Answer(engine, p, mp);
                Check("Mystic Probe vs Toon World: the 1000 LP cost is still paid; Toon World stays",
                    activated && answered && opp.LifePoints == lp - 1000 && toon.FaceUp &&
                    opp.TryFindSpellTrap(toon, out _) && toon.IsNegated,
                    $"act={activated} answered={answered} lp={lp}->{opp.LifePoints} {Window(engine)}");
            }

            {
                // Swords of Concealing Light chained by Mystic Probe: nothing is flipped face-down, but its
                // "destroy this card during your 2nd Standby Phase" timer is set at activation and cannot be
                // negated — it still leaves on schedule.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var swords = PutInHand(engine, opp, SwordsOfConcealingLight);
                var mp = PlaceSetTrap(engine, p, MysticProbe, 0);
                var activated = engine.TryActivateSpellTrap(opp, swords, fromHand: true);
                var answered = ChainWindowOffers(engine, p, mp) && Answer(engine, p, mp);
                Check("Mystic Probe vs Swords of Concealing Light: your monster is not flipped face-down",
                    activated && answered && celtic.FaceUp && celtic.Position == BattlePosition.Attack &&
                    swords.FaceUp && opp.TryFindSpellTrap(swords, out _) && swords.ContinuousTurnsRemaining == 2,
                    $"act={activated} answered={answered} up={celtic.FaceUp} timer={swords.ContinuousTurnsRemaining}");
                var turn = engine.TurnNumber;
                ReachMain(engine, opp, turn + 1);
                var afterFirst = opp.TryFindSpellTrap(swords, out _);
                ReachMain(engine, opp, engine.TurnNumber + 1);
                Check("Mystic Probe vs Swords of Concealing Light: still destroyed in its controller's 2nd Standby Phase",
                    afterFirst && !opp.TryFindSpellTrap(swords, out _) && opp.Graveyard.Contains(swords),
                    $"afterFirst={afterFirst} turn={engine.TurnNumber} gy={opp.Graveyard.Contains(swords)}");
            }

            {
                // A face-up Continuous Spell's own effect cannot be activated while negated. Mass Driver's program has
                // no Tribute cost (its Lua damage is a Tribute-cost ignition), so since the fuzz-audit fix its
                // face-up card is not re-activatable at all (TextEffectRuntime.FaceUpCardCannotActivateAgain); the
                // negated state must stay refused too.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var driver = PlaceFaceUpSpellTrap(engine, p, MassDriver, 0);
                var canBefore = engine.CanActivateSpellTrap(p, driver, fromHand: false);
                TurnNegationTrapsRuntime.Register(engine, opp, null, TurnNegationTrapsRuntime.ContinuousSpell);
                Check("Mystic Probe: a negated face-up Continuous Spell cannot activate its effect",
                    !canBefore && !engine.CanActivateSpellTrap(p, driver, fromHand: false),
                    $"before={canBefore}");
                var lp = opp.LifePoints;
                var yls = PutInHand(engine, p, YellowLusterShield);
                var played = engine.TryActivateSpellTrap(p, yls, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Mystic Probe: a Continuous Spell can still be activated from the hand (it resolves negated)",
                    played && yls.FaceUp && p.TryFindSpellTrap(yls, out _) && yls.IsNegated && opp.LifePoints == lp,
                    $"played={played} ylsNeg={yls.IsNegated}");
            }
        }

        // ───────────────────────── World Suppression ─────────────────────────

        static void WorldSuppressionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            CheckFn Check)
        {
            {
                // No response possible: the Field Spell resolves at once, as before.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var sogen = PutInHand(engine, p, Sogen);
                var ok = engine.TryActivateSpellTrap(p, sogen, fromHand: true);
                Check("Field Spell with nothing to answer it: placed and applied at once",
                    ok && p.FieldSpellZone.Occupant == sogen && !engine.IsAwaitingResponse &&
                    engine.Chain.Count == 0 && celtic.CurrentAtk == 1600,
                    $"ok={ok} chain={engine.Chain.Count} atk={celtic.CurrentAtk} {Window(engine)}");
            }

            {
                // The opponent activates Yami; World Suppression answers it and negates every Field Spell
                // on the field (your Mountain too) for the turn.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var mountain = PlaceFaceUpField(engine, p, Mountain);
                var yami = PutInHand(engine, opp, Yami);
                var ws = PlaceSetTrap(engine, p, WorldSuppression, 0);
                var mp = PlaceSetTrap(engine, p, MysticProbe, 1);
                engine.NotifyPublic();
                var bewdBefore = bewd.CurrentAtk;
                var activated = engine.TryActivateSpellTrap(opp, yami, fromHand: true);
                Check("World Suppression: offered in the chain window of a Field Spell's activation (Mystic Probe is not)",
                    reached && bewdBefore == 3200 && activated && opp.FieldSpellZone.Occupant == yami &&
                    ChainWindowOffers(engine, p, ws) && !engine.PendingResponse.LegalCards.Contains(mp),
                    $"reached={reached} bewd={bewdBefore} act={activated} {Window(engine)}");
                var answered = Answer(engine, p, ws);
                engine.NotifyPublic();
                Check("World Suppression: Yami resolves negated and stays; Mountain is negated too",
                    answered && opp.FieldSpellZone.Occupant == yami && yami.FaceUp && yami.IsNegated &&
                    mountain.IsNegated && skull.CurrentAtk == 2500 && bewd.CurrentAtk == 3000 &&
                    p.Graveyard.Contains(ws) && engine.Chain.Count == 0,
                    $"answered={answered} skull={skull.CurrentAtk} bewd={bewd.CurrentAtk} chain={engine.Chain.Count}");
                EndTurn(engine);
                Check("World Suppression: next turn both Field Spells apply again",
                    engine.TurnPlayer == p && skull.CurrentAtk == 2700 && bewd.CurrentAtk == 3200,
                    $"skull={skull.CurrentAtk} bewd={bewd.CurrentAtk}");
            }

            {
                // Passing the Field Spell's chain window leaves it applied.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var yami = PutInHand(engine, opp, Yami);
                var ws = PlaceSetTrap(engine, p, WorldSuppression, 0);
                engine.TryActivateSpellTrap(opp, yami, fromHand: true);
                var offered = ChainWindowOffers(engine, p, ws);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check("World Suppression: passing the window resolves the Field Spell normally",
                    offered && !engine.IsAwaitingResponse && engine.Chain.Count == 0 &&
                    opp.FieldSpellZone.Occupant == yami && !yami.IsNegated && skull.CurrentAtk == 2700 &&
                    !ws.FaceUp && p.TryFindSpellTrap(ws, out _),
                    $"offered={offered} chain={engine.Chain.Count} atk={skull.CurrentAtk}");
            }
        }

        // ───────────────────────── AI answers ─────────────────────────

        static void AiAnswers(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // You flip Gravity Bind; the AI answers with Metal Detector.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var gb = PlaceSetTrap(engine, p, GravityBind, 0);
                var md = PlaceSetTrap(engine, opp, MetalDetector, 0);
                var ok = engine.TryActivateSpellTrap(p, gb, fromHand: false);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("AI: chains Metal Detector to your Continuous Trap's activation",
                    ok && opp.Graveyard.Contains(md) && gb.FaceUp && gb.IsNegated && !engine.IsAwaitingResponse,
                    $"ok={ok} mdGy={opp.Graveyard.Contains(md)} gbNeg={gb.IsNegated} {Window(engine)}");
            }

            {
                // The AI keeps Metal Detector when it controls more face-up Continuous Traps than you.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceFaceUpSpellTrap(engine, opp, SpiritBarrier, 1);
                PlaceFaceUpSpellTrap(engine, opp, GravityBind, 2);
                var gb = PlaceSetTrap(engine, p, GravityBind, 0);
                var md = PlaceSetTrap(engine, opp, MetalDetector, 0);
                var ok = engine.TryActivateSpellTrap(p, gb, fromHand: false);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("AI: passes when the negation would cost it more",
                    ok && opp.TryFindSpellTrap(md, out _) && !md.FaceUp && gb.FaceUp && !gb.IsNegated,
                    $"ok={ok} mdSet={opp.TryFindSpellTrap(md, out _)} gbNeg={gb.IsNegated}");
            }

            {
                // You activate Sogen; the AI answers with World Suppression (new Field Spell chain window).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var sogen = PutInHand(engine, p, Sogen);
                var ws = PlaceSetTrap(engine, opp, WorldSuppression, 0);
                var ok = engine.TryActivateSpellTrap(p, sogen, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check("AI: chains World Suppression to your Field Spell's activation",
                    ok && opp.Graveyard.Contains(ws) && p.FieldSpellZone.Occupant == sogen && sogen.IsNegated &&
                    celtic.CurrentAtk == 1400 && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"ok={ok} wsGy={opp.Graveyard.Contains(ws)} atk={celtic.CurrentAtk} chain={engine.Chain.Count}");
            }

            {
                // You activate Yellow Luster Shield; the AI answers with Mystic Probe.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var yls = PutInHand(engine, p, YellowLusterShield);
                var mp = PlaceSetTrap(engine, opp, MysticProbe, 0);
                var ok = engine.TryActivateSpellTrap(p, yls, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check("AI: chains Mystic Probe to your Continuous Spell's activation",
                    ok && opp.Graveyard.Contains(mp) && yls.IsNegated && celtic.CurrentDef == 1200,
                    $"ok={ok} mpGy={opp.Graveyard.Contains(mp)} def={celtic.CurrentDef}");
            }
        }

        // ───────────────────────── review fixes (v141 fixer) ─────────────────────────

        static void ReviewFixes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            UpkeepCostsAreNotNegated(db, pDeck, aDeck, Check);
            DragonCaptureJarWindow(db, pDeck, aDeck, Check);
            SnatchStealAfterBrainwashingNegated(db, pDeck, aDeck, Check);
            FieldSpellAppliesOnlyOnceResolved(db, pDeck, aDeck, Check);
            NegatedBattleScarredStays(db, pDeck, aDeck, Check);
        }

        /// <summary>
        /// A negated card still pays its Standby upkeep: ygopro marks every maintenance cost
        /// EFFECT_FLAG_CANNOT_DISABLE (Messenger of Peace c44656491 e3, Spirit's Invitation c92394653 e3,
        /// Archfiend maintenance c35975813 e3). Its other Standby effects stay negated (Burning Land).
        /// </summary>
        static void UpkeepCostsAreNotNegated(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            foreach (var (id, family, cost, name) in new[]
                     {
                         (MessengerOfPeace, TurnNegationTrapsRuntime.ContinuousSpell, 100, "Messenger of Peace"),
                         (SpiritsInvitation, TurnNegationTrapsRuntime.ContinuousTrap, 500, "Spirit's Invitation")
                     })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                engine.Opponent.Hand.Clear();
                var card = PlaceFaceUpSpellTrap(engine, p, id, 0);
                TurnNegationTrapsRuntime.Register(engine, engine.Opponent, null, family);
                var lp = p.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.StandbyPhase);
                Check($"Upkeep: a negated {name} still pays its {cost} LP Standby cost and stays",
                    card.IsNegated && p.LifePoints == lp - cost && p.TryFindSpellTrap(card, out _),
                    $"neg={card.IsNegated} lp={lp}->{p.LifePoints} field={p.TryFindSpellTrap(card, out _)}");
            }

            {
                // A stored negation (Imperial Order / Jinzo shape) on an Archfiend: the maintenance is paid.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var bishop = PlaceMonster(engine, p, DarkbishopArchfiend, 0, BattlePosition.Attack, true);
                bishop.IsNegated = true;
                var lp = p.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.StandbyPhase);
                Check("Upkeep: a negated Archfiend still pays its 500 LP maintenance",
                    bishop.IsNegated && p.LifePoints == lp - 500 && p.TryFindMonster(bishop, out _),
                    $"lp={lp}->{p.LifePoints}");
            }

            foreach (var negate in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                PlaceFaceUpSpellTrap(engine, p, BurningLand, 0);
                if (negate)
                    TurnNegationTrapsRuntime.Register(engine, engine.Opponent, null,
                        TurnNegationTrapsRuntime.ContinuousSpell);
                var lp = p.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.StandbyPhase);
                Check(negate
                        ? "Upkeep gate: a negated Burning Land still deals no Standby damage (not an upkeep)"
                        : "Burning Land (control): the turn player takes 500 in the Standby Phase",
                    p.LifePoints == (negate ? lp : lp - 500), $"lp={lp}->{p.LifePoints}");
            }
        }

        /// <summary>
        /// Dragon Capture Jar is an untargeted Continuous Trap with a Type filter. Its activation now seats
        /// a Chain Link and opens the opponent's chain window, so Metal Detector can answer it.
        /// </summary>
        static void DragonCaptureJarWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Control: with nothing to answer it, the Jar resolves at once.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(engine, opp, DragonCaptureJar, 0);
                var ok = engine.TryActivateSpellTrap(opp, jar, fromHand: false);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check("Dragon Capture Jar (control): with no answer, Dragons go to Defense Position",
                    ok && jar.FaceUp && opp.TryFindSpellTrap(jar, out _) &&
                    bewd.Position == BattlePosition.Defense && engine.Chain.Count == 0,
                    $"ok={ok} pos={bewd.Position} chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // The opponent flips Dragon Capture Jar in your turn; Metal Detector answers it.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(engine, opp, DragonCaptureJar, 0);
                var md = PlaceSetTrap(engine, p, MetalDetector, 0);
                var ok = engine.TryActivateSpellTrap(opp, jar, fromHand: false);
                var offered = ChainWindowOffers(engine, p, md);
                var w = Window(engine);
                var answered = offered && Answer(engine, p, md);
                engine.NotifyPublic();
                engine.TryEnterBattlePhase(p);
                Check("Metal Detector vs Dragon Capture Jar: offered; the Jar resolves negated, the Dragon attacks",
                    ok && offered && answered && jar.IsNegated && opp.TryFindSpellTrap(jar, out _) &&
                    bewd.Position == BattlePosition.Attack && engine.CanAttack(p, bewd),
                    $"ok={ok} offered={offered} {w} pos={bewd.Position} canAtk={engine.CanAttack(p, bewd)}");
            }

            {
                // You flip Dragon Capture Jar; the AI answers with Metal Detector.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var oppBewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(engine, p, DragonCaptureJar, 0);
                var md = PlaceSetTrap(engine, opp, MetalDetector, 0);
                var ok = engine.TryActivateSpellTrap(p, jar, fromHand: false);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check("AI: chains Metal Detector to your Dragon Capture Jar",
                    ok && opp.Graveyard.Contains(md) && jar.IsNegated && oppBewd.Position == BattlePosition.Attack,
                    $"ok={ok} mdGy={opp.Graveyard.Contains(md)} neg={jar.IsNegated} pos={oppBewd.Position}");
            }

            {
                // Exile of the Wicked (untargeted Type-wide Normal Spell) now takes the same untargeted
                // chain path: it still destroys every Fiend on the field and nothing else.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                var exile = PutInHand(engine, p, ExileOfTheWicked);
                var ok = engine.TryActivateSpellTrap(p, exile, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Exile of the Wicked (untargeted chain path): destroys the Fiend only",
                    ok && opp.Graveyard.Contains(skull) && opp.TryFindMonster(celtic, out _) &&
                    p.Graveyard.Contains(exile) && engine.Chain.Count == 0,
                    $"ok={ok} skullGy={opp.Graveyard.Contains(skull)} celtic={opp.TryFindMonster(celtic, out _)} " +
                    $"chain={engine.Chain.Count}");
            }

            foreach (var answer in new[] { false, true })
            {
                // Level Limit - Area B (Continuous Spell) answered by Mystic Probe: its continuous
                // position change never applied, since its activation resolved negated.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var lla = PutInHand(engine, opp, LevelLimitAreaB);
                var mp = PlaceSetTrap(engine, p, MysticProbe, 0);
                var ok = engine.TryActivateSpellTrap(opp, lla, fromHand: true);
                var offered = ChainWindowOffers(engine, p, mp);
                var posDuring = bewd.Position;
                if (answer && offered) Answer(engine, p, mp);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check(answer
                        ? "Mystic Probe vs Level Limit - Area B: the Level 8 is never changed to Defense Position"
                        : "Level Limit - Area B (control): applies once its activation resolves",
                    ok && offered && posDuring == BattlePosition.Attack && lla.FaceUp &&
                    bewd.Position == (answer ? BattlePosition.Attack : BattlePosition.Defense),
                    $"ok={ok} offered={offered} during={posDuring} after={bewd.Position} neg={lla.IsNegated}");
            }

            {
                // Battle-Scarred (a targeted Continuous Trap on the compiled pick path) is a real Chain Link since
                // CompiledActivationChain: the AI's Metal Detector answers it, and Battle-Scarred resolves negated —
                // face-up, with no selected monster (ygopro's persistent procedure), and Metal Detector goes to the GY.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var bs = PlaceSetTrap(engine, p, BattleScarred, 0);
                var md = PlaceSetTrap(engine, opp, MetalDetector, 0);
                var ok = engine.TryActivateSpellTrap(p, bs, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Battle-Scarred vs the AI's Metal Detector: answered in its chain window; resolves face-up and " +
                      "negated with no selected monster",
                    ok && bs.FaceUp && p.TryFindSpellTrap(bs, out _) && bs.EquippedTo == null && bs.IsNegated &&
                    p.TryFindMonster(skull, out _) && opp.Graveyard.Contains(md) && !engine.Chain.HasLinks,
                    $"ok={ok} linked={bs.EquippedTo?.Name} neg={bs.IsNegated} onField={p.TryFindSpellTrap(bs, out _)} " +
                    $"mdGy={opp.Graveyard.Contains(md)} chain={engine.Chain.Count}");
            }
        }

        /// <summary>
        /// Remove Brainwashing already snapped a Snatch Steal monster back; negating it for the turn lets
        /// the still-equipped Snatch Steal take control again (ygopro: the Remove Brainwashing override
        /// ends, the Equip's control effect applies). When the negation ends, control returns.
        /// </summary>
        static void SnatchStealAfterBrainwashingNegated(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            CheckFn Check)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ReachMain(engine, engine.Player, 3);
            ClearBoard(engine);
            var p = engine.Player;
            var opp = engine.Opponent;
            p.Hand.Clear();
            opp.Hand.Clear();
            var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
            var rb = PlaceFaceUpSpellTrap(engine, opp, RemoveBrainwashing, 0);
            var snatch = PutInHand(engine, p, SnatchSteal);
            var ok = engine.TryActivateSpellTrap(p, snatch, fromHand: true);
            if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            engine.NotifyPublic();
            var before = opp.TryFindMonster(skull, out _) && snatch.EquippedTo == skull &&
                         p.TryFindSpellTrap(snatch, out _);
            Check("Snatch Steal vs face-up Remove Brainwashing (control): equipped, but the owner keeps control",
                ok && before, $"ok={ok} oppHas={opp.TryFindMonster(skull, out _)} eq={snatch.EquippedTo?.Name}");
            TurnNegationTrapsRuntime.Register(engine, p, null, TurnNegationTrapsRuntime.ContinuousTrap);
            engine.NotifyPublic();
            Check("Metal Detector: Remove Brainwashing negated — the equipped Snatch Steal takes control this turn",
                rb.IsNegated && p.TryFindMonster(skull, out _) && snatch.EquippedTo == skull,
                $"rbNeg={rb.IsNegated} pHas={p.TryFindMonster(skull, out _)}");
            EndTurn(engine);
            Check("Metal Detector: when the negation ends, Remove Brainwashing returns control to the owner",
                !rb.IsNegated && opp.TryFindMonster(skull, out _) && snatch.EquippedTo == skull,
                $"rbNeg={rb.IsNegated} oppHas={opp.TryFindMonster(skull, out _)} turn={engine.TurnNumber}");
        }

        /// <summary>
        /// A Field Spell's effects apply only once its activation resolves (ygopro STATUS_EFFECT_ENABLED):
        /// in its own chain window Yami gives no +200 and "Umi is on the field" is false (Tornado Wall
        /// cannot be chained to Umi's activation).
        /// </summary>
        static void FieldSpellAppliesOnlyOnceResolved(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            CheckFn Check)
        {
            {
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var yami = PutInHand(engine, opp, Yami);
                var ws = PlaceSetTrap(engine, p, WorldSuppression, 0);
                engine.TryActivateSpellTrap(opp, yami, fromHand: true);
                var offered = ChainWindowOffers(engine, p, ws);
                var during = skull.CurrentAtk;
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                Check("Field Spell window: Yami gives no +200 until its activation resolves, then it does",
                    offered && during == 2500 && skull.CurrentAtk == 2700 && !yami.IsNegated &&
                    !yami.ActivationUnresolvedOnChain,
                    $"offered={offered} during={during} after={skull.CurrentAtk} {Window(engine)}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var umi = PutInHand(engine, opp, Umi);
                var ws = PlaceSetTrap(engine, p, WorldSuppression, 0);
                var wall = PlaceSetTrap(engine, p, TornadoWall, 1);
                engine.TryActivateSpellTrap(opp, umi, fromHand: true);
                var offered = ChainWindowOffers(engine, p, ws);
                var wallOffered = engine.PendingResponse != null && engine.PendingResponse.LegalCards.Contains(wall);
                var umiDuring = FieldSpellEffects.UmiIsOnField(engine);
                var w = Window(engine);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Field Spell window: Umi is not yet 'on the field' — Tornado Wall is not offered",
                    offered && !wallOffered && !umiDuring && FieldSpellEffects.UmiIsOnField(engine) &&
                    opp.FieldSpellZone.Occupant == umi,
                    $"offered={offered} wall={wallOffered} umiDuring={umiDuring} {w}");
            }

            {
                // You activate Sogen; the AI holds Magic Jammer (+ a card to discard). Whether it negates
                // the activation or passes, the pending mark never outlives the chain.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var sogen = PutInHand(engine, p, Sogen);
                var jammer = PlaceSetTrap(engine, opp, MagicJammer, 0);
                PutInHand(engine, opp, PotOfGreed);
                var ok = engine.TryActivateSpellTrap(p, sogen, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.NotifyPublic();
                var jammed = opp.Graveyard.Contains(jammer);
                Check("Field Spell window: Magic Jammer negates the activation (Sogen to the GY) or passes (Sogen applies)",
                    ok && !sogen.ActivationUnresolvedOnChain && engine.Chain.Count == 0 &&
                    (jammed
                        ? p.Graveyard.Contains(sogen) && p.FieldSpellZone.Occupant == null && celtic.CurrentAtk == 1400
                        : p.FieldSpellZone.Occupant == sogen && celtic.CurrentAtk == 1600),
                    $"ok={ok} jammed={jammed} sogenGy={p.Graveyard.Contains(sogen)} atk={celtic.CurrentAtk} " +
                    $"pending={sogen.ActivationUnresolvedOnChain} chain={engine.Chain.Count}");
            }

            {
                // World Suppression answering the window leaves the Field Spell negated for the turn, and
                // the pending mark is gone.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var yami = PutInHand(engine, opp, Yami);
                var ws = PlaceSetTrap(engine, p, WorldSuppression, 0);
                engine.TryActivateSpellTrap(opp, yami, fromHand: true);
                var answered = ChainWindowOffers(engine, p, ws) && Answer(engine, p, ws);
                engine.NotifyPublic();
                Check("Field Spell window: answered by World Suppression — resolved, negated for the turn",
                    answered && !yami.ActivationUnresolvedOnChain && yami.NegatedByTurnFamily &&
                    skull.CurrentAtk == 2500 && engine.Chain.Count == 0,
                    $"answered={answered} pending={yami.ActivationUnresolvedOnChain} atk={skull.CurrentAtk}");
            }
        }

        /// <summary>
        /// Battle-Scarred is a persistent Continuous Trap, not an Equip Card (c94463200:
        /// aux.AddPersistentProcedure; "destroy this card" when its monster is destroyed is effect e3).
        /// While negated it stays when its Archfiend is destroyed; not negated, it goes with it.
        /// </summary>
        static void NegatedBattleScarredStays(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            foreach (var negate in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var bs = PlaceFaceUpSpellTrap(engine, p, BattleScarred, 0);
                bs.EquippedTo = skull;
                skull.Equips.Add(bs);
                if (negate)
                    TurnNegationTrapsRuntime.Register(engine, engine.Opponent, null,
                        TurnNegationTrapsRuntime.ContinuousTrap);
                engine.DestroyMonsterPublic(p, skull);
                var stayed = p.TryFindSpellTrap(bs, out _);
                Check(negate
                        ? "Metal Detector: negated Battle-Scarred stays when its Archfiend is destroyed"
                        : "Battle-Scarred (control): destroyed with its Archfiend",
                    p.Graveyard.Contains(skull) && stayed == negate,
                    $"skullGy={p.Graveyard.Contains(skull)} stayed={stayed}");
            }
        }
    }
}
