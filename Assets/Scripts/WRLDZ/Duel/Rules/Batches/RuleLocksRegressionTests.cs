using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "RuleLocks" (<see cref="RuleLocksAtoms"/>, <see cref="RuleLocksRuntime"/>): continuous rule locks read
    /// at the DuelEngine entry points and legality checks.
    /// <list type="bullet">
    /// <item>Jowgen the Spiritualist: neither player can Special Summon (Lua EFFECT_CANNOT_SPECIAL_SUMMON 1,1); ignition
    /// — discard 1 random card, destroy every Special Summoned monster on the field (itself too).</item>
    /// <item>Jam Breeding Machine: mandatory Slime Token in its controller's Standby Phase; its controller cannot Normal,
    /// Flip or Special Summon except Slime Tokens, but can Set.</item>
    /// <item>Royal Command: the activated effects of Flip monsters are negated (FLIP and other triggers); a Set or negated
    /// Royal Command does nothing.</item>
    /// <item>Kycoo the Ghost Destroyer: on battle damage, target up to 2 monsters in the opponent's GY and banish them;
    /// its controller's opponent cannot banish cards from either GY (effects and costs).</item>
    /// <item>Chain Energy: each player pays 500 LP per face-up copy to Normal Summon, Special Summon, Set or activate a card
    /// from the hand, and cannot with less LP; refunded when a hand activation's target choice is cancelled.</item>
    /// </list>
    /// </summary>
    public static class RuleLocksRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Jowgen = 41855169;
        const int JamBreedingMachine = 21770260;
        const int RoyalCommand = 33950246;
        const int Kycoo = 88240808;
        const int MonsterReborn = 83764718;
        const int SoulRelease = 5758500;
        const int SpecialHurricane = 42598242;
        const int ImmortalOfThunder = 84926738;   // FLIP: gain 3000 LP; sent from the field to the GY: lose 5000 LP
        const int OldVindictiveMagician = 45141844; // FLIP: target 1 monster your opponent controls; destroy it
        const int CelticGuardian = 91152256;      // Normal 1400/1200
        const int GiantSoldierOfStone = 13039848; // Normal 1300/2000
        const int BlueEyes = 89631139;            // Normal 3000/2500
        const int ChainEnergy = 79323590;
        const int DianKeto = 84257640;            // Normal Spell: gain 1000 LP
        const int FiendMegacyber = 66362965;      // procedure from the hand (no Chain Link)

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("RuleLocks: " + name, ok, detail ?? "");

            void Guard(string label, Action body)
            {
                try { body(); }
                catch (Exception ex) { Check($"{label}: no exception", false, ex.GetType().Name + ": " + ex.Message); }
            }

            Guard("shapes", () => CompileShapes(db, Check));
            Guard("jowgen", () => JowgenDuels(db, pDeck, aDeck, Check));
            Guard("jam", () => JamDuels(db, pDeck, aDeck, Check));
            Guard("royal command", () => RoyalCommandDuels(db, pDeck, aDeck, Check));
            Guard("kycoo", () => KycooDuels(db, pDeck, aDeck, Check));
            Guard("chain energy", () => ChainEnergyDuels(db, pDeck, aDeck, Check));
            Guard("cross-group", () => CrossGroupDuels(db, pDeck, aDeck, Check));
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Prog(int id) => db.Get(id) is { } d ? CardTextEffectCompiler.Compile(d) : null;
            CardEffectStatusKind Status(int id) =>
                db.Get(id) is { } d ? CardEffectStatus.Classify(d) : CardEffectStatusKind.Unimplemented;

            var jw = Prog(Jowgen);
            Check("Jowgen: Implemented — random-discard ignition destroying Special Summoned monsters + Special Summon lock",
                Status(Jowgen) == CardEffectStatusKind.Implemented && jw.FullyCompiled && jw.ClauseList.Count == 2 &&
                jw.ClauseList.Exists(c => c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.DestroySpecialSummonedMonsters &&
                                          c.RequiresDiscardCost && c.DiscardCostRandom && c.DiscardCostAttribute == "*" &&
                                          c.ActivationCondition == RuleLocksRuntime.SpecialSummonedMonsterOnField &&
                                          !c.RequiresTargetChoice) &&
                jw.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                          c.Action == EffectActionKind.NeitherPlayerCanSpecialSummon),
                Describe(jw));

            var jam = Prog(JamBreedingMachine);
            Check("Jam Breeding Machine: Implemented — one mandatory Standby Slime Token + controller summon lock",
                Status(JamBreedingMachine) == CardEffectStatusKind.Implemented && jam.FullyCompiled &&
                jam.ClauseList.Count == 2 &&
                jam.ClauseList.Exists(c => c.Timing == EffectTiming.StandbyPhase &&
                                           c.Action == EffectActionKind.SpecialSummonToken && !c.IsOptional &&
                                           !c.OpponentTurnOnly && c.TokenName == "Slime Token" && c.TokenRace == "Aqua" &&
                                           c.TokenAttribute == "WATER" && c.TokenLevel == 1 && c.TokenAtk == 500 &&
                                           c.TokenDef == 500 && c.TokenCount == 1 && !c.SummonInDefense &&
                                           !c.TokenToOpponent) &&
                jam.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                           c.Action == EffectActionKind.ControllerCannotSummonExceptNamed &&
                                           c.NamedCard == "Slime Token"),
                Describe(jam));

            var rc = Prog(RoyalCommand);
            Check("Royal Command: Implemented — continuous negation of Flip monsters' activations",
                Status(RoyalCommand) == CardEffectStatusKind.Implemented && rc.FullyCompiled && rc.ClauseList.Count == 1 &&
                rc.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                rc.ClauseList[0].Action == EffectActionKind.NegateFlipMonsterEffectActivations,
                Describe(rc));

            var ky = Prog(Kycoo);
            Check("Kycoo: Implemented — optional battle-damage trigger banishing up to 2 opp GY monsters + banish lock",
                Status(Kycoo) == CardEffectStatusKind.Implemented && ky.FullyCompiled && ky.ClauseList.Count == 2 &&
                ky.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                          c.Action == EffectActionKind.Banish && c.Zone == EffectZoneFilter.OppGyMonsters &&
                                          c.Amount == 2 && c.RequiresTargetChoice && c.IsPsctTarget && c.IsOptional &&
                                          !c.RequiresDirectAttack) &&
                ky.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                          c.Action == EffectActionKind.OpponentCannotBanishFromGraveyards),
                Describe(ky));

            var ce = Prog(ChainEnergy);
            Check("Chain Energy: Implemented — a continuous 500 LP toll on hand actions, no bare LP payment",
                Status(ChainEnergy) == CardEffectStatusKind.Implemented && ce.FullyCompiled && ce.ClauseList.Count == 1 &&
                ce.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                ce.ClauseList[0].Action == EffectActionKind.HandActionsCostLp && ce.ClauseList[0].Amount == 500 &&
                ce.ClauseList[0].PayLpAmount == 0,
                Describe(ce));

            // Near misses: the look-alike sentences do not take these shapes.
            var sh = Prog(SpecialHurricane);
            Check("Near miss: Special Hurricane (Spell, mandatory discard) is not compiled by the Jowgen atom",
                sh != null && !sh.ClauseList.Exists(c => c.DiscardCostRandom ||
                                                         c.Action == EffectActionKind.NeitherPlayerCanSpecialSummon),
                Describe(sh));

            var jowgen = db.Get(Jowgen);
            foreach (var (text, label) in new[]
                     {
                         ("You can discard 1 card from your hand to the Graveyard; destroy all Special Summoned monsters " +
                          "on the field. Neither player can Special Summon monsters.", "a chosen (not random) discard"),
                         ("You can discard 1 random card from your hand to the Graveyard; destroy all Special Summoned " +
                          "monsters on the field.", "no Special Summon lock sentence"),
                         ("Neither player can Special Summon monsters during the Battle Phase.", "a conditional lock"),
                     })
            {
                var p = CardTextEffectCompiler.Compile(Synthetic(jowgen, text));
                Check($"Near miss: Jowgen text with {label} does not compile to the RuleLocks shapes",
                    p != null && !p.ClauseList.Exists(c => c.DiscardCostRandom ||
                                                           c.Action == EffectActionKind.NeitherPlayerCanSpecialSummon),
                    Describe(p));
            }

            var jamDef = db.Get(JamBreedingMachine);
            foreach (var (text, label) in new[]
                     {
                         ("During each of your Standby Phases: Special Summon 1 \"Slime Token\" (Aqua-Type/WATER/LEVEL 1/ATK " +
                          "500/DEF 500) in Attack Position. You cannot Summon any monsters, except \"Slime Tokens\".",
                             "no \"(but you can Set)\""),
                         ("During each of your Standby Phases: Special Summon 1 \"Slime Token\" (Aqua-Type/WATER/LEVEL 1/ATK " +
                          "500/DEF 500) in Attack Position.", "no summon lock"),
                     })
            {
                var p = CardTextEffectCompiler.Compile(Synthetic(jamDef, text));
                Check($"Near miss: Jam text with {label} does not compile the summon lock",
                    p != null && !p.ClauseList.Exists(c => c.Action == EffectActionKind.ControllerCannotSummonExceptNamed),
                    Describe(p));
            }

            var kyDef = db.Get(Kycoo);
            var kyNear = CardTextEffectCompiler.Compile(Synthetic(kyDef,
                "Your opponent cannot banish cards from your Graveyard."));
            Check("Near miss: a one-GY banish lock does not compile to Kycoo's either-GY lock",
                kyNear != null && !kyNear.ClauseList.Exists(c => c.Action == EffectActionKind.OpponentCannotBanishFromGraveyards),
                Describe(kyNear));

            var rcNear = CardTextEffectCompiler.Compile(Synthetic(db.Get(RoyalCommand),
                "Flip monsters' effects cannot be activated."));
            var ceDef = db.Get(ChainEnergy);
            foreach (var (text, label) in new[]
                     {
                         ("Each player must pay 500 Life Points to Normal Summon, Special Summon, Set or activate cards " +
                          "from his/her respective hand.", "no \"per card\""),
                         ("Each player must pay 500 Life Points per card to Normal Summon or Set cards from his/her " +
                          "respective hand.", "fewer hand actions"),
                         ("Your opponent must pay 500 Life Points per card to Normal Summon, Special Summon, Set or " +
                          "activate cards from his/her hand.", "one player only"),
                     })
            {
                var p = CardTextEffectCompiler.Compile(Synthetic(ceDef, text));
                Check($"Near miss: Chain Energy text with {label} does not compile the hand toll",
                    p != null && !p.ClauseList.Exists(c => c.Action == EffectActionKind.HandActionsCostLp),
                    Describe(p));
            }

            Check("Near miss: a shortened Royal Command text does not compile",
                rcNear != null && !rcNear.ClauseList.Exists(c => c.Action == EffectActionKind.NegateFlipMonsterEffectActivations),
                Describe(rcNear));
        }

        // ───────────────────────────── Jowgen the Spiritualist ─────────────────────────────

        static void JowgenDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The ignition: a random hand card is discarded (queued pick 1), every Special Summoned monster is destroyed.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                var jw = PlaceMonster(e, p, Jowgen, 0, BattlePosition.Attack, true);
                var ss = PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                ss.WasSpecialSummoned = true;
                var ns = PlaceMonster(e, o, GiantSoldierOfStone, 1, BattlePosition.Defense, true);
                var keep = PutInHand(e, p, BlueEyes);
                var gone = PutInHand(e, p, CelticGuardian);
                e.Rng.QueuePick(1);
                var can = e.CanActivateSpellTrap(p, jw, fromHand: false);
                var ok = can && e.TryActivateSpellTrap(p, jw, fromHand: false);
                Drain(e);
                Check("Jowgen: ignition discards the random pick, destroys the Special Summoned monster only",
                    ok && !e.IsAwaitingEffectTarget && p.Graveyard.Contains(gone) && p.Hand.Contains(keep) &&
                    o.Graveyard.Contains(ss) && o.TryFindMonster(ns, out _) && p.TryFindMonster(jw, out _),
                    $"can={can} ok={ok} gone={Where(e, gone)} keep={Where(e, keep)} ss={Where(e, ss)} ns={Where(e, ns)} " +
                    $"jw={Where(e, jw)} awaiting={e.IsAwaitingEffectTarget}");

                // No once-per-turn (Lua e2 has no count limit): with another Special Summoned monster it can go again.
                var again = PlaceMonster(e, o, BlueEyes, 2, BattlePosition.Attack, true);
                again.WasSpecialSummoned = true;
                var second = e.CanActivateSpellTrap(p, jw, fromHand: false) &&
                             e.TryActivateSpellTrap(p, jw, fromHand: false);
                Drain(e);
                Check("Jowgen: the ignition has no once-per-turn limit",
                    second && o.Graveyard.Contains(again) && p.Graveyard.Contains(keep) && p.Hand.Count == 0,
                    $"second={second} again={Where(e, again)} keep={Where(e, keep)}");
            }

            // A Special Summoned Jowgen destroys itself too (Lua: no exception in the destroyed group).
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out _);
                var jw = PlaceMonster(e, p, Jowgen, 0, BattlePosition.Attack, true);
                jw.WasSpecialSummoned = true;
                PutInHand(e, p, BlueEyes);
                var ok = e.TryActivateSpellTrap(p, jw, fromHand: false);
                Drain(e);
                Check("Jowgen: a Special Summoned Jowgen is destroyed by its own effect",
                    ok && p.Graveyard.Contains(jw), $"ok={ok} jw={Where(e, jw)}");
            }

            // No Special Summoned monster on the field, or an empty hand: it cannot be activated.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                var jw = PlaceMonster(e, p, Jowgen, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                PutInHand(e, p, BlueEyes);
                var noTarget = e.CanActivateSpellTrap(p, jw, fromHand: false);
                var ss = PlaceMonster(e, o, GiantSoldierOfStone, 1, BattlePosition.Attack, true);
                ss.WasSpecialSummoned = true;
                p.Hand.Clear();
                var noHand = e.CanActivateSpellTrap(p, jw, fromHand: false);
                PutInHand(e, p, BlueEyes);
                var legal = e.CanActivateSpellTrap(p, jw, fromHand: false);
                Check("Jowgen: needs a Special Summoned monster on the field and a card in hand",
                    !noTarget && !noHand && legal, $"noTarget={noTarget} noHand={noHand} legal={legal}");
            }

            // The lock: neither player can Special Summon while it is face-up and not negated.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                p.Graveyard.Add(e.CreateCardInstance(CelticGuardian));
                var reborn = PutInHand(e, p, MonsterReborn);
                var before = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                var jw = PlaceMonster(e, o, Jowgen, 0, BattlePosition.Attack, true);
                var locked = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                var mine = e.CreateCardInstance(GiantSoldierOfStone);
                var ssMine = e.SpecialSummonToField(p, mine, BattlePosition.Attack, true);
                var theirs = e.CreateCardInstance(GiantSoldierOfStone);
                var ssTheirs = e.SpecialSummonToField(o, theirs, BattlePosition.Attack, true);
                Check("Jowgen: Monster Reborn cannot be activated; no Special Summon on either side",
                    before && !locked && !ssMine && !ssTheirs && !p.TryFindMonster(mine, out _) &&
                    !o.TryFindMonster(theirs, out _),
                    $"before={before} locked={locked} ssMine={ssMine} ssTheirs={ssTheirs}");

                jw.IsNegated = true;
                var negated = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                jw.IsNegated = false;
                jw.FaceUp = false;
                jw.Position = BattlePosition.Defense;
                var faceDown = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                Check("Jowgen: negated or face-down, it locks nothing",
                    negated && faceDown, $"negated={negated} faceDown={faceDown}");
            }
        }

        // ───────────────────────────── Jam Breeding Machine ─────────────────────────────

        static void JamDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The lock: no Normal / Flip / Special Summon for its controller (Slime Tokens excepted); Set is legal.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                var jam = PlaceFaceUpSpellTrap(e, p, JamBreedingMachine, 0);
                var celtic = PutInHand(e, p, CelticGuardian);
                var snap = LegalIntentService.Build(e, p);
                Check("Jam: the legal intents offer Set, not Summon (LegalIntentService)",
                    !snap.HasKind(celtic, LegalIntentService.LegalKind.NormalSummonAtk) &&
                    snap.HasKind(celtic, LegalIntentService.LegalKind.SetMonsterDef),
                    string.Join(",", snap.ForCard(celtic).Select(x => x.Kind.ToString())));
                var ns = SummonProcedures.CheckNormalOrTribute(e, p, celtic, asSet: false).Legal;
                var setLegal = SummonProcedures.CheckNormalOrTribute(e, p, celtic, asSet: true).Legal;
                var set = e.TryNormalSummon(p, celtic, asSet: true);
                Check("Jam: its controller cannot Normal Summon but can Set",
                    !ns && setLegal && set && p.TryFindMonster(celtic, out _) && !celtic.FaceUp,
                    $"ns={ns} setLegal={setLegal} set={set} celtic={Where(e, celtic)} faceUp={celtic.FaceUp}");

                var down = PlaceMonster(e, p, GiantSoldierOfStone, 2, BattlePosition.Defense, false);
                var flip = e.CanFlipSummon(p, down);
                var tribute = PutInHand(e, p, BlueEyes);
                var tributeSummon = SummonProcedures.CheckNormalOrTribute(e, p, tribute, asSet: false).Legal;
                var mon = e.CreateCardInstance(CelticGuardian);
                var ssMon = e.SpecialSummonToField(p, mon, BattlePosition.Attack, true);
                var slime = e.CreateToken("Slime Token", "Aqua", "WATER", 1, 500, 500);
                var ssSlime = e.SpecialSummonToField(p, slime, BattlePosition.Attack, true);
                var other = e.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                var ssOther = e.SpecialSummonToField(p, other, BattlePosition.Defense, true);
                p.Graveyard.Add(e.CreateCardInstance(CelticGuardian));
                var reborn = PutInHand(e, p, MonsterReborn);
                var rebornLegal = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                var oppMon = e.CreateCardInstance(CelticGuardian);
                var ssOpp = e.SpecialSummonToField(o, oppMon, BattlePosition.Attack, true);
                Check("Jam: no Flip / Tribute / Special Summon except a Slime Token; the opponent is not locked",
                    !flip && !tributeSummon && !ssMon && ssSlime && !ssOther && !rebornLegal && ssOpp &&
                    p.TryFindMonster(slime, out _) && o.TryFindMonster(oppMon, out _),
                    $"flip={flip} tribute={tributeSummon} ssMon={ssMon} ssSlime={ssSlime} ssOther={ssOther} " +
                    $"reborn={rebornLegal} ssOpp={ssOpp}");

                jam.IsNegated = true;
                var flipNegated = e.CanFlipSummon(p, down);
                Check("Jam: negated, it locks nothing", flipNegated, $"flip={flipNegated}");
            }

            // The Standby Token: one face-up Attack Position Slime Token in each of its controller's Standby Phases only.
            {
                var e = MainOf(db, pDeck, aDeck, player: false, out var p, out _);
                PlaceFaceUpSpellTrap(e, p, JamBreedingMachine, 0);
                var mine = ReachMain(e, p);
                var tokens = Slimes(p);
                Check("Jam: its controller's Standby Phase Special Summons 1 Slime Token (Aqua/WATER/1/500/500, ATK)",
                    mine && tokens.Count == 1 && tokens[0].FaceUp && tokens[0].Position == BattlePosition.Attack &&
                    tokens[0].IsToken && tokens[0].CurrentAtk == 500 && tokens[0].CurrentDef == 500 &&
                    tokens[0].Def.attribute == "WATER" && tokens[0].Def.race == "Aqua" && tokens[0].Level == 1,
                    $"reached={mine} tokens={Names(tokens)}");
                var theirs = ReachMain(e, e.Opponent);
                Check("Jam: no Token in the opponent's Standby Phase",
                    theirs && Slimes(p).Count == 1 && Slimes(e.Opponent).Count == 0,
                    $"reached={theirs} mine={Slimes(p).Count} theirs={Slimes(e.Opponent).Count}");
            }

            // Under Jowgen the Token cannot be Special Summoned.
            {
                var e = MainOf(db, pDeck, aDeck, player: false, out var p, out var o);
                PlaceFaceUpSpellTrap(e, p, JamBreedingMachine, 0);
                PlaceMonster(e, o, Jowgen, 0, BattlePosition.Attack, true);
                var mine = ReachMain(e, p);
                Check("Jam + Jowgen: no Slime Token is Special Summoned",
                    mine && Slimes(p).Count == 0, $"reached={mine} tokens={Slimes(p).Count}");
            }
        }

        static List<CardInstance> Slimes(DuelistState who) =>
            who.MonstersOnField().Where(m => m != null && m.IsToken && m.IsNamed("Slime Token")).ToList();

        // ───────────────────────────── Royal Command ─────────────────────────────

        static void RoyalCommandDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // FLIP gain and the sent-to-GY loss of The Immortal of Thunder, with / without / negated Royal Command.
            (int afterFlip, int afterGy) Run(int royalState)
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                if (royalState > 0)
                {
                    var rc = PlaceFaceUpSpellTrap(e, o, RoyalCommand, 0);
                    if (royalState == 2) rc.IsNegated = true;
                    if (royalState == 3) rc.FaceUp = false;
                }
                var imm = PlaceMonster(e, p, ImmortalOfThunder, 0, BattlePosition.Defense, false);
                var lp = p.LifePoints;
                e.TryFlipSummon(p, imm);
                Drain(e);
                var afterFlip = p.LifePoints - lp;
                lp = p.LifePoints;
                e.DestroyMonsterPublic(p, imm);
                Drain(e);
                return (afterFlip, p.LifePoints - lp);
            }

            var none = Run(0);
            var royal = Run(1);
            var negated = Run(2);
            var set = Run(3);
            Check("Royal Command: control — The Immortal of Thunder gains 3000 on FLIP, loses 5000 when sent to the GY",
                none == (3000, -5000), $"{none}");
            Check("Royal Command: both activated effects of the Flip monster are negated",
                royal == (0, 0), $"{royal}");
            Check("Royal Command: negated or Set, it negates nothing",
                negated == (3000, -5000) && set == (3000, -5000), $"negated={negated} set={set}");

            // A targeting FLIP (Old Vindictive Magician): no target prompt, the opponent's monster stays.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                PlaceFaceUpSpellTrap(e, o, RoyalCommand, 0);
                var ovm = PlaceMonster(e, p, OldVindictiveMagician, 0, BattlePosition.Defense, false);
                var victim = PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(p, ovm);
                var prompt = e.IsAwaitingEffectTarget;
                Drain(e);
                Check("Royal Command: a targeting FLIP gets no target prompt and destroys nothing",
                    flipped && ovm.FaceUp && !prompt && o.TryFindMonster(victim, out _),
                    $"flipped={flipped} prompt={prompt} victim={Where(e, victim)}");
            }

            // A non-Flip monster's trigger is untouched (Sangan-style is not needed: Jowgen's ignition still works).
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                PlaceFaceUpSpellTrap(e, o, RoyalCommand, 0);
                var jw = PlaceMonster(e, p, Jowgen, 0, BattlePosition.Attack, true);
                var ss = PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                ss.WasSpecialSummoned = true;
                PutInHand(e, p, BlueEyes);
                var ok = e.TryActivateSpellTrap(p, jw, fromHand: false);
                Drain(e);
                Check("Royal Command: a non-Flip monster's effect still resolves",
                    ok && o.Graveyard.Contains(ss), $"ok={ok} ss={Where(e, ss)}");
            }
        }

        // ───────────────────────────── Kycoo the Ghost Destroyer ─────────────────────────────

        static void KycooDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI's Kycoo inflicts battle damage: it banishes 2 of the 3 monsters in the player's GY.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                ClearPiles(e);
                var reached = ReachOpponentBattle(e);
                var p = e.Player;
                var o = e.Opponent;
                ClearBoard(e);
                var ky = PlaceMonster(e, o, Kycoo, 0, BattlePosition.Attack, true);
                var gy = new[] { CelticGuardian, GiantSoldierOfStone, BlueEyes }
                    .Select(id => { var c = e.CreateCardInstance(id); p.Graveyard.Add(c); return c; }).ToList();
                var lp = p.LifePoints;
                var attacked = e.TryAttack(o, ky, null);
                DrainCombat(e);
                Check("Kycoo (AI): battle damage banishes 2 of the opponent's GY monsters",
                    reached && attacked && p.LifePoints == lp - 1800 && gy.Count(c => p.Banished.Contains(c)) == 2 &&
                    gy.Count(c => p.Graveyard.Contains(c)) == 1 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} attacked={attacked} lp {lp}->{p.LifePoints} gy={string.Join(",", gy.Select(c => Where(e, c)))}");
            }

            // The player's Kycoo: up to 2 picks; Cancel after 1 banishes that one; Cancel with none declines.
            foreach (var picks in new[] { 1, 0, 2 })
            {
                var e = Fresh(db, pDeck, aDeck);
                if (e.IsAwaitingResponse) e.PassResponse();
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, o) && ReachMain(e, p);
                ClearBoard(e);
                ClearPiles(e);
                var ky = PlaceMonster(e, p, Kycoo, 0, BattlePosition.Attack, true);
                var gy = new[] { CelticGuardian, GiantSoldierOfStone, BlueEyes }
                    .Select(id => { var c = e.CreateCardInstance(id); o.Graveyard.Add(c); return c; }).ToList();
                var mineGy = e.CreateCardInstance(CelticGuardian);
                p.Graveyard.Add(mineGy);
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var lp = o.LifePoints;
                var attacked = e.TryAttack(p, ky, null);
                for (var i = 0; i < 8 && !e.IsAwaitingEffectTarget; i++)
                {
                    if (e.IsAwaitingResponse) e.PassResponse();
                    else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                }

                var prompt = e.IsAwaitingEffectTarget;
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                var offer = prompt && legal.Count == 3 && gy.All(legal.Contains) && !legal.Contains(mineGy);
                for (var k = 0; k < picks; k++)
                    e.TrySelectEffectTarget(gy[k]);
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                DrainCombat(e);
                var banished = gy.Count(c => o.Banished.Contains(c));
                Check($"Kycoo (player): the prompt offers the 3 opp GY monsters; {picks} pick(s) → {picks} banished",
                    reached && bp && attacked && o.LifePoints == lp - 1800 && offer && banished == picks &&
                    p.Graveyard.Contains(mineGy) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} bp={bp} attacked={attacked} lp {lp}->{o.LifePoints} prompt={prompt} " +
                    $"legal={Names(legal)} banished={banished}");
            }

            // The lock: Kycoo's controller's opponent cannot banish from either GY (effect or cost); its controller can.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                var ky = PlaceMonster(e, o, Kycoo, 0, BattlePosition.Attack, true);
                var inMine = e.CreateCardInstance(CelticGuardian);
                p.Graveyard.Add(inMine);
                var inTheirs = e.CreateCardInstance(GiantSoldierOfStone);
                o.Graveyard.Add(inTheirs);
                var release = PutInHand(e, p, SoulRelease);
                var releaseLocked = e.CanActivateSpellTrap(p, release, fromHand: true);
                e.BanishCard(p, inMine, by: p);
                e.BanishCard(o, inTheirs, by: p);
                var stayed = p.Graveyard.Contains(inMine) && o.Graveyard.Contains(inTheirs);
                e.BanishCard(p, inMine, by: o);
                var ownerMay = p.Banished.Contains(inMine);
                ky.IsNegated = true;
                var releaseNegated = e.CanActivateSpellTrap(p, release, fromHand: true);
                Check("Kycoo: its opponent cannot banish from either GY (Soul Release illegal); its controller can",
                    !releaseLocked && stayed && ownerMay && releaseNegated,
                    $"releaseLocked={releaseLocked} stayed={stayed} ownerMay={ownerMay} releaseNegated={releaseNegated}");
            }
        }

        // ───────────────────────────── Chain Energy ─────────────────────────────

        static void ChainEnergyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Normal Summon, Set (monster and Spell), Spell activation from the hand: 500 LP each; Flip Summon and a Set
            // card activated on the field: free.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out _);
                var energy = PlaceFaceUpSpellTrap(e, p, ChainEnergy, 0);
                p.LifePoints = 8000;
                var celtic = PutInHand(e, p, CelticGuardian);
                var ns = e.TryNormalSummon(p, celtic, asSet: false);
                var afterNs = p.LifePoints;
                var keto = PutInHand(e, p, DianKeto);
                var act = e.TryActivateSpellTrap(p, keto, fromHand: true);
                Drain(e);
                var afterAct = p.LifePoints;
                var reborn = PutInHand(e, p, MonsterReborn);
                var setSt = e.TrySetSpellTrap(p, reborn);
                var afterSet = p.LifePoints;
                Check("Chain Energy: Normal Summon, a Spell from the hand and a Spell Set each cost 500 LP",
                    ns && afterNs == 7500 && act && afterAct == 7500 - 500 + 1000 && setSt && afterSet == 7500,
                    $"ns={ns} lp={afterNs} act={act} lp={afterAct} set={setSt} lp={afterSet}");

                var down = PlaceMonster(e, p, GiantSoldierOfStone, 2, BattlePosition.Defense, false);
                down.SetThisTurn = false;
                var flip = e.TryFlipSummon(p, down);
                Check("Chain Energy: a Flip Summon (not from the hand) costs nothing",
                    flip && down.FaceUp && p.LifePoints == 7500, $"flip={flip} lp={p.LifePoints}");

                // A second copy doubles it; a Set monster from the hand pays too.
                PlaceFaceUpSpellTrap(e, p, ChainEnergy, 1);
                var setMon = PutInHand(e, p, CelticGuardian);
                p.NormalSummonUsed = false;
                var mset = e.TryNormalSummon(p, setMon, asSet: true);
                Check("Chain Energy: two copies — Setting a monster costs 1000 LP",
                    mset && !setMon.FaceUp && p.LifePoints == 6500, $"set={mset} lp={p.LifePoints}");

                // Too little LP: nothing from the hand is legal, the AI's intents included.
                p.LifePoints = 900;
                var poor = PutInHand(e, p, CelticGuardian);
                var poorSpell = PutInHand(e, p, DianKeto);
                p.NormalSummonUsed = false;
                var canSummon = e.CanNormalSummonOrSet(p, poor);
                var canSet = e.CanSetSpellTrap(p, poorSpell);
                var canAct = e.CanActivateSpellTrap(p, poorSpell, fromHand: true);
                var tried = e.TryActivateSpellTrap(p, poorSpell, fromHand: true);
                var snap = LegalIntentService.Build(e, p);
                Check("Chain Energy: with less LP than the toll nothing can be Summoned, Set or activated from the hand",
                    !canSummon && !canSet && !canAct && !tried && p.LifePoints == 900 && p.Hand.Contains(poorSpell) &&
                    snap.ForCard(poor).Count() == 0 && snap.ForCard(poorSpell).Count() == 0,
                    $"summon={canSummon} set={canSet} act={canAct} tried={tried} lp={p.LifePoints} " +
                    $"intents={string.Join(",", snap.ForCard(poor).Concat(snap.ForCard(poorSpell)).Select(x => x.Kind.ToString()))}");

                energy.IsNegated = true;
                p.LifePoints = 900;
                var halfAct = e.CanActivateSpellTrap(p, poorSpell, fromHand: true);
                Check("Chain Energy: one copy negated — the toll is 500 (900 LP is enough)",
                    halfAct && RuleLocksRuntime.HandActionLpCost(e, p) == 500,
                    $"act={halfAct} toll={RuleLocksRuntime.HandActionLpCost(e, p)}");
            }

            // Special Summons: from the hand (The Fiend Megacyber's procedure) 500, from the GY (Monster Reborn) nothing;
            // the opponent pays as well.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                PlaceFaceUpSpellTrap(e, o, ChainEnergy, 0);
                p.LifePoints = 8000;
                o.LifePoints = 8000;
                PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, GiantSoldierOfStone, 1, BattlePosition.Attack, true);
                var mega = PutInHand(e, p, FiendMegacyber);
                var proc = e.TryActivateSpellTrap(p, mega, fromHand: true);
                Drain(e);
                Check("Chain Energy: a Special Summon from the hand by procedure costs 500 LP (no activation toll)",
                    proc && p.TryFindMonster(mega, out _) && p.LifePoints == 7500,
                    $"proc={proc} mega={Where(e, mega)} lp={p.LifePoints}");

                var dead = e.CreateCardInstance(BlueEyes);
                p.Graveyard.Add(dead);
                var reborn = PlaceSetTrap(e, p, MonsterReborn, 1);
                var rebornAct = e.TryActivateSpellTrap(p, reborn, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(dead);
                Drain(e);
                Check("Chain Energy: a Set Monster Reborn reviving from the GY costs nothing",
                    rebornAct && p.TryFindMonster(dead, out _) && p.LifePoints == 7500,
                    $"act={rebornAct} dead={Where(e, dead)} lp={p.LifePoints}");

                var oppHand = PutInHand(e, o, CelticGuardian);
                var direct = e.CreateCardInstance(CelticGuardian);
                e.MarkSpecialSummonFromHand(direct);
                var ssFromHand = e.SpecialSummonToField(o, direct, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                o.LifePoints = 400;
                e.MarkSpecialSummonFromHand(oppHand);
                o.Hand.Remove(oppHand);
                var ssPoor = e.SpecialSummonToField(o, oppHand, BattlePosition.Attack, true);
                Check("Chain Energy: its controller pays too; with less LP a Special Summon from the hand fails",
                    ssFromHand && oppLp == 7500 && !ssPoor && o.LifePoints == 400,
                    $"ss={ssFromHand} lp={oppLp} ssPoor={ssPoor} lpPoor={o.LifePoints}");
            }

            // The AI under Chain Energy: with the LP it pays as it summons; without it, it does not try (no soft-lock).
            foreach (var lp in new[] { 8000, 400 })
            {
                var e = MainOf(db, pDeck, aDeck, player: false, out var p, out var o);
                PlaceFaceUpSpellTrap(e, p, ChainEnergy, 0);
                o.LifePoints = lp;
                var celtic = PutInHand(e, o, CelticGuardian);
                SimpleAi.TakeTurn(e);
                var played = o.TryFindMonster(celtic, out _);
                Check($"Chain Energy: the AI at {lp} LP " + (lp >= 500 ? "plays its monster and pays 500" : "keeps it in hand"),
                    lp >= 500 ? played && o.LifePoints == lp - 500 : !played && o.Hand.Contains(celtic) && o.LifePoints == lp,
                    $"celtic={Where(e, celtic)} lp={o.LifePoints}");
            }

            // A hand activation whose target choice is cancelled gets its toll back.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out _);
                PlaceFaceUpSpellTrap(e, p, ChainEnergy, 0);
                p.LifePoints = 8000;
                p.Graveyard.Add(e.CreateCardInstance(CelticGuardian));
                p.Graveyard.Add(e.CreateCardInstance(GiantSoldierOfStone));
                var reborn = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                var awaiting = e.IsAwaitingEffectTarget;
                var paid = p.LifePoints;
                if (awaiting) e.CancelEffectTargeting();
                Check("Chain Energy: Monster Reborn from the hand pays 500, cancelling its target refunds it",
                    act && awaiting && paid == 7500 && p.LifePoints == 8000 && p.Hand.Contains(reborn),
                    $"act={act} awaiting={awaiting} paid={paid} lp={p.LifePoints} reborn={Where(e, reborn)}");
            }
        }

        // ─────────────────── cross-group (v159-lon-b integration audit) ───────────────────
        // Cards of the other v159-lon-b groups, written in parallel trees, meeting this group's locks and toll.

        const int SummonerOfIllusions = 14644902; // FLIP: Tribute 1 other monster, Special Summon 1 Fusion Monster
        const int GaiaDragonChampion = 66889139;
        const int DeFusion = 95286165;
        const int Polymerization = 24094653;
        const int FlameSwordsman = 45231177;      // "Flame Manipulator" + "Masaki the Legendary Swordsman"
        const int FlameManipulator = 34460851;
        const int Masaki = 44287299;
        const int ChosenOne = 21888494;
        const int MysticalElf = 15025844;
        const int Mst = 5318639;
        const int DarkHole = 53129443;
        const int CardOfSafeReturn = 57953380;
        const int ScrollOfBewitchment = 10352095;
        const int FireSorcerer = 27132350;        // FLIP (cost: banish 2 random hand cards): 800 damage

        static void CrossGroupDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Summoner of Illusions under Jowgen: no Fusion Monster can be Special Summoned (Lua rfilter
            // IsCanBeSpecialSummoned), so no monster is Tributed.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(GaiaDragonChampion);
                PlaceMonster(e, o, Jowgen, 0, BattlePosition.Attack, true);
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                var asked = e.IsAwaitingEffectTarget;
                Drain(e);
                Check("cross-group: Summoner of Illusions under Jowgen Tributes nothing (no Fusion Monster can be Special Summoned)",
                    !asked && p.TryFindMonster(celtic, out _) && p.ExtraDeck.Contains(GaiaDragonChampion),
                    $"asked={asked} celtic={Where(e, celtic)} extra={p.ExtraDeck.Count}");
            }

            // De-Fusion under Jowgen: the Fusion Monster returns; its materials cannot be Special Summoned, so none is offered.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                var fused = FuseFlameSwordsman(e, p, out var manip, out var masaki);
                PlaceMonster(e, o, Jowgen, 0, BattlePosition.Attack, true);
                var df = PutInHand(e, p, DeFusion);
                var act = e.TryActivateSpellTrap(p, df, fromHand: true);
                var offered = PickThenPass(e, fused);
                Drain(e);
                Check("cross-group: De-Fusion under Jowgen returns the Fusion Monster and offers no material Summon",
                    act && fused != null && p.ExtraDeck.Contains(FlameSwordsman) && !offered &&
                    p.Graveyard.Contains(manip) && p.Graveyard.Contains(masaki),
                    $"act={act} fused={fused?.Name} offered={offered} manip={Where(e, manip)}");
            }

            // Chosen One under Jowgen: no monster in the hand can be Special Summoned (Lua s.spfilter), so it cannot be
            // activated.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                PlaceMonster(e, o, Jowgen, 0, BattlePosition.Attack, true);
                var co = PutInHand(e, p, ChosenOne);
                PutInHand(e, p, MysticalElf);
                PutInHand(e, p, Mst);
                PutInHand(e, p, DarkHole);
                var can = e.CanActivateSpellTrap(p, co, fromHand: true);
                Check("cross-group: Chosen One cannot be activated under Jowgen", !can, $"can={can}");
            }

            // Chosen One under Chain Energy: its activation from the hand and the monster's Special Summon from the hand
            // each cost 500.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out _);
                PlaceFaceUpSpellTrap(e, p, ChainEnergy, 0);
                p.LifePoints = 8000;
                var co = PutInHand(e, p, ChosenOne);
                var elf = PutInHand(e, p, MysticalElf);
                var mst = PutInHand(e, p, Mst);
                var hole = PutInHand(e, p, DarkHole);
                e.Rng.QueuePick(0); // the monster is the card chosen at random
                var act = e.TryActivateSpellTrap(p, co, fromHand: true);
                for (var i = 0; i < 8; i++)
                {
                    if (e.IsAwaitingResponse)
                    {
                        e.PassResponse();
                        continue;
                    }

                    if (!e.IsAwaitingEffectTarget) break;
                    var lt = e.PendingActivation.LegalTargets;
                    var pick = lt.Contains(elf) ? elf : lt.Contains(mst) ? mst : lt.Contains(hole) ? hole : null;
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                }

                Check("cross-group: Chosen One under Chain Energy pays 500 to activate and 500 for the hand Special Summon",
                    act && p.TryFindMonster(elf, out _) && p.LifePoints == 7000,
                    $"act={act} elf={Where(e, elf)} lp={p.LifePoints}");
            }

            // Card of Safe Return and De-Fusion: both materials are Special Summoned at once (Lua Duel.SpecialSummon(mg)),
            // one EVENT_SPSUMMON_SUCCESS — Card of Safe Return draws 1, not 1 per material.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out _);
                var fused = FuseFlameSwordsman(e, p, out var manip, out var masaki);
                PlaceFaceUpSpellTrap(e, p, CardOfSafeReturn, 4);
                var df = PutInHand(e, p, DeFusion);
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, df, fromHand: true);
                var offered = PickThenPass(e, fused);
                if (offered) e.TrySelectEffectTarget(manip);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(masaki);
                Drain(e);
                Check("cross-group: De-Fusion Summons both materials as one event — Card of Safe Return draws 1",
                    act && offered && p.TryFindMonster(manip, out _) && p.TryFindMonster(masaki, out _) &&
                    p.Deck.Count == deck - 1,
                    $"act={act} offered={offered} manip={Where(e, manip)} deck {deck}->{p.Deck.Count}");
            }

            // Scroll of Bewitchment from the hand under Chain Energy: the toll paid as it began stays refundable after
            // the Attribute declaration, so cancelling the monster pick gives it back.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                PlaceFaceUpSpellTrap(e, p, ChainEnergy, 0);
                p.LifePoints = 8000;
                PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var scroll = PutInHand(e, p, ScrollOfBewitchment);
                var act = e.TryActivateSpellTrap(p, scroll, fromHand: true);
                var dark = e.PendingActivation?.LegalTargets.FirstOrDefault(t =>
                    t?.Def != null && string.Equals(t.Def.name, "DARK", StringComparison.OrdinalIgnoreCase));
                var declared = dark != null && e.TrySelectEffectTarget(dark);
                var monsterPick = e.IsAwaitingEffectTarget;
                var paid = p.LifePoints;
                if (monsterPick) e.CancelEffectTargeting();
                Check("cross-group: Scroll of Bewitchment under Chain Energy — cancelling the monster pick refunds the toll",
                    act && declared && monsterPick && paid == 7500 && p.Hand.Contains(scroll) && p.LifePoints == 8000,
                    $"act={act} declared={declared} pick={monsterPick} paid={paid} lp={p.LifePoints} scroll={Where(e, scroll)}");
            }

            // Royal Command and Fire Sorcerer: the FLIP effect's activation is negated after its cost is paid (Lua
            // EVENT_CHAIN_ACTIVATING), so 2 random hand cards are banished and no damage is dealt.
            {
                var e = MainOf(db, pDeck, aDeck, player: true, out var p, out var o);
                PlaceFaceUpSpellTrap(e, o, RoyalCommand, 0);
                var fs = PlaceMonster(e, p, FireSorcerer, 0, BattlePosition.Attack, true);
                PutInHand(e, p, CelticGuardian);
                PutInHand(e, p, GiantSoldierOfStone);
                var oppLp = o.LifePoints;
                MonsterEffects.OnFlipSummoned(e, p, fs);
                Drain(e);
                Check("cross-group: Royal Command negates Fire Sorcerer's FLIP after its cost — 2 hand cards banished, no damage",
                    p.Hand.Count == 0 && p.Banished.Count == 2 && o.LifePoints == oppLp,
                    $"hand={p.Hand.Count} banished={p.Banished.Count} oppLp {oppLp}->{o.LifePoints}");
            }
        }

        /// <summary>Polymerization from the hand: Flame Manipulator + Masaki → Flame Swordsman.</summary>
        static CardInstance FuseFlameSwordsman(DuelEngine e, DuelistState who, out CardInstance manip,
            out CardInstance masaki)
        {
            who.ExtraDeck.Clear();
            who.ExtraDeck.Add(FlameSwordsman);
            manip = PutInHand(e, who, FlameManipulator);
            masaki = PutInHand(e, who, Masaki);
            var poly = PutInHand(e, who, Polymerization);
            e.TryActivateSpellTrap(who, poly, fromHand: true);
            for (var i = 0; i < 20 && e.IsAwaitingResponse; i++) e.PassResponse();
            return who.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == FlameSwordsman);
        }

        /// <summary>
        /// The activation's target pick takes <paramref name="target"/>; response windows pass. True when a De-Fusion
        /// material pick is then open.
        /// </summary>
        static bool PickThenPass(DuelEngine e, CardInstance target)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget && !FlipCostsAndFusionRuntime.IsBatchPick(e.PendingActivation) &&
                    e.PendingActivation.LegalTargets.Contains(target))
                {
                    if (!e.TrySelectEffectTarget(target)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return e.IsAwaitingEffectTarget && FlipCostsAndFusionRuntime.IsBatchPick(e.PendingActivation);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A fresh duel in <paramref name="player"/>'s (or the opponent's) Main Phase, board, hands and piles empty.</summary>
        static DuelEngine MainOf(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool player, out DuelistState p,
            out DuelistState o)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            p = e.Player;
            o = e.Opponent;
            ReachMain(e, player ? p : o);
            ClearBoard(e);
            ClearPiles(e);
            return e;
        }

        static void ClearPiles(DuelEngine e)
        {
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        /// <summary>End turns (passing every window) until <paramref name="who"/> is in a Main Phase.</summary>
        static bool ReachMain(DuelEngine e, DuelistState who)
        {
            for (var t = 0; t < 8; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                if (e.PendingActivation?.AwaitingUpkeepChoice == true) e.TrySelectLpCost(e.PendingActivation.CostNumeric);
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (e.TurnPlayer == who && e.InMainPhase && !e.IsAwaitingResponse) return true;
                if (e.TurnPlayer == who && e.InMainPhase) continue;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == who && e.InMainPhase;
        }

        static void Drain(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else e.RecoverStuckCombat();
            }
        }

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "RuleLocks near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/opt={c.IsOptional}/tgt={c.RequiresTargetChoice}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
