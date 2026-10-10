using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "EquipFamily": Equip declarations (Dragonic Attack's Type change, Scroll of Bewitchment's declared
    /// Attribute) and Equip moves (Collected Power over the shared equip limit
    /// <see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/>).
    /// </summary>
    public static class EquipFamilyRegressionTests
    {
        const int DragonicAttack = 32437102;
        const int ScrollOfBewitchment = 10352095;
        const int CollectedPower = 7565547;

        const int DnaSurgery = 74701381;
        const int Earthshaker = 60866277;
        const int LegendarySword = 61854111;    // Equip only to a Warrior: +300/+300
        const int AxeOfDespair = 40619825;      // any monster: +1000 ATK
        const int SwordOfDarkDestruction = 37120512; // DARK only: +400 ATK / -200 DEF
        const int SnatchSteal = 45986603;
        const int RingOfMagnetism = 20436034;   // its controller's monster only
        const int Megamorph = 22046459;         // any monster, no leave-field effect

        const int DarkNecrofear = 31829185;
        const int Mst = 5318639;
        const int HarpiesFeatherDuster = 18144507;
        const int FeralImp = 41392891;      // Fiend DARK 1300/1400
        const int GarudaTheWindSpirit = 12800777; // opponent's End Phase: change a face-up monster's position

        const int Celtic = 91152256;        // Warrior EARTH 1400/1200
        const int Gaia = 6368038;           // Warrior EARTH 2300/2100
        const int SummonedSkull = 70781052; // Fiend DARK 2500/1200
        const int Bewd = 89631139;          // Dragon LIGHT 3000/2500

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("EquipFamily: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            DragonicAttackDuels(db, pDeck, aDeck, Check);
            ScrollDuels(db, pDeck, aDeck, Check);
            CollectedPowerDuels(db, pDeck, aDeck, Check);
            DarkNecrofearDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            {
                var def = db.Get(DragonicAttack);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Dragonic Attack: one Activate equip — a Warrior on either field, becomes Dragon, +500/+500",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.EquipThisToTarget && c.Zone == EffectZoneFilter.FieldAnyMonster &&
                    c.RaceFilter == "Warrior" && c.EquipChangesRaceTo == "Dragon" && c.EquipAtkBonus == 500 &&
                    c.EquipDefBonus == 500 && c.RequiresTargetChoice && c.IsPsctTarget && c.StaysOnField &&
                    !c.EquipDeclaresAnotherAttribute,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check("Dragonic Attack: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(ScrollOfBewitchment);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Scroll of Bewitchment: one Activate equip — any face-up monster, declares another Attribute",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.EquipThisToTarget && c.Zone == EffectZoneFilter.FieldAnyMonster &&
                    c.EquipDeclaresAnotherAttribute && string.IsNullOrEmpty(c.RaceFilter) &&
                    string.IsNullOrEmpty(c.AttributeFilter) && c.EquipAtkBonus == 0 && c.EquipDefBonus == 0 &&
                    c.StaysOnField && c.IsPsctTarget,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check("Scroll of Bewitchment: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(CollectedPower);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Collected Power: one targeted Activate — a face-up monster on the field, needs an Equip Card",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.EquipAllEquipCardsToTarget &&
                    c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetFaceUp && c.IsPsctTarget &&
                    c.ActivationCondition == EquipFamilyRuntime.FaceUpEquipCardOnField && !c.StaysOnField,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check("Collected Power: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(DarkNecrofear);
                var prog = CardTextEffectCompiler.Compile(def);
                var cl = prog?.ClauseList ?? new List<EffectClause>();
                var proc = cl.Count == 2 ? cl[0] : null;
                var eq = cl.Count == 2 ? cl[1] : null;
                Check("Dark Necrofear: hand procedure (banish 3 Fiends from the GY, no chain) + End Phase GY equip-and-take-control",
                    prog != null && prog.FullyCompiled && proc != null && eq != null &&
                    proc.Timing == EffectTiming.Activate && proc.Action == EffectActionKind.SpecialSummonThisFromHand &&
                    proc.ActivatesFromHand && proc.BanishFromGyCount == 3 && proc.RaceFilter == "Fiend" &&
                    string.IsNullOrEmpty(proc.AttributeFilter) && !proc.MakesChainLink &&
                    eq.Timing == EffectTiming.EndPhase &&
                    eq.Action == EffectActionKind.EquipThisFromGyToTargetTakeControl &&
                    eq.Zone == EffectZoneFilter.OppFaceUpMonsters && eq.ResolvesFromGy && eq.TakeControlOfTarget &&
                    eq.RequiresDestroyedByOpponentThisTurn && eq.IsPsctTarget && !eq.IsOptional &&
                    !eq.OpponentTurnOnly,
                    string.Join(" | ", cl.Select(EffectVocabulary.Describe)));
                Check("Dark Necrofear: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
                var tc = EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.EquipThisFromGyToTargetTakeControl });
                Check("EquipThisFromGyToTargetTakeControl maps to the shared resolution TakeControl",
                    tc == EffectResolutionKind.TakeControl, tc.ToString());
            }

            foreach (var gate in new[] { 47606319, 13522325 }) // Gigantes / Spirit of Flames: an Attribute, not a Type
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(gate));
                Check($"{db.Get(gate)?.name}: the Attribute banish procedure stays a summon restriction (no hand Activate)",
                    prog == null || !prog.ClauseList.Exists(c => c != null &&
                        c.Action == EffectActionKind.SpecialSummonThisFromHand),
                    prog == null ? "null" : string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe)));
            }

            var kind = EffectVocabulary.ResolutionOf(new EffectClause
                { Action = EffectActionKind.EquipAllEquipCardsToTarget });
            Check("EquipAllEquipCardsToTarget maps to the shared resolution Equip",
                kind == EffectResolutionKind.Equip, kind.ToString());
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            foreach (var (name, text) in new[]
                     {
                         ("Type change with no stat rider",
                             "This card can only be used to equip Warrior-Type monsters. The equipped monster is treated as Dragon-Type."),
                         ("Attribute change that names the Attribute",
                             "Select 1 Attribute. Change the Attribute of the equipped monster to DARK."),
                         ("Collected Power without the incorrect-target rider",
                             "Target 1 face-up monster on the field; equip it with all Equip Cards on the field.")
                     })
            {
                var def = new CardDef
                {
                    id = 990001, name = "EquipFamily near miss", type = "Spell Card", race = "Equip",
                    frameType = "spell", desc = text, atk = -1, def = -1, attribute = "", archetype = ""
                };
                var prog = CardTextEffectCompiler.Compile(def);
                var hit = prog != null && prog.ClauseList.Exists(c => c != null &&
                    (!string.IsNullOrEmpty(c.EquipChangesRaceTo) || c.EquipDeclaresAnotherAttribute ||
                     c.Action == EffectActionKind.EquipAllEquipCardsToTarget));
                Check($"near miss stays off this batch's atoms: {name}", !hit,
                    prog == null ? "null" : string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe)));
            }
        }

        // ───────────────────────── helpers ─────────────────────────

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

        /// <summary>An Equip Spell already equipped (placed face-up in <paramref name="who"/>'s zone).</summary>
        static CardInstance PlaceEquipped(DuelEngine engine, DuelistState who, int id, int zone, CardInstance host)
        {
            var c = PlaceFaceUpSpellTrap(engine, who, id, zone);
            c.EquippedTo = host;
            host.Equips.Add(c);
            EquipFamilyRuntime.NoteEquipped(engine, c, host);
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

        /// <summary>Pick <paramref name="target"/> in the pending activation (a lone target resolves with no prompt).</summary>
        static bool PickIfAsked(DuelEngine engine, CardInstance target)
        {
            if (!engine.IsAwaitingEffectTarget) return true;
            return engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(target) &&
                   engine.TrySelectEffectTarget(target);
        }

        static bool ActivateAndDeclare(DuelEngine engine, DuelistState who, CardInstance card, string type)
        {
            if (!engine.TryActivateSpellTrap(who, card, fromHand: false)) return false;
            var token = Pending(engine, type);
            if (token == null || !engine.TrySelectEffectTarget(token)) return false;
            PassAll(engine);
            return !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0;
        }

        static DuelEngine Board(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachMain(engine, engine.Player, 3);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        // ───────────────────────── Dragonic Attack ─────────────────────────

        static void DragonicAttackDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var oppGaia = PlaceMonster(engine, opp, Gaia, 1, BattlePosition.Attack, true);
                var da = PutInHand(engine, p, DragonicAttack);
                var activated = engine.TryActivateSpellTrap(p, da, fromHand: true);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                Check("Dragonic Attack: only Warriors are legal, on either field",
                    reached && activated && legal.Contains(celtic) && legal.Contains(oppGaia) && !legal.Contains(skull),
                    $"reached={reached} act={activated} {PendingNames(engine)}");
                var picked = PickIfAsked(engine, celtic);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Dragonic Attack: the Warrior becomes a Dragon and gains 500 ATK and DEF",
                    picked && da.EquippedTo == celtic && celtic.Race == "Dragon" && celtic.CurrentAtk == 1900 &&
                    celtic.CurrentDef == 1700 && p.TryFindSpellTrap(da, out _),
                    $"picked={picked} host={da.EquippedTo?.Name} race={celtic.Race} {celtic.CurrentAtk}/{celtic.CurrentDef}");
                Check("Dragonic Attack: the equip stays on its host now that the host is a Dragon (its own limit)",
                    EquipFamilyRuntime.IsCorrectEquipTarget(engine, da, celtic) &&
                    !EquipFamilyRuntime.IsCorrectEquipTarget(engine, da, skull) &&
                    EquipFamilyRuntime.IsCorrectEquipTarget(engine, da, oppGaia));

                // Control changes: the Equip Card keeps applying to its monster.
                var took = engine.TryTakeControl(opp, celtic);
                engine.NotifyPublic();
                Check("Dragonic Attack: the equipped monster keeps the Type and the boost after a control change",
                    took && engine.ControllerOf(celtic) == opp && da.EquippedTo == celtic && celtic.Race == "Dragon" &&
                    celtic.CurrentAtk == 1900, $"took={took} race={celtic.Race} atk={celtic.CurrentAtk}");

                // DNA Surgery declared later wins; once it is gone the Equip Card's Type applies again.
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 3);
                var ok = ActivateAndDeclare(engine, p, dna, "Zombie");
                Check("Dragonic Attack then DNA Surgery (Zombie): the later change wins",
                    ok && celtic.Race == "Zombie" && skull.Race == "Zombie", $"ok={ok} celtic={celtic.Race}");
                engine.SendCardToGrave(p, dna);
                Check("Dragonic Attack: with DNA Surgery gone the equipped monster is a Dragon again",
                    celtic.Race == "Dragon" && skull.Race == "Fiend", $"celtic={celtic.Race} skull={skull.Race}");
                engine.SendCardToGrave(p, da);
                engine.NotifyPublic();
                Check("Dragonic Attack: once it leaves the field the monster is a Warrior again",
                    celtic.Race == "Warrior" && celtic.CurrentAtk == 1400, $"race={celtic.Race} atk={celtic.CurrentAtk}");
            }

            {
                // DNA Surgery (Warrior) first: a Fiend is now a Warrior and can be equipped; the later equip wins.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var dna = PlaceSetTrap(engine, p, DnaSurgery, 0);
                var ok = ActivateAndDeclare(engine, p, dna, "Warrior");
                var da = PutInHand(engine, p, DragonicAttack);
                var activated = engine.TryActivateSpellTrap(p, da, fromHand: true);
                var legalSkull = engine.PendingActivation == null ||
                                 engine.PendingActivation.LegalTargets.Contains(skull);
                var picked = PickIfAsked(engine, skull);
                PassAll(engine);
                engine.NotifyPublic();
                Check("DNA Surgery (Warrior) then Dragonic Attack: the Fiend counts as a Warrior and becomes a Dragon",
                    reached && ok && activated && legalSkull && picked && da.EquippedTo == skull &&
                    skull.Race == "Dragon" && bewd.Race == "Warrior" && skull.CurrentAtk == 3000,
                    $"ok={ok} act={activated} picked={picked} skull={skull.Race} bewd={bewd.Race} atk={skull.CurrentAtk}");
                engine.SendCardToGrave(p, dna);
                engine.NotifyPublic();
                Check("Dragonic Attack: after DNA Surgery leaves, the Fiend stays a Dragon and keeps the equip",
                    skull.Race == "Dragon" && da.EquippedTo == skull && p.TryFindSpellTrap(da, out _) &&
                    bewd.Race == "Dragon", $"skull={skull.Race} host={da.EquippedTo?.Name}");
            }
        }

        // ───────────────────────── Scroll of Bewitchment ─────────────────────────

        static void ScrollDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var scroll = PutInHand(engine, p, ScrollOfBewitchment);
                var activated = engine.TryActivateSpellTrap(p, scroll, fromHand: true);
                var pa = engine.PendingActivation;
                Check("Scroll of Bewitchment: activating it asks for an Attribute (declaration tokens)",
                    reached && activated && pa != null && pa.LegalTargets.Count == 7 &&
                    pa.LegalTargets.All(TypeDeclarationRuntime.IsAttributeToken) && Pending(engine, "DARK") != null,
                    $"reached={reached} act={activated} {PendingNames(engine)}");
                var dark = Pending(engine, "DARK");
                var declared = dark != null && engine.TrySelectEffectTarget(dark);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                Check("Scroll of Bewitchment: then only a monster without that Attribute can be chosen",
                    declared && (legal.Count == 0 || (legal.Contains(bewd) && !legal.Contains(skull))),
                    $"declared={declared} {PendingNames(engine)}");
                var picked = PickIfAsked(engine, bewd);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Scroll of Bewitchment: the equipped monster's Attribute becomes the declared one",
                    picked && scroll.EquippedTo == bewd && bewd.Attribute == "DARK" && bewd.Def.attribute == "LIGHT" &&
                    p.TryFindSpellTrap(scroll, out _), $"picked={picked} host={scroll.EquippedTo?.Name} attr={bewd.Attribute}");

                // Earthshaker reads the live Attribute: both face-up monsters are now DARK.
                var es = PutInHand(engine, p, Earthshaker);
                var canShake = engine.CanActivateSpellTrap(p, es, fromHand: true);
                Check("Scroll of Bewitchment: Earthshaker sees the new Attribute (only DARK on the field)",
                    !canShake && TypeDeclarationRuntime.FaceUpAttributes(engine).Count == 1,
                    $"can={canShake} attrs={string.Join(",", TypeDeclarationRuntime.FaceUpAttributes(engine))}");

                // A DARK-only Equip Spell may now be equipped to it.
                var sodd = PutInHand(engine, p, SwordOfDarkDestruction);
                var sActivated = engine.TryActivateSpellTrap(p, sodd, fromHand: true);
                var sLegal = engine.PendingActivation == null ||
                             engine.PendingActivation.LegalTargets.Contains(bewd);
                Check("Scroll of Bewitchment: a DARK-only Equip Spell may target the now-DARK monster",
                    sActivated && sLegal, $"act={sActivated} {PendingNames(engine)}");
                engine.CancelEffectTargeting();

                engine.TryTakeControl(opp, bewd);
                Check("Scroll of Bewitchment: the declared Attribute stays after a control change",
                    bewd.Attribute == "DARK" && engine.ControllerOf(bewd) == opp, bewd.Attribute);
                engine.SendCardToGrave(p, scroll);
                Check("Scroll of Bewitchment: once it leaves the field the printed Attribute returns",
                    bewd.Attribute == "LIGHT", bewd.Attribute);
            }

            {
                // The AI activates it (its own monster) and declares an Attribute that monster does not have.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var scroll = PutInHand(engine, opp, ScrollOfBewitchment);
                var activated = engine.TryActivateSpellTrap(opp, scroll, fromHand: true);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Scroll of Bewitchment (AI): equips its own monster and declares another Attribute",
                    reached && activated && !engine.IsAwaitingEffectTarget && scroll.EquippedTo == skull &&
                    !string.IsNullOrEmpty(skull.Attribute) && skull.Attribute != "DARK",
                    $"reached={reached} act={activated} host={scroll.EquippedTo?.Name} attr={skull.Attribute} {PendingNames(engine)}");
            }
        }

        // ───────────────────────── Dark Necrofear ─────────────────────────

        static void EndTurn(DuelEngine engine)
        {
            PassAll(engine);
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
        }

        static void DarkNecrofearDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The procedure: banish 3 Fiends from your GY (your pick), never a Normal Summon.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var necro = PutInHand(engine, p, DarkNecrofear);
                var f1 = engine.CreateCardInstance(SummonedSkull);
                var f2 = engine.CreateCardInstance(FeralImp);
                p.Graveyard.Add(f1);
                p.Graveyard.Add(f2);
                var celticGy = engine.CreateCardInstance(Celtic);
                p.Graveyard.Add(celticGy);
                var twoOnly = engine.CanActivateSpellTrap(p, necro, fromHand: true);
                var f3 = engine.CreateCardInstance(FeralImp);
                p.Graveyard.Add(f3);
                var f4 = engine.CreateCardInstance(SummonedSkull);
                p.Graveyard.Add(f4);
                var normal = engine.CanNormalSummonOrSet(p, necro);
                var can = engine.CanActivateSpellTrap(p, necro, fromHand: true);
                Check("Dark Necrofear: cannot be Normal Summoned; its procedure needs 3 Fiends in the GY",
                    reached && !normal && !twoOnly && can, $"reached={reached} normal={normal} two={twoOnly} can={can}");
                var activated = engine.TryActivateSpellTrap(p, necro, fromHand: true);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                Check("Dark Necrofear: the player picks the Fiends to banish (only Fiends are offered)",
                    activated && legal.Count == 4 && !legal.Contains(celticGy),
                    $"act={activated} {PendingNames(engine)}");
                foreach (var f in new[] { f1, f2, f3 })
                    if (engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(f))
                        engine.TrySelectEffectTarget(f);
                PassAll(engine);
                Check("Dark Necrofear: Special Summoned from the hand with the 3 chosen Fiends banished",
                    p.TryFindMonster(necro, out _) && necro.FaceUp && p.Banished.Contains(f1) &&
                    p.Banished.Contains(f2) && p.Banished.Contains(f3) && p.Graveyard.Contains(f4) &&
                    p.Graveyard.Contains(celticGy) && engine.Chain.Count == 0,
                    $"onField={p.TryFindMonster(necro, out _)} banished={p.Banished.Count} {PendingNames(engine)}");
            }

            {
                // Destroyed by an opponent's card effect: at the End Phase it equips itself and takes control.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var necro = PlaceMonster(engine, p, DarkNecrofear, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var feral = PlaceMonster(engine, opp, FeralImp, 1, BattlePosition.Attack, true);
                var oppCard = PlaceFaceUpSpellTrap(engine, opp, Mst, 0);
                TextEffectRuntime.DestroyCard(engine, necro, oppCard, false, opp);
                var flagged = EquipFamilyRuntime.DestroyedByOpponentThisTurn(engine, p, necro);
                EndTurn(engine);
                var pa = engine.PendingActivation;
                Check("Dark Necrofear: destroyed by the opponent's card — in the End Phase the player targets a monster",
                    reached && flagged && pa != null && pa.LegalTargets.Contains(skull) && pa.LegalTargets.Contains(feral) &&
                    engine.Phase == DuelPhase.End, $"flag={flagged} phase={engine.Phase} {PendingNames(engine)}");
                var picked = pa != null && engine.TrySelectEffectTarget(skull);
                Check("Dark Necrofear: equipped to that monster from the GY, and its controller takes control of it",
                    picked && p.TryFindSpellTrap(necro, out _) && necro.EquippedTo == skull &&
                    engine.ControllerOf(skull) == p && skull.TakenByEquipControl && !p.Graveyard.Contains(necro),
                    $"picked={picked} host={necro.EquippedTo?.Name} skull={engine.ControllerOf(skull)?.Name}");
                Check("Dark Necrofear: the End Phase goes on after the pick (the opponent's turn starts)",
                    engine.TurnPlayer == opp && !engine.IsAwaitingEffectTarget,
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} {PendingNames(engine)}");
                var mst = PutInHand(engine, opp, Mst);
                var mstClause = CardTextEffectCompiler.Compile(db.Get(Mst)).ClauseList
                    .First(c => c != null && c.RequiresTargetChoice);
                var mstTargets = TextEffectRuntime.CollectTargets(engine, opp, mstClause, mst);
                Check("Dark Necrofear: as an Equip Card it is a Mystical Space Typhoon target",
                    mstTargets.Contains(necro), string.Join(",", mstTargets.Select(t => t.Name)));
                TextEffectRuntime.DestroyCard(engine, necro, mst, false, opp);
                engine.NotifyPublic();
                Check("Dark Necrofear: once it is no longer equipped, control of the monster returns",
                    p.Graveyard.Contains(necro) && engine.ControllerOf(skull) == opp && !skull.TakenByEquipControl,
                    $"necroGy={p.Graveyard.Contains(necro)} skull={engine.ControllerOf(skull)?.Name}");
            }

            {
                // Destroyed by battle during the opponent's turn: it triggers in the opponent's End Phase.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                ClearBoard(engine);
                p.Hand.Clear();
                opp.Hand.Clear();
                var necro = PlaceMonster(engine, p, DarkNecrofear, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var attacked = engine.TryAttack(opp, bewd, necro);
                DrainCombat(engine);
                var inGy = p.Graveyard.Contains(necro);
                EndTurn(engine);
                Check("Dark Necrofear: destroyed by battle on the opponent's turn — equips in that End Phase (lone target, no prompt)",
                    reached && attacked && inGy && necro.EquippedTo == bewd && engine.ControllerOf(bewd) == p &&
                    !engine.IsAwaitingEffectTarget,
                    $"reached={reached} att={attacked} gy={inGy} host={necro.EquippedTo?.Name} bewd={engine.ControllerOf(bewd)?.Name} {PendingNames(engine)}");

                // Harpie's Feather Duster treats it as one of the player's Spell & Trap Zone cards.
                var reached2 = ReachOpponentMain(engine);
                var hfd = PutInHand(engine, opp, HarpiesFeatherDuster);
                var used = engine.TryActivateSpellTrap(opp, hfd, fromHand: true);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Dark Necrofear: Harpie's Feather Duster destroys it, and the monster goes back",
                    reached2 && used && p.Graveyard.Contains(necro) && engine.ControllerOf(bewd) == opp,
                    $"reached={reached2} used={used} necroGy={p.Graveyard.Contains(necro)} bewd={engine.ControllerOf(bewd)?.Name}");
            }

            {
                // Near misses: destroyed by its own controller's card, or sent to the GY without being destroyed.
                var engine = Board(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var necro = PlaceMonster(engine, p, DarkNecrofear, 0, BattlePosition.Attack, true);
                var necro2 = PlaceMonster(engine, p, DarkNecrofear, 1, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var own = PlaceFaceUpSpellTrap(engine, p, Mst, 0);
                TextEffectRuntime.DestroyCard(engine, necro, own, false, p);
                engine.SendCardToGrave(p, necro2);
                EndTurn(engine);
                Check("Dark Necrofear: destroyed by your own card, or sent (not destroyed): no End Phase equip",
                    p.Graveyard.Contains(necro) && p.Graveyard.Contains(necro2) && necro.EquippedTo == null &&
                    necro2.EquippedTo == null && engine.ControllerOf(skull) == opp,
                    $"n1={necro.EquippedTo?.Name} n2={necro2.EquippedTo?.Name} {PendingNames(engine)}");
            }
            {
                // Review (rv-combined PROBE1): a human's Garuda the Wind Spirit pick and Dark Necrofear's pick queued in
                // the same (opponent's) End Phase. Garuda is asked first; once it is answered, the End Phase must still
                // ask Necrofear's mandatory pick before the turn passes (ResumeEndTurnAfterTrigger drains the queue).
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                PlaceMonster(engine, p, GarudaTheWindSpirit, 1, BattlePosition.Attack, true);
                var necro = PlaceMonster(engine, p, DarkNecrofear, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var feral = PlaceMonster(engine, opp, FeralImp, 1, BattlePosition.Defense, true);
                var oppCard = PlaceFaceUpSpellTrap(engine, opp, Mst, 0);
                TextEffectRuntime.DestroyCard(engine, necro, oppCard, false, opp);
                engine.TryEndTurnSafe(opp);
                var firstCard = engine.PendingActivation?.Card?.Name;
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(feral);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var secondCard = engine.PendingActivation?.Card?.Name;
                var secondOffersSkull = engine.IsAwaitingEffectTarget &&
                                        engine.PendingActivation.LegalTargets.Contains(skull);
                if (secondOffersSkull) engine.TrySelectEffectTarget(skull);
                Check("Dark Necrofear: still asked after a Garuda pick in the same End Phase, then the turn passes",
                    reached && firstCard == "Garuda the Wind Spirit" && secondCard == "Dark Necrofear" &&
                    secondOffersSkull && feral.Position == BattlePosition.Attack && necro.EquippedTo == skull &&
                    engine.ControllerOf(skull) == p && !engine.IsAwaitingEffectTarget && engine.TurnPlayer == p,
                    $"reached={reached} first={firstCard} second={secondCard} feral={feral.Position} " +
                    $"host={necro.EquippedTo?.Name} skull={engine.ControllerOf(skull)?.Name} " +
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} {PendingNames(engine)}");
            }
        }

        // ───────────────────────── Collected Power ─────────────────────────

        static void CollectedPowerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var cp0 = PlaceSetTrap(engine, p, CollectedPower, 4);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var none = engine.CanActivateSpellTrap(p, cp0, fromHand: false);
                Check("Collected Power: cannot be activated with no Equip Card on the field", reached && !none);

                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var sword = PlaceEquipped(engine, p, LegendarySword, 0, celtic);
                var axe = PlaceEquipped(engine, opp, AxeOfDespair, 0, skull);
                var sodd = PlaceEquipped(engine, opp, SwordOfDarkDestruction, 1, skull);
                engine.NotifyPublic();
                var activated = engine.TryActivateSpellTrap(p, cp0, fromHand: false);
                var picked = PickIfAsked(engine, celtic);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Collected Power: every correct Equip Card moves to the target, keeping its controller",
                    activated && picked && sword.EquippedTo == celtic && axe.EquippedTo == celtic &&
                    celtic.Equips.Contains(axe) && !skull.Equips.Contains(axe) && opp.TryFindSpellTrap(axe, out _) &&
                    p.TryFindSpellTrap(sword, out _),
                    $"act={activated} picked={picked} sword={sword.EquippedTo?.Name} axe={axe.EquippedTo?.Name} {PendingNames(engine)}");
                Check("Collected Power: an Equip Card the target cannot take (DARK-only on an EARTH Warrior) is destroyed",
                    opp.Graveyard.Contains(sodd) && !opp.TryFindSpellTrap(sodd, out _) && sodd.EquippedTo == null,
                    $"inGy={opp.Graveyard.Contains(sodd)} host={sodd.EquippedTo?.Name}");
                Check("Collected Power: the stats follow the Equip Cards",
                    celtic.CurrentAtk == 1400 + 300 + 1000 && skull.CurrentAtk == 2500 &&
                    p.Graveyard.Contains(cp0), $"celtic={celtic.CurrentAtk} skull={skull.CurrentAtk}");
            }

            {
                // Snatch Steal moves with its control; Ring of Magnetism (its controller's monster only) is destroyed.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(engine, p, Gaia, 1, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var snatch = PlaceEquipped(engine, opp, SnatchSteal, 0, celtic);
                celtic.TakenByEquipControl = true;
                var stolen = engine.TryTakeControl(opp, celtic);
                var ring = PlaceEquipped(engine, opp, RingOfMagnetism, 1, skull);
                engine.NotifyPublic();
                var cp = PlaceSetTrap(engine, p, CollectedPower, 4);
                var activated = engine.TryActivateSpellTrap(p, cp, fromHand: false);
                var picked = PickIfAsked(engine, gaia);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Collected Power: Snatch Steal moves to the target — the old monster returns, the new one is taken",
                    reached && stolen && activated && picked && snatch.EquippedTo == gaia &&
                    engine.ControllerOf(celtic) == p && engine.ControllerOf(gaia) == opp && gaia.TakenByEquipControl &&
                    !celtic.TakenByEquipControl,
                    $"stolen={stolen} act={activated} picked={picked} host={snatch.EquippedTo?.Name} " +
                    $"celtic={engine.ControllerOf(celtic)?.Name} gaia={engine.ControllerOf(gaia)?.Name} {PendingNames(engine)}");
                Check("Collected Power: Ring of Magnetism (its controller's monster only) is destroyed",
                    opp.Graveyard.Contains(ring) && ring.EquippedTo == null,
                    $"ringGy={opp.Graveyard.Contains(ring)} host={ring.EquippedTo?.Name}");
            }

            {
                // The target left the field before resolution: every Equip Card is destroyed (Lua s.operation).
                var engine = Board(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var sword = PlaceEquipped(engine, p, LegendarySword, 0, celtic);
                var mega = PlaceEquipped(engine, opp, Megamorph, 0, skull);
                var cp = PlaceFaceUpSpellTrap(engine, p, CollectedPower, 4);
                engine.SendCardToGrave(p, celtic);
                EquipFamilyRuntime.ResolveCollectAllEquips(engine, p, cp, celtic);
                Check("Collected Power: with its target gone every Equip Card on the field is destroyed",
                    p.Graveyard.Contains(sword) && opp.Graveyard.Contains(mega) && skull.Equips.Count == 0,
                    $"sword={p.Graveyard.Contains(sword)} megamorph={opp.Graveyard.Contains(mega)}");
            }
        }
    }
}
