using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "EquipModel": the "a monster was equipped" window and the turn-long Equip Card negation (Armored
    /// Glass), and switching an Equip Card to another correct target with two targets (Tailor of the Fickle).
    /// Sword Hunter was deferred here (no generic "monster Card treated as an Equip Spell" state); it landed with the
    /// BothSideAndMonsterEquip batch (v161) and its guard below asserts the compiled shape.
    /// </summary>
    public static class EquipModelRegressionTests
    {
        const int ArmoredGlass = 36868108;
        const int TailorOfTheFickle = 43641473;
        const int SwordHunter = 51345461;
        const int FiendsHandMirror = 58607704;

        const int AxeOfDespair = 40619825;      // any monster: +1000 ATK
        const int SwordOfDarkDestruction = 37120512; // DARK only: +400 ATK / -200 DEF
        const int LegendarySword = 61854111;    // Warrior only: +300 ATK/DEF
        const int SnatchSteal = 45986603;
        const int Mst = 5318639;
        const int PotOfGreed = 55144522;
        const int Kiseitai = 4266839;
        const int CallOfTheHaunted = 97077563;

        const int Celtic = 91152256;            // Warrior EARTH 1400/1200
        const int Gaia = 6368038;               // Warrior EARTH 2300/2100
        const int SummonedSkull = 70781052;     // Fiend DARK 2500/1200
        const int FeralImp = 41392891;          // Fiend DARK 1300/1400

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("EquipModel: " + name, ok, detail);

            CompileShapes(db, Check);
            ArmoredGlassDuels(db, pDeck, aDeck, Check);
            TailorDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static CompiledCardProgram CompileText(string text, string type, string race) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990160, name = "Near-miss Card", type = type, frameType = type.StartsWith("Trap") ? "trap" : "spell",
                race = race, desc = text
            });

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            {
                var def = db.Get(ArmoredGlass);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Armored Glass: one untargeted Activate clause negating the Equip Card family this turn, " +
                      "legal only in the equip window",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn &&
                    c.NegatedCardFamily == TurnNegationTrapsRuntime.EquipCard &&
                    c.ActivationCondition == EquipModelRuntime.MonsterEquippedWithEquipCard &&
                    c.CheckedAt == ConditionCheckedAt.Activation && !c.RequiresTargetChoice && c.MakesChainLink &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
            }
            {
                var def = db.Get(TailorOfTheFickle);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Tailor of the Fickle: one targeted Activate clause on an Equip Card with another correct target",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.MoveEquipCardToAnotherCorrectTarget &&
                    c.Zone == EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget && c.RequiresTargetChoice &&
                    c.IsPsctTarget && c.MakesChainLink && !c.RequiresSecondTarget &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
            }
            Check("MoveEquipCardToAnotherCorrectTarget maps to the Equip resolution",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.MoveEquipCardToAnotherCorrectTarget }) == EffectResolutionKind.Equip);

            // Near misses: a reworded / longer text stays uncompiled (whole-text atoms).
            var nearGlass = CompileText("You can only activate this card when a monster is equipped with an Equip Card. " +
                                        "Negate the effects of all Equip Cards on the field.", "Trap Card", "Normal");
            Check("near miss: Armored Glass without \"during the turn this card is activated\" is not FullyCompiled",
                nearGlass == null || !nearGlass.FullyCompiled ||
                !nearGlass.ClauseList.Exists(x => x?.NegatedCardFamily == TurnNegationTrapsRuntime.EquipCard));
            var nearTailor = CompileText("Switch 1 Equip Card equipped to a monster to another correct target. Draw 1 card.",
                "Spell Card", "Quick-Play");
            Check("near miss: Tailor of the Fickle with an extra sentence is not FullyCompiled",
                nearTailor == null || !nearTailor.FullyCompiled);
            var mirror = db.Get(FiendsHandMirror);
            var mirrorProg = CardTextEffectCompiler.Compile(mirror);
            Check("near miss: Fiend's Hand Mirror (\"switch ... to another correct target\") gets no Equip switch",
                mirrorProg == null || !mirrorProg.ClauseList.Exists(x =>
                    x?.Action == EffectActionKind.MoveEquipCardToAnotherCorrectTarget));

            // Sword Hunter was deferred here (no "monster Card treated as an Equip Spell" state); the
            // BothSideAndMonsterEquip batch (v161) added that state. Guard rewritten to its compiled shape.
            var hunter = db.Get(SwordHunter);
            var hunterProg = CardTextEffectCompiler.Compile(hunter);
            Check("Sword Hunter (v161) compiles to its end-of-Battle-Phase equip and is Implemented",
                hunterProg != null && hunterProg.FullyCompiled && hunterProg.ClauseList.Exists(x =>
                    x?.Action == EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells) &&
                CardEffectStatus.Classify(hunter) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(hunter).ToString());
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

        static DuelEngine Board(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachMain(engine, engine.Player, 3);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static DuelEngine OpponentBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static CardInstance PlaceEquipped(DuelEngine engine, DuelistState who, int id, int zone, CardInstance host)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            c.EquippedTo = host;
            host.Equips.Add(c);
            EquipFamilyRuntime.NoteEquipped(engine, c, host);
            return c;
        }

        /// <summary>Pick <paramref name="target"/> in the pending activation (a lone target resolves with no prompt).</summary>
        static bool PickIfAsked(DuelEngine engine, CardInstance target)
        {
            if (!engine.IsAwaitingEffectTarget) return true;
            return engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(target) &&
                   engine.TrySelectEffectTarget(target);
        }

        static string PendingNames(DuelEngine engine) =>
            engine.PendingActivation == null
                ? "no pending"
                : $"kind={engine.PendingActivation.TargetKind} " +
                  $"[{string.Join(",", engine.PendingActivation.LegalTargets.Select(t => t?.Name))}]";

        static bool EquipWindowFor(DuelEngine engine, DuelistState who) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.MonsterEquipped &&
            engine.PendingResponse.Responder == who;

        static void EndTurn(DuelEngine engine)
        {
            PassAll(engine);
            engine.TryEndTurnSafe(engine.TurnPlayer);
            PassAll(engine);
            engine.NotifyPublic();
        }

        // ───────────────────────── Armored Glass ─────────────────────────

        static void ArmoredGlassDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The AI answers the player's equip: every Equip Card is negated this turn, later ones too.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var glass = PlaceSetTrap(engine, opp, ArmoredGlass, 0);
                var axe = PutInHand(engine, p, AxeOfDespair);
                engine.NotifyPublic();
                var openState = engine.CanActivateSpellTrap(opp, glass, fromHand: false);
                var act = engine.TryActivateSpellTrap(p, axe, fromHand: true);
                var picked = PickIfAsked(engine, celtic);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Armored Glass: not activatable in an open game state (no equip just happened)",
                    reached && !openState);
                Check("Armored Glass (AI): answers the equip — the Equip Card stays equipped, its effect is negated",
                    act && picked && opp.Graveyard.Contains(glass) && axe.EquippedTo == celtic && axe.IsNegated &&
                    ContinuousProtections.EquipCardEffectsAreNegated(engine, axe) && celtic.CurrentAtk == 1400,
                    $"act={act} picked={picked} glassGy={opp.Graveyard.Contains(glass)} host={axe.EquippedTo?.Name} " +
                    $"neg={axe.IsNegated} atk={celtic.CurrentAtk} {PendingNames(engine)}");

                var sword = PutInHand(engine, p, LegendarySword);
                var act2 = engine.TryActivateSpellTrap(p, sword, fromHand: true);
                var picked2 = PickIfAsked(engine, celtic);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Armored Glass: an Equip Card equipped later this turn is equipped but negated too",
                    act2 && picked2 && sword.EquippedTo == celtic && sword.IsNegated && celtic.CurrentAtk == 1400,
                    $"act={act2} picked={picked2} host={sword.EquippedTo?.Name} neg={sword.IsNegated} atk={celtic.CurrentAtk}");

                EndTurn(engine);
                Check("Armored Glass: the negation ends with the turn",
                    engine.TurnPlayer == opp && !axe.IsNegated && !sword.IsNegated &&
                    celtic.CurrentAtk == 1400 + 1000 + 300,
                    $"turn={engine.TurnPlayer?.Name} axeNeg={axe.IsNegated} atk={celtic.CurrentAtk}");
            }

            {
                // The player answers their own Snatch Steal's equip window: control returns for the turn.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var glass = PlaceSetTrap(engine, p, ArmoredGlass, 0);
                var snatch = PutInHand(engine, p, SnatchSteal);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, snatch, fromHand: true);
                var picked = PickIfAsked(engine, skull);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse && !EquipWindowFor(engine, p); i++)
                    engine.PassResponse();
                var window = EquipWindowFor(engine, p);
                var taken = engine.ControllerOf(skull) == p;
                var answered = window && engine.TryActivateSpellTrap(p, glass, fromHand: false);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Armored Glass (player): the equip window opens right after Snatch Steal resolves",
                    reached && act && picked && taken && window,
                    $"act={act} picked={picked} taken={taken} window={window} pr={engine.PendingResponse?.Timing}");
                Check("Armored Glass: a negated Snatch Steal gives its monster back, still equipped",
                    answered && p.Graveyard.Contains(glass) && snatch.EquippedTo == skull && snatch.IsNegated &&
                    engine.ControllerOf(skull) == opp && !skull.TakenByEquipControl,
                    $"answered={answered} host={snatch.EquippedTo?.Name} neg={snatch.IsNegated} " +
                    $"ctrl={engine.ControllerOf(skull)?.Name}");
                var again = engine.CanActivateSpellTrap(p, glass, fromHand: false);
                EndTurn(engine);
                Check("Armored Glass ended: Snatch Steal takes its monster again",
                    !again && engine.TurnPlayer == opp && !snatch.IsNegated && snatch.EquippedTo == skull &&
                    engine.ControllerOf(skull) == p && skull.TakenByEquipControl,
                    $"turn={engine.TurnPlayer?.Name} ctrl={engine.ControllerOf(skull)?.Name} taken={skull.TakenByEquipControl}");
            }

            {
                // The window passes: the chance is gone; Armored Glass negates monster Equip Cards, not links.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(engine, p, Gaia, 1, BattlePosition.Attack, true);
                var imp = PlaceMonster(engine, opp, FeralImp, 0, BattlePosition.Attack, true);
                var glass = PlaceSetTrap(engine, p, ArmoredGlass, 0);
                var glass2 = PlaceSetTrap(engine, p, ArmoredGlass, 1);
                var axe = PutInHand(engine, p, AxeOfDespair);
                engine.NotifyPublic();
                engine.TryActivateSpellTrap(p, axe, fromHand: true);
                PickIfAsked(engine, celtic);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse && !EquipWindowFor(engine, p); i++)
                    engine.PassResponse();
                var window = EquipWindowFor(engine, p);
                engine.PassResponse();
                engine.NotifyPublic();
                Check("Armored Glass: once its window is passed it cannot be activated for that equip",
                    reached && window && axe.EquippedTo == celtic &&
                    !engine.CanActivateSpellTrap(p, glass, fromHand: false) && celtic.CurrentAtk == 2400,
                    $"window={window} host={axe.EquippedTo?.Name} atk={celtic.CurrentAtk}");

                // A monster that became an Equip Card (Kiseitai) and a Continuous Trap link (Call of the Haunted).
                var kiseitai = PlaceEquipped(engine, opp, Kiseitai, 0, imp);
                var coth = engine.CreateCardInstance(CallOfTheHaunted);
                coth.FaceUp = true;
                coth.SetThisTurn = false;
                p.SpellTrapZones[4].Occupant = coth;
                coth.EquippedTo = gaia;
                gaia.Equips.Add(coth);
                engine.NotifyPublic();
                var window2 = EquipWindowFor(engine, p);
                var answered = window2 && engine.TryActivateSpellTrap(p, glass2, fromHand: false);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Armored Glass: a monster acting as an Equip Card is negated; a Call of the Haunted link is not",
                    window2 && answered && kiseitai.IsNegated &&
                    ContinuousProtections.EquipCardEffectsAreNegated(engine, kiseitai) && axe.IsNegated &&
                    !coth.IsNegated && !ContinuousProtections.EquipCardEffectsAreNegated(engine, coth),
                    $"window={window2} answered={answered} kiseitai={kiseitai.IsNegated} axe={axe.IsNegated} " +
                    $"coth={coth.IsNegated} pr={engine.PendingResponse?.Timing}");
            }
        }

        // ───────────────────────── Tailor of the Fickle ─────────────────────────

        static void TailorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Open game state: the Equip Card, then the monster it moves to.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(engine, p, Gaia, 1, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var sword = PlaceEquipped(engine, p, LegendarySword, 0, celtic);
                var tailor = PutInHand(engine, p, TailorOfTheFickle);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, tailor, fromHand: true);
                var first = engine.PendingActivation?.LegalTargets.ToList();
                var pick1 = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(sword);
                var second = engine.PendingActivation?.LegalTargets.ToList();
                var pick2 = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(gaia);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Tailor of the Fickle: the player targets the Equip Card, then only its correct new monsters",
                    reached && act && pick1 && pick2 && first != null && first.Count == 1 && first[0] == sword &&
                    second != null && second.Count == 1 && second[0] == gaia,
                    $"act={act} first=[{string.Join(",", first?.Select(t => t.Name) ?? new string[0])}] " +
                    $"second=[{string.Join(",", second?.Select(t => t.Name) ?? new string[0])}] {PendingNames(engine)}");
                Check("Tailor of the Fickle: the Equip Card moves (stats follow), Tailor goes to the GY",
                    sword.EquippedTo == gaia && gaia.Equips.Contains(sword) && !celtic.Equips.Contains(sword) &&
                    gaia.CurrentAtk == 2600 && celtic.CurrentAtk == 1400 && p.Graveyard.Contains(tailor) &&
                    skull.CurrentAtk == 2500,
                    $"host={sword.EquippedTo?.Name} gaia={gaia.CurrentAtk} celtic={celtic.CurrentAtk} " +
                    $"gy={p.Graveyard.Contains(tailor)}");
            }

            {
                // No other correct target (Legendary Sword: Warrior only): cannot be activated.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                PlaceEquipped(engine, p, LegendarySword, 0, celtic);
                var tailor = PutInHand(engine, p, TailorOfTheFickle);
                engine.NotifyPublic();
                Check("Tailor of the Fickle: no Equip Card with another correct target — cannot be activated",
                    reached && !engine.CanActivateSpellTrap(p, tailor, fromHand: true));
            }

            {
                // The AI activates it (both targets auto-picked); the monster target leaves before resolution:
                // the Equip Card is sent to the GY.
                var engine = OpponentBoard(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var imp = PlaceMonster(engine, opp, FeralImp, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                // Sword of Dark Destruction (DARK only, +400 ATK / -200 DEF; no leave-the-field effect of its own).
                var axe = PlaceEquipped(engine, opp, SwordOfDarkDestruction, 0, skull);
                var tailor = PlaceSetTrap(engine, opp, TailorOfTheFickle, 1);
                var mst = PlaceSetTrap(engine, p, Mst, 0);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, tailor, fromHand: false);
                var planned = EquipModelRuntime.PlannedMonster(engine, tailor);
                var link = engine.Chain.Last;
                var window = engine.PendingResponse != null &&
                             engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                             engine.PendingResponse.Responder == p;
                var linkTargets = link != null && link.Card == tailor && link.Targets.Contains(axe) &&
                                  link.Targets.Contains(imp);
                engine.SendCardToGrave(opp, imp); // the monster target leaves the field before Tailor resolves
                PassAll(engine);
                engine.NotifyPublic();
                Check("Tailor of the Fickle (AI): picks its Equip Card and the monster it moves to; both are link targets",
                    reached && act && planned == imp && window && linkTargets,
                    $"act={act} planned={planned?.Name} window={window} targets=[{string.Join(",", link?.Targets.Select(t => t?.Name) ?? new string[0])}]");
                Check("Tailor of the Fickle: the monster target is gone — the Equip Card is sent to the GY",
                    opp.Graveyard.Contains(axe) && axe.EquippedTo == null && !skull.Equips.Contains(axe) &&
                    skull.CurrentAtk == 2500 && p.TryFindSpellTrap(mst, out _),
                    $"axeGy={opp.Graveyard.Contains(axe)} host={axe.EquippedTo?.Name} skull={skull.CurrentAtk}");
            }

            {
                // A chain response by the player (two targets in a chain window): Set Tailor answers Pot of Greed.
                var engine = OpponentBoard(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(engine, p, Gaia, 1, BattlePosition.Attack, true);
                var sword = PlaceEquipped(engine, p, LegendarySword, 0, celtic);
                var tailor = PlaceSetTrap(engine, p, TailorOfTheFickle, 1);
                var pot = PutInHand(engine, opp, PotOfGreed);
                var hand = opp.Hand.Count;
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, pot, fromHand: true);
                var window = engine.PendingResponse != null &&
                             engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                             engine.PendingResponse.Responder == p;
                var chained = window && engine.TryActivateSpellTrap(p, tailor, fromHand: false);
                var pick1 = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(sword);
                var second = engine.PendingActivation?.LegalTargets.ToList();
                var pick2 = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(gaia);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Tailor of the Fickle (player, chain response): both targets are picked in the chain window",
                    reached && act && window && chained && pick1 && pick2 && second != null && second.Contains(gaia),
                    $"act={act} window={window} chained={chained} pick1={pick1} pick2={pick2} {PendingNames(engine)}");
                Check("Tailor of the Fickle (chain response): the Equip Card moves; the answered Pot of Greed resolves",
                    sword.EquippedTo == gaia && gaia.CurrentAtk == 2600 && celtic.CurrentAtk == 1400 &&
                    p.Graveyard.Contains(tailor) && opp.Hand.Count == hand - 1 + 2,
                    $"host={sword.EquippedTo?.Name} gaia={gaia.CurrentAtk} hand={opp.Hand.Count} (was {hand})");
            }

            {
                // Two Tailor links on the same Equip Card: each link keeps its own monster target (Lua reads
                // CHAININFO_TARGET_CARDS of its own link). CL1 (AI): Sword of Dark Destruction -> the AI's Feral Imp;
                // CL2 (player): the same Sword -> the player's Feral Imp. CL2 resolves, then CL1 equips the Sword to
                // the AI's Imp — its own monster target is still there, so the Sword is not sent to the GY.
                var engine = OpponentBoard(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var aiImp = PlaceMonster(engine, opp, FeralImp, 1, BattlePosition.Attack, true);
                var myImp = PlaceMonster(engine, p, FeralImp, 0, BattlePosition.Attack, true);
                var sword = PlaceEquipped(engine, opp, SwordOfDarkDestruction, 0, skull);
                var aiTailor = PlaceSetTrap(engine, opp, TailorOfTheFickle, 1);
                var myTailor = PlaceSetTrap(engine, p, TailorOfTheFickle, 0);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, aiTailor, fromHand: false);
                var aiPlanned = EquipModelRuntime.PlannedMonster(engine, aiTailor);
                var window = engine.PendingResponse != null &&
                             engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                             engine.PendingResponse.Responder == p;
                var chained = window && engine.TryActivateSpellTrap(p, myTailor, fromHand: false);
                var pick1 = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(sword);
                var pick2 = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(myImp);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Tailor of the Fickle: a second Tailor link picks the same Equip Card and another monster",
                    reached && act && aiPlanned == aiImp && chained && pick1 && pick2,
                    $"act={act} aiPlanned={aiPlanned?.Name} window={window} chained={chained} pick1={pick1} " +
                    $"pick2={pick2} {PendingNames(engine)}");
                Check("Tailor of the Fickle: CL2 moves the Equip Card, then CL1 moves it to its own monster target",
                    sword.EquippedTo == aiImp && aiImp.Equips.Contains(sword) && !myImp.Equips.Contains(sword) &&
                    !opp.Graveyard.Contains(sword) && aiImp.CurrentAtk == 1700 && myImp.CurrentAtk == 1300 &&
                    skull.CurrentAtk == 2500 && opp.Graveyard.Contains(aiTailor) && p.Graveyard.Contains(myTailor),
                    $"host={sword.EquippedTo?.Name} swordGy={opp.Graveyard.Contains(sword)} aiImp={aiImp.CurrentAtk} " +
                    $"myImp={myImp.CurrentAtk} skull={skull.CurrentAtk}");
            }

            {
                // AI "use it or lose it" (Mystical Space Typhoon on its Set Tailor): it does not chain Tailor when its
                // only switch helps the opponent (its Axe of Despair would go to the player's Celtic Guardian).
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var axe = PlaceEquipped(engine, opp, AxeOfDespair, 0, skull);
                var tailor = PlaceSetTrap(engine, opp, TailorOfTheFickle, 1);
                var mst = PutInHand(engine, p, Mst);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                var picked = PickIfAsked(engine, tailor);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Tailor of the Fickle (AI): not chained to save itself when its only switch helps the opponent",
                    reached && act && picked && axe.EquippedTo == skull && skull.CurrentAtk == 3500 &&
                    celtic.CurrentAtk == 1400 && opp.Graveyard.Contains(tailor),
                    $"act={act} picked={picked} host={axe.EquippedTo?.Name} skull={skull.CurrentAtk} " +
                    $"celtic={celtic.CurrentAtk} tailorGy={opp.Graveyard.Contains(tailor)}");
            }

            {
                // The same MST, but the switch helps the AI (the player's Axe of Despair to the AI's Summoned Skull):
                // the AI chains Tailor before it is destroyed.
                var engine = Board(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var axe = PlaceEquipped(engine, p, AxeOfDespair, 0, celtic);
                var tailor = PlaceSetTrap(engine, opp, TailorOfTheFickle, 1);
                var mst = PutInHand(engine, p, Mst);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                var picked = PickIfAsked(engine, tailor);
                PassAll(engine);
                engine.NotifyPublic();
                Check("Tailor of the Fickle (AI): chained to save itself when the switch helps it",
                    reached && act && picked && axe.EquippedTo == skull && skull.CurrentAtk == 3500 &&
                    celtic.CurrentAtk == 1400 && opp.Graveyard.Contains(tailor),
                    $"act={act} picked={picked} host={axe.EquippedTo?.Name} skull={skull.CurrentAtk} " +
                    $"celtic={celtic.CurrentAtk} tailorGy={opp.Graveyard.Contains(tailor)}");
            }
        }
    }
}
