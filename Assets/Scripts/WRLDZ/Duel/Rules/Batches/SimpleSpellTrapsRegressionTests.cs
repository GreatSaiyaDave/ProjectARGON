using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SimpleSpellTraps" (v160 LOD), checked against the ygopro scripts:
    /// <list type="bullet">
    /// <item>A Wingbeat of Giant Dragon c28596933: needs a face-up Lv5+ Dragon you control and another Spell/Trap on the
    /// field; SelectMatchingCard at resolution (no target); destroy every other Spell/Trap only if it reached the hand.</item>
    /// <item>Burst Breath c80163754: cost Tribute 1 Dragon (s.cfilter: another face-up monster has DEF &lt;= its ATK);
    /// destroy every face-up monster with DEF &lt;= the Tributed monster's ATK.</item>
    /// <item>Dragon's Gunfire c55991637: condition a face-up Dragon you control; 800 damage, or target a face-up monster
    /// with 800 or less DEF and destroy it (re-checked at resolution).</item>
    /// <item>Ready for Intercepting c31785398: target a face-up Warrior or Spellcaster; face-down Defense Position.</item>
    /// <item>Fiend Comedian c81172176: Duel.CallCoin; right: banish the opponent's GY; wrong: send that many cards from
    /// the top of your Deck to the GY. Needs a card in the opponent's GY and at least that many in your Deck.</item>
    /// <item>Emergency Provisions c53046408: cost 1..5 other Spell/Traps you control; 1000 LP per card sent.</item>
    /// <item>The A. Forces c403847: your Warriors gain 200 ATK per face-up Warrior or Spellcaster you control.</item>
    /// </list>
    /// </summary>
    public static class SimpleSpellTrapsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Wingbeat = 28596933;
        const int BurstBreath = 80163754;
        const int Gunfire = 55991637;
        const int ReadyForIntercepting = 31785398;
        const int FiendComedian = 81172176;
        const int Provisions = 53046408;
        const int AForces = 403847;

        const int Bewd = 89631139;            // Dragon Lv8 3000/2500
        const int CurseOfDragon = 28279543;   // Dragon Lv5 2000/1500
        const int TriHorned = 39111158;       // Dragon Lv8 2850/2350
        const int Koumori = 67724379;         // Dragon Lv4 1500/1200
        const int PetitDragon = 75356564;     // Dragon Lv2 600/700
        const int DragonZombie = 66672569;    // Zombie 1600/0 ("Dragon" only in its name)
        const int Celtic = 91152256;          // Warrior 1400/1200
        const int Gaia = 6368038;             // Warrior 2300/2100
        const int MysticalElf = 15025844;     // Spellcaster 800/2000
        const int DarkMagician = 46986414;    // Spellcaster 2500/2100
        const int BattleOx = 5053103;         // Beast-Warrior 1700/1000
        const int GiantSoldier = 13039848;    // Rock 1300/2000
        const int SilverFang = 90357090;      // Beast 1200/800
        const int TrapHole = 4206964;
        const int MirrorForce = 44095762;
        const int Mountain = 50913601;
        const int HeavyStorm = 19613556;
        const int PotOfGreed = 55144522;
        const int BookOfMoon = 14087893;
        const int Waboku = 12607053;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SimpleSpellTraps: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);

            WingbeatReturnsThenDestroysAll(db, pDeck, aDeck, Check);
            WingbeatPicksAtResolution(db, pDeck, aDeck, Check);
            WingbeatActivationNeedsBoth(db, pDeck, aDeck, Check);
            WingbeatAi(db, pDeck, aDeck, Check);

            BurstBreathDestroysDefLeqTributedAtk(db, pDeck, aDeck, Check);
            BurstBreathNeedsAVictim(db, pDeck, aDeck, Check);
            BurstBreathAi(db, pDeck, aDeck, Check);
            BurstBreathAnswersAnAttack(db, pDeck, aDeck, Check);
            BurstBreathChainedToASpell(db, pDeck, aDeck, Check);
            BurstBreathAiChainsBeforeAStorm(db, pDeck, aDeck, Check);
            BurstBreathTributeIsARelease(db, pDeck, aDeck, Check);

            GunfirePlayerBullets(db, pDeck, aDeck, Check);
            GunfireLoneBulletAndCondition(db, pDeck, aDeck, Check);
            GunfireCancelForgetsTheBullet(db, pDeck, aDeck, Check);
            GunfireAi(db, pDeck, aDeck, Check);

            ReadyForInterceptingTargetsTwoTypes(db, pDeck, aDeck, Check);
            ReadyForInterceptingInAnAttackWindow(db, pDeck, aDeck, Check);

            FiendComedianCalls(db, pDeck, aDeck, Check);
            FiendComedianActivationNeedsGyAndDeck(db, pDeck, aDeck, Check);
            FiendComedianAi(db, pDeck, aDeck, Check);

            ProvisionsPicksThenCancelFinishes(db, pDeck, aDeck, Check);
            ProvisionsCancelWithNoneUndoes(db, pDeck, aDeck, Check);
            ProvisionsChainedToHeavyStorm(db, pDeck, aDeck, Check);
            ProvisionsAi(db, pDeck, aDeck, Check);
            ProvisionsAnswersAnAttack(db, pDeck, aDeck, Check);
            ProvisionsInOpponentsOpenState(db, pDeck, aDeck, Check);
            ProvisionsAiKeepsActiveCards(db, pDeck, aDeck, Check);

            AForcesLiveCount(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            (CompiledCardProgram prog, CardEffectStatusKind st, List<EffectClause> act) Of(int id)
            {
                var def = db.Get(id);
                var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
                return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented,
                    p?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>());
            }

            {
                var (p, st, a) = Of(Wingbeat);
                Check("A Wingbeat of Giant Dragon: one untargeted return-then-destroy clause (Lv5+ Dragon), no Heavy Storm fragment",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && p.ClauseList.Count == 1 &&
                    a.Count == 1 && a[0].Action == EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps &&
                    !a[0].RequiresTargetChoice && a[0].ConditionName == "Dragon" && a[0].ConditionAmount == 5 &&
                    a[0].ActivationCondition == SimpleSpellTrapsRuntime.OwnRaceLevelMonsterAndOtherSpellTrap,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, a) = Of(BurstBreath);
                Check("Burst Breath: Tribute 1 Dragon (cost), destroy face-up monsters with DEF <= its ATK",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && p.ClauseList.Count == 1 &&
                    a.Count == 1 && a[0].Action == EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk &&
                    a[0].RequiresTributeCount == 1 && a[0].TributeRaceFilter == "Dragon" && !a[0].RequiresTargetChoice,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, a) = Of(Gunfire);
                Check("Dragon's Gunfire: two bullets (800 damage | target a face-up monster with 800 or less DEF), Dragon condition",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && a.Count == 2 &&
                    a[0].Action == EffectActionKind.InflictDamageToOpponent && a[0].Amount == 800 &&
                    a[0].EffectChoiceIndex == 1 && !a[0].RequiresTargetChoice &&
                    a[1].Action == EffectActionKind.DestroyTargetWithDefAtMost && a[1].Amount == 800 &&
                    a[1].EffectChoiceIndex == 2 && a[1].RequiresTargetChoice && a[1].RequiresTargetFaceUp &&
                    a[1].Zone == EffectZoneFilter.FieldAnyMonster &&
                    a.All(c => c.ActivationCondition == PoisonMeteorCestusRuntime.YouControlRace && c.ConditionName == "Dragon"),
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, a) = Of(ReadyForIntercepting);
                Check("Ready for Intercepting: target a face-up Warrior or Spellcaster on the field, face-down Defense",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && a.Count == 1 &&
                    a[0].Action == EffectActionKind.SetTargetFaceDownDefense && a[0].Zone == EffectZoneFilter.FieldAnyMonster &&
                    a[0].RaceFilter == "Warrior|Spellcaster" && a[0].RequiresTargetChoice && a[0].IsPsctTarget,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, a) = Of(FiendComedian);
                Check("Fiend Comedian: one coin-call clause with its GY/Deck activation condition",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && a.Count == 1 &&
                    a[0].Action == EffectActionKind.CallCoinBanishOppGyOrSendDeckEqualToOppGy &&
                    a[0].ActivationCondition == SimpleSpellTrapsRuntime.OppGyCardsAndDeckCovers,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, a) = Of(Provisions);
                Check("Emergency Provisions: send-cost clause, 1000 LP per card sent",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && a.Count == 1 &&
                    a[0].Action == EffectActionKind.GainLpPerSpellTrapSentAsCost && a[0].Amount == 1000 &&
                    a[0].Side == EffectSide.Controller &&
                    EffectVocabulary.CostOf(a[0]) == EffectCostKind.SendToGy,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, _) = Of(AForces);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("The A. Forces: continuous +200 ATK to your Warriors per face-up Warrior/Spellcaster you control",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                    c.Amount == 200 && c.DefAmount == 0 && c.RaceFilter == "Warrior" && c.Side == EffectSide.Controller &&
                    c.CountSource == SimpleSpellTrapsRuntime.FaceUpRaceYouControl && c.CountRaceFilter == "Warrior|Spellcaster",
                    $"status={st} {Describe(p)}");
            }
        }

        static CompiledCardProgram Probe(string desc, string type) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990160000 + Math.Abs(desc.GetHashCode() % 9999), name = "SimpleSpellTraps Probe", type = type,
                race = "Normal",
                frameType = type.StartsWith("Trap") ? "trap" : "spell", desc = desc
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> pred) =>
            p != null && p.ClauseList.Any(c => c != null && pred(c));

        /// <summary>Look-alike sentences do not take these atoms (anchored whole-text shapes).</summary>
        static void NearMisses(CardDatabase db, Chk Check)
        {
            var book = db.Get(BookOfMoon);
            var bookProg = book != null ? CardTextEffectCompiler.Compile(book) : null;
            Check("Book of Moon keeps its unfiltered face-down target (no Type filter)",
                bookProg != null && bookProg.FullyCompiled &&
                Has(bookProg, c => c.Action == EffectActionKind.SetTargetFaceDownDefense && string.IsNullOrEmpty(c.RaceFilter)),
                Describe(bookProg));

            var rows = new (string desc, string type, Func<EffectClause, bool> wrong, string label)[]
            {
                ("Return 1 Level 5 or higher Dragon-Type monster you control to the hand.", "Spell Card",
                    c => c.Action == EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps,
                    "a return without the destroy"),
                ("Return 1 Level 5 or higher Dragon-Type monster you control to the hand, and if you do, destroy all " +
                 "Spell and Trap Cards on the field. Then draw 1 card.", "Spell Card",
                    c => c.Action == EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps,
                    "Wingbeat plus a rider"),
                ("Tribute 1 Dragon-Type monster; destroy all face-up monsters your opponent controls whose DEF is less " +
                 "than or equal to the ATK the Tributed monster had on the field.", "Trap Card",
                    c => c.Action == EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk, "only the opponent's monsters"),
                ("If you control a Dragon monster: You can activate 1 of these effects;\r\n● Inflict 800 damage to your " +
                 "opponent.\r\n● Target 1 monster with 800 or less DEF; destroy that target.", "Spell Card",
                    c => c.Action == EffectActionKind.DestroyTargetWithDefAtMost, "\"You can activate\" wording"),
                ("Target 1 Warrior or Spellcaster monster on the field; change that target to face-down Defense " +
                 "Position, then draw 1 card.", "Trap Card",
                    c => c.Action == EffectActionKind.SetTargetFaceDownDefense && c.RaceFilter == "Warrior|Spellcaster",
                    "Ready for Intercepting plus a draw"),
                ("Toss a coin and call it. If you call it right, all your opponent's cards in the Graveyard are removed " +
                 "from play immediately.", "Trap Card",
                    c => c.Action == EffectActionKind.CallCoinBanishOppGyOrSendDeckEqualToOppGy, "only the right call"),
                ("Send any number of other Spell/Trap Cards you control to the Graveyard; draw 1 card for each card sent " +
                 "to the Graveyard this way.", "Spell Card",
                    c => c.Action == EffectActionKind.GainLpPerSpellTrapSentAsCost, "a draw per card, not LP"),
                ("All Warrior-Type monsters you control gain 200 ATK for each Warrior or Spellcaster-Type monster on " +
                 "the field.", "Spell Card",
                    c => c.CountSource == SimpleSpellTrapsRuntime.FaceUpRaceYouControl, "count on the field, not yours"),
            };
            foreach (var (desc, type, wrong, label) in rows)
            {
                var p = Probe(desc, type);
                Check($"near-miss ({label}) does not take the atom", p != null && !Has(p, wrong), Describe(p));
            }
        }

        // ───────────────────────────── A Wingbeat of Giant Dragon ─────────────────────────────

        static void WingbeatReturnsThenDestroysAll(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
            var petit = PlaceMonster(e, p, PetitDragon, 1, BattlePosition.Attack, true);
            var hole = PlaceSetTrap(e, p, TrapHole, 0);
            var mirror = PlaceSetTrap(e, o, MirrorForce, 0);
            var mountain = e.CreateCardInstance(Mountain);
            mountain.FaceUp = true;
            o.FieldSpellZone.Occupant = mountain;
            var wing = PutInHand(e, p, Wingbeat);
            var act = e.TryActivateSpellTrap(p, wing, fromHand: true);
            Drive(e, _ => false);
            Check("A Wingbeat of Giant Dragon: the lone Lv5+ Dragon returns, then every Spell/Trap (Field too) is destroyed",
                act && p.Hand.Contains(bewd) && Where(e, petit) == "P.mz" && Where(e, hole) == "P.gy" &&
                Where(e, mirror) == "O.gy" && Where(e, mountain) == "O.gy" && Where(e, wing) == "P.gy",
                $"act={act} bewd={Where(e, bewd)} petit={Where(e, petit)} hole={Where(e, hole)} mirror={Where(e, mirror)} " +
                $"mountain={Where(e, mountain)} wing={Where(e, wing)} {State(e)}");
        }

        static void WingbeatPicksAtResolution(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
            var curse = PlaceMonster(e, p, CurseOfDragon, 1, BattlePosition.Attack, true);
            var mirror = PlaceSetTrap(e, o, MirrorForce, 0);
            var wing = PutInHand(e, p, Wingbeat);
            var act = e.TryActivateSpellTrap(p, wing, fromHand: true);
            // No pick as the card is activated (not a target): the opponent's window comes first.
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var legal = Legal(e);
            var offered = e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(bewd) && legal.Contains(curse);
            var picked = offered && e.TrySelectEffectTarget(curse);
            Drive(e, _ => false);
            Check("A Wingbeat of Giant Dragon: with two Dragons the player selects at resolution; that one returns",
                act && offered && picked && p.Hand.Contains(curse) && Where(e, bewd) == "P.mz" && Where(e, mirror) == "O.gy" &&
                !e.Chain.HasLinks,
                $"act={act} offered={offered} legal={Names(legal)} picked={picked} curse={Where(e, curse)} bewd={Where(e, bewd)} " +
                $"mirror={Where(e, mirror)} {State(e)}");
        }

        static void WingbeatActivationNeedsBoth(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, e.Opponent, MirrorForce, 0);
                var wing = PutInHand(e, p, Wingbeat);
                Check("A Wingbeat of Giant Dragon: a Level 4 Dragon is not enough",
                    !e.CanActivateSpellTrap(p, wing, fromHand: true), State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var wing = PutInHand(e, p, Wingbeat);
                Check("A Wingbeat of Giant Dragon: no other Spell/Trap on the field — cannot activate",
                    !e.CanActivateSpellTrap(p, wing, fromHand: true), State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Bewd, 0, BattlePosition.Defense, false);
                PlaceSetTrap(e, e.Opponent, MirrorForce, 0);
                var wing = PutInHand(e, p, Wingbeat);
                Check("A Wingbeat of Giant Dragon: a face-down Dragon does not count",
                    !e.CanActivateSpellTrap(p, wing, fromHand: true), State(e));
            }
        }

        static void WingbeatAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
            var curse = PlaceMonster(e, o, CurseOfDragon, 1, BattlePosition.Attack, true);
            var hole = PlaceSetTrap(e, p, TrapHole, 0);
            var wing = PutInHand(e, o, Wingbeat);
            var act = e.TryActivateSpellTrap(o, wing, fromHand: true);
            Drive(e, _ => false);
            Check("A Wingbeat of Giant Dragon (AI): returns its weaker Dragon, destroys the Set Trap, no prompt left",
                main && act && o.Hand.Contains(curse) && Where(e, bewd) == "O.mz" && Where(e, hole) == "P.gy" &&
                !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                $"main={main} act={act} curse={Where(e, curse)} bewd={Where(e, bewd)} hole={Where(e, hole)} {State(e)}");
        }

        // ───────────────────────────── Burst Breath ─────────────────────────────

        static void BurstBreathDestroysDefLeqTributedAtk(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
            var koumori = PlaceMonster(e, p, Koumori, 1, BattlePosition.Attack, true);
            var petit = PlaceMonster(e, p, PetitDragon, 2, BattlePosition.Attack, true);
            var zombie = PlaceMonster(e, p, DragonZombie, 3, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
            var tri = PlaceMonster(e, o, TriHorned, 2, BattlePosition.Attack, true);
            var stone = PlaceMonster(e, o, GiantSoldier, 3, BattlePosition.Defense, false);
            var bb = PlaceSetTrap(e, p, BurstBreath, 0);
            var act = e.TryActivateSpellTrap(p, bb, fromHand: false);
            var legal = Legal(e);
            // Petit Dragon (600 ATK) qualifies through Dragon Zombie (0 DEF); Dragon Zombie itself is a Zombie.
            var offered = e.IsAwaitingEffectTarget && legal.Count == 3 && legal.Contains(bewd) && legal.Contains(koumori) &&
                          legal.Contains(petit) && !legal.Contains(zombie);
            var picked = offered && e.TrySelectEffectTarget(bewd);
            Drive(e, _ => false);
            Check("Burst Breath: Tribute Blue-Eyes (3000) — every face-up monster with DEF <= 3000 is destroyed, Sets stay",
                act && offered && picked && Where(e, bewd) == "P.gy" && Where(e, koumori) == "P.gy" &&
                Where(e, petit) == "P.gy" && Where(e, zombie) == "P.gy" && Where(e, celtic) == "O.gy" &&
                Where(e, elf) == "O.gy" && Where(e, tri) == "O.gy" && Where(e, stone) == "O.mz" && Where(e, bb) == "P.gy",
                $"act={act} offered={offered} legal={Names(legal)} picked={picked} koumori={Where(e, koumori)} " +
                $"petit={Where(e, petit)} zombie={Where(e, zombie)} celtic={Where(e, celtic)} elf={Where(e, elf)} " +
                $"tri={Where(e, tri)} stone={Where(e, stone)} bb={Where(e, bb)} {State(e)}");

            // The smaller Tribute: Koumori Dragon (1500) — Mystical Elf (2000 DEF) survives.
            var e2 = FreshCleared(db, pDeck, aDeck);
            var p2 = e2.Player;
            var o2 = e2.Opponent;
            var k2 = PlaceMonster(e2, p2, Koumori, 0, BattlePosition.Attack, true);
            var b2 = PlaceMonster(e2, p2, Bewd, 1, BattlePosition.Attack, true);
            var c2 = PlaceMonster(e2, o2, Celtic, 0, BattlePosition.Attack, true);
            var elf2 = PlaceMonster(e2, o2, MysticalElf, 1, BattlePosition.Attack, true);
            var bb2 = PlaceSetTrap(e2, p2, BurstBreath, 0);
            var act2 = e2.TryActivateSpellTrap(p2, bb2, fromHand: false);
            var picked2 = e2.IsAwaitingEffectTarget && e2.TrySelectEffectTarget(k2);
            Drive(e2, _ => false);
            Check("Burst Breath: the cap is the Tributed monster's ATK (1500): DEF 1200 destroyed, DEF 2000 and 2500 stay",
                act2 && picked2 && Where(e2, k2) == "P.gy" && Where(e2, c2) == "O.gy" && Where(e2, elf2) == "O.mz" &&
                Where(e2, b2) == "P.mz",
                $"act={act2} picked={picked2} celtic={Where(e2, c2)} elf={Where(e2, elf2)} bewd={Where(e2, b2)} {State(e2)}");
        }

        static void BurstBreathNeedsAVictim(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Koumori, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, MysticalElf, 0, BattlePosition.Attack, true);
                var bb = PlaceSetTrap(e, e.Player, BurstBreath, 0);
                Check("Burst Breath: no other face-up monster with DEF <= the Dragon's ATK — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, bb, fromHand: false), State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, DragonZombie, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, SilverFang, 0, BattlePosition.Attack, true);
                var bb = PlaceSetTrap(e, e.Player, BurstBreath, 0);
                Check("Burst Breath: a Zombie named \"Dragon …\" is not a Dragon to Tribute",
                    !e.CanActivateSpellTrap(e.Player, bb, fromHand: false), State(e));
            }
        }

        static void BurstBreathAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var koumori = PlaceMonster(e, o, Koumori, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var bb = PlaceSetTrap(e, o, BurstBreath, 0);
            var act = e.TryActivateSpellTrap(o, bb, fromHand: false);
            Drive(e, _ => false);
            Check("Burst Breath (AI): Tributes its Dragon automatically and destroys Celtic Guardian",
                main && act && Where(e, koumori) == "O.gy" && Where(e, celtic) == "P.gy" && !e.IsAwaitingEffectTarget &&
                !e.Chain.HasLinks,
                $"main={main} act={act} koumori={Where(e, koumori)} celtic={Where(e, celtic)} {State(e)}");
        }

        /// <summary>
        /// Burst Breath is a free-chain Normal Trap (Lua EVENT_FREE_CHAIN, hint timing END_PHASE / CHECK_MONSTER): it can
        /// answer an attack declaration in the opponent's turn. The Tribute is paid as it is activated (the seat), and the
        /// DEF cap is that Tribute's ATK. CANCEL on the Tribute pick undoes the activation and re-offers the window.
        /// </summary>
        static void BurstBreathAnswersAnAttack(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var koumori = PlaceMonster(e, p, Koumori, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var bb = PlaceSetTrap(e, p, BurstBreath, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var atk = e.TryAttack(o, celtic, koumori);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, bb, fromHand: false);
                var legal = Legal(e);
                var offered = e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(bewd) && legal.Contains(koumori);
                var picked = offered && e.TrySelectEffectTarget(koumori);
                Drive(e, _ => false);
                DrainCombat(e);
                Check("Burst Breath: answers the opponent's attack — Tribute Koumori (1500): Celtic (DEF 1200) destroyed, Blue-Eyes (DEF 2500) stays",
                    battle && atk && window && act && offered && picked && Where(e, koumori) == "P.gy" &&
                    Where(e, celtic) == "O.gy" && Where(e, bewd) == "P.mz" && Where(e, bb) == "P.gy" && p.LifePoints == pLp &&
                    !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"battle={battle} atk={atk} window={window} act={act} offered={offered} legal={Names(legal)} picked={picked} " +
                    $"koumori={Where(e, koumori)} celtic={Where(e, celtic)} bewd={Where(e, bewd)} bb={Where(e, bb)} lp={p.LifePoints} {State(e)}");
            }
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var koumori = PlaceMonster(e, p, Koumori, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var bb = PlaceSetTrap(e, p, BurstBreath, 0);
                e.NotifyPublic();
                var atk = e.TryAttack(o, celtic, koumori);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, bb, fromHand: false);
                var asked = e.IsAwaitingEffectTarget;
                e.CancelEffectTargeting();
                var reOffered = e.IsAwaitingResponse && e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                                e.PendingResponse?.Responder == p;
                var undone = !bb.FaceUp && Where(e, bb) == "P.stz" && Where(e, bewd) == "P.mz" &&
                             Where(e, koumori) == "P.mz" && !e.Chain.HasLinks;
                Drive(e, _ => false);
                DrainCombat(e);
                Check("Burst Breath: CANCEL on the Tribute pick undoes the activation — Set again, nothing Tributed, the window is offered again",
                    battle && atk && window && act && asked && reOffered && undone,
                    $"battle={battle} atk={atk} window={window} act={act} asked={asked} reOffered={reOffered} undone={undone} " +
                    $"bb={Where(e, bb)}/{bb.FaceUp} {State(e)}");
            }
        }

        /// <summary>Burst Breath chained to the opponent's Heavy Storm: the Tribute is paid as it is chained; it resolves first.</summary>
        static void BurstBreathChainedToASpell(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
            var koumori = PlaceMonster(e, p, Koumori, 1, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var bb = PlaceSetTrap(e, p, BurstBreath, 0);
            var storm = PutInHand(e, o, HeavyStorm);
            var act = e.TryActivateSpellTrap(o, storm, fromHand: true);
            var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
            var chained = window && e.TryActivateSpellTrap(p, bb, fromHand: false);
            var legal = Legal(e);
            var offered = e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(koumori);
            var picked = offered && e.TrySelectEffectTarget(koumori);
            Drive(e, _ => false);
            Check("Burst Breath: chained to Heavy Storm — Tribute Koumori as it is chained, Celtic destroyed, then the Storm resolves",
                main && act && window && chained && offered && picked && Where(e, koumori) == "P.gy" &&
                Where(e, celtic) == "O.gy" && Where(e, bewd) == "P.mz" && Where(e, bb) == "P.gy" && Where(e, storm) == "O.gy" &&
                !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"main={main} act={act} window={window} chained={chained} offered={offered} legal={Names(legal)} picked={picked} " +
                $"koumori={Where(e, koumori)} celtic={Where(e, celtic)} bewd={Where(e, bewd)} bb={Where(e, bb)} storm={Where(e, storm)} {State(e)}");
        }

        /// <summary>
        /// v160 integration audit (SimpleSpellTraps x EngineHygiene): Burst Breath's Tribute is an EVENT_RELEASE, so Super
        /// Rejuvenation counts the Tributed Dragon once, whether Burst Breath is activated in the open state or chained.
        /// </summary>
        static void BurstBreathTributeIsARelease(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var k = PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
            PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
            var bb = PlaceSetTrap(e, p, BurstBreath, 0);
            var before = SuperRejuvenation.CountFor(e, p);
            var act = e.TryActivateSpellTrap(p, bb, fromHand: false);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(k);
            Drive(e, _ => false);
            var openCount = SuperRejuvenation.CountFor(e, p) - before;
            Check("Burst Breath (open state): its Dragon Tribute counts once for Super Rejuvenation",
                act && Where(e, k) == "P.gy" && openCount == 1, $"act={act} koumori={Where(e, k)} count={openCount} {State(e)}");

            var e2 = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e2);
            ClearBoard(e2);
            var p2 = e2.Player;
            var o2 = e2.Opponent;
            p2.Hand.Clear();
            o2.Hand.Clear();
            PlaceMonster(e2, p2, Bewd, 0, BattlePosition.Attack, true);
            var k2 = PlaceMonster(e2, p2, Koumori, 1, BattlePosition.Attack, true);
            PlaceMonster(e2, o2, Celtic, 0, BattlePosition.Attack, true);
            var bb2 = PlaceSetTrap(e2, p2, BurstBreath, 0);
            var storm = PutInHand(e2, o2, HeavyStorm);
            var before2 = SuperRejuvenation.CountFor(e2, p2);
            var act2 = e2.TryActivateSpellTrap(o2, storm, fromHand: true);
            var chained = act2 && e2.IsAwaitingResponse && e2.PendingResponse?.Responder == p2 &&
                          e2.TryActivateSpellTrap(p2, bb2, fromHand: false);
            var picked = chained && e2.IsAwaitingEffectTarget && e2.TrySelectEffectTarget(k2);
            Drive(e2, _ => false);
            var chainCount = SuperRejuvenation.CountFor(e2, p2) - before2;
            Check("Burst Breath (chained): its Dragon Tribute paid as it is chained counts once for Super Rejuvenation",
                main && chained && picked && Where(e2, k2) == "P.gy" && chainCount == 1,
                $"main={main} chained={chained} picked={picked} koumori={Where(e2, k2)} count={chainCount} {State(e2)}");
        }

        /// <summary>The AI chains its threatened Burst Breath to the player's Heavy Storm and pays the Tribute itself.</summary>
        static void BurstBreathAiChainsBeforeAStorm(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var koumori = PlaceMonster(e, o, Koumori, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var bb = PlaceSetTrap(e, o, BurstBreath, 0);
            var storm = PutInHand(e, p, HeavyStorm);
            var act = e.TryActivateSpellTrap(p, storm, fromHand: true);
            Drive(e, _ => false);
            var chained = Where(e, koumori) == "O.gy";
            Check("Burst Breath (AI): chained to a Heavy Storm, the Tribute is paid as it is chained — Celtic destroyed, no prompt",
                act && chained && Where(e, celtic) == "P.gy" && Where(e, bb) == "O.gy" &&
                Where(e, storm) == "P.gy" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                $"act={act} chained={chained} koumori={Where(e, koumori)} celtic={Where(e, celtic)} bb={Where(e, bb)} {State(e)}");
        }

        // ───────────────────────────── Dragon's Gunfire ─────────────────────────────

        static void GunfirePlayerBullets(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var destroy in new[] { true, false })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
                var fang = PlaceMonster(e, o, SilverFang, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var gun = PutInHand(e, p, Gunfire);
                var act = e.TryActivateSpellTrap(p, gun, fromHand: true);
                var tokens = Legal(e);
                var twoBullets = tokens.Count == 2 && tokens.All(t => t.CardId == 0);
                var picked = Drive(e, c => c?.Def != null &&
                                           (c.CardId == 0
                                               ? c.Def.name.StartsWith(destroy ? "Destroy" : "Inflict", StringComparison.Ordinal)
                                               : c == fang));
                var ok = destroy
                    ? Where(e, fang) == "O.gy" && o.LifePoints == oLp
                    : Where(e, fang) == "O.mz" && o.LifePoints == oLp - 800;
                Check($"Dragon's Gunfire: the player picks the {(destroy ? "destroy" : "damage")} bullet — only that one resolves",
                    act && twoBullets && picked && ok && Where(e, elf) == "O.mz" && Where(e, gun) == "P.gy" &&
                    !PoisonMeteorCestusRuntime.HasChosenBullet(gun),
                    $"act={act} tokens={Names(tokens)} picked={picked} fang={Where(e, fang)} o={o.LifePoints} gun={Where(e, gun)} {State(e)}");
            }
        }

        static void GunfireLoneBulletAndCondition(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var gun = PutInHand(e, p, Gunfire);
                var act = e.TryActivateSpellTrap(p, gun, fromHand: true);
                var noPrompt = !e.IsAwaitingEffectTarget;
                Drive(e, _ => false);
                Check("Dragon's Gunfire: no monster with 800 or less DEF — still legal, the damage bullet is taken",
                    act && noPrompt && o.LifePoints == oLp - 800 && Where(e, elf) == "O.mz" && Where(e, gun) == "P.gy",
                    $"act={act} noPrompt={noPrompt} o={o.LifePoints} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Koumori, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, SilverFang, 0, BattlePosition.Attack, true);
                var gun = PutInHand(e, p, Gunfire);
                Check("Dragon's Gunfire: a face-down Dragon is not \"control a Dragon monster\" — cannot activate",
                    !e.CanActivateSpellTrap(p, gun, fromHand: true), State(e));
            }
            {
                // A face-down monster with 0 DEF is not a target (s.dfilter IsFaceup).
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
                var setZombie = PlaceMonster(e, o, DragonZombie, 0, BattlePosition.Defense, false);
                var oLp = o.LifePoints;
                var gun = PutInHand(e, p, Gunfire);
                var act = e.TryActivateSpellTrap(p, gun, fromHand: true);
                Drive(e, _ => false);
                Check("Dragon's Gunfire: a face-down monster is not a target — the damage bullet is taken",
                    act && Where(e, setZombie) == "O.mz" && o.LifePoints == oLp - 800,
                    $"act={act} zombie={Where(e, setZombie)} o={o.LifePoints} {State(e)}");
            }
        }

        static void GunfireCancelForgetsTheBullet(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
            var fang = PlaceMonster(e, o, SilverFang, 0, BattlePosition.Attack, true);
            var fang2 = PlaceMonster(e, o, SilverFang, 1, BattlePosition.Attack, true);
            var oLp = o.LifePoints;
            var gun = PutInHand(e, p, Gunfire);
            var act = e.TryActivateSpellTrap(p, gun, fromHand: true);
            var token = Legal(e).FirstOrDefault(t => t.CardId == 0 && t.Def.name.StartsWith("Destroy", StringComparison.Ordinal));
            var tokenOk = token != null && e.TrySelectEffectTarget(token);
            var targetPrompt = e.IsAwaitingEffectTarget && Legal(e).Contains(fang) && Legal(e).Contains(fang2);
            e.CancelEffectTargeting();
            var undone = p.Hand.Contains(gun) && !e.Chain.HasLinks && !PoisonMeteorCestusRuntime.HasChosenBullet(gun);
            // Activated again: both bullets are offered again.
            var again = e.TryActivateSpellTrap(p, gun, fromHand: true);
            var reoffered = Legal(e).Count == 2 && Legal(e).All(t => t.CardId == 0);
            Drive(e, c => c?.Def != null && c.CardId == 0 && c.Def.name.StartsWith("Inflict", StringComparison.Ordinal));
            Check("Dragon's Gunfire: CANCEL at the target pick undoes it and forgets the bullet; activated again it asks again",
                act && tokenOk && targetPrompt && undone && again && reoffered && o.LifePoints == oLp - 800 &&
                Where(e, fang) == "O.mz" && Where(e, fang2) == "O.mz",
                $"act={act} tokenOk={tokenOk} targetPrompt={targetPrompt} undone={undone} again={again} reoffered={reoffered} " +
                $"o={o.LifePoints} {State(e)}");
        }

        static void GunfireAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var hasTarget in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                var main = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceMonster(e, o, Koumori, 0, BattlePosition.Attack, true);
                var victim = PlaceMonster(e, p, hasTarget ? SilverFang : MysticalElf, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var gun = PutInHand(e, o, Gunfire);
                var act = e.TryActivateSpellTrap(o, gun, fromHand: true);
                Drive(e, _ => false);
                var ok = hasTarget
                    ? Where(e, victim) == "P.gy" && p.LifePoints == pLp
                    : Where(e, victim) == "P.mz" && p.LifePoints == pLp - 800;
                Check($"Dragon's Gunfire (AI): {(hasTarget ? "destroys the player's 800-DEF monster" : "no target — 800 damage")}",
                    main && act && ok && !e.IsAwaitingEffectTarget && Where(e, gun) == "O.gy",
                    $"main={main} act={act} victim={Where(e, victim)} p={p.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Ready for Intercepting ─────────────────────────────

        static void ReadyForInterceptingTargetsTwoTypes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
            var ox = PlaceMonster(e, o, BattleOx, 2, BattlePosition.Attack, true);
            var stone = PlaceMonster(e, o, GiantSoldier, 3, BattlePosition.Attack, true);
            var setDm = PlaceMonster(e, o, DarkMagician, 4, BattlePosition.Defense, false);
            var dm = PlaceMonster(e, p, DarkMagician, 0, BattlePosition.Attack, true);
            var rfi = PlaceSetTrap(e, p, ReadyForIntercepting, 0);
            var legacy = SpellTrapEffects.CollectLegalTargets(e, p, EffectTargetKind.AnyMonsterOnField, rfi);
            var legacyOk = legacy.Count == 3 && legacy.Contains(celtic) && legacy.Contains(elf) && legacy.Contains(dm);
            var act = e.TryActivateSpellTrap(p, rfi, fromHand: false);
            var legal = Legal(e);
            var textOk = legal.Count == 3 && legal.Contains(celtic) && legal.Contains(elf) && legal.Contains(dm);
            var picked = textOk && e.TrySelectEffectTarget(celtic);
            Drive(e, _ => false);
            Check("Ready for Intercepting: only face-up Warriors / Spellcasters (both fields, both target mirrors); the target is Set",
                act && legacyOk && textOk && picked && !celtic.FaceUp && celtic.Position == BattlePosition.Defense &&
                Where(e, celtic) == "O.mz" && elf.FaceUp && ox.FaceUp && stone.FaceUp && !setDm.FaceUp &&
                Where(e, rfi) == "P.gy",
                $"act={act} legacy={Names(legacy)} legal={Names(legal)} picked={picked} celtic={celtic.FaceUp}/{celtic.Position} " +
                $"rfi={Where(e, rfi)} {State(e)}");

            var e2 = FreshCleared(db, pDeck, aDeck);
            PlaceMonster(e2, e2.Opponent, BattleOx, 0, BattlePosition.Attack, true);
            PlaceMonster(e2, e2.Opponent, Celtic, 1, BattlePosition.Defense, false);
            var rfi2 = PlaceSetTrap(e2, e2.Player, ReadyForIntercepting, 0);
            Check("Ready for Intercepting: a Beast-Warrior and a face-down Warrior are no targets — cannot activate",
                !e2.CanActivateSpellTrap(e2.Player, rfi2, fromHand: false), State(e2));
        }

        static void ReadyForInterceptingInAnAttackWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var battle = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var rfi = PlaceSetTrap(e, p, ReadyForIntercepting, 0);
            e.NotifyPublic();
            var pLp = p.LifePoints;
            var atk = e.TryAttack(o, celtic, null);
            var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
            var act = window && e.TryActivateSpellTrap(p, rfi, fromHand: false);
            Drive(e, c => c == celtic);
            DrainCombat(e);
            Check("Ready for Intercepting: answers the attack — the attacker is turned face-down, no damage",
                battle && atk && window && act && !celtic.FaceUp && celtic.Position == BattlePosition.Defense &&
                p.LifePoints == pLp && Where(e, rfi) == "P.gy",
                $"battle={battle} atk={atk} window={window} act={act} celtic={celtic.FaceUp}/{celtic.Position} p={p.LifePoints} {State(e)}");
        }

        // ───────────────────────────── Fiend Comedian ─────────────────────────────

        static DuelEngine FiendBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out CardInstance fc)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            p.Deck.AddRange(new[] { Celtic, MysticalElf, GiantSoldier, Koumori, BattleOx });
            foreach (var id in new[] { Celtic, PotOfGreed, MirrorForce })
                o.Graveyard.Add(e.CreateCardInstance(id));
            fc = PlaceSetTrap(e, p, FiendComedian, 0);
            return e;
        }

        static void FiendComedianCalls(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var right in new[] { true, false })
            {
                var e = FiendBoard(db, pDeck, aDeck, out var fc);
                var p = e.Player;
                var o = e.Opponent;
                e.Rng.QueueCoin(true); // the toss is Heads
                var act = e.TryActivateSpellTrap(p, fc, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall &&
                            e.PendingActivation.Controller == p;
                var called = asked && e.TrySelectCoinCall(right);
                Drive(e, _ => false);
                var ok = right
                    ? o.Graveyard.Count == 0 && o.Banished.Count == 3 && p.Deck.Count == 5
                    : o.Graveyard.Count == 3 && o.Banished.Count == 0 && p.Deck.Count == 2 &&
                      p.Graveyard.Count(g => g?.CardId == Celtic || g?.CardId == MysticalElf || g?.CardId == GiantSoldier) == 3;
                Check($"Fiend Comedian: the player calls at resolution — {(right ? "right: the opponent's GY is banished" : "wrong: 3 cards from the Deck to the GY")}",
                    act && asked && called && ok && Where(e, fc) == "P.gy" && !e.Chain.HasLinks,
                    $"act={act} asked={asked} called={called} oGy={o.Graveyard.Count} oBan={o.Banished.Count} deck={p.Deck.Count} " +
                    $"pGy={p.Graveyard.Count} fc={Where(e, fc)} {State(e)}");
            }

            {
                var e = FiendBoard(db, pDeck, aDeck, out var fc);
                e.Rng.QueueCoin(true);
                var act = e.TryActivateSpellTrap(e.Player, fc, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall;
                e.CancelEffectTargeting();
                Drive(e, _ => false);
                Check("Fiend Comedian: CANCEL at the call — the call is mandatory (Heads), the effect resolves",
                    act && asked && e.Opponent.Banished.Count == 3 && e.PendingActivation == null && !e.Chain.HasLinks,
                    $"act={act} asked={asked} oBan={e.Opponent.Banished.Count} {State(e)}");
            }
        }

        static void FiendComedianActivationNeedsGyAndDeck(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FiendBoard(db, pDeck, aDeck, out var fc);
                e.Opponent.Graveyard.Clear();
                Check("Fiend Comedian: the opponent's GY is empty — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, fc, fromHand: false), State(e));
            }
            {
                var e = FiendBoard(db, pDeck, aDeck, out var fc);
                e.Player.Deck.RemoveRange(0, 3); // 2 cards left, 3 in the opponent's GY
                Check("Fiend Comedian: fewer cards in your Deck than in the opponent's GY — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, fc, fromHand: false), State(e));
            }
        }

        static void FiendComedianAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            p.Graveyard.Clear();
            p.Graveyard.Add(e.CreateCardInstance(Celtic));
            p.Graveyard.Add(e.CreateCardInstance(PotOfGreed));
            var fc = PlaceSetTrap(e, o, FiendComedian, 0);
            e.Rng.QueueCoin(true);
            var act = e.TryActivateSpellTrap(o, fc, fromHand: false);
            Drive(e, _ => false);
            Check("Fiend Comedian (AI): calls Heads by itself — right: the player's GY is banished, no prompt left",
                main && act && p.Graveyard.Count == 0 && p.Banished.Count == 2 && !e.IsAwaitingEffectTarget &&
                !e.Chain.HasLinks && Where(e, fc) == "O.gy",
                $"main={main} act={act} pGy={p.Graveyard.Count} pBan={p.Banished.Count} {State(e)}");
        }

        // ───────────────────────────── Emergency Provisions ─────────────────────────────

        static void ProvisionsPicksThenCancelFinishes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var hole = PlaceSetTrap(e, p, TrapHole, 0);
            var mirror = PlaceSetTrap(e, p, MirrorForce, 1);
            var mountain = e.CreateCardInstance(Mountain);
            mountain.FaceUp = true;
            p.FieldSpellZone.Occupant = mountain;
            var lp = p.LifePoints;
            var ep = PutInHand(e, p, Provisions);
            var act = e.TryActivateSpellTrap(p, ep, fromHand: true);
            var legal = Legal(e);
            var offered = legal.Count == 3 && legal.Contains(hole) && legal.Contains(mirror) && legal.Contains(mountain);
            var pick1 = e.TrySelectEffectTarget(hole);
            var pick2 = e.TrySelectEffectTarget(mirror);
            var stillPicking = e.IsAwaitingEffectTarget && Legal(e).Count == 1 && Legal(e).Contains(mountain);
            e.CancelEffectTargeting(); // done: send these two
            var paidAtActivation = Where(e, hole) == "P.gy" && Where(e, mirror) == "P.gy";
            Drive(e, _ => false);
            Check("Emergency Provisions: the player sends two other Spell/Traps (CANCEL finishes) — 2000 LP, the Field Spell stays",
                act && offered && pick1 && pick2 && stillPicking && paidAtActivation && p.LifePoints == lp + 2000 &&
                p.FieldSpellZone.Occupant == mountain && Where(e, ep) == "P.gy",
                $"act={act} legal={Names(legal)} picks={pick1}/{pick2} still={stillPicking} paid={paidAtActivation} lp={p.LifePoints} " +
                $"ep={Where(e, ep)} {State(e)}");
        }

        static void ProvisionsCancelWithNoneUndoes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var hole = PlaceSetTrap(e, p, TrapHole, 0);
            var lp = p.LifePoints;
            var ep = PutInHand(e, p, Provisions);
            var act = e.TryActivateSpellTrap(p, ep, fromHand: true);
            var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 1;
            e.CancelEffectTargeting();
            Check("Emergency Provisions: CANCEL with nothing picked undoes the activation — nothing sent, no LP",
                act && asked && p.Hand.Contains(ep) && Where(e, hole) == "P.stz" && p.LifePoints == lp && !e.Chain.HasLinks &&
                !e.IsAwaitingEffectTarget,
                $"act={act} asked={asked} ep={Where(e, ep)} hole={Where(e, hole)} lp={p.LifePoints} {State(e)}");

            var e2 = FreshCleared(db, pDeck, aDeck);
            var ep2 = PutInHand(e2, e2.Player, Provisions);
            Check("Emergency Provisions: no other Spell/Trap you control — cannot activate",
                !e2.CanActivateSpellTrap(e2.Player, ep2, fromHand: true), State(e2));
        }

        /// <summary>Chained to the opponent's Heavy Storm: the Set cards about to be destroyed are sent for LP instead.</summary>
        static void ProvisionsChainedToHeavyStorm(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var ep = PlaceSetTrap(e, p, Provisions, 0);
            var hole = PlaceSetTrap(e, p, TrapHole, 1);
            var mirror = PlaceSetTrap(e, p, MirrorForce, 2);
            var lp = p.LifePoints;
            var storm = PutInHand(e, o, HeavyStorm);
            var act = e.TryActivateSpellTrap(o, storm, fromHand: true);
            var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
            var chained = window && e.TryActivateSpellTrap(p, ep, fromHand: false);
            var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2;
            var pick1 = asked && e.TrySelectEffectTarget(hole);
            var pick2 = pick1 && e.TrySelectEffectTarget(mirror); // last candidate: the picks finish by themselves
            Drive(e, _ => false);
            Check("Emergency Provisions: chained to Heavy Storm — both Set cards are sent as the cost, 2000 LP, then the Storm resolves",
                main && act && window && chained && asked && pick1 && pick2 && p.LifePoints == lp + 2000 &&
                Where(e, hole) == "P.gy" && Where(e, mirror) == "P.gy" && Where(e, ep) == "P.gy" && Where(e, storm) == "O.gy" &&
                !e.Chain.HasLinks,
                $"main={main} act={act} window={window} chained={chained} asked={asked} picks={pick1}/{pick2} lp={p.LifePoints} " +
                $"ep={Where(e, ep)} storm={Where(e, storm)} {State(e)}");
        }

        static void ProvisionsAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var hole = PlaceSetTrap(e, o, TrapHole, 0);
            var mirror = PlaceSetTrap(e, o, MirrorForce, 1);
            var lp = o.LifePoints;
            var ep = PutInHand(e, o, Provisions);
            var act = e.TryActivateSpellTrap(o, ep, fromHand: true);
            Drive(e, _ => false);
            var sent = new[] { hole, mirror }.Count(c => Where(e, c) == "O.gy");
            Check("Emergency Provisions (AI): pays its cost without a prompt (nothing threatened: 1 card), 1000 LP per card",
                main && act && sent == 1 && o.LifePoints == lp + 1000 && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks &&
                Where(e, ep) == "O.gy",
                $"main={main} act={act} sent={sent} lp={o.LifePoints} {State(e)}");
        }

        /// <summary>
        /// Emergency Provisions (Quick-Play Spell, Lua EVENT_FREE_CHAIN) Set earlier answers the opponent's attack: the send
        /// cost is paid as it is activated, then the attack goes on.
        /// </summary>
        static void ProvisionsAnswersAnAttack(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var battle = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var ep = PlaceSetTrap(e, p, Provisions, 0);
            var hole = PlaceSetTrap(e, p, TrapHole, 1);
            var mirror = PlaceSetTrap(e, p, MirrorForce, 2);
            e.NotifyPublic();
            var lp = p.LifePoints;
            var atk = e.TryAttack(o, celtic, gaia);
            var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
            var act = window && e.TryActivateSpellTrap(p, ep, fromHand: false);
            var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2;
            var pick = asked && e.TrySelectEffectTarget(hole);
            var finish = pick && e.IsAwaitingEffectTarget && e.CancelEffectTargeting(); // CANCEL sends the one picked
            Drive(e, _ => false);
            DrainCombat(e);
            Check("Emergency Provisions: answers the opponent's attack — Trap Hole sent as the cost, 1000 LP, the attack goes on",
                battle && atk && window && act && asked && pick && finish && p.LifePoints == lp + 1000 &&
                Where(e, hole) == "P.gy" && Where(e, mirror) == "P.stz" && Where(e, ep) == "P.gy" && Where(e, gaia) == "P.mz" &&
                Where(e, celtic) == "O.gy" && !e.Chain.HasLinks,
                $"battle={battle} atk={atk} window={window} act={act} asked={asked} pick={pick} finish={finish} lp={p.LifePoints} " +
                $"hole={Where(e, hole)} mirror={Where(e, mirror)} ep={Where(e, ep)} celtic={Where(e, celtic)} {State(e)}");
        }

        /// <summary>Emergency Provisions in the opponent's open game state (their End Phase, for instance).</summary>
        static void ProvisionsInOpponentsOpenState(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var ep = PlaceSetTrap(e, p, Provisions, 0);
            var hole = PlaceSetTrap(e, p, TrapHole, 1);
            var mirror = PlaceSetTrap(e, p, MirrorForce, 2);
            var lp = p.LifePoints;
            var opened = e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
            var act = opened && e.TryActivateSpellTrap(p, ep, fromHand: false);
            var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2;
            var pick1 = asked && e.TrySelectEffectTarget(hole);
            var pick2 = pick1 && e.TrySelectEffectTarget(mirror); // last candidate: the picks finish by themselves
            Drive(e, _ => false);
            Check("Emergency Provisions: in the opponent's open game state — both Set cards sent, 2000 LP",
                main && opened && act && asked && pick1 && pick2 && p.LifePoints == lp + 2000 && Where(e, hole) == "P.gy" &&
                Where(e, mirror) == "P.gy" && Where(e, ep) == "P.gy" && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"main={main} opened={opened} act={act} asked={asked} picks={pick1}/{pick2} lp={p.LifePoints} {State(e)}");
        }

        /// <summary>
        /// The AI with nothing threatened sends a card that does nothing (a Set card) rather than its active Continuous
        /// Spell (The A. Forces) or Field Spell.
        /// </summary>
        static void ProvisionsAiKeepsActiveCards(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var forces = PlaceSetTrap(e, o, AForces, 0);
            forces.FaceUp = true;
            var hole = PlaceSetTrap(e, o, TrapHole, 1);
            var lp = o.LifePoints;
            var ep = PutInHand(e, o, Provisions);
            var act = e.TryActivateSpellTrap(o, ep, fromHand: true);
            Drive(e, _ => false);
            Check("Emergency Provisions (AI): nothing threatened — sends the Set Trap Hole, keeps its face-up The A. Forces",
                main && act && Where(e, hole) == "O.gy" && Where(e, forces) == "O.stz" && o.LifePoints == lp + 1000,
                $"main={main} act={act} hole={Where(e, hole)} forces={Where(e, forces)} lp={o.LifePoints} {State(e)}");
        }

        // ───────────────────────────── The A. Forces ─────────────────────────────

        static void AForcesLiveCount(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
            var dm = PlaceMonster(e, p, DarkMagician, 2, BattlePosition.Defense, false);
            var ox = PlaceMonster(e, p, BattleOx, 3, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var forces = PutInHand(e, p, AForces);
            var act = e.TryActivateSpellTrap(p, forces, fromHand: true);
            Drive(e, _ => false);
            e.NotifyPublic();
            var two = celtic.CurrentAtk == 1400 + 400 && elf.CurrentAtk == 800 && ox.CurrentAtk == 1700 &&
                      gaia.CurrentAtk == 2300;
            dm.FaceUp = true;
            dm.Position = BattlePosition.Attack;
            e.NotifyPublic();
            var three = celtic.CurrentAtk == 1400 + 600;
            e.SendCardToGrave(p, elf);
            e.NotifyPublic();
            var back = celtic.CurrentAtk == 1400 + 400;
            Check("The A. Forces: your Warriors gain 200 per face-up Warrior/Spellcaster you control, counted live",
                act && Where(e, forces) == "P.stz" && two && three && back,
                $"act={act} forces={Where(e, forces)} two={two} three={three} back={back} celtic={celtic.CurrentAtk} " +
                $"elf={elf.CurrentAtk} ox={ox.CurrentAtk} gaia={gaia.CurrentAtk} {State(e)}");
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

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Every prompt picks the first legal card matching <paramref name="want"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
        }

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

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/amt={c.Amount}/race={c.RaceFilter}/cond={c.ActivationCondition}:" +
                             $"{c.ConditionName}:{c.ConditionAmount}/choice={c.EffectChoiceIndex}/count={c.CountSource}:{c.CountRaceFilter}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
