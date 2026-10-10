using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// StandbyGrowthAndMaintenance batch (v142): Castle of Dark Illusions, Pumpking the King of Ghosts, Mirror Wall
    /// and Skull Invitation. Compile shapes, mini-duels
    /// through the real engine entry points (TryFlipSummon, TryAttack, TryActivateSpellTrap, response windows,
    /// EndTurn / Standby Phases), the shared chain guard for a Continuous activation whose card left the field
    /// before it resolved, the "pay N LP or destroy this card" upkeep choice, the review-fix cases (Tokens, Burning
    /// Land, costs paid at resolution, the AI's Skull Invitation) and fail-closed near misses. Runtime:
    /// <see cref="StandbyGrowthAndMaintenanceRuntime"/>.
    /// </summary>
    public static class StandbyGrowthAndMaintenanceRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Castle = 62121;             // Fiend Lv4 920/1930, FLIP
        const int Pumpking = 29155212;        // Zombie Lv6 1800/2000
        const int MirrorWall = 22359980;
        const int SkullInvitation = 98139712;
        const int SevenCompleted = 86198326;

        const int ArmoredZombie = 20277860;   // Zombie 1500/0
        const int DragonZombie = 66672569;    // Zombie 1600/0
        const int ClownZombie = 92667214;     // Zombie 1350/0
        const int ThreeLeggedZombies = 33734439; // Zombie 1100/800
        const int Celtic = 91152256;          // Warrior 1400/1200
        const int SummonedSkull = 70781052;   // Fiend 2500/1200
        const int GeminiElf = 69140098;       // 1900/900
        const int RoyalDecree = 51452091;
        const int BookOfMoon = 14087893;
        const int PotOfGreed = 55144522;
        const int HeavyStorm = 19613556;
        const int Raigeki = 12580477;
        const int Mst = 5318639;
        const int ImperialOrder = 61740673;
        const int MessengerOfPeace = 44656491;
        const int CallOfTheHaunted = 97077563;
        const int BurningLand = 24294108;     // Continuous Spell: "When this card is activated: … destroy them."
        const int Sogen = 86318356;           // Field Spell
        const int Umi = 22702055;             // Field Spell
        const int FinalDestiny = 18591904;    // Normal Spell: discard 5 cards; destroy all cards on the field
        const int LeviaDragonDaedalus = 37721209; // send 1 face-up "Umi" you control to the GY; destroy all other cards

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("StandbyGrowth: " + name, ok, detail ?? "");

            void Guard(string label, Action body)
            {
                try { body(); }
                catch (Exception ex) { Check($"{label}: no exception", false, ex.GetType().Name + ": " + ex.Message); }
            }

            Guard("compile shapes", () => CompileShapes(db, Check));
            Guard("castle", () => CastleDuels(db, pDeck, aDeck, Check));
            Guard("pumpking", () => PumpkingDuels(db, pDeck, aDeck, Check));
            Guard("mirror wall", () => MirrorWallDuels(db, pDeck, aDeck, Check));
            Guard("skull invitation", () => SkullInvitationDuels(db, pDeck, aDeck, Check));
            Guard("left the field on the chain", () => LeftFieldOnChain(db, pDeck, aDeck, Check));
            Guard("upkeep choice", () => UpkeepChoice(db, pDeck, aDeck, Check));
            Guard("review fixes", () => ReviewFixes(db, pDeck, aDeck, Check));
            Guard("near misses", () => NearMisses(db, Check));
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=" +
                  string.Join(" | ", prog.ClauseList.Select(c => $"{c?.Timing}:{c?.Action}")) +
                  (prog.UnparsedFragments != null && prog.UnparsedFragments.Length > 0
                      ? " unparsed=" + string.Join(" / ", prog.UnparsedFragments)
                      : "");

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 16; i++)
            {
                if (e.IsAwaitingResponse) { e.PassResponse(); continue; }
                if (e.IsAwaitingEffectTarget) { e.CancelEffectTargeting(); continue; }
                if (e.HasDeclaredAttack) { e.ResolveDeclaredAttack(); continue; }
                break;
            }
        }

        /// <summary>End turns until <paramref name="who"/> is in Main Phase 1 of a turn later than <paramref name="after"/>.</summary>
        static bool AdvanceToMain(DuelEngine e, DuelistState who, int after)
        {
            bool There() => e.TurnPlayer == who && e.Phase == DuelPhase.Main1 && e.TurnNumber > after &&
                            !e.IsAwaitingResponse;

            for (var t = 0; t < 12 && !There() && !e.GameOver; t++)
            {
                PassAll(e);
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (There()) break;
                if (e.TurnPlayer == who && e.Phase == DuelPhase.Main1 && e.TurnNumber > after) continue;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            PassAll(e);
            return There();
        }

        /// <summary>
        /// End turns until it is <paramref name="who"/>'s turn after <paramref name="after"/>. Unlike
        /// <see cref="AdvanceToMain"/> it stops as soon as that turn starts, so a choice waiting in its Standby
        /// Phase (the upkeep prompt) is left unanswered.
        /// </summary>
        static bool AdvanceToOwnTurn(DuelEngine e, DuelistState who, int after)
        {
            bool There() => e.TurnPlayer == who && e.TurnNumber > after;
            for (var t = 0; t < 12 && !There() && !e.GameOver; t++)
            {
                PassAll(e);
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (There()) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            return There();
        }

        static CardInstance FaceUpTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static DuelEngine FreshHvh(CardDatabase db, DeckFile p, DeckFile a)
        {
            var e = new DuelEngine { HumanVsHuman = true };
            e.StartDuel(db, p, a, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            return e;
        }

        static string Stats(CardInstance c) => c == null ? "null" : $"{c.CurrentAtk}/{c.CurrentDef}";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var castle = CardTextEffectCompiler.Compile(db.Get(Castle));
            Check("Castle of Dark Illusions: FullyCompiled FLIP Zombie +200/+200, +200 more on each of your next 4 Standby Phases",
                castle != null && castle.FullyCompiled && castle.ClauseList.Count == 1 &&
                castle.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                              c.Action == EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp &&
                                              c.RaceFilter == "Zombie" && c.Amount == 200 && c.DefAmount == 200 &&
                                              c.StandbyGrowthAmount == 200 && c.StandbyGrowthSteps == 4 &&
                                              !c.RequiresTargetChoice),
                Describe(castle));
            Check("Castle of Dark Illusions classifies Implemented",
                CardEffectStatus.Classify(db.Get(Castle)) == CardEffectStatusKind.Implemented);

            var pk = CardTextEffectCompiler.Compile(db.Get(Pumpking));
            Check("Pumpking: FullyCompiled continuous +100/+100 while Castle is on the field, +100 per Standby Phase (4 max)",
                pk != null && pk.FullyCompiled && pk.ClauseList.Count == 1 &&
                pk.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                          c.Action == EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby &&
                                          c.NamedCard == "Castle of Dark Illusions" && c.Amount == 100 &&
                                          c.DefAmount == 100 && c.StandbyGrowthAmount == 100 &&
                                          c.StandbyGrowthSteps == 4),
                Describe(pk));
            Check("Pumpking classifies Implemented",
                CardEffectStatus.Classify(db.Get(Pumpking)) == CardEffectStatusKind.Implemented);

            var mw = CardTextEffectCompiler.Compile(db.Get(MirrorWall));
            Check("Mirror Wall: FullyCompiled Activate + continuous halving, and your-Standby 2000 LP pay-or-destroy",
                mw != null && mw.FullyCompiled && mw.ClauseList.Count == 3 &&
                mw.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField &&
                                          c.StaysOnField && !c.RequiresTargetChoice) &&
                mw.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                          c.Action == EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField) &&
                mw.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.StandbyPhase &&
                                          c.Action == EffectActionKind.PayLpOrDestroyThis &&
                                          c.PayLpAmount == 2000 && !c.EachStandbyPhase),
                Describe(mw));
            Check("Mirror Wall classifies Implemented",
                CardEffectStatus.Classify(db.Get(MirrorWall)) == CardEffectStatusKind.Implemented);

            bool Optional(int id) => CardTextEffectCompiler.Compile(db.Get(id))?.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.PayLpOrDestroyThis && c.UpkeepIsOptional) ?? false;
            bool Mandatory(int id) => CardTextEffectCompiler.Compile(db.Get(id))?.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.PayLpOrDestroyThis && !c.UpkeepIsOptional) ?? false;
            Check("Upkeep: \"pay N LP or destroy this card\" is optional (Mirror Wall, Messenger of Peace); " +
                  "\"you must pay … (this is not optional)\" is not (Imperial Order)",
                Optional(MirrorWall) && Optional(MessengerOfPeace) && Mandatory(ImperialOrder) &&
                !Optional(ImperialOrder));

            var si = CardTextEffectCompiler.Compile(db.Get(SkullInvitation));
            Check("Skull Invitation: FullyCompiled Activate + continuous 300 to the owner per card sent to the GY",
                si != null && si.FullyCompiled && si.ClauseList.Count == 2 &&
                si.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.InflictDamageToOwnerPerCardSentToGy &&
                                          c.Amount == 300 && c.StaysOnField) &&
                si.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                          c.Action == EffectActionKind.InflictDamageToOwnerPerCardSentToGy &&
                                          c.Amount == 300),
                Describe(si));
            Check("Skull Invitation classifies Implemented",
                CardEffectStatus.Classify(db.Get(SkullInvitation)) == CardEffectStatusKind.Implemented);

            var seven = CardTextEffectCompiler.Compile(db.Get(SevenCompleted));
            Check("7 Completed: FullyCompiled equip, ATK or DEF chosen at activation",
                seven != null && seven.FullyCompiled &&
                seven.ClauseList.Exists(c => c != null && c.ChooseAtkOrDef &&
                    c.Action == EffectActionKind.EquipThisToTarget &&
                    c.RaceFilter == "Machine" && c.EquipAtkBonus == 700 && c.EquipDefBonus == 700) &&
                CardEffectStatus.Classify(db.Get(SevenCompleted)) == CardEffectStatusKind.Implemented,
                Describe(seven));

            Check("New actions map to shared resolution kinds",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp }) ==
                EffectResolutionKind.ModifyStats &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby }) ==
                EffectResolutionKind.ModifyStats &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField }) ==
                EffectResolutionKind.ModifyStats &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.InflictDamageToOwnerPerCardSentToGy }) ==
                EffectResolutionKind.Damage);
        }

        // ───────────────────────────── Castle of Dark Illusions ─────────────────────────────

        static void CastleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // FLIP: every Zombie on the field now (both sides) gains 200/200; not a non-Zombie, not a Zombie that
            // arrives later; a face-down Zombie that was there gains once it is face-up. Your next 4 Standby
            // Phases add 200 each (the opponent's do not), then it stops at +1000.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var castle = PlaceMonster(e, p, Castle, 0, BattlePosition.Defense, false);
                var armored = PlaceMonster(e, p, ArmoredZombie, 1, BattlePosition.Attack, true);
                var dragonZ = PlaceMonster(e, o, DragonZombie, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var clownFd = PlaceMonster(e, o, ClownZombie, 2, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, castle);
                PassAll(e);
                e.NotifyPublic();
                Check("Castle: FLIP gives every Zombie on the field (both sides) +200/+200, not the Warrior",
                    flipped && castle.FaceUp && armored.CurrentAtk == 1700 && armored.CurrentDef == 200 &&
                    dragonZ.CurrentAtk == 1800 && dragonZ.CurrentDef == 200 && celtic.CurrentAtk == 1400 &&
                    celtic.CurrentDef == 1200,
                    $"flipped={flipped} armored={Stats(armored)} dragon={Stats(dragonZ)} celtic={Stats(celtic)}");

                var late = PlaceMonster(e, p, ThreeLeggedZombies, 2, BattlePosition.Attack, true);
                late.FieldEntryStamp++;
                clownFd.FaceUp = true;
                clownFd.Position = BattlePosition.Attack;
                e.NotifyPublic();
                Check("Castle: a Zombie that arrives later gains nothing; a Zombie face-down at the FLIP gains once face-up",
                    late.CurrentAtk == 1100 && late.CurrentDef == 800 && clownFd.CurrentAtk == 1550 &&
                    clownFd.CurrentDef == 200,
                    $"late={Stats(late)} clown={Stats(clownFd)}");

                var t0 = e.TurnNumber;
                e.EndTurn(p);
                PassAll(e);
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                var afterOppStandby = armored.CurrentAtk;
                var reached1 = AdvanceToMain(e, p, t0);
                Check("Castle: the opponent's Standby Phase does not count; your next Standby Phase adds 200 more",
                    afterOppStandby == 1700 && reached1 && armored.CurrentAtk == 1900 && armored.CurrentDef == 400 &&
                    dragonZ.CurrentAtk == 2000 && celtic.CurrentAtk == 1400,
                    $"oppStandby={afterOppStandby} reached={reached1} armored={Stats(armored)} dragon={Stats(dragonZ)}");

                var turns = 1;
                for (var i = 0; i < 4; i++)
                {
                    if (AdvanceToMain(e, p, e.TurnNumber)) turns++;
                }

                Check("Castle: 4 of your Standby Phases → +1000 in all, and a 5th adds nothing",
                    turns == 5 && armored.CurrentAtk == 2500 && armored.CurrentDef == 1000 &&
                    castle.StandbyGrowth != null && castle.StandbyGrowth.Steps == 4,
                    $"turns={turns} armored={Stats(armored)} steps={castle.StandbyGrowth?.Steps}");

                e.DestroyMonsterPublic(p, castle);
                e.NotifyPublic();
                Check("Castle: leaving the field ends every gain",
                    armored.CurrentAtk == 1500 && armored.CurrentDef == 0 && dragonZ.CurrentAtk == 1600,
                    $"armored={Stats(armored)} dragon={Stats(dragonZ)}");
            }

            // Turned face-down (Book of Moon) → the gain ends and does not come back on its own.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                var castle = PlaceMonster(e, p, Castle, 0, BattlePosition.Defense, false);
                var armored = PlaceMonster(e, p, ArmoredZombie, 1, BattlePosition.Attack, true);
                e.TryFlipSummon(p, castle);
                PassAll(e);
                e.NotifyPublic();
                var boosted = armored.CurrentAtk == 1700;
                var moon = PutInHand(e, p, BookOfMoon);
                var act = e.TryActivateSpellTrap(p, moon, fromHand: true);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(castle);
                PassAll(e);
                e.NotifyPublic();
                var downAtk = armored.CurrentAtk;
                castle.FaceUp = true; // face-up again without a new FLIP resolution
                e.NotifyPublic();
                Check("Castle: turned face-down → the gain ends for good (a new FLIP would be a new effect)",
                    boosted && act && sel && downAtk == 1500 && armored.CurrentAtk == 1500,
                    $"boosted={boosted} act={act} sel={sel} down={downAtk} after={armored.CurrentAtk}");
            }

            // Negated → the gain ends for good (RESETS_STANDARD_DISABLE).
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var castle = PlaceMonster(e, p, Castle, 0, BattlePosition.Defense, false);
                var armored = PlaceMonster(e, p, ArmoredZombie, 1, BattlePosition.Attack, true);
                e.TryFlipSummon(p, castle);
                PassAll(e);
                castle.IsNegated = true;
                e.NotifyPublic();
                var negAtk = armored.CurrentAtk;
                castle.IsNegated = false;
                e.NotifyPublic();
                Check("Castle: negated → the gain ends and does not return when the negation ends",
                    negAtk == 1500 && armored.CurrentAtk == 1500, $"neg={negAtk} after={armored.CurrentAtk}");
            }

            // A boosted Zombie that leaves the field and comes back is a new monster: no gain.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var castle = PlaceMonster(e, p, Castle, 0, BattlePosition.Defense, false);
                var armored = PlaceMonster(e, p, ArmoredZombie, 1, BattlePosition.Attack, true);
                e.TryFlipSummon(p, castle);
                PassAll(e);
                e.NotifyPublic();
                e.DestroyMonsterPublic(p, armored);
                p.Graveyard.Remove(armored);
                var back = e.SpecialSummonToField(p, armored, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Castle: a boosted Zombie that left the field and was Special Summoned again gains nothing",
                    back && p.TryFindMonster(armored, out _) && armored.CurrentAtk == 1500,
                    $"back={back} armored={Stats(armored)}");
            }
        }

        // ───────────────────────────── Pumpking ─────────────────────────────

        static void PumpkingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // +100 while Castle is on the field (either side); +100 more on each of your Standby Phases while it is,
            // four at most; the opponent's Standby Phases do not count; Castle gone → gain and count reset.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var pk = PlaceMonster(e, p, Pumpking, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var alone = Stats(pk);
                var castle = PlaceMonster(e, o, Castle, 0, BattlePosition.Defense, true);
                e.NotifyPublic();
                Check("Pumpking: 1800/2000 alone; +100/+100 while a face-up Castle is on the field (the opponent's too)",
                    alone == "1800/2000" && pk.CurrentAtk == 1900 && pk.CurrentDef == 2100,
                    $"alone={alone} with={Stats(pk)}");

                castle.FaceUp = false;
                e.NotifyPublic();
                var faceDown = Stats(pk);
                castle.FaceUp = true;
                e.NotifyPublic();
                Check("Pumpking: a face-down Castle does not count",
                    faceDown == "1800/2000" && pk.CurrentAtk == 1900, $"fd={faceDown} back={Stats(pk)}");

                var t0 = e.TurnNumber;
                e.EndTurn(p);
                PassAll(e);
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                var oppStandby = pk.CurrentAtk;
                var reached = AdvanceToMain(e, p, t0);
                Check("Pumpking: the opponent's Standby Phase does not count; your Standby Phase adds 100",
                    oppStandby == 1900 && reached && pk.CurrentAtk == 2000 && pk.CurrentDef == 2200,
                    $"opp={oppStandby} reached={reached} pk={Stats(pk)}");

                for (var i = 0; i < 4; i++)
                    AdvanceToMain(e, p, e.TurnNumber);
                Check("Pumpking: four of your Standby Phases at most → 2300/2500",
                    pk.CurrentAtk == 2300 && pk.CurrentDef == 2500, $"pk={Stats(pk)}");

                e.DestroyMonsterPublic(o, castle);
                e.NotifyPublic();
                var gone = Stats(pk);
                var castle2 = PlaceMonster(e, p, Castle, 1, BattlePosition.Defense, true);
                e.NotifyPublic();
                Check("Pumpking: Castle gone → the gain disappears; a new Castle starts again at +100",
                    gone == "1800/2000" && pk.CurrentAtk == 1900 && pk.CurrentDef == 2100 && castle2.FaceUp,
                    $"gone={gone} again={Stats(pk)}");

                var t1 = e.TurnNumber;
                var ok = AdvanceToMain(e, p, t1);
                Check("Pumpking: after the reset its Standby count starts over (+100 once)",
                    ok && pk.CurrentAtk == 2000, $"ok={ok} pk={Stats(pk)}");
            }

            // No Castle during your Standby Phase → nothing counts; flipping Castle adds its own +200 on top.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var pk = PlaceMonster(e, p, Pumpking, 0, BattlePosition.Attack, true);
                var t0 = e.TurnNumber;
                var reached = AdvanceToMain(e, p, t0);
                e.NotifyPublic();
                var noCastle = Stats(pk);
                var castle = PlaceMonster(e, p, Castle, 1, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, castle);
                PassAll(e);
                e.NotifyPublic();
                Check("Pumpking: no Castle at your Standby Phase → no count; Castle's FLIP adds +200 to its +100",
                    reached && noCastle == "1800/2000" && flipped && pk.CurrentAtk == 2100 && pk.CurrentDef == 2300,
                    $"reached={reached} noCastle={noCastle} flipped={flipped} pk={Stats(pk)}");

                pk.IsNegated = true;
                e.NotifyPublic();
                var neg = pk.CurrentAtk;
                pk.IsNegated = false;
                Check("Pumpking: negated → only Castle's own +200 remains", neg == 2000, $"neg={neg}");
            }
        }

        // ───────────────────────────── Mirror Wall ─────────────────────────────

        static void MirrorWallDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Face-up Mirror Wall: the opponent's attacker is halved at declaration (direct attack: 1250), and
            // stays halved while the wall remains; it is back to 2500 once the wall leaves.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var before = skull.CurrentAtk;
                var lp = p.LifePoints;
                var declared = e.TryAttack(o, skull, null);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Mirror Wall: an opponent's monster that attacks is halved at once (direct attack 2500 → 1250)",
                    reached && before == 2500 && declared && p.LifePoints == lp - 1250 && skull.CurrentAtk == 1250,
                    $"reached={reached} before={before} declared={declared} lp {lp}->{p.LifePoints} atk={skull.CurrentAtk}");

                e.TryEnterMainPhase2(o);
                e.NotifyPublic();
                var mp2 = skull.CurrentAtk;
                e.SendCardToGrave(p, wall);
                e.NotifyPublic();
                Check("Mirror Wall: it stays halved while the wall remains; the wall leaves → full ATK again",
                    mp2 == 1250 && skull.CurrentAtk == 2500, $"mp2={mp2} after={skull.CurrentAtk}");
            }

            // Activated in response to the attack: the current attacker is halved for that battle.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var wall = PlaceSetTrap(e, p, MirrorWall, 0);
                var elf = PlaceMonster(e, p, GeminiElf, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var oppLp = o.LifePoints;
                var declared = e.TryAttack(o, skull, elf);
                var offered = e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                              e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(wall);
                var act = offered && e.TryActivateSpellTrap(p, wall, fromHand: false);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Mirror Wall: activated when the opponent attacks → that attacker is halved (1250 loses to 1900)",
                    reached && declared && offered && act && wall.FaceUp && p.TryFindSpellTrap(wall, out _) &&
                    o.Graveyard.Contains(skull) && p.TryFindMonster(elf, out _) && o.LifePoints == oppLp - 650,
                    $"reached={reached} declared={declared} offered={offered} act={act} skullGy={o.Graveyard.Contains(skull)} " +
                    $"elf={p.TryFindMonster(elf, out _)} oppLp {oppLp}->{o.LifePoints}");
            }

            // Your own attacking monster is never halved by your Mirror Wall.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Player, 2);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, MirrorWall, 0);
                var mine = PlaceMonster(e, p, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var oppLp = o.LifePoints;
                var declared = e.TryAttack(p, mine, null);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Mirror Wall: your own attacking monster is not halved (2500 direct damage)",
                    reached && bp && declared && o.LifePoints == oppLp - 2500 && mine.CurrentAtk == 2500,
                    $"reached={reached} bp={bp} declared={declared} opp {oppLp}->{o.LifePoints} atk={mine.CurrentAtk}");
            }

            // A marked monster that leaves and comes back is a new monster; halving is the final step (Castle's
            // +200 counts before the half).
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, MirrorWall, 0);
                var castle = PlaceMonster(e, o, Castle, 1, BattlePosition.Defense, true);
                var zombie = PlaceMonster(e, o, ArmoredZombie, 0, BattlePosition.Attack, true);
                castle.StandbyGrowth = new StandbyGrowthGrant
                {
                    EntryStamp = castle.FieldEntryStamp, FaceDownStamp = castle.FaceDownStamp,
                    StartTurn = e.TurnNumber, Targets = new System.Collections.Generic.List<(CardInstance, int)>
                        { (zombie, zombie.FieldEntryStamp) }
                };
                e.NotifyPublic();
                var boosted = zombie.CurrentAtk;
                var lp = p.LifePoints;
                var declared = e.TryAttack(o, zombie, null);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Mirror Wall: the halving is the final ATK — Armored Zombie 1500 + Castle 200 = 1700 → 850",
                    reached && boosted == 1700 && declared && zombie.CurrentAtk == 850 && p.LifePoints == lp - 850,
                    $"reached={reached} boosted={boosted} declared={declared} atk={zombie.CurrentAtk} lp {lp}->{p.LifePoints}");

                e.DestroyMonsterPublic(o, zombie);
                o.Graveyard.Remove(zombie);
                var back = e.SpecialSummonToField(o, zombie, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Mirror Wall: a marked monster that leaves and returns is a new monster (full ATK)",
                    back && zombie.CurrentAtk == 1500, $"back={back} atk={zombie.CurrentAtk}");
            }

            // Negated by Royal Decree: attacks are not recorded and nothing is halved.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                var decree = FaceUpTrap(e, o, RoyalDecree, 0);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var declared = e.TryAttack(o, skull, null);
                DrainCombat(e);
                e.NotifyPublic();
                var underDecree = skull.CurrentAtk;
                e.SendCardToGrave(o, decree);
                e.NotifyPublic();
                Check("Mirror Wall under Royal Decree: the attack is not recorded (full damage, still 2500 after Decree leaves)",
                    reached && wall.IsNegated == false && declared && p.LifePoints == lp - 2500 && underDecree == 2500 &&
                    skull.CurrentAtk == 2500,
                    $"reached={reached} declared={declared} lp {lp}->{p.LifePoints} under={underDecree} after={skull.CurrentAtk}");
            }

            // Your Standby Phase: pay 2000 LP (it stays), or it is destroyed when you cannot.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                var t0 = e.TurnNumber;
                var lp = p.LifePoints;
                e.EndTurn(p);
                PassAll(e);
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                var lpAfterOpp = p.LifePoints;
                // Your Standby Phase asks "pay 2000 LP or destroy this card"; PAY 2000 keeps it.
                var ownTurn = AdvanceToOwnTurn(e, p, t0);
                var asked = e.PendingActivation?.AwaitingUpkeepChoice == true;
                var paid = asked && e.TrySelectLpCost(2000);
                var reached = AdvanceToMain(e, p, t0);
                Check("Mirror Wall: pays 2000 LP in your Standby Phase only, and stays",
                    ownTurn && asked && paid && reached && lpAfterOpp == lp && p.LifePoints == lp - 2000 &&
                    p.TryFindSpellTrap(wall, out _),
                    $"ownTurn={ownTurn} asked={asked} paid={paid} reached={reached} oppStandbyLp={lpAfterOpp} " +
                    $"lp {lp}->{p.LifePoints}");

                p.LifePoints = 1500;
                var t1 = e.TurnNumber;
                var reached2 = AdvanceToMain(e, p, t1);
                Check("Mirror Wall: cannot pay 2000 LP → destroyed, no LP paid",
                    reached2 && p.LifePoints == 1500 && p.Graveyard.Contains(wall) && !p.TryFindSpellTrap(wall, out _),
                    $"reached={reached2} lp={p.LifePoints} gy={p.Graveyard.Contains(wall)}");
            }

            // Exactly 2000 LP: paying would lose the Duel, so the optional upkeep is not paid — the wall is destroyed.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                var t0 = e.TurnNumber;
                e.EndTurn(p);
                PassAll(e);
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                p.LifePoints = 2000;
                var reached = AdvanceToMain(e, p, t0);
                Check("Mirror Wall: with exactly 2000 LP the optional upkeep is not paid (destroyed, still 2000 LP, no loss)",
                    reached && !e.GameOver && p.LifePoints == 2000 && p.Graveyard.Contains(wall),
                    $"reached={reached} over={e.GameOver} lp={p.LifePoints} gy={p.Graveyard.Contains(wall)}");
            }

            // AI: its Set Mirror Wall answers a strong attack; the attacker is halved.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Player, 2);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var wall = PlaceSetTrap(e, o, MirrorWall, 0);
                var skull = PlaceMonster(e, p, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var lp = o.LifePoints;
                var declared = e.TryAttack(p, skull, null);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Mirror Wall (AI): answers a 2500 attack — the attacker is halved (1250 damage)",
                    reached && bp && declared && wall.FaceUp && o.LifePoints == lp - 1250 && skull.CurrentAtk == 1250,
                    $"reached={reached} bp={bp} declared={declared} wallUp={wall.FaceUp} lp {lp}->{o.LifePoints} atk={skull.CurrentAtk}");
            }
        }

        // ───────────────────────────── Skull Invitation ─────────────────────────────

        static void SkullInvitationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Activated from a Set: cards already in the GY never count; Pot of Greed sent to its owner's GY → 300
            // to its owner.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                p.Graveyard.Add(e.CreateCardInstance(Celtic));
                var skull = PlaceSetTrap(e, p, SkullInvitation, 0);
                skull.SetThisTurn = false;
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, skull, fromHand: false);
                PassAll(e);
                e.NotifyPublic();
                var afterAct = (p.LifePoints, o.LifePoints);
                var pot = PutInHand(e, p, PotOfGreed);
                var potOk = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation: activation costs nothing for cards already in the GY; Pot of Greed → 300 to you",
                    act && skull.FaceUp && afterAct.Item1 == lp && afterAct.Item2 == oppLp && potOk &&
                    p.Graveyard.Contains(pot) && p.LifePoints == lp - 300 && o.LifePoints == oppLp,
                    $"act={act} afterAct={afterAct} potOk={potOk} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }

            // Battle: both monsters destroyed at once → 300 to each owner. Raigeki: 3 of the opponent's monsters
            // → 900 to them, and Raigeki itself → 300 to you.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, SkullInvitation, 0);
                var mine = PlaceMonster(e, p, GeminiElf, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(e, o, GeminiElf, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var declared = e.TryAttack(o, theirs, mine);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Skull Invitation: both battlers destroyed → 300 to each owner",
                    reached && declared && p.Graveyard.Contains(mine) && o.Graveyard.Contains(theirs) &&
                    p.LifePoints == lp - 300 && o.LifePoints == oppLp - 300,
                    $"reached={reached} declared={declared} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, o, SkullInvitation, 0);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, ArmoredZombie, 1, BattlePosition.Attack, true);
                PlaceMonster(e, o, DragonZombie, 2, BattlePosition.Defense, false);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var rai = PutInHand(e, p, Raigeki);
                var ok = e.TryActivateSpellTrap(p, rai, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation (opponent's): Raigeki destroys 3 of their monsters → 900 to them, Raigeki → 300 to you",
                    ok && o.MonsterCount == 0 && o.LifePoints == oppLp - 900 && p.LifePoints == lp - 300,
                    $"ok={ok} oppMonsters={o.MonsterCount} opp {oppLp}->{o.LifePoints} lp {lp}->{p.LifePoints}");
            }

            // Sent to the GY together with other cards (Heavy Storm) → it is gone before they arrive: no damage.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var skull = FaceUpTrap(e, o, SkullInvitation, 0);
                PlaceSetTrap(e, o, MirrorWall, 1);
                PlaceSetTrap(e, p, RoyalDecree, 0);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var storm = PutInHand(e, p, HeavyStorm);
                var ok = e.TryActivateSpellTrap(p, storm, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation: destroyed with the other Spells/Traps (Heavy Storm) → no damage at all",
                    ok && o.Graveyard.Contains(skull) && p.Graveyard.Contains(storm) && p.LifePoints == lp &&
                    o.LifePoints == oppLp,
                    $"ok={ok} skullGy={o.Graveyard.Contains(skull)} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }

            // The same with Skull Invitation on the caster's side, after another of the caster's cards in zone order:
            // a whole-field destruction is one event, so it never sees the cards destroyed with it.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceSetTrap(e, p, RoyalDecree, 0);
                var skull = FaceUpTrap(e, p, SkullInvitation, 4);
                PlaceSetTrap(e, o, MirrorWall, 0);
                PlaceSetTrap(e, o, RoyalDecree, 1);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var storm = PutInHand(e, p, HeavyStorm);
                var ok = e.TryActivateSpellTrap(p, storm, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation (caster's side, last zone): Heavy Storm destroys it with 3 other cards → no damage",
                    ok && p.Graveyard.Contains(skull) && p.Graveyard.Contains(mine) && o.SpellTrapsOnField().Count() == 0 &&
                    p.LifePoints == lp && o.LifePoints == oppLp,
                    $"ok={ok} skullGy={p.Graveyard.Contains(skull)} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }

            // Activated as Chain Link 1 (the opponent's, in their turn): a card that your Chain Link 2 MST destroys
            // before Skull Invitation's own activation resolves does not count (its effect applies only once it
            // resolves); your MST, which reaches the GY after the chain, does (300 to you).
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Opponent, 1);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                var skull = PlaceSetTrap(e, o, SkullInvitation, 0);
                skull.SetThisTurn = false;
                var theirs = PlaceSetTrap(e, o, RoyalDecree, 1);
                var myMst = PlaceSetTrap(e, p, Mst, 0);
                myMst.SetThisTurn = false;
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var dbg = new System.Text.StringBuilder();
                e.OnLog += m => dbg.Append(" | ").Append(m);
                var cl1 = e.TryActivateSpellTrap(o, skull, fromHand: false);
                var cl2 = e.IsAwaitingResponse && e.PendingResponse.Responder == p &&
                          e.TryActivateChainResponse(p, myMst);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(theirs);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation as CL1: CL2's destruction before it resolves is free; the MST sent after the chain costs its owner 300",
                    reached && cl1 && cl2 && skull.FaceUp && o.TryFindSpellTrap(skull, out _) &&
                    o.Graveyard.Contains(theirs) && p.Graveyard.Contains(myMst) && o.LifePoints == oppLp &&
                    p.LifePoints == lp - 300,
                    $"reached={reached} cl1={cl1} cl2={cl2} theirsGy={o.Graveyard.Contains(theirs)} " +
                    $"mstGy={p.Graveyard.Contains(myMst)} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints} LOG{dbg}");
            }

            // Chain: CL1 Pot of Greed, CL2 the opponent's MST on Skull Invitation, CL3 your MST on their Set card.
            // CL3's destruction happens while Skull Invitation is still there (300 to them); CL2 then destroys it,
            // so the two MSTs and Pot of Greed that reach the GY afterwards cost nothing.
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Player, 2);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var skull = FaceUpTrap(e, p, SkullInvitation, 0);
                var myMst = PlaceSetTrap(e, p, Mst, 1);
                var oppMst = PlaceSetTrap(e, o, Mst, 0);
                var target = PlaceSetTrap(e, o, RoyalDecree, 1);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var pot = PutInHand(e, p, PotOfGreed);
                var dbg = new System.Text.StringBuilder();
                e.OnLog += m => dbg.Append(" | ").Append(m);
                var cl1 = e.TryActivateSpellTrap(p, pot, fromHand: true);
                var cl2 = e.IsAwaitingResponse && e.PendingResponse.Responder == o &&
                          e.TryActivateChainResponse(o, oppMst);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
                var cl3 = e.IsAwaitingResponse && e.PendingResponse.Responder == p &&
                          e.TryActivateChainResponse(p, myMst);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(target);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation: cards sent by an earlier-resolving link count; after a later link destroys it, nothing does",
                    reached && cl1 && cl2 && cl3 && o.Graveyard.Contains(target) && p.Graveyard.Contains(skull) &&
                    p.Graveyard.Contains(pot) && o.LifePoints == oppLp - 300 && p.LifePoints == lp,
                    $"reached={reached} cl1={cl1} cl2={cl2} cl3={cl3} targetGy={o.Graveyard.Contains(target)} " +
                    $"skullGy={p.Graveyard.Contains(skull)} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints} LOG{dbg}");
            }

            // Activated in response to an attack: it watches from its resolution, so the battle's destruction counts.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var skull = PlaceSetTrap(e, p, SkullInvitation, 0);
                var elf = PlaceMonster(e, p, GeminiElf, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var declared = e.TryAttack(o, attacker, elf);
                var offered = e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                              e.PendingResponse.LegalCards.Contains(skull);
                var act = offered && e.TryActivateSpellTrap(p, skull, fromHand: false);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Skull Invitation: activated when attacked → the destroyed Gemini Elf costs you 300 (plus 600 battle damage)",
                    reached && declared && offered && act && skull.FaceUp && p.Graveyard.Contains(elf) &&
                    p.LifePoints == lp - 900,
                    $"reached={reached} declared={declared} offered={offered} act={act} lp {lp}->{p.LifePoints}");
            }

            // Negated by Royal Decree → no damage; a Tribute is a card sent to the GY.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                FaceUpTrap(e, p, SkullInvitation, 0);
                var decree = FaceUpTrap(e, o, RoyalDecree, 0);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var pot = PutInHand(e, p, PotOfGreed);
                e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                var underDecree = p.LifePoints;
                e.SendCardToGrave(o, decree);
                e.NotifyPublic();
                var afterDecreeLeft = (p.LifePoints, o.LifePoints);
                var tribute = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var big = PutInHand(e, p, Pumpking);
                var summoned = e.TryNormalSummon(p, big, asSet: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation != null) e.TrySelectEffectTarget(tribute);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation: negated by Royal Decree → no damage; once Decree is gone, a Tribute costs its owner 300",
                    underDecree == lp && afterDecreeLeft.Item1 == lp && p.Graveyard.Contains(tribute) &&
                    p.TryFindMonster(big, out _) && p.LifePoints == lp - 300,
                    $"under={underDecree} afterDecree={afterDecreeLeft} summoned={summoned} tributeGy={p.Graveyard.Contains(tribute)} lp {lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── a Continuous activation that loses its card ─────────────────────────────

        /// <summary>
        /// A Continuous Trap whose activation is answered by an MST that destroys it: the activation resolves with no
        /// effect and the card stays in the GY (it was put back on the field by the stays-on-field finish, and so sat
        /// in the GY and on the field at once). Shared chain-loop guard
        /// (<see cref="StandbyGrowthAndMaintenanceRuntime.ContinuousActivationLeftField"/>).
        /// </summary>
        static void LeftFieldOnChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var id in new[] { SkullInvitation, MirrorWall, CallOfTheHaunted })
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Player, 2);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var elf = e.CreateCardInstance(GeminiElf);
                p.Graveyard.Add(elf);
                var card = PlaceSetTrap(e, p, id, 0);
                card.SetThisTurn = false;
                var oppMst = PlaceSetTrap(e, o, Mst, 0);
                oppMst.SetThisTurn = false;
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var cl1 = e.TryActivateSpellTrap(p, card, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(elf);
                // The opponent's MST (an AI-side pick) targets the only Spell/Trap it can destroy: this card.
                var cl2 = e.IsAwaitingResponse && e.PendingResponse.Responder == o &&
                          e.TryActivateChainResponse(o, oppMst);
                PassAll(e);
                e.NotifyPublic();
                var name = e.CreateCardInstance(id).Name;
                Check($"{name} destroyed by an MST chained to its activation → no effect, only in the GY",
                    reached && cl1 && cl2 && p.Graveyard.Contains(card) && !p.TryFindSpellTrap(card, out _) &&
                    o.Graveyard.Contains(oppMst) && p.MonsterCount == 0 && p.LifePoints == lp &&
                    o.LifePoints == oppLp,
                    $"reached={reached} cl1={cl1} cl2={cl2} gy={p.Graveyard.Contains(card)} " +
                    $"field={p.TryFindSpellTrap(card, out _)} monsters={p.MonsterCount} lp {lp}->{p.LifePoints} " +
                    $"opp {oppLp}->{o.LifePoints}");
            }

            // Mirror Wall seated at an attack declaration, destroyed by a chained MST: the attacker keeps its ATK.
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Opponent, 1);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                var wall = PlaceSetTrap(e, p, MirrorWall, 0);
                wall.SetThisTurn = false;
                var oppMst = PlaceSetTrap(e, o, Mst, 0);
                oppMst.SetThisTurn = false;
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(o);
                PassAll(e);
                var lp = p.LifePoints;
                var declared = e.TryAttack(o, skull, null);
                var act = e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                          e.TryActivateSpellTrap(p, wall, fromHand: false);
                var cl2 = e.IsAwaitingResponse && e.PendingResponse.Responder == o &&
                          e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                          e.TryActivateChainResponse(o, oppMst);
                DrainCombat(e);
                e.NotifyPublic();
                Check("Mirror Wall answered by an MST at the attack → not halved (2500 damage), the wall only in the GY",
                    reached && bp && declared && act && cl2 && p.Graveyard.Contains(wall) &&
                    !p.TryFindSpellTrap(wall, out _) && skull.CurrentAtk == 2500 && p.LifePoints == lp - 2500,
                    $"reached={reached} bp={bp} declared={declared} act={act} cl2={cl2} gy={p.Graveyard.Contains(wall)} " +
                    $"field={p.TryFindSpellTrap(wall, out _)} atk={skull.CurrentAtk} lp {lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── upkeep choice ─────────────────────────────

        /// <summary>
        /// "Pay N LP or destroy this card" is the controller's choice (Lua CheckLPCost + SelectYesNo). A human is
        /// asked in their Standby Phase through the LP-cost picker (PAY N / Cancel), and the Standby Phase waits;
        /// the AI pays only while it keeps more than N LP afterwards.
        /// </summary>
        static void UpkeepChoice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human, 2500 LP: the Standby Phase waits for the choice; PAY 2000 keeps the wall, Cancel destroys it.
            foreach (var pay in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                p.LifePoints = 2500;
                var t0 = e.TurnNumber;
                var ownTurn = AdvanceToOwnTurn(e, p, t0);
                var pend = e.PendingActivation;
                var asked = ownTurn && e.Phase == DuelPhase.Standby && pend != null && pend.AwaitingUpkeepChoice &&
                            pend.Controller == p && ReferenceEquals(pend.Card, wall) && pend.AwaitingLpCost &&
                            pend.LpCostChoices.Count == 1 && pend.LpCostChoices[0] == 2000 &&
                            p.LifePoints == 2500 && p.TryFindSpellTrap(wall, out _);
                var prompt = pend?.Prompt ?? "";
                var held = e.Phase == DuelPhase.Standby;
                e.ContinueAfterStandby(); // the choice must be answered first
                var stillHeld = e.Phase == DuelPhase.Standby && e.PendingActivation == pend;
                var answered = pay ? e.TrySelectLpCost(2000) : e.CancelEffectTargeting();
                var ok = pay
                    ? p.LifePoints == 500 && p.TryFindSpellTrap(wall, out _)
                    : p.LifePoints == 2500 && p.Graveyard.Contains(wall) && !p.TryFindSpellTrap(wall, out _);
                Check(pay
                        ? "Upkeep choice: your Standby Phase waits for Mirror Wall's \"pay 2000 LP or destroy\"; PAY 2000 " +
                          "keeps it (2500 → 500), then Main Phase 1"
                        : "Upkeep choice: Cancel destroys Mirror Wall instead (LP stays 2500), then Main Phase 1",
                    asked && held && stillHeld && answered && ok && e.Phase == DuelPhase.Main1 &&
                    !e.IsAwaitingEffectTarget && prompt.Contains("2000") && prompt.Contains("destroy"),
                    $"ownTurn={ownTurn} asked={asked} held={held} stillHeld={stillHeld} answered={answered} " +
                    $"phase={e.Phase} lp={p.LifePoints} field={p.TryFindSpellTrap(wall, out _)} " +
                    $"gy={p.Graveyard.Contains(wall)} prompt='{prompt}'");
            }

            // Two optional upkeeps: asked one after the other; the Standby Phase ends after the last answer.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                var msg = FaceUpTrap(e, p, MessengerOfPeace, 1);
                var lp = p.LifePoints;
                var t0 = e.TurnNumber;
                var ownTurn = AdvanceToOwnTurn(e, p, t0);
                var first = e.PendingActivation;
                var firstOk = first != null && first.AwaitingUpkeepChoice &&
                              (ReferenceEquals(first.Card, wall) || ReferenceEquals(first.Card, msg));
                var firstAmount = first?.CostNumeric ?? 0;
                var paid = firstOk && e.TrySelectLpCost(firstAmount);
                var second = e.PendingActivation;
                var secondOk = second != null && second.AwaitingUpkeepChoice && second != first &&
                               (ReferenceEquals(second.Card, wall) || ReferenceEquals(second.Card, msg)) &&
                               !ReferenceEquals(second.Card, first.Card) && e.Phase == DuelPhase.Standby;
                var declined = secondOk && e.CancelEffectTargeting();
                var kept = first?.Card;
                var lost = second?.Card;
                Check("Upkeep choice: Mirror Wall and Messenger of Peace are asked one after the other (pay one, " +
                      "destroy the other), then Main Phase 1",
                    ownTurn && firstOk && paid && secondOk && declined && e.Phase == DuelPhase.Main1 &&
                    p.LifePoints == lp - firstAmount && p.TryFindSpellTrap(kept, out _) && p.Graveyard.Contains(lost),
                    $"ownTurn={ownTurn} first={first?.Card?.Name} paid={paid} second={second?.Card?.Name} " +
                    $"declined={declined} phase={e.Phase} lp {lp}->{p.LifePoints}");
            }

            // Queued with 2400 LP, but your Burning Land deals 500 in the same Standby Phase first: with 1900 LP the
            // 2000 LP upkeep cannot be paid, so Mirror Wall is destroyed without a prompt.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var wall = FaceUpTrap(e, p, MirrorWall, 0);
                FaceUpTrap(e, p, BurningLand, 1);
                e.NotifyPublic();
                var oppTurn = AdvanceToOwnTurn(e, o, e.TurnNumber);
                PassAll(e);
                p.LifePoints = 2400;
                var ownTurn = AdvanceToOwnTurn(e, p, e.TurnNumber);
                Check("Upkeep choice: a Standby burn first (Burning Land 500: 2400 → 1900) leaves too few LP — " +
                      "Mirror Wall is destroyed without a prompt",
                    oppTurn && ownTurn && !e.IsAwaitingEffectTarget && e.Phase == DuelPhase.Main1 &&
                    p.LifePoints == 1900 && p.Graveyard.Contains(wall) && !p.TryFindSpellTrap(wall, out _),
                    $"oppTurn={oppTurn} ownTurn={ownTurn} pending={e.IsAwaitingEffectTarget} phase={e.Phase} " +
                    $"lp={p.LifePoints} gy={p.Graveyard.Contains(wall)}");
            }

            // A human in HvH is asked too (Player 2's own Standby Phase).
            {
                var e = FreshHvh(db, pDeck, aDeck);
                ClearBoard(e);
                e.ClearPendingResponse();
                var o = e.Opponent;
                o.Hand.Clear();
                var wall = FaceUpTrap(e, o, MirrorWall, 0);
                var lp = o.LifePoints;
                var ownTurn = AdvanceToOwnTurn(e, o, e.TurnNumber);
                var asked = e.PendingActivation?.AwaitingUpkeepChoice == true && e.PendingActivation.Controller == o;
                var paid = asked && e.TrySelectLpCost(2000);
                Check("Upkeep choice (HvH): Player 2 is asked in their own Standby Phase; PAY 2000 keeps Mirror Wall",
                    ownTurn && asked && paid && o.LifePoints == lp - 2000 && o.TryFindSpellTrap(wall, out _) &&
                    e.Phase == DuelPhase.Main1,
                    $"ownTurn={ownTurn} asked={asked} paid={paid} lp {lp}->{o.LifePoints} phase={e.Phase}");
            }

            // AI: it keeps Mirror Wall only while more than 2000 LP would be left (8000 → pays; 3000 → lets it go).
            foreach (var aiLp in new[] { 8000, 3000 })
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var wall = FaceUpTrap(e, o, MirrorWall, 0);
                o.LifePoints = aiLp;
                var ownTurn = AdvanceToOwnTurn(e, o, e.TurnNumber);
                var keeps = aiLp > 4000;
                Check(keeps
                        ? "Upkeep (AI): at 8000 LP it pays 2000 and keeps Mirror Wall"
                        : "Upkeep (AI): at 3000 LP it does not pay — Mirror Wall is destroyed, LP stays 3000",
                    ownTurn && !e.IsAwaitingEffectTarget &&
                    (keeps
                        ? o.LifePoints == aiLp - 2000 && o.TryFindSpellTrap(wall, out _)
                        : o.LifePoints == aiLp && o.Graveyard.Contains(wall)),
                    $"ownTurn={ownTurn} pending={e.IsAwaitingEffectTarget} lp {aiLp}->{o.LifePoints} " +
                    $"field={o.TryFindSpellTrap(wall, out _)} gy={o.Graveyard.Contains(wall)}");
            }

            // Mandatory upkeep is never asked: Imperial Order ("you must pay 700 LP (this is not optional)") is paid.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                var io = FaceUpTrap(e, p, ImperialOrder, 0);
                var lp = p.LifePoints;
                var t0 = e.TurnNumber;
                var ownTurn = AdvanceToOwnTurn(e, p, t0);
                var asked = e.PendingActivation?.AwaitingUpkeepChoice == true;
                Check("Upkeep: mandatory Imperial Order is paid without a prompt",
                    ownTurn && !asked && p.TryFindSpellTrap(io, out _) && p.LifePoints < lp,
                    $"ownTurn={ownTurn} asked={asked} lp {lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── review fixes ─────────────────────────────

        /// <summary>
        /// Cases the v142 review found: a Tributed Token under Skull Invitation, Burning Land's "When this card is
        /// activated:" effect after a chained MST, cards sent by an earlier step of the resolution that then removes
        /// Skull Invitation (Final Destiny's discards, Daedalus' Umi cost), and the AI's use of a Set Skull
        /// Invitation.
        /// </summary>
        static void ReviewFixes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A Token Tributed for a Tribute Summon is never sent to the GY: no Skull Invitation damage.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                FaceUpTrap(e, p, SkullInvitation, 0);
                e.NotifyPublic();
                var tok = e.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                p.MonsterZones[0].Occupant = tok;
                e.NotifyPublic();
                var lp = p.LifePoints;
                var pk = PutInHand(e, p, Pumpking);
                var summoned = e.TryNormalSummon(p, pk, asSet: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation != null) e.TrySelectEffectTarget(tok);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation: a Token Tributed for a Tribute Summon deals no damage (a Token never reaches the GY)",
                    p.TryFindMonster(pk, out _) && !p.TryFindMonster(tok, out _) && p.LifePoints == lp,
                    $"summoned={summoned} onField={p.TryFindMonster(pk, out _)} tokenInGyList={p.Graveyard.Contains(tok)} " +
                    $"lp {lp}->{p.LifePoints}");
            }

            // Burning Land destroyed by an MST chained to its activation (from the hand, or from a Set): "When this
            // card is activated:" still destroys the Field Spells (ygopro s.activate has no c:IsRelateToEffect), and
            // Burning Land is only in the GY — it is not put back on the field.
            foreach (var fromHand in new[] { true, false })
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var reached = AdvanceToMain(e, e.Opponent, 1);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                var sogen = e.CreateCardInstance(Sogen);
                sogen.FaceUp = true;
                p.FieldSpellZone.Occupant = sogen;
                var myMst = PlaceSetTrap(e, p, Mst, 0);
                myMst.SetThisTurn = false;
                CardInstance land;
                if (fromHand)
                    land = PutInHand(e, o, BurningLand);
                else
                {
                    land = PlaceSetTrap(e, o, BurningLand, 1);
                    land.SetThisTurn = false;
                }

                e.NotifyPublic();
                var cl1 = e.TryActivateSpellTrap(o, land, fromHand: fromHand);
                var cl2 = e.IsAwaitingResponse && e.PendingResponse.Responder == p &&
                          e.TryActivateChainResponse(p, myMst);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(land);
                PassAll(e);
                e.NotifyPublic();
                Check($"Burning Land ({(fromHand ? "from the hand" : "from a Set")}) destroyed by a chained MST: its " +
                      "activation still destroys the Field Spells, and it stays in the GY (not re-seated)",
                    reached && cl1 && cl2 && p.FieldSpellZone.Occupant == null && p.Graveyard.Contains(sogen) &&
                    o.Graveyard.Contains(land) && !o.TryFindSpellTrap(land, out _),
                    $"reached={reached} cl1={cl1} cl2={cl2} sogenField={p.FieldSpellZone.Occupant == sogen} " +
                    $"sogenGy={p.Graveyard.Contains(sogen)} landGy={o.Graveyard.Contains(land)} " +
                    $"landField={o.TryFindSpellTrap(land, out _)}");
            }

            // Final Destiny: its 5 discards reach the GY while Skull Invitation is still on the field (it is destroyed
            // only by the next step), so they cost their owner 300 each. With and without a chain window.
            foreach (var window in new[] { false, true })
            {
                var e = FreshHvh(db, pDeck, aDeck);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var sk = FaceUpTrap(e, p, SkullInvitation, 0);
                PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                if (window)
                {
                    var mst = PlaceSetTrap(e, o, Mst, 2);
                    mst.SetThisTurn = false;
                }

                for (var i = 0; i < 5; i++) PutInHand(e, p, Celtic);
                var fd = PutInHand(e, p, FinalDestiny);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var ok = e.TryActivateSpellTrap(p, fd, fromHand: true);
                var windowOpened = e.IsAwaitingResponse;
                PassAll(e);
                e.NotifyPublic();
                Check($"Skull Invitation: Final Destiny's 5 discards cost 1500 before it destroys Skull Invitation " +
                      $"({(window ? "chain window" : "no response")})",
                    ok && windowOpened == window && p.Graveyard.Contains(sk) && p.Hand.Count == 0 &&
                    p.LifePoints == lp - 1500 && o.LifePoints == oppLp,
                    $"ok={ok} window={windowOpened} skullGy={p.Graveyard.Contains(sk)} hand={p.Hand.Count} " +
                    $"lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }

            // Levia-Dragon - Daedalus: the "Umi" sent as its cost reaches the GY while the opponent's Skull Invitation
            // is still face-up → 300 to Umi's owner; the cards destroyed together with Skull Invitation cost nothing.
            {
                var e = FreshHvh(db, pDeck, aDeck);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var sk = FaceUpTrap(e, o, SkullInvitation, 0);
                var dd = PlaceMonster(e, p, LeviaDragonDaedalus, 0, BattlePosition.Attack, true);
                var umi = e.CreateCardInstance(Umi);
                umi.FaceUp = true;
                p.FieldSpellZone.Occupant = umi;
                PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var ok = e.TryActivateSpellTrap(p, dd, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(umi);
                PassAll(e);
                e.NotifyPublic();
                Check("Skull Invitation: Daedalus' Umi cost reaches the GY before Skull Invitation is destroyed → 300",
                    ok && p.Graveyard.Contains(umi) && o.Graveyard.Contains(sk) && p.LifePoints == lp - 300 &&
                    o.LifePoints == oppLp,
                    $"ok={ok} umiGy={p.Graveyard.Contains(umi)} skullGy={o.Graveyard.Contains(sk)} " +
                    $"lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }

            // AI: a Set Skull Invitation is chained to the opponent's one-shot Spell (Pot of Greed goes to its
            // owner's GY once the chain resolves → 300), but not to a link that would destroy it (Heavy Storm).
            foreach (var storm in new[] { false, true })
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var sk = PlaceSetTrap(e, o, SkullInvitation, 0);
                sk.SetThisTurn = false;
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oppLp = o.LifePoints;
                var spell = PutInHand(e, p, storm ? HeavyStorm : PotOfGreed);
                var ok = e.TryActivateSpellTrap(p, spell, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                if (!storm)
                    Check("Skull Invitation (AI): chained to the opponent's Pot of Greed → Skull face-up, Pot of Greed " +
                          "reaches the GY after the chain → 300 to its owner",
                        ok && sk.FaceUp && o.TryFindSpellTrap(sk, out _) && p.Graveyard.Contains(spell) &&
                        p.LifePoints == lp - 300 && o.LifePoints == oppLp,
                        $"ok={ok} up={sk.FaceUp} field={o.TryFindSpellTrap(sk, out _)} lp {lp}->{p.LifePoints} " +
                        $"opp {oppLp}->{o.LifePoints}");
                else
                    Check("Skull Invitation (AI): not chained to a Heavy Storm that would destroy it (no damage)",
                        ok && o.Graveyard.Contains(sk) && p.LifePoints == lp && o.LifePoints == oppLp,
                        $"ok={ok} gy={o.Graveyard.Contains(sk)} lp {lp}->{p.LifePoints} opp {oppLp}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Compile(int donorId, string text)
            {
                var donor = db.Get(donorId);
                var def = new CardDef
                {
                    id = 0,
                    name = "StandbyGrowth near miss",
                    type = donor.type,
                    frameType = donor.frameType,
                    race = donor.race,
                    attribute = donor.attribute,
                    level = donor.level,
                    atk = donor.atk,
                    def = donor.def,
                    desc = text
                };
                return CardTextEffectCompiler.Compile(def);
            }

            bool Has(CompiledCardProgram prog, EffectActionKind a) =>
                prog != null && prog.ClauseList.Exists(c => c != null && c.Action == a);

            // "your opponent's next 4 Standby Phases" / "each Zombie-Type monster you control" are not Castle.
            var castleOpp = Compile(Castle,
                "FLIP: All Zombie-Type monsters gain 200 ATK and DEF. During each of your opponent's next 4 Standby " +
                "Phases, each of those Zombie-Type monsters gains 200 more ATK and DEF. These effects last as long as " +
                "this card is face-up on the field.");
            var castleNoDuration = Compile(Castle,
                "FLIP: All Zombie-Type monsters gain 200 ATK and DEF. During each of your next 4 Standby Phases, each " +
                "of those Zombie-Type monsters gains 200 more ATK and DEF.");
            Check("Castle near miss: the opponent's Standby Phases, or no \"while face-up\" duration → not the Castle atom",
                !Has(castleOpp, EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp) &&
                !(castleOpp?.FullyCompiled ?? false) &&
                !Has(castleNoDuration, EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp) &&
                !(castleNoDuration?.FullyCompiled ?? false),
                $"opp={Describe(castleOpp)} || noDur={Describe(castleNoDuration)}");

            // Pumpking without its reset sentence (the gain would be permanent) fails closed.
            var pkNoReset = Compile(Pumpking,
                "Gains 100 ATK/DEF while \"Castle of Dark Illusions\" is on the field. Also, during your Standby Phase, " +
                "if \"Castle of Dark Illusions\" is on the field: This card gains 100 ATK/DEF. This effect activates " +
                "once per turn, for four of your Standby Phases (counting this as the first). \"Castle of Dark " +
                "Illusions\" must be on the field to activate and to resolve this effect.");
            Check("Pumpking near miss: without \"this ATK/DEF gain disappears and this effect resets\" → not compiled",
                !Has(pkNoReset, EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby) &&
                !(pkNoReset?.FullyCompiled ?? false),
                Describe(pkNoReset));

            // "your monsters that conducted an attack" / "until the End Phase" are not Mirror Wall.
            var mwOwn = Compile(MirrorWall,
                "Each of your monsters that conducted an attack while this card was face-up on the field has its ATK " +
                "halved as long as this card remains on the field. During each of your Standby Phases, pay 2000 LP or " +
                "destroy this card.");
            Check("Mirror Wall near miss: \"your monsters\" → not the Mirror Wall atom, not FullyCompiled",
                !Has(mwOwn, EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField) &&
                !(mwOwn?.FullyCompiled ?? false),
                Describe(mwOwn));

            // "sent to your opponent's Graveyard" / "inflict 300 damage to your opponent" are not Skull Invitation.
            var siOpp = Compile(SkullInvitation,
                "Each time a card(s) is sent to the Graveyard, inflict 300 damage to your opponent for each card sent.");
            var siFrom = Compile(SkullInvitation,
                "Each time a card(s) is sent to the Graveyard from the field, inflict 300 damage to its owner for each " +
                "card sent.");
            Check("Skull Invitation near miss: \"to your opponent\" / \"from the field\" → not the Skull atom",
                !Has(siOpp, EffectActionKind.InflictDamageToOwnerPerCardSentToGy) &&
                !Has(siFrom, EffectActionKind.InflictDamageToOwnerPerCardSentToGy) &&
                !(siFrom?.FullyCompiled ?? false),
                $"opp={Describe(siOpp)} || from={Describe(siFrom)}");
        }
    }
}
