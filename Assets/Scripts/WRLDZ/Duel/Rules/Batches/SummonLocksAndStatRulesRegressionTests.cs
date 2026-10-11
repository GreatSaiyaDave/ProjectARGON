using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SummonLocksAndStatRules batch: Gyaku-Gire Panda, Amazoness Tiger, Amazoness Spellcaster, Rivalry of Warlords
    /// (Kaiser Colosseum stays fail-closed). Compile shapes through the real compiler plus mini-duels through the
    /// engine entry points (TryActivateSpellTrap, TryNormalSummon, TryFlipSummon, TryAttack, TrySelectEffectTarget,
    /// response windows). Turn 1 is the Player's, turn 2 the AI's. Runtime: <see cref="SummonLocksAndStatRulesRuntime"/>.
    /// </summary>
    public static class SummonLocksAndStatRulesRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GyakuGirePanda = 9817927;       // Beast 800/1600
        const int AmazonessTiger = 10979723;      // Beast 1100/1500
        const int AmazonessSpellcaster = 81325903; // Normal Spell
        const int KaiserColosseum = 35059553;     // Continuous Spell (fail-closed)
        const int RivalryOfWarlords = 90846359;   // Continuous Trap

        const int AmazonessFighter = 55821894;    // Warrior 1500/1300
        const int Celtic = 91152256;              // Warrior 1400/1200
        const int GiantSoldier = 13039848;        // Rock 1300/2000
        const int Kuriboh = 40640057;             // Fiend 300/200
        const int BlueEyes = 89631139;            // Dragon 3000/2500
        const int Mst = 5318639;                  // Mystical Space Typhoon (Quick-Play Spell)
        const int MonsterReborn = 83764718;       // Normal Spell: target 1 monster in either GY; Special Summon it

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SummonLocksAndStatRules: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            PandaDuels(db, pDeck, aDeck, Check);
            TigerDuels(db, pDeck, aDeck, Check);
            SpellcasterDuels(db, pDeck, aDeck, Check);
            RivalryDuels(db, pDeck, aDeck, Check);
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

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(int id, string type, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "SummonLocksAndStatRules probe", type = type,
                frameType = type.StartsWith("Spell") || type.StartsWith("Trap") ? "spell" : "effect",
                race = type.StartsWith("Spell") ? "Normal" : type.StartsWith("Trap") ? "Continuous" : "Beast",
                attribute = type.StartsWith("Spell") || type.StartsWith("Trap") ? null : "EARTH",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var panda = Prog(db, GyakuGirePanda);
            Check("Gyaku-Gire Panda FullyCompiled: +500 per opponent monster (any position) and piercing",
                panda != null && panda.FullyCompiled && Implemented(db, GyakuGirePanda) && panda.ClauseList.Count == 2 &&
                Has(panda, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.GainSelfAtkPerCount && c.Amount == 500 &&
                                c.CountSource == SelfStatMonstersRuntime.OppMonsterCount) &&
                Has(panda, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.ContinuousPiercing),
                Describe(panda));

            var tiger = Prog(db, AmazonessTiger);
            Check("Amazoness Tiger FullyCompiled: unique rule, per-\"Amazoness\" gain, archetype attack lock except itself",
                tiger != null && tiger.FullyCompiled && Implemented(db, AmazonessTiger) && tiger.ClauseList.Count == 3 &&
                Has(tiger, c => c.Action == EffectActionKind.YouCanOnlyControlOneFaceUpNamed &&
                                c.NamedCard == "Amazoness Tiger") &&
                Has(tiger, c => c.Action == EffectActionKind.ContinuousGainAtkDef && c.Amount == 400 &&
                                c.ScaleThisAtkByMatchingCount && c.NamedCard == "Amazoness" && c.NamedCardIsSeries) &&
                Has(tiger, c => c.Action == EffectActionKind.CannotBeAttackTarget && c.ExceptThisCard &&
                                c.NamedCard == "Amazoness" && c.NamedCardIsSeries),
                Describe(tiger));

            var spell = Prog(db, AmazonessSpellcaster);
            var sc = spell?.ClauseList.Count == 1 ? spell.ClauseList[0] : null;
            Check("Amazoness Spellcaster FullyCompiled: two targets (own face-up \"Amazoness\", opponent face-up), switch",
                spell != null && spell.FullyCompiled && Implemented(db, AmazonessSpellcaster) && sc != null &&
                sc.Timing == EffectTiming.Activate &&
                sc.Action == EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn &&
                sc.Zone == EffectZoneFilter.ControllerFaceUpSeriesMonsters && sc.NamedCard == "Amazoness" &&
                sc.NamedCardIsSeries && sc.RequiresTargetChoice && sc.IsPsctTarget && sc.RequiresSecondTarget &&
                sc.SecondZone == EffectZoneFilter.OppFaceUpMonsters &&
                sc.SecondAction == EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn &&
                EffectVocabulary.ResolutionOf(sc) == EffectResolutionKind.ModifyStats,
                Describe(spell));

            var rivalry = Prog(db, RivalryOfWarlords);
            var rc = rivalry?.ClauseList.Count == 1 ? rivalry.ClauseList[0] : null;
            Check("Rivalry of Warlords FullyCompiled: one continuous one-Type rule",
                rivalry != null && rivalry.FullyCompiled && Implemented(db, RivalryOfWarlords) && rc != null &&
                rc.Timing == EffectTiming.ContinuousWhileFaceUp &&
                rc.Action == EffectActionKind.EachPlayerKeepsOneMonsterType && !rc.MakesChainLink,
                Describe(rivalry));

            var kaiser = Prog(db, KaiserColosseum);
            Check("Kaiser Colosseum stays fail-closed (no placement cap with the Lua's reason/player exemptions)",
                kaiser != null && !kaiser.FullyCompiled && !Implemented(db, KaiserColosseum), Describe(kaiser));

            // Near-misses: look-alike sentences do not compile to this batch's clauses.
            var ownSide = CompileText(90175001, "Effect Monster",
                "Increase the ATK of this card by 500 points for each monster on your side of the field.");
            Check("Near-miss: a per-monster gain on your own side is not the opponent-count Panda clause",
                !Has(ownSide, c => c.Action == EffectActionKind.GainSelfAtkPerCount &&
                                   c.CountSource == SelfStatMonstersRuntime.OppMonsterCount),
                Describe(ownSide));

            var noExcept = CompileText(90175002, "Effect Monster",
                "Your opponent cannot attack any face-up \"Amazoness\" monsters.");
            Check("Near-miss: the attack lock without \"except this one\" is not Tiger's clause",
                !Has(noExcept, c => c.Action == EffectActionKind.CannotBeAttackTarget && c.ExceptThisCard),
                Describe(noExcept));

            var plainAtk = CompileText(90175003, "Spell Card",
                "Target 1 \"Amazoness\" monster you control and 1 face-up monster your opponent controls; switch the " +
                "ATK of those targets until the end of this turn.");
            Check("Near-miss: switching current ATK (not original ATK) is not Spellcaster's clause",
                !Has(plainAtk, c => c.Action == EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn),
                Describe(plainAtk));

            var halfRivalry = CompileText(90175004, "Trap Card", "Each player can only control 1 Type of monster.");
            Check("Near-miss: the one-Type sentence without the send is not Rivalry's rule",
                !Has(halfRivalry, c => c.Action == EffectActionKind.EachPlayerKeepsOneMonsterType),
                Describe(halfRivalry));
        }

        // ───────────────────────────── Gyaku-Gire Panda ─────────────────────────────

        static void PandaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var panda = PlaceMonster(e, e.Player, GyakuGirePanda, 2, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Kuriboh, 3, BattlePosition.Defense, false);
                FieldSpellEffects.RefreshBoard(e);
                var two = panda.CurrentAtk;
                e.Opponent.MonsterZones[1].Occupant = null;
                FieldSpellEffects.RefreshBoard(e);
                var one = panda.CurrentAtk;
                Check("Panda: +500 for each monster the opponent controls, face-down included (800 → 1800 → 1300)",
                    two == 1800 && one == 1300, $"two={two} one={one}");
            }

            {
                // The AI's Panda attacks the player's face-down Kuriboh (DEF 200): 800 + 500 = 1300 → 1100 piercing.
                var e = Setup(db, pDeck, aDeck);
                var panda = PlaceMonster(e, e.Opponent, GyakuGirePanda, 2, BattlePosition.Attack, true);
                var kuri = PlaceMonster(e, e.Player, Kuriboh, 2, BattlePosition.Defense, false);
                var reached = ReachOpponentBattle(e);
                var lp = e.Player.LifePoints;
                var atk = panda.CurrentAtk;
                var attacked = reached && e.TryAttack(e.Opponent, panda, kuri);
                DrainCombat(e);
                Check("Panda: attacking a face-down Defense monster inflicts piercing damage (1300 - 200 = 1100)",
                    reached && attacked && atk == 1300 && lp - e.Player.LifePoints == 1100 &&
                    e.Player.Graveyard.Contains(kuri),
                    $"reached={reached} attacked={attacked} atk={atk} lp {lp}→{e.Player.LifePoints}");
            }
        }

        // ───────────────────────────── Amazoness Tiger ─────────────────────────────

        static void TigerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var tiger = PlaceMonster(e, e.Player, AmazonessTiger, 2, BattlePosition.Attack, true);
                PlaceMonster(e, e.Player, AmazonessFighter, 1, BattlePosition.Attack, true);
                PlaceMonster(e, e.Player, AmazonessFighter, 3, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, AmazonessFighter, 2, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                Check("Tiger: +400 for each face-up \"Amazoness\" monster you control, itself included (1100 → 1900)",
                    tiger.CurrentAtk == 1900, $"atk={tiger.CurrentAtk}");

                var second = PutInHand(e, e.Player, AmazonessTiger);
                var summoned = e.TryNormalSummon(e.Player, second, asSet: false);
                var set = !summoned && e.TryNormalSummon(e.Player, second, asSet: true);
                Check("Tiger: a second \"Amazoness Tiger\" cannot be Normal Summoned face-up; it can be Set",
                    !summoned && set && !second.FaceUp && e.Player.TryFindMonster(second, out _),
                    $"summoned={summoned} set={set}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var tiger = PlaceMonster(e, e.Player, AmazonessTiger, 2, BattlePosition.Attack, true);
                var fighter = PlaceMonster(e, e.Player, AmazonessFighter, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, e.Player, Celtic, 3, BattlePosition.Attack, true);
                var be = PlaceMonster(e, e.Opponent, BlueEyes, 2, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var onFighter = reached && e.TryAttack(e.Opponent, be, fighter);
                var celticOpen = !ContinuousProtections.CannotBeAttackTarget(e, celtic);
                var onTiger = reached && !onFighter && e.TryAttack(e.Opponent, be, tiger);
                DrainCombat(e);
                Check("Tiger: the opponent cannot attack another face-up \"Amazoness\" monster; Tiger and others stay open",
                    reached && !onFighter && celticOpen && onTiger && e.Player.Graveyard.Contains(tiger),
                    $"reached={reached} onFighter={onFighter} celticOpen={celticOpen} onTiger={onTiger}");
            }
        }

        // ───────────────────────────── Amazoness Spellcaster ─────────────────────────────

        static void SpellcasterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var fighter = PlaceMonster(e, e.Player, AmazonessFighter, 2, BattlePosition.Attack, true);
                PlaceMonster(e, e.Player, Celtic, 1, BattlePosition.Attack, true);
                var be = PlaceMonster(e, e.Opponent, BlueEyes, 2, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Kuriboh, 1, BattlePosition.Attack, true);
                var sc = PutInHand(e, e.Player, AmazonessSpellcaster);
                var activated = e.TryActivateSpellTrap(e.Player, sc, fromHand: true);
                var firstLegal = Legal(e);
                Drive(e, c => c == fighter || c == be);
                var fAtk = fighter.CurrentAtk;
                var bAtk = be.CurrentAtk;
                Check("Spellcaster: own face-up \"Amazoness\" and the opponent's monster switch original ATK (1500 ↔ 3000)",
                    activated && !firstLegal.Exists(c => c.Name == "Celtic Guardian") && fAtk == 3000 && bAtk == 1500 &&
                    e.Player.Graveyard.Contains(sc),
                    $"activated={activated} legal1=[{string.Join(",", firstLegal.Select(c => c.Name))}] f={fAtk} be={bAtk}");

                e.TryEndTurnSafe(e.Player);
                Drive(e, _ => false);
                Check("Spellcaster: the switch ends with the turn (1500 / 3000 again)",
                    fighter.CurrentAtk == 1500 && be.CurrentAtk == 3000,
                    $"f={fighter.CurrentAtk} be={be.CurrentAtk} turn={e.TurnNumber}");
            }

            {
                // The opponent's target is turned face-down before the Spell resolves: nothing is switched. Human vs
                // human with a Set Mystical Space Typhoon, so the opponent's response window stays open.
                var e = Setup(db, pDeck, aDeck);
                e.HumanVsHuman = true;
                var fighter = PlaceMonster(e, e.Player, AmazonessFighter, 2, BattlePosition.Attack, true);
                var be = PlaceMonster(e, e.Opponent, BlueEyes, 2, BattlePosition.Attack, true);
                PlaceSetTrap(e, e.Opponent, Mst, 0);
                var sc = PutInHand(e, e.Player, AmazonessSpellcaster);
                var activated = e.TryActivateSpellTrap(e.Player, sc, fromHand: true);
                for (var i = 0; i < 4 && e.IsAwaitingEffectTarget; i++)
                    e.TrySelectEffectTarget(Legal(e).FirstOrDefault());
                var window = e.IsAwaitingResponse;
                be.FaceUp = false;
                Drive(e, _ => false);
                be.FaceUp = true;
                FieldSpellEffects.RefreshBoard(e);
                Check("Spellcaster: a target turned face-down before resolution leaves both original ATK unchanged",
                    activated && window && fighter.CurrentAtk == 1500 && be.CurrentAtk == 3000 &&
                    e.Player.Graveyard.Contains(sc),
                    $"activated={activated} window={window} f={fighter.CurrentAtk} be={be.CurrentAtk}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, AmazonessFighter, 2, BattlePosition.Defense, false);
                PlaceMonster(e, e.Player, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, BlueEyes, 2, BattlePosition.Attack, true);
                var sc = PutInHand(e, e.Player, AmazonessSpellcaster);
                var noOwn = e.CanActivateSpellTrap(e.Player, sc, fromHand: true);
                var e2 = Setup(db, pDeck, aDeck);
                PlaceMonster(e2, e2.Player, AmazonessFighter, 2, BattlePosition.Attack, true);
                PlaceMonster(e2, e2.Opponent, BlueEyes, 2, BattlePosition.Defense, false);
                var sc2 = PutInHand(e2, e2.Player, AmazonessSpellcaster);
                var noOpp = e2.CanActivateSpellTrap(e2.Player, sc2, fromHand: true);
                Check("Spellcaster: cannot be activated without a face-up \"Amazoness\" or a face-up opponent monster",
                    !noOwn && !noOpp, $"noOwn={noOwn} noOpp={noOpp}");
            }
        }

        // ───────────────────────────── Rivalry of Warlords ─────────────────────────────

        static void RivalryDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var rivalry = PlaceSetTrap(e, p, RivalryOfWarlords, 0);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var soldier = PlaceMonster(e, p, GiantSoldier, 2, BattlePosition.Attack, true);
            var setKuri = PlaceMonster(e, p, Kuriboh, 3, BattlePosition.Defense, false);
            var be = PlaceMonster(e, o, BlueEyes, 2, BattlePosition.Attack, true);
            var oppKuri = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Attack, true);

            var activated = e.TryActivateSpellTrap(p, rivalry, fromHand: false);
            for (var i = 0; i < 6 && e.IsAwaitingResponse; i++) e.PassResponse();
            var asked = e.IsAwaitingEffectTarget;
            var offered = Legal(e);
            var picked = asked && e.TrySelectEffectTarget(soldier);
            Drive(e, _ => false);
            Check("Rivalry: on resolution the player picks a Type to keep; other face-up monsters of both players go to the GY",
                activated && asked && picked && offered.Contains(celtic) && offered.Contains(soldier) &&
                !offered.Contains(setKuri) &&
                p.Graveyard.Contains(celtic) && p.TryFindMonster(soldier, out _) && p.TryFindMonster(setKuri, out _) &&
                o.Graveyard.Contains(oppKuri) && o.TryFindMonster(be, out _) && rivalry.FaceUp,
                $"activated={activated} asked={asked} picked={picked} offered=[{string.Join(",", offered.Select(c => c.Name))}] " +
                $"pGy=[{string.Join(",", p.Graveyard.Select(c => c.Name))}] oGy=[{string.Join(",", o.Graveyard.Select(c => c.Name))}]");

            var warrior = PutInHand(e, p, Celtic);
            var nsBlocked = !e.TryNormalSummon(p, warrior, asSet: false);
            var flipBlocked = !e.TryFlipSummon(p, setKuri);
            var setOk = e.TryNormalSummon(p, warrior, asSet: true);
            Check("Rivalry: a monster of another Type cannot be Normal or Flip Summoned; it can be Set",
                nsBlocked && flipBlocked && setOk && !warrior.FaceUp && setKuri.FaceUp == false,
                $"nsBlocked={nsBlocked} flipBlocked={flipBlocked} setOk={setOk}");

            // Turned face-up by an effect: the kept Type is still there, so no choice — the newcomer is sent.
            setKuri.FaceUp = true;
            setKuri.Position = BattlePosition.Attack;
            FieldSpellEffects.RefreshBoard(e);
            Check("Rivalry: a monster of another Type turned face-up is sent to the GY at once (kept Type, no prompt)",
                !e.IsAwaitingEffectTarget && p.Graveyard.Contains(setKuri) && p.TryFindMonster(soldier, out _),
                $"await={e.IsAwaitingEffectTarget} pGy=[{string.Join(",", p.Graveyard.Select(c => c.Name))}]");

            // Lua e4 EFFECT_FORCE_SPSUMMON_POSITION(POS_FACEDOWN) masks the positions a Special Summon may use
            // (sumpos &= value; none left = cannot be Special Summoned). Monster Reborn summons POS_FACEUP, so an
            // off-Type monster is not a legal target and is never summoned; Enneacraft's "cannot Special Summon …,
            // except in face-down Defense Position" is the same code. A face-down Special Summon is allowed and stays.
            var rockGy = e.CreateCardInstance(GiantSoldier);
            p.Graveyard.Add(rockGy);
            var reborn = PutInHand(e, p, MonsterReborn);
            var rbActivated = e.TryActivateSpellTrap(p, reborn, fromHand: true);
            var rbOffered = Legal(e);
            var rbPicked = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(rockGy);
            Drive(e, _ => false);
            Check("Rivalry: Monster Reborn may target a GY monster of the kept Type, not one of another Type",
                rbActivated && rbPicked && rbOffered.Contains(rockGy) && !rbOffered.Contains(celtic) &&
                !rbOffered.Contains(oppKuri) && p.TryFindMonster(rockGy, out _) && rockGy.FaceUp &&
                p.Graveyard.Contains(celtic),
                $"activated={rbActivated} picked={rbPicked} offered=[{string.Join(",", rbOffered.Select(c => c.Name))}] " +
                $"pGy=[{string.Join(",", p.Graveyard.Select(c => c.Name))}]");

            var faceUpLegal = SpecialSummonLegality.CanBeSpecialSummoned(e, p, celtic, reborn, SummonOrigin.Graveyard);
            // Major Riot's hand summon is face-down Defense only (Lua POS_FACEDOWN_DEFENSE): an off-Type card qualifies.
            var faceDownLegal = SpecialSummonLegality.CanBeSpecialSummoned(e, p, celtic, reborn, SummonOrigin.Graveyard,
                faceUp: false);
            var ssUp = e.SpecialSummonToField(p, celtic, BattlePosition.Attack, true);
            var upStayedInGy = p.Graveyard.Contains(celtic) && !p.TryFindMonster(celtic, out _);
            p.Graveyard.Remove(celtic);
            var ssDown = e.SpecialSummonToField(p, celtic, BattlePosition.Defense, false);
            FieldSpellEffects.RefreshBoard(e);
            Check("Rivalry: another Type cannot be Special Summoned face-up; face-down Defense is allowed and is not sent",
                !faceUpLegal && faceDownLegal && !ssUp && upStayedInGy && ssDown && p.TryFindMonster(celtic, out _) && !celtic.FaceUp &&
                celtic.Position == BattlePosition.Defense && !e.IsAwaitingEffectTarget,
                $"faceUpLegal={faceUpLegal} faceDownLegal={faceDownLegal} ssUp={ssUp} upStayedInGy={upStayedInGy} ssDown={ssDown} " +
                $"faceUp={celtic.FaceUp} pos={celtic.Position}");

            // Off the field, the rule and the lock are gone.
            var e3 = Setup(db, pDeck, aDeck);
            PlaceMonster(e3, e3.Player, GiantSoldier, 2, BattlePosition.Attack, true);
            var w3 = PutInHand(e3, e3.Player, Celtic);
            var free = e3.TryNormalSummon(e3.Player, w3, asSet: false);
            FieldSpellEffects.RefreshBoard(e3);
            Check("Rivalry: without it, two Types stay and summon freely",
                free && e3.Player.TryFindMonster(w3, out _) && e3.Player.Graveyard.Count == 0, $"free={free}");
        }
    }
}
