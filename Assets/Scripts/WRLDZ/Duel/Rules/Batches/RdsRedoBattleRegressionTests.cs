using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// RdsRedoBattle batch: Silent Swordsman LV3 / LV5, Mystic Swordsman LV6, Sasuke Samurai #4, Covering Fire, Cross
    /// Counter and Rare Metalmorph (the RDS / FET battle cards v208 failed closed), with the deferred Tragedy pinned.
    /// Compile shapes, mini-duels through the real entry points (TryActivateSpellTrap, TryAttack, response windows, the
    /// Standby Phase) and near misses.
    /// </summary>
    public static class RdsRedoBattleRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int SilentLv3 = 1995985;
        const int SilentLv5 = 74388798;
        const int SilentLv7 = 37267041;
        const int MysticLv6 = 60482781;
        const int Sasuke4 = 64538655;
        const int CoveringFire = 74458486;
        const int CrossCounter = 37083210;
        const int RareMetalmorph = 12503902;
        const int Tragedy = 35686188;

        const int BookOfMoon = 14087893;      // Quick-Play: target 1 face-up monster; change it to face-down Defense
        const int RushRecklessly = 70046172;  // Quick-Play: target 1 face-up monster; +700 ATK until the End Phase
        const int DarkHole = 53129443;        // Normal Spell: destroy all monsters on the field
        const int GiantSoldier = 13039848;    // Normal Rock 1300/2000
        const int Celtic = 91152256;          // Normal Warrior 1400/1200
        const int GeminiElf = 69140098;       // Normal Spellcaster 1900/900
        const int MysticalElf = 15025844;     // Normal Spellcaster 800/2000
        const int Mechanicalchaser = 7359741; // Normal Machine 1850/800
        const int BlueEyes = 89631139;        // Normal Dragon 3000/2500

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("RdsRedoBattle: " + name, ok, detail ?? "");

            SilentSwordsmanLv3(db, pDeck, aDeck, Check);
            SilentSwordsmanLv5(db, pDeck, aDeck, Check);
            MysticSwordsmanLv6(db, pDeck, aDeck, Check);
            SasukeSamurai4(db, pDeck, aDeck, Check);
            CoveringFireDuels(db, pDeck, aDeck, Check);
            CrossCounterDuels(db, pDeck, aDeck, Check);
            RareMetalmorphDuels(db, pDeck, aDeck, Check);
            Deferred(db, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CardEffectStatusKind Status(CardDatabase db, int id) => CardEffectStatus.Classify(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [" + string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} named={c.NamedCard} cond={c.ActivationCondition}")) +
                  "] unparsed=[" + string.Join("|", p.UnparsedFragments ?? Array.Empty<string>()) + "]";

        static void Wipe(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Wipe(e);
            return e;
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            reached = ReachOpponentBattle(e);
            Wipe(e);
            return e;
        }

        static bool EnterBattle(DuelEngine e, DuelistState who)
        {
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(who);
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            return e.Phase == DuelPhase.Battle;
        }

        /// <summary>Pick the first legal card at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e)
        {
            for (var i = 0; i < 30; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    if (e.PendingActivation.AwaitingCoinCall)
                    {
                        e.TrySelectCoinCall(true);
                        continue;
                    }

                    var pick = e.PendingActivation?.LegalTargets?.FirstOrDefault();
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                if (e.HasDeclaredAttack)
                {
                    e.ResolveDeclaredAttack();
                    continue;
                }

                break;
            }
        }

        /// <summary>Pass windows until <paramref name="who"/>'s Damage Calculation window is open.</summary>
        static bool ReachDamageCalculation(DuelEngine e, DuelistState who)
        {
            for (var k = 0; k < 6; k++)
            {
                var pr = e.PendingResponse;
                if (pr != null && pr.Timing == ResponseTiming.DamageCalculation && pr.Responder == who) return true;
                if (!e.IsAwaitingResponse) return false;
                e.PassResponse();
            }

            return false;
        }

        static bool InWindow(DuelEngine e, CardInstance card) =>
            e.PendingResponse?.LegalCards != null && e.PendingResponse.LegalCards.Contains(card);

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone,
            BattlePosition pos = BattlePosition.Attack) => PlaceMonster(e, who, id, zone, pos, true);

        static CardInstance Down(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        // ───────────────────────────── Silent Swordsman LV3 ─────────────────────────────

        static void SilentSwordsmanLv3(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, SilentLv3);
            var marker = prog?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.NegateOpponentSpellEffectsThatTargetThis);
            Check("Silent Swordsman LV3: the negation marker + the Standby LV5 clause (Implemented)",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && marker != null &&
                marker.Timing == EffectTiming.ContinuousWhileFaceUp && !marker.MakesChainLink &&
                prog.ClauseList.Exists(c => c.Timing == EffectTiming.StandbyPhase && c.NamedCard == "Silent Swordsman LV5" &&
                                            c.ActivationCondition == LvChainRuntime.NotTurnSummoned) &&
                Status(db, SilentLv3) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // The opponent's Book of Moon targeting it resolves with no effect; Book of Moon is not destroyed by it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv3 = Up(e, o, SilentLv3, 0);
                var bom = PutInHand(e, p, BookOfMoon);
                e.NotifyPublic();
                var activated = reached && e.TryActivateSpellTrap(p, bom, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(lv3)) e.TrySelectEffectTarget(lv3);
                Drive(e);
                Check("Silent Swordsman LV3: the opponent's Book of Moon that targets it is negated (it stays face-up Attack)",
                    activated && o.TryFindMonster(lv3, out _) && lv3.FaceUp && lv3.Position == BattlePosition.Attack &&
                    p.Graveyard.Contains(bom),
                    $"reached={reached} activated={activated} faceUp={lv3.FaceUp} pos={lv3.Position} bomGy={p.Graveyard.Contains(bom)}");
            }

            {
                // Its controller's own Spell is not negated (Lua rp==tp returns): Rush Recklessly gives it 700 ATK.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var lv3 = Up(e, p, SilentLv3, 0);
                var rush = PutInHand(e, p, RushRecklessly);
                e.NotifyPublic();
                var activated = reached && e.TryActivateSpellTrap(p, rush, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(lv3)) e.TrySelectEffectTarget(lv3);
                Drive(e);
                Check("Silent Swordsman LV3: its controller's own targeting Spell still applies (Rush Recklessly +700)",
                    activated && lv3.CurrentAtk == 1700,
                    $"reached={reached} activated={activated} atk={lv3.CurrentAtk}");
            }

            {
                // Negated, it protects nothing: the opponent's Book of Moon flips it face-down.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv3 = Up(e, o, SilentLv3, 0);
                lv3.IsNegated = true;
                var bom = PutInHand(e, p, BookOfMoon);
                var activated = reached && e.TryActivateSpellTrap(p, bom, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(lv3)) e.TrySelectEffectTarget(lv3);
                Drive(e);
                Check("Silent Swordsman LV3: while its effects are negated, Book of Moon turns it face-down",
                    activated && !lv3.FaceUp,
                    $"reached={reached} activated={activated} faceUp={lv3.FaceUp}");
            }

            var near = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990210401, name = "RdsRedoBattle LV3 probe", type = "Effect Monster", frameType = "effect",
                race = "Warrior", attribute = "LIGHT", level = 3, atk = 1000, def = 1000,
                desc = "Negate your opponent's Trap effects that target this card."
            });
            Check("Near miss: \"Negate your opponent's Trap effects that target this card.\" does not use the Spell marker",
                near == null || !near.ClauseList.Exists(c => c.Action == EffectActionKind.NegateOpponentSpellEffectsThatTargetThis),
                Describe(near));
        }

        // ───────────────────────────── Silent Swordsman LV5 ─────────────────────────────

        static void SilentSwordsmanLv5(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, SilentLv5);
            var imm = prog?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.UnaffectedByCardEffects);
            var lvUp = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.StandbyPhase);
            Check("Silent Swordsman LV5: opponent-only Spell immunity + the LV7 Standby clause after a direct attack",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && imm != null &&
                imm.UnaffectedByFilter == "Spell" && imm.UnaffectedOnlyByOpponentEffects && lvUp != null &&
                lvUp.Action == EffectActionKind.SpecialSummonNamed && lvUp.NamedCard == "Silent Swordsman LV7" &&
                lvUp.RequiresSendThisToGy && lvUp.FromHand && lvUp.FromDeck && lvUp.IsOptional && lvUp.OnlyDuringOwnStandby &&
                lvUp.ActivationCondition == RdsRedoBattleRuntime.NextStandbyAfterDirectBattleDamage &&
                Status(db, SilentLv5) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // The opponent's Book of Moon does not affect it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv5 = Up(e, o, SilentLv5, 0);
                var celtic = Up(e, o, Celtic, 1);
                var bom = PutInHand(e, p, BookOfMoon);
                var activated = reached && e.TryActivateSpellTrap(p, bom, fromHand: true);
                var offered = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(lv5);
                if (offered) e.TrySelectEffectTarget(lv5);
                Drive(e);
                Check("Silent Swordsman LV5: unaffected by the opponent's Book of Moon (stays face-up Attack)",
                    activated && lv5.FaceUp && lv5.Position == BattlePosition.Attack && o.TryFindMonster(lv5, out _),
                    $"reached={reached} activated={activated} offered={offered} faceUp={lv5.FaceUp} celticUp={celtic.FaceUp}");
            }

            foreach (var ownersDarkHole in new[] { false, true })
            {
                // A non-targeting Spell too: the opponent's Dark Hole leaves it on the field; its controller's own does not.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var holder = ownersDarkHole ? p : o;
                var lv5 = Up(e, holder, SilentLv5, 0);
                var celtic = Up(e, o, Celtic, 1);
                var hole = PutInHand(e, p, DarkHole);
                var activated = reached && e.TryActivateSpellTrap(p, hole, fromHand: true);
                Drive(e);
                var survived = holder.TryFindMonster(lv5, out _);
                Check(ownersDarkHole
                        ? "Silent Swordsman LV5: its controller's own Dark Hole destroys it"
                        : "Silent Swordsman LV5: the opponent's Dark Hole does not destroy it (Celtic Guardian is destroyed)",
                    activated && o.Graveyard.Contains(celtic) && survived == !ownersDarkHole,
                    $"reached={reached} activated={activated} survived={survived} celticGy={o.Graveyard.Contains(celtic)}");
            }

            {
                // Its controller's own Spell applies (the filter is the opponent's Spells only).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var lv5 = Up(e, p, SilentLv5, 0);
                var rush = PutInHand(e, p, RushRecklessly);
                var activated = reached && e.TryActivateSpellTrap(p, rush, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(lv5)) e.TrySelectEffectTarget(lv5);
                Drive(e);
                Check("Silent Swordsman LV5: its controller's own Rush Recklessly applies (3000 ATK)",
                    activated && lv5.CurrentAtk == 3000,
                    $"reached={reached} activated={activated} atk={lv5.CurrentAtk}");
            }

            {
                // A direct attack's battle damage → its controller's next Standby Phase sends it to the GY and Special
                // Summons LV7 from the hand (LV7 "must be Special Summoned by Silent Swordsman LV5").
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv5 = Up(e, p, SilentLv5, 0);
                var lv7 = PutInHand(e, p, SilentLv7);
                var olp = o.LifePoints;
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, lv5, null);
                Drive(e);
                var damaged = o.LifePoints == olp - 2300;
                var ctrl = Prog(db, SilentLv5)?.ClausesFor(EffectTiming.StandbyPhase).FirstOrDefault();
                RdsRedoBattleRuntime.TryActivationCondition(e, p, lv5, ctrl, out var sameTurn, out _);
                e.TryEndTurnSafe(p);
                Drive(e);
                RdsRedoBattleRuntime.TryActivationCondition(e, p, lv5, ctrl, out var oppTurn, out _);
                e.TryEndTurnSafe(e.Opponent);
                Drive(e);
                var lv7Up = p.MonstersOnField().Any(m => m?.CardId == SilentLv7);
                Check("Silent Swordsman LV5: after a direct attack, its next Standby Phase sends it to the GY and Special " +
                      "Summons LV7 from the hand",
                    reached && attacked && damaged && !sameTurn && !oppTurn && p.Graveyard.Contains(lv5) && lv7Up &&
                    !p.Hand.Contains(lv7),
                    $"reached={reached} attacked={attacked} olp={olp}->{o.LifePoints} sameTurn={sameTurn} oppTurn={oppTurn} " +
                    $"lv5Gy={p.Graveyard.Contains(lv5)} lv7Up={lv7Up} turn={e.TurnNumber} phase={e.Phase}");
            }

            {
                // Near miss: battle damage by attacking a monster does not open the level-up.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv5 = Up(e, p, SilentLv5, 0);
                var celtic = Up(e, o, Celtic, 0);
                PutInHand(e, p, SilentLv7);
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, lv5, celtic);
                Drive(e);
                e.TryEndTurnSafe(p);
                Drive(e);
                e.TryEndTurnSafe(e.Opponent);
                Drive(e);
                Check("Silent Swordsman LV5: battle damage from attacking a monster does not level it up",
                    reached && attacked && o.Graveyard.Contains(celtic) && p.TryFindMonster(lv5, out _) &&
                    !p.MonstersOnField().Any(m => m?.CardId == SilentLv7),
                    $"reached={reached} attacked={attacked} celticGy={o.Graveyard.Contains(celtic)} " +
                    $"lv5Field={p.TryFindMonster(lv5, out _)}");
            }
        }

        // ───────────────────────────── Mystic Swordsman LV6 ─────────────────────────────

        static void MysticSwordsmanLv6(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, MysticLv6);
            Check("Mystic Swordsman LV6: one StartOfDamageStep face-down destroy with the top-of-Deck choice (Implemented)",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                prog.ClauseList[0].Timing == EffectTiming.StartOfDamageStep &&
                prog.ClauseList[0].Action == EffectActionKind.DestroyAttackedFaceDownDefenseReturnToOwnerDeckTopOrGy &&
                prog.ClauseList[0].RequiresTargetFaceDownDefense &&
                Status(db, MysticLv6) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // "Cannot be Normal Summoned (but can be Normal Set)": the Summon is refused, the Set is not refused for it.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var lv6 = PutInHand(e, p, MysticLv6);
                var summon = SummonProcedures.CheckNormalOrTribute(e, p, lv6, asSet: false);
                var set = SummonProcedures.CheckNormalOrTribute(e, p, lv6, asSet: true);
                Check("Mystic Swordsman LV6: cannot be Normal Summoned, but can be Normal Set",
                    !summon.Legal && (summon.Reason ?? "").Contains("cannot be Normal Summoned") &&
                    !(set.Reason ?? "").Contains("cannot be Normal Summoned"),
                    $"summon={summon.Legal}/{summon.Reason} set={set.Legal}/{set.Reason}");
            }

            {
                // The player taps the destroyed monster: it goes on top of the opponent's Deck, never flipped, no damage.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv6 = Up(e, p, MysticLv6, 0);
                var soldier = Down(e, o, GiantSoldier, 0);
                var deck = o.Deck.Count;
                var plp = p.LifePoints;
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, lv6, soldier);
                for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(soldier);
                var tapped = asked && e.TrySelectEffectTarget(soldier);
                Drive(e);
                Check("Mystic Swordsman LV6: the attacked face-down monster is destroyed and placed on top of the " +
                      "opponent's Deck (the player chose it)",
                    reached && attacked && asked && tapped && !o.TryFindMonster(soldier, out _) &&
                    !o.Graveyard.Contains(soldier) && o.Deck.Count == deck + 1 && o.Deck[0] == GiantSoldier &&
                    p.LifePoints == plp,
                    $"reached={reached} attacked={attacked} asked={asked} tapped={tapped} deck={deck}->{o.Deck.Count} " +
                    $"top={(o.Deck.Count > 0 ? o.Deck[0] : 0)} gy={o.Graveyard.Contains(soldier)} plp={plp}->{p.LifePoints}");
            }

            {
                // The AI's LV6 places the player's Set monster on top of the player's Deck.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv6 = Up(e, o, MysticLv6, 0);
                var elf = Down(e, p, MysticalElf, 0);
                var deck = p.Deck.Count;
                var attacked = reached && e.TryAttack(o, lv6, elf);
                Drive(e);
                Check("Mystic Swordsman LV6 (AI): the player's face-down monster goes on top of the player's Deck",
                    attacked && !p.TryFindMonster(elf, out _) && !p.Graveyard.Contains(elf) && p.Deck.Count == deck + 1 &&
                    p.Deck[0] == MysticalElf,
                    $"reached={reached} attacked={attacked} deck={deck}->{p.Deck.Count} gy={p.Graveyard.Contains(elf)}");
            }

            {
                // Near miss: a face-up Defense Position monster is battled normally (no start-of-Damage-Step destroy).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv6 = Up(e, p, MysticLv6, 0);
                var elf = Up(e, o, MysticalElf, 0, BattlePosition.Defense);
                var deck = o.Deck.Count;
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, lv6, elf);
                var asked = e.IsAwaitingEffectTarget;
                Drive(e);
                Check("Mystic Swordsman LV6: a face-up Defense Position monster is destroyed by battle into the GY (no choice)",
                    reached && attacked && !asked && o.Graveyard.Contains(elf) && o.Deck.Count == deck,
                    $"reached={reached} attacked={attacked} asked={asked} gy={o.Graveyard.Contains(elf)} deck={deck}->{o.Deck.Count}");
            }
        }

        // ───────────────────────────── Sasuke Samurai #4 ─────────────────────────────

        static void SasukeSamurai4(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, Sasuke4);
            Check("Sasuke Samurai #4: one StartOfDamageStep coin-call destroy (Implemented)",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                prog.ClauseList[0].Timing == EffectTiming.StartOfDamageStep &&
                prog.ClauseList[0].Action == EffectActionKind.CoinCallDestroyBattledMonsterOnCorrect &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.CoinCallDestroyBattledMonsterOnCorrect) &&
                Status(db, Sasuke4) == CardEffectStatusKind.Implemented,
                Describe(prog));

            foreach (var right in new[] { true, false })
            {
                // Attacking: the player calls Heads; a right call destroys Gemini Elf before damage calculation.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sasuke = Up(e, p, Sasuke4, 0);
                var elf = Up(e, o, GeminiElf, 0);
                var plp = p.LifePoints;
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, sasuke, elf);
                for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall;
                e.Rng.QueueCoin(right);
                var called = asked && e.TrySelectCoinCall(true);
                Drive(e);
                var ok = right
                    ? o.Graveyard.Contains(elf) && p.TryFindMonster(sasuke, out _) && p.LifePoints == plp
                    : o.TryFindMonster(elf, out _) && p.Graveyard.Contains(sasuke) && p.LifePoints == plp - 700;
                Check(right
                        ? "Sasuke Samurai #4: attacking, a right call destroys the battled monster (no damage calculation)"
                        : "Sasuke Samurai #4: a wrong call — the battle goes on (it is destroyed by battle, 700 damage)",
                    reached && attacked && asked && called && ok,
                    $"reached={reached} attacked={attacked} asked={asked} called={called} elfGy={o.Graveyard.Contains(elf)} " +
                    $"sasukeGy={p.Graveyard.Contains(sasuke)} plp={plp}->{p.LifePoints}");
            }

            {
                // Attacked (face-up) on the opponent's turn: the player still calls; a right call destroys the attacker.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sasuke = Up(e, p, Sasuke4, 0);
                var elf = Up(e, o, GeminiElf, 0);
                var plp = p.LifePoints;
                var attacked = reached && e.TryAttack(o, elf, sasuke);
                for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall;
                e.Rng.QueueCoin(true);
                var called = asked && e.TrySelectCoinCall(true);
                Drive(e);
                Check("Sasuke Samurai #4: attacked, a right call destroys the attacking monster",
                    attacked && asked && called && o.Graveyard.Contains(elf) && p.TryFindMonster(sasuke, out _) &&
                    p.LifePoints == plp,
                    $"reached={reached} attacked={attacked} asked={asked} called={called} elfGy={o.Graveyard.Contains(elf)} " +
                    $"plp={plp}->{p.LifePoints}");
            }

            {
                // The AI's Sasuke Samurai #4 calls by itself (Heads): the player's face-down monster is destroyed unflipped.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sasuke = Up(e, o, Sasuke4, 0);
                var soldier = Down(e, p, GiantSoldier, 0);
                e.Rng.QueueCoin(true);
                var attacked = reached && e.TryAttack(o, sasuke, soldier);
                Drive(e);
                Check("Sasuke Samurai #4 (AI): its own call — a right call destroys the player's face-down monster",
                    attacked && p.Graveyard.Contains(soldier) && o.TryFindMonster(sasuke, out _),
                    $"reached={reached} attacked={attacked} gy={p.Graveyard.Contains(soldier)}");
            }

            {
                // Near miss: a face-down Sasuke Samurai #4 that is attacked does not toss (it is flipped by the battle).
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sasuke = Down(e, p, Sasuke4, 0);
                var elf = Up(e, o, GeminiElf, 0);
                var attacked = reached && e.TryAttack(o, elf, sasuke);
                for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall;
                Drive(e);
                Check("Sasuke Samurai #4: attacked while face-down, no coin call (destroyed by battle)",
                    attacked && !asked && p.Graveyard.Contains(sasuke) && o.TryFindMonster(elf, out _),
                    $"reached={reached} attacked={attacked} asked={asked} gy={p.Graveyard.Contains(sasuke)}");
            }
        }

        // ───────────────────────────── Covering Fire ─────────────────────────────

        static void CoveringFireDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, CoveringFire);
            var c0 = prog?.ClauseList.FirstOrDefault();
            Check("Covering Fire: one Damage Calculation window clause that targets another face-up monster (Implemented)",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 && c0 != null &&
                c0.Timing == EffectTiming.DamageCalculation &&
                c0.Action == EffectActionKind.TargetGainsAtkEqualToAnotherTargetDuringDamageStep && c0.IsPsctTarget &&
                c0.RequiresTargetFaceUp && !EffectVocabulary.HasNoRuntime(c0.Action) &&
                Status(db, CoveringFire) == CardEffectStatusKind.Implemented,
                Describe(prog));

            foreach (var twoTargets in new[] { false, true })
            {
                // Blue-Eyes attacks Gemini Elf (1900): + Mechanicalchaser's 1850 = 3750 → Blue-Eyes is destroyed.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var elf = Up(e, p, GeminiElf, 0);
                var chaser = Up(e, p, Mechanicalchaser, 1);
                CardInstance celtic = twoTargets ? Up(e, p, Celtic, 2) : null;
                var cf = PlaceSetTrap(e, p, CoveringFire, 0);
                var bewd = Up(e, o, BlueEyes, 0);
                var olp = o.LifePoints;
                var attacked = reached && e.TryAttack(o, bewd, elf);
                var window = ReachDamageCalculation(e, p) && InWindow(e, cf);
                var fired = window && e.TryActivateSpellTrap(p, cf, fromHand: false);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(chaser) &&
                            (celtic == null || e.PendingActivation.LegalTargets.Contains(celtic)) &&
                            !e.PendingActivation.LegalTargets.Contains(elf);
                if (asked) e.TrySelectEffectTarget(chaser);
                Drive(e);
                Check(twoTargets
                        ? "Covering Fire: the player picks among 2 other monsters; the attacked monster wins the battle"
                        : "Covering Fire: the attacked Gemini Elf gains Mechanicalchaser's ATK and destroys Blue-Eyes",
                    attacked && window && fired && (!twoTargets || asked) && o.Graveyard.Contains(bewd) &&
                    p.TryFindMonster(elf, out _) && o.LifePoints == olp - 750 && elf.CurrentAtk == 1900 &&
                    p.Graveyard.Contains(cf),
                    $"reached={reached} attacked={attacked} window={window} fired={fired} asked={asked} " +
                    $"bewdGy={o.Graveyard.Contains(bewd)} olp={olp}->{o.LifePoints} elfAtk={elf.CurrentAtk}");
            }

            {
                // Near miss: no other face-up monster to target → not offered in the window.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var elf = Up(e, p, GeminiElf, 0);
                Down(e, p, Mechanicalchaser, 1);
                var cf = PlaceSetTrap(e, p, CoveringFire, 0);
                var bewd = Up(e, o, BlueEyes, 0);
                var attacked = reached && e.TryAttack(o, bewd, elf);
                var offered = ReachDamageCalculation(e, p) && InWindow(e, cf);
                Drive(e);
                Check("Covering Fire: not activatable without another face-up monster to target",
                    attacked && !offered && p.Graveyard.Contains(elf),
                    $"reached={reached} attacked={attacked} offered={offered}");
            }

            {
                // The AI uses it when the gain wins the battle.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var elf = Up(e, o, GeminiElf, 0);
                Up(e, o, Mechanicalchaser, 1);
                var cf = PlaceSetTrap(e, o, CoveringFire, 0);
                var bewd = Up(e, p, BlueEyes, 0);
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, bewd, elf);
                Drive(e);
                Check("Covering Fire (AI): activated in its Damage Calculation window; the player's Blue-Eyes is destroyed",
                    reached && attacked && o.Graveyard.Contains(cf) && p.Graveyard.Contains(bewd) &&
                    o.TryFindMonster(elf, out _),
                    $"reached={reached} attacked={attacked} cfGy={o.Graveyard.Contains(cf)} bewdGy={p.Graveyard.Contains(bewd)}");
            }
        }

        // ───────────────────────────── Cross Counter ─────────────────────────────

        static void CrossCounterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, CrossCounter);
            var c0 = prog?.ClauseList.FirstOrDefault();
            Check("Cross Counter: one Damage Calculation window clause (Implemented)",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 && c0 != null &&
                c0.Timing == EffectTiming.DamageCalculation &&
                c0.Action == EffectActionKind.DoubleBattleDamageToAttackerThenDestroyAttacker &&
                Status(db, CrossCounter) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // Gemini Elf (1900) attacks face-up Defense Mystical Elf (2000): 100 × 2 = 200 damage, Gemini Elf destroyed.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mystical = Up(e, p, MysticalElf, 0, BattlePosition.Defense);
                var cc = PlaceSetTrap(e, p, CrossCounter, 0);
                var gemini = Up(e, o, GeminiElf, 0);
                var olp = o.LifePoints;
                var attacked = reached && e.TryAttack(o, gemini, mystical);
                var window = ReachDamageCalculation(e, p) && InWindow(e, cc);
                var fired = window && e.TryActivateSpellTrap(p, cc, fromHand: false);
                Drive(e);
                Check("Cross Counter: the attacker's controller takes double the battle damage (200) and the attacker " +
                      "is destroyed at the end of the Damage Step",
                    attacked && window && fired && o.LifePoints == olp - 200 && o.Graveyard.Contains(gemini) &&
                    p.TryFindMonster(mystical, out _) && p.Graveyard.Contains(cc),
                    $"reached={reached} attacked={attacked} window={window} fired={fired} olp={olp}->{o.LifePoints} " +
                    $"geminiGy={o.Graveyard.Contains(gemini)}");
            }

            {
                // A face-down Defense monster flipped by the attack counts (it is face-up at damage calculation).
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var soldier = Down(e, p, GiantSoldier, 0);
                var cc = PlaceSetTrap(e, p, CrossCounter, 0);
                var celtic = Up(e, o, Celtic, 0);
                var olp = o.LifePoints;
                var attacked = reached && e.TryAttack(o, celtic, soldier);
                var window = ReachDamageCalculation(e, p) && InWindow(e, cc);
                var fired = window && e.TryActivateSpellTrap(p, cc, fromHand: false);
                Drive(e);
                Check("Cross Counter: a Set monster flipped by the attack (DEF 2000 vs 1400) — 1200 damage, attacker destroyed",
                    attacked && window && fired && o.LifePoints == olp - 1200 && o.Graveyard.Contains(celtic),
                    $"reached={reached} attacked={attacked} window={window} fired={fired} olp={olp}->{o.LifePoints}");
            }

            {
                // Near miss: the attacked monster is in Attack Position (or its DEF is not higher) → not offered.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mystical = Up(e, p, MysticalElf, 0);
                var cc = PlaceSetTrap(e, p, CrossCounter, 0);
                var gemini = Up(e, o, GeminiElf, 0);
                var attacked = reached && e.TryAttack(o, gemini, mystical);
                var offered = ReachDamageCalculation(e, p) && InWindow(e, cc);
                Drive(e);
                Check("Cross Counter: not activatable when the attacked monster is in Attack Position",
                    attacked && !offered && p.Graveyard.Contains(mystical),
                    $"reached={reached} attacked={attacked} offered={offered}");
            }

            {
                // The AI answers the player's attack with it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mystical = Up(e, o, MysticalElf, 0, BattlePosition.Defense);
                var cc = PlaceSetTrap(e, o, CrossCounter, 0);
                var gemini = Up(e, p, GeminiElf, 0);
                var plp = p.LifePoints;
                var battle = EnterBattle(e, p);
                var attacked = battle && e.TryAttack(p, gemini, mystical);
                Drive(e);
                Check("Cross Counter (AI): the player takes 200 and Gemini Elf is destroyed",
                    reached && attacked && o.Graveyard.Contains(cc) && p.LifePoints == plp - 200 &&
                    p.Graveyard.Contains(gemini),
                    $"reached={reached} attacked={attacked} ccGy={o.Graveyard.Contains(cc)} plp={plp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Rare Metalmorph ─────────────────────────────

        static void RareMetalmorphDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, RareMetalmorph);
            var act = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
            Check("Rare Metalmorph: link to a face-up Machine on the field (+500), a once-only Spell negation, leaves with it",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && act != null &&
                act.Action == EffectActionKind.LinkThisToTarget && act.Zone == EffectZoneFilter.FieldAnyMonster &&
                act.RaceFilter == "Machine" && act.RequiresTargetFaceUp && act.EquipAtkBonus == 500 &&
                act.DestroyThisWhenBoundHostLeaves &&
                prog.ClauseList.Exists(c => c.Action == EffectActionKind.NegateOnceSpellEffectTargetingBoundMonster) &&
                Status(db, RareMetalmorph) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var chaser = Up(e, p, Mechanicalchaser, 0);
                var celtic = Up(e, o, Celtic, 0);
                var rm = PlaceSetTrap(e, p, RareMetalmorph, 0);
                e.NotifyPublic();
                var activated = reached && e.TryActivateSpellTrap(p, rm, fromHand: false);
                var notCeltic = !e.IsAwaitingEffectTarget || !e.PendingActivation.LegalTargets.Contains(celtic);
                Drive(e);
                var linked = rm.FaceUp && rm.EquippedTo == chaser && chaser.CurrentAtk == 2350;
                Check("Rare Metalmorph: activated on the face-up Machine (not on the Warrior); it gains 500 ATK",
                    activated && notCeltic && linked,
                    $"reached={reached} activated={activated} notCeltic={notCeltic} up={rm.FaceUp} " +
                    $"linked={rm.EquippedTo?.Name} atk={chaser.CurrentAtk}");

                // Once: the first Book of Moon on that monster (even its controller's own) is negated, the second is not.
                var bom1 = PutInHand(e, p, BookOfMoon);
                var a1 = e.TryActivateSpellTrap(p, bom1, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(chaser)) e.TrySelectEffectTarget(chaser);
                Drive(e);
                var firstNegated = chaser.FaceUp && p.Graveyard.Contains(bom1) && chaser.CurrentAtk == 2350;
                var bom2 = PutInHand(e, p, BookOfMoon);
                var a2 = e.TryActivateSpellTrap(p, bom2, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(chaser)) e.TrySelectEffectTarget(chaser);
                Drive(e);
                var secondApplied = !chaser.FaceUp;
                Check("Rare Metalmorph: negates only the first Spell effect that targets its monster",
                    linked && a1 && firstNegated && a2 && secondApplied,
                    $"a1={a1} firstNegated={firstNegated} a2={a2} secondApplied={secondApplied} faceUp={chaser.FaceUp}");
            }

            {
                // When that monster leaves the field, this card is destroyed.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var chaser = Up(e, p, Mechanicalchaser, 0);
                var rm = PlaceSetTrap(e, p, RareMetalmorph, 0);
                var activated = reached && e.TryActivateSpellTrap(p, rm, fromHand: false);
                Drive(e);
                var linked = rm.EquippedTo == chaser;
                e.DestroyMonsterPublic(p, chaser);
                e.NotifyPublic();
                Check("Rare Metalmorph: when the linked monster leaves the field, it leaves too",
                    activated && linked && p.Graveyard.Contains(chaser) && p.Graveyard.Contains(rm) &&
                    !p.TryFindSpellTrap(rm, out _),
                    $"reached={reached} activated={activated} linked={linked} rmGy={p.Graveyard.Contains(rm)}");
            }

            {
                // Near miss: no face-up Machine on the field (a face-down Machine and a face-up Warrior) → cannot activate.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Down(e, p, Mechanicalchaser, 0);
                Up(e, e.Opponent, Celtic, 0);
                var rm = PlaceSetTrap(e, p, RareMetalmorph, 0);
                Check("Rare Metalmorph: cannot be activated without a face-up Machine on the field",
                    reached && !e.CanActivateSpellTrap(p, rm, fromHand: false),
                    $"reached={reached}");
            }
        }

        // ───────────────────────────── deferred ─────────────────────────────

        static void Deferred(CardDatabase db, Chk Check)
        {
            Check("Tragedy: deferred — not Implemented (no \"Attack Position monster changed to Defense\" response window)",
                Status(db, Tragedy) != CardEffectStatusKind.Implemented && !CardEffectStatus.MayIncludeInDeck(db.Get(Tragedy)) &&
                EffectVocabulary.HasNoRuntime(EffectActionKind.DestroyAllOppDefenseWhenOppAttackBecomesDefense),
                Describe(Prog(db, Tragedy)));
        }
    }
}
