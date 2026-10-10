using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// A single targeted clause's Amount was read as a number of targets (found 2026-09-29 in a headless probe). The
    /// player's target prompt took Amount picks, "up to", so Rush Recklessly ("Target 1 face-up monster on the field;
    /// it gains 700 ATK") asked for another target 700 times, and every picked monster gained 700 ATK. The same misread
    /// hit Reinforcements and Energy Drain (ATK), Barrel Dragon and Blowback Dragon (2 heads: 2 monsters destroyed),
    /// Spirit Caller (Level 3 or lower: up to 3 Special Summons, for the AI too), Marauding Captain (Level 4) and
    /// Reinforcement of the Army (Level 4). Fix: TextEffectRuntime.TargetPicks / AmountIsUpToTargetCount, used by every
    /// single-target prompt (activation, Flip, "this card is Summoned", ignition after cost) and by the automatic
    /// picks. Amount is a count only in the classic "up to N" Banish / Destroy / return-to-hand / discard templates.
    /// Every "takes 1 target" check failed before the fix. The multi-target checks keep Backup Soldier, Dark Factory
    /// of Mass Production, Gravedigger Ghoul, Soul Release, The Cheerful Coffin and Penguin Soldier taking several.
    /// </summary>
    public static class TargetCountRegressionTests
    {
        const int RushRecklessly = 70046172;
        const int Reinforcements = 17814387;
        const int EnergyDrain = 56916805;
        const int BarrelDragon = 81480460;
        const int BlowbackDragon = 25551951;
        const int SpiritCaller = 48659020;
        const int MaraudingCaptain = 2460565;
        const int ReinforcementOfTheArmy = 32807846;
        const int BackupSoldier = 36280194;
        const int DarkFactory = 90928333;
        const int GravediggerGhoul = 82542267;
        const int SoulRelease = 5758500;
        const int CheerfulCoffin = 41142615;
        const int PenguinSoldier = 93920745;

        const int Celtic = 91152256;          // Normal Warrior Lv4 1400/1200
        const int GiantSoldier = 13039848;    // Normal Rock Lv3 1300/2000
        const int MysticalElf = 15025844;     // Normal Lv4 800/2000
        const int SummonedSkull = 70781052;   // Normal Lv6 2500/1200
        const int BlueEyes = 89631139;        // Normal Lv8 3000
        const int ManEaterBug = 54652250;     // Flip Effect Lv2
        const int ObnoxiousCeltic = 52077741; // Effect Warrior Lv4
        const int PotOfGreed = 55144522;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("Target count: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            StatAmountTakesOneTarget(db, pDeck, aDeck, Check);
            HeadsCountTakesOneTarget(db, pDeck, aDeck, Check);
            LevelCapTakesOneTarget(db, pDeck, aDeck, Check);
            MultiTargetCardsTakeSeveral(db, pDeck, aDeck, Check);
            AutomaticPicks(db, pDeck, aDeck, Check);
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

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 12 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static string Prompt(DuelEngine e)
        {
            var p = e.PendingActivation;
            return p == null
                ? "no prompt"
                : $"left={p.TargetPicksRemaining} upTo={p.TargetPicksAreUpTo} legal={p.LegalTargets.Count} multi={p.MultiTarget}";
        }

        /// <summary>The player is asked for exactly one target, not "up to N".</summary>
        static bool OnePickPrompt(DuelEngine e) =>
            e.IsAwaitingEffectTarget && e.PendingActivation != null &&
            e.PendingActivation.TargetPicksRemaining == 1 && !e.PendingActivation.TargetPicksAreUpTo;

        static bool UpToPrompt(DuelEngine e, int n) =>
            e.IsAwaitingEffectTarget && e.PendingActivation != null &&
            e.PendingActivation.TargetPicksRemaining == n && e.PendingActivation.TargetPicksAreUpTo;

        static bool Pick(DuelEngine e, CardInstance target)
        {
            var ok = target != null && e.TrySelectEffectTarget(target);
            Settle(e);
            return ok;
        }

        static CardInstance FaceDown(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        static CardInstance FaceUp(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);

        static CardInstance ToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static EffectClause TargetedActivate(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault(c => c != null && c.RequiresTargetChoice);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            // Amount is the ATK/DEF change: one target, no TargetCount / TargetUpTo / ControllerTargetCount.
            foreach (var (id, amount) in new[] { (RushRecklessly, 700), (Reinforcements, 500), (EnergyDrain, 200) })
            {
                var c = TargetedActivate(db, id, out var prog);
                Check($"{db.Get(id)?.name} compiles to 1 target with Amount {amount} (a stat, not a count)",
                    prog != null && prog.FullyCompiled && c != null && c.Amount == amount &&
                    c.TargetCount <= 1 && !c.TargetUpTo && c.ControllerTargetCount == 0,
                    c == null ? "no targeted Activate clause" : $"amt={c.Amount} tc={c.TargetCount} upTo={c.TargetUpTo}");
            }

            // The "up to N" templates that keep N in Amount, and the two TargetCount cards.
            foreach (var (id, action, amount) in new[]
                     {
                         (GravediggerGhoul, EffectActionKind.Banish, 2),
                         (SoulRelease, EffectActionKind.Banish, 5),
                         (CheerfulCoffin, EffectActionKind.DiscardUpToMonstersFromHand, 3)
                     })
            {
                var c = TargetedActivate(db, id, out var prog);
                Check($"{db.Get(id)?.name} keeps its \"up to {amount}\" in Amount",
                    prog != null && prog.FullyCompiled && c != null && c.Action == action && c.Amount == amount,
                    c == null ? "no targeted Activate clause" : $"action={c.Action} amt={c.Amount}");
            }

            var bs = TargetedActivate(db, BackupSoldier, out var bsProg);
            Check("Backup Soldier: up to 3 targets (TargetCount), Amount is its 1500 ATK cap",
                bsProg != null && bsProg.FullyCompiled && bs != null && bs.TargetCount == 3 && bs.TargetUpTo &&
                bs.AmountIsAtkMax && bs.Amount == 1500,
                bs == null ? "no targeted Activate clause" : $"tc={bs.TargetCount} upTo={bs.TargetUpTo} amt={bs.Amount}");
            var df = TargetedActivate(db, DarkFactory, out var dfProg);
            Check("Dark Factory of Mass Production: exactly 2 targets (TargetCount)",
                dfProg != null && dfProg.FullyCompiled && df != null && df.TargetCount == 2 && !df.TargetUpTo,
                df == null ? "no targeted Activate clause" : $"tc={df.TargetCount} upTo={df.TargetUpTo}");
        }

        // ───────────────────────────── ATK / DEF amounts ─────────────────────────────

        static void StatAmountTakesOneTarget(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Rush Recklessly from the hand: 3 face-up monsters, the player targets the opponent's.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var mine1 = FaceUp(e, p, Celtic, 0);
                var mine2 = FaceUp(e, p, GiantSoldier, 1);
                var theirs = FaceUp(e, opp, SummonedSkull, 0);
                var card = PutInHand(e, p, RushRecklessly);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                Check("Rush Recklessly asks for 1 target, not up to 700",
                    act && OnePickPrompt(e) && e.PendingActivation.LegalTargets.Count == 3, Prompt(e));
                var picked = Pick(e, theirs);
                Check("Rush Recklessly: 1 pick resolves it; only that monster gains 700 ATK",
                    picked && !e.IsAwaitingEffectTarget && theirs.CurrentAtk == 3200 &&
                    mine1.CurrentAtk == 1400 && mine2.CurrentAtk == 1300 && p.Graveyard.Contains(card),
                    $"picked={picked} {Prompt(e)} atk={mine1.CurrentAtk}/{mine2.CurrentAtk}/{theirs.CurrentAtk}");
            }

            // Reinforcements (Set Trap).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var mine1 = FaceUp(e, p, Celtic, 0);
                var mine2 = FaceUp(e, p, GiantSoldier, 1);
                var theirs = FaceUp(e, opp, SummonedSkull, 0);
                var trap = PlaceSetTrap(e, p, Reinforcements, 0);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                Settle(e);
                Check("Reinforcements asks for 1 target, not up to 500", act && OnePickPrompt(e), Prompt(e));
                var picked = Pick(e, mine2);
                Check("Reinforcements: 1 pick resolves it; only that monster gains 500 ATK",
                    picked && !e.IsAwaitingEffectTarget && mine2.CurrentAtk == 1800 &&
                    mine1.CurrentAtk == 1400 && theirs.CurrentAtk == 2500,
                    $"picked={picked} {Prompt(e)} atk={mine1.CurrentAtk}/{mine2.CurrentAtk}/{theirs.CurrentAtk}");
            }

            // Energy Drain (Set Trap): 3 cards in the opponent's hand, +600 ATK/DEF to one face-up monster.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var mine1 = FaceUp(e, p, Celtic, 0);
                var mine2 = FaceUp(e, p, GiantSoldier, 1);
                FaceDown(e, p, MysticalElf, 2);
                for (var i = 0; i < 3; i++) PutInHand(e, opp, Celtic);
                var trap = PlaceSetTrap(e, p, EnergyDrain, 0);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                Settle(e);
                Check("Energy Drain asks for 1 face-up monster you control, not up to 200",
                    act && OnePickPrompt(e) && e.PendingActivation.LegalTargets.Count == 2, Prompt(e));
                var picked = Pick(e, mine2);
                Check("Energy Drain: 1 pick resolves it; only that monster gains 600 ATK/DEF",
                    picked && !e.IsAwaitingEffectTarget && mine2.CurrentAtk == 1900 && mine2.CurrentDef == 2600 &&
                    mine1.CurrentAtk == 1400 && mine1.CurrentDef == 1200,
                    $"picked={picked} {Prompt(e)} {mine1.CurrentAtk}/{mine1.CurrentDef} {mine2.CurrentAtk}/{mine2.CurrentDef}");
            }
        }

        // ───────────────────────────── coin heads count ─────────────────────────────

        static void HeadsCountTakesOneTarget(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var id in new[] { BarrelDragon, BlowbackDragon })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var dragon = FaceUp(e, p, id, 0);
                var prey = new[] { 0, 1, 2 }.Select(z => FaceUp(e, opp, Celtic, z)).ToList();
                for (var i = 0; i < 3; i++) e.Rng.QueueCoin(true);
                var act = e.TryActivateSpellTrap(p, dragon, fromHand: false);
                Settle(e);
                Check($"{dragon.Name} asks for 1 target (\"at least 2 heads\" is not 2 targets)",
                    act && OnePickPrompt(e), Prompt(e));
                var picked = Pick(e, prey[0]);
                var destroyed = prey.Count(c => opp.Graveyard.Contains(c));
                Check($"{dragon.Name}: 3 heads destroy only the one target",
                    picked && !e.IsAwaitingEffectTarget && destroyed == 1 && opp.Graveyard.Contains(prey[0]),
                    $"picked={picked} destroyed={destroyed} {Prompt(e)}");
            }
        }

        // ───────────────────────────── Level caps ─────────────────────────────

        static void LevelCapTakesOneTarget(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Spirit Caller, player: FLIP Special Summons 1 Level 3 or lower Normal Monster, not up to 3.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var gy = Enumerable.Range(0, 3).Select(_ => ToGy(e, p, GiantSoldier)).ToList();
                var caller = FaceDown(e, p, SpiritCaller, 0);
                var flip = e.TryFlipSummon(p, caller);
                Settle(e);
                Check("Spirit Caller (player) asks for 1 GY monster, not up to 3", flip && OnePickPrompt(e), Prompt(e));
                var picked = Pick(e, gy[0]);
                Check("Spirit Caller (player): Special Summons exactly 1",
                    picked && !e.IsAwaitingEffectTarget && p.MonsterCount == 2 && p.TryFindMonster(gy[0], out _) &&
                    p.Graveyard.Count == 2,
                    $"picked={picked} monsters={p.MonsterCount} gy={p.Graveyard.Count} {Prompt(e)}");
            }

            // Spirit Caller, AI: its automatic Flip picks took 3 as well.
            {
                var e = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(e))
                {
                    ClearBoard(e);
                    var opp = e.Opponent;
                    opp.Hand.Clear();
                    opp.Graveyard.Clear();
                    for (var i = 0; i < 3; i++) ToGy(e, opp, GiantSoldier);
                    var caller = FaceDown(e, opp, SpiritCaller, 0);
                    var flip = e.TryFlipSummon(opp, caller);
                    Settle(e);
                    Check("Spirit Caller (AI): Special Summons exactly 1",
                        flip && !e.IsAwaitingEffectTarget && opp.MonsterCount == 2 && opp.Graveyard.Count == 2,
                        $"flip={flip} monsters={opp.MonsterCount} gy={opp.Graveyard.Count}");
                }
                else
                    Check("Spirit Caller (AI) setup: reach the opponent's Main Phase", false,
                        $"turnP={e.TurnPlayer == e.Opponent} phase={e.Phase}");
            }

            // Marauding Captain: "Special Summon 1 Level 4 or lower monster from your hand", not up to 4.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var captain = PutInHand(e, p, MaraudingCaptain);
                var hand = Enumerable.Range(0, 3).Select(_ => PutInHand(e, p, Celtic)).ToList();
                var summoned = e.TryNormalSummon(p, captain, asSet: false);
                Settle(e);
                Check("Marauding Captain asks for 1 hand monster, not up to 4", summoned && OnePickPrompt(e), Prompt(e));
                var picked = Pick(e, hand[0]);
                Check("Marauding Captain: Special Summons exactly 1",
                    picked && !e.IsAwaitingEffectTarget && p.MonsterCount == 2 && p.TryFindMonster(hand[0], out _) &&
                    p.Hand.Count == 2,
                    $"picked={picked} monsters={p.MonsterCount} hand={p.Hand.Count} {Prompt(e)}");
            }

            // Reinforcement of the Army: "Add 1 Level 4 or lower Warrior monster", not up to 4.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, MaraudingCaptain, ObnoxiousCeltic, SummonedSkull });
                var card = PutInHand(e, p, ReinforcementOfTheArmy);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                Check("Reinforcement of the Army asks for 1 Warrior, not up to 4",
                    act && OnePickPrompt(e) && e.PendingActivation.LegalTargets.Count == 3, Prompt(e));
                var picked = e.IsAwaitingEffectTarget && Pick(e, e.PendingActivation.LegalTargets[0]);
                Check("Reinforcement of the Army: adds exactly 1",
                    picked && !e.IsAwaitingEffectTarget && p.Hand.Count == 1 && p.Deck.Count == 3,
                    $"picked={picked} hand={p.Hand.Count} deck={p.Deck.Count} {Prompt(e)}");
            }
        }

        // ───────────────────────────── real multi-target cards ─────────────────────────────

        static void MultiTargetCardsTakeSeveral(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Gravedigger Ghoul: up to 2 monsters in the opponent's GY.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var gy = new[] { Celtic, GiantSoldier, SummonedSkull }.Select(id => ToGy(e, opp, id)).ToList();
                var card = PutInHand(e, p, GravediggerGhoul);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                Check("Gravedigger Ghoul still asks for up to 2", act && UpToPrompt(e, 2), Prompt(e));
                var first = Pick(e, gy[0]);
                var stillAsking = e.IsAwaitingEffectTarget;
                var second = Pick(e, gy[1]);
                Check("Gravedigger Ghoul: banishes both picks",
                    first && stillAsking && second && !e.IsAwaitingEffectTarget &&
                    opp.Banished.Contains(gy[0]) && opp.Banished.Contains(gy[1]) && opp.Graveyard.Contains(gy[2]),
                    $"first={first} stillAsking={stillAsking} second={second} banished={opp.Banished.Count} {Prompt(e)}");
            }

            // Soul Release: up to 5 cards in any GY; 3 legal, so the third pick resolves it.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var gy = new List<CardInstance> { ToGy(e, p, Celtic), ToGy(e, opp, GiantSoldier), ToGy(e, opp, PotOfGreed) };
                var card = PutInHand(e, p, SoulRelease);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                Check("Soul Release still asks for up to 5", act && UpToPrompt(e, 5), Prompt(e));
                var picks = gy.Count(t => Pick(e, t));
                Check("Soul Release: banishes all 3 picks",
                    picks == 3 && !e.IsAwaitingEffectTarget && p.Banished.Contains(gy[0]) &&
                    opp.Banished.Contains(gy[1]) && opp.Banished.Contains(gy[2]),
                    $"picks={picks} banished={p.Banished.Count}+{opp.Banished.Count} {Prompt(e)}");
            }

            // The Cheerful Coffin: up to 3 Monster Cards from the hand; Spells stay.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var monsters = new[] { Celtic, GiantSoldier, MysticalElf, SummonedSkull }
                    .Select(id => PutInHand(e, p, id)).ToList();
                var spell = PutInHand(e, p, PotOfGreed);
                var card = PutInHand(e, p, CheerfulCoffin);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                Check("The Cheerful Coffin still asks for up to 3", act && UpToPrompt(e, 3), Prompt(e));
                var picks = monsters.Take(3).Count(t => Pick(e, t));
                Check("The Cheerful Coffin: discards all 3 picks",
                    picks == 3 && !e.IsAwaitingEffectTarget && monsters.Take(3).All(p.Graveyard.Contains) &&
                    p.Hand.Contains(monsters[3]) && p.Hand.Contains(spell),
                    $"picks={picks} gyMonsters={p.Graveyard.Count(c => c.Def.IsMonster)} hand={p.Hand.Count} {Prompt(e)}");
            }

            // Backup Soldier: up to 3 non-Effect monsters with 1500 or less ATK (GY has 5 monsters).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var legal = new[] { Celtic, MysticalElf, GiantSoldier }.Select(id => ToGy(e, p, id)).ToList();
                ToGy(e, p, ManEaterBug);
                ToGy(e, p, BlueEyes);
                var trap = PlaceSetTrap(e, p, BackupSoldier, 0);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                Settle(e);
                Check("Backup Soldier still opens an up-to-3 multi-target pick",
                    act && e.IsAwaitingEffectTarget && e.PendingActivation.MultiTarget &&
                    e.PendingActivation.MultiStepUpTo && e.PendingActivation.MultiStepRemaining == 3,
                    Prompt(e) + $" rem={e.PendingActivation?.MultiStepRemaining}");
                var picks = legal.Count(t => Pick(e, t));
                Check("Backup Soldier: adds all 3 picks to the hand",
                    picks == 3 && !e.IsAwaitingEffectTarget && legal.All(p.Hand.Contains),
                    $"picks={picks} hand={p.Hand.Count} {Prompt(e)}");
            }

            // Dark Factory of Mass Production: exactly 2 Normal Monsters in your GY.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var gy = new[] { Celtic, MysticalElf, GiantSoldier }.Select(id => ToGy(e, p, id)).ToList();
                var card = PutInHand(e, p, DarkFactory);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                Check("Dark Factory still opens an exact-2 multi-target pick",
                    act && e.IsAwaitingEffectTarget && e.PendingActivation.MultiTarget &&
                    !e.PendingActivation.MultiStepUpTo && e.PendingActivation.MultiStepRemaining == 2,
                    Prompt(e) + $" rem={e.PendingActivation?.MultiStepRemaining}");
                var first = Pick(e, gy[0]);
                var stillAsking = e.IsAwaitingEffectTarget;
                var second = Pick(e, gy[1]);
                Check("Dark Factory: adds both picks to the hand",
                    first && stillAsking && second && !e.IsAwaitingEffectTarget &&
                    p.Hand.Contains(gy[0]) && p.Hand.Contains(gy[1]) && p.Graveyard.Contains(gy[2]),
                    $"first={first} stillAsking={stillAsking} second={second} hand={p.Hand.Count} {Prompt(e)}");
            }

            // Penguin Soldier: the Flip prompt keeps its "up to 2".
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var penguin = FaceDown(e, p, PenguinSoldier, 0);
                var v1 = FaceUp(e, opp, Celtic, 0);
                var v2 = FaceUp(e, opp, GiantSoldier, 1);
                var flip = e.TryFlipSummon(p, penguin);
                Settle(e);
                Check("Penguin Soldier still asks for up to 2", flip && UpToPrompt(e, 2), Prompt(e));
                var first = Pick(e, v1);
                var second = Pick(e, v2);
                Check("Penguin Soldier: returns both picks to the hand",
                    first && second && !e.IsAwaitingEffectTarget && opp.Hand.Contains(v1) && opp.Hand.Contains(v2),
                    $"first={first} second={second} oppHand={opp.Hand.Count} {Prompt(e)}");
            }
        }

        // ───────────────────────────── automatic picks (AI) ─────────────────────────────

        static void AutomaticPicks(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // AI Rush Recklessly: one monster gains 700.
            {
                var e = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(e))
                {
                    ClearBoard(e);
                    var p = e.Player;
                    var opp = e.Opponent;
                    opp.Hand.Clear();
                    var monsters = new List<CardInstance>
                    {
                        FaceUp(e, opp, Celtic, 0), FaceUp(e, opp, GiantSoldier, 1), FaceUp(e, p, SummonedSkull, 0)
                    };
                    var before = monsters.Sum(m => m.CurrentAtk);
                    var card = PutInHand(e, opp, RushRecklessly);
                    var act = e.TryActivateSpellTrap(opp, card, fromHand: true);
                    Settle(e);
                    var gained = monsters.Count(m => m.CurrentAtk == m.Def.atk + 700);
                    Check("Rush Recklessly (AI): exactly one monster gains 700 ATK",
                        act && !e.IsAwaitingEffectTarget && gained == 1 && monsters.Sum(m => m.CurrentAtk) == before + 700,
                        $"act={act} gained={gained} total={monsters.Sum(m => m.CurrentAtk)} (was {before})");
                }
                else
                    Check("Rush Recklessly (AI) setup: reach the opponent's Main Phase", false,
                        $"turnP={e.TurnPlayer == e.Opponent} phase={e.Phase}");
            }

            // AI Soul Release: its automatic picks still take several (up to 5).
            {
                var e = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(e))
                {
                    ClearBoard(e);
                    var p = e.Player;
                    var opp = e.Opponent;
                    opp.Hand.Clear();
                    p.Graveyard.Clear();
                    opp.Graveyard.Clear();
                    for (var i = 0; i < 3; i++) ToGy(e, p, Celtic);
                    var card = PutInHand(e, opp, SoulRelease);
                    var act = e.TryActivateSpellTrap(opp, card, fromHand: true);
                    Settle(e);
                    Check("Soul Release (AI): banishes all 3 cards in your GY",
                        act && !e.IsAwaitingEffectTarget && p.Graveyard.Count == 0 &&
                        p.Banished.Count(c => c.CardId == Celtic) == 3,
                        $"act={act} gy={p.Graveyard.Count} banished={p.Banished.Count}");
                }
                else
                    Check("Soul Release (AI) setup: reach the opponent's Main Phase", false,
                        $"turnP={e.TurnPlayer == e.Opponent} phase={e.Phase}");
            }
        }
    }
}
