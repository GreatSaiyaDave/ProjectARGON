using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "TypeDeclaration" (v142): DNA Surgery, The Regulation of Tribe, Earthshaker — a Monster Type
    /// declared when the card is activated, a Type change every Type check honours, a Standby upkeep with a
    /// Tribute pick, and Attribute selections by both players while the effect resolves. Prohibition (declare a
    /// card name) stays unimplemented.
    /// </summary>
    public static class TypeDeclarationRegressionTests
    {
        const int DnaSurgery = 74701381;
        const int RegulationOfTribe = 296499;
        const int Earthshaker = 60866277;
        const int Prohibition = 43711255;

        const int TribeInfectingVirus = 33184167;
        const int ArrayOfRevealingLight = 69296555;
        const int HunterWith7Weapons = 1525329;
        const int DnaTransplant = 56769674;
        const int UnfriendlyAmazon = 65475294;

        const int InsectBarrier = 23615409;  // Continuous Spell: your opponent's Insects cannot declare an attack
        const int Sogen = 86318356;          // Field: Warrior / Beast-Warrior +200 ATK/DEF
        const int RoyalDecree = 51452091;    // Continuous Trap: negate all other Trap effects on the field
        const int MaskOfRestrict = 29549364; // Continuous Trap: neither player can Tribute
        const int LordOfD = 17985575;        // Dragons on the field cannot be targeted by card effects
        const int CallOfTheHaunted = 97077563;
        const int Mst = 5318639;             // Mystical Space Typhoon
        const int SolemnJudgment = 41420027; // Counter Trap: pay half your LP; negate a Summon or a Spell/Trap activation

        const int Bewd = 89631139;          // Dragon LIGHT 3000
        const int Celtic = 91152256;        // Warrior EARTH 1400
        const int SummonedSkull = 70781052; // Fiend DARK 2500
        const int SkullServant = 32274490;  // Zombie DARK 300
        const int MysticalElf = 15025844;   // Spellcaster LIGHT 800
        const int WingedDragon = 87796900;  // Dragon WIND 1400
        const int Sangan = 26202165;        // Fiend DARK 1000: sent from the field to the GY → search

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("TypeDeclaration: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            DnaSurgeryDuels(db, pDeck, aDeck, Check);
            RegulationDuels(db, pDeck, aDeck, Check);
            EarthshakerDuels(db, pDeck, aDeck, Check);
            AutopilotDrains(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static bool DeclareClause(EffectClause c) =>
            c != null && c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.DeclareMonsterType &&
            c.RequiresTargetChoice && c.ChoiceDoesNotTarget && !c.IsPsctTarget &&
            c.Zone == EffectZoneFilter.DeclaredMonsterTypes && c.MakesChainLink && c.StaysOnField;

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            {
                var def = db.Get(DnaSurgery);
                var prog = CardTextEffectCompiler.Compile(def);
                var cl = prog?.ClauseList ?? new List<EffectClause>();
                Check("DNA Surgery: FullyCompiled — Activate DeclareMonsterType + ContinuousWhileFaceUp AllFaceUpMonstersBecomeDeclaredType",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && cl.Count == 2 &&
                    DeclareClause(cl[0]) &&
                    cl[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                    cl[1].Action == EffectActionKind.AllFaceUpMonstersBecomeDeclaredType &&
                    cl[1].Side == EffectSide.Both && !cl[1].MakesChainLink,
                    string.Join(" | ", cl.Select(EffectVocabulary.Describe)));
                Check("DNA Surgery: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(RegulationOfTribe);
                var prog = CardTextEffectCompiler.Compile(def);
                var cl = prog?.ClauseList ?? new List<EffectClause>();
                Check("The Regulation of Tribe: FullyCompiled — declare, declared-Type attack lock (both sides), Standby upkeep",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && cl.Count == 3 &&
                    DeclareClause(cl[0]) &&
                    cl[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                    cl[1].Action == EffectActionKind.ContinuousCannotAttack && cl[1].RaceFilterIsDeclared &&
                    string.IsNullOrEmpty(cl[1].RaceFilter) && cl[1].Side == EffectSide.Both && cl[1].Amount == 0 &&
                    cl[2].Timing == EffectTiming.StandbyPhase &&
                    cl[2].Action == EffectActionKind.StandbyTributeOrDestroyThis && !cl[2].MakesChainLink &&
                    !cl[2].OpponentTurnOnly,
                    string.Join(" | ", cl.Select(EffectVocabulary.Describe)));
                Check("The Regulation of Tribe: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(Earthshaker);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Earthshaker: FullyCompiled — one untargeted Activate, needs 2 Attributes face-up",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo &&
                    !c.RequiresTargetChoice && !c.IsPsctTarget && c.MakesChainLink && !c.StaysOnField &&
                    c.ActivationCondition == TypeDeclarationRuntime.TwoAttributesFaceUp,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check("Earthshaker: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(Prohibition);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Prohibition stays fail-closed (no card-name declaration, no forbidden-card rules)",
                    (prog == null || !prog.FullyCompiled) &&
                    CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            foreach (var (a, want) in new[]
                     {
                         (EffectActionKind.DeclareMonsterType, EffectResolutionKind.TreatAsName),
                         // Every face-up monster's Type changes: a stat change, not a rename (the AI's Solemn
                         // Judgment skips TreatAsName activations as not worth half its LP).
                         (EffectActionKind.AllFaceUpMonstersBecomeDeclaredType, EffectResolutionKind.ModifyStats),
                         (EffectActionKind.StandbyTributeOrDestroyThis, EffectResolutionKind.Destroy),
                         (EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo, EffectResolutionKind.Destroy)
                     })
            {
                var kind = EffectVocabulary.ResolutionOf(new EffectClause { Action = a });
                Check($"{a} maps to the shared resolution {want}", kind == want, kind.ToString());
            }

            foreach (var id in new[] { DnaSurgery, RegulationOfTribe, Earthshaker })
            {
                var link = new ChainLink { Card = new CardInstance { CardId = id, Def = db.Get(id) } };
                Check($"{db.Get(id)?.name}: the AI's Solemn Judgment counts its activation as worth half its LP",
                    SummonNegationRuntime.AiWorthHalfLpNegation(link));
            }
        }

        static CompiledCardProgram CompileText(string text, string race = "Continuous") =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990142, name = "Near-miss Trap", type = "Trap Card", frameType = "trap", race = race, desc = text
            });

        static bool Has(CompiledCardProgram prog, EffectActionKind a) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == a);

        static bool FullyWith(CompiledCardProgram prog, EffectActionKind a) =>
            prog != null && prog.FullyCompiled && Has(prog, a);

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            {
                var tiv = CardTextEffectCompiler.Compile(db.Get(TribeInfectingVirus));
                var cl = tiv?.ClauseList ?? new List<EffectClause>();
                Check("Tribe-Infecting Virus: the old print compiles discard + declare + destroy face-up of that Type",
                    tiv != null && tiv.FullyCompiled && cl.Count == 2 &&
                    cl[0].Action == EffectActionKind.DeclareMonsterType && cl[0].RequiresDiscardCost &&
                    cl[0].DiscardCostAttribute == "*" && cl[0].Zone == EffectZoneFilter.DeclaredMonsterTypes &&
                    cl[1].Action == EffectActionKind.Destroy && cl[1].RaceFilterIsDeclared &&
                    cl[1].Side == EffectSide.Both && string.IsNullOrEmpty(cl[1].RaceFilter),
                    tiv == null ? "null" : string.Join(" | ", cl.Select(EffectVocabulary.Describe)));
            }

            foreach (var (id, why) in new[]
                     {
                         (HunterWith7Weapons, "declares a Type when Normal Summoned (monster effect)")
                     })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"Near miss: {db.Get(id)?.name} ({why}) gets no DeclareMonsterType",
                    !Has(prog, EffectActionKind.DeclareMonsterType),
                    prog == null ? "null" : string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe)));
            }

            // Array of Revealing Light (BattleRules batch, v161): its declaration compiles with its own summon-turn lock,
            // never with the Regulation's permanent lock.
            var array = CardTextEffectCompiler.Compile(db.Get(ArrayOfRevealingLight));
            Check("Array of Revealing Light declares a Type for the summon-turn lock only (not the Regulation lock)",
                array != null && array.FullyCompiled && Has(array, EffectActionKind.DeclareMonsterType) &&
                array.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ContinuousCannotAttack &&
                                             c.RaceFilterIsDeclared && c.RequiresSummonedOrFlippedThisTurn),
                array == null ? "null" : string.Join(" | ", array.ClauseList.Select(EffectVocabulary.Describe)));

            var transplant = CardTextEffectCompiler.Compile(db.Get(DnaTransplant));
            Check("Near miss: DNA Transplant (declares an Attribute) is not the Type template",
                !Has(transplant, EffectActionKind.DeclareMonsterType) &&
                !Has(transplant, EffectActionKind.AllFaceUpMonstersBecomeDeclaredType));
            var amazon = CardTextEffectCompiler.Compile(db.Get(UnfriendlyAmazon));
            // StandbyBurnsAndDice batch (v162): the Amazon's wording compiles to the same upkeep, but never Tributing
            // itself and with no way to decline; The Regulation of Tribe's upkeep keeps neither flag.
            Check("Near miss: The Unfriendly Amazon's upkeep is the mandatory, excluding-itself variant",
                amazon != null && amazon.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.StandbyTributeOrDestroyThis && c.TributeExceptThis &&
                    c.UpkeepTributeMandatory));

            Check("Near miss: DNA Surgery wording limited to your monsters is not compiled",
                !FullyWith(CompileText("Activate this card by declaring 1 Monster Type. All face-up monsters you " +
                                       "control become that Type."),
                    EffectActionKind.AllFaceUpMonstersBecomeDeclaredType));
            Check("Near miss: a Type change that lasts only until the End Phase is not compiled",
                !FullyWith(CompileText("Activate this card by declaring 1 Monster Type. All face-up monsters on the " +
                                       "field become that Type until the End Phase."),
                    EffectActionKind.AllFaceUpMonstersBecomeDeclaredType));
            Check("Near miss: a declared-Type lock limited to the Summon turn is not the Regulation lock",
                !FullyWith(CompileText("Declare 1 Type of monster. Monsters of the declared Type cannot attack " +
                                       "during the turn they are Summoned."),
                    EffectActionKind.DeclareMonsterType));
            Check("Near miss: Earthshaker wording that destroys only on your side is not compiled",
                !FullyWith(CompileText("Select 2 Monster Card Attributes. Your opponent then selects 1 of the 2 " +
                                       "Attributes and destroys all face-up monsters with that Attribute on your " +
                                       "side of the field.", race: "Normal"),
                    EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo));
            Check("Near miss: selecting 3 Attributes is not the Earthshaker template",
                !FullyWith(CompileText("Select 3 Monster Card Attributes. Your opponent then selects 1 of the 2 " +
                                       "Attributes and destroys all face-up monsters with that Attribute on the " +
                                       "field.", race: "Normal"),
                    EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo));
            Check("Near miss: an upkeep paying LP instead of a Tribute is not StandbyTributeOrDestroyThis",
                !Has(CompileText("Pay 500 Life Points during each of your Standby Phases. If you do not, this " +
                                 "card is destroyed."),
                    EffectActionKind.StandbyTributeOrDestroyThis));
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

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance Pending(DuelEngine engine, string name) =>
            engine.PendingActivation?.LegalTargets.FirstOrDefault(t =>
                t?.Def != null && string.Equals(t.Def.name, name, StringComparison.OrdinalIgnoreCase));

        static string PendingNames(DuelEngine engine) =>
            engine.PendingActivation == null
                ? "no pending"
                : $"kind={engine.PendingActivation.TargetKind} ctrl={engine.PendingActivation.Controller?.Name} " +
                  $"[{string.Join(",", engine.PendingActivation.LegalTargets.Select(t => t?.Name))}]";

        static List<string> Capture(DuelEngine engine)
        {
            var lines = new List<string>();
            engine.OnLog += s => lines.Add(s);
            return lines;
        }

        /// <summary>The Player activates a Set DNA Surgery / Regulation and declares <paramref name="type"/>.</summary>
        static bool ActivateAndDeclare(DuelEngine engine, DuelistState who, CardInstance card, string type)
        {
            if (!engine.TryActivateSpellTrap(who, card, fromHand: false)) return false;
            var token = Pending(engine, type);
            if (token == null || !engine.TrySelectEffectTarget(token)) return false;
            PassAll(engine);
            return !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0;
        }

        static bool Tribute(DuelEngine engine, CardInstance pick) =>
            engine.PendingActivation != null && engine.TrySelectEffectTarget(pick);

        // ───────────────────────── DNA Surgery ─────────────────────────

        static void DnaSurgeryDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The player declares the Type while activating; every face-up monster on the field becomes it.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var setCeltic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Defense, false);
                var handBewd = PutInHand(engine, p, Bewd);
                var gyBewd = engine.CreateCardInstance(Bewd);
                p.Graveyard.Add(gyBewd);
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 0);
                var log = Capture(engine);
                var activated = engine.TryActivateSpellTrap(p, dna, fromHand: false);
                var pa = engine.PendingActivation;
                var types = TypeDeclarationRuntime.MonsterTypesOf(engine);
                Check("DNA Surgery: activating it asks the player to declare a Monster Type (one token per Type)",
                    reached && activated && pa != null && pa.TargetKind == EffectTargetKind.DeclareMonsterType &&
                    pa.LegalTargets.Count == types.Length && types.Length >= 20 &&
                    pa.LegalTargets.All(TypeDeclarationRuntime.IsTypeToken) &&
                    Pending(engine, "Zombie") != null && Pending(engine, "Dragon") != null &&
                    pa.Prompt.Contains("declare 1 Monster Type"),
                    $"reached={reached} act={activated} {PendingNames(engine)}");
                Check("DNA Surgery: the Type tokens are not cards (never on the field, CardId 0)",
                    pa != null && pa.LegalTargets.All(t => t.CardId == 0 && engine.ControllerOf(t) == null));
                var zombie = Pending(engine, "Zombie");
                var picked = zombie != null && engine.TrySelectEffectTarget(zombie);
                var announced = log.Any(l => l.Contains("DNA Surgery: declares Zombie"));
                PassAll(engine);
                Check("DNA Surgery: the declaration is announced with the activation",
                    picked && announced, $"picked={picked} announced={announced}");
                Check("DNA Surgery: resolves and stays face-up with the declared Type",
                    !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0 && dna.FaceUp &&
                    p.TryFindSpellTrap(dna, out _) && TypeDeclarationRuntime.DeclaredTypeOf(dna) == "Zombie",
                    $"pending={PendingNames(engine)} chain={engine.Chain.Count} up={dna.FaceUp} " +
                    $"declared={TypeDeclarationRuntime.DeclaredTypeOf(dna)}");
                Check("DNA Surgery: both players' face-up monsters become the declared Type",
                    bewd.Race == "Zombie" && skull.Race == "Zombie", $"bewd={bewd.Race} skull={skull.Race}");
                Check("DNA Surgery: face-down monsters and cards in the hand or GY keep their printed Type",
                    setCeltic.Race == "Warrior" && handBewd.Race == "Dragon" && gyBewd.Race == "Dragon",
                    $"set={setCeltic.Race} hand={handBewd.Race} gy={gyBewd.Race}");
                setCeltic.FaceUp = true;
                var flipped = setCeltic.Race;
                var arrived = PlaceMonster(engine, p, WingedDragon, 1, BattlePosition.Attack, true);
                Check("DNA Surgery: a monster flipped face-up or arriving later is the declared Type too",
                    flipped == "Zombie" && arrived.Race == "Zombie", $"flipped={flipped} new={arrived.Race}");
                engine.SendCardToGrave(p, dna);
                Check("DNA Surgery: once it leaves the field every monster has its printed Type again",
                    bewd.Race == "Dragon" && skull.Race == "Fiend" && setCeltic.Race == "Warrior",
                    $"bewd={bewd.Race} skull={skull.Race}");
            }

            {
                // Type checks read the changed Type: Insect Barrier, a Field Spell aura, Lord of D.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                PlaceFaceUpSpellTrap(engine, p, InsectBarrier, 1);
                engine.NotifyPublic();
                var before = engine.ContinuousCannotAttackBlocks(opp, skull);
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 0);
                var ok = ActivateAndDeclare(engine, p, dna, "Insect");
                Check("DNA Surgery (Insect) + Insect Barrier: the opponent's monsters, now Insects, cannot attack",
                    ok && !before && engine.ContinuousCannotAttackBlocks(opp, skull) &&
                    engine.ContinuousCannotAttackBlocks(opp, celtic) && !engine.ContinuousCannotAttackBlocks(p, bewd),
                    $"ok={ok} before={before} skull={skull.Race}");

                var sogen = engine.CreateCardInstance(Sogen);
                sogen.FaceUp = true;
                opp.FieldSpellZone.Occupant = sogen;
                engine.NotifyPublic();
                var insectAtk = bewd.CurrentAtk;
                var dna2 = PlaceSetTrap(engine, p, DnaSurgery, 2);
                var ok2 = ActivateAndDeclare(engine, p, dna2, "Warrior");
                engine.NotifyPublic();
                Check("DNA Surgery (Warrior) + Sogen: the Field Spell powers up every face-up monster (latest DNA Surgery wins)",
                    ok2 && insectAtk == 3000 && bewd.Race == "Warrior" && bewd.CurrentAtk == 3200 &&
                    skull.CurrentAtk == 2700 && !engine.ContinuousCannotAttackBlocks(opp, skull),
                    $"ok2={ok2} insectAtk={insectAtk} bewd={bewd.Race}/{bewd.CurrentAtk} skull={skull.CurrentAtk}");
                engine.SendCardToGrave(p, dna2);
                engine.NotifyPublic();
                Check("DNA Surgery: when the latest one leaves, the earlier declaration applies again",
                    bewd.Race == "Insect" && bewd.CurrentAtk == 3000 && engine.ContinuousCannotAttackBlocks(opp, skull),
                    $"bewd={bewd.Race}/{bewd.CurrentAtk}");

                var lord = PlaceMonster(engine, opp, LordOfD, 2, BattlePosition.Attack, true);
                var dna3 = PlaceSetTrap(engine, p, DnaSurgery, 3);
                var before3 = engine.IsDragonTargetProtected(celtic);
                var ok3 = ActivateAndDeclare(engine, p, dna3, "Dragon");
                Check("DNA Surgery (Dragon) + Lord of D.: every face-up monster is a Dragon that cannot be targeted",
                    ok3 && !before3 && engine.IsDragonTargetProtected(celtic) && engine.IsDragonTargetProtected(skull),
                    $"ok3={ok3} before={before3} celtic={celtic.Race} lord={lord.Race}");
            }

            {
                // Negation: the declaration is made on activation, so a negated resolution still records it; the
                // Type change applies only while DNA Surgery is not negated.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var decree = PlaceFaceUpSpellTrap(engine, opp, RoyalDecree, 0);
                engine.NotifyPublic();
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 0);
                var ok = ActivateAndDeclare(engine, p, dna, "Zombie");
                engine.NotifyPublic();
                Check("DNA Surgery under Royal Decree: activates, the Type is declared, but no Type changes",
                    ok && dna.FaceUp && dna.IsNegated && TypeDeclarationRuntime.DeclaredTypeOf(dna) == "Zombie" &&
                    bewd.Race == "Dragon",
                    $"ok={ok} neg={dna.IsNegated} declared={TypeDeclarationRuntime.DeclaredTypeOf(dna)} bewd={bewd.Race}");
                engine.SendCardToGrave(opp, decree);
                engine.NotifyPublic();
                Check("DNA Surgery: once Royal Decree is gone the declared Type applies",
                    !dna.IsNegated && bewd.Race == "Zombie", $"neg={dna.IsNegated} bewd={bewd.Race}");
            }

            {
                // Destroyed in response (Mystical Space Typhoon chained to it): it is not on the field when its
                // activation resolves, so no monster changes Type.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var dna = PlaceSetTrap(engine, opp, DnaSurgery, 0);
                var mst = PutInHand(engine, p, Mst);
                var activated = engine.TryActivateSpellTrap(opp, dna, fromHand: false);
                var answered = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(dna);
                PassAll(engine);
                Check("DNA Surgery destroyed in response: its declared Type applies to nothing",
                    activated && answered && opp.Graveyard.Contains(dna) &&
                    TypeDeclarationRuntime.DeclaredTypeOf(dna) == null && celtic.Race == "Warrior" &&
                    !engine.IsAwaitingEffectTarget,
                    $"act={activated} mst={answered} dnaGy={opp.Graveyard.Contains(dna)} " +
                    $"declared={TypeDeclarationRuntime.DeclaredTypeOf(dna)} celtic={celtic.Race} {PendingNames(engine)}");
            }

            {
                // Cancel on the declaration undoes the activation.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 0);
                var activated = engine.TryActivateSpellTrap(p, dna, fromHand: false);
                var cancelled = engine.CancelEffectTargeting();
                Check("DNA Surgery: cancelling the declaration leaves it Set with no chain link",
                    activated && cancelled && !dna.FaceUp && p.TryFindSpellTrap(dna, out _) &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingEffectTarget &&
                    TypeDeclarationRuntime.DeclaredTypeOf(dna) == null,
                    $"act={activated} cancel={cancelled} up={dna.FaceUp} chain={engine.Chain.Count}");
            }

            {
                // The AI declares: it picks the Type of the strongest face-up monsters on the other side. The test
                // activates the AI's card directly: SimpleAi never activates a Set Continuous Trap (it only Sets
                // them, and AiAutoRespond's "use it or lose it" skips cards that stay on the field), so this checks
                // the AI's declaration and upkeep choices, not when the AI would play the card.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var dna = PlaceSetTrap(engine, opp, DnaSurgery, 0);
                var log = Capture(engine);
                var activated = engine.TryActivateSpellTrap(opp, dna, fromHand: false);
                var announced = log.Any(l => l.Contains("DNA Surgery: declares Dragon"));
                PassAll(engine);
                Check("DNA Surgery (AI): declares on activation and resolves with no player prompt",
                    activated && announced && !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0 &&
                    TypeDeclarationRuntime.DeclaredTypeOf(dna) == "Dragon" && celtic.Race == "Dragon" &&
                    bewd.Race == "Dragon",
                    $"act={activated} announced={announced} declared={TypeDeclarationRuntime.DeclaredTypeOf(dna)} " +
                    $"{PendingNames(engine)}");
            }

            {
                // The AI's Set Solemn Judgment answers DNA Surgery (declared Insect, with Insect Barrier face-up it
                // would stop every AI attacker): changing every monster's Type is worth half its LP.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                PlaceFaceUpSpellTrap(engine, p, InsectBarrier, 1);
                var solemn = PlaceSetTrap(engine, opp, SolemnJudgment, 0);
                opp.LifePoints = 8000;
                engine.NotifyPublic();
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 0);
                var activated = engine.TryActivateSpellTrap(p, dna, fromHand: false);
                var insect = Pending(engine, "Insect");
                var picked = insect != null && engine.TrySelectEffectTarget(insect);
                PassAll(engine);
                Check("DNA Surgery vs the AI's Solemn Judgment: the AI negates it (half its LP) — no Type changes",
                    activated && picked && opp.Graveyard.Contains(solemn) && p.Graveyard.Contains(dna) &&
                    opp.LifePoints == 4000 && skull.Race == "Fiend" && !engine.ContinuousCannotAttackBlocks(opp, skull) &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingEffectTarget,
                    $"act={activated} picked={picked} solemnGy={opp.Graveyard.Contains(solemn)} " +
                    $"dnaGy={p.Graveyard.Contains(dna)} lp={opp.LifePoints} skull={skull.Race} {PendingNames(engine)}");
            }
        }

        // ───────────────────────── The Regulation of Tribe ─────────────────────────

        static void RegulationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Declared Dragon: Dragons on either side cannot attack; other Types can. Then the upkeep.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var myDragon = PlaceMonster(engine, p, WingedDragon, 0, BattlePosition.Attack, true);
                var setElf = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Defense, false);
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var ok = ActivateAndDeclare(engine, p, reg, "Dragon");
                Check("The Regulation of Tribe: declares Dragon and stays face-up",
                    ok && reg.FaceUp && TypeDeclarationRuntime.DeclaredTypeOf(reg) == "Dragon",
                    $"ok={ok} declared={TypeDeclarationRuntime.DeclaredTypeOf(reg)}");
                Check("The Regulation of Tribe: Dragons on both sides cannot attack; a Warrior can",
                    engine.ContinuousCannotAttackBlocks(opp, bewd) && engine.ContinuousCannotAttackBlocks(p, myDragon) &&
                    !engine.ContinuousCannotAttackBlocks(opp, celtic));
                var battle = ReachOpponentBattle(engine);
                var bewdAttack = battle && engine.TryAttack(opp, bewd, null);
                DrainCombat(engine);
                var celticAttack = battle && engine.CanAttack(opp, celtic);
                Check("The Regulation of Tribe: in the opponent's Battle Phase their Dragon cannot declare an attack",
                    battle && !bewdAttack && celticAttack,
                    $"battle={battle} bewdAttack={bewdAttack} celticCan={celticAttack} phase={engine.Phase}");

                // The player's next Standby Phase: the upkeep asks for a Tribute and holds the Standby Phase.
                EndTurn(engine);
                var pa = engine.PendingActivation;
                Check("The Regulation of Tribe: in its controller's Standby Phase the upkeep asks for a Tribute",
                    engine.TurnPlayer == p && engine.Phase == DuelPhase.Standby && pa != null &&
                    pa.AwaitingUpkeepTribute && pa.Controller == p &&
                    pa.TargetKind == EffectTargetKind.TributeMonsterYouControl &&
                    pa.LegalTargets.Contains(myDragon) && pa.LegalTargets.Contains(setElf) &&
                    !pa.LegalTargets.Contains(bewd) && pa.Prompt.Contains("Tribute"),
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} {PendingNames(engine)}");
                var tributed = Tribute(engine, setElf);
                Check("The Regulation of Tribe: Tributing a face-down monster keeps the card; the turn goes on to Main Phase 1",
                    tributed && p.Graveyard.Contains(setElf) && reg.FaceUp && p.TryFindSpellTrap(reg, out _) &&
                    engine.Phase == DuelPhase.Main1 && !engine.IsAwaitingEffectTarget,
                    $"tributed={tributed} phase={engine.Phase} regOnField={p.TryFindSpellTrap(reg, out _)}");

                // Next Standby: Cancel = no Tribute, the card is destroyed.
                EndTurn(engine);
                EndTurn(engine);
                var second = engine.PendingActivation?.AwaitingUpkeepTribute == true;
                var cancelled = engine.CancelEffectTargeting();
                Check("The Regulation of Tribe: declining the Tribute destroys it; the Standby Phase ends",
                    second && cancelled && p.Graveyard.Contains(reg) && !p.TryFindSpellTrap(reg, out _) &&
                    p.TryFindMonster(myDragon, out _) && engine.Phase == DuelPhase.Main1,
                    $"second={second} cancel={cancelled} phase={engine.Phase}");
                Check("The Regulation of Tribe: gone, Dragons can attack again",
                    !engine.ContinuousCannotAttackBlocks(p, myDragon));
            }

            {
                // No monster to Tribute: destroyed at once, no prompt.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var ok = ActivateAndDeclare(engine, p, reg, "Dragon");
                EndTurn(engine);
                EndTurn(engine);
                Check("The Regulation of Tribe: with no monster to Tribute it is destroyed in the Standby Phase",
                    ok && engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 && p.Graveyard.Contains(reg) &&
                    !engine.IsAwaitingEffectTarget,
                    $"ok={ok} phase={engine.Phase} {PendingNames(engine)}");
            }

            {
                // Negated by Royal Decree: the lock stops, the upkeep does not (EFFECT_FLAG_CANNOT_DISABLE).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var elf = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Defense, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var ok = ActivateAndDeclare(engine, p, reg, "Dragon");
                var locked = engine.ContinuousCannotAttackBlocks(opp, bewd);
                PlaceFaceUpSpellTrap(engine, opp, RoyalDecree, 0);
                engine.NotifyPublic();
                Check("The Regulation of Tribe under Royal Decree: the attack lock is negated",
                    ok && locked && reg.IsNegated && !engine.ContinuousCannotAttackBlocks(opp, bewd),
                    $"ok={ok} locked={locked} neg={reg.IsNegated}");
                EndTurn(engine);
                EndTurn(engine);
                var pa = engine.PendingActivation;
                Check("The Regulation of Tribe under Royal Decree: the Standby upkeep is still owed",
                    engine.TurnPlayer == p && engine.Phase == DuelPhase.Standby && pa != null &&
                    pa.AwaitingUpkeepTribute && pa.LegalTargets.Contains(elf),
                    $"phase={engine.Phase} {PendingNames(engine)}");
                var paid = Tribute(engine, elf);
                Check("The Regulation of Tribe under Royal Decree: paying the upkeep keeps it on the field",
                    paid && p.Graveyard.Contains(elf) && p.TryFindSpellTrap(reg, out _) &&
                    engine.Phase == DuelPhase.Main1,
                    $"paid={paid} phase={engine.Phase}");
            }

            {
                // Mask of Restrict: nobody can Tribute, so the upkeep destroys it.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var elf = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Defense, true);
                var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var ok = ActivateAndDeclare(engine, p, reg, "Dragon");
                PlaceFaceUpSpellTrap(engine, opp, MaskOfRestrict, 0);
                engine.NotifyPublic();
                EndTurn(engine);
                EndTurn(engine);
                Check("The Regulation of Tribe + Mask of Restrict: no Tribute is possible, so it is destroyed",
                    ok && engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 && p.Graveyard.Contains(reg) &&
                    p.TryFindMonster(elf, out _) && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} phase={engine.Phase} {PendingNames(engine)}");
            }

            {
                // AI controller: declares the Type of the player's strongest monster; pays the upkeep with its
                // weakest monster while the lock is worth more. (Activated directly — SimpleAi does not activate a
                // Set Continuous Trap; see the DNA Surgery AI check.)
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, opp, SkullServant, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                var reg = PlaceSetTrap(engine, opp, RegulationOfTribe, 0);
                var activated = engine.TryActivateSpellTrap(opp, reg, fromHand: false);
                PassAll(engine);
                Check("The Regulation of Tribe (AI): declares Dragon — the player's Blue-Eyes cannot attack",
                    activated && TypeDeclarationRuntime.DeclaredTypeOf(reg) == "Dragon" &&
                    engine.ContinuousCannotAttackBlocks(p, bewd) && !engine.IsAwaitingEffectTarget,
                    $"act={activated} declared={TypeDeclarationRuntime.DeclaredTypeOf(reg)} {PendingNames(engine)}");
                EndTurn(engine);
                Check("The Regulation of Tribe (AI): its upkeep Tributes its weakest monster and keeps the card",
                    engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1 && opp.Graveyard.Contains(servant) &&
                    opp.TryFindMonster(celtic, out _) && opp.TryFindSpellTrap(reg, out _) &&
                    !engine.IsAwaitingEffectTarget,
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} servantGy={opp.Graveyard.Contains(servant)}");
                engine.SendCardToGrave(p, bewd);
                EndTurn(engine);
                EndTurn(engine);
                Check("The Regulation of Tribe (AI): with nothing left to lock it lets the card be destroyed",
                    engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1 && opp.Graveyard.Contains(reg) &&
                    opp.TryFindMonster(celtic, out _),
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} regGy={opp.Graveyard.Contains(reg)}");
            }

            {
                // Two copies, one monster: each upkeep is decided before any is paid, so one monster cannot pay
                // for both; the Tribute's own trigger (Sangan) opens after the upkeep, and the turn goes on.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var sangan = PlaceMonster(engine, p, Sangan, 0, BattlePosition.Attack, true);
                var reg1 = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var reg2 = PlaceSetTrap(engine, p, RegulationOfTribe, 1);
                var ok = ActivateAndDeclare(engine, p, reg1, "Dragon") && ActivateAndDeclare(engine, p, reg2, "Fiend");
                EndTurn(engine);
                p.Deck.Clear();
                for (var i = 0; i < 4; i++) p.Deck.Add(Celtic);
                EndTurn(engine);
                var first = engine.PendingActivation;
                var firstOk = first != null && first.AwaitingUpkeepTribute && first.LegalTargets.Contains(sangan);
                var paid = firstOk && engine.TrySelectEffectTarget(sangan);
                var search = engine.PendingActivation;
                var bothDecided = paid && p.Graveyard.Contains(sangan) &&
                                  (p.TryFindSpellTrap(reg1, out _) ^ p.TryFindSpellTrap(reg2, out _));
                Check("The Regulation of Tribe x2, one monster: it pays for one copy only; the other is destroyed",
                    ok && firstOk && bothDecided && engine.Phase == DuelPhase.Main1,
                    $"ok={ok} firstOk={firstOk} paid={paid} reg1={p.TryFindSpellTrap(reg1, out _)} " +
                    $"reg2={p.TryFindSpellTrap(reg2, out _)} phase={engine.Phase} {PendingNames(engine)}");
                var searchPick = search?.LegalTargets.FirstOrDefault(t => t.CardId == Celtic);
                var searched = search != null && search.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq &&
                               searchPick != null && engine.TrySelectEffectTarget(searchPick);
                Check("The Regulation of Tribe: a Tributed Sangan's search opens after the upkeep and resolves",
                    searched && p.Hand.Exists(c => c.CardId == Celtic) && !engine.IsAwaitingEffectTarget &&
                    engine.Phase == DuelPhase.Main1,
                    $"search={search?.TargetKind} searched={searched} {PendingNames(engine)}");
            }

            {
                // Destroyed in response to its own activation (the opponent chains Mystical Space Typhoon): the card
                // is in the GY, so it owes no upkeep. Human vs human, so its controller would be asked to Tribute.
                var engine = Fresh(db, pDeck, aDeck);
                engine.HumanVsHuman = true;
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var mst = PlaceSetTrap(engine, opp, Mst, 0);
                var activated = engine.TryActivateSpellTrap(p, reg, fromHand: false);
                var dragon = Pending(engine, "Dragon");
                var picked = dragon != null && engine.TrySelectEffectTarget(dragon);
                var window = engine.PendingResponse;
                var answered = window != null && window.Responder == opp &&
                               engine.TryActivateSpellTrap(opp, mst, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(reg);
                PassAll(engine);
                var destroyed = p.Graveyard.Contains(reg) && TypeDeclarationRuntime.DeclaredTypeOf(reg) == null &&
                                engine.Chain.Count == 0;
                var log = Capture(engine);
                EndTurn(engine);
                EndTurn(engine);
                var pa = engine.PendingActivation;
                var asked = pa != null && pa.AwaitingUpkeepTribute;
                var upkeepLogged = log.Any(l => l.Contains("The Regulation of Tribe") &&
                                                (l.Contains("Tribute") || l.Contains("is destroyed")));
                Check("The Regulation of Tribe destroyed in response: no Tribute is asked in its controller's next Standby Phase",
                    activated && picked && answered && destroyed && engine.TurnPlayer == p &&
                    engine.Phase == DuelPhase.Main1 && !asked && !upkeepLogged && p.TryFindMonster(celtic, out _) &&
                    p.Graveyard.Count(c => c == reg) == 1,
                    $"act={activated} picked={picked} answered={answered} destroyed={destroyed} " +
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} asked={asked} logged={upkeepLogged} " +
                    $"celtic={p.TryFindMonster(celtic, out _)} gyCopies={p.Graveyard.Count(c => c == reg)} " +
                    $"{PendingNames(engine)}");
                if (asked) engine.CancelEffectTargeting();
            }

            {
                // The same for the AI's copy: destroyed by the player's MST in response, it neither Tributes nor is
                // "destroyed" again in the AI's next Standby Phase.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, opp, SkullServant, 0, BattlePosition.Defense, true);
                var reg = PlaceSetTrap(engine, opp, RegulationOfTribe, 0);
                var mst = PutInHand(engine, p, Mst);
                var activated = engine.TryActivateSpellTrap(opp, reg, fromHand: false);
                var answered = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(reg);
                PassAll(engine);
                var destroyed = opp.Graveyard.Contains(reg) && TypeDeclarationRuntime.DeclaredTypeOf(reg) == null;
                var log = Capture(engine);
                EndTurn(engine);
                var upkeepLogged = log.Any(l => l.Contains("The Regulation of Tribe") &&
                                                (l.Contains("Tribute") || l.Contains("is destroyed")));
                Check("The Regulation of Tribe (AI) destroyed in response: no upkeep in the AI's next Standby Phase",
                    activated && answered && destroyed && engine.TurnPlayer == opp && !upkeepLogged &&
                    opp.TryFindMonster(servant, out _) && opp.Graveyard.Count(c => c == reg) == 1,
                    $"act={activated} mst={answered} destroyed={destroyed} turn={engine.TurnPlayer?.Name} " +
                    $"logged={upkeepLogged} gyCopies={opp.Graveyard.Count(c => c == reg)} " +
                    $"lines={string.Join(" / ", log.Where(l => l.Contains("Regulation")))}");
            }

            {
                // DNA Surgery + The Regulation of Tribe: every face-up monster is a Dragon, so none can attack.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 1);
                var ok1 = ActivateAndDeclare(engine, p, reg, "Dragon");
                var before = engine.ContinuousCannotAttackBlocks(p, celtic);
                var ok2 = ActivateAndDeclare(engine, p, dna, "Dragon");
                Check("DNA Surgery (Dragon) + The Regulation of Tribe (Dragon): no face-up monster can attack",
                    ok1 && ok2 && !before && engine.ContinuousCannotAttackBlocks(p, celtic) &&
                    engine.ContinuousCannotAttackBlocks(opp, skull),
                    $"ok1={ok1} ok2={ok2} before={before} celtic={celtic.Race}");
            }
        }

        // ───────────────────────── Earthshaker ─────────────────────────

        static void EarthshakerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Fewer than 2 Attributes face-up: it cannot be activated (face-down monsters do not count).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, SkullServant, 0, BattlePosition.Defense, false);
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var one = engine.CanActivateSpellTrap(p, es, fromHand: false);
                PlaceMonster(engine, opp, Bewd, 1, BattlePosition.Attack, true);
                var two = engine.CanActivateSpellTrap(p, es, fromHand: false);
                Check("Earthshaker: needs face-up monsters with 2 different Attributes (face-down ones do not count)",
                    !one && two, $"oneAttr={one} twoAttrs={two}");
            }

            {
                // The player selects 2 of 3 Attributes; the AI keeps the one that costs the player most.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);              // LIGHT
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);          // EARTH
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);  // DARK
                var setElf = PlaceMonster(engine, opp, MysticalElf, 1, BattlePosition.Defense, false); // LIGHT, set
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var activated = engine.TryActivateSpellTrap(p, es, fromHand: false);
                PassAll(engine);
                var pa = engine.PendingActivation;
                Check("Earthshaker: at resolution the player selects from the Attributes on the field",
                    activated && pa != null && pa.AwaitingDeclaration && pa.Controller == p &&
                    pa.TargetKind == EffectTargetKind.DeclareAttribute && pa.HoldsChainResolution &&
                    pa.LegalTargets.Count == 3 && pa.LegalTargets.All(TypeDeclarationRuntime.IsAttributeToken) &&
                    Pending(engine, "LIGHT") != null && Pending(engine, "DARK") != null &&
                    Pending(engine, "EARTH") != null,
                    $"act={activated} {PendingNames(engine)}");
                var cancel = engine.CancelEffectTargeting();
                Check("Earthshaker: the selection is mandatory (Cancel keeps waiting)",
                    !cancel && engine.PendingActivation == pa);
                var first = engine.TrySelectEffectTarget(Pending(engine, "LIGHT"));
                var afterFirst = engine.PendingActivation?.LegalTargets.Count ?? -1;
                var second = engine.TrySelectEffectTarget(Pending(engine, "DARK"));
                Check("Earthshaker: after 2 selections the AI opponent picks LIGHT (the player's Blue-Eyes) — every face-up LIGHT monster is destroyed",
                    first && afterFirst == 2 && second && p.Graveyard.Contains(bewd) &&
                    opp.TryFindMonster(skull, out _) && p.TryFindMonster(celtic, out _) &&
                    opp.TryFindMonster(setElf, out _),
                    $"first={first} left={afterFirst} second={second} bewdGy={p.Graveyard.Contains(bewd)} " +
                    $"skull={opp.TryFindMonster(skull, out _)}");
                Check("Earthshaker: resolved — the chain is finished, the Trap is in the GY",
                    !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0 && !engine.IsResolvingChain &&
                    p.Graveyard.Contains(es),
                    $"pending={PendingNames(engine)} chain={engine.Chain.Count} resolving={engine.IsResolvingChain}");
            }

            {
                // Only 2 Attributes on the field: both are selected; the AI picks at once.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);          // EARTH
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);  // DARK
                var servant = PlaceMonster(engine, opp, SkullServant, 1, BattlePosition.Attack, true); // DARK
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var activated = engine.TryActivateSpellTrap(p, es, fromHand: false);
                PassAll(engine);
                Check("Earthshaker: with exactly 2 Attributes there is nothing to select; the AI keeps EARTH",
                    activated && !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(celtic) &&
                    opp.TryFindMonster(skull, out _) && opp.TryFindMonster(servant, out _) && engine.Chain.Count == 0,
                    $"act={activated} {PendingNames(engine)} celticGy={p.Graveyard.Contains(celtic)}");
            }

            {
                // The AI activates; the player selects 1 of the AI's 2 Attributes.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);             // LIGHT 3000
                var wind = PlaceMonster(engine, p, WingedDragon, 1, BattlePosition.Attack, true);      // WIND 1400
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);        // EARTH 1400
                var es = PlaceSetTrap(engine, opp, Earthshaker, 0);
                var activated = engine.TryActivateSpellTrap(opp, es, fromHand: false);
                PassAll(engine);
                var pa = engine.PendingActivation;
                var offered = pa?.LegalTargets.Select(t => t.Def.attribute).OrderBy(a => a).ToList() ??
                              new List<string>();
                Check("Earthshaker (AI): selects LIGHT and WIND (the player's monsters); the player picks 1 of them",
                    activated && pa != null && pa.AwaitingDeclaration && pa.Controller == p &&
                    pa.HoldsChainResolution && offered.SequenceEqual(new[] { "LIGHT", "WIND" }),
                    $"act={activated} {PendingNames(engine)}");
                var windToken = Pending(engine, "WIND");
                var picked = windToken != null && engine.TrySelectEffectTarget(windToken);
                Check("Earthshaker (AI): the player's pick decides — the WIND monster is destroyed, the LIGHT one stays",
                    picked && p.Graveyard.Contains(wind) && p.TryFindMonster(bewd, out _) &&
                    opp.TryFindMonster(celtic, out _) && !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0 &&
                    opp.Graveyard.Contains(es),
                    $"picked={picked} windGy={p.Graveyard.Contains(wind)} chain={engine.Chain.Count}");
            }

            {
                // Chain Link 2: the player's selection holds the chain, so Chain Link 1 resolves after it.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);   // LIGHT
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);            // EARTH
                PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);   // DARK
                var servant = engine.CreateCardInstance(SkullServant);                      // DARK, in the GY
                opp.Graveyard.Add(servant);
                var coth = PlaceSetTrap(engine, opp, CallOfTheHaunted, 0);
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var cothOk = engine.TryActivateSpellTrap(opp, coth, fromHand: false);
                var offeredEs = engine.PendingResponse != null && engine.PendingResponse.LegalCards.Contains(es);
                var esOk = engine.TryActivateSpellTrap(p, es, fromHand: false);
                PassAll(engine);
                var held = engine.PendingActivation?.AwaitingDeclaration == true && engine.Chain.Count == 1 &&
                           opp.Graveyard.Contains(servant);
                Check("Earthshaker as Chain Link 2: its selection holds Chain Link 1 (Call of the Haunted has not resolved)",
                    cothOk && offeredEs && esOk && held,
                    $"coth={cothOk} offered={offeredEs} es={esOk} chain={engine.Chain.Count} " +
                    $"servantGy={opp.Graveyard.Contains(servant)} {PendingNames(engine)}");
                var light = Pending(engine, "LIGHT");
                if (light != null) engine.TrySelectEffectTarget(light);
                var dark = Pending(engine, "DARK");
                if (dark != null) engine.TrySelectEffectTarget(dark);
                PassAll(engine);
                Check("Earthshaker as Chain Link 2: LIGHT is destroyed first, then Call of the Haunted revives its DARK target",
                    p.Graveyard.Contains(bewd) && opp.TryFindMonster(servant, out _) && coth.FaceUp &&
                    !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0 && !engine.IsResolvingChain,
                    $"bewdGy={p.Graveyard.Contains(bewd)} servant={opp.TryFindMonster(servant, out _)} " +
                    $"chain={engine.Chain.Count} resolving={engine.IsResolvingChain} {PendingNames(engine)}");
            }

            {
                // A destroyed Sangan's search opens after the selections; the chain still finishes.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                p.Deck.Clear();
                for (var i = 0; i < 3; i++) p.Deck.Add(Celtic);
                var sangan = PlaceMonster(engine, p, Sangan, 0, BattlePosition.Attack, true);      // DARK
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);        // LIGHT
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);    // EARTH
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var activated = engine.TryActivateSpellTrap(p, es, fromHand: false);
                PassAll(engine);
                var dark = Pending(engine, "DARK");
                var earth = Pending(engine, "EARTH");
                if (dark != null) engine.TrySelectEffectTarget(dark);
                if (earth != null) engine.TrySelectEffectTarget(earth);
                var search = engine.PendingActivation;
                Check("Earthshaker: the AI keeps DARK (the player's Sangan) — Sangan's search opens and the chain is finished",
                    activated && p.Graveyard.Contains(sangan) && opp.TryFindMonster(bewd, out _) &&
                    opp.TryFindMonster(celtic, out _) && search != null &&
                    search.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq && engine.Chain.Count == 0 &&
                    !engine.IsResolvingChain,
                    $"act={activated} sanganGy={p.Graveyard.Contains(sangan)} chain={engine.Chain.Count} " +
                    $"resolving={engine.IsResolvingChain} {PendingNames(engine)}");
                var pick = search?.LegalTargets.FirstOrDefault(t => t.CardId == Celtic);
                var searched = pick != null && engine.TrySelectEffectTarget(pick);
                Check("Earthshaker: Sangan's search then resolves normally",
                    searched && p.Hand.Exists(c => c.CardId == Celtic) && !engine.IsAwaitingEffectTarget);
            }

            {
                // Royal Decree negates Earthshaker's effect: nothing is selected or destroyed.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                PlaceFaceUpSpellTrap(engine, opp, RoyalDecree, 0);
                engine.NotifyPublic();
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var activated = engine.TryActivateSpellTrap(p, es, fromHand: false);
                PassAll(engine);
                Check("Earthshaker under Royal Decree: its effect is negated — no selection, nothing destroyed",
                    activated && !engine.IsAwaitingEffectTarget && p.TryFindMonster(bewd, out _) &&
                    opp.TryFindMonster(skull, out _) && opp.TryFindMonster(celtic, out _) && p.Graveyard.Contains(es),
                    $"act={activated} {PendingNames(engine)}");
            }

            {
                // "Use it or lose it": the player's MST targets the AI's Set Earthshaker, but every face-up Attribute
                // is the AI's own (EARTH, DARK), so chaining it would only destroy the AI's monsters. It lets it go.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);        // EARTH
                var skull = PlaceMonster(engine, opp, SummonedSkull, 1, BattlePosition.Attack, true);  // DARK
                var es = PlaceSetTrap(engine, opp, Earthshaker, 0);
                var mst = PutInHand(engine, p, Mst);
                var cast = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(es);
                PassAll(engine);
                Check("Earthshaker (AI): not chained to an MST when only the AI's own monsters would be destroyed",
                    cast && opp.Graveyard.Contains(es) && opp.TryFindMonster(celtic, out _) &&
                    opp.TryFindMonster(skull, out _) && engine.Chain.Count == 0 && !engine.IsAwaitingEffectTarget,
                    $"cast={cast} esGy={opp.Graveyard.Contains(es)} celtic={opp.TryFindMonster(celtic, out _)} " +
                    $"skull={opp.TryFindMonster(skull, out _)} chain={engine.Chain.Count} {PendingNames(engine)}");
            }

            {
                // Worth it: the player's LIGHT and DARK monsters outweigh the AI's EARTH one, so the AI chains its
                // threatened Earthshaker, selects LIGHT and DARK, and the player picks which one goes.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);              // LIGHT
                var skull = PlaceMonster(engine, p, SummonedSkull, 1, BattlePosition.Attack, true);    // DARK
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);        // EARTH
                var es = PlaceSetTrap(engine, opp, Earthshaker, 0);
                var mst = PutInHand(engine, p, Mst);
                var cast = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(es);
                PassAll(engine);
                var pa = engine.PendingActivation;
                var offered = pa?.LegalTargets.Select(t => t.Def.attribute).OrderBy(a => a).ToList() ??
                              new List<string>();
                var chained = pa != null && pa.AwaitingDeclaration && pa.Controller == p &&
                              offered.SequenceEqual(new[] { "DARK", "LIGHT" });
                var dark = Pending(engine, "DARK");
                var picked = chained && dark != null && engine.TrySelectEffectTarget(dark);
                PassAll(engine);
                Check("Earthshaker (AI): chained to the MST when the player's monsters are worth it; the MST then has no target",
                    cast && chained && picked && p.Graveyard.Contains(skull) && p.TryFindMonster(bewd, out _) &&
                    opp.TryFindMonster(celtic, out _) && opp.Graveyard.Contains(es) && p.Graveyard.Contains(mst) &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingEffectTarget,
                    $"cast={cast} chained={chained} offered={string.Join(",", offered)} picked={picked} " +
                    $"skullGy={p.Graveyard.Contains(skull)} esGy={opp.Graveyard.Contains(es)} chain={engine.Chain.Count} " +
                    $"{PendingNames(engine)}");
            }

            {
                // Human vs human: the opponent's selection is theirs to make.
                var engine = Fresh(db, pDeck, aDeck);
                engine.HumanVsHuman = true;
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var es = PlaceSetTrap(engine, p, Earthshaker, 0);
                var activated = engine.TryActivateSpellTrap(p, es, fromHand: false);
                PassAll(engine);
                var pa = engine.PendingActivation;
                var theirs = pa != null && pa.AwaitingDeclaration && pa.Controller == opp && pa.LegalTargets.Count == 2;
                var darkToken = Pending(engine, "DARK");
                var picked = theirs && darkToken != null && engine.TrySelectEffectTarget(darkToken);
                Check("Earthshaker (human vs human): the opponent picks 1 of the 2 Attributes themselves",
                    activated && theirs && picked && opp.Graveyard.Contains(skull) && p.TryFindMonster(bewd, out _) &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingEffectTarget,
                    $"act={activated} theirs={theirs} picked={picked} {PendingNames(engine)}");
            }
        }

        // ───────────────────────── autopilot ─────────────────────────

        static void AutopilotDrains(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // The stress autopilot answers the declaration, the Attribute selections and the upkeep without a lock.
            var engine = Fresh(db, pDeck, aDeck);
            ReachMain(engine, engine.Player, 3);
            ClearBoard(engine);
            var p = engine.Player;
            var opp = engine.Opponent;
            p.Hand.Clear();
            opp.Hand.Clear();
            PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
            PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
            PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
            PlaceMonster(engine, opp, MysticalElf, 1, BattlePosition.Attack, true);
            var reg = PlaceSetTrap(engine, p, RegulationOfTribe, 0);
            var es = PlaceSetTrap(engine, p, Earthshaker, 1);
            engine.TryActivateSpellTrap(p, reg, fromHand: false);
            DuelStressAgent.DrainWindows(engine);
            var regOk = TypeDeclarationRuntime.DeclaredTypeOf(reg) != null && !engine.IsAwaitingEffectTarget;
            engine.TryActivateSpellTrap(p, es, fromHand: false);
            DuelStressAgent.DrainWindows(engine);
            var esOk = p.Graveyard.Contains(es) && !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0;
            EndTurn(engine);
            EndTurn(engine);
            DuelStressAgent.DrainWindows(engine);
            Check("Autopilot: declaration, Earthshaker selections and the Standby upkeep never soft-lock",
                regOk && esOk && engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 &&
                !engine.IsAwaitingEffectTarget,
                $"reg={regOk} es={esOk} turn={engine.TurnPlayer?.Name} phase={engine.Phase} {PendingNames(engine)}");
        }
    }
}
